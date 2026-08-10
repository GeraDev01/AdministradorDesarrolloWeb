namespace AdminWeb.Domain.Entities;

/// <summary>
/// Registro de qué work items YA se conocían como asignados a un usuario, para no volver a avisar
/// del mismo y, sobre todo, para no soltar un aluvión de avisos la primera vez (línea base).
/// </summary>
public class DevOpsAssignmentSeen
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ExternalId { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}
