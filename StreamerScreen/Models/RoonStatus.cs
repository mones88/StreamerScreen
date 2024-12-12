using RoonApiLib;

namespace StreamerScreen.Models;

public class RoonStatus
{
    public RoonZone? ActiveZone { get; set; }

    public RoonPlayStatus RoonPlayStatus { get; set; }
    
    public bool IsConnectedToRoon { get; set; }
    
    public RoonConnection? RoonConnection { get; set; }

    public void UpdateFromZoneData(RoonApiTransport.Zone zone)
    {
        RoonPlayStatus = zone.State switch
        {
            RoonApiTransport.EState.playing => RoonPlayStatus.Playing,
            _ => RoonPlayStatus.NotPlaying
        };
        
        ActiveZone ??= new RoonZone();
        ActiveZone.DisplayName = zone.DisplayName;
        ActiveZone.Id = zone.ZoneId;
        if (zone.State == RoonApiTransport.EState.playing)
        {
            ActiveZone.Track = zone.NowPlaying.ThreeLine.Line1;
            ActiveZone.Artist = zone.NowPlaying.ThreeLine.Line2;
            ActiveZone.Album = zone.NowPlaying.ThreeLine.Line3;
            ActiveZone.TotalSeconds = zone.NowPlaying.Length;
            ActiveZone.PositionSeconds = zone.NowPlaying.SeekPosition.GetValueOrDefault();
            ActiveZone.AlbumArtImageKey = zone.NowPlaying.ImageKey;
        }
    }
}

public class RoonZone
{
    public string DisplayName { get; set; } = default!;
    public string Id { get; set; } = default!;
    public string Artist { get; set; } = default!;
    public string Album { get; set; } = default!;
    public string Track { get; set; } = default!;
    public string AlbumArtImageKey { get; set; } = default!;
    public int TotalSeconds { get; set; }
    public int PositionSeconds { get; set; }
}

public enum RoonPlayStatus
{
    Playing, NotPlaying 
}

public class RoonConnection
{
    public required string ServerAddress { get; set; }
    public required int HttpPort { get; set; }
}