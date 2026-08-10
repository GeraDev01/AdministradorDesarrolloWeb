using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El cronómetro por item. Portado del escritorio con las mismas garantías —un solo cronómetro
/// activo por persona, el tramo colgante no se regala— más lo que la web obliga a añadir: aquí
/// nadie «cierra la aplicación», así que el tiempo se consolida por LATIDO.
/// </summary>
public class WorkSessionTests
{
    private static WorkSessionService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static (WorkSessionService work, Developer dev, Requirement r1, Requirement r2) Setup(
        AppDbContext db, out ICurrentUser cu)
    {
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();
        var r1 = new Requirement { Title = "R1", Status = RequirementStatus.Estimado, CreatedAt = DateTime.UtcNow };
        var r2 = new Requirement { Title = "R2", Status = RequirementStatus.EnDesarrollo, CreatedAt = DateTime.UtcNow };
        db.Requirements.AddRange(r1, r2); db.SaveChanges();
        cu = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id);
        return (Svc(db, cu), dev, r1, r2);
    }

    [Fact]
    public async Task StartOrResume_crea_activa_y_mueve_item_a_EnDesarrollo()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);

        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);

        Assert.Equal(WorkSessionStatus.Activa, s.Status);
        Assert.Equal(RequirementStatus.EnDesarrollo, db.Requirements.Find(r1.Id)!.Status);
    }

    [Fact]
    public async Task Solo_una_sesion_activa_por_dev()
    {
        using var db = TestDb.New();
        var (work, dev, r1, r2) = Setup(db, out _);

        var a = await work.StartOrResumeAsync(dev.Id, r1.Id);
        var b = await work.StartOrResumeAsync(dev.Id, r2.Id);   // debe pausar la de r1

        Assert.Equal(WorkSessionStatus.Pausada, db.WorkSessions.Find(a.Id)!.Status);
        Assert.Equal(WorkSessionStatus.Activa, db.WorkSessions.Find(b.Id)!.Status);
        Assert.Equal(1, db.WorkSessions.Count(w => w.DeveloperId == dev.Id && w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public async Task Pausar_consolida_el_tramo()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-30); await db.SaveChangesAsync();

        await work.PauseAsync(dev.Id, r1.Id);

        var f = db.WorkSessions.Find(s.Id)!;
        Assert.Equal(WorkSessionStatus.Pausada, f.Status);
        Assert.InRange(f.AccumulatedSeconds, 28, 33);
    }

    [Fact]
    public async Task ReconcileOrphans_descarta_el_tramo_colgante()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        int accBefore = db.WorkSessions.Find(s.Id)!.AccumulatedSeconds;
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-100); await db.SaveChangesAsync();

        int n = await work.ReconcileOrphansAsync();

        var f = db.WorkSessions.Find(s.Id)!;
        Assert.True(n >= 1);
        Assert.Equal(WorkSessionStatus.Pausada, f.Status);
        Assert.Equal(accBefore, f.AccumulatedSeconds);   // NO se suman los 100s colgantes
    }

    [Fact]
    public async Task AccumulatedSeconds_nunca_negativo_con_reloj_hacia_adelante()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(5); await db.SaveChangesAsync(); // reloj "adelantado"
        await work.StopAsync(dev.Id, r1.Id);
        Assert.True(db.WorkSessions.Find(s.Id)!.AccumulatedSeconds >= 0);
    }

    [Fact]
    public async Task Un_dev_no_puede_operar_el_cronometro_de_otro()
    {
        using var db = TestDb.New();
        var (_, dev, r1, _) = Setup(db, out var cu);   // cu = dev (Ana)
        var otro = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.Add(otro); db.SaveChanges();
        var work = Svc(db, cu);

        // Ana intenta operar el cronómetro de Beto → prohibido
        await Assert.ThrowsAsync<AuthorizationException>(() => work.StartOrResumeAsync(otro.Id, r1.Id));
    }

    [Fact]
    public async Task Un_admin_si_puede_operar_cualquier_cronometro()
    {
        using var db = TestDb.New();
        var (_, dev, r1, _) = Setup(db, out _);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Admin));

        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);   // admin sin DeveloperId, pero es admin
        Assert.Equal(WorkSessionStatus.Activa, s.Status);
    }

    [Fact]
    public async Task Pausar_registra_un_tramo_fechado()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-40); await db.SaveChangesAsync();

        await work.PauseAsync(dev.Id, r1.Id);

        var iv = db.WorkIntervals.Single();
        Assert.Equal(dev.Id, iv.DeveloperId);
        Assert.Equal(r1.Id, iv.RequirementId);
        Assert.InRange(iv.Seconds, 38, 43);
        Assert.Equal(DateTime.Now.Date, iv.LocalDate);   // se atribuye al día local del inicio del tramo
    }

    [Fact]
    public async Task Detener_y_reanudar_registra_dos_tramos()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-10); await db.SaveChangesAsync();
        await work.StopAsync(dev.Id, r1.Id);
        var s2 = await work.StartOrResumeAsync(dev.Id, r1.Id);   // sesión nueva
        db.WorkSessions.Find(s2.Id)!.LastResumedAt = DateTime.UtcNow.AddSeconds(-20); await db.SaveChangesAsync();
        await work.StopAsync(dev.Id, r1.Id);

        Assert.Equal(2, db.WorkIntervals.Count(w => w.RequirementId == r1.Id));
        Assert.InRange(db.WorkIntervals.Where(w => w.RequirementId == r1.Id).Sum(w => w.Seconds), 28, 33);
    }

    [Fact]
    public async Task ResumenPorDia_agrupa_por_dia_y_por_item()
    {
        using var db = TestDb.New();
        var (work, dev, r1, r2) = Setup(db, out _);
        var hoy = DateTime.Today; var ayer = hoy.AddDays(-1);
        db.WorkIntervals.AddRange(
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r1.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 600, LocalDate = hoy },
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r1.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 300, LocalDate = hoy },
            new WorkInterval { DeveloperId = dev.Id, RequirementId = r2.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 120, LocalDate = ayer });
        db.SaveChanges();

        var rep = await work.ResumenPorDiaAsync(dev.Id, ayer, hoy);

        var hoyR1 = rep.Single(x => x.Date == hoy && x.RequirementId == r1.Id);
        Assert.Equal(900, hoyR1.Seconds);          // 600 + 300 el mismo día, mismo item
        Assert.Equal("R1", hoyR1.Target);
        var ayerR2 = rep.Single(x => x.Date == ayer && x.RequirementId == r2.Id);
        Assert.Equal(120, ayerR2.Seconds);
    }

    // ── El latido: lo que la web obliga a añadir ─────────────────────────────────

    /// <summary>Envejece el latido de una sesión: simula una pestaña que dejó de dar señales.</summary>
    private static async Task SinLatirDesdeHace(AppDbContext db, int sesionId, TimeSpan cuanto)
    {
        var s = db.WorkSessions.Find(sesionId)!;
        s.LastHeartbeatUtc = DateTime.UtcNow - cuanto;
        s.LastResumedAt = s.LastHeartbeatUtc;   // el tramo abierto empezó con ese latido
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ElLatido_RefrescaLaMarcaDeVida()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        await SinLatirDesdeHace(db, s.Id, TimeSpan.FromMinutes(3));

        Assert.True(await work.RegistrarLatidoAsync(dev.Id, r1.Id));

        var f = db.WorkSessions.AsNoTracking().Single(w => w.Id == s.Id);
        Assert.True(DateTime.UtcNow - f.LastHeartbeatUtc!.Value < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ElLatido_SobreUnaSesionYaDetenida_NoLaResucita()
    {
        // Se detuvo desde otra pestaña, que en la web es lo normal. El navegador rezagado debe
        // recibir un «no» y dejar de latir, no reabrir el cronómetro de nadie.
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        await work.StartOrResumeAsync(dev.Id, r1.Id);
        await work.StopAsync(dev.Id, r1.Id);

        Assert.False(await work.RegistrarLatidoAsync(dev.Id, r1.Id));
        Assert.Equal(WorkSessionStatus.Detenida, db.WorkSessions.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task ElLatido_EsDeCadaQuien()
    {
        using var db = TestDb.New();
        var (_, dev, r1, _) = Setup(db, out _);
        var intruso = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77));

        await Assert.ThrowsAsync<AuthorizationException>(() => intruso.RegistrarLatidoAsync(dev.Id, r1.Id));
    }

    [Fact]
    public async Task SinLatido_LaSesionSeConsolidaHastaElUltimoLatido_NoHastaAhora()
    {
        // Lo importante de verdad, misma política que las jornadas caídas: cerrar «con la hora de
        // ahora» le regalaría a alguien todas las horas que su pestaña pasó cerrada.
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);

        // Media hora de trabajo con latido, y luego nueve horas de silencio.
        var f = db.WorkSessions.Find(s.Id)!;
        f.LastResumedAt     = DateTime.UtcNow.AddHours(-9).AddMinutes(-30);
        f.LastHeartbeatUtc  = DateTime.UtcNow.AddHours(-9);
        await db.SaveChangesAsync();

        Assert.Equal(1, await work.ConsolidarSesionesSinLatidoAsync());

        var g = db.WorkSessions.AsNoTracking().Single();
        Assert.Equal(WorkSessionStatus.Pausada, g.Status);
        // Media hora, no nueve horas y media.
        Assert.InRange(g.AccumulatedSeconds, 29 * 60, 31 * 60);
        Assert.Null(g.LastResumedAt);   // sin tramo abierto: no vuelve a contar sola
    }

    [Fact]
    public async Task SinLatido_ElTramoConsolidadoQuedaFechado()
    {
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        var f = db.WorkSessions.Find(s.Id)!;
        f.LastResumedAt    = DateTime.UtcNow.AddMinutes(-45);
        f.LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        await work.ConsolidarSesionesSinLatidoAsync();

        var iv = db.WorkIntervals.AsNoTracking().Single();
        Assert.Equal(r1.Id, iv.RequirementId);
        Assert.InRange(iv.Seconds, 14 * 60, 16 * 60);   // de la reanudación al último latido
    }

    [Fact]
    public async Task UnaPausaCortaDelNavegador_NoCortaElCronometro()
    {
        // Una pestaña en segundo plano que el navegador ralentiza, o un parpadeo de red, no puede
        // cortarle el tiempo a nadie: por eso la tolerancia es varias veces el intervalo.
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        await SinLatirDesdeHace(db, s.Id, WorkSessionService.ToleranciaSinLatido - TimeSpan.FromMinutes(1));

        Assert.Equal(0, await work.ConsolidarSesionesSinLatidoAsync());
        Assert.Equal(WorkSessionStatus.Activa, db.WorkSessions.AsNoTracking().Single().Status);
    }

    [Fact]
    public void LaToleranciaEsVariasVecesElIntervalo()
    {
        // Si la tolerancia fuera igual o menor que el intervalo, un solo latido perdido bastaría
        // para cortarle el cronómetro a quien sigue trabajando.
        Assert.True(WorkSessionService.ToleranciaSinLatido >= WorkSessionService.IntervaloLatido * 3);
    }

    [Fact]
    public async Task SinNingunLatido_ElTramoNoSeInventa()
    {
        // Sesión heredada del escritorio (sin latido ninguno): no hay dato que respalde ese tiempo,
        // así que se consolida a cero en vez de regalarle las horas de la noche.
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        var f = db.WorkSessions.Find(s.Id)!;
        f.LastResumedAt    = DateTime.UtcNow.AddHours(-12);
        f.LastHeartbeatUtc = null;
        await db.SaveChangesAsync();

        Assert.Equal(1, await work.ConsolidarSesionesSinLatidoAsync());

        var g = db.WorkSessions.AsNoTracking().Single();
        Assert.Equal(WorkSessionStatus.Pausada, g.Status);
        Assert.Equal(0, g.AccumulatedSeconds);
        Assert.Empty(db.WorkIntervals);
    }

    [Fact]
    public async Task ReanudarUnaSesion_LeDaLatidoNuevo()
    {
        // Sin esto, una sesión reanudada arrastraría el latido de antes de la pausa y el barrido la
        // consolidaría de inmediato, quitándole a la persona el tiempo que acaba de empezar.
        using var db = TestDb.New();
        var (work, dev, r1, _) = Setup(db, out _);
        var s = await work.StartOrResumeAsync(dev.Id, r1.Id);
        await work.PauseAsync(dev.Id, r1.Id);
        db.WorkSessions.Find(s.Id)!.LastHeartbeatUtc = DateTime.UtcNow.AddHours(-3);
        await db.SaveChangesAsync();

        await work.StartOrResumeAsync(dev.Id, r1.Id);

        Assert.Equal(0, await work.ConsolidarSesionesSinLatidoAsync());
        Assert.Equal(WorkSessionStatus.Activa, db.WorkSessions.AsNoTracking().Single().Status);
    }
}
