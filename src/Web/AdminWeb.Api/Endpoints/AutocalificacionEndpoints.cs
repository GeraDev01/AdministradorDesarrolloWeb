using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Autocalificacion;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Lo que el desarrollador escribe sobre su propio trabajo: se autocalifica, corrige lo que sigue
/// abierto, replica un rechazo, y lleva sus actividades libres con la evidencia que las respalda.
///
/// <b>Ninguna ruta lleva identificador de desarrollador</b>, igual que en la jornada y por el mismo
/// motivo: todas actúan sobre quien tiene la sesión. Lo que sí llevan identificador son las
/// entradas y las actividades, porque se opera sobre una en concreto; de que sea suya responde el
/// servicio con <c>RequireOwnershipOrAdmin</c>, que es donde está el dato y donde no se puede
/// olvidar.
///
/// <para>El grupo exige la política <c>AdminUDesarrollador</c>. No sobra a pesar de las guardas de
/// los servicios: son dos barreras, y la de aquí es la que impide que Operaciones llegue siquiera a
/// ejecutar el endpoint.</para>
/// </summary>
public static class AutocalificacionEndpoints
{
    public static void MapAutocalificacionEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/autocalificacion")
            .WithTags("Autocalificación")
            .RequireAuthorization("AdminUDesarrollador");

        grupo.MapGet("/mias", async (AutocalificacionQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.MisActividadesAsync(ct)))
        .WithSummary("Entradas de puntos, actividades libres y catálogos de los formularios, de una vez");

        // ── Autocalificación ─────────────────────────────────────────────────────
        //
        // REGISTRAR SE APAGÓ, y la ruta se queda publicada a propósito. Quien la llame recibe un 400
        // con el texto que explica a dónde ir —«proponer al pool»— en vez de un 404 mudo, que es lo
        // que recibiría un cliente viejo, una pestaña abierta desde ayer o el escritorio mientras
        // siga siendo la marcha atrás. Quitarla no habría hecho el sistema más simple: habría hecho
        // el fallo más difícil de entender.
        //
        // Y el apagado NO ESTÁ AQUÍ sino en PerformanceScoringService.RegistrarAutocalificacionAsync,
        // por la regla de la casa: las decisiones de negocio viven donde está el dato, no en la
        // capa que solo traduce HTTP. Lo demás del grupo —corregir, replicar, la captura, las
        // actividades libres— sigue vivo y sin tocar.

