using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using WColor = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace Administrador_Desarrollo_Web.Services;

public record EvaluationRow(DateTime Date, string? Period, int? Rating, string? Strengths, string? Weaknesses, string? Comments, string? Evaluator);
public record MilestoneRow(DateTime Date, MilestoneKind Kind, string Title, string? Description);

public record DeveloperReportData(
    string FullName, string? Email, string? Phone, string? Seniority, string? Team, string? TeamRole, DateTime? HireDate,
    int ApprovedPointsYear, string TotalTime, int ActiveAssignments,
    IReadOnlyList<EvaluationRow> Evaluations, IReadOnlyList<MilestoneRow> Milestones, string GeneratedAt);

/// <summary>
/// Genera el reporte de un desarrollador (fortalezas y debilidades, hitos y evaluaciones de líder).
/// Construye un DOCX en memoria (DocumentFormat.OpenXml) y lo convierte a PDF con el convertidor
/// existente (LibreOffice), sin Office. El armado del DOCX está separado para poder probarlo sin red.
/// </summary>
public class DeveloperReportService
{
    private readonly AppDbContext _db;
    private readonly IDocxToPdfConverter _converter;
    private readonly WorkSessionService _work;

    public DeveloperReportService(AppDbContext db, IDocxToPdfConverter converter, WorkSessionService work)
    {
        _db = db; _converter = converter; _work = work;
    }

    public bool ConverterAvailable(out string? diagnostic) => _converter.IsAvailable(out diagnostic);

