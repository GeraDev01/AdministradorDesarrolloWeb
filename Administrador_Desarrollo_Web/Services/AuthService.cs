using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly AuditService _audit;

    public AuthService(AppDbContext db, CurrentUserContext currentUser, AuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>
    /// Públicas porque las pantallas explican la regla al usuario («5 intentos / 15 minutos») y
    /// tenerla escrita a mano en la UI garantizaba que un día dejaran de coincidir.
    /// </summary>
    public const int MaxFailedAttempts = 5;
    public const int LockoutMinutes = 15;

    /// <summary>¿La cuenta está bloqueada AHORA por intentos fallidos?</summary>
    public static bool EstaBloqueado(User u) => u.LockoutUntil is DateTime until && until > DateTime.UtcNow;

    public (bool success, string message, User? user) Login(string username, string password)
    {
        var user = _db.Users
            .FirstOrDefault(u => u.Username == username && u.IsActive);

        if (user == null)
            return (false, "Usuario o contraseña incorrectos.", null);

        // Bloqueo temporal por intentos fallidos. Se relee de la base: el AppDbContext es Singleton
        // y la entidad rastreada podría traer un bloqueo ya vencido —o ya levantado por el
        // administrador desde otro equipo— y dejar fuera a quien sí puede entrar.
        _db.Entry(user).Reload();
        if (EstaBloqueado(user))
            return (false, $"Cuenta bloqueada temporalmente por intentos fallidos. " +
                           $"Reintenta a las {user.LockoutUntil!.Value.ToLocalTime():HH:mm} o pide al líder que la desbloquee.", null);

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginCount = 0;
                _audit.Record(AuditAction.Login, "User", user.Id.ToString(), $"Cuenta bloqueada {LockoutMinutes} min por {MaxFailedAttempts} intentos fallidos");
            }
            _db.SaveChanges();
            return (false, "Usuario o contraseña incorrectos.", null);
        }

        // Éxito: limpiar contadores de bloqueo si los hubiera.
        if (user.FailedLoginCount != 0 || user.LockoutUntil != null)
        {
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            _db.SaveChanges();
        }

        _currentUser.SetUser(user);
        _audit.Record(AuditAction.Login, "User", user.Id.ToString(), "Login exitoso desde la aplicación.");
        return (true, "OK", user);
    }

    public void Logout()
    {
        if (_currentUser.IsLoggedIn)
            _audit.Record(AuditAction.Logout, "User", _currentUser.User!.Id.ToString());
        _currentUser.Clear();
    }

    public (bool success, string message) ChangePassword(int userId, string newPassword)
    {
        if (newPassword.Length < 8)
            return (false, "La contraseña debe tener al menos 8 caracteres.");

        var user = _db.Users.Find(userId);
        if (user == null) return (false, "Usuario no encontrado.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        _db.SaveChanges();

        _audit.Record(AuditAction.PasswordChange, "User", userId.ToString());
        return (true, "Contraseña actualizada.");
    }

    /// <summary>
    /// Crea el usuario administrador inicial si aún no hay usuarios. Devuelve la contraseña
    /// TEMPORAL generada (aleatoria) para mostrarla una sola vez en el primer arranque, o null
    /// si ya existían usuarios. Reemplaza la antigua constante 'Admin@123' hardcodeada.
    /// </summary>
    public string? SeedAdmin()
    {
        if (_db.Users.Any()) return null;

        var temp = GenerateTempPassword();
        var admin = new User
        {
            Username = "admin",
            FullName = "Líder",
            PasswordHash = PasswordHasher.Hash(temp),
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(admin);
        _db.SaveChanges();
        return temp;
    }

    /// <summary>
    /// AsNoTracking: el AppDbContext es Singleton y esta lista alimenta una pantalla que se
    /// recarga sola. Con entidades rastreadas, el estado de bloqueo se quedaba pegado al de la
    /// primera carga aunque otro equipo lo hubiera cambiado, y era justo el dato que el
    /// administrador viene a consultar aquí.
    /// </summary>
    public List<User> GetAllUsers() =>
        _db.Users.Include(u => u.Developer).OrderBy(u => u.FullName).AsNoTracking().ToList();

    /// <summary>
    /// Levanta el bloqueo por intentos fallidos de una cuenta. Sin esto el bloqueo solo caducaba
    /// por tiempo: quien se equivocaba 5 veces quedaba fuera 15 minutos aunque el administrador
    /// estuviera a un lado y pudiera confirmar su identidad.
    ///
    /// Limpia también el contador de intentos, no solo la fecha: dejarlo en 4 haría que el
    /// siguiente error volviera a bloquear la cuenta de inmediato, que es lo contrario de
    /// desbloquearla. NO toca la contraseña — para eso está <see cref="ResetPassword"/>.
    /// </summary>
    public (bool success, string message) DesbloquearCuenta(int userId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var user = _db.Users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return (false, "Usuario no encontrado.");

        // El bloqueo lo escribe el equipo donde falló el login, no este: lo rastreado está viejo.
        _db.Entry(user).Reload();

        bool estaba = EstaBloqueado(user);
        if (!estaba && user.FailedLoginCount == 0)
            return (false, $"«{user.Username}» no está bloqueado. No hay nada que levantar.");

        var hasta = user.LockoutUntil;
        user.LockoutUntil = null;
        user.FailedLoginCount = 0;
        _db.SaveChanges();

        _audit.Record(AuditAction.Update, "User", user.Id.ToString(),
            estaba
                ? $"Bloqueo por intentos fallidos levantado por el líder (vencía {hasta!.Value.ToLocalTime():dd/MM/yyyy HH:mm})"
                : $"Contador de intentos fallidos reiniciado por el líder");

        return (true, estaba
            ? $"Cuenta «{user.Username}» desbloqueada. Ya puede iniciar sesión."
            : $"Contador de intentos de «{user.Username}» reiniciado.");
    }

    public (bool success, string message) CreateUser(User user, string password)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        if (_db.Users.Any(u => u.Username == user.Username))
            return (false, $"El usuario '{user.Username}' ya existe.");

        if (password.Length < 8)
            return (false, "La contraseña debe tener al menos 8 caracteres.");

        if (user.Role == UserRole.Desarrollador && user.DeveloperId == null)
            return (false, "Un usuario Desarrollador debe tener un desarrollador vinculado.");

        user.PasswordHash = PasswordHasher.Hash(password);
        user.CreatedAt = DateTime.UtcNow;
        _db.Users.Add(user);
        _db.SaveChanges();

        _audit.Record(AuditAction.Create, "User", user.Id.ToString(), $"Usuario creado: {user.Username}");
        return (true, "Usuario creado correctamente.");
    }

    public (bool success, string message) UpdateUser(User user)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var existing = _db.Users.Find(user.Id);
        if (existing == null) return (false, "Usuario no encontrado.");

        if (_db.Users.Any(u => u.Username == user.Username && u.Id != user.Id))
            return (false, $"El usuario '{user.Username}' ya existe.");

        if (user.Role == UserRole.Desarrollador && user.DeveloperId == null)
            return (false, "Un usuario Desarrollador debe tener un desarrollador vinculado.");

        existing.Username = user.Username;
        existing.FullName = user.FullName;
        existing.Role = user.Role;
        existing.IsActive = user.IsActive;
        existing.DeveloperId = user.DeveloperId;
        _db.SaveChanges();

        _audit.Record(AuditAction.Update, "User", user.Id.ToString(), $"Usuario actualizado: {user.Username}");
        return (true, "Usuario actualizado.");
    }

    /// <summary>
    /// Cuántos registros del histórico quedarían con una atribución huérfana si se elimina el
    /// usuario. Son campos sueltos (AssignedByUserId, ReviewedById, StartedById…), no llaves
    /// foráneas, así que la base no lo impide: por eso hay que contarlo y avisar antes.
    /// </summary>
    public int ContarReferencias(int userId) =>
        _db.PointEntries.Count(p => p.AssignedByUserId == userId || p.ReviewedByUserId == userId)
      + _db.TeamPointEntries.Count(p => p.AssignedByUserId == userId)
      + _db.VacationRequests.Count(v => v.ReviewedById == userId)
      + _db.VacationDocuments.Count(d => d.SignedByUserId == userId)
      + _db.RequirementAttachments.Count(a => a.UploadedByUserId == userId)
      + _db.Minutes.Count(m => m.CreatedById == userId)
      + _db.AppReleases.Count(r => r.CreatedById == userId)
      + _db.DeploymentJobs.Count(j => j.StartedById == userId)
      + _db.ScheduledDeployments.Count(s => s.CreatedByUserId == userId)
      + _db.SlaCommitments.Count(s => s.CreatedByUserId == userId)
      + _db.TeamRotations.Count(r => r.RotatedByUserId == userId);

    /// <summary>
    /// Elimina una cuenta de usuario. Solo un administrador, y solo cuentas que NO sean de
    /// administrador: quitarle el acceso a otro admin es un cambio de gobierno de la aplicación
    /// que no debe poder hacerse desde una lista con un clic, y evita además que alguien elimine
    /// al último administrador y deje el sistema sin quien lo gestione.
    ///
    /// La bitácora conserva el nombre del usuario (AuditLog guarda UserName además del id), así
    /// que el historial sigue siendo legible después de borrarlo.
    /// </summary>
    public (bool success, string message) DeleteUser(int userId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var user = _db.Users.Find(userId);
        if (user == null) return (false, "Usuario no encontrado.");

        if (user.Id == _currentUser.UserId)
        {
            _audit.RecordDenied(AuditAction.Delete, "User", userId.ToString(),
                "Intento de eliminar la propia cuenta.");
            return (false, "No puedes eliminar tu propia cuenta.");
        }

        if (user.Role == UserRole.Admin)
        {
            _audit.RecordDenied(AuditAction.Delete, "User", userId.ToString(),
                $"Intento de eliminar la cuenta de líder «{user.Username}».");
            return (false,
                $"«{user.Username}» es líder y no se puede eliminar. " +
                "Cámbiale el rol primero si de verdad quieres darlo de baja.");
        }

        int referencias = ContarReferencias(userId);
        var instantanea = new
        {
            user.Id, user.Username, user.FullName, user.Role,
            user.DeveloperId, user.IsActive, user.CreatedAt
        };

        _db.Users.Remove(user);
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Delete, "User", userId.ToString(),
            $"Usuario eliminado: {user.Username} ({user.Role})"
            + (referencias > 0 ? $" — {referencias} registro(s) del histórico quedaron sin atribución." : ""),
            AuditOutcome.Exito, oldValues: instantanea);

        return (true, referencias == 0
            ? $"Usuario «{user.Username}» eliminado."
            : $"Usuario «{user.Username}» eliminado. {referencias} registro(s) del histórico quedaron sin atribuir a nadie.");
    }

    public (bool success, string message) ResetPassword(int userId, string newPassword)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        if (newPassword.Length < 8)
            return (false, "La contraseña debe tener al menos 8 caracteres.");

        var user = _db.Users.Find(userId);
        if (user == null) return (false, "Usuario no encontrado.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        _db.SaveChanges();

        _audit.Record(AuditAction.PasswordChange, "User", userId.ToString(), "Contraseña restablecida por admin");
        return (true, "Contraseña restablecida. El usuario deberá cambiarla al iniciar sesión.");
    }

    /// <summary>¿El desarrollador ya tiene una cuenta de acceso? Devuelve el username si existe.</summary>
    public string? GetDeveloperAccountUsername(int developerId) =>
        _db.Users.Where(u => u.DeveloperId == developerId).Select(u => u.Username).FirstOrDefault();

    /// <summary>Ids de desarrolladores que ya tienen cuenta de acceso.</summary>
    public HashSet<int> GetDeveloperIdsWithAccount() =>
        _db.Users.Where(u => u.DeveloperId != null).Select(u => u.DeveloperId!.Value).ToHashSet();

    /// <summary>
    /// Crea la cuenta de acceso (rol Desarrollador) de un desarrollador con un username
    /// único y una contraseña temporal que deberá cambiar al primer inicio de sesión.
    /// </summary>
    public (bool success, string message, bool alreadyExisted, string? username, string? tempPassword)
        ProvisionDeveloperAccount(int developerId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var dev = _db.Developers.Find(developerId);
        if (dev == null) return (false, "Desarrollador no encontrado.", false, null, null);

        var existing = _db.Users.FirstOrDefault(u => u.DeveloperId == developerId);
        if (existing != null)
            return (false, $"'{dev.FullName}' ya tiene la cuenta '{existing.Username}'.", true, existing.Username, null);

        string username = GenerateUniqueUsername(dev);
        string temp = GenerateTempPassword();

        var user = new User
        {
            Username = username,
            FullName = dev.FullName,
            Role = UserRole.Desarrollador,
            DeveloperId = dev.Id,
            IsActive = true,
            MustChangePassword = true,
            PasswordHash = PasswordHasher.Hash(temp),
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "User", user.Id.ToString(), $"Cuenta de acceso creada para {dev.FullName} ({username})");
        return (true, "Cuenta creada.", false, username, temp);
    }

    /// <summary>Restablece la contraseña de la cuenta de un desarrollador y devuelve la nueva temporal.</summary>
    public (bool success, string message, string? username, string? tempPassword)
        ResetDeveloperAccountPassword(int developerId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var user = _db.Users.FirstOrDefault(u => u.DeveloperId == developerId);
        if (user == null) return (false, "El desarrollador no tiene cuenta de acceso.", null, null);

        string temp = GenerateTempPassword();
        user.PasswordHash = PasswordHasher.Hash(temp);
        user.MustChangePassword = true;
        _db.SaveChanges();
        _audit.Record(AuditAction.PasswordChange, "User", user.Id.ToString(), $"Contraseña de acceso restablecida para {user.Username}");
        return (true, "Contraseña restablecida.", user.Username, temp);
    }

    private string GenerateUniqueUsername(Developer dev)
    {
        string baseName = !string.IsNullOrWhiteSpace(dev.Email) && dev.Email.Contains('@')
            ? dev.Email[..dev.Email.IndexOf('@')]
            : dev.FullName;
        baseName = NormalizeUsername(baseName);
        if (string.IsNullOrEmpty(baseName)) baseName = "dev";

        string candidate = baseName;
        int n = 1;
        while (_db.Users.Any(u => u.Username == candidate))
            candidate = $"{baseName}{++n}";
        return candidate;
    }

    /// <summary>Convierte un nombre/correo en un username ascii: minúsculas, sin acentos, [a-z0-9._-].</summary>
    private static string NormalizeUsername(string s)
    {
        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch is ' ' or '.' or '_' or '-') sb.Append('.');
        }
        var result = sb.ToString();
        while (result.Contains("..")) result = result.Replace("..", ".");
        return result.Trim('.');
    }

    /// <summary>Contraseña temporal robusta (12 chars, mezcla de clases, sin caracteres ambiguos).</summary>
    private static string GenerateTempPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";  // sin I, O
        const string lower = "abcdefghijkmnpqrstuvwxyz";  // sin l, o
        const string digits = "23456789";                 // sin 0, 1
        const string symbols = "@#$%*+=?";
        const string all = upper + lower + digits + symbols;

        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
        while (chars.Count < 12) chars.Add(Pick(all));
        for (int i = chars.Count - 1; i > 0; i--)   // barajar (Fisher–Yates)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string([.. chars]);

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }
}
