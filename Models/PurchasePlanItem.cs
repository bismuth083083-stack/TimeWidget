namespace TimeWidget.Models;

public enum PurchasePriority
{
    Low,
    Medium,
    High,
    Urgent
}

public sealed class PurchasePlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public decimal EstimatedPrice { get; set; }
    public PurchasePriority Priority { get; set; } = PurchasePriority.Medium;
    public DateTime? TargetDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool IsPurchased { get; set; }
    public DateTime? PurchasedAt { get; set; }
    public decimal? ActualPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
