using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Lo elegido en los filtros del panel de Azure DevOps. null / false = no filtrar.</summary>
public record DevOpsLinkFilter(
    string? Texto = null,
    string? Estado = null,
    string? Tipo = null,
    string? Asignado = null,
    bool SoloSinVincular = false);

/// <summary>Lo elegido en los filtros del panel de Freshdesk. null / false = no filtrar.</summary>
public record FreshDeskLinkFilter(
    string? Texto = null,
    string? Estado = null,
    string? Prioridad = null,
    string? Agente = null,
    bool SoloSinVincular = false);

/// <summary>
/// Filtrado de las dos listas de la pantalla «Vincular tickets». Vive fuera del control para poder
/// probarlo: son cinco condiciones que se combinan, y equivocarse en una significa esconder tickets
/// que sí había que vincular sin que nada lo delate en pantalla.
///
/// Criterios firmes:
///  · el texto busca por CONTENIDO (lo que se recuerda de un ticket es un trozo del título);
///  · los combos empatan por valor COMPLETO (elegir el estado «New» no debe traer «Newer»);
///  · todo ignora mayúsculas y acentos de capitalización, porque los datos vienen de dos sistemas
///    externos donde nadie garantiza cómo se escribió un nombre.
/// </summary>
public static class TicketLinkFilter
{
    public static List<DevOpsTicket> Aplicar(
        IEnumerable<DevOpsTicket> tickets, DevOpsLinkFilter filtro, IReadOnlySet<int> vinculados)
    {
        var texto = Normalizar(filtro.Texto);
        return tickets.Where(t =>
            (texto == null
                || Contiene(t.Title, texto)
                || t.ExternalId.ToString().Contains(texto)
                || Contiene(t.AssignedTo, texto))
            && Igual(t.State, filtro.Estado)
            && Igual(t.WorkItemType, filtro.Tipo)
            && Igual(t.AssignedTo, filtro.Asignado)
            && (!filtro.SoloSinVincular || !vinculados.Contains(t.Id)))
            .ToList();
    }

    public static List<FreshDeskTicket> Aplicar(
        IEnumerable<FreshDeskTicket> tickets, FreshDeskLinkFilter filtro, IReadOnlySet<int> vinculados)
    {
        var texto = Normalizar(filtro.Texto);
        return tickets.Where(t =>
            (texto == null
                || Contiene(t.Subject, texto)
                || t.ExternalId.ToString().Contains(texto)
                || Contiene(t.RequesterName, texto))
            && Igual(FreshDeskTicket.StatusLabel(t.Status), filtro.Estado)
            && Igual(FreshDeskTicket.PriorityLabel(t.Priority), filtro.Prioridad)
            && Igual(t.AgentName, filtro.Agente)
            && (!filtro.SoloSinVincular || !vinculados.Contains(t.Id)))
            .ToList();
    }

    /// <summary>
    /// Valores distintos, sin vacíos y ordenados, para llenar un combo de filtro con lo que
    /// REALMENTE hay en los datos. Un combo que ofrece estados inexistentes es una lista de
    /// callejones sin salida.
    /// </summary>
    public static List<string> Opciones(IEnumerable<string?> valores) =>
        valores
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Pie de la lista. Dice siempre cuántos quedan SIN VINCULAR, que es el trabajo pendiente real
    /// de esta pantalla, aunque el filtro de turno no lo esté mostrando.
    /// </summary>
    public static string Resumen(int mostrados, int total, int sinVincular, string sustantivo)
    {
        var cuantos = mostrados == total
            ? $"{total} {sustantivo}(s)"
            : $"{mostrados} de {total} {sustantivo}(s)";
        return $"{cuantos}  ·  {sinVincular} sin vincular";
    }

    private static string? Normalizar(string? texto)
    {
        texto = texto?.Trim();
        return string.IsNullOrEmpty(texto) ? null : texto;
    }

    private static bool Contiene(string? valor, string texto) =>
        valor != null && valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);

    private static bool Igual(string? valor, string? filtro) =>
        string.IsNullOrEmpty(filtro) || string.Equals(valor, filtro, StringComparison.CurrentCultureIgnoreCase);
}
