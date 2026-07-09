using System.Diagnostics;
using Microsoft.Win32;

namespace TimeWidget.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DesktopMiniWidgets";

    public static void EnableCurrentUserStartup()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enable startup: {ex}");
        }
    }
}
