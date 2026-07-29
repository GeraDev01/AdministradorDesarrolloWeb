using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

public class PerformanceScoringTests
{
    private static ScoringCriterion Crit(Administrador_Desarrollo_Web.Data.AppDbContext db, int pts = 5)
    {
        var c = new ScoringCriterion { Name = "C", DefaultPoints = pts, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(c); db.SaveChanges();
        return c;
    }

    [Fact]
    public void Ranking_individual_solo_cuenta_aprobados()
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

        var ranking = new PerformanceScoringService(db).IndividualRanking(2026, 7);

        Assert.Equal(10, ranking.Single(r => r.DeveloperId == ana.Id).Total);   // pendiente/rechazado excluidos
        Assert.Equal(1, ranking.Single(r => r.DeveloperId == ana.Id).Count);    // solo la aprobada
        Assert.Equal(ana.Id, ranking[0].DeveloperId);                            // Ana primera (10 > 4)
    }

    [Fact]
    public void Totales_por_dev_separan_estados()
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

        var t = new PerformanceScoringService(db).DevMonthlyTotals(ana.Id, 2026, 7);
        Assert.Equal(16, t.Approved);
        Assert.Equal(5, t.Pending);
        Assert.Equal(2, t.RejectedCount);
    }

    [Fact]
    public void Team_ranking_suma_integrantes_aprobados_mas_propios()
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

        var row = new PerformanceScoringService(db).TeamRanking(2026, 7).Single(r => r.TeamId == team.Id);
        Assert.Equal(30, row.MembersSum);   // 10 + 20 (pendiente excluida)
        Assert.Equal(15, row.TeamOwn);
        Assert.Equal(45, row.Total);
        Assert.Equal(2, row.MemberCount);
    }
}
