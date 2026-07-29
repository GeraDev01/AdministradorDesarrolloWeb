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
