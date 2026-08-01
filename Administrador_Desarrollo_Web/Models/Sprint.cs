namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Un sprint: el tramo de calendario contra el que se mide el avance. El administrador fija las
/// fechas y le cuelga requerimientos; la pantalla de seguimiento compara el avance real de esos
/// requerimientos contra el tiempo consumido.
///
/// Las fechas son DÍAS locales (sin hora): un sprint «del 3 al 14» incluye ambos extremos. Se
/// guardan como fecha a medianoche; toda la aritmética vive en <c>SprintService.CalcularAvance</c>.
/// </summary>
public class Sprint
{
    public int Id { get; set; }

    /// <summary>«Sprint 14», «Agosto 1.ª quincena»… como el equipo los nombre.</summary>
    public string Name { get; set; } = "";

    /// <summary>El objetivo del sprint, opcional: qué se quiere poder decir al terminarlo.</summary>
    public string? Goal { get; set; }

    /// <summary>Primer día del sprint (fecha local, inclusive).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Último día del sprint (fecha local, inclusive).</summary>
    public DateTime EndDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Requirement> Requirements { get; set; } = new List<Requirement>();
}
