using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Por aquí salen los bytes de todo lo que está guardado en la base: capturas del foro, evidencias
/// de una actividad, justificantes de un permiso, respaldos de unas vacaciones y los documentos de
/// un requerimiento.
///
/// Van juntos y no repartidos por módulo porque comparten exactamente el mismo trato —todos
/// devuelven un BLOB con su nombre y su tipo— y porque la parte delicada conviene tenerla escrita
/// una sola vez: el tipo se decide por los BYTES, se manda <c>nosniff</c> y el nombre se limpia. Ver
/// <see cref="ResultadosDeArchivo.Adjunto"/>.
///
/// <para>El permiso NO se decide aquí: lo comprueba cada servicio antes de soltar los bytes. El foro
/// exige que la entrada siga viva; la evidencia, el justificante y el respaldo, que quien pide sea su
/// dueño o administrador (<c>RequireOwnershipOrAdmin</c>); los documentos de un requerimiento, que sea
/// el líder, porque esa pantalla es suya entera. Pedir lo ajeno lanza y sale 403; lo que no existe o
/// está retirado vuelve vacío y sale 404.</para>
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
        .WithSummary("El original de una imagen del foro (la miniatura ya viaja con el hilo)");

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
