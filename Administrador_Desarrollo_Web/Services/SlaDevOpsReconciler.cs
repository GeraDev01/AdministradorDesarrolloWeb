using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Un SLA que se cerró solo porque su ticket de DevOps ya terminó.</summary>
public readonly record struct SlaCerradoPorTicket(int SlaId, int TicketExternalId, string EstadoTicket, SlaStatus Nuevo);

/// <summary>
/// Cierra los SLA cuyo ticket de Azure DevOps ya terminó.
///
/// <b>El problema que resuelve.</b> Un SLA solo salía de «Activo» de tres maneras: que el
/// administrador lo cerrara a mano, que lo cancelara, o que se le pasara la fecha. Que el work item
/// se moviera a <i>Done</i> en DevOps no era ninguna de las tres. Resultado: el desarrollador seguía
/// viendo en «Mis SLA» compromisos de trabajo ya entregado, le seguían llegando recordatorios para
/// comentar un ticket cerrado, y al pasar el plazo el compromiso se marcaba <i>Vencido</i> y se
/// escalaba al jefe — contando como incumplimiento algo que se había entregado a tiempo.
///
/// <b>Cómo decide.</b> Se mira el estado del ticket YA SINCRONIZADO en local (no se llama a la red:
/// esto corre al abrir una pantalla):
///  · <i>Removed / Cancelled</i> → <b>Cancelado</b>. El trabajo se descartó; no es un incumplimiento
///    y el reporte de cumplimiento no lo cuenta en el porcentaje.
///  · <i>Done / Closed / Completed</i> → <b>Cumplido</b> o <b>Vencido</b> <i>según el plazo</i>. Dar
///    por cumplido todo lo que aparece cerrado inflaría el cumplimiento con trabajo entregado tarde:
///    lo que se mide es la fecha, no la etiqueta.
///
/// El momento del cierre se toma de la última modificación del ticket en DevOps, no del reloj de
/// ahora: si nadie abre la aplicación en tres días, un ticket cerrado a tiempo no puede acabar
/// contando como vencido solo por eso.
///
/// Solo toca los <b>Activos</b>. Un SLA ya marcado Vencido se queda como está: se escaló al
/// administrador y hay constancia de ello; borrarla en silencio sería peor que el error que arregla.
/// Si el administrador considera que se entregó, tiene «Marcar cumplido».
///
/// Vive fuera de <see cref="SlaService"/> y sin inyección para que la regla se pueda probar sola, y
/// porque <see cref="AzureDevOpsService"/> ya depende de más de media aplicación.
/// </summary>
public static class SlaDevOpsReconciler
{
    /// <summary>
    /// Qué hacer con un SLA activo dado el estado de su ticket. null = no tocarlo (el ticket sigue
    /// vivo, o no sabemos en qué estado está).
    /// </summary>
    public static SlaStatus? Decidir(string? estadoTicket, DateTime dueAtUtc, DateTime? cerradoEnUtc, DateTime nowUtc)
    {
        var estado = (estadoTicket ?? "").Trim();
        if (estado.Length == 0) return null;
        if (!AzureDevOpsService.EsCerrado(estado)) return null;

        // Descartado, no incumplido: nadie tenía ya que atender esto.
        if (AzureDevOpsService.MapStatus(estado) == RequirementStatus.Cancelado) return SlaStatus.Cancelado;

        // Cuándo se cerró de verdad. Sin ese dato (o con una fecha futura por relojes desfasados) se
        // usa el ahora, que es lo más conservador que se puede afirmar.
        var cerrado = cerradoEnUtc is DateTime c && c > DateTime.MinValue && c < nowUtc ? c : nowUtc;
        return cerrado <= dueAtUtc ? SlaStatus.Cumplido : SlaStatus.Vencido;
    }

    /// <summary>
    /// Aplica la decisión a los SLA activos con ticket ligado y guarda. Con
    /// <paramref name="developerId"/> se limita a los de esa persona: la pantalla de un
    /// desarrollador no tiene por qué reescribir en silencio los compromisos de todo el equipo.
    ///
    /// Devuelve lo que cambió, para que quien llame lo deje en la bitácora.
    /// </summary>
    public static List<SlaCerradoPorTicket> Aplicar(AppDbContext db, int? developerId, DateTime nowUtc)
    {
        var activos = db.SlaCommitments
            .Where(s => s.Status == SlaStatus.Activo && s.DevOpsTicketExternalId != null)
            .Where(s => developerId == null || s.DeveloperId == developerId)
            .ToList();
        if (activos.Count == 0) return [];

        var ids = activos.Select(s => s.DevOpsTicketExternalId!.Value).Distinct().ToList();
        var tickets = db.DevOpsTickets.AsNoTracking()
            .Where(t => ids.Contains(t.ExternalId))
            .Select(t => new { t.ExternalId, t.State, t.UpdatedAtExternal })
            .ToList()
            .GroupBy(t => t.ExternalId)
            .ToDictionary(g => g.Key, g => g.First());

        var cerrados = new List<SlaCerradoPorTicket>();
        foreach (var sla in activos)
        {
            // Sin ticket sincronizado no se supone nada: puede ser un SLA con un número escrito a
            // mano, o de un proyecto que aún no se ha traído.
            if (!tickets.TryGetValue(sla.DevOpsTicketExternalId!.Value, out var t)) continue;
            if (Decidir(t.State, sla.DueAtUtc, t.UpdatedAtExternal, nowUtc) is not SlaStatus nuevo) continue;

            sla.Status = nuevo;
            sla.NextReminderAtUtc = null;   // deja de pedir comentarios sobre un ticket terminado
            sla.Notes = Anotar(sla.Notes, t.State, nuevo);
            cerrados.Add(new SlaCerradoPorTicket(sla.Id, t.ExternalId, t.State, nuevo));
        }

        if (cerrados.Count > 0) db.SaveChanges();
        return cerrados;
    }

    /// <summary>Deja dicho por qué se cerró solo, que si no parece que alguien lo tocó a mano.</summary>
    private static string Anotar(string? notas, string estadoTicket, SlaStatus nuevo)
    {
        var linea = nuevo == SlaStatus.Cancelado
            ? $"Cerrado automáticamente: el ticket pasó a «{estadoTicket}» en Azure DevOps."
            : $"Cerrado automáticamente como {nuevo}: el ticket pasó a «{estadoTicket}» en Azure DevOps.";
        return string.IsNullOrWhiteSpace(notas) ? linea : $"{linea}\n{notas.Trim()}";
    }
}
