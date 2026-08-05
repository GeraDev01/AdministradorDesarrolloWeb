using System.Collections.Generic;
using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Administrador_Desarrollo_Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Puesta al día de los secretos que ya estaban en la base con el cifrado viejo (DPAPI, atado a la
/// cuenta de Windows que los guardó).
///
/// La regla que estas pruebas vigilan de cerca es <b>nunca destruir lo que hay</b>: un valor que
/// este equipo no puede descifrar se reporta, no se borra ni se sobrescribe. Esa fila es la única
/// copia de esa contraseña, y la PC que sí puede leerla tiene que encontrarla ahí.
/// </summary>
public class SecretosCompartidosTests
{
    private sealed record Entorno(AppDbContext Db, SharedSecretMigrationService Migracion,
        SettingsService Settings);

    private static Entorno Nuevo()
    {
        var db = TestDb.New();
        var audit = new AuditService(db, Ctx.Anonymous());
        return new Entorno(db,
            new SharedSecretMigrationService(db, audit, NullLogger<SharedSecretMigrationService>.Instance),
            new SettingsService(db, audit));
    }

    private static DeploymentTarget Servidor(string nombre, string contrasenaCifrada) => new()
    {
        Nombre = nombre, Host = "ftps://web.empresa.com", Puerto = 21, Usuario = "deploy",
        Contrasena = contrasenaCifrada, RutaRemota = "/site/wwwroot", IsActive = true
    };

    // ── Migración ───────────────────────────────────────────────────────────────

    [Fact]
    public void LosValoresHeredados_PasanAlCifradoPortable()
    {
        var e = Nuevo();
        e.Db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Keys.AzureBlobConnectionString,
            Value = SecretProtector.Protect("DefaultEndpointsProtocol=https;AccountKey=abc"),
            IsSecret = true
        });
        e.Db.DeploymentTargets.Add(Servidor("web-01", SecretProtector.Protect("ftp-secreto")));
        e.Db.SaveChanges();

        var r = e.Migracion.Ejecutar();

        Assert.Equal(2, r.Migrados);
        Assert.False(r.HayPendientes);

        var blob = e.Db.AppSettings.Single().Value;
        var srv  = e.Db.DeploymentTargets.Single().Contrasena;
        Assert.True(SharedSecretProtector.EsPortable(blob));
        Assert.True(SharedSecretProtector.EsPortable(srv));
        Assert.Equal("DefaultEndpointsProtocol=https;AccountKey=abc", e.Settings.Get(SettingsService.Keys.AzureBlobConnectionString));
    }

    /// <summary>
    /// Lo que este equipo no puede descifrar (se guardó en OTRA PC) se deja intacto y se reporta. Si
    /// se sobrescribiera o se vaciara, se perdería la única copia de esa contraseña — la PC que sí
    /// puede leerla ya no encontraría nada que migrar.
    /// </summary>
    [Fact]
    public void LoQueNoSePuedeDescifrar_NiSeToca_PeroSeReporta()
    {
        var e = Nuevo();
        const string ajeno = "AQIDBAUGBwgJCgsMDQ4PEA==";   // Base64 válido, pero no es DPAPI de aquí
        e.Db.DeploymentTargets.Add(Servidor("web-otra-pc", ajeno));
        e.Db.SaveChanges();

        var r = e.Migracion.Ejecutar();

        Assert.Equal(0, r.Migrados);
        Assert.Equal(ajeno, e.Db.DeploymentTargets.Single().Contrasena);
        Assert.Contains(r.Ilegibles, m => m.Contains("web-otra-pc"));
    }

    [Fact]
    public void CorrerlaDeNuevo_NoVuelveATocarNada()
    {
        var e = Nuevo();
        e.Db.DeploymentTargets.Add(Servidor("web-01", SecretProtector.Protect("ftp-secreto")));
        e.Db.SaveChanges();

        Assert.Equal(1, e.Migracion.Ejecutar().Migrados);

        var segunda = e.Migracion.Ejecutar();
        Assert.Equal(0, segunda.Migrados);
        Assert.Equal(1, segunda.YaPortables);
    }

    /// <summary>Un servidor dado de baja también se migra: reactivarlo meses después no debería
    /// estrenar un problema de credenciales que ya estaba resuelto.</summary>
    [Fact]
    public void TambienMigraLosServidoresDadosDeBaja()
    {
        var e = Nuevo();
        var baja = Servidor("web-baja", SecretProtector.Protect("ftp-secreto"));
        baja.IsActive = false;
        e.Db.DeploymentTargets.Add(baja);
        e.Db.SaveChanges();

        Assert.Equal(1, e.Migracion.Ejecutar().Migrados);
        Assert.True(SharedSecretProtector.EsPortable(e.Db.DeploymentTargets.Single().Contrasena));
    }

    // ── Recorrido completo: capturar aquí, desplegar allá ────────────────────────

    /// <summary>
    /// El caso que motivó todo esto, de punta a punta: alguien da de alta un servidor en SU equipo y
    /// otra persona, en OTRO equipo, tiene que poder desplegar en él sin capturar nada. Lo único que
    /// viaja entre los dos es la fila de la base — aquí, el texto cifrado.
    /// </summary>
    [Fact]
    public void UnServidorCapturadoEnUnEquipo_LoUsaOtro()
    {
        var e = Nuevo();

        // Equipo A da de alta el servidor (lo que hace DeploymentTargetDetailForm al guardar).
        e.Db.DeploymentTargets.Add(Servidor("web-nuevo", SharedSecretProtector.Protect("ftp-de-produccion")));
        e.Db.SaveChanges();

        // Equipo B lee esa misma fila y saca la contraseña para conectarse. Nadie capturó nada allá,
        // y no hizo falta republicar el ejecutable.
        var desdeB = e.Db.DeploymentTargets.Single(t => t.Nombre == "web-nuevo");
        Assert.True(SharedSecretProtector.TryUnprotect(desdeB.Contrasena, out var clave));
        Assert.Equal("ftp-de-produccion", clave);
    }
}
