using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class Discovery : IDisposable
    {
        const int       SOOD_PORT           = 9003;
        const string    SOOD_MULTICAST_IP   = "239.255.90.90";
        const string    SOOD_SID            = "00720724-5143-4a9b-abac-0e50cba674bb";

        UdpClient                   _udpClient;
        IPAddress                   _soodMcAddress;
        int                         _timeout;
        Dictionary<string, string>  _dict;
        ILogger                     _logger;

        public class Message
        {
            public string ServiceId;
            public string TId;
        }
        public class Result
        {
            public string CoreName          { get; set; }
            public string CoreIPAddress     { get; set; }
            public string DisplayVersion    { get; set; }
            public string UniqueId          { get; set; }
            public string ServiceId         { get; set; }
            public string TId               { get; set; }
            public int TcpPort              { get; set; }
            public int HttpPort             { get; set; }
        }
        public Discovery (string myIPAddress, int timeout = 1000, ILogger logger = null)
        {
            _timeout = timeout;
            _logger = logger == null ? NullLogger.Instance : logger;
            IPEndPoint localEp;
            _soodMcAddress = IPAddress.Parse(SOOD_MULTICAST_IP);
            localEp = new IPEndPoint(IPAddress.Parse(myIPAddress), 0);
            _udpClient = new UdpClient(localEp);
        }

        public async Task<List<Result>> QueryServiceId (Func<Result, bool> findFunction = null)
        {
            Message msg = new Message { ServiceId = SOOD_SID, TId = null };
            return await Query(msg, findFunction);
        }
        public async Task<List<Result>> Query (Message msg, Func<Result,bool> findFunction = null)
        {
            if (msg.TId == null)
                msg.TId = Guid.NewGuid().ToString();

            byte[] buffer = new byte[256];
            byte[] sood = Encoding.UTF8.GetBytes("SOOD");
            sood.CopyTo(buffer, 0);
            int index = sood.Length;
            buffer[index++] = 2;
            buffer[index++] = Encoding.UTF8.GetBytes("Q")[0];

            index = AddToBuffer(buffer, index, "query_service_id", msg.ServiceId);
            index = AddToBuffer(buffer, index, "_tid", msg.TId);

            _logger.LogTrace($"Query service id msg : {msg.ServiceId} tid : {msg.TId}");

            List<Result> list = new List<Result>();
            int len = await _udpClient.SendAsync(buffer, index, new IPEndPoint(_soodMcAddress, SOOD_PORT));

            for (; ; )
            {
                Task<UdpReceiveResult> recvResultT;
                await Task.WhenAny(recvResultT = _udpClient.ReceiveAsync(), Task.Delay(_timeout));
                if (recvResultT.Status != TaskStatus.RanToCompletion)
                    break;
                var recvResult = await recvResultT;
                byte[] received = recvResult.Buffer;
                if (received != null && received.Length > 10)
                {
                    string soodString = Encoding.UTF8.GetString(received, 0, 4);
                    string rString = Encoding.UTF8.GetString(received, 5, 1);
                    if (soodString == "SOOD" && received[4] == 2 && rString == "R")
                    {
                        string name, value;
                        index = 6;
                        _dict = new Dictionary<string, string>();
                        while (index < received.Length)
                        {
                            index = DecodeFromBuffer(received, index, out name, out value);
                            _dict.Add(name, value);
                        }
                        string ipAddress = recvResult.RemoteEndPoint.Address.ToString();
                        if (!list.Exists((r) => r.CoreIPAddress == ipAddress))
                        {
                            Result res = new Result();
                            res.CoreIPAddress = ipAddress;
                            if (_dict.TryGetValue("name", out value))
                                res.CoreName = value;
                            if (_dict.TryGetValue("display_version", out value))
                                res.DisplayVersion = value;
                            if (_dict.TryGetValue("unique_id", out value))
                                res.UniqueId = value;
                            if (_dict.TryGetValue("service_id", out value))
                                res.ServiceId = value;
                            if (_dict.TryGetValue("_tid", out value))
                                res.TId = value;
                            if (_dict.TryGetValue("tcp_port", out value))
                                res.TcpPort = Int32.Parse(value);
                            if (_dict.TryGetValue("http_port", out value))
                                res.HttpPort = Int32.Parse(value);

                            list.Add(res);
                            if (_logger != NullLogger.Instance)
                            {
                                string data = JsonConvert.SerializeObject(res);
                                _logger.LogTrace($"Received : {data}");
                            }
                            if (findFunction != null)
                            {
                                if (findFunction(res))
                                    break;
                            }
                        }
                    }
                }
            }
            return list;
        }
        private static int AddToBuffer (byte[]buffer, int index, string name, string value)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            buffer[index++] = (byte)nameBytes.Length;
            for (int i = 0; i < nameBytes.Length; i++)
                buffer[index++] = nameBytes[i];
            byte[] valueBytes = Encoding.UTF8.GetBytes(value);
            buffer[index++] = (byte)(valueBytes.Length >> 8);
            buffer[index++] = (byte)(valueBytes.Length & 0xff);
            for (int i = 0; i < valueBytes.Length; i++)
                buffer[index++] = valueBytes[i];
            return index;
        }
        private static int DecodeFromBuffer(byte[] buffer, int index, out string name, out string value)
        {
            name = value = null;
            if (index >= buffer.Length)
                return buffer.Length;
            int length = buffer[index++];
            name = Encoding.UTF8.GetString(buffer, index, length);
            index += length;
            if (index >= buffer.Length)
                return buffer.Length;
            length = (buffer[index++] << 8) + buffer[index++];
            value = Encoding.UTF8.GetString(buffer, index, length);
            index += length;
            return index;
        }
        public void Dispose()
        {
            _udpClient.Dispose();
        }
    }

}
