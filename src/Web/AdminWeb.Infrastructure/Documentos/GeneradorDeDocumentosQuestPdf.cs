using AdminWeb.Domain.Documentos;
using AdminWeb.Shared.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdminWeb.Infrastructure.Documentos;

/// <summary>
/// Los tres documentos, maquetados con QuestPDF.
///
/// <b>Esta clase es el único sitio del proyecto que sabe qué librería de PDF se usa.</b> Es la razón
/// de que exista <see cref="IGeneradorDeDocumentos"/>: si algún día hay que cambiar de librería —por
/// licencia o por lo que sea— se escribe otra implementación y nadie más se entera.
///
/// No consulta la base ni mira quién pide el documento: recibe los datos ya resueltos y los maqueta.
/// Así el contenido se prueba sin abrir un PDF, y aquí solo puede fallar la presentación.
/// </summary>
public class GeneradorDeDocumentosQuestPdf : IGeneradorDeDocumentos
{
    /// <summary>
    /// Se declara la licencia una sola vez y al cargar el tipo. QuestPDF exige que se declare antes
    /// de generar nada; hacerlo en cada documento sería repetirlo, y olvidarlo en uno haría fallar
    /// justo ese —un fallo que solo aparece en producción y solo en un documento.
    /// </summary>
    static GeneradorDeDocumentosQuestPdf() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private const string Tinta = "#1f2937";
    private const string Tenue = "#6b7280";
    private const string Linea = "#d1d5db";

    // Verde y rojo de la sección de fortalezas y debilidades. Son los MISMOS del escritorio, para
    // que el documento no cambie de aspecto al cambiar de aplicación. Los oscuros van en el texto y
    // los claros en el título: al revés, el cuerpo se leería mal impreso en blanco y negro.
    private const string Verde = "#2e7d32";
    private const string VerdeOscuro = "#1b5e20";
    private const string Rojo = "#c62828";
    private const string RojoOscuro = "#8e1b1b";

    // ── Solicitud de vacaciones ─────────────────────────────────────────────────

    public byte[] SolicitudDeVacaciones(DatosDeVacaciones d) =>
        Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
                Hoja(pagina);

                pagina.Header().Element(e => Titulo(e, "Solicitud de vacaciones", d.FechaSolicitud));

                pagina.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Element(e => Campos(e,
                    [
                        ("Nombre", d.Nombre),
                        ("Departamento", d.Departamento),
                        ("Puesto", d.Puesto),
                        ("Jefe directo", d.JefeDirecto),
                        ("Fecha de ingreso", d.FechaIngreso)
                    ]));

                    col.Item().Element(e => Campos(e,
                    [
                        ("Días solicitados", d.TotalDias),
                        ("Período", d.Periodo),
                        ("Del", d.FechaInicio),
                        ("Al", d.FechaFin),
                        ("Se presenta a trabajar el", d.FechaRegreso),
                        ("Días pendientes", d.DiasPendientes)
                    ]));

                    // Las dos casillas se imprimen SIEMPRE, marcada la que corresponda. Enseñar solo
                    // la resolución dejaría un documento que no se parece al que se firma a mano, y
                    // este acaba archivado en el expediente de la persona.
                    col.Item().Element(e => Autorizacion(e, d.Autorizada, d.Rechazada));

                    if (!string.IsNullOrWhiteSpace(d.Observaciones))
                        col.Item().Element(e => Bloque(e, "Observaciones", d.Observaciones));

