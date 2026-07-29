namespace Administrador_Desarrollo_Web.Models;

public class DevOpsTicket
{
    public int Id { get; set; }
    public int ExternalId { get; set; }
    public string Title { get; set; } = "";
    public string WorkItemType { get; set; } = "";
    public string State { get; set; } = "";
    public string Priority { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    /// <summary>uniqueName del asignado en DevOps (correo/UPN). Llave FIABLE para saber de quién es
    /// el ticket, a diferencia del nombre para mostrar, que trae/omite acentos y segundos nombres.
    /// Se llena al sincronizar; nulo en tickets sincronizados antes de agregar esta columna.</summary>
    public string? AssignedToUniqueName { get; set; }
    public string AreaPath { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public string Tags { get; set; } = "";
    public string? Description { get; set; }
    public double? StoryPoints { get; set; }
    public DateTime? CreatedAtExternal { get; set; }
    public DateTime? UpdatedAtExternal { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string Url { get; set; } = "";
    public int CommentCount { get; set; }

    public ICollection<TicketLink> TicketLinks { get; set; } = [];
}
