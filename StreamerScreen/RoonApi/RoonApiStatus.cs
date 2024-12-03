using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiStatus
    {
        public class RoonStatus
        {
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("is_error")]
            public bool IsError { get; set; }
        }

        RoonApi     _api;
        RoonStatus  _currentStatus;
        RoonApiSubscriptionHandler _subscriptionHandler;

        public RoonApiStatus(RoonApi api, string message = "", bool isError = false)
        {
            _api = api;
            _subscriptionHandler = new RoonApiSubscriptionHandler();
            _currentStatus = new RoonStatus { Message = message, IsError = isError };
            _api.AddService(RoonApi.ServiceStatus, OnStatus);
        }
        public RoonStatus CurrentStatus => _currentStatus;
        public bool HasSubscriptions => _subscriptionHandler.NumberOfSubcriptions > 0;

        async Task<bool> OnStatus(string information, int requestId, string body)
        {
            string replyBody;
            bool rc = true;
            switch (information)
            {
                case RoonApi.ServiceStatus + "/subscribe_status":
                    _subscriptionHandler.AddSubscription(body, requestId);
                    replyBody = JsonConvert.SerializeObject(_currentStatus);
                    rc = await _api.Reply("Subscribed", requestId, true, replyBody);
                    break;
                case RoonApi.ServiceStatus + "/unsubscribe_controls":
                    _subscriptionHandler.RemoveSubscription(body);
                    rc = await _api.Reply("Unsubscribed", requestId);
                    break;
                case RoonApi.ServiceStatus + "/get_Status":
                    replyBody = JsonConvert.SerializeObject(_currentStatus);
                    rc = await _api.Reply("Success", requestId, false, replyBody);
                    break;
            }

            return rc;
        }
        public async Task<bool> SetStatus(RoonStatus status)
        {
            return await SetStatus(status.Message, status.IsError);
        }
        public async Task<bool> SetStatus(string message, bool isError)
        {
            _currentStatus.Message = message;
            _currentStatus.IsError = isError;
            string replyBody = JsonConvert.SerializeObject(_currentStatus);
            var result = await _subscriptionHandler.ReplyAll(_api, "Changed", replyBody);
            return true;
        }
    }
}
