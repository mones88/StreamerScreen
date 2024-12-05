using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CliWrap;
using MaterialColorUtilities.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RoonApiLib;
using StreamerScreen.Models;
using StreamerScreen.ViewModels;

namespace StreamerScreen.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    private readonly Timer _displayOffTimer;

    private RoonPlayStatus _lastRoonPlayStatus = RoonPlayStatus.Stopped;

    public MainWindow()
    {
        InitializeComponent();

        if (!Design.IsDesignMode)
        {
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;


            RoonServiceAdapter.OnConnectedToRoon += OnConnectedToRoon;
            RoonServiceAdapter.OnDisconnectedFromRoon += OnDisconnectedFromRoon;
            RoonServiceAdapter.OnRoonStatusChanged += OnRoonStatusChanged;
            RoonServiceAdapter.StartRoonService();

            _displayOffTimer = new Timer(TimeSpan.FromSeconds(App.Settings.ScreenOffDelaySeconds));
            _displayOffTimer.AutoReset = false;
            _displayOffTimer.Elapsed += TurnScreenOff;
        }
    }

    private async void OnRoonStatusChanged(RoonStatus newStatus)
    {
        if (_lastRoonPlayStatus != newStatus.RoonPlayStatus)
        {
            _lastRoonPlayStatus = newStatus.RoonPlayStatus;
            
            if (newStatus.RoonPlayStatus is RoonPlayStatus.Stopped or RoonPlayStatus.Paused)
            {
                _viewModel.SetIdleState();
                if (App.Settings.ScreenControlEnabled)
                {
                    _displayOffTimer.Start();
                }
            }
            else
            {
                _displayOffTimer.Stop();
                if (App.Settings.ScreenControlEnabled)
                {
                    await Cli.Wrap("/usr/bin/xrandr")
                        .WithArguments(App.Settings.ScreenOnCommand ?? string.Empty).ExecuteAsync();
                }

                _viewModel.SetPlayingState();
            }
        }

        if (newStatus.RoonPlayStatus == RoonPlayStatus.Playing && newStatus.ActiveZone != null)
        {
            await _viewModel.UpdateZoneData(newStatus);
        }
    }

    private void OnConnectedToRoon()
    {
        _viewModel.SetIdleState();
    }

    private void OnDisconnectedFromRoon()
    {
        _viewModel.SetDisconnectedState();
    }


    private async void TurnScreenOff(object? sender, ElapsedEventArgs e)
    {
        await Cli.Wrap("/usr/bin/xrandr")
            .WithArguments(App.Settings.ScreenOffCommand ?? string.Empty)
            .ExecuteAsync();
    }


    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (App.Settings.FullScreen)
        {
            WindowState = WindowState.FullScreen;
        }
    }
}