using Microsoft.Win32;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class ProxyStatusService
{
    public static ProxyStatusInfo GetStatus()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            bool enabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1;
            string server = key?.GetValue("ProxyServer")?.ToString() ?? string.Empty;
            (string http, string socks) = ParseProxyServer(server);

            return new ProxyStatusInfo
            {
                IsEnabled = enabled,
                HttpProxy = string.IsNullOrWhiteSpace(http) ? "Not set" : http,
                SocksProxy = string.IsNullOrWhiteSpace(socks) ? "Not set" : socks
            };
        }
        catch
        {
            return new ProxyStatusInfo
            {
                IsEnabled = false,
                HttpProxy = "Unavailable",
                SocksProxy = "Unavailable"
            };
        }
    }

    private static (string Http, string Socks) ParseProxyServer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, string.Empty);
        }

        if (!value.Contains('='))
        {
            return (value, string.Empty);
        }

        string http = string.Empty;
        string socks = string.Empty;
        string[] parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            string[] pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0].Equals("http", StringComparison.OrdinalIgnoreCase)
                || pair[0].Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                http = pair[1];
            }
            else if (pair[0].Equals("socks", StringComparison.OrdinalIgnoreCase)
                     || pair[0].Equals("socks5", StringComparison.OrdinalIgnoreCase))
            {
                socks = pair[1];
            }
        }

        return (http, socks);
    }
}
