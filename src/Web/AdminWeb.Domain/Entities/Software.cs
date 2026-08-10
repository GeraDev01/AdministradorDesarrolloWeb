using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

public class Software
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SoftwareCategory Category { get; set; }
    public SoftwareLicenseType LicenseType { get; set; }
    public SoftwareStatus Status { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? LicenseKey { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? InstalledOn { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
