using System.Reflection;
using AdminWeb.Domain.Documentos;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace AdminWeb.Infrastructure.Documentos;

/// <summary>
/// Rellena la plantilla de Word con OpenXml. Es el port de <c>VacationDocumentService.GenerateDocx</c>
/// del escritorio, con cuatro cambios que el original no necesitaba y aquí sí.
/// </summary>
public sealed class PlantillaDeVacacionesOpenXml : IPlantillaDeVacacionesEnWord
{
    /// <summary>La plantilla incrustada. El nombre lo fija el .csproj para que mover la carpeta no
    /// rompa la lectura.</summary>
    private const string RecursoDeFabrica = "AdminWeb.Plantillas.Solicitud_Vacaciones_Plantilla.docx";

    public byte[] DeFabrica()
    {
        using var flujo = typeof(PlantillaDeVacacionesOpenXml).Assembly
            .GetManifestResourceStream(RecursoDeFabrica)
            ?? throw new InvalidOperationException(
                $"Falta el recurso incrustado «{RecursoDeFabrica}». Es un error de compilación, no de datos.");

        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        return memoria.ToArray();
    }

    public byte[] Rellenar(byte[] plantillaDocx, DatosDeVacaciones datos, FirmaEnPng? firmaDelJefe)
    {
        // (1) Se trabaja sobre una COPIA. OpenXml abre el MemoryStream en escritura y modifica el
        //     búfer: pasarle el array recibido corrompería la plantilla guardada en la base la
        //     primera vez que alguien emitiera un documento.
        using var memoria = new MemoryStream(plantillaDocx.Length);
        memoria.Write(plantillaDocx, 0, plantillaDocx.Length);
        memoria.Position = 0;

        using (var doc = WordprocessingDocument.Open(memoria, true))
        {
            var principal = doc.MainDocumentPart
                ?? throw new InvalidDataException("La plantilla no tiene documento principal.");

            var mapa = TokensDeVacaciones.Mapa(datos);

            // (2) También encabezados y pies, no solo el cuerpo. El escritorio recorría solo el Body
            //     porque su plantilla no tenía marcadores fuera; en cuanto RH mueva el nombre al
            //     membrete, esa suposición deja de valer y el token saldría impreso.
            foreach (var raiz in Raices(principal))
            {
                Normalizar(raiz);
                foreach (var texto in raiz.Descendants<Text>())
                    foreach (var (token, valor) in mapa)
                        if (texto.Text.Contains(token))
                            texto.Text = texto.Text.Replace(token, valor);
            }

            EstamparFirma(principal, firmaDelJefe);

            // Se guarda CADA parte, no solo el documento principal: si un marcador estaba en el
            // membrete, guardar únicamente el cuerpo dejaría ese cambio sin escribir.
            principal.Document.Save();
            foreach (var h in principal.HeaderParts) h.Header?.Save();
            foreach (var f in principal.FooterParts) f.Footer?.Save();
        }

        return memoria.ToArray();
    }

    public ValidacionDePlantilla Validar(byte[] posibleDocx)
    {
        if (posibleDocx.Length == 0)
            return new ValidacionDePlantilla(false, "El archivo está vacío.", []);

        // Un .docx es un ZIP: empieza por «PK». Se comprueba por los BYTES y no por la extensión,
        // que la pone quien sube el archivo.
        if (posibleDocx.Length < 4 || posibleDocx[0] != 0x50 || posibleDocx[1] != 0x4B)
            return new ValidacionDePlantilla(false,
                "Eso no es un documento de Word (.docx). Guárdalo desde Word como .docx, " +
                "no como .doc ni como PDF.", []);

        try
        {
            using var memoria = new MemoryStream(posibleDocx, writable: false);
            using var doc = WordprocessingDocument.Open(memoria, false);

            var principal = doc.MainDocumentPart;
            if (principal is null)
                return new ValidacionDePlantilla(false,
                    "El archivo se abre pero no tiene documento dentro. Puede estar dañado.", []);

            // El texto se lee CONCATENADO por parte, no marcador a marcador dentro de cada <w:t>:
            // Word parte «{{NOMBRE}}» en varios trozos en cuanto alguien lo edita, y buscándolo
            // entero en cada trozo no aparecería ninguno.
            var texto = string.Concat(Raices(principal)
                .SelectMany(r => r.Descendants<Text>())
                .Select(t => t.Text));

            var faltan = TokensDeVacaciones.Obligatorios
                .Where(t => !texto.Contains(t, StringComparison.Ordinal))
                .ToList();

            return faltan.Count == 0
                ? new ValidacionDePlantilla(true,
                    $"Plantilla válida: se encontraron los {TokensDeVacaciones.Obligatorios.Count} marcadores.", [])
                : new ValidacionDePlantilla(false,
                    $"Faltan {faltan.Count} marcador(es) en la plantilla: {string.Join(", ", faltan)}. " +
                    "Escríbelos tal cual, con las dos llaves.", faltan);
        }
        catch (Exception ex)
        {
            return new ValidacionDePlantilla(false,
                $"No se pudo abrir como documento de Word: {ex.Message}", []);
        }
    }

    /// <summary>
    /// Las raíces donde puede haber marcadores: el cuerpo, los encabezados y los pies.
    ///
    /// Se devuelven los ELEMENTOS y no las partes porque es sobre el árbol XML donde se busca; una
    /// OpenXmlPart no se puede recorrer directamente.
    /// </summary>
    private static IEnumerable<OpenXmlElement> Raices(MainDocumentPart principal)
    {
        if (principal.Document?.Body is { } cuerpo) yield return cuerpo;
        foreach (var h in principal.HeaderParts) if (h.Header is { } x) yield return x;
        foreach (var f in principal.FooterParts) if (f.Footer is { } x) yield return x;
    }

