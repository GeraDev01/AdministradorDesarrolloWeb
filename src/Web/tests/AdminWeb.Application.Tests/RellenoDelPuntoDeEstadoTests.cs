using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El punto de color de las pantallas SIEMPRE tiene con qué rellenarse.
///
/// <para><b>Qué se está protegiendo.</b> Cuando el estado se pintaba sobre la palabra, que una tabla
/// de colores devolviera la cadena vacía para algún valor era inofensivo: la palabra salía en tinta
/// normal y nadie notaba nada. Con el punto delante deja de serlo. Un <c>background</c> vacío no
/// pinta «neutro»: deja un círculo hueco, y un círculo hueco se lee como que algo se rompió al
/// pintar —que es justo lo que el borde gris del punto está ahí para hacer visible—. No es una
/// hipótesis: <c>EstadoDeServidores.ColorDelEstado</c> devolvía <c>""</c> para «Desplegado» hasta que
/// esa columna recibió su punto, y ése fue el primer arreglo que hubo que hacerle.</para>
///
/// <para><b>Por qué se prueba el valor DESCONOCIDO y no solo los de hoy.</b> Los tres <c>switch</c>
/// cierran en un caso por omisión, así que hoy no hay agujero posible; lo que esta prueba fija es que
/// siga siendo así el día que alguien añada un estado —«Disfrutada» a las vacaciones, un séptimo a
/// las citas— o convierta el caso por omisión en ramas explícitas. Ese día el enumerado nuevo tiene
/// que salir con un punto gris y no con uno hueco, y el compilador no diría nada.</para>
///
/// <para>Se comprueba además que el relleno sea una VARIABLE del tema y no un color escrito a mano.
/// Un hex se ve bien en un modo y queda ilegible en el otro, y nadie se entera hasta que cambia de
/// modo semanas después; la variable la resuelve el navegador, así que obedece al tema claro y al
/// oscuro sin que este código sepa cuál está puesto.</para>
///
/// <para>Aquí están las tres tablas que alimentan los puntos que se pusieron en «Mis permisos»,
/// «Permisos» del líder, «Mis vacaciones» y «Programados». Las de los puntos que ya existían
/// —requerimientos, pool, sugerencias, presencia— no se añaden por no repetir: el defecto que se
/// persigue es el mismo, y si un día conviene cubrirlas todas, esta prueba se convierte en un
/// recorrido por los métodos de <c>ColoresDeEstado</c> en vez de crecer a mano.</para>
///
/// <para>Lo que NO se puede probar desde aquí es la otra mitad de la regla —que la palabra vaya
/// siempre al lado del punto y que el punto lleve su borde gris—, porque eso vive en el marcado de
/// cada página y este proyecto no monta componentes. Ahí lo que sostiene la regla es el comentario
/// que cada columna lleva escrito encima.</para>
/// </summary>
public class RellenoDelPuntoDeEstadoTests
{
    /// <summary>
    /// Un valor que no es de nadie, para provocar el caso por omisión de cada tabla. Se elige un
    /// número absurdo a propósito: cualquiera pequeño podría convertirse mañana en un estado real y
    /// entonces la prueba dejaría de comprobar lo que dice comprobar sin que nadie lo notara.
    /// </summary>
    private const int Desconocido = 9_999;

    /// <summary>
    /// Falla si eso no sirve de relleno para un punto. El mensaje dice la pantalla y no solo el
    /// método, porque quien rompa esto va a estar mirando una tabla de colores y no una rejilla.
    /// </summary>
    private static void SirveDeRelleno(string css, string donde)
    {
        Assert.False(string.IsNullOrWhiteSpace(css),
            $"{donde}: sin relleno el punto sale hueco, y un punto hueco no se lee como «neutro» " +
            "sino como que el pintado falló. Devuelve el neutro del tema, no la cadena vacía.");

        Assert.StartsWith("var(--", css);
        Assert.EndsWith(")", css);

        Assert.DoesNotContain("#", css);
        Assert.DoesNotContain("color:", css);   // la variable SOLA: «color:var(--…)» no vale de background
    }

    [Fact]
    public void Permisos_todosLosEstadosTienenRelleno()
    {
        foreach (var estado in Enum.GetValues<LeaveStatus>())
            SirveDeRelleno(ColoresDeEstado.ColorDePermiso(estado), $"ColorDePermiso({estado})");

        SirveDeRelleno(ColoresDeEstado.ColorDePermiso((LeaveStatus)Desconocido),
            "ColorDePermiso(un estado que todavía no existe)");
    }

    [Fact]
    public void Vacaciones_todosLosEstadosTienenRelleno()
    {
        foreach (var estado in Enum.GetValues<VacationStatus>())
            SirveDeRelleno(ColoresDeEstado.ColorDeVacaciones(estado), $"ColorDeVacaciones({estado})");

        SirveDeRelleno(ColoresDeEstado.ColorDeVacaciones((VacationStatus)Desconocido),
            "ColorDeVacaciones(un estado que todavía no existe)");
    }

    [Fact]
    public void CitasDeDespliegue_todosLosEstadosTienenRelleno()
    {
        foreach (var estado in Enum.GetValues<ScheduledDeploymentStatus>())
            SirveDeRelleno(ColoresDeEstado.ColorDeCitaDeDespliegue(estado),
                $"ColorDeCitaDeDespliegue({estado})");

        SirveDeRelleno(ColoresDeEstado.ColorDeCitaDeDespliegue((ScheduledDeploymentStatus)Desconocido),
            "ColorDeCitaDeDespliegue(un estado que todavía no existe)");
    }

    /// <summary>
    /// «Perdido» tiene que salir en ROJO y no en gris, y por eso vale una prueba propia: es la cita a
    /// la que se le pasó la hora sin que ninguna aplicación estuviera abierta para ejecutarla, o sea
    /// el fallo silencioso que la pantalla de programados existe para enseñar. Es el único de los seis
    /// que, pintado en neutro, no lo encontraría nadie — y es también el que más se parece a
    /// «Cancelado», que sí es neutro porque se dejó sin efecto a propósito.
    /// </summary>
    [Fact]
    public void CitaPerdida_seVeComoUnFalloYNoComoAlgoQueSeDejoSinEfecto()
    {
        var perdido = ColoresDeEstado.ColorDeCitaDeDespliegue(ScheduledDeploymentStatus.Perdido);
        var fallido = ColoresDeEstado.ColorDeCitaDeDespliegue(ScheduledDeploymentStatus.Fallido);
        var cancelado = ColoresDeEstado.ColorDeCitaDeDespliegue(ScheduledDeploymentStatus.Cancelado);

        Assert.Equal(fallido, perdido);
        Assert.NotEqual(cancelado, perdido);
    }
}
