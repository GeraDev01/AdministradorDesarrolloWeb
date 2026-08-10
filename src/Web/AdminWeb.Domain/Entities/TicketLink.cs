namespace AdminWeb.Domain.Entities;

public class TicketLink
{
    public int Id { get; set; }
    public int DevOpsTicketId { get; set; }
    public int FreshDeskTicketId { get; set; }
    public string? Notes { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string? LinkedByUser { get; set; }

    public DevOpsTicket DevOpsTicket { get; set; } = null!;
    public FreshDeskTicket FreshDeskTicket { get; set; } = null!;
}
