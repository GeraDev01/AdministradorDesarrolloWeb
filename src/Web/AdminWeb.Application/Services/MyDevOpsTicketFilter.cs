using AdminWeb.Domain.Entities;

namespace AdminWeb.Application.Services;

/// <summary>Lo elegido en los filtros de «Mis tickets DevOps». Nulo o false = no filtrar.</summary>
/// <param name="UltimosDias">Ventana de tiempo. Nula = todo el historial.</param>
/// <param name="SoloSinEstimar">Lo que te asignaron y todavía no dijiste cuánto te va a llevar.</param>
public record FiltroDeMisTickets(
    string? Texto = null,
    string? Estado = null,
    string? Tipo = null,
    string? Iteracion = null,
    bool SoloAbiertos = false,
    bool SoloSinEstimar = false,
    int? UltimosDias = null);

/// <summary>
/// Filtrado de la lista de tickets del desarrollador.
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte.</para>
///
/// Existe porque la pantalla traía el historial COMPLETO: años de work items cerrados encima de los
/// tres que la persona tiene abiertos hoy. Vive fuera de la pantalla para poder probarlo — una
/// ventana de días mal calculada esconde trabajo pendiente sin que nada lo delate.
/// </summary>
public static class MyDevOpsTicketFilter
{
    /// <summary>Ventana por omisión al abrir la pantalla y al sincronizar: un trimestre.</summary>
    public const int DiasPorOmision = 90;

    /// <summary>
    /// Cuando se pide «todo el historial», la SINCRONIZACIÓN se acota a un año de todos modos: traer
    /// de golpe años de work items cerrados por una sola pulsación no le sirve a nadie. Ver la lista
    /// entera sí se puede; es solo lo que se le pide a DevOps lo que se limita.
    /// </summary>
    public const int DiasMaximosDeSincronizacion = 365;

    public static List<DevOpsTicket> Aplicar(
        IEnumerable<DevOpsTicket> tickets, FiltroDeMisTickets filtro, DateTime ahoraUtc)
    {
        var texto = string.IsNullOrWhiteSpace(filtro.Texto) ? null : filtro.Texto.Trim();
        DateTime? desde = filtro.UltimosDias is int dias && dias > 0 ? ahoraUtc.AddDays(-dias) : null;

        return tickets.Where(t =>
            (texto == null
                || t.ExternalId.ToString().Contains(texto)
                || Contiene(t.Title, texto)
                || Contiene(t.State, texto)
                || Contiene(t.WorkItemType, texto)
                || Contiene(t.IterationPath, texto))
            && Igual(t.State, filtro.Estado)
            && Igual(t.WorkItemType, filtro.Tipo)
            && Igual(t.IterationPath, filtro.Iteracion)
            && (!filtro.SoloAbiertos || !DevOpsService.EsCerrado(t.State))
            // Lo pendiente de estimar se cuenta solo sobre lo ABIERTO: pedir la estimación de un
            // ticket ya cerrado no sirve para planear nada.
            && (!filtro.SoloSinEstimar || (t.SinEstimar && !DevOpsService.EsCerrado(t.State)))
            // Sin fecha de DevOps se deja pasar: esconder un ticket porque le falta un dato de
            // sincronización sería esconder trabajo real.
            && (desde == null || t.UpdatedAtExternal == null || t.UpdatedAtExternal >= desde))
            .ToList();
    }

    /// <summary>Valores presentes en los datos, para llenar un desplegable sin callejones sin salida.</summary>
    public static List<string> Opciones(IEnumerable<string?> valores) =>
        valores
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Pie de la lista. El conteo de ABIERTOS se calcula sobre todo lo que hay, no sobre lo filtrado:
    /// es el trabajo pendiente real, y no debe cambiar porque alguien esté mirando otra cosa.
    /// </summary>
    public static string Resumen(int mostrados, int total, int abiertos, DateTime? ultimaSincronizacionUtc)
    {
        var cuantos = mostrados == total ? $"{total} ticket(s)" : $"{mostrados} de {total} ticket(s)";
        var sincronizacion = ultimaSincronizacionUtc is DateTime s
            ? s.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "nunca";

        return $"{cuantos}  ·  {abiertos} sin cerrar  ·  última sincronización: {sincronizacion}";
    }

    private static bool Contiene(string? valor, string texto) =>
        valor != null && valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);

    private static bool Igual(string? valor, string? filtro) =>
        string.IsNullOrEmpty(filtro) || string.Equals(valor, filtro, StringComparison.CurrentCultureIgnoreCase);
}
