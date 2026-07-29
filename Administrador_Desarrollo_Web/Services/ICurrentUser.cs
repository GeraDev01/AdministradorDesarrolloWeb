using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Identidad de solo lectura del usuario de la sesión. La abstracción permite que
/// el escritorio la implemente con un singleton en memoria (CurrentUserContext) y
/// una futura versión web la implemente por-request desde los claims. Los servicios
/// que necesiten conocer/validar la identidad dependen de esta interfaz, no de la
/// implementación concreta.
/// </summary>
public interface ICurrentUser
{
    User? User { get; }
    int? UserId { get; }
    string? Username { get; }
    UserRole? Role { get; }
    int? DeveloperId { get; }
    bool IsLoggedIn { get; }
    bool IsAdmin { get; }
    bool IsDesarrollador { get; }

    /// <summary>
    /// Rol operativo. Se comprueba de forma POSITIVA y no por descarte: antes "Operaciones" era
    /// simplemente "ni admin ni desarrollador", así que un rol nuevo habría caído ahí en silencio
    /// heredando sus permisos.
    /// </summary>
    bool IsOperaciones { get; }
}
