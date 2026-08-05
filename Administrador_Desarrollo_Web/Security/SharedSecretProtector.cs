using System.Security.Cryptography;
using System.Text;

namespace Administrador_Desarrollo_Web.Security;

/// <summary>
/// Cifrado de los secretos que viven en la BASE COMPARTIDA: contraseñas de los servidores FTP,
/// connection string de Blob Storage, PAT de la organización, contraseña del correo.
///
/// POR QUÉ NO DPAPI. <see cref="SecretProtector"/> cifra con la cuenta de Windows que guardó el
/// valor, y eso es correcto para un archivo local del equipo (dbprovider.json, devops-personal.json):
/// ahí el aislamiento es justo lo que se busca. Pero para un valor que viaja en la base de datos es
/// exactamente lo contrario de lo que hace falta: la PC que lo guardó era la única capaz de leerlo.
/// En la práctica eso significaba que
///  · abrir la aplicación en otra PC dejaba el Blob como «no configurado», y al recapturarlo se
///    sobrescribía la fila compartida — rompiéndoselo a quien lo había capturado antes; y
///  · el despliegue no fallaba con un mensaje claro sino con «530 User cannot log in» del servidor,
///    porque se mandaba el texto cifrado ilegible como contraseña.
///
/// QUÉ TAN SEGURO ES. La llave se deriva de una semilla que está en este mismo ensamblado: es
/// OFUSCACIÓN, no seguridad, el mismo trato explícito que <see cref="Data.EmbeddedDbConfig"/>. Quien
/// tenga el ejecutable Y acceso a la base puede recuperar los secretos. Lo que de verdad los protege
/// es el acceso a la base (login restringido) y a la red; esto evita que queden en claro y que
/// cualquiera que abra una consulta los lea de pasada.
///
/// FORMATO. <c>ADW1:</c> + Base64(IV de 16 bytes ‖ texto cifrado), AES-256-CBC con relleno PKCS7.
/// El prefijo lleva «:», que no existe en Base64, así que nunca se confunde con un valor DPAPI
/// heredado: <see cref="TryUnprotect"/> lo usa para decidir con qué esquema descifrar.
/// </summary>
public static class SharedSecretProtector
{
    /// <summary>Marca de esquema y versión. Cambiarla obliga a una migración: no la reutilices.</summary>
    public const string Prefijo = "ADW1:";

    /// <summary>
    /// Semilla de la llave. Debe coincidir con $SharedKeySeed en Deploy/build-app.ps1, que descifra
    /// estos mismos valores para incrustarlos al publicar (un test lo verifica leyendo el script).
    /// Internal para ese test.
    /// </summary>
    internal const string KeySeed = "Administrador_Desarrollo_Web::SharedSecret::v1";

    /// <summary>true si el valor ya está en el formato portable (y no en el DPAPI heredado).</summary>
    public static bool EsPortable(string? cipherText) =>
        cipherText != null && cipherText.StartsWith(Prefijo, StringComparison.Ordinal);

    public static string Protect(string plainText)
    {
        using var aes = CrearAes();
        aes.GenerateIV();

        var datos = Encoding.UTF8.GetBytes(plainText);
        using var encryptor = aes.CreateEncryptor();
        var cifrado = encryptor.TransformFinalBlock(datos, 0, datos.Length);

        return Prefijo + Convert.ToBase64String([.. aes.IV, .. cifrado]);
    }

    /// <summary>
    /// Descifra un secreto compartido. Acepta también el formato DPAPI heredado, que solo funciona
    /// en el equipo y la cuenta de Windows que lo guardaron: así una base a medio migrar sigue
    /// funcionando ahí mientras <see cref="Services.SharedSecretMigrationService"/> la pone al día.
    /// </summary>
    public static bool TryUnprotect(string? cipherText, out string plainText)
    {
        plainText = "";
        if (string.IsNullOrEmpty(cipherText)) return false;

        if (!EsPortable(cipherText))
            return SecretProtector.TryUnprotect(cipherText, out plainText);   // heredado (DPAPI)

        try
        {
            plainText = Descifrar(Convert.FromBase64String(cipherText[Prefijo.Length..]));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>AES-256-CBC; el blob es IV (16 bytes) seguido del texto cifrado. Internal para que
    /// un test verifique que descifra lo que produce Deploy/build-app.ps1 (mismo esquema y llave).</summary>
    internal static string Descifrar(byte[] blob)
    {
        if (blob.Length <= 16) throw new InvalidOperationException("Secreto cifrado corrupto (demasiado corto).");

        using var aes = CrearAes();
        aes.IV = blob[..16];

        using var decryptor = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(blob, 16, blob.Length - 16));
    }

    private static Aes CrearAes()
    {
        var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySeed));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }
}
