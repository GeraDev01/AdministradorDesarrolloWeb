using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las costuras entre la integración con Azure DevOps y el resto de la aplicación: detener el
/// cronómetro reporta el tiempo, registrar avance comenta el ticket, cambiar la prioridad reajusta
/// el compromiso y los tickets propios bajan a «Mis asignaciones».
///
/// <para><b>Lo que se cuida aquí es que un fallo de DevOps no se lleve por delante la operación
/// local.</b> Cada pieza por separado ya está probada; lo que puede perderse al unirlas es
/// justamente eso — que el cronómetro se detenga aunque el reporte falle, y que un avance NO cuente
/// cuando el comentario no llegó al ticket.</para>
///
/// <para>El cliente de DevOps es siempre un doble: ninguna prueba sale a la red.</para>
/// </summary>
public class CosturasDevOpsTests
{
    // ── Montaje ──────────────────────────────────────────────────────────────────

    private const string TokenDeLaInstalacion = "token-de-la-instalacion";

    private static DevOpsService Integracion(
        AppDbContext db, ICurrentUser usuario, ClienteDevOpsDePrueba cliente)
    {
        var bitacora = new AuditService(db, usuario, new OrigenDePrueba());
        return new DevOpsService(
            db, usuario, new SettingsService(db, usuario, bitacora),
            new UserSecretsService(db, usuario, new ProtectorSimulado(), bitacora),
            bitacora, new NotificationService(db), cliente);
    }

    private static SlaService Compromisos(
        AppDbContext db, ICurrentUser usuario, ClienteDevOpsDePrueba cliente)
    {
        var bitacora = new AuditService(db, usuario, new OrigenDePrueba());
        return new SlaService(
            db, usuario, bitacora, new SettingsService(db, usuario, bitacora),
            Integracion(db, usuario, cliente));
    }

    private static WorkSessionService Cronometros(AppDbContext db, ICurrentUser usuario) =>
        new(db, usuario, new AuditService(db, usuario, new OrigenDePrueba()));

