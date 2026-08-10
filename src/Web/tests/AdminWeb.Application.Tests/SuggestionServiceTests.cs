using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Sugerencias/propuestas: el desarrollador envía y ve las suyas; el administrador lista, responde y
/// cambia el estado. Se prueban validación, permisos, filtrado por autor y el aviso al responder.
/// </summary>
public class SuggestionServiceTests
{
    private static SuggestionService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new NotificationService(db));

    /// <summary>La sugerencia tiene FK a Developers: si el usuario tiene ficha ligada, debe existir.</summary>
    private static void SeedDev(AppDbContext db, int id)
    {
        db.Developers.Add(new Developer { Id = id, FullName = $"Dev {id}", IsActive = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task Enviar_GuardaLosDatos()
    {
        var db = TestDb.New();
        SeedDev(db, 7);
        var cu = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 7, userId: 3);

        var (ok, _, sug) = await Svc(db, cu).EnviarAsync(
            SuggestionCategory.Departamento, "Café gratis", "Propongo café para el equipo.", anonima: false);

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
    public async Task Enviar_ValidaTituloYCuerpo()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3));

        Assert.False((await svc.EnviarAsync(SuggestionCategory.Producto, "ab", "cuerpo suficiente", false)).ok);   // título corto
        Assert.False((await svc.EnviarAsync(SuggestionCategory.Producto, "Título válido", "x", false)).ok);        // cuerpo corto
        Assert.Empty(db.Suggestions);
    }

    [Fact]
    public async Task Enviar_Anonima_GuardaLaBandera()
    {
        var db = TestDb.New();
        SeedDev(db, 7);
        var (ok, _, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 7, userId: 3))
            .EnviarAsync(SuggestionCategory.Otro, "Idea", "Descripción de la idea.", anonima: true);
        Assert.True(ok);
        Assert.True(db.Suggestions.Single().Anonymous);
    }

    [Fact]
    public async Task Enviar_Anonima_NoDelataAlAutorEnLaBitacora()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var cu = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10);   // Username = "desarrollador"
        await Svc(db, cu).EnviarAsync(SuggestionCategory.Departamento, "Cambiar al líder", "cuerpo delicado", anonima: true);

        // Ninguna entrada estampa al autor, ni ata la entidad Suggestion a un usuario: no se puede cruzar.
        Assert.DoesNotContain(db.AuditLogs, a => a.UserName == cu.Username);
        Assert.DoesNotContain(db.AuditLogs, a => a.EntityType == "Suggestion");
        // Pero sí queda constancia (del sistema) de que llegó una sugerencia anónima.
        Assert.Contains(db.AuditLogs, a => a.UserName == "sistema" && a.Action == AuditAction.Create);
    }

    [Fact]
    public async Task Enviar_NoAnonima_SiRegistraAutorYEntidad()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var cu = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10);
        await Svc(db, cu).EnviarAsync(SuggestionCategory.Producto, "Idea abierta", "cuerpo de la idea", anonima: false);

        Assert.Contains(db.AuditLogs, a => a.UserName == cu.Username && a.EntityType == "Suggestion");
    }

    [Fact]
    public async Task Mias_SoloDevuelveLasDelUsuarioActual()
    {
        var db = TestDb.New();
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 1))
            .EnviarAsync(SuggestionCategory.Producto, "Mía", "cuerpo de la sugerencia", false);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2))
            .EnviarAsync(SuggestionCategory.Producto, "De otro", "cuerpo de la sugerencia", false);

        var mias = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 1)).MiasAsync();
        Assert.Single(mias);
        Assert.Equal("Mía", mias[0].Title);
    }

    [Fact]
    public async Task Todas_RequiereAdmin()
    {
        var db = TestDb.New();
        var dev = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.TodasAsync());
    }

    [Fact]
    public async Task Responder_RequiereAdmin()
    {
        var db = TestDb.New();
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3))
            .EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);
        var dev = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.ResponderAsync(sug!.Id, SuggestionStatus.Aceptada, "ok"));
    }

    [Fact]
    public async Task Responder_CambiaEstadoYRespuesta_YAvisaAlAutor()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = 5, FullName = "Ana", IsActive = true });
        db.Users.Add(new User { Id = 10, Username = "ana", Role = UserRole.Desarrollador, DeveloperId = 5, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Producto, "Filtro nuevo", "Agregar un filtro por cliente.", false);

        var (ok, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99))
            .ResponderAsync(sug!.Id, SuggestionStatus.Aceptada, "Buena idea, va para el próximo sprint.");

        Assert.True(ok);
        var g = db.Suggestions.Single(s => s.Id == sug.Id);
        Assert.Equal(SuggestionStatus.Aceptada, g.Status);
        Assert.Equal("Buena idea, va para el próximo sprint.", g.AdminResponse);
        Assert.Equal(99, g.ReviewedByUserId);
        Assert.NotNull(g.ReviewedAt);

        // El autor (usuario 10) recibió un aviso.
        Assert.Contains(db.Notifications, n => n.ForUserId == 10);
    }

    [Fact]
    public async Task Eliminar_AutorSoloMientrasEstaNueva()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var autorCu = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10);
        var (_, _, sug) = await Svc(db, autorCu).EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);

        // Mientras está Nueva, el autor puede eliminarla — pero primero probamos que tras revisión no.
        await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99)).ResponderAsync(sug!.Id, SuggestionStatus.EnRevision, null);
        var (ok1, _) = await Svc(db, autorCu).EliminarAsync(sug.Id);
        Assert.False(ok1);                       // ya no es «Nueva»
        Assert.Single(db.Suggestions);

        // El administrador sí puede eliminarla en cualquier estado.
        var (ok2, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99)).EliminarAsync(sug.Id);
        Assert.True(ok2);
        Assert.Empty(db.Suggestions);
    }

    [Fact]
    public async Task Votar_alternaYCuenta()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);

        var votante = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20));
        var (ok, votado, total) = await votante.VotarAsync(sug!.Id);
        Assert.True(ok); Assert.True(votado); Assert.Equal(1, total);

        var (_, votado2, total2) = await votante.VotarAsync(sug.Id);   // vuelve a votar → quita el voto
        Assert.False(votado2); Assert.Equal(0, total2);
    }

    [Fact]
    public async Task Votar_dosUsuarios_suman()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).VotarAsync(sug!.Id);
        var (_, _, total) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 21)).VotarAsync(sug.Id);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task Equipo_traeVotosYSiYoVote()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var autor = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10);
        var (_, _, sug) = await Svc(db, autor).EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).VotarAsync(sug!.Id);

        var fila20 = (await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).EquipoAsync())
            .Single(x => x.Sug.Id == sug.Id);
        Assert.Equal(1, fila20.Votos);
        Assert.True(fila20.YoVote);
        Assert.False((await Svc(db, autor).EquipoAsync()).Single(x => x.Sug.Id == sug.Id).YoVote);   // el autor no votó
    }

    // ── Visibilidad y votación ───────────────────────────────────────────────────

    [Fact]
    public async Task PorOmision_EsPublicaYVotable()
    {
        // Lo que ya existía seguía siendo público y votable; los valores por omisión lo respetan.
        var db = TestDb.New();
        SeedDev(db, 5);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la idea", false);

        var g = db.Suggestions.Single();
        Assert.Equal(SuggestionVisibility.Publica, g.Visibility);
        Assert.True(g.OpenToVoting);
        Assert.True(g.SePuedeVotar);
    }

    [Fact]
    public async Task SoloAdministrador_NoAparecEnElTableroDelEquipo()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var autor = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10);
        await Svc(db, autor).EnviarAsync(SuggestionCategory.Departamento, "Algo delicado", "cuerpo delicado",
            anonima: false, visibilidad: SuggestionVisibility.SoloAdministrador);
        await Svc(db, autor).EnviarAsync(SuggestionCategory.Producto, "Idea pública", "cuerpo público", false);

        // Ni un compañero…
        var deOtro = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).EquipoAsync();
        Assert.Single(deOtro);
        Assert.Equal("Idea pública", deOtro[0].Sug.Title);

        // …ni su propio autor: si la viera en el tablero del equipo no habría forma de saber que
        // nadie más la ve. Para eso está «Mis sugerencias».
        Assert.Single(await Svc(db, autor).EquipoAsync());
        Assert.Equal(2, (await Svc(db, autor).MiasAsync()).Count);
    }

    [Fact]
    public async Task SoloAdministrador_SiLaVeElAdministrador()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Departamento, "Para ti nada más", "cuerpo",
                anonima: false, visibilidad: SuggestionVisibility.SoloAdministrador);

        var admin = Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));
        Assert.Single(await admin.TodasAsync());
        Assert.Single(await admin.TodasAsync(visibilidad: SuggestionVisibility.SoloAdministrador));
        Assert.Empty(await admin.TodasAsync(visibilidad: SuggestionVisibility.Publica));
    }

    [Fact]
    public async Task SoloAdministrador_NoSePuedeVotar_AunqueSeLlameDirectoAlServicio()
    {
        // La comprobación vive en el servicio, no solo en el botón: esconder el botón no impide
        // que otra ruta —o una llamada directa al endpoint— vote lo que nadie debería votar.
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Otro, "Privada", "cuerpo",
                anonima: false, visibilidad: SuggestionVisibility.SoloAdministrador);

        var (ok, votado, total) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).VotarAsync(sug!.Id);

        Assert.False(ok);
        Assert.False(votado);
        Assert.Equal(0, total);
        Assert.Empty(db.SuggestionVotes);
    }

    [Fact]
    public async Task PublicaSinVotacion_SeVePeroNoSeVota()
    {
        var db = TestDb.New();
        SeedDev(db, 5);
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Producto, "No es un concurso", "cuerpo",
                anonima: false, visibilidad: SuggestionVisibility.Publica, abiertaAVotacion: false);

        // Se ve en el tablero…
        Assert.Single(await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).EquipoAsync());
        // …pero no se puede apoyar.
        var (ok, _, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 20)).VotarAsync(sug!.Id);
        Assert.False(ok);
        Assert.False(db.Suggestions.Single().SePuedeVotar);
    }

    [Fact]
    public async Task SoloAdministrador_ApagaLaVotacionAunqueSePidaEncendida()
    {
        // Dejar la bandera encendida daría a entender que hay una votación en marcha que el equipo
        // ni siquiera puede ver.
        var db = TestDb.New();
        SeedDev(db, 5);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Otro, "Privada", "cuerpo",
                anonima: false, visibilidad: SuggestionVisibility.SoloAdministrador, abiertaAVotacion: true);

        Assert.False(db.Suggestions.Single().OpenToVoting);
    }

    [Fact]
    public async Task AnonimaYSoloAdministrador_SeCombinan()
    {
        // Son cosas distintas: una esconde QUIÉN, la otra limita QUIÉNES la leen.
        var db = TestDb.New();
        SeedDev(db, 5);
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 5, userId: 10))
            .EnviarAsync(SuggestionCategory.Departamento, "Sobre el ambiente", "cuerpo",
                anonima: true, visibilidad: SuggestionVisibility.SoloAdministrador);

        var g = db.Suggestions.Single();
        Assert.True(g.Anonymous);
        Assert.Equal(SuggestionVisibility.SoloAdministrador, g.Visibility);
        // El anonimato sigue sin delatarse en la bitácora.
        Assert.DoesNotContain(db.AuditLogs, a => a.EntityType == "Suggestion");
    }

    [Fact]
    public void EtiquetaAlcance_DiceLasTresSituaciones()
    {
        var privada = new Suggestion { Visibility = SuggestionVisibility.SoloAdministrador, OpenToVoting = false };
        var votable = new Suggestion { Visibility = SuggestionVisibility.Publica, OpenToVoting = true };
        var sinVoto = new Suggestion { Visibility = SuggestionVisibility.Publica, OpenToVoting = false };

        Assert.Contains("Solo líder", SuggestionService.EtiquetaAlcance(privada));
        Assert.Contains("se vota", SuggestionService.EtiquetaAlcance(votable));
        Assert.Contains("sin votación", SuggestionService.EtiquetaAlcance(sinVoto));
    }

    [Fact]
    public async Task Eliminar_OtroDesarrollador_Rechaza()
    {
        var db = TestDb.New();
        var (_, _, sug) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 1))
            .EnviarAsync(SuggestionCategory.Producto, "Idea", "cuerpo de la sugerencia", false);

        var (ok, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2)).EliminarAsync(sug!.Id);
        Assert.False(ok);
        Assert.Single(db.Suggestions);
    }
}
