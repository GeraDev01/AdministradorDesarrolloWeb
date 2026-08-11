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

/// <summary>
/// La puerta: entrada en dos tramos, salida, cambio de contraseña y todo lo del segundo factor.
///
/// <para><b>Por qué el segundo factor vive AQUÍ y no en su propio archivo de endpoints.</b> Porque
/// es la misma puerta. El alta, el código, los equipos recordados y los códigos de rescate solo
/// existen en función de cómo se entra, y separarlos habría dejado la decisión repartida entre dos
/// archivos que hay que leer juntos para entender qué protege qué. La única parte que no está aquí
/// es el REINICIO que hace el líder desde la pantalla de Usuarios: esa es una operación sobre la
/// cuenta de otra persona y pertenece a la administración de usuarios, no a la puerta.</para>
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Código que el cliente reconoce para llevar a la pantalla de cambio obligatorio. Se manda como
    /// dato y no como texto del mensaje: un mensaje se reescribe cualquier día y el cliente dejaría
    /// de reaccionar sin que nada avise.
    /// </summary>
    public const string CodigoDebeCambiarContrasena = "MUST_CHANGE_PASSWORD";

    /// <summary>El equivalente para el alta del segundo factor. Mismo trato y por el mismo motivo.</summary>
    public const string CodigoDebeActivarSegundoFactor = "MUST_ENROLL_2FA";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Auth");

        // ── TRAMO 1: usuario y contraseña ────────────────────────────────────────
        //
        // Anónimo por necesidad: es la puerta.
        //
        // Lo que hay que mirar de este endpoint es DÓNDE está el SignInAsync. Solo se llama en dos
        // sitios y los dos están detrás de una comprobación completa: cuando la cuenta no tiene
        // segundo factor, y cuando este navegador ya está recordado. En el camino que falta —hay
        // segundo factor y el navegador no se conoce— NO se emite ninguna cookie de ningún tipo.
        // Ése es todo el diseño: si el código todavía no se ha tecleado, para la API esta persona
        // sigue sin haber entrado.
        grupo.MapPost("/login", async (
            LoginRequest req, AuthService auth, SegundoFactorService segundo, TramoDeAcceso tramos,
            IWebHostEnvironment entorno, HttpContext ctx, CancellationToken ct) =>
        {
            var (exito, mensaje, usuario) = await auth.LoginAsync(req.Usuario, req.Contrasena, ct);
            if (!exito || usuario == null)
                // 401 y no 400: el problema es la credencial, no la forma de la petición.
                return Results.Json(new ResultadoDto(false, mensaje), statusCode: StatusCodes.Status401Unauthorized);

            if (usuario.SegundoFactorActivo)
            {
                // Dos comprobaciones independientes, y las dos hacen falta:
                //
                //  · TestigoVigente exige que la cookie se emitiera con el SELLO que la cuenta tiene
                //    ahora. Cualquier rotación del sello —cambio de contraseña, restablecimiento,
                //    baja de la cuenta— la deja sin valor sin que nadie tenga que acordarse de nada.
                //  · EsEquipoRecordadoAsync exige que el testigo sea de ESTA CUENTA y siga vigente en
                //    la base. Es lo que evita el fallo más tentador de todo esto: la cookie del
                //    equipo recordado de Ana no puede saltarse el segundo factor de Beto, ni aunque
                //    sea el mismo navegador y aunque Beto sepa su propia contraseña.
                var testigo = CookieDeEquipoRecordado.TestigoVigente(ctx, usuario);

                if (testigo != null && await segundo.EsEquipoRecordadoAsync(usuario.Id, testigo, ct))
                {
                    // El acceso quedó COMPLETO por este camino, así que aquí es donde se ponen a cero
                    // los contadores de bloqueo: LoginAsync ya no los toca cuando hay segundo factor
                    // —si lo hiciera, quien tuviera la contraseña probaría códigos sin límite— y este
                    // es el único final que no pasa por el servicio del segundo factor.
                    await auth.LimpiarBloqueoTrasAccesoCompletoAsync(usuario.Id, ct);

                    await EmitirSesionAsync(ctx, usuario);
                    await auth.RegistrarAccesoCompletadoAsync(
                        usuario, AuthService.ComoSePasoElSegundoFactor.EquipoRecordado, ct);

                    return Results.Ok(new RespuestaDeAccesoDto(false, null, ADto(usuario), "Equipo reconocido."));
                }

                // Traía una cookie que ya no vale —el sello cambió, la fila se borró, se olvidaron los
                // equipos, o simplemente venció—. Se retira del navegador aquí mismo: si no, la
                // seguiría mandando durante semanas para que se rechace cada vez, y quien mirase sus
                // cookies creería que su equipo sigue recordado.
                if (CookieDeEquipoRecordado.TraeAlguna(ctx))
                    CookieDeEquipoRecordado.Borrar(ctx, entorno.IsDevelopment());

                return Results.Ok(new RespuestaDeAccesoDto(
                    true, tramos.Emitir(usuario), null,
                    "Teclea el código que muestra tu aplicación del teléfono."));
            }

            await EmitirSesionAsync(ctx, usuario);
            return Results.Ok(new RespuestaDeAccesoDto(false, null, ADto(usuario), null));
        })
        .AllowAnonymous()
        .WithSummary("Primer tramo del acceso: usuario y contraseña");

        // ── TRAMO 2: el código ───────────────────────────────────────────────────
        //
        // También anónimo, y no es un descuido: aquí todavía no hay sesión. Lo que autoriza es el
        // tramo —que solo el servidor pudo emitir y solo tras acertar la contraseña— más el código.
        grupo.MapPost("/login/segundo-factor", async (
            SegundoFactorLoginRequest req, AuthService auth, SegundoFactorService segundo,
            TramoDeAcceso tramos, IWebHostEnvironment entorno, HttpContext ctx, CancellationToken ct) =>
        {
            const string tramoInvalido =
                "El paso caducó o dejó de ser válido. Vuelve a escribir tu usuario y tu contraseña.";

            if (tramos.Leer(req.Tramo) is not { } leido)
                return Results.Json(new ResultadoDto(false, tramoInvalido),
                    statusCode: StatusCodes.Status401Unauthorized);

            var usuario = await auth.ObtenerAsync(leido.UserId, ct);

            // El sello se vuelve a comprobar contra la base y no se da por bueno el que venía dentro
            // del tramo. Sin esto, cambiar la contraseña mientras alguien tiene un tramo en vuelo no
            // lo invalidaría, y ese es justo el momento en que hace falta que se caiga.
            if (usuario == null || !usuario.IsActive || usuario.SecurityStamp != leido.Sello)
                return Results.Json(new ResultadoDto(false, tramoInvalido),
                    statusCode: StatusCodes.Status401Unauthorized);

            // Una sola puerta para el código del teléfono y para el de rescate: el servicio los
            // distingue por su forma. Tener dos puertas habría significado dos sitios donde aplicar
            // el bloqueo por intentos, y el día que una se olvidara sería la que se usaría para
            // probar códigos sin límite.
            var resultado = await segundo.VerificarAsync(usuario.Id, req.Codigo, ct);
            if (!resultado.Exito)
                return Results.Json(new ResultadoDto(false, resultado.Mensaje),
                    statusCode: StatusCodes.Status401Unauthorized);

            if (req.RecordarEquipo)
            {
                var testigo = await segundo.RecordarEsteEquipoAsync(usuario.Id, DescribirNavegador(ctx), ct);
                CookieDeEquipoRecordado.Poner(ctx, usuario, testigo, entorno.IsDevelopment());
            }

            await EmitirSesionAsync(ctx, usuario);
            await auth.RegistrarAccesoCompletadoAsync(usuario, resultado.FueCodigoDeRescate
                ? AuthService.ComoSePasoElSegundoFactor.CodigoDeRescate
                : AuthService.ComoSePasoElSegundoFactor.CodigoDelTelefono, ct);

            // El mensaje del servicio se conserva: cuando se entró con un código de rescate dice
            // cuántos quedan, y ese aviso es la diferencia entre enterarse a tiempo y descubrir que
            // no quedaba ninguno el día que hacía falta.
            return Results.Ok(new RespuestaDeAccesoDto(
                false, null, ADto(usuario),
                resultado.FueCodigoDeRescate ? resultado.Mensaje : null));
        })
        .AllowAnonymous()
        .WithSummary("Segundo tramo del acceso: el código del teléfono o uno de rescate");

        grupo.MapPost("/logout", async (
            HttpContext ctx, AuditService audit, ICurrentUser actual, CancellationToken ct) =>
        {
            if (actual.IsLoggedIn)
                await audit.RecordAsync(Shared.Enums.AuditAction.Logout, "User", actual.UserId?.ToString(), ct: ct);

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // La cookie del equipo recordado NO se toca al salir, y es deliberado: «este equipo es
            // mío» no deja de ser cierto porque alguien cierre su sesión al terminar el día. Si se
            // borrara aquí, la casilla de los treinta días no serviría de nada para quien cierra
            // sesión a diario, que es precisamente quien la marca.
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
            if (usuario != null) await EmitirSesionAsync(ctx, usuario);

            return Results.Ok(new ResultadoDto(true, mensaje));
        })
        .WithSummary("Cambia la contraseña propia");

        MapSegundoFactor(grupo);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL SEGUNDO FACTOR, DESDE DENTRO DE LA SESIÓN
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Todo lo que se hace con la sesión ya abierta: el alta obligatoria, los códigos de rescate y
    /// los equipos recordados.
    ///
    /// <para>Estas rutas son las únicas que el middleware del alta obligatoria deja pasar cuando
    /// falta activar el segundo factor. Todas exigen sesión: nada de esto es anónimo.</para>
    /// </summary>
    private static void MapSegundoFactor(RouteGroupBuilder grupo)
    {
        var dosFactores = grupo.MapGroup("/segundo-factor");

        dosFactores.MapGet("/estado", async (
            SegundoFactorService segundo, AuthService auth, ICurrentUser actual, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int id) return Results.Unauthorized();

            var estado = await segundo.EstadoAsync(id, ct);
            var equipos = await auth.EquiposRecordadosAsync(id, ct);

            return Results.Ok(new EstadoDeSegundoFactorDto(
                estado.Activo, estado.DesdeUtc, estado.CodigosDeRescateRestantes,
                equipos.Count, SegundoFactorService.DiasQueSeRecuerdaElEquipo));
        })
        .WithSummary("Cómo está mi segundo factor");

        // POST y no GET aunque «solo devuelva datos»: cada llamada GENERA un secreto nuevo y tira el
        // anterior. Con GET, el navegador podría repetirlo al recargar o al volver atrás y la persona
        // se encontraría con que el código QR que acaba de escanear ya no es el bueno.
        dosFactores.MapPost("/alta", async (SegundoFactorService segundo, CancellationToken ct) =>
        {
            var (ok, mensaje, alta) = await segundo.ComenzarAltaAsync(ct);
            if (!ok || alta == null) return Results.BadRequest(new ResultadoDto(false, mensaje));

            return Results.Ok(new InicioDeAltaDto(
                alta.SecretoEnBase32,
                AgruparDeCuatro(alta.SecretoEnBase32),
                alta.Uri,

                // El PNG viaja incrustado en la respuesta. Ver InicioDeAltaDto: una imagen con
                // dirección propia dejaría el secreto escrito en el historial y en los registros.
                "data:image/png;base64," + Convert.ToBase64String(alta.CodigoQrPng)));
        })
        .WithSummary("Empieza el alta del segundo factor y devuelve el código QR");

        dosFactores.MapPost("/confirmar", async (
            ConfirmarAltaRequest req, SegundoFactorService segundo, AuthService auth,
            ICurrentUser actual, HttpContext ctx, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int id) return Results.Unauthorized();

            var (ok, mensaje, codigos) = await segundo.ConfirmarAltaAsync(req.Codigo, ct);
            if (!ok || codigos == null) return Results.BadRequest(new ResultadoDto(false, mensaje));

            // Igual que al cambiar la contraseña: la cookie sigue llevando el claim de «te falta el
            // segundo factor», así que hay que reemitirla o la persona quedaría atrapada en la
            // pantalla de alta que acaba de completar. Es el mismo fallo, en el mismo sitio, con la
            // misma cura.
            var usuario = await auth.ObtenerAsync(id, ct);
            if (usuario != null) await EmitirSesionAsync(ctx, usuario);

            return Results.Ok(new AltaConfirmadaDto(mensaje, codigos));
        })
        .WithSummary("Confirma el alta con un código y devuelve los códigos de rescate");

        dosFactores.MapPost("/regenerar-codigos", async (
            RegenerarCodigosRequest req, SegundoFactorService segundo, CancellationToken ct) =>
        {
            var (ok, mensaje, codigos) = await segundo.RegenerarCodigosDeRescateAsync(req.Codigo, ct);
            return ok && codigos != null
                ? Results.Ok(new AltaConfirmadaDto(mensaje, codigos))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Emite ocho códigos de rescate nuevos (exige un código del teléfono)");

        dosFactores.MapGet("/equipos", async (
            AuthService auth, ICurrentUser actual, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int id) return Results.Unauthorized();

            return Results.Ok(await auth.EquiposRecordadosAsync(id, ct));
        })
        .WithSummary("Los navegadores a los que ya no se les pide el código");

        dosFactores.MapPost("/equipos/olvidar", async (
            AuthService auth, ICurrentUser actual, IWebHostEnvironment entorno,
            HttpContext ctx, CancellationToken ct) =>
        {
            AuthorizationGuard.RequireLoggedIn(actual);
            if (actual.UserId is not int id) return Results.Unauthorized();

            int cuantos = await auth.OlvidarEquiposRecordadosAsync(id, ct);

            // Y se retira también la cookie de ESTE navegador, que si no se quedaría apuntando a una
            // fila que ya no existe. No es imprescindible —el testigo ya no vale— pero sí es lo que
            // hace que el resultado se corresponda con lo que la persona ve en su navegador.
            CookieDeEquipoRecordado.Borrar(ctx, entorno.IsDevelopment());

            return Results.Ok(new ResultadoDto(true, cuantos == 0
                ? "No tenías ningún equipo recordado."
                : $"Se olvidaron {cuantos} equipo(s). En todos ellos se volverá a pedir el código."));
        })
        .WithSummary("Deja de confiar en todos mis navegadores recordados");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  Piezas comunes
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emite la cookie de sesión. <b>Es el único sitio de la API que lo hace</b>, para que la
    /// pregunta «¿desde dónde se puede quedar alguien dentro?» tenga una sola respuesta.
    /// </summary>
    private static Task EmitirSesionAsync(HttpContext ctx, User usuario) =>
        ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ConstruirPrincipal(usuario),
            new AuthenticationProperties { IsPersistent = true });

    private static UsuarioSesionDto ADto(User u) =>
        new(u.Id, u.Username, u.FullName, u.Role, u.DeveloperId, u.MustChangePassword, !u.SegundoFactorActivo);

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

        if (!u.SegundoFactorActivo)
            claims.Add(new Claim(ClaimsPersonalizados.SegundoFactorPendiente, "1"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// Parte la clave en grupos de cuatro para poder teclearla sin equivocarse.
    ///
    /// <para>Son treinta y dos caracteres seguidos: escritos de corrido, quien los copia a mano
    /// pierde la cuenta y se salta uno. El fallo no se ve al teclearlos —el alta simplemente no
    /// confirma nunca— y la conclusión que saca cualquiera es «el segundo factor no funciona».</para>
    /// </summary>
    private static string AgruparDeCuatro(string texto)
    {
        var partes = new List<string>((texto.Length / 4) + 1);
        for (int i = 0; i < texto.Length; i += 4)
            partes.Add(texto.Substring(i, Math.Min(4, texto.Length - i)));

        return string.Join(' ', partes);
    }

    /// <summary>
    /// Cómo llamar a este navegador en la lista de equipos recordados.
    ///
    /// <para><b>Es una pista y no una identificación</b>, y por eso se resume a lo que una persona
    /// puede reconocer en vez de guardar la cadena entera. Lo que el navegador dice de sí mismo lo
    /// puede decir cualquiera, así que esto no decide nada: no se compara, no se valida y no forma
    /// parte de la confianza. Solo sirve para que quien mire la lista sepa cuál es cuál antes de
    /// olvidarlos todos.</para>
    ///
    /// <para>El orden de las comprobaciones importa: Edge se anuncia también como Chrome y como
    /// Safari, y Chrome se anuncia como Safari. Comprobado de más específico a más general, cada uno
    /// sale con su nombre; al revés, todos serían Safari.</para>
    /// </summary>
    private static string DescribirNavegador(HttpContext ctx)
    {
        var agente = ctx.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(agente)) return "Equipo sin identificar";

        string navegador =
            agente.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            agente.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            agente.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            agente.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            agente.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" : "Navegador";

        string sistema =
            agente.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            agente.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            agente.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            agente.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            agente.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            agente.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "sistema desconocido";

        return $"{navegador} en {sistema}";
    }
}
