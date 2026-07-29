using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WColor = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace Administrador_Desarrollo_Web.Services;

public record TeamRoster(string Name, string? Description, string? Lead, string? ColorHex, List<string> Members)
{
    public List<string> Systems { get; init; } = [];
    public List<string> Projects { get; init; } = [];
}

/// <summary>
/// Genera un PDF con la organización de equipos. Construye un DOCX en memoria
/// (DocumentFormat.OpenXml) y lo convierte a PDF con el convertidor existente
/// (LibreOffice), sin Office.
/// </summary>
public class TeamRosterService
{
    private readonly IDocxToPdfConverter _converter;

    public TeamRosterService(IDocxToPdfConverter converter) => _converter = converter;

    public bool ConverterAvailable(out string? diagnostic) => _converter.IsAvailable(out diagnostic);

    public async Task<byte[]> BuildPdfAsync(IReadOnlyList<TeamRoster> teams, IReadOnlyList<string> unassigned, string generatedAt, CancellationToken ct = default)
    {
        var docx = BuildDocx(teams, unassigned, generatedAt);
        return await _converter.ConvertAsync(docx, ct);
    }

    public byte[] BuildDocx(IReadOnlyList<TeamRoster> teams, IReadOnlyList<string> unassigned, string generatedAt)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.Append(Para("Organización de Equipos", 36, bold: true, center: true, colorHex: null));
            body.Append(Para($"Generado: {generatedAt}", 18, bold: false, center: true, colorHex: "808080"));
            body.Append(Para("", 12, false, false, null));

            foreach (var t in teams)
            {
                body.Append(Para($"{t.Name}   ({t.Members.Count} miembro(s))", 28, bold: true, center: false, colorHex: NormalizeHex(t.ColorHex)));
                if (!string.IsNullOrWhiteSpace(t.Lead))
                    body.Append(Para($"Líder: {t.Lead}", 22, bold: true, center: false, colorHex: null));
                if (!string.IsNullOrWhiteSpace(t.Description))
                    body.Append(Para(t.Description!, 20, bold: false, center: false, colorHex: "606060"));
                if (t.Members.Count == 0)
                    body.Append(Bullet("(sin miembros)", "808080"));
                else
                    foreach (var m in t.Members) body.Append(Bullet(m, null));

                if (t.Systems.Count > 0)
                {
                    body.Append(Para("Sistemas:", 20, bold: true, center: false, colorHex: "606060"));
                    foreach (var s in t.Systems) body.Append(Bullet("🖥  " + s, null));
                }
                if (t.Projects.Count > 0)
                {
                    body.Append(Para("Proyectos:", 20, bold: true, center: false, colorHex: "606060"));
                    foreach (var p in t.Projects) body.Append(Bullet("📁  " + p, null));
                }
                body.Append(Para("", 10, false, false, null));
            }

            if (unassigned.Count > 0)
            {
                body.Append(Para($"Sin equipo   ({unassigned.Count})", 28, bold: true, center: false, colorHex: "808080"));
                foreach (var m in unassigned) body.Append(Bullet(m, null));
            }

            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Paragraph Bullet(string text, string? colorHex) =>
        Para("•   " + text, 22, bold: false, center: false, colorHex: colorHex, indentTwips: "360");

    private static Paragraph Para(string text, int halfPointSize, bool bold, bool center, string? colorHex, string? indentTwips = null)
    {
        var rp = new RunProperties();
        rp.Append(new RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI" });
        if (bold) rp.Append(new Bold());
        rp.Append(new FontSize { Val = halfPointSize.ToString() });
        if (!string.IsNullOrEmpty(colorHex)) rp.Append(new WColor { Val = colorHex });

        var run = new Run();
        run.Append(rp);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var pp = new ParagraphProperties();
        if (center) pp.Append(new Justification { Val = JustificationValues.Center });
        pp.Append(new SpacingBetweenLines { After = "60" });
        if (indentTwips != null) pp.Append(new Indentation { Left = indentTwips });

        var p = new Paragraph();
        p.Append(pp);
        p.Append(run);
        return p;
    }

    private static string? NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        return hex.Length == 6 ? hex.ToUpperInvariant() : null;
    }
}
