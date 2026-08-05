using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Enlaces dentro del cuerpo de una entrada del foro.
///
/// Lo que más importa aquí no es que una dirección se vuelva pulsable, sino QUÉ se vuelve pulsable:
/// un enlace del foro acaba en Process.Start, así que un esquema sin filtrar convertiría una
/// publicación en un lanzador de programas para todo el equipo. Por eso hay tantas pruebas de lo
/// que NO debe enlazarse como de lo que sí.
/// </summary>
public class ForumRichTextTests
{
    private static List<string> Enlaces(string texto) =>
        ForumRichText.Analizar(texto).Where(s => s.EsEnlace).Select(s => s.Url!).ToList();

    private static string Visible(string texto) =>
        string.Concat(ForumRichText.Analizar(texto).Select(s => s.Texto));

    // ── Lo que sí se enlaza ──────────────────────────────────────────────────────

    [Fact]
    public void UnaDireccionEscritaTalCual_SeVuelvePulsable()
    {
        var segmentos = ForumRichText.Analizar("Está en https://dev.azure.com/zorroDesierto/Webpro y ya.");

        var enlace = Assert.Single(segmentos.Where(s => s.EsEnlace));
        Assert.Equal("https://dev.azure.com/zorroDesierto/Webpro", enlace.Texto);
        Assert.Equal("https://dev.azure.com/zorroDesierto/Webpro", enlace.Url);
    }

    [Fact]
    public void ElTextoVisible_NoCambia_CuandoLaDireccionVaSuelta()
    {
        // Quien pinta esto fija los rangos de enlace por posición dentro del texto concatenado: si
        // Analizar añadiera o quitara un carácter, el subrayado caería sobre las letras de al lado.
        const string original = "Mira https://ejemplo.com/guia, es corta.";
        Assert.Equal(original, Visible(original));
    }

    [Fact]
    public void ElPuntoFinalDeLaFrase_NoEsParteDelEnlace()
    {
        var segmentos = ForumRichText.Analizar("La guía está en https://ejemplo.com/guia.");

        var enlace = Assert.Single(segmentos.Where(s => s.EsEnlace));
        Assert.Equal("https://ejemplo.com/guia", enlace.Texto);
        Assert.EndsWith(".", segmentos[^1].Texto);   // el punto vuelve al texto
    }

    [Fact]
    public void UnParentesisQueEsDeLaDireccion_SeConserva()
    {
        var enlace = Assert.Single(ForumRichText.Analizar("ver https://es.wikipedia.org/wiki/Java_(lenguaje)").Where(s => s.EsEnlace));
        Assert.Equal("https://es.wikipedia.org/wiki/Java_(lenguaje)", enlace.Texto);
    }

    [Fact]
    public void UnParentesisQueEraDeLaFrase_SeQueda_Fuera()
    {
        var enlace = Assert.Single(ForumRichText.Analizar("(está en https://ejemplo.com/x)").Where(s => s.EsEnlace));
        Assert.Equal("https://ejemplo.com/x", enlace.Texto);
    }

    [Fact]
    public void ElAtajoWww_SeAbreComoHttps()
    {
        var enlace = Assert.Single(ForumRichText.Analizar("www.ejemplo.com tiene el manual").Where(s => s.EsEnlace));
        Assert.Equal("www.ejemplo.com", enlace.Texto);          // se sigue leyendo como se escribió
        Assert.StartsWith("https://", enlace.Url);              // pero se abre por https
    }

    [Fact]
    public void ConEtiqueta_SeLeeElNombre_YSeAbreLaDireccion()
    {
        // Es lo que salva a un hilo de quedar empapelado de URLs de doscientos caracteres.
        var segmentos = ForumRichText.Analizar("Lo dejé en [el ticket 4821](https://dev.azure.com/x/_workitems/edit/4821).");

        var enlace = Assert.Single(segmentos.Where(s => s.EsEnlace));
        Assert.Equal("el ticket 4821", enlace.Texto);
        Assert.Equal("https://dev.azure.com/x/_workitems/edit/4821", enlace.Url);
        Assert.DoesNotContain("](", Visible(segmentos[0].Texto + enlace.Texto));
    }

    [Fact]
    public void VariosEnlaces_SalenEnOrden()
    {
        Assert.Equal(
            ["https://uno.com/", "https://dos.com/"],
            Enlaces("primero https://uno.com y después [el otro](https://dos.com)"));
    }

    // ── Lo que NO se enlaza ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file://servidor/compartido/algo.exe")]
    [InlineData("ms-msdt:/id")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    public void UnEsquemaQueNoEsWeb_NoSeVuelvePulsable(string peligrosa)
    {
        // Con etiqueta es el caso feo: se lee «el informe» y por dentro dice otra cosa. Si esto
        // enlazara, pulsar en una publicación cualquiera podría lanzar un programa.
        var texto = $"Abre [el informe]({peligrosa}) cuando puedas";

        Assert.Empty(Enlaces(texto));
        Assert.Equal(texto, Visible(texto));   // se queda tal cual estaba escrito, a la vista
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("data:text/html;base64,SGVsbG8=")]
    [InlineData("ftp://servidor/archivo")]
    [InlineData("/solo/una/ruta")]
    [InlineData("")]
    [InlineData(null)]
    public void EsEnlaceSeguro_SoloAceptaHttpYHttps(string? url)
    {
        Assert.False(ForumRichText.EsEnlaceSeguro(url, out _));
    }

    [Theory]
    [InlineData("https://ejemplo.com")]
    [InlineData("http://intranet/soporte")]
    [InlineData("www.ejemplo.com")]
    public void EsEnlaceSeguro_AceptaLoQueSiEsWeb(string url)
    {
        Assert.True(ForumRichText.EsEnlaceSeguro(url, out var normalizada));
        Assert.StartsWith("http", normalizada);
    }

    [Fact]
    public void UnaDireccionConSaltoDeLinea_NoPasa()
    {
        // Permitirlo dejaría enseñar una dirección y abrir otra: la parte de después del salto no
        // se vería en pantalla.
        Assert.False(ForumRichText.EsEnlaceSeguro("https://bueno.com\nhttps://otro.com", out _));
    }

    [Fact]
    public void UnTextoSinDirecciones_NoTraeEnlaces()
    {
        const string texto = "Bajamos el reporte de 40 s a 1.2 s con una vista indexada.";
        Assert.False(ForumRichText.TieneEnlaces(texto));
        Assert.Equal(texto, Visible(texto));
    }

    [Fact]
    public void UnCuerpoVacio_NoRevienta()
    {
        Assert.Empty(ForumRichText.Analizar(null));
        Assert.Empty(ForumRichText.Analizar(""));
    }

    [Fact]
    public void UnaAvalanchaDeEnlaces_SeCorta_PeroElTextoSigueEntero()
    {
        var texto = string.Join(" ", Enumerable.Range(0, ForumRichText.MaxEnlaces + 25).Select(i => $"https://ejemplo.com/{i}"));

        var segmentos = ForumRichText.Analizar(texto);

        Assert.Equal(ForumRichText.MaxEnlaces, segmentos.Count(s => s.EsEnlace));
        Assert.Equal(texto, Visible(texto));   // lo que pasa del tope se lee, solo que sin enlazar
    }
}
