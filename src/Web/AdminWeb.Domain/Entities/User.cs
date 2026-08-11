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

    // ── Segundo factor (código de la aplicación del teléfono) ────────────────────────────────────
    //
    // El SECRETO no está aquí, y no es un olvido: vive cifrado en UserSecrets, con la protección de
    // datos del servidor. Una columna en claro en esta tabla sería una llave de acceso legible para
    // cualquiera que consultara la base. Aquí solo queda el ESTADO, que no sirve de nada por sí solo.

    /// <summary>
    /// Si esta cuenta ya tiene el segundo factor funcionando.
    ///
    /// <para><b>Solo se pone en cierto tras haber tecleado un código válido</b>, dentro de
    /// <c>SegundoFactorService.ConfirmarAltaAsync</c>. Ningún otro sitio lo escribe. Activarlo al
    /// enseñar el código QR sería la forma de dejar a alguien fuera de su cuenta sin que ni esa
    /// persona ni nadie se enterara: si el escaneo salió mal, la aplicación del teléfono no muestra
    /// ningún error — muestra códigos, que simplemente no son los buenos.</para>
    ///
    /// <para>Empieza en falso para TODAS las cuentas, incluidas las que ya existían. Es lo que hace
    /// que el segundo factor sea obligatorio sin excepciones: quien no lo tiene activo no puede
    /// hacer nada más que activarlo.</para>
    /// </summary>
    public bool SegundoFactorActivo { get; set; }

    /// <summary>Cuándo quedó activo. Es dato de bitácora: responde «¿desde cuándo está protegida esta cuenta?».</summary>
    public DateTime? SegundoFactorDesdeUtc { get; set; }

    /// <summary>
    /// La última ventana de treinta segundos cuyo código se aceptó. Es la ANTIRREPETICIÓN: un código
    /// ya usado no vuelve a valer aunque siga vigente.
    ///
    /// <para>Es <c>long</c> y no <c>int</c> a propósito: son segundos desde 1970 divididos entre 30,
    /// y aunque hoy quepan de sobra en 32 bits, el tipo de la cuenta no debería tener fecha de
    /// caducidad escrita en él.</para>
    ///
    /// <para>Nulo mientras la cuenta no haya aceptado ningún código nunca.</para>
    /// </summary>
    public long? SegundoFactorUltimaVentana { get; set; }

    public Developer? Developer { get; set; }
}
