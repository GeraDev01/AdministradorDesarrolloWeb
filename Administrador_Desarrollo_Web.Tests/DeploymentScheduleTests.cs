using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Agenda de despliegues. No se ejecuta ningún FTP: se prueba la lógica de horas, tolerancia,
/// permisos y la toma exclusiva de una cita (que es lo que evita ejecuciones dobles cuando hay
/// varias aplicaciones abiertas).
/// </summary>
public class DeploymentScheduleTests
{
    private sealed record Entorno(AppDbContext Db, DeploymentScheduleService Sched, int ReleaseId, int PerfilOps, int PerfilAdmin);

    private static Entorno Nuevo(UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();

        var sys = new AppSystem { Name = "Portal", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.AppSystems.Add(sys);
        db.SaveChanges();

        var rel = new AppRelease { AppSystemId = sys.Id, Version = "1.0.0", CreatedAt = DateTime.UtcNow };
        db.AppReleases.Add(rel);

        var pOps = new DeploymentProfile { Name = "QA", AllowedForOperaciones = true, CreatedAt = DateTime.UtcNow };
        var pAdm = new DeploymentProfile { Name = "Producción", AllowedForOperaciones = false, CreatedAt = DateTime.UtcNow };
        db.DeploymentProfiles.AddRange(pOps, pAdm);
        db.SaveChanges();

        var user = Ctx.As(rol);
        var audit = new AuditService(db, user);
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetConnectionString()).Options;
        var blob = new BlobStorageService(new SettingsService(db, audit));
        var deploy = new DeploymentService(db, blob, audit, user,
            new RemoteBackupService(blob, new SettingsService(db, audit)), opts);

        return new Entorno(db, new DeploymentScheduleService(db, user, audit, deploy, opts), rel.Id, pOps.Id, pAdm.Id);
    }

    // ── Programar ───────────────────────────────────────────────────────────────

    [Fact]
    public void SePuedeProgramarUnDespliegueFuturo()
    {
        var e = Nuevo();

        var (ok, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(3), 60, "nocturno");

        Assert.True(ok);
        Assert.Equal(ScheduledDeploymentStatus.Programado, cita!.Status);
        Assert.Equal(60, cita.ToleranciaMinutos);
    }

    [Fact]
    public void NoSePuedeProgramarEnElPasado()
    {
        var e = Nuevo();
        var (ok, mensaje, _) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddMinutes(-5), 60, null);

        Assert.False(ok);
        Assert.Contains("futuro", mensaje);
        Assert.Empty(e.Db.ScheduledDeployments);
    }

    [Fact]
    public void OperacionesNoPuedeProgramarUnPerfilQueNoTieneHabilitado()
    {
        var e = Nuevo(UserRole.Operaciones);

        Assert.Throws<AuthorizationException>(() =>
            e.Sched.Programar(e.ReleaseId, e.PerfilAdmin, DateTime.Now.AddHours(1), 60, null));
        Assert.Empty(e.Db.ScheduledDeployments);
    }

    [Fact]
    public void OperacionesSiPuedeProgramarSuPerfilHabilitado()
    {
        var e = Nuevo(UserRole.Operaciones);

        var (ok, _, _) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 60, null);

        Assert.True(ok);
    }

    [Fact]
    public void UnDesarrolladorNoPuedeProgramarNada()
    {
        var e = Nuevo(UserRole.Desarrollador);
        Assert.Throws<AuthorizationException>(() =>
            e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 60, null));
    }

    [Fact]
    public void ElIntentoDenegadoQuedaEnBitacora()
    {
        var e = Nuevo(UserRole.Operaciones);
        try { e.Sched.Programar(e.ReleaseId, e.PerfilAdmin, DateTime.Now.AddHours(1), 60, null); }
        catch (AuthorizationException) { }

        Assert.Contains(e.Db.AuditLogs.ToList(), l => l.Outcome == AuditOutcome.Denegado);
    }

    // ── Tolerancia y pérdida ────────────────────────────────────────────────────

    [Fact]
    public void UnaCitaVencidaSeMarcaComoPerdidaYNoSeEjecuta()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 30, null);

        // Se simula que la hora pasó hace mucho: tolerancia de 30 min, ya van 3 horas.
        e.Db.ScheduledDeployments.Find(cita!.Id)!.ScheduledAtUtc = DateTime.UtcNow.AddHours(-3);
        e.Db.SaveChanges();

        var perdidas = e.Sched.MarcarPerdidas();

        Assert.Single(perdidas);
        Assert.Equal(ScheduledDeploymentStatus.Perdido, e.Db.ScheduledDeployments.Find(cita.Id)!.Status);
        Assert.Null(e.Sched.TomarSiguiente());   // ya no se puede tomar
    }

    [Fact]
    public void DentroDeLaToleranciaSiSeEjecuta()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 60, null);

        // Su hora fue hace 10 minutos, con tolerancia de 60: todavía cuenta.
        e.Db.ScheduledDeployments.Find(cita!.Id)!.ScheduledAtUtc = DateTime.UtcNow.AddMinutes(-10);
        e.Db.SaveChanges();

        e.Sched.MarcarPerdidas();
        var tomada = e.Sched.TomarSiguiente();

        Assert.NotNull(tomada);
        Assert.Equal(cita.Id, tomada!.Id);
    }

    [Fact]
    public void UnaCitaFuturaNoSeTomaAntesDeTiempo()
    {
        var e = Nuevo();
        e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(5), 60, null);

        Assert.Null(e.Sched.TomarSiguiente());
    }

    // ── Toma exclusiva ──────────────────────────────────────────────────────────

    [Fact]
    public void UnaCitaSoloSePuedeTomarUnaVez()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 60, null);
        e.Db.ScheduledDeployments.Find(cita!.Id)!.ScheduledAtUtc = DateTime.UtcNow.AddMinutes(-1);
        e.Db.SaveChanges();

        var primera = e.Sched.TomarSiguiente();
        var segunda = e.Sched.TomarSiguiente();   // simula otra aplicación abierta

        Assert.NotNull(primera);
        Assert.Null(segunda);
        Assert.Equal(ScheduledDeploymentStatus.EnEjecucion, e.Db.ScheduledDeployments.Find(cita.Id)!.Status);
        Assert.NotNull(e.Db.ScheduledDeployments.Find(cita.Id)!.ClaimedBy);
    }

    // ── Cancelación ─────────────────────────────────────────────────────────────

    [Fact]
    public void SePuedeCancelarUnaCitaPendiente()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(2), 60, null);

        var (ok, _) = e.Sched.Cancelar(cita!.Id);

        Assert.True(ok);
        Assert.Equal(ScheduledDeploymentStatus.Cancelado, e.Db.ScheduledDeployments.Find(cita.Id)!.Status);
        Assert.Null(e.Sched.TomarSiguiente());
    }

    [Fact]
    public void NoSePuedeCancelarUnaCitaYaTomada()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.Programar(e.ReleaseId, e.PerfilOps, DateTime.Now.AddHours(1), 60, null);
        e.Db.ScheduledDeployments.Find(cita!.Id)!.ScheduledAtUtc = DateTime.UtcNow.AddMinutes(-1);
        e.Db.SaveChanges();
        e.Sched.TomarSiguiente();

        var (ok, mensaje) = e.Sched.Cancelar(cita.Id);

        Assert.False(ok);
        Assert.Contains("EnEjecucion", mensaje);
    }
}
