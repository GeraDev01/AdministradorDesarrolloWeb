using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Freshdesk;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Los vínculos entre un work item de Azure DevOps y un ticket de Freshdesk: el mismo trabajo visto
/// desde los dos sistemas.
///
/// <para>El vínculo lo hace una PERSONA y no una regla automática. Nadie puede deducir de forma
/// fiable que el ticket «no me deja facturar» es el work item «arreglar redondeo en el timbrado»; por
/// eso esta pantalla existe, y por eso un vínculo hecho a mano no se borra solo —ni siquiera cuando
/// la sincronización de Freshdesk retira un ticket por no cumplir el filtro—.</para>
///
/// <para>La lista se arma en el SERVIDOR y filtrada: son dos tablas que crecen sin tope y mandarlas
/// enteras al navegador para que él decidiera qué enseñar sería regalarle megabytes por recarga.</para>
/// </summary>
public class VinculosDeTicketsService(AppDbContext db, ICurrentUser quien, AuditService bitacora)
{
    /// <summary>
    /// Cuántas filas viajan como mucho en cada una de las tres listas. Cuando se recorta, la pantalla
    /// lo dice y el pie sigue contando la verdad: cuántas cumplen el filtro y cuántas quedan sin
    /// vincular. Recortar en silencio escondería justo lo que se venía a buscar.
    /// </summary>
    private const int TopeDeFilas = 200;

    /// <summary>
    /// La pantalla completa: los vínculos que ya existen y las dos listas desde las que se enlaza,
    /// cada una con sus opciones de filtro sacadas de los datos que hay.
    /// </summary>
    /// <param name="buscarEnVinculos">Texto libre sobre los vínculos ya hechos (título, asunto,
    /// números o notas).</param>
    public async Task<PantallaDeVinculosDto> PantallaAsync(
        FiltroDeWorkItems filtroDeWorkItems,
        FiltroDeTickets filtroDeTickets,
        string? buscarEnVinculos = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var enlaces = await db.TicketLinks.AsNoTracking()
            .Include(l => l.DevOpsTicket)
            .Include(l => l.FreshDeskTicket)
            .OrderByDescending(l => l.LinkedAt)
            .ToListAsync(ct);

        var workItemsVinculados = enlaces.Select(l => l.DevOpsTicketId).ToHashSet();
        var ticketsVinculados = enlaces.Select(l => l.FreshDeskTicketId).ToHashSet();

        // Se traen solo las columnas que la pantalla usa. Las dos entidades arrastran la descripción
        // completa del ticket —kilobytes por fila— y aquí no se enseña ninguna: leerlas sería pagar
        // por algo que se tira.
        var workItems = await db.DevOpsTickets.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAtExternal)
            .Select(t => new DevOpsTicket
            {
                Id = t.Id, ExternalId = t.ExternalId, Title = t.Title,
                WorkItemType = t.WorkItemType, State = t.State, AssignedTo = t.AssignedTo, Url = t.Url
            })
            .ToListAsync(ct);

