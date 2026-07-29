namespace Administrador_Desarrollo_Web.Models;

public class WatchedTicket
{
    public int Id { get; set; }
    public int DevOpsTicketId { get; set; }
    public string WatchedByUser { get; set; } = "";
    public DateTime WatchedSince { get; set; } = DateTime.UtcNow;
    public DevOpsTicket DevOpsTicket { get; set; } = null!;
}
