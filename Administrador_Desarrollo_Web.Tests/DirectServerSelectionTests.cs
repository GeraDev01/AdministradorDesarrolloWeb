using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Selección directa de servidores (estilo Blobup): se respalda en un único perfil interno marcado
/// IsAdHoc, que se reutiliza y reescribe con los servidores elegidos, y no ensucia la lista de perfiles.
/// </summary>
public class DirectServerSelectionTests
{
    private static DeploymentService Nuevo(out AppDbContext db, out CurrentUserContext user, UserRole rol = UserRole.Admin)
    {
        db = TestDb.New();
        user = Ctx.As(rol);
        var audit = new AuditService(db, user);
        var settings = new SettingsService(db, audit);
        var blob = new BlobStorageService(settings);
        // dbOptions solo lo usa DeployAsync (que no se ejerce aquí): estas pruebas llaman a PrepararSeleccionDirecta.
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        return new DeploymentService(db, blob, audit, user, new RemoteBackupService(blob, settings), opts);
    }

    private static int SeedTarget(AppDbContext db, string nombre, bool activo = true)
    {
        var t = new DeploymentTarget { Nombre = nombre, Host = "ftps://x", Puerto = 21, Usuario = "u", Contrasena = "p", RutaRemota = "/site", IsActive = activo };
        db.DeploymentTargets.Add(t); db.SaveChanges();
        return t.Id;
    }

    [Fact]
    public void PrepararSeleccionDirecta_CreaUnPerfilAdHocConLosServidoresElegidos()
    {
        var svc = Nuevo(out var db, out _);
        var a = SeedTarget(db, "A");
        var b = SeedTarget(db, "B");

        var pid = svc.PrepararSeleccionDirecta([a, b]);

        var perfil = db.DeploymentProfiles.Single();
        Assert.Equal(pid, perfil.Id);
        Assert.True(perfil.IsAdHoc);
        Assert.False(perfil.AllowedForOperaciones);   // da igual: IsAdHoc lo oculta de todos los selectores
        Assert.Equal([a, b], db.DeploymentProfileTargets.Where(pt => pt.ProfileId == pid).OrderBy(pt => pt.Order).Select(pt => pt.TargetId).ToList());
    }

    [Fact]
    public void PrepararSeleccionDirecta_ReutilizaElMismoPerfil_YReescribeLosServidores()
    {
        var svc = Nuevo(out var db, out _);
        var a = SeedTarget(db, "A");
        var b = SeedTarget(db, "B");
        var c = SeedTarget(db, "C");

        var p1 = svc.PrepararSeleccionDirecta([a, b]);
        var p2 = svc.PrepararSeleccionDirecta([c]);

        Assert.Equal(p1, p2);                                  // el mismo perfil ad-hoc
        Assert.Single(db.DeploymentProfiles);                  // no se acumulan
        Assert.Equal([c], db.DeploymentProfileTargets.Where(pt => pt.ProfileId == p2).Select(pt => pt.TargetId).ToList());
    }

    [Fact]
    public void PrepararSeleccionDirecta_IgnoraLosInactivos_YFallaSiNoQuedaNinguno()
    {
        var svc = Nuevo(out var db, out _);
        var activo = SeedTarget(db, "Activo");
        var inactivo = SeedTarget(db, "Inactivo", activo: false);

        var pid = svc.PrepararSeleccionDirecta([activo, inactivo]);
        Assert.Equal([activo], db.DeploymentProfileTargets.Where(pt => pt.ProfileId == pid).Select(pt => pt.TargetId).ToList());

        Assert.Throws<InvalidOperationException>(() => svc.PrepararSeleccionDirecta([inactivo]));
    }

    // ── Quién puede desplegar a la selección directa ────────────────────────────
    // Antes DeployToServersAsync exigía RequireAdmin; ahora Operaciones también elige servidores.

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Operaciones)]
    public async Task DeployToServers_AdminYOperaciones_PasanLaGuarda(UserRole rol)
    {
        var svc = Nuevo(out _, out _, rol);
        // Selección vacía: si la guarda de rol pasa, el fallo es de negocio (no hay servidores), no de permiso.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DeployToServersAsync(1, [], new Progress<string>()));
    }

    [Fact]
    public async Task DeployToServers_UnDesarrollador_EsRechazadoPorPermisos()
    {
        var svc = Nuevo(out var db, out _, UserRole.Desarrollador);
        var a = SeedTarget(db, "A");   // aunque haya servidores válidos, la guarda corta antes.
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            svc.DeployToServersAsync(1, [a], new Progress<string>()));
    }

    [Fact]
    public void ElPerfilAdHoc_NoAparece_EnLaListaDePerfiles()
    {
        var svc = Nuevo(out var db, out _);
        db.DeploymentProfiles.Add(new DeploymentProfile { Name = "Normal", CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        svc.PrepararSeleccionDirecta([SeedTarget(db, "A")]);

        // La consulta que usa la pantalla de Perfiles excluye los ad-hoc.
        var visibles = db.DeploymentProfiles.Where(p => !p.IsAdHoc).Select(p => p.Name).ToList();
        Assert.Equal(["Normal"], visibles);
    }

    // ── Autorización del perfil al desplegar (la guarda interna de DeployAsync) ──────
    // Bug detectado: los perfiles internos IsAdHoc tienen AllowedForOperaciones=false, y DeployAsync
    // revalidaba eso, así que Operaciones NO podía desplegar por selección directa (ni en vivo ni
    // programado). Los internos deben estar exentos: solo se llegan por rutas ya autorizadas.

    [Fact]
    public void Operaciones_PuedeDesplegarPerfilesInternosAdHoc()
    {
        var ops = Ctx.As(UserRole.Operaciones);
        // «Selección directa» y snapshots congelados: IsAdHoc con AllowedForOperaciones=false.
        Assert.True(DeploymentService.PuedeDesplegarPerfil(ops, new DeploymentProfile { IsAdHoc = true, AllowedForOperaciones = false }));
        // Un perfil NORMAL no habilitado sigue vedado para Operaciones...
        Assert.False(DeploymentService.PuedeDesplegarPerfil(ops, new DeploymentProfile { IsAdHoc = false, AllowedForOperaciones = false }));
        // ...y uno habilitado sí.
        Assert.True(DeploymentService.PuedeDesplegarPerfil(ops, new DeploymentProfile { IsAdHoc = false, AllowedForOperaciones = true }));
    }

    [Fact]
    public void Admin_PuedeDesplegarCualquierPerfil()
    {
        var admin = Ctx.As(UserRole.Admin);
        Assert.True(DeploymentService.PuedeDesplegarPerfil(admin, new DeploymentProfile { IsAdHoc = false, AllowedForOperaciones = false }));
    }
}
