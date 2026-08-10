using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

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
    /// Valores de partida de la matriz: (tipo, complejidad, puntos, días para entregar).
    ///
    /// La progresión no es lineal a propósito (5-8-12-18): si «muy alta» valiera solo el doble que
    /// «baja», salir del pool tomando lo fácil sería siempre la mejor estrategia. Los requerimientos
    /// valen más que las tareas del mismo nivel porque incluyen entender qué se pide y validarlo con
    /// quien lo pidió, no solo programarlo.
    /// </summary>
    public static readonly (PoolWorkType Tipo, PoolComplexity Complejidad, int Puntos, int Dias)[] Matriz =
    [
        (PoolWorkType.Bug,           PoolComplexity.Baja,    5,  3),
        (PoolWorkType.Bug,           PoolComplexity.Media,   8,  5),
        (PoolWorkType.Bug,           PoolComplexity.Alta,   12,  8),
        (PoolWorkType.Bug,           PoolComplexity.MuyAlta, 18, 13),

        (PoolWorkType.Tarea,         PoolComplexity.Baja,    3,  3),
        (PoolWorkType.Tarea,         PoolComplexity.Media,   6,  5),
        (PoolWorkType.Tarea,         PoolComplexity.Alta,   10,  8),
        (PoolWorkType.Tarea,         PoolComplexity.MuyAlta, 15, 13),

        (PoolWorkType.Requerimiento, PoolComplexity.Baja,    8,  5),
        (PoolWorkType.Requerimiento, PoolComplexity.Media,  12,  8),
        (PoolWorkType.Requerimiento, PoolComplexity.Alta,   18, 13),
        (PoolWorkType.Requerimiento, PoolComplexity.MuyAlta, 25, 20),
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
    public static int Sembrar(AppDbContext db)
    {
        int agregadas = 0;
        agregadas += SembrarCriterios(db);
        agregadas += SembrarMatriz(db);
        agregadas += SembrarChecklists(db);
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
    private static int SembrarCriterios(AppDbContext db)
    {
        int agregados = 0;
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
        {
            var nombre = NombreCriterio(tipo);
            if (db.ScoringCriteria.Any(c => c.Name == nombre)) continue;

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
        if (agregados > 0) db.SaveChanges();
        return agregados;
    }

    private static int SembrarMatriz(AppDbContext db)
    {
        var existentes = db.PoolPointsMatrix.AsNoTracking()
            .Select(m => new { m.WorkType, m.Complexity })
            .ToList()
            .Select(m => (m.WorkType, m.Complexity))
            .ToHashSet();

        int agregadas = 0;
        foreach (var (tipo, complejidad, puntos, dias) in Matriz)
        {
            if (existentes.Contains((tipo, complejidad))) continue;
            db.PoolPointsMatrix.Add(new PoolPointsMatrixEntry
            {
                WorkType = tipo, Complexity = complejidad, Points = puntos, DiasLimite = dias
            });
            agregadas++;
        }
        if (agregadas > 0) db.SaveChanges();
        return agregadas;
    }

    /// <summary>
    /// Los checklists de partida, idempotentes por (tipo, texto). Si el líder desactivó un punto, no
    /// se vuelve a activar: la comprobación es por existencia, no por estado.
    /// </summary>
    private static int SembrarChecklists(AppDbContext db)
    {
        var existentes = db.PoolChecklistTemplateItems.AsNoTracking()
            .Select(t => new { t.WorkType, t.Text })
            .ToList()
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
        if (agregados > 0) db.SaveChanges();
        return agregados;
    }
}
