using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Sla;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Los compromisos de atención (SLA) y sus recordatorios.
///
/// <b>Dos alcances y por eso dos políticas</b>, que es la traducción de lo que el escritorio decidía
/// escondiendo botones:
///
///  · ASIGNAR, cerrar, cancelar y configurar las políticas automáticas es del líder
///    (<c>SoloAdmin</c>): él compromete el plazo ante el cliente y él responde por él.
///  · «MIS SLA» es de quien tiene la sesión (<c>AdminUDesarrollador</c>) y <b>su ruta no lleva
///    identificador</b>, igual que las de la jornada y el pool. Con un <c>/{devId}</c> habría que
///    comprobar en cada endpoint que es el propio, y el día que a uno se le olvidara sería «mira —o
///    pospone— el compromiso de otra persona».
///
/// <b>El desarrollador escribe en dos sitios</b>, y la diferencia entre ellos es la regla del
/// vertical entero: <c>posponer</c> aplaza el recordatorio con dos límites que no se negocian —entre
/// 1 y 72 horas, y nunca más allá del vencimiento—, mientras que <c>avance</c> deja CONSTANCIA
/// comentando el work item en Azure DevOps y solo cuenta si DevOps aceptó el comentario. Posponer
/// sirve para decir «estoy en ello»; registrar avance es lo que prueba que se atendió.
///
/// <b>Tampoco está aquí el reporte de cumplimiento</b>: vive en <c>MapCumplimientoSlaEndpoints</c>
/// desde la fase de solo lectura, y con él las reglas de conteo que el escritorio tenía en
/// <c>SlaComplianceStats</c> —clasificar, agrupar, el porcentaje solo sobre lo resuelto y el «—»
/// cuando no hay nada que medir—. Volver a portar aquel archivo habría dejado dos copias de las
/// mismas reglas esperando a separarse; lo que sí se reutiliza desde este vertical es su decisión
/// sobre los tickets cerrados (ver <see cref="SlaDevOpsReconciler"/>).
/// </summary>
public static class SlaEndpoints
{
    /// <summary>Asignar, cerrar, cancelar y configurar: del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    /// <summary>Lo propio: del líder y del desarrollador, que también puede tener compromisos.</summary>
    private const string PoliticaDelEquipo = "AdminUDesarrollador";

    public static void MapSlaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/sla").WithTags("SLA");

        // ── Pantalla del líder ───────────────────────────────────────────────────

