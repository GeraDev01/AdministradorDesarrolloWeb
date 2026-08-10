namespace AdminWeb.Domain.Security;

/// <summary>
/// La sesión actual no tiene permiso para esta operación. Un filtro de la API la traduce a 403,
/// que es exactamente lo que el escritorio anotó que pasaría cuando llegara la web.
/// </summary>
public class AuthorizationException(string message) : Exception(message);

/// <summary>
/// Guardas de autorización en la capa de SERVICIOS.
///
/// Son la SEGUNDA barrera, no la única: la primera son las políticas declaradas en los endpoints.
/// Tener las dos es deliberado — el cliente Blazor se ejecuta en la máquina del usuario y cualquiera
/// puede llamar a la API sin pasar por él, así que la regla tiene que vivir donde está el dato.
/// Es la misma disposición que el escritorio ya tenía entre el menú y los servicios.
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
            throw new AuthorizationException("Esta operación requiere permisos de líder.");
    }

    /// <summary>Permite la operación solo si es el propio desarrollador dueño de los datos o un admin.</summary>
    public static void RequireOwnershipOrAdmin(ICurrentUser user, int developerId)
    {
        if (user.IsAdmin) return;
        if (user.DeveloperId is int mine && mine == developerId) return;
        throw new AuthorizationException("No tienes permiso para operar sobre datos de otro desarrollador.");
    }

    /// <summary>Operación del área de despliegues: Admin u Operaciones. Un desarrollador no despliega.</summary>
    public static void RequireAdminOrOperaciones(ICurrentUser user)
    {
        RequireLoggedIn(user);
        if (user.IsAdmin || user.IsOperaciones) return;
        throw new AuthorizationException("Esta operación es del área de despliegues (Líder u Operaciones).");
    }

    /// <summary>
    /// Admin o Desarrollador. La comprobación es POSITIVA por rol a propósito: escrita por descarte
    /// («no es admin»), Operaciones y cualquier rol futuro heredarían el acceso en silencio — que es
    /// exactamente el error que ya se cometió una vez.
    /// </summary>
    public static void RequireAdminOrDesarrollador(ICurrentUser user, string ambito = "de la biblioteca de plantillas")
    {
        RequireLoggedIn(user);
        if (user.IsAdmin || user.IsDesarrollador) return;
        throw new AuthorizationException($"Esta operación es {ambito} (Líder o Desarrollador).");
    }
}
