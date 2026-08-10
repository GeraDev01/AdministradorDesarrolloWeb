using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Un SLA que se cerró solo porque su ticket de DevOps ya terminó.</summary>
public readonly record struct SlaCerradoPorTicket(int SlaId, int TicketExternalId, string EstadoTicket, SlaStatus Nuevo);

/// <summary>
/// Cierra los SLA cuyo ticket de Azure DevOps ya terminó.
///
/// <para><b>El problema que resuelve.</b> Un SLA solo salía de «Activo» de tres maneras: que el líder
/// lo cerrara a mano, que lo cancelara, o que se le pasara la fecha. Que el work item se moviera a
/// <i>Done</i> en DevOps no era ninguna de las tres. Resultado: el desarrollador seguía viendo en
/// «Mis SLA» compromisos de trabajo ya entregado, le seguían llegando recordatorios para comentar un
/// ticket cerrado, y al pasar el plazo el compromiso se marcaba <i>Vencido</i> y se escalaba al
/// líder — contando como incumplimiento algo que se había entregado a tiempo.</para>
///
/// <para><b>Dónde vive cada mitad de la regla.</b> La decisión —qué estado le toca a un SLA activo
/// según cómo terminó su ticket— ya está portada y probada en
/// <see cref="SlaCumplimientoQueryService.ResolverPorTicket"/>, porque el reporte de cumplimiento la
/// necesitaba antes que nadie. Aquí NO se vuelve a escribir: se llama. Lo que esta clase añade es la
/// otra mitad, la que aquella pantalla no podía hacer por ser de solo lectura — dejar el cierre
/// ESCRITO en la base, que es lo que apaga los recordatorios y evita el escalamiento.</para>
///
/// <para>Solo toca los <b>Activos</b>. Un SLA ya marcado Vencido se queda como está: se escaló al
/// líder y hay constancia de ello; borrarla en silencio sería peor que el error que arregla. Si el
/// líder considera que se entregó, tiene «Marcar cumplido».</para>
///
/// <para>Es estático y recibe el contexto en vez de inyectarlo para que la regla se pueda probar sola
/// y para que quien la llame decida en qué unidad de trabajo se guarda.</para>
/// </summary>
public static class SlaDevOpsReconciler
{
    /// <summary>
    /// Aplica la decisión a los SLA activos con ticket ligado y guarda. Con
    /// <paramref name="developerId"/> se limita a los de esa persona: refrescar «Mis SLA» no tiene
    /// por qué reescribir en silencio los compromisos de todo el equipo.
    ///
    /// Solo mira tickets YA SINCRONIZADOS en local: esto corre al abrir una pantalla y no puede
    /// salir a la red. Devuelve lo que cambió, para que quien llame lo deje en la bitácora.
    /// </summary>
    public static async Task<List<SlaCerradoPorTicket>> AplicarAsync(
        AppDbContext db, int? developerId, DateTime nowUtc, CancellationToken ct = default)
    {
        var activos = await db.SlaCommitments
            .Where(s => s.Status == SlaStatus.Activo && s.DevOpsTicketExternalId != null)
            .Where(s => developerId == null || s.DeveloperId == developerId)
            .ToListAsync(ct);
        if (activos.Count == 0) return [];

        var ids = activos.Select(s => s.DevOpsTicketExternalId!.Value).Distinct().ToList();
        var filas = await db.DevOpsTickets.AsNoTracking()
            .Where(t => ids.Contains(t.ExternalId))
            .Select(t => new { t.ExternalId, t.State, t.UpdatedAtExternal })
            .ToListAsync(ct);

        // TryAdd y no ToDictionary: una sincronización pudo dejar más de una fila por work item, y
        // una llave repetida tumbaría la pantalla entera por un dato duplicado.
        var tickets = new Dictionary<int, (string? Estado, DateTime? Actualizado)>();
        foreach (var t in filas) tickets.TryAdd(t.ExternalId, (t.State, t.UpdatedAtExternal));

        var cerrados = new List<SlaCerradoPorTicket>();
        foreach (var sla in activos)
        {
            // Sin ticket sincronizado no se supone nada: puede ser un SLA con un número escrito a
            // mano, o de un proyecto que aún no se ha traído.
            if (!tickets.TryGetValue(sla.DevOpsTicketExternalId!.Value, out var t)) continue;

            var nuevo = SlaCumplimientoQueryService.ResolverPorTicket(
                t.Estado, sla.DueAtUtc, t.Actualizado, nowUtc);
            if (nuevo is not SlaStatus estado) continue;

            sla.Status = estado;
            sla.NextReminderAtUtc = null;   // deja de pedir comentarios sobre un ticket terminado
            sla.Notes = Anotar(sla.Notes, t.Estado ?? "", estado);
            cerrados.Add(new SlaCerradoPorTicket(sla.Id, sla.DevOpsTicketExternalId!.Value, t.Estado ?? "", estado));
        }

        if (cerrados.Count > 0) await db.SaveChangesAsync(ct);
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
