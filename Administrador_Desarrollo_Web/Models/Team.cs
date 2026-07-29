namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Equipo de desarrollo. Cada desarrollador pertenece a lo sumo a un equipo
/// (Developer.TeamId); el tablero de organización mueve desarrolladores entre equipos.
/// </summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Color del equipo en formato hex (ej. "#2563EB"), para el tablero y el PDF.</summary>
    public string? ColorHex { get; set; }
    public int? LeadDeveloperId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Developer? Lead { get; set; }
    public ICollection<Developer> Members { get; set; } = new List<Developer>();
}
