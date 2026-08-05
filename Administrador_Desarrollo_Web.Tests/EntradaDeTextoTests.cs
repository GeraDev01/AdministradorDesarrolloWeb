using Administrador_Desarrollo_Web.Forms;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El caso que motiva esto: escribir un changelog —o unas notas, o un comentario— y pulsar Enter
/// para bajar de renglón guardaba y cerraba la ventana a media frase. En Windows Forms, si el
/// control con el foco no reclama la tecla Enter, se la queda el botón «Guardar» del diálogo, y un
/// TextBox solo la reclama con <c>AcceptsReturn = true</c>, que viene apagado por omisión.
/// </summary>
public class EntradaDeTextoTests
{
    private static Panel Con(params Control[] hijos)
    {
        var panel = new Panel();
        panel.Controls.AddRange(hijos);
        return panel;
    }

    [Fact]
    public void UnCuadroDeVariasLineas_SeQuedaConElEnter()
    {
        var caja = new TextBox { Multiline = true };
        Assert.False(caja.AcceptsReturn);   // el valor de fábrica, que es justo el problema

        EntradaDeTexto.PermitirSaltoDeLinea(Con(caja));

        Assert.True(caja.AcceptsReturn);
    }

    /// <summary>
    /// En un cuadro de un solo renglón, Enter DEBE seguir guardando: es lo que espera cualquiera al
    /// llenar un formulario corto, y ahí no hay renglón nuevo que escribir.
    /// </summary>
    [Fact]
    public void UnCuadroDeUnSoloRenglon_NoSeToca()
    {
        var caja = new TextBox();

        EntradaDeTexto.PermitirSaltoDeLinea(Con(caja));

        Assert.False(caja.AcceptsReturn);
    }

    /// <summary>
    /// Los visores de solo lectura se saltan a propósito: no se escribe en ellos, y que Enter siga
    /// cerrando con el botón «Cerrar» es lo cómodo. Ojo con el orden — hay formularios que ponen
    /// ReadOnly después de construir los controles (el changelog en modo visor, por ejemplo), y por
    /// eso esto corre en OnLoad y no en el constructor.
    /// </summary>
    [Fact]
    public void UnVisorDeSoloLectura_SeSalta()
    {
        var caja = new TextBox { Multiline = true, ReadOnly = true };

        EntradaDeTexto.PermitirSaltoDeLinea(Con(caja));

        Assert.False(caja.AcceptsReturn);
    }

    /// <summary>
    /// Los diálogos de la aplicación anidan: TableLayoutPanel → Panel → el cuadro. Si el recorrido
    /// se quedara en el primer nivel no arreglaría prácticamente ninguno.
    /// </summary>
    [Fact]
    public void LlegaALosCuadrosAnidados()
    {
        var hondo = new TextBox { Multiline = true };
        var raiz = Con(Con(Con(hondo)));

        EntradaDeTexto.PermitirSaltoDeLinea(raiz);

        Assert.True(hondo.AcceptsReturn);
    }

    [Fact]
    public void AplicarloDosVeces_NoRompeNada()
    {
        var caja = new TextBox { Multiline = true };
        var rico = new RichTextBox();
        var raiz = Con(caja, rico);

        EntradaDeTexto.PermitirSaltoDeLinea(raiz);
        EntradaDeTexto.PermitirSaltoDeLinea(raiz);

        Assert.True(caja.AcceptsReturn);
    }

    // ── RichTextBox (el cuadro del changelog) ───────────────────────────────────

    /// <summary>
    /// RichTextBox no tiene AcceptsReturn, así que la tecla se reclama en PreviewKeyDown marcándola
    /// como «de entrada»: eso hace que el mensaje llegue al control en vez de convertirse en la
    /// pulsación del botón por omisión.
    /// </summary>
    [Fact]
    public void EnUnRichTextBox_ElEnterSeReclama()
    {
        var e = new PreviewKeyDownEventArgs(Keys.Enter);

        EntradaDeTexto.ReclamarEnter(null, e);

        Assert.True(e.IsInputKey);
    }

    [Theory]
    [InlineData(Keys.Enter | Keys.Alt)]   // Alt+Enter es de Windows (propiedades), no del cuadro
    [InlineData(Keys.Tab)]                // Tab tiene que seguir saltando de campo
    [InlineData(Keys.Escape)]             // y Escape, cerrando
    public void LasDemasTeclas_SeDejanEnPaz(Keys tecla)
    {
        var e = new PreviewKeyDownEventArgs(tecla);

        EntradaDeTexto.ReclamarEnter(null, e);

        Assert.False(e.IsInputKey);
    }
}
