using System.Text;
using System.Text.RegularExpressions;
using AdminWeb.Shared.Dtos.Conocimiento;

namespace AdminWeb.Application.Services;

/// <summary>
/// Convierte el cuerpo de un artículo en BLOQUES y SEGMENTOS ya decididos. Es el equivalente de
/// <see cref="ForumRichText"/> para la base de conocimiento, y existe por la misma razón, con una
/// exigencia de más.
///
/// <para><b>El problema.</b> La documentación necesita títulos, listas y trozos de código: con texto
/// llano, un procedimiento de despliegue se lee como un párrafo interminable y nadie lo usa. Pero
/// producir HTML a partir de lo que teclea una persona —y que leen todas las demás— es un XSS
/// almacenado para el equipo entero, servido además desde el mismo origen que la aplicación y con
/// la sesión de quien lo abre.</para>
///
/// <para><b>La salida.</b> La misma que ya tomó el foro y que documenta <c>CuerpoDeForo.razor</c>:
/// no se construye marcado, se construyen COMPONENTES. Aquí el servidor devuelve una estructura
/// —bloques con renglones con segmentos— donde cada trozo trae dicho qué es (texto, negrita,
/// código, enlace), y el cliente pinta cada trozo con el componente que le toca emitiendo el texto
/// COMO TEXTO. Nunca <c>MarkupString</c>, nunca <c>innerHTML</c>. No hay un punto del camino donde
/// exista una cadena de marcado que alguien pudiera olvidarse de escapar, porque no se produce
/// ninguna.</para>
///
/// <para><b>El subconjunto es corto y cerrado</b>, y eso es la mitad de la defensa —lo que no se
/// interpreta no puede interpretarse mal—:</para>
/// <list type="bullet">
/// <item><c># </c>, <c>## </c>, <c>### </c> al principio de línea: títulos de tres niveles.</item>
/// <item><c>- </c> o <c>* </c>: punto de una lista. <c>1. </c>: punto de una lista numerada.</item>
/// <item>Tres comillas invertidas en su propia línea: bloque de código, con lenguaje opcional al lado.</item>
/// <item><c>**negrita**</c> y <c>`código`</c> dentro de una línea.</item>
/// <item>Enlaces, <c>[etiqueta](url)</c> o escritos tal cual.</item>
/// <item><c>![descripción](imagen:12)</c>, SOLA en su renglón: una imagen ya subida al artículo.</item>
/// </list>
/// <para>Todo lo demás es texto. No hay tablas, ni HTML incrustado, ni forma de meter un atributo en
/// ninguna parte.</para>
///
/// <para><b>LAS IMÁGENES SE NOMBRAN POR NÚMERO, y eso es la defensa entera.</b> La marca no admite
/// una dirección: admite el identificador de una imagen que ya está guardada en este artículo
/// (<c>KnowledgeImage</c>), y el bloque que sale de aquí lleva ese ENTERO y nada más. El navegador
/// arma con él la ruta de <c>/api/adjuntos/conocimiento/{id}</c>; no recibe ninguna cadena que
/// alguien haya tecleado, así que no hay dónde colar un <c>onerror</c>, ni un <c>javascript:</c>, ni
/// un <c>data:</c>, ni una dirección de fuera. Lo que no case con el patrón exacto —empezando por
/// cualquier cosa que no sean dígitos entre <c>imagen:</c> y el paréntesis— no es una imagen y se
/// queda como texto, que es como se queda todo lo que este subconjunto no reconoce.</para>
///
/// <para><b>Y el número tiene que ser de ESTE artículo</b>, no de cualquiera: quien llama a
/// <see cref="Analizar(string, IReadOnlySet{int})"/> le pasa los que el artículo tiene de verdad, y
/// un número que no esté ahí deja de ser una imagen y se queda en texto como todo lo demás. Sin esa
/// comprobación, la frase de arriba era mentira y se notaba en la pantalla: un número tecleado a
/// mano —o heredado de un cuerpo copiado de otro artículo— pintaba una etiqueta hacia una ruta que
/// contesta 404, y el lector se quedaba con el recuadro roto del navegador sin que nada le dijera
/// qué había pasado. Un cuerpo copiado de un BORRADOR ajeno o propio es el caso feo: su autor sigue
/// viendo la imagen —él sí puede— y el resto del equipo no ve nada. Con la marca a la vista como
/// texto, el defecto se lee y se arregla.</para>
///
/// <para>Una consecuencia de lo anterior, y es deliberada: <b>no se incrusta lo que vive fuera</b>.
/// <c>![x](https://otro-sitio/foto.png)</c> no pinta ninguna imagen; a lo sumo queda como enlace,
/// que es lo que ya hacía. Un artículo revisado y publicado cuya ilustración la sirve un tercero
/// puede cambiar de contenido después de la revisión sin que nadie se entere, y además cuenta a ese
/// tercero quién lo está leyendo y desde dónde.</para>
///
/// <para><b>Los enlaces NO se validan aquí.</b> Se delega en
/// <see cref="ForumRichText.EsEnlaceSeguro"/> a través de <see cref="ForumRichText.Analizar"/>, que
/// es el que ya decide qué dirección es aceptable —solo http y https; <c>javascript:</c>,
/// <c>data:</c> y compañía se quedan como texto llano—. Reescribir esa comprobación aquí habría
/// dejado dos definiciones de «enlace aceptable» que se irían separando con el tiempo, y la que se
/// quedara atrás sería un agujero. Y lo decide el SERVIDOR, como en el foro: el cliente corre en la
/// máquina de quien lee.</para>
/// </summary>
public static class ConocimientoTexto
{
    /// <summary>
    /// Tope del cuerpo, el mismo que el del foro y por el mismo saneador.
    ///
    /// <para>Veinte mil caracteres son unas ocho páginas: un artículo largo cabe de sobra. Lo que no
    /// cabe es un manual entero, y eso es deliberado — un texto que no entra aquí se parte en
    /// artículos enlazados y etiquetados, que es como se encuentra después. Un documento único de
    /// cien páginas no lo lee nadie y no lo encuentra el buscador.</para>
    /// </summary>
    public const int MaxCuerpo = ForumRichText.MaxCuerpo;

