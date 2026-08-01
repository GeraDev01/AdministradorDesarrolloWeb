namespace Administrador_Desarrollo_Web.Services;

/// <summary>Se lanza cuando la sesión actual no tiene permiso para una operación. La UI la
/// captura y la muestra como aviso; en la web se traduce a 403.</summary>
public class AuthorizationException(string message) : Exception(message);

/// <summary>
/// Defensa en profundidad: guardas de autorización en la capa de SERVICIOS (no solo en la UI).
/// Hoy la app de escritorio solo gatea por visibilidad de botones; estas guardas aseguran que
/// una operación privilegiada o sobre datos ajenos falle aunque se invoque desde otra ruta,
/// y son la base de la autorización del futuro portal web.
/// </summary>
public static class AuthorizationGuard
{
    public static void RequireLoggedIn(ICurrentUser user)
    {
        if (!user.IsLoggedIn)
            throw new AuthorizationException("Debes iniciar sesión para realizar esta acción.");
    }

    public static void RequireAdmin(ICurrentUser user)
    {
        if (!user.IsAdmin)
            throw new AuthorizationException("Esta operación requiere permisos de administrador.");
    }

    /// <summary>Permite la operación solo si es el propio desarrollador dueño de los datos o un admin.</summary>
    public static void RequireOwnershipOrAdmin(ICurrentUser user, int developerId)
    {
        if (user.IsAdmin) return;
        if (user.DeveloperId is int mine && mine == developerId) return;
        throw new AuthorizationException("No tienes permiso para operar sobre datos de otro desarrollador.");
    }

    /// <summary>
    /// Operación del área de despliegues: la puede hacer Admin u Operaciones, nadie más.
    /// Un desarrollador no despliega.
    /// </summary>
    public static void RequireAdminOrOperaciones(ICurrentUser user)
    {
        RequireLoggedIn(user);
        if (user.IsAdmin || user.IsOperaciones) return;
        throw new AuthorizationException("Esta operación es del área de despliegues (Administrador u Operaciones).");
    }

    /// <summary>
    /// Lectura de la biblioteca de plantillas: Admin o Desarrollador. La comprobación es POSITIVA
    /// por rol a propósito — escrita por descarte («no es admin»), Operaciones y cualquier rol
    /// futuro heredarían la lectura en silencio, que es exactamente el error que ya se cometió una
    /// vez (ver la nota de ICurrentUser).
    /// </summary>
    public static void RequireAdminOrDesarrollador(ICurrentUser user)
    {
        RequireLoggedIn(user);
        if (user.IsAdmin || user.IsDesarrollador) return;
        throw new AuthorizationException("Esta operación es de la biblioteca de plantillas (Administrador o Desarrollador).");
    }
}
