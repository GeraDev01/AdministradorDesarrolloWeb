using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;

namespace AdminWeb.Api.Endpoints;

/// <summary>Preferencias de interfaz propias. Cada quien las suyas; no hay id de usuario en la ruta.</summary>
public static class PreferenciasEndpoints
{
    public static void MapPreferenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/preferencias").WithTags("Preferencias");

        grupo.MapGet("/{clave}", async (string clave, UserPreferencesService prefs, CancellationToken ct) =>
        {
            var json = await prefs.LeerAsync(clave, ct);
            // Sin preferencia guardada NO es un error: es alguien que aún no ha tocado nada, que es
            // el caso normal. Se devuelve vacío y la pantalla usa su configuración por omisión.
            return Results.Ok(new PreferenciaDto(clave, json));
        })
        .WithSummary("Lee una preferencia propia");

        grupo.MapPut("/{clave}", async (
            string clave, PreferenciaDto cuerpo, UserPreferencesService prefs, CancellationToken ct) =>
        {
            var (ok, mensaje) = await prefs.GuardarAsync(clave, cuerpo.Json, ct);
            return ok ? Results.Ok(new ResultadoDto(true, mensaje))
                      : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Guarda una preferencia propia (vacío la restablece)");
    }
}
