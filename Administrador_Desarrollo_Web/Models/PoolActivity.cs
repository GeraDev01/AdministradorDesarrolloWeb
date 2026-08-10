namespace Administrador_Desarrollo_Web.Models;

/// <summary>Tipo de trabajo de una actividad del pool. Junto con la complejidad determina sus puntos.</summary>
public enum PoolWorkType { Bug = 0, Tarea = 1, Requerimiento = 2 }

/// <summary>Qué tan complicada es. La fija el líder al crearla, ANTES de que nadie la trabaje.</summary>
public enum PoolComplexity { Baja = 0, Media = 1, Alta = 2, MuyAlta = 3 }

/// <summary>
/// Estados de una actividad del pool.
/// <list type="bullet">
/// <item><b>Disponible</b>: en el pool, cualquiera puede tomarla.</item>
/// <item><b>Tomada</b>: alguien la está trabajando.</item>
/// <item><b>EnRevision</b>: entregada, esperando la verificación del líder.</item>
/// <item><b>Devuelta</b>: el líder la regresó con un motivo; sigue siendo de quien la tomó.</item>
/// <item><b>Aceptada</b>: verificada; ya generó sus puntos. Es terminal.</item>
/// <item><b>Retirada</b>: el líder la quitó del pool antes de que nadie la tomara.</item>
/// </list>
/// </summary>
public enum PoolActivityStatus { Disponible = 0, Tomada = 1, EnRevision = 2, Devuelta = 3, Aceptada = 4, Retirada = 5 }

/// <summary>
/// Una actividad del pool: trabajo con un valor en puntos fijado ANTES de que nadie lo tome.
///
/// Es lo que resuelve el problema del sistema anterior. Con la autocalificación libre, el
/// desarrollador elegía qué actividad registrar y cuántas veces, y el líder decidía después si lo
/// valía: dos juicios subjetivos sobre trabajo ya hecho. Aquí el valor sale de una matriz
/// (tipo × complejidad) que el líder configura una sola vez, y queda CONGELADO en
/// <see cref="Points"/> al crear la actividad. Cuando alguien la toma ya sabe exactamente cuánto
/// vale, y al verificarla no hay nada que negociar: solo comprobar que el trabajo está hecho.
///
/// No es un <see cref="Requirement"/>. Un requerimiento se asigna (a varias personas si hace
/// falta), vive en un sprint, se sincroniza con Azure DevOps y tiene su propio ciclo de estados.
/// Una actividad del pool la TOMA una sola persona, se completa contra un checklist y termina en
/// puntos. Mezclarlas obligaría a filtrar «las del pool» en todas las consultas de requerimientos
/// —métricas, sprint, capacidad, import de DevOps— y un filtro olvidado metería actividades del
/// pool en el backlog.
///
/// Tampoco es una <see cref="DevActivity"/>, aunque use una: al tomarla se crea una actividad libre
/// enlazada (<see cref="LinkedDevActivityId"/>) para que el cronómetro de siempre funcione sin
/// tocarlo.
/// </summary>
public class PoolActivity
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public PoolWorkType WorkType { get; set; }
    public PoolComplexity Complexity { get; set; }

    /// <summary>
    /// Puntos que vale, copiados de la matriz al crearla. CONGELADOS a propósito: si se leyeran de
    /// la matriz al aceptar, cambiar la tabla revaluaría hacia atrás todo lo ya trabajado, y quien
    /// tomó una actividad de 13 puntos podría acabar cobrando 5. Es el mismo patrón que
    /// <see cref="PointEntry.Points"/>.
    /// </summary>
    public int Points { get; set; }

    public PoolActivityStatus Status { get; set; } = PoolActivityStatus.Disponible;

    /// <summary>Enlace al work item, ticket o incidencia que la origina. Solo http/https.</summary>
    public string? ExternalUrl { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Reclamo ──────────────────────────────────────────────────────────
    /// <summary>Quién la tomó. Uno solo: no es una asignación múltiple como la de los requerimientos.</summary>
    public int? ClaimedByDeveloperId { get; set; }
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// Hasta cuándo se espera que esté entregada, calculado al tomarla con los días de la matriz.
    /// Vencerla no dispara nada automático: se resalta y el líder decide si la libera. Liberar sola
    /// una actividad que alguien está trabajando ahora mismo sería peor que el problema.
    /// </summary>
    public DateTime? ClaimDeadlineAt { get; set; }

    /// <summary>
    /// Cuántas veces se devolvió al pool. Devolver no se castiga —penalizarlo haría que nadie se
    /// atreviera a tomar nada difícil—, pero el número queda a la vista del líder: una actividad
    /// devuelta cinco veces dice algo, de la actividad o de quien la toma.
    /// </summary>
    public int ReturnedCount { get; set; }

    // ── Entrega y verificación ───────────────────────────────────────────
    public DateTime? DeliveredAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Motivo de la última devolución. Solo la última: el hilo completo va en el historial.</summary>
    public string? ReviewComment { get; set; }

    /// <summary>Cuántas veces se ha vuelto a entregar tras una devolución.</summary>
    public int ReviewRound { get; set; }

    /// <summary>
    /// Bitácora del ida y vuelta con el líder, fechada y en orden. Existe por lo mismo que la de
    /// <see cref="PointEntry.ReviewHistory"/>: <see cref="ReviewComment"/> guarda solo la última
    /// decisión, así que sin esto la segunda devolución borraría el motivo de la primera.
    /// </summary>
    public string? ReviewHistory { get; set; }

    // ── Enlaces (sin FK a propósito) ─────────────────────────────────────
    /// <summary>
    /// Entrada de puntos que generó al aceptarse. Sirve para dos cosas: rastrear de dónde salieron
    /// esos puntos, e impedir que una segunda aceptación los duplique.
    ///
    /// Sin clave foránea, igual que <see cref="WorkInterval"/>: con FK habría dos rutas de borrado
    /// en cascada desde Developers (por PointEntries y por DevActivities) y SQL Server rechaza
    /// crear esas restricciones. El precio asumido: si la limpieza de datos purga las entradas de
    /// puntos, este número queda colgando — inocuo, es solo una traza.
    /// </summary>
    public int? PointEntryId { get; set; }

    /// <summary>
    /// Actividad libre creada al tomarla, para poder cronometrar el trabajo con el cronómetro que
    /// ya existe. Es lo que evita añadir un tercer objetivo al par requerimiento/actividad de
    /// <see cref="WorkSession"/>, que obligaría a rehacer la tabla y a tocar cada pantalla de
    /// tiempo. Sin FK por lo mismo que el campo anterior.
    /// </summary>
    public int? LinkedDevActivityId { get; set; }

    public Developer? ClaimedBy { get; set; }

    /// <summary>Está en manos de alguien (tomada o devuelta para corregir).</summary>
    public bool EnCurso => Status is PoolActivityStatus.Tomada or PoolActivityStatus.Devuelta;

    /// <summary>Pasó su fecha límite y sigue sin entregarse.</summary>
    public bool Vencida => EnCurso && ClaimDeadlineAt is DateTime f && f < DateTime.UtcNow;
}

