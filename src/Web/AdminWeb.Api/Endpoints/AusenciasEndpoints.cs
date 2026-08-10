using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Ausencias;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Las ausencias PROPIAS: pedir, consultar, cancelar y retirar vacaciones y permisos, más el
/// justificante de un permiso.
///
/// <b>Ninguna ruta lleva identificador de persona</b>, igual que en jornada: todas actúan sobre quien
/// tiene la sesión. Con un <c>/{developerId}</c> habría que comprobar en cada endpoint que ese
/// identificador es el propio, y el día que a uno se le olvidara sería «pide vacaciones a nombre de
/// otro». Los identificadores que sí viajan son los de la solicitud, y de esos se encarga la guarda
/// de pertenencia que ya traen los servicios.
///
/// <para>Lo que resuelve el líder —aprobar, rechazar, el documento firmado— no está aquí y no es un
/// descuido: es otra pantalla y otra fase.</para>
/// </summary>
public static class AusenciasEndpoints
{
    public static void MapAusenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ausencias")
            .WithTags("Ausencias")
            .RequireAuthorization("AdminUDesarrollador");

        // ── Vacaciones ───────────────────────────────────────────────────────────

        grupo.MapGet("/vacaciones/mias", async (AusenciasService ausencias, CancellationToken ct) =>
            Results.Ok(await ausencias.MisVacacionesAsync(ct)))
        .WithSummary("Saldo del año y solicitudes de vacaciones propias, de una vez");

        grupo.MapPost("/vacaciones", async (
            NuevaSolicitudDeVacacionesRequest cuerpo, AusenciasService ausencias, CancellationToken ct) =>
        {
            var (ok, mensaje, id) = await ausencias.SolicitarVacacionesAsync(
                cuerpo.Inicio, cuerpo.Fin, cuerpo.Comentario, ct);
            return ok ? Results.Ok(new SolicitudCreadaDto(true, mensaje, id)) : Rechazo(mensaje);
        })
        .WithSummary("Pide vacaciones para uno mismo; la solicitud nace pendiente");

