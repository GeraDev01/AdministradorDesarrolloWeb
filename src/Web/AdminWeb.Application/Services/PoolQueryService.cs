using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Pool;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma de una sola vez lo que enseña cada pantalla del pool y lo traduce a contratos propios.
///
/// Existe por dos motivos, ninguno de negocio: las reglas siguen enteras en
/// <see cref="PoolActivityService"/>, que es quien decide y quien avisa.
///
/// El primero es que ningún tipo de EF puede cruzar al navegador. Una <see cref="PoolActivity"/>
/// arrastra su sello de concurrencia, su desarrollador enlazado y los identificadores de la entrada
/// de puntos y del cronómetro; nada de eso pinta una pantalla y todo eso viajaría en cada respuesta.
///
/// El segundo es el avance del checklist. En el escritorio la pantalla llamaba a <c>ChecklistDe</c>
/// una vez POR FILA para escribir «3/5», y contra una base local eso era gratis; aquí sería una
/// consulta por actividad y por refresco. Aquí se cuenta agrupado, de una sola vez.
/// </summary>
public class PoolQueryService(AppDbContext db, ICurrentUser currentUser, PoolActivityService pool)
{
    /// <summary>
    /// El pool visto por quien tiene la sesión: lo libre y lo suyo.
    ///
    /// Una cuenta sin ficha de desarrollador SÍ ve lo disponible —mirar qué hay no le hace daño a
    /// nadie— pero se va sin la lista de «mías», porque no tiene ninguna ni podría tomarla: los
    /// puntos se abonan a una ficha.
    /// </summary>
    public async Task<MiPoolDto> MiPoolAsync(PoolWorkType? tipo = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // La matriz se lee UNA vez —son doce filas— para poder resolver el plazo efectivo de cada
        // actividad libre antes de mandarla. Sin esto, quien mira el pool no puede ver en cuántas
        // horas se le va a pedir un bug hasta DESPUÉS de tomarlo, que es justo cuando ya no le sirve
        // para decidir. Resolverlo en el navegador exigiría mandarle la matriz entera y repetir ahí
        // la regla de precedencia, que es la clase de duplicado que acaba desincronizándose.
        var plazoDeLaMatriz = (await pool.ObtenerMatrizAsync(ct))
            .ToDictionary(m => (m.WorkType, m.Complexity), m => m.HorasLimite);

        var disponibles = (await pool.DisponiblesAsync(tipo, ct: ct))
            .Select(a => AVistaLibre(a, plazoDeLaMatriz))
            .ToList();

        if (currentUser.DeveloperId is not int developerId)
            return new MiPoolDto(TieneFicha: false, TiposDeTrabajo, disponibles, []);

        var mias = await pool.MisDelPoolAsync(developerId, ct);
        var avance = await AvanceDelChecklistAsync(mias.Select(a => a.Id).ToList(), ct);

        return new MiPoolDto(
            TieneFicha: true,
            TiposDeTrabajo,
            disponibles,
            mias.Select(a => AVistaPropia(a, avance.GetValueOrDefault(a.Id))).ToList());
    }

    /// <summary>
    /// El pool completo y la cola de verificación, para la pantalla del líder. Sin guarda propia
    /// porque las dos consultas que usa ya exigen ser líder dentro del servicio; repetirla aquí
    /// daría a entender que la de allá no basta.
    /// </summary>
    public async Task<PoolDelLiderDto> PoolDelLiderAsync(
        PoolActivityStatus? estado = null, PoolWorkType? tipo = null, CancellationToken ct = default)
    {
        var actividades = await pool.TodasAsync(estado, tipo, ct);
        var pendientes = await pool.PendientesDeVerificarAsync(ct);

        // El NOMBRE del equipo al que está publicada cada una. Se resuelve aquí, de una vez, para que
        // la pantalla no tenga que cruzar identificadores contra otra lista suya: son dos consultas
        // distintas y podrían llegar desfasadas.
        var nombreDeEquipo = await db.Teams.AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return new PoolDelLiderDto(
            actividades.Select(a => AVistaDelLider(
                a, a.EquipoId is int e ? nombreDeEquipo.GetValueOrDefault(e) : null)).ToList(),
            pendientes.Select(AVistaPorVerificar).ToList());
    }

