using System;
using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Agendar un despliegue a SERVIDORES directos (no un perfil): los servidores se congelan en un perfil
/// interno oculto propio de la cita, de modo que una selección directa posterior NO le cambia los destinos.
/// </summary>
public class ScheduleDirectServersTests
{
    private sealed record Entorno(AppDbContext Db, DeploymentService Deploy, DeploymentScheduleService Sched,
        int ReleaseId, int T1, int T2, int T3);

    private static DeploymentTarget Srv(string n) => new()
    { Nombre = n, Host = "ftps://x", Puerto = 21, Usuario = "u", Contrasena = "p", RutaRemota = "/site", IsActive = true };

    private static Entorno Nuevo(UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();
        var sys = new AppSystem { Name = "Portal", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.AppSystems.Add(sys); db.SaveChanges();
        var rel = new AppRelease { AppSystemId = sys.Id, Version = "1.0.0", CreatedAt = DateTime.UtcNow };
        db.AppReleases.Add(rel);
        var t1 = Srv("web-01"); var t2 = Srv("web-02"); var t3 = Srv("web-03");
        db.DeploymentTargets.AddRange(t1, t2, t3);
        db.SaveChanges();

        var user = Ctx.As(rol);
        var audit = new AuditService(db, user);
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetConnectionString()).Options;
        var blob = new BlobStorageService(new SettingsService(db, audit));
        var deploy = new DeploymentService(db, blob, audit, user, new RemoteBackupService(blob, new SettingsService(db, audit)), opts);
        var sched = new DeploymentScheduleService(db, user, audit, deploy, opts);
        return new Entorno(db, deploy, sched, rel.Id, t1.Id, t2.Id, t3.Id);
    }

    [Fact]
    public void ProgramarServidores_CongelaLosServidoresElegidos()
    {
        var e = Nuevo();

        var (ok, _, cita) = e.Sched.ProgramarServidores(e.ReleaseId, new[] { e.T1, e.T2 }, DateTime.Now.AddHours(2), 60, "nocturno");

        Assert.True(ok);
        Assert.Equal(ScheduledDeploymentStatus.Programado, cita!.Status);
        var perfil = e.Db.DeploymentProfiles.Include(p => p.ProfileTargets).AsNoTracking().Single(p => p.Id == cita.DeploymentProfileId);
        Assert.True(perfil.IsAdHoc);                                              // oculto de los selectores
        Assert.NotEqual(DeploymentService.PerfilSeleccionDirecta, perfil.Name);   // NO es el reutilizable
        Assert.Equal(new[] { e.T1, e.T2 }.OrderBy(x => x),
                     perfil.ProfileTargets.Select(pt => pt.TargetId).OrderBy(x => x));
    }

    [Fact]
    public void UnaSeleccionDirectaPosterior_NoAlteraElAgendadoCongelado()
    {
        var e = Nuevo();
        var (_, _, cita) = e.Sched.ProgramarServidores(e.ReleaseId, new[] { e.T1, e.T2 }, DateTime.Now.AddHours(2), 60, null);
        int frozenId = cita!.DeploymentProfileId;

        // Alguien hace un despliegue directo a OTROS servidores: reescribe el perfil REUTILIZABLE, no el congelado.
        e.Deploy.PrepararSeleccionDirecta(new[] { e.T3 });

        var congelado = e.Db.DeploymentProfiles.Include(p => p.ProfileTargets).AsNoTracking().Single(p => p.Id == frozenId);
        Assert.Equal(new[] { e.T1, e.T2 }.OrderBy(x => x),
                     congelado.ProfileTargets.Select(pt => pt.TargetId).OrderBy(x => x));   // intacto

        var reutilizable = e.Db.DeploymentProfiles.Include(p => p.ProfileTargets).AsNoTracking()
            .Single(p => p.Name == DeploymentService.PerfilSeleccionDirecta);
        Assert.NotEqual(frozenId, reutilizable.Id);                              // es OTRO perfil
        Assert.Equal(new[] { e.T3 }, reutilizable.ProfileTargets.Select(pt => pt.TargetId).ToArray());
    }

    [Fact]
    public void Operaciones_PuedeProgramarServidoresDirectos()
    {
        var e = Nuevo(UserRole.Operaciones);
        var (ok, _, _) = e.Sched.ProgramarServidores(e.ReleaseId, new[] { e.T1 }, DateTime.Now.AddHours(1), 60, null);
        Assert.True(ok);
    }

    [Fact]
    public void Desarrollador_NoPuedeProgramarServidores()
    {
        var e = Nuevo(UserRole.Desarrollador);
        Assert.Throws<AuthorizationException>(() =>
            e.Sched.ProgramarServidores(e.ReleaseId, new[] { e.T1 }, DateTime.Now.AddHours(1), 60, null));
        Assert.Empty(e.Db.ScheduledDeployments);
    }

    [Fact]
    public void SinServidores_NoPrograma()
    {
        var e = Nuevo();
        Assert.False(e.Sched.ProgramarServidores(e.ReleaseId, Array.Empty<int>(), DateTime.Now.AddHours(1), 60, null).ok);
        Assert.Empty(e.Db.ScheduledDeployments);
    }

    [Fact]
    public void EnElPasado_NoPrograma()
    {
        var e = Nuevo();
        Assert.False(e.Sched.ProgramarServidores(e.ReleaseId, new[] { e.T1 }, DateTime.Now.AddMinutes(-5), 60, null).ok);
        Assert.Empty(e.Db.ScheduledDeployments);
    }
}
