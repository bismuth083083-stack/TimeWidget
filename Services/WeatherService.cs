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
    public string DataSource { get; init; } = WeatherService.DataSourceName;
    public string CityName { get; init; } = "Beijing";
    public string CityCode { get; init; } = "101010100";
    public DateTimeOffset UpdatedAt { get; init; }
    public List<WeatherDayInfo> Days { get; init; } = [];
    public List<WeatherAlertInfo> Alerts { get; init; } = [];
    public WeatherAlertInfo Alert { get; init; } = new();
    public bool IsFromCache { get; init; }
    public string? StatusMessage { get; init; }
    public bool AlertsAreCurrent { get; init; }
    public DateTimeOffset? AlertsUpdatedAt { get; init; }
    public string? AlertStatusMessage { get; init; }
}

public sealed class WeatherService
{
    public const string DataSourceName = "China Weather";
    private const int CacheSchemaVersion = 3;
    private readonly ChinaWeatherAlertService _alertService = new();

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

    public async Task<WeatherSnapshot> GetWeatherAsync(WeatherCityInfo city, bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        Task<WeatherSnapshot> forecastTask = GetForecastAsync(city, forceRefresh, cancellationToken);
        Task<WeatherAlertResult> alertTask = _alertService.GetAlertsAsync(city, forceRefresh, cancellationToken);
        await Task.WhenAll(forecastTask, alertTask).ConfigureAwait(false);
        WeatherSnapshot forecast = await forecastTask.ConfigureAwait(false);
        WeatherAlertResult alerts = await alertTask.ConfigureAwait(false);
        return new WeatherSnapshot
        {
            DataSource = forecast.DataSource, CityName = city.Name, CityCode = city.Code,
            UpdatedAt = forecast.UpdatedAt, Days = forecast.Days,
            IsFromCache = forecast.IsFromCache, StatusMessage = forecast.StatusMessage,
            Alerts = alerts.Alerts, AlertsAreCurrent = alerts.IsCurrent,
            AlertsUpdatedAt = alerts.UpdatedAt, AlertStatusMessage = alerts.StatusMessage,
            Alert = alerts.Alerts.FirstOrDefault() ?? new WeatherAlertInfo
            {
                WeatherType = alerts.IsCurrent ? "No active alerts" : "Alerts unavailable",
                Title = alerts.IsCurrent ? "No active alerts" : "Alerts unavailable",
                LocationName = city.Name,
                Description = alerts.StatusMessage ?? "No active warning in the China Weather alert list.",
                LinkUrl = "https://www.weather.com.cn/alarm/"
            }
        };
    }

    private async Task<WeatherSnapshot> GetForecastAsync(WeatherCityInfo city, bool forceRefresh, CancellationToken cancellationToken)
    {
        WeatherCache? cache = await LoadCacheAsync().ConfigureAwait(false);
        if (!forceRefresh
            && cache is not null
            && cache.SchemaVersion == CacheSchemaVersion
            && cache.DataSource == DataSourceName
            && cache.Days.Count > 0
            && cache.CityCode == city.Code
            && DateTimeOffset.Now - cache.UpdatedAt < TimeSpan.FromHours(3))
        {
            return FromCache(cache, "Using cached weather.");
        }

        try
        {
            WeatherSnapshot snapshot = await FetchWeatherAsync(city, cancellationToken).ConfigureAwait(false);
            await SaveCacheAsync(snapshot).ConfigureAwait(false);
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"Weather request failed: {ex}");
            return cache is not null
                && cache.SchemaVersion == CacheSchemaVersion
                && cache.DataSource == DataSourceName
                && cache.CityCode == city.Code
                ? FromCache(cache, "Network failed. Showing cached weather.")
                : CreateErrorSnapshot(city, "Unable to load weather. Check network or choose a city.");
        }
    }

    public async Task<WeatherSnapshot?> TryLoadCacheAsync()
    {
        WeatherCache? cache = await LoadCacheAsync().ConfigureAwait(false);
        return cache is null
            || cache.SchemaVersion != CacheSchemaVersion
            || cache.DataSource != DataSourceName
            || cache.Days.Count == 0
            ? null
            : FromCache(cache, "Showing cached weather.");
    }

    private static async Task<WeatherSnapshot> FetchWeatherAsync(WeatherCityInfo city, CancellationToken cancellationToken)
    {
        string payload = await FetchChinaWeatherPayloadAsync(city, cancellationToken).ConfigureAwait(false);
        JsonElement forecast = ExtractJsonObject(payload, "fc");
        JsonElement current = ExtractJsonObject(payload, "dataSK");
        List<WeatherDayInfo> days = CreateDaysFromChinaWeather(forecast, current);

        if (days.Count == 0)
        {
            throw new InvalidDataException("China Weather returned no forecast days.");
        }

        return new WeatherSnapshot
        {
            DataSource = DataSourceName,
            CityName = city.Name,
            CityCode = city.Code,
            UpdatedAt = DateTimeOffset.Now,
            Days = days,
            StatusMessage = "Updated from China Weather."
        };
    }

    private static async Task<string> FetchChinaWeatherPayloadAsync(WeatherCityInfo city, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"https://d1.weather.com.cn/weather_index/{city.Code}.html");
        request.Headers.Referrer = new Uri($"http://www.weather.com.cn/weather1d/{city.Code}.shtml");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 TimeWidget/1.0");

        using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<WeatherDayInfo> CreateDaysFromChinaWeather(JsonElement forecast, JsonElement current)
    {
        if (!forecast.TryGetProperty("f", out JsonElement forecastDays)
            || forecastDays.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<WeatherDayInfo> days = [];
        int currentHumidity = ParsePercentage(GetOptionalString(current, "SD")) ?? 0;
        int count = Math.Min(3, forecastDays.GetArrayLength());

        for (int i = 0; i < count; i++)
        {
            JsonElement item = forecastDays[i];
            WeatherCodeInfo? daytime = GetChinaWeatherCodeInfo(GetOptionalString(item, "fa"));
            WeatherCodeInfo? nighttime = GetChinaWeatherCodeInfo(GetOptionalString(item, "fb"));
            WeatherCodeInfo primary = daytime ?? nighttime ?? new WeatherCodeInfo("Cloudy", "\u2601\uFE0F", 0);
            WeatherCodeInfo iconSource = daytime is not null && nighttime is not null
                ? (daytime.Value.Severity >= nighttime.Value.Severity ? daytime.Value : nighttime.Value)
                : primary;
            string weatherText = daytime is not null
                && nighttime is not null
                && !string.Equals(daytime.Value.Text, nighttime.Value.Text, StringComparison.Ordinal)
                    ? $"{daytime.Value.Text} / {nighttime.Value.Text}"
                    : primary.Text;

            int high = TryParseInt(GetOptionalString(item, "fc"))
                ?? TryParseInt(GetOptionalString(item, "fd"))
                ?? 0;
            int low = TryParseInt(GetOptionalString(item, "fd")) ?? high;
            int humidity = GetForecastHumidity(item, currentHumidity);

            days.Add(new WeatherDayInfo
            {
                DateLabel = i switch
                {
                    0 => "Today",
                    1 => "Tomorrow",
                    _ => DateTime.Today.AddDays(i).ToString("ddd", CultureInfo.InvariantCulture)
                },
                WeatherText = weatherText,
                WeatherIcon = iconSource.Icon,
                HighTemp = high,
                LowTemp = low,
                Humidity = humidity
            });
        }

        return days;
    }

    private static int GetForecastHumidity(JsonElement item, int fallback)
    {
        int? maximum = ParsePercentage(GetOptionalString(item, "fm"));
        int? minimum = ParsePercentage(GetOptionalString(item, "fn"));
        if (maximum is not null && minimum is not null)
        {
            return (int)Math.Round((maximum.Value + minimum.Value) / 2d);
        }

        return maximum ?? minimum ?? fallback;
    }

    private static int? ParsePercentage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized = text.Trim().TrimEnd('%');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp((int)Math.Round(value), 0, 100)
            : null;
    }

    private static WeatherCodeInfo? GetChinaWeatherCodeInfo(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return code switch
        {
            "00" => new WeatherCodeInfo("Sunny", "\u2600\uFE0F", 0),
            "01" => new WeatherCodeInfo("Cloudy", "\u26C5", 1),
            "02" => new WeatherCodeInfo("Overcast", "\u2601\uFE0F", 2),
            "03" => new WeatherCodeInfo("Shower", "\U0001F326\uFE0F", 4),
            "04" or "05" => new WeatherCodeInfo("Thunderstorm", "\u26C8\uFE0F", 7),
            "06" or "19" => new WeatherCodeInfo("Sleet", "\U0001F328\uFE0F", 5),
            "07" or "21" => new WeatherCodeInfo("Light Rain", "\U0001F327\uFE0F", 4),
            "08" or "22" => new WeatherCodeInfo("Moderate Rain", "\U0001F327\uFE0F", 5),
            "09" or "23" => new WeatherCodeInfo("Heavy Rain", "\U0001F327\uFE0F", 6),
            "10" or "24" => new WeatherCodeInfo("Rainstorm", "\U0001F327\uFE0F", 7),
            "11" or "25" => new WeatherCodeInfo("Heavy Rainstorm", "\U0001F327\uFE0F", 8),
            "12" => new WeatherCodeInfo("Severe Rainstorm", "\U0001F327\uFE0F", 9),
            "13" => new WeatherCodeInfo("Snow Flurry", "\U0001F328\uFE0F", 4),
            "14" or "26" => new WeatherCodeInfo("Light Snow", "\u2744\uFE0F", 4),
            "15" or "27" => new WeatherCodeInfo("Moderate Snow", "\u2744\uFE0F", 5),
            "16" or "28" => new WeatherCodeInfo("Heavy Snow", "\u2744\uFE0F", 6),
            "17" => new WeatherCodeInfo("Snowstorm", "\u2744\uFE0F", 8),
            "18" => new WeatherCodeInfo("Fog", "\U0001F32B\uFE0F", 3),
            "20" or "29" or "30" or "31" => new WeatherCodeInfo("Dust", "\U0001F32B\uFE0F", 5),
            "49" or "53" or "54" or "55" or "56" or "57" or "58" => new WeatherCodeInfo("Haze", "\U0001F32B\uFE0F", 3),
            "301" => new WeatherCodeInfo("Rain", "\U0001F327\uFE0F", 4),
            "302" => new WeatherCodeInfo("Snow", "\u2744\uFE0F", 4),
            _ => new WeatherCodeInfo("Cloudy", "\u2601\uFE0F", 0)
        };
    }


    private static JsonElement ExtractJsonObject(string payload, string variablePrefix)
    {
        Match match = Regex.Match(
            payload,
            $@"var\s+{Regex.Escape(variablePrefix)}\d*\s*=\s*(\{{.*?\}})(?=\s*(?:;|var\s+|$))",
            RegexOptions.Singleline);
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

    private static int? TryParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = Regex.Match(text, @"-?\d+");
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    private readonly record struct WeatherCodeInfo(string Text, string Icon, int Severity);


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
                    SchemaVersion = CacheSchemaVersion,
                    DataSource = DataSourceName,
                    UpdatedAt = snapshot.UpdatedAt,
                    CityName = snapshot.CityName,
                    CityCode = snapshot.CityCode,
                    Days = snapshot.Days,
                    Alerts = snapshot.Alerts,
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
        List<WeatherAlertInfo> alerts = cache.Alerts.Count > 0
            ? cache.Alerts
            : cache.Alert.Level != AlertLevel.None
                ? [cache.Alert]
                : [];

        return new WeatherSnapshot
        {
            DataSource = cache.DataSource,
            CityName = cache.CityName,
            CityCode = cache.CityCode,
            UpdatedAt = cache.UpdatedAt,
            Days = cache.Days,
            Alerts = alerts,
            Alert = alerts.FirstOrDefault() ?? cache.Alert,
            IsFromCache = true,
            StatusMessage = message
        };
    }

    private static WeatherSnapshot CreateErrorSnapshot(WeatherCityInfo city, string message)
    {
        return new WeatherSnapshot
        {
            DataSource = DataSourceName,
            CityName = city.Name,
            CityCode = city.Code,
            UpdatedAt = DateTimeOffset.Now,
            Days = [],
            Alerts = [],
            Alert = new WeatherAlertInfo
            {
                Level = AlertLevel.None,
                WeatherType = "Weather unavailable",
                LocationName = city.Name,
                Title = "Weather unavailable",
                Description = message,
                LinkText = "China Weather",
                LinkUrl = $"http://www.weather.com.cn/weather1d/{city.Code}.shtml"
            },
            StatusMessage = message
        };
    }
}
