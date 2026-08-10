using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las preferencias de interfaz de cada persona: qué columnas oculta en una rejilla, qué filtro deja
/// puesto.
///
/// En el escritorio esto vivía en un archivo por máquina, con la consecuencia de que quien cambiaba
/// de computadora perdía su configuración. Aquí van en la base y siguen a la persona, que es lo que
/// se espera de una web.
///
/// Nunca reciben un id de usuario por parámetro: cada quien lee y escribe las suyas. Un id por
/// parámetro, aunque hoy lo protegiera una comprobación, basta que un llamador futuro lo pase mal
/// para acabar leyendo —o pisando— la configuración de otra persona.
/// </summary>
public class UserPreferencesService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Lo guardado para una clave, o null si esa persona nunca la configuró.</summary>
    public async Task<string?> LeerAsync(string clave, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return null;

        return await db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && p.Clave == clave)
            .Select(p => p.Json)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Guarda (o reemplaza) una preferencia. Un valor vacío la BORRA en lugar de guardar una fila
    /// sin contenido: «volví a la configuración de siempre» y «tengo una configuración que resulta
    /// ser la de siempre» son lo mismo para quien lo usa, y guardar la segunda haría que una columna
    /// nueva no le apareciera nunca.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarAsync(
        string clave, string? json, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        clave = (clave ?? "").Trim();
        if (clave.Length == 0) return (false, "Falta la clave de la preferencia.");
        if (clave.Length > 100) return (false, "La clave no puede pasar de 100 caracteres.");

        var fila = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Clave == clave, ct);

        if (string.IsNullOrWhiteSpace(json))
        {
            if (fila == null) return (true, "No había nada que borrar.");
            db.UserPreferences.Remove(fila);
            await db.SaveChangesAsync(ct);
            return (true, "Preferencia restablecida.");
        }

        if (fila == null)
        {
            fila = new UserPreference { UserId = userId, Clave = clave };
            db.UserPreferences.Add(fila);
        }

        fila.Json = json;
        await db.SaveChangesAsync(ct);
        return (true, "Preferencia guardada.");
    }
}