    private static async Task ConfigurarAsync(
        AppDbContext db, string? modoDeReporte = null, IEnumerable<SlaPolicy>? politicas = null)
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var settings = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsOrgUrl, "https://dev.azure.com/org");
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsProject, "Webpro");
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsPat, TokenDeLaInstalacion);

        if (modoDeReporte != null)
        {
            await settings.GuardarAsync(DevOpsTimeReport.ClaveHabilitado, "true");
            await settings.GuardarAsync(DevOpsTimeReport.ClaveModo, modoDeReporte);
        }

        if (politicas != null)
            await settings.GuardarAsync(SlaPolicyStore.ClaveDeConfiguracion, SlaPolicyStore.Serialize(politicas));
    }

    /// <summary>La política que hace falta para que exista SLA automático: activa y con plazo.</summary>
    private static IEnumerable<SlaPolicy> PoliticaActiva(int prioridad, int horas = 4) =>
        SlaPolicyStore.PorDefecto()
            .Select(p => p.Priority == prioridad ? p with { Enabled = true, Hours = horas } : p);

    private static Developer SembrarPersona(
        AppDbContext db, int id = 1, string nombre = "Ana Pérez", string correo = "ana@empresa.com")
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

    private static DevOpsTicket SembrarTicket(
        AppDbContext db, int numero, string prioridad = "2", string estado = "Active",
        string asignadoA = "Ana Pérez", string? correo = "ana@empresa.com")
    {
        var ticket = new DevOpsTicket
        {
            ExternalId = numero, Title = $"Ticket #{numero}", WorkItemType = "Bug", State = estado,
            Priority = prioridad, AssignedTo = asignadoA, AssignedToUniqueName = correo,
            Description = "detalle",
            Url = $"https://dev.azure.com/org/Webpro/_workitems/edit/{numero}",
            SyncedAt = DateTime.UtcNow, UpdatedAtExternal = DateTime.UtcNow
        };
        db.DevOpsTickets.Add(ticket);
        db.SaveChanges();
        return ticket;
    }

    private static Requirement SembrarRequerimiento(AppDbContext db, int numeroDeTicket)
    {
        var requerimiento = new Requirement
        {
            Title = $"Ticket #{numeroDeTicket}",
            Source = RequirementSource.AzureDevOps,
            ExternalId = numeroDeTicket.ToString(),
            Priority = RequirementPriority.Media,
            Status = RequirementStatus.EnDesarrollo,
            CreatedAt = DateTime.UtcNow
        };
        db.Requirements.Add(requerimiento);
        db.SaveChanges();
        return requerimiento;
    }

    private static UsuarioDePrueba Ana() =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);

    // ── Costura 1: detener el cronómetro reporta el tiempo ───────────────────────

    /// <summary>
    /// Lo mismo que hace el endpoint <c>/cronometro/detener</c>: detiene, pide el total del par
    /// persona/objetivo y lo reporta. Se prueba junto porque lo que puede romperse es la unión —que
    /// el total que se envía no sea el que quedó consolidado—, no cada pieza por su lado.
    /// </summary>
    private static async Task<(bool intentado, bool ok, string mensaje)> DetenerYReportarAsync(
        AppDbContext db, ICurrentUser usuario, ClienteDevOpsDePrueba cliente, int devId, int requerimientoId)
    {
        var cronometros = Cronometros(db, usuario);
        var objetivo = WorkTarget.Requerimiento(requerimientoId);

        await cronometros.StopAsync(devId, objetivo);

        int total = await cronometros.GetTotalSecondsAsync(devId, objetivo);
        return await Integracion(db, usuario, cliente)
            .ReportarTiempoAsync(requerimientoId, total);
    }

    /// <summary>Una sesión pausada con tiempo ya acumulado, para no depender del reloj.</summary>
    private static void SembrarCronometroPausado(AppDbContext db, int devId, int requerimientoId, int segundos)
    {
        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = devId,
            RequirementId = requerimientoId,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            Status = WorkSessionStatus.Pausada,
            AccumulatedSeconds = segundos
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task AlDetener_ElTiempoSeRegistraEnElTicketDeDevOps()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, modoDeReporte: "comentario");
        SembrarTicket(db, 4821);
        var requerimiento = SembrarRequerimiento(db, 4821);
        SembrarCronometroPausado(db, devId: 1, requerimiento.Id, segundos: 3600);

        var cliente = new ClienteDevOpsDePrueba();
        var (intentado, ok, _) = await DetenerYReportarAsync(db, Ana(), cliente, 1, requerimiento.Id);

        Assert.True(intentado);
        Assert.True(ok);
        Assert.Single(cliente.ComentariosPublicados);
        Assert.Equal(3600, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task SiDevOpsEstaCaido_ElCronometroSeDetieneYElTiempoSeConsolidaIgual()
    {
        // Es la razón de que el reporte vaya DESPUÉS de detener: parar el cronómetro es lo que la
        // persona pidió, y no puede depender de que un servicio externo conteste.
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, modoDeReporte: "comentario");
        SembrarTicket(db, 4821);
        var requerimiento = SembrarRequerimiento(db, 4821);
        SembrarCronometroPausado(db, devId: 1, requerimiento.Id, segundos: 3600);

        var cliente = new ClienteDevOpsDePrueba
        {
            Fallo = new ErrorDeAzureDevOps("No se pudo contactar con Azure DevOps: se agotó el tiempo.")
        };
        var (intentado, ok, mensaje) = await DetenerYReportarAsync(db, Ana(), cliente, 1, requerimiento.Id);

        Assert.True(intentado);
        Assert.False(ok);
        Assert.Contains("No se pudo contactar", mensaje);

        // Lo local quedó intacto: sesión detenida, tiempo acumulado, y la marca de agua sin avanzar
        // para que el próximo intento vuelva a mandar las mismas horas.
        var sesion = db.WorkSessions.AsNoTracking().Single();
        Assert.Equal(WorkSessionStatus.Detenida, sesion.Status);
        Assert.Equal(3600, sesion.AccumulatedSeconds);
        Assert.Equal(0, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task DetenerUnaActividadDelPool_NoIntentaNadaContraDevOps()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, modoDeReporte: "comentario");

        db.DevActivities.Add(new DevActivity
        {
            Id = 7, DeveloperId = 1, Title = "Apoyo a soporte",
            Status = DevActivityStatus.Abierta, CreatedAt = DateTime.UtcNow
        });
        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = 1, ActivityId = 7,
            StartedAt = DateTime.UtcNow.AddHours(-1), CreatedAt = DateTime.UtcNow.AddHours(-1),
            Status = WorkSessionStatus.Pausada, AccumulatedSeconds = 3600
        });
        db.SaveChanges();

        var cliente = new ClienteDevOpsDePrueba();
        var usuario = Ana();
        await Cronometros(db, usuario).StopAsync(1, WorkTarget.Actividad(7));

        // El endpoint ni siquiera llama al reporte cuando el objetivo no es un requerimiento; lo que
        // se comprueba aquí es que detenerlo no deja nada pendiente contra la integración.
        Assert.Empty(cliente.Llamadas);
        Assert.Equal(WorkSessionStatus.Detenida, db.WorkSessions.AsNoTracking().Single().Status);
    }

    // ── Costura 2: registrar avance comentando el ticket ─────────────────────────

    private static SlaCommitment SembrarCompromiso(
        AppDbContext db, int requerimientoId, int? ticket, int developerId = 1,
        SlaStatus estado = SlaStatus.Activo)
    {
        var ahora = DateTime.UtcNow;
        var sla = new SlaCommitment
        {
            RequirementId = requerimientoId,
            DeveloperId = developerId,
            DevOpsTicketExternalId = ticket,
            DueAtUtc = ahora.AddDays(2),
            ReminderEveryHours = 24,
            NextReminderAtUtc = ahora.AddHours(-1),   // el recordatorio ya venció: toca comentar
            Status = estado,
            CreatedAt = ahora
        };
        db.SlaCommitments.Add(sla);
        db.SaveChanges();
        return sla;
    }

    [Fact]
    public async Task RegistrarAvance_ApuntaLaConstanciaYReprogramaSoloSiElComentarioQuedoEnElTicket()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);
        var requerimiento = SembrarRequerimiento(db, 4821);
        var sla = SembrarCompromiso(db, requerimiento.Id, ticket: 4821);

        var cliente = new ClienteDevOpsDePrueba();
        var (ok, mensaje) = await Compromisos(db, Ana(), cliente)
            .RegistrarAvanceAsync(sla.Id, "Ya quedó la corrección, falta probarla.", []);

        Assert.True(ok);
        Assert.Contains("recordatorio", mensaje);
        Assert.Single(cliente.ComentariosPublicados);

        var guardado = db.SlaCommitments.AsNoTracking().Single();
        Assert.Equal(1, guardado.CommentCount);
        Assert.NotNull(guardado.LastCommentAtUtc);
        Assert.True(guardado.NextReminderAtUtc > DateTime.UtcNow);   // dejó de reclamar
    }

    [Fact]
    public async Task SiElComentarioNoLlegaAlTicket_ElAvanceNoCuenta()
    {
        // Es LA regla del botón: sin constancia en el ticket, el compromiso sigue reclamando. Un
        // «registrar avance» que solo aplazara el aviso sería peor que no tenerlo.
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);
        var requerimiento = SembrarRequerimiento(db, 4821);
        var sla = SembrarCompromiso(db, requerimiento.Id, ticket: 4821);
        var recordatorioAntes = sla.NextReminderAtUtc;

        var cliente = new ClienteDevOpsDePrueba
        {
            Fallo = new ErrorDeAzureDevOps("Tu token de Azure DevOps expiró (401). Genera otro.")
        };
        var (ok, mensaje) = await Compromisos(db, Ana(), cliente)
            .RegistrarAvanceAsync(sla.Id, "Ya casi", []);

        Assert.False(ok);
        Assert.Contains("expiró", mensaje);   // el mensaje de la integración, tal cual

        var guardado = db.SlaCommitments.AsNoTracking().Single();
        Assert.Equal(0, guardado.CommentCount);
        Assert.Null(guardado.LastCommentAtUtc);
        Assert.Equal(recordatorioAntes, guardado.NextReminderAtUtc);
    }

    [Fact]
    public async Task RegistrarAvance_SinTicketLigado_SeExplicaYNoSeLlamaADevOps()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        var requerimiento = SembrarRequerimiento(db, 4821);
        var sla = SembrarCompromiso(db, requerimiento.Id, ticket: null);

        var cliente = new ClienteDevOpsDePrueba();
        var (ok, mensaje) = await Compromisos(db, Ana(), cliente)
            .RegistrarAvanceAsync(sla.Id, "Avancé", []);

        Assert.False(ok);
        Assert.Contains("no tiene ticket", mensaje);
        Assert.Empty(cliente.Llamadas);
    }

    [Fact]
    public async Task RegistrarAvance_EnElCompromisoDeOtraPersona_NoSePuede()
    {
        var db = TestDb.New();
        SembrarPersona(db, id: 1);
        SembrarPersona(db, id: 2, nombre: "Beto Ruiz", correo: "beto@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821, asignadoA: "Beto Ruiz", correo: "beto@empresa.com");
        var requerimiento = SembrarRequerimiento(db, 4821);
        var sla = SembrarCompromiso(db, requerimiento.Id, ticket: 4821, developerId: 2);

        var cliente = new ClienteDevOpsDePrueba();
        var servicio = Compromisos(db, Ana(), cliente);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => servicio.RegistrarAvanceAsync(sla.Id, "no es mío", []));
        Assert.Empty(cliente.Llamadas);
    }

    [Fact]
    public async Task RegistrarAvance_SobreUnTicketQueNoEstaATuNombre_SeRechazaEnElServidor()
    {
        // El compromiso SÍ es suyo, pero el ticket está a nombre de otro: la guarda de DevOps manda,
        // porque lo que se publicaría iría firmado con su token contra el trabajo de alguien más.
        var db = TestDb.New();
        SembrarPersona(db, id: 1);
        SembrarPersona(db, id: 2, nombre: "Beto Ruiz", correo: "beto@empresa.com");
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821, asignadoA: "Beto Ruiz", correo: "beto@empresa.com");
        var requerimiento = SembrarRequerimiento(db, 4821);
        var sla = SembrarCompromiso(db, requerimiento.Id, ticket: 4821, developerId: 1);

        var cliente = new ClienteDevOpsDePrueba();
        var (ok, mensaje) = await Compromisos(db, Ana(), cliente)
            .RegistrarAvanceAsync(sla.Id, "voy avanzando", []);

        Assert.False(ok);
        Assert.Contains("no está a tu nombre", mensaje);
        Assert.Empty(cliente.ComentariosPublicados);
        Assert.Equal(0, db.SlaCommitments.AsNoTracking().Single().CommentCount);
    }

    // ── Costura 3: cambiar la prioridad reajusta el SLA ──────────────────────────

    [Fact]
    public async Task CambiarPrioridad_ReprogramaElCompromisoVigenteAlPlazoDeLaNuevaPrioridad()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, politicas: PoliticaActiva(prioridad: 1, horas: 4));
        SembrarTicket(db, 4821, prioridad: "3");
        var requerimiento = SembrarRequerimiento(db, 4821);
        SembrarCompromiso(db, requerimiento.Id, ticket: 4821);

        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var (ok, _) = await Integracion(db, admin, new ClienteDevOpsDePrueba())
            .CambiarPrioridadAsync(4821, 1);

        Assert.True(ok);

        var sla = db.SlaCommitments.AsNoTracking().Single();
        Assert.True(sla.DueAtUtc < DateTime.UtcNow.AddHours(5));   // el plazo de «muy alta», no los 2 días
        Assert.Contains("prioridad 1", sla.Notes);
    }

    [Fact]
    public async Task CambiarPrioridad_SinCompromisoPrevio_LeCreaUnoAlDuenoDelTicket()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, politicas: PoliticaActiva(prioridad: 1, horas: 4));
        SembrarTicket(db, 4821, prioridad: "3");
        SembrarRequerimiento(db, 4821);

        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        await Integracion(db, admin, new ClienteDevOpsDePrueba())
            .CambiarPrioridadAsync(4821, 1);

        var sla = db.SlaCommitments.AsNoTracking().Single();
        Assert.Equal(1, sla.DeveloperId);        // el asignado del ticket, empatado por identidad
        Assert.Equal(4821, sla.DevOpsTicketExternalId);
        Assert.Equal(SlaStatus.Activo, sla.Status);
    }

    [Fact]
    public async Task CambiarPrioridad_NoResucitaUnCompromisoQueAlguienCancelo()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, politicas: PoliticaActiva(prioridad: 1));
        SembrarTicket(db, 4821, prioridad: "3");
        var requerimiento = SembrarRequerimiento(db, 4821);
        SembrarCompromiso(db, requerimiento.Id, ticket: 4821, estado: SlaStatus.Cancelado);

        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        await Integracion(db, admin, new ClienteDevOpsDePrueba())
            .CambiarPrioridadAsync(4821, 1);

        var sla = Assert.Single(db.SlaCommitments.AsNoTracking().ToList());
        Assert.Equal(SlaStatus.Cancelado, sla.Status);
    }

    [Fact]
    public async Task CambiarPrioridad_SinPoliticaActiva_NoTocaNingunCompromiso()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);   // todas las políticas desactivadas, que es lo por omisión
        SembrarTicket(db, 4821, prioridad: "3");
        SembrarRequerimiento(db, 4821);

        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        await Integracion(db, admin, new ClienteDevOpsDePrueba())
            .CambiarPrioridadAsync(4821, 1);

        Assert.Empty(db.SlaCommitments.AsNoTracking().ToList());
    }

    // ── Costura 5: materializar los tickets propios ──────────────────────────────

    [Fact]
    public async Task Materializar_DaDeAltaLosAbiertosATuNombreYTeLosAsigna()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);
        SembrarTicket(db, 4822, estado: "Closed");                               // cerrado: no entra
        SembrarTicket(db, 4823, asignadoA: "Beto Ruiz", correo: "beto@empresa.com");   // ajeno

        var (ok, mensaje) = await Integracion(db, Ana(), new ClienteDevOpsDePrueba())
            .MaterializarMisAsignadosAsync();

        Assert.True(ok);
        Assert.Contains("1", mensaje);

        var requerimiento = Assert.Single(db.Requirements.AsNoTracking().ToList());
        Assert.Equal("4821", requerimiento.ExternalId);
        Assert.Equal(RequirementSource.AzureDevOps, requerimiento.Source);

        var asignacion = Assert.Single(db.Assignments.AsNoTracking().ToList());
        Assert.Equal(1, asignacion.DeveloperId);
    }

    [Fact]
    public async Task Materializar_NoDuplicaNiPisaElAvanceMedidoAqui()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var servicio = Integracion(db, Ana(), new ClienteDevOpsDePrueba());
        await servicio.MaterializarMisAsignadosAsync();

        // La persona avanza el requerimiento aquí: eso es lo que el ticket no sabe.
        var requerimiento = db.Requirements.Single();
        requerimiento.Status = RequirementStatus.EnPruebas;
        requerimiento.ProgressPercent = 80;
        db.SaveChanges();

        await servicio.MaterializarMisAsignadosAsync();

        Assert.Single(db.Requirements.AsNoTracking().ToList());
        Assert.Single(db.Assignments.AsNoTracking().ToList());

        var despues = db.Requirements.AsNoTracking().Single();
        Assert.Equal(RequirementStatus.EnPruebas, despues.Status);
        Assert.Equal(80, despues.ProgressPercent);
    }

    [Fact]
    public async Task Materializar_PoneElSlaAutomaticoQueDictaLaPrioridadDelTicket()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, politicas: PoliticaActiva(prioridad: 1, horas: 4));
        SembrarTicket(db, 4821, prioridad: "1");
        SembrarTicket(db, 4822, prioridad: "4");   // su política sigue desactivada: sin compromiso

        var (ok, _) = await Integracion(db, Ana(), new ClienteDevOpsDePrueba())
            .MaterializarMisAsignadosAsync();

        Assert.True(ok);

        var sla = Assert.Single(db.SlaCommitments.AsNoTracking().ToList());
        Assert.Equal(4821, sla.DevOpsTicketExternalId);
        Assert.Equal(1, sla.DeveloperId);
        Assert.True(sla.DueAtUtc <= DateTime.UtcNow.AddHours(4).AddMinutes(1));

        // El recordatorio nunca se programa más allá del vencimiento: la misma regla que el alta a
        // mano, y por eso se reutiliza en vez de recalcularse aquí.
        Assert.True(sla.NextReminderAtUtc <= sla.DueAtUtc);
    }

    [Fact]
    public async Task Materializar_NoRecreaUnCompromisoQueYaExistio()
    {
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db, politicas: PoliticaActiva(prioridad: 1));
        SembrarTicket(db, 4821, prioridad: "1");

        var servicio = Integracion(db, Ana(), new ClienteDevOpsDePrueba());
        await servicio.MaterializarMisAsignadosAsync();

        // Alguien lo cierra a propósito. Volver a pasar no debe abrirle otro.
        var sla = db.SlaCommitments.Single();
        sla.Status = SlaStatus.Cumplido;
        db.SaveChanges();

        await servicio.MaterializarMisAsignadosAsync();

        Assert.Single(db.SlaCommitments.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Materializar_SinFichaDeDesarrollador_SeExplica()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);

        var sinFicha = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: null, userId: 5);
        var (ok, mensaje) = await Integracion(db, sinFicha, new ClienteDevOpsDePrueba())
            .MaterializarMisAsignadosAsync();

        Assert.False(ok);
        Assert.Contains("ficha de desarrollador", mensaje);
    }

    [Fact]
    public async Task Materializar_NoSaleALaRed()
    {
        // Trabaja sobre lo YA sincronizado a propósito: la pantalla puede llamarlo sin gastar una
        // petición a DevOps por visita.
        var db = TestDb.New();
        SembrarPersona(db);
        await ConfigurarAsync(db);
        SembrarTicket(db, 4821);

        var cliente = new ClienteDevOpsDePrueba();
        await Integracion(db, Ana(), cliente)
            .MaterializarMisAsignadosAsync();

        Assert.Empty(cliente.Llamadas);
    }
}
