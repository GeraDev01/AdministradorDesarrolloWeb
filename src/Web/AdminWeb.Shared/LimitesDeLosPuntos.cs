namespace AdminWeb.Shared;

/// <summary>
/// LOS TOPES DE LO QUE SE PUEDE MOVER DE UNA VEZ EN EL MARCADOR DE ALGUIEN.
///
/// <para>Vive en Shared por lo mismo que <see cref="CapacidadDeTrabajo"/>: el número lo tiene que
/// imponer el servidor —es él quien decide, y a la API se llega sin pasar por la pantalla— pero la
/// pantalla tiene que poder <b>enseñarlo</b>, y desde el navegador no se ve la capa de aplicación.
/// Escribirlo dos veces daría dos topes que un día son distintos, y el que se quedaría atrás sería
/// justo el de la casilla que la persona ve.</para>
/// </summary>
public static class LimitesDeLosPuntos
{
    /// <summary>
    /// Lo más que puede restar UN descuento.
    ///
    /// <para>No es una regla de contabilidad: es un freno para que un dedo no le borre el mes a nadie.
    /// El criterio más severo del catálogo resta 20, así que cien deja sitio de sobra para pesar un
    /// hecho grave y aun así hace imposible el −500 por error de teclado. Si de verdad hace falta más,
    /// se aplican dos y cada uno dice su motivo — que además es mejor histórico que uno enorme sin
    /// desglosar.</para>
    /// </summary>
    public const int MinDescuento = -100;
}