                    col.Item().PaddingTop(24)
                       .Element(e => Firmas(e, d.Nombre, d.JefeDirecto, d.FirmaDelJefe, d.FirmaDelColaborador));
                });

                pagina.Footer().Element(PieDePagina);
            });
        }).GeneratePdf();

    private static void Autorizacion(IContainer c, bool autorizada, bool rechazada) =>
        c.Border(1).BorderColor(Linea).Padding(10).Row(fila =>
        {
            fila.RelativeItem().Text("Autorización del jefe directo").SemiBold().FontColor(Tinta);
            fila.ConstantItem(110).Text($"[{(autorizada ? "X" : " ")}]  Autorizada");
            fila.ConstantItem(110).Text($"[{(rechazada ? "X" : " ")}]  Rechazada");
        });

    /// <summary>
    /// El pie de firmas. La del colaborador iba siempre en blanco —se firmaba a mano sobre el papel
    /// impreso— y ahora entra cuando la persona firmó su solicitud desde la web. El hueco se sigue
    /// reservando igual si no la hay: es el mismo papel, con la raya esperando una pluma.
    /// </summary>
    private static void Firmas(IContainer c, string colaborador, string jefe, byte[]? firmaDelJefe,
        byte[]? firmaDelColaborador) =>
        c.Row(fila =>
        {
            fila.RelativeItem().Element(e => Firma(e, colaborador, "Colaborador", firmaDelColaborador));
            fila.ConstantItem(40);
            fila.RelativeItem().Element(e => Firma(e, jefe, "Jefe directo", firmaDelJefe));
        });

    private static void Firma(IContainer c, string nombre, string papel, byte[]? png) =>
        c.Column(col =>
        {
            // Se reserva la altura tenga firma o no: sin eso, el documento sin firmar y el firmado
            // saldrían con las líneas a distinta altura y no parecerían el mismo papel.
            col.Item().Height(50).AlignBottom().Element(e =>
            {
                if (png is { Length: > 0 }) e.MaxHeight(48).Image(png).FitHeight();
                else e.Text("");
            });
            col.Item().PaddingTop(2).BorderTop(1).BorderColor(Tinta);
            col.Item().PaddingTop(3).Text(nombre).SemiBold().FontSize(9).FontColor(Tinta);
            col.Item().Text(papel).FontSize(8).FontColor(Tenue);
        });

    // ── Ficha de desarrollador ──────────────────────────────────────────────────

    public byte[] FichaDeDesarrollador(DatosDeFicha d) =>
        Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
                Hoja(pagina);

                pagina.Header().Element(e => Titulo(e, d.NombreCompleto, $"Generado el {d.GeneradoEl}"));

                pagina.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Element(e => Campos(e,
                    [
                        ("Correo", d.Correo ?? "—"),
                        ("Teléfono", d.Telefono ?? "—"),
                        ("Nivel", d.Nivel ?? "—"),
                        ("Equipo", d.Equipo ?? "Sin equipo"),
                        ("Rol", d.RolEnElEquipo ?? "—"),
                        ("Ingreso", d.FechaDeIngreso?.ToString("dd/MM/yyyy") ?? "—")
                    ]));

                    col.Item().Row(fila =>
                    {
                        fila.RelativeItem().Element(e => Indicador(e, "Puntos del año", d.PuntosAprobadosDelAnio.ToString()));
                        fila.RelativeItem().Element(e => Indicador(e, "Tiempo registrado", d.TiempoTotal));
                        fila.RelativeItem().Element(e => Indicador(e, "Asignaciones activas", d.AsignacionesActivas.ToString()));
                    });

                    // FORTALEZAS Y DEBILIDADES de la evaluación MÁS RECIENTE, destacadas aparte.
                    //
                    // Están además dentro de su tarjeta más abajo, junto al resto del historial;
                    // esto es lo que el escritorio ponía primero y en color. La diferencia importa:
                    // quien abre esta ficha —normalmente para una conversación de desempeño— viene a
                    // por lo de AHORA, y tenerlo que buscar entre ocho evaluaciones anteriores hace
                    // que se lea la primera que aparece, que es la más vieja.
                    col.Item().Element(e => Seccion(e, "Fortalezas y debilidades"));
                    col.Item().Element(e => UltimaEvaluacion(e, d.Evaluaciones));

                    col.Item().Element(e => Seccion(e, "Evaluaciones"));
                    if (d.Evaluaciones.Count == 0)
                        col.Item().Text("Todavía no tiene evaluaciones.").FontColor(Tenue).FontSize(9);
                    else
                        foreach (var ev in d.Evaluaciones)
                            col.Item().Element(e => Evaluacion(e, ev));

                    col.Item().Element(e => Seccion(e, "Hitos"));
                    if (d.Hitos.Count == 0)
                        col.Item().Text("Sin hitos registrados.").FontColor(Tenue).FontSize(9);
                    else
                        foreach (var h in d.Hitos)
                            col.Item().Element(e => Hito(e, h));
                });

                pagina.Footer().Element(PieDePagina);
            });
        }).GeneratePdf();

    /// <summary>
    /// Lo más reciente que se dijo de esta persona, en verde y rojo como en el escritorio.
    ///
    /// <para>El color NO es decoración: separa de un vistazo lo que se le reconoce de lo que se le
    /// pide, y en una conversación de desempeño esas dos listas se leen por separado. En gris, como
    /// estaban dentro de la tarjeta, se leen como un párrafo más.</para>
    ///
    /// <para>Se toma la PRIMERA de la lista porque llegan ordenadas de más reciente a más antigua;
    /// es la misma suposición que hacía el escritorio y la que sostiene el título de la sección.</para>
    /// </summary>
    private static void UltimaEvaluacion(IContainer c, IReadOnlyList<EvaluacionImpresa> evaluaciones)
    {
        if (evaluaciones.Count == 0)
        {
            c.Text("(sin evaluaciones registradas todavía)").FontColor(Tenue).FontSize(9);
            return;
        }

        var ultima = evaluaciones[0];

        c.PaddingBottom(8).Column(col =>
        {
            col.Item().Text($"Según la evaluación del {ultima.Fecha:dd/MM/yyyy}" +
                            (string.IsNullOrWhiteSpace(ultima.Periodo) ? "" : $" · {ultima.Periodo}"))
                .FontSize(8).FontColor(Tenue);

            col.Item().PaddingTop(4).Text("Fortalezas").SemiBold().FontColor(Verde);
            col.Item().Text(Vacio(ultima.Fortalezas, "(no se anotaron)")).FontSize(9).FontColor(VerdeOscuro);

            col.Item().PaddingTop(4).Text("Debilidades / áreas de mejora").SemiBold().FontColor(Rojo);
            col.Item().Text(Vacio(ultima.Debilidades, "(no se anotaron)")).FontSize(9).FontColor(RojoOscuro);
        });
    }

    /// <summary>El texto, o una nota en su lugar cuando está vacío. Un hueco en blanco en un papel
    /// oficial se lee como un error de impresión, no como «no había nada que decir».</summary>
    private static string Vacio(string? texto, string siNoHay) =>
        string.IsNullOrWhiteSpace(texto) ? siNoHay : texto.Trim();

    private static void Evaluacion(IContainer c, EvaluacionImpresa ev) =>
        c.PaddingBottom(8).Border(1).BorderColor(Linea).Padding(8).Column(col =>
        {
            col.Item().Row(fila =>
            {
                fila.RelativeItem().Text($"{ev.Fecha:dd/MM/yyyy}{(ev.Periodo is null ? "" : $" · {ev.Periodo}")}")
                    .SemiBold().FontColor(Tinta);
                // Sobre CINCO, que es la escala real de la evaluación («1 Deficiente» … «5
                // Sobresaliente»). Ponerla sobre diez convertiría un 4 —que es «Bueno»— en un 40 %
                // para quien lea el papel sin conocer la escala, y este documento acaba en el
                // expediente de una persona.
                fila.ConstantItem(90).AlignRight()
                    .Text(ev.Calificacion is int n ? $"{n} / 5 · {EtiquetaDeCalificacion(n)}" : "sin calificar")
                    .FontColor(Tenue);
            });

            if (!string.IsNullOrWhiteSpace(ev.Fortalezas)) col.Item().Element(e => Etiquetado(e, "Fortalezas", ev.Fortalezas));
            if (!string.IsNullOrWhiteSpace(ev.Debilidades)) col.Item().Element(e => Etiquetado(e, "A mejorar", ev.Debilidades));
            if (!string.IsNullOrWhiteSpace(ev.Comentarios)) col.Item().Element(e => Etiquetado(e, "Comentarios", ev.Comentarios));

            if (!string.IsNullOrWhiteSpace(ev.Evaluador))
                col.Item().PaddingTop(4).Text($"Evaluó: {ev.Evaluador}").FontSize(8).FontColor(Tenue);
        });

    private static void Hito(IContainer c, HitoImpreso h) =>
        c.PaddingBottom(5).Row(fila =>
        {
            fila.ConstantItem(72).Text($"{h.Fecha:dd/MM/yyyy}").FontSize(9).FontColor(Tenue);
            fila.ConstantItem(96).Text(EtiquetaDeHito(h.Tipo)).FontSize(9).FontColor(Tenue);
            fila.RelativeItem().Column(col =>
            {
                col.Item().Text(h.Titulo).SemiBold().FontSize(9).FontColor(Tinta);
                if (!string.IsNullOrWhiteSpace(h.Descripcion))
                    col.Item().Text(h.Descripcion).FontSize(9).FontColor(Tenue);
            });
        });

    /// <summary>
    /// Las mismas palabras que ofrece el desplegable al evaluar. Van junto al número porque un «4»
    /// suelto no dice nada a quien lee el documento meses después.
    /// </summary>
    private static string EtiquetaDeCalificacion(int n) => n switch
    {
        1 => "Deficiente",
        2 => "Bajo",
        3 => "Aceptable",
        4 => "Bueno",
        5 => "Sobresaliente",
        _ => ""
    };

    private static string EtiquetaDeHito(MilestoneKind k) => k switch
    {
        MilestoneKind.Logro => "Logro",
        MilestoneKind.Proyecto => "Proyecto",
        MilestoneKind.Certificacion => "Certificación",
        MilestoneKind.Reconocimiento => "Reconocimiento",
        _ => "Otro"
    };

    // ── El organigrama ──────────────────────────────────────────────────────────
    //
    // ESTO ERA UNA LISTA y ahora es un diagrama, que es lo que se pidió. Merece explicación porque
    // parece un capricho de forma y no lo es: en la lista, el equipo se reconocía por una franja de
    // color a la izquierda y todo lo demás —descripción, líder, integrantes— era texto corrido; para
    // saber cuánta gente lleva un equipo había que contar nombres separados por puntos. En el
    // diagrama, cada equipo es una caja que se ve entera de un golpe, y el tamaño de la caja ES el
    // tamaño del equipo. Eso es lo que se mira en un organigrama.
    //
    // POR QUÉ EL PAPEL NO IMPORTA EL DIBUJO DE LA PANTALLA, que era la vía obvia teniendo el diagrama
    // ya dibujado allá. Mientras aquel fue un SVG la tentación tenía hasta nombre —QuestPDF admite
    // Svg(string), desde 2024.3—; hoy la pantalla dibuja con elementos normales del navegador (cambió
    // para poder arrastrar: los elementos de un SVG no admiten «draggable»), así que ni siquiera queda
    // un dibujo que mandar. Las cuatro razones de entonces no dependían de que fuera un SVG y siguen
    // en pie por si algún día vuelve a haber uno:
    //
    //   · El diagrama de la pantalla pinta TODO con variables del tema (var(--rz-…)). Fuera del
    //     navegador nadie resuelve esas variables: el diagrama saldría en negro sobre negro o
    //     directamente sin pintar. Habría que mantener una segunda copia del dibujo con colores fijos.
    //   · Un dibujo importado entra como UNA imagen. Una imagen no se parte por la mitad, así que un
    //     organigrama de veinte equipos o cabe encogido hasta ser ilegible en una hoja, o no cabe.
    //     Aquí las cajas son contenido de verdad y QuestPDF las va pasando de página él solo.
    //   · El texto de una imagen lo dibuja Skia como trazos. En el PDF que sale de aquí los nombres
    //     son TEXTO: se buscan con Ctrl+F, se copian y se leen con un lector de pantalla. Es un
    //     documento que se archiva y se reparte, no una captura de pantalla.
    //   · Y el detalle que lo cierra: el corte de línea. Una descripción de equipo de tres renglones
    //     hay que partirla a mano contando caracteres a ojo; aquí la parte QuestPDF con la métrica
    //     real de la fuente.
    //
    // Lo que sí se conserva es el CONCEPTO: mismo nodo arriba, mismas cajas, mismo orden y la misma
    // caja final de «sin equipo». Quien vea la pantalla y luego la primera hoja del papel reconoce de
    // qué documento se trata.
    //
    // En lo que NO se parecen es en CÓMO DIBUJAN LA JERARQUÍA, y no es un descuido: la pantalla es un
    // organigrama clásico —la caja arriba y sus hijos debajo, en una fila— y aquí los subequipos van
    // con SANGRÍA. En pantalla el lienzo se desplaza de lado y el árbol puede crecer a lo ancho todo
    // lo que haga falta; el papel no crece, y un árbol de arriba abajo dobla su ancho en cada nivel,
    // así que a la tercera generación o se encoge hasta no leerse o se sale de la hoja. Reducirlo
    // para que quepa arregla un diagrama en una pantalla, donde se puede acercar; éste se imprime.
    //
    // Tampoco reparten el sitio igual, y es por lo mismo: la pantalla tiene UNA superficie que se
    // desplaza y se pliega, así que ahí cabe el árbol entero de corrido; el papel no se desplaza pero
    // sí pasa de hoja, así que aquí cada rama se lleva la suya.
    // No es que se contradigan —el orden, los nombres y de quién cuelga cada uno salen de la misma
    // consulta—: es la misma estructura repartida como cada soporte puede. Y por lo mismo el papel
    // sigue listando los sistemas y los proyectos de cada equipo por su nombre, que es lo que se viene
    // a mirar cuando este documento se lleva a una junta.
    //
    // ── CÓMO SE REPARTE EL PAPEL DESDE QUE UN EQUIPO PUEDE COLGAR DE OTRO ───────────────────────────
    //
    // QuestPDF pagina solo, y parte por donde le toca. Con un árbol eso es lo peor que puede pasar:
    // una rama cortada por la mitad deja unas cajas abriendo la hoja siguiente sin decir de dónde
    // vienen, y eso no se lee como un organigrama partido, se lee como OTRO organigrama. Así que la
    // hoja se reparte por RAMAS y no por altura:
    //
    //   · La PRIMERA hoja es la organización de un vistazo: el nodo de arriba y los equipos RAÍZ, en
    //     filas por el ancho del papel. Mientras nadie cuelgue un equipo de otro —que es como está la
    //     casa hoy— no hay ninguna hoja más y el documento sale exactamente igual que antes.
    //   · Cada equipo RAÍZ que tiene subequipos se lleva su rama ENTERA —hijos, nietos y lo que haya—
    //     a una hoja propia, con su nombre en la cabecera. Los subequipos de en medio NO abren hoja
    //     aparte: van en la de su raíz, que es donde se lee de un golpe cómo encaja la rama; por eso
    //     su caja dice cuántos le cuelgan pero no manda a ninguna hoja. Una rama no comparte hoja con
    //     otra nunca, y si es tan grande que no cabe, las hojas que siguen repiten esa misma cabecera:
    //     se sabe de qué rama son sin buscar hacia atrás. Esto es lo que da declarar una `Page` por
    //     rama en vez de meter saltos de página: cada `Page` empieza en hoja nueva y tiene su propia
    //     cabecera repetida.
    //   · Dentro de una rama el árbol se dibuja con SANGRÍA, no de arriba abajo. Un dibujo de arriba
    //     abajo dobla su ancho en cada nivel y la hoja no crece: a la tercera generación o se encoge
    //     hasta no leerse o se sale del papel. La sangría crece en línea recta, solo por la izquierda,
    //     y admite la profundidad que haga falta. Y sobre todo se puede CORTAR sin perder el hilo: en
    //     un dibujo de arriba abajo el corte parte un renglón de hermanos y deja en la hoja anterior
    //     las líneas que los unían, mientras que aquí lo peor que pasa es que una caja se parta por
    //     dentro y su gente siga en la hoja siguiente. Eso ÚLTIMO sí ocurre —QuestPDF parte por donde
    //     le cabe, no por donde nos gustaría— y el fragmento que abre la hoja no repite el nombre del
    //     equipo; lo que sigue diciendo de quién es todo eso es la cabecera de la hoja, que es de la
    //     rama. Forzar que la caja no se parta (ShowEntire) no es opción: un equipo con más gente de
    //     la que cabe en una hoja no se podría dibujar y el documento entero dejaría de generarse.
    //   · Lo que NO se hizo: encoger el diagrama para que quepa. Escalar es lo que arregla un
    //     organigrama en una pantalla, donde se puede acercar; esto se imprime y se reparte, y a la
    //     segunda reducción los nombres hay que leerlos con lupa.

    /// <summary>Cuántas cajas por fila en la hoja de la organización. Tres caben legibles en una carta
    /// apaisada; cuatro dejan los nombres largos partidos en dos renglones y la caja se vuelve un
    /// párrafo.</summary>
    private const int CajasPorFila = 3;

    /// <summary>El hueco entre dos cajas vecinas de la primera hoja. Se reparte como relleno a los dos
    /// lados de cada celda —media a cada una— en vez de pedírselo a la fila: así la celda mide un
    /// tercio exacto del papel y su centro cae donde la barra y las bajadas dan por hecho que cae.</summary>
    private const float EspacioEntreCajas = 10f;

    /// <summary>Lo que se mete hacia la derecha cada nivel dentro de la hoja de una rama.</summary>
    private const float Sangria = 18f;

    /// <summary>
    /// A partir de aquí la sangría deja de crecer. Veinte niveles se comerían un tercio del ancho de
    /// la hoja y las cajas del fondo saldrían como una columna de palabras sueltas. Que dos niveles
    /// muy hondos se dibujen a la misma altura es un dibujo peor; una caja que no se puede leer es un
    /// documento peor — y de quién cuelga cada equipo sigue estando escrito dentro de su caja.
    /// </summary>
    private const int NivelesConSangria = 8;

    /// <summary>Lo que baja la línea desde el equipo de arriba hasta el codo que entra en el subequipo.</summary>
    private const float AlturaDelCodo = 9f;

    /// <summary>El hueco entre la caja de un subequipo y la siguiente. Va DENTRO de la caja —debajo de
    /// ella— y no entre una y otra, para que las verticales del árbol lo crucen sin cortarse.</summary>
    private const float EspacioEntreSubequipos = 8f;

    public byte[] OrganizacionDeEquipos(DatosDeEquipos d) => DocumentoDelOrganigrama(d).GeneratePdf();

    /// <summary>El documento antes de convertirlo en bytes. Va aparte porque el diagrama es lo único
    /// que se revisa mirándolo, y así se puede pedir una página como imagen sin generar el PDF.</summary>
    private static IDocument DocumentoDelOrganigrama(DatosDeEquipos d) =>
        Document.Create(doc =>
        {
            // El árbol se monta UNA vez, antes de repartir hojas: la primera necesita saber qué
            // equipos tienen rama para no dibujarlos dos veces, y cada una de las demás es una rama.
            var ramas = Ramas(d.Equipos);

            doc.Page(pagina => HojaDeLaOrganizacion(pagina, d, ramas));

            foreach (var rama in ramas.Where(r => r.Subequipos.Count > 0))
                doc.Page(pagina => HojaDeUnaRama(pagina, d, rama));
        });

    /// <summary>
    /// La primera hoja: el nodo de arriba, los equipos RAÍZ en filas y la caja de quien no está en
    /// ninguno.
    ///
    /// <para>Los subequipos NO se dibujan aquí, aunque llegan en la misma lista: cada uno está en la
    /// hoja de su rama, y repetirlos sería imprimir dos veces la gente de un equipo grande y dejar al
    /// lector sin saber cuál de las dos cajas es la buena. Lo que sí queda aquí es el aviso: la caja
    /// de un equipo con rama dice cuántos subequipos tiene y en qué hoja están.</para>
    /// </summary>
    private static void HojaDeLaOrganizacion(PageDescriptor pagina, DatosDeEquipos d, IReadOnlyList<Rama> ramas)
    {
        HojaApaisada(pagina);

        pagina.Header().Element(e => Titulo(e, "Organigrama de equipos", $"Generado el {d.GeneradoEl}"));

        pagina.Content().PaddingVertical(10).Column(col =>
        {
            col.Item().Element(e => NodoRaiz(e, d, ramas));

            // Las cajas en el orden en que se leen: los equipos raíz y, al final, la de quien no
            // está en ninguno. Va con ellas y no en una nota al pie porque en un organigrama esa
            // caja es media pregunta del que lo abre.
            var cajas = new List<Rama>(ramas);
            if (d.SinEquipo.Count > 0)
                cajas.Add(new Rama(new EquipoImpreso(
                    "Sin equipo", "Todavía no están asignados a ninguno.", null, null,
                    // Sin padre, y no porque falte el dato: no es un equipo, es la caja de quien no
                    // está en ninguno. Colgarla de algo sería inventar una estructura.
                    null,
                    d.SinEquipo, [], []), []));

            if (cajas.Count == 0)
            {
                col.Item().PaddingTop(20).AlignCenter()
                  .Text("Todavía no hay equipos ni personas que dibujar.")
                  .FontSize(10).FontColor(Tenue);
                return;
            }

            for (int desde = 0; desde < cajas.Count; desde += CajasPorFila)
            {
                var fila = cajas.Skip(desde).Take(CajasPorFila).ToList();

                col.Item().Element(e => Reparto(e, fila.Count));
                col.Item().PaddingBottom(12).Row(r =>
                {
                    // El hueco entre cajas se hace con relleno DENTRO de cada celda y no con el
                    // «Spacing» de la fila, y de eso depende que las líneas caigan donde deben: con
                    // Spacing las celdas miden (ancho − huecos)/3 y sus centros dejan de estar en los
                    // tercios del papel, mientras que la barra y las bajadas —que se reparten el ancho
                    // en tercios limpios— sí. Salían tres puntos y pico corridas, lo justo para que la
                    // barra no llegara a tocar la bajada de los extremos. Con relleno, la celda mide un
                    // tercio exacto, su centro es el tercio y todo el mundo mide igual.
                    foreach (var caja in fila)
                        // Un equipo raíz no hereda color de nadie: o tiene el suyo, o el gris.
                        r.RelativeItem().PaddingHorizontal(EspacioEntreCajas / 2)
                         .Element(x => CajaDeEquipo(x, caja.Equipo,
                            ColorDeLaBanda(caja.Equipo.ColorHex, Linea),
                            caja.CuantosSubequipos, suRamaEnOtraHoja: caja.Subequipos.Count > 0));

                    // Los huecos de la última fila se reservan igual. Sin esto, dos equipos solos se
                    // estirarían a media hoja cada uno y no parecerían del mismo tamaño que los de la
                    // fila de arriba: en un diagrama, el tamaño se lee como un dato.
                    for (int hueco = fila.Count; hueco < CajasPorFila; hueco++)
                        r.RelativeItem();
                });
            }
        });

        pagina.Footer().Element(PieDePagina);
    }

    /// <summary>
    /// La hoja de una rama: el árbol que cuelga de un equipo, con la profundidad que haga falta.
    ///
    /// <para>El equipo del que cuelga todo esto NO se vuelve a dibujar aquí: da nombre a la hoja. Su
    /// caja, con su gente, está en la primera, y la cabecera lo dice para que nadie la busque.</para>
    /// </summary>
    private static void HojaDeUnaRama(PageDescriptor pagina, DatosDeEquipos d, Rama rama)
    {
        HojaApaisada(pagina);

        // Esta cabecera se repite en TODAS las hojas de la rama, que es lo que impide que una rama
        // larga se lea como dos organigramas: la segunda hoja sigue diciendo de quién es.
        pagina.Header().Element(e => Titulo(e,
            $"{rama.Equipo.Nombre} y sus subequipos",
            $"{rama.CuantosSubequipos} subequipo(s) · {rama.CuantasPersonas} persona(s) en la rama · " +
            $"Generado el {d.GeneradoEl}"));

        // La SECCIÓN es lo que deja que la caja de la primera hoja diga «su rama, en la hoja 3» con el
        // número de verdad. Se nombra con el nombre del equipo, que no se repite: la pantalla no deja
        // crear dos equipos que se llamen igual.
        pagina.Content().Section(rama.Equipo.Nombre).PaddingVertical(10).Column(col =>
        {
            col.Item().PaddingBottom(8)
               .Text($"La caja de «{rama.Equipo.Nombre}», con su gente, está en la primera hoja.")
               .FontSize(8).FontColor(Tenue);

            Subequipos(col, rama.Subequipos, [], ColorDeLaBanda(rama.Equipo.ColorHex, Linea));
        });

        pagina.Footer().Element(PieDePagina);
    }

    /// <summary>
    /// Los subequipos de un equipo, cada uno con lo suyo debajo y una sangría más adentro.
    ///
    /// <para>Se escriben en la MISMA columna que sus padres —no una columna dentro de otra— para que
    /// el corte de página caiga como mucho dentro de UNA caja y nunca dentro de un dibujo anidado que
    /// arrastre consigo a la rama entera. La jerarquía la dibuja la sangría, no el anidamiento de
    /// contenedores.</para>
    /// </summary>
    /// <param name="verticales">Por cada nivel de encima, si a AQUEL antepasado le quedan hermanos
    /// más abajo. De eso depende que su vertical siga pasando de largo por el costado de esta caja,
    /// y es lo que une a un subequipo con su padre cuando entre los dos hay una rama entera: sin esa
    /// vertical, el codo del segundo hermano sale flotando en el blanco, a la altura de una caja con
    /// la que no tiene nada que ver.</param>
    private static void Subequipos(ColumnDescriptor col, IReadOnlyList<Rama> ramas,
        IReadOnlyList<bool> verticales, string colorDeLaRama)
    {
        for (int i = 0; i < ramas.Count; i++)
        {
            var rama = ramas[i];
            bool ultimo = i == ramas.Count - 1;
            var color = ColorDeLaBanda(rama.Equipo.ColorHex, colorDeLaRama);

            col.Item().Element(e => FilaDeSubequipo(e, rama, verticales, ultimo, color));

            // El hijo hereda las verticales de sus antepasados y añade la del padre, que solo sigue
            // bajando si a éste le quedan hermanos por dibujar.
            Subequipos(col, rama.Subequipos, [.. verticales, !ultimo], color);
        }
    }

    /// <summary>
    /// La caja de un subequipo con las líneas que la unen a su rama: las verticales de sus
    /// antepasados a la izquierda y su propio codo pegado a ella.
    ///
    /// <para><b>Las líneas van en una CAPA de fondo y la caja en la primaria</b>, y no una al lado de
    /// otra dentro de una fila. Es lo que hace que una vertical mida lo que mide la caja: en una fila,
    /// una celda solo mide lo que mida su contenido —una línea sin altura propia mide cero— y para
    /// estirarla haría falta pedir todo el espacio disponible, con lo que cada caja acabaría midiendo
    /// la hoja entera. Con capas, la que manda la altura es la caja y las líneas se ajustan a ella.</para>
    /// </summary>
    private static void FilaDeSubequipo(IContainer c, Rama rama, IReadOnlyList<bool> verticales,
        bool ultimo, string color)
    {
        // La sangría deja de crecer pasado el tope, así que a partir de ahí las verticales de más
        // adentro no se dibujan: caerían todas en la misma columna, una encima de otra.
        int pasos = Math.Min(verticales.Count, NivelesConSangria);

        c.Layers(capas =>
        {
            capas.Layer().Row(r =>
            {
                for (int j = 0; j < pasos; j++)
                {
                    var celda = r.ConstantItem(Sangria);
                    if (verticales[j]) celda.Element(VerticalDeLargo);
                }

                r.ConstantItem(Sangria).Element(ultimo ? CodoFinal : Codo);
                r.RelativeItem();
            });

            capas.PrimaryLayer()
                 .PaddingLeft((pasos + 1) * Sangria).PaddingBottom(EspacioEntreSubequipos)
                 .Element(e => CajaDeEquipo(e, rama.Equipo, color, rama.CuantosSubequipos,
                     // Su rama está justo debajo, en esta misma hoja: no hay a dónde mandar al lector.
                     suRamaEnOtraHoja: false));
        });
    }

    /// <summary>La vertical de un antepasado que todavía tiene hermanos debajo: pasa de largo por el
    /// costado de esta caja y del hueco que la separa de la siguiente.</summary>
    private static void VerticalDeLargo(IContainer c) =>
        c.AlignLeft().LineVertical(0.8f).LineColor(Linea);

    /// <summary>
    /// La línea que baja del equipo de arriba y entra en la caja del subequipo, y SIGUE bajando
    /// porque a este subequipo le quedan hermanos.
    ///
    /// <para>Ocupa un hueco de exactamente una sangría de ancho: la bajada se pega a su borde
    /// izquierdo y el travesaño lo cruza entero, de la bajada al borde de la caja. Por construcción no
    /// puede sobrar ni faltar por los extremos, porque aquí no hay celdas ni centros que puedan no
    /// coincidir: la bajada es el borde del hueco y el travesaño es el hueco.</para>
    ///
    /// <para><b>La bajada y el travesaño van en CAPAS y no uno debajo del otro en una columna.</b> En
    /// una columna, el último trozo —la bajada que tiene que seguir hasta el hermano de abajo— mide lo
    /// que mida su contenido, y una línea vertical no trae altura propia: medía CERO y no se dibujaba
    /// nada. El resultado era que el codo de cada subequipo salía flotando, con su palito y su
    /// travesaño, sin ninguna línea que lo uniera al de al lado; una hoja de rama con dos subequipos
    /// enseñaba dos marcas sueltas en el margen y ni un árbol. Con capas la altura la manda la bajada,
    /// que pide toda la que haya —la misma que la caja, porque quien manda ahí es la capa primaria de
    /// <see cref="FilaDeSubequipo"/>—, y el travesaño se pinta encima a su altura.</para>
    /// </summary>
    private static void Codo(IContainer c) =>
        c.Layers(capas =>
        {
            capas.PrimaryLayer().Element(VerticalDeLargo);
            capas.Layer().Element(Travesano);
        });

    /// <summary>El travesaño que cruza la sangría, de la bajada al borde de la caja, a la altura del
    /// renglón del nombre.</summary>
    private static void Travesano(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Height(AlturaDelCodo);
            col.Item().LineHorizontal(0.8f).LineColor(Linea);
        });

    /// <summary>El mismo codo, pero de la ÚLTIMA caja de su rama: la bajada se corta ahí, que es lo
    /// que dice que por debajo ya no cuelga nada de ese padre.</summary>
    private static void CodoFinal(IContainer c) =>
        c.Column(col =>
        {
            col.Item().Height(AlturaDelCodo).AlignLeft().LineVertical(0.8f).LineColor(Linea);
            col.Item().LineHorizontal(0.8f).LineColor(Linea);
        });

    /// <summary>
    /// El nodo de arriba: de quién cuelga todo esto y cuánta gente hay en total. Sin él, las cajas
    /// sueltas son una cuadrícula; con él, son un organigrama.
    /// </summary>
    private static void NodoRaiz(IContainer c, DatosDeEquipos d, IReadOnlyList<Rama> ramas) =>
        c.Column(col =>
        {
            // Cuántos equipos NO se dibujan en esta hoja porque están en la de su rama. Sin este
            // renglón, quien cuente las cajas de aquí y las compare con el «12 equipo(s)» de arriba
            // pensaría que al documento le faltan equipos.
            int enOtrasHojas = ramas.Sum(r => r.CuantosSubequipos);

            col.Item().AlignCenter().Border(1).BorderColor(Tinta).Padding(8).Column(caja =>
            {
                caja.Item().AlignCenter().Text("Organización").SemiBold().FontSize(13).FontColor(Tinta);
                caja.Item().AlignCenter().Text(
                    $"{d.Equipos.Count} equipo(s) · {d.TotalPersonas} persona(s)" +
                    (d.SinEquipo.Count > 0 ? $" · {d.SinEquipo.Count} sin equipo" : ""))
                    .FontSize(8).FontColor(Tenue);

                if (enOtrasHojas > 0)
                    caja.Item().AlignCenter()
                        .Text($"{enOtrasHojas} de ellos son subequipos y van en la hoja de su rama")
                        .FontSize(8).FontColor(Tenue);
            });

            col.Item().Height(10).AlignCenter().LineVertical(0.8f).LineColor(Linea);
        });

    /// <summary>
    /// La barra de la que cuelgan las cajas de una fila de la primera hoja, con su bajada a cada una.
    ///
    /// <para>Se repite en CADA fila, y no solo bajo el nodo de arriba, porque una fila que empieza en
    /// la página siguiente aparecería suelta: sin la barra, esas cajas parecerían colgar de las de la
    /// fila de arriba, y las filas de esta hoja son un reparto del ancho del papel, no la jerarquía.
    /// De quién cuelga cada equipo lo dice su caja, con todas las letras: «Subequipo de …».</para>
    ///
    /// <para>Lo que la barra reparte sí es el primer nivel del árbol: en esta hoja solo hay equipos
    /// RAÍZ —más la caja de quien no está en ninguno—, y todos cuelgan de la organización de verdad.
    /// Lo que sigue siendo un reparto del ancho, y no jerarquía, es CUÁLES caen en el mismo renglón.
    /// Los niveles de abajo no se dibujan aquí: van en la hoja de su rama, con sangría y codos.</para>
    /// </summary>
    private static void Reparto(IContainer c, int cuantas) =>
        c.Column(col =>
        {
            float derecha = ExtremoDerechoDeLaBarra(cuantas);

            col.Item().Row(barra =>
            {
                barra.RelativeItem(0.5f);
                barra.RelativeItem(derecha - 0.5f).LineHorizontal(0.8f).LineColor(Linea);
                barra.RelativeItem(CajasPorFila - derecha);
            });

            // Sin «Spacing», por lo mismo que la fila de las cajas: las bajadas tienen que caer en
            // los tercios limpios del papel, que es donde empieza y acaba la barra de aquí arriba.
            col.Item().Height(10).Row(r =>
            {
                for (int i = 0; i < CajasPorFila; i++)
                {
                    var celda = r.RelativeItem();
                    if (i < cuantas) celda.AlignCenter().LineVertical(0.8f).LineColor(Linea);
                }
            });
        });

    /// <summary>
    /// Dónde ACABA la barra de una fila, medido en celdas: 0,5 es el centro de la primera —por donde
    /// baja su caja— y <see cref="CajasPorFila"/> el borde derecho del papel.
    ///
    /// <para>La barra va de la PRIMERA bajada a la ÚLTIMA y no de borde a borde de las cajas. Las
    /// bajadas salen del CENTRO de cada celda, así que una barra que ocupara celdas enteras asomaría
    /// media celda por cada extremo —más de cien puntos en una carta apaisada— y esos dos trozos
    /// colgarían sin unir nada. Tampoco cruza la hoja entera: en la última fila, que casi nunca va
    /// completa, una barra de lado a lado parece que va a repartir a cajas que no están.</para>
    ///
    /// <para><b>De ahí el mínimo, que es lo que aquí importa.</b> Con UNA sola caja, «de la primera
    /// bajada a la última» da una barra de ancho cero: no se dibuja nada y lo único que queda es la
    /// bajada. Y una bajada suelta no se lee como «cuelga de la organización», se lee como que cuelga
    /// de lo que tenga justo encima —en la primera fila, un palmo de blanco bajo el nodo; en las
    /// demás, la caja de la fila anterior, o sea «Equipo 7 cuelga de Equipo 4» cuando los dos son
    /// equipos raíz—. Por eso la barra llega como mínimo al centro del papel, que además es por donde
    /// baja el nodo de la organización: en la primera fila empalma con él y en las demás basta para
    /// leerse como barra. Con dos cajas o más la cuenta de siempre ya da igual o más, así que el
    /// mínimo solo toca el caso de la caja sola.</para>
    ///
    /// <para>Está aparte del dibujo, y no metido en <see cref="Reparto"/>, porque es una cuenta que se
    /// puede comprobar sin generar el documento: el fallo que arregla no reventaba nada, dibujaba una
    /// línea que decía algo falso, y eso ninguna prueba de «que salga» lo atrapa.</para>
    /// </summary>
    internal static float ExtremoDerechoDeLaBarra(int cuantas) =>
        Math.Max(cuantas - 0.5f, CajasPorFila / 2f);

    /// <summary>
    /// La caja de un equipo: banda de color, nombre, a qué se dedica y su gente.
    ///
    /// <para><b>El color va en la banda de arriba y NUNCA detrás de un texto.</b> Lo teclea una
    /// persona en una caja de texto, así que puede ser cualquier cosa —un amarillo pálido o un azul
    /// marino—; con el nombre del equipo encima, el primero deja el texto ilegible en blanco y el
    /// segundo en negro, y no hay forma de acertar sin adivinar el color. En una banda maciza sobre
    /// el borde de la caja no hay nada que leer encima: el color identifica, y el texto se lee
    /// siempre en tinta sobre papel.</para>
    /// </summary>
    /// <param name="color">Ya resuelto por quien dibuja: el suyo, el heredado de su rama o el gris.
    /// La caja no lo decide porque para decidirlo hay que saber de qué rama cuelga, y eso es del
    /// árbol, no de la caja.</param>
    /// <param name="subequipos">Cuántos equipos cuelgan de éste en TODA su rama, no solo los directos.</param>
    /// <param name="suRamaEnOtraHoja">Si sus subequipos están en otra hoja hay que mandar allí al
    /// lector; si están justo debajo, decírselo sería mandarlo a donde ya está mirando.</param>
    private static void CajaDeEquipo(IContainer c, EquipoImpreso eq, string color, int subequipos,
        bool suRamaEnOtraHoja) =>
        c.Border(1).BorderColor(Linea).Column(col =>
        {
            col.Item().Height(6).Background(color);

            col.Item().Padding(8).PaddingBottom(6).Column(cab =>
            {
                cab.Item().Text(eq.Nombre).SemiBold().FontSize(12).FontColor(Tinta);

                // De quién cuelga, cuando cuelga de alguien. Va ESCRITO además de dibujado con el
                // codo, porque el codo no siempre está: la caja del padre puede estar en la PRIMERA
                // hoja mientras ésta abre la de su rama, y el corte de página puede dejar esta caja
                // arrancando una hoja lejos de su codo. Una línea de texto se lee igual en cualquier
                // sitio del documento.
                //
                // Solo se imprime si lo hay, al revés que la descripción: aquella deja el hueco para
                // que todas las cajas midan igual, pero un «(sin padre)» en la mayoría de las cajas
                // sería ruido en la línea que más se mira.
                if (!string.IsNullOrWhiteSpace(eq.EquipoPadre))
                    cab.Item().PaddingTop(2).Text($"Subequipo de {eq.EquipoPadre}")
                        .FontSize(8).FontColor(Tenue);

                // Cuántos le cuelgan y, si están en otra hoja, en cuál.
                //
                // Se cuenta la RAMA ENTERA y no solo los subequipos directos, y por eso lo dice con
                // esas palabras: el número tiene que cuadrar con lo que el lector va a encontrar al
                // pasar la hoja —tantas cajas como dice— y con el que la cabecera de esa hoja repite.
                // Dos cuentas distintas del mismo equipo en dos renglones del mismo documento es lo
                // que hace que se desconfíe de los dos.
                //
                // El número de hoja lo pone QuestPDF al cerrar el documento: escribirlo a mano sería
                // contar hojas aquí y equivocarse en cuanto una rama creciera y empujara a las demás.
                if (subequipos > 0)
                    cab.Item().PaddingTop(2).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor(Tenue));
                        t.Span($"{subequipos} subequipo(s) en su rama");
                        if (suRamaEnOtraHoja)
                        {
                            t.Span(" · en la hoja ");
                            t.BeginPageNumberOfSection(eq.Nombre);
                        }
                    });

                // La descripción se imprime SIEMPRE, aunque no la haya. Un renglón que falta en una
                // caja y está en la de al lado descoloca la fila entera y se lee como que a esa caja
                // le pasa algo; el paréntesis dice lo que ocurre de verdad, que es que nadie la ha
                // escrito todavía.
                cab.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(eq.Descripcion)
                        ? "(sin descripción)"
                        : eq.Descripcion)
                    .FontSize(8).FontColor(Tenue);

                cab.Item().PaddingTop(3).Text($"{eq.Integrantes.Count} persona(s)")
                    .FontSize(8).FontColor(Tenue);
            });

            col.Item().LineHorizontal(0.8f).LineColor(Linea);

            col.Item().Padding(8).PaddingTop(6).Column(gente =>
            {
                gente.Spacing(4);

                if (eq.Integrantes.Count == 0)
                    gente.Item().Text("(sin integrantes)").FontSize(8).FontColor(Tenue);

                foreach (var p in eq.Integrantes)
                    gente.Item().Element(e => PersonaImpresa(e, p));

                if (eq.Sistemas.Count > 0)
                    gente.Item().PaddingTop(4).Text($"Sistemas: {string.Join(", ", eq.Sistemas)}")
                        .FontSize(7).FontColor(Tenue);
                if (eq.Proyectos.Count > 0)
                    gente.Item().Text($"Proyectos: {string.Join(", ", eq.Proyectos)}")
                        .FontSize(7).FontColor(Tenue);
            });
        });

    /// <summary>
    /// Una persona dentro de su caja: nombre, nivel y rol en una línea, y su función debajo.
    ///
    /// <para>Al LÍDER se le pone una raya vertical al lado y el nombre en negrita. No lleva ningún
    /// pictograma —ni corona ni estrella— por lo mismo que se quitaron los de las rejillas: los dibuja
    /// la fuente del sistema, cambian de forma según la máquina y donde no hay fuente de emoji salen
    /// como un cuadro vacío. Una raya la dibuja el propio documento y sale igual en cualquier
    /// impresora, y además el rol ya dice «Líder» con todas sus letras.</para>
    /// </summary>
    private static void PersonaImpresa(IContainer c, IntegranteImpreso p) =>
        // Los dos con el MISMO sangrado: el borde del líder se pinta dentro del margen y no empuja al
        // texto, así que con menos relleno su nombre arrancaría dos puntos antes que los demás y la
        // columna de nombres saldría descuadrada justo en el renglón que más se mira.
        (p.EsLider ? c.BorderLeft(2).BorderColor(Tinta) : c).PaddingLeft(7)
        .Column(col =>
        {
            col.Item().Text(t =>
            {
                var nombre = t.Span(p.Nombre).FontSize(9).FontColor(Tinta);
                if (p.EsLider) nombre.Bold(); else nombre.SemiBold();

                if (!string.IsNullOrWhiteSpace(p.Nivel))
                    t.Span($" ({p.Nivel})").FontSize(7).FontColor(Tenue);
                t.Span($" · {p.Rol}").FontSize(7).FontColor(Tenue);
            });

            // La función solo ocupa sitio si existe. Al revés que la descripción del equipo, aquí NO
            // se imprime un «(sin función)»: son tantos renglones como personas tenga el equipo, y
            // repetir un paréntesis vacío quince veces convierte la caja en una lista de excusas.
            if (!string.IsNullOrWhiteSpace(p.Funcion))
                col.Item().Text(p.Funcion).FontSize(7.5f).FontColor(Tenue);
        });

    /// <summary>
    /// El color de la banda de un equipo: <b>el suyo si lo tiene, y si no el de la rama de la que
    /// cuelga</b>; el gris de siempre cuando en toda la rama nadie eligió ninguno.
    ///
    /// <para><b>El equipo NO pierde su color por colgar de otro.</b> Lo eligió una persona para ese
    /// equipo, y pintarlo del color del padre sería tirar esa decisión sin avisar y dejar a los
    /// subequipos de una rama indistinguibles entre sí, que es justo para lo que sirve el color.</para>
    ///
    /// <para><b>Pero el que no tiene ninguno hereda el de arriba en vez de caer al gris.</b> El gris es
    /// el mismo del borde de la caja: un subequipo sin color queda con una banda que no se ve, y en la
    /// hoja de una rama —donde las cajas van una debajo de otra— eso rompe la columna de color justo
    /// donde se está leyendo que todas son de la misma rama. Un subequipo recién creado casi nunca
    /// tiene color; heredarlo dice la verdad («es de esta rama») y no inventa ninguna elección.</para>
    /// </summary>
    private static string ColorDeLaBanda(string? propio, string heredado) =>
        EsColorEscrito(propio) ? propio! : heredado;

    /// <summary>
    /// ¿Es un «#RRGGBB» de verdad? El valor lo escribe una persona en una caja de texto, y un color
    /// mal escrito no debe impedir imprimir el documento a todo el mundo.
    /// </summary>
    private static bool EsColorEscrito(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#' && hex[1..].All(Uri.IsHexDigit);

    // ── El árbol, montado desde la lista plana ──────────────────────────────────

    /// <summary>
    /// Un equipo con lo que cuelga de él.
    ///
    /// <para>El servidor manda los equipos PLANOS y en orden de dibujo —cada padre delante de su
    /// rama— porque un contrato que se refiere a sí mismo convierte un dato mal grabado en un
    /// serializador dando vueltas. Aquí se monta el árbol otra vez porque el papel sí necesita saber
    /// qué cuelga de qué: para decidir qué rama se lleva una hoja y cuánta sangría le toca a cada
    /// caja. Montarlo no es reordenar: los hermanos se quedan en el orden en que llegaron.</para>
    /// </summary>
    private sealed record Rama(EquipoImpreso Equipo, IReadOnlyList<Rama> Subequipos)
    {
        /// <summary>Cuántos equipos cuelgan de éste a cualquier profundidad, sin contarlo a él.</summary>
        public int CuantosSubequipos => Subequipos.Sum(s => 1 + s.CuantosSubequipos);

        /// <summary>La gente de toda la rama, la suya y la de sus subequipos.</summary>
        public int CuantasPersonas => Equipo.Integrantes.Count + Subequipos.Sum(s => s.CuantasPersonas);
    }

    /// <summary>
    /// Monta las ramas a partir de la lista plana: devuelve los equipos RAÍZ, cada uno con lo suyo
    /// colgando.
    ///
    /// <para>El padre viaja por su NOMBRE, que es lo que trae el contrato del documento —aquí no hay
    /// identificadores— y sirve igual porque no puede haber dos equipos que se llamen igual. Un padre
    /// que no está en la lista se trata como si no lo hubiera: el equipo sale como raíz en vez de
    /// desaparecer, y en un organigrama faltar no es una caja menos, es un equipo que oficialmente no
    /// está en ninguna parte.</para>
    /// </summary>
    private static IReadOnlyList<Rama> Ramas(IReadOnlyList<EquipoImpreso> equipos)
    {
        var existe = new HashSet<string>(equipos.Select(e => e.Nombre));
        var hijosDe = new Dictionary<string, List<EquipoImpreso>>();
        var raices = new List<EquipoImpreso>();

        foreach (var eq in equipos)
        {
            if (eq.EquipoPadre is string padre && padre != eq.Nombre && existe.Contains(padre))
            {
                if (!hijosDe.TryGetValue(padre, out var hijos)) hijosDe[padre] = hijos = [];
                hijos.Add(eq);
            }
            else raices.Add(eq);
        }

        var visitados = new HashSet<string>();
        var ramas = raices.Select(r => Montar(r, hijosDe, visitados)).ToList();

        // Lo que no colgaba de ninguna raíz solo puede ser un círculo escrito a mano contra la base
        // —la aplicación no deja crearlo—. Sale como raíz, descolocado, pero SALE: perder un equipo
        // del organigrama es peor que dibujarlo donde no toca, y así además se nota que algo va mal.
        foreach (var eq in equipos)
            if (!visitados.Contains(eq.Nombre))
                ramas.Add(Montar(eq, hijosDe, visitados));

        return ramas;
    }

    /// <summary>
    /// Un equipo y su rama, hacia abajo. El conjunto de visitados no es una optimización: es lo que
    /// impide que un círculo escrito contra la base deje esto bajando para siempre. Un equipo que ya
    /// salió se dibuja como hoja, sin repetir lo que cuelga de él.
    /// </summary>
    private static Rama Montar(EquipoImpreso eq, Dictionary<string, List<EquipoImpreso>> hijosDe,
        HashSet<string> visitados)
    {
        if (!visitados.Add(eq.Nombre)) return new Rama(eq, []);

        var hijos = hijosDe.TryGetValue(eq.Nombre, out var lista) ? lista : [];
        return new Rama(eq, [.. hijos.Select(h => Montar(h, hijosDe, visitados))]);
    }

    // ── Piezas comunes ──────────────────────────────────────────────────────────

    private static void Hoja(PageDescriptor pagina)
    {
        pagina.Size(PageSizes.Letter);
        pagina.Margin(2, Unit.Centimetre);
        pagina.DefaultTextStyle(t => t.FontSize(10).FontColor(Tinta));
    }

    /// <summary>
    /// La hoja del organigrama: APAISADA, al revés que los otros dos documentos. Un organigrama crece
    /// a lo ancho: en vertical solo caben dos cajas por fila y el diagrama se convierte en una columna
    /// larguísima, que es exactamente la lista de la que se venía huyendo.
    ///
    /// <para>La declaran por igual la hoja de la organización y la de cada rama: son el mismo
    /// documento repartido, y una rama en vertical detrás de un resumen apaisado se lee como si
    /// alguien hubiera imprimido dos papeles distintos y los hubiera grapado.</para>
    /// </summary>
    private static void HojaApaisada(PageDescriptor pagina)
    {
        pagina.Size(PageSizes.Letter.Landscape());
        pagina.Margin(1.2f, Unit.Centimetre);
        pagina.DefaultTextStyle(t => t.FontSize(10).FontColor(Tinta));
    }

    private static void Titulo(IContainer c, string titulo, string subtitulo) =>
        c.Column(col =>
        {
            col.Item().Text(titulo).SemiBold().FontSize(16).FontColor(Tinta);
            col.Item().Text(subtitulo).FontSize(9).FontColor(Tenue);
            col.Item().PaddingTop(6).BorderBottom(1).BorderColor(Linea);
        });

    private static void Seccion(IContainer c, string texto) =>
        c.PaddingTop(6).BorderBottom(1).BorderColor(Linea).PaddingBottom(2)
            .Text(texto).SemiBold().FontSize(11).FontColor(Tinta);

    /// <summary>Pares etiqueta/valor en dos columnas: es como se lee un formulario en papel.</summary>
    private static void Campos(IContainer c, IReadOnlyList<(string Etiqueta, string Valor)> campos) =>
        c.Column(col =>
        {
            foreach (var campo in campos)
                col.Item().PaddingBottom(3).Row(fila =>
                {
                    fila.ConstantItem(150).Text(campo.Etiqueta).FontColor(Tenue);
                    fila.RelativeItem().Text(string.IsNullOrWhiteSpace(campo.Valor) ? "—" : campo.Valor);
                });
        });

    private static void Indicador(IContainer c, string titulo, string valor) =>
        c.Border(1).BorderColor(Linea).Padding(8).Column(col =>
        {
            col.Item().Text(titulo).FontSize(8).FontColor(Tenue);
            col.Item().Text(valor).SemiBold().FontSize(14).FontColor(Tinta);
        });

    private static void Bloque(IContainer c, string titulo, string texto) =>
        c.Column(col =>
        {
            col.Item().Text(titulo).SemiBold().FontSize(9).FontColor(Tenue);
            col.Item().Text(texto);
        });

    private static void Etiquetado(IContainer c, string etiqueta, string? texto) =>
        c.PaddingTop(3).Text(t =>
        {
            t.Span($"{etiqueta}: ").SemiBold().FontSize(9).FontColor(Tenue);
            t.Span(texto ?? "").FontSize(9);
        });

    private static void PieDePagina(IContainer c) =>
        c.BorderTop(1).BorderColor(Linea).PaddingTop(4).Row(fila =>
        {
            fila.RelativeItem().Text("Administrador de Desarrollo").FontSize(8).FontColor(Tenue);
            fila.ConstantItem(80).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Tenue));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
}
