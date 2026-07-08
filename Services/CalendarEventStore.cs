using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class CalendarEventStore
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
            return Path.Combine(appData, "DesktopMiniWidgets", "calendar_events.json");
        }
    }

    public static List<CalendarEventInfo> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<CalendarEventInfo>>(json)?
                .Select(Normalize)
                .OrderBy(item => item.Date.Date)
                .ThenBy(item => item.TimeText)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Calendar event load failed: {ex}");
            BackupCorruptStore();
            return [];
        }
    }

    public static void Save(IEnumerable<CalendarEventInfo> events)
    {
        try
        {
            string? directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<CalendarEventInfo> ordered = events
                .Select(Normalize)
                .OrderBy(item => item.Date.Date)
                .ThenBy(item => item.TimeText)
                .ToList();

            File.WriteAllText(StorePath, JsonSerializer.Serialize(ordered, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Calendar event save failed: {ex}");
        }
    }

    private static CalendarEventInfo Normalize(CalendarEventInfo eventInfo)
    {
        eventInfo.Date = eventInfo.Date.Date;
        eventInfo.Title ??= string.Empty;
        eventInfo.TimeText ??= string.Empty;
        eventInfo.Description ??= string.Empty;
        return eventInfo;
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

            Directory.CreateDirectory(directory);
            string backupPath = Path.Combine(directory, $"calendar_events_corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Move(StorePath, backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Calendar corrupt store backup failed: {ex}");
        }
    }
}
