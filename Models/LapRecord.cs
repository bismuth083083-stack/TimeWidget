namespace TimeWidget.Models;

public sealed class LapRecord
{
    public int Number { get; set; }
    public TimeSpan Elapsed { get; set; }

    public string DisplayText => $"Lap {Number}: {Elapsed:hh\\:mm\\:ss\\.ff}";
}
