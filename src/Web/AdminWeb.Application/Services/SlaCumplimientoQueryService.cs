using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Sla;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Cómo terminó (o va) un compromiso, para efectos de cumplimiento.</summary>
public enum ResultadoSla { Cumplido, Vencido, EnCurso, Cancelado }

/// <summary>
/// Reporte de CUMPLIMIENTO de SLA, en solo lectura: de los compromisos con fecha límite dentro del
/// período, cuántos se cumplieron a tiempo y cuántos se vencieron, global y desglosado por
/// desarrollador, prioridad de DevOps, cliente (tag) o mes.
///
/// Dos reglas que vienen del escritorio y que conviene no perder de vista:
///
/// 1. El porcentaje se mide SOLO sobre lo ya resuelto (cumplidos + vencidos). Lo que sigue en curso
///    todavía no dice nada, y lo cancelado se descartó: contarlos hundiría o inflaría el número
///    según cuándo se mire, que es justo lo que un reporte de cumplimiento no puede hacer.
/// 2. Un compromiso ACTIVO cuya fecha ya pasó cuenta como vencido aunque nadie lo haya cerrado. Se
///    mide el plazo, no la etiqueta: si no, bastaría no cerrar nada para no incumplir nunca.
///
/// <b>Diferencia deliberada con el escritorio (fase 1 es solo lectura).</b> Allí, antes de contar, se
/// llamaba a <c>SlaDevOpsReconciler.Aplicar</c>, que CERRABA en la base los SLA cuyo ticket de Azure
/// DevOps ya había terminado. Existía por un motivo real: sin eso, un ticket entregado a tiempo pero
/// con el SLA aún abierto se contaba como incumplimiento en cuanto pasaba su fecha — justo el número
/// que un jefe mira para juzgar al equipo. Aquí ese motivo se respeta pero la escritura no: la misma
/// decisión se aplica EN MEMORIA al contar (<see cref="ResolverPorTicket"/>), así el reporte no
/// miente y una pantalla de consulta no modifica datos. El cierre persistente vuelve con la fase de
/// escritura.
/// </summary>
public class SlaCumplimientoQueryService(AppDbContext db)
{
    /// <summary>
    /// Tope del período consultable. No es una regla de negocio sino de tamaño: el reporte lee todos
    /// los compromisos del rango para clasificarlos uno a uno, y un rango abierto («desde 2020»)
    /// acabaría trayendo la tabla entera. Dos años cubre cualquier comparación anual con holgura.
    /// </summary>
    public const int MaxDiasDelPeriodo = 731;

    /// <summary>
    /// Calcula el reporte. Devuelve <c>ok = false</c> con el motivo cuando el período no es válido,
    /// en vez de lanzar: es un dato que escribió una persona en un par de calendarios.
    /// </summary>
    public async Task<(bool ok, string error, SlaCumplimientoDto? reporte)> CalcularAsync(
        DateOnly desde, DateOnly hasta, AgrupacionSla agrupacion, CancellationToken ct = default)
    {
        if (hasta < desde)
            return (false, "El «hasta» no puede ser anterior al «desde».", null);
        if (hasta.DayNumber - desde.DayNumber > MaxDiasDelPeriodo)
            return (false, $"El período no puede pasar de {MaxDiasDelPeriodo} días. Acota las fechas.", null);

        // Las fechas se interpretan en UTC y no en la hora local del servidor. En el escritorio
        // «local» era la zona de quien miraba la pantalla; aquí el proceso corre en un servidor que
        // bien puede estar en UTC, así que tomar su reloj como referencia daría un corte de día
        // distinto al que la persona cree estar pidiendo, y encima variable según dónde se hospede.
        // Los vencimientos ya se guardan en UTC: comparar en la misma escala es lo predecible.
        var desdeUtc = desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hastaUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);   // fin de día inclusivo

        var slas = await db.SlaCommitments.AsNoTracking()
            .Where(s => s.DueAtUtc >= desdeUtc && s.DueAtUtc < hastaUtc)
            .Select(s => new
            {
                s.DueAtUtc,
                s.Status,
                s.DevOpsTicketExternalId,
                Desarrollador = s.Developer != null ? s.Developer.FullName : null
            })
            .ToListAsync(ct);

        // Prioridad, tags y estado salen del ticket de DevOps ligado (los que tengan uno). Se leen de
        // lo ya sincronizado en local: esto es una consulta de pantalla, no se sale a la red.
        var ids = slas.Where(s => s.DevOpsTicketExternalId != null)
                      .Select(s => s.DevOpsTicketExternalId!.Value).Distinct().ToList();

