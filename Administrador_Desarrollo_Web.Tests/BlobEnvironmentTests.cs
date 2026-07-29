using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Carpetas del contenedor: se crean desde la aplicación y se derivan de los nombres de blob, no
/// de una lista configurada. Aquí se prueba la parte que no toca la red.
/// </summary>
public class BlobEnvironmentTests
{
    private static BlobStorageService Nuevo(out SettingsService settings)
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        settings = new SettingsService(db, new AuditService(db, user));
        return new BlobStorageService(settings);
    }

    // ── Derivación de subcarpetas a partir de los nombres de blob ───────────────

    [Fact]
    public void LasSubcarpetasSeDerivanDeLosNombresDeBlob()
    {
        string[] nombres =
        [
            "releases/QA/portal_1.0.zip",
            "releases/QA/portal_1.1.zip",
            "releases/Productivo/portal_1.0.zip",
            "releases/Infrasur/portal_1.0.zip",
            "releases/suelto.zip",              // sin subcarpeta: no aporta ninguna
            "backups/app.db"                    // otra rama: se ignora
        ];

        var carpetas = BlobStorageService.DerivarSubcarpetas(nombres, "releases");

        Assert.Equal(["Infrasur", "Productivo", "QA"], carpetas);   // ordenadas
    }

    [Fact]
    public void UnaCarpetaVaciaSeVeGraciasASuMarcador()
    {
        // El marcador es un blob de cero bytes terminado en «/»: es lo que hace visible una
        // carpeta recién creada que todavía no tiene archivos.
        string[] nombres = ["releases/ClienteNuevo/"];

        var carpetas = BlobStorageService.DerivarSubcarpetas(nombres, "releases");

        Assert.Equal(["ClienteNuevo"], carpetas);
    }

    [Fact]
    public void NoSeDuplicanNiDistinguenMayusculas()
    {
        string[] nombres = ["releases/QA/a.zip", "releases/qa/b.zip", "releases/QA/"];

        Assert.Single(BlobStorageService.DerivarSubcarpetas(nombres, "releases"));
    }

    [Fact]
    public void SoloSeDerivaElPrimerNivel()
    {
        string[] nombres = ["releases/Infrasur/2026/enero/a.zip"];

        var carpetas = BlobStorageService.DerivarSubcarpetas(nombres, "releases");

        Assert.Equal(["Infrasur"], carpetas);
    }

    [Fact]
    public void SinBlobsNoHaySubcarpetas()
    {
        Assert.Empty(BlobStorageService.DerivarSubcarpetas([], "releases"));
    }

    // ── Marcadores ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("releases/QA/", true)]
    [InlineData("releases/", true)]
    [InlineData("releases/QA/portal.zip", false)]
    public void ElMarcadorDeCarpetaSeReconocePorLaBarraFinal(string nombre, bool esMarcador)
    {
        Assert.Equal(esMarcador, BlobStorageService.EsMarcadorDeCarpeta(nombre));
    }

    // ── Ruta resultante ─────────────────────────────────────────────────────────

    [Fact]
    public void LaVersionVaALaSubcarpetaElegida()
    {
        var blob = Nuevo(out _);
        Assert.Equal("releases/Productivo", blob.RutaVersiones("Productivo"));
        Assert.Equal("releases/Infrasur", blob.RutaVersiones("Infrasur"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinSubcarpetaLaVersionVaALaRaizDeVersiones(string? sub)
    {
        var blob = Nuevo(out _);
        Assert.Equal("releases", blob.RutaVersiones(sub));
    }

    [Fact]
    public void LaRutaRespetaLaCarpetaDeVersionesConfigurada()
    {
        var blob = Nuevo(out var settings);
        settings.Set(SettingsService.Keys.AzureBlobReleasesPrefix, "versiones");

        Assert.Equal("versiones/QA", blob.RutaVersiones("QA"));
    }

    [Fact]
    public void LaSubcarpetaTambienSeNormalizaAlArmarLaRuta()
    {
        var blob = Nuevo(out _);
        Assert.Equal("releases/QA", blob.RutaVersiones("/QA/"));
        Assert.Equal("releases/cliente/infrasur", blob.RutaVersiones("cliente\\infrasur"));
    }

    // ── Alta de carpeta ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public async Task NoSePuedeCrearUnaCarpetaSinNombre(string ruta)
    {
        var blob = Nuevo(out var settings);
        settings.Set(SettingsService.Keys.AzureBlobConnectionString,
            "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=aaaa;EndpointSuffix=core.windows.net", true);

        // Se valida ANTES de tocar la red.
        await Assert.ThrowsAsync<ArgumentException>(() => blob.CrearCarpetaAsync(ruta));
    }

    [Fact]
    public async Task NoSePuedeCrearUnaCarpetaConCaracteresImposibles()
    {
        var blob = Nuevo(out var settings);
        settings.Set(SettingsService.Keys.AzureBlobConnectionString,
            "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=aaaa;EndpointSuffix=core.windows.net", true);

        await Assert.ThrowsAsync<ArgumentException>(() => blob.CrearCarpetaAsync("carpeta:mala"));
    }

    // ── Probar conexión ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ProbarConexionSinCadenaAvisaEnLugarDeReventar()
    {
        var blob = Nuevo(out _);

        var (ok, mensaje) = await blob.ProbarConexionAsync();

        Assert.False(ok);
        Assert.Contains("Falta la connection string", mensaje);
    }

    [Fact]
    public async Task ProbarConexionConCadenaInvalidaDevuelveUnMensajeUtil()
    {
        var blob = Nuevo(out _);

        // No lanza: devuelve el error traducido para que la pantalla lo muestre.
        var (ok, mensaje) = await blob.ProbarConexionAsync("esto-no-es-una-connection-string");

        Assert.False(ok);
        Assert.Contains("formato esperado", mensaje);
    }

    [Fact]
    public async Task ProbarConexionUsaLoQueSeLePasa_NoLoGuardado()
    {
        var blob = Nuevo(out var settings);
        settings.Set(SettingsService.Keys.AzureBlobConnectionString, "", true);

        // Aunque no haya nada guardado, probar lo tecleado debe intentarlo igual.
        var (ok, mensaje) = await blob.ProbarConexionAsync("tampoco-sirve");

        Assert.False(ok);
        Assert.DoesNotContain("Falta la connection string", mensaje);
    }

    // ── Vigencia del SAS ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(8761)]
    public void LaVigenciaDelSasFueraDeRangoSeRechaza(double horas)
    {
        var blob = Nuevo(out var settings);
        settings.Set(SettingsService.Keys.AzureBlobConnectionString,
            "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=aaaa;EndpointSuffix=core.windows.net", true);

        Assert.Throws<ArgumentOutOfRangeException>(() => blob.GenerarSasDescarga("releases/x.zip", horas));
    }
}
