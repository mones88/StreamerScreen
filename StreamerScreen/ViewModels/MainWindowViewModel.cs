using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using RoonApiLib;

namespace StreamerScreen.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private string? _zone;
    private string? _artist;
    private string? _album;
    private string? _track;
    private string? _imageKey;
    private Task<Bitmap?>? _cover;
    private int _totalSeconds;
    private int _actualSeconds;

    public string? Zone
    {
        get => _zone;
        set => _zone = value;
    }

    public string? Artist
    {
        get => _artist;
        set => _artist = value;
    }

    public string? Album
    {
        get => _album;
        set => _album = value;
    }

    public string? Track
    {
        get => _track;
        set => _track = value;
    }

    public Task<Bitmap?>? Cover
    {
        get => _cover;
        set => _cover = value;
    }

    public int TotalSeconds
    {
        get => _totalSeconds;
        set => _totalSeconds = value;
    }

    public int ActualSeconds
    {
        get => _actualSeconds;
        set => _actualSeconds = value;
    }

    public MainWindowViewModel()
    {
        _zone = "Nessuna zona attiva";
        _cover = Task.FromResult(ImageHelper.LoadFromResource(new Uri("avares://StreamerScreen/Assets/ui-cover.jpeg")));
        _artist = "Genesis";
        _album = "Foxtrot";
        _track = "Beauty And The Beast";
        _totalSeconds = 208;
        _actualSeconds = 45;
    }

    public async Task UpdateFromZone(RoonApiTransport.Zone? zone, Discovery.Result core)
    {
        if (zone is null)
        {
            SetProperty(ref _zone, "Nessuna zona attiva", nameof(Zone));
            SetProperty(ref _artist, string.Empty, nameof(Artist));
            SetProperty(ref _album, string.Empty, nameof(Album));
            SetProperty(ref _track, string.Empty, nameof(Track));
        }
        else
        {
            SetProperty(ref _zone, zone.DisplayName, nameof(Zone));
            if (zone.NowPlaying != null)
            {
                SetProperty(ref _artist, zone.NowPlaying.ThreeLine.Line2, nameof(Artist));
                SetProperty(ref _album, zone.NowPlaying.ThreeLine.Line3, nameof(Album));
                SetProperty(ref _track, zone.NowPlaying.ThreeLine.Line1, nameof(Track));
                SetProperty(ref _totalSeconds, zone.NowPlaying.Length, nameof(TotalSeconds));
                SetProperty(ref _actualSeconds, zone.NowPlaying.SeekPosition.GetValueOrDefault(), nameof(ActualSeconds));
                if (_imageKey != zone.NowPlaying.ImageKey && zone.NowPlaying.ImageKey != null)
                {
                    _imageKey = zone.NowPlaying.ImageKey;
                    var url = $"http://{core.CoreIPAddress}:{core.HttpPort}/api/image/{_imageKey}?scale=fit&width=800&height=600";
                    SetProperty(ref _cover, ImageHelper.LoadFromWeb(new Uri(url)), nameof(Cover));
                }
            }
        }
    }
}