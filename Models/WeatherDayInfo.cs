namespace TimeWidget.Models;

public sealed class WeatherDayInfo
{
    public string DateLabel { get; set; } = string.Empty;
    public string WeatherText { get; set; } = string.Empty;
    public string WeatherIcon { get; set; } = string.Empty;
    public int HighTemp { get; set; }
    public int LowTemp { get; set; }
    public int Humidity { get; set; }
}
