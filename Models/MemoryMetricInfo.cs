namespace TimeWidget.Models;

public sealed class MemoryMetricInfo
{
    public double? UsedGB { get; init; }
    public double? TotalGB { get; init; }
    public double? AvailableGB { get; init; }
    public double? UsagePercent { get; init; }
}
