using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.DevOps;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Un cliente de Azure DevOps de mentira.
///
/// Es la razón de que exista <see cref="IClienteAzureDevOps"/>: <b>las pruebas no tocan la red</b>.
/// Todo lo que estas pruebas comprueban —de quién es un ticket, qué token se usa, qué se guarda
/// cuando DevOps contesta y qué se responde cuando no— vive delante de esta frontera.
///
/// Apunta las CREDENCIALES de cada llamada a propósito: la regla de que se firma con el token de
/// quien lo provocó no se puede comprobar de otra forma.
/// </summary>
internal sealed class ClienteDevOpsDePrueba : IClienteAzureDevOps
{
    public List<WorkItemDevOps> WorkItems { get; set; } = [];
    public List<ComentarioDevOps> Comentarios { get; set; } = [];
    public List<BugHijoDevOps> BugsHijos { get; set; } = [];
    public List<CambioDeAsignacionDevOps> Historial { get; set; } = [];

    /// <summary>Con false, DevOps rechaza el campo Effort (hay plantillas donde no existe).</summary>
    public bool AceptaEstimacion { get; set; } = true;

    public bool AceptaHorasDeTrabajo { get; set; } = true;

    /// <summary>Si se pone, TODAS las llamadas fallan con él. Simula DevOps caído o token caducado.</summary>
    public ErrorDeAzureDevOps? Fallo { get; set; }

    public List<CredencialesDevOps> Llamadas { get; } = [];
    public List<string> ComentariosPublicados { get; } = [];
    public int? UltimaPrioridad { get; private set; }
    public double? UltimasHorasEstimadas { get; private set; }
    public double HorasDeTrabajoSumadas { get; private set; }

    /// <summary>El token con el que se firmó la última llamada. Solo lo mira una prueba.</summary>
    public string? UltimoToken => Llamadas.Count == 0 ? null : Llamadas[^1].Pat;

    private void Registrar(CredencialesDevOps credenciales)
    {
        Llamadas.Add(credenciales);
        if (Fallo != null) throw Fallo;
    }

    public Task<IReadOnlyList<int>> ConsultarIdsAsync(
        CredencialesDevOps credenciales, string? wiql, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult<IReadOnlyList<int>>(WorkItems.Select(w => w.Id).ToList());
    }

    public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(
        CredencialesDevOps credenciales, IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult<IReadOnlyList<WorkItemDevOps>>(
            WorkItems.Where(w => ids.Contains(w.Id)).ToList());
    }

    public Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult<IReadOnlyList<ComentarioDevOps>>(Comentarios);
    }

    public Task PublicarComentarioAsync(
        CredencialesDevOps credenciales, int numero, string textoHtml, CancellationToken ct = default)
    {
        Registrar(credenciales);
        ComentariosPublicados.Add(textoHtml);
        return Task.CompletedTask;
    }

    public Task<string> SubirAdjuntoAsync(
        CredencialesDevOps credenciales, byte[] contenido, string nombre, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult($"https://dev.azure.com/adjuntos/{nombre}");
    }

    public Task<(string nombre, string correo)> ReasignarAsync(
        CredencialesDevOps credenciales, int numero, string? correoOVacio, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult(string.IsNullOrWhiteSpace(correoOVacio)
            ? ("", "")
            : ("Nuevo Dueño", correoOVacio));
    }

    public Task<string> CambiarEstadoAsync(
        CredencialesDevOps credenciales, int numero, string nuevoEstado, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult(nuevoEstado);
    }

    public Task<ColumnaDeTablero> CambiarColumnaAsync(
        CredencialesDevOps credenciales, int numero, string columna, bool mitadHecha,
        CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult(new ColumnaDeTablero(columna, mitadHecha));
    }

    public Task<ColumnaDeTablero> LeerColumnaAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult(new ColumnaDeTablero("", false));
    }

    public Task CambiarPrioridadAsync(
        CredencialesDevOps credenciales, int numero, int prioridad, CancellationToken ct = default)
    {
        Registrar(credenciales);
        UltimaPrioridad = prioridad;
        return Task.CompletedTask;
    }

    public Task<(bool escrito, string aviso)> EscribirEstimacionAsync(
        CredencialesDevOps credenciales, int numero, double horas, CancellationToken ct = default)
    {
        Registrar(credenciales);
        UltimasHorasEstimadas = horas;
        return Task.FromResult(AceptaEstimacion
            ? (true, "")
            : (false, "Azure DevOps no aceptó el campo Effort (400): TF401320."));
    }

    public Task<bool> SumarTrabajoCompletadoAsync(
        CredencialesDevOps credenciales, int numero, double horasDelta, bool reducirRestante,
        CancellationToken ct = default)
    {
        Registrar(credenciales);
        if (AceptaHorasDeTrabajo) HorasDeTrabajoSumadas += horasDelta;
        return Task.FromResult(AceptaHorasDeTrabajo);
    }

    public Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult<IReadOnlyList<BugHijoDevOps>>(BugsHijos);
    }

    public Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult<IReadOnlyList<CambioDeAsignacionDevOps>>(Historial);
    }

    public Task<(bool ok, string mensaje)> ProbarCredencialesAsync(
        CredencialesDevOps credenciales, CancellationToken ct = default)
    {
        Registrar(credenciales);
        return Task.FromResult((true, "Conectado como «ana@empresa.com» en el proyecto Webpro."));
    }
}

/// <summary>Un protector que marca el texto en vez de cifrarlo, para no arrastrar la API a las pruebas.</summary>
internal sealed class ProtectorSimulado : IProtectorDeSecretos
{
    private const string Marca = "cifrado:";

    public string Proteger(string valorEnClaro) => Marca + valorEnClaro;

    public string? Desproteger(string cifrado) =>
        cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
}

/// <summary>
/// La integración con Azure DevOps vista desde el negocio.
///
/// Lo que estas pruebas cuidan es lo que el port podría perder sin que nadie se entere: que el token
/// no salga por ningún lado, que un desarrollador no opere sobre tickets ajenos —en el escritorio eso
/// lo daba la interfaz, y aquí a la API se la llama sin pasar por ella—, y que un fallo de la
/// integración se conteste con un mensaje en vez de reventar.
/// </summary>
public class DevOpsServiceTests
{
    private const string TokenPersonal = "token-personal-de-ana";
    private const string TokenDeLaInstalacion = "token-de-la-instalacion";

    // ── Montaje ──────────────────────────────────────────────────────────────────

    private static (DevOpsService servicio, AppDbContext db, ClienteDevOpsDePrueba cliente)
        Nuevo(AppDbContext db, ICurrentUser usuario, ClienteDevOpsDePrueba? cliente = null)
    {
        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, usuario, origen);
        var configuracion = new SettingsService(db, usuario, bitacora);
        var secretos = new UserSecretsService(db, usuario, new ProtectorSimulado(), bitacora);

        cliente ??= new ClienteDevOpsDePrueba();

        return (new DevOpsService(db, usuario, configuracion, secretos, bitacora,
                                  new NotificationService(db), cliente),
                db, cliente);
    }

    /// <summary>La configuración compartida la escribe el administrador; aquí se siembra como él.</summary>
    private static async Task ConfigurarAsync(
        AppDbContext db, bool habilitada = true, string? tokenDeLaInstalacion = TokenDeLaInstalacion,
        string? organizacion = "https://dev.azure.com/org", string? proyecto = "Webpro")
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var settings = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsOrgUrl, organizacion);
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsProject, proyecto);
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsEnabled, habilitada ? "true" : "false");
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsPat, tokenDeLaInstalacion);
    }

    private static Developer SembrarPersona(AppDbContext db, int id, string nombre, string correo)
    {
        var dev = new Developer { Id = id, FullName = nombre, Email = correo, IsActive = true };
        db.Developers.Add(dev);
        db.Users.Add(new User
        {
            Id = id, Username = nombre.ToLowerInvariant(), FullName = nombre,
            PasswordHash = "x", IsActive = true, DeveloperId = id, Role = UserRole.Desarrollador
        });
        db.SaveChanges();
        return dev;
    }

    private static WorkItemDevOps Item(
        int id, string titulo = "Error al guardar", string estado = "Active",
        string asignadoA = "Ana Pérez", string correo = "ana@empresa.com", double? horas = null) =>
        new(id, titulo, "Bug", estado, "2", "detalle",
            $"https://dev.azure.com/org/Webpro/_workitems/edit/{id}",
            "Webpro\\Area", "Webpro\\Sprint 1", "Bepensa;Bug",
            asignadoA, correo, 3, horas, 0, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow);

    private static DevOpsTicket SembrarTicket(
        AppDbContext db, int numero, string asignadoA = "Ana Pérez", string? correo = "ana@empresa.com",
        string estado = "Active")
    {
        var ticket = new DevOpsTicket
        {
            ExternalId = numero, Title = "Error al guardar", WorkItemType = "Bug", State = estado,
            Priority = "2", AssignedTo = asignadoA, AssignedToUniqueName = correo,
            Url = $"https://dev.azure.com/org/Webpro/_workitems/edit/{numero}",
            SyncedAt = DateTime.UtcNow
        };
        db.DevOpsTickets.Add(ticket);
        db.SaveChanges();
        return ticket;
    }

    // ── El token no sale del servidor ────────────────────────────────────────────

    [Fact]
    public async Task ElEstado_DiceSiHayToken_NoCualEs()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var (servicio, _, _) = Nuevo(db, usuario);
        await servicio.GuardarMiPatAsync(TokenPersonal);

        var estado = await servicio.EstadoAsync();

        Assert.True(estado.TengoPatPropio);
        Assert.True(estado.HayPatDeOrganizacion);
        Assert.DoesNotContain(TokenPersonal, estado.Explicacion);
        Assert.DoesNotContain(TokenDeLaInstalacion, estado.Explicacion);

        // La organización y el proyecto SÍ viajan: no son secretos, y saber a dónde apunta la
        // integración es justo lo que hace falta para diagnosticar cuando algo no cuadra.
        Assert.Equal("https://dev.azure.com/org", estado.Organizacion);
    }

    [Fact]
    public async Task LaBitacora_NuncaGuardaElToken()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var (servicio, _, _) = Nuevo(db, usuario);

        await servicio.GuardarMiPatAsync(TokenPersonal);
        await servicio.EstimarAsync(4821, 4);
        await servicio.ComentarAsync(4821, "listo", []);

        var registros = db.AuditLogs.AsNoTracking().ToList();
        Assert.NotEmpty(registros);
        Assert.All(registros, r =>
        {
            Assert.DoesNotContain(TokenPersonal, r.Details ?? "");
            Assert.DoesNotContain(TokenPersonal, r.NewValues ?? "");
            Assert.DoesNotContain(TokenDeLaInstalacion, r.Details ?? "");
        });
    }

    [Fact]
    public async Task ElTokenPropio_TienePreferenciaSobreElDeLaInstalacion()
    {
        // Es lo que hace que en DevOps los comentarios queden firmados por quien los escribió, y no
        // por una cuenta compartida: sin eso, un compromiso de servicio no puede probar quién atendió.
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var (servicio, _, cliente) = Nuevo(db, usuario);

        await servicio.ComentarAsync(4821, "sin token propio", []);
        Assert.Equal(TokenDeLaInstalacion, cliente.UltimoToken);

        await servicio.GuardarMiPatAsync(TokenPersonal);
        await servicio.ComentarAsync(4821, "ya con el mío", []);
        Assert.Equal(TokenPersonal, cliente.UltimoToken);
    }

    // ── Un fallo de la integración se explica, no revienta ───────────────────────

    [Fact]
    public async Task SinOrganizacionNiProyecto_ElMensajeLoDice()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db, organizacion: null, proyecto: null);

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var resultado = await servicio.SincronizarAsync();

        Assert.False(resultado.Ok);
        Assert.Contains("organización", resultado.Mensaje);
    }

    [Fact]
    public async Task ConLaIntegracionApagada_NoSeSincronizaYSeDiceElPorQue()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db, habilitada: false);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var resultado = await servicio.SincronizarAsync();

        Assert.False(resultado.Ok);
        Assert.Contains("apagada", resultado.Mensaje);
        Assert.Empty(cliente.Llamadas);   // ni se intentó
    }

    [Fact]
    public async Task SiDevOpsNoContesta_SeDevuelveSuMensaje_NoUnaExcepcion()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);

        var cliente = new ClienteDevOpsDePrueba
        {
            Fallo = new ErrorDeAzureDevOps("No se pudo contactar con Azure DevOps: se agotó el tiempo.")
        };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);

        var resultado = await servicio.SincronizarAsync();

        Assert.False(resultado.Ok);
        Assert.Contains("No se pudo contactar", resultado.Mensaje);
    }

    // ── Sincronización ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Sincronizar_GuardaLosTickets_YLaSegundaVezLosActualiza()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);

        var cliente = new ClienteDevOpsDePrueba { WorkItems = [Item(4821)] };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);

        var primera = await servicio.SincronizarAsync();
        Assert.True(primera.Ok);
        Assert.Equal(1, primera.Nuevos);

        cliente.WorkItems = [Item(4821, estado: "Closed")];
        var segunda = await servicio.SincronizarAsync();

        Assert.Equal(0, segunda.Nuevos);
        Assert.Equal(1, segunda.Actualizados);

        var ticket = db.DevOpsTickets.AsNoTracking().Single();
        Assert.Equal("Closed", ticket.State);
    }

    [Fact]
    public async Task Sincronizar_ConservaLaEstimacionLocalSiDevOpsNoTraeNinguna()
    {
        // La persona pudo estimar hace un momento y el valor todavía no estar escrito allá; pisarlo
        // con un vacío borraría lo que acaba de capturar.
        var db = TestDb.New();
        await ConfigurarAsync(db);

        var cliente = new ClienteDevOpsDePrueba { WorkItems = [Item(4821, horas: 8)] };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);
        await servicio.SincronizarAsync();

        cliente.WorkItems = [Item(4821, horas: null)];
        await servicio.SincronizarAsync();

        Assert.Equal(8, db.DevOpsTickets.AsNoTracking().Single().EstimatedHours);
    }

    [Fact]
    public async Task SincronizarTodo_EsDelLider()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.SincronizarAsync());
    }

    [Fact]
    public async Task SincronizarLoMio_ExigeTokenPROPIO_NoElDeLaInstalacion()
    {
        // La consulta usa la macro @Me, que DevOps resuelve contra el DUEÑO del token: con el
        // compartido traería los tickets de otra cuenta sin avisar de nada.
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var (servicio, _, cliente) = Nuevo(db, usuario);

        var sinToken = await servicio.SincronizarMisTicketsAsync();

        Assert.False(sinToken.Ok);
        Assert.Contains("tu propio token", sinToken.Mensaje);
        Assert.Empty(cliente.Llamadas);

        cliente.WorkItems = [Item(4821)];
        await servicio.GuardarMiPatAsync(TokenPersonal);

        var conToken = await servicio.SincronizarMisTicketsAsync();

        Assert.True(conToken.Ok);
        Assert.Equal(TokenPersonal, cliente.UltimoToken);
    }

    [Fact]
    public async Task SincronizarLoMio_NoExigeQueElLiderHayaEncendidoLaIntegracion()
    {
        // Si dependiera del interruptor del administrador, nadie podría actualizar su propia lista
        // hasta que el líder sincronizara, que era exactamente el problema.
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db, habilitada: false);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var cliente = new ClienteDevOpsDePrueba { WorkItems = [Item(4821)] };
        var (servicio, _, _) = Nuevo(db, usuario, cliente);
        await servicio.GuardarMiPatAsync(TokenPersonal);

        var resultado = await servicio.SincronizarMisTicketsAsync();

        Assert.True(resultado.Ok);
        Assert.Equal(1, resultado.Nuevos);
    }

    [Fact]
    public async Task LaPrimeraSincronizacionPropia_FijaLineaBaseSinSoltarUnAluvionDeAvisos()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var cliente = new ClienteDevOpsDePrueba { WorkItems = [Item(1), Item(2), Item(3)] };
        var (servicio, _, _) = Nuevo(db, usuario, cliente);
        await servicio.GuardarMiPatAsync(TokenPersonal);

        await servicio.SincronizarMisTicketsAsync();
        Assert.Empty(db.Notifications.AsNoTracking().ToList());

        // Ya con la línea base puesta, lo NUEVO sí avisa.
        cliente.WorkItems = [Item(1), Item(2), Item(3), Item(4)];
        await servicio.SincronizarMisTicketsAsync();

        var aviso = Assert.Single(db.Notifications.AsNoTracking().ToList());
        Assert.Contains("#4", aviso.Title);
    }

    // ── Lo que un desarrollador puede tocar ──────────────────────────────────────

    [Fact]
    public async Task ElDesarrollador_NoPuedeOperarSobreUnTicketAjeno()
    {
        // En el escritorio esto lo daba la interfaz: su pantalla solo listaba lo suyo. Aquí a la API
        // se la puede llamar sin pasar por ella, así que se comprueba contra la fila.
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821, asignadoA: "Beto Ruiz", correo: "beto@empresa.com");

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var (servicio, _, cliente) = Nuevo(db, usuario);

        var (ok, mensaje) = await servicio.EstimarAsync(4821, 4);

        Assert.False(ok);
        Assert.Contains("no está a tu nombre", mensaje);
        Assert.Empty(cliente.Llamadas);
        Assert.Null(db.DevOpsTickets.AsNoTracking().Single().EstimatedHours);
    }

    [Fact]
    public async Task ElLider_SiPuedeOperarSobreCualquierTicket()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821, asignadoA: "Beto Ruiz", correo: "beto@empresa.com");

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, _) = await servicio.EstimarAsync(4821, 4);

        Assert.True(ok);
    }

    [Fact]
    public async Task Reasignar_EsDelLider()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1));

        await Assert.ThrowsAsync<AuthorizationException>(
            () => servicio.ReasignarAsync(4821, "beto@empresa.com"));
    }

    // ── Estimación ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Estimar_SeGuardaEnLocalAunqueDevOpsRechaceElCampoEffort()
    {
        // Hay plantillas de proceso donde Effort no existe en ese tipo de work item. Perder lo que la
        // persona acaba de capturar por eso sería mucho peor que guardarlo solo aquí.
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var cliente = new ClienteDevOpsDePrueba { AceptaEstimacion = false };
        var (servicio, _, _) = Nuevo(db, usuario, cliente);

        var (ok, mensaje) = await servicio.EstimarAsync(4821, 6.5);

        Assert.True(ok);                                   // sí quedó guardada
        Assert.Contains("solo quedó guardado aquí", mensaje);
        Assert.Equal(6.5, db.DevOpsTickets.AsNoTracking().Single().EstimatedHours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(5000)]
    public async Task Estimaciones_QueNoSonReales_SeRechazanConMensaje(double horas)
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, mensaje) = await servicio.EstimarAsync(4821, horas);

        Assert.False(ok);
        Assert.NotEmpty(mensaje);
        Assert.Empty(cliente.Llamadas);
    }

    // ── Prioridad ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task PrioridadFueraDeRango_SeRechaza(int prioridad)
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, _) = await servicio.CambiarPrioridadAsync(4821, prioridad);

        Assert.False(ok);
        Assert.Empty(cliente.Llamadas);
    }

    [Fact]
    public async Task CambiarPrioridad_DejaConstanciaDeQueAlguienLaPenso()
    {
        // El campo Priority por sí solo no dice nada: DevOps le pone 2 por omisión a todo lo que se
        // crea, así que sin esta marca no hay forma de saber qué falta por priorizar.
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, _) = await servicio.CambiarPrioridadAsync(4821, 1);

        Assert.True(ok);
        Assert.Equal(1, cliente.UltimaPrioridad);

        var ticket = db.DevOpsTickets.AsNoTracking().Single();
        Assert.Equal("1", ticket.Priority);
        Assert.False(ticket.SinPrioridadDefinida);
        Assert.Equal(99, ticket.PriorityConfirmedByUserId);
    }

    [Fact]
    public async Task CambiarPrioridad_SeReflejaEnElRequerimientoLocal()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);
        db.Requirements.Add(new Requirement
        {
            Title = "Error al guardar", Source = RequirementSource.AzureDevOps,
            ExternalId = "4821", Priority = RequirementPriority.Media
        });
        db.SaveChanges();

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        await servicio.CambiarPrioridadAsync(4821, 1);   // 1 en DevOps es lo más urgente

        Assert.Equal(RequirementPriority.Critica, db.Requirements.AsNoTracking().Single().Priority);
    }

    // ── Comentarios ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosComentarios_LleganSinMarcado()
    {
        // DevOps los guarda en HTML y los escribe gente de FUERA del equipo: si el marcado llegara al
        // navegador, bastaría un comentario para meter algo en la página de quien lo lee.
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var cliente = new ClienteDevOpsDePrueba
        {
            Comentarios =
            [
                new ComentarioDevOps(
                    "<div>Ya quedó <b>listo</b><script>alert(1)</script></div>", "Beto", DateTime.UtcNow)
            ]
        };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);

        var (ok, _, datos) = await servicio.ComentariosAsync(4821);

        Assert.True(ok);
        var comentario = Assert.Single(datos!.Comentarios);
        Assert.DoesNotContain("<", comentario.Texto);
        Assert.Contains("Ya quedó listo", comentario.Texto);
    }

    [Fact]
    public async Task ComentarSinTextoNiEvidencias_SeRechaza()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, _) = await servicio.ComentarAsync(4821, "   ", []);

        Assert.False(ok);
        Assert.Empty(cliente.ComentariosPublicados);
    }

    [Fact]
    public async Task ElTextoDelComentario_SeEscapaAntesDeArmarElHtml()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        await servicio.ComentarAsync(4821, "<script>alert(1)</script>", []);

        var publicado = Assert.Single(cliente.ComentariosPublicados);
        Assert.DoesNotContain("<script>", publicado);
        Assert.Contains("&lt;script&gt;", publicado);
    }

    [Fact]
    public async Task UnaEvidenciaQueNoEsImagen_SeRechazaAntesDeSubirla()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var (servicio, _, cliente) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, mensaje) = await servicio.ComentarAsync(
            4821, "mira esto", [("informe.png", "no soy una imagen"u8.ToArray())]);

        Assert.False(ok);
        Assert.Contains("no es una imagen", mensaje);
        Assert.Empty(cliente.Llamadas);
    }

    // ── Vigilancia ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Vigilar_EsPersonal()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        SembrarPersona(db, 2, "Beto Ruiz", "beto@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        // El nombre de usuario se pone a mano: es la columna con la que se ata la vigilancia, y el
        // atajo de la fábrica de pruebas usa el nombre del rol.
        var ana = new UsuarioDePrueba
        {
            UserId = 1, Username = "ana", FullName = "Ana Pérez",
            Role = UserRole.Desarrollador, DeveloperId = 1
        };
        var (deAna, _, _) = Nuevo(db, ana);
        await deAna.AlternarVigilanciaAsync(4821);

        var fila = Assert.Single(db.WatchedTickets.AsNoTracking().ToList());
        Assert.Equal("ana", fila.WatchedByUser);

        // Alternar otra vez deja de vigilarlo.
        await deAna.AlternarVigilanciaAsync(4821);
        Assert.Empty(db.WatchedTickets.AsNoTracking().ToList());
    }

    // ── Ficha ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaFicha_CuentaRegresionesYDevolucionesDelDuenoActual()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821, asignadoA: "Ana Pérez", correo: "ana@empresa.com");

        var cliente = new ClienteDevOpsDePrueba
        {
            BugsHijos =
            [
                new BugHijoDevOps(1, "Se rompió el alta", "Active", "u1"),
                new BugHijoDevOps(2, "Ya arreglado", "Closed", "u2"),
            ],
            Historial =
            [
                new CambioDeAsignacionDevOps(new DateTime(2026, 8, 1), null, "Ana Pérez", "ana@empresa.com"),
                new CambioDeAsignacionDevOps(new DateTime(2026, 8, 2), "Ana Pérez", "Beto", "beto@empresa.com"),
                new CambioDeAsignacionDevOps(new DateTime(2026, 8, 3), "Beto", "Ana Pérez", "ana@empresa.com"),
            ]
        };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);

        var (ok, _, ficha) = await servicio.FichaAsync(4821);

        Assert.True(ok);
        Assert.Equal(2, ficha!.Regresiones.Count);
        Assert.Equal(1, ficha.RegresionesAbiertas);
        Assert.Equal(1, ficha.Devoluciones);
        Assert.Equal(2, ficha.Manos);
    }

    // ── Reglas de auto-asignación ────────────────────────────────────────────────

    [Fact]
    public async Task UnaReglaSinValor_SeRechaza()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");

        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, _) = await servicio.GuardarReglaAsync(
            new GuardarReglaRequest(0, DevOpsRuleMatch.TagContiene, "  ", 1, 0, true));

        Assert.False(ok);
        Assert.Empty(db.DevOpsAssignmentRules.AsNoTracking().ToList());
    }

    [Fact]
    public async Task ImportarARequerimientos_RepartePorReglaYPorIdentidad()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        SembrarPersona(db, 2, "Beto Ruiz", "beto@empresa.com");
        await ConfigurarAsync(db);

        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var cliente = new ClienteDevOpsDePrueba
        {
            WorkItems =
            [
                // Sin regla que coincida: se reparte por identidad (correo del asignado).
                Item(1, asignadoA: "Ana Pérez", correo: "ana@empresa.com"),
                // Cerrado: no se da de alta nada que ya no haya que cronometrar.
                Item(2, estado: "Closed", asignadoA: "Beto Ruiz", correo: "beto@empresa.com"),
            ]
        };
        var (servicio, _, _) = Nuevo(db, admin, cliente);

        var resultado = await servicio.ImportarComoRequerimientosAsync();

        Assert.True(resultado.Ok);
        var requerimiento = Assert.Single(db.Requirements.AsNoTracking().ToList());
        Assert.Equal("1", requerimiento.ExternalId);

        var asignacion = Assert.Single(db.Assignments.AsNoTracking().ToList());
        Assert.Equal(1, asignacion.DeveloperId);
    }

    [Fact]
    public async Task ImportarDosVeces_NoDuplica()
    {
        var db = TestDb.New();
        SembrarPersona(db, 1, "Ana Pérez", "ana@empresa.com");
        await ConfigurarAsync(db);

        var cliente = new ClienteDevOpsDePrueba { WorkItems = [Item(1)] };
        var (servicio, _, _) = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99), cliente);

        await servicio.ImportarComoRequerimientosAsync();
        await servicio.ImportarComoRequerimientosAsync();

        Assert.Single(db.Requirements.AsNoTracking().ToList());
    }
}
