namespace AdminWeb.Client.Componentes;

/// <summary>
/// LO QUE SE ENSEÑA DE UNA FILA CUANDO NO CABE EN LA CELDA.
///
/// <para>El problema que resuelve es concreto: una celda de rejilla recorta. Una descripción de tres
/// párrafos se ve como media línea con puntos suspensivos y no hay forma de leerla entera —ni
/// ensanchando la columna, porque entonces la fila mide tres renglones y la rejilla deja de servir
/// para barrer—. Esta clase es el contenido de ese cuadro; quien lo pinta es
/// <c>CuadroDeDetalle.razor</c> y quien lo abre es <c>AvisosDeInterfaz.VerDetalleAsync</c>.</para>
///
/// <para><b>Los valores llegan YA ESCRITOS, como cadenas.</b> Es la decisión de fondo y no es
/// pereza: la rejilla escribe una fecha con <c>HorasDelPool.Limite</c>, un estado con la etiqueta
/// que manda el servidor y unas horas con su formato; si este cuadro recibiera el <c>DateTime</c> y
/// lo formateara por su cuenta, la misma fila diría dos cosas distintas según dónde se mirara —y la
/// que se corregiría el día que cambie el formato sería una de las dos—. La pantalla pasa
/// exactamente lo que ya pinta en la celda.</para>
///
/// <para>Hay DOS clases de campo y la diferencia es de pintado, no de importancia: los cortos van en
/// dos columnas —etiqueta y valor, uno por renglón— y los largos ocupan el ancho entero con su
/// texto íntegro debajo. Por eso se declaran con métodos distintos y por eso el cuadro los agrupa en
/// vez de intercalarlos: un párrafo de veinte líneas metido entre «Tipo» y «Puntos» rompe la lectura
/// de la lista de campos.</para>
/// </summary>
public sealed class DetalleDeFila
{
    /// <summary>
    /// Lo que se pinta donde no hay nada. El MISMO guion largo con el que las rejillas de esta
    /// aplicación escriben ya un campo vacío («—»), para que el cuadro y la celda digan lo mismo.
    ///
    /// <para>Un campo vacío se ENSEÑA en lugar de omitirse, y eso es a propósito: el cuadro de una
    /// misma rejilla tiene siempre la misma forma, así que se aprende dónde mirar. Escondiendo los
    /// vacíos, cada fila daría un cuadro distinto y quien busca «Quién la tiene» no sabría si la
    /// actividad no la tiene nadie o si el cuadro se olvidó de pintarlo.</para>
    /// </summary>
    public const string SinDato = "—";

    private readonly List<CampoDelDetalle> _campos = [];

    /// <param name="encabezado">Qué CLASE de fila es («Actividad del pool», «Ticket»). Es el título
    /// de la ventana, y va aparte del titular porque una cosa dice qué se está mirando y la otra
    /// cuál: un título de ticket de cien caracteres en la barra del cuadro no se lee.</param>
    /// <param name="titulo">El titular de esta fila en concreto. Puede venir vacío —hay rejillas
    /// cuya fila no tiene un campo que la nombre— y entonces el cuadro sencillamente no lo pinta.</param>
    public DetalleDeFila(string encabezado, string? titulo = null)
    {
        Encabezado = Limpiar(encabezado);
        Titulo = Limpiar(titulo);
    }

    /// <summary>Qué clase de fila es. Va en la barra de título del cuadro.</summary>
    public string Encabezado { get; }

    /// <summary>El titular de la fila, o cadena vacía si esa rejilla no tiene ninguno.</summary>
    public string Titulo { get; }

    /// <summary>Todos los campos, en el orden en que los declaró la pantalla.</summary>
    public IReadOnlyList<CampoDelDetalle> Campos => _campos;

    /// <summary>Los campos que caben en un renglón, en su orden.</summary>
    public IEnumerable<CampoDelDetalle> Cortos => _campos.Where(c => !c.EsTextoLargo);

    /// <summary>Los textos largos, en su orden. Son la razón de ser de este cuadro.</summary>
    public IEnumerable<CampoDelDetalle> Largos => _campos.Where(c => c.EsTextoLargo);

    /// <summary>Un campo de un renglón: un tipo, un estado, una fecha, un número.</summary>
    public DetalleDeFila Campo(string etiqueta, string? valor) => Agregar(etiqueta, valor, largo: false);

    /// <summary>
    /// Un campo de texto libre que en la celda no cabe: una descripción, un motivo, unas notas.
    /// Se pinta ENTERO y respetando sus saltos de línea.
    /// </summary>
    public DetalleDeFila Texto(string etiqueta, string? texto) => Agregar(etiqueta, texto, largo: true);

    private DetalleDeFila Agregar(string etiqueta, string? valor, bool largo)
    {
        // Solo se quitan los espacios de los EXTREMOS. El texto de dentro no se toca ni se acorta:
        // acortarlo aquí sería repetir dentro del cuadro el mismo recorte que obligó a abrirlo.
        var limpio = Limpiar(valor);

        // Un valor que YA LLEGA ESCRITO como el guion es un hueco, no un dato. No es un caso
        // rebuscado, es la consecuencia directa de que los valores vengan escritos por los mismos
        // ayudantes que pintan las celdas: HorasDelPool.Esfuerzo y HorasDelPool.Limite devuelven ese
        // mismo guion cuando no hay nada. Sin esta comprobación, el cuadro de una actividad del pool
        // que nadie ha tomado enseñaría «Quién la tiene: —» atenuado y «Esfuerzo: —» y «Entrega
        // esperada: —» a plena tinta: tres huecos con dos pesos distintos en el mismo cuadro, que es
        // exactamente lo que EstaVacio existe para evitar.
        var vacio = limpio.Length == 0 || limpio == SinDato;

        _campos.Add(new CampoDelDetalle(Limpiar(etiqueta), vacio ? SinDato : limpio, largo, vacio));
        return this;
    }

    private static string Limpiar(string? texto) => (texto ?? "").Trim();
}

/// <summary>
/// Un campo del cuadro: cómo se llama y lo que vale, ya escrito.
/// </summary>
/// <param name="Valor">Nunca nulo ni vacío: cuando no hay dato trae <see cref="DetalleDeFila.SinDato"/>,
/// para que quien lo pinta no tenga que acordarse del caso.</param>
/// <param name="EsTextoLargo">Si va a ancho completo y con sus saltos de línea respetados.</param>
/// <param name="EstaVacio">Que el valor que se pinta es el guion y no un dato. Se usa solo para
/// atenuarlo: un cuadro con seis guiones a todo color se lee como si dijeran algo. Vale igual para
/// el hueco que llegó vacío y para el que llegó ya escrito como <see cref="DetalleDeFila.SinDato"/>,
/// porque en el cuadro son el mismo hueco y tienen que verse igual.</param>
public sealed record CampoDelDetalle(string Etiqueta, string Valor, bool EsTextoLargo, bool EstaVacio);
