using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Implementación de escritorio de <see cref="ICurrentUser"/>: guarda el usuario
/// logueado en memoria (app mono-usuario). AuthService ya no la muta; el login/logout la fija.</summary>
public class CurrentUserContext : ICurrentUser
{
    public User? User { get; private set; }
    public bool IsLoggedIn => User != null;

    /// <summary>
    /// Rol de la sesión: el de la cuenta con la que se inició sesión, y nada más. Es una sola
    /// aplicación para todo el equipo — quien entra como Desarrollador ve el menú de desarrollador
    /// y quien entra como Admin lo ve todo. Todo lo demás (menú de MainForm y las guardas de
    /// <see cref="AuthorizationGuard"/>) se deriva de aquí, para que la visibilidad y los permisos
    /// no puedan discrepar.
    /// </summary>
    public UserRole? Role => User?.Role;

    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsDesarrollador => Role == UserRole.Desarrollador;
    public bool IsOperaciones => Role == UserRole.Operaciones;
    public int? DeveloperId => User?.DeveloperId;
    public int? UserId => User?.Id;
    public string? Username => User?.Username;

    public void SetUser(User user) => User = user;
    public void Clear() => User = null;
}
