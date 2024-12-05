namespace StreamerScreen.Models;

public class Settings
{
    public required string ZonesToMonitor { get; set; }
    public required string BindInterface { get; set; }
    public bool FullScreen { get; set; }
    public bool ScreenControlEnabled { get; set; }
    public string? ScreenOffCommand{ get; set; }
    public string? ScreenOnCommand { get; set; }
    public int ScreenOffDelaySeconds { get; set; }
    public string? ScreenLowBrightenessCommand { get; set; }
    public string? ScreenNormalBrightnessCommad { get; set; }
    public int ScreenLowBrightnessDelaySeconds { get; set; }
}