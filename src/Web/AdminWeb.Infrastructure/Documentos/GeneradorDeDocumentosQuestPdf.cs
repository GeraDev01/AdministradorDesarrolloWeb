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

                    col.Item().PaddingTop(24).Element(e => Firmas(e, d.Nombre, d.JefeDirecto, d.FirmaDelJefe));
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

    private static void Firmas(IContainer c, string colaborador, string jefe, byte[]? firmaDelJefe) =>
        c.Row(fila =>
        {
            fila.RelativeItem().Element(e => Firma(e, colaborador, "Colaborador", null));
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

    // ── Organización de equipos ─────────────────────────────────────────────────

    public byte[] OrganizacionDeEquipos(DatosDeEquipos d) =>
        Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
                Hoja(pagina);

                pagina.Header().Element(e => Titulo(e, "Organización de equipos", $"Generado el {d.GeneradoEl}"));

                pagina.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(10);

                    foreach (var eq in d.Equipos)
                        col.Item().Element(e => Equipo(e, eq));

                    if (d.SinEquipo.Count > 0)
                    {
                        col.Item().Element(e => Seccion(e, "Sin equipo asignado"));
                        col.Item().Text(string.Join(" · ", d.SinEquipo)).FontSize(9).FontColor(Tenue);
                    }
                });

                pagina.Footer().Element(PieDePagina);
            });
        }).GeneratePdf();

    private static void Equipo(IContainer c, EquipoImpreso eq) =>
        // La franja de color a la izquierda es el mismo color con el que el equipo se ve en pantalla:
        // quien mira el papel y quien mira la aplicación reconocen lo mismo.
        c.BorderLeft(4).BorderColor(ColorValido(eq.ColorHex)).PaddingLeft(8).PaddingBottom(6).Column(col =>
        {
            col.Item().Text(eq.Nombre).SemiBold().FontSize(12).FontColor(Tinta);

            if (!string.IsNullOrWhiteSpace(eq.Descripcion))
                col.Item().Text(eq.Descripcion).FontSize(9).FontColor(Tenue);

            if (!string.IsNullOrWhiteSpace(eq.Lider))
                col.Item().PaddingTop(2).Text($"Líder: {eq.Lider}").FontSize(9).FontColor(Tinta);

            col.Item().PaddingTop(3).Text(eq.Integrantes.Count == 0
                ? "Sin integrantes."
                : string.Join(" · ", eq.Integrantes)).FontSize(9);

            if (eq.Sistemas.Count > 0)
                col.Item().Text($"Sistemas: {string.Join(", ", eq.Sistemas)}").FontSize(8).FontColor(Tenue);
            if (eq.Proyectos.Count > 0)
                col.Item().Text($"Proyectos: {string.Join(", ", eq.Proyectos)}").FontSize(8).FontColor(Tenue);
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
