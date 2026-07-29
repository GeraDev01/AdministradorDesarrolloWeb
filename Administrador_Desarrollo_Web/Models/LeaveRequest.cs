namespace Administrador_Desarrollo_Web.Models;

public enum LeaveType
{
    PermisoPersonal = 0,
    Incapacidad     = 1,
    CitaMedica      = 2,
    AsuntoFamiliar  = 3,
    Capacitacion    = 4,
    Otro            = 5
}

public class LeaveRequest
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;
    public LeaveType Type { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int DaysCount { get; set; } = 1;
    public string? Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
