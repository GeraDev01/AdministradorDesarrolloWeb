using System.IO.Compression;
using System.Text;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Armado del DOCX del reporte de desarrollador (parte pura, sin LibreOffice): que sea un OpenXML
/// válido y que incluya las secciones pedidas (fortalezas/debilidades, hitos, evaluaciones).
/// </summary>
public class DeveloperReportTests
{
    private static DeveloperReportData Muestra() => new(
        FullName: "Angel Palma", Email: "angel@x.com", Phone: "555-1234", Seniority: "Senior",
        Team: "Webpro", TeamRole: "Líder", HireDate: new DateTime(2022, 3, 1),
        ApprovedPointsYear: 42, TotalTime: "12h 30m", ActiveAssignments: 3,
        Evaluations:
        [
            new EvaluationRow(new DateTime(2026, 6, 1), "2026 Q2", 4,
                "Resuelve rápido\nBuen mentor", "Documentar más", "Gran semestre", "Gerardo Admin")
        ],
        Milestones:
        [
            new MilestoneRow(new DateTime(2026, 5, 10), MilestoneKind.Certificacion, "Azure AZ-204", "Certificación oficial")
        ],
        GeneratedAt: "23/07/2026 20:00");

    private static string TextoDelDocx(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")!;
        using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
        return sr.ReadToEnd();
    }

    [Fact]
    public void BuildDocx_ProduceUnDocxValidoConLasSecciones()
    {
        var bytes = DeveloperReportService.BuildDocx(Muestra());

        Assert.True(bytes.Length > 0);
        var xml = TextoDelDocx(bytes);

        Assert.Contains("Reporte de Desarrollador", xml);
        Assert.Contains("Angel Palma", xml);
        Assert.Contains("Fortalezas y debilidades", xml);
        Assert.Contains("Hitos", xml);
        Assert.Contains("Evaluaciones de l", xml);           // "Evaluaciones de líder"
        // Contenido de la muestra presente:
        Assert.Contains("Azure AZ-204", xml);
        Assert.Contains("Documentar m", xml);                // "Documentar más"
        Assert.Contains("Gerardo Admin", xml);
    }

    [Fact]
    public void BuildDocx_SinDatos_NoRevienta_YMuestraVacios()
    {
        var vacio = new DeveloperReportData("Nadie", null, null, null, null, null, null,
            0, "0m", 0, [], [], "hoy");

        var xml = TextoDelDocx(DeveloperReportService.BuildDocx(vacio));

        Assert.Contains("sin evaluaciones registradas", xml);
        Assert.Contains("sin hitos registrados", xml);
    }
}
