using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Administrador_Desarrollo_Web.Data;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Contrato entre la app y Deploy/build-app.ps1: la conexión que el script incrusta (cifrada) debe
/// poder descifrarla la app. Si alguien cambia la llave o el esquema en un solo lado, esto truena
/// antes de repartir un ejecutable que no conecta.
/// </summary>
public class EmbeddedDbConfigTests
{
    private static byte[] CifrarComoElScript(string plano)
    {
        // Mismo esquema que build-app.ps1: AES-256-CBC, llave = SHA256(KeySeed), IV(16) al frente, PKCS7.
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(EmbeddedDbConfig.KeySeed));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();
        var bytes  = Encoding.UTF8.GetBytes(plano.Trim());
        var cifrado = aes.CreateEncryptor().TransformFinalBlock(bytes, 0, bytes.Length);
        return aes.IV.Concat(cifrado).ToArray();   // IV (16 bytes) + texto cifrado
    }

    [Fact]
    public void LaApp_DescifraLoQueElEsquemaCifra()
    {
        var cs = "Server=tcp:x.database.windows.net,1433;Database=SOLTUM_DEV_WD;User Id=app_dev;Password=secreto123;Encrypt=True";
        var blob = CifrarComoElScript(cs);
        Assert.Equal(cs, EmbeddedDbConfig.Decrypt(blob));
    }

    [Fact]
    public void BlobDemasiadoCorto_LanzaExplicando()
    {
        Assert.Throws<InvalidOperationException>(() => EmbeddedDbConfig.Decrypt(new byte[10]));
    }

    // Cruza el literal REAL del script con la constante de la app: si alguien cambia $KeySeed en
    // build-app.ps1 (o KeySeed en la app) por su cuenta, el ejecutable incrustado dejaría de descifrar.
    [Fact]
    public void ElKeySeedDelScript_CoincideConElDeLaApp()
    {
        var ps1 = LocalizarBuildScript();
        if (ps1 == null) return;   // fuera del árbol de fuentes (CI empaquetado): el round-trip cubre el esquema

        var m = Regex.Match(File.ReadAllText(ps1), @"\$KeySeed\s*=\s*'([^']*)'");
        Assert.True(m.Success, "No se encontró $KeySeed en build-app.ps1");
        Assert.Equal(EmbeddedDbConfig.KeySeed, m.Groups[1].Value);
    }

    /// <summary>
    /// La app corre sobre Microsoft.Data.SqlClient y guarda la cadena en la forma canónica de ESE
    /// proveedor, que separa algunas palabras clave: «Trust Server Certificate». El proveedor viejo
    /// (System.Data.SqlClient), único disponible en Windows PowerShell 5.1, solo conoce
    /// «TrustServerCertificate» y rechaza la otra con «Palabra clave no admitida».
    ///
    /// Este test deja constancia de POR QUÉ build-app.ps1 traduce la cadena antes de probarla. Si
    /// algún día el proveedor deja de escribirla separada, esto falla y toca revisar (y quizá
    /// simplificar) esa traducción.
    /// </summary>
    [Fact]
    public void LaCadenaCanonica_UsaPalabrasClaveQueElProveedorViejoNoConoce()
    {
        var canonica = SqlConnectionStringHelper.NormalizeOrOriginal(
            "server=x.database.windows.net;database=SOLTUM_DEV_WD;uid=app_dev;pwd=secreto123");

        Assert.Contains("Trust Server Certificate", canonica);
        Assert.DoesNotContain("TrustServerCertificate=", canonica);
    }

    /// <summary>
    /// La prueba de conexión del script NO debe abrirse con la cadena tal cual: eso truena con
    /// «Palabra clave no admitida: 'trust server certificate'» antes siquiera de intentar conectar,
    /// y deja el empaquetado bloqueado sin motivo real.
    /// </summary>
    [Fact]
    public void ElScript_TraduceLaCadenaAntesDeProbarLaConexion()
    {
        var ps1 = LocalizarBuildScript();
        if (ps1 == null) return;   // fuera del árbol de fuentes

        var texto = File.ReadAllText(ps1);

        Assert.Contains("ConvertTo-CadenaDePrueba", texto);
        Assert.False(
            Regex.IsMatch(texto, @"New-Object\s+System\.Data\.SqlClient\.SqlConnection\s+\$ConnectionString"),
            "build-app.ps1 vuelve a abrir la conexión con la cadena sin traducir: el proveedor viejo " +
            "de Windows PowerShell 5.1 no admite «Trust Server Certificate» y el empaquetado falla.");
    }

    // Sube desde el binario de pruebas buscando Deploy/build-app.ps1 dentro del proyecto.
    private static string? LocalizarBuildScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName,
                "Administrador_Desarrollo_Web", "Deploy", "build-app.ps1");
            if (File.Exists(candidato)) return candidato;
        }
        return null;
    }
}
