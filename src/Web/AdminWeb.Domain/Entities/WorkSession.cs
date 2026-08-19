using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

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

    /// <summary>
    /// Último latido del cronómetro. Es nuevo de la web y no existía en el escritorio: allí cerrar
    /// la ventana era un acto deliberado y el tiempo se consolidaba en ese momento, mientras que en
    /// la web cerrar la pestaña —o que el equipo se duerma, o que se caiga la red— es lo normal y no
    /// avisa de nada. Sin este dato, una sesión Activa seguiría sumando segundos toda la noche, o
    /// habría que descartar el tramo entero cada vez que alguien no detiene el cronómetro a mano.
    ///
    /// Es null en las sesiones que vienen del escritorio y en las que aún no han latido; quien barre
    /// (ver <c>WorkSessionService.ConsolidarSesionesSinLatidoAsync</c>) sabe qué hacer con ellas.
    /// </summary>
    public DateTime? LastHeartbeatUtc { get; set; }

    /// <summary>
    /// Cuándo se publicó en Azure DevOps el aviso de que esta sesión ARRANCÓ. Nulo si todavía no se
    /// publicó, o si lo que se cronometra no está ligado a ningún work item.
    ///
    /// <para><b>Es marca por SESIÓN y no por objetivo</b>, y ahí está toda la regla: detener y volver
    /// a empezar crea una sesión nueva y merece su propio aviso, mientras que reanudar una PAUSADA
    /// sigue siendo la misma y no debe comentar nada. Una marca colgada del requerimiento o de la
    /// actividad no sabría distinguir esos dos casos, que es justo lo que hay que distinguir.</para>
    ///
    /// <para><b>Guarda cuándo y no un sí/no</b> por lo mismo que las marcas del pool: con la fecha se
    /// puede diagnosticar un aviso duplicado o uno que salió tardísimo; con un booleano solo se sabe
    /// que en algún momento pasó algo.</para>
    ///
    /// <para>Anulable a la fuerza: la aplicación de escritorio sigue insertando en esta tabla sin
    /// conocer esta columna. Por eso mismo, quien barre no puede preguntar solo por el nulo —el
    /// histórico entero lo es— y el migrador siembra un centinela al crearla.</para>
    /// </summary>
    public DateTime? InicioComentadoEnUtc { get; set; }

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