/// <summary>
/// Una celda de la matriz de puntos: cuánto vale un tipo de trabajo con una complejidad dada, y en
/// cuántos días se espera terminarlo.
///
/// Es una tabla y no un ajuste en JSON porque cada celda se edita, se consulta y se audita por
/// separado, y porque la unicidad de (tipo, complejidad) la debe garantizar la base y no la
/// disciplina de quien escriba el código que la lea. Es el mismo criterio por el que
/// <see cref="ScoringCriterion"/> es una tabla.
/// </summary>
public class PoolPointsMatrixEntry
{
    public int Id { get; set; }
    public PoolWorkType WorkType { get; set; }
    public PoolComplexity Complexity { get; set; }

    /// <summary>Puntos que otorga. Debe ser mayor que cero: una actividad que no suma no es trabajo.</summary>
    public int Points { get; set; }

    /// <summary>Días desde que se toma hasta que se espera entregada. 0 = sin fecha límite.</summary>
    public int DiasLimite { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
}

/// <summary>
/// Un punto del checklist de un tipo de actividad: la definición de «terminado» que el líder
/// escribe una vez y vale para todas las actividades de ese tipo.
///
/// No hay tabla de cabecera para la plantilla porque hay exactamente UNA por tipo: la plantilla es
/// el conjunto de sus puntos activos.
/// </summary>
public class PoolChecklistTemplateItem
{
    public int Id { get; set; }
    public PoolWorkType WorkType { get; set; }
    public string Text { get; set; } = "";
    public int Orden { get; set; }

    /// <summary>Exige un enlace (al PR, al work item) para poder marcarlo. Es lo que hace
    /// verificable el checklist: sin evidencia, marcar una casilla no cuesta nada.</summary>
    public bool RequiereEvidencia { get; set; }

    /// <summary>
    /// Los puntos no se borran, se desactivan: las actividades en curso llevan su propia copia, pero
    /// borrar la plantilla haría perder qué se pedía cuando se hicieron las anteriores.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Un punto del checklist de UNA actividad concreta. Es una COPIA de la plantilla, hecha en el
/// momento de tomarla.
///
/// Copiada y no referenciada por la misma razón que los puntos están congelados: a quien tomó una
/// actividad se le puede exigir lo que se le pidió al tomarla, no lo que el líder agregue a la
/// plantilla mientras la trabaja. Y se copia al TOMARLA, no al crearla, para que el líder pueda
/// seguir afinando la plantilla mientras la actividad espera en el pool.
/// </summary>
public class PoolActivityChecklistItem
{
    public int Id { get; set; }
    public int PoolActivityId { get; set; }

    public string Text { get; set; } = "";
    public int Orden { get; set; }
    public bool RequiereEvidencia { get; set; }

    public bool IsDone { get; set; }
    public DateTime? DoneAtUtc { get; set; }

    /// <summary>Enlace que respalda este punto. Solo http/https, validado al marcarlo.</summary>
    public string? EvidenceUrl { get; set; }

    public PoolActivity Activity { get; set; } = null!;
}
