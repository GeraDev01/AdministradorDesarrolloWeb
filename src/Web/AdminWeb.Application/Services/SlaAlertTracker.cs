using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Decide de qué compromisos hay que avisar en cada revisión, para no repetir el mismo aviso una y
/// otra vez pero tampoco quedarse callado cuando aparece algo nuevo.
///
/// <para>Reglas, las mismas del escritorio:</para>
/// <list type="bullet">
///   <item>un compromiso se avisa la primera vez que aparece como pendiente;</item>
///   <item>vuelve a avisarse si pasa a estar FUERA DE PLAZO, porque es información nueva y más grave;</item>
///   <item>deja de recordarse en cuanto sale de la lista de pendientes —porque se comentó, se pospuso
///         o se cerró—, de modo que su siguiente recordatorio vuelva a avisar.</item>
/// </list>
///
/// <para><b>Qué cambia respecto al escritorio y por qué no había alternativa.</b> Allí ese rastro
/// eran dos <c>HashSet</c> en la memoria del proceso, y bastaba: el proceso era la sesión de una
/// persona y moría con ella. En el servidor no vale. El trabajo de fondo corre siempre, pero el
/// proceso se reinicia en cada despliegue; con el rastro en memoria, la primera vuelta después de
/// publicar una versión volvería a avisar de TODO lo ya avisado, a todo el equipo. Y con más de una
/// instancia de la API cada una avisaría por su cuenta. Por eso el rastro se guarda en la base, en el
/// propio compromiso (<see cref="SlaCommitment.ReminderNotifiedAtUtc"/> y
/// <see cref="SlaCommitment.OverdueNotifiedAtUtc"/>), donde sobrevive a los reinicios y es el mismo
/// para todas las instancias.</para>
///
/// <para>Va en el compromiso y no en una tabla aparte porque es un dato de uno a uno con él, sin
/// historia que conservar, y así cerrar o borrar el SLA se lleva su rastro sin dejar filas huérfanas
/// — exactamente como ya hacía <c>BreachNotifiedAtUtc</c>, que es el equivalente para el
/// escalamiento al líder y lleva persistido desde el escritorio.</para>
///
/// <para>La DECISIÓN es pura y estática para poder probarse sola; lo único que necesita base es dejar
/// constancia, y va en un método aparte que se llama DESPUÉS de que el aviso salió: dar por avisado
/// algo cuyo aviso falló es el error que este rastro existe para no cometer.</para>
/// </summary>
public class SlaAlertTracker(AppDbContext db)
{
    /// <summary>
    /// De los pendientes, aquello de lo que toca avisar ahora. No escribe nada.
    /// </summary>
    public static List<SlaCommitment> Nuevos(IReadOnlyList<SlaCommitment> pendientes, DateTime nowUtc) =>
        pendientes
            .Where(s => s.TocaAvisarDelRecordatorio(nowUtc) || s.TocaAvisarDeVencimiento(nowUtc))
            .ToList();

    /// <summary>
    /// Deja escrito que ya se avisó de estos compromisos. Devuelve cuántos quedaron marcados.
    ///
    /// Las filas se releen del contexto en vez de reutilizar las que llegan: quien decide trabaja con
    /// copias sin rastreo (AsNoTracking), y marcar sobre ellas no guardaría nada — el rastro se
    /// perdería en silencio y volveríamos al problema que esta clase resuelve.
    /// </summary>
    public async Task<int> MarcarAvisadosAsync(
        IEnumerable<SlaCommitment> avisados, DateTime nowUtc, CancellationToken ct = default)
    {
        var ids = avisados.Select(s => s.Id).Distinct().ToList();
        if (ids.Count == 0) return 0;

        var filas = await db.SlaCommitments.Where(s => ids.Contains(s.Id)).ToListAsync(ct);

        int marcados = 0;
        foreach (var s in filas)
        {
            // Se vuelve a preguntar sobre la fila fresca y no se marca a ciegas: entre la decisión y
            // este momento el compromiso pudo cerrarse o reprogramarse, y sellar entonces el rastro
            // silenciaría el recordatorio siguiente, que sí toca dar.
            bool recordatorio = s.TocaAvisarDelRecordatorio(nowUtc);
            bool vencimiento = s.TocaAvisarDeVencimiento(nowUtc);
            if (!recordatorio && !vencimiento) continue;

            if (recordatorio) s.ReminderNotifiedAtUtc = nowUtc;
            if (vencimiento) s.OverdueNotifiedAtUtc = nowUtc;
            marcados++;
        }

        if (marcados > 0) await db.SaveChangesAsync(ct);
        return marcados;
    }
}
