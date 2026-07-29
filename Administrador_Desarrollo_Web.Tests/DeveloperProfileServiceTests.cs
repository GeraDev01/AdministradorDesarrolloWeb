using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

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
        var user = Ctx.As(rol);
        return (new DeveloperProfileService(db, user, new AuditService(db, user)), db);
    }

    [Fact]
    public void Guardar_yObtener_esUpsert()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        Assert.True(svc.Guardar(1, "fuerte", "débil", "C#, EF", 50000m, "MXN", "lead", "notas").ok);

        var f = svc.Obtener(1)!;
        Assert.Equal("fuerte", f.Strengths);
        Assert.Equal("C#, EF", f.TechStack);
        Assert.Equal(50000m, f.Salary);

        // Guardar de nuevo actualiza la MISMA ficha (índice único), no duplica.
        svc.Guardar(1, "más fuerte", null, null, null, null, null, null);
        Assert.Single(db.DeveloperProfiles);
        Assert.Equal("más fuerte", svc.Obtener(1)!.Strengths);
        Assert.Null(svc.Obtener(1)!.Salary);
    }

    [Fact]
    public void NoAdmin_esRechazado()
    {
        var (svc, _) = Nuevo(UserRole.Desarrollador);
        Assert.Throws<AuthorizationException>(() => svc.Obtener(1));
        Assert.Throws<AuthorizationException>(() => svc.Guardar(1, "x", null, null, null, null, null, null));
    }

    [Fact]
    public void SalarioNegativo_seRechaza()
    {
        var (svc, _) = Nuevo(UserRole.Admin);
        Assert.False(svc.Guardar(1, null, null, null, -5m, null, null, null).ok);
    }

    [Fact]
    public void ElSalario_noLlegaALaBitacora()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        svc.Guardar(1, null, null, null, 123456m, "MXN", null, null);
        Assert.DoesNotContain(db.AuditLogs, a => (a.Details ?? "").Contains("123456"));
        Assert.Contains(db.AuditLogs, a => a.EntityType == "DeveloperProfile");   // pero sí queda constancia del cambio
    }
}
