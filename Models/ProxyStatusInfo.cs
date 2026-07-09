namespace TimeWidget.Models;

public sealed class ProxyStatusInfo
{
    public bool IsEnabled { get; init; }
    public string HttpProxy { get; init; } = "Not set";
    public string SocksProxy { get; init; } = "Not set";
}