    /// <summary>
    /// Tope de bloques. Es defensivo: un cuerpo de veinte mil caracteres formado solo por líneas de
    /// un carácter produciría veinte mil bloques, y esa estructura viaja en cada lectura del
    /// artículo. Con este tope el peor caso está acotado sin que un artículo real lo roce nunca.
    /// </summary>
    public const int MaxBloques = 1000;

    /// <summary>Largo del extracto de una tarjeta. Más que esto deja de ser un extracto.</summary>
    public const int LargoExtracto = 220;

    /// <summary>
    /// Limpia el cuerpo antes de guardarlo: uniforma los saltos de línea, quita los caracteres de
    /// control invisibles —que rompen el pintado y sirven para disfrazar una dirección— y recorta al
    /// tope.
    ///
    /// <para>Es <see cref="ForumRichText.Sanear"/> tal cual, no una copia. El saneado del texto que
    /// escribe una persona y leen todas las demás tiene que ser UNO: dos implementaciones acaban
    /// divergiendo, y la que se quede corta es por donde entra el problema. Y como allá,
    /// deliberadamente NO escapa HTML: no hace falta, porque el cuerpo nunca se convierte en
    /// marcado, y escaparlo estropearía lo que la gente escribe (quien documente «si a &lt; b»
    /// acabaría leyendo «a &amp;lt; b» en su propio artículo).</para>
    /// </summary>
    public static string Sanear(string? cuerpo) => ForumRichText.Sanear(cuerpo);

    // ── Análisis por bloques ─────────────────────────────────────────────────────

