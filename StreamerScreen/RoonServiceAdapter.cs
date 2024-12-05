using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using RoonApiLib;
using StreamerScreen.Models;

namespace StreamerScreen;

public static class RoonServiceAdapter
{
    public static event Action? OnConnectedToRoon;
    public static event Action? OnDisconnectedFromRoon;
    public static event Action<RoonStatus>? OnRoonStatusChanged;


    private static AsyncContextThread ServiceThread;

    private static Discovery.Result? _core;
    private static RoonApi _api;
    private static RoonApiTransport _apiTransport;
    private static RoonApi.RoonRegister _roonRegister;
    private static string _myIpAddress;
    private static string? _latestZoneId;
    private static ILoggerFactory _loggerFactory;
    private static string[] _zonesToMonitor;

    private static readonly RoonStatus RoonStatus = new RoonStatus();

    public static void StartRoonService()
    {
        ServiceThread = new AsyncContextThread();
        ServiceThread.Factory.StartNew(Bootstrap);
    }

    private static async void Bootstrap()
    {
        var thread = Thread.CurrentThread;
        thread.Name = "RoonServiceThread";

        _zonesToMonitor = App.Settings.ZonesToMonitor.Split(",", StringSplitOptions.TrimEntries);
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        _loggerFactory = new LoggerFactory();
        _api = new RoonApi(OnPaired, OnUnPaired, appDir, _loggerFactory.CreateLogger("RoonApi"));
        _apiTransport = new RoonApiTransport(_api);
        _myIpAddress = GetIpAddress(App.Settings.BindInterface);
        _roonRegister = new RoonApi.RoonRegister
        {
            DisplayName = $"StreamerDisplay@{Dns.GetHostName()}",
            DisplayVersion = "1.0.0",
            Publisher = "mones88",
            Email = "mones88@gmail.com",
            WebSite = "https://github.com/christian-riedl/roon-extension-test",
            ExtensionId = "it.mones88.streamerdisplay",
            Token = null,
            OptionalServices = new string[0],
            RequiredServices =
                new string[] {RoonApi.ServiceTransport, RoonApi.ServiceImage, RoonApi.ServiceBrowse,},
            ProvidedServices = new string[]
            {
                RoonApi.ServiceStatus, RoonApi.ServicePairing, RoonApi.ServiceSettings, RoonApi.ServicePing,
                RoonApi.ControlVolume, RoonApi.ControlSource
            }
        };

        await DiscoverCore();
        _api.StartReceiver(_core!.CoreIPAddress, _core.HttpPort, _roonRegister);
    }

    private static string GetIpAddress(string interfaceName)
    {
        var ni = NetworkInterface.GetAllNetworkInterfaces()
            .Single(x => x.Name == interfaceName);

        foreach (var ip in ni.GetIPProperties().UnicastAddresses)
        {
            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ip.Address.ToString();
            }
        }

        throw new Exception();
    }

    private static async Task DiscoverCore(string? coreName = null)
    {
        Discovery discovery = new Discovery(_myIpAddress, 5000, _loggerFactory.CreateLogger("Discovery"));
        var coreList = await discovery.QueryServiceId((res) =>
        {
            if (coreName != null && res.CoreName == coreName)
            {
                _core = res;
                return true;
            }

            return false;
        });
        if (_core == null)
        {
            if (coreList.Count == 1)
            {
                _core = coreList[0];
            }
            else
            {
                string corenames = string.Join(", ", coreList.Select((s) => s.CoreName));
                throw new Exception("Multiple Roon Cores found");
            }
        }
    }

    private static async Task OnPaired(string coreId)
    {
        RoonStatus.IsConnectedToRoon = true;
        RoonStatus.RoonConnection = new RoonConnection()
        {
            ServerAddress = _core!.CoreIPAddress,
            HttpPort = _core!.HttpPort
        };
        if (OnConnectedToRoon != null)
        {
            Dispatcher.UIThread.Post(() => OnConnectedToRoon());
        }

        var zones = await _apiTransport.SubscribeZones(0, OnZoneChanged);
        Console.WriteLine($"OnPaired: {coreId} {zones}");
    }

    private static Task OnUnPaired(string coreId)
    {
        RoonStatus.IsConnectedToRoon = false;
        RoonStatus.ActiveZone = null;
        RoonStatus.RoonPlayStatus = RoonPlayStatus.Stopped;
        RoonStatus.RoonConnection = null;
        if (OnDisconnectedFromRoon != null)
        {
            Dispatcher.UIThread.Post(() => OnDisconnectedFromRoon());
        }
        Console.WriteLine($"Unpaired from {coreId}");
        return Task.CompletedTask;
    }

    private static Task OnZoneChanged(RoonApiTransport.ChangedZoones changedZones)
    {
        var zonesToMonitor = (changedZones.ZonesAdded ?? [])
            .UnionBy(changedZones.ZonesChanged ?? [], keySelector: z => z.ZoneId)
            .Where(z => _zonesToMonitor.Contains(z.DisplayName))
            .ToArray();
        
        RoonApiTransport.Zone? zone = zonesToMonitor.FirstOrDefault(z => z.State == RoonApiTransport.EState.playing);
        if (zone == null)
        {
            zone = zonesToMonitor.FirstOrDefault(z => z.ZoneId == _latestZoneId);
            if (changedZones.ZonesRemoved?.Contains(_latestZoneId) == true)
            {
                zone = zonesToMonitor.FirstOrDefault();
            }
        }

        if (zone != null)
        {
            Console.WriteLine($"OnZoneChanged {zone.DisplayName} {zone.State}");
            
            RoonStatus.UpdateFromZoneData(zone);
            _latestZoneId = zone.ZoneId;
            if (OnRoonStatusChanged != null)
            {
                Dispatcher.UIThread.Post(() => OnRoonStatusChanged(RoonStatus));
            }
        }
        
        return Task.CompletedTask;

        /*var isPlaying = zone is {State: RoonApiTransport.EState.playing};
        if (_isPlaying != isPlaying)
        {
            if (_isPlaying && !isPlaying)
            {
                //play => pause
                _displayOffTimer.Start();
            }
            else
            {
                //pause => play
                _displayOffTimer.Stop();
                if (_screenControlEnabled)
                {
                    await Cli.Wrap("/usr/bin/xrandr")
                        .WithArguments(_screenOnCommand).ExecuteAsync();
                }
            }

            _isPlaying = isPlaying;
        }

        _currentZoneId = zone?.ZoneId;

        Dispatcher.UIThread.Post(async () => await _viewModel.UpdateFromZone(zone, _core!));*/
    }
}