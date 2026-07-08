namespace TimeWidget.Models;

public sealed class WeatherCache
{
    public DateTimeOffset UpdatedAt { get; set; }
    public string CityName { get; set; } = "Beijing";
    public string CityCode { get; set; } = "101010100";
    public List<WeatherDayInfo> Days { get; set; } = [];
    public WeatherAlertInfo Alert { get; set; } = new();
}
