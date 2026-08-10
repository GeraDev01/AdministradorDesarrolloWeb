using System.Net.Http.Json;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
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
    /// </summary>
    public HttpClient NuevoCliente() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>La contraseña definitiva del administrador, una vez cambiada la temporal.</summary>
    public const string ContrasenaDelAdmin = "ClaveDePrueba123";

    /// <summary>
    /// El cambio de la contraseña temporal, hecho UNA sola vez para toda la clase de pruebas.
    ///
    /// Es lo que obliga a que exista: la temporal solo vale hasta que se cambia. Si cada prueba la
    /// usara para entrar, la primera en correr funcionaría y el resto fallaría con un 401 — y como
    /// xUnit no garantiza el orden, «la primera» sería una distinta cada vez.
    /// </summary>
    private Task? _preparacion;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    /// <summary>
    /// Un cliente con la sesión del administrador ya abierta y su contraseña ya cambiada.
    ///
    /// El cambio no es comodidad: hasta que se hace, la API responde 403 MUST_CHANGE_PASSWORD a todo
    /// lo demás, y cada prueba empezaría tropezando con eso en lugar de probar lo suyo.
    /// </summary>
    public async Task<HttpClient> ClienteAdminAsync()
    {
        await PrepararAdminAsync();

        var cliente = NuevoCliente();
        var acceso = await cliente.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin", ContrasenaDelAdmin));
        acceso.EnsureSuccessStatusCode();

        return cliente;
    }

    private async Task PrepararAdminAsync()
    {
        await _cerrojo.WaitAsync();
        try
        {
            _preparacion ??= CambiarLaTemporalAsync();
        }
        finally
        {
            _cerrojo.Release();
        }

        await _preparacion;
    }

    /// <summary>
    /// Siembra una cuenta con el rol pedido y devuelve un cliente con su sesión abierta.
    ///
    /// Se escribe directamente en la base porque el alta de usuarios es una pantalla de fase 3 y
    /// todavía no hay endpoint. Lo que importa es que la cuenta quede como la dejaría el alta real
    /// —activa, con su hash BCrypt y sin cambio obligatorio pendiente—, para que el 403 que se prueba
    /// venga de la política de rol y no de un usuario mal formado.
    /// </summary>
    public async Task<HttpClient> ClienteComoAsync(UserRole rol, string usuario)
    {
        const string contrasena = "OtraClave123";

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
                    PasswordHash = PasswordHasher.Hash(contrasena)
                });
                await db.SaveChangesAsync();
            }
        }

        var cliente = NuevoCliente();
        var acceso = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(usuario, contrasena));
        acceso.EnsureSuccessStatusCode();
        return cliente;
    }

    private async Task CambiarLaTemporalAsync()
    {
        var cliente = NuevoCliente();

        var acceso = await cliente.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin", ContrasenaTemporal));
        acceso.EnsureSuccessStatusCode();

        var cambio = await cliente.PostAsJsonAsync("/api/auth/change-password",
            new CambioContrasenaRequest(ContrasenaDelAdmin, ContrasenaDelAdmin));
        cambio.EnsureSuccessStatusCode();
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
