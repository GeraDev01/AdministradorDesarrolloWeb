namespace AdminWeb.Shared.Enums;

/// <summary>
/// Tipo de trabajo de una actividad del pool. Junto con la complejidad determina sus puntos.
///
/// <para><b>El valor nuevo va al final</b>, igual que en <see cref="PoolActivityStatus"/>: los
/// números están escritos en la columna de una base en producción y renumerar convertiría cada
/// actividad publicada en otra cosa. Dónde sale en pantalla lo decide el orden de la lista, no el
/// número.</para>
/// </summary>
public enum PoolWorkType
{
    Bug = 0,
    Tarea = 1,
    Requerimiento = 2,

    /// <summary>
    /// RETRABAJO: un bug sobre algo que YA SE ENTREGÓ.
    ///
    /// <para>Es la única clase de trabajo del pool que <b>vale puntos NEGATIVOS</b>, y ése es todo su
    /// motivo de existir. Un bug corriente es trabajo que había que hacer; volver a abrir un
    /// requerimiento que ya se dio por entregado es trabajo que no debería haber hecho falta, y
    /// pagarlo igual que al resto haría que entregar de más y corregir después saliera a cuenta.</para>
    ///
    /// <para>Se distingue del <see cref="Bug"/> por DE DÓNDE VIENE, no por lo que cuesta arreglarlo:
    /// nace de un ticket de bug de Azure DevOps que apunta a un requerimiento ya entregado. Por eso
    /// es una clasificación que hace el líder al publicar —o al clasificar lo que entró solo— y no
    /// algo que se pueda deducir de la actividad.</para>
    ///
    /// <para>En todo lo demás se comporta EXACTAMENTE como un bug —el plazo lo pone el líder, el
    /// esfuerzo lo estima quien lo toma— y eso no se comprueba tipo a tipo por ahí: lo dice
    /// <see cref="TipoDeTrabajoDelPool.ComoBug"/>, en un solo sitio.</para>
    /// </summary>
    Retrabajo = 3
}

/// <summary>
/// Lo que se puede preguntar sobre un tipo de trabajo sin repetir la lista de tipos por media
/// aplicación.
/// </summary>
public static class TipoDeTrabajoDelPool
{
    /// <summary>
    /// El tipo se comporta como un BUG en lo que toca a las HORAS: el <b>plazo</b> lo pone el líder
    /// al publicar —y es obligatorio, la matriz no lo pone por él— y el <b>esfuerzo</b> lo estima
    /// quien lo toma, en el momento de tomarlo.
    ///
    /// <para>Existe porque esa regla estaba escrita como <c>WorkType == PoolWorkType.Bug</c> en
    /// quince sitios repartidos entre el servicio, las dos pantallas del pool y sus validaciones. Al
    /// aparecer <see cref="PoolWorkType.Retrabajo"/> —que es un bug con otro precio— cada uno de esos
    /// quince habría tenido que acordarse, y el que se olvidara no fallaría: dejaría que el líder
    /// precargara un esfuerzo que le toca escribir a otra persona, o publicaría un retrabajo sin
    /// plazo. Con una sola frase, quien añada el tipo dieciséis lo decide aquí.</para>
    ///
    /// <para>Lo que NO decide es cuánto vale: eso sale de la matriz tipo × complejidad, donde el
    /// retrabajo lleva números negativos y el bug positivos.</para>
    /// </summary>
    public static bool ComoBug(this PoolWorkType tipo) =>
        tipo is PoolWorkType.Bug or PoolWorkType.Retrabajo;

    /// <summary>
    /// El tipo RESTA en vez de sumar: sus puntos son un descuento, no un premio.
    ///
    /// <para>Se pregunta por el TIPO y no por el signo de los puntos de la celda a propósito. El
    /// signo es un dato editable de la matriz y podría quedarse en cero un día por descuido; esto es
    /// la regla, y es la que valida que la celda de un retrabajo no pueda guardarse en positivo.</para>
    /// </summary>
    public static bool Resta(this PoolWorkType tipo) => tipo is PoolWorkType.Retrabajo;
}
