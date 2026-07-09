namespace TimeWidget.Models;

public sealed class BrightnessStatusInfo
{
    public bool IsSupported { get; init; }
    public int? Value { get; init; }
    public string Message { get; init; } = string.Empty;
}
