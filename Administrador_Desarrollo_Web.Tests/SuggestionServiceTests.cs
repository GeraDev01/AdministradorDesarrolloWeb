using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Sugerencias/propuestas: el desarrollador envía y ve las suyas; el administrador lista, responde y
/// cambia el estado. Se prueban validación, permisos, filtrado por autor y el aviso al responder.
/// </summary>
public class SuggestionServiceTests
{
    private static SuggestionService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu), new NotificationService(db));

    /// <summary>La sugerencia tiene FK a Developers: si el usuario tiene ficha ligada, debe existir.</summary>
    private static void SeedDev(AppDbContext db, int id)
    {
        db.Developers.Add(new Developer { Id = id, FullName = $"Dev {id}", IsActive = true });
        db.SaveChanges();
    }

    [Fact]
    public void Enviar_GuardaLosDatos()
    {
        var db = TestDb.New();
        SeedDev(db, 7);
        var cu = Ctx.As(UserRole.Desarrollador, developerId: 7, userId: 3);

        var (ok, _, sug) = Svc(db, cu).Enviar(SuggestionCategory.Departamento, "Café gratis", "Propongo café para el equipo.", anonima: false);

        Assert.True(ok);
        Assert.NotNull(sug);
        var g = db.Suggestions.Single();
        Assert.Equal("Café gratis", g.Title);
        Assert.Equal(SuggestionCategory.Departamento, g.Category);
        Assert.Equal(SuggestionStatus.Nueva, g.Status);
        Assert.Equal(3, g.CreatedByUserId);
        Assert.Equal(7, g.DeveloperId);
        Assert.False(g.Anonymous);
    }

    [Fact]
    public void Enviar_ValidaTituloYCuerpo()
    {
        var db = TestDb.New();
        var svc = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));

        Assert.False(svc.Enviar(SuggestionCategory.Producto, "ab", "cuerpo suficiente", false).ok);   // título corto
        Assert.False(svc.Enviar(SuggestionCategory.Producto, "Título válido", "x", false).ok);        // cuerpo corto
        Assert.Empty(db.Suggestions);
    }

    [Fact]
    public void Enviar_Anonima_GuardaLaBandera()
    {
        var db = TestDb.New();
        SeedDev(db, 7);
        var (ok, _, _) = Svc(db, Ctx.As(UserRole.Desarrollador, developerId: 7, userId: 3))
            .Enviar(SuggestionCategory.Otro, "Idea", "Descripción de la idea.", anonima: true);
        Assert.True(ok);
        Assert.True(db.Suggestions.Single().Anonymous);
    }

    [Fact]
    public void Enviar_Anonima_NoDelataAlAutorEnLaBitacora()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var cu = Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10);   // Username = "desarrollador"
        Svc(db, cu).Enviar(SuggestionCategory.Departamento, "Cambiar al jefe", "cuerpo delicado", anonima: true);

        // Ninguna entrada estampa al autor, ni ata la entidad Suggestion a un usuario: no se puede cruzar.
        Assert.DoesNotContain(db.AuditLogs, a => a.UserName == cu.Username);
        Assert.DoesNotContain(db.AuditLogs, a => a.EntityType == "Suggestion");
        // Pero sí queda constancia (del sistema) de que llegó una sugerencia anónima.
        Assert.Contains(db.AuditLogs, a => a.UserName == "sistema" && a.Action == AuditAction.Create);
    }

    [Fact]
    public void Enviar_NoAnonima_SiRegistraAutorYEntidad()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var cu = Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10);
        Svc(db, cu).Enviar(SuggestionCategory.Producto, "Idea abierta", "cuerpo de la idea", anonima: false);

        Assert.Contains(db.AuditLogs, a => a.UserName == cu.Username && a.EntityType == "Suggestion");
    }

    [Fact]
    public void Mias_SoloDevuelveLasDelUsuarioActual()
    {
        var db = TestDb.New();
        Svc(db, Ctx.As(UserRole.Desarrollador, userId: 1)).Enviar(SuggestionCategory.Producto, "Mía", "cuerpo de la sugerencia", false);
        Svc(db, Ctx.As(UserRole.Desarrollador, userId: 2)).Enviar(SuggestionCategory.Producto, "De otro", "cuerpo de la sugerencia", false);

        var mias = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 1)).Mias();
        Assert.Single(mias);
        Assert.Equal("Mía", mias[0].Title);
    }

    [Fact]
    public void Todas_RequiereAdmin()
    {
        var db = TestDb.New();
        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));
        Assert.Throws<AuthorizationException>(() => dev.Todas());
    }

    [Fact]
    public void Responder_RequiereAdmin()
    {
        var db = TestDb.New();
        var (_, _, sug) = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3))
            .Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);
        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));
        Assert.Throws<AuthorizationException>(() => dev.Responder(sug!.Id, SuggestionStatus.Aceptada, "ok"));
    }

    [Fact]
    public void Responder_CambiaEstadoYRespuesta_YAvisaAlAutor()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = 5, FullName = "Ana", IsActive = true });
        db.Users.Add(new User { Id = 10, Username = "ana", Role = UserRole.Desarrollador, DeveloperId = 5, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var (_, _, sug) = Svc(db, Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10))
            .Enviar(SuggestionCategory.Producto, "Filtro nuevo", "Agregar un filtro por cliente.", false);

        var (ok, _) = Svc(db, Ctx.As(UserRole.Admin, userId: 99))
            .Responder(sug!.Id, SuggestionStatus.Aceptada, "Buena idea, va para el próximo sprint.");

        Assert.True(ok);
        var g = db.Suggestions.Find(sug.Id)!;
        Assert.Equal(SuggestionStatus.Aceptada, g.Status);
        Assert.Equal("Buena idea, va para el próximo sprint.", g.AdminResponse);
        Assert.Equal(99, g.ReviewedByUserId);
        Assert.NotNull(g.ReviewedAt);

        // El autor (usuario 10) recibió un aviso.
        Assert.Contains(db.Notifications, n => n.ForUserId == 10);
    }

    [Fact]
    public void Eliminar_AutorSoloMientrasEstaNueva()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var autorCu = Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10);
        var (_, _, sug) = Svc(db, autorCu).Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);

        // Mientras está Nueva, el autor puede eliminarla — pero primero probamos que tras revisión no.
        Svc(db, Ctx.As(UserRole.Admin, userId: 99)).Responder(sug!.Id, SuggestionStatus.EnRevision, null);
        var (ok1, _) = Svc(db, autorCu).Eliminar(sug.Id);
        Assert.False(ok1);                       // ya no es «Nueva»
        Assert.Single(db.Suggestions);

        // El administrador sí puede eliminarla en cualquier estado.
        var (ok2, _) = Svc(db, Ctx.As(UserRole.Admin, userId: 99)).Eliminar(sug.Id);
        Assert.True(ok2);
        Assert.Empty(db.Suggestions);
    }

    [Fact]
    public void Votar_alternaYCuenta()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = Svc(db, Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10))
            .Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);

        var votante = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 20));
        var (ok, votado, total) = votante.Votar(sug!.Id);
        Assert.True(ok); Assert.True(votado); Assert.Equal(1, total);

        var (_, votado2, total2) = votante.Votar(sug.Id);   // vuelve a votar → quita el voto
        Assert.False(votado2); Assert.Equal(0, total2);
    }

    [Fact]
    public void Votar_dosUsuarios_suman()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = Svc(db, Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10))
            .Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);
        Svc(db, Ctx.As(UserRole.Desarrollador, userId: 20)).Votar(sug!.Id);
        var (_, _, total) = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 21)).Votar(sug.Id);
        Assert.Equal(2, total);
    }

    [Fact]
    public void Equipo_traeVotosYSiYoVote()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var autor = Ctx.As(UserRole.Desarrollador, developerId: 5, userId: 10);
        var (_, _, sug) = Svc(db, autor).Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);
        Svc(db, Ctx.As(UserRole.Desarrollador, userId: 20)).Votar(sug!.Id);

        var fila20 = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 20)).Equipo().Single(x => x.Sug.Id == sug.Id);
        Assert.Equal(1, fila20.Votos);
        Assert.True(fila20.YoVote);
        Assert.False(Svc(db, autor).Equipo().Single(x => x.Sug.Id == sug.Id).YoVote);   // el autor no votó
    }

    [Fact]
    public void Eliminar_OtroDesarrollador_Rechaza()
    {
        var db = TestDb.New();
        var (_, _, sug) = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 1))
            .Enviar(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);

        var (ok, _) = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 2)).Eliminar(sug!.Id);
        Assert.False(ok);
        Assert.Single(db.Suggestions);
    }
}
