using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Administrador_Desarrollo_Web.Security;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El cifrado de los secretos que viven en la BASE COMPARTIDA. Lo que estas pruebas protegen es una
/// sola propiedad, la que estaba rota: <b>lo que cifra una PC lo descifra cualquier otra</b>. Antes
/// se usaba DPAPI, atado a la cuenta de Windows, y desplegar desde otro equipo fallaba con «530 User
/// cannot log in» porque se mandaba el texto cifrado como contraseña del FTP.
/// </summary>
public class SharedSecretProtectorTests
{
    [Fact]
    public void LoQueCifra_LoDescifra()
    {
        const string secreto = "P@ssw0rd con espacios, acentós y ;=símbolos";

        Assert.True(SharedSecretProtector.TryUnprotect(SharedSecretProtector.Protect(secreto), out var plano));
        Assert.Equal(secreto, plano);
    }

    /// <summary>
    /// La llave NO depende del equipo ni de la cuenta: sale de una constante del ensamblado. Esta es
    /// la prueba de que un valor cifrado en la PC de quien capturó las credenciales lo lee cualquier
    /// otra — el objetivo entero del cambio. Se comprueba descifrando un texto fijo, generado en otro
    /// momento y en otro proceso, sin ningún dato de esta máquina de por medio.
    /// </summary>
    [Fact]
    public void UnValorCifradoEnOtroLado_SeDescifraAqui()
    {
        // Cifrado con la misma llave pero con un IV fijo, tal como lo haría otra máquina.
        var iv = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes("Administrador_Desarrollo_Web::SharedSecret::v1"));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        var datos = Encoding.UTF8.GetBytes("ftp-secreto-de-otra-pc");
        var cifrado = aes.CreateEncryptor().TransformFinalBlock(datos, 0, datos.Length);
        var texto = "ADW1:" + System.Convert.ToBase64String(iv.Concat(cifrado).ToArray());

        Assert.True(SharedSecretProtector.TryUnprotect(texto, out var plano));
        Assert.Equal("ftp-secreto-de-otra-pc", plano);
    }

    [Fact]
    public void DosCifradosDelMismoTexto_NoSonIguales()
    {
        // IV aleatorio por cifrado: si no, dos servidores con la misma contraseña se delatarían al
        // mirar la tabla, sin necesidad de descifrar nada.
        Assert.NotEqual(SharedSecretProtector.Protect("misma"), SharedSecretProtector.Protect("misma"));
    }

    [Theory]
    [InlineData("ADW1:no-es-base64-valido")]
    [InlineData("ADW1:AAAA")]                 // base64 válido pero más corto que el IV
    [InlineData("texto en claro cualquiera")]
    [InlineData("")]
    [InlineData(null)]
    public void UnValorIlegible_DevuelveFalseYNoRevienta(string? basura)
    {
        Assert.False(SharedSecretProtector.TryUnprotect(basura, out var plano));
        Assert.Equal("", plano);
    }

    [Fact]
    public void ElPrefijo_DistingueLoPortableDeLoHeredado()
    {
        Assert.True(SharedSecretProtector.EsPortable(SharedSecretProtector.Protect("x")));

        // Un valor DPAPI es Base64 a secas: nunca lleva «:», así que jamás se confunde con uno nuevo.
        Assert.False(SharedSecretProtector.EsPortable(SecretProtector.Protect("x")));
        Assert.False(SharedSecretProtector.EsPortable(null));
    }

    /// <summary>
    /// Compatibilidad hacia atrás: una base a medio migrar sigue funcionando en la PC que capturó los
    /// valores. Sin esto, publicar el cambio dejaría a TODOS sin desplegar hasta recapturar 22
    /// contraseñas — el remedio peor que la enfermedad.
    /// </summary>
    [Fact]
    public void UnValorDpapiHeredado_SeSigueLeyendoEnEsteEquipo()
    {
        var heredado = SecretProtector.Protect("contraseña vieja");

        Assert.True(SharedSecretProtector.TryUnprotect(heredado, out var plano));
        Assert.Equal("contraseña vieja", plano);
    }

    /// <summary>
    /// El formato queda fijado con un texto cifrado generado en otra ocasión. Cambiar la semilla, el
    /// prefijo o el esquema haría ilegible todo lo que ya está guardado en la base — 22 contraseñas
    /// FTP entre otras cosas—, y el síntoma volvería a ser un «530» del servidor.
    /// </summary>
    [Fact]
    public void ElFormatoNoCambia_LoViejoSeSigueLeyendo()
    {
        const string cifradoAntes = "ADW1:2Edg0J1ijs/rtYFToOehxvNgP1R5P41d76DM/ER7d4p2pMj/KAdGQP61qqNLCE6X";

        Assert.True(SharedSecretProtector.TryUnprotect(cifradoAntes, out var plano));
        Assert.Equal("vector-de-prueba", plano);
    }
}
