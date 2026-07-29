using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

public class WorkSessionTests
{
    private static (WorkSessionService work, Developer dev, Requirement r1, Requirement r2) Setup(AppDbContext db, out CurrentUserContext cu)
    {
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();
        var r1 = new Requirement { Title = "R1", Status = RequirementStatus.Estimado, CreatedAt = DateTime.UtcNow };
        var r2 = new Requirement { Title = "R2", Status = RequirementStatus.EnDesarrollo, CreatedAt = DateTime.UtcNow };
        db.Requirements.AddRange(r1, r2); db.SaveChanges();
        cu = Ctx.As(UserRole.Desarrollador, developerId: dev.Id);
        var work = new WorkSessionService(db, new AuditService(db, cu), cu);
        return (work, dev, r1, r2);
    }

    [Fact]
    public void StartOrResume_crea_activa_y_mueve_item_a_EnDesarrollo()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);

        var s = work.StartOrResume(dev.Id, r1.Id);

        Assert.Equal(WorkSessionStatus.Activa, s.Status);
        Assert.Equal(RequirementStatus.EnDesarrollo, db.Requirements.Find(r1.Id)!.Status);
    }

    [Fact]
    public void Solo_una_sesion_activa_por_dev()
    {
        using var db = TestDb.New();
        var (work, dev, r1, r2) = Setup(db, out _);

        var a = work.StartOrResume(dev.Id, r1.Id);
        var b = work.StartOrResume(dev.Id, r2.Id);   // debe pausar la de r1

        Assert.Equal(WorkSessionStatus.Pausada, db.WorkSessions.Find(a.Id)!.Status);
        Assert.Equal(WorkSessionStatus.Activa, db.WorkSessions.Find(b.Id)!.Status);
        Assert.Equal(1, db.WorkSessions.Count(w => w.DeveloperId == dev.Id && w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public void Pausar_consolida_el_tramo()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = work.StartOrResume(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-30); db.SaveChanges();

        work.Pause(dev.Id, r1.Id);

        var f = db.WorkSessions.Find(s.Id)!;
        Assert.Equal(WorkSessionStatus.Pausada, f.Status);
        Assert.InRange(f.AccumulatedSeconds, 28, 33);
    }

    [Fact]
    public void ReconcileOrphans_descarta_el_tramo_colgante()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = work.StartOrResume(dev.Id, r1.Id);
        int accBefore = db.WorkSessions.Find(s.Id)!.AccumulatedSeconds;
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-100); db.SaveChanges();

        int n = work.ReconcileOrphans();

        var f = db.WorkSessions.Find(s.Id)!;
        Assert.True(n >= 1);
        Assert.Equal(WorkSessionStatus.Pausada, f.Status);
        Assert.Equal(accBefore, f.AccumulatedSeconds);   // NO se suman los 100s colgantes
    }

    [Fact]
    public void AccumulatedSeconds_nunca_negativo_con_reloj_hacia_adelante()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = work.StartOrResume(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(5); db.SaveChanges(); // reloj "adelantado"
        work.Stop(dev.Id, r1.Id);
        Assert.True(db.WorkSessions.Find(s.Id)!.AccumulatedSeconds >= 0);
    }

    [Fact]
    public void Un_dev_no_puede_operar_el_cronometro_de_otro()
    {
        using var db = TestDb.New();
        var (_, dev, r1, _) = Setup(db, out var cu);   // cu = dev (Ana)
        var otro = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.Add(otro); db.SaveChanges();
        var work = new WorkSessionService(db, new AuditService(db, cu), cu);

        // Ana intenta operar el cronómetro de Beto → prohibido
        Assert.Throws<AuthorizationException>(() => work.StartOrResume(otro.Id, r1.Id));
    }

    [Fact]
    public void Un_admin_si_puede_operar_cualquier_cronometro()
    {
        using var db = TestDb.New();
        var (_, dev, r1, _) = Setup(db, out _);
        var admin = Ctx.As(UserRole.Admin);
        var work = new WorkSessionService(db, new AuditService(db, admin), admin);

        var s = work.StartOrResume(dev.Id, r1.Id);   // admin sin DeveloperId, pero es admin
        Assert.Equal(WorkSessionStatus.Activa, s.Status);
    }

    [Fact]
    public void Pausar_registra_un_tramo_fechado()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = work.StartOrResume(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-40); db.SaveChanges();

        work.Pause(dev.Id, r1.Id);

        var iv = db.WorkIntervals.Single();
        Assert.Equal(dev.Id, iv.DeveloperId);
        Assert.Equal(r1.Id, iv.RequirementId);
        Assert.InRange(iv.Seconds, 38, 43);
        Assert.Equal(DateTime.Now.Date, iv.LocalDate);   // se atribuye al día local del inicio del tramo
    }

    [Fact]
    public void Detener_y_reanudar_registra_dos_tramos()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = work.StartOrResume(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-10); db.SaveChanges();
        work.Stop(dev.Id, r1.Id);
        var s2 = work.StartOrResume(dev.Id, r1.Id);   // sesión nueva
        db.WorkSessions.Find(s2.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-20); db.SaveChanges();
        work.Stop(dev.Id, r1.Id);

        Assert.Equal(2, db.WorkIntervals.Count(w => w.RequirementId == r1.Id));
        Assert.InRange(db.WorkIntervals.Where(w => w.RequirementId == r1.Id).Sum(w => w.Seconds), 28, 33);
    }

    [Fact]
    public void ResumenPorDia_agrupa_por_dia_y_por_item()
    {
        using var db = TestDb.New();
        var (work, dev, r1, r2) = Setup(db, out _);
        var hoy = DateTime.Today; var ayer = hoy.AddDays(-1);
        db.WorkIntervals.AddRange(
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r1.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 600, LocalDate = hoy },
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r1.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 300, LocalDate = hoy },
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r2.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 120, LocalDate = ayer });
        db.SaveChanges();

        var rep = work.ResumenPorDia(dev.Id, ayer, hoy);

        var hoyR1 = rep.Single(x => x.Date == hoy && x.RequirementId == r1.Id);
        Assert.Equal(900, hoyR1.Seconds);          // 600 + 300 el mismo día, mismo item
        Assert.Equal("R1", hoyR1.Target);
        var ayerR2 = rep.Single(x => x.Date == ayer && x.RequirementId == r2.Id);
        Assert.Equal(120, ayerR2.Seconds);
    }
}
