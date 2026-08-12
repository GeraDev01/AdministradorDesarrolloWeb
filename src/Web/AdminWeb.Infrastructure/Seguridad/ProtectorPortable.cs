using System.Security.Cryptography;
using System.Text;

namespace AdminWeb.Infrastructure.Seguridad;

/// <summary>
/// Lee y escribe los secretos de la tabla de configuración con el MISMO esquema que la aplicación de
/// escritorio.
///
/// <para><b>Es una copia deliberada, no un descuido.</b> Estas filas —la contraseña del correo, la
/// cadena de Blob, el PAT de la organización— las comparten las dos aplicaciones contra la misma
/// base, y el escritorio sigue en producción hasta el corte. Si la web escribiera con su propio
/// cifrado, el escritorio dejaría de poder leer cualquier secreto que alguien tocara desde el
/// navegador: no fallaría al compilar ni al guardar, fallaría un martes cualquiera al desplegar, con
/// un «530 User cannot log in» del servidor FTP. Por eso la web escribe en el formato de siempre
/// mientras el escritorio viva.</para>
///
/// <para><b>Qué tan seguro es.</b> Lo mismo que en el escritorio: la llave se deriva de una semilla
/// que está en el propio ensamblado, así que esto es OFUSCACIÓN, no seguridad. Quien tenga el binario
/// y acceso a la base recupera los secretos. Lo que de verdad los protege es el acceso a la base.
/// Aquí evita que queden en claro para cualquiera que abra una consulta de pasada.</para>
///
/// <para><b>Después del corte</b> esto se sustituye por <c>IDataProtection</c> con el llavero
/// cifrado de verdad — con un certificado en esta suscripción, que no tiene Key Vault, o con Key
/// Vault donde lo haya; las dos las resuelve <c>Llavero</c>. No se puede hacer antes sin romper el
/// escritorio, y por eso <see cref="Descifrar"/> ya reconoce el prefijo nuevo: el día de la
/// migración solo hay que dejar de escribir con este.</para>
///
/// <para><b>Lo heredado de DPAPI no se puede leer aquí.</b> El escritorio cifraba antes con la cuenta
/// de Windows que guardaba el valor; eso no existe fuera de Windows y menos en un servidor. Su propio
/// <c>SharedSecretMigrationService</c> los va convirtiendo a este formato. Un valor así se reconoce y
/// se informa como tal en vez de devolver un silencio que parecería «no configurado».</para>
/// </summary>
public static class ProtectorPortable
{
    /// <summary>Marca de esquema del escritorio. No se reutiliza ni se cambia: identifica el formato.</summary>
    public const string Prefijo = "ADW1:";

    /// <summary>
    /// Semilla de la llave. <b>Tiene que ser byte por byte la del escritorio</b>
    /// (<c>Security/SharedSecretProtector.KeySeed</c>) o las dos aplicaciones dejarían de entenderse.
    /// Una prueba lo comprueba.
    /// </summary>
    internal const string Semilla = "Administrador_Desarrollo_Web::SharedSecret::v1";

    /// <summary>¿Está en el formato compartido y legible?</summary>
    public static bool EsPortable(string? cifrado) =>
        cifrado != null && cifrado.StartsWith(Prefijo, StringComparison.Ordinal);

    /// <summary>
    /// ¿Es un valor heredado de DPAPI, que solo podía leer la máquina que lo escribió?
    ///
    /// Se distingue por descarte: los del formato compartido llevan prefijo con dos puntos, que no
    /// existen en Base64. Saberlo permite decir «este secreto hay que volver a capturarlo» en lugar
    /// de enseñarlo como si nunca se hubiera configurado.
    /// </summary>
    public static bool EsHeredadoDeWindows(string? cifrado) =>
        !string.IsNullOrEmpty(cifrado) && !EsPortable(cifrado);

    public static string Cifrar(string textoPlano)
    {
        using var aes = CrearAes();
        aes.GenerateIV();

        var datos = Encoding.UTF8.GetBytes(textoPlano);
        using var cifrador = aes.CreateEncryptor();
        var cifrado = cifrador.TransformFinalBlock(datos, 0, datos.Length);

        return Prefijo + Convert.ToBase64String([.. aes.IV, .. cifrado]);
    }

    /// <summary>
    /// Devuelve el secreto en claro, o null si no se puede leer —porque está corrupto o porque es de
    /// los heredados de DPAPI—. Quien llama distingue los dos casos con
    /// <see cref="EsHeredadoDeWindows"/>.
    /// </summary>
    public static string? Descifrar(string? cifrado)
    {
        if (!EsPortable(cifrado)) return null;

        try
        {
            var blob = Convert.FromBase64String(cifrado![Prefijo.Length..]);
            // El blob es IV (16 bytes) seguido del texto cifrado; menos que eso no es un secreto.
            if (blob.Length <= 16) return null;

            using var aes = CrearAes();
            aes.IV = blob[..16];

            using var descifrador = aes.CreateDecryptor();
            return Encoding.UTF8.GetString(descifrador.TransformFinalBlock(blob, 16, blob.Length - 16));
        }
        catch
        {
            // Un secreto ilegible se trata como «no hay»: devolver basura acabaría mandándose como
            // contraseña a un servidor, que es exactamente el fallo incomprensible que este formato
            // vino a arreglar en el escritorio.
            return null;
        }
    }

    private static Aes CrearAes()
    {
        var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(Semilla));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }
}