        grupo.MapPost("/entradas", async (
            AutocalificacionRequest cuerpo, PerformanceScoringService puntuacion,
            AuditService bitacora, ICurrentUser quien, CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId)
                return Results.BadRequest(new ResultadoDto(false,
                    "Tu cuenta no tiene ficha de desarrollador, así que no puede registrar actividades."));

            var (capturaOk, capturaError, captura, nombreDeCaptura) = ValidarCaptura(cuerpo);
            if (!capturaOk) return Results.BadRequest(new ResultadoDto(false, capturaError));

            var borrador = ABorrador(cuerpo, devId, captura, nombreDeCaptura);
            var (ok, mensaje, entrada) = await puntuacion.RegistrarAutocalificacionAsync(borrador, ct);

            // La bitácora la deja quien llama y no el servicio, igual que en el escritorio: el
            // servicio de puntuación lo usan también el registro del líder y los ajustes, y cada uno
            // anota una cosa distinta. Sin esta línea, la autocalificación sería lo único del módulo
            // que no deja rastro.
            if (ok)
                await bitacora.RecordAsync(AuditAction.Create, "PointEntry", entrada!.Id.ToString(),
                    $"Autocalificación pendiente (+{entrada.Points} pts)", ct);

            return Resultado(ok, mensaje);
        })
        .WithSummary("RETIRADA: registrar puntos por cuenta propia se sustituyó por proponer al pool");

        grupo.MapPost("/entradas/{id:int}/correccion", async (
            int id, AutocalificacionRequest cuerpo, PerformanceScoringService puntuacion,
            AutocalificacionQueryService consultas, AuditService bitacora, CancellationToken ct) =>
        {
            var (capturaOk, capturaError, captura, nombreDeCaptura) = ValidarCaptura(cuerpo);
            if (!capturaOk) return Results.BadRequest(new ResultadoDto(false, capturaError));

            // Tres estados, no dos: imagen nueva, quitar la que había, o dejarla como está. El
            // servicio escribe SIEMPRE lo que reciba en Screenshot, así que el tercer caso hay que
            // resolverlo aquí releyendo la que ya estaba guardada; si no, corregir el comentario
            // borraría la captura sin que nadie lo hubiera pedido.
            if (captura == null && !cuerpo.QuitarCaptura)
            {
                var (guardada, nombreGuardado) = await consultas.CapturaDeAsync(id, ct);
                if (guardada.Length > 0) (captura, nombreDeCaptura) = (guardada, nombreGuardado);
            }

            var cambios = ABorrador(cuerpo, developerId: 0, captura, nombreDeCaptura);
            var (ok, mensaje) = await puntuacion.EditarAutocalificacionAsync(id, cambios, ct);

            if (ok)
                await bitacora.RecordAsync(AuditAction.Update, "PointEntry", id.ToString(),
                    "Autocalificación corregida por el desarrollador", ct);

            return Resultado(ok, mensaje);
        })
        .WithSummary("Corrige una autocalificación propia mientras no esté aprobada");

        grupo.MapPost("/entradas/{id:int}/replica", async (
            int id, ReplicaRequest cuerpo, PerformanceScoringService puntuacion,
            AuditService bitacora, CancellationToken ct) =>
        {
            var (ok, mensaje) = await puntuacion.ReplicarAsync(id, cuerpo.Argumento, ct);

            if (ok)
                await bitacora.RecordAsync(AuditAction.Update, "PointEntry", id.ToString(),
                    "Autocalificación replicada por el desarrollador y devuelta a revisión", ct);

            return Resultado(ok, mensaje);
        })
        .WithSummary("Responde a un rechazo con un argumento y devuelve la actividad a revisión");

        // La captura sale por aquí y no por /api/adjuntos porque los bytes los suelta el servicio de
        // consulta de este módulo, que es quien comprueba que la entrada sea del que pregunta. El
        // trato es el mismo que el del resto de adjuntos: lo decide ResultadosDeArchivo.
        grupo.MapGet("/entradas/{id:int}/captura", async (
            int id, HttpContext ctx, AutocalificacionQueryService consultas, CancellationToken ct) =>
        {
            var (bytes, nombre) = await consultas.CapturaDeAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("La captura que respalda una entrada de puntos");

        // ── Actividades libres ───────────────────────────────────────────────────

        grupo.MapPost("/actividades", async (
            ActividadLibreRequest cuerpo, DevActivityService actividades, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId)
                return Results.BadRequest(new ResultadoDto(false,
                    "Tu cuenta no tiene ficha de desarrollador, así que no puede registrar actividades."));

            var (ok, mensaje, _) = await actividades.CrearAsync(devId, cuerpo.Titulo, cuerpo.Descripcion, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Crea una actividad libre");

        grupo.MapPost("/actividades/{id:int}/edicion", async (
            int id, ActividadLibreRequest cuerpo, DevActivityService actividades, CancellationToken ct) =>
        {
            var (ok, mensaje) = await actividades.RenombrarAsync(id, cuerpo.Titulo, cuerpo.Descripcion, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Cambia el título o la descripción de una actividad abierta");

        grupo.MapPost("/actividades/{id:int}/cierre", async (
            int id, DevActivityService actividades, CancellationToken ct) =>
        {
            var (ok, mensaje) = await actividades.CerrarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Cierra la actividad y detiene su cronómetro si estaba corriendo");

        grupo.MapPost("/actividades/{id:int}/reapertura", async (
            int id, DevActivityService actividades, CancellationToken ct) =>
        {
            var (ok, mensaje) = await actividades.ReabrirAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Reabre una actividad cerrada");

        grupo.MapPost("/actividades/{id:int}/eliminacion", async (
            int id, DevActivityService actividades, CancellationToken ct) =>
        {
            var (ok, mensaje) = await actividades.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una actividad, solo si no tiene tiempo registrado");

        // ── Evidencia de las actividades libres ──────────────────────────────────
        //
        // Los BYTES no salen por aquí: se piden a /api/adjuntos/actividad/{id}, que es por donde sale
        // todo lo que está guardado en la base y donde ya está resuelto lo delicado (tipo por
        // contenido, nosniff y nombre limpio).

        grupo.MapGet("/actividades/{id:int}/evidencias", async (
            int id, AutocalificacionQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.EvidenciasDeAsync(id, ct)))
        .WithSummary("Los archivos que respaldan una actividad, sin su contenido");

        // Declara IFormFile a propósito: eso es lo que marca el endpoint como formulario y hace que
        // el middleware de antiforgery le exija su testigo. Es la única familia de peticiones que lo
        // necesita —un cuerpo JSON obliga al navegador a preguntar antes y sin política CORS no
        // pasa—, pero un formulario multipart desde otro sitio sí llegaría con la cookie puesta.
        // El testigo lo adjunta el cliente sin que la pantalla tenga que acordarse (ClienteApi.SubirAsync).
        grupo.MapPost("/actividades/{id:int}/evidencias", async (
            int id, IFormFile archivo, [FromForm] string? nota,
            DevActivityService actividades, CancellationToken ct) =>
        {
            if (archivo.Length == 0)
                return Results.BadRequest(new ResultadoDto(false, "No llegó ningún archivo."));

            // Se mira el tamaño ANTES de copiarlo: si no, un archivo enorme se materializaría entero
            // en memoria solo para rechazarlo después.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Results.BadRequest(new ResultadoDto(false,
                    $"El archivo pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB."));

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var bytes = memoria.ToArray();

            // La comprobación que cuenta es esta, la del servidor: lo que filtre el navegador es
            // comodidad, y a esta ruta se puede llegar sin pasar por él.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, bytes);
            if (!valido) return Results.BadRequest(new ResultadoDto(false, error));

            var (ok, mensaje, _) = await actividades.AgregarEvidenciaAsync(id, nombreSeguro, bytes, nota, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Adjunta una captura o un documento a una actividad abierta");

        grupo.MapPost("/evidencias/{id:int}/eliminacion", async (
            int id, DevActivityService actividades, CancellationToken ct) =>
        {
            var (ok, mensaje) = await actividades.EliminarEvidenciaAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Quita un archivo de la evidencia de una actividad");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«el criterio "X" fue desactivado»—; eso es lo que se
    /// enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Comprueba la imagen que llega con el formulario. Solo imágenes: la captura acaba enseñándose
    /// dentro de la página al revisar, y es lo mismo que admitía el escritorio.
    ///
    /// Sin captura no es un error: el campo es opcional y se devuelve nulo.
    /// </summary>
    private static (bool ok, string error, byte[]? bytes, string? nombre) ValidarCaptura(
        AutocalificacionRequest cuerpo)
    {
        if (cuerpo.Captura is not { Length: > 0 }) return (true, "", null, null);

        var (ok, error, nombreSeguro) = ArchivosSubidos.Validar(
            cuerpo.NombreDeCaptura, cuerpo.Captura, soloImagenes: true);

        return ok ? (true, "", cuerpo.Captura, nombreSeguro) : (false, error, null, null);
    }

    /// <summary>
    /// Traduce la petición al borrador que espera el servicio.
    ///
    /// Solo se rellena lo que el desarrollador decide. Los puntos, el estado y los campos de revisión
    /// los fija <see cref="PerformanceScoringService"/> a partir del criterio: mandarlos desde aquí
    /// sería devolverle a la pantalla la decisión de cuánto vale su propio trabajo.
    ///
    /// Al corregir, <paramref name="developerId"/> da igual —el servicio lo sustituye por el dueño
    /// real de la entrada, que es lo que impide cambiarla de persona—, así que se pasa cero.
    /// </summary>
    private static PointEntry ABorrador(
        AutocalificacionRequest c, int developerId, byte[]? captura, string? nombreDeCaptura) => new()
    {
        DeveloperId = developerId,
        CriterionId = c.CriterioId,
        Year = c.Anio,
        Month = c.Mes,
        Comment = string.IsNullOrWhiteSpace(c.Comentario) ? null : c.Comentario.Trim(),
        RequirementId = c.RequerimientoId,
        MinutesSpent = c.MinutosDeclarados,
        EvidenceUrl = c.Enlace,
        Screenshot = captura,
        ScreenshotFileName = nombreDeCaptura
    };
}
