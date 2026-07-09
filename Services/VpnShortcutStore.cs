using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class VpnShortcutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string StorePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DesktopMiniWidgets", "vpn_shortcuts.json");
        }
    }

    public static VpnShortcutSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new VpnShortcutSettings();
            }

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<VpnShortcutSettings>(json) ?? new VpnShortcutSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load VPN shortcut settings: {ex}");
            return new VpnShortcutSettings();
        }
    }

    public static void Save(VpnShortcutSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save VPN shortcut settings: {ex}");
        }
    }
}
