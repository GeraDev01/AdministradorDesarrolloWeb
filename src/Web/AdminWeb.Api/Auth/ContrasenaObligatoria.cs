using System.Security.Claims;
using AdminWeb.Api.Endpoints;
using AdminWeb.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace AdminWeb.Api.Auth;

/// <summary>
/// Dos reglas que solo tienen sentido cuando la sesión vive en una cookie y no en un proceso.
/// </summary>
public static class ContrasenaObligatoria
{
    /// <summary>
    /// Corta cualquier petición de una cuenta que todavía no ha cambiado su contraseña temporal.
    ///
    /// Se hace en el servidor y no escondiendo el menú: quien entra con una contraseña que le
    /// dictaron por chat no debe poder tocar nada mientras esa contraseña siga siendo válida, y el
    /// cliente Blazor corre en su máquina y es manipulable. Se dejan pasar las rutas
    /// imprescindibles para poder salir del atolladero: cambiar la contraseña, saber quién soy y
    /// cerrar sesión.
    /// </summary>
    public static IApplicationBuilder UseContrasenaObligatoria(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            bool debeCambiar = ctx.User?.HasClaim(ClaimsPersonalizados.MustChangePassword, "1") == true;

            if (debeCambiar && !EsRutaPermitida(ctx.Request.Path))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Tienes que cambiar tu contraseña",
                    Detail = "Estás usando una contraseña temporal. Cámbiala para poder continuar.",
                    Extensions = { ["code"] = AuthEndpoints.CodigoDebeCambiarContrasena }
                });
                return;
            }

            await next();
        });

    private static bool EsRutaPermitida(PathString ruta) =>
        !ruta.StartsWithSegments("/api")                                  // el cliente y sus estáticos
        || ruta.StartsWithSegments("/api/auth/change-password")
        || ruta.StartsWithSegments("/api/auth/me")
        || ruta.StartsWithSegments("/api/auth/logout")
        || ruta.StartsWithSegments("/api/health");

    /// <summary>
    /// Revalida que la cookie siga valiendo: que la cuenta exista, siga activa y conserve el mismo
    /// sello. Es lo que hace que cambiar la contraseña o desactivar a alguien lo eche de verdad, en
    /// vez de tener que esperar a que su cookie expire sola dentro de unas horas.
    ///
    /// Se comprueba cada pocos minutos y no en cada petición: verificar contra la base en cada
    /// llamada convertiría una comprobación de seguridad en el cuello de botella de toda la API.
    /// </summary>
    public static readonly TimeSpan IntervaloRevalidacion = TimeSpan.FromMinutes(5);

    public static async Task ValidarSelloAsync(CookieValidatePrincipalContext ctx)
    {
        var emitida = ctx.Properties.IssuedUtc;
        if (emitida != null && DateTimeOffset.UtcNow - emitida < IntervaloRevalidacion) return;

        var principal = ctx.Principal;
        if (principal == null) { await Rechazar(ctx); return; }

        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            await Rechazar(ctx);
            return;
        }

        var auth = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
        var usuario = await auth.ObtenerAsync(userId, ctx.HttpContext.RequestAborted);

        var selloDeLaCookie = principal.FindFirstValue(ClaimsPersonalizados.SecurityStamp);
        if (usuario == null || !usuario.IsActive || usuario.SecurityStamp != selloDeLaCookie)
        {
            await Rechazar(ctx);
            return;
        }

        // Sigue siendo válida: se renueva para que el próximo control vuelva a caer dentro de plazo.
        ctx.ShouldRenew = true;
    }

    private static async Task Rechazar(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
