using System.Security.Claims;
using AdminWeb.Api.Auth;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AdminWeb.Api.Endpoints;

/// <summary>Entrada, salida y cambio de contraseña.</summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Código que el cliente reconoce para llevar a la pantalla de cambio obligatorio. Se manda como
    /// dato y no como texto del mensaje: un mensaje se reescribe cualquier día y el cliente dejaría
    /// de reaccionar sin que nada avise.
    /// </summary>
    public const string CodigoDebeCambiarContrasena = "MUST_CHANGE_PASSWORD";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Auth");

        // Anónimo por necesidad: es la puerta.
        grupo.MapPost("/login", async (
            LoginRequest req, AuthService auth, HttpContext ctx, CancellationToken ct) =>
        {
            var (exito, mensaje, usuario) = await auth.LoginAsync(req.Usuario, req.Contrasena, ct);
            if (!exito || usuario == null)
                // 401 y no 400: el problema es la credencial, no la forma de la petición.
                return Results.Json(new ResultadoDto(false, mensaje), statusCode: StatusCodes.Status401Unauthorized);

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                ConstruirPrincipal(usuario),
                new AuthenticationProperties { IsPersistent = true });

            return Results.Ok(ADto(usuario));
        })
        .AllowAnonymous()
        .WithSummary("Inicia sesión y emite la cookie de sesión");

        grupo.MapPost("/logout", async (HttpContext ctx, AuditService audit, ICurrentUser actual, CancellationToken ct) =>
        {
            if (actual.IsLoggedIn)
                await audit.RecordAsync(Shared.Enums.AuditAction.Logout, "User", actual.UserId?.ToString(), ct: ct);

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new ResultadoDto(true, "Sesión cerrada."));
        })
        .WithSummary("Cierra la sesión");

        // El cliente lo llama al arrancar para saber si ya hay sesión y qué menú pintar.
        grupo.MapGet("/me", async (ICurrentUser actual, AuthService auth, CancellationToken ct) =>
        {
            if (actual.UserId is not int id) return Results.Unauthorized();

            var usuario = await auth.ObtenerAsync(id, ct);
            return usuario == null ? Results.Unauthorized() : Results.Ok(ADto(usuario));
        })
        .WithSummary("Devuelve la sesión actual");

        // El testigo que acompaña a las subidas de archivos. Solo lo necesitan esas: una petición con
        // cuerpo JSON obliga al navegador a preguntar antes (preflight) y sin política CORS no pasa,
        // pero un formulario multipart de otro sitio sí llegaría con la cookie puesta.
        //
        // Que se pida por separado y no viaje en cada respuesta es lo que permite que el cliente lo
        // guarde una vez por sesión en lugar de arrastrarlo en todas partes.
        grupo.MapGet("/antiforgery", (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var testigos = antiforgery.GetAndStoreTokens(ctx);
            return Results.Ok(new TestigoDto(testigos.RequestToken ?? ""));
        })
        .WithSummary("Emite el testigo antiforgery para las subidas de archivos");

        // Deliberadamente NO recibe userId: cada quien cambia la suya. Un id por parámetro, aunque
        // hoy lo protegiera una comprobación, basta que un llamador futuro lo pase mal para que
        // alguien cambie la contraseña de otro.
        grupo.MapPost("/change-password", async (
            CambioContrasenaRequest req, AuthService auth, ICurrentUser actual,
            HttpContext ctx, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int id) return Results.Unauthorized();

            if (req.NuevaContrasena != req.Confirmacion)
                return Results.BadRequest(new ResultadoDto(false, "Las dos contraseñas no coinciden."));

            var (ok, mensaje) = await auth.ChangePasswordAsync(id, req.NuevaContrasena, ct);
            if (!ok) return Results.BadRequest(new ResultadoDto(false, mensaje));

            // La cookie lleva el sello viejo y el claim de «debe cambiar»: hay que reemitirla o la
            // persona seguiría atrapada en la pantalla de cambio que acaba de completar.
            var usuario = await auth.ObtenerAsync(id, ct);
            if (usuario != null)
                await ctx.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    ConstruirPrincipal(usuario),
                    new AuthenticationProperties { IsPersistent = true });

            return Results.Ok(new ResultadoDto(true, mensaje));
        })
        .WithSummary("Cambia la contraseña propia");
    }

    private static UsuarioSesionDto ADto(User u) =>
        new(u.Id, u.Username, u.FullName, u.Role, u.DeveloperId, u.MustChangePassword);

    private static ClaimsPrincipal ConstruirPrincipal(User u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Username),
            new(ClaimTypes.GivenName, u.FullName),
            new(ClaimTypes.Role, u.Role.ToString()),
            new(ClaimsPersonalizados.SecurityStamp, u.SecurityStamp)
        };

        if (u.DeveloperId is int dev)
            claims.Add(new Claim(ClaimsPersonalizados.DeveloperId, dev.ToString()));

        if (u.MustChangePassword)
            claims.Add(new Claim(ClaimsPersonalizados.MustChangePassword, "1"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
