using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

public class PerformanceScoringTests
{
    /// <summary>
    /// El ranking no depende de quién pregunte: son consultas de lectura sin guarda. La identidad
    /// solo hace falta para registrar o corregir, y eso se prueba en sus propios archivos.
    /// </summary>
    private static PerformanceScoringService Svc(AppDbContext db) =>
        new(db, UsuarioDePrueba.Anonimo());

    private static ScoringCriterion Crit(AppDbContext db, int pts = 5)
    {
        var c = new ScoringCriterion { Name = "C", DefaultPoints = pts, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(c); db.SaveChanges();
        return c;
    }

    [Fact]
    public async Task Ranking_individual_solo_cuenta_aprobados()
    {
        using var db = TestDb.New();
        var ana = new Developer { FullName = "Ana", IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(ana, beto); db.SaveChanges();
        var crit = Crit(db);

        void Add(int dev, int pts, PointApprovalStatus st) => db.PointEntries.Add(
            new PointEntry { DeveloperId = dev, CriterionId = crit.Id, Points = pts, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = st });
        Add(ana.Id, 10, PointApprovalStatus.Aprobado);
        Add(ana.Id, 5, PointApprovalStatus.Pendiente);   // NO cuenta
        Add(ana.Id, 3, PointApprovalStatus.Rechazado);   // NO cuenta
        Add(beto.Id, 4, PointApprovalStatus.Aprobado);
        db.SaveChanges();

        var ranking = await Svc(db).IndividualRankingAsync(2026, 7);

        Assert.Equal(10, ranking.Single(r => r.DeveloperId == ana.Id).Total);   // pendiente/rechazado excluidos
        Assert.Equal(1, ranking.Single(r => r.DeveloperId == ana.Id).Count);    // solo la aprobada
        Assert.Equal(ana.Id, ranking[0].DeveloperId);                            // Ana primera (10 > 4)
    }

    [Fact]
    public async Task Totales_por_dev_separan_estados()
    {
        using var db = TestDb.New();
        var ana = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(ana); db.SaveChanges();
        var crit = Crit(db);
        void Add(int pts, PointApprovalStatus st) => db.PointEntries.Add(
            new PointEntry { DeveloperId = ana.Id, CriterionId = crit.Id, Points = pts, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = st });
        Add(10, PointApprovalStatus.Aprobado); Add(6, PointApprovalStatus.Aprobado);
        Add(5, PointApprovalStatus.Pendiente);
        Add(8, PointApprovalStatus.Rechazado); Add(2, PointApprovalStatus.Rechazado);
        db.SaveChanges();

        var t = await Svc(db).DevMonthlyTotalsAsync(ana.Id, 2026, 7);
        Assert.Equal(16, t.Approved);
        Assert.Equal(5, t.Pending);
        Assert.Equal(2, t.RejectedCount);
    }

    [Fact]
    public async Task Team_ranking_suma_integrantes_aprobados_mas_propios()
    {
        using var db = TestDb.New();
        var team = new Team { Name = "Alfa", CreatedAt = DateTime.UtcNow };
        db.Teams.Add(team); db.SaveChanges();
        var m1 = new Developer { FullName = "M1", IsActive = true, TeamId = team.Id };
        var m2 = new Developer { FullName = "M2", IsActive = true, TeamId = team.Id };
        db.Developers.AddRange(m1, m2); db.SaveChanges();
        var crit = Crit(db);

        db.PointEntries.AddRange(
            new PointEntry { DeveloperId = m1.Id, CriterionId = crit.Id, Points = 10, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado },
            new PointEntry { DeveloperId = m2.Id, CriterionId = crit.Id, Points = 20, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado },
            new PointEntry { DeveloperId = m1.Id, CriterionId = crit.Id, Points = 99, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Pendiente }); // NO cuenta
        db.TeamPointEntries.Add(new TeamPointEntry { TeamId = team.Id, CriterionId = crit.Id, Points = 15, Year = 2026, Month = 7, Date = DateTime.UtcNow });
        db.SaveChanges();

        var row = (await Svc(db).TeamRankingAsync(2026, 7)).Single(r => r.TeamId == team.Id);
        Assert.Equal(30, row.MembersSum);   // 10 + 20 (pendiente excluida)
        Assert.Equal(15, row.TeamOwn);
        Assert.Equal(45, row.Total);
        Assert.Equal(2, row.MemberCount);
    }

    // ── Nivel Lead fuera del ranking individual ─────────────────────────────────
    //
    // La regla es una asimetría deliberada: quien tiene el NIVEL «Lead» (Seniority) no figura en
    // el ranking individual —evalúa y reparte parte de los puntos— pero sus puntos SÍ suman a su
    // equipo. Y OJO: ser LÍDER DE EQUIPO (TeamRole.Lider) NO excluye; esa confusión ya ocurrió una
    // vez y estas pruebas la dejan clavada.

    private static PointEntry Aprobada(int devId, int critId, int pts) => new()
    {
        DeveloperId = devId, CriterionId = critId, Points = pts,
        Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado
    };

    [Fact]
    public async Task NivelLead_no_figura_en_el_ranking_individual()
    {
        using var db = TestDb.New();
        var lead = new Developer { FullName = "Lía", IsActive = true, Seniority = "Lead" };
        var dev  = new Developer { FullName = "Ana", IsActive = true, Seniority = "Junior" };
        db.Developers.AddRange(lead, dev); db.SaveChanges();
        var crit = Crit(db);
        db.PointEntries.AddRange(Aprobada(lead.Id, crit.Id, 50), Aprobada(dev.Id, crit.Id, 5));
        db.SaveChanges();

        var ranking = await Svc(db).IndividualRankingAsync(2026, 7);

        Assert.DoesNotContain(ranking, r => r.DeveloperId == lead.Id);
        Assert.Equal(dev.Id, ranking[0].DeveloperId);   // el 1.º es el mejor que SÍ compite
    }

    [Fact]
    public async Task LiderDeEquipo_SI_figura_en_el_ranking()
    {
        // La corrección del criterio: coordinar un equipo no es tener el nivel Lead. Un Senior
        // que lidera el equipo Alfa sigue compitiendo — por rol Y por LeadDeveloperId.
        using var db = TestDb.New();
        var senior = new Developer { FullName = "Luis", IsActive = true, Seniority = "Senior", TeamRole = TeamRole.Lider };
        db.Developers.Add(senior); db.SaveChanges();
        db.Teams.Add(new Team { Name = "Alfa", CreatedAt = DateTime.UtcNow, LeadDeveloperId = senior.Id });
        db.SaveChanges();

        var ranking = await Svc(db).IndividualRankingAsync(2026, 7);

        Assert.Contains(ranking, r => r.DeveloperId == senior.Id);
    }

    [Theory]
    [InlineData("Junior")]
    [InlineData("Mid")]
    [InlineData("Senior")]
    [InlineData("Arquitecto")]
    [InlineData(null)]
    public async Task Los_demas_niveles_compiten(string? seniority)
    {
        using var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true, Seniority = seniority };
        db.Developers.Add(dev); db.SaveChanges();

        Assert.Contains(await Svc(db).IndividualRankingAsync(2026, 7),
            r => r.DeveloperId == dev.Id);
    }

    [Theory]
    [InlineData("Lead")]
    [InlineData("lead")]
    [InlineData(" Lead ")]
    public async Task NivelLead_se_reconoce_aunque_venga_capturado_a_mano(string seniority)
    {
        // Seniority hoy es un combo cerrado, pero las fichas viejas pudieron capturarse a mano.
        using var db = TestDb.New();
        var lead = new Developer { FullName = "Lía", IsActive = true, Seniority = seniority };
        db.Developers.Add(lead); db.SaveChanges();

        var svc = Svc(db);
        Assert.Contains(lead.Id, await svc.IdsConNivelLeadAsync());
        Assert.DoesNotContain(await svc.IndividualRankingAsync(2026, 7), r => r.DeveloperId == lead.Id);
    }

    [Fact]
    public async Task IncluirNivelLead_LosDevuelve_MarcadosYConSuTotalIntacto()
    {
        // La pantalla del administrador los necesita para «Ajustar puntos» y «Limpiar mes».
        using var db = TestDb.New();
        var lead = new Developer { FullName = "Lía", IsActive = true, Seniority = "Lead" };
        db.Developers.Add(lead); db.SaveChanges();
        var crit = Crit(db);
        db.PointEntries.Add(Aprobada(lead.Id, crit.Id, 50)); db.SaveChanges();

        var fila = (await Svc(db).IndividualRankingAsync(2026, 7, incluirNivelLead: true))
            .Single(r => r.DeveloperId == lead.Id);

        Assert.True(fila.EsNivelLead);
        Assert.Equal(50, fila.Total);
    }

    [Fact]
    public async Task Los_puntos_del_nivelLead_siguen_sumando_a_su_equipo()
    {
        using var db = TestDb.New();
        var team = new Team { Name = "Alfa", CreatedAt = DateTime.UtcNow };
        db.Teams.Add(team); db.SaveChanges();
        var lead = new Developer { FullName = "Lía", IsActive = true, TeamId = team.Id, Seniority = "Lead" };
        var dev  = new Developer { FullName = "Ana", IsActive = true, TeamId = team.Id };
        db.Developers.AddRange(lead, dev); db.SaveChanges();
        var crit = Crit(db);
        db.PointEntries.AddRange(Aprobada(lead.Id, crit.Id, 20), Aprobada(dev.Id, crit.Id, 10));
        db.SaveChanges();

        var svc = Svc(db);
        var row = (await svc.TeamRankingAsync(2026, 7)).Single(r => r.TeamId == team.Id);

        Assert.Equal(30, row.MembersSum);                                          // 20 del Lead + 10
        Assert.Equal(2, row.MemberCount);                                          // el Lead sigue contando
        Assert.Equal(30, await svc.TeamMembersApprovedSumAsync(team.Id, 2026, 7)); // y el detalle cuadra
    }

    [Fact]
    public async Task El_panel_personal_del_nivelLead_no_cambia()
    {
        using var db = TestDb.New();
        var lead = new Developer { FullName = "Lía", IsActive = true, Seniority = "Lead" };
        db.Developers.Add(lead); db.SaveChanges();
        var crit = Crit(db);
        db.PointEntries.Add(Aprobada(lead.Id, crit.Id, 20));
        db.PointEntries.Add(new PointEntry { DeveloperId = lead.Id, CriterionId = crit.Id, Points = 7, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Pendiente });
        db.SaveChanges();

        var t = await Svc(db).DevMonthlyTotalsAsync(lead.Id, 2026, 7);

        Assert.Equal(20, t.Approved);
        Assert.Equal(7, t.Pending);
    }

    [Fact]
    public async Task PositionOf_deUnNivelLead_DevuelveQueNoCompite()
    {
        using var db = TestDb.New();
        var lead = new Developer { FullName = "Lía", IsActive = true, Seniority = "Lead" };
        var dev  = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.AddRange(lead, dev); db.SaveChanges();
        var crit = Crit(db);
        db.PointEntries.AddRange(Aprobada(lead.Id, crit.Id, 50), Aprobada(dev.Id, crit.Id, 5));
        db.SaveChanges();

        var svc = Svc(db);

        Assert.Null((await svc.PositionOfAsync(lead.Id, 2026, 7)).position);
        // Y quien sí compite es 1.º aunque el Lead tenga más puntos: los Lead no ocupan lugar.
        Assert.Equal(1, (await svc.PositionOfAsync(dev.Id, 2026, 7)).position);
    }

    [Fact]
    public async Task Todos_nivelLead_RankingVacio_SinExcepcion()
    {
        using var db = TestDb.New();
        var l1 = new Developer { FullName = "L1", IsActive = true, Seniority = "Lead" };
        var l2 = new Developer { FullName = "L2", IsActive = true, Seniority = "Lead" };
        db.Developers.AddRange(l1, l2); db.SaveChanges();

        var svc = Svc(db);
        var ranking = await svc.IndividualRankingAsync(2026, 7);

        Assert.Empty(ranking);
        var (pos, total) = await svc.PositionOfAsync(l1.Id, 2026, 7);
        Assert.Null(pos);
        Assert.Equal(0, total);
    }
}
