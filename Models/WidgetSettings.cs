namespace TimeWidget.Models;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public bool IsLocked { get; set; }
    public string? FolderPath { get; set; }
    public double? FolderLeft { get; set; }
    public double? FolderTop { get; set; }
    public bool FolderTopmost { get; set; } = true;
    public bool FolderIsLocked { get; set; }
    public double? MediaLeft { get; set; }
    public double? MediaTop { get; set; }
    public bool MediaTopmost { get; set; } = true;
    public bool MediaIsLocked { get; set; }
    public double? WeatherLeft { get; set; }
    public double? WeatherTop { get; set; }
    public bool WeatherTopmost { get; set; } = true;
    public bool WeatherIsLocked { get; set; }
    public bool WeatherLocationDenied { get; set; }
    public string? WeatherCityCode { get; set; }
    public string? WeatherCityName { get; set; }
}
