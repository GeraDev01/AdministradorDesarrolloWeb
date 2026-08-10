using System.Security.Cryptography;
using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Resultado de un intento de acceso. Quien lo llama decide qué hacer con el usuario.</summary>
public record ResultadoLogin(bool Exito, string Mensaje, User? Usuario);

/// <summary>
/// Acceso y cuentas.
///
/// Copiado del escritorio con TRES cambios de fondo, todos consecuencia de que aquí la verificación
/// ocurre en el servidor y no en la máquina de quien entra:
///
/// 1. <see cref="LoginAsync"/> ya NO fija la sesión como efecto colateral. Devuelve el usuario y es
///    el endpoint quien emite la cookie. El escritorio podía permitírselo porque su
///    <c>CurrentUserContext</c> era un singleton en memoria del proceso del usuario; aquí la
///    identidad la lleva la petición y un servicio no tiene por qué saber cómo se transporta.
/// 2. Al cambiar la contraseña o desbloquear una cuenta se renueva el <c>SecurityStamp</c>, lo que
///    invalida las sesiones abiertas. En el escritorio esto no existía porque la sesión moría con
///    el proceso; en la web una cookie puede sobrevivir horas a un cambio de contraseña.
/// 3. Desaparecen los <c>Entry(user).Reload()</c>: estaban para desconfiar de una entidad rastreada
///    por un contexto Singleton compartido. Con un contexto por petición, lo que se lee ya es
///    fresco por definición.
/// </summary>
public class AuthService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// Públicas porque las pantallas explican la regla al usuario («5 intentos / 15 minutos») y
    /// tenerla escrita a mano en la interfaz garantizaba que un día dejaran de coincidir.
    /// </summary>
    public const int MaxFailedAttempts = 5;
    public const int LockoutMinutes = 15;

    public const int MinPasswordLength = 8;

    /// <summary>¿La cuenta está bloqueada AHORA por intentos fallidos?</summary>
    public static bool EstaBloqueado(User u) => u.LockoutUntil is DateTime until && until > DateTime.UtcNow;

    /// <summary>
    /// Verifica credenciales. El mensaje de fallo es el MISMO para usuario inexistente y para
    /// contraseña incorrecta: distinguirlos le diría a un desconocido qué nombres de usuario existen.
    /// </summary>
    public async Task<ResultadoLogin> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0 || string.IsNullOrEmpty(password))
            return new(false, "Escribe tu usuario y tu contraseña.", null);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user == null)
            return new(false, "Usuario o contraseña incorrectos.", null);

        if (EstaBloqueado(user))
            return new(false, "Cuenta bloqueada temporalmente por intentos fallidos. " +
                              $"Reintenta a las {user.LockoutUntil!.Value.ToLocalTime():HH:mm} o pide al líder que la desbloquee.", null);

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginCount = 0;
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
                    $"Cuenta bloqueada {LockoutMinutes} min por {MaxFailedAttempts} intentos fallidos", ct);
            }
            else await db.SaveChangesAsync(ct);

            return new(false, "Usuario o contraseña incorrectos.", null);
        }

        // Éxito: limpiar contadores de bloqueo si los hubiera.
        if (user.FailedLoginCount != 0 || user.LockoutUntil != null)
        {
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await db.SaveChangesAsync(ct);
        }

        await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(), "Login exitoso.", ct);
        return new(true, "OK", user);
    }

    /// <summary>
    /// Cambia la contraseña e invalida las sesiones abiertas de esa cuenta renovando su sello.
    /// Sin eso, alguien con la cookie robada seguiría dentro después de que la víctima cambiara su
    /// contraseña — que es justo el momento en que se cambia.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ChangePasswordAsync(
        int userId, string newPassword, CancellationToken ct = default)
    {
        if ((newPassword ?? "").Length < MinPasswordLength)
            return (false, $"La contraseña debe tener al menos {MinPasswordLength} caracteres.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.");

        user.PasswordHash = PasswordHasher.Hash(newPassword!);
        user.MustChangePassword = false;
        user.SecurityStamp = NuevoSello();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.PasswordChange, "User", userId.ToString(), ct: ct);
        return (true, "Contraseña actualizada.");
    }

    /// <summary>
    /// El líder asigna una contraseña temporal a otra cuenta. La cuenta queda obligada a cambiarla
    /// al entrar y sus sesiones abiertas se invalidan.
    /// </summary>
    public async Task<(bool ok, string mensaje, string? temporal)> ResetPasswordAsync(
        int userId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.", null);

        var temp = GenerarContrasenaTemporal();
        user.PasswordHash = PasswordHasher.Hash(temp);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.SecurityStamp = NuevoSello();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.PasswordChange, "User", user.Id.ToString(),
            "Contraseña restablecida por el líder", ct);
        return (true, $"Contraseña temporal de «{user.Username}» generada. Se muestra una sola vez.", temp);
    }

    /// <summary>
    /// Levanta el bloqueo por intentos fallidos. Limpia también el contador, no solo la fecha:
    /// dejarlo en 4 haría que el siguiente error volviera a bloquear la cuenta de inmediato, que es
    /// lo contrario de desbloquearla. NO toca la contraseña.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DesbloquearCuentaAsync(int userId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.");

        bool estaba = EstaBloqueado(user);
        if (!estaba && user.FailedLoginCount == 0)
            return (false, $"«{user.Username}» no está bloqueado. No hay nada que levantar.");

        var hasta = user.LockoutUntil;
        user.LockoutUntil = null;
        user.FailedLoginCount = 0;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", user.Id.ToString(),
            estaba
                ? $"Bloqueo por intentos fallidos levantado por el líder (vencía {hasta!.Value.ToLocalTime():dd/MM/yyyy HH:mm})"
                : "Contador de intentos fallidos reiniciado por el líder", ct);

        return (true, estaba
            ? $"Cuenta «{user.Username}» desbloqueada. Ya puede iniciar sesión."
            : $"Contador de intentos de «{user.Username}» reiniciado.");
    }

    /// <summary>
    /// Crea el administrador inicial si no hay ninguna cuenta y devuelve su contraseña TEMPORAL para
    /// mostrarla una sola vez. Devuelve null si ya existían usuarios.
    /// </summary>
    public async Task<string?> SeedAdminAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return null;

        var temp = GenerarContrasenaTemporal();
        db.Users.Add(new User
        {
            Username = "admin",
            FullName = "Líder",
            PasswordHash = PasswordHasher.Hash(temp),
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return temp;
    }

    /// <summary>La cuenta tal como está en la base ahora mismo. La usa la validación de la cookie.</summary>
    public Task<User?> ObtenerAsync(int userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

    private static string NuevoSello() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Contraseña temporal de 12 caracteres SIN los que se confunden al dictarla o copiarla a mano
    /// (0/O, 1/l/I): esta contraseña se lee en voz alta o se manda por chat, y un cero que alguien
    /// teclea como O es un bloqueo de cuenta garantizado.
    /// </summary>
    private static string GenerarContrasenaTemporal()
    {
        const string alfabeto = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789@#$%";
        var sb = new StringBuilder(12);
        var bytes = RandomNumberGenerator.GetBytes(12);
        foreach (var b in bytes) sb.Append(alfabeto[b % alfabeto.Length]);
        return sb.ToString();
    }
}
