using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

public class AzureResource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AzureResourceType ResourceType { get; set; }
    public AzureResourceStatus Status { get; set; }
    public AzureEnvironment Environment { get; set; }
    public string? ResourceGroup { get; set; }
    public string? SubscriptionName { get; set; }
    public string? Region { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public decimal? MonthlyCostEstimate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
