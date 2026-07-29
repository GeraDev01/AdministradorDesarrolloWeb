using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Consulta reutilizable «los tickets de DevOps de un desarrollador», empatando por identidad
/// (<see cref="DevOpsIdentityMatcher"/>). La usan tanto la pantalla «Mis tickets DevOps» como el
/// panel del desarrollador, para no duplicar la lógica de empate.
/// </summary>
public static class DevOpsTicketQuery
{
    /// <summary>Todos los tickets sincronizados que corresponden al desarrollador (por correo/nombre).</summary>
    public static List<DevOpsTicket> ForDeveloper(AppDbContext db, Developer dev)
    {
        // Identidades distintas presentes en los tickets (decenas, no miles): se evalúa el empate
        // una sola vez por identidad y luego se traen solo los tickets de las que coinciden.
        var idents = db.DevOpsTickets
            .Select(t => new { t.AssignedTo, t.AssignedToUniqueName })
            .Distinct()
            .ToList();

        var displays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in idents)
            if (DevOpsIdentityMatcher.Matches(id.AssignedTo, id.AssignedToUniqueName, dev))
            {
                if (!string.IsNullOrWhiteSpace(id.AssignedTo)) displays.Add(id.AssignedTo);
                if (!string.IsNullOrWhiteSpace(id.AssignedToUniqueName)) emails.Add(id.AssignedToUniqueName);
            }

        if (displays.Count == 0 && emails.Count == 0) return [];

        return db.DevOpsTickets
            .Where(t => emails.Contains(t.AssignedToUniqueName!) || displays.Contains(t.AssignedTo))
            .OrderByDescending(t => t.UpdatedAtExternal)
            .ToList();
    }
}
