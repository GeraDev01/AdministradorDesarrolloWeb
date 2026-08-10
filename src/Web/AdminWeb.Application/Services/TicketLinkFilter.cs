using AdminWeb.Domain.Entities;

namespace AdminWeb.Application.Services;

/// <summary>Lo elegido en los filtros del panel de Azure DevOps. null / false = no filtrar.</summary>
public record FiltroDeWorkItems(
    string? Texto = null,
    string? Estado = null,
    string? Tipo = null,
    string? Asignado = null,
    bool SoloSinVincular = false);

/// <summary>Lo elegido en los filtros del panel de Freshdesk. null / false = no filtrar.</summary>
public record FiltroDeTickets(
    string? Texto = null,
    string? Estado = null,
    string? Prioridad = null,
    string? Agente = null,
    bool SoloSinVincular = false);

/// <summary>
/// Filtrado de las dos listas de la pantalla «Vincular tickets». Vive fuera de la pantalla para poder
/// probarlo: son cinco condiciones que se combinan, y equivocarse en una significa esconder tickets
/// que sí había que vincular sin que nada lo delate a la vista.
///
/// Criterios firmes, portados tal cual del escritorio:
///  · el texto busca por CONTENIDO (lo que se recuerda de un ticket es un trozo del título);
///  · los combos empatan por valor COMPLETO (elegir el estado «New» no debe traer «Newer»);
///  · todo ignora mayúsculas y acentos de capitalización, porque los datos vienen de dos sistemas
///    externos donde nadie garantiza cómo se escribió un nombre.
///
/// <para><b>Se filtra en memoria y en el SERVIDOR</b>, no en la base ni en el navegador. En la base
/// no cabe: las comparaciones son culturales y sin distinguir mayúsculas, y traducirlas a SQL las
/// dejaría a merced de la intercalación de la columna. En el navegador tampoco: obligaría a mandarle
/// las dos tablas enteras para que él decidiera qué enseñar.</para>
/// </summary>
public static class TicketLinkFilter
{
    /// <summary>Los work items que cumplen el filtro, en el orden en que llegaron.</summary>
    /// <param name="vinculados">Identificadores de los work items que ya tienen al menos un vínculo.</param>
    public static List<DevOpsTicket> Aplicar(
        IEnumerable<DevOpsTicket> tickets, FiltroDeWorkItems filtro, IReadOnlySet<int> vinculados)
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

    /// <summary>Los tickets de Freshdesk que cumplen el filtro, en el orden en que llegaron.</summary>
    /// <param name="vinculados">Identificadores de los tickets que ya tienen al menos un vínculo.</param>
    public static List<FreshDeskTicket> Aplicar(
        IEnumerable<FreshDeskTicket> tickets, FiltroDeTickets filtro, IReadOnlySet<int> vinculados)
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