    public DeveloperReportData GatherData(int developerId)
    {
        var dev = _db.Developers.Include(d => d.Team).FirstOrDefault(d => d.Id == developerId)
            ?? throw new ArgumentException("Desarrollador no encontrado.");

        int year = DateTime.Today.Year;
        int aprobados = _db.PointEntries
            .Where(p => p.DeveloperId == developerId && p.Year == year && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .Sum(p => (int?)p.Points) ?? 0;

        int activos = _db.Requirements.Count(r =>
            r.Assignments.Any(a => a.DeveloperId == developerId)
            && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado);

        var evals = _db.DeveloperEvaluations
            .Where(e => e.DeveloperId == developerId)
            .OrderByDescending(e => e.EvaluationDate).ThenByDescending(e => e.Id)
            .Select(e => new EvaluationRow(e.EvaluationDate, e.PeriodLabel, e.OverallRating, e.Strengths, e.Weaknesses, e.Comments, e.EvaluatorName))
            .ToList();

        var hitos = _db.DeveloperMilestones
            .Where(m => m.DeveloperId == developerId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Select(m => new MilestoneRow(m.Date, m.Kind, m.Title, m.Description))
            .ToList();

        return new DeveloperReportData(
            dev.FullName, dev.Email, dev.Phone, dev.Seniority,
            dev.Team?.Name, dev.TeamId != null ? TeamRoleLabel(dev.TeamRole) : null, dev.HireDate,
            aprobados, WorkSessionService.Format(_work.GetTotalSecondsByDeveloper(developerId)), activos,
            evals, hitos, DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
    }

    public async Task<byte[]> BuildPdfAsync(int developerId, CancellationToken ct = default)
        => await _converter.ConvertAsync(BuildDocx(GatherData(developerId)), ct);

    // ── Armado del documento (puro, testeable) ───────────────────
    public static byte[] BuildDocx(DeveloperReportData d)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.Append(Para("Reporte de Desarrollador", 34, bold: true, center: true, colorHex: "1F4E79"));
            body.Append(Para(d.FullName, 26, bold: true, center: true, colorHex: null));
            body.Append(Para($"Generado: {d.GeneratedAt}", 16, bold: false, center: true, colorHex: "808080"));
            body.Append(Blank());

            // ── Datos generales ─────────────────────────────────
            Heading(body, "Datos generales");
            Field(body, "Correo", d.Email);
            Field(body, "Teléfono", d.Phone);
            Field(body, "Seniority", d.Seniority);
            Field(body, "Equipo", string.IsNullOrWhiteSpace(d.Team) ? "Sin equipo" : $"{d.Team}{(string.IsNullOrWhiteSpace(d.TeamRole) ? "" : $"  ({d.TeamRole})")}");
            Field(body, "Ingreso", d.HireDate?.ToString("dd/MM/yyyy"));
            body.Append(Blank());

            // ── Resumen de desempeño ────────────────────────────
            Heading(body, "Resumen de desempeño");
            Field(body, $"Puntos aprobados ({DateTime.Today.Year})", (d.ApprovedPointsYear >= 0 ? "+" : "") + d.ApprovedPointsYear);
            Field(body, "Tiempo dedicado (total)", d.TotalTime);
            Field(body, "Requerimientos activos", d.ActiveAssignments.ToString());
            body.Append(Blank());

            // ── Fortalezas y debilidades (de la evaluación más reciente) ──
            Heading(body, "Fortalezas y debilidades");
            var ultima = d.Evaluations.Count > 0 ? d.Evaluations[0] : null;
            if (ultima == null)
                body.Append(Para("(sin evaluaciones registradas todavía)", 20, false, false, "808080", indentTwips: "360"));
            else
            {
                body.Append(Para($"Según la evaluación del {ultima.Date:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(ultima.Period) ? "" : $" · {ultima.Period}"), 18, false, false, "808080"));
                body.Append(Para("Fortalezas", 22, bold: true, center: false, colorHex: "2E7D32"));
                Multiline(body, ultima.Strengths, "1B5E20");
                body.Append(Para("Debilidades / áreas de mejora", 22, bold: true, center: false, colorHex: "C62828"));
                Multiline(body, ultima.Weaknesses, "8E1B1B");
            }
            body.Append(Blank());

            // ── Hitos ───────────────────────────────────────────
            Heading(body, "Hitos");
            if (d.Milestones.Count == 0)
                body.Append(Para("(sin hitos registrados)", 20, false, false, "808080", indentTwips: "360"));
            else
                foreach (var h in d.Milestones)
                {
                    body.Append(Para($"{h.Date:dd/MM/yyyy}  —  {KindLabel(h.Kind)}: {h.Title}", 22, bold: true, center: false, colorHex: null, indentTwips: "360"));
                    if (!string.IsNullOrWhiteSpace(h.Description))
                        Multiline(body, h.Description, "606060", indent: "720");
                }
            body.Append(Blank());

            // ── Evaluaciones de líder ───────────────────────────
            Heading(body, "Evaluaciones de líder");
            if (d.Evaluations.Count == 0)
                body.Append(Para("(sin evaluaciones)", 20, false, false, "808080", indentTwips: "360"));
            else
                foreach (var ev in d.Evaluations)
                {
                    var cab = $"{ev.Date:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(ev.Period) ? "" : $"  ·  {ev.Period}") + $"  ·  {Estrellas(ev.Rating)}";
                    body.Append(Para(cab, 22, bold: true, center: false, colorHex: "1F4E79"));
                    if (!string.IsNullOrWhiteSpace(ev.Evaluator))
                        body.Append(Para($"Evaluó: {ev.Evaluator}", 18, false, false, "808080", indentTwips: "360"));
                    if (!string.IsNullOrWhiteSpace(ev.Strengths))
                    { body.Append(Para("Fortalezas:", 20, bold: true, center: false, colorHex: "2E7D32", indentTwips: "360")); Multiline(body, ev.Strengths, "1B5E20", indent: "720"); }
                    if (!string.IsNullOrWhiteSpace(ev.Weaknesses))
                    { body.Append(Para("Debilidades:", 20, bold: true, center: false, colorHex: "C62828", indentTwips: "360")); Multiline(body, ev.Weaknesses, "8E1B1B", indent: "720"); }
                    if (!string.IsNullOrWhiteSpace(ev.Comments))
                    { body.Append(Para("Comentarios:", 20, bold: true, center: false, colorHex: "606060", indentTwips: "360")); Multiline(body, ev.Comments, "606060", indent: "720"); }
                    body.Append(Blank());
                }

            main.Document.Save();
        }
        return ms.ToArray();
    }

    // ── Helpers de contenido ─────────────────────────────────────
    private static void Heading(Body body, string text) =>
        body.Append(Para(text, 26, bold: true, center: false, colorHex: "1F4E79"));

    private static void Field(Body body, string label, string? value) =>
        body.Append(Para($"•   {label}: {(string.IsNullOrWhiteSpace(value) ? "—" : value)}", 22, bold: false, center: false, colorHex: null, indentTwips: "360"));

    /// <summary>Texto de varias líneas: cada renglón no vacío se emite como viñeta.</summary>
    private static void Multiline(Body body, string? text, string? colorHex, string indent = "540")
    {
        if (string.IsNullOrWhiteSpace(text)) { body.Append(Para("—", 20, false, false, "808080", indentTwips: indent)); return; }
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            body.Append(Para("•   " + line, 20, bold: false, center: false, colorHex: colorHex, indentTwips: indent));
        }
    }

    private static Paragraph Blank() => Para("", 10, false, false, null);

    private static string Estrellas(int? r) =>
        r is null or < 1 ? "Sin calificar"
        : new string('★', Math.Min(5, r.Value)) + new string('☆', Math.Max(0, 5 - r.Value)) + $"  ({r}/5)";

    private static string KindLabel(MilestoneKind k) => k switch
    {
        MilestoneKind.Logro          => "🏆 Logro",
        MilestoneKind.Proyecto       => "📁 Proyecto",
        MilestoneKind.Certificacion  => "🎓 Certificación",
        MilestoneKind.Reconocimiento => "⭐ Reconocimiento",
        _                            => "• Otro"
    };

    private static string TeamRoleLabel(TeamRole r) => r switch
    {
        TeamRole.Lider => "Líder", TeamRole.SinRol => "Integrante", _ => r.ToString()
    };

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
}
