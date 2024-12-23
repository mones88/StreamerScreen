using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CliWrap;
using StreamerScreen.Models;
using StreamerScreen.ViewModels;
using Exception = System.Exception;

namespace StreamerScreen.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    private CancellationTokenSource _ctGoToIdleState = new();
    private bool _isFirstRoonConnection = true;
    private RoonPlayStatus _lastRoonPlayStatus = RoonPlayStatus.NotPlaying;

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

            if (App.Settings.FullScreen)
            {
                WindowState = WindowState.FullScreen;
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (App.Settings.ScreenControlEnabled &&
            !string.IsNullOrWhiteSpace(App.Settings.ScreenNormalBrightnessCommad))
        {
            Cli.Wrap("/usr/bin/xrandr")
                .WithArguments(App.Settings.ScreenOffCommand ?? string.Empty)
                .ExecuteAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();

            Cli.Wrap("/usr/bin/xrandr")
                .WithArguments(App.Settings.ScreenOnCommand ?? string.Empty).ExecuteAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        base.OnClosing(e);
    }

    private async Task SetIdleState(bool immediate, CancellationToken token)
    {
        try
        {
            Console.WriteLine("Going idle..");
            if (!immediate)
            {
                Console.WriteLine("in 5 secs");
                await Task.Delay(5000, token);
            }

            if (!token.IsCancellationRequested)
            {
                _viewModel.SetIdleState();
            }

            Console.WriteLine("Idle.");

            if (App.Settings.ScreenControlEnabled)
            {
                if (App.Settings.ScreenLowBrightnessDelaySeconds > 0)
                    await Task.Delay(App.Settings.ScreenLowBrightnessDelaySeconds * 1000, token);

                if (!string.IsNullOrWhiteSpace(App.Settings.ScreenLowBrightenessCommand))
                    await Cli.Wrap("/usr/bin/bash")
                        .WithArguments(["-c", App.Settings.ScreenLowBrightenessCommand])
                        .ExecuteAsync(token);

                if (App.Settings.ScreenOffDelaySeconds > 0)
                    await Task.Delay(App.Settings.ScreenOffDelaySeconds * 1000, token);

                await Cli.Wrap("/usr/bin/xrandr")
                    .WithArguments(App.Settings.ScreenOffCommand ?? string.Empty)
                    .ExecuteAsync(token);
            }
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Idle request aborted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async Task SetPlayingState()
    {
        if (App.Settings.ScreenControlEnabled)
        {
            if (!string.IsNullOrWhiteSpace(App.Settings.ScreenNormalBrightnessCommad))
                await Cli.Wrap("/bin/bash")
                    .WithArguments(["-c", App.Settings.ScreenNormalBrightnessCommad])
                    .ExecuteAsync();

            await Cli.Wrap("/usr/bin/xrandr")
                .WithArguments(App.Settings.ScreenOnCommand ?? string.Empty).ExecuteAsync();
        }

        _viewModel.SetPlayingState();
    }

    private async void OnRoonStatusChanged(RoonStatus newStatus)
    {
        if (_lastRoonPlayStatus != newStatus.RoonPlayStatus)
        {
            _lastRoonPlayStatus = newStatus.RoonPlayStatus;

            if (newStatus.RoonPlayStatus == RoonPlayStatus.NotPlaying)
            {
                _ctGoToIdleState?.Dispose();
                _ctGoToIdleState = new CancellationTokenSource();
                await SetIdleState(false, _ctGoToIdleState.Token);
            }
            else
            {
                _ctGoToIdleState?.Cancel();
                await SetPlayingState();
            }
        }

        if (newStatus.RoonPlayStatus == RoonPlayStatus.Playing && newStatus.ActiveZone != null)
        {
            await _viewModel.UpdateZoneData(newStatus);
        }
    }

    private async void OnConnectedToRoon()
    {
        if (_isFirstRoonConnection)
        {
            _isFirstRoonConnection = false;
            await SetIdleState(true, _ctGoToIdleState.Token);
        }
    }

    private void OnDisconnectedFromRoon()
    {
        _viewModel.SetDisconnectedState();
    }
}