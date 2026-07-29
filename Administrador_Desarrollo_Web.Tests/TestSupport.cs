using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>Crea una BD SQLite temporal ya migrada (mismo patrón que los smoke tests).</summary>
internal static class TestDb
{
    public static AppDbContext New()
    {
        var path = Path.Combine(Path.GetTempPath(), "advtest_" + Guid.NewGuid().ToString("N") + ".db");
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options;
        var db = new AppDbContext(opts);
        DatabaseMigrator.EnsureUpToDate(db);
        return db;
    }
}

/// <summary>Fabrica un contexto de sesión (CurrentUserContext) con un rol/desarrollador dados.</summary>
internal static class Ctx
{
    public static CurrentUserContext As(UserRole role, int? developerId = null, int userId = 1)
    {
        var cu = new CurrentUserContext();
        cu.SetUser(new User { Id = userId, Username = role.ToString().ToLowerInvariant(), Role = role, DeveloperId = developerId, IsActive = true });
        return cu;
    }

    public static CurrentUserContext Anonymous() => new();
}
