using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiSubscriptionHandler
    {
        internal class Subscription
        {
            [JsonProperty("subscription_key")]
            public int SubscriptionKey { get; set; }
        }
        Dictionary<int, int> _subscriptions = new Dictionary<int, int>();

        internal int NumberOfSubcriptions => _subscriptions.Count;
        internal bool AddSubscription(string body, int requestId)
        {
            Subscription subscription = JsonConvert.DeserializeObject<Subscription>(body);
            return AddSubscription(subscription.SubscriptionKey, requestId);
        }
        internal bool AddSubscription(int key, int requestId)
        {
            lock (_subscriptions)
            {
                if (_subscriptions.ContainsKey(key))
                    return false;
                _subscriptions.Add(key, requestId);
                return true;
            }
        }
        internal bool RemoveSubscription(string body)
        {
            Subscription subscription = JsonConvert.DeserializeObject<Subscription>(body);
            return RemoveSubscription(subscription.SubscriptionKey);
        }
        internal bool RemoveSubscription(int key)
        {
            lock (_subscriptions)
            {
                if (!_subscriptions.ContainsKey(key))
                    return false;
                _subscriptions.Remove(key);
                return true;
            }
        }
        internal async Task<int> ReplyAll(RoonApi api, string command, string body = null, string contentType = "application/json")
        {
            int count = 0;
            foreach (var subscription in _subscriptions)
            {
                if (await api.Reply(command, subscription.Value, true, body, false, contentType))
                    count++;
            }
            return count;
        }
    }
}
