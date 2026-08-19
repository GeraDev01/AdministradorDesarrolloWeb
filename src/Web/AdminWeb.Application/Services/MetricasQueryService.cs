using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Metricas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Consultas de SOLO LECTURA de las dos pantallas de planeación del líder: «Métricas» (antigüedad y
/// ciclo de vida de los requerimientos, carga y atrasos por persona) y «Estimación y capacidad»
/// (precisión de las estimaciones y a quién se le puede asignar lo siguiente).
///
/// No inventa aritmética: la precisión sale entera de <see cref="EstimationStats"/> y el semáforo de
/// <see cref="CapacityStats"/>, que son los cálculos puros portados del escritorio. Lo que aporta
/// este servicio es reunir los datos que aquellos necesitan —que en el escritorio eran consultas
/// locales gratis y aquí son viajes a la base— y RECORTARLOS al contrato web: ningún tipo de EF
/// cruza al navegador.
///
/// <para>Las dos pantallas son del líder. La política del endpoint lo exige y la guarda de aquí lo
/// vuelve a exigir: son datos de evaluación de terceros —quién va atrasado, quién está sobrecargado—
/// y a la API se puede llegar sin pasar por el cliente.</para>
/// </summary>
public class MetricasQueryService(AppDbContext db, ICurrentUser actual)
{
    /// <summary>Ventana por omisión para contar vacaciones, la misma con la que abría el escritorio.</summary>
    public const int DiasDeVentanaPorDefecto = 30;

    /// <summary>Tope de la ventana. Es el mismo máximo del control del escritorio.</summary>
    public const int MaxDiasDeVentana = 365;

    // ── Métricas de ciclo de vida ────────────────────────────────────────────────

    /// <summary>
    /// Antigüedad y ciclo de vida de cada requerimiento, más la carga y los atrasos por persona.
    ///
    /// Todo se deriva de fechas que ya existen; no hay ningún campo que alguien tenga que mantener
    /// al día, que es lo que hacía útil esta pantalla en el escritorio.
    /// </summary>
    public async Task<MetricasDto> MetricasAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var hoy = DateTime.Today;

        // Se proyecta en vez de traer la entidad: sin esto entrarían en el SELECT las descripciones
        // completas de todos los requerimientos para acabar mandando un título y unas cuantas fechas.
        var reqs = (await db.Requirements.AsNoTracking()
            .Select(r => new
            {
                r.Id, r.Title, r.Status, r.CreatedAt, r.StatusChangedAt,
                r.CommittedDeliveryDate, r.ActualDeliveryDate, r.ProgressPercent,
                Integrantes = r.Assignments.Select(a => a.Developer.FullName).ToList(),
                IntegrantesIds = r.Assignments.Select(a => a.DeveloperId).ToList()
            })
            .ToListAsync(ct))
            .Select(r => new DatosDeRequerimiento(
                r.Id, r.Title, r.Status, r.CreatedAt, r.StatusChangedAt,
                r.CommittedDeliveryDate, r.ActualDeliveryDate, r.ProgressPercent,
                r.Integrantes, r.IntegrantesIds))
            .ToList();

        var filas = reqs.Select(r =>
        {
            bool cerrado = r.Estado is RequirementStatus.Entregado or RequirementStatus.Cancelado;

            // La edad se mide contra la ENTREGA cuando ya la hubo: si se midiera contra hoy, un
            // requerimiento entregado hace un año seguiría envejeciendo en el reporte.
            var referencia = r.EntregaReal ?? hoy;
            int diasVivo = Math.Max(0, (int)(referencia.Date - r.CreadoUtc.Date).TotalDays);
            int diasEnEstado = Math.Max(0, (int)(hoy - (r.EstadoCambiadoUtc ?? r.CreadoUtc).Date).TotalDays);
            int? paraCompromiso = r.Compromiso.HasValue
                ? (int)(r.Compromiso.Value.Date - hoy).TotalDays
                : null;
            bool atrasado = r.Compromiso < hoy && !cerrado;

            return new MetricaDeRequerimientoDto(
                r.Id, r.Titulo, EstadoDeRequerimiento(r.Estado), diasVivo, diasEnEstado,
                r.Compromiso?.ToString("dd/MM/yyyy") ?? "—",
                cerrado ? null : paraCompromiso,
                atrasado,
                string.Join(", ", r.Integrantes),
                r.Avance);
        })
        .OrderByDescending(m => m.Atrasado).ThenByDescending(m => m.DiasVivo)
        .ToList();

        var devs = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName })
            .ToListAsync(ct);

        var porDesarrollador = devs.Select(dev =>
        {
            var mios = reqs
                .Where(r => r.IntegrantesIds.Contains(dev.Id)
                         && r.Estado is not (RequirementStatus.Entregado or RequirementStatus.Cancelado))
                .ToList();

            return new MetricaPorDesarrolladorDto(
                dev.FullName,
                Activos: mios.Count,
                Atrasados: mios.Count(r => r.Compromiso < hoy),
                EdadPromedio: mios.Count == 0 ? 0 : (int)mios.Average(r => (hoy.Date - r.CreadoUtc.Date).TotalDays),
                MasAntiguo: mios.Count == 0 ? 0 : mios.Max(r => (int)(hoy.Date - r.CreadoUtc.Date).TotalDays));
        })
        .OrderByDescending(m => m.Atrasados).ThenByDescending(m => m.Activos)
        .ToList();

        return new MetricasDto(IndicadoresDeCicloDeVida(reqs, hoy), filas, porDesarrollador);
    }

    /// <summary>
    /// Lo que hay que traer de cada requerimiento para medirlo. Es un tipo con nombre y no un
    /// anónimo porque lo consumen dos métodos: el que arma las filas y el que arma las tarjetas.
    /// </summary>
    private sealed record DatosDeRequerimiento(
        int Id, string Titulo, RequirementStatus Estado, DateTime CreadoUtc, DateTime? EstadoCambiadoUtc,
        DateTime? Compromiso, DateTime? EntregaReal, int Avance,
        List<string> Integrantes, List<int> IntegrantesIds);

    /// <summary>
    /// Las cuatro tarjetas de arriba, tal como las armaba <c>MetricsControl.BuildKpis</c>.
    ///
    /// <para><b>Un cambio respecto al escritorio.</b> Allí, sin ninguna entrega comprometida todavía,
    /// el porcentaje de cumplimiento se enseñaba como «0 %», que se lee como «cumplió el cero por
    /// ciento» cuando lo cierto es que no hay nada que medir. Aquí se enseña «—», que es la misma
    /// decisión —y por el mismo motivo— que ya se tomó en la pantalla de Cumplimiento de SLA de esta
    /// aplicación. La alternativa era conservar el 0 %, pero entonces dos pantallas de la MISMA
    /// aplicación presentarían la misma medida de forma contradictoria.</para>
    /// </summary>
    private static List<IndicadorDto> IndicadoresDeCicloDeVida(List<DatosDeRequerimiento> reqs, DateTime hoy)
    {
        var abiertos = reqs
            .Where(r => r.Estado is not (RequirementStatus.Entregado or RequirementStatus.Cancelado))
            .ToList();
        int atrasados = abiertos.Count(r => r.Compromiso < hoy);

        // Solo lo entregado QUE TENÍA COMPROMISO: sin fecha contra la que medir, incluirlo diría que
        // se cumplió algo que nunca se prometió.
        var entregadosConCompromiso = reqs
            .Where(r => r.Estado == RequirementStatus.Entregado && r.EntregaReal.HasValue && r.Compromiso.HasValue)
            .ToList();
        int aTiempo = entregadosConCompromiso.Count(r => r.EntregaReal!.Value.Date <= r.Compromiso!.Value.Date);
        int porcentaje = entregadosConCompromiso.Count == 0
            ? 0
            : (int)Math.Round(100.0 * aTiempo / entregadosConCompromiso.Count);

        int edadPromedio = abiertos.Count == 0
            ? 0
            : (int)abiertos.Average(r => (hoy.Date - r.CreadoUtc.Date).TotalDays);

        return
        [
            new IndicadorDto("Activos", abiertos.Count.ToString(), TonoDeIndicador.Neutro,
                "Requerimientos que no están entregados ni cancelados."),
            new IndicadorDto("Atrasados", atrasados.ToString(),
                atrasados > 0 ? TonoDeIndicador.Peligro : TonoDeIndicador.Exito,
                "Abiertos cuya fecha comprometida ya pasó."),
            new IndicadorDto("Entregados a tiempo",
                entregadosConCompromiso.Count == 0 ? "—" : $"{porcentaje}%",
                entregadosConCompromiso.Count == 0 ? TonoDeIndicador.Neutro
                    : porcentaje >= 70 ? TonoDeIndicador.Exito : TonoDeIndicador.Aviso,
                "De lo entregado que tenía fecha comprometida, cuánto llegó dentro del plazo."),
            new IndicadorDto("Edad prom. activos", $"{edadPromedio} d", TonoDeIndicador.Neutro,
                "Días promedio desde que se dieron de alta los requerimientos abiertos."),
        ];
    }

    // ── Estimación y capacidad ───────────────────────────────────────────────────

    /// <summary>
    /// Precisión de las estimaciones y capacidad del equipo, las dos pestañas del reporte de
    /// planeación del escritorio.
    /// </summary>
    /// <param name="dias">
    /// Ventana en la que se cuentan las vacaciones, HOY INCLUIDO. Se acota aquí y no se confía en el
    /// cliente: a la API se llega sin pasar por él, y un 100000 haría recorrer un siglo de fechas.
    /// </param>
    public async Task<EstimacionYCapacidadDto> EstimacionYCapacidadAsync(
        int dias = DiasDeVentanaPorDefecto, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        dias = Math.Clamp(dias, 1, MaxDiasDeVentana);
        var hoy = DateTime.Today;
        // Rango inclusivo de EXACTAMENTE `dias` días (hoy incluido): con AddDays(dias) serían dias+1.
        var fin = hoy.AddDays(dias - 1);
        var ahora = DateTime.UtcNow;

        // Las sesiones se materializan porque el tiempo en curso lo calcula la propia entidad
        // (WorkSession.LiveSeconds) y esa cuenta no se traduce a SQL. Repetir aquí su fórmula sería
        // tener dos versiones del mismo número esperando a discrepar.
        var sesiones = await db.WorkSessions.AsNoTracking().ToListAsync(ct);

        var segundosPorRequerimiento = sesiones
            .Where(w => w.RequirementId != null)
            .GroupBy(w => w.RequirementId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)));

        var segundosPorDesarrollador = sesiones
            .GroupBy(w => w.DeveloperId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)));

        // ── Estimación vs real ───────────────────────────────────────────────────
        var conEstimacion = await db.Requirements.AsNoTracking()
            .Where(r => r.EstimateHours != null && r.EstimateHours > 0)
            .Select(r => new { r.Id, r.Title, r.EstimateHours, r.Status })
            .ToListAsync(ct);

        var estimacion = EstimationStats.Filas(conEstimacion.Select(r => new EstimationInput(
                r.Id, r.Title, r.EstimateHours,
                segundosPorRequerimiento.GetValueOrDefault(r.Id), r.Status)))
            .OrderByDescending(f => f.DeltaHrs)
            .ToList();

        var resumen = EstimationStats.Resumen(estimacion);

        var filasDeEstimacion = estimacion.Select(f => new FilaDeEstimacionDto(
            f.ReqId, f.Title, EstadoDeRequerimiento(f.Estado),
            Math.Round(f.EstimateHrs, 1), Math.Round(f.ActualHrs, 1), f.DeltaHrs, f.Ratio,
            EstimationStats.EtiquetaClase(f.Clase), TonoDeClase(f.Clase)))
            .ToList();

        // ── Capacidad del equipo ─────────────────────────────────────────────────
        var estimadasPorRequerimiento = await db.Requirements.AsNoTracking()
            .Where(r => r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
            .Select(r => new { r.Id, r.EstimateHours })
            .ToListAsync(ct);
        var abiertos = estimadasPorRequerimiento.ToDictionary(r => r.Id, r => (double)(r.EstimateHours ?? 0));

        var asignaciones = await db.Assignments.AsNoTracking()
            .Select(a => new { a.DeveloperId, a.RequirementId })
            .ToListAsync(ct);

        // EL POOL TAMBIÉN ES CARGA, y hasta ahora no contaba.
        //
        // La carga se medía SOLO con asignaciones sobre requerimientos, y un equipo que trabaja por
        // el pool salía entero con cero horas pendientes: cuatro tarjetas diciendo «Libres = N», la
        // gráfica plana y una rejilla de ceros. Esa pantalla contesta «¿a quién le doy lo
        // siguiente?», así que contestarla ignorando la mitad del trabajo real no es que enseñe de
        // menos: es que enseña lo contrario.
        //
        // Que era un fallo y no «es que no hay trabajo» se veía en la MISMA fila: «Horas
        // registradas» suma todas las sesiones de cronómetro de la persona, y el tiempo del pool sí
        // entra ahí —al tomar una actividad, el pool le cuelga una actividad libre que es lo que el
        // cronómetro mide—. Se leían filas con «82 h registradas · 0 h pendientes · Libre».
        //
        // Los estados son los mismos dos que <c>PoolActivity.EnCurso</c> —tomada y devuelta para
        // corregir—, que es LA definición de «está en manos de alguien». No se reutiliza esa
        // propiedad porque es calculada y no se traduce a SQL; se escriben aquí y se dice de dónde
        // salen, que es lo que evita que las dos listas se separen sin que nadie lo note.
        var delPool = await db.PoolActivities.AsNoTracking()
            .Where(a => a.ClaimedByDeveloperId != null
                     && (a.Status == PoolActivityStatus.Tomada || a.Status == PoolActivityStatus.Devuelta))
            .Select(a => new { DeveloperId = a.ClaimedByDeveloperId!.Value, a.HorasEstimadas })
            .ToListAsync(ct);

        var vacaciones = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Status == VacationStatus.Aprobada)
            .Select(v => new { v.DeveloperId, v.StartDate, v.EndDate })
            .ToListAsync(ct);

        var activos = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName })
            .ToListAsync(ct);

        var capacidad = new List<CapacityRow>(activos.Count);
        foreach (var d in activos)
        {
            var mios = asignaciones.Where(a => a.DeveloperId == d.Id)
                .Select(a => a.RequirementId).Distinct()
                .Where(abiertos.ContainsKey)
                .ToList();

            // Un bug del pool que nadie ha estimado todavía cuenta como actividad pero suma cero
            // horas: no hay número que sumar, y meterle uno inventado falsearía la única cifra con
            // la que se decide.
            var delPoolMias = delPool.Where(a => a.DeveloperId == d.Id).ToList();
            double horasDelPool = delPoolMias.Sum(a => (double)(a.HorasEstimadas ?? 0));

            double horasPendientes = mios.Sum(id => abiertos[id]) + horasDelPool;
            double horasRegistradas = EstimationStats.SegundosAHoras(
                segundosPorDesarrollador.GetValueOrDefault(d.Id));

            var mias = vacaciones.Where(v => v.DeveloperId == d.Id).ToList();
            int diasDeVacaciones = mias.Sum(v => CapacityStats.DiasVacacionEnRango(v.StartDate, v.EndDate, hoy, fin));
            bool deVacacionesHoy = mias.Any(v => CapacityStats.EnVacacion(v.StartDate, v.EndDate, hoy));

            capacidad.Add(new CapacityRow(
                d.FullName, mios.Count,
                Math.Round(horasPendientes, 1), Math.Round(horasRegistradas, 1),
                diasDeVacaciones,
                // «Libre» es no tener NADA entre manos, así que las dos cosas cuentan: quien solo
                // tiene actividades del pool ya no sale libre.
                CapacityStats.Clasificar(deVacacionesHoy, mios.Count + delPoolMias.Count,
                    horasPendientes, CapacityStats.CapacidadPorDefecto),
                delPoolMias.Count));
        }

        var filasDeCapacidad = capacidad
            .OrderByDescending(x => x.Estado == Disponibilidad.Sobrecargado).ThenBy(x => x.Developer)
            .Select(x => new FilaDeCapacidadDto(
                x.Developer, x.Abiertos, x.HorasPendientes, x.HorasRegistradas, x.DiasVacaciones,
                CapacityStats.EtiquetaEstado(x.Estado), TonoDeDisponibilidad(x.Estado), x.PoolTomadas))
            .ToList();

        return new EstimacionYCapacidadDto(
            DiasDeVentana: dias,
            CapacidadHoras: CapacityStats.CapacidadPorDefecto,
            IndicadoresDeEstimacion: IndicadoresDeEstimacion(resumen),
            Estimacion: filasDeEstimacion,
            ResumenDeEstimacion: filasDeEstimacion.Count == 0
                ? "No hay requerimientos con horas estimadas."
                : $"{filasDeEstimacion.Count} requerimiento(s) con estimación · {resumen.ConDatos} ya con tiempo medido.",
            IndicadoresDeCapacidad: IndicadoresDeCapacidad(capacidad, CapacityStats.CapacidadPorDefecto),
            Capacidad: filasDeCapacidad,
            // La capacidad se DICE aquí, y no solo en las tarjetas, porque ésta es la única línea de
            // la pestaña que se pinta siempre: sin filas no hay rejilla, sin filas no hay gráfica, y
            // las tarjetas se leen de un vistazo pero no explican de dónde sale el número. Que la
            // capacidad pudiera «no aparecer» es exactamente lo que se vino a arreglar.
            ResumenDeCapacidad:
                $"{capacidad.Count} desarrollador(es) activo(s) · capacidad de trabajo " +
                $"{CapacityStats.CapacidadPorDefecto:0} h por persona · vacaciones contadas en los " +
                $"próximos {dias} días.");
    }

    /// <summary>Las seis tarjetas de la pestaña de estimación, con los mismos umbrales del escritorio.</summary>
    private static List<IndicadorDto> IndicadoresDeEstimacion(EstimationSummary r) =>
    [
        new IndicadorDto("Ratio promedio",
            r.ConDatos == 0 ? "—" : $"{r.RatioPromedio:0.00}×",
            r.ConDatos == 0 ? TonoDeIndicador.Neutro
                : r.RatioPromedio > 1.2 ? TonoDeIndicador.Peligro
                : r.RatioPromedio < 0.8 ? TonoDeIndicador.Aviso
                : TonoDeIndicador.Exito,
            "Horas reales entre horas estimadas. Por encima de 1 se tarda más de lo que se dice."),
        new IndicadorDto("✓ Precisos", r.Precisos.ToString(), TonoDeIndicador.Exito,
            "Dentro de ±20 % de lo estimado."),
        new IndicadorDto("▲ Subestimados", r.Subestimados.ToString(), TonoDeIndicador.Peligro,
            "Costaron bastante más de lo estimado."),
        new IndicadorDto("▼ Sobreestimados", r.Sobreestimados.ToString(), TonoDeIndicador.Aviso,
            "Costaron bastante menos de lo estimado."),
        new IndicadorDto("Horas estimadas", $"{r.HorasEstimadas:0.#}", TonoDeIndicador.Neutro, null),
        new IndicadorDto("Horas reales", $"{r.HorasReales:0.#}", TonoDeIndicador.Neutro, null),
    ];

    /// <summary>
    /// Las tarjetas de la pestaña de capacidad: primero CUÁNTO CABE y después el semáforo de cómo va
    /// el equipo contra eso.
    ///
    /// <para>Las dos primeras son nuevas y son la respuesta a una queja concreta: «la capacidad de
    /// trabajo del equipo debe ser de 40 horas por persona; no aparece nada». No aparecía: el número
    /// viajaba en el DTO pero la pantalla lo pintaba en un solo sitio —letra chica, dentro de la
    /// segunda pestaña, y redactado como umbral de sobrecarga en vez de como capacidad—. Un semáforo
    /// que dice «sobrecargado» sin decir contra qué no se puede ni discutir.</para>
    ///
    /// <para><b>La del EQUIPO no cuenta a quien hoy está de vacaciones</b>, y eso no es un detalle:
    /// la regla de esta pantalla es que las vacaciones ganan a todo lo demás —da igual lo que tenga
    /// abierto quien hoy no está—, así que sumarle sus cuarenta horas a la capacidad del equipo
    /// prometería un trabajo que nadie va a hacer. La explicación dice por cuántas personas se
    /// multiplica, para que el número se pueda comprobar.</para>
    /// </summary>
    private static List<IndicadorDto> IndicadoresDeCapacidad(List<CapacityRow> filas, double capacidadHoras)
    {
        int disponibles = filas.Count(x => x.Estado != Disponibilidad.DeVacaciones);

        return
        [
            new IndicadorDto("Capacidad por persona", $"{capacidadHoras:0} h", TonoDeIndicador.Neutro,
                "Horas de trabajo que se consideran una carga completa para una persona. " +
                "Por encima de eso se marca «Sobrecargado»."),
            new IndicadorDto("Capacidad del equipo", $"{capacidadHoras * disponibles:0} h",
                TonoDeIndicador.Neutro,
                $"{disponibles} persona(s) disponible(s) × {capacidadHoras:0} h. " +
                "No cuenta a quien está de vacaciones hoy."),
            new IndicadorDto("🟢 Libres", filas.Count(x => x.Estado == Disponibilidad.Libre).ToString(),
                TonoDeIndicador.Exito, "Sin nada abierto: ni requerimientos ni actividades del pool."),
            new IndicadorDto("🟡 Ocupados", filas.Count(x => x.Estado == Disponibilidad.Ocupado).ToString(),
                TonoDeIndicador.Aviso, "Con trabajo, dentro de su capacidad."),
            new IndicadorDto("🔴 Sobrecargados", filas.Count(x => x.Estado == Disponibilidad.Sobrecargado).ToString(),
                TonoDeIndicador.Peligro,
                $"Con más de {capacidadHoras:0} horas estimadas pendientes."),
            new IndicadorDto("🏖 De vacaciones", filas.Count(x => x.Estado == Disponibilidad.DeVacaciones).ToString(),
                TonoDeIndicador.Neutro, "De vacaciones aprobadas HOY."),
        ];
    }

    private static TonoDeIndicador TonoDeClase(EstimationClass c) => c switch
    {
        EstimationClass.Preciso => TonoDeIndicador.Exito,
        EstimationClass.Subestimado => TonoDeIndicador.Peligro,
        EstimationClass.Sobreestimado => TonoDeIndicador.Aviso,
        _ => TonoDeIndicador.Neutro
    };

    private static TonoDeIndicador TonoDeDisponibilidad(Disponibilidad d) => d switch
    {
        Disponibilidad.Libre => TonoDeIndicador.Exito,
        Disponibilidad.Ocupado => TonoDeIndicador.Aviso,
        Disponibilidad.Sobrecargado => TonoDeIndicador.Peligro,
        _ => TonoDeIndicador.Neutro
    };

    /// <summary>
    /// El estado en palabras. Se escribe aquí, en el servidor, y no en el navegador: mientras el
    /// escritorio siga en producción, las dos aplicaciones tienen que llamar «En desarrollo» a lo
    /// mismo.
    /// </summary>
    private static string EstadoDeRequerimiento(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "Por estimar",
        RequirementStatus.Estimado     => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas    => "En pruebas",
        RequirementStatus.PorEntregar  => "Por entregar",
        RequirementStatus.Entregado    => "Entregado",
        RequirementStatus.Cancelado    => "Cancelado",
        _                              => s.ToString()
    };
}
