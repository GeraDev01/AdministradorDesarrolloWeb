using AdminWeb.Application.Services;
using AdminWeb.Domain.Avisos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los avisos que llegan con la pestaña cerrada.
///
/// Sustituyen a los globos de la bandeja del sistema, que era lo ÚNICO que la web no podía hacer y
/// el escritorio sí. Lo delicado no es el envío —eso lo hace una librería— sino la contabilidad de
/// las suscripciones: una fila de más entrega el aviso por duplicado, una fila que sobrevive a un
/// cambio de dueño se lo entrega a quien no toca, y una fila muerta se reintenta para siempre.
/// </summary>
public class AvisosPushServiceTests
{
    /// <summary>
    /// Un envío de mentira que anota a dónde se mandó y puede fingir que una dirección murió, que es
    /// lo que hace el servicio de entrega real cuando el navegador ya no existe.
    /// </summary>
    private sealed class EnvioDePrueba : IEnvioDeAvisosPush
    {
        public bool Configurado { get; init; } = true;
        public string? LlavePublica => "llave-de-prueba";

        public List<AvisoPush> Enviados { get; } = [];
        public HashSet<string> Muertas { get; } = [];

        public Task<bool> EnviarAsync(AvisoPush aviso, CancellationToken ct = default)
        {
            Enviados.Add(aviso);
            return Task.FromResult(!Muertas.Contains(aviso.Endpoint));
        }
    }

