using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Carpetas configurables dentro del contenedor de Blob Storage y validación de metadatos.
/// No se toca Azure: se prueba la normalización y la validación, que es donde un dato mal escrito
/// acaba creando carpetas fantasma o provocando errores poco descriptivos del SDK.
/// </summary>
public class BlobFolderTests
{
    // ── Normalización del prefijo ───────────────────────────────────────────────

    [Theory]
    [InlineData("releases", "releases")]
    [InlineData("/releases/", "releases")]              // barras sobrantes
    [InlineData("  releases  ", "releases")]            // espacios
    [InlineData("versiones\\prod", "versiones/prod")]   // separador de Windows
    [InlineData("a//b", "a/b")]                          // segmento vacío
    [InlineData("nivel1/nivel2/nivel3", "nivel1/nivel2/nivel3")]
    public void ElPrefijoSeNormaliza(string entrada, string esperado)
    {
        Assert.Equal(esperado, BlobStorageService.Normalizar(entrada, "porDefecto"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("///")]
    public void SinValorSeUsaElPorDefecto(string? entrada)
    {
        Assert.Equal("porDefecto", BlobStorageService.Normalizar(entrada, "porDefecto"));
    }

    [Fact]
    public void NormalizarEsIdempotente()
    {
        var una = BlobStorageService.Normalizar("/Versiones\\Prod/", "x");
        Assert.Equal(una, BlobStorageService.Normalizar(una, "x"));
    }

    // ── Validación del prefijo ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("releases")]
    [InlineData("versiones/produccion")]
    [InlineData("respaldos_2026")]
    public void LosPrefijosRazonablesSeAceptan(string? valor)
    {
        Assert.Null(BlobStorageService.ValidarPrefijo(valor));
    }

    [Theory]
    [InlineData("carpeta:mala")]
    [InlineData("con*asterisco")]
    [InlineData("con?interrogacion")]
    [InlineData("con<menor")]
    [InlineData("con|tuberia")]
    [InlineData("con\"comilla")]
    public void LosCaracteresImposiblesSeRechazan(string valor)
    {
        var error = BlobStorageService.ValidarPrefijo(valor);
        Assert.NotNull(error);
        Assert.Contains("no se puede usar", error);
    }

    [Fact]
    public void UnPrefijoDemasiadoLargoSeRechaza()
    {
        Assert.NotNull(BlobStorageService.ValidarPrefijo(new string('a', 250)));
    }

    // ── Nombres de metadato ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("sistema")]
    [InlineData("checksum_sha256")]
    [InlineData("Version2")]
    [InlineData("_interno")]
    public void LosNombresDeMetadatoValidosSeAceptan(string clave)
    {
        Assert.Null(BlobStorageService.ValidarClaveMetadato(clave));
    }

    [Fact]
    public void UnMetadatoNoPuedeEmpezarConNumero()
    {
        // Azure exige identificadores estilo C#; si no, falla con un error poco descriptivo.
        var error = BlobStorageService.ValidarClaveMetadato("2fase");
        Assert.NotNull(error);
        Assert.Contains("empezar con un número", error);
    }

    [Theory]
    [InlineData("con-guion")]
    [InlineData("con espacio")]
    [InlineData("con.punto")]
    [InlineData("acentuación")]
    public void UnMetadatoSoloAdmiteLetrasNumerosYGuionBajo(string clave)
    {
        Assert.NotNull(BlobStorageService.ValidarClaveMetadato(clave));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ElNombreDeMetadatoNoPuedeEstarVacio(string? clave)
    {
        Assert.NotNull(BlobStorageService.ValidarClaveMetadato(clave));
    }

    // ── Presentación ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    public void ElTamanoSeMuestraLegible(long bytes, string esperado)
    {
        var item = new BlobItem("carpeta/archivo.zip", bytes, null, new Dictionary<string, string>());
        Assert.Equal(esperado, item.SizeLegible);
    }

    [Fact]
    public void ElNombreCortoQuitaLaCarpeta()
    {
        var item = new BlobItem("releases/sistema_1.0_20260722.zip", 0, null, new Dictionary<string, string>());
        Assert.Equal("sistema_1.0_20260722.zip", item.ShortName);
    }
}