        var tickets = new Dictionary<int, TicketDeSla>();
        if (ids.Count > 0)
        {
            var ligados = await db.DevOpsTickets.AsNoTracking()
                .Where(t => ids.Contains(t.ExternalId))
                .Select(t => new TicketDeSla(t.ExternalId, t.Priority, t.Tags, t.State, t.UpdatedAtExternal))
                .ToListAsync(ct);

            // TryAdd y no ToDictionary: puede haber más de una fila por work item si una
            // sincronización duplicó, y una llave repetida dejaría la pantalla en blanco.
            foreach (var t in ligados) tickets.TryAdd(t.ExternalId, t);
        }

        var ahora = DateTime.UtcNow;
        var compromisos = slas.Select(s =>
        {
            string? prioridad = null, tags = null, estadoTicket = null;
            DateTime? cerradoEn = null;
            if (s.DevOpsTicketExternalId is int ext && tickets.TryGetValue(ext, out var tk))
            {
                prioridad = tk.Priority;
                tags = tk.Tags;
                estadoTicket = tk.State;
                cerradoEn = tk.UpdatedAtExternal;
            }

            // El estado que vale para contar: el guardado, salvo que el ticket ligado ya haya
            // terminado y el compromiso siga abierto (ver la nota de la cabecera).
            var estado = s.Status == SlaStatus.Activo
                ? ResolverPorTicket(estadoTicket, s.DueAtUtc, cerradoEn, ahora) ?? s.Status
                : s.Status;

            return new Compromiso(
                s.DueAtUtc,
                Clasificar(estado, s.DueAtUtc, ahora),
                string.IsNullOrWhiteSpace(s.Desarrollador) ? "(sin asignar)" : s.Desarrollador!,
                prioridad, tags);
        }).ToList();

        var filas = agrupacion switch
        {
            AgrupacionSla.Prioridad => Agrupar(compromisos, c => [NombreDePrioridad(c.Prioridad)]),
            AgrupacionSla.Tag => Agrupar(compromisos, c =>
            {
                // Los que no traen tag no pueden desaparecer del desglose: caen en «(sin tag)», igual
                // que «(sin asignar)» en las otras vistas, para que todo cuadre con el total.
                var tags = SepararTags(c.Tags);
                return tags.Count > 0 ? tags : ["(sin tag)"];
            }),
            AgrupacionSla.Mes => Agrupar(compromisos, c => [c.DueAtUtc.ToString("yyyy-MM")])
                                .OrderByDescending(f => f.Grupo, StringComparer.Ordinal).ToList(),
            _ => Agrupar(compromisos, c => [c.Desarrollador]),
        };

        var mensaje = compromisos.Count == 0
            ? "No hay compromisos de SLA con fecha límite en el período elegido."
            : $"{compromisos.Count} compromiso(s) con vencimiento en el período · agrupado por {EtiquetaDeAgrupacion(agrupacion)}.";

