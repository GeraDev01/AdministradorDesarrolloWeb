namespace Administrador_Desarrollo_Web.Models;

public class FreshDeskTicket
{
    public int Id { get; set; }
    public long ExternalId { get; set; }
    public string Subject { get; set; } = "";
    public int Status { get; set; }      // 2=Open 3=Pending 4=Resolved 5=Closed
    public int Priority { get; set; }    // 1=Low 2=Medium 3=High 4=Urgent
    public string? Type { get; set; }
    public int Source { get; set; }      // 1=Email 2=Portal 3=Phone 7=Chat
    /// <summary>Id del agente asignado en Freshdesk (responder_id). Sirve para detectar «me asignaron
    /// este ticket» comparándolo con mi propio id de agente (/agents/me). Null = sin asignar.</summary>
    public long? ResponderId { get; set; }
    public string AgentName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string RequesterEmail { get; set; } = "";
    public string Tags { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? CreatedAtExternal { get; set; }
    public DateTime? UpdatedAtExternal { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string Url { get; set; } = "";

    public ICollection<TicketLink> TicketLinks { get; set; } = [];

    public static string StatusLabel(int s) => s switch
    {
        2 => "Abierto",
        3 => "Pendiente",
        4 => "Resuelto",
        5 => "Cerrado",
        _ => s.ToString()
    };

    public static string PriorityLabel(int p) => p switch
    {
        1 => "Baja",
        2 => "Media",
        3 => "Alta",
        4 => "Urgente",
        _ => p.ToString()
    };

    public static string SourceLabel(int src) => src switch
    {
        1 => "Email",
        2 => "Portal",
        3 => "Teléfono",
        7 => "Chat",
        9 => "Widget",
        _ => "Otro"
    };
}
