using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Quién está dentro, según el servidor.
///
/// El cliente NO guarda ningún token: la sesión viaja en una cookie HttpOnly que este código ni
/// siquiera puede leer. Por eso «saber quién soy» es preguntárselo a la API (<c>/api/auth/me</c>) en
/// vez de decodificar nada localmente — y por eso un XSS no puede robar la sesión.
///
/// <para>Aquí vive también el acceso EN DOS TRAMOS y todo lo que la persona puede hacer con su
/// segundo factor. Está junto y no repartido porque es la misma conversación con la misma puerta, y
/// porque estas llamadas van por el cliente SIN el manejador de respuestas —ver Program.cs—: un 401
/// respondiendo «ese código no es» no debe echar a nadie a la pantalla de acceso, que es justo donde
/// ya está.</para>
/// </summary>
public class EstadoSesion(HttpClient http)
{
    public UsuarioSesionDto? Usuario { get; private set; }
    public bool HaySesion => Usuario != null;

    /// <summary>Avisa a la interfaz cuando alguien entra o sale, para repintar el menú.</summary>
    public event Action? Cambio;

    /// <summary>
    /// El acceso a medias: quién acertó su contraseña y está pendiente de teclear el código.
    ///
    /// <para><b>Privado y en memoria, nunca en almacenamiento local.</b> Guardarlo en el navegador lo
    /// dejaría escrito en disco para que lo leyera cualquier JavaScript de la página; en un campo
    /// muere al recargar, que es exactamente lo que debe pasarle a un acceso a medias.</para>
    ///
    /// <para>No es una credencial: sin el código no abre nada. Ver <c>TramoDeAcceso</c> en la API.</para>
    /// </summary>
    private string? _tramo;

    /// <summary>Hay un acceso empezado esperando el código.</summary>
    public bool EsperandoSegundoFactor => _tramo != null;

    /// <summary>
    /// Consulta al servidor si hay sesión. Devuelve null si no la hay — que en el arranque es lo
    /// normal y no un error.
    /// </summary>
    public async Task<UsuarioSesionDto?> RefrescarAsync()
    {
        try
        {
            var respuesta = await http.GetAsync("api/auth/me");
            Usuario = respuesta.StatusCode == HttpStatusCode.Unauthorized
                ? null
                : await respuesta.Content.ReadFromJsonAsync<UsuarioSesionDto>();
        }
        catch
        {
            // Sin red no se puede afirmar que la sesión sea inválida; se trata como «no sé todavía».
            Usuario = null;
        }

        Cambio?.Invoke();
        return Usuario;
    }

    /// <summary>
    /// PRIMER TRAMO: usuario y contraseña.
    ///
    /// <para>Devuelve <c>faltaCodigo</c> en cierto cuando la cuenta tiene segundo factor y este
    /// navegador no está recordado. En ese caso <b>no hay sesión todavía</b>: <see cref="Usuario"/>
    /// sigue en nulo y la API sigue contestando 401 a todo. Lo único que queda guardado es el tramo.</para>
    /// </summary>
    public async Task<(bool ok, bool faltaCodigo, string mensaje)> EntrarAsync(string usuario, string contrasena)
    {
        _tramo = null;

        var respuesta = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(usuario, contrasena));
        if (!respuesta.IsSuccessStatusCode) return (false, false, await LeerResultado(respuesta));

        var acceso = await respuesta.Content.ReadFromJsonAsync<RespuestaDeAccesoDto>();

        if (acceso?.SegundoFactorRequerido == true)
        {
            _tramo = acceso.Tramo;
            return (true, true, acceso.Mensaje ?? "Teclea el código de tu aplicación.");
        }

