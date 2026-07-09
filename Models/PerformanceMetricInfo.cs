namespace TimeWidget.Models;

public sealed class PerformanceMetricInfo
{
    public double? UsagePercent { get; init; }
    public double? FrequencyMHz { get; init; }
    public double? TemperatureC { get; init; }
    public double? PowerW { get; init; }
}
