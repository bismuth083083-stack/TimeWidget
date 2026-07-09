namespace TimeWidget.Models;

public sealed class BluetoothStatusInfo
{
    public bool IsAvailable { get; init; }
    public bool IsEnabled { get; init; }
    public string DisplayName { get; init; } = "Bluetooth";
}
