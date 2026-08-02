using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El aviso de versión nueva. Lo que se prueba aquí es exactamente lo que se rompe en silencio:
/// comparar «1.10.0» con «1.9.0» como texto, y la trampa de System.Version con componentes
/// desiguales («1.2» resulta MENOR que «1.2.0» porque lo no especificado vale -1).
/// </summary>
public class UpdateNoticeTests
{
    private static readonly Version EnMarcha = new(1, 2, 0, 0);

    // ── Normalización ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.0", "1.2.0.0")]
    [InlineData("  1.2.0  ", "1.2.0.0")]
    [InlineData("v1.2.0", "1.2.0.0")]
    [InlineData("1.2", "1.2.0.0")]          // rellena, no deja -1
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void Normalizar_ToleraLoQueSeCapturaAMano(string texto, string esperado)
    {
        Assert.Equal(Version.Parse(esperado), AppVersion.Normalizar(texto));
    }

    [Fact]
    public void Normalizar_QuitaElHashDeGitQuePegaElSdk()
    {
        // VERIFICADO en el .exe publicado: ProductVersion llega como «1.0.0+f896b4d…». Sin cortar
        // el sufijo, Version.TryParse falla y la función quedaría muda para siempre.
        Assert.Equal(new Version(1, 0, 0, 0), AppVersion.Normalizar("1.0.0+f896b4d74a0c2afd"));
        Assert.Equal(new Version(1, 3, 0, 0), AppVersion.Normalizar("1.3.0-beta.2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("próximamente")]
    [InlineData("1.2.x")]
    public void Normalizar_LoQueNoEsVersion_EsNull(string? texto)
    {
        Assert.Null(AppVersion.Normalizar(texto));
    }

    [Fact]
    public void Normalizar_NoComparaComoTexto()
    {
        // El bug clásico: en orden alfabético «1.10.0» < «1.9.0». Como Version, no.
        Assert.True(AppVersion.Normalizar("1.10.0") > AppVersion.Normalizar("1.9.0"));
    }

    // ── La decisión ──────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluar_VersionMayor_Avisa()
    {
        var a = UpdateNotice.Evaluar("1.3.0", "https://x/app.exe", "Arregla el login", EnMarcha, null);

        Assert.NotNull(a);
        Assert.Equal("1.3.0", a!.VersionTexto);
        Assert.Equal("https://x/app.exe", a.Url);
        Assert.Equal("Arregla el login", a.Novedades);
    }

    [Theory]
    [InlineData("1.2.0")]   // la misma
    [InlineData("1.1.9")]   // anterior
    [InlineData("1.2")]     // la misma, capturada corta: NO debe avisar
    public void Evaluar_MismaOAnterior_NoAvisa(string publicada)
    {
        Assert.Null(UpdateNotice.Evaluar(publicada, null, null, EnMarcha, null));
    }

    [Fact]
    public void Evaluar_YaAvisada_NoSeRepite_PeroUnaMasNuevaSi()
    {
        Assert.Null(UpdateNotice.Evaluar("1.3.0", null, null, EnMarcha, yaAvisada: "1.3.0"));
        Assert.NotNull(UpdateNotice.Evaluar("1.4.0", null, null, EnMarcha, yaAvisada: "1.3.0"));
    }

    [Fact]
    public void Evaluar_MalCapturada_Calla()
    {
        // Mejor no avisar que avisar de una versión que nadie puede identificar.
        Assert.Null(UpdateNotice.Evaluar("próximamente", null, null, EnMarcha, null));
        Assert.Null(UpdateNotice.Evaluar(null, null, null, EnMarcha, null));
    }

    [Fact]
    public void Evaluar_MemoriaCorrupta_NoBloqueaElAviso()
    {
        // Si el archivo local trae basura, se avisa igual: perder un aviso es peor que repetirlo.
        Assert.NotNull(UpdateNotice.Evaluar("1.3.0", null, null, EnMarcha, yaAvisada: "basura"));
    }

    // ── El enlace ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://blob.core.windows.net/app/Administrador.exe")]
    [InlineData("http://intranet/app.exe")]
    [InlineData(@"\\servidor\compartido\Administrador.exe")]
    public void UrlValida_AceptaEnlaceYRutaDeRed(string url) => Assert.True(UpdateNotice.UrlValida(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pídeselo al jefe")]
    [InlineData("javascript:alert(1)")]
    public void UrlValida_RechazaLoDemas(string? url) => Assert.False(UpdateNotice.UrlValida(url));

    [Fact]
    public void Evaluar_ConUrlInvalida_AvisaPeroSinEnlace()
    {
        // Un AppSetting mal capturado no puede convertirse en «que Windows abra lo que sea».
        var a = UpdateNotice.Evaluar("1.3.0", "javascript:alert(1)", null, EnMarcha, null);
        Assert.NotNull(a);
        Assert.Null(a!.Url);
    }

    // ── La versión en marcha ─────────────────────────────────────────────────────

    [Fact]
    public void VersionEnMarcha_SeResuelveYEsComparable()
    {
        // No se fija el número (sube en cada entrega), pero sí que se resolvió algo real: un 0.0.0
        // significaría que la lectura de atributos falló y el aviso saldría siempre.
        Assert.True(AppVersion.Actual > new Version(0, 0, 0, 0));
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Texto));
    }

    [Fact]
    public void Resolver_PrefiereFileVersion_YCaeALaInformativaLimpia()
    {
        Assert.Equal(new Version(2, 0, 0, 0), AppVersion.Resolver("2.0.0.0", "9.9.9", new Version(3, 0)));
        Assert.Equal(new Version(9, 9, 9, 0), AppVersion.Resolver(null, "9.9.9+abc123", new Version(3, 0)));
        Assert.Equal(new Version(3, 0, 0, 0), AppVersion.Resolver(null, null, new Version(3, 0)));
        Assert.Equal(new Version(0, 0, 0, 0), AppVersion.Resolver(null, null, null));
    }

    // ── La memoria local ─────────────────────────────────────────────────────────

    [Fact]
    public void Estado_LaVersionAvisada_SobreviveAlReinicio()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upd-" + Guid.NewGuid().ToString("N"));
        var archivo = Path.Combine(dir, "actualizacion.json");
        try
        {
            new UpdateNoticeState(archivo) { UltimaVersionAvisada = "1.3.0" }.Guardar();
            Assert.Equal("1.3.0", UpdateNoticeState.Cargar(archivo).UltimaVersionAvisada);

            File.WriteAllText(archivo, "{roto");
            Assert.Null(UpdateNoticeState.Cargar(archivo).UltimaVersionAvisada);   // corrupto: se ignora
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
