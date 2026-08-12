using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que se escribe AQUÍ cuando Azure DevOps ya aceptó una prioridad para un work item: el ticket
/// local, el requerimiento si llegó a materializarse y el compromiso de SLA que dicta esa prioridad.
///
/// <para><b>Por qué vive fuera de los dos servicios que lo usan.</b> La prioridad de un work item se
/// puede empujar desde dos sitios: la pantalla de tickets
/// (<see cref="DevOpsService.CambiarPrioridadAsync"/>) y el vínculo del pool
/// (<see cref="PoolDevOpsService.EmpujarAsync"/>). Durante un tiempo solo el primero reajustaba el
/// compromiso; el empuje del pool escribía en DevOps y nada más, así que la fila del ticket se
/// corregía sola en la siguiente sincronización pero el SLA seguía corriendo con el plazo de la
/// prioridad VIEJA — que es la peor forma de fallar, porque el aviso sigue llegando y llega tarde.
/// Copiar el reajuste al segundo camino habría dejado dos copias condenadas a separarse otra vez;
/// con una sola, subir un ticket a «muy alta» significa lo mismo se haga desde donde se haga.</para>
///
/// <para><b>Es estática y recibe el contexto</b> por lo mismo que
/// <see cref="PoolDevOpsService.NadieMasLoTieneAsync"/>: el vínculo del pool no depende de
/// <see cref="DevOpsService"/> —no necesita su sincronización, su importación ni sus avisos— y
/// obligarle a arrastrarlo entero para reajustar un compromiso sería pagar de más por compartir.</para>
///
/// <para><b>Solo se llama cuando DevOps YA aceptó el cambio.</b> Si la escritura de allá falla, aquí
/// no se toca nada: recalcular el plazo sobre una prioridad que el work item nunca llegó a tener
/// pondría a correr un compromiso que nadie pidió y que en DevOps no se sostiene.</para>
/// </summary>
public static class ReconciliacionDePrioridadDeDevOps
{
    /// <summary>
    /// Refleja aquí la prioridad que DevOps acaba de aceptar y guarda: el ticket, el requerimiento y
    /// el compromiso, todo en el MISMO <c>SaveChanges</c>, para que no exista un instante con la
    /// prioridad nueva y el plazo viejo.
    ///
    /// <para><paramref name="porUsuarioId"/> es quien provocó el cambio en esta petición —quien
    /// pulsó en la pantalla de tickets, o quien publicó, editó o tomó la actividad del pool—. Queda
    /// en <c>PriorityConfirmedAt</c>/<c>PriorityConfirmedByUserId</c>, que es lo que distingue una
    /// prioridad PENSADA del 2 que DevOps le pone por omisión a todo.</para>
    ///
    /// <para>Si el work item no está sincronizado aquí no hay nada que reflejar, y no es un error: el
    /// pool liga por NÚMERO y escribe con normalidad en tickets que esta base todavía no conoce.</para>
    ///
    /// <para><b>Se puede llamar sin que la prioridad haya cambiado</b> —el empuje del pool manda la
    /// suya cada vez que la marca de agua no coincide, aunque el work item ya estuviera en ella— y por
    /// eso un plazo que ya corre solo se mueve cuando el cambio es real. Lo detalla
    /// <see cref="ReajustarCompromisoAsync"/>.</para>
    /// </summary>
    public static async Task ReconciliarAsync(
        AppDbContext db, SettingsService configuracion, int? porUsuarioId,
        int numero, int prioridad, CancellationToken ct)
    {
        var ticket = await db.DevOpsTickets.FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket == null) return;

        // Escribir la prioridad y CAMBIARLA no son lo mismo, y hay que saber cuál de las dos fue
        // antes de pisar el campo. Solo importa para el plazo: lo explica ReajustarCompromisoAsync.
        bool cambio = !YaEstabaEn(ticket.Priority, prioridad);

        // DevOps aceptó el valor: el autoritativo es el que se envió. La respuesta lo trae como
        // número y leerlo como texto daba vacío, así que no se relee.
        ticket.Priority = prioridad.ToString();
        ticket.PriorityConfirmedAt = DateTime.UtcNow;
        ticket.PriorityConfirmedByUserId = porUsuarioId;

