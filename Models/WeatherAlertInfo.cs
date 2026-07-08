namespace TimeWidget.Models;

public enum AlertLevel
{
    None,
    Blue,
    Yellow,
    Orange,
    Red
}

public sealed class WeatherAlertInfo
{
    public AlertLevel Level { get; set; }
    public string Title { get; set; } = "No weather alert";
    public string Description { get; set; } = string.Empty;
    public string LinkText { get; set; } = "China Weather";
    public string LinkUrl { get; set; } = "http://www.weather.com.cn/";
}
