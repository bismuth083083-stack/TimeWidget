using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class CourseScheduleStore
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
            return Path.Combine(appData, "DesktopMiniWidgets", "courses.json");
        }
    }

    public static List<CourseScheduleInfo> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<CourseScheduleInfo>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load course schedule: {ex}");
            BackupCorruptFile();
            return [];
        }
    }

    public static void Save(IEnumerable<CourseScheduleInfo> courses)
    {
        try
        {
            string? directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(courses.OrderBy(item => item.DayOfWeek).ThenBy(item => item.StartSection), JsonOptions);
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save course schedule: {ex}");
        }
    }

    private static void BackupCorruptFile()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(StorePath) ?? string.Empty;
            string backupName = $"courses_corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            File.Move(StorePath, Path.Combine(directory, backupName), true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to backup corrupt course schedule file: {ex}");
        }
    }
}
