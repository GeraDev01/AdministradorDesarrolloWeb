using AdminWeb.Api.Endpoints;
using Microsoft.AspNetCore.Mvc;

namespace AdminWeb.Api.Auth;

/// <summary>
/// Corta cualquier petición de una cuenta que todavía no ha activado su segundo factor.
///
/// <para><b>Es la misma pieza que <see cref="ContrasenaObligatoria"/>, escrita a propósito con la
/// misma forma.</b> No se inventó otro mecanismo porque ya hay uno probado en producción para
/// exactamente este problema —«esta cuenta no puede hacer nada hasta que arregle una cosa»— y dos
/// mecanismos distintos para el mismo problema significan dos sitios donde mirar cuando algo no
/// corta.</para>
///
/// <para><b>Se hace en el servidor y no escondiendo el menú</b>, por la misma razón de siempre: el
/// cliente Blazor corre en la máquina de cada persona y es manipulable. Quien todavía no tiene
/// segundo factor no debe poder tocar nada aunque llame a la API sin pasar por el navegador.</para>
///
/// <para><b>El orden con la contraseña temporal está decidido y no es arbitrario: primero la
/// contraseña.</b> Este middleware va DESPUÉS del de la contraseña, así que quien arrastra las dos
/// cosas se topa antes con el cambio de contraseña y las rutas del alta del segundo factor le quedan
/// cortadas hasta que la cambie. El motivo: la contraseña temporal se dicta por chat o en voz alta, y
/// mientras siga viva cualquiera que la haya visto puede entrar. Si esa persona pudiera dar de alta
/// el segundo factor, daría de alta SU teléfono, y a partir de ese momento sería la dueña de la
/// cuenta: la víctima ya no podría entrar ni siquiera sabiendo su contraseña, y la recuperación
/// pasaría por el líder. Al revés no hay daño equivalente: exigir cambiar primero la contraseña
/// significa que quien da de alta el teléfono es quien conoce una contraseña que solo él ha
/// escrito.</para>
/// </summary>
public static class SegundoFactorObligatorio
{
    public static IApplicationBuilder UseSegundoFactorObligatorio(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            bool debeActivar = ctx.User?.HasClaim(ClaimsPersonalizados.SegundoFactorPendiente, "1") == true;

            if (debeActivar && !EsRutaPermitida(ctx.Request.Path))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Tienes que activar el segundo factor",
                    Detail = "Esta cuenta necesita el código de una aplicación de tu teléfono. " +
                             "Actívalo para poder continuar.",
                    Extensions = { ["code"] = AuthEndpoints.CodigoDebeActivarSegundoFactor }
                });
                return;
            }

            await next();
        });

    /// <summary>
    /// Lo que se deja pasar: lo justo para poder salir del atolladero, y nada más.
    ///
    /// <para>Se cierra también <c>/hubs</c>, que es la conexión en vivo, y esa línea es la que
    /// distingue esto de una comprobación a medias: por ahí viajan la presencia y el latido del
    /// cronómetro, así que sin cerrarla una cuenta sin segundo factor podría seguir apareciendo como
    /// presente y acumulando horas mientras la API le contesta 403 a todo lo demás.</para>
    ///
    /// <para><c>/api/auth/change-password</c> se permite porque el middleware de la contraseña ya
    /// pasó y puede haberla dejado pasar a ella: si aquí se cortara, quien arrastra las dos cosas se
    /// quedaría encerrado entre dos puertas —la primera lo manda a cambiar la contraseña y la segunda
    /// no le deja hacerlo—.</para>
    ///
    /// <para>El prefijo <c>/api/auth/segundo-factor</c> se abre ENTERO y no ruta por ruta. Es
    /// deliberado: lo que hay debajo o pertenece al alta, o exige tener ya el segundo factor activo
    /// —y a quien está aquí le falta justamente eso, así que el propio servicio lo rechaza—. Abrirlo
    /// por partes obligaría a acordarse de esta lista cada vez que se añada una ruta, y el olvido
    /// típico sería dejar fuera la del alta, que es la única salida.</para>
    /// </summary>
    private static bool EsRutaPermitida(PathString ruta) =>
        (!ruta.StartsWithSegments("/api") && !ruta.StartsWithSegments("/hubs"))   // el cliente y sus estáticos
        || ruta.StartsWithSegments("/api/auth/segundo-factor")
        || ruta.StartsWithSegments("/api/auth/change-password")
        || ruta.StartsWithSegments("/api/auth/me")
        || ruta.StartsWithSegments("/api/auth/logout")
        || ruta.StartsWithSegments("/api/health")
        || ruta.StartsWithSegments("/api/version");
}
