using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class WeatherSnapshot
{
    public string CityName { get; init; } = "Beijing";
    public string CityCode { get; init; } = "101010100";
    public DateTimeOffset UpdatedAt { get; init; }
    public List<WeatherDayInfo> Days { get; init; } = [];
    public WeatherAlertInfo Alert { get; init; } = new();
    public bool IsFromCache { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class WeatherService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string CachePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TimeWidget", "weather-cache.json");
        }
    }

    public async Task<WeatherSnapshot> GetWeatherAsync(WeatherCityInfo city, bool forceRefresh)
    {
        WeatherCache? cache = await LoadCacheAsync().ConfigureAwait(false);
        if (!forceRefresh
            && cache is not null
            && cache.Days.Count > 0
            && cache.CityCode == city.Code
            && DateTimeOffset.Now - cache.UpdatedAt < TimeSpan.FromHours(3))
        {
            return FromCache(cache, "Using cached weather.");
        }

        try
        {
            WeatherSnapshot snapshot = await FetchWeatherAsync(city).ConfigureAwait(false);
            await SaveCacheAsync(snapshot).ConfigureAwait(false);
            return snapshot;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather request failed: {ex}");
            return cache is not null
                ? FromCache(cache, "Network failed. Showing cached weather.")
                : CreateErrorSnapshot(city, "Unable to load weather. Check network or choose a city.");
        }
    }

    public async Task<WeatherSnapshot?> TryLoadCacheAsync()
    {
        WeatherCache? cache = await LoadCacheAsync().ConfigureAwait(false);
        return cache is null || cache.Days.Count == 0 ? null : FromCache(cache, "Showing cached weather.");
    }

    private static async Task<WeatherSnapshot> FetchWeatherAsync(WeatherCityInfo city)
    {
        string url = "http://api.open-meteo.com/v1/forecast"
            + $"?latitude={city.Latitude.ToString(CultureInfo.InvariantCulture)}"
            + $"&longitude={city.Longitude.ToString(CultureInfo.InvariantCulture)}"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&hourly=relative_humidity_2m"
            + "&current=relative_humidity_2m"
            + "&timezone=auto"
            + "&forecast_days=3";

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TimeWidget/1.0");

        using HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement daily = root.GetProperty("daily");

        int currentHumidity = TryGetCurrentHumidity(root) ?? 0;
        int[] hourlyHumidity = TryGetHourlyHumidity(root);
        List<WeatherDayInfo> days = CreateDaysFromOpenMeteo(daily, hourlyHumidity, currentHumidity);
        WeatherAlertInfo alert = await TryFetchChinaWeatherAlertAsync(city).ConfigureAwait(false);

        return new WeatherSnapshot
        {
            CityName = city.Name,
            CityCode = city.Code,
            UpdatedAt = DateTimeOffset.Now,
            Days = days,
            Alert = alert,
            StatusMessage = "Weather updated."
        };
    }

    private static List<WeatherDayInfo> CreateDaysFromOpenMeteo(JsonElement daily, int[] hourlyHumidity, int currentHumidity)
    {
        JsonElement times = daily.GetProperty("time");
        JsonElement weatherCodes = daily.GetProperty("weather_code");
        JsonElement highs = daily.GetProperty("temperature_2m_max");
        JsonElement lows = daily.GetProperty("temperature_2m_min");

        List<WeatherDayInfo> days = [];
        int count = Math.Min(3, times.GetArrayLength());

        for (int i = 0; i < count; i++)
        {
            DateTime date = DateTime.Parse(times[i].GetString() ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int code = weatherCodes[i].GetInt32();
            int humidity = GetDailyHumidity(hourlyHumidity, i, currentHumidity);
            WeatherCodeInfo weather = GetWeatherCodeInfo(code);

            days.Add(new WeatherDayInfo
            {
                DateLabel = i switch
                {
                    0 => "Today",
                    1 => "Tomorrow",
                    _ => date.ToString("ddd", CultureInfo.InvariantCulture)
                },
                WeatherText = weather.Text,
                WeatherIcon = weather.Icon,
                HighTemp = (int)Math.Round(highs[i].GetDouble()),
                LowTemp = (int)Math.Round(lows[i].GetDouble()),
                Humidity = humidity
            });
        }

        return days;
    }

    private static int? TryGetCurrentHumidity(JsonElement root)
    {
        if (!root.TryGetProperty("current", out JsonElement current)
            || !current.TryGetProperty("relative_humidity_2m", out JsonElement humidity)
            || humidity.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return humidity.GetInt32();
    }

    private static int[] TryGetHourlyHumidity(JsonElement root)
    {
        if (!root.TryGetProperty("hourly", out JsonElement hourly)
            || !hourly.TryGetProperty("relative_humidity_2m", out JsonElement humidity)
            || humidity.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return humidity.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => value.GetInt32())
            .ToArray();
    }

    private static int GetDailyHumidity(int[] hourlyHumidity, int dayIndex, int fallback)
    {
        int start = dayIndex * 24;
        if (hourlyHumidity.Length <= start)
        {
            return fallback;
        }

        int count = Math.Min(24, hourlyHumidity.Length - start);
        return (int)Math.Round(hourlyHumidity.Skip(start).Take(count).DefaultIfEmpty(fallback).Average());
    }

    private static WeatherCodeInfo GetWeatherCodeInfo(int code)
    {
        return code switch
        {
            0 => new WeatherCodeInfo("Sunny", "\u2600"),
            1 or 2 => new WeatherCodeInfo("Partly Cloudy", "\u26C5"),
            3 => new WeatherCodeInfo("Cloudy", "\u2601"),
            45 or 48 => new WeatherCodeInfo("Fog", "\U0001F32B"),
            51 or 53 or 55 or 56 or 57 => new WeatherCodeInfo("Drizzle", "\U0001F327"),
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => new WeatherCodeInfo("Rain", "\U0001F327"),
            71 or 73 or 75 or 77 or 85 or 86 => new WeatherCodeInfo("Snow", "\u2744"),
            95 or 96 or 99 => new WeatherCodeInfo("Thunderstorm", "\u26C8"),
            _ => new WeatherCodeInfo("Cloudy", "\u2601")
        };
    }

    private static async Task<WeatherAlertInfo> TryFetchChinaWeatherAlertAsync(WeatherCityInfo city)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"http://d1.weather.com.cn/weather_index/{city.Code}.html");
            request.Headers.Referrer = new Uri($"http://www.weather.com.cn/weather1d/{city.Code}.shtml");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 TimeWidget/1.0");

            using HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseAlert(payload, city);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"China Weather alert request failed: {ex}");
            return CreateNoAlert(city);
        }
    }

    private static WeatherAlertInfo ParseAlert(string payload, WeatherCityInfo city)
    {
        try
        {
            Match alarmMatch = Regex.Match(payload, @"var\s+alarmDZ\d+\s*=\s*(\{.*?\});", RegexOptions.Singleline);
            if (!alarmMatch.Success)
            {
                return CreateNoAlert(city);
            }

            using JsonDocument document = JsonDocument.Parse(alarmMatch.Groups[1].Value);
            JsonElement root = document.RootElement;
            JsonElement alertElement = root.TryGetProperty("w", out JsonElement warnings) && warnings.ValueKind == JsonValueKind.Array
                ? warnings.EnumerateArray().FirstOrDefault()
                : root;

            if (alertElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return CreateNoAlert(city);
            }

            string title = GetOptionalString(alertElement, "w2")
                ?? GetOptionalString(alertElement, "title")
                ?? "Weather Alert";
            string description = GetOptionalString(alertElement, "w9")
                ?? GetOptionalString(alertElement, "description")
                ?? "Please check China Weather for details.";
            AlertLevel level = ParseAlertLevel(title + description);

            return new WeatherAlertInfo
            {
                Level = level,
                Title = title,
                Description = description,
                LinkText = "China Weather",
                LinkUrl = $"http://www.weather.com.cn/weather1d/{city.Code}.shtml"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather alert parse failed: {ex}");
            return CreateNoAlert(city);
        }
    }

    private static WeatherAlertInfo CreateNoAlert(WeatherCityInfo city)
    {
        return new WeatherAlertInfo
        {
            Level = AlertLevel.None,
            Title = "No weather alert",
            Description = "No active weather warning.",
            LinkText = "China Weather",
            LinkUrl = $"http://www.weather.com.cn/weather1d/{city.Code}.shtml"
        };
    }

    private static JsonElement ExtractJsonObject(string payload, string variablePrefix)
    {
        Match match = Regex.Match(payload, $@"var\s+{Regex.Escape(variablePrefix)}\d+\s*=\s*(\{{.*?\}});", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidDataException($"Missing {variablePrefix} weather payload.");
        }

        using JsonDocument document = JsonDocument.Parse(match.Groups[1].Value);
        return document.RootElement.Clone();
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static (int High, int Low) ParseTemperatureRange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        int[] values = Regex.Matches(text, @"-?\d+")
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();

        if (values.Length == 0)
        {
            return (0, 0);
        }

        if (values.Length == 1)
        {
            return (values[0], values[0]);
        }

        return (values.Max(), values.Min());
    }

    private static int? TryParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = Regex.Match(text, @"-?\d+");
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    private static string GetWeatherIcon(string weatherText)
    {
        if (weatherText.Contains("\u96ea", StringComparison.Ordinal))
        {
            return "\u2744";
        }

        if (weatherText.Contains("\u96e8", StringComparison.Ordinal))
        {
            return "\U0001F327";
        }

        if (weatherText.Contains("\u96f7", StringComparison.Ordinal))
        {
            return "\u26C8";
        }

        if (weatherText.Contains("\u9634", StringComparison.Ordinal)
            || weatherText.Contains("Cloud", StringComparison.OrdinalIgnoreCase))
        {
            return "\u2601";
        }

        if (weatherText.Contains("\u4e91", StringComparison.Ordinal))
        {
            return "\u26C5";
        }

        return "\u2600";
    }

    private readonly record struct WeatherCodeInfo(string Text, string Icon);

    private static AlertLevel ParseAlertLevel(string text)
    {
        if (text.Contains("\u7ea2", StringComparison.Ordinal)
            || text.Contains("Red", StringComparison.OrdinalIgnoreCase))
        {
            return AlertLevel.Red;
        }

        if (text.Contains("\u6a59", StringComparison.Ordinal)
            || text.Contains("Orange", StringComparison.OrdinalIgnoreCase))
        {
            return AlertLevel.Orange;
        }

        if (text.Contains("\u9ec4", StringComparison.Ordinal)
            || text.Contains("Yellow", StringComparison.OrdinalIgnoreCase))
        {
            return AlertLevel.Yellow;
        }

        if (text.Contains("\u84dd", StringComparison.Ordinal)
            || text.Contains("Blue", StringComparison.OrdinalIgnoreCase))
        {
            return AlertLevel.Blue;
        }

        return AlertLevel.None;
    }

    private static async Task<WeatherCache?> LoadCacheAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(CachePath))
                {
                    return null;
                }

                string json = File.ReadAllText(CachePath);
                return JsonSerializer.Deserialize<WeatherCache>(json);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather cache load failed: {ex}");
            return null;
        }
    }

    private static async Task SaveCacheAsync(WeatherSnapshot snapshot)
    {
        try
        {
            await Task.Run(() =>
            {
                string? directory = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                WeatherCache cache = new()
                {
                    UpdatedAt = snapshot.UpdatedAt,
                    CityName = snapshot.CityName,
                    CityCode = snapshot.CityCode,
                    Days = snapshot.Days,
                    Alert = snapshot.Alert
                };

                File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, JsonOptions));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather cache save failed: {ex}");
        }
    }

    private static WeatherSnapshot FromCache(WeatherCache cache, string message)
    {
        return new WeatherSnapshot
        {
            CityName = cache.CityName,
            CityCode = cache.CityCode,
            UpdatedAt = cache.UpdatedAt,
            Days = cache.Days,
            Alert = cache.Alert,
            IsFromCache = true,
            StatusMessage = message
        };
    }

    private static WeatherSnapshot CreateErrorSnapshot(WeatherCityInfo city, string message)
    {
        return new WeatherSnapshot
        {
            CityName = city.Name,
            CityCode = city.Code,
            UpdatedAt = DateTimeOffset.Now,
            Days = [],
            Alert = new WeatherAlertInfo
            {
                Level = AlertLevel.None,
                Title = "Weather unavailable",
                Description = message,
                LinkText = "China Weather",
                LinkUrl = $"http://www.weather.com.cn/weather1d/{city.Code}.shtml"
            },
            StatusMessage = message
        };
    }
}
