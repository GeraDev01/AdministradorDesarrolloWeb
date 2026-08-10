using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Ficha de perfil del desarrollador (admin): upsert 1:1, solo el administrador puede leer/guardar,
/// y el salario NO llega a la bitácora.
/// </summary>
public class DeveloperProfileServiceTests
{
    private static (DeveloperProfileService svc, AppDbContext db) Nuevo(UserRole rol)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = 1, FullName = "Dev", IsActive = true });
        db.SaveChanges();
        var user = UsuarioDePrueba.Como(rol);
        return (new DeveloperProfileService(db, user, new AuditService(db, user, new OrigenDePrueba())), db);
    }

    [Fact]
    public async Task Guardar_yObtener_esUpsert()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        Assert.True((await svc.GuardarAsync(1, "fuerte", "débil", "C#, EF", 50000m, "MXN", "lead", "notas")).ok);

        var f = (await svc.ObtenerAsync(1))!;
        Assert.Equal("fuerte", f.Strengths);
        Assert.Equal("C#, EF", f.TechStack);
        Assert.Equal(50000m, f.Salary);

        // Guardar de nuevo actualiza la MISMA ficha (índice único), no duplica.
        await svc.GuardarAsync(1, "más fuerte", null, null, null, null, null, null);
        Assert.Single(db.DeveloperProfiles);
        Assert.Equal("más fuerte", (await svc.ObtenerAsync(1))!.Strengths);
        Assert.Null((await svc.ObtenerAsync(1))!.Salary);
    }

    [Fact]
    public async Task NoAdmin_esRechazado()
    {
        var (svc, _) = Nuevo(UserRole.Desarrollador);
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ObtenerAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarAsync(1, "x", null, null, null, null, null, null));
    }

    [Fact]
    public async Task SalarioNegativo_seRechaza()
    {
        var (svc, _) = Nuevo(UserRole.Admin);
        Assert.False((await svc.GuardarAsync(1, null, null, null, -5m, null, null, null)).ok);
    }

    [Fact]
    public async Task ElSalario_noLlegaALaBitacora()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        await svc.GuardarAsync(1, null, null, null, 123456m, "MXN", null, null);
        Assert.DoesNotContain(db.AuditLogs, a => (a.Details ?? "").Contains("123456"));
        Assert.Contains(db.AuditLogs, a => a.EntityType == "DeveloperProfile");   // pero sí queda constancia del cambio
    }
}
