using System.IO;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class FolderListingService
{
    public static List<FolderFileItem> Read(string path, string order, bool showDirectories, CancellationToken token)
    {
        List<FolderFileItem> items = [];
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (entry is DirectoryInfo directory && showDirectories)
                    items.Add(FolderFileItem.FromDirectoryInfo(directory));
                else if (entry is FileInfo file) items.Add(FolderFileItem.FromFileInfo(file));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        IEnumerable<FolderFileItem> sorted = order switch
        {
            "NameAscending" => items.OrderBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase),
            "NameDescending" => items.OrderByDescending(x => x.FileName, StringComparer.CurrentCultureIgnoreCase),
            "ModifiedAscending" => items.OrderBy(x => x.LastWriteTime),
            "SizeAscending" => items.OrderBy(x => x.SizeInBytes),
            "SizeDescending" => items.OrderByDescending(x => x.SizeInBytes),
            "TypeAscending" => items.OrderBy(x => x.FileType, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.FileName),
            _ => items.OrderByDescending(x => x.LastWriteTime)
        };
        return sorted.OrderByDescending(x => x.IsDirectory).ToList();
    }
}
