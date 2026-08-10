using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La limpieza de datos: lo único de la aplicación que borra de verdad y sin vuelta atrás.
///
/// Lo que estas pruebas cuidan son las tres promesas que hacen que la pantalla se pueda usar sin
/// miedo: que NADIE llegue a borrar sin ser el líder, sin escribir la palabra y sin su contraseña;
/// que un área se lleve exactamente lo que su ficha dice que arrastra —ni más ni menos—; y que la
/// bitácora conserve el rastro de la propia limpieza, incluso cuando lo que se está borrando es la
/// bitácora.
/// </summary>
public class DataCleanupServiceTests
{
    private const string Contrasena = "contrasena-de-prueba";

    private static DataCleanupService Svc(AppDbContext db, ICurrentUser cu)
    {
        var bitacora = new AuditService(db, cu, new OrigenDePrueba());
        return new DataCleanupService(db, cu, bitacora, new AuthService(db, cu, bitacora));
    }

    /// <summary>Una cuenta de líder con contraseña real: la reautenticación la comprueba de verdad.</summary>
    private static User SembrarLider(AppDbContext db, string usuario = "lider")
    {
        var cuenta = new User
        {
            Username = usuario,
            FullName = "Líder",
            PasswordHash = PasswordHasher.Hash(Contrasena),
            Role = UserRole.Admin,
            IsActive = true
        };
        db.Users.Add(cuenta);
        db.SaveChanges();
        return cuenta;
    }

    private static ICurrentUser Como(User cuenta) =>
        new UsuarioDePrueba
        {
            UserId = cuenta.Id,
            Username = cuenta.Username,
            FullName = cuenta.FullName,
            Role = cuenta.Role
        };

    // ── Permisos y confirmaciones ────────────────────────────────────────────────