        // Se refleja en el requerimiento local si el ticket ya se materializó como tal. El mapeo es
        // inverso: en DevOps 1 es lo más urgente y en el catálogo local lo más urgente es el valor
        // más alto.
        var identificador = numero.ToString();
        var requerimiento = await db.Requirements.FirstOrDefaultAsync(
            r => r.ExternalId == identificador && r.Source == RequirementSource.AzureDevOps, ct);
        if (requerimiento != null)
        {
            requerimiento.Priority = (RequirementPriority)(4 - prioridad);
            await ReajustarCompromisoAsync(db, configuracion, requerimiento, ticket, cambio, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Si el ticket ya venía con esa misma prioridad. Se compara como NÚMERO y con la misma tolerancia
    /// que <see cref="SlaPolicyStore.Resolver"/> —el campo de DevOps es texto—, y lo que no sea un
    /// número se cuenta como distinto, que es lo cierto: pasar de «sin prioridad» a una de verdad sí
    /// es un cambio.
    /// </summary>
    private static bool YaEstabaEn(string? prioridadDelTicket, int prioridad) =>
        int.TryParse((prioridadDelTicket ?? "").Trim(), out var actual) && actual == prioridad;

    /// <summary>
    /// Reajusta el compromiso del requerimiento a la política de la prioridad que su ticket acaba de
    /// estrenar. NO guarda: lo hace <see cref="ReconciliarAsync"/>, en el mismo guardado que el
    /// cambio de prioridad.
    ///
    /// <para>El compromiso es de quien tiene el ticket AHORA: se empata por identidad contra el
    /// asignado del work item y no contra una fila de asignación que pudo quedar de un dueño
    /// anterior. Como respaldo, la asignación local más reciente.</para>
    ///
    /// <para><paramref name="cambioDePrioridad"/> dice si la prioridad de verdad cambió, y es lo que
    /// decide si un plazo que YA CORRE se puede mover.</para>
    /// </summary>
    private static async Task ReajustarCompromisoAsync(
        AppDbContext db, SettingsService configuracion,
        Requirement requerimiento, DevOpsTicket ticket, bool cambioDePrioridad, CancellationToken ct)
    {
        var politicas = await PoliticasAsync(configuracion, ct);
        if (SlaPolicyStore.Resolver(politicas, ticket.Priority) is not SlaPolicy politica) return;

        var ahora = DateTime.UtcNow;

        var vigente = await db.SlaCommitments.FirstOrDefaultAsync(
            s => s.RequirementId == requerimiento.Id && s.Status == SlaStatus.Activo, ct);

        if (vigente != null)
        {
            // UN PLAZO QUE YA CORRE SOLO SE MUEVE SI LA PRIORIDAD CAMBIÓ. Aquí no siempre se llega
            // desde un cambio: el empuje del pool manda la prioridad cada vez que su marca de agua no
            // coincide —al publicar la actividad, al ligarla, al reintentar—, y en esos casos el work
            // item se queda casi siempre en la prioridad que ya tenía. Reprogramar entonces le
            // regalaría al compromiso el plazo entero desde cero, y encima con una nota que habla de
            // un cambio que nunca ocurrió: bastaría desligar y volver a ligar una actividad para
            // perdonar, sin dejar rastro, un SLA a punto de vencer.
            if (!cambioDePrioridad) return;

            var vence = ahora.AddHours(politica.Hours);
            vigente.DueAtUtc = vence;
            vigente.ReminderEveryHours = politica.ReminderEveryHours;
            vigente.NextReminderAtUtc = SlaService.PrimerRecordatorio(ahora, vence, politica.ReminderEveryHours);
            vigente.Notes =
                $"SLA ajustado por cambio a prioridad {politica.Priority} " +
                $"({SlaPolicyStore.NombrePrioridad(politica.Priority)}) del ticket #{ticket.ExternalId}.";
            return;
        }

        // Sin compromiso vigente solo se crea si NUNCA tuvo ninguno, por lo mismo que en la
        // materialización: un SLA cerrado o cancelado se decidió a propósito.
        if (await db.SlaCommitments.AnyAsync(s => s.RequirementId == requerimiento.Id, ct)) return;

        if (await ResponsableDelTicketAsync(db, requerimiento.Id, ticket, ct) is not int developerId) return;
        AgregarSlaAutomatico(db, requerimiento.Id, ticket, politica, developerId, ahora);
    }

    /// <summary>
    /// A quién se le exige el compromiso de un ticket: el asignado actual en DevOps si tiene ficha
    /// y, si no se le encuentra, quien lo tenga asignado aquí desde hace menos.
    /// </summary>
    private static async Task<int?> ResponsableDelTicketAsync(
        AppDbContext db, int requerimientoId, DevOpsTicket ticket, CancellationToken ct)
    {
        var dev = DevOpsIdentityMatcher.Buscar(ticket.AssignedTo, ticket.AssignedToUniqueName,
            await db.Developers.AsNoTracking().Where(d => d.IsActive).ToListAsync(ct));
        if (dev != null) return dev.Id;

        return await db.Assignments.AsNoTracking()
            .Where(a => a.RequirementId == requerimientoId)
            .OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.Id)
            .Select(a => (int?)a.DeveloperId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Añade —sin guardar— el compromiso que dicta una política. Reutiliza la MISMA regla de primer
    /// recordatorio que los SLA que asigna el líder a mano: dos formas de calcularlo acabarían
    /// separándose, y la diferencia solo se notaría el día que un aviso llegara tarde.
    ///
    /// <para>Es pública porque la materialización de los tickets propios pone el mismo compromiso a
    /// lo que acaba de dar de alta, y tiene que ser exactamente éste.</para>
    /// </summary>
    public static void AgregarSlaAutomatico(
        AppDbContext db, int requerimientoId, DevOpsTicket ticket, SlaPolicy politica,
        int developerId, DateTime ahora)
    {
        var vence = ahora.AddHours(politica.Hours);

        db.SlaCommitments.Add(new SlaCommitment
        {
            RequirementId = requerimientoId,
            DeveloperId = developerId,
            DevOpsTicketExternalId = ticket.ExternalId,
            DevOpsTicketUrl = string.IsNullOrWhiteSpace(ticket.Url) ? null : ticket.Url,
            DueAtUtc = vence,
            ReminderEveryHours = politica.ReminderEveryHours,
            NextReminderAtUtc = SlaService.PrimerRecordatorio(ahora, vence, politica.ReminderEveryHours),
            Status = SlaStatus.Activo,
            Notes =
                $"SLA automático por prioridad {politica.Priority} " +
                $"({SlaPolicyStore.NombrePrioridad(politica.Priority)}) del ticket #{ticket.ExternalId}.",
            CreatedAt = ahora
        });
    }

    /// <summary>Las políticas que el líder tenga guardadas, o las de por omisión —todas
    /// desactivadas— si no ha tocado ninguna.</summary>
    public static async Task<List<SlaPolicy>> PoliticasAsync(
        SettingsService configuracion, CancellationToken ct) =>
        SlaPolicyStore.Parse(await configuracion.ObtenerAsync(SlaPolicyStore.ClaveDeConfiguracion, ct));
}
