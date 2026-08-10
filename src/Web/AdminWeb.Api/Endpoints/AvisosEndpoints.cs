using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Dtos.Avisos;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La bandeja de avisos del usuario.
///
/// No lleva política de rol: los avisos los tiene todo el mundo, sea cual sea su papel, y la
/// política de reserva de la aplicación (sesión iniciada) es exactamente la barrera que hace falta.
/// Lo que sí es innegociable es que NINGUNA ruta recibe un identificador de usuario: cada quien lee
/// la suya y el destinatario sale de la sesión. Un id por parámetro, aunque hoy lo protegiera una
/// comprobación, basta que un llamador futuro lo pase mal para que alguien lea la correspondencia de
/// otro.
///
/// «Marcar leído» y «marcar todo leído» escriben, y son la única escritura de esta fase: sin ellas la
/// pantalla no sirve para nada: una bandeja donde todo queda no leído para siempre es una lista.
/// </summary>
public static class AvisosEndpoints
{
    /// <summary>Cuántos avisos por página si nadie dice otra cosa. Caben de sobra en una pantalla.</summary>
    private const int TamanoPaginaPorOmision = 25;

    public static void MapAvisosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/avisos").WithTags("Avisos");

        grupo.MapGet("/", async (
            AvisosQueryService consulta, CancellationToken ct,
            int pagina = 1, int tamanoPagina = TamanoPaginaPorOmision) =>
            Results.Ok(await consulta.PaginaAsync(pagina, tamanoPagina, ct)))
        .WithSummary("Página de los avisos propios, del más reciente al más viejo");

        // Lo consume el contador del menú, que se refresca a menudo: por eso es su propia ruta y no
        // un campo colgado de la rejilla.
        grupo.MapGet("/contador", async (
            NotificationService avisos, ICurrentUser actual, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int usuario) return Results.Unauthorized();

            return Results.Ok(new ContadorAvisosDto(await avisos.CountUnreadAsync(usuario, ct)));
        })
        .WithSummary("Cuántos avisos sin leer tiene la sesión actual");

        grupo.MapPost("/{id:int}/leido", async (
            int id, AvisosQueryService consulta, NotificationService avisos, CancellationToken ct) =>
        {
            // La pertenencia se comprueba ANTES de marcar: MarkReadAsync recibe solo el id y no sabe
            // de quién es el aviso. Y se responde 404 y no 403 porque un «no tienes permiso» sobre un
            // id concreto confirma que ese aviso existe, que es justo lo que no hay que confirmarle a
            // quien está probando números.
            if (!await consulta.EsMioAsync(id, ct))
                return Results.NotFound(new ResultadoDto(false, "Ese aviso ya no existe. Recarga la lista."));

            await avisos.MarkReadAsync(id, ct);
            return Results.Ok(new ResultadoDto(true, "Aviso marcado como leído."));
        })
        .WithSummary("Marca como leído un aviso propio");

        grupo.MapPost("/leidos", async (
            NotificationService avisos, ICurrentUser actual, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int usuario) return Results.Unauthorized();

            await avisos.MarkAllReadAsync(usuario, ct);
            return Results.Ok(new ResultadoDto(true, "Todos tus avisos quedaron marcados como leídos."));
        })
        .WithSummary("Marca como leídos todos los avisos propios");

        // ── Avisos con la pestaña cerrada ────────────────────────────────────────
        //
        // Tampoco llevan identificador de usuario, por lo mismo: la suscripción es del navegador de
        // quien tiene la sesión. Lo único que viaja es lo que el propio navegador generó.

        grupo.MapGet("/push/configuracion", (AvisosPushService push) =>
            Results.Ok(new ConfiguracionDePushDto(push.Disponible, push.LlavePublica)))
        .WithSummary("Si hay avisos push disponibles y con qué llave suscribirse");

        grupo.MapPost("/push/suscripcion", async (
            SuscripcionPushRequest cuerpo, AvisosPushService push, CancellationToken ct) =>
        {
            var (ok, mensaje) = await push.SuscribirAsync(
                cuerpo.Endpoint, cuerpo.P256dh, cuerpo.Auth, cuerpo.Descripcion, ct);

            return ok ? Results.Ok(new ResultadoDto(true, mensaje))
                      : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Registra este navegador para recibir avisos con la pestaña cerrada");

        grupo.MapPost("/push/cancelacion", async (
            CancelarPushRequest cuerpo, AvisosPushService push, CancellationToken ct) =>
        {
            var (ok, mensaje) = await push.CancelarAsync(cuerpo.Endpoint, ct);
            return Results.Ok(new ResultadoDto(ok, mensaje));
        })
        .WithSummary("Retira el permiso de este navegador");
    }
}