        Usuario = acceso?.Usuario;
        Cambio?.Invoke();
        return (true, false, acceso?.Mensaje ?? "OK");
    }

    /// <summary>
    /// SEGUNDO TRAMO: el código del teléfono o uno de rescate, en el mismo campo.
    ///
    /// <para>Si el tramo caducó —cinco minutos— se avisa aquí mismo en vez de mandar una petición que
    /// se sabe que va a fallar, para que el mensaje diga qué hacer en lugar de «no autorizado».</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> CompletarSegundoFactorAsync(string codigo, bool recordarEquipo)
    {
        if (_tramo is not string tramo)
            return (false, "El acceso ya no está en curso. Vuelve a escribir tu usuario y tu contraseña.");

        var respuesta = await http.PostAsJsonAsync("api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(tramo, codigo, recordarEquipo));

        if (!respuesta.IsSuccessStatusCode) return (false, await LeerResultado(respuesta));

        var acceso = await respuesta.Content.ReadFromJsonAsync<RespuestaDeAccesoDto>();

        // El tramo se gasta al usarse: dejarlo vivo permitiría reintentar con él después de haber
        // entrado, y no hay ningún motivo legítimo para eso.
        _tramo = null;
        Usuario = acceso?.Usuario;
        Cambio?.Invoke();

        return (true, acceso?.Mensaje ?? "OK");
    }

    /// <summary>Abandona un acceso a medias (quien pulsa «volver» en el paso del código).</summary>
    public void OlvidarTramo() => _tramo = null;

    public async Task SalirAsync()
    {
        try { await http.PostAsync("api/auth/logout", null); }
        catch { /* si la red falla, la sesión local se limpia igual */ }

        Olvidar();
    }

    /// <summary>
    /// Borra la sesión de este lado sin avisar al servidor. Lo usa el manejador de respuestas cuando
    /// la API contesta que la sesión ya no vale: pedirle entonces que la cierre sería pedirle que
    /// cierre algo que ya cerró él.
    /// </summary>
    public void Olvidar()
    {
        Usuario = null;
        _tramo = null;
        Cambio?.Invoke();
    }

    public async Task<(bool ok, string mensaje)> CambiarContrasenaAsync(string nueva, string confirmacion)
    {
        var respuesta = await http.PostAsJsonAsync("api/auth/change-password",
            new CambioContrasenaRequest(nueva, confirmacion));

        var texto = await LeerResultado(respuesta);
        if (!respuesta.IsSuccessStatusCode) return (false, texto);

        // La contraseña temporal dejó de serlo: hay que releer la sesión o el usuario seguiría
        // atrapado en la pantalla de cambio que acaba de completar.
        await RefrescarAsync();
        return (true, texto);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  SEGUNDO FACTOR, YA DENTRO DE LA SESIÓN
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Cómo está mi segundo factor, con cuántos equipos tengo recordados.</summary>
    public async Task<EstadoDeSegundoFactorDto?> EstadoDeSegundoFactorAsync()
    {
        try { return await http.GetFromJsonAsync<EstadoDeSegundoFactorDto>("api/auth/segundo-factor/estado"); }
        catch { return null; }
    }

    /// <summary>
    /// Pide un alta nueva: secreto, código QR y clave en texto.
    ///
    /// <para>Cada llamada tira el alta anterior. La pantalla la hace UNA vez al abrirse, no en cada
    /// repintado: si no, el código QR cambiaría bajo la cámara del teléfono.</para>
    /// </summary>
    public async Task<(InicioDeAltaDto? alta, string mensaje)> ComenzarAltaAsync()
    {
        var respuesta = await http.PostAsync("api/auth/segundo-factor/alta", null);
        if (!respuesta.IsSuccessStatusCode) return (null, await LeerResultado(respuesta));

        return (await respuesta.Content.ReadFromJsonAsync<InicioDeAltaDto>(), "OK");
    }

    /// <summary>
    /// Confirma el alta con un código y recoge los ocho códigos de rescate.
    ///
    /// <para>Al terminar se relee la sesión: la API reemite la cookie sin el aviso de «te falta el
    /// segundo factor», y sin releerla el cliente seguiría creyendo que hace falta.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, IReadOnlyList<string> codigos)> ConfirmarAltaAsync(string codigo)
    {
        var respuesta = await http.PostAsJsonAsync("api/auth/segundo-factor/confirmar",
            new ConfirmarAltaRequest(codigo));

        if (!respuesta.IsSuccessStatusCode) return (false, await LeerResultado(respuesta), []);

        var hecho = await respuesta.Content.ReadFromJsonAsync<AltaConfirmadaDto>();
        await RefrescarAsync();

        return (true, hecho?.Mensaje ?? "Segundo factor activado.", hecho?.CodigosDeRescate ?? []);
    }

    /// <summary>Ocho códigos de rescate nuevos. Los anteriores dejan de valer.</summary>
    public async Task<(bool ok, string mensaje, IReadOnlyList<string> codigos)> RegenerarCodigosAsync(string codigo)
    {
        var respuesta = await http.PostAsJsonAsync("api/auth/segundo-factor/regenerar-codigos",
            new RegenerarCodigosRequest(codigo));

        if (!respuesta.IsSuccessStatusCode) return (false, await LeerResultado(respuesta), []);

        var hecho = await respuesta.Content.ReadFromJsonAsync<AltaConfirmadaDto>();
        return (true, hecho?.Mensaje ?? "Códigos nuevos.", hecho?.CodigosDeRescate ?? []);
    }

    /// <summary>Los navegadores a los que ya no se les pide el código.</summary>
    public async Task<IReadOnlyList<EquipoRecordadoDto>> EquiposRecordadosAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<EquipoRecordadoDto>>("api/auth/segundo-factor/equipos") ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Deja de confiar en todos ellos: la próxima vez volverán a pedir el código.</summary>
    public async Task<(bool ok, string mensaje)> OlvidarEquiposRecordadosAsync()
    {
        var respuesta = await http.PostAsync("api/auth/segundo-factor/equipos/olvidar", null);
        return (respuesta.IsSuccessStatusCode, await LeerResultado(respuesta));
    }

    private static async Task<string> LeerResultado(HttpResponseMessage r)
    {
        try
        {
            var dto = await r.Content.ReadFromJsonAsync<ResultadoDto>();
            if (!string.IsNullOrWhiteSpace(dto?.Mensaje)) return dto!.Mensaje;
        }
        catch { /* la respuesta pudo no ser un ResultadoDto */ }

        return r.IsSuccessStatusCode ? "Listo." : "No se pudo completar la operación.";
    }
}

/// <summary>
/// Traduce la sesión del servidor a lo que entienden AuthorizeView y [Authorize] del cliente.
///
/// Insistir en lo obvio porque se olvida: esto solo sirve para PINTAR (esconder un menú, redirigir).
/// La barrera real está en la API. Cualquiera puede modificar lo que corre en su propio navegador.
/// </summary>
public class ProveedorEstadoAutenticacion(EstadoSesion sesion) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var usuario = sesion.Usuario ?? await sesion.RefrescarAsync();
        if (usuario == null) return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Usuario),
            new(ClaimTypes.GivenName, usuario.NombreCompleto),
            new(ClaimTypes.Role, usuario.Rol.ToString())
        };
        if (usuario.DeveloperId is int dev) claims.Add(new Claim("developer_id", dev.ToString()));

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie")));
    }

    /// <summary>Lo llaman el login y el logout para que la interfaz se repinte al instante.</summary>
    public void Notificar() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
