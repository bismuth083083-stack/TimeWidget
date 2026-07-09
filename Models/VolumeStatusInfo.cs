namespace TimeWidget.Models;

public sealed class VolumeStatusInfo
{
    public bool IsSupported { get; init; }
    public int? Value { get; init; }
    public string Message { get; init; } = string.Empty;
}
