namespace AdminWeb.Domain.Entities;

/// <summary>
/// Voto (apoyo) de un usuario a una sugerencia. Un usuario vota a lo sumo una vez por sugerencia
/// (índice único en SuggestionId + UserId); votar de nuevo lo quita. Sirve para que el equipo priorice
/// y el administrador vea las más pedidas.
/// </summary>
public class SuggestionVote
{
    public int Id { get; set; }
    public int SuggestionId { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Suggestion Suggestion { get; set; } = null!;
}
