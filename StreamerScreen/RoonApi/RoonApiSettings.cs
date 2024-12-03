using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiSettings
    {
        public class LayoutAndSettings
        {
            [JsonProperty("values")]
            public Dictionary<string, string>   Values { get; set; }
            [JsonProperty("layout")]
            public LayoutBase[]                 Layout { get; set; }
            [JsonProperty("has_error")]
            public bool                         HasError { get; set; }
        }
        public class LayoutBase
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("title")]
            public string Title { get; set; }
            [JsonProperty("subtitle")]
            public string SubTitle { get; set; }
        }
        public class LayoutLabel : LayoutBase
        {
            public LayoutLabel (string title)
            {
                Type = "label";
                Title = title;
            }
        }
        public class Layout : LayoutBase
        {
            [JsonProperty("setting")]
            public string                       Setting { get; set; }
            [JsonProperty("error")]
            public string                       Error { get; set; }
        }
        public class LayoutGroup : LayoutBase
        {
            public LayoutGroup(string title, LayoutBase[] items)
            {
                Type = "group";
                Title = title;
                Items = items;
            }
            [JsonProperty("items")]
            public LayoutBase[] Items { get; set; }
        }
        public class LayoutString : Layout
        {
            public LayoutString(string title, string setting, int maxLength = 20)
            {
                Type = "string";
                Title = title;
                Setting = setting;
                MaxLength = maxLength;
            }

            [JsonProperty("maxlength")]
            public int                          MaxLength { get; set; }
        }
        public class LayoutInteger : Layout
        {
            public LayoutInteger(string title, string setting, int minValue, int maxValue)
            {
                Type = "integer";
                Title = title;
                Setting = setting;
                Min = minValue;
                Max = maxValue;
            }

            [JsonProperty("min")]
            public int Min { get; set; }
            [JsonProperty("max")]
            public int Max { get; set; }
        }
        public class LayoutButton : Layout
        {
            public LayoutButton(string title, string setting, string buttonId)
            {
                Type = "string";
                Title = title;
                Setting = setting;
                ButtonId = buttonId;
            }
            [JsonProperty("buttonid")]
            public string                       ButtonId { get; set; }
        }
        public class LayoutDropDown : Layout
        {
            public LayoutDropDown(string title, string setting, LayoutDropDownValue[] values)
            {
                Type = "dropdown";
                Title = title;
                Setting = setting;
                Values = values;
            }
            [JsonProperty("values")]
            public LayoutDropDownValue[]        Values { get; set; }
        }
        public class LayoutDropDownValue
        {
            public LayoutDropDownValue (string value)
            {
                Value = value;
            }
            [JsonProperty("value")]
            public string                       Value{ get; set; }
        }

        public class SettingsAll
        {
            [JsonProperty("settings")]
            public LayoutAndSettings            Settings { get; set; }
        }
        public class SaveSettings
        {
            [JsonProperty("is_dry_run")]
            public bool                         IsDryRun { get; set; }
            [JsonProperty("settings")]
            public LayoutAndSettings            Settings { get; set; }
        }
        public class ButtonPressed
        {
            [JsonProperty("buttonid")]
            public int                          ButtonId { get; set; }
        }
        public class Functions
        {
            public Func<SaveSettings, Task<bool>>   SaveSettings;
            public Func<ButtonPressed, Task<bool>>  ButtonPressed;
        }

        RoonApi                         _api;
        List<LayoutBase>                _layout;
        List<Functions>                 _functions;
        Dictionary<string, string>      _values;
        RoonApiSubscriptionHandler      _subscriptionHandler;
        public RoonApiSettings(RoonApi api, List<LayoutBase> layout, Dictionary<string, string> values, Functions functions)
        {
            _api = api;
            _layout = layout;
            _values = values;

            _subscriptionHandler = new RoonApiSubscriptionHandler();
            _api.AddService(RoonApi.ServiceSettings, OnSettings);
            _functions = new List<Functions>();
            _functions.Add(functions);
        }
        public List<LayoutBase>             CurrentLayout => _layout;
        public Dictionary<string, string>   CurrentValues => _values;
        public bool                         HasSubscriptions => _subscriptionHandler.NumberOfSubcriptions > 0;

        async Task<bool> OnSettings(string information, int requestId, string body)
        {
            string replyBody;
            bool rc = true;
            switch (information)
            {
                case RoonApi.ServiceSettings + "/subscribe_settings":
                    _subscriptionHandler.AddSubscription(body, requestId);
                    replyBody = JsonConvert.SerializeObject(new SettingsAll { Settings = new LayoutAndSettings { Layout = _layout.ToArray(), Values = _values, HasError = false } });
                    rc = await _api.Reply("Subscribed", requestId, true, replyBody);
                    break;
                case RoonApi.ServiceSettings + "/unsubscribe_settings":
                    _subscriptionHandler.RemoveSubscription(body);
                    rc = await _api.Reply("Unsubscribed", requestId);
                    break;
                case RoonApi.ServiceSettings + "/get_settings":
                    replyBody = JsonConvert.SerializeObject(new SettingsAll { Settings = new LayoutAndSettings { Layout = _layout.ToArray(), Values = _values, HasError = false } } );
                    rc = await _api.Reply("Success", requestId, false, replyBody);
                    break;
                case RoonApi.ServiceSettings + "/save_settings":
                    var settings = JsonConvert.DeserializeObject<SaveSettings>(body);
                    rc = await _functions[0].SaveSettings?.Invoke(settings);
                    var settingsAll = new SettingsAll { Settings = new LayoutAndSettings { Layout = _layout.ToArray(), Values = _values, HasError = !rc } };
                    replyBody = JsonConvert.SerializeObject(settingsAll);
                    rc = await _api.Reply(rc ? "Success" : "NotValid", requestId, false, replyBody);
                    // rc = await UpdateSettings();
                    break;
                case RoonApi.ServiceSettings + "/button_pressed":
                    var button = JsonConvert.DeserializeObject<ButtonPressed>(body);
                    rc = await _api.Reply("Success", requestId, false);
                    rc = await _functions[0].ButtonPressed?.Invoke(button);
                    break;
            }
            return rc;
        }
        public Layout LookupLayout (string setting)
        {
            return LookupLayout(_layout, setting);
        }
        public static Layout LookupLayout (IEnumerable<LayoutBase> layouts, string setting)
        {
            foreach (var l in layouts)
            {
                Layout layout = l as Layout;
                if (layout != null && layout.Setting == setting)
                    return layout;
                LayoutGroup layoutGroup = l as LayoutGroup;
                if (layoutGroup != null)
                    return LookupLayout(layoutGroup.Items, setting);
            }
            return null;
        }
        public async Task<bool> UpdateSettings(bool hasError = false)
        {
            SettingsAll settings = new SettingsAll { Settings = new LayoutAndSettings { Layout = _layout.ToArray(), Values = _values, HasError = hasError } } ;
            string replyBody = JsonConvert.SerializeObject(settings);
            var result = await _subscriptionHandler.ReplyAll(_api, "Changed", replyBody);
            return true;
        }
    }
}
