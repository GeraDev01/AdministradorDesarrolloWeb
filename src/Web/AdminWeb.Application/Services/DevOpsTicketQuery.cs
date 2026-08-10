using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La consulta reutilizable «los tickets de DevOps de un desarrollador», empatando por identidad
/// (<see cref="DevOpsIdentityMatcher"/>).
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte. La
/// usan la pantalla «Mis tickets DevOps» y la detección de asignaciones nuevas, para no tener dos
/// copias de la regla de empate.</para>
/// </summary>
public static class DevOpsTicketQuery
{
    /// <summary>
    /// Todos los tickets sincronizados que corresponden al desarrollador, el más reciente primero.
    ///
    /// El empate se evalúa una sola vez por IDENTIDAD distinta y no por ticket: en la base hay miles
    /// de tickets pero decenas de asignados, así que comparar nombre a nombre sobre cada fila sería
    /// pagar el mismo cálculo cientos de veces. Es la misma estrategia del escritorio.
    /// </summary>
    public static async Task<List<DevOpsTicket>> DeDesarrolladorAsync(
        AppDbContext db, Developer dev, CancellationToken ct = default)
    {
        var identidades = await db.DevOpsTickets.AsNoTracking()
            .Select(t => new { t.AssignedTo, t.AssignedToUniqueName })
            .Distinct()
            .ToListAsync(ct);

        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var correos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var identidad in identidades)
            if (DevOpsIdentityMatcher.Corresponde(identidad.AssignedTo, identidad.AssignedToUniqueName, dev))
            {
                if (!string.IsNullOrWhiteSpace(identidad.AssignedTo))
                    nombres.Add(identidad.AssignedTo);
                if (!string.IsNullOrWhiteSpace(identidad.AssignedToUniqueName))
                    correos.Add(identidad.AssignedToUniqueName);
            }

        if (nombres.Count == 0 && correos.Count == 0) return [];

        return await db.DevOpsTickets.AsNoTracking()
            .Where(t => correos.Contains(t.AssignedToUniqueName!) || nombres.Contains(t.AssignedTo))
            .OrderByDescending(t => t.UpdatedAtExternal)
            .ToListAsync(ct);
    }
}
