using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

public enum SearchKind { Requerimiento, Ticket, Desarrollador, Sugerencia, Plantilla, Articulo }

/// <summary>Un resultado de la búsqueda global: qué es, cómo se muestra y a qué pantalla lleva.</summary>
/// <param name="NavKey">
/// A dónde lleva. Casi siempre es una clave fija («requirements»); los artículos de conocimiento son
/// la excepción y llevan además su identificador («conocimiento/48»), porque buscar un tema y
/// aterrizar en la lista completa de la documentación no es encontrarlo — es volver a empezar.
/// </param>
public record SearchHit(SearchKind Kind, string Texto, string Detalle, string NavKey);

/// <summary>
/// Búsqueda global (Ctrl+K) para el administrador: encuentra requerimientos, tickets de DevOps,
/// desarrolladores, sugerencias, plantillas y artículos de la base de conocimiento por texto o
/// número, y devuelve a qué pantalla saltar. Solo lectura.
///
/// <para><b>Por qué la documentación entra AQUÍ y no en un buscador propio.</b> «Buscar un tema
/// concreto entre la documentación» es exactamente lo que esta caja ya hace con todo lo demás. Un
/// segundo buscador, con su propio atajo y su propia caja, sería una herramienta más que aprender
/// para hacer lo mismo — y la que menos se usara acabaría siendo la que no encuentra nada. La base
/// de conocimiento tiene además su buscador CON FILTROS dentro de su pantalla (etiqueta, estado,
/// autoría); esta caja es la otra mitad: la que se abre sin salir de donde estabas.</para>
/// </summary>
public class SearchService(AppDbContext db)
{
    /// <summary>Máximo de resultados POR TIPO, para que ninguna categoría se coma el presupuesto y
    /// todas aparezcan (antes un tope global las escondía).</summary>
    public const int MaxPorTipo = 12;

