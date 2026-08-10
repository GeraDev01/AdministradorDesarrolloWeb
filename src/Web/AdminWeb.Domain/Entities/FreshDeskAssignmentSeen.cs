namespace AdminWeb.Domain.Entities;

/// <summary>
/// Igual que <see cref="DevOpsAssignmentSeen"/> pero para los tickets de Freshdesk asignados a un
/// usuario. El id de ticket de Freshdesk es <c>long</c>, por eso es una tabla aparte.
///
/// Se guarda también el <see cref="AgentId"/> (el agente de Freshdesk resuelto por /agents/me): la
/// línea base es POR agente, así que rotar la API key a otro agente no dispara un aluvión con todo
/// el backlog del nuevo agente. Las filas de un ticket que deja de estar asignado se BORRAN, para que
/// una reasignación posterior vuelva a avisar.
/// </summary>
public class FreshDeskAssignmentSeen
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public long AgentId { get; set; }
    public long ExternalId { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}
