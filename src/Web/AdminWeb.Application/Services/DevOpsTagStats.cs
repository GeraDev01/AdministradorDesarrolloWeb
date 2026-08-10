namespace AdminWeb.Application.Services;

/// <summary>Datos mínimos de un ticket para agregar por etiqueta, independientes de EF para poder probarlo.</summary>
public readonly record struct TicketParaEtiquetas(string? Etiquetas, string? Tipo, string? Estado);

/// <summary>Una fila del tablero por etiqueta: cuánto hay de cada tipo bajo esa etiqueta.</summary>
public record FilaDeEtiqueta(
    string Etiqueta, int Total, int Abiertos, int Bugs, int Tareas, int UserStories, int Otros);

/// <summary>
/// Agrega tickets de DevOps por ETIQUETA (las del work item: cliente «Bepensa», tipo «Bug»…).
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte.</para>
///
/// Permite «entrar» a una etiqueta para ver el resto dentro de ese subconjunto: filtrar «Bepensa»
/// para saber cuántos bugs y tareas tiene, o filtrar «Bug» para ver qué cliente acumula más. Puro y
/// probable: no toca la base ni la red.
/// </summary>
public static class DevOpsTagStats
{
    private static readonly char[] Separadores = ['|', ';', ','];

    /// <summary>Parte la cadena de etiquetas en piezas limpias, sin vacíos ni repetidas.</summary>
    public static List<string> Partir(string? etiquetas) =>
        (etiquetas ?? "")
        .Split(Separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Si el estado cuenta como cerrado PARA ESTE TABLERO.
    ///
    /// Incluye «resolved», a diferencia de <see cref="DevOpsService.EsCerrado"/>, que lo trata como
    /// «por entregar». La diferencia viene del escritorio y es deliberada: aquí se cuenta trabajo
    /// pendiente de ATENDER por cliente, y algo resuelto ya no lo está; allá se decide si un
    /// requerimiento se puede dar por entregado, y resuelto todavía no lo es.
    /// </summary>
    public static bool EsCerrado(string? estado) => (estado ?? "").Trim().ToLowerInvariant() switch
    {
        "done" or "closed" or "completed" or "resolved" or "removed" or "cancelled" or "canceled" => true,
        _ => false
    };

    private enum Tipo { Bug, Tarea, UserStory, Otro }

    private static Tipo Clasificar(string? tipoDeWorkItem, IReadOnlyCollection<string> etiquetasDelTicket)
    {
        var t = (tipoDeWorkItem ?? "").ToLowerInvariant();
        bool EtiquetaEs(string s) => etiquetasDelTicket.Any(x => x.Equals(s, StringComparison.OrdinalIgnoreCase));

        // La etiqueta cuenta además del tipo porque en la organización se etiqueta «bug» sobre work
        // items de tipo Task cuando el proceso no ofrece el tipo Bug.
        if (t.Contains("bug") || EtiquetaEs("bug")) return Tipo.Bug;
        if (t.Contains("task") || t.Contains("tarea") || EtiquetaEs("tarea") || EtiquetaEs("task")) return Tipo.Tarea;
        if (t.Contains("user story") || t.Contains("story") || t.Contains("backlog") || t.Contains("historia"))
            return Tipo.UserStory;

        return Tipo.Otro;
    }

    /// <summary>
    /// Agrega por etiqueta.
    /// </summary>
    /// <param name="dentroDe">Limita a los tickets que tengan esa etiqueta, y esa etiqueta no aparece
    /// como fila: dentro de «Bepensa» todos los tickets son de Bepensa y contarlo no diría nada.</param>
    /// <param name="soloAbiertos">Excluye los cerrados.</param>
    public static List<FilaDeEtiqueta> Agregar(
        IEnumerable<TicketParaEtiquetas> tickets, string? dentroDe = null, bool soloAbiertos = false)
    {
        // [Total, Abiertos, Bug, Tarea, UserStory, Otro] por etiqueta.
        var acumulado = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticket in tickets)
        {
            if (soloAbiertos && EsCerrado(ticket.Estado)) continue;

            var etiquetas = Partir(ticket.Etiquetas);
            if (!string.IsNullOrWhiteSpace(dentroDe)
                && !etiquetas.Any(x => x.Equals(dentroDe, StringComparison.OrdinalIgnoreCase)))
                continue;   // no pertenece al subconjunto filtrado

            var tipo = Clasificar(ticket.Tipo, etiquetas);
            bool abierto = !EsCerrado(ticket.Estado);

            foreach (var etiqueta in etiquetas)
            {
                if (!string.IsNullOrWhiteSpace(dentroDe)
                    && etiqueta.Equals(dentroDe, StringComparison.OrdinalIgnoreCase))
                    continue;   // la etiqueta del filtro no se lista a sí misma

                if (!acumulado.TryGetValue(etiqueta, out var celdas))
                {
                    celdas = new int[6];
                    acumulado[etiqueta] = celdas;
                }

                celdas[0]++;                       // total
                if (abierto) celdas[1]++;          // abiertos
                celdas[2 + (int)tipo]++;           // bug / tarea / user story / otro
            }
        }

        return acumulado
            .Select(kv => new FilaDeEtiqueta(
                kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5]))
            .OrderByDescending(f => f.Total)
            .ThenBy(f => f.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Todas las etiquetas distintas presentes, para el desplegable de «entrar a».</summary>
    public static List<string> EtiquetasDistintas(IEnumerable<TicketParaEtiquetas> tickets) =>
        tickets.SelectMany(t => Partir(t.Etiquetas))
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>
    /// Los indicadores del subconjunto que se está mirando: cuántos tickets, y de ellos cuántos son
    /// bugs, tareas e historias.
    ///
    /// Se cuenta por TICKET y no por etiqueta, a diferencia de <see cref="Agregar"/>: un ticket con
    /// tres etiquetas suma una fila en cada una, pero es un solo bug.
    /// </summary>
    public static (int tickets, int bugs, int tareas, int userStories) Indicadores(
        IEnumerable<TicketParaEtiquetas> tickets, string? dentroDe = null, bool soloAbiertos = false)
    {
        int total = 0, bugs = 0, tareas = 0, userStories = 0;

        foreach (var ticket in tickets)
        {
            if (soloAbiertos && EsCerrado(ticket.Estado)) continue;

            var etiquetas = Partir(ticket.Etiquetas);
            if (!string.IsNullOrWhiteSpace(dentroDe)
                && !etiquetas.Any(x => x.Equals(dentroDe, StringComparison.OrdinalIgnoreCase)))
                continue;

            total++;
            switch (Clasificar(ticket.Tipo, etiquetas))
            {
                case Tipo.Bug: bugs++; break;
                case Tipo.Tarea: tareas++; break;
                case Tipo.UserStory: userStories++; break;
            }
        }

        return (total, bugs, tareas, userStories);
    }
}
