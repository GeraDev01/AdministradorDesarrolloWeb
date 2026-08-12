using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Por aquí salen los bytes de todo lo que está guardado en la base: capturas del foro, imágenes de
/// un artículo de la base de conocimiento, evidencias de una actividad, justificantes de un permiso,
/// respaldos de unas vacaciones y los documentos de un requerimiento.
///
/// Van juntos y no repartidos por módulo porque comparten exactamente el mismo trato —todos
/// devuelven un BLOB con su nombre y su tipo— y porque la parte delicada conviene tenerla escrita
/// una sola vez: el tipo se decide por los BYTES, se manda <c>nosniff</c> y el nombre se limpia. Ver
/// <see cref="ResultadosDeArchivo.Adjunto"/>.
///
/// <para>El permiso lo comprueba, como regla, cada servicio antes de soltar los bytes: el foro exige
/// que la entrada siga viva; la imagen de un artículo, que a quien pide le toque ver ESE artículo
/// —un borrador es privado también en sus capturas, y lo retirado deja de servirse—; la evidencia, el
/// justificante y el respaldo, que quien pide sea su dueño o administrador
/// (<c>RequireOwnershipOrAdmin</c>); los documentos de un requerimiento, que sea el líder, porque esa
/// pantalla es suya entera. Pedir lo ajeno lanza y sale 403; lo que no existe o está retirado vuelve
/// vacío y sale 404.</para>
///
/// <para><b>Una excepción, y con motivo: la ruta del foro.</b> Lleva política propia
/// (<c>AdminUDesarrollador</c>), la misma del grupo <c>/api/foro</c>. Sin ella, cerrar el foro a
/// Operaciones no habría cerrado nada: el muro contestaría 403 y las capturas se seguirían bajando
/// por número desde aquí, que es la puerta que nadie recuerda. Va en el endpoint porque es donde se
/// nombra el foro; lo suyo es que además exista la guarda gemela dentro de
/// <c>ForumService.BytesDeImagenAsync</c> —dos barreras, como en el resto de la casa—, y mientras no
/// esté, esta línea es la única. NO la quites creyendo que el servicio ya lo cubre: compruébalo.</para>
/// </summary>
public static class AdjuntosEndpoints
{
    public static void MapAdjuntosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/adjuntos").WithTags("Adjuntos");

        grupo.MapGet("/foro/{id:int}", async (
            int id, HttpContext ctx, ForumService foro, CancellationToken ct) =>
        {
            var (bytes, nombre, _) = await foro.BytesDeImagenAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("El original de una imagen del foro (la miniatura ya viaja con el hilo)");

        // SIN política de rol, y aquí eso es lo correcto: un artículo publicado lo lee cualquiera con
        // sesión —Operaciones incluida—, igual que el grupo /api/conocimiento. Lo que decide es la
        // guarda del servicio, que pregunta por el ARTÍCULO al que pertenece la imagen; poner aquí
        // una política de rol dejaría fuera precisamente a quien despliega los sistemas que la
        // documentación explica.
        grupo.MapGet("/conocimiento/{id:int}", async (
            int id, HttpContext ctx, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (bytes, nombre) = await conocimiento.BytesDeImagenAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("Una imagen incrustada en un artículo de la base de conocimiento");

        grupo.MapGet("/actividad/{id:int}", async (
            int id, HttpContext ctx, DevActivityService actividades, CancellationToken ct) =>
        {
            var (bytes, nombre, _) = await actividades.BytesDeEvidenciaAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("La evidencia adjunta a una actividad libre");

        grupo.MapGet("/permiso/{id:int}", async (
            int id, HttpContext ctx, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (bytes, nombre) = await permisos.AdjuntoAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("El justificante de un permiso");

        // Una sola ruta para los dos que lo miran: quien pidió las vacaciones y el líder que las
        // resuelve. La guarda del servicio es «el dueño o el líder», así que los dos pasan por aquí y
        // no hay dos comprobaciones de permiso que puedan acabar diciendo cosas distintas.
        grupo.MapGet("/vacaciones/{id:int}", async (
            int id, HttpContext ctx, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var (bytes, nombre) = await vacaciones.AdjuntoAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("El documento de respaldo de una solicitud de vacaciones");

        grupo.MapGet("/requerimiento/{id:int}", async (
            int id, HttpContext ctx, RequirementAttachmentService adjuntos, CancellationToken ct) =>
        {
            var (bytes, nombre) = await adjuntos.BytesAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, bytes, nombre);
        })
        .WithSummary("Un documento de requerimiento o de estimación");
    }
}
