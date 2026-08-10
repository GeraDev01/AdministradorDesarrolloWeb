using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>Búsqueda global: encuentra por texto y por número en las cuatro entidades.</summary>
public class SearchServiceTests
{
    [Fact]
    public async Task Buscar_encuentraEnLasCuatroEntidades()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { FullName = "Ana López", Email = "ana@x.com", IsActive = true });
        db.Requirements.Add(new Requirement { Id = 100, Title = "Login nuevo", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 555, Title = "Bug de sesión", State = "Active" });
        db.Suggestions.Add(new Suggestion { Title = "Café gratis", Body = "propuesta", CreatedByUserId = 1, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();

        var svc = new SearchService(db);
        Assert.Contains(await svc.BuscarAsync("Login"), h => h.Kind == SearchKind.Requerimiento && h.NavKey == "requirements");
        Assert.Contains(await svc.BuscarAsync("555"),   h => h.Kind == SearchKind.Ticket);
        Assert.Contains(await svc.BuscarAsync("Ana"),   h => h.Kind == SearchKind.Desarrollador);
        Assert.Contains(await svc.BuscarAsync("Café"),  h => h.Kind == SearchKind.Sugerencia);
    }

    [Fact]
    public async Task Buscar_noStarvaCategorias_yTopePorTipo()
    {
        var db = TestDb.New();
        for (int i = 1; i <= 15; i++)
            db.Requirements.Add(new Requirement { Title = $"reporte 20 #{i}", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 20, Title = "otro ticket", State = "Active" });
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("20");
        // 15 requerimientos coinciden por texto, pero el ticket #20 (match exacto) NO se pierde.
        Assert.Contains(hits, h => h.Kind == SearchKind.Requerimiento);
        Assert.Contains(hits, h => h.Kind == SearchKind.Ticket);
        Assert.True(hits.Count(h => h.Kind == SearchKind.Requerimiento) <= SearchService.MaxPorTipo);
    }

    [Fact]
    public async Task Buscar_menosDeDosCaracteres_noBusca()
    {
        var db = TestDb.New();
        db.Requirements.Add(new Requirement { Title = "algo", Source = RequirementSource.Manual, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Assert.Empty(await new SearchService(db).BuscarAsync("a"));
        Assert.Empty(await new SearchService(db).BuscarAsync(""));
        Assert.Empty(await new SearchService(db).BuscarAsync(null));
    }

    [Fact]
    public async Task Buscar_LasPlantillasArchivadas_NoVuelvenPorElBuscador()
    {
        // Se sacaron de la vista a propósito; el buscador no puede ser la puerta de atrás.
        var db = TestDb.New();
        db.Templates.Add(new Template { Title = "Bloqueos en SQL", Body = "SELECT 1", Kind = TemplateKind.ScriptSql });
        db.Templates.Add(new Template { Title = "Bloqueos viejos", Body = "SELECT 2", Kind = TemplateKind.ScriptSql, IsArchived = true });
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("Bloqueos");

        var unica = Assert.Single(hits.Where(h => h.Kind == SearchKind.Plantilla));
        Assert.Equal("Bloqueos en SQL", unica.Texto);
    }
}
