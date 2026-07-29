using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>Búsqueda global: encuentra por texto y por número en las cuatro entidades.</summary>
public class SearchServiceTests
{
    [Fact]
    public void Buscar_encuentraEnLasCuatroEntidades()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { FullName = "Ana López", Email = "ana@x.com", IsActive = true });
        db.Requirements.Add(new Requirement { Id = 100, Title = "Login nuevo", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 555, Title = "Bug de sesión", State = "Active" });
        db.Suggestions.Add(new Suggestion { Title = "Café gratis", Body = "propuesta", CreatedByUserId = 1, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();

        var svc = new SearchService(db);
        Assert.Contains(svc.Buscar("Login"), h => h.Kind == SearchKind.Requerimiento && h.NavKey == "requirements");
        Assert.Contains(svc.Buscar("555"),   h => h.Kind == SearchKind.Ticket);
        Assert.Contains(svc.Buscar("Ana"),   h => h.Kind == SearchKind.Desarrollador);
        Assert.Contains(svc.Buscar("Café"),  h => h.Kind == SearchKind.Sugerencia);
    }

    [Fact]
    public void Buscar_noStarvaCategorias_yTopePorTipo()
    {
        var db = TestDb.New();
        for (int i = 1; i <= 15; i++)
            db.Requirements.Add(new Requirement { Title = $"reporte 20 #{i}", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 20, Title = "otro ticket", State = "Active" });
        db.SaveChanges();

        var hits = new SearchService(db).Buscar("20");
        // 15 requerimientos coinciden por texto, pero el ticket #20 (match exacto) NO se pierde.
        Assert.Contains(hits, h => h.Kind == SearchKind.Requerimiento);
        Assert.Contains(hits, h => h.Kind == SearchKind.Ticket);
        Assert.True(hits.Count(h => h.Kind == SearchKind.Requerimiento) <= SearchService.MaxPorTipo);
    }

    [Fact]
    public void Buscar_menosDeDosCaracteres_noBusca()
    {
        var db = TestDb.New();
        db.Requirements.Add(new Requirement { Title = "algo", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Assert.Empty(new SearchService(db).Buscar("a"));
        Assert.Empty(new SearchService(db).Buscar(""));
        Assert.Empty(new SearchService(db).Buscar(null));
    }
}