    /// <summary>El checklist de una actividad, con lo cumplido y la evidencia de cada punto.</summary>
    public async Task<IReadOnlyList<PuntoDeChecklistDto>> ChecklistAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        var items = await pool.ChecklistDeAsync(poolActivityId, ct);
        return items
            .Select(c => new PuntoDeChecklistDto(c.Id, c.Text, c.IsDone, c.RequiereEvidencia, c.EvidenceUrl))
            .ToList();
    }

    /// <summary>
    /// La matriz y las plantillas de los tres tipos.
    ///
    /// La guarda de líder se pone AQUÍ y no se hereda de nadie: las dos lecturas del servicio no la
    /// llevan porque él mismo las usa por dentro —al validar un borrador y al copiar el checklist de
    /// quien toma una actividad—, y ponérsela allí rompería el ciclo normal del desarrollador.
    /// </summary>
    public async Task<ConfiguracionDelPoolDto> ConfiguracionAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var matriz = (await pool.ObtenerMatrizAsync(ct))
            .Select(m => new CeldaDeMatrizDto(
                m.WorkType, PoolSeed.Etiqueta(m.WorkType),
                m.Complexity, PoolSeed.Etiqueta(m.Complexity))
            {
                Puntos = m.Points,
                HorasLimite = m.HorasLimite
            })
            .ToList();

        // Con los inactivos incluidos: el líder tiene que poder reactivar lo que desactivó, y una
        // lista donde lo desactivado desaparece no deja hacerlo.
        var plantilla = new List<PuntoDePlantillaDto>();
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
            plantilla.AddRange((await pool.PlantillaAsync(tipo, incluirInactivos: true, ct))
                .Select(t => new PuntoDePlantillaDto(t.Id, t.WorkType, t.Text, t.Orden, t.RequiereEvidencia, t.IsActive)));

        return new ConfiguracionDelPoolDto(
            matriz, plantilla, TiposDeTrabajo, Complejidades, Estados);
    }

    // ── Etiquetas de los desplegables ────────────────────────────────────────────
    //
    // Las manda el servidor porque los textos viven en PoolSeed, portado del escritorio, y el
    // cliente no puede referenciar esta capa. Escribirlos otra vez en la pantalla dejaría dos copias
    // que se desincronizarían; son fijos, así que se arman una sola vez.

    private static readonly IReadOnlyList<OpcionDelPoolDto<PoolWorkType>> TiposDeTrabajo =
        Enum.GetValues<PoolWorkType>()
            .Select(t => new OpcionDelPoolDto<PoolWorkType>(t, PoolSeed.Etiqueta(t)))
            .ToList();

    private static readonly IReadOnlyList<OpcionDelPoolDto<PoolComplexity>> Complejidades =
        Enum.GetValues<PoolComplexity>()
            .Select(c => new OpcionDelPoolDto<PoolComplexity>(c, PoolSeed.Etiqueta(c)))
            .ToList();

    private static readonly IReadOnlyList<OpcionDelPoolDto<PoolActivityStatus>> Estados =
        Enum.GetValues<PoolActivityStatus>()
            .Select(e => new OpcionDelPoolDto<PoolActivityStatus>(e, PoolSeed.Etiqueta(e)))
            .ToList();

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cuántos puntos del checklist lleva cumplidos cada actividad. Una sola consulta agrupada para
    /// todas: el «3/5» de una rejilla no vale un viaje a la base por fila.
    /// </summary>
    private async Task<Dictionary<int, (int Hechos, int Total)>> AvanceDelChecklistAsync(
        IReadOnlyList<int> actividades, CancellationToken ct)
    {
        if (actividades.Count == 0) return [];

        var filas = await db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => actividades.Contains(c.PoolActivityId))
            .GroupBy(c => c.PoolActivityId)
            .Select(g => new { Actividad = g.Key, Total = g.Count(), Hechos = g.Count(c => c.IsDone) })
            .ToListAsync(ct);

        return filas.ToDictionary(f => f.Actividad, f => (f.Hechos, f.Total));
    }

    /// <summary>
    /// Lo que ve quien todavía no la ha tomado. Lleva la urgencia, el plazo y los criterios extra
    /// porque son exactamente los datos con los que se decide si tomarla: enterarse después de que
    /// «además había que documentarla» convertiría el extra en una trampa.
    ///
    /// <para>El plazo viaja dos veces y no es redundancia: el crudo (nulo = «el de la matriz») y el
    /// EFECTIVO, ya resuelto con la misma precedencia que aplicará <c>TomarAsync</c> —el de la
    /// actividad manda; si no hay, el de su celda—. El segundo es el que se enseña, y por eso se
    /// resuelve aquí y no en la pantalla: si la regla se escribiera también allá, el día que cambie
    /// habría dos sitios que corregir y uno se quedaría atrás.</para>
    /// </summary>
    private static ActividadLibreDto AVistaLibre(
        PoolActivity a, IReadOnlyDictionary<(PoolWorkType, PoolComplexity), decimal> plazoDeLaMatriz) => new(
        a.Id, a.Title, a.Description,
        a.WorkType, PoolSeed.Etiqueta(a.WorkType),
        a.Complexity, PoolSeed.Etiqueta(a.Complexity),
        a.Points, a.ExternalUrl,
        a.Priority, EtiquetasDeCatalogo.PrioridadDelPool(a.Priority),
        a.HorasLimite,
        a.HorasLimite ?? (plazoDeLaMatriz.TryGetValue((a.WorkType, a.Complexity), out var deLaMatriz)
            ? deLaMatriz
            : 0m),
        a.Points + a.ExtraCriteria.Sum(c => c.Points),
        [.. a.ExtraCriteria
              .OrderByDescending(c => c.Points).ThenBy(c => c.Name)
              .Select(c => new CriterioExtraDto(c.Id, c.ScoringCriterionId, c.Name, c.Points, c.IsMet, c.Comment))]);

    private static MiActividadDelPoolDto AVistaPropia(PoolActivity a, (int Hechos, int Total) avance) => new(
        a.Id, a.Title, a.Description,
        PoolSeed.Etiqueta(a.WorkType),
        a.Points,
        a.Status, PoolSeed.Etiqueta(a.Status),
        a.ClaimDeadlineAt, a.Vencida, a.EnCurso,
        // Lo que se prometió, para poder verlo junto al avance: es contra este número contra el que
        // se va a contrastar el cronómetro, y quien la trabaja debería tenerlo delante.
        a.HorasEstimadas,
        avance.Hechos, avance.Total,
        // El motivo solo acompaña a una devolución: en cualquier otro estado es el comentario de una
        // vuelta anterior ya resuelta, y enseñarlo haría creer que sigue habiendo algo que corregir.
        a.Status == PoolActivityStatus.Devuelta ? a.ReviewComment : null,
        a.ExternalUrl);

    private static ActividadDelPoolDto AVistaDelLider(PoolActivity a, string? equipo = null) => new(
        a.Id, a.Title, a.Description,
        a.WorkType, PoolSeed.Etiqueta(a.WorkType),
        a.Complexity, PoolSeed.Etiqueta(a.Complexity),
        a.Points,
        a.Status, PoolSeed.Etiqueta(a.Status),
        a.ClaimedBy?.FullName,
        a.ClaimDeadlineAt, a.Vencida,
        a.ReturnedCount,
        a.ExternalUrl,
        a.Priority, EtiquetasDeCatalogo.PrioridadDelPool(a.Priority),
        // Los dos números de horas, y son cosas distintas: el PLAZO (cuándo se espera entregada) y el
        // ESFUERZO (cuánto trabajo se cree que cuesta). Quién escribió el segundo se deduce del tipo
        // que va unas líneas más arriba: en un bug es de quien la tomó, en lo demás es del líder.
        a.HorasLimite,
        a.HorasEstimadas,
        // El máximo alcanzable, para que se vea de un vistazo cuánto está realmente en juego. Suma
        // TODOS los extra, cumplidos o no: mientras nadie los haya evaluado, todos siguen en juego.
        a.Points + a.ExtraCriteria.Sum(c => c.Points),
        a.ExtraCriteria.Count,
        a.EquipoId, equipo);

    private static EntregaPorVerificarDto AVistaPorVerificar(PoolActivity a) => new(
        a.Id, a.Title,
        PoolSeed.Etiqueta(a.WorkType), PoolSeed.Etiqueta(a.Complexity),
        a.Points,
        a.ClaimedBy?.FullName ?? "(sin ficha)",
        a.DeliveredAt,
        a.ReviewRound + 1,
        UltimaLineaDelHistorial(a.ReviewHistory),
        a.ExternalUrl);

    /// <summary>
    /// La última anotación del ida y vuelta. Solo la última, igual que en el escritorio: el hilo
    /// completo puede tener miles de caracteres y lo que hace falta para decidir es lo que se dijo
    /// la vez pasada.
    /// </summary>
    private static string? UltimaLineaDelHistorial(string? historial) =>
        string.IsNullOrWhiteSpace(historial) ? null : historial.Split('\n')[^1];

    // ── Criterios extra ──────────────────────────────────────────────────────────

    /// <summary>
    /// El catálogo de criterios que el líder puede pedir como extra al publicar una actividad.
    ///
    /// <para>Solo los INDIVIDUALES y ACTIVOS. Los de equipo quedan fuera desde la lista y no solo
    /// rechazados al guardar: una actividad del pool la cobra una sola persona, así que ofrecer un
    /// reconocimiento colectivo aquí sería invitar a un error que luego hay que explicar.</para>
    ///
    /// <para>Los de 0 puntos también quedan fuera: un criterio extra que no suma nada no es un
    /// extra, es una casilla que hace perder el tiempo a quien revisa.</para>
    /// </summary>
    public async Task<IReadOnlyList<CriterioDisponibleDto>> CriteriosExtraDisponiblesAsync(
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        return await db.ScoringCriteria.AsNoTracking()
            .Where(c => c.IsActive && c.Scope == CriterionScope.Individual && c.DefaultPoints > 0)
            .OrderByDescending(c => c.DefaultPoints).ThenBy(c => c.Name)
            .Select(c => new CriterioDisponibleDto(c.Id, c.Name, c.Description, c.DefaultPoints))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Los criterios extra de una actividad y en qué quedó cada uno.
    ///
    /// Lo consultan las DOS pantallas —quien la trabaja, para saber qué se le va a mirar; el líder,
    /// para evaluarlo— y por eso no es SoloAdmin. Quien la trabaja tiene que poder ver lo que se le
    /// pide antes de decidir si la toma: esconderlo convertiría el extra en una sorpresa.
    /// </summary>
    public async Task<IReadOnlyList<CriterioExtraDto>> CriteriosExtraDeAsync(
        int actividadId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        return await db.PoolActivityExtraCriteria.AsNoTracking()
            .Where(c => c.PoolActivityId == actividadId)
            .OrderByDescending(c => c.Points).ThenBy(c => c.Name)
            .Select(c => new CriterioExtraDto(
                c.Id, c.ScoringCriterionId, c.Name, c.Points, c.IsMet, c.Comment))
            .ToListAsync(ct);
    }
}
