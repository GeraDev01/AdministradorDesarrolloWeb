namespace Administrador_Desarrollo_Web.Services;

/// <summary>Datos mínimos de un ticket para agregar por tag (independiente de EF, para poder probarlo).</summary>
public readonly record struct TagStatInput(string? Tags, string? WorkItemType, string? State);

/// <summary>Fila del dashboard por tag: cuánto hay de cada tipo para ese tag.</summary>
public record TagRow(string Tag, int Total, int Abiertos, int Bugs, int Tareas, int UserStories, int Otros);

/// <summary>
/// Agrega tickets de DevOps por TAG (los tags del work item: cliente «Bepensa», tipo «Bug», etc.).
/// Permite «entrar» a un tag para ver el resto dentro de ese subconjunto (p. ej. filtrar «Bepensa»
/// para ver cuántos Bugs/Tareas tiene, o filtrar «Bug» para ver qué cliente tiene más). Puro y
/// testeable: no toca la base ni la red.
/// </summary>
public static class DevOpsTagStats
{
    private static readonly char[] Separadores = ['|', ';', ','];

    /// <summary>Parte la cadena de tags en piezas limpias, sin vacíos ni duplicados (ignorando mayúsculas).</summary>
    public static List<string> SplitTags(string? tags) =>
        (tags ?? "")
        .Split(Separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static bool EsCerrado(string? state) => (state ?? "").Trim().ToLowerInvariant() switch
    {
        "done" or "closed" or "completed" or "resolved" or "removed" or "cancelled" or "canceled" => true,
        _ => false
    };

    private enum Tipo { Bug, Tarea, UserStory, Otro }

    private static Tipo Clasificar(string? workItemType, IReadOnlyCollection<string> tagsDelTicket)
    {
        var t = (workItemType ?? "").ToLowerInvariant();
        bool TagEs(string s) => tagsDelTicket.Any(x => x.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (t.Contains("bug") || TagEs("bug")) return Tipo.Bug;
        if (t.Contains("task") || t.Contains("tarea") || TagEs("tarea") || TagEs("task")) return Tipo.Tarea;
        if (t.Contains("user story") || t.Contains("story") || t.Contains("backlog") || t.Contains("historia")) return Tipo.UserStory;
        return Tipo.Otro;
    }

    /// <summary>
    /// Agrega por tag. <paramref name="dentroDelTag"/> limita a los tickets que tengan ese tag (y ese
    /// tag no aparece como fila). <paramref name="soloAbiertos"/> excluye los cerrados.
    /// </summary>
    public static List<TagRow> Aggregate(IEnumerable<TagStatInput> tickets, string? dentroDelTag = null, bool soloAbiertos = false)
    {
        var acc = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase); // [Total,Abiertos,Bug,Tarea,US,Otro]

        foreach (var tk in tickets)
        {
            if (soloAbiertos && EsCerrado(tk.State)) continue;

            var tags = SplitTags(tk.Tags);
            if (!string.IsNullOrWhiteSpace(dentroDelTag) &&
                !tags.Any(x => x.Equals(dentroDelTag, StringComparison.OrdinalIgnoreCase)))
                continue;   // no pertenece al subconjunto filtrado

            var tipo = Clasificar(tk.WorkItemType, tags);
            bool abierto = !EsCerrado(tk.State);

            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(dentroDelTag) && tag.Equals(dentroDelTag, StringComparison.OrdinalIgnoreCase))
                    continue;   // el tag del filtro no se lista a sí mismo

                if (!acc.TryGetValue(tag, out var c)) { c = new int[6]; acc[tag] = c; }
                c[0]++;                       // total
                if (abierto) c[1]++;          // abiertos
                c[2 + (int)tipo]++;           // bug/tarea/us/otro
            }
        }

        return acc
            .Select(kv => new TagRow(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]))
            .OrderByDescending(r => r.Total).ThenBy(r => r.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Todos los tags distintos presentes (para el combo de «entrar al tag»).</summary>
    public static List<string> TagsDistintos(IEnumerable<TagStatInput> tickets) =>
        tickets.SelectMany(t => SplitTags(t.Tags))
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
               .ToList();
}
