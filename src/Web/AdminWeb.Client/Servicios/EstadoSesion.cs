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
/// </summary>
public class EstadoSesion(HttpClient http)
{
    public UsuarioSesionDto? Usuario { get; private set; }
    public bool HaySesion => Usuario != null;

    /// <summary>Avisa a la interfaz cuando alguien entra o sale, para repintar el menú.</summary>
    public event Action? Cambio;

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

    public async Task<(bool ok, string mensaje)> EntrarAsync(string usuario, string contrasena)
    {
        var respuesta = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(usuario, contrasena));

        if (!respuesta.IsSuccessStatusCode)
        {
            var error = await LeerResultado(respuesta);
            return (false, error);
        }

        Usuario = await respuesta.Content.ReadFromJsonAsync<UsuarioSesionDto>();
        Cambio?.Invoke();
        return (true, "OK");
    }

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
