using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RoonApiLib;
using StreamerScreen.ViewModels;

namespace StreamerScreen.Views;

public partial class MainWindow : Window
{
    private readonly string[] _zonesToMonitor;
    private readonly string _bindInterface;

    private readonly MainWindowViewModel _viewModel = new();
    private readonly ILoggerFactory _loggerFactory;
    private Discovery.Result? _core;
    private readonly RoonApi _api;
    private readonly RoonApiTransport _apiTransport;
    private readonly RoonApi.RoonRegister _roonRegister;
    private readonly string _myIpAddress;
    private string? _currentZoneId;

    public MainWindow()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        _zonesToMonitor = configuration["ZonesToMonitor"]!.Split(",", StringSplitOptions.TrimEntries);
        _bindInterface = configuration["BindInterface"]!;
        
        DataContext = _viewModel;
            
        InitializeComponent();

        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        _loggerFactory = new LoggerFactory();
        _api = new RoonApi(OnPaired, OnUnPaired, appDir, _loggerFactory.CreateLogger("RoonApi"));
        _apiTransport = new RoonApiTransport(_api);
        _myIpAddress = GetIpAddress();
        _roonRegister = new RoonApi.RoonRegister
        {
            DisplayName = "Streamer Display",
            DisplayVersion = "1.0.0",
            Publisher = "mones88",
            Email = "mones88@gmail.com",
            WebSite = "https://github.com/christian-riedl/roon-extension-test",
            ExtensionId = "it.mones88.streamerdisplay",
            Token = null,
            OptionalServices = new string[0],
            RequiredServices = new string[] {RoonApi.ServiceTransport, RoonApi.ServiceImage, RoonApi.ServiceBrowse,},
            ProvidedServices = new string[]
            {
                RoonApi.ServiceStatus, RoonApi.ServicePairing, RoonApi.ServiceSettings, RoonApi.ServicePing,
                RoonApi.ControlVolume, RoonApi.ControlSource
            }
        };
    }

    private string GetIpAddress()
    {
        var ni = NetworkInterface.GetAllNetworkInterfaces()
            .Single(x => x.Name == _bindInterface);
        
        foreach (var ip in ni.GetIPProperties().UnicastAddresses)
        {
            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ip.Address.ToString();
            }
        }

        throw new Exception();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Task.Factory.StartNew(async () =>
        {
            await DiscoverCore();
            await Task.Run(() => _api.StartReceiver(_core!.CoreIPAddress, _core.HttpPort, _roonRegister));
        });
    }


    async Task DiscoverCore(string? coreName = null)
    {
        Discovery discovery = new Discovery(_myIpAddress, 10000, _loggerFactory.CreateLogger("Discovery"));
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

    async Task OnPaired(string coreId)
    {
        var zones = await _apiTransport.SubscribeZones(0, OnZooneChanged);
        Console.WriteLine($"OnPaired: {coreId} {zones}");
    }

    Task OnUnPaired(string coreId)
    {
        Console.WriteLine(coreId);
        return Task.CompletedTask;
    }

    async Task OnZooneChanged(RoonApiTransport.ChangedZoones changedZones)
    {
        var zonesToMonitor = (changedZones.ZonesAdded ?? [])
            .Union(changedZones.ZonesChanged ?? [])
            .Where(z => _zonesToMonitor.Contains(z.DisplayName))
            .ToArray();
        
        var zone = zonesToMonitor.FirstOrDefault(z => z.State == RoonApiTransport.EState.playing);
        if (zone == null)
        {
            zone = zonesToMonitor.FirstOrDefault(z => z.ZoneId == _currentZoneId) 
                   ?? zonesToMonitor.FirstOrDefault();
        }

        _currentZoneId = zone?.ZoneId;
        await _viewModel.UpdateFromZone(zone, _core!);
    }
}