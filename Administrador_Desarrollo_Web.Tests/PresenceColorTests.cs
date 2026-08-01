using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El mapa estado→color de presencia. Lo que se afirma aquí no es gusto: «Disponible es verde» es
/// un requisito pedido tal cual, y el contraste mínimo es lo que evita el reclamo «el verde no se
/// ve» — la paleta general (Success, Warning) NO alcanza como texto sobre blanco (2.3:1) y por eso
/// estos tonos son más oscuros a propósito.
/// </summary>
public class PresenceColorTests
{
    // ── Luminancia relativa y contraste según WCAG 2.x ──────────────────────────

    private static double Canal(byte c)
    {
        var s = c / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Luminancia(Color c) =>
        0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);

    private static double Contraste(Color a, Color b)
    {
        double la = Luminancia(a), lb = Luminancia(b);
        var (claro, oscuro) = la >= lb ? (la, lb) : (lb, la);
        return (claro + 0.05) / (oscuro + 0.05);
    }

    [Fact]
    public void Disponible_EsVerde()
    {
        // El canal verde domina; no se clava un hex para poder ajustar el tono sin romper esto.
        var c = AppTheme.PresenceColor(PresenceState.Disponible);
        Assert.True(c.G > c.R && c.G > c.B, $"Disponible no es verde: R={c.R} G={c.G} B={c.B}");
    }

    [Fact]
    public void Ocupado_EsRojo_YNoElAmbarDeLosAvisos()
    {
        var c = AppTheme.PresenceColor(PresenceState.Ocupado);
        Assert.True(c.R > c.G && c.R > c.B, $"Ocupado no es rojo: R={c.R} G={c.G} B={c.B}");
        // «Ocupado» significa «no me interrumpas», no «advertencia»: no debe compartir color.
        Assert.NotEqual(AppTheme.Warning, c);
    }

    [Fact]
    public void CadaEstado_TieneUnColorDistinto()
    {
        // Si alguien agrega un estado al enum y lo olvida en el switch, cae al gris del default y
        // este conteo baja: es el aviso que la compilación no da.
        var distintos = Enum.GetValues<PresenceState>()
            .Select(AppTheme.PresenceColor)
            .Distinct()
            .Count();
        Assert.Equal(Enum.GetValues<PresenceState>().Length, distintos);
    }

    [Fact]
    public void TodosLosColores_SeLeenComoTextoSobreLaRejilla()
    {
        // La celda «Estado» pinta el color como TEXTO sobre CardBg (blanco) y sobre GridAlt (la
        // fila alterna). 4.5:1 es el mínimo WCAG AA para texto normal.
        foreach (var e in Enum.GetValues<PresenceState>())
        {
            var c = AppTheme.PresenceColor(e);
            Assert.True(Contraste(c, AppTheme.CardBg) >= 4.5,
                $"{e}: contraste {Contraste(c, AppTheme.CardBg):0.00}:1 sobre la rejilla, se necesita 4.5");
            Assert.True(Contraste(c, AppTheme.GridAlt) >= 4.3,
                $"{e}: contraste {Contraste(c, AppTheme.GridAlt):0.00}:1 sobre la fila alterna");
        }
    }

    [Fact]
    public void TodosLosColores_AguantanTextoBlancoEncima()
    {
        // Es lo que autoriza a usarlos como FONDO del botón de estado de la barra superior.
        foreach (var e in Enum.GetValues<PresenceState>())
        {
            var c = AppTheme.PresenceColor(e);
            Assert.True(Contraste(c, Color.White) >= 4.5,
                $"{e}: con texto blanco da {Contraste(c, Color.White):0.00}:1, se necesita 4.5");
        }
    }

    [Fact]
    public void Ausente_UsaElMismoGrisQueElDesconectado()
    {
        // La fila del desconectado ya usa TextSecondary; dos grises distintos en la misma pantalla
        // se leerían como dos estados distintos.
        Assert.Equal(AppTheme.TextSecondary, AppTheme.PresenceColor(PresenceState.Ausente));
    }

    [Fact]
    public void Etiqueta_EIcono_NoSeRepitenNiVienenVacios()
    {
        // Comparten con el color el mismo riesgo del «_ =>»: un estado nuevo caería en silencio al
        // texto de otro. Hoy nada probaba estos dos métodos.
        var estados = Enum.GetValues<PresenceState>();

        var etiquetas = estados.Select(PresenceService.Etiqueta).ToList();
        Assert.All(etiquetas, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(estados.Length, etiquetas.Distinct().Count());

        var iconos = estados.Select(PresenceService.Icono).ToList();
        Assert.All(iconos, i => Assert.False(string.IsNullOrWhiteSpace(i)));
    }
}
