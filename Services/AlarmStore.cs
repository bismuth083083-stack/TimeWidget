using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class AlarmStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string StorePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DesktopMiniWidgets", "alarms.json");
        }
    }

    public static AlarmStoreData Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new AlarmStoreData();
            }

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<AlarmStoreData>(json) ?? new AlarmStoreData();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Alarm store load failed: {ex}");
            BackupCorruptStore();
            return new AlarmStoreData();
        }
    }

    public static void Save(AlarmStoreData data)
    {
        try
        {
            string? directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Alarm store save failed: {ex}");
        }
    }

    private static void BackupCorruptStore()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(StorePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(directory, $"alarms_corrupt_{timestamp}.json");
            File.Move(StorePath, backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Alarm corrupt store backup failed: {ex}");
        }
    }
}
