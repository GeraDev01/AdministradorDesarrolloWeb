using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El dashboard. Una sola lectura que devuelve la pantalla entera.
///
/// Es un único GET a propósito: son seis conteos y cuatro listas que se pintan juntos y se refrescan
/// juntos, y partirlos en cinco llamadas solo conseguiría que la pantalla se dibujara a trozos.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        grupo.MapGet("/", async (DashboardQueryService consulta, CancellationToken ct) =>
            Results.Ok(await consulta.ObtenerAsync(ct)))
        // Operaciones NO entra: el dashboard enseña carga del equipo, recordatorios internos y
        // ranking de desempeño, y nada de eso corresponde a un área cuyo alcance son los despliegues.
        // El menú del cliente ya lo omite, pero eso es comodidad; la barrera es esta línea.
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Dashboard, ya recortado a lo que le toca ver a quien pregunta");
    }
}
