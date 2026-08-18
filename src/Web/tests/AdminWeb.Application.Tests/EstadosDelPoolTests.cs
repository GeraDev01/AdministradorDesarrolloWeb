using AdminWeb.Application.Services;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// QUE NINGÚN ESTADO DEL POOL SE QUEDE SIN NOMBRE, SIN COLOR NI SIN SITIO.
///
/// <para><b>Por qué hace falta esto y no basta con mirar el código.</b> Las tres funciones que
/// traducen un estado —la etiqueta, el color y el orden— tienen rama por omisión, y esa rama dice
/// «Retirada». Un estado nuevo que se olvide en cualquiera de ellas no rompe nada: se lee y se pinta
/// como «Retirada», en gris, y el desplegable del líder acaba con dos entradas iguales que filtran
/// cosas distintas. Es exactamente la clase de fallo que pasa una revisión sin que nadie lo vea.</para>
///
/// <para>Está modelada sobre la prueba que recorre las prioridades del pool contra su tabla de
/// traducción a DevOps, y por el mismo motivo: obligar a que añadir un valor al enumerado sea una
/// decisión y no un descuido.</para>
/// </summary>
public class EstadosDelPoolTests
{
    private static IEnumerable<PoolActivityStatus> Todos => Enum.GetValues<PoolActivityStatus>();

    /// <summary>
    /// Cada estado tiene su propia etiqueta, y no se repite ninguna.
    ///
    /// <para>Lo que caza es el estado que se cayó en la rama por omisión: sin la comprobación de
    /// REPETIDAS, un valor olvidado devolvería «Retirada» y la prueba pasaría en verde.</para>
    /// </summary>
    [Fact]
    public void CadaEstado_tieneEtiquetaPropiaYSinRepetir()
    {
        var etiquetas = Todos.Select(PoolSeed.Etiqueta).ToList();

        Assert.All(etiquetas, e => Assert.False(string.IsNullOrWhiteSpace(e)));
        Assert.Equal(etiquetas.Count, etiquetas.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Lo mismo con el color: dos estados del mismo color no es un fallo —«Disponible» y
    /// «Retirada» comparten el neutro a propósito— pero uno SIN color sí lo sería.</summary>
    [Fact]
    public void CadaEstado_tieneColor()
    {
        foreach (var estado in Todos)
            Assert.False(string.IsNullOrWhiteSpace(ColoresDeEstado.ColorDelPool(estado)));
    }

    /// <summary>
    /// Cada estado tiene su sitio en el orden de la rejilla, y no lo comparte con otro.
    ///
    /// <para>El orden NO es el del enumerado, y por eso existe la función: «Por clasificar» tuvo que
    /// declararse con el último valor —los números están escritos en una base en producción y
    /// renumerar convertiría cada actividad aceptada en otra cosa—, así que ordenando por el número
    /// lo único que reclama una decisión del líder saldría al fondo, detrás de todo lo muerto.</para>
    /// </summary>
    [Fact]
    public void CadaEstado_tieneUnSitioPropioEnElOrden()
    {
        var ordenes = Todos.Select(PoolSeed.OrdenDeEstado).ToList();
        Assert.Equal(ordenes.Count, ordenes.Distinct().Count());
    }

    /// <summary>Y lo que espera al líder va ARRIBA, que es la razón de que ese orden exista.</summary>
    [Fact]
    public void LoQueEsperaAlLider_ordenaAntesQueLoCerrado()
    {
        Assert.True(PoolSeed.OrdenDeEstado(PoolActivityStatus.PorClasificar)
                  < PoolSeed.OrdenDeEstado(PoolActivityStatus.Disponible));

        Assert.True(PoolSeed.OrdenDeEstado(PoolActivityStatus.PorClasificar)
                  < PoolSeed.OrdenDeEstado(PoolActivityStatus.Aceptada));

        Assert.True(PoolSeed.OrdenDeEstado(PoolActivityStatus.PorClasificar)
                  < PoolSeed.OrdenDeEstado(PoolActivityStatus.Retirada));
    }

    /// <summary>
    /// <b>Disponible sigue siendo el valor 0.</b> No es trivia: toda fila que se cree sin fijar el
    /// estado nace TOMABLE, y de ahí que el alta automática lo escriba siempre a mano. Si algún día
    /// se renumerara, esa suposición dejaría de ser cierta en silencio.
    /// </summary>
    [Fact]
    public void Disponible_sigueSiendoElValorCero()
    {
        Assert.Equal(0, (int)PoolActivityStatus.Disponible);
    }
}
