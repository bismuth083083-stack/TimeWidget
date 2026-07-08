using System.Globalization;
using System.IO;

namespace TimeWidget.Models;

public sealed class FolderFileItem
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
    public required DateTime LastWriteTime { get; init; }
    public required long SizeInBytes { get; init; }

    public string ModifiedText => LastWriteTime.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture);

    public string SizeText => FormatFileSize(SizeInBytes);

    public static FolderFileItem FromFileInfo(FileInfo file)
    {
        return new FolderFileItem
        {
            FullPath = file.FullName,
            FileName = file.Name,
            FileType = string.IsNullOrWhiteSpace(file.Extension) ? "File" : file.Extension.TrimStart('.').ToUpperInvariant(),
            LastWriteTime = file.LastWriteTime,
            SizeInBytes = file.Length
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size:0.#} {units[unitIndex]}";
    }
}
