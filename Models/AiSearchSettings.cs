namespace TimeWidget.Models;

public sealed class AiSearchSettings
{
    public string ModelName { get; set; } = "gpt-4.1-mini";
    public int MaxOutputTokens { get; set; } = 500;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public bool IsLocked { get; set; }
}
