using Administrador_Desarrollo_Web.Forms;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El caso que motiva todo esto: portátiles de 1366×768 (y peor, con Windows al 125 %, que deja un
/// área útil de ~1093×574). Varios diálogos de la aplicación miden 790 px de alto y son fijos.
/// </summary>
public class ResponsiveLayoutTests
{
    private static readonly Rectangle PantallaGrande   = new(0, 0, 1920, 1080);
    private static readonly Rectangle PortatilChico    = new(0, 0, 1366, 728);   // 768 menos la barra
    private static readonly Rectangle PortatilEscalado = new(0, 0, 1093, 574);   // el mismo al 125 %

    [Fact]
    public void EnPantallaGrande_NoTocaNada()
    {
        var a = ResponsiveLayout.Calcular(new Size(480, 790), new Size(0, 0), new Point(100, 50), PantallaGrande);

        Assert.Equal(new Size(480, 790), a.Tamano);
        Assert.Equal(new Point(100, 50), a.Ubicacion);
        Assert.False(a.LiberarBorde);
    }

    [Fact]
    public void DialogoMasAltoQueLaPantalla_SeRecortaYSeLiberaElBorde()
    {
        // PointEntryForm: 480×790, FixedDialog. En un portátil escalado no cabe y no se puede
        // redimensionar: «Guardar» queda debajo del borde inferior, inalcanzable.
        var a = ResponsiveLayout.Calcular(new Size(480, 790), Size.Empty, new Point(0, 0), PortatilEscalado);

        Assert.Equal(480, a.Tamano.Width);                                 // el ancho sí cabía
        Assert.Equal(574 - ResponsiveLayout.Holgura, a.Tamano.Height);     // el alto se recorta
        Assert.True(a.LiberarBorde);                                       // y deja de ser fijo
    }

    [Fact]
    public void MinimoMayorQueLaPantalla_SeAfloja()
    {
        // MainForm declara MinimumSize 1100×700. En un área útil de 1093×574 ese mínimo impide
        // que la ventana se encoja siquiera hasta caber.
        var a = ResponsiveLayout.Calcular(new Size(1300, 820), new Size(1100, 700), Point.Empty, PortatilEscalado);

        Assert.True(a.Minimo.Width  <= PortatilEscalado.Width);
        Assert.True(a.Minimo.Height <= PortatilEscalado.Height);
        Assert.Equal(new Size(1093 - 16, 574 - 16), a.Tamano);
    }

    [Fact]
    public void MinimoQueYaCabia_SeRespeta()
    {
        var a = ResponsiveLayout.Calcular(new Size(920, 640), new Size(760, 520), Point.Empty, PortatilChico);

        Assert.Equal(new Size(760, 520), a.Minimo);   // no se toca lo que no estorba
        Assert.Equal(new Size(920, 640), a.Tamano);
    }

    [Fact]
    public void VentanaQueNaceFueraDelAreaUtil_SeMeteDentro()
    {
        // CenterParent respecto de una ventana en otro monitor la deja a medias fuera.
        var a = ResponsiveLayout.Calcular(new Size(400, 300), Size.Empty, new Point(1200, 600), PortatilChico);

        Assert.True(a.Ubicacion.X + 400 <= PortatilChico.Right);
        Assert.True(a.Ubicacion.Y + 300 <= PortatilChico.Bottom);
    }

    [Fact]
    public void UbicacionNegativa_SeCorrigeAlOrigen()
    {
        var a = ResponsiveLayout.Calcular(new Size(400, 300), Size.Empty, new Point(-500, -80), PortatilChico);

        Assert.Equal(new Point(0, 0), a.Ubicacion);
    }

    [Fact]
    public void MonitorSecundarioConOrigenDesplazado_RespetaSuOrigen()
    {
        // Un segundo monitor a la derecha del principal no empieza en 0: el ajuste tiene que
        // devolver la ventana a ESE origen, no al del escritorio.
        var secundario = new Rectangle(1920, 0, 1280, 1000);
        var a = ResponsiveLayout.Calcular(new Size(400, 300), Size.Empty, new Point(1000, 10), secundario);

        Assert.Equal(1920, a.Ubicacion.X);
        Assert.True(a.Ubicacion.X >= secundario.Left);
    }

    [Fact]
    public void PantallaAbsurdamentePequena_NoEncogeMasAllaDelMinimoAbsoluto()
    {
        var a = ResponsiveLayout.Calcular(new Size(800, 600), Size.Empty, Point.Empty, new Rectangle(0, 0, 100, 90));

        Assert.Equal(ResponsiveLayout.AnchoMinimoAbsoluto, a.Tamano.Width);
        Assert.Equal(ResponsiveLayout.AltoMinimoAbsoluto,  a.Tamano.Height);
    }

    [Fact]
    public void MasAnchaQueLaPantallaYDesplazada_GanaElBordeIzquierdo()
    {
        // Tras el recorte la ventana casi siempre cabe, así que este desempate solo entra en juego
        // cuando choca contra el mínimo absoluto: una pantalla de 200 px no admite los 320 px por
        // debajo de los cuales no encogemos. Ahí prefiero ver su lado izquierdo (títulos, etiquetas)
        // antes que su lado derecho.
        var a = ResponsiveLayout.Calcular(new Size(400, 300), Size.Empty, new Point(50, 20), new Rectangle(0, 0, 200, 900));

        Assert.Equal(ResponsiveLayout.AnchoMinimoAbsoluto, a.Tamano.Width);   // más ancha que la pantalla
        Assert.Equal(0, a.Ubicacion.X);
    }

    [Fact]
    public void EsIdempotente_AplicarloDosVecesDaLoMismo()
    {
        // ResponsiveForm.OnLoad puede dispararse más de una vez si el diálogo se reutiliza.
        var uno = ResponsiveLayout.Calcular(new Size(480, 790), new Size(1100, 700), new Point(900, 500), PortatilEscalado);
        var dos = ResponsiveLayout.Calcular(uno.Tamano, uno.Minimo, uno.Ubicacion, PortatilEscalado);

        Assert.Equal(uno.Tamano,    dos.Tamano);
        Assert.Equal(uno.Minimo,    dos.Minimo);
        Assert.Equal(uno.Ubicacion, dos.Ubicacion);
    }
}
