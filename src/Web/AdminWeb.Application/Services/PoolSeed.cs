using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Datos con los que arranca el pool: los criterios bajo los que se anotan sus puntos, la matriz
/// tipo × complejidad y los checklists de cada tipo.
///
/// Todo es idempotente y solo AGREGA lo que falte: nunca pisa lo que el líder haya ajustado. Los
/// valores de aquí son un punto de partida razonable, no una política — se editan desde la pantalla.
/// </summary>
public static class PoolSeed
{
    /// <summary>
    /// Prefijo de los criterios del pool. Sirve para reconocerlos y para no ofrecerlos donde no
    /// corresponde (la asignación manual del líder).
    /// </summary>
    public const string PrefijoCriterio = "Pool: ";

    /// <summary>El criterio bajo el que se anotan los puntos de un tipo de actividad.</summary>
    public static string NombreCriterio(PoolWorkType tipo) => tipo switch
    {
        PoolWorkType.Bug           => PrefijoCriterio + "Bug",
        PoolWorkType.Tarea         => PrefijoCriterio + "Tarea",
        _                          => PrefijoCriterio + "Requerimiento"
    };

    public static string Etiqueta(PoolWorkType tipo) => tipo switch
    {
        PoolWorkType.Bug   => "Bug",
        PoolWorkType.Tarea => "Tarea",
        _                  => "Requerimiento"
    };

    public static string Etiqueta(PoolComplexity complejidad) => complejidad switch
    {
        PoolComplexity.Baja    => "Baja",
        PoolComplexity.Media   => "Media",
        PoolComplexity.Alta    => "Alta",
        _                      => "Muy alta"
    };

    /// <summary>
    /// Cómo se lee un estado en pantalla. Vive aquí, junto a las demás etiquetas, para que las dos
    /// pantallas del pool —la del líder y la del desarrollador— digan exactamente lo mismo.
    /// </summary>
    public static string Etiqueta(PoolActivityStatus estado) => estado switch
    {
        PoolActivityStatus.Disponible => "Libre en el pool",
        PoolActivityStatus.Tomada     => "Tomada",
        PoolActivityStatus.EnRevision => "Por verificar",
        PoolActivityStatus.Devuelta   => "Devuelta para corregir",
        PoolActivityStatus.Aceptada   => "Aceptada",
        _                             => "Retirada"
    };

    /// <summary>
    /// Valores de partida de la matriz: (tipo, complejidad, puntos, HORAS para entregar).
    ///
    /// <para>La progresión de puntos no es lineal a propósito (5-8-12-18): si «muy alta» valiera solo
    /// el doble que «baja», salir del pool tomando lo fácil sería siempre la mejor estrategia. Los
    /// requerimientos valen más que las tareas del mismo nivel porque incluyen entender qué se pide y
    /// validarlo con quien lo pidió, no solo programarlo.</para>
    ///
    /// <para><b>Las horas son las de antes multiplicadas por ocho</b> —3/5/8/13/20 días con jornada de
    /// ocho horas— y ese ocho tiene que ser EL MISMO que usa la conversión de datos del migrador
    /// (<c>ConvertirPlazosDeDiasAHorasUnaVez</c>). Si los dos números divergieran, una base nueva y
    /// una migrada arrancarían con matrices distintas y nadie sabría cuál es la buena.</para>
    ///
    /// <para>Son horas de RELOJ: al tomar la actividad se suman con <c>AddHours</c> sobre el instante
    /// de ahora, así que 40 h es pasado mañana y no dentro de cinco días laborales. Es lo coherente
    /// con medir contra el cronómetro, que también cuenta horas; contar solo jornadas hábiles exigiría
    /// un calendario laboral con festivos, que es una funcionalidad nueva y no una conversión.</para>
    /// </summary>
    public static readonly (PoolWorkType Tipo, PoolComplexity Complejidad, int Puntos, decimal Horas)[] Matriz =
    [
        (PoolWorkType.Bug,           PoolComplexity.Baja,    5,  24m),
        (PoolWorkType.Bug,           PoolComplexity.Media,   8,  40m),
        (PoolWorkType.Bug,           PoolComplexity.Alta,   12,  64m),
        (PoolWorkType.Bug,           PoolComplexity.MuyAlta, 18, 104m),

        (PoolWorkType.Tarea,         PoolComplexity.Baja,    3,  24m),
        (PoolWorkType.Tarea,         PoolComplexity.Media,   6,  40m),
        (PoolWorkType.Tarea,         PoolComplexity.Alta,   10,  64m),
        (PoolWorkType.Tarea,         PoolComplexity.MuyAlta, 15, 104m),

        (PoolWorkType.Requerimiento, PoolComplexity.Baja,    8,  40m),
        (PoolWorkType.Requerimiento, PoolComplexity.Media,  12,  64m),
        (PoolWorkType.Requerimiento, PoolComplexity.Alta,   18, 104m),
        (PoolWorkType.Requerimiento, PoolComplexity.MuyAlta, 25, 160m),
    ];

