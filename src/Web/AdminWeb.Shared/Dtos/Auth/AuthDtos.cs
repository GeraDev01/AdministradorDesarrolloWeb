using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Auth;

/// <summary>Lo que se manda al iniciar sesión.</summary>
public record LoginRequest(string Usuario, string Contrasena);

/// <summary>
/// Quién está dentro. Es lo ÚNICO que el navegador sabe de la cuenta: ni el hash, ni el sello de
/// sesión, ni los contadores de bloqueo salen de aquí. El menú se pinta con esto.
/// </summary>
public record UsuarioSesionDto(
    int Id,
    string Usuario,
    string NombreCompleto,
    UserRole Rol,
    int? DeveloperId,
    bool DebeCambiarContrasena);

/// <summary>Cambio de contraseña propio (el obligatorio del primer ingreso usa el mismo).</summary>
public record CambioContrasenaRequest(string NuevaContrasena, string Confirmacion);