    private static ICurrentUser Sembrar(AppDbContext db, int userId = 1, string nombre = "ana")
    {
        db.Users.Add(new User
        {
            Id = userId, Username = nombre, FullName = nombre,
            Role = UserRole.Desarrollador, IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();

        return new UsuarioDePrueba
        {
            UserId = userId, Username = nombre, FullName = nombre, Role = UserRole.Desarrollador
        };
    }

    // ── Suscribirse ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElMismoNavegadorSuscribiendoseDosVeces_NoDejaDosFilas()
    {
        // El navegador renueva su suscripción por su cuenta y vuelve con la MISMA dirección. Si se
        // acumularan filas, cada aviso llegaría tantas veces como renovaciones hubo.
        using var db = TestDb.New();
        var svc = new AvisosPushService(db, Sembrar(db), new EnvioDePrueba());

        await svc.SuscribirAsync("https://push.test/abc", "clave1", "auth1", "Windows");
        await svc.SuscribirAsync("https://push.test/abc", "clave2", "auth2", "Windows");

        var fila = Assert.Single(db.PushSubscriptions);
        Assert.Equal("clave2", fila.P256dh);
    }

    [Fact]
    public async Task UnEquipoCompartido_ReasignaLaSuscripcionAQuienEntroDespues()
    {
        // Alguien cierra sesión en un equipo compartido y entra otra persona. Si la fila siguiera
        // apuntando a la primera, recibiría avisos de trabajo que ya no le tocan.
        using var db = TestDb.New();
        var ana = Sembrar(db, 1, "ana");
        var beto = Sembrar(db, 2, "beto");

        await new AvisosPushService(db, ana, new EnvioDePrueba())
            .SuscribirAsync("https://push.test/compartido", "k", "a", null);
        await new AvisosPushService(db, beto, new EnvioDePrueba())
            .SuscribirAsync("https://push.test/compartido", "k", "a", null);

        var fila = Assert.Single(db.PushSubscriptions);
        Assert.Equal(2, fila.UserId);
    }

    [Fact]
    public async Task UnaSuscripcionIncompleta_SeRechaza()
    {
        using var db = TestDb.New();
        var svc = new AvisosPushService(db, Sembrar(db), new EnvioDePrueba());

        var (ok, _) = await svc.SuscribirAsync("https://push.test/abc", "", "auth", null);

        Assert.False(ok);
        Assert.Empty(db.PushSubscriptions);
    }

    [Fact]
    public async Task SinSesion_NoSePuedeSuscribir()
    {
        using var db = TestDb.New();
        var svc = new AvisosPushService(db, UsuarioDePrueba.Anonimo(), new EnvioDePrueba());

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.SuscribirAsync("https://push.test/abc", "k", "a", null));
    }

    // ── Cancelar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NadiePuedeDesuscribirElNavegadorDeOtraPersona()
    {
        // La dirección de entrega no es un secreto que proteja nada: si bastara con conocerla,
        // cualquiera con sesión podría dejar a otro sin avisos.
        using var db = TestDb.New();
        var ana = Sembrar(db, 1, "ana");
        var beto = Sembrar(db, 2, "beto");

        await new AvisosPushService(db, ana, new EnvioDePrueba())
            .SuscribirAsync("https://push.test/de-ana", "k", "a", null);

        await new AvisosPushService(db, beto, new EnvioDePrueba()).CancelarAsync("https://push.test/de-ana");

        Assert.Single(db.PushSubscriptions);
    }

    [Fact]
    public async Task CancelarLoPropio_BorraLaFila()
    {
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var svc = new AvisosPushService(db, cu, new EnvioDePrueba());

        await svc.SuscribirAsync("https://push.test/mia", "k", "a", null);
        var (ok, _) = await svc.CancelarAsync("https://push.test/mia");

        Assert.True(ok);
        Assert.Empty(db.PushSubscriptions);
    }

    // ── Entregar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElAvisoLlegaATODOSLosNavegadoresDeLaPersona()
    {
        // Quien usa el portátil y el teléfono tiene dos suscripciones, y el aviso debe llegar a las
        // dos: no se sabe cuál está mirando.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba();
        var svc = new AvisosPushService(db, cu, envio);

        await svc.SuscribirAsync("https://push.test/portatil", "k1", "a1", "Windows");
        await svc.SuscribirAsync("https://push.test/telefono", "k2", "a2", "Android");

        await svc.EmpujarAsync(1, "Título", "Cuerpo", "/avisos");

        Assert.Equal(2, envio.Enviados.Count);
        Assert.All(envio.Enviados, a => Assert.Equal("Título", a.Titulo));
    }

    [Fact]
    public async Task UnaSuscripcionMuerta_SeBorraAlDescubrirla()
    {
        // Es la ÚNICA forma de enterarse: el navegador no avisa de que se desinstaló. Sin borrarla,
        // el servidor haría una petición inútil por cada aviso y por siempre.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba();
        envio.Muertas.Add("https://push.test/vieja");

        var svc = new AvisosPushService(db, cu, envio);
        await svc.SuscribirAsync("https://push.test/vieja", "k1", "a1", null);
        await svc.SuscribirAsync("https://push.test/viva", "k2", "a2", null);

        await svc.EmpujarAsync(1, "Título", "Cuerpo", null);

        var queda = Assert.Single(db.PushSubscriptions);
        Assert.Equal("https://push.test/viva", queda.Endpoint);
    }

    [Fact]
    public async Task ElAvisoDeUnaPersona_NoLlegaAlNavegadorDeOtra()
    {
        using var db = TestDb.New();
        var ana = Sembrar(db, 1, "ana");
        Sembrar(db, 2, "beto");
        var envio = new EnvioDePrueba();

        await new AvisosPushService(db, ana, envio).SuscribirAsync("https://push.test/de-ana", "k", "a", null);
        await new AvisosPushService(db, ana, envio).EmpujarAsync(2, "Para Beto", "…", null);

        Assert.Empty(envio.Enviados);
    }

    [Fact]
    public async Task SinLlavesConfiguradas_NoSeIntentaEntregarNada()
    {
        // Una instalación sin llaves VAPID tiene que funcionar igual: los avisos dentro de la
        // aplicación son los que cuentan y este es el extra.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba { Configurado = false };
        var svc = new AvisosPushService(db, cu, envio);

        await svc.SuscribirAsync("https://push.test/abc", "k", "a", null);
        await svc.EmpujarAsync(1, "Título", "Cuerpo", null);

        Assert.Empty(envio.Enviados);
        Assert.False(svc.Disponible);
    }

    // ── El aviso in-app manda ───────────────────────────────────────────────────

    [Fact]
    public async Task ElAvisoSeGuardaAunqueNoHayaNingunNavegadorSuscrito()
    {
        // Es la regla de fondo: el push es un empujón, no el aviso. Quien no lo active se entera
        // igual al volver a la aplicación.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba();
        var avisos = new NotificationService(db, new AvisosPushService(db, cu, envio));

        var aviso = await avisos.NotifyAsync(1, NotificationKind.General, "Algo pasó", "Detalle");

        Assert.NotNull(aviso);
        Assert.Single(db.Notifications);
        Assert.Empty(envio.Enviados);
    }

    [Fact]
    public async Task UnAvisoNuevo_SeEmpujaAlNavegadorSuscrito()
    {
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba();
        var push = new AvisosPushService(db, cu, envio);
        await push.SuscribirAsync("https://push.test/abc", "k", "a", null);

        await new NotificationService(db, push)
            .NotifyAsync(1, NotificationKind.General, "Te asignaron algo", "Requerimiento #12", "/mis-asignaciones");

        var entregado = Assert.Single(envio.Enviados);
        Assert.Equal("Te asignaron algo", entregado.Titulo);
        Assert.Equal("/mis-asignaciones", entregado.Url);
    }

    [Fact]
    public async Task UnAvisoDeduplicado_NoSeEmpuja()
    {
        // Si se empujara, el aviso repetido llegaría al navegador aunque no se haya guardado: la
        // deduplicación dejaría de servir justo donde más molesta.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var envio = new EnvioDePrueba();
        var push = new AvisosPushService(db, cu, envio);
        await push.SuscribirAsync("https://push.test/abc", "k", "a", null);

        var avisos = new NotificationService(db, push);
        await avisos.NotifyAsync(1, NotificationKind.General, "Uno", "…", dedupeKey: "misma");
        await avisos.NotifyAsync(1, NotificationKind.General, "Otro", "…", dedupeKey: "misma");

        Assert.Single(db.Notifications);
        Assert.Single(envio.Enviados);
    }
}
