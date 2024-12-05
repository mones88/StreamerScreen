using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MaterialColorUtilities.ColorAppearance;
using MaterialColorUtilities.Utils;
using RoonApiLib;
using StreamerScreen.Models;

namespace StreamerScreen.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private ViewModelBase _currentPage;
    private static readonly ZoneViewModel _zoneViewModel = new ();
    
    protected readonly ViewModelBase[] Pages =
    [
        new AwaitingConnectionViewModel(),
        new IdleViewModel(),
        _zoneViewModel
    ];

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public MainWindowViewModel()
    {
        _currentPage = Pages[0];
    }

    public void SetIdleState()
    {
        CurrentPage = Pages[1];
    }

    public void SetDisconnectedState()
    {
        CurrentPage = Pages[0];
    }

    public void SetPlayingState()
    {
        CurrentPage = _zoneViewModel;
    }

    public Task UpdateZoneData(RoonStatus newStatus)
    {
        if (newStatus.ActiveZone == null || newStatus.RoonConnection == null)
            throw new InvalidOperationException();
        
        return _zoneViewModel.UpdateFromZone(newStatus.ActiveZone, newStatus.RoonConnection);
    }
}

public class MainWindowViewModelTestData : MainWindowViewModel
{
    public MainWindowViewModelTestData()
    {
        
    }
}