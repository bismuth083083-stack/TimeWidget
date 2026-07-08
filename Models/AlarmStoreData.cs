namespace TimeWidget.Models;

public sealed class AlarmStoreData
{
    public List<AlarmInfo> Alarms { get; set; } = [];
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Topmost { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsResizable { get; set; }
    public int DefaultSnoozeMinutes { get; set; } = 5;
}
