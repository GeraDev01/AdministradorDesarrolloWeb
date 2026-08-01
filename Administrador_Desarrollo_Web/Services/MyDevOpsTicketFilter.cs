using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Lo elegido en los filtros de «Mis tickets DevOps». null / false = no filtrar.</summary>
public record MyDevOpsFilter(
    string? Texto = null,
    string? Estado = null,
    string? Tipo = null,
    string? Iteracion = null,
    bool SoloAbiertos = false,
    int? UltimosDias = null);

/// <summary>
/// Filtrado de la lista de tickets del desarrollador.
///
/// Existe porque la pantalla traía el historial COMPLETO: años de work items cerrados encima de los
/// tres que la persona tiene abiertos hoy. Vive fuera del control para poder probarlo — una ventana
/// de días mal calculada esconde trabajo pendiente sin que nada lo delate.
/// </summary>
public static class MyDevOpsTicketFilter
{
    /// <summary>Ventana por omisión al abrir la pantalla y al sincronizar: un trimestre.</summary>
    public const int DiasPorOmision = 90;

    public static List<DevOpsTicket> Aplicar(
        IEnumerable<DevOpsTicket> tickets, MyDevOpsFilter filtro, DateTime ahoraUtc)
    {
        var texto = string.IsNullOrWhiteSpace(filtro.Texto) ? null : filtro.Texto.Trim();
        DateTime? desde = filtro.UltimosDias is int d && d > 0 ? ahoraUtc.AddDays(-d) : null;

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
            && (!filtro.SoloAbiertos || !AzureDevOpsService.EsCerrado(t.State))
            // Sin fecha de DevOps se deja pasar: esconder un ticket porque le falta un dato de
            // sincronización sería esconder trabajo real.
            && (desde == null || t.UpdatedAtExternal == null || t.UpdatedAtExternal >= desde))
            .ToList();
    }

    /// <summary>Valores presentes en los datos, para llenar un combo sin ofrecer callejones sin salida.</summary>
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
    public static string Resumen(int mostrados, int total, int abiertos, DateTime? ultimaSync)
    {
        var cuantos = mostrados == total ? $"{total} ticket(s)" : $"{mostrados} de {total} ticket(s)";
        var sync = ultimaSync is DateTime s ? s.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "nunca";
        return $"{cuantos}  ·  {abiertos} sin cerrar  ·  última sincronización: {sync}";
    }

    private static bool Contiene(string? valor, string texto) =>
        valor != null && valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);

    private static bool Igual(string? valor, string? filtro) =>
        string.IsNullOrEmpty(filtro) || string.Equals(valor, filtro, StringComparison.CurrentCultureIgnoreCase);
}
