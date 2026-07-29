using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Datos mínimos de un compromiso de SLA para medir cumplimiento (independiente de EF, testeable).</summary>
public readonly record struct SlaComplianceInput(
    DateTime DueAtUtc, SlaStatus Status, string Desarrollador, string? DevOpsPriority, string? Tags);

/// <summary>Cómo terminó (o va) un compromiso, para efectos de cumplimiento.</summary>
public enum SlaOutcome { Cumplido, Vencido, EnCurso, Cancelado }

/// <summary>
/// Fila del reporte de cumplimiento para un grupo (un desarrollador, una prioridad, un cliente/tag,
/// un mes…). El «% de cumplimiento» se calcula solo sobre los ya resueltos (cumplidos + vencidos);
/// los que siguen en curso y los cancelados no cuentan en el porcentaje.
/// </summary>
public record SlaComplianceRow(string Grupo, int Cumplidos, int Vencidos, int EnCurso, int Cancelados)
{
    public int Total => Cumplidos + Vencidos + EnCurso + Cancelados;
    public int Resueltos => Cumplidos + Vencidos;
    public double PorcentajeCumplimiento => Resueltos == 0 ? 0 : Math.Round(100.0 * Cumplidos / Resueltos, 1);
}

/// <summary>
/// Agrega los compromisos de SLA para medir cumplimiento (a tiempo vs vencidos) global y por
/// desarrollador, prioridad de DevOps, cliente (tag) o mes. Lógica pura: no toca base ni red.
/// </summary>
public static class SlaComplianceStats
{
    /// <summary>
    /// Clasifica un compromiso. Un ACTIVO cuya fecha límite ya pasó cuenta como vencido (un gerente
    /// mide el plazo, no la etiqueta): así el incumplimiento no se «esconde» por no haberse cerrado.
    /// </summary>
    public static SlaOutcome Clasificar(SlaComplianceInput s, DateTime nowUtc) => s.Status switch
    {
        SlaStatus.Cancelado => SlaOutcome.Cancelado,
        SlaStatus.Cumplido  => SlaOutcome.Cumplido,
        SlaStatus.Vencido   => SlaOutcome.Vencido,
        _                   => nowUtc > s.DueAtUtc ? SlaOutcome.Vencido : SlaOutcome.EnCurso   // Activo
    };

    /// <summary>Resumen de un conjunto en una sola fila.</summary>
    public static SlaComplianceRow Resumen(IEnumerable<SlaComplianceInput> items, DateTime nowUtc, string grupo = "Total")
    {
        int cumpl = 0, venc = 0, curso = 0, canc = 0;
        foreach (var s in items)
            switch (Clasificar(s, nowUtc))
            {
                case SlaOutcome.Cumplido: cumpl++; break;
                case SlaOutcome.Vencido:  venc++;  break;
                case SlaOutcome.EnCurso:  curso++; break;
                default:                  canc++;  break;
            }
        return new SlaComplianceRow(grupo, cumpl, venc, curso, canc);
    }

    /// <summary>
    /// Agrupa por una o varias claves por elemento (para tags, un compromiso puede caer en varios
    /// grupos). Ordena por total descendente. Se omiten claves vacías.
    /// </summary>
    public static List<SlaComplianceRow> Agrupar(
        IEnumerable<SlaComplianceInput> items, DateTime nowUtc, Func<SlaComplianceInput, IEnumerable<string>> claves)
    {
        var buckets = new Dictionary<string, List<SlaComplianceInput>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in items)
            foreach (var k in claves(s))
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                if (!buckets.TryGetValue(k, out var lista)) { lista = []; buckets[k] = lista; }
                lista.Add(s);
            }

        return buckets
            .Select(kv => Resumen(kv.Value, nowUtc, kv.Key))
            .OrderByDescending(r => r.Total).ThenBy(r => r.Grupo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<SlaComplianceRow> PorDesarrollador(IEnumerable<SlaComplianceInput> items, DateTime nowUtc) =>
        Agrupar(items, nowUtc, s => [string.IsNullOrWhiteSpace(s.Desarrollador) ? "(sin asignar)" : s.Desarrollador]);

    public static List<SlaComplianceRow> PorPrioridad(IEnumerable<SlaComplianceInput> items, DateTime nowUtc) =>
        Agrupar(items, nowUtc, s => [NombrePrioridad(s.DevOpsPriority)]);

    public static List<SlaComplianceRow> PorTag(IEnumerable<SlaComplianceInput> items, DateTime nowUtc) =>
        Agrupar(items, nowUtc, s =>
        {
            // Los que no traen tag no pueden desaparecer del desglose: caen en «(sin tag)», igual que
            // «(sin asignar)» / «(sin prioridad)» en las otras vistas, para que todo cuadre con el total.
            var tags = DevOpsTagStats.SplitTags(s.Tags);
            return tags.Count > 0 ? tags : new List<string> { "(sin tag)" };
        });

    /// <summary>Por mes de la fecha límite (hora local), del más reciente al más antiguo.</summary>
    public static List<SlaComplianceRow> PorMes(IEnumerable<SlaComplianceInput> items, DateTime nowUtc) =>
        Agrupar(items, nowUtc, s => [s.DueAtUtc.ToLocalTime().ToString("yyyy-MM")])
            .OrderByDescending(r => r.Grupo, StringComparer.Ordinal)
            .ToList();

    private static string NombrePrioridad(string? devOpsPriority) =>
        int.TryParse((devOpsPriority ?? "").Trim(), out var p) && p is >= 1 and <= 4
            ? $"P{p} — {SlaPolicyStore.NombrePrioridad(p)}"
            : "(sin prioridad)";
}
