using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Connection string incrustada en el ejecutable al publicarlo con
/// <c>-p:EmbedDbConnection=true</c> (ver Deploy/build-app.ps1).
///
/// Es UN SOLO ejecutable para todo el equipo: el menú y los permisos siguen dependiendo del rol
/// de la cuenta con la que cada quien inicia sesión. Lo único que aporta este recurso es evitar
/// que cada persona tenga que capturar la conexión a mano la primera vez.
///
/// PRECEDENCIA: es un valor POR DEFECTO. Si el equipo tiene una configuración local propia
/// (dbprovider.json, capturada desde Configuración → Base de datos), esa gana. Así el
/// administrador puede apuntar a otra base o usar credenciales con más permisos sin necesidad de
/// que le generen un ejecutable distinto.
///
/// SOBRE EL CIFRADO: es OFUSCACIÓN, no seguridad. La llave está en este mismo ensamblado, así que
/// cualquiera con el ejecutable puede recuperar la cadena. Por eso lo que se incrusta debe ser un
/// login restringido (ver Deploy/crear-login-desarrollador.sql), nunca la cuenta con permisos
/// completos: esa el administrador la captura en su propio equipo.
/// </summary>
public static class EmbeddedDbConfig
{
    /// <summary>Nombre lógico del recurso que incrusta el .csproj cuando EmbedDbConnection=true.</summary>
    public const string ResourceName = "EmbeddedDbConnection";

    /// <summary>Semilla de la llave. Debe coincidir con $KeySeed en Deploy/build-app.ps1 (un test lo
    /// verifica leyendo el propio script). Internal para ese test.</summary>
    internal const string KeySeed = "Administrador_Desarrollo_Web::DevBuild::v1";

    private static readonly Lazy<string?> _connection = new(Load);

    /// <summary>Connection string incrustada, o null si este ejecutable se publicó sin ella.</summary>
    public static string? ConnectionString => _connection.Value;

    public static bool HasConnection => !string.IsNullOrWhiteSpace(ConnectionString);

    private static string? Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return null;   // build normal: no hay nada incrustado, y está bien

        try
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Decrypt(ms.ToArray());
        }
        catch
        {
            // Recurso corrupto: se ignora y se sigue con la configuración local o SQLite.
            // Program avisa cuál quedó en efecto, así que el problema no pasa desapercibido.
            return null;
        }
    }

    /// <summary>AES-256-CBC; el blob es IV (16 bytes) seguido del texto cifrado. Internal para que
    /// un test verifique que descifra lo que produce Deploy/build-app.ps1 (mismo esquema y llave).</summary>
    internal static string Decrypt(byte[] blob)
    {
        if (blob.Length <= 16) throw new InvalidOperationException("Recurso de conexión corrupto.");

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySeed));
        aes.IV = blob[..16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(blob, 16, blob.Length - 16)).Trim();
    }
}
