namespace TimeWidget.Models;

public sealed class PerformanceSnapshot
{
    public PerformanceMetricInfo Cpu { get; init; } = new();
    public PerformanceMetricInfo Gpu { get; init; } = new();
    public MemoryMetricInfo Memory { get; init; } = new();
}
