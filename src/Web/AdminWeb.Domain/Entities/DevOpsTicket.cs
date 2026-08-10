namespace AdminWeb.Domain.Entities;

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

    // ── Prioridad definida por el líder ──────────────────────────────────
    /// <summary>
    /// Cuándo se fijó la prioridad DESDE esta aplicación. Es distinto de <see cref="Priority"/>:
    /// ese campo siempre trae algo porque DevOps le pone 2 por omisión a todo, así que no sirve
    /// para saber si alguien la pensó. Null = nadie la ha definido todavía.
    /// </summary>
    public DateTime? PriorityConfirmedAt { get; set; }
    public int? PriorityConfirmedByUserId { get; set; }

    // ── Estimación del desarrollador ─────────────────────────────────────
    /// <summary>
    /// Horas estimadas por quien tiene el ticket. Se escribe también en el campo Effort del work
    /// item, para que valga fuera de esta aplicación. Null = todavía no lo ha estimado.
    /// </summary>
    public double? EstimatedHours { get; set; }
    public DateTime? EstimatedAt { get; set; }
    public int? EstimatedByDeveloperId { get; set; }

    /// <summary>Le falta lo que el líder tiene que definir en todo ticket.</summary>
    public bool SinPrioridadDefinida => PriorityConfirmedAt == null;

    /// <summary>Está asignado y quien lo tiene todavía no dijo cuánto le va a llevar.</summary>
    public bool SinEstimar => EstimatedHours == null && !string.IsNullOrWhiteSpace(AssignedTo);

    public ICollection<TicketLink> TicketLinks { get; set; } = [];
}
