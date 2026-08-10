using AdminWeb.Domain.Entities;

namespace AdminWeb.Application.Services;

/// <summary>
/// Búsqueda, filtrado y ordenado de lectura del foro. Vive fuera del servicio porque es la parte
/// que se puede probar sola y la que más fácil se rompe en silencio: un hilo mal aplanado convierte
/// una conversación en una lista de mensajes sueltos, y en un foro que sirve para auditar eso es
/// perder el sentido de lo que se dijo.
/// </summary>
public static class ForumFilter
{
    public static List<ForumPost> Aplicar(
        IEnumerable<ForumPost> entradas, ForumFiltro filtro, int userId, DateTime ahoraUtc)
    {
        var texto = string.IsNullOrWhiteSpace(filtro.Texto) ? null : filtro.Texto.Trim();
        DateTime? desde = filtro.UltimosDias is int d && d > 0 ? ahoraUtc.AddDays(-d) : null;

        return entradas.Where(p =>
            (texto == null
                || Contiene(p.Title, texto)
                // El cuerpo de una entrada retirada NO se busca: si se pudiera encontrar por su
                // texto, retirarla no serviría de nada.
                || (!p.Eliminado && Contiene(p.Body, texto))
                || Contiene(p.Tags, texto)
                || Contiene(p.AuthorName, texto))
            && (filtro.Tema == null || p.Topic == filtro.Tema)
            && (string.IsNullOrEmpty(filtro.Autor)
                || string.Equals(p.AuthorName, filtro.Autor, StringComparison.CurrentCultureIgnoreCase))
            && (!filtro.SoloMios || p.AuthorUserId == userId)
            && (desde == null || p.CreatedAtUtc >= desde))
            .ToList();
    }

    /// <summary>
    /// Aplana un hilo en ORDEN DE LECTURA: cada entrada seguida de sus respuestas, y los hermanos
    /// por fecha. Devuelve además el nivel con el que sangrar cada una.
    ///
    /// El nivel se recalcula aquí en vez de confiar en <c>Depth</c>: si una entrada intermedia
    /// desapareciera, sus respuestas quedarían sangradas contra un padre inexistente.
    /// </summary>
    public static List<(ForumPost Post, int Nivel)> Aplanar(IEnumerable<ForumPost> entradas)
    {
        var todas = entradas.ToList();
        if (todas.Count == 0) return [];

        var porPadre = todas
            .Where(p => p.ParentId != null)
            .GroupBy(p => p.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id).ToList());

        var conocidos = todas.Select(p => p.Id).ToHashSet();
        // Raíces: las que no tienen padre, y las huérfanas (su padre no vino en el lote) — así
        // ningún comentario se pierde por un dato incompleto.
        var raices = todas
            .Where(p => p.ParentId == null || !conocidos.Contains(p.ParentId.Value))
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToList();

        var salida = new List<(ForumPost, int)>(todas.Count);
        var visitados = new HashSet<int>();

        void Recorrer(ForumPost p, int nivel)
        {
            // Un ciclo (dato corrupto) colgaría la aplicación; se corta en seco.
            if (!visitados.Add(p.Id)) return;
            salida.Add((p, nivel));
            if (porPadre.TryGetValue(p.Id, out var hijos))
                foreach (var h in hijos) Recorrer(h, nivel + 1);
        }

        foreach (var r in raices) Recorrer(r, 0);

        // Red de seguridad: lo que no colgara de ninguna raíz se añade al final en vez de perderse.
        foreach (var p in todas.OrderBy(p => p.CreatedAtUtc))
            if (!visitados.Contains(p.Id)) { visitados.Add(p.Id); salida.Add((p, 0)); }

        return salida;
    }

    /// <summary>Autores presentes, para llenar el combo de filtro.</summary>
    public static List<string> Autores(IEnumerable<ForumPost> entradas) =>
        entradas
            .Select(p => p.AuthorName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>«hace 5 min», «ayer», «12/03/2026» — para la cabecera de cada tarjeta.</summary>
    public static string HaceCuanto(DateTime utc, DateTime ahoraUtc)
    {
        var t = ahoraUtc - utc;
        if (t < TimeSpan.Zero) return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        if (t < TimeSpan.FromMinutes(1)) return "ahora mismo";
        if (t < TimeSpan.FromHours(1))   return $"hace {(int)t.TotalMinutes} min";
        if (t < TimeSpan.FromDays(1))    return $"hace {(int)t.TotalHours} h";
        if (t < TimeSpan.FromDays(2))    return "ayer";
        if (t < TimeSpan.FromDays(7))    return $"hace {(int)t.TotalDays} días";
        return utc.ToLocalTime().ToString("dd/MM/yyyy");
    }

    private static bool Contiene(string? valor, string texto) =>
        valor != null && valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);
}
