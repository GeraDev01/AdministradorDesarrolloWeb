namespace Administrador_Desarrollo_Web.Models;

/// <summary>Estado de una sesión de trabajo (cronómetro) sobre un item.</summary>
public enum WorkSessionStatus { Activa = 0, Pausada = 1, Detenida = 2 }

/// <summary>
/// Sesión de trabajo de un desarrollador: mide el tiempo REAL dedicado, con
/// inicio/pausa/reanudación/detención. Un mismo objetivo puede acumular varias sesiones a lo
/// largo del tiempo; el total es la suma de todas.
///
/// El objetivo es un requerimiento asignado (<see cref="RequirementId"/>) O una actividad libre
/// (<see cref="ActivityId"/>), nunca ambos ni ninguno — ver <see cref="EsValida"/>.
/// </summary>
public class WorkSession
{
    public int Id { get; set; }

    /// <summary>Requerimiento cronometrado; null si la sesión es de una actividad libre.</summary>
    public int? RequirementId { get; set; }

    /// <summary>Actividad libre cronometrada; null si la sesión es de un requerimiento.</summary>
    public int? ActivityId { get; set; }

    public int DeveloperId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    /// <summary>Segundos ya consolidados (sin contar el tramo en curso cuando está Activa).</summary>
    public int AccumulatedSeconds { get; set; }

    /// <summary>Momento de la última reanudación; se usa para calcular el tramo en curso mientras está Activa.</summary>
    public DateTime? LastResumedAt { get; set; }

    public WorkSessionStatus Status { get; set; } = WorkSessionStatus.Activa;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Requirement? Requirement { get; set; }
    public DevActivity? Activity { get; set; }
    public Developer Developer { get; set; } = null!;

    /// <summary>Exactamente uno de los dos objetivos debe estar puesto.</summary>
    public bool EsValida => RequirementId.HasValue ^ ActivityId.HasValue;

    /// <summary>Tope defensivo del tramo en curso (24 h) para que una sesión huérfana
    /// transitoria nunca muestre valores absurdos antes de reconciliarse al arranque.</summary>
    public const int MaxLiveSegmentSeconds = 24 * 3600;

    /// <summary>Segundos totales incluyendo el tramo en curso (acotado) si la sesión está Activa.</summary>
    public int LiveSeconds(DateTime nowUtc) =>
        AccumulatedSeconds + (Status == WorkSessionStatus.Activa && LastResumedAt is DateTime r
            ? Math.Clamp((int)(nowUtc - r).TotalSeconds, 0, MaxLiveSegmentSeconds)
            : 0);
}
