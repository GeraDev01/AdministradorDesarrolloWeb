using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// La ruta personal de LibreOffice y su precedencia. Nada de aquí necesita LibreOffice instalado:
/// se prueban el JSON local (con archivos temporales, como GridLayoutConfigTests), la validación
/// del nombre y el resolutor puro — que la ruta de esta máquina gane sobre la compartida y esta
/// sobre la autodetección es el corazón del requerimiento.
/// </summary>
public class LibreOfficeConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lo-tests-" + Guid.NewGuid().ToString("N"));
    private string ArchivoCfg => Path.Combine(_dir, "libreoffice.json");

    public LibreOfficeConfigTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Crea un soffice falso (0 bytes basta: la validación no lo ejecuta).</summary>
    private string Falso(string nombre)
    {
        var ruta = Path.Combine(_dir, nombre);
        File.WriteAllText(ruta, "");
        return ruta;
    }

    // ── Persistencia del JSON local ──────────────────────────────────────────────

    [Fact]
    public void LaRuta_SobreviveAlReinicio()
    {
        var cfg = new LibreOfficeLocalConfig(ArchivoCfg) { SofficePath = @"D:\Apps\LibreOffice\program\soffice.exe" };
        cfg.Guardar();

        // Instancia nueva = reinicio de la aplicación.
        Assert.Equal(@"D:\Apps\LibreOffice\program\soffice.exe",
            LibreOfficeLocalConfig.Cargar(ArchivoCfg).SofficePath);
    }

    [Fact]
    public void SinArchivo_ConfigVacia_SinExcepcion()
    {
        Assert.Null(LibreOfficeLocalConfig.Cargar(ArchivoCfg).SofficePath);
    }

    [Fact]
    public void ArchivoCorrupto_SeIgnora_YSePuedeVolverAGuardar()
    {
        File.WriteAllText(ArchivoCfg, "{esto no es json");

        var cfg = LibreOfficeLocalConfig.Cargar(ArchivoCfg);
        Assert.Null(cfg.SofficePath);

        cfg.SofficePath = @"C:\lo\soffice.exe";
        cfg.Guardar();   // no debe lanzar
        Assert.Equal(@"C:\lo\soffice.exe", LibreOfficeLocalConfig.Cargar(ArchivoCfg).SofficePath);
    }

    [Fact]
    public void CarpetaInexistente_SeCreaAlGuardar()
    {
        var anidada = Path.Combine(_dir, "no", "existe", "libreoffice.json");
        new LibreOfficeLocalConfig(anidada) { SofficePath = @"C:\lo\soffice.exe" }.Guardar();
        Assert.True(File.Exists(anidada));
    }

    // ── Validación del nombre ────────────────────────────────────────────────────

    [Theory]
    [InlineData("soffice.exe")]
    [InlineData("SOFFICE.EXE")]
    [InlineData("soffice.com")]
    public void EsSofficeValido_AceptaSofficeRealSinImportarMayusculas(string nombre)
    {
        Assert.True(LibreOfficeLocalConfig.EsSofficeValido(Falso(nombre)));
    }

    [Fact]
    public void EsSofficeValido_RechazaOtroEjecutable_AunqueExista()
    {
        // El error clásico del diálogo de abrir: elegir cualquier .exe. Existir no basta.
        Assert.False(LibreOfficeLocalConfig.EsSofficeValido(Falso("winword.exe")));
    }

    [Fact]
    public void EsSofficeValido_RechazaRutaInexistenteYVacia()
    {
        Assert.False(LibreOfficeLocalConfig.EsSofficeValido(Path.Combine(_dir, "no-existe", "soffice.exe")));
        Assert.False(LibreOfficeLocalConfig.EsSofficeValido(""));
        Assert.False(LibreOfficeLocalConfig.EsSofficeValido(null));
    }

    // ── Precedencia del resolutor ────────────────────────────────────────────────

    private static readonly string[] SinConocidas = [];

    [Fact]
    public void LaRutaLocal_GanaSobreLaCompartidaYLaAutodeteccion()
    {
        var (ruta, origen) = LibreOfficeConverter.ResolverRuta(
            local: @"C:\local\soffice.exe", global: @"C:\global\soffice.exe",
            conocidas: [@"C:\conocida\soffice.exe"], existe: _ => true);

        Assert.Equal(@"C:\local\soffice.exe", ruta);
        Assert.Contains("esta computadora", origen);
    }

    [Fact]
    public void LocalRota_CaeALaCompartida_EnVezDeAtorarse()
    {
        // Quien movió su LibreOffice no debe quedarse atrapado en su captura vieja.
        var (ruta, origen) = LibreOfficeConverter.ResolverRuta(
            local: @"C:\vieja\soffice.exe", global: @"C:\global\soffice.exe",
            conocidas: SinConocidas, existe: p => p == @"C:\global\soffice.exe");

        Assert.Equal(@"C:\global\soffice.exe", ruta);
        Assert.Contains("compartida", origen);
    }

    [Fact]
    public void SinConfigurar_CaeALaAutodeteccion()
    {
        var (ruta, origen) = LibreOfficeConverter.ResolverRuta(
            local: null, global: null,
            conocidas: [@"C:\Program Files\LibreOffice\program\soffice.exe"],
            existe: p => p.Contains("Program Files"));

        Assert.Equal(@"C:\Program Files\LibreOffice\program\soffice.exe", ruta);
        Assert.Equal("autodetectada", origen);
    }

    [Fact]
    public void NadaExiste_DevuelveNull()
    {
        var (ruta, origen) = LibreOfficeConverter.ResolverRuta(
            local: @"C:\a\soffice.exe", global: @"C:\b\soffice.exe",
            conocidas: [@"C:\c\soffice.exe"], existe: _ => false);

        Assert.Null(ruta);
        Assert.Equal("ninguna", origen);
    }

    [Fact]
    public void Precedencia_ConArchivosReales_NoSoloConElPredicado()
    {
        // El mismo contrato, pero con File.Exists de verdad sobre archivos falsos en el temp:
        // asegura que el resolutor y la validación hablan del mismo sistema de archivos.
        var localReal = Falso("soffice.exe");

        var (conLocal, _) = LibreOfficeConverter.ResolverRuta(localReal, null, SinConocidas, File.Exists);
        Assert.Equal(localReal, conLocal);

        File.Delete(localReal);
        var (sinLocal, _) = LibreOfficeConverter.ResolverRuta(localReal, null, SinConocidas, File.Exists);
        Assert.Null(sinLocal);
    }
}
