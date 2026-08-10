using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Cuenta de acceso. Es la MISMA tabla que usa el escritorio: los hashes BCrypt existentes siguen
/// validando, así que nadie tiene que cambiar su contraseña por la mudanza a la web.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Operaciones;
    public int? DeveloperId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Anti-fuerza-bruta: intentos fallidos consecutivos y bloqueo temporal.
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }

    /// <summary>
    /// Sello que invalida las sesiones abiertas. NO existe en el escritorio y es propio de la web:
    /// allí la sesión moría con el proceso, aquí vive en una cookie que puede durar horas. Al
    /// cambiar la contraseña o desactivar la cuenta se renueva el sello y la cookie deja de valer,
    /// que es la única forma de echar a alguien de verdad sin esperar a que expire.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public Developer? Developer { get; set; }
}
