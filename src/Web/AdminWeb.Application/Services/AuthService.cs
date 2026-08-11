using System.Security.Cryptography;
using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Auth;
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
        //
        // PERO SOLO CUANDO ACERTAR LA CONTRASEÑA ES YA HABER ENTRADO, o sea cuando la cuenta no
        // tiene segundo factor. Con segundo factor, limpiarlos aquí desarma el bloqueo por intentos
        // justo contra el atacante para el que existe el segundo factor: quien tiene la contraseña
        // puede pedir un acceso, fallar cuatro códigos, VOLVER a pedir acceso —que le pondría el
        // contador a cero—, fallar otros cuatro, y así sin fin. El quinto fallo nunca llega y los
        // seis dígitos se prueban sin límite. Y peor: como aquí también se borraba LockoutUntil, un
        // acceso correcto levantaba un bloqueo que acababa de saltar.
        //
        // Quien los limpia es quien CIERRA el acceso: SegundoFactorService al aceptar un código o un
        // código de rescate, y LimpiarBloqueoTrasAccesoCompletoAsync en el camino del equipo
        // recordado. Si se vuelve a limpiar aquí, hay que borrar el segundo factor entero, porque
        // deja de proteger.
        if (!user.SegundoFactorActivo && (user.FailedLoginCount != 0 || user.LockoutUntil != null))
        {
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await db.SaveChangesAsync(ct);
        }

        // El apunte distingue los dos finales posibles, y esa distinción es la mitad del valor de la
        // bitácora desde que hay segundo factor: «acertó la contraseña» y «entró» dejaron de ser lo
        // mismo. Una ráfaga de contraseñas acertadas que nunca llegan a una entrada completa es la
        // señal de que alguien tiene credenciales robadas y se está estrellando contra el código.
        //
        // El apunte de la entrada COMPLETA lo pone quien cierra el segundo tramo
        // (<see cref="RegistrarAccesoCompletadoAsync"/>), porque es el único que sabe cómo terminó.
        await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
            user.SegundoFactorActivo
                ? "Contraseña correcta; falta el segundo factor."
                : "Login exitoso.", ct);

        return new(true, "OK", user);
    }

    /// <summary>Las tres formas de pasar el segundo factor. Se escriben tal cual en la bitácora.</summary>
    public static class ComoSePasoElSegundoFactor
    {
        public const string CodigoDelTelefono = "con el código del teléfono";
        public const string CodigoDeRescate = "con un código de rescate";

        /// <summary>
        /// Ni siquiera se pidió: este navegador estaba recordado.
        ///
        /// <para>Que quede escrito importa más de lo que parece. Si en la bitácora una entrada desde
        /// un equipo recordado fuera indistinguible de una con el código tecleado, después de un
        /// incidente no habría forma de saber cuáles fueron accesos con el teléfono delante y cuáles
        /// se apoyaron en una confianza de hace semanas — que es justo lo que hay que revisar.</para>
        /// </summary>
        public const string EquipoRecordado = "desde un equipo recordado, sin pedir código";
    }

    /// <summary>
    /// Deja constancia de que alguien terminó de entrar, y de CÓMO pasó el segundo factor.
    ///
    /// <para>Se pasa el nombre en el texto a propósito: en este punto la cookie todavía no está
    /// puesta, así que la bitácora anotaría «sistema» como autor —igual que ya le pasa al apunte de
    /// la contraseña—. Con el nombre dentro del detalle, el renglón se entiende al leerlo.</para>
    /// </summary>
    public Task RegistrarAccesoCompletadoAsync(User user, string comoPaso, CancellationToken ct = default) =>
        audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
            $"Login exitoso de «{user.Username}» {comoPaso}.", ct);

    /// <summary>
    /// Pone a cero los contadores de bloqueo de quien ACABA DE TERMINAR de entrar.
    ///
    /// <para>Existe porque <see cref="LoginAsync"/> ya no los limpia cuando la cuenta tiene segundo
    /// factor —ahí acertar la contraseña es medio acceso, y limpiarlos permitiría probar códigos sin
    /// límite—. Con el código tecleado los limpia <c>SegundoFactorService</c>; en el único camino que
    /// no pasa por él, el del equipo recordado, los limpia esto. Sin esta llamada, los fallos de un
    /// día se sumarían a los del siguiente hasta bloquear una cuenta que nunca hizo nada raro.</para>
    /// </summary>
    public async Task LimpiarBloqueoTrasAccesoCompletoAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null || (user.FailedLoginCount == 0 && user.LockoutUntil == null)) return;

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await db.SaveChangesAsync(ct);
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
        RotarSelloYDejarDeConfiarEnLosEquipos(user);
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
        RotarSelloYDejarDeConfiarEnLosEquipos(user);
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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EQUIPOS RECORDADOS — los navegadores a los que ya no se les pide el código
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // Están AQUÍ y no en el servicio del segundo factor, aunque de él vengan, por una razón muy
    // concreta: quien los tiene que borrar es el sello de seguridad, y el sello se rota aquí. Tenerlo
    // repartido garantizaría que un día alguien añada una tercera rotación de sello y se olvide de
    // los equipos, que es el fallo que deja viva la puerta de atrás justo cuando se cambia la
    // contraseña porque la robaron.

    /// <summary>
    /// Los navegadores VIGENTES en los que esta persona ya no tiene que teclear el código.
    ///
    /// <para>Los vencidos no se listan aunque su fila siga ahí: la fila se limpia sola cuando alguien
    /// vuelve a usar ese navegador, y mientras tanto enseñarla haría creer que se confía en un equipo
    /// en el que ya no se confía.</para>
    /// </summary>
    public async Task<IReadOnlyList<EquipoRecordadoDto>> EquiposRecordadosAsync(
        int userId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        return await db.UserTrustedDevices.AsNoTracking()
            .Where(d => d.UserId == userId && d.ExpiraEnUtc > ahora)
            .OrderByDescending(d => d.UltimoUsoUtc ?? d.CreatedAtUtc)
            .Select(d => new EquipoRecordadoDto(
                d.Descripcion ?? "Equipo sin identificar",
                d.CreatedAtUtc, d.ExpiraEnUtc, d.UltimoUsoUtc))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Deja de confiar en TODOS los navegadores de una cuenta: el «se me quedó la sesión abierta en
    /// un equipo que ya no es mío» sin tener que molestar al líder.
    ///
    /// <para>Es deliberadamente todo o nada. Un botón por equipo obligaría a distinguirlos, y lo
    /// único que hay para distinguirlos es lo que el propio navegador dice de sí mismo —tres
    /// portátiles con el mismo Chrome en el mismo Windows salen idénticos—. Quien duda de uno acaba
    /// olvidando el que no era y creyendo que ya está a salvo. Olvidarlos todos cuesta un código de
    /// más la próxima vez en cada equipo y no deja lugar a la duda.</para>
    /// </summary>
    public async Task<int> OlvidarEquiposRecordadosAsync(int userId, CancellationToken ct = default)
    {
        var filas = await db.UserTrustedDevices.Where(d => d.UserId == userId).ToListAsync(ct);
        if (filas.Count == 0) return 0;

        db.UserTrustedDevices.RemoveRange(filas);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", userId.ToString(),
            $"Se dejó de confiar en {filas.Count} equipo(s) recordado(s): volverán a pedir el código", ct);

        return filas.Count;
    }

    /// <summary>
    /// Un sello de seguridad nuevo: lo que echa fuera a las sesiones abiertas de una cuenta.
    ///
    /// <para>Público porque el reinicio del segundo factor —que vive en <c>SegundoFactorService</c>—
    /// también tiene que rotarlo, y dos formas de fabricar un sello serían dos formatos que un día
    /// dejan de coincidir con el que la cookie lleva dentro.</para>
    /// </summary>
    public static string NuevoSello() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Renueva el sello de seguridad —lo que echa fuera a las sesiones abiertas— y, EN EL MISMO ACTO,
    /// deja de confiar en los navegadores recordados de esa cuenta.
    ///
    /// <para><b>Las dos cosas van juntas o no sirven.</b> Un equipo recordado se salta el segundo
    /// factor entero; si sobreviviera a un cambio de contraseña, el escenario que esto tiene que
    /// cubrir —«me robaron la cuenta, cambié la contraseña»— dejaría al ladrón exactamente donde
    /// estaba: sabe la contraseña vieja, no, pero desde SU navegador el sistema seguiría sin pedirle
    /// el código, y le bastaría con que la víctima no cambiara nada más. Rotar el sello sin borrar
    /// estas filas es dejar la puerta de atrás abierta mientras se cambia la cerradura de la de
    /// delante.</para>
    ///
    /// <para>No guarda: entra en el mismo <c>SaveChanges</c> que quien la llama, para que no exista
    /// un instante con el sello nuevo y los equipos viejos.</para>
    /// </summary>
    private void RotarSelloYDejarDeConfiarEnLosEquipos(User user)
    {
        user.SecurityStamp = NuevoSello();
        db.UserTrustedDevices.RemoveRange(db.UserTrustedDevices.Where(d => d.UserId == user.Id));
    }

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
