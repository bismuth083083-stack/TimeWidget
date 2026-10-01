using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class WeatherAlertResult
{
    public string CityCode { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<WeatherAlertInfo> Alerts { get; set; } = [];
    public bool IsCurrent { get; set; }
    public string? StatusMessage { get; set; }
}

public sealed class ChinaWeatherAlertService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _client;
    private readonly string _cacheDirectory;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChinaWeatherAlertService() : this(SharedClient,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeWidget", "weather-alerts"),
        () => DateTimeOffset.UtcNow)
    {
    }

    internal ChinaWeatherAlertService(HttpClient client, string cacheDirectory, Func<DateTimeOffset> now)
    {
        _client = client;
        _cacheDirectory = cacheDirectory;
        _now = now;
    }

    public async Task<WeatherAlertResult> GetAlertsAsync(WeatherCityInfo city, bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string cachePath = Path.Combine(_cacheDirectory, $"{city.Code}.json");
            WeatherAlertResult? cached = await LoadCacheAsync(cachePath, city.Code, cancellationToken).ConfigureAwait(false);
            TimeSpan age = cached?.UpdatedAt is { } updated ? _now() - updated : TimeSpan.MaxValue;
            if (!forceRefresh && cached is not null && age >= TimeSpan.Zero && age < RefreshInterval)
            {
                cached.IsCurrent = true;
                return cached;
            }

            // These are the lightweight list and map endpoints used by China's weather alert page.
            foreach (string endpoint in new[]
            {
                "https://product.weather.com.cn/alarm/grepalarm_cn.php",
                "https://forecast.weather.com.cn/api/v1/traffic/alarm/alarmMap"
            })
            {
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
                    request.Headers.Referrer = new Uri("https://www.weather.com.cn/alarm/");
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 TimeWidget/1.0");
                    request.Headers.CacheControl = new() { NoCache = true };
                    using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    List<WeatherAlertInfo> alerts = ParseAlerts(payload, city);
                    WeatherAlertResult result = new()
                    {
                        CityCode = city.Code, UpdatedAt = _now(), Alerts = alerts, IsCurrent = true
                    };
                    await SaveCacheAsync(cachePath, result, cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { Debug.WriteLine($"Weather alerts ({city.Code}) failed at {endpoint}: {ex.Message}"); }
            }

            if (cached is not null && age >= TimeSpan.Zero && age < TimeSpan.FromHours(6))
            {
                cached.IsCurrent = false;
                cached.StatusMessage = $"Alert update failed. Last checked {cached.UpdatedAt:MM/dd HH:mm} UTC.";
                return cached;
            }

            return new WeatherAlertResult { CityCode = city.Code, StatusMessage = "Weather alerts unavailable. Please check China Weather." };
        }
        finally { _gate.Release(); }
    }

    internal static List<WeatherAlertInfo> ParseAlerts(string payload, WeatherCityInfo city)
    {
        string json = payload.Trim().TrimStart('\uFEFF');
        if (json.StartsWith("var", StringComparison.Ordinal))
        {
            Match assignment = Regex.Match(json, @"^var\s+alarminfo\s*=\s*", RegexOptions.CultureInvariant);
            if (!assignment.Success) throw new InvalidDataException("Unexpected alert response.");
            json = json[assignment.Length..].TrimEnd().TrimEnd(';');
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("status", out JsonElement status))
        {
            if (status.GetString() != "success") throw new InvalidDataException("Alert API reported failure.");
            root = root.GetProperty("result");
        }
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("count", out JsonElement count)
            || !int.TryParse(count.ToString(), out int expectedCount) || expectedCount != data.GetArrayLength())
            throw new InvalidDataException("Incomplete alert list; cannot confirm that there are no warnings.");

        List<WeatherAlertInfo> alerts = [];
        foreach (JsonElement row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 7
                || row[0].ValueKind != JsonValueKind.String || row[1].ValueKind != JsonValueKind.String
                || row[6].ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Unrecognized alert row.");

            string file = row[1].GetString()!;
            Match id = Regex.Match(file, @"^(?<area>101\d{2,6})-(?<time>\d{14})-(?<type>\d{2})(?<level>\d{2})\.html$");
            if (!id.Success) throw new InvalidDataException("Unrecognized alert identifier.");
            string areaCode = id.Groups["area"].Value;
            if (!MatchesCity(areaCode, city.Code)) continue;

            string location = row[0].GetString()!;
            string title = row[6].GetString()!;
            // A cancellation notice is not an active warning.
            if (title.Contains("\u89e3\u9664", StringComparison.Ordinal)
                || title.Contains("\u64a4\u9500", StringComparison.Ordinal)) continue;
            Match type = Regex.Match(title, @"\u53d1\u5e03(?<type>.+?)(?:\u84dd\u8272|\u9ec4\u8272|\u6a59\u8272|\u7ea2\u8272)?\u9884\u8b66");
            string weatherType = type.Success ? type.Groups["type"].Value : "Weather alert";
            DateTimeOffset? published = DateTime.TryParseExact(id.Groups["time"].Value, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? new DateTimeOffset(date, TimeSpan.FromHours(8)) : null;
            string cityName = GetCityName(location, city.Name);
            alerts.Add(new WeatherAlertInfo
            {
                Level = id.Groups["level"].Value switch
                {
                    "01" => AlertLevel.Blue, "02" => AlertLevel.Yellow,
                    "03" => AlertLevel.Orange, "04" => AlertLevel.Red, _ => AlertLevel.None
                },
                WeatherType = weatherType, LocationName = cityName, PublishedAt = published,
                Title = title,
                Description = $"{title}\n{location}\n{published:yyyy/MM/dd HH:mm} (UTC+8)",
                LinkText = "China Weather",
                LinkUrl = $"https://www.weather.com.cn/alarm/newalarmcontent.shtml?file={Uri.EscapeDataString(file)}"
            });
        }

        // Keep distinct districts and warning types; identical list rows alone are duplicates.
        return alerts.DistinctBy(alert => alert.LinkUrl)
            .OrderByDescending(alert => alert.Level).ThenByDescending(alert => alert.PublishedAt).ToList();
    }

    internal static bool MatchesCity(string areaCode, string cityCode)
    {
        if (cityCode.Length != 9) return false;
        string province = cityCode[..5];
        // Municipality districts use multiple seven-digit groups.
        if (province is "10101" or "10102" or "10103" or "10104")
            return areaCode.StartsWith(province, StringComparison.Ordinal);
        return areaCode == province || areaCode.StartsWith(cityCode[..7], StringComparison.Ordinal);
    }

    private static string GetCityName(string location, string fallback)
    {
        string local = Regex.Replace(location, @"^.*?(?:\u7701|\u81ea\u6cbb\u533a|\u7279\u522b\u884c\u653f\u533a)", "");
        Match match = Regex.Match(local, @"^.+?(?:\u81ea\u6cbb\u5dde|\u5730\u533a|\u76df|\u5e02)");
        return match.Success ? match.Value : fallback;
    }

    private static async Task<WeatherAlertResult?> LoadCacheAsync(string path, string code, CancellationToken token)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
            WeatherAlertResult? cache = JsonSerializer.Deserialize<WeatherAlertResult>(json);
            return cache?.CityCode == code && cache.Alerts is not null && cache.UpdatedAt is not null ? cache : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static async Task SaveCacheAsync(string path, WeatherAlertResult result, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result), token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { Debug.WriteLine($"Alert cache save failed: {ex.Message}"); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) { Debug.WriteLine($"Alert cache cleanup failed: {ex.Message}"); }
        }
    }
}
