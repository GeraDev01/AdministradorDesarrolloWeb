using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Trabajo;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma de una sola vez lo que enseña cada pantalla del trabajo —requerimientos, sprint y mis
/// asignaciones— y lo traduce a contratos propios.
///
/// Existe por dos motivos, ninguno de negocio: las reglas siguen enteras en
/// <see cref="RequirementService"/> y <see cref="SprintService"/>, que son quienes deciden.
///
/// El primero es que ningún tipo de EF puede cruzar al navegador. Un <see cref="Requirement"/>
/// arrastra su sello de concurrencia, sus asignaciones con la ficha completa de cada desarrollador y
/// los segundos ya reportados a Azure DevOps; nada de eso pinta una pantalla y todo eso viajaría en
/// cada respuesta.
///
/// El segundo es el número de viajes. En el escritorio, «Mis asignaciones» llamaba a
/// <c>GetTotalSeconds</c> una vez POR FILA para escribir el tiempo dedicado, y contra una base local
/// eso era gratis; aquí sería una consulta por requerimiento y por refresco. Aquí se suma agrupado,
/// de una sola vez.
/// </summary>
public class TrabajoQueryService(
    AppDbContext db,
    ICurrentUser currentUser,
    RequirementService requerimientos,
    RequirementAttachmentService adjuntos,
    SprintService sprints,
    JornadaQueryService jornada)
{
    // ── Pantalla de requerimientos (líder) ───────────────────────────────────────

    /// <summary>
    /// Los requerimientos que cumplen el filtro y los desarrolladores con los que se filtra y se
    /// asigna. La guarda la pone <see cref="RequirementService"/> en las dos consultas que se usan
    /// aquí; repetirla daría a entender que las de allá no bastan.
    /// </summary>
    public async Task<RequerimientosDto> RequerimientosAsync(
        RequirementStatus? estado = null, int? developerId = null, string? busqueda = null,
        CancellationToken ct = default)
    {
        var filas = await requerimientos.ListarAsync(estado, developerId, busqueda, ct);
        var asignables = await requerimientos.DesarrolladoresAsignablesAsync(ct);

        // El nombre de CUALQUIER desarrollador asignado, no solo de los activos: un requerimiento
        // asignado a alguien que ya no está debe seguir diciendo a quién, y no un hueco.
        var nombres = await db.Developers.AsNoTracking()
            .Select(d => new { d.Id, d.FullName })
            .ToDictionaryAsync(d => d.Id, d => d.FullName, ct);

        // Cuántos documentos cuelga cada uno, en UNA consulta agrupada. El escritorio la hacía igual
        // (CountsByRequirement) y aquí importa más: una subconsulta por fila serían tantos viajes de
        // red como requerimientos enseñe el filtro.
        var conteoDeAdjuntos = await adjuntos.ConteoPorRequerimientoAsync(filas.Select(r => r.Id), ct);

        var hoy = DateTime.Today;
        return new RequerimientosDto(
            filas.Select(r => AVista(r, nombres, hoy, conteoDeAdjuntos.GetValueOrDefault(r.Id))).ToList(),
            asignables.Select(d => new OpcionDto(d.Id, d.FullName)).ToList(),
            ArchivosSubidos.MaxBytes);
    }

    private static RequerimientoDto AVista(
        Requirement r, IReadOnlyDictionary<int, string> nombres, DateTime hoy, int adjuntos)
    {
        var ids = r.Assignments.Select(a => a.DeveloperId).ToList();
        return new RequerimientoDto(
            r.Id,
            r.Title,
            r.Description,
            r.Status, EtiquetasDeTrabajo.Estado(r.Status),
            r.Priority, EtiquetasDeTrabajo.Prioridad(r.Priority),
            ids,
            string.Join(", ", ids.Select(id => nombres.GetValueOrDefault(id, $"Desarrollador #{id}"))),
            r.EstimateHours,
            r.RequestDate,
            r.CommittedDeliveryDate,
            r.ActualDeliveryDate,
            r.ProgressPercent,
            EtiquetasDeTrabajo.Origen(r.Source),
            r.Source == RequirementSource.AzureDevOps,
            r.ExternalUrl,
            EstaVencido(r.Status, r.CommittedDeliveryDate, hoy),
            RequirementService.SelloDe(r),
            adjuntos);
    }

    /// <summary>
    /// Compromiso pasado y sin entregar, con la misma regla del escritorio: entregado y cancelado no
    /// se marcan nunca, porque ya no hay nada que llegue tarde.
    ///
    /// Lo decide el SERVIDOR con su fecha. Dejárselo al navegador pintaría de rojo lo que no lo está
    /// en cuanto un equipo tuviera el reloj corrido, y ese rojo es el que dispara una conversación.
    /// </summary>
    private static bool EstaVencido(RequirementStatus estado, DateTime? compromiso, DateTime hoy) =>
        compromiso is { } fecha
        && fecha.Date < hoy
        && estado != RequirementStatus.Entregado
        && estado != RequirementStatus.Cancelado;

    // ── Pantalla de sprint ───────────────────────────────────────────────────────

    /// <summary>Los sprints del desplegable, el más reciente primero.</summary>
    public async Task<IReadOnlyList<SprintDto>> SprintsAsync(CancellationToken ct = default)
    {
        var hoy = DateTime.Today;
        return (await sprints.ListarAsync(ct))
            .Select(s => new SprintDto(s.Id, s.Name, s.Goal, s.StartDate, s.EndDate,
                EnCurso: s.StartDate.Date <= hoy && hoy <= s.EndDate.Date))
            .ToList();
    }

    /// <summary>
    /// El seguimiento de un sprint: cabecera, avance y lo comprometido. Null si el sprint ya no
    /// existe (alguien pudo borrarlo mientras esta pantalla estaba abierta).
    ///
    /// El avance se calcula con <see cref="SprintService.CalcularAvance"/> sobre los requerimientos
    /// que ya se trajeron, y no llamando a <c>AvanceAsync</c>: ese los volvería a consultar para
    /// obtener exactamente la misma lista. Es la misma composición que hacía el control del
    /// escritorio.
    /// </summary>
    public async Task<SeguimientoDeSprintDto?> SeguimientoAsync(int sprintId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, "de consulta del sprint");

        var s = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sprintId, ct);
        if (s == null) return null;

        var reqs = await sprints.RequerimientosAsync(sprintId, ct);
        var mios = await sprints.MisRequerimientosAsync(sprintId, ct);

        // Sobre TODO el sprint, siempre. El filtro «solo los míos» de la pantalla esconde filas y no
        // toca este número: dos porcentajes distintos para el mismo sprint serían dos verdades.
        var avance = SprintService.CalcularAvance(s, reqs, DateTime.Today);
        var hoy = DateTime.Today;

        return new SeguimientoDeSprintDto(
            new SprintDto(s.Id, s.Name, s.Goal, s.StartDate, s.EndDate,
                EnCurso: s.StartDate.Date <= hoy && hoy <= s.EndDate.Date),
            new AvanceDeSprintDto(
                avance.TotalRequerimientos, avance.Entregados, avance.EnCurso, avance.SinEmpezar,
                avance.Cancelados, avance.AvanceRealPct, avance.TiempoPct,
                avance.DiasTotales, avance.DiasTranscurridos, avance.DiasRestantes, avance.Veredicto),
            reqs.Select(r => new RequerimientoDeSprintDto(
                    r.Id,
                    r.Title,
                    r.Status, EtiquetasDeTrabajo.Estado(r.Status),
                    // Entregado ES cien, aunque su porcentaje se haya quedado a medio capturar: es la
                    // misma regla con la que el servicio calcula el avance, y la columna no puede
                    // contradecir al KPI que tiene encima.
                    r.Status == RequirementStatus.Entregado ? 100 : Math.Clamp(r.ProgressPercent, 0, 100),
                    r.CommittedDeliveryDate,
                    r.ActualDeliveryDate,
                    r.EstimateHours,
                    Mio: mios.Contains(r.Id),
                    CompromisoVencido: EstaVencido(r.Status, r.CommittedDeliveryDate, hoy)))
                .ToList(),
            Mios: mios.Count);
    }

    /// <summary>
    /// El histórico y la velocidad. Solo del líder: la guarda vive en
    /// <see cref="SprintService.HistoricoAsync"/>, que es de donde salen los datos.
    /// </summary>
    public async Task<HistoricoDeSprintsDto> HistoricoAsync(CancellationToken ct = default)
    {
        var historico = await sprints.HistoricoAsync(ct);
        var (velocidad, contados) = SprintService.Velocidad(historico);

        // Solo los cerrados CON trabajo, igual que la velocidad: un sprint vacío aportaría un 0% que
        // no es un incumplimiento y hundiría el promedio.
        var conTrabajo = historico.Where(h => h.Cerrado && h.Total > 0).ToList();

        return new HistoricoDeSprintsDto(
            historico.Select(h => new SprintResumenDto(
                    h.SprintId, h.Name, h.StartDate, h.EndDate,
                    h.Total, h.Entregados, h.Cancelados, h.CompletadoPct, h.DiasTotales, h.Cerrado))
                .ToList(),
            velocidad,
            contados,
            conTrabajo.Count == 0
                ? null
                : (int)Math.Round(conTrabajo.Average(h => h.CompletadoPct), MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Los requerimientos que se le pueden colgar al sprint: los que están sin sprint más los que ya
    /// son suyos. Ni los cancelados —no hay nada que seguirles— ni los de otros sprints: mudarlos
    /// tiene que ser una decisión tomada desde el otro sprint y no el accidente de una casilla.
    ///
    /// Es del líder porque es la lista con la que se compromete el alcance, y comprometerlo es suyo.
    /// </summary>
    public async Task<IReadOnlyList<CandidatoDeSprintDto>> CandidatosAsync(
        int sprintId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        return await db.Requirements.AsNoTracking()
            .Where(r => r.Status != RequirementStatus.Cancelado
                     && (r.SprintId == null || r.SprintId == sprintId))
            .OrderBy(r => r.SprintId == sprintId ? 0 : 1)   // primero lo que ya está dentro
            .ThenByDescending(r => r.Id)
            .Select(r => new CandidatoDeSprintDto(
                r.Id, r.Title, EtiquetasDeTrabajo.Estado(r.Status), r.CommittedDeliveryDate,
                r.SprintId == sprintId))
            .ToListAsync(ct);
    }

    // ── Pantalla de mis asignaciones ─────────────────────────────────────────────

    /// <summary>
    /// Mis requerimientos con el tiempo que llevo en cada uno, y el cronómetro que esté corriendo.
    ///
    /// Una cuenta sin ficha de desarrollador se va con la lista vacía y <c>TieneFicha</c> en falso:
    /// no es un error, es el caso real de las cuentas de administración sin ficha, y la pantalla lo
    /// dice en vez de enseñar una tabla vacía sin explicación.
    /// </summary>
    public async Task<MisAsignacionesDto> MisAsignacionesAsync(
        RequirementStatus? estado = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, "de mis asignaciones");

        if (currentUser.DeveloperId is not int devId)
            return new MisAsignacionesDto(TieneFicha: false, [], null);

        var q = db.Requirements.AsNoTracking()
            .Where(r => r.Assignments.Any(a => a.DeveloperId == devId));
        if (estado is { } e) q = q.Where(r => r.Status == e);

        var filas = await q
            .OrderByDescending(r => r.CommittedDeliveryDate).ThenByDescending(r => r.Id)
            .ToListAsync(ct);

        var tiempos = await TiemposAsync(devId, filas.Select(r => r.Id).ToList(), ct);
        var hoy = DateTime.Today;

        return new MisAsignacionesDto(
            TieneFicha: true,
            filas.Select(r =>
            {
                var (segundos, cronometro) = tiempos.GetValueOrDefault(r.Id);
                return new MiAsignacionDto(
                    r.Id,
                    r.Title,
                    r.Status, EtiquetasDeTrabajo.Estado(r.Status),
                    EtiquetasDeTrabajo.Prioridad(r.Priority),
                    r.EstimateHours,
                    r.CommittedDeliveryDate,
                    r.ProgressPercent,
                    EstaVencido(r.Status, r.CommittedDeliveryDate, hoy),
                    segundos,
                    WorkSessionService.Format(segundos),
                    cronometro);
            }).ToList(),
            await jornada.CronometroAsync(ct));
    }

    /// <summary>
    /// Segundos dedicados y estado del cronómetro de cada requerimiento, en UNA sola consulta.
    ///
    /// El tramo en curso se suma con <see cref="WorkSession.LiveSeconds"/>, que es lógica de la
    /// entidad y por tanto se evalúa en memoria: de ahí que se traigan las sesiones y se agrupen
    /// aquí en vez de pedirle la suma al motor.
    /// </summary>
    private async Task<Dictionary<int, (int segundos, EstadoDelCronometro estado)>> TiemposAsync(
        int devId, IReadOnlyCollection<int> requirementIds, CancellationToken ct)
    {
        if (requirementIds.Count == 0) return [];

        var sesiones = await db.WorkSessions.AsNoTracking()
            .Where(w => w.DeveloperId == devId
                     && w.RequirementId != null && requirementIds.Contains(w.RequirementId.Value))
            .ToListAsync(ct);

        var ahora = DateTime.UtcNow;
        return sesiones
            .GroupBy(w => w.RequirementId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (
                    g.Sum(w => w.LiveSeconds(ahora)),
                    // Una activa manda sobre una pausada: es la que tiene el contador corriendo y la
                    // que decide si el botón dice «Iniciar» o «Pausar».
                    g.Any(w => w.Status == WorkSessionStatus.Activa) ? EstadoDelCronometro.Activo
                    : g.Any(w => w.Status == WorkSessionStatus.Pausada) ? EstadoDelCronometro.Pausado
                    : EstadoDelCronometro.SinSesion));
    }
}
