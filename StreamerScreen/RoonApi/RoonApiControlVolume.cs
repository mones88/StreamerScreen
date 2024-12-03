using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiControlVolume
    {
        public enum EStatus
        {
            standby = 0,
            selected, deselected
        }
        public class Volume
        {
            [JsonProperty("display_name")]
            public string DisplayName { get; set; }
            [JsonProperty("volume_type")]
            public string VolumeType { get; set; }
            [JsonProperty("volume_min")]
            public int VolumeMin { get; set; }
            [JsonProperty("volume_max")]
            public int VolumeMax { get; set; }
            [JsonProperty("volume_value")]
            public int VolumeValue { get; set; }
            [JsonProperty("volume_step")]
            public int VolumeStep { get; set; }
            [JsonProperty("is_muted")]
            public bool IsMuted { get; set; }
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
        }
        public class VolumeControls
        {
            [JsonProperty("controls")]
            public Volume[] Controls { get; set; }
        }
        public class VolumeControlsChanged
        {
            [JsonProperty("controls_changed")]
            public Volume[] Controls { get; set; }
        }
        public class SetVolume
        {
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
            [JsonProperty("mode")]
            public RoonApiTransport.EVolumeMode Mode { get; set; }
            [JsonProperty("value")]
            public int Value { get; set; }
        }
        public class SetMute
        {
            [JsonProperty("control_key")]
            public int ControlKey { get; set; }
            [JsonProperty("mute")]
            public RoonApiTransport.EMute Mute { get; set; }
        }
        public class VolumeFunctions
        {
            public Func<SetVolume, Task<bool>>   SetVolume;
            public Func<SetMute, Task<bool>>     Mute;
        }
        RoonApi                         _api;
        List<Volume>                    _controls;
        List<VolumeFunctions>           _functions;
        RoonApiSubscriptionHandler      _subscriptionHandler;
        int                             _id;
        bool                            _simulateFeedback;
        public RoonApiControlVolume(RoonApi api, bool simulateFeedback)
        {
            _id = 0;
            _api = api;
            _simulateFeedback = simulateFeedback;
            _subscriptionHandler = new RoonApiSubscriptionHandler();
            _api.AddService(RoonApi.ControlVolume, OnVolumeControl);
            _controls = new List<Volume>();
            _functions = new List<VolumeFunctions>();
        }
        public bool HasSubscriptions => _subscriptionHandler.NumberOfSubcriptions > 0;
        public void AddControl (Volume volume, VolumeFunctions functions)
        {
            volume.ControlKey = _id;
            _controls.Add(volume);
            _functions.Add(functions);
            _id++;
        }
        async Task<bool> OnVolumeControl(string information, int requestId, string body)
        {
            string replyBody;
            bool rc = true;
            switch (information)
            {
                case RoonApi.ControlVolume + "/subscribe_controls":
                    _subscriptionHandler.AddSubscription(body, requestId);
                    replyBody = JsonConvert.SerializeObject(new VolumeControls { Controls = _controls.ToArray() });
                    rc = await _api.Reply("Subscribed", requestId, true, replyBody);
                    break;
                case RoonApi.ControlVolume + "/unsubscribe_controls":
                    _subscriptionHandler.RemoveSubscription(body);
                    rc = await _api.Reply("Unsubscribed", requestId);
                    break;
                case RoonApi.ControlVolume + "/get_all":
                    replyBody = JsonConvert.SerializeObject(new VolumeControls { Controls = _controls.ToArray() });
                    rc = await _api.Reply("Success", requestId, false, replyBody);
                    break;
                case RoonApi.ControlVolume + "/set_volume":
                    var volume = JsonConvert.DeserializeObject<SetVolume>(body);
                    if (volume.ControlKey >= _controls.Count)
                    {
                        rc = await _api.Reply("Failure", requestId);
                    }
                    else
                    {
                        rc = await _functions[volume.ControlKey].SetVolume?.Invoke(volume);
                        _controls[volume.ControlKey].VolumeValue = volume.Value;
                        rc = await _api.Reply("Success", requestId);
                        if (_simulateFeedback)
                            rc = await UpdateState(_controls[volume.ControlKey]);
                    }
                    break;
                case RoonApi.ControlVolume + "/set_mute":
                    var mute = JsonConvert.DeserializeObject<SetMute>(body);
                    if (mute.ControlKey >= _controls.Count)
                    {
                        rc = await _api.Reply("Failure", requestId);
                    }
                    else
                    {
                        rc = await _functions[mute.ControlKey].Mute?.Invoke(mute);
                        _controls[mute.ControlKey].IsMuted = mute.Mute == RoonApiTransport.EMute.mute;
                        rc = await _api.Reply("Success", requestId);
                        if (_simulateFeedback)
                            rc = await UpdateState(_controls[mute.ControlKey]);
                    }
                    break;
            }

            return rc;
        }
        public async Task<bool> UpdateState (Volume change)
        {
            VolumeControlsChanged changed = new VolumeControlsChanged { Controls = new Volume[] { change } };
            string replyBody = JsonConvert.SerializeObject(changed);
            var result = await _subscriptionHandler.ReplyAll(_api, "Changed", replyBody);
            return true;
        }
    }
}
