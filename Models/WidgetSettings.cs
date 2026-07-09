namespace TimeWidget.Models;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Topmost { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsResizable { get; set; }
    public string? FolderPath { get; set; }
    public double? FolderLeft { get; set; }
    public double? FolderTop { get; set; }
    public double? FolderWidth { get; set; }
    public double? FolderHeight { get; set; }
    public bool FolderTopmost { get; set; } = true;
    public bool FolderIsLocked { get; set; }
    public bool FolderIsResizable { get; set; }
    public double? MediaLeft { get; set; }
    public double? MediaTop { get; set; }
    public double? MediaWidth { get; set; }
    public double? MediaHeight { get; set; }
    public bool MediaTopmost { get; set; } = true;
    public bool MediaIsLocked { get; set; }
    public bool MediaIsResizable { get; set; }
    public double? WeatherLeft { get; set; }
    public double? WeatherTop { get; set; }
    public double? WeatherWidth { get; set; }
    public double? WeatherHeight { get; set; }
    public bool WeatherTopmost { get; set; } = true;
    public bool WeatherIsLocked { get; set; }
    public bool WeatherIsResizable { get; set; }
    public bool WeatherLocationDenied { get; set; }
    public string? WeatherCityCode { get; set; }
    public string? WeatherCityName { get; set; }
    public double? TimerLeft { get; set; }
    public double? TimerTop { get; set; }
    public bool TimerTopmost { get; set; } = true;
    public bool TimerIsLocked { get; set; }
    public string TimerMode { get; set; } = "Stopwatch";
    public int TimerDefaultMinutes { get; set; } = 25;
    public AiSearchSettings AiSearch { get; set; } = new();
    public double? CalendarLeft { get; set; }
    public double? CalendarTop { get; set; }
    public double? CalendarWidth { get; set; }
    public double? CalendarHeight { get; set; }
    public bool CalendarTopmost { get; set; } = true;
    public bool CalendarIsLocked { get; set; }
    public bool CalendarIsResizable { get; set; }
    public double? ScheduleLeft { get; set; }
    public double? ScheduleTop { get; set; }
    public double? ScheduleWidth { get; set; }
    public double? ScheduleHeight { get; set; }
    public bool ScheduleTopmost { get; set; } = true;
    public bool ScheduleIsLocked { get; set; }
    public bool ScheduleIsResizable { get; set; } = true;
    public bool ScheduleIsLightMode { get; set; }
    public DateTime? ScheduleTermStartDate { get; set; }
    public double? VpnLeft { get; set; }
    public double? VpnTop { get; set; }
    public double? VpnWidth { get; set; }
    public double? VpnHeight { get; set; }
    public bool VpnTopmost { get; set; } = true;
    public bool VpnIsLocked { get; set; }
    public bool VpnIsResizable { get; set; }
    public double? QuickSettingsLeft { get; set; }
    public double? QuickSettingsTop { get; set; }
    public double? QuickSettingsWidth { get; set; }
    public double? QuickSettingsHeight { get; set; }
    public bool QuickSettingsTopmost { get; set; } = true;
    public bool QuickSettingsIsLocked { get; set; }
    public bool QuickSettingsIsResizable { get; set; }
    public bool QuickSettingsAirplaneMode { get; set; }
    public double? PerformanceLeft { get; set; }
    public double? PerformanceTop { get; set; }
    public bool PerformanceTopmost { get; set; } = true;
    public bool PerformanceIsLocked { get; set; }
}
