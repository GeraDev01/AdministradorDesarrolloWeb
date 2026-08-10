using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Security;

/// <summary>
/// Identidad de solo lectura de quien hace la petición.
///
/// Es la misma abstracción que el escritorio dejó escrita anticipando este port («una futura
/// versión web la implemente por-request desde los claims»); aquí esa implementación existe y es
/// <c>ClaimsCurrentUser</c>, registrada como scoped.
///
/// UNA DIFERENCIA DELIBERADA con la versión de escritorio: allí la interfaz exponía también la
/// entidad <c>User</c> completa, porque el singleton la tenía cargada en memoria de todos modos.
/// Aquí no se expone: rehidratarla en cada petición sería una consulta a la base por request
/// —cobrada incluso cuando nadie la usa— y tener una entidad EF colgando de la identidad invita a
/// modificarla desde cualquier parte. Los servicios que necesiten la fila la cargan por
/// <see cref="UserId"/> desde su propio contexto.
/// </summary>
public interface ICurrentUser
{
    int? UserId { get; }
    string? Username { get; }

    /// <summary>Nombre para mostrar y para estampar en la bitácora.</summary>
    string? FullName { get; }

    UserRole? Role { get; }

    /// <summary>Ficha de desarrollador ligada a la cuenta, si la tiene.</summary>
    int? DeveloperId { get; }

    bool IsLoggedIn { get; }
    bool IsAdmin { get; }
    bool IsDesarrollador { get; }

    /// <summary>
    /// Rol operativo. Se comprueba de forma POSITIVA y no por descarte: en el escritorio
    /// «Operaciones» fue una vez «ni admin ni desarrollador», y con eso un rol nuevo habría caído
    /// ahí en silencio heredando sus permisos.
    /// </summary>
    bool IsOperaciones { get; }
}
