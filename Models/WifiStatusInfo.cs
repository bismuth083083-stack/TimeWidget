namespace TimeWidget.Models;

public sealed class WifiStatusInfo
{
    public bool IsAvailable { get; init; }
    public bool IsConnected { get; init; }
    public string DisplayName { get; init; } = "Wi-Fi";
}
