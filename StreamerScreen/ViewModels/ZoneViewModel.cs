using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MaterialColorUtilities.Utils;
using RoonApiLib;
using StreamerScreen.Models;

namespace StreamerScreen.ViewModels;

public class ZoneViewModelDesign : ZoneViewModel
{
    public ZoneViewModelDesign()
    {
        _zone = "Nessuna zona attiva";
        _artist = "Genesis";
        _album = "Foxtrot";
        _track = "Hope Is a Dangerous Thing for a Woman Like Me to Have - but I have It";
        _totalSeconds = 208;
        _actualSeconds = 45;
        _isConnectedToRoon = true;
        _cover = ImageHelper.LoadFromResource(new Uri("avares://StreamerScreen/Assets/ui-cover.jpeg"))!;
        _progressColor = CalculateProgressColor();
    }
}

public class ZoneViewModel : ViewModelBase
{
    protected string? _zone;
    protected string? _artist;
    protected string? _album;
    protected string? _track;
    protected string? _imageKey;
    protected Bitmap? _cover;
    protected int _totalSeconds;
    protected int _actualSeconds;
    protected bool _isConnectedToRoon;
    protected Brush _progressColor;

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

    public Bitmap? Cover
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

    public Brush ProgressColor
    {
        get => _progressColor;
        set => _progressColor = value;
    }

    public async Task UpdateFromZone(RoonZone zone, RoonConnection connection)
    {
        SetProperty(ref _zone, zone.DisplayName, nameof(Zone));
        SetProperty(ref _artist, zone.Artist, nameof(Artist));
        SetProperty(ref _album, zone.Album, nameof(Album));
        SetProperty(ref _track, zone.Track, nameof(Track));
        SetProperty(ref _totalSeconds, zone.TotalSeconds, nameof(TotalSeconds));
        SetProperty(ref _actualSeconds, zone.PositionSeconds, nameof(ActualSeconds));
        if (_imageKey != zone.AlbumArtImageKey)
        {
            _imageKey = zone.AlbumArtImageKey;
            var url =
                $"http://{connection.ServerAddress}:{connection.HttpPort}/api/image/{_imageKey}?scale=fit&width=1024&height=768";
            var bmp = await ImageHelper.LoadFromWeb(new Uri(url));
            SetProperty(ref _cover, bmp, nameof(Cover));
            SetProperty(ref _progressColor, CalculateProgressColor(), nameof(ProgressColor));
        }
    }

    protected unsafe Brush CalculateProgressColor()
    {
        var scaled = _cover!.CreateScaledBitmap(new PixelSize(100, 100), BitmapInterpolationMode.MediumQuality);

        var pixels = new byte[4 * 100 * 100]; //BGRA 8888
        fixed (byte* buff = pixels)
        {
            //int stride = 4 * ((width * bytesPerPixel + 3) / 4);
            int stride = 4 * ((100 * 4 + 3) / 4);
            scaled.CopyPixels(new PixelRect(0, 0, 100, 100), (IntPtr) buff, pixels.Length, stride);
        }

        var argb = new uint[pixels.Length / 4];
        for (int i = 0; i < argb.Length; i += 4)
        {
            int px = pixels[i + 3] << 24;
            px |= pixels[i + 2] << 16;
            px |= pixels[i + 1] << 8;
            px |= pixels[i];
            argb[i / 4] = (uint) px;
        }

        var colors = ImageUtils.ColorsFromImage(argb);

        var brush = new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft,
            EndPoint = RelativePoint.BottomRight
        };
        var maxColors = Math.Min(2, colors.Count);
        brush.GradientStops.AddRange(colors.Take(maxColors).Select((c, num) =>
            new GradientStop {Color = Color.Parse($"#{c:X}"), Offset = (num + 1) / (float) maxColors}));

        return brush;
    }
}