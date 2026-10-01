namespace TimeWidget.Models;

public sealed class NetworkTrafficSnapshot
{
    public bool IsAvailable { get; init; }
    public string AdapterName { get; init; } = string.Empty;
    public double DownloadBytesPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }
    public long SessionReceivedBytes { get; init; }
    public long SessionSentBytes { get; init; }
}