    /// <summary>
    /// Checklists de partida. Cada tipo tiene al menos un punto que EXIGE evidencia: sin eso, marcar
    /// las casillas no cuesta nada y la verificación del líder se queda sin nada que mirar.
    /// </summary>
    public static readonly (PoolWorkType Tipo, string Texto, bool Evidencia)[] Checklists =
    [
        (PoolWorkType.Bug, "Reproduje el error y anoté cómo", false),
        (PoolWorkType.Bug, "Identifiqué la causa raíz (no solo el síntoma)", false),
        (PoolWorkType.Bug, "Pull request enlazado", true),
        (PoolWorkType.Bug, "Probé que el error ya no ocurre", false),
        (PoolWorkType.Bug, "Desplegado y verificado en el ambiente que corresponde", false),

        (PoolWorkType.Tarea, "El alcance quedó claro antes de empezar", false),
        (PoolWorkType.Tarea, "Pull request o artefacto enlazado", true),
        (PoolWorkType.Tarea, "Probado", false),
        (PoolWorkType.Tarea, "Documentado donde toca", false),

        (PoolWorkType.Requerimiento, "Validé el alcance con quien lo pidió", false),
        (PoolWorkType.Requerimiento, "Registré el diseño y la estimación", false),
        (PoolWorkType.Requerimiento, "Pull request enlazado", true),
        (PoolWorkType.Requerimiento, "Pruebas realizadas", false),
        (PoolWorkType.Requerimiento, "Entregado y aceptado por quien lo pidió", false),
    ];

    /// <summary>Siembra lo que falte. Devuelve cuántas filas se agregaron, para la bitácora del arranque.</summary>
    public static async Task<int> SembrarAsync(AppDbContext db, CancellationToken ct = default)
    {
        int agregadas = 0;
        agregadas += await SembrarCriteriosAsync(db, ct);
        agregadas += await SembrarMatrizAsync(db, ct);
        agregadas += await SembrarChecklistsAsync(db, ct);
        return agregadas;
    }

    /// <summary>
    /// Los tres criterios bajo los que se anotan los puntos del pool.
    ///
    /// Nacen con <c>DefaultPoints = 0</c> a propósito, y eso no es un olvido: los puntos reales los
    /// pone la actividad, que los trae congelados de la matriz. Además el cero es una guarda gratis
    /// — <see cref="PerformanceScoringService"/> rechaza autocalificarse con criterios que no otorgan
    /// puntos positivos, así que nadie puede registrar «Pool: Bug» a mano para regalarse trabajo del
    /// pool que no hizo.
    /// </summary>
    private static async Task<int> SembrarCriteriosAsync(AppDbContext db, CancellationToken ct)
    {
        int agregados = 0;
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
        {
            var nombre = NombreCriterio(tipo);
            if (await db.ScoringCriteria.AnyAsync(c => c.Name == nombre, ct)) continue;

            db.ScoringCriteria.Add(new ScoringCriterion
            {
                Name = nombre,
                Description = $"Actividad del pool de tipo {Etiqueta(tipo)}. Los puntos los fija la " +
                              "matriz de complejidad y quedan congelados en la actividad al crearla.",
                DefaultPoints = 0,
                Scope = CriterionScope.Individual,
                IsActive = true
            });
            agregados++;
        }
        if (agregados > 0) await db.SaveChangesAsync(ct);
        return agregados;
    }

    private static async Task<int> SembrarMatrizAsync(AppDbContext db, CancellationToken ct)
    {
        var existentes = (await db.PoolPointsMatrix.AsNoTracking()
            .Select(m => new { m.WorkType, m.Complexity })
            .ToListAsync(ct))
            .Select(m => (m.WorkType, m.Complexity))
            .ToHashSet();

        int agregadas = 0;
        foreach (var (tipo, complejidad, puntos, horas) in Matriz)
        {
            if (existentes.Contains((tipo, complejidad))) continue;
            // Solo HorasLimite. DiasLimite se queda en su 0 por omisión a propósito: la web ya no lo
            // escribe, y una celda nueva solo aparece en una base recién creada —donde el escritorio
            // no está mirando—, porque las doce combinaciones ya existen en cualquier base con datos.
            db.PoolPointsMatrix.Add(new PoolPointsMatrixEntry
            {
                WorkType = tipo, Complexity = complejidad, Points = puntos, HorasLimite = horas
            });
            agregadas++;
        }
        if (agregadas > 0) await db.SaveChangesAsync(ct);
        return agregadas;
    }

    /// <summary>
    /// Los checklists de partida, idempotentes por (tipo, texto). Si el líder desactivó un punto, no
    /// se vuelve a activar: la comprobación es por existencia, no por estado.
    /// </summary>
    private static async Task<int> SembrarChecklistsAsync(AppDbContext db, CancellationToken ct)
    {
        var existentes = (await db.PoolChecklistTemplateItems.AsNoTracking()
            .Select(t => new { t.WorkType, t.Text })
            .ToListAsync(ct))
            .Select(t => (t.WorkType, t.Text))
            .ToHashSet();

        int agregados = 0;
        int orden = 0;
        PoolWorkType? tipoPrevio = null;
        foreach (var (tipo, texto, evidencia) in Checklists)
        {
            if (tipoPrevio != tipo) { orden = 0; tipoPrevio = tipo; }
            orden += 10;   // de diez en diez: deja hueco para intercalar sin renumerar todo

            if (existentes.Contains((tipo, texto))) continue;
            db.PoolChecklistTemplateItems.Add(new PoolChecklistTemplateItem
            {
                WorkType = tipo, Text = texto, Orden = orden, RequiereEvidencia = evidencia, IsActive = true
            });
            agregados++;
        }
        if (agregados > 0) await db.SaveChangesAsync(ct);
        return agregados;
    }
}
