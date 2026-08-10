using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Jornada;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma de una sola vez todo lo que enseña «Mi jornada»: el marcaje del día, el cronómetro en
/// marcha y el historial reciente.
///
/// Es una consulta propia de la web y no un servicio portado. En el escritorio esta pantalla eran
/// tres controles que consultaban por su cuenta contra la base local; aquí cada consulta sería un
/// viaje de red, y una pantalla que se pinta en cuatro tandas parpadea. Los servicios de fondo
/// —asistencia, presencia, cronómetro— siguen siendo los dueños de su lógica: esto solo los junta.
/// </summary>
public class JornadaQueryService(
    AppDbContext db,
    ICurrentUser currentUser,
    AttendanceService asistencia,
    PresenceService presencia)
{
    /// <summary>Cuántos días atrás enseña el historial por omisión. Un mes cubre el ciclo de nómina.</summary>
    public const int DiasDeHistorial = 30;

    /// <summary>
    /// Tope del rango que se puede pedir. Un año es más de lo que nadie repasa de una vez, y sin
    /// tope una petición de una línea podría traer todas las jornadas que existan.
    /// </summary>
    public const int TopeDeDias = 366;

    /// <param name="desdeLocal">Primer día del historial. Nulo = los últimos 30, como antes.</param>
    /// <param name="hastaLocal">Último día, incluido. Nulo = hoy.</param>
    public async Task<MiJornadaDto> MiJornadaAsync(
        DateTime? desdeLocal = null, DateTime? hastaLocal = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // El rango se acota en el SERVIDOR y no se confía en el que llegue. Sin tope, «desde 1990»
        // traería años de filas por una petición de una línea; y un rango al revés devolvería vacío
        // sin que nadie entendiera por qué, así que se ordena en vez de rechazarlo.
        var hasta = (hastaLocal ?? DateTime.Today).Date;
        var desde = (desdeLocal ?? hasta.AddDays(-DiasDeHistorial)).Date;
        if (desde > hasta) (desde, hasta) = (hasta, desde);
        if ((hasta - desde).TotalDays > TopeDeDias) desde = hasta.AddDays(-TopeDeDias);

        var abierto = await asistencia.MiRegistroAbiertoAsync(ct);
        var registros = await asistencia.MisRegistrosAsync(desde, hasta, ct);

        // Cuánto se cronometró cada día, para poder contrastarlo con lo marcado. Es el dato que hace
        // útil la pantalla: una jornada de ocho horas con veinte minutos de cronómetro no es una
        // falta, pero es justo lo que la persona quiere ver antes de que se lo pregunten.
        var porDia = await SegundosPorDiaAsync(desde, ct);

        // Lo que la aplicación vio SOLA, agrupado por día local. Es la otra mitad de la pantalla: sin
        // esto, quien quiere comprobar si su marcaje cuadra con lo que trabajó no tiene con qué
        // compararlo, y era justo lo que el escritorio sí enseñaba.
        var telemetria = (await presencia.MisJornadasAsync(desde, hasta, ct))
            .GroupBy(j => DateOnly.FromDateTime(j.InicioUtc.ToLocalTime()))
            .ToDictionary(g => g.Key, g => new TelemetriaDelDia(
                g.Min(j => j.InicioUtc),
                g.Max(j => j.FinUtc ?? j.InicioUtc),
                (int)g.Sum(j => j.Duracion.TotalSeconds),
                // «Sin señal» si alguna de las jornadas del día se cerró sola por dejar de latir:
                // esa salida no es una hora real y conviene que se vea.
                g.Any(j => j.Cierre == PresenceEnd.SinLatido),
                g.Select(j => j.Equipo).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))));

        var historial = registros
            .OrderByDescending(r => r.CheckInUtc)
            .Select(r => new DiaDeJornadaDto(
                r.Id,
                r.CheckInUtc,
                r.CheckOutUtc,
                r.CheckInNote,
                r.CheckOutNote,
                AttendanceService.EtiquetaCierre(r.CloseKind),
                r.CorrectionRequestedAtUtc != null,
                r.CorrectionRequestNote,
                porDia.GetValueOrDefault(DateOnly.FromDateTime(r.CheckInUtc.ToLocalTime())),
                telemetria.GetValueOrDefault(DateOnly.FromDateTime(r.CheckInUtc.ToLocalTime()))))
            .ToList();

        // Días con TELEMETRÍA pero SIN marcaje: la aplicación estuvo abierta y nadie marcó. Se
        // añaden como filas propias porque son exactamente el caso que la pantalla existe para
        // enseñar —«olvidé marcar el jueves»— y sin ellas ese día simplemente no aparece.
        var conMarcaje = registros
            .Select(r => DateOnly.FromDateTime(r.CheckInUtc.ToLocalTime()))
            .ToHashSet();

        historial.AddRange(telemetria
            .Where(t => !conMarcaje.Contains(t.Key))
            .Select(t => new DiaDeJornadaDto(
                // Sin Id: no hay registro que corregir, así que tampoco se ofrece el botón.
                null,
                t.Value.PrimeraSenalUtc, t.Value.UltimaSenalUtc,
                null, null,
                "⚠ Sin marcar (solo telemetría)",
                false, null, porDia.GetValueOrDefault(t.Key), t.Value)));

        historial = [.. historial.OrderByDescending(h => h.EntradaUtc)];

        // «Ya cerró hoy» también impide volver a marcar: si no, quien se equivoca al salir abriría una
        // segunda jornada del mismo día en vez de pedir la corrección, que es lo que debe hacer.
        bool cerroHoy = registros.Any(r =>
            r.CheckOutUtc != null && r.CheckInUtc.ToLocalTime().Date == DateTime.Today);

        return new MiJornadaDto(
            abierto?.Id,
            abierto?.CheckInUtc,
            abierto?.CheckInNote,
            PuedeMarcarEntrada: abierto == null && !cerroHoy,
            CorreccionSolicitada: abierto?.CorrectionRequestedAtUtc != null,
            Cronometro: await CronometroAsync(ct),
            Historial: historial);
    }

    /// <summary>
    /// Lo justo para el botón de la barra superior.
    ///
    /// Existe aparte de <see cref="MiJornadaAsync"/> porque ese botón se ve en todas las pantallas y
    /// se refresca solo: traerle un mes de historial cada vez para decidir el texto de un botón sería
    /// un desperdicio que se paga en cada refresco y en cada persona.
    /// </summary>
    public async Task<EstadoDeMarcajeDto> EstadoDeMarcajeAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var abierto = await asistencia.MiRegistroAbiertoAsync(ct);
        if (abierto != null) return new EstadoDeMarcajeDto(abierto.CheckInUtc, false);

        var hoy = await asistencia.MisRegistrosAsync(DateTime.Today, DateTime.Today, ct);
        return new EstadoDeMarcajeDto(null, PuedeMarcarEntrada: hoy.Count == 0);
    }

    /// <summary>
    /// El cronómetro en marcha de quien tiene la sesión, o null.
    ///
    /// Solo cuenta el ACTIVO: uno pausado no tiene contador que correr en la pantalla y enseñarlo
    /// como si estuviera andando sería mentir sobre el tiempo que se está registrando.
    /// </summary>
    public async Task<CronometroDto?> CronometroAsync(CancellationToken ct = default)
    {
        if (currentUser.DeveloperId is not int devId) return null;

        var s = await db.WorkSessions.AsNoTracking()
            .Include(w => w.Requirement)
            .Include(w => w.Activity)
            .Where(w => w.DeveloperId == devId && w.Status == WorkSessionStatus.Activa)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(ct);
        if (s?.LastResumedAt is not DateTime desde) return null;

        var titulo = s.Requirement != null ? $"#{s.Requirement.Id} {s.Requirement.Title}"
                   : s.Activity != null ? s.Activity.Title
                   : "Trabajo sin título";

        // AhoraUtc es la hora del SERVIDOR y va aparte a propósito: el navegador cuenta desde ella y
        // no desde su propio reloj. Un equipo con la hora adelantada enseñaría, si no, tiempo que
        // nadie trabajó — y el reloj de un portátil recién despertado suele estar desajustado.
        return new CronometroDto(
            s.RequirementId, s.ActivityId, titulo, desde, s.AccumulatedSeconds, DateTime.UtcNow);
    }

    /// <summary>
    /// Segundos cronometrados por día local, dentro de la ventana del historial.
    ///
    /// Se suman los TRAMOS (<see cref="Domain.Entities.WorkInterval"/>) y no las sesiones: una sesión
    /// abierta el lunes y detenida el martes no es tiempo del lunes. El tramo ya trae su día local
    /// calculado, así que el agrupado lo hace SQL y aquí no se convierte ninguna zona horaria.
    /// </summary>
    private async Task<Dictionary<DateOnly, int>> SegundosPorDiaAsync(
        DateTime desde, CancellationToken ct)
    {
        if (currentUser.DeveloperId is not int devId) return [];

        var porDia = await db.WorkIntervals.AsNoTracking()
            .Where(i => i.DeveloperId == devId && i.LocalDate >= desde)
            .GroupBy(i => i.LocalDate)
            .Select(g => new { Dia = g.Key, Segundos = g.Sum(i => i.Seconds) })
            .ToListAsync(ct);

        return porDia.ToDictionary(x => DateOnly.FromDateTime(x.Dia), x => x.Segundos);
    }
}