    public async Task<List<SearchHit>> BuscarAsync(string? query, int maxPorTipo = MaxPorTipo,
        CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return [];
        bool esNumero = int.TryParse(query, out var n);

        var hits = new List<SearchHit>();

        // Requerimientos — el match EXACTO por Id va primero para que un tope no lo esconda.
        var reqQ = db.Requirements.AsNoTracking()
            .Where(r => r.Title.Contains(query) || (esNumero && r.Id == n) || (r.ExternalId != null && r.ExternalId.Contains(query)));
        reqQ = esNumero ? reqQ.OrderByDescending(r => r.Id == n).ThenByDescending(r => r.Id) : reqQ.OrderByDescending(r => r.Id);
        hits.AddRange((await reqQ.Take(maxPorTipo).Select(r => new { r.Id, r.Title }).ToListAsync(ct))
            .Select(r => new SearchHit(SearchKind.Requerimiento, $"#{r.Id}  {r.Title}", "Requerimiento", "requirements")));

        var tkQ = db.DevOpsTickets.AsNoTracking()
            .Where(t => t.Title.Contains(query) || (esNumero && t.ExternalId == n));
        tkQ = esNumero ? tkQ.OrderByDescending(t => t.ExternalId == n).ThenByDescending(t => t.ExternalId) : tkQ.OrderByDescending(t => t.ExternalId);
        hits.AddRange((await tkQ.Take(maxPorTipo).Select(t => new { t.ExternalId, t.Title, t.State }).ToListAsync(ct))
            .Select(t => new SearchHit(SearchKind.Ticket, $"#{t.ExternalId}  {t.Title}", $"Ticket DevOps · {t.State}", "devops-tickets")));

        hits.AddRange((await db.Developers.AsNoTracking()
            .Where(d => d.FullName.Contains(query) || (d.Email != null && d.Email.Contains(query)))
            .OrderBy(d => d.FullName).Take(maxPorTipo)
            .Select(d => new { d.FullName, d.Email }).ToListAsync(ct))
            .Select(d => new SearchHit(SearchKind.Desarrollador, d.FullName, $"Desarrollador · {d.Email}", "developers")));

        hits.AddRange((await db.Suggestions.AsNoTracking()
            .Where(s => s.Title.Contains(query))
            .OrderByDescending(s => s.Id).Take(maxPorTipo)
            .Select(s => new { s.Title }).ToListAsync(ct))
            .Select(s => new SearchHit(SearchKind.Sugerencia, s.Title, "Sugerencia", "suggestions")));

        // Plantillas: solo las activas. Las archivadas se sacaron de la vista a propósito y no
        // deben volver por la puerta de atrás del buscador.
        //
        // OJO si algún día se abre Ctrl+K a más roles: esta consulta lista título y etiquetas de
        // TODAS las plantillas sin mirar el rol. Hoy no fuga nada porque la búsqueda global es
        // solo del administrador, pero el desarrollador únicamente puede LEER los tipos de
        // TemplateService.TiposDelEquipo — habría que filtrar aquí igual que en Legibles().
        hits.AddRange((await db.Templates.AsNoTracking()
            .Where(t => !t.IsArchived && (t.Title.Contains(query) || (t.Tags != null && t.Tags.Contains(query))))
            .OrderByDescending(t => t.UsageCount).ThenBy(t => t.Title).Take(maxPorTipo)
            .Select(t => new { t.Title, t.Kind }).ToListAsync(ct))
            .Select(t => new SearchHit(SearchKind.Plantilla, t.Title,
                $"Plantilla · {TemplateService.EtiquetaTipo(t.Kind)}", "templates")));

        // ── La base de conocimiento: SOLO lo PUBLICADO ───────────────────────────
        //
        // Esa condición es la seguridad de esta consulta, y por eso va escrita como lo PRIMERO del
        // Where y no como un filtro que se añade después. Un borrador es privado de quien lo
        // escribe —ni el líder lo lee—, y lo devuelto es cosa del autor y del revisor; si el
        // buscador global los enseñara, la privacidad del borrador dependería de que nadie tecleara
        // la palabra correcta en la caja de arriba. Lo publicado, en cambio, lo puede leer cualquiera
        // con sesión, así que este trozo sigue siendo correcto el día que la búsqueda global se abra
        // a más roles —al revés que las plantillas de aquí arriba, que sí habría que filtrar—.
        //
        // Se busca también en el CUERPO, y ahí está la diferencia con las demás categorías: a un
        // requerimiento se llega por su título o su número, pero a un artículo se llega por una
        // palabra que alguien recuerda haber leído dentro. Buscar solo por el título convertiría la
        // documentación en algo que hay que saber cómo se llama para poder encontrarlo.
        //
        // Y la comparación va en minúsculas por las dos partes, igual que en el buscador de la
        // propia base de conocimiento: si no, que «índices» encuentre o no «Índices» dependería de
        // con qué intercalación se creó la columna, que es de esas cosas que nadie ve venir.
        var q = query.ToLower();
        hits.AddRange((await db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.Status == KnowledgeStatus.Publicado
                        && (a.Title.ToLower().Contains(q)
                            || a.Body.ToLower().Contains(q)
                            || (a.Tags != null && a.Tags.ToLower().Contains(q))))
            .OrderByDescending(a => a.PublishedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(maxPorTipo)
            .Select(a => new { a.Id, a.Title, a.Tags }).ToListAsync(ct))
            .Select(a => new SearchHit(SearchKind.Articulo, a.Title,
                // Las etiquetas van en el detalle porque son lo que distingue dos artículos que se
                // llaman parecido: «Despliegue» a secas no dice nada, «Despliegue · sql, respaldos»
                // sí.
                string.IsNullOrWhiteSpace(a.Tags) ? "Documentación" : $"Documentación · {a.Tags}",
                $"conocimiento/{a.Id}")));

        return hits;
    }

    public static string Icono(SearchKind k) => k switch
    {
        SearchKind.Requerimiento => "📋",
        SearchKind.Ticket        => "🔷",
        SearchKind.Desarrollador => "👤",
        SearchKind.Sugerencia    => "💡",
        SearchKind.Plantilla     => "📚",
        SearchKind.Articulo      => "📖",
        _                        => "•"
    };
}
