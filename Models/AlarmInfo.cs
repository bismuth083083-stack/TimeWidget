namespace TimeWidget.Models;

public sealed class AlarmInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TimeSpan Time { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public List<DayOfWeek> RepeatDays { get; set; } = [];
    public int SnoozeMinutes { get; set; } = 5;
    public DateTime? LastTriggeredDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsTemporarySnooze { get; set; }
}
