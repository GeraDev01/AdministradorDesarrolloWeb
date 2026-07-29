namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Registro histórico de una rotación de un desarrollador entre equipos
/// (de un equipo a otro, o a/desde "sin equipo"). Guarda nombres como snapshot
/// para que el historial sobreviva aunque se borre el equipo o el desarrollador.
/// </summary>
public class TeamRotation
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public string DeveloperName { get; set; } = "";
    public int? FromTeamId { get; set; }
    public string FromTeamName { get; set; } = "Sin equipo";
    public int? ToTeamId { get; set; }
    public string ToTeamName { get; set; } = "Sin equipo";
    public string? Note { get; set; }
    public int? RotatedByUserId { get; set; }
    public DateTime RotatedAt { get; set; } = DateTime.UtcNow;
}
