namespace AdminWeb.Client.Recorridos;

/// <summary>
/// Un recorrido guiado: lo que se le enseña a alguien que abre una pantalla por primera vez y no
/// sabe qué mira.
///
/// <para>Un recorrido pertenece a UNA pantalla, identificada por su ruta, y se lanza a mano desde el
/// botón de la barra superior. Nunca salta solo: la ayuda que se impone se cierra sin leer y a la
/// tercera vez se cierra antes de que termine de aparecer.</para>
///
/// <para>Los guiones viven en C# y no en JavaScript, y eso no es una preferencia de estilo. Es lo
/// único que permite que una prueba los LEA y compruebe, uno por uno, que cada paso apunta a un
/// control que sigue existiendo en el marcado. Con un recorrido por pantalla —más de sesenta—
/// escritos a mano por varias personas y a lo largo de meses, esa prueba es la diferencia entre una
/// ayuda que envejece bien y más de trescientos pasos que se pudren en silencio.</para>
/// </summary>
public sealed class Recorrido
{
    /// <summary>
    /// La ruta de la pantalla, COPIADA TAL CUAL de su directiva <c>@page</c>, incluidos los
    /// parámetros: <c>"/pool"</c>, <c>"/conocimiento/{Id:int}"</c>.
    ///
    /// <para>Se copia en vez de inventarse un identificador aparte por dos motivos: es lo que
    /// permite encontrar la pantalla en tiempo de ejecución sin ninguna tabla intermedia que
    /// mantener, y es lo que deja que la prueba verifique que la ruta EXISTE — un recorrido colgado
    /// de una ruta mal escrita no se lanzaría nunca y nadie se enteraría, porque el síntoma es que
    /// el botón está apagado, que es exactamente lo que se ve en una pantalla sin recorrido.</para>
    /// </summary>
    public string Ruta { get; }

    /// <summary>
    /// Cómo se llama el recorrido. Sale en el <c>title</c> del botón, así que es lo que alguien lee
    /// antes de decidir si lo lanza: «El pool de tickets», no «Recorrido del pool».
    /// </summary>
    public string Titulo { get; }

    /// <summary>Los pasos, en el orden en que se enseñan.</summary>
    public IReadOnlyList<Paso> Pasos { get; }

    public Recorrido(string ruta, string titulo, params Paso[] pasos)
    {
        Ruta = ruta;
        Titulo = titulo;
        Pasos = pasos;
    }

    /// <summary>
    /// Con qué nombre se recuerda que este recorrido ya se vio. Es la ruta, que ya es única por
    /// pantalla: un identificador aparte solo sería una segunda cosa que mantener sincronizada.
    /// </summary>
    public string Id => Ruta;

    public override string ToString() => $"«{Titulo}» ({Ruta})";
}

/// <summary>
/// Un paso del recorrido: un control señalado y una explicación.
/// </summary>
/// <param name="Marca">
/// El valor del atributo <c>data-recorrido</c> del control que se señala. Cadena vacía en el paso de
/// portada, que no señala nada (usa <see cref="Portada"/>).
/// </param>
/// <param name="Titulo">Cuatro palabras. Es el encabezado del globo.</param>
/// <param name="Texto">
/// Qué es y para qué sirve, en dos o tres frases. Se escribe para quien no ha usado nunca la
/// pantalla, y se dice lo que el control HACE, no cómo se llama — «desde aquí se aprueba la
/// solicitud y le llega el aviso a quien la pidió», no «botón Aprobar».
/// </param>
public sealed record Paso(string Marca, string Titulo, string Texto)
{
    /// <summary>
    /// EL ATRIBUTO. Los pasos se anclan con una marca explícita en el marcado y jamás con un
    /// selector de CSS sobre las clases de Radzen.
    ///
    /// <para>La diferencia importa más de lo que parece: <c>.rz-datagrid tbody tr:nth-child(2)
    /// button</c> se rompe el día que alguien añade una columna, y se rompe EN SILENCIO — el
    /// recorrido señala otra cosa o se salta el paso, y quien lo está siguiendo concluye que la
    /// ayuda miente. Una marca explícita se puede buscar con una búsqueda de texto, se ve al leer el
    /// marcado y, sobre todo, se puede PROBAR: es lo que permite que la prueba de la red de
    /// seguridad falle en cuanto alguien quita el control.</para>
    ///
    /// <para>El nombre está aquí, en una constante, porque el mismo literal vive también en el
    /// envoltorio de JavaScript. Hay una prueba que comprueba que los dos dicen lo mismo.</para>
    /// </summary>
    public const string Atributo = "data-recorrido";

    /// <summary>
    /// Dónde se coloca el globo respecto al control. Lo normal es no tocarlo: sin indicación, se
    /// elige el lado donde quepa. Se fija a mano solo cuando la elección automática tapa justo lo
    /// que se está explicando.
    /// </summary>
    public LadoDelPaso Lado { get; init; } = LadoDelPaso.Automatico;

    /// <summary>
    /// El primer paso de todo recorrido: qué es esta pantalla y para qué se entra en ella. No señala
    /// ningún control — sale centrado, sobre la pantalla entera.
    ///
    /// <para>Es obligatorio, y la prueba lo exige. Dos razones. La primera es que la pregunta que
    /// trae a alguien a pedir ayuda es «¿qué es esto?», y un recorrido que arranca explicando el
    /// tercer botón de la barra de filtros contesta a una pregunta que nadie hizo. La segunda es
    /// mecánica: los pasos cuyo control no está en pantalla se saltan, así que un recorrido hecho
    /// solo de controles puede quedarse SIN NINGÚN PASO según el rol de quien lo lanza o según si
    /// hay datos. La portada no se puede saltar, y por eso el botón nunca se queda sin decir
    /// nada.</para>
    /// </summary>
    public static Paso Portada(string titulo, string texto) => new(string.Empty, titulo, texto);

    /// <summary>Un paso sin marca es la portada: no señala nada y nunca se salta.</summary>
    public bool EsPortada => string.IsNullOrEmpty(Marca);

    /// <summary>El selector con el que se busca la marca en el documento.</summary>
    public string Selector => $"[{Atributo}=\"{Marca}\"]";

    public override string ToString() =>
        EsPortada ? $"portada «{Titulo}»" : $"«{Titulo}» → {Marca}";
}

/// <summary>
/// El lado por el que sale el globo. <see cref="Automatico"/> deja que se coloque donde quepa, que
/// es lo que se quiere casi siempre: una pantalla estrecha, una fila cerca del borde o un panel
/// desplegado cambian el sitio disponible, y un lado fijo acaba dejando el globo fuera de la vista.
/// </summary>
public enum LadoDelPaso
{
    Automatico,
    Arriba,
    Abajo,
    Izquierda,
    Derecha
}
