using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// La API levantada en memoria, contra una base SQLite propia que se borra al terminar.
///
/// <b>Nunca toca una base de verdad</b>: la cadena de conexión y el proveedor se fijan aquí por
/// configuración, que es la misma vía por la que se configuran en producción. Así se prueba el
/// arranque real —migrador y siembra incluidos— sin ningún atajo que oculte un fallo de
/// configuración, que es justo lo que estas pruebas existen para atrapar.
///
/// Los trabajos de fondo se quedan APAGADOS, como en producción mientras el escritorio siga vivo.
/// Encenderlos aquí haría que las pruebas compitieran contra un barrido que consolida cronómetros
/// por debajo, y los fallos saldrían un día sí y otro no.
/// </summary>
public class ApiDePrueba : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _archivo =
        Path.Combine(Path.GetTempPath(), $"adminweb_api_{Guid.NewGuid():N}.db");

    private readonly CazadorDeContrasenaTemporal _cazador = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={_archivo}",
            ["AdminWeb:ProveedorDeBase"] = "Sqlite",
            ["AdminWeb:TrabajosDeFondoActivos"] = "false"
        }));

        // La contraseña del administrador inicial solo se dice una vez, por el registro de salida:
        // no se guarda en ningún sitio (a propósito, es su valor). En pruebas no hay consola que
        // mirar, así que se escucha el propio registro.
        builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(_cazador));

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Un cliente que conserva las cookies, que es lo que hace que estas pruebas signifiquen algo:
    /// sin ellas la sesión no viajaría y todo respondería 401 por el motivo equivocado.
    ///
    /// <para>Cada llamada trae un frasco de cookies NUEVO: es un navegador recién estrenado, sin
    /// sesión y sin equipo recordado. Las pruebas que miran la puerta desde fuera cuentan con eso.</para>
    /// </summary>
    public HttpClient NuevoCliente() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>La contraseña definitiva del administrador, una vez cambiada la temporal.</summary>
    public const string ContrasenaDelAdmin = "ClaveDePrueba123";

    /// <summary>La que se le pone a cualquier cuenta sembrada por <see cref="ClienteComoAsync"/>.</summary>
    private const string ContrasenaSembrada = "OtraClave123";

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LA PUESTA A PUNTO DE UNA CUENTA
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // Una cuenta recién creada NO puede usar la API: arrastra una contraseña temporal y le falta el
    // segundo factor, y el servidor corta todo lo demás mientras siga así. Eso es el producto
    // funcionando, no un estorbo de las pruebas, y por eso este andamiaje hace exactamente lo que
    // hará una persona el primer día — cambiar la contraseña y dar de alta el teléfono— pasando por
    // las rutas de verdad. Las pruebas empiezan donde empieza el trabajo real, en vez de tropezar
    // cada una con el mismo 403.
    //
    // LO ÚNICO DELICADO ES EL RELOJ. Un código de seis dígitos vale para una ventana de treinta
    // segundos y no se puede repetir: el servidor apunta la última ventana aceptada justamente para
    // que nadie reutilice un código visto. Consecuencia para estas pruebas: tras confirmar el alta,
    // el siguiente acceso caería DENTRO de la misma ventana y sería rechazado con toda la razón.
    //
    // Se resuelve como lo resolvería cualquiera: se entra UNA vez con un código de rescate —que no
    // dependen del reloj— marcando «recuerda este equipo», y a partir de ahí ese frasco de cookies
    // ya no necesita ningún código. De ahí que haya un frasco POR CUENTA y no uno por cliente.

    /// <summary>
    /// Un frasco de cookies por CUENTA, compartido por todos los clientes que se pidan para ella.
    ///
    /// <para>Es lo que hace que el equipo recordado sobreviva de una prueba a la siguiente. Cerrar
    /// sesión no lo vacía —la cookie del equipo no se retira al salir, a propósito—, así que una
    /// prueba que cierre sesión no deja a las demás sin poder entrar.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, CookieContainer> _frascos = new();

    /// <summary>Los códigos de rescate que le quedan sin gastar a cada cuenta de prueba.</summary>
    private readonly ConcurrentDictionary<string, Queue<string>> _codigosDeRescate = new();

    /// <summary>La puesta a punto de cada cuenta, hecha UNA sola vez por cuenta.</summary>
    private readonly Dictionary<string, Task> _preparaciones = [];
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    /// <summary>
    /// Un cliente con la sesión del administrador ya abierta, su contraseña ya cambiada y su segundo
    /// factor ya dado de alta.
    ///
    /// Nada de eso es comodidad: hasta que se hace, la API responde 403 a todo lo demás y cada
    /// prueba empezaría tropezando con eso en lugar de probar lo suyo.
    /// </summary>
    public async Task<HttpClient> ClienteAdminAsync()
    {
        await PrepararUnaVezAsync("admin", PrepararAlAdminAsync);
        return await ClienteConSesionAsync("admin", ContrasenaDelAdmin);
    }

    /// <summary>
    /// Siembra una cuenta con el rol pedido y devuelve un cliente con su sesión abierta.
    ///
    /// Se escribe directamente en la base porque lo que se quiere probar es otra cosa. Lo que importa
    /// es que la cuenta quede como la dejaría el alta real —activa, con su hash BCrypt y sin cambio
    /// obligatorio pendiente—, para que el 403 que se prueba venga de la política de rol y no de un
    /// usuario mal formado ni del alta del segundo factor pendiente.
    /// </summary>
    public async Task<HttpClient> ClienteComoAsync(UserRole rol, string usuario)
    {
        await PrepararUnaVezAsync(usuario, () => SembrarYPrepararAsync(rol, usuario));
        return await ClienteConSesionAsync(usuario, ContrasenaSembrada);
    }

    private async Task PrepararUnaVezAsync(string usuario, Func<Task> preparar)
    {
        Task tarea;

        await _cerrojo.WaitAsync();
        try
        {
            if (!_preparaciones.TryGetValue(usuario, out tarea!))
                _preparaciones[usuario] = tarea = preparar();
        }
        finally
        {
            _cerrojo.Release();
        }

        await tarea;
    }

    /// <summary>
    /// El primer día del administrador: entra con la temporal, la cambia y da de alta su segundo
    /// factor.
    ///
    /// <para>La temporal solo vale hasta que se cambia, y por eso esto ocurre una vez para toda la
    /// clase de pruebas: si cada prueba la usara para entrar, la primera en correr funcionaría y el
    /// resto fallaría con un 401 — y como xUnit no garantiza el orden, «la primera» sería una
    /// distinta cada vez.</para>
    /// </summary>
    private async Task PrepararAlAdminAsync()
    {
        var cliente = ClienteDe("admin");

        var acceso = await cliente.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin", ContrasenaTemporal));
        acceso.EnsureSuccessStatusCode();

        var cambio = await cliente.PostAsJsonAsync("/api/auth/change-password",
            new CambioContrasenaRequest(ContrasenaDelAdmin, ContrasenaDelAdmin));
        cambio.EnsureSuccessStatusCode();

        await DarDeAltaElSegundoFactorAsync(cliente, "admin", ContrasenaDelAdmin);
    }

    private async Task SembrarYPrepararAsync(UserRole rol, string usuario)
    {
        using (var ambito = Services.CreateScope())
        {
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(u => u.Username == usuario))
            {
                db.Users.Add(new User
                {
                    Username = usuario,
                    FullName = usuario,
                    Role = rol,
                    IsActive = true,
                    MustChangePassword = false,
                    PasswordHash = PasswordHasher.Hash(ContrasenaSembrada)
                });
                await db.SaveChangesAsync();
            }
        }

        var cliente = ClienteDe(usuario);
        var acceso = await cliente.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(usuario, ContrasenaSembrada));
        acceso.EnsureSuccessStatusCode();

        await DarDeAltaElSegundoFactorAsync(cliente, usuario, ContrasenaSembrada);
    }

    /// <summary>
    /// Da de alta el segundo factor por las rutas de verdad y deja este frasco de cookies como equipo
    /// recordado, para que las demás pruebas no tengan que teclear ningún código.
    ///
    /// <para>El acceso de después va con un CÓDIGO DE RESCATE y no con uno del teléfono, y es la
    /// línea que hay que entender de todo esto: el alta acaba de gastar la ventana de treinta
    /// segundos en curso, así que un código del teléfono sería rechazado por repetido —y con razón—.
    /// Los de rescate no dependen del reloj.</para>
    /// </summary>
    private async Task DarDeAltaElSegundoFactorAsync(HttpClient cliente, string usuario, string contrasena)
    {
        var inicio = await cliente.PostAsync("/api/auth/segundo-factor/alta", null);
        inicio.EnsureSuccessStatusCode();
        var alta = await inicio.Content.ReadFromJsonAsync<InicioDeAltaDto>();

        // El código se calcula igual que lo calcularía el teléfono: mismo secreto, misma ventana.
        var codigo = Totp.Calcular(Base32.Decodificar(alta!.SecretoEnBase32)!,
                                   Totp.VentanaDe(DateTimeOffset.UtcNow));

        var confirmacion = await cliente.PostAsJsonAsync("/api/auth/segundo-factor/confirmar",
            new ConfirmarAltaRequest(codigo));
        confirmacion.EnsureSuccessStatusCode();

        var hecho = await confirmacion.Content.ReadFromJsonAsync<AltaConfirmadaDto>();
        _codigosDeRescate[usuario] = new Queue<string>(hecho!.CodigosDeRescate);

        // Y ahora sí: se entra de nuevo marcando el equipo como recordado. De aquí en adelante,
        // cualquier cliente que use este frasco entra solo con la contraseña.
        await EntrarAsync(cliente, usuario, contrasena);
    }

    /// <summary>Un cliente sobre el frasco de cookies de esa cuenta, ya con la sesión abierta.</summary>
    private async Task<HttpClient> ClienteConSesionAsync(string usuario, string contrasena)
    {
        var cliente = ClienteDe(usuario);
        await EntrarAsync(cliente, usuario, contrasena);
        return cliente;
    }

    private HttpClient ClienteDe(string usuario) =>
        CreateDefaultClient(new CookieContainerHandler(
            _frascos.GetOrAdd(usuario, _ => new CookieContainer())));

    /// <summary>
    /// El acceso completo: contraseña y, si hace falta, segundo factor.
    ///
    /// <para>Lo normal es que no haga falta —el frasco lleva la cookie del equipo recordado—. Cuando
    /// hace falta se gasta un código de rescate: son ocho por cuenta y solo se llega aquí la primera
    /// vez de cada una, pero si alguna prueba futura invalidara los equipos recordados, esto seguiría
    /// funcionando en vez de dejar un fallo incomprensible.</para>
    /// </summary>
    private async Task EntrarAsync(HttpClient cliente, string usuario, string contrasena)
    {
        var acceso = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(usuario, contrasena));
        acceso.EnsureSuccessStatusCode();

        var respuesta = await acceso.Content.ReadFromJsonAsync<RespuestaDeAccesoDto>();
        if (respuesta?.SegundoFactorRequerido != true) return;

        if (!_codigosDeRescate.TryGetValue(usuario, out var codigos) || codigos.Count == 0)
            throw new InvalidOperationException(
                $"«{usuario}» necesita segundo factor y no quedan códigos de rescate de prueba. " +
                "O el equipo recordado dejó de valer, o esta cuenta no pasó por la puesta a punto.");

        var segundo = await cliente.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(respuesta.Tramo!, codigos.Dequeue(), RecordarEquipo: true));
        segundo.EnsureSuccessStatusCode();
    }

    /// <summary>La contraseña temporal que el arranque sembró para el administrador inicial.</summary>
    public string ContrasenaTemporal =>
        _cazador.Temporal ?? throw new InvalidOperationException(
            "El arranque no anunció ninguna contraseña temporal. O la base no estaba vacía, o la " +
            "siembra dejó de anunciarla por el registro y hay que actualizar esta ayuda de pruebas.");

    public Task InitializeAsync()
    {
        // Forzar la creación del host aquí, y no en la primera prueba, deja los fallos de arranque
        // donde se entienden en lugar de disfrazados de «la primera prueba falla».
        _ = Server;
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        try { File.Delete(_archivo); } catch { /* que quede un archivo temporal no rompe nada */ }
    }

    /// <summary>
    /// Escucha el registro de salida y se queda con el valor <c>Temporal</c> del apunte de la
    /// siembra. Se lee del estado ESTRUCTURADO y no del texto del mensaje: un mensaje se reescribe
    /// cualquier día y estas pruebas dejarían de encontrarlo sin que nada lo avise.
    /// </summary>
    private sealed class CazadorDeContrasenaTemporal : ILoggerProvider
    {
        public string? Temporal { get; private set; }

        public ILogger CreateLogger(string categoryName) => new Escucha(this);

        public void Dispose() { }

        private sealed class Escucha(CazadorDeContrasenaTemporal dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex,
                Func<TState, Exception?, string> formatear)
            {
                if (estado is not IReadOnlyList<KeyValuePair<string, object?>> campos) return;

                foreach (var campo in campos)
                    if (campo.Key == "Temporal" && campo.Value is string valor && valor.Length > 0)
                        dueno.Temporal = valor;
            }
        }
    }
}