        grupo.MapPost("/vacaciones/{id:int}/cancelacion", async (
            int id, CancelacionRequest? cuerpo, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.CancelarAsync(id, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Retira una solicitud propia, esté pendiente o ya aprobada");

        // Eliminar va por POST y no por DELETE porque el cliente solo sabe leer y enviar (ver
        // ClienteApi): un verbo que la única aplicación que llama no puede usar sería decorativo.
        grupo.MapPost("/vacaciones/{id:int}/eliminacion", async (
            int id, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra una solicitud que nunca llegó a ser una decisión del líder");

        // El respaldo SUBE por aquí y BAJA por /api/adjuntos/vacaciones/{id}, exactamente igual que el
        // justificante de un permiso: allí está resuelto lo delicado (tipo por bytes, nosniff y nombre
        // limpio) y allí lo pide también el líder al resolver la solicitud, sin una segunda ruta.
        grupo.MapPost("/vacaciones/{id:int}/respaldo", async (
            int id, IFormFile archivo, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar. Validar también lo comprueba, pero para entonces el
            // archivo ya estaría entero en la memoria de la API, que es justo lo que se quiere evitar
            // de una subida desmedida.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Rechazo($"El respaldo pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ, en el servidor: a esta ruta se puede llamar sin
            // pasar por el navegador, así que lo que filtre el cliente no es una barrera. Se admite
            // cualquier documento y no solo imágenes —un respaldo suele ser un PDF o un .docx—; el
            // filtro de lo peligroso lo pone ArchivosSubidos con su lista de extensiones.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, contenido);
            if (!valido) return Rechazo(error);

            var (ok, mensaje) = await vacaciones.AdjuntarRespaldoAsync(id, contenido, nombreSeguro, ct);
            return Resultado(ok, mensaje);
        })
        // El testigo antifalsificación lo exige el middleware por ser multipart, y lo adjunta el
        // cliente sin que la pantalla tenga que acordarse (ClienteApi.SubirAsync).
        .WithSummary("Sube o reemplaza el documento de respaldo de unas vacaciones propias aún pendientes");

        // ── Permisos ─────────────────────────────────────────────────────────────

        grupo.MapGet("/permisos/mios", async (AusenciasService ausencias, CancellationToken ct) =>
            Results.Ok(await ausencias.MisPermisosAsync(ct)))
        .WithSummary("Indicadores del año, permisos propios y catálogos del formulario");

        grupo.MapPost("/permisos", async (
            NuevaSolicitudDePermisoRequest cuerpo, AusenciasService ausencias, CancellationToken ct) =>
        {
            var (ok, mensaje, id) = await ausencias.SolicitarPermisoAsync(
                cuerpo.Tipo, cuerpo.Desde, cuerpo.Dias, cuerpo.Motivo, cuerpo.Notas, ct);
            return ok ? Results.Ok(new SolicitudCreadaDto(true, mensaje, id)) : Rechazo(mensaje);
        })
        .WithSummary("Solicita un permiso para uno mismo; queda pendiente de que el líder lo resuelva");

        grupo.MapPost("/permisos/{id:int}/cancelacion", async (
            int id, CancelacionRequest? cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.CancelarAsync(id, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Retira un permiso propio, esté pendiente o ya aprobado");

        grupo.MapPost("/permisos/{id:int}/eliminacion", async (
            int id, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra un permiso que nunca llegó a resolverse");

        // El justificante SUBE por aquí y BAJA por /api/adjuntos/permiso/{id}, que ya existe y sirve
        // todos los adjuntos guardados en la base con el mismo trato (tipo por bytes, nosniff, nombre
        // limpio). Duplicar esa bajada aquí sería tener dos sitios donde equivocarse.
        grupo.MapPost("/permisos/{id:int}/justificante", async (
            int id, IFormFile archivo, AusenciasService ausencias, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar. Validar también lo comprueba, pero para entonces el
            // archivo ya estaría entero en la memoria de la API, que es justo lo que se quiere evitar
            // de una subida desmedida.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Rechazo($"El justificante pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ, en el servidor: a esta ruta se puede llamar sin
            // pasar por el navegador, así que lo que filtre el cliente no es una barrera. El nombre
            // que vuelve es el ya saneado; el que escribió quien sube el archivo no se guarda.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, contenido);
            if (!valido) return Rechazo(error);

            var (ok, mensaje) = await ausencias.AdjuntarJustificanteAsync(id, contenido, nombreSeguro, ct);
            return Resultado(ok, mensaje);
        })
        // El testigo antifalsificación SÍ se exige aquí: es la única familia de peticiones que puede
        // provocarse desde otro sitio, porque un formulario multipart ajeno llegaría con la cookie de
        // sesión puesta. Lo adjunta el cliente sin que la pantalla tenga que acordarse
        // (ClienteApi.SubirAsync).
        .WithSummary("Sube o reemplaza el justificante de un permiso propio aún pendiente");
    }

    /// <summary>
    /// La respuesta de una operación de escritura. El mensaje lo escribió el servicio y se manda tal
    /// cual: explica el motivo en concreto —«No se puede eliminar una solicitud «Aprobada»: es parte
    /// del historial»— y eso es lo que permite saber qué hacer después.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje)) : Rechazo(mensaje);

    /// <summary>
    /// Un rechazo de negocio: 400 con el mensaje del servicio en <c>Detail</c>.
    ///
    /// La FORMA del cuerpo importa. El cliente lee toda respuesta fallida como ProblemDetails (ver
    /// <c>ClienteApi.DescribirAsync</c>) y se queda con <c>Detail</c>; cualquier otra forma acabaría
    /// enseñando un «no se pudo completar la operación» genérico y tirando por el camino justo lo que
    /// el servicio se molestó en explicar.
    /// </summary>
    private static IResult Rechazo(string mensaje) =>
        Results.Problem(detail: mensaje, statusCode: StatusCodes.Status400BadRequest, title: "No se pudo");
}
