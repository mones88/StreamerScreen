using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiControlSource
    {
        public enum EStatus
        {
            standby = 0,
            selected, deselected
        }
        public class Source
        {
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
            [JsonProperty("display_name")]
            public string DisplayName { get; set; }
            [JsonProperty("supports_standby")]
            public bool SupportsStandBy { get; set; }
            [JsonConverter(typeof(StringEnumConverter))]
            [JsonProperty("status")]
            public EStatus Status { get; set; }
        }
        public class SourceControls
        {
            [JsonProperty("controls")]
            public Source[] Controls { get; set; }
        }
        public class SourceControlsChanged
        {
            [JsonProperty("controls_changed")]
            public Source[] Controls { get; set; }
        }
        public class SetState
        {
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
            [JsonProperty("status")]
            public RoonApiControlSource.EStatus Status { get; set; }
        }
        public class SetConvenience
        {
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
        }

        public class SourceFunctions
        {
            public Func<SetState, Task<bool>>        SetStandby;
            public Func<SetConvenience, Task<bool>>  SetConvenience;
        }

        RoonApi                          _api;
        List<Source>                     _controls;
        List<SourceFunctions>            _functions;
        RoonApiSubscriptionHandler       _subscriptionHandler;
        int                              _id;
        bool                             _simulateFeedback;
        public RoonApiControlSource(RoonApi api, bool simulateFeedback)
        {
            _id = 0;
            _api = api;
            _simulateFeedback = simulateFeedback;
            _subscriptionHandler = new RoonApiSubscriptionHandler();
            _api.AddService(RoonApi.ControlSource, OnSourceControl);
            _controls = new List<Source>();
            _functions = new List<SourceFunctions> ();
        }

        public bool HasSubscriptions => _subscriptionHandler.NumberOfSubcriptions > 0;
        public void AddControl(Source source, SourceFunctions functions)
        {
            source.ControlKey = _id;
            _controls.Add(source);
            _functions.Add(functions);
            _id++;
        }
        async Task<bool> OnSourceControl (string information, int requestId, string body)
        {
            string replyBody;
            bool rc = true;
            switch (information)
            {
                case RoonApi.ControlSource + "/subscribe_controls":
                    _subscriptionHandler.AddSubscription(body, requestId);
                    replyBody = JsonConvert.SerializeObject(new SourceControls { Controls = _controls.ToArray() });
                    rc = await _api.Reply("Subscribed", requestId, true, replyBody);
                    break;
                case RoonApi.ControlSource + "/unsubscribe_controls":
                    _subscriptionHandler.RemoveSubscription(body);
                    rc = await _api.Reply("Unsubscribed", requestId);
                    break;
                case RoonApi.ControlSource + "/get_all":
                    replyBody = JsonConvert.SerializeObject(new SourceControls { Controls = _controls.ToArray() });
                    rc = await _api.Reply("Success", requestId, false, replyBody);
                    break;
                case RoonApi.ControlSource + "/standby":
                    var state = JsonConvert.DeserializeObject<SetState>(body);
                    if (state.ControlKey >= _controls.Count)
                    {
                        rc = await _api.Reply("Failure", requestId);
                    }
                    else
                    {
                        rc = await _functions[state.ControlKey].SetStandby?.Invoke(state);
                        _controls[state.ControlKey].Status = state.Status;
                        rc = await _api.Reply("Success", requestId);
                        if (_simulateFeedback)
                            rc = await UpdateState(_controls[state.ControlKey]);
                    }
                    break;
                case RoonApi.ControlSource + "/convenience_switch":
                    var convenience = JsonConvert.DeserializeObject<SetConvenience>(body);
                    if (convenience.ControlKey >= _controls.Count)
                    {
                        rc = await _api.Reply("Failure", requestId);
                    }
                    else
                    {
                        rc = await _functions[convenience.ControlKey].SetConvenience?.Invoke(convenience);
                        rc = await _api.Reply("Success", requestId);
                        if (_simulateFeedback)
                            rc = await UpdateState(_controls[convenience.ControlKey]);
                    }
                    break;
            }

            return rc;
        }
        public async Task<bool> UpdateState(Source change)
        {
            SourceControlsChanged changed = new SourceControlsChanged { Controls = new Source[] { change } };
            string replyBody = JsonConvert.SerializeObject(changed);
            var result = await _subscriptionHandler.ReplyAll(_api, "Changed", replyBody);
            return true;
        }
    }
}