        grupo.MapGet("/", async (SlaStatus? estado, SlaService servicio, CancellationToken ct) =>
            Results.Ok(await servicio.PantallaDelLiderAsync(estado, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los compromisos del filtro, las tarjetas y las políticas automáticas");

        grupo.MapGet("/opciones", async (SlaService servicio, CancellationToken ct) =>
            Results.Ok(await servicio.OpcionesAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Responsables y objetivos a los que se les puede asignar un SLA");

        grupo.MapPost("/nuevo", async (
            AsignarSlaRequest cuerpo, SlaService servicio, CancellationToken ct) =>
        {
            var objetivo = cuerpo.EsRequerimiento
                ? SlaObjetivo.Requerimiento(cuerpo.ObjetivoId)
                : SlaObjetivo.Actividad(cuerpo.ObjetivoId);

            var (ok, mensaje, _) = await servicio.AsignarAsync(
                objetivo, cuerpo.DesarrolladorId, cuerpo.VenceUtc, cuerpo.RecordatorioCadaHoras,
                cuerpo.TicketExternalId, cuerpo.TicketUrl, cuerpo.Notas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Asigna un compromiso con fecha límite y su cadencia de recordatorio");

        grupo.MapPost("/{id:int}/cumplido", async (
            int id, NotaDeSlaRequest? cuerpo, SlaService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.MarcarCumplidoAsync(id, cuerpo?.Nota, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da el compromiso por atendido");

        grupo.MapPost("/{id:int}/cancelar", async (
            int id, SlaService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.CancelarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Deja el compromiso sin efecto (no cuenta como incumplimiento)");

        // El mismo trabajo que hace solo el job cada cuarto de hora, a petición. Se conserva del
        // escritorio —era su botón «Revisar vencidos»— porque mientras los trabajos de fondo estén
        // apagados es la ÚNICA forma de escalar, y después sigue sirviendo para no esperar el ciclo.
        grupo.MapPost("/revisar-vencidos", async (
            SlaNotificationService avisos, CancellationToken ct) =>
        {
            var resultado = await avisos.EscalarIncumplimientosAsync(ct);

            var mensaje = (resultado.Vencidos == 0
                ? "No hay compromisos vencidos sin reportar."
                : resultado.SinDestinatario
                    ? $"{resultado.Vencidos} compromiso(s) marcado(s) como vencido(s), pero no hay a quién " +
                      "escalarlos: configura «SlaEscalationEmail» o deja al menos un líder activo."
                    : $"{resultado.Vencidos} compromiso(s) marcado(s) como vencido(s) y escalado(s).")
                + NotaDelCorreo(resultado);

            // Un 200 aunque no hubiera destinatario: la revisión SÍ se hizo y los vencimientos
            // quedaron marcados. Lo que falta se explica en el mensaje, que es lo que se enseña.
            return Results.Ok(new ResultadoDto(true, mensaje));
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Marca los vencimientos y escala los incumplimientos ahora mismo");

        grupo.MapPost("/politicas", async (
            GuardarPoliticasSlaRequest cuerpo, SlaService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.GuardarPoliticasAsync(cuerpo.Politicas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Guarda los plazos automáticos por prioridad de DevOps");

        // ── Mis SLA ──────────────────────────────────────────────────────────────

        grupo.MapGet("/mios", async (
            bool? incluirCerrados, SlaService servicio, CancellationToken ct) =>
            Results.Ok(await servicio.MisCompromisosAsync(incluirCerrados == true, ct)))
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Mis compromisos, con lo que falta para cada plazo");

        grupo.MapPost("/{id:int}/posponer", async (
            int id, PosponerSlaRequest cuerpo, SlaService servicio, CancellationToken ct) =>
        {
            // Sin comprobar aquí de quién es: el servicio exige propiedad o ser líder, y duplicar la
            // comprobación en el endpoint invitaría a que un día solo se arregle una de las dos.
            var (ok, mensaje) = await servicio.PosponerAsync(id, cuerpo.Horas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Aplaza el recordatorio sin mover la fecha límite");

        // Multipart SIEMPRE, lleve o no capturas, por lo mismo que el comentario de DevOps: es una
        // sola forma de mandar la operación, y declarar IFormCollection es lo que hace obligatorio el
        // testigo antifalsificación. Lo pone ClienteApi.SubirAsync.
        //
        // Comparte con aquel la lectura de evidencias: acaba publicando por el mismo camino, así que
        // los límites de número y tamaño tienen que ser LOS MISMOS.
        grupo.MapPost("/{id:int}/avance", async (
            int id, IFormCollection formulario, SlaService servicio, CancellationToken ct) =>
        {
            var (evidencias, error) = await DevOpsEndpoints.LeerEvidenciasAsync(formulario.Files, ct);
            if (error != null) return Resultado(false, error);

            // Sin comprobar aquí de quién es el compromiso ni de quién es el ticket: lo primero lo
            // exige SlaService y lo segundo DevOpsService, contra la fila.
            var (ok, mensaje) = await servicio.RegistrarAvanceAsync(
                id, formulario["texto"], evidencias, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Registra el avance comentando el ticket en DevOps; sin eso no cuenta");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ese objetivo ya tiene un SLA activo», «solo se
    /// puede posponer entre 1 y 72 horas»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Qué pasó con el correo del escalamiento, añadido al mensaje de la revisión.
    ///
    /// El correo es el extra —el aviso dentro de la aplicación ya salió—, así que su fallo no
    /// convierte la revisión en un error; se cuenta. El motivo lo escribió el cliente de correo y se
    /// enseña tal cual: distingue «no está configurado» de «el servidor lo rechazó».
    /// </summary>
    private static string NotaDelCorreo(EscalamientoDeSla resultado) => resultado switch
    {
        { Vencidos: 0 } => "",
        { SalioPorCorreo: true } => " También salió por correo.",
        { FalloDelCorreo: string motivo } => $" No salió por correo: {motivo}",
        _ => ""
    };
}