    [Fact]
    public async Task Contar_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ContarAsync());
    }

    [Fact]
    public async Task Limpiar_LoExigeElLider()
    {
        var db = TestDb.New();
        db.Notifications.Add(new Notification { ForUserId = 1, Title = "x", Message = "y" });
        db.SaveChanges();

        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.LimpiarAsync(["avisos"], DataCleanupService.FraseConfirmacion, Contrasena));
        Assert.Equal(1, db.Notifications.Count());
    }

    [Fact]
    public async Task Limpiar_SinLaPalabraDeConfirmacion_NoBorraNada()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);
        db.Notifications.Add(new Notification { ForUserId = lider.Id, Title = "x", Message = "y" });
        db.SaveChanges();

        var (ok, mensaje, areas) = await Svc(db, Como(lider))
            .LimpiarAsync(["avisos"], "sí", Contrasena);

        Assert.False(ok);
        Assert.Contains(DataCleanupService.FraseConfirmacion, mensaje);
        Assert.Empty(areas);
        Assert.Equal(1, db.Notifications.Count());
    }

    [Fact]
    public async Task Limpiar_ConContrasenaEquivocada_NoBorraNadaYQuedaRegistrado()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);
        db.Notifications.Add(new Notification { ForUserId = lider.Id, Title = "x", Message = "y" });
        db.SaveChanges();

        var (ok, mensaje, _) = await Svc(db, Como(lider))
            .LimpiarAsync(["avisos"], DataCleanupService.FraseConfirmacion, "otra-cosa");

        Assert.False(ok);
        Assert.Contains("contraseña", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, db.Notifications.Count());

        // El intento rechazado es justo lo que una bitácora tiene que poder enseñar después.
        var denegado = db.AuditLogs.Single();
        Assert.Equal(AuditOutcome.Denegado, denegado.Outcome);
        Assert.Equal("Limpieza", denegado.EntityType);
    }

    [Fact]
    public async Task Limpiar_ContarLosIntentosFallidos_NoBloqueaLaCuentaDelLider()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);
        var svc = Svc(db, Como(lider));

        // Más intentos que el tope de bloqueo de AuthService: si la reautenticación pasara por él,
        // el líder acabaría fuera de la aplicación por teclear mal su contraseña en esta pantalla.
        for (int i = 0; i < AuthService.MaxFailedAttempts + 2; i++)
            await svc.LimpiarAsync(["avisos"], DataCleanupService.FraseConfirmacion, "mal");

        var cuenta = db.Users.Single();
        Assert.Equal(0, cuenta.FailedLoginCount);
        Assert.Null(cuenta.LockoutUntil);
    }

    [Fact]
    public async Task Limpiar_SinApartadosSeleccionados_LoDice()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);

        var (ok, mensaje, _) = await Svc(db, Como(lider))
            .LimpiarAsync([], DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.False(ok);
        Assert.Contains("apartado", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Lo que arrastra cada área ────────────────────────────────────────────────

    [Fact]
    public async Task Limpiar_Requerimientos_SeLlevaLoQueCuelgaYConservaLosPuntos()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);

        var dev = new Developer { FullName = "Ana", IsActive = true };
        var criterio = new ScoringCriterion { Name = "Entrega" };
        db.Developers.Add(dev);
        db.ScoringCriteria.Add(criterio);
        db.SaveChanges();

        var req = new Requirement { Title = "Pantalla nueva" };
        db.Requirements.Add(req);
        db.SaveChanges();

        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev.Id });
        db.RequirementAttachments.Add(new RequirementAttachment
        {
            RequirementId = req.Id, FileName = "doc.pdf", FileBytes = [1, 2, 3], SizeBytes = 3
        });
        db.WorkSessions.Add(new WorkSession
        {
            RequirementId = req.Id, DeveloperId = dev.Id, Status = WorkSessionStatus.Detenida,
            AccumulatedSeconds = 3600
        });
        db.SlaCommitments.Add(new SlaCommitment
        {
            RequirementId = req.Id, DeveloperId = dev.Id, DueAtUtc = DateTime.UtcNow.AddDays(1)
        });
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = criterio.Id, RequirementId = req.Id,
            Points = 5, Year = 2026, Month = 8
        });
        db.WorkIntervals.Add(new WorkInterval
        {
            DeveloperId = dev.Id, RequirementId = req.Id, Seconds = 60,
            StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, LocalDate = DateTime.Today
        });
        db.SaveChanges();

        var (ok, _, areas) = await Svc(db, Como(lider))
            .LimpiarAsync(["requerimientos"], DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.True(ok);
        Assert.True(areas.Single().Ok);
        Assert.Equal(1, areas.Single().Habia);

        Assert.Empty(db.Requirements);
        Assert.Empty(db.Assignments);
        Assert.Empty(db.RequirementAttachments);
        Assert.Empty(db.WorkSessions);
        Assert.Empty(db.SlaCommitments);

        // Los puntos ganados y las horas trabajadas son del desarrollador, no del requerimiento: se
        // conservan desligados en vez de irse con él.
        var punto = db.PointEntries.AsNoTracking().Single();
        Assert.Null(punto.RequirementId);
        Assert.Equal(5, punto.Points);

        var tramo = db.WorkIntervals.AsNoTracking().Single();
        Assert.Null(tramo.RequirementId);

        // Y la ficha de la persona tampoco se toca: eso es otra área.
        Assert.Equal(1, db.Developers.Count());
    }

    [Fact]
    public async Task Limpiar_Sprints_DevuelveLosRequerimientosAlBacklogEnVezDeBorrarlos()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);

        var sprint = new Sprint { Name = "Sprint 1", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(14) };
        db.Sprints.Add(sprint);
        db.SaveChanges();

        db.Requirements.Add(new Requirement { Title = "Con sprint", SprintId = sprint.Id });
        db.SaveChanges();

        var (ok, _, _) = await Svc(db, Como(lider))
            .LimpiarAsync(["sprints"], DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.True(ok);
        Assert.Empty(db.Sprints);

        var req = db.Requirements.AsNoTracking().Single();
        Assert.Null(req.SprintId);
    }

    [Fact]
    public async Task Limpiar_Usuarios_NuncaBorraAlLiderNiLaPropiaCuenta()
    {
        var db = TestDb.New();
        var yo = SembrarLider(db, "yo");
        SembrarLider(db, "otro-lider");

        db.Users.Add(new User
        {
            Username = "dev", FullName = "Dev", PasswordHash = PasswordHasher.Hash("x"),
            Role = UserRole.Desarrollador
        });
        db.SaveChanges();

        var (ok, _, areas) = await Svc(db, Como(yo))
            .LimpiarAsync(["usuarios"], DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.True(ok);
        Assert.Equal(1, areas.Single().Filas);

        var quedan = db.Users.AsNoTracking().Select(u => u.Username).OrderBy(u => u).ToList();
        Assert.Equal(["otro-lider", "yo"], quedan);
    }

    [Fact]
    public async Task Limpiar_Desarrolladores_DesligaLoQueSobreviveAlaPersona()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);

        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        lider.DeveloperId = dev.Id;
        db.Notes.Add(new Note { Title = "Recordatorio", DeveloperId = dev.Id });

        var minuta = new Minute { Title = "Daily", Date = DateTime.UtcNow };
        db.Minutes.Add(minuta);
        db.SaveChanges();

        db.MinuteActionItems.Add(new MinuteActionItem
        {
            MinuteId = minuta.Id, Description = "Revisar", ResponsibleDeveloperId = dev.Id
        });
        db.SaveChanges();

        var (ok, _, areas) = await Svc(db, Como(lider))
            .LimpiarAsync(["desarrolladores"], DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.True(ok);
        Assert.True(areas.Single().Ok, areas.Single().Error);
        Assert.Empty(db.Developers);

        // Nada de esto se borra: deja de apuntar a la ficha.
        Assert.Null(db.Users.AsNoTracking().Single(u => u.Id == lider.Id).DeveloperId);
        Assert.Null(db.Notes.AsNoTracking().Single().DeveloperId);
        Assert.Null(db.MinuteActionItems.AsNoTracking().Single().ResponsibleDeveloperId);
        Assert.Equal(1, db.Minutes.Count());
    }

    // ── La bitácora ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ordenar_PoneLaBitacoraPrimero()
    {
        var orden = DataCleanupService.Ordenar(["avisos", DataCleanupService.ClaveBitacora, "contactos"]);

        Assert.Equal(DataCleanupService.ClaveBitacora, orden[0].Clave);
        Assert.Equal(3, orden.Count);
    }

    [Fact]
    public async Task Limpiar_ConLaBitacoraDentro_ConservaElRastroDeLaPropiaLimpieza()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);

        db.AuditLogs.Add(new AuditLog { UserName = "viejo", Action = AuditAction.Login, Timestamp = DateTime.UtcNow });
        db.Notifications.Add(new Notification { ForUserId = lider.Id, Title = "x", Message = "y" });
        db.Contacts.Add(new Contact { Name = "Proveedor" });
        db.SaveChanges();

        var (ok, _, areas) = await Svc(db, Como(lider)).LimpiarAsync(
            ["avisos", DataCleanupService.ClaveBitacora, "contactos"],
            DataCleanupService.FraseConfirmacion, Contrasena);

        Assert.True(ok);
        Assert.All(areas, a => Assert.True(a.Ok, a.Error));
        Assert.Empty(db.Notifications);
        Assert.Empty(db.Contacts);

        // La bitácora se borró PRIMERO, así que lo que queda es el rastro de esta misma pasada: el
        // renglón de la propia bitácora y el de las otras dos áreas. Al revés, la operación más
        // destructiva de la aplicación sería la única que no deja huella.
        var renglones = db.AuditLogs.AsNoTracking().ToList();
        Assert.Equal(3, renglones.Count);
        Assert.All(renglones, r => Assert.Equal("Limpieza", r.EntityType));
        Assert.Contains(renglones, r => r.EntityId == DataCleanupService.ClaveBitacora);
        Assert.Contains(renglones, r => r.EntityId == "avisos");
        Assert.Contains(renglones, r => r.EntityId == "contactos");

        // Todas comparten correlación: es lo que permite leer la pasada completa de un vistazo.
        Assert.Single(renglones.Select(r => r.CorrelationId).Distinct());
    }

    // ── Recuento ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Contar_DevuelveUnaEntradaPorArea()
    {
        var db = TestDb.New();
        var lider = SembrarLider(db);
        db.Contacts.Add(new Contact { Name = "Proveedor" });
        db.SaveChanges();

        var cuentas = await Svc(db, Como(lider)).ContarAsync();

        Assert.Equal(DataCleanupService.Areas.Length, cuentas.Count);
        Assert.Equal(1, cuentas["contactos"]);
        Assert.Equal(0, cuentas["requerimientos"]);
    }

    [Fact]
    public void Areas_TodasTienenClaveUnicaYFichaCompleta()
    {
        // El catálogo es lo que la pantalla enseña antes de que alguien marque una casilla: una
        // clave repetida haría que dos apartados se pisaran en la bitácora, y una ficha vacía
        // dejaría a quien decide sin saber qué se lleva por delante.
        Assert.Equal(DataCleanupService.Areas.Length,
                     DataCleanupService.Areas.Select(a => a.Clave).Distinct().Count());

        Assert.All(DataCleanupService.Areas, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Nombre));
            Assert.False(string.IsNullOrWhiteSpace(a.Grupo));
            Assert.False(string.IsNullOrWhiteSpace(a.Arrastra));
        });
    }
}
