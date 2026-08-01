using Administrador_Desarrollo_Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

public enum SearchKind { Requerimiento, Ticket, Desarrollador, Sugerencia, Plantilla }

/// <summary>Un resultado de la búsqueda global: qué es, cómo se muestra y a qué pantalla lleva.</summary>
public record SearchHit(SearchKind Kind, string Texto, string Detalle, string NavKey);

/// <summary>
/// Búsqueda global (Ctrl+K) para el administrador: encuentra requerimientos, tickets de DevOps,
/// desarrolladores, sugerencias y plantillas por texto o número, y devuelve a qué pantalla saltar.
/// Solo lectura.
/// </summary>
public class SearchService
{
    /// <summary>Máximo de resultados POR TIPO, para que ninguna categoría se coma el presupuesto y
    /// todas aparezcan (antes un tope global las escondía).</summary>
    public const int MaxPorTipo = 12;

    private readonly AppDbContext _db;
    public SearchService(AppDbContext db) => _db = db;

    public List<SearchHit> Buscar(string? query, int maxPorTipo = MaxPorTipo)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return [];
        bool esNumero = int.TryParse(query, out var n);

        var hits = new List<SearchHit>();

        // Requerimientos — el match EXACTO por Id va primero para que un tope no lo esconda.
        var reqQ = _db.Requirements.AsNoTracking()
            .Where(r => r.Title.Contains(query) || (esNumero && r.Id == n) || (r.ExternalId != null && r.ExternalId.Contains(query)));
        reqQ = esNumero ? reqQ.OrderByDescending(r => r.Id == n).ThenByDescending(r => r.Id) : reqQ.OrderByDescending(r => r.Id);
        hits.AddRange(reqQ.Take(maxPorTipo).Select(r => new { r.Id, r.Title }).ToList()
            .Select(r => new SearchHit(SearchKind.Requerimiento, $"#{r.Id}  {r.Title}", "Requerimiento", "requirements")));

        var tkQ = _db.DevOpsTickets.AsNoTracking()
            .Where(t => t.Title.Contains(query) || (esNumero && t.ExternalId == n));
        tkQ = esNumero ? tkQ.OrderByDescending(t => t.ExternalId == n).ThenByDescending(t => t.ExternalId) : tkQ.OrderByDescending(t => t.ExternalId);
        hits.AddRange(tkQ.Take(maxPorTipo).Select(t => new { t.ExternalId, t.Title, t.State }).ToList()
            .Select(t => new SearchHit(SearchKind.Ticket, $"#{t.ExternalId}  {t.Title}", $"Ticket DevOps · {t.State}", "devops-tickets")));

        hits.AddRange(_db.Developers.AsNoTracking()
            .Where(d => d.FullName.Contains(query) || (d.Email != null && d.Email.Contains(query)))
            .OrderBy(d => d.FullName).Take(maxPorTipo)
            .Select(d => new { d.FullName, d.Email }).ToList()
            .Select(d => new SearchHit(SearchKind.Desarrollador, d.FullName, $"Desarrollador · {d.Email}", "developers")));

        hits.AddRange(_db.Suggestions.AsNoTracking()
            .Where(s => s.Title.Contains(query))
            .OrderByDescending(s => s.Id).Take(maxPorTipo)
            .Select(s => new { s.Title }).ToList()
            .Select(s => new SearchHit(SearchKind.Sugerencia, s.Title, "Sugerencia", "suggestions")));

        // Plantillas: solo las activas. Las archivadas se sacaron de la vista a propósito y no
        // deben volver por la puerta de atrás del buscador.
        //
        // OJO si algún día se abre Ctrl+K a más roles: esta consulta lista título y etiquetas de
        // TODAS las plantillas sin mirar el rol. Hoy no fuga nada porque la búsqueda global es
        // solo del administrador, pero el desarrollador únicamente puede LEER los tipos de
        // TemplateService.TiposDelEquipo — habría que filtrar aquí igual que en Legibles().
        hits.AddRange(_db.Templates.AsNoTracking()
            .Where(t => !t.IsArchived && (t.Title.Contains(query) || (t.Tags != null && t.Tags.Contains(query))))
            .OrderByDescending(t => t.UsageCount).ThenBy(t => t.Title).Take(maxPorTipo)
            .Select(t => new { t.Title, t.Kind }).ToList()
            .Select(t => new SearchHit(SearchKind.Plantilla, t.Title,
                $"Plantilla · {TemplateService.EtiquetaTipo(t.Kind)}", "templates")));

        return hits;
    }

    public static string Icono(SearchKind k) => k switch
    {
        SearchKind.Requerimiento => "📋",
        SearchKind.Ticket        => "🔷",
        SearchKind.Desarrollador => "👤",
        SearchKind.Sugerencia    => "💡",
        SearchKind.Plantilla     => "📚",
        _                        => "•"
    };
}
