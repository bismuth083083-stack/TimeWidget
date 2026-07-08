namespace TimeWidget.Models;

public sealed record WeatherCityInfo(
    string Name,
    string Code,
    double Latitude,
    double Longitude);
