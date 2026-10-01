using System.IO;
using System.Text;
using System.Text.Json;
using TimeWidget.Models;

namespace TimeWidget.Services;

public static class FinanceDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string DataDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DesktopMiniWidgets");
        }
    }

    public static string TransactionsPath => Path.Combine(DataDirectory, "transactions.json");
    public static string PurchasePlansPath => Path.Combine(DataDirectory, "purchase_plans.json");

    public static List<TransactionRecord> LoadTransactions()
    {
        return LoadList<TransactionRecord>(TransactionsPath, "transactions");
    }

    public static void SaveTransactions(IEnumerable<TransactionRecord> records)
    {
        SaveList(TransactionsPath, records.OrderByDescending(record => record.Date).ThenByDescending(record => record.CreatedAt));
    }

    public static List<PurchasePlanItem> LoadPurchasePlans()
    {
        return LoadList<PurchasePlanItem>(PurchasePlansPath, "purchase_plans");
    }

    public static void SavePurchasePlans(IEnumerable<PurchasePlanItem> items)
    {
        SaveList(PurchasePlansPath, items);
    }

    public static void ExportTransactionsCsv(string path, IEnumerable<TransactionRecord> records)
    {
        StringBuilder builder = new();
        builder.AppendLine("Id,Type,Amount,Category,Date,Note,CreatedAt,UpdatedAt");
        foreach (TransactionRecord record in records)
        {
            builder.AppendLine(string.Join(",", [
                Csv(record.Id.ToString()),
                Csv(record.Type.ToString()),
                Csv(record.Amount.ToString("0.00")),
                Csv(record.Category),
                Csv(record.Date.ToString("yyyy-MM-dd")),
                Csv(record.Note),
                Csv(record.CreatedAt.ToString("O")),
                Csv(record.UpdatedAt.ToString("O"))
            ]));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void ExportPurchasePlansCsv(string path, IEnumerable<PurchasePlanItem> items)
    {
        StringBuilder builder = new();
        builder.AppendLine("Id,Name,EstimatedPrice,Priority,TargetDate,Category,Note,IsPurchased,PurchasedAt,ActualPrice,CreatedAt,UpdatedAt");
        foreach (PurchasePlanItem item in items)
        {
            builder.AppendLine(string.Join(",", [
                Csv(item.Id.ToString()),
                Csv(item.Name),
                Csv(item.EstimatedPrice.ToString("0.00")),
                Csv(item.Priority.ToString()),
                Csv(item.TargetDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                Csv(item.Category),
                Csv(item.Note),
                Csv(item.IsPurchased.ToString()),
                Csv(item.PurchasedAt?.ToString("yyyy-MM-dd") ?? string.Empty),
                Csv(item.ActualPrice?.ToString("0.00") ?? string.Empty),
                Csv(item.CreatedAt.ToString("O")),
                Csv(item.UpdatedAt.ToString("O"))
            ]));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static List<T> LoadList<T>(string path, string backupPrefix)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!File.Exists(path))
            {
                return [];
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
        }
        catch
        {
            BackupCorruptFile(path, backupPrefix);
            return [];
        }
    }

    private static void SaveList<T>(string path, IEnumerable<T> items)
    {
        Directory.CreateDirectory(DataDirectory);
        string tempPath = $"{path}.tmp";
        string json = JsonSerializer.Serialize(items, JsonOptions);
        File.WriteAllText(tempPath, json);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }

    private static void BackupCorruptFile(string path, string prefix)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            string backupPath = Path.Combine(DataDirectory, $"{prefix}_corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Move(path, backupPath, overwrite: true);
        }
        catch
        {
            // The widget can continue with an empty list if backup fails.
        }
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
