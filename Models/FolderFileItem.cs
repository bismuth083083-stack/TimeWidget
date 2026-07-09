using System.Globalization;
using System.IO;

namespace TimeWidget.Models;

public sealed class FolderFileItem
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
    public required string IconGlyph { get; init; }
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
            IconGlyph = GetIconGlyph(file.Extension),
            LastWriteTime = file.LastWriteTime,
            SizeInBytes = file.Length
        };
    }

    private static string GetIconGlyph(string extension)
    {
        string normalized = extension.TrimStart('.').ToLowerInvariant();

        return normalized switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "heic" or "svg" => "\uEB9F",
            "mp3" or "wav" or "flac" or "aac" or "m4a" or "ogg" => "\uE8D6",
            "mp4" or "mov" or "mkv" or "avi" or "webm" or "wmv" => "\uE714",
            "zip" or "rar" or "7z" or "tar" or "gz" => "\uE8B1",
            "exe" or "msi" or "bat" or "cmd" or "ps1" => "\uE756",
            "cs" or "xaml" or "js" or "ts" or "html" or "css" or "json" or "xml" or "py" => "\uE943",
            _ => "\uE8A5"
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