        var tickets = await db.FreshDeskTickets.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAtExternal)
            .Select(t => new FreshDeskTicket
            {
                Id = t.Id, ExternalId = t.ExternalId, Subject = t.Subject,
                Status = t.Status, Priority = t.Priority, AgentName = t.AgentName, Url = t.Url
            })
            .ToListAsync(ct);

        return new PantallaDeVinculosDto(
            new ResumenDeVinculosDto(
                workItems.Count, tickets.Count, enlaces.Count,
                workItemsVinculados.Count, ticketsVinculados.Count),
            FiltrarVinculos(enlaces, buscarEnVinculos),
            ListaDeWorkItems(workItems, filtroDeWorkItems, workItemsVinculados),
            ListaDeTickets(tickets, filtroDeTickets, ticketsVinculados));
    }

    /// <summary>
    /// Enlaza un work item con un ticket. <paramref name="notas"/> es opcional: es el sitio donde
    /// explicar por qué se enlazaron, que es lo único que nadie podrá deducir después.
    /// </summary>
    public async Task<(bool ok, string mensaje)> VincularAsync(
        int workItemId, int ticketId, string? notas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var workItem = await db.DevOpsTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == workItemId, ct);
        if (workItem == null)
            return (false, "Ese work item ya no está en la lista. Actualiza y vuelve a intentarlo.");

        var ticket = await db.FreshDeskTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket == null)
            return (false, "Ese ticket de Freshdesk ya no está en la lista. Actualiza y vuelve a intentarlo.");

        // Se comprueba antes de insertar aunque la base tenga el índice único: así el mensaje explica
        // qué pasó, en vez de dejar salir un choque de clave que nadie sabe leer.
        if (await db.TicketLinks.AnyAsync(
                l => l.DevOpsTicketId == workItemId && l.FreshDeskTicketId == ticketId, ct))
            return (false, $"DevOps #{workItem.ExternalId} y Freshdesk #{ticket.ExternalId} ya estaban vinculados.");

        db.TicketLinks.Add(new TicketLink
        {
            DevOpsTicketId = workItemId,
            FreshDeskTicketId = ticketId,
            Notes = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
            LinkedAt = DateTime.UtcNow,
            LinkedByUser = quien.FullName
        });
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Create, "TicketLink", null,
            $"Vínculo creado: DevOps #{workItem.ExternalId} ↔ Freshdesk #{ticket.ExternalId}", ct);

        return (true, $"Vinculados: DevOps #{workItem.ExternalId} ↔ Freshdesk #{ticket.ExternalId}.");
    }

    /// <summary>
    /// Quita un vínculo. No toca ninguno de los dos tickets: deshace la relación que alguien afirmó,
    /// nada más.
    /// </summary>
    public async Task<(bool ok, string mensaje)> QuitarAsync(int vinculoId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var enlace = await db.TicketLinks
            .Include(l => l.DevOpsTicket)
            .Include(l => l.FreshDeskTicket)
            .FirstOrDefaultAsync(l => l.Id == vinculoId, ct);
        if (enlace == null)
            return (false, "Ese vínculo ya no existe. Actualiza la lista.");

        var descripcion = $"DevOps #{enlace.DevOpsTicket.ExternalId} ↔ Freshdesk #{enlace.FreshDeskTicket.ExternalId}";

        db.TicketLinks.Remove(enlace);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Delete, "TicketLink", vinculoId.ToString(),
            $"Vínculo quitado: {descripcion}", ct);

        return (true, $"Vínculo quitado: {descripcion}.");
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    private static List<VinculoDto> FiltrarVinculos(IReadOnlyList<TicketLink> enlaces, string? buscar)
    {
        var texto = (buscar ?? "").Trim();

        return enlaces
            .Where(l => texto.Length == 0
                || Contiene(l.DevOpsTicket.Title, texto)
                || Contiene(l.FreshDeskTicket.Subject, texto)
                || l.DevOpsTicket.ExternalId.ToString().Contains(texto)
                || l.FreshDeskTicket.ExternalId.ToString().Contains(texto)
                || Contiene(l.Notes, texto))
            .Take(TopeDeFilas)
            .Select(l => new VinculoDto(
                l.Id,
                l.DevOpsTicket.ExternalId, l.DevOpsTicket.WorkItemType, l.DevOpsTicket.Title,
                l.DevOpsTicket.State, EnlaceONulo(l.DevOpsTicket.Url),
                l.FreshDeskTicket.ExternalId, l.FreshDeskTicket.Subject,
                FreshDeskTicket.StatusLabel(l.FreshDeskTicket.Status), l.FreshDeskTicket.AgentName,
                EnlaceONulo(l.FreshDeskTicket.Url),
                l.LinkedAt, l.LinkedByUser, l.Notes))
            .ToList();
    }

    private static ListaDeWorkItemsDto ListaDeWorkItems(
        IReadOnlyList<DevOpsTicket> todos, FiltroDeWorkItems filtro, IReadOnlySet<int> vinculados)
    {
        var cumplen = TicketLinkFilter.Aplicar(todos, filtro, vinculados);
        var filas = cumplen.Take(TopeDeFilas).ToList();

        return new ListaDeWorkItemsDto(
            filas.Select(t => new WorkItemParaVincularDto(
                    t.Id, t.ExternalId, t.WorkItemType, t.Title, t.State, t.AssignedTo,
                    vinculados.Contains(t.Id), EnlaceONulo(t.Url)))
                .ToList(),
            TicketLinkFilter.Resumen(
                cumplen.Count, todos.Count, todos.Count(t => !vinculados.Contains(t.Id)), "work item"),
            Truncada: cumplen.Count > filas.Count,
            TicketLinkFilter.Opciones(todos.Select(t => t.State)),
            TicketLinkFilter.Opciones(todos.Select(t => t.WorkItemType)),
            TicketLinkFilter.Opciones(todos.Select(t => t.AssignedTo)));
    }

    private static ListaDeTicketsDto ListaDeTickets(
        IReadOnlyList<FreshDeskTicket> todos, FiltroDeTickets filtro, IReadOnlySet<int> vinculados)
    {
        var cumplen = TicketLinkFilter.Aplicar(todos, filtro, vinculados);
        var filas = cumplen.Take(TopeDeFilas).ToList();

        return new ListaDeTicketsDto(
            filas.Select(t => new TicketParaVincularDto(
                    t.Id, t.ExternalId, t.Subject,
                    FreshDeskTicket.StatusLabel(t.Status), FreshDeskTicket.PriorityLabel(t.Priority),
                    t.AgentName, vinculados.Contains(t.Id), EnlaceONulo(t.Url)))
                .ToList(),
            TicketLinkFilter.Resumen(
                cumplen.Count, todos.Count, todos.Count(t => !vinculados.Contains(t.Id)), "ticket"),
            Truncada: cumplen.Count > filas.Count,
            TicketLinkFilter.Opciones(todos.Select(t => FreshDeskTicket.StatusLabel(t.Status))),
            TicketLinkFilter.Opciones(todos.Select(t => FreshDeskTicket.PriorityLabel(t.Priority))),
            TicketLinkFilter.Opciones(todos.Select(t => t.AgentName)));
    }

    private static bool Contiene(string? valor, string texto) =>
        valor != null && valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Una URL vacía se manda como nula: así la pantalla decide con una sola pregunta si
    /// pinta el enlace, en vez de tener que distinguir entre cadena vacía y nulo.</summary>
    private static string? EnlaceONulo(string? url) => string.IsNullOrWhiteSpace(url) ? null : url;
}
