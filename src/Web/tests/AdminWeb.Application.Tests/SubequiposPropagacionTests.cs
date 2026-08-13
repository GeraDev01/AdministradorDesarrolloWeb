using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// HASTA DÓNDE LLEGA la jerarquía, y hasta dónde NO.
///
/// <para>El equipo nunca ha decidido permisos en esta aplicación —eso lo decide el rol de la cuenta—,
/// y colgar un equipo de otro no lo cambia: el líder de un subequipo sigue siendo un desarrollador y
/// no gana ninguna función. Lo que la jerarquía sí hace es propagarse por donde el equipo YA se usaba:
/// agrupar el organigrama, sumar por rama y marcar «es de mi equipo».</para>
///
/// <para>Cada comprobación va emparejada con la misma situación SIN jerarquía, porque lo que hay que
/// poder afirmar no es solo que la novedad funcione: es que para los equipos de hoy —todos raíz— no
/// haya cambiado absolutamente nada.</para>
/// </summary>
public class SubequiposPropagacionTests
{
    private const int Web = 1;      // el equipo padre
    private const int Front = 2;    // subequipo de Web
    private const int Datos = 3;    // equipo raíz aparte

    /// <summary>Web → Front, y Datos suelto. Es el árbol mínimo que distingue rama de hermano.</summary>
    private static AppDbContext ConRama()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = Web, Name = "Web" });
        db.Teams.Add(new Team { Id = Front, Name = "Front", EquipoPadreId = Web });
        db.Teams.Add(new Team { Id = Datos, Name = "Datos" });
        db.SaveChanges();
        return db;
    }

    private static ScoringCriterion Criterio(AppDbContext db)
    {
        var c = new ScoringCriterion { Name = "C", DefaultPoints = 5, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return c;
    }

    private static void Puntos(AppDbContext db, int developerId, int criterioId, int puntos) =>
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = developerId,
            CriterionId = criterioId,
            Points = puntos,
            Year = 2026,
            Month = 7,
            Date = DateTime.UtcNow,
            ApprovalStatus = PointApprovalStatus.Aprobado
        });

    // ── Puntuar: la rama suma aparte, y el total que compite no se toca ──────────

    [Fact]
    public async Task ElRanking_sumaLaRamaAparte_ySinCambiarElTotalQueCompite()
    {
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = Web });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = Front });
        db.SaveChanges();
        var criterio = Criterio(db);
        Puntos(db, 1, criterio.Id, 10);
        Puntos(db, 2, criterio.Id, 7);
        db.SaveChanges();

        var ranking = await new PerformanceScoringService(db, UsuarioDePrueba.Anonimo()).TeamRankingAsync(2026, 7);

        // WEB NO SALE: tiene subequipos, así que no compite. Es la regla del dueño, y con ella los
        // 10 puntos de su gente directa no cuentan para ningún equipo — la pantalla lo advierte.
        Assert.DoesNotContain(ranking, t => t.TeamId == Web);

        // Y el subequipo sí compite, con lo suyo.
        Assert.Equal(7, ranking.Single(t => t.TeamId == Front).Total);
    }

    [Fact]
    public async Task UnPadreConGenteDirecta_SALE_EN_LA_LISTA_DE_LOS_QUE_NO_COMPITEN_CON_SU_CUENTA()
    {
        // El caso que duele y que la pantalla tiene que poder explicar: hay puntos de alguien que no
        // suman para ningún equipo. Si esto devolviera cero personas, el aviso se quedaría corto y
        // nadie sabría que se está perdiendo algo.
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = Web });
        db.SaveChanges();

        var fuera = await new PerformanceScoringService(db, UsuarioDePrueba.Anonimo())
            .EquiposQueNoCompitenAsync();

        var web = Assert.Single(fuera, e => e.TeamId == Web);
        Assert.Equal(1, web.PersonasDirectas);
    }

    [Fact]
    public async Task LosEquipos_SIN_SUBEQUIPOS_SI_COMPITEN_AUNQUE_CUELGUEN_DE_OTRO()
    {
        // La regla es «tiene subequipos», no «es raíz»: un subequipo hoja compite como cualquiera, y
        // un equipo de en medio —con padre Y con hijos— tampoco juega. Confundir las dos cosas
        // dejaría fuera del ranking a media plantilla.
        using var db = ConRama();
        db.SaveChanges();

        var ranking = await new PerformanceScoringService(db, UsuarioDePrueba.Anonimo()).TeamRankingAsync(2026, 7);

        Assert.Contains(ranking, t => t.TeamId == Front);   // hoja, cuelga de Web
        Assert.Contains(ranking, t => t.TeamId == Datos);   // raíz sin hijos
        Assert.DoesNotContain(ranking, t => t.TeamId == Web);
    }

    [Fact]
    public async Task SinJerarquia_COMPITEN_TODOS_COMO_SIEMPRE()
    {
        // La foto de antes de los subequipos tiene que comportarse exactamente igual que antes: sin
        // padres no hay padres que excluir, así que no se cae nadie de la tabla.
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Uno" });
        db.Teams.Add(new Team { Id = 2, Name = "Dos" });
        db.SaveChanges();

        var puntuacion = new PerformanceScoringService(db, UsuarioDePrueba.Anonimo());

        Assert.Equal(2, (await puntuacion.TeamRankingAsync(2026, 7)).Count);
        Assert.Empty(await puntuacion.EquiposQueNoCompitenAsync());
    }

    // ── «Es de mi equipo» en el panel del desarrollador ──────────────────────────

    [Fact]
    public async Task EnMiPanel_laFilaDelEquipoPadre_tambienEsMia()
    {
        // Quien está en el subequipo se reconoce en la fila de su equipo y en la de su área. Lo que
        // NO se marca es el equipo de al lado, que es otro equipo.
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Beto", IsActive = true, TeamId = Front });
        db.SaveChanges();

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1);
        var puntuacion = new PerformanceScoringService(db, yo);
        var panel = await new DesempenoQueryService(db, puntuacion, yo).MiPanelAsync(2026, 7);

        Assert.True(panel.Equipos.Single(f => f.Nombre == "Front").EsMio);

        // «Web» es el padre y ya no compite, así que no hay fila suya que marcar. Antes la había y
        // salía como mía; con la regla del ranking desapareció del listado entero.
        Assert.DoesNotContain(panel.Equipos, f => f.Nombre == "Web");

        // Y «Datos» cuelga de otra raíz: sigue sin ser mío por mucha jerarquía que haya.
        Assert.False(panel.Equipos.Single(f => f.Nombre == "Datos").EsMio);
    }

    [Fact]
    public async Task SinJerarquia_enMiPanel_soloEsMiaLaFilaDeMiEquipo()
    {
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Uno" });
        db.Teams.Add(new Team { Id = 2, Name = "Dos" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1 });
        db.SaveChanges();

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1);
        var panel = await new DesempenoQueryService(db, new PerformanceScoringService(db, yo), yo)
            .MiPanelAsync(2026, 7);

        Assert.True(panel.Equipos.Single(f => f.Nombre == "Uno").EsMio);
        Assert.False(panel.Equipos.Single(f => f.Nombre == "Dos").EsMio);
    }

    // ── «Es de mi equipo» en el cruce de ausencias ───────────────────────────────

    [Fact]
    public async Task EnElCruceDeAusencias_elDeLaRama_saleComoDeMiEquipo()
    {
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = Web });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = Front });
        db.Developers.Add(new Developer { Id = 3, FullName = "Carla", IsActive = true, TeamId = Datos });
        db.SaveChanges();

        Vacacion(db, 2);
        Vacacion(db, 3);
        db.SaveChanges();

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1);
        var fuera = await new AusenciasDelEquipoService(db, yo)
            .QuienEstaraFueraAsync(new DateTime(2026, 8, 10), new DateTime(2026, 8, 14));

        // Beto está en el subequipo del mío: su semana fuera me toca. Carla es de otro árbol.
        Assert.True(fuera.Fuera.Single(c => c.Nombre == "Beto").MismoEquipo);
        Assert.False(fuera.Fuera.Single(c => c.Nombre == "Carla").MismoEquipo);
        Assert.Contains("de tu mismo equipo", fuera.Resumen);
    }

    [Fact]
    public async Task SinJerarquia_soloEsDeMiEquipoQuienEstaEnElMismo()
    {
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Uno" });
        db.Teams.Add(new Team { Id = 2, Name = "Dos" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1 });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 1 });
        db.Developers.Add(new Developer { Id = 3, FullName = "Carla", IsActive = true, TeamId = 2 });
        db.SaveChanges();

        Vacacion(db, 2);
        Vacacion(db, 3);
        db.SaveChanges();

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1);
        var fuera = await new AusenciasDelEquipoService(db, yo)
            .QuienEstaraFueraAsync(new DateTime(2026, 8, 10), new DateTime(2026, 8, 14));

        Assert.True(fuera.Fuera.Single(c => c.Nombre == "Beto").MismoEquipo);
        Assert.False(fuera.Fuera.Single(c => c.Nombre == "Carla").MismoEquipo);
    }

    private static void Vacacion(AppDbContext db, int developerId) =>
        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = developerId,
            StartDate = new DateTime(2026, 8, 10),
            EndDate = new DateTime(2026, 8, 12),
            Status = VacationStatus.Aprobada,
            CreatedAt = DateTime.UtcNow
        });

    // ── Lo que la jerarquía NO reparte: funciones ────────────────────────────────

    private static PersonasQueryService Personas(AppDbContext db, ICurrentUser usuario)
    {
        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, usuario, origen);
        return new PersonasQueryService(
            db, usuario, bitacora,
            new AuthService(db, usuario, bitacora),
            new PresenceService(db, usuario, origen),
            new AttendanceService(db, usuario, bitacora, origen),
            new DeveloperProfileService(db, usuario, bitacora),
            new AnnouncementService(db, usuario, bitacora));
    }

    /// <summary>
    /// LA REGLA DEL DUEÑO, comprobada: el líder de un equipo —del padre o del subequipo— <b>no gana
    /// ninguna función</b> por serlo. Sigue siendo un desarrollador.
    ///
    /// <para>Se prueba con el caso más favorable que hay: alguien que es líder del equipo de ARRIBA,
    /// o sea el que «manda» sobre los subequipos, y con su cuenta de desarrollador. Si en algún
    /// momento apareciera una autoridad derivada del árbol, esta prueba sería la primera en verlo, y
    /// se pondría roja diciendo exactamente eso. Quien mande aquí lo hace porque su CUENTA es de
    /// administrador, no porque lidere nada.</para>
    /// </summary>
    [Fact]
    public async Task ElLiderDelEquipoPadre_conCuentaDeDesarrollador_noPuedeTocarLosEquipos()
    {
        using var db = ConRama();
        db.Developers.Add(new Developer
        {
            Id = 1, FullName = "Ana", IsActive = true, TeamId = Web, TeamRole = TeamRole.Lider
        });
        db.SaveChanges();

        // Y además apuntada como líder en el propio equipo, que es el otro dato que lo dice.
        db.Teams.Single(t => t.Id == Web).LeadDeveloperId = 1;
        db.SaveChanges();

        var lider = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1);
        var svc = Personas(db, lider);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarEquipoAsync(new GuardarEquipoRequest(0, "Nuevo", null, null, null)));
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.EliminarEquipoAsync(Front));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([1], Front, null)));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.AsignarRolAsync(new AsignarRolRequest(1, TeamRole.Backend)));
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.OrganigramaAsync());
    }

    // ── Reportar: una fila por equipo, con lo SUYO ───────────────────────────────

    private static ReportesService Reportes(AppDbContext db)
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        var bitacora = new AuditService(db, admin, new OrigenDePrueba());
        return new ReportesService(
            db, admin, new SettingsService(db, admin, bitacora), new CorreoDeMentira(), bitacora);
    }

    private static Task<ReporteGeneradoDto?> ResumenPorEquipo(AppDbContext db) =>
        Reportes(db).GenerarAsync(
            "resumen-por-equipo", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), []);

    [Fact]
    public async Task ElReportePorEquipo_cuentaSoloLoDeCadaEquipo_yDiceDeQuienCuelga()
    {
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = Web });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = Front });
        db.Developers.Add(new Developer { Id = 3, FullName = "Carla", IsActive = true, TeamId = Front });
        db.SaveChanges();

        var reporte = await ResumenPorEquipo(db);

        // En orden de dibujo: las raíces alfabéticas —«Datos» antes que «Web»— y cada padre delante
        // de su rama, de modo que «Front» sale pegado debajo del suyo y no al final de la hoja.
        Assert.NotNull(reporte);
        string[] enOrdenDeDibujo = ["Datos", "Web", "Front"];
        Assert.Equal(enOrdenDeDibujo, reporte.Filas.Select(f => f[0]));

        var web = reporte.Filas.Single(f => f[0] == "Web");
        var front = reporte.Filas.Single(f => f[0] == "Front");

        // Web cuenta UNA integrante y no tres. Si sumara su rama, la hoja contaría dos veces a la
        // misma gente y su cifra grande de «Total» dejaría de ser la de la empresa.
        Assert.Equal("1", web[1]);
        Assert.Equal("2", front[1]);

        // Y cada fila dice de quién cuelga, que es lo que hace legible ese «1»: sin la columna, un
        // equipo padre con poca gente directa parece un error de cuentas.
        Assert.Equal("Cuelga de", reporte.Columnas[^1]);
        Assert.Equal("—", web[^1]);
        Assert.Equal("Web", front[^1]);
    }

    [Fact]
    public async Task ElReportePorEquipo_filtradoPorUnaPersona_noArrastraAlEquipoDeEncima()
    {
        // Pedir el reporte de quien está en un subequipo NO añade la fila de su equipo padre: es
        // otro equipo con otra gente, y colarlo enseñaría justo las cifras que el filtro dejaba
        // fuera.
        using var db = ConRama();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = Web });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = Front });
        db.SaveChanges();

        var reporte = await Reportes(db).GenerarAsync(
            "resumen-por-equipo", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), [2]);

        Assert.NotNull(reporte);
        string[] soloElSubequipo = ["Front"];
        Assert.Equal(soloElSubequipo, reporte.Filas.Select(f => f[0]));
    }

    [Fact]
    public async Task SinJerarquia_elReportePorEquipo_saleAlfabeticoYSinPadres()
    {
        // La foto de hoy: mismas cifras, mismo orden y una raya en la columna nueva.
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Zeta" });
        db.Teams.Add(new Team { Id = 2, Name = "Alfa" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 2 });
        db.SaveChanges();

        var reporte = await ResumenPorEquipo(db);

        Assert.NotNull(reporte);
        string[] alfabetico = ["Alfa", "Zeta"];
        Assert.Equal(alfabetico, reporte.Filas.Select(f => f[0]));
        Assert.All(reporte.Filas, f => Assert.Equal("—", f[^1]));

        // Las cuatro columnas de siempre conservan su sitio, y con ellas la gráfica y las cifras
        // grandes, que apuntan a las columnas por su ÍNDICE: la nueva va la última por eso.
        string[] lasDeSiempre = ["Equipo", "Integrantes", "Reqs activos", "Pts propios (período)"];
        Assert.Equal(lasDeSiempre, reporte.Columnas.Take(4));
    }
}
