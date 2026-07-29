using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Administrador_Desarrollo_Web.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Administrador_Desarrollo_Web.Services;

public record SignaturePng(byte[] Png, int Width, int Height);

/// <summary>Valores ya resueltos que se inyectan en la plantilla.</summary>
public class VacationDocFields
{
    public string Nombre = "";
    public string FechaSolicitud = "";
    public string Departamento = "";
    public string Puesto = "";
    public string JefeDirecto = "";
    public string FechaIngreso = "";
    public string TotalDias = "";
    public string Periodo = "";
    public string FechaInicio = "";
    public string FechaFin = "";
    public string FechaRegreso = "";
    public string DiasPendientes = "";
    public string AutorizaSi = "";
    public string AutorizaNo = "";
    public string Observaciones = "";
}

/// <summary>
/// Genera el DOCX de la solicitud de vacaciones a partir de la plantilla
/// (Plantillas/Solicitud_Vacaciones_Plantilla.docx) rellenando los tokens {{...}}
/// e insertando la firma del gerente en su ancla. Sin Office (DocumentFormat.OpenXml).
/// </summary>
public class VacationDocumentService
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string TemplatePath =>
        Path.Combine(AppContext.BaseDirectory, "Plantillas", "Solicitud_Vacaciones_Plantilla.docx");

    public bool TemplateExists => File.Exists(TemplatePath);

    /// <summary>Construye los campos derivados desde la solicitud + el desarrollador.</summary>
    public static VacationDocFields BuildFields(VacationRequest req, Developer dev, string departamento, string puesto, string jefeDirecto)
    {
        var regreso = NextBusinessDay(req.EndDate);
        return new VacationDocFields
        {
            Nombre         = dev.FullName,
            FechaSolicitud = LongDate(DateTime.Today),
            Departamento   = departamento,
            Puesto         = puesto,
            JefeDirecto    = jefeDirecto,
            FechaIngreso   = dev.HireDate.HasValue ? LongDate(dev.HireDate.Value) : "—",
            TotalDias      = req.TotalDays.ToString(),
            Periodo        = $"{DayAndMonth(req.StartDate)} al {LongDate(req.EndDate)}",
            FechaInicio    = LongDate(req.StartDate),
            FechaFin       = LongDate(req.EndDate),
            FechaRegreso   = $"{Capitalize(regreso.ToString("dddd", Es))} {LongDate(regreso)}",
            DiasPendientes = dev.VacationDaysLeft.ToString(),
            AutorizaSi     = req.Status == VacationStatus.Aprobada ? "X" : "",
            AutorizaNo     = req.Status == VacationStatus.Rechazada ? "X" : "",
            Observaciones  = req.ReviewComment ?? req.Comment ?? ""
        };
    }

    /// <summary>
    /// Rellena la plantilla y devuelve el DOCX en memoria. Si <paramref name="firmaGerente"/>
    /// es null, el ancla de firma queda en blanco (borrador sin firmar).
    /// </summary>
    public byte[] GenerateDocx(VacationDocFields f, SignaturePng? firmaGerente)
    {
        if (!TemplateExists)
            throw new FileNotFoundException($"No se encontró la plantilla: {TemplatePath}");

        var bytes = File.ReadAllBytes(TemplatePath);
        using var ms = new MemoryStream();
        ms.Write(bytes, 0, bytes.Length);
        ms.Position = 0;

        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var mainPart = doc.MainDocumentPart ?? throw new InvalidDataException("Plantilla sin MainDocumentPart.");
            var body = mainPart.Document.Body ?? throw new InvalidDataException("Plantilla sin Body.");

            var map = new Dictionary<string, string>
            {
                ["{{NOMBRE}}"]         = f.Nombre,
                ["{{FECHA_SOLICITUD}}"]= f.FechaSolicitud,
                ["{{DEPARTAMENTO}}"]   = f.Departamento,
                ["{{PUESTO}}"]         = f.Puesto,
                ["{{JEFE_DIRECTO}}"]   = f.JefeDirecto,
                ["{{FECHA_INGRESO}}"]  = f.FechaIngreso,
                ["{{TOTAL_DIAS}}"]     = f.TotalDias,
                ["{{PERIODO}}"]        = f.Periodo,
                ["{{FECHA_INICIO}}"]   = f.FechaInicio,
                ["{{FECHA_FIN}}"]      = f.FechaFin,
                ["{{FECHA_REGRESO}}"]  = f.FechaRegreso,
                ["{{DIAS_PENDIENTES}}"]= f.DiasPendientes,
                ["{{AUTORIZA_SI}}"]    = f.AutorizaSi,
                ["{{AUTORIZA_NO}}"]    = f.AutorizaNo,
                ["{{OBSERVACIONES}}"]  = f.Observaciones,
                ["{{FIRMA_COLABORADOR}}"] = ""
            };

            foreach (var t in body.Descendants<Text>())
            {
                foreach (var kv in map)
                    if (t.Text.Contains(kv.Key))
                        t.Text = t.Text.Replace(kv.Key, kv.Value);
            }

            // Firma del gerente en su ancla
            InsertSignature(mainPart, body, "{{FIRMA_GERENTE}}", firmaGerente);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static void InsertSignature(MainDocumentPart mainPart, Body body, string token, SignaturePng? firma)
    {
        var target = body.Descendants<Text>().FirstOrDefault(t => t.Text.Contains(token));
        if (target == null) return;

        if (firma == null)
        {
            target.Text = target.Text.Replace(token, "");
            return;
        }

        // Añadir la imagen como parte y referenciarla
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var s = new MemoryStream(firma.Png)) imagePart.FeedData(s);
        var relId = mainPart.GetIdOfPart(imagePart);

        // Escalar a un ancho máximo razonable dentro de la celda (~190px)
        const int maxWidthPx = 190;
        double scale = firma.Width > maxWidthPx ? (double)maxWidthPx / firma.Width : 1.0;
        long cx = (long)Math.Round(firma.Width * scale * 9525);   // EMU (9525 EMU/px @96dpi)
        long cy = (long)Math.Round(firma.Height * scale * 9525);
        if (cx <= 0) cx = 9525; if (cy <= 0) cy = 9525;

        var run = target.Ancestors<Run>().FirstOrDefault();
        if (run == null) { target.Text = target.Text.Replace(token, ""); return; }

        run.RemoveAllChildren<Text>();
        run.AppendChild(BuildImageDrawing(relId, cx, cy));
    }

    private static Drawing BuildImageDrawing(string relId, long cx, long cy)
    {
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new DW.DocProperties { Id = 1U, Name = "Firma" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "firma.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            )
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }

    // ── Formato de fechas en español ────────────────────────────────
    private static string LongDate(DateTime d) => $"{d.Day} de {Capitalize(d.ToString("MMMM", Es))} de {d.Year}";
    private static string DayAndMonth(DateTime d) => $"{d.Day} de {Capitalize(d.ToString("MMMM", Es))}";
    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Es) + s[1..];

    private static DateTime NextBusinessDay(DateTime from)
    {
        var d = from.Date.AddDays(1);
        while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }
}