        return (true, "", new SlaCumplimientoDto(
            desde, hasta, agrupacion, compromisos.Count,
            Resumir(compromisos, "Total"), filas, mensaje));
    }

    /// <summary>Datos mínimos de un compromiso para medir cumplimiento, ya clasificado.</summary>
    private readonly record struct Compromiso(
        DateTime DueAtUtc, ResultadoSla Resultado, string Desarrollador, string? Prioridad, string? Tags);

    /// <summary>Lo único que hace falta del ticket ligado: de dónde salen prioridad, cliente y cierre.</summary>
    private sealed record TicketDeSla(
        int ExternalId, string? Priority, string? Tags, string? State, DateTime? UpdatedAtExternal);

    // ── Reglas puras (mismas que el escritorio; sin base ni red) ─────────────────────────

    /// <summary>
    /// Clasifica un compromiso por su estado y su plazo. Un ACTIVO cuya fecha límite ya pasó cuenta
    /// como vencido: se mide el plazo, no la etiqueta, y así el incumplimiento no se «esconde» por no
    /// haberse cerrado.
    /// </summary>
    public static ResultadoSla Clasificar(SlaStatus estado, DateTime dueAtUtc, DateTime nowUtc) => estado switch
    {
        SlaStatus.Cancelado => ResultadoSla.Cancelado,
        SlaStatus.Cumplido => ResultadoSla.Cumplido,
        SlaStatus.Vencido => ResultadoSla.Vencido,
        _ => nowUtc > dueAtUtc ? ResultadoSla.Vencido : ResultadoSla.EnCurso   // Activo
    };

    /// <summary>
    /// Qué estado le corresponde a un SLA ACTIVO cuyo ticket de DevOps ya terminó. Null = no tocarlo
    /// (el ticket sigue vivo, o no sabemos en qué estado está).
    ///
    ///  · Removed / Cancelled → Cancelado. El trabajo se descartó: no es un incumplimiento y no entra
    ///    en el porcentaje.
    ///  · Done / Closed / Completed / Resolved → Cumplido o Vencido SEGÚN EL PLAZO. Dar por cumplido
    ///    todo lo que aparece cerrado inflaría el número con trabajo entregado tarde.
    ///
    /// El momento del cierre se toma de la última modificación del ticket, no del reloj de ahora: si
    /// nadie mira el reporte en tres días, un ticket cerrado a tiempo no puede acabar contando como
    /// vencido solo por eso.
    /// </summary>
    public static SlaStatus? ResolverPorTicket(
        string? estadoTicket, DateTime dueAtUtc, DateTime? cerradoEnUtc, DateTime nowUtc)
    {
        var estado = (estadoTicket ?? "").Trim();
        if (estado.Length == 0 || !EsCerrado(estado)) return null;
        if (EsDescartado(estado)) return SlaStatus.Cancelado;

        // Sin fecha de cierre (o con una futura por relojes desfasados) se usa el ahora, que es lo
        // más conservador que se puede afirmar.
        var cerrado = cerradoEnUtc is DateTime c && c > DateTime.MinValue && c < nowUtc ? c : nowUtc;
        return cerrado <= dueAtUtc ? SlaStatus.Cumplido : SlaStatus.Vencido;
    }

    /// <summary>Estados con los que Azure DevOps da por terminado un work item.</summary>
    public static bool EsCerrado(string? estado) => (estado ?? "").Trim().ToLowerInvariant() switch
    {
        "done" or "closed" or "completed" or "resolved" or "removed" or "cancelled" or "canceled" => true,
        _ => false
    };

    /// <summary>Terminado por descarte, no por entrega.</summary>
    public static bool EsDescartado(string? estado) => (estado ?? "").Trim().ToLowerInvariant() switch
    {
        "removed" or "cancelled" or "canceled" => true,
        _ => false
    };

    private static readonly char[] Separadores = [';', ',', '|'];

    /// <summary>Tags de un ticket de DevOps, sin repetidos ni espacios sobrantes.</summary>
    public static List<string> SepararTags(string? tags) =>
        (tags ?? "")
        .Split(Separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Nombre legible de la prioridad de DevOps. Lo que no sea 1..4 cae en «(sin prioridad)»: DevOps
    /// deja el campo vacío o con valores libres y agruparlos por su texto crudo produciría un grupo
    /// por cada variante.
    /// </summary>
    public static string NombreDePrioridad(string? prioridadDevOps) =>
        int.TryParse((prioridadDevOps ?? "").Trim(), out var p) && p is >= 1 and <= 4
            ? $"P{p} — {p switch { 1 => "Muy alta", 2 => "Alta", 3 => "Media", _ => "Baja" }}"
            : "(sin prioridad)";

    /// <summary>
    /// Agrupa por una o varias claves por elemento —para los tags, un compromiso puede caer en varios
    /// grupos— y ordena por total descendente. Las claves vacías se omiten.
    /// </summary>
    private static List<SlaCumplimientoFilaDto> Agrupar(
        IEnumerable<Compromiso> items, Func<Compromiso, IEnumerable<string>> claves)
    {
        var cubos = new Dictionary<string, List<Compromiso>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in items)
            foreach (var k in claves(c))
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                if (!cubos.TryGetValue(k, out var lista)) { lista = []; cubos[k] = lista; }
                lista.Add(c);
            }

        return cubos
            .Select(kv => Resumir(kv.Value, kv.Key))
            .OrderByDescending(f => f.Total).ThenBy(f => f.Grupo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Resume un conjunto de compromisos en una sola fila.</summary>
    private static SlaCumplimientoFilaDto Resumir(IEnumerable<Compromiso> items, string grupo)
    {
        int cumplidos = 0, vencidos = 0, enCurso = 0, cancelados = 0;
        foreach (var c in items)
            switch (c.Resultado)
            {
                case ResultadoSla.Cumplido: cumplidos++; break;
                case ResultadoSla.Vencido: vencidos++; break;
                case ResultadoSla.EnCurso: enCurso++; break;
                default: cancelados++; break;
            }

        int total = cumplidos + vencidos + enCurso + cancelados;
        int resueltos = cumplidos + vencidos;
        return new SlaCumplimientoFilaDto(
            grupo, cumplidos, vencidos, enCurso, cancelados, total, resueltos,
            resueltos == 0 ? null : Math.Round(100.0 * cumplidos / resueltos, 1));
    }

    private static string EtiquetaDeAgrupacion(AgrupacionSla a) => a switch
    {
        AgrupacionSla.Prioridad => "Prioridad",
        AgrupacionSla.Tag => "Cliente (tag)",
        AgrupacionSla.Mes => "Mes",
        _ => "Desarrollador"
    };
}
