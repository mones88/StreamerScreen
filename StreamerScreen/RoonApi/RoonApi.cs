using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using RoonApiLib.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApi : BindableBase
    {
        public const string ServiceRegistry     = "com.roonlabs.registry:1";
        public const string ServiceTransport    = "com.roonlabs.transport:1";
        public const string ServiceStatus       = "com.roonlabs.status:1";
        public const string ServicePairing      = "com.roonlabs.pairing:1";
        public const string ServicePing         = "com.roonlabs.ping:1";
        public const string ServiceImage        = "com.roonlabs.image:1";
        public const string ServiceBrowse       = "com.roonlabs.browse:1";
        public const string ServiceSettings     = "com.roonlabs.settings:1";
        public const string ControlVolume       = "com.roonlabs.volumecontrol:1";
        public const string ControlSource       = "com.roonlabs.sourcecontrol:1";

        public const string MessageRequest      = "REQUEST";
        public const string MessageComplete     = "COMPLETE";
        public const string MessageContinue     = "CONTINUE";


        public class RoonRequest
        {
            public RoonRequest()
            {
                OnReceived = null;
                Event = new TaskCompletionSource<bool>();
                StringResult = null;
                DataResult = null;
                StartTime = DateTime.UtcNow;
            }
            public RoonRequest(OnRoonReceived onReceived)
            {
                OnReceived = onReceived;
                Event = null;
                StringResult = null;
                DataResult = null;
                StartTime = DateTime.UtcNow;
            }
            public OnRoonReceived OnReceived;
            public TaskCompletionSource<bool> Event;
            public string StringResult;
            public byte[] DataResult;
            public DateTime StartTime;
        }
        public class RoonReply
        {
            [JsonProperty("core_id")]
            public string CoreId { get; set; }
            [JsonProperty("display_name")]
            public string DisplayName { get; set; }
            [JsonProperty("display_version")]
            public string DisplayVersion { get; set; }
        }
        public class RoonRegisterReply : RoonReply
        {
            [JsonProperty("token")]
            public string Token { get; set; }
            [JsonProperty("provided_services")]
            public string[] ProvidedServices { get; set; }
            [JsonProperty("http_port")]
            public string HttpPort { get; set; }
        }
        public class RoonRegister
        {
            [JsonProperty("extension_id")]
            public string ExtensionId { get; set; }
            [JsonProperty("display_name")]
            public string DisplayName { get; set; }
            [JsonProperty("display_version")]
            public string DisplayVersion { get; set; }
            [JsonProperty("publisher")]
            public string Publisher { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
            [JsonProperty("required_services")]
            public string[] RequiredServices { get; set; }
            [JsonProperty("optional_services")]
            public string[] OptionalServices { get; set; }
            [JsonProperty("provided_services")]
            public string[] ProvidedServices { get; set; }
            [JsonProperty("token")]
            public string Token { get; set; }
            [JsonProperty("website")]
            public string WebSite { get; set; }
        }
        public class RoonState
        {
            public RoonState()
            {
                Tokens = new Dictionary<string, string>();
            }
            [JsonProperty("tokens")]
            public Dictionary<string, string> Tokens;
            [JsonProperty("paired_core_id")]
            public string PairedCoreId { get; set; }
        }
        public class RoonPaired
        {
            [JsonProperty("paired_core_id")]
            public string PairedCoreId { get; set; }
        }
        public class RoonSavings
        {
            public RoonSavings()
            {
                RoonState = new RoonState();
            }
            [JsonProperty("roonstate")]
            public RoonState RoonState { get; set; }
        }

        public delegate Task<bool> OnRoonReceived(string information, int requestId, string body);

        ClientWebSocket                     _webSocket;
        CancellationTokenSource             _cancellationTokenSource;
        int                                 _requestId;
        string                              _configPath;
        Dictionary<int, RoonRequest>        _requests;
        ILogger                             _logger;
        JsonSerializerSettings              _jsonSettings;
        RoonSavings                         _savings;
        string                              _coreId;
        bool                                _paired;
        AsyncLock                           _lock;
        Dictionary<string, object>          _configuration;
        Dictionary<string, OnRoonReceived>  _services;
        Dictionary<int, OnRoonReceived>     _subscriptions;
        public Func<string, Task>           _onPaired;
        public Func<string, Task>           _onUnPaired;
        RoonApiSubscriptionHandler          _subscriptionHandler;
        int                                 _receiveCount;
        RoonRegister                        _registration;
        Action                              _onRegistration;

        class ReceivedContent
        {
            public int ContentLength;
            public string ContentType;
            public string MessageType;
            public string Information;
            public string Body;
            public byte[] Data;
        }

        public RoonApi(Func<string, Task> onPaired, Func<string, Task> onUnPaired, string configPath = "C:\\Temp\\", ILogger logger = null)
        {
            _onPaired = onPaired;
            _onUnPaired = onUnPaired;
            _logger = logger == null ? NullLogger.Instance : logger;
            _requestId = 0;
            if (!configPath.EndsWith("\\"))
            {
                configPath += "\\";
            }
            _configPath = configPath;
            _coreId = null;
            _requests = new Dictionary<int, RoonRequest>();
            _jsonSettings = new JsonSerializerSettings();
            _jsonSettings.NullValueHandling = NullValueHandling.Ignore;
            _lock = new AsyncLock();
            _services = new Dictionary<string, OnRoonReceived>();
            _subscriptions = new Dictionary<int, OnRoonReceived>();
            _subscriptionHandler = new RoonApiSubscriptionHandler();
            _configuration = new Dictionary<string, object>();

            _savings = LoadRoonSettings();
        }
        public ILogger Logger { get => _logger; }
        public bool Paired
        {
            get => _paired;
            set => SetProperty(ref _paired, value);
        }
        internal void AddService (string service, OnRoonReceived handler)
        {
            _services.Add(service, handler);
        }
        internal void AddSubscription(int requestId, OnRoonReceived handler)
        {
            _subscriptions.Add(requestId, handler);
        }
        public void StartReceiver (string ip, int port, RoonRegister roonRegister, Action onRegistration = null)
        {
            string uri = $"ws://{ip}:{port}/api";
            _registration = roonRegister;
            _onRegistration = onRegistration;
            Task.Run(async() => await ReceiveLoop(uri));
        }
        public async Task ReceiveLoop (string uri)
        {
            do
            {
                try
                {
                    _receiveCount = 0;
                    _logger.LogInformation($"Start Receiving at '{uri}'");
                    var webSocket = new ClientWebSocket();
                    _cancellationTokenSource = new CancellationTokenSource();
                    await webSocket.ConnectAsync(new Uri(uri), _cancellationTokenSource.Token);
                    _webSocket = webSocket;     // For the senders
                    await Receive();
                    _webSocket = null;
                    webSocket.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ReceiveLoop error {ex.Message}, {_receiveCount} received");
                }
                if (_receiveCount > 10)
                    await Task.Delay(1000);
                else
                    await Task.Delay(60000);
                _requests.Clear();
            } while (!_cancellationTokenSource.IsCancellationRequested);
            _logger.LogError("ReceiveLoop exit");    
        }

        public async Task<RoonReply> GetRegistryInfo ()
        {
            var info = await SendReceive<RoonReply>(ServiceRegistry + "/info");
            _coreId = info.CoreId;
            return info;
        }
        public async Task<bool> GetRegistryInfo (Action<RoonReply> action)
        {
            RoonRequest request = new RoonRequest((info, id, body) =>
            {
                RoonReply reply = JsonConvert.DeserializeObject<RoonReply>(body);
                action(reply);
                return Task.FromResult(true);
            });
            int requestId = _requestId++;
            _requests.Add(requestId, request);
            return await Send(ServiceRegistry + "/info", requestId);
        }
        public async Task<bool> RegisterService(RoonRegister service)
        {
            string token;

            _savings.RoonState.Tokens.TryGetValue(_coreId, out token);
            service.Token = token;
            string bodyString = JsonConvert.SerializeObject(service, _jsonSettings);
            int requestId = _requestId++;
            AddSubscription(requestId, OnRegistrationReceived);
            bool rc = await Send(ServiceRegistry + "/register", requestId, bodyString);
            return rc;
        }
        internal Task<bool> OnRegistrationReceived (string information, int requestId, string body)
        {
            switch (information)
            {
                case "Registered":
                    string token;
                    _savings.RoonState.Tokens.TryGetValue(_coreId, out token);
                    RoonRegisterReply registration = JsonConvert.DeserializeObject<RoonRegisterReply>(body);
                    _logger.LogInformation($"Registration done {registration.DisplayName}");
                    if (registration.Token != null && (token == null || token != registration.Token))
                    {
                        if (token != null)
                            _savings.RoonState.Tokens[_coreId] = registration.Token;
                        else
                            _savings.RoonState.Tokens.Add(_coreId, registration.Token);
                        SaveRoonSettings(_savings);
                    }
                    if (_onRegistration != null)
                        _onRegistration();
                    break;
            }
            return Task.FromResult(true);
        }
        internal async Task<int> SendSubscription (string command, int subscriptionKey)
        {
            string bodyString = JsonConvert.SerializeObject(new RoonApiSubscriptionHandler.Subscription { SubscriptionKey = subscriptionKey  }, _jsonSettings);
            int requestId = _requestId++;
            bool rc = await Send(command, requestId, bodyString);
            return rc ? requestId : -1;

        }
        internal async Task<RESULT> SendReceive<RESULT,REQUEST>(string command, REQUEST body)
        {
            string bodyString = JsonConvert.SerializeObject(body, _jsonSettings);
            return await SendReceive<RESULT>(command, bodyString);
        }
        internal async Task<RESULT> SendReceive<RESULT> (string command, string body = null, string contentType = "application/json")
        {
            RoonRequest request = new RoonRequest();
            int requestId = _requestId++;
            _requests.Add(requestId, request);

            bool rc = await Send(command, requestId, body, contentType);

            await request.Event.Task;
            RESULT obj = default(RESULT);
            if (typeof(RESULT) == typeof(byte[]))
            {
                obj = (RESULT)(object)request.DataResult;
            }
            else if (typeof(RESULT) == typeof(bool))
            {
                obj = (RESULT)(object)rc;    
            }
            else
            {
                string str = request.StringResult;
                obj = JsonConvert.DeserializeObject<RESULT>(str);
            }
            _requests.Remove(requestId);
            return obj;
        }
        async Task<bool> Send(string command, int requestId, string body = null, string contentType = "application/json")
        {
            using (var release = await _lock.LockAsync())
            {
                try
                {
                    string send;
                    if (body == null)
                        send = $"MOO/1 REQUEST {command}\nRequest-Id: {requestId}\n\n";
                    else
                        send = $"MOO/1 REQUEST {command}\nRequest-Id: {requestId}\nContent-Length: {body.Length}\nContent-Type: {contentType}\n\n" + body;

                    _logger.LogTrace(send);

                    return await SendData(send);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Send error {ex.Message} at {command}");
                    return false;
                }
            }
        }
        public async Task<bool> Reply(string command, int requestId, bool cont = false, string body = null, bool silent = false, string contentType = "application/json")
        {
            using (var release = await _lock.LockAsync())
            {
                try
                {
                    string send;
                    if (body == null)
                        send = $"MOO/1 {(cont ? "CONTINUE" : "COMPLETE")} {command}\nRequest-Id: {requestId}\n\n";
                    else
                        send = $"MOO/1 {(cont ? "CONTINUE" : "COMPLETE")} {command}\nRequest-Id: {requestId}\nContent-Length: {body.Length}\nContent-Type: {contentType}\n\n" + body;

                    if (!silent)
                        _logger.LogTrace(send);

                    return await SendData(send);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Reply error {ex.Message} at {command}");
                    return false;
                }
            }
        }
        private async Task<bool> SendData (string data, int timeout = 120000)
        {
            for (int i = 0; i < timeout; i++)
            {
                if (_webSocket != null)
                {
                    ArraySegment<byte> dataSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(data));
                    await _webSocket.SendAsync(dataSend, WebSocketMessageType.Binary, true, _cancellationTokenSource.Token);
                    return true;
                }
                await Task.Delay(100);
            }
            _logger.LogError("Wait for socket timed out");
            return false;
        }
        async Task ParseResult(byte[] data, int byteCount, ReceivedContent content,  Dictionary<string, string> headerDict)
        {
            content.Body = null;
            content.Data = null;
            content.ContentLength = 0;
            content.ContentType = null;

            int index = ParseHeader(data, out content.MessageType, out content.Information, headerDict);
            string help;

            if (headerDict.TryGetValue("Content-Length", out help))
            {
                content.ContentLength = Int32.Parse(help);
                int copiedLength = 0;
                int dataLength = byteCount - index;
                byte[] buffer = new byte[content.ContentLength];
                Array.Copy(data, index, buffer, copiedLength, dataLength);
                copiedLength += dataLength;
                while (copiedLength < content.ContentLength)
                {
                    ArraySegment<byte> dataRecv = new ArraySegment<byte>(data);
                    var result = await _webSocket.ReceiveAsync(dataRecv, _cancellationTokenSource.Token);
                    Array.Copy(data, 0, buffer, copiedLength, result.Count);
                    copiedLength += result.Count;
                }
                content.Data = buffer;
                if (headerDict.TryGetValue("Content-Type", out content.ContentType) && content.ContentType.StartsWith ("image"))
                {
                    _logger.LogTrace($"{content.MessageType} {content.Information} : image[{content.ContentLength}]");
                }
                else
                {
                    content.Body = Encoding.UTF8.GetString(buffer);
                    _logger.LogTrace($"{content.MessageType} {content.Information} [{content.ContentLength}]: {content.Body}");
                }
            }
            else
            {
                if (!content.Information.Contains ("ping"))
                _logger.LogTrace($"{content.MessageType} {content.Information}");
            }
        }
        int ParseHeader (byte[]data, out string messageType, out string information, Dictionary<string, string> headerDict)
        {
            int end, start;
            string line1 = null;
            messageType = information = null;
            for (end = start = 0; end < data.Length; end++)
            {
                if (data[end] == 0xa)
                {
                    int count = end - start;
                    if (count == 0)     // \n\n detected
                    {
                        break;
                    }
                    string str = Encoding.UTF8.GetString(data, start, count);
                    if (line1 == null)
                        line1 = str;
                    else
                    {
                        string[] header = str.Split(new char[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (header.Length >= 2)
                            headerDict.Add(header[0], header[1]);
                    }
                    start = end + 1;
                }
            }
            string[] split = line1.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length > 1)
                messageType = split[1];
            if (split.Length > 2)
                information = split[2];
            return end + 1;
        }
        public async Task Receive ()
        {
            Dictionary<string, string> headerDict = new Dictionary<string, string>();
            ReceivedContent content = new ReceivedContent();
            byte[] buffer = new byte[10000];
            int requestId = 0;
            bool rc;

            _coreId = null;
            rc = await GetRegistryInfo(async (reply) =>
            {
                _logger.LogInformation($"Registry Info {reply.DisplayName} ID {reply.CoreId}");
                _coreId = reply.CoreId;
                rc = await RegisterService(_registration);
            });
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Initialisation
                    ArraySegment<byte> dataRecv = new ArraySegment<byte>(buffer);
                    var result = await _webSocket.ReceiveAsync(dataRecv, _cancellationTokenSource.Token);
                    headerDict.Clear();
                    await ParseResult (dataRecv.Array, result.Count, content, headerDict);
                    string requestIdString;
                    if (headerDict.TryGetValue("Request-Id", out requestIdString))
                    {
                        Int32.TryParse(requestIdString, out requestId);
                    }
                    switch (content.MessageType)
                    {
                        case MessageRequest:
                            string replyBody;
                            switch (content.Information)
                            {
                                case ServicePing + "/ping":
                                    rc = await Reply("Success", requestId, false, null, true);
                                    break;
                                case ServicePairing + "/subscribe_pairing":
                                    _subscriptionHandler.AddSubscription(content.Body, requestId);
                                    replyBody = JsonConvert.SerializeObject(new RoonPaired { PairedCoreId = _coreId });
                                    rc = await Reply("Subscribed", requestId, false, replyBody);
                                    _savings.RoonState.PairedCoreId = _coreId;
                                    SaveRoonSettings(_savings);
                                    Paired = true;
                                    if (_onPaired != null)
                                        await _onPaired(_coreId);
                                    break;
                                case ServicePairing + "/unsubscribe_pairing":
                                    _subscriptionHandler.RemoveSubscription(content.Body);
                                    rc = await Reply("Unsubscribed", requestId);
                                    _savings.RoonState.PairedCoreId = string.Empty;
                                    SaveRoonSettings(_savings);
                                    Paired = false;
                                    if (_onUnPaired != null)
                                        await _onUnPaired(_coreId);
                                    break;
                                default:
                                    int idx = content.Information.IndexOf('/');
                                    if (idx > 0)
                                    {
                                        string service = content.Information.Substring(0, idx);
                                        OnRoonReceived onRequestReceived;
                                        if (_services.TryGetValue(service, out onRequestReceived))
                                        {
                                            rc = await onRequestReceived(content.Information, requestId, content.Body);
                                            break;
                                        }
                                    }
                                    _logger.LogError($"REQUEST {content.Information}");
                                    break;
                            }
                            break;
                        case MessageComplete:
                            RoonRequest request;
                            if (_requests.TryGetValue(requestId, out request))
                            {
                                if (request.OnReceived != null)
                                {
                                    _requests.Remove(requestId);
                                    await request.OnReceived(content.Information, requestId, content.Body);
                                }
                                else
                                {
                                    request.StringResult = content.Body;
                                    request.DataResult = content.Data;
                                    request.Event.SetResult(true);
                                }
                            }
                            else
                                _logger.LogError($"COMPLETE {content.Information}");
                            break;
                        case MessageContinue:
                            OnRoonReceived onContinueReceived;
                            if (_subscriptions.TryGetValue(requestId, out onContinueReceived))
                            {
                                rc = await onContinueReceived(content.Information, requestId, content.Body);
                                break;
                            }
                            else
                                _logger.LogError($"CONTINUE {content.Information}");

                            break;
                    }
                    _receiveCount++;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogTrace("Receive Cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Receive error {ex.Message}");
                    break;
                }
            }
            _logger.LogInformation($"Stop Receiving, {_receiveCount} received");
        }
        public void Close()
        {
            if (_webSocket == null)
                return;
            try
            {
                _logger.LogTrace("Closing");
                //await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None);
                _cancellationTokenSource.Cancel();
                _requests.Clear();
                _webSocket.Dispose();
                _logger.LogTrace("Closed");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Close Error {ex.Message}");
            }
        }
        public void SaveRoonSettings (RoonSavings settings)
        {
            SaveConfig(typeof(RoonSavings).Name, settings);
        }
        public RoonSavings LoadRoonSettings ()
        {
            RoonSavings settings = LoadConfig<RoonSavings>(typeof(RoonSavings).Name);
            if (settings == null)
                settings = new RoonSavings();
            return settings;
        }
        public object LoadConfig (string key)
        {
            object config;
            if (_configuration.Count == 0)
            {
                if (!LoadConfiguration())
                    return null;
            }
            _configuration.TryGetValue(key, out config);
            return config;
        }
        public T LoadConfig<T> (string key)
        {
            object config = LoadConfig(key);
            if (config == null)
                return default(T);
            if (config.GetType() == typeof(T))
                return (T)config;
            string str = JsonConvert.SerializeObject(config);
            T result = JsonConvert.DeserializeObject<T>(str);
            _configuration[key] = result;
            return result;
        }
        public void SaveConfig<T> (string key, T config)
        {
            lock (this)
            {
                object oldConfig;
                if (_configuration.TryGetValue(key, out oldConfig))
                    _configuration[key] = config;
                else
                    _configuration.Add(key, config);
            }
            SaveConfiguration();
        }
        bool LoadConfiguration()
        {
            lock (this)
            {
                try
                {
                    string str = File.ReadAllText(_configPath + "Configuration.json");
                    _logger.LogTrace($"LoadConfiguration : {str}");
                    _configuration = JsonConvert.DeserializeObject<Dictionary<string, object>>(str);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"LoadRoonSettings : Error {ex.Message}");
                    _configuration = new Dictionary<string, object>();
                    return false;
                }
            }
        }
        void SaveConfiguration()
        {
            lock (this)
            {
                string str = JsonConvert.SerializeObject(_configuration);
                _logger.LogTrace($"SaveConfiguration : {str}");
                File.WriteAllText(_configPath + "Configuration.json", str);
            }
        }
    }
}
