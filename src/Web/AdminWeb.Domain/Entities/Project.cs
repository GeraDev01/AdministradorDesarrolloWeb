using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>Proyecto relacionado a un equipo.</summary>
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Client { get; set; }
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Activo;
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