    private static readonly Regex Titulo = new(@"^(?<gato>\#{1,3})\s+(?<txt>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Vineta = new(@"^\s{0,3}[-*]\s+(?<txt>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Numerada = new(@"^\s{0,3}\d{1,3}[.)]\s+(?<txt>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Solo letras, dígitos y los signos que llevan los nombres de lenguaje reales (c#, c++, f#).</summary>
    private static readonly Regex LenguajeValido = new(@"^[A-Za-z0-9+#._-]{1,20}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// La marca de una imagen del artículo, y hay que leerla con cuidado porque es estrecha a
    /// propósito: la línea ENTERA, sin nada delante ni detrás, y entre <c>imagen:</c> y el paréntesis
    /// solo dígitos. Cualquier otra cosa —una dirección, un espacio, unas comillas, un atributo
    /// colado— no casa, y lo que no casa es texto.
    ///
    /// <para>La descripción admite lo mismo que la etiqueta de un enlace del foro (todo menos
    /// <c>]</c> y los saltos de línea) porque va a salir como TEXTO, no dentro de marcado: quien la
    /// pinta la emite como el atributo <c>alt</c> y como pie, y ahí Blazor escapa lo que haga falta.
    /// Puede ir vacía —una captura que se explica sola—.</para>
    ///
    /// <para>El largo sale de <see cref="MaxDescripcionDeImagen"/> y no escrito a mano: es el mismo
    /// número por el que recorta <see cref="Marca"/>, y con la cifra en dos sitios bastaría con
    /// cambiar uno para que las marcas que se producen dejaran de reconocerse.</para>
    /// </summary>
    private static readonly Regex Imagen = new(
        $@"^!\[(?<txt>[^\]\r\n]{{0,{MaxDescripcionDeImagen}}})\]\(imagen:(?<id>\d{{1,9}})\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parte el cuerpo en bloques listos para pintar. Nunca lanza: un cuerpo raro devuelve bloques
    /// raros, no una excepción — un artículo que reventara al leerse sería imposible de arreglar,
    /// porque para arreglarlo hay que poder abrirlo.
    /// </summary>
    /// <param name="imagenesDelArticulo">
    /// Los números de las imágenes que el artículo tiene de verdad. Una marca que nombre otro número
    /// no es una imagen: se queda en texto, a la vista, que es la única forma de que el defecto se
    /// note y se pueda arreglar.
    ///
    /// <para><b>Nulo significa «no lo compruebes», y solo vale cuando no hay artículo del que
    /// preguntar</b> — el resumen de una tarjeta, o una prueba de la gramática—. Ahí no importa: un
    /// resumen se salta las imágenes de todas formas, así que reconocer una de más no enseña marcado
    /// a nadie. Lo que se pinta como artículo va SIEMPRE con la lista puesta.</para>
    /// </param>
    public static List<ConocimientoBloqueDto> Analizar(string? cuerpo, IReadOnlySet<int>? imagenesDelArticulo = null)
    {
        var texto = Sanear(cuerpo);
        if (texto.Length == 0) return [];

        var bloques = new List<ConocimientoBloqueDto>();
        var lineas = texto.Split('\n');

        // Lo que se está acumulando: las líneas de un párrafo, o los puntos de una lista.
        var parrafo = new List<string>();
        var puntos = new List<string>();
        bool listaNumerada = false;

        void CerrarParrafo()
        {
            if (parrafo.Count == 0) return;
            // Las líneas de un párrafo se unen conservando su salto: quien escribió una dirección y
            // debajo su explicación quiere verlas en dos renglones, y el contenedor las respeta.
            bloques.Add(new ConocimientoBloqueDto(
                TipoDeBloque.Parrafo, [new ConocimientoRenglonDto(AnalizarLinea(string.Join("\n", parrafo)))]));
            parrafo.Clear();
        }

        void CerrarLista()
        {
            if (puntos.Count == 0) return;
            bloques.Add(new ConocimientoBloqueDto(
                TipoDeBloque.Lista,
                puntos.Select(p => new ConocimientoRenglonDto(AnalizarLinea(p))).ToList(),
                Numerada: listaNumerada));
            puntos.Clear();
        }

        void Cerrar() { CerrarParrafo(); CerrarLista(); }

        for (int i = 0; i < lineas.Length && bloques.Count < MaxBloques; i++)
        {
            var linea = lineas[i];
            var recortada = linea.TrimEnd();

            // ── Bloque de código ────────────────────────────────────────────
            // Se mira ANTES que nada: dentro de él no se interpreta ni un título ni una viñeta, que
            // es justo lo que hace útil un bloque de código (un script lleno de «# comentario» no
            // puede convertirse en una ristra de títulos).
            if (recortada.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Cerrar();

                var etiqueta = recortada.TrimStart()[3..].Trim();
                var lenguaje = LenguajeValido.IsMatch(etiqueta) ? etiqueta.ToLowerInvariant() : null;

                var codigo = new List<string>();
                i++;
                // Sin cierre, el bloque llega hasta el final del artículo: es lo que más se parece a
                // lo que quiso quien escribió, y desde luego mejor que descartar el resto del texto.
                while (i < lineas.Length && !lineas[i].TrimEnd().TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codigo.Add(lineas[i]);
                    i++;
                }

                bloques.Add(new ConocimientoBloqueDto(
                    TipoDeBloque.Codigo,
                    // Texto CRUDO, un renglón por línea y sin analizar nada: dentro de un bloque de
                    // código, una dirección es código y no un enlace que pulsar.
                    codigo.Select(c => new ConocimientoRenglonDto([new ConocimientoSegmentoDto(c, Codigo: true)])).ToList(),
                    Lenguaje: lenguaje));
                continue;
            }

            // ── Línea en blanco: separa bloques ─────────────────────────────
            if (recortada.Trim().Length == 0) { Cerrar(); continue; }

            // ── Imagen ──────────────────────────────────────────────────────
            // Va DESPUÉS del bloque de código, como todo: dentro de tres comillas invertidas, la
            // marca de una imagen es texto de un ejemplo y no una imagen. Y es un bloque propio, no
            // un trozo de línea, porque lo que se pinta ocupa el ancho de la columna: una imagen
            // metida en mitad de un párrafo no tendría dónde ponerse.
            //
            // El patrón acota el número a nueve dígitos, así que cabe en un entero; la conversión aun
            // así puede fallar, porque «\d» reconoce también los dígitos de otros alfabetos —los
            // árabes, los de ancho completo— que int.TryParse no lee, y entonces esto no es ninguna
            // imagen. El cero tampoco lo es, ni un número que no sea de ESTE artículo: los tres casos
            // se quedan como texto, a la vista, en vez de dejar un hueco roto en mitad del artículo.
            var imagen = Imagen.Match(recortada.Trim());
            if (imagen.Success && int.TryParse(imagen.Groups["id"].Value, out var idImagen) && idImagen > 0
                && (imagenesDelArticulo is null || imagenesDelArticulo.Contains(idImagen)))
            {
                Cerrar();
                bloques.Add(new ConocimientoBloqueDto(
                    TipoDeBloque.Imagen,
                    // La descripción viaja como TEXTO y sin analizar: dentro del pie de una imagen no
                    // hay enlaces que pulsar ni negritas que pintar, hay lo que se lee cuando la
                    // imagen no se puede ver.
                    [new ConocimientoRenglonDto([new ConocimientoSegmentoDto(imagen.Groups["txt"].Value.Trim())])],
                    ImagenId: idImagen));
                continue;
            }

            // ── Título ──────────────────────────────────────────────────────
            var titulo = Titulo.Match(recortada);
            if (titulo.Success)
            {
                Cerrar();
                bloques.Add(new ConocimientoBloqueDto(
                    TipoDeBloque.Titulo,
                    [new ConocimientoRenglonDto(AnalizarLinea(titulo.Groups["txt"].Value.Trim()))],
                    Nivel: titulo.Groups["gato"].Value.Length));
                continue;
            }

            // ── Punto de una lista ──────────────────────────────────────────
            var vineta = Vineta.Match(recortada);
            var numerada = vineta.Success ? Match.Empty : Numerada.Match(recortada);

            if (vineta.Success || numerada.Success)
            {
                CerrarParrafo();
                bool esNumerada = numerada.Success;
                // Cambiar de viñetas a números (o al revés) empieza otra lista: son dos listas
                // distintas, y meterlas en una sola pintaría con el mismo signo cosas que el autor
                // escribió con signos diferentes.
                if (puntos.Count > 0 && esNumerada != listaNumerada) CerrarLista();

                listaNumerada = esNumerada;
                puntos.Add((esNumerada ? numerada : vineta).Groups["txt"].Value.Trim());
                continue;
            }

            // ── Cualquier otra cosa: párrafo ────────────────────────────────
            CerrarLista();
            parrafo.Add(recortada);
        }

        Cerrar();

        // Si se llegó al tope, se dice. Cortar en seco dejaría a quien lee creyendo que el artículo
        // terminaba ahí, que es la única forma de que un recorte haga daño de verdad.
        if (bloques.Count >= MaxBloques)
            bloques.Add(new ConocimientoBloqueDto(
                TipoDeBloque.Parrafo, [new ConocimientoRenglonDto([new ConocimientoSegmentoDto("…")])]));

        return bloques;
    }

    // ── Análisis dentro de una línea ─────────────────────────────────────────────

    // Código PRIMERO y negrita después: dentro de `code` no hay negritas ni enlaces, así que la
    // alternativa de código tiene que ganar cuando las dos podrían casar sobre el mismo texto.
    private static readonly Regex Marcas = new(
        @"`(?<cod>[^`\r\n]{1,1000})`|\*\*(?<neg>[^*\r\n]{1,1000})\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Trocea una línea en segmentos. Lo que no es negrita ni código se pasa por
    /// <see cref="ForumRichText.Analizar"/>, que es quien decide qué es un enlace y con qué
    /// dirección — la misma decisión, en el mismo sitio, que en el foro.
    /// </summary>
    public static List<ConocimientoSegmentoDto> AnalizarLinea(string? linea)
    {
        if (string.IsNullOrEmpty(linea)) return [];

        var salida = new List<ConocimientoSegmentoDto>();
        int cursor = 0;

        foreach (Match m in Marcas.Matches(linea))
        {
            if (m.Index < cursor) continue;   // solapamiento: no debería pasar, pero no se arriesga

            if (m.Index > cursor) AgregarLlano(salida, linea[cursor..m.Index]);

            if (m.Groups["cod"].Success)
                salida.Add(new ConocimientoSegmentoDto(m.Groups["cod"].Value, Codigo: true));
            else
                salida.Add(new ConocimientoSegmentoDto(m.Groups["neg"].Value, Negrita: true));

            cursor = m.Index + m.Length;
        }

        if (cursor < linea.Length) AgregarLlano(salida, linea[cursor..]);
        return salida;
    }

    /// <summary>
    /// Mete un trozo de texto normal, buscándole antes los enlaces con el analizador del foro.
    /// Concatenar los textos resultantes devuelve exactamente lo que el lector ve.
    /// </summary>
    private static void AgregarLlano(List<ConocimientoSegmentoDto> salida, string trozo)
    {
        if (trozo.Length == 0) return;

        var partes = ForumRichText.Analizar(trozo);
        if (partes.Count == 0) { salida.Add(new ConocimientoSegmentoDto(trozo)); return; }

        foreach (var p in partes)
            salida.Add(new ConocimientoSegmentoDto(p.Texto, p.Url));
    }

    // ── La marca de una imagen ───────────────────────────────────────────────────

    /// <summary>
    /// Largo de la descripción de una imagen. Es el mismo que el de la etiqueta de un enlace, y por
    /// la misma razón: pasado ese punto ya no describe, cuenta.
    /// </summary>
    public const int MaxDescripcionDeImagen = 300;

    /// <summary>
    /// La marca que hay que escribir en el cuerpo para que se vea una imagen ya subida.
    ///
    /// <para><b>La escribe el servidor, no la pantalla</b>, y por eso está aquí: la sintaxis y quien
    /// la interpreta tienen que ser lo mismo. Con el editor armando la cadena por su cuenta, el día
    /// que el patrón cambiara habría dos definiciones y la de la pantalla seguiría produciendo marcas
    /// que ya no se reconocen — o sea, artículos con la marca a la vista en lugar de la imagen.</para>
    ///
    /// <para>La descripción se limpia de lo que rompería la propia marca (el corchete de cierre y los
    /// saltos de línea) en vez de rechazarse: viene del nombre del archivo, y un nombre raro no es
    /// motivo para no poder poner la captura. Lo que quede se puede reescribir a mano, que es
    /// justamente lo que hay que hacer con ella.</para>
    /// </summary>
    public static string Marca(int imagenId, string? descripcion)
    {
        var limpia = (descripcion ?? "")
            .Replace('\r', ' ').Replace('\n', ' ')
            .Replace("[", "").Replace("]", "")
            .Trim();

        if (limpia.Length > MaxDescripcionDeImagen) limpia = limpia[..MaxDescripcionDeImagen].TrimEnd();

        return $"![{limpia}](imagen:{imagenId})";
    }

    // ── Texto llano ──────────────────────────────────────────────────────────────

    /// <summary>
    /// El artículo sin su marcado, en una sola línea: es lo que se pinta en una tarjeta de resultado
    /// y lo que se le enseña a quien busca.
    ///
    /// <para>Se calcula recorriendo los BLOQUES ya analizados, no con expresiones regulares sobre el
    /// texto crudo: así el extracto no puede enseñar un <c>**</c>, ni la marca de una imagen, ni la
    /// dirección larga de un enlace cuya etiqueta era «la guía», que es lo que pasaría al limpiar la
    /// fuente a mano. Y si mañana se añade una forma al marcado, el extracto la entiende sin
    /// tocarlo.</para>
    ///
    /// <para>Las imágenes se saltan ENTERAS, descripción incluida. Un resumen es para decidir si
    /// abrir el artículo, y el nombre con el que se pegó una captura —«captura_2026-08-12T…»— no
    /// ayuda a decidir nada; ocupa el sitio de la frase que sí lo haría.</para>
    ///
    /// <para><b>Salvo que no quede nada más.</b> Un artículo puede ser tres capturas y ni una frase
    /// —el mínimo para mandarlo a revisar lo cumplen las marcas ellas solas—, y saltárselas todas
    /// dejaba la tarjeta del buscador con el renglón del resumen EN BLANCO, que se lee como una
    /// pantalla rota y no dice de qué va el artículo. En ese caso se dice cuántas imágenes hay, que
    /// es el único dato cierto que queda.</para>
    ///
    /// <para><b>Aquí las marcas se cuentan SIN comprobar contra la base</b>, al revés que en
    /// <see cref="Analizar(string, IReadOnlySet{int})"/>, y es a propósito. Una marca cuyo número no
    /// sea de este artículo la pantalla la pinta como texto, así que el resumen dirá «una imagen» de
    /// algo que se lee como texto; el precio de afinarlo sería dejar que el resumen escupiera
    /// <c>![x](imagen:9)</c> en la tarjeta del buscador, que es justo lo que este método existe para
    /// evitar. Entre un recuento que se pasa por uno y marcado crudo en pantalla, se prefiere lo
    /// primero.</para>
    /// </summary>
    public static string Extracto(string? cuerpo, int largo = LargoExtracto)
    {
        var plano = new StringBuilder();
        int imagenes = 0;

        foreach (var bloque in Analizar(cuerpo))
        {
            if (bloque.Tipo == TipoDeBloque.Imagen) { imagenes++; continue; }

            foreach (var renglon in bloque.Renglones)
            {
                foreach (var s in renglon.Segmentos)
                {
                    if (plano.Length > 0 && plano[^1] != ' ') plano.Append(' ');
                    plano.Append(s.Texto.Replace('\n', ' '));
                }
                if (plano.Length > largo * 2) break;   // ya sobra de dónde recortar
            }
            if (plano.Length > largo * 2) break;
        }

        // Espacios repetidos: los deja el pegado de renglones, y en una tarjeta se ven como huecos.
        var texto = Regex.Replace(plano.ToString().Trim(), @"\s{2,}", " ");

        // El recuento no puede quedarse corto por el corte de arriba: solo se sale antes de tiempo
        // cuando ya sobra texto, y entonces esta rama no se pisa.
        if (texto.Length == 0 && imagenes > 0)
            return imagenes == 1 ? "Una imagen, sin texto que resumir."
                                 : $"{imagenes} imágenes, sin texto que resumir.";

        return texto.Length <= largo ? texto : texto[..largo].TrimEnd() + "…";
    }
}
