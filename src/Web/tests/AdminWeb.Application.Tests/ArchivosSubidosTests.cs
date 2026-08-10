using AdminWeb.Application.Services;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lo que se acepta al subir un archivo y con qué nombre acaba guardado.
///
/// Estas pruebas fijan tres cosas que no son opcionales, porque los adjuntos se sirven después desde
/// el MISMO ORIGEN que la aplicación —y lo que el navegador interprete como marcado se ejecuta con
/// la sesión de quien lo abre:
///
/// <list type="number">
///   <item>El nombre nunca lleva ruta. Viaja en <c>Content-Disposition</c> y acaba en el disco de
///         quien descarga.</item>
///   <item>El tipo lo dictan los bytes, jamás la extensión.</item>
///   <item>Nada que el navegador pueda ejecutar entra, ni aunque se llame «foto.png».</item>
/// </list>
///
/// Varias de ellas comprueban rutas de Windows, y eso es deliberado: el servidor corre en Linux
/// (App Service), donde <c>\</c> no es separador, pero el navegador que sube el archivo sí puede ser
/// de Windows. Es exactamente el caso que se escapaba antes.
/// </summary>
public class ArchivosSubidosTests
{
    // Cabeceras reales, para que la detección por bytes se pruebe de verdad.
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3];
    private static byte[] Pdf() => [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0, 0, 0, 0];
    private static byte[] Html() => "<html><script>alert(1)</script></html>"u8.ToArray();

    // ── El nombre nunca lleva ruta ───────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\ana\Desktop\captura.png", "captura.png")]
    [InlineData(@"..\..\..\windows\system32\algo.png", "algo.png")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("../../../etc/shadow", "shadow")]
    [InlineData(@"carpeta\subcarpeta/foto.jpg", "foto.jpg")]
    public void UnNombreConRuta_SeQuedaSoloEnElNombre(string entrada, string esperado) =>
        Assert.Equal(esperado, ArchivosSubidos.NombreSeguro(entrada));

    [Fact]
    public void UnaRutaDeWindows_SeCortaAunqueElServidorSeaLinux()
    {
        // Path.GetFileName no vale aquí: en Linux devuelve la cadena entera porque «\» no es
        // separador. Esta es LA prueba que justifica no usarlo.
        var nombre = ArchivosSubidos.NombreSeguro(@"C:\temp\..\..\evidencia.png");

        Assert.Equal("evidencia.png", nombre);
        Assert.DoesNotContain('\\', nombre);
        Assert.DoesNotContain('/', nombre);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"C:\ruta\")]
    public void UnNombreQueNoDejaNadaUtil_SaleVacio(string? entrada) =>
        Assert.Equal("", ArchivosSubidos.NombreSeguro(entrada));

    [Fact]
    public void UnNombreOculto_PierdeElPuntoDelPrincipio() =>
        Assert.Equal("bashrc", ArchivosSubidos.NombreSeguro(".bashrc"));

    [Fact]
    public void LosCaracteresDeControl_NoLleganAlNombre()
    {
        // Una nueva línea metida en el nombre acaba en la cabecera Content-Disposition y permite
        // colar cabeceras extra en la respuesta.
        var nombre = ArchivosSubidos.NombreSeguro("foto\r\nX-Cosa: mala.png");

        Assert.DoesNotContain('\r', nombre);
        Assert.DoesNotContain('\n', nombre);
    }

    [Fact]
    public void UnNombreLarguisimo_SeRecortaPeroConservaLaExtension()
    {
        var nombre = ArchivosSubidos.NombreSeguro(new string('a', 400) + ".png");

        Assert.Equal(120, nombre.Length);
        // Sin esto, el archivo descargado dejaría de abrirse con doble clic.
        Assert.EndsWith(".png", nombre);
    }

    // ── El tipo lo dictan los bytes ──────────────────────────────────────────────

    [Fact]
    public void ElTipo_SaleDeLosBytes_NoDeLaExtension()
    {
        // El caso que importa: alguien llama «captura.png» a un HTML. Si el tipo saliera del nombre,
        // se serviría como image/png y el navegador acabaría ejecutando el script.
        Assert.Equal("application/octet-stream", ArchivosSubidos.TipoDeContenido(Html()));

        Assert.Equal("image/png", ArchivosSubidos.TipoDeContenido(Png()));
        Assert.Equal("image/jpeg", ArchivosSubidos.TipoDeContenido(Jpeg()));
        Assert.Equal("application/pdf", ArchivosSubidos.TipoDeContenido(Pdf()));
    }

    [Fact]
    public void ElTipoDeUnaImagen_LoDecideElMismoSitioQueEnElForo()
    {
        // Dos detectores que se separaran acabarían aceptando cada uno lo que el otro rechaza, y el
        // que manda es el que decide el Content-Type con el que salen los bytes.
        foreach (var bytes in new[] { Png(), Jpeg() })
            Assert.Equal(ForumMedia.TipoDeImagen(bytes), ArchivosSubidos.TipoDeContenido(bytes));
    }

    [Fact]
    public void LoDesconocido_SaleComoBinario_ParaQueSeDescargueEnVezDeInterpretarse() =>
        Assert.Equal("application/octet-stream", ArchivosSubidos.TipoDeContenido([1, 2, 3, 4, 5, 6, 7, 8]));

    [Fact]
    public void UnArchivoVacio_NoRevientaAlMirarleLosBytes() =>
        Assert.Equal("application/octet-stream", ArchivosSubidos.TipoDeContenido([]));

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public void LoQueSePuedeVer_SeEnsenaEnLaPagina(string tipo) =>
        Assert.True(ArchivosSubidos.SePuedePrevisualizar(tipo));

    [Fact]
    public void LoQueNoSePuedeVer_SeDescarga() =>
        Assert.False(ArchivosSubidos.SePuedePrevisualizar("application/octet-stream"));

    // ── Nada ejecutable entra ────────────────────────────────────────────────────

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.ps1")]
    [InlineData("macro.vbs")]
    [InlineData("cosa.bat")]
    [InlineData("libreria.dll")]
    [InlineData("instalador.msi")]
    [InlineData("acceso.lnk")]
    public void LoEjecutable_SeRechazaPorLaExtension(string nombre)
    {
        var (ok, error, _) = ArchivosSubidos.Validar(nombre, Pdf());

        Assert.False(ok);
        Assert.Contains("No se admiten", error);
    }

    [Theory]
    [InlineData("dibujo.svg")]
    [InlineData("pagina.html")]
    [InlineData("pagina.htm")]
    public void ElMarcadoQueElNavegadorEjecuta_TampocoEntra(string nombre)
    {
        // SVG y HTML no son mapas de bits: son marcado. Servidos desde el mismo origen que la
        // aplicación, su script corre con la sesión de quien los abre. Por eso ForumMedia tampoco
        // los reconoce como imagen.
        var (ok, _, _) = ArchivosSubidos.Validar(nombre, Png());

        Assert.False(ok);
    }

    [Fact]
    public void UnEjecutableDisfrazadoDeImagen_NoPasaLaComprobacionDeImagenes()
    {
        // La extensión dice png y la comprobación de extensiones lo deja pasar; los bytes no.
        var (ok, error, _) = ArchivosSubidos.Validar("captura.png", Html(), soloImagenes: true);

        Assert.False(ok);
        Assert.Contains("no es una imagen", error);
    }

    [Fact]
    public void UnDocumentoNormal_EntraSiNoSePidieronSoloImagenes()
    {
        var (ok, error, nombre) = ArchivosSubidos.Validar(@"C:\docs\estimación.pdf", Pdf());

        Assert.True(ok, error);
        Assert.Equal("estimación.pdf", nombre);
    }

    [Fact]
    public void UnDocumento_NoEntraDondeSoloCabenImagenes()
    {
        var (ok, _, _) = ArchivosSubidos.Validar("estimacion.pdf", Pdf(), soloImagenes: true);

        Assert.False(ok);
    }

    [Fact]
    public void UnaImagenDeVerdad_Entra()
    {
        var (ok, error, nombre) = ArchivosSubidos.Validar("captura.png", Png(), soloImagenes: true);

        Assert.True(ok, error);
        Assert.Equal("captura.png", nombre);
    }

    [Fact]
    public void UnArchivoVacio_SeRechaza()
    {
        var (ok, error, _) = ArchivosSubidos.Validar("vacio.png", []);

        Assert.False(ok);
        Assert.Contains("vacío", error);
    }

    [Fact]
    public void UnArchivoQueNoCabe_SeRechazaAntesDeGuardarlo()
    {
        var enorme = new byte[ArchivosSubidos.MaxBytes + 1];
        Png().CopyTo(enorme, 0);

        var (ok, error, _) = ArchivosSubidos.Validar("enorme.png", enorme);

        Assert.False(ok);
        Assert.Contains("pasa de", error);
    }

    [Fact]
    public void UnArchivoSinNombreUtil_SeRechazaEnVezDeInventarleUno()
    {
        // Inventarlo aquí escondería el problema; cada servicio pone el suyo («evidencia»,
        // «justificante») porque solo él sabe cuál tiene sentido.
        var (ok, error, _) = ArchivosSubidos.Validar("../..", Png());

        Assert.False(ok);
        Assert.Contains("nombre", error);
    }
}
