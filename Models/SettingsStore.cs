using System.IO;
using System.Text.Json;

namespace TimeWidget.Models;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TimeWidget", "settings.json");
        }
    }

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new WidgetSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<WidgetSettings>(json) ?? new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public static void Save(WidgetSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public static void Update(Action<WidgetSettings> updateSettings)
    {
        WidgetSettings settings = Load();
        updateSettings(settings);
        Save(settings);
    }
}
