namespace Administrador_Desarrollo_Web.Models;

public enum UserRole { Admin = 0, Operaciones = 1, Desarrollador = 2 }

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

    public Developer? Developer { get; set; }
}
