using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// La utilería de limpieza. Es la única operación de la aplicación sin deshacer, así que lo que se
/// prueba no es que borre —eso es lo fácil— sino los tres compromisos que la hacen usable: que
/// borrar un apartado NO se lleve lo que no le corresponde, que las protecciones que impiden dejar
/// la aplicación inservible no se puedan saltar, y que quede constancia de lo que se borró.
/// </summary>
public class DataCleanupTests
{
    private static DataCleanupService Svc(AppDbContext db, DbContextOptions<AppDbContext> opts, CurrentUserContext cu) =>
        new(db, opts, new AuditService(db, cu), cu);

    private static (DataCleanupService svc, AppDbContext db) Admin(int userId = 99)
    {
        var (db, opts) = TestDb.NewConOpciones();
        return (Svc(db, opts, Ctx.As(UserRole.Admin, userId: userId)), db);
    }

    private static Developer NuevoDev(AppDbContext db, string nombre = "Ana")
    {
        var d = new Developer { FullName = nombre };
        db.Developers.Add(d); db.SaveChanges();
        return d;
    }

    // ── Permisos ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Desarrollador)]
    [InlineData(UserRole.Operaciones)]
    public async Task SoloElAdministrador_ENTRA(UserRole rol)
    {
        // Ni siquiera contar: el recuento por apartado ya dibuja el tamaño de la operación de todo
        // el equipo, y esta pantalla no es de nadie más que del administrador.
        var (db, opts) = TestDb.NewConOpciones();
        var svc = Svc(db, opts, Ctx.As(rol, userId: 7));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ContarAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.LimpiarAsync(["notas"]));
    }

    // ── El catálogo ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CadaArea_TieneClaveUnica_YFichaCompleta()
    {
        // Las claves quedan escritas en la bitácora: duplicarlas la haría ambigua.
        var claves = DataCleanupService.Areas.Select(a => a.Clave).ToList();
        Assert.Equal(claves.Count, claves.Distinct().Count());

        Assert.All(DataCleanupService.Areas, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Grupo));
            Assert.False(string.IsNullOrWhiteSpace(a.Nombre));
            // «Qué más se lleva» es obligatorio: un área que no lo declara es la que sorprende.
            Assert.False(string.IsNullOrWhiteSpace(a.Arrastra));
        });
    }

    [Fact]
    public async Task Contar_DiceLoQueHay()
    {
        var (svc, db) = Admin();
        db.Notes.AddRange(new Note { Title = "una" }, new Note { Title = "otra" });
        db.Contacts.Add(new Contact { Name = "Proveedor" });
        db.SaveChanges();

        var cuentas = await svc.ContarAsync();

        Assert.Equal(2, cuentas["notas"]);
        Assert.Equal(1, cuentas["contactos"]);
        Assert.Equal(0, cuentas["minutas"]);
    }

    [Fact]
    public async Task ClaveDesconocida_NoHaceNada()
    {
        var (svc, db) = Admin();
        db.Notes.Add(new Note { Title = "sobrevive" }); db.SaveChanges();

        var resultados = await svc.LimpiarAsync(["no-existe"]);

        Assert.Empty(resultados);
        Assert.Equal(1, await db.Notes.CountAsync());
    }

    // ── Que cada área se lleve lo suyo, y solo lo suyo ───────────────────────────────────

    [Fact]
    public async Task BorrarUnArea_NoTocaLasDemas()
    {
        var (svc, db) = Admin();
        db.Notes.Add(new Note { Title = "se va" });
        db.Contacts.Add(new Contact { Name = "se queda" });
        db.SaveChanges();

        await svc.LimpiarAsync(["notas"]);

        Assert.Equal(0, await db.Notes.CountAsync());
        Assert.Equal(1, await db.Contacts.CountAsync());
    }

    [Fact]
    public async Task BorrarRequerimientos_SeLlevaLoQueCuelga_YConservaLoQueNo()
    {
        var (svc, db) = Admin();
        var dev = NuevoDev(db);
        var crit = new ScoringCriterion { Name = "Entrega a tiempo" };
        db.ScoringCriteria.Add(crit); db.SaveChanges();

        var req = new Requirement { Title = "Requerimiento de prueba" };
        db.Requirements.Add(req); db.SaveChanges();
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev.Id });
        db.WorkSessions.Add(new WorkSession { RequirementId = req.Id, DeveloperId = dev.Id });
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, RequirementId = req.Id,
            Points = 5, Year = 2026, Month = 8
        });
        db.SaveChanges();

        await svc.LimpiarAsync(["requerimientos"]);

        Assert.Equal(0, await db.Requirements.CountAsync());
        Assert.Equal(0, await db.Assignments.CountAsync());
        Assert.Equal(0, await db.WorkSessions.CountAsync());
        // La persona y sus puntos ganados son suyos, no del requerimiento: se conservan, sueltos.
        Assert.Equal(1, await db.Developers.CountAsync());
        var punto = await db.PointEntries.AsNoTracking().SingleAsync();
        Assert.Equal(5, punto.Points);
        Assert.Null(punto.RequirementId);
    }

    [Fact]
    public async Task BorrarSprints_DevuelveElTrabajoAlBacklog_NoLoBorra()
    {
        // Es el mismo criterio que el modelo declara con SetNull. Si esto se rompiera, «limpiar los
        // sprints de prueba» se llevaría por delante el trabajo real que contenían.
        var (svc, db) = Admin();
        var sprint = new Sprint { Name = "Sprint de prueba", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(7) };
        db.Sprints.Add(sprint); db.SaveChanges();
        db.Requirements.Add(new Requirement { Title = "Trabajo real", SprintId = sprint.Id });
        db.SaveChanges();

        await svc.LimpiarAsync(["sprints"]);

        Assert.Equal(0, await db.Sprints.CountAsync());
        var req = await db.Requirements.AsNoTracking().SingleAsync();
        Assert.Equal("Trabajo real", req.Title);
        Assert.Null(req.SprintId);
    }

    [Fact]
    public async Task BorrarDesarrolladores_DesvinculaLaCuenta_EnVezDeBorrarla()
    {
        var (svc, db) = Admin();
        var dev = NuevoDev(db);
        db.Users.Add(new User { Username = "ana", PasswordHash = "x", Role = UserRole.Desarrollador, DeveloperId = dev.Id });
        db.Teams.Add(new Team { Name = "Equipo A", LeadDeveloperId = dev.Id });
        db.SaveChanges();

        await svc.LimpiarAsync(["desarrolladores"]);

        Assert.Equal(0, await db.Developers.CountAsync());
        // La cuenta y el equipo sobreviven sueltos: borrar la ficha de una persona no es dar de baja
        // su acceso ni disolver su equipo.
        Assert.Null((await db.Users.AsNoTracking().SingleAsync()).DeveloperId);
        Assert.Null((await db.Teams.AsNoTracking().SingleAsync()).LeadDeveloperId);
    }

    [Fact]
    public async Task BorrarEquipos_DejaALosDesarrolladoresSinEquipo()
    {
        var (svc, db) = Admin();
        var equipo = new Team { Name = "Equipo A" };
        db.Teams.Add(equipo); db.SaveChanges();
        var dev = NuevoDev(db);
        dev.TeamId = equipo.Id; db.SaveChanges();

        await svc.LimpiarAsync(["equipos"]);

        Assert.Equal(0, await db.Teams.CountAsync());
        Assert.Null((await db.Developers.AsNoTracking().SingleAsync()).TeamId);
    }

    [Fact]
    public async Task BorrarElForo_AguantaLosComentariosAnidados()
    {
        // Un comentario apunta a su padre. Borrando de la publicación hacia abajo, la primera fila
        // que se va deja huérfanas a las siguientes y la FK lo rechaza a mitad del borrado.
        var (svc, db) = Admin();
        var pub = new ForumPost { AuthorUserId = 1, AuthorName = "Ana", Body = "publicación", Depth = 0 };
        db.ForumPosts.Add(pub); db.SaveChanges();
        pub.RootId = pub.Id; db.SaveChanges();

        var com = new ForumPost { AuthorUserId = 1, AuthorName = "Ana", Body = "comentario", ParentId = pub.Id, RootId = pub.Id, Depth = 1 };
        db.ForumPosts.Add(com); db.SaveChanges();
        db.ForumPosts.Add(new ForumPost { AuthorUserId = 1, AuthorName = "Ana", Body = "respuesta", ParentId = com.Id, RootId = pub.Id, Depth = 2 });
        db.ForumLikes.Add(new ForumLike { PostId = pub.Id, UserId = 1 });
        // Una imagen incrustada también apunta a su entrada: si no se va primero, la FK corta el
        // borrado a medias y el foro queda a mitad de limpiar.
        db.ForumAttachments.Add(new ForumAttachment
        {
            PostId = pub.Id, FileName = "captura.png", ContentType = "image/png",
            Bytes = [1, 2, 3], Thumb = [1], SizeBytes = 3
        });
        db.SaveChanges();

        var resultado = Assert.Single(await svc.LimpiarAsync(["foro"]));

        Assert.True(resultado.Ok, resultado.Error);
        Assert.Equal(0, await db.ForumPosts.CountAsync());
        Assert.Equal(0, await db.ForumLikes.CountAsync());
        Assert.Equal(0, await db.ForumAttachments.CountAsync());
    }

    // ── Las protecciones ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Usuarios_NuncaBorraAdministradores_NiLaPropiaCuenta()
    {
        // La única equivocación de esta pantalla que no se arregla desde la pantalla: para arreglarla
        // habría que entrar, y ya no habría con qué.
        var (svc, db) = Admin(userId: 1);
        db.Users.AddRange(
            new User { Id = 1, Username = "yo",    PasswordHash = "x", Role = UserRole.Admin },
            new User { Id = 2, Username = "otro",  PasswordHash = "x", Role = UserRole.Admin },
            new User { Id = 3, Username = "ops",   PasswordHash = "x", Role = UserRole.Operaciones },
            new User { Id = 4, Username = "dev",   PasswordHash = "x", Role = UserRole.Desarrollador });
        db.SaveChanges();

        Assert.Equal(2, (await svc.ContarAsync())["usuarios"]);
        await svc.LimpiarAsync(["usuarios"]);

        var quedan = await db.Users.AsNoTracking().Select(u => u.Username).OrderBy(u => u).ToListAsync();
        Assert.Equal(["otro", "yo"], quedan);
    }

    [Fact]
    public async Task Usuarios_ProtegeLaCuentaDeQuienLimpia_AunqueNoSeaAdmin_PorSuId()
    {
        // Doble filtro a propósito (rol Y id): si mañana un rol nuevo pudiera entrar aquí, la cuenta
        // en uso seguiría protegida sin que nadie tenga que acordarse de añadirla.
        var (db, opts) = TestDb.NewConOpciones();
        var svc = Svc(db, opts, Ctx.As(UserRole.Admin, userId: 5));
        db.Users.AddRange(
            new User { Id = 5, Username = "sesion", PasswordHash = "x", Role = UserRole.Desarrollador },
            new User { Id = 6, Username = "otro",   PasswordHash = "x", Role = UserRole.Desarrollador });
        db.SaveChanges();

        await svc.LimpiarAsync(["usuarios"]);

        Assert.Equal("sesion", (await db.Users.AsNoTracking().SingleAsync()).Username);
    }

    // ── La constancia ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Limpiar_DejaConstanciaDeCadaArea()
    {
        var (svc, db) = Admin();
        db.Notes.Add(new Note { Title = "una" });
        db.Contacts.Add(new Contact { Name = "otro" });
        db.SaveChanges();

        await svc.LimpiarAsync(["notas", "contactos"]);

        var renglones = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Limpieza").ToListAsync();
        Assert.Equal(2, renglones.Count);
        Assert.All(renglones, r => Assert.Equal(AuditAction.Delete, r.Action));
        // Misma correlación: la pasada se lee como una sola operación, no como hechos sueltos.
        Assert.Single(renglones.Select(r => r.CorrelationId).Distinct());
    }

    [Fact]
    public void LaBitacora_SeBorraPRIMERO()
    {
        // Al final se llevaría los renglones que la propia limpieza acaba de escribir, y la operación
        // más destructiva de la aplicación sería la única sin rastro.
        var orden = DataCleanupService.Ordenar(["notas", DataCleanupService.ClaveBitacora, "contactos"]);

        Assert.Equal(DataCleanupService.ClaveBitacora, orden[0].Clave);
        Assert.Equal(3, orden.Count);
    }

    [Fact]
    public async Task BorrarLaBitacora_DejaEscritoQueSeBorro()
    {
        var (svc, db) = Admin();
        db.Notes.Add(new Note { Title = "una" });
        db.AuditLogs.Add(new AuditLog { UserName = "viejo", Action = AuditAction.Login });
        db.SaveChanges();

        await svc.LimpiarAsync([DataCleanupService.ClaveBitacora, "notas"]);

        var renglones = await db.AuditLogs.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(renglones, r => r.UserName == "viejo");   // la bitácora vieja se fue
        Assert.Equal(2, renglones.Count);                               // y quedó el acta de las dos áreas
        Assert.Contains(renglones, r => r.EntityId == DataCleanupService.ClaveBitacora);
    }

    [Fact]
    public async Task ElResultado_DiceCuantoHabiaYCuantoSeFue()
    {
        var (svc, db) = Admin();
        db.Minutes.Add(new Minute { Title = "Junta" }); db.SaveChanges();
        db.MinuteActionItems.Add(new MinuteActionItem { MinuteId = db.Minutes.Single().Id, Description = "Acuerdo" });
        db.SaveChanges();

        var r = Assert.Single(await svc.LimpiarAsync(["minutas"]));

        Assert.True(r.Ok, r.Error);
        Assert.Equal(1, r.Habia);   // una minuta…
        Assert.Equal(2, r.Filas);   // …pero dos filas, contando el acuerdo que arrastró
    }
}