    /// <summary>
    /// (3) Junta el texto de cada párrafo en un solo <c>&lt;w:t&gt;</c> cuando ve marcadores partidos.
    ///
    /// <para><b>Es el fallo clásico de OpenXml y el que más caro sale.</b> Word divide el texto en
    /// «runs» por motivos suyos —una corrección ortográfica, un cambio de idioma, haberlo tecleado en
    /// dos veces—, así que «{{NOMBRE}}» puede acabar como «{{NOM», «BRE}}». Buscando el token entero
    /// dentro de cada trozo no se encuentra NINGUNO, la sustitución no ocurre y el documento sale con
    /// el marcador impreso. Y no se nota al probar con la plantilla de fábrica, donde todavía están
    /// enteros: se nota el día que RH la edita y la vuelve a guardar.</para>
    ///
    /// <para>Solo se tocan los párrafos que tienen llaves, y solo si el token no cabe ya en un trozo:
    /// juntar todos los runs de todos los párrafos perdería negritas y colores del formato.</para>
    /// </summary>
    private static void Normalizar(OpenXmlElement raiz)
    {
        foreach (var parrafo in raiz.Descendants<Paragraph>())
        {
            var textos = parrafo.Descendants<Text>().ToList();
            if (textos.Count < 2) continue;

            var completo = string.Concat(textos.Select(t => t.Text));
            if (!completo.Contains("{{", StringComparison.Ordinal)) continue;

            // Si cada marcador ya cabe entero en algún trozo, no hay nada que arreglar y no vale la
            // pena perder el formato del párrafo.
            bool partido = TokensDeVacaciones.Obligatorios
                .Concat([TokensDeVacaciones.FirmaDelColaborador])
                .Any(t => completo.Contains(t, StringComparison.Ordinal)
                       && !textos.Any(x => x.Text.Contains(t, StringComparison.Ordinal)));
            if (!partido) continue;

            textos[0].Text = completo;
            textos[0].Space = SpaceProcessingModeValues.Preserve;
            for (int i = 1; i < textos.Count; i++) textos[i].Text = "";
        }
    }

    /// <summary>
    /// Pone la firma del jefe en su ancla. Sin firma, borra el marcador para que no salga impreso.
    /// </summary>
    private static void EstamparFirma(MainDocumentPart principal, FirmaEnPng? firma)
    {
        var ancla = Raices(principal)
            .SelectMany(r => r.Descendants<Text>())
            .FirstOrDefault(t => t.Text.Contains(TokensDeVacaciones.FirmaDelJefe, StringComparison.Ordinal));

        if (ancla is null) return;

        if (firma is null || firma.Png.Length == 0)
        {
            ancla.Text = ancla.Text.Replace(TokensDeVacaciones.FirmaDelJefe, "");
            return;
        }

        var imagen = principal.AddImagePart(ImagePartType.Png);
        using (var flujo = new MemoryStream(firma.Png)) imagen.FeedData(flujo);
        var relacion = principal.GetIdOfPart(imagen);

        // (4) Medidas de respaldo si vinieran en cero. El escritorio ponía 9525 EMU —UN píxel— y la
        //     firma salía como un punto invisible en mitad del papel. 190x60 es el hueco que la
        //     plantilla reserva.
        int ancho = firma.Ancho > 0 ? firma.Ancho : 190;
        int alto  = firma.Alto  > 0 ? firma.Alto  : 60;

        // Se ACOTA a la misma altura que usa el PDF (48 puntos) conservando la proporción.
        //
        // Sin esto los dos formatos del MISMO documento salían distintos: el PDF limita la firma a
        // 48 puntos de alto y el Word la estampaba a su tamaño natural en píxeles, así que un trazo
        // capturado en un lienzo grande se imprimía enorme y se salía de la celda de la tabla. Que
        // el mismo papel se vea igual en Word y en PDF no es cosmética: es lo que permite revisar
        // uno y archivar el otro sin compararlos.
        const double AltoMaximoEnPuntos = 48;
        double escala = Math.Min(1, AltoMaximoEnPuntos / AlturaEnPuntos(alto));

        // EMU: 914400 por pulgada, 96 píxeles por pulgada → 9525 EMU por píxel.
        long cx = (long)(ancho * 9525L * escala);
        long cy = (long)(alto  * 9525L * escala);

        ancla.Text = ancla.Text.Replace(TokensDeVacaciones.FirmaDelJefe, "");
        ancla.Parent?.AppendChild(Dibujo(relacion, cx, cy));
    }

    /// <summary>Los píxeles pasados a puntos de documento: 96 píxeles por pulgada, 72 puntos por pulgada.</summary>
    private static double AlturaEnPuntos(int pixeles) => pixeles * 72.0 / 96.0;

    /// <summary>El armazón XML de una imagen en línea. Copiado del escritorio sin cambios.</summary>
    private static Drawing Dibujo(string relacion, long cx, long cy) => new(
        new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = 1U, Name = "Firma" },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = "firma.png" },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(
                        new A.Blip { Embed = relacion },
                        new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(
                            new A.Offset { X = 0L, Y = 0L },
                            new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
            ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U, DistanceFromBottom = 0U,
            DistanceFromLeft = 0U, DistanceFromRight = 0U
        });
}
