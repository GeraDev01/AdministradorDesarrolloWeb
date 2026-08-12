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
    // POR QUÉ NO SE MANDA EL SVG DE LA PANTALLA A ESTE PDF, que era la vía obvia teniendo el diagrama
    // ya dibujado allá y sabiendo que QuestPDF admite SVG (Svg(string), desde 2024.3):
    //
    //   · El SVG de la pantalla pinta TODO con variables del tema (var(--rz-…)). Fuera del navegador
    //     nadie resuelve esas variables: el diagrama saldría en negro sobre negro o directamente sin
    //     pintar. Habría que mantener una segunda copia del dibujo con colores fijos.
    //   · Un SVG entra como UNA imagen. Una imagen no se parte por la mitad, así que un organigrama
    //     de veinte equipos o cabe encogido hasta ser ilegible en una hoja, o no cabe. Aquí las cajas
    //     son contenido de verdad y QuestPDF las va pasando de página él solo.
    //   · El texto de un SVG lo dibuja Skia como trazos. En el PDF que sale de aquí los nombres son
    //     TEXTO: se buscan con Ctrl+F, se copian y se leen con un lector de pantalla. Es un documento
    //     que se archiva y se reparte, no una captura de pantalla.
    //   · Y el detalle que lo cierra: el corte de línea. Una descripción de equipo de tres renglones
    //     hay que partirla a mano en el SVG contando caracteres a ojo; aquí la parte QuestPDF con la
    //     métrica real de la fuente.
    //
    // Lo que sí se conserva del SVG es el CONCEPTO: mismo nodo arriba, mismas cajas, mismo orden y la
    // misma caja final de «sin equipo». Quien vea la pantalla y luego el papel reconoce el dibujo.

    /// <summary>Cuántas cajas por fila. Tres caben legibles en una carta apaisada; cuatro dejan los
    /// nombres largos partidos en dos renglones y la caja se vuelve un párrafo.</summary>
    private const int CajasPorFila = 3;

    private const float EspacioEntreCajas = 10f;

    public byte[] OrganizacionDeEquipos(DatosDeEquipos d) => DocumentoDelOrganigrama(d).GeneratePdf();

    /// <summary>El documento antes de convertirlo en bytes. Va aparte porque el diagrama es lo único
    /// que se revisa mirándolo, y así se puede pedir una página como imagen sin generar el PDF.</summary>
    private static IDocument DocumentoDelOrganigrama(DatosDeEquipos d) =>
        Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
                // APAISADA, al revés que los otros dos documentos. Un organigrama crece a lo ancho:
                // en vertical solo caben dos cajas por fila y el diagrama se convierte en una
                // columna larguísima, que es exactamente la lista de la que se venía huyendo.
                pagina.Size(PageSizes.Letter.Landscape());
                pagina.Margin(1.2f, Unit.Centimetre);
                pagina.DefaultTextStyle(t => t.FontSize(10).FontColor(Tinta));

                pagina.Header().Element(e => Titulo(e, "Organigrama de equipos", $"Generado el {d.GeneradoEl}"));

                pagina.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Element(e => NodoRaiz(e, d));

                    // Las cajas en el orden en que se leen: los equipos y, al final, la de quien no
                    // está en ninguno. Va con ellas y no en una nota al pie porque en un organigrama
                    // esa caja es media pregunta del que lo abre.
                    var cajas = new List<EquipoImpreso>(d.Equipos);
                    if (d.SinEquipo.Count > 0)
                        cajas.Add(new EquipoImpreso(
                            "Sin equipo", "Todavía no están asignados a ninguno.", null, null,
                            d.SinEquipo, [], []));

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
                            r.Spacing(EspacioEntreCajas);
                            foreach (var caja in fila)
                                r.RelativeItem().Element(x => CajaDeEquipo(x, caja));

                            // Los huecos de la última fila se reservan igual. Sin esto, dos equipos
                            // solos se estirarían a media hoja cada uno y no parecerían del mismo
                            // tamaño que los de la fila de arriba: en un diagrama, el tamaño se lee
                            // como un dato.
                            for (int hueco = fila.Count; hueco < CajasPorFila; hueco++)
                                r.RelativeItem();
                        });
                    }
                });

                pagina.Footer().Element(PieDePagina);
            });
        });

    /// <summary>
    /// El nodo de arriba: de quién cuelga todo esto y cuánta gente hay en total. Sin él, las cajas
    /// sueltas son una cuadrícula; con él, son un organigrama.
    /// </summary>
    private static void NodoRaiz(IContainer c, DatosDeEquipos d) =>
        c.Column(col =>
        {
            col.Item().AlignCenter().Border(1).BorderColor(Tinta).Padding(8).Column(caja =>
            {
                caja.Item().AlignCenter().Text("Organización").SemiBold().FontSize(13).FontColor(Tinta);
                caja.Item().AlignCenter().Text(
                    $"{d.Equipos.Count} equipo(s) · {d.TotalPersonas} persona(s)" +
                    (d.SinEquipo.Count > 0 ? $" · {d.SinEquipo.Count} sin equipo" : ""))
                    .FontSize(8).FontColor(Tenue);
            });

            col.Item().Height(10).AlignCenter().LineVertical(0.8f).LineColor(Linea);
        });

    /// <summary>
    /// La barra de la que cuelgan las cajas de una fila, con su bajada a cada una.
    ///
    /// <para>Se repite en CADA fila, y no solo bajo el nodo de arriba, porque una fila que empieza en
    /// la página siguiente aparecería suelta: sin la barra, esas cajas parecerían colgar de las de
    /// arriba —o sea, subequipos— y aquí todos los equipos están al mismo nivel.</para>
    /// </summary>
    private static void Reparto(IContainer c, int cuantas) =>
        c.Column(col =>
        {
            // La barra va de la PRIMERA bajada a la ÚLTIMA, no de borde a borde de las cajas. Las
            // bajadas salen del centro de cada celda, así que una barra que ocupe celdas enteras
            // asoma media celda por cada extremo —más de cien puntos en una carta apaisada— y esos
            // dos trozos cuelgan sin unir nada. De ahí los medios de relleno a los lados.
            //
            // Y se queda donde acaba la última caja de la fila, sin cruzar la hoja entera: en la
            // última fila —que casi nunca va completa— una barra de lado a lado parece que va a
            // repartir a cajas que no están, y lo primero que se piensa es que falta algo.
            //
            // Con una sola caja no hay barra: no hay nada que repartir y una raya de ancho cero
            // sobre su propia bajada solo ensucia.
            if (cuantas > 1)
            {
                col.Item().Row(barra =>
                {
                    barra.RelativeItem(0.5f);
                    barra.RelativeItem(cuantas - 1).LineHorizontal(0.8f).LineColor(Linea);
                    barra.RelativeItem(CajasPorFila - cuantas + 0.5f);
                });
            }

            col.Item().Height(10).Row(r =>
            {
                r.Spacing(EspacioEntreCajas);
                for (int i = 0; i < CajasPorFila; i++)
                {
                    var celda = r.RelativeItem();
                    if (i < cuantas) celda.AlignCenter().LineVertical(0.8f).LineColor(Linea);
                }
            });
        });

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
    private static void CajaDeEquipo(IContainer c, EquipoImpreso eq) =>
        c.Border(1).BorderColor(Linea).Column(col =>
        {
            col.Item().Height(6).Background(ColorValido(eq.ColorHex));

            col.Item().Padding(8).PaddingBottom(6).Column(cab =>
            {
                cab.Item().Text(eq.Nombre).SemiBold().FontSize(12).FontColor(Tinta);

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
    /// El color del equipo, o el gris de siempre si no es un «#RRGGBB» válido. El valor lo escribe
    /// una persona en una pantalla, y un color mal escrito no debe impedir imprimir el documento.
    /// </summary>
    private static string ColorValido(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#'
        && hex[1..].All(Uri.IsHexDigit)
            ? hex
            : Linea;

    // ── Piezas comunes ──────────────────────────────────────────────────────────

    private static void Hoja(PageDescriptor pagina)
    {
        pagina.Size(PageSizes.Letter);
        pagina.Margin(2, Unit.Centimetre);
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
