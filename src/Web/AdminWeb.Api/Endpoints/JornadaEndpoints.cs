using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Jornada;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La jornada propia: marcar entrada y salida, y el cronómetro.
///
/// <b>De quién es este módulo:</b> del líder y del desarrollador, y de nadie más. Operaciones queda
/// fuera —por eso el grupo entero lleva <c>AdminUDesarrollador</c>— porque no registra jornada: su
/// alcance son los despliegues, su estado es siempre «Disponible» y lo pone el servidor. La jornada
/// sigue siendo de la CUENTA y no de la ficha de desarrollador (un líder sin ficha marca igual); lo
/// que cambió es quién la registra, no de qué cuelga.
///
/// <para>La política va en el GRUPO y no endpoint por endpoint a propósito: así cubre las nueve
/// rutas de una vez y una ruta nueva nace protegida en lugar de nacer abierta. Los tres endpoints del
/// cronómetro ya eran inalcanzables de hecho para una cuenta sin ficha —contestaban 400—, pero
/// «devuelve 400 porque no tiene ficha» no es un permiso: ahora dan 403, que es lo que son.</para>
///
/// <b>Ninguna ruta lleva identificador de usuario, y no es un descuido.</b> Todas actúan sobre quien
/// tiene la sesión, leído de la cookie. Una ruta con <c>/{userId}</c> obligaría a comprobar en cada
/// endpoint que ese identificador es el propio, y el día que a uno se le olvidara sería «marca la
/// entrada de otra persona». Sin identificador no hay nada que comprobar ni que olvidar.
///
/// <para>La hora la pone siempre el servidor. Los endpoints de marcaje no reciben fecha ni la
/// recibirán: en cuanto alguien pueda escribir su propia hora de llegada, el registro deja de probar
/// nada. Lo retroactivo lo corrige el líder, con motivo y rastro, por otra ruta.</para>
/// </summary>
public static class JornadaEndpoints
{
    public static void MapJornadaEndpoints(this IEndpointRouteBuilder app)
    {
        // AVISO: hoy esta línea es la ÚNICA barrera del módulo. La casa protege dos veces —política
        // aquí y guarda dentro del servicio—, y de esas guardas solo está puesta la de
        // PresenceService.MisJornadasAsync; AttendanceService y JornadaQueryService siguen exigiendo
        // nada más sesión iniciada. Falta cambiar ahí RequireLoggedIn por RequireAdminOrDesarrollador
        // en lo «propio» (marcar, mi registro, mis registros, pedir corrección, mi jornada, estado de
        // marcaje), sin tocar lo que ya es RequireAdmin — el líder tiene que seguir mirando y
        // corrigiendo días viejos de un operativo, que existen y no se borran.
        var grupo = app.MapGroup("/api/jornada").WithTags("Jornada")
                       .RequireAuthorization("AdminUDesarrollador");

        // El rango es opcional: sin él salen los últimos 30 días, como antes. Con él, la pantalla
        // ofrece Hoy / Esta semana / Este mes / un rango libre, que es lo que tenía el escritorio.
        grupo.MapGet("/mia", async (
            DateOnly? desde, DateOnly? hasta, JornadaQueryService jornada, CancellationToken ct) =>
            Results.Ok(await jornada.MiJornadaAsync(
                desde?.ToDateTime(TimeOnly.MinValue), hasta?.ToDateTime(TimeOnly.MinValue), ct)))
        .WithSummary("Marcaje del día, cronómetro, telemetría propia e historial del rango pedido");

        grupo.MapGet("/estado", async (JornadaQueryService jornada, CancellationToken ct) =>
            Results.Ok(await jornada.EstadoDeMarcajeAsync(ct)))
        .WithSummary("Lo justo para el botón de marcaje de la barra superior");

        // El estado de presencia propio, con su nota. El servicio existía desde el port y no lo
        // llamaba nadie: el botón de la barra arrancaba de una variable local en «Disponible», así
        // que tras recargar decía eso aunque en el tablero del líder pusiera «En reunión».
        grupo.MapGet("/presencia", async (PresenceService presencia, CancellationToken ct) =>
        {
            var (estado, nota) = await presencia.MiEstadoAsync(ct);
            return Results.Ok(new MiPresenciaDto(estado, PresenceService.Etiqueta(estado), nota));
        })
        .WithSummary("Mi estado de presencia actual y su nota");

        grupo.MapPost("/entrada", async (
            MarcajeRequest? cuerpo, AttendanceService asistencia, CancellationToken ct) =>
        {
            var (ok, mensaje) = await asistencia.MarcarEntradaAsync(cuerpo?.Nota, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Marca la entrada, con la hora del servidor");

        grupo.MapPost("/salida", async (
            MarcajeRequest? cuerpo, AttendanceService asistencia, CancellationToken ct) =>
        {
            var (ok, mensaje) = await asistencia.MarcarSalidaAsync(cuerpo?.Nota, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Marca la salida, con la hora del servidor");

        grupo.MapPost("/registros/{id:int}/correccion", async (
            int id, CorreccionRequest cuerpo, AttendanceService asistencia, CancellationToken ct) =>
        {
            var (ok, mensaje) = await asistencia.SolicitarCorreccionAsync(id, cuerpo.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Pide al líder que corrija un registro propio");

        // ── Cronómetro ───────────────────────────────────────────────────────────
        //
        // Arrancar y parar van por HTTP y no por el hub: son operaciones que tienen que confirmarse o
        // fallar con un motivo, y una llamada de SignalR que se pierde no deja al usuario saber si
        // su cronómetro arrancó. Por el hub va solo el latido, que sí puede perderse sin consecuencia.

        grupo.MapPost("/cronometro/iniciar", async (
            CronometroRequest cuerpo, WorkSessionService cronometros, ICurrentUser quien,
            JornadaQueryService jornada, CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId)
                return Results.BadRequest(new ResultadoDto(false,
                    "Tu cuenta no tiene ficha de desarrollador, así que no puede cronometrar trabajo."));

            var objetivo = AObjetivo(cuerpo);
            if (objetivo is null)
                return Results.BadRequest(new ResultadoDto(false,
                    "Indica un requerimiento o una actividad, no ambos."));

            await cronometros.StartOrResumeAsync(devId, objetivo.Value, ct: ct);
            return Results.Ok(await jornada.CronometroAsync(ct));
        })
        .WithSummary("Arranca o reanuda el cronómetro (pausa cualquier otro del mismo desarrollador)");

        grupo.MapPost("/cronometro/pausar", async (
            CronometroRequest cuerpo, WorkSessionService cronometros, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId || AObjetivo(cuerpo) is not { } objetivo)
                return Results.BadRequest(new ResultadoDto(false, "Petición incompleta."));

            await cronometros.PauseAsync(devId, objetivo, ct);
            return Results.Ok(new ResultadoDto(true, "Cronómetro pausado."));
        })
        .WithSummary("Pausa el cronómetro conservando lo acumulado");

        grupo.MapPost("/cronometro/detener", async (
            CronometroRequest cuerpo, WorkSessionService cronometros, ICurrentUser quien,
            DevOpsService devops, CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId || AObjetivo(cuerpo) is not { } objetivo)
                return Results.BadRequest(new ResultadoDto(false, "Petición incompleta."));

            await cronometros.StopAsync(devId, objetivo, ct);

            // El reporte va DESPUÉS de detener y de consolidar, y por eso no puede tumbarlos.
            var aviso = await ReportarADevOpsAsync(devops, cronometros, devId, objetivo, ct);

            return Results.Ok(new ResultadoDto(true, "Cronómetro detenido y tiempo consolidado." + aviso));
        })
        .WithSummary("Detiene el cronómetro, consolida el tramo y reporta el tiempo al ticket");
    }

    /// <summary>
    /// Registra en el ticket de Azure DevOps el tiempo del requerimiento que se acaba de detener, si
    /// el líder activó el reporte y el requerimiento vino de allá. Devuelve lo que hay que añadir al
    /// mensaje, o cadena vacía cuando no había nada que reportar.
    ///
    /// <para><b>Detener no depende de que esto salga bien.</b> El tramo ya está consolidado en la
    /// base cuando se llega aquí: contestar un fallo dejaría a la persona pulsando «detener» una y
    /// otra vez sobre un cronómetro que ya se detuvo, y cada intento volvería a intentar el reporte.
    /// Lo que pasó se cuenta en el mismo mensaje, que es donde se lee.</para>
    ///
    /// <para>El total va por PERSONA y objetivo, como en el escritorio. Es a propósito aunque la
    /// marca de agua de lo ya reportado sea del requerimiento: lo que se publica queda firmado con su
    /// token en DevOps, y sumar el rato de otro dejaría horas ajenas a su nombre.</para>
    /// </summary>
    private static async Task<string> ReportarADevOpsAsync(
        DevOpsService devops, WorkSessionService cronometros, int devId, WorkTarget objetivo,
        CancellationToken ct)
    {
        // Una actividad libre del pool no tiene work item que actualizar.
        if (objetivo.RequirementId is not int requerimientoId) return "";

        try
        {
            int total = await cronometros.GetTotalSecondsAsync(devId, objetivo, ct);
            var (intentado, ok, mensaje) = await devops.ReportarTiempoAsync(requerimientoId, total, ct);

            // Ni el reporte está activado, ni el requerimiento es de DevOps, ni hay tiempo nuevo: no
            // hay nada que contar, y decirlo sería ruido en cada parada de todo el mundo.
            if (!intentado) return "";

            return ok
                ? $" Tiempo registrado en Azure DevOps: {mensaje}."
                : $" No se registró en Azure DevOps: {mensaje}";
        }
        catch (Exception)
        {
            // El detalle no se enseña a propósito: aquí solo llega lo IMPREVISTO —los fallos de la
            // integración ya vienen explicados dentro del resultado— y el texto de una excepción
            // cualquiera no es algo que convenga poner delante de nadie.
            return " El tiempo quedó guardado aquí, pero no se pudo registrar en Azure DevOps.";
        }
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ya marcaste tu entrada hoy a las 09:12»—; eso es lo
    /// que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Exactamente uno de los dos identificadores. Los dos a la vez, o ninguno, es una petición mal
    /// formada: el tiempo se cuenta contra un requerimiento o contra una actividad libre.
    /// </summary>
    private static WorkTarget? AObjetivo(CronometroRequest c) =>
        (c.RequerimientoId, c.ActividadId) switch
        {
            (int r, null) => WorkTarget.Requerimiento(r),
            (null, int a) => WorkTarget.Actividad(a),
            _             => null
        };
}
