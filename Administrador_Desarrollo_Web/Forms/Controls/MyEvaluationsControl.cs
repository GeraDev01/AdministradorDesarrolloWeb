using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Vista del DESARROLLADOR de SUS PROPIAS evaluaciones (fortalezas, debilidades, calificación) e
/// hitos, en solo lectura. Puede descargar su reporte en PDF. No puede editar nada: eso es del líder.
/// </summary>
public class MyEvaluationsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly DeveloperReportService _report;

    private DataGridView _gridEval = null!, _gridHito = null!;
    private Label _lblEmpty = null!;
    private List<DeveloperEvaluation> _evals = [];

    public MyEvaluationsControl(AppDbContext db, CurrentUserContext currentUser, DeveloperReportService report)
    {
        _db = db; _currentUser = currentUser; _report = report;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        toolbar.Controls.Add(new Label { Text = "Tus evaluaciones e hitos, tal como los registró tu líder. Doble clic en una evaluación para ver el detalle completo.", AutoSize = true, Margin = new Padding(0, 8, 16, 0), ForeColor = AppTheme.TextSecondary });
        var btnPdf = AppTheme.MakePrimaryButton("📄 Descargar mi reporte (PDF)", 240); btnPdf.Margin = new Padding(0, 2, 8, 0); btnPdf.Click += BtnPdf_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(0, 2, 8, 0); btnReload.Click += (_, _) => LoadData();
        // Acceso directo a la ruta de LibreOffice: no solo cuando el PDF falla — quien acaba de
        // reinstalarlo puede corregirla antes de chocar con el error.
        var btnLibre = AppTheme.MakeSecondaryButton("⚙ LibreOffice…", 140); btnLibre.Margin = new Padding(0, 2, 0, 0);
        btnLibre.Click += (_, _) => { using var f = new LibreOfficePathForm(); f.ShowDialog(FindForm()); };
        toolbar.Controls.AddRange([btnPdf, btnReload, btnLibre]);

        // Evaluaciones (solo lectura)
        _gridEval = AppTheme.MakeGrid(); _gridEval.MultiSelect = false;
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",  Name = "Date", FillWeight = 11 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Periodo", Name = "Per", FillWeight = 12 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Calificación", Name = "Rate", FillWeight = 12 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Evaluó", Name = "By", FillWeight = 20 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fortalezas / debilidades (resumen — doble clic para ver todo)", Name = "Sum", FillWeight = 45 });
        _gridEval.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) VerDetalle(ev.RowIndex); };

        var pnlEval = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 6), BackColor = AppTheme.ContentBg };
        pnlEval.Controls.Add(_gridEval);
        _lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false, ForeColor = AppTheme.TextSecondary, Text = "Todavía no tienes evaluaciones registradas por tu líder." };
        pnlEval.Controls.Add(_lblEmpty);

        // Hitos (solo lectura)
        _gridHito = AppTheme.MakeGrid(); _gridHito.MultiSelect = false;
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Name = "Date",  FillWeight = 12 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",  Name = "Kind",  FillWeight = 16 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title", FillWeight = 30 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc", FillWeight = 42 });
        var pnlHito = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(10, 2, 10, 10) };
        pnlHito.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        pnlHito.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlHito.Controls.Add(new Label { Text = "🏆 Mis hitos", AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Margin = new Padding(0, 4, 0, 0) }, 0, 0);
        pnlHito.Controls.Add(_gridHito, 0, 1);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlEval, 0, 1);
        tbl.Controls.Add(pnlHito, 0, 2);
        Controls.Add(tbl);
    }

    private int DevId => _currentUser.User?.DeveloperId ?? -1;

    private void LoadData()
    {
        _gridEval.Rows.Clear(); _gridHito.Rows.Clear(); _evals = [];
        if (DevId < 0)
        {
            _lblEmpty.Text = "Tu cuenta no está vinculada a un desarrollador.";
            _lblEmpty.Visible = true; _lblEmpty.BringToFront();
            return;
        }

        _evals = _db.DeveloperEvaluations.Where(e => e.DeveloperId == DevId)
            .OrderByDescending(e => e.EvaluationDate).ThenByDescending(e => e.Id).ToList();
        foreach (var e in _evals)
            _gridEval.Rows.Add(e.EvaluationDate.ToString("dd/MM/yyyy"), e.PeriodLabel ?? "—",
                Estrellas(e.OverallRating), e.EvaluatorName ?? "—", Resumen(e));
        _lblEmpty.Visible = _evals.Count == 0;
        if (_lblEmpty.Visible) _lblEmpty.BringToFront();

        var hitos = _db.DeveloperMilestones.Where(m => m.DeveloperId == DevId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToList();
        foreach (var m in hitos)
            _gridHito.Rows.Add(m.Date.ToString("dd/MM/yyyy"), KindLabel(m.Kind), m.Title, Recorte(m.Description, 90));
        if (hitos.Count == 0) _gridHito.Rows.Add("", "", "(sin hitos registrados)", "");
    }

    private void VerDetalle(int rowIndex)
    {
        if (rowIndex >= _evals.Count) return;
        var e = _evals[rowIndex];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Fecha: {e.EvaluationDate:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(e.PeriodLabel) ? "" : $"   ·   Periodo: {e.PeriodLabel}"));
        sb.AppendLine($"Calificación: {Estrellas(e.OverallRating)}");
        sb.AppendLine($"Evaluó: {e.EvaluatorName ?? "—"}");
        sb.AppendLine();
        sb.AppendLine("FORTALEZAS:"); sb.AppendLine(string.IsNullOrWhiteSpace(e.Strengths) ? "—" : e.Strengths); sb.AppendLine();
        sb.AppendLine("DEBILIDADES / ÁREAS DE MEJORA:"); sb.AppendLine(string.IsNullOrWhiteSpace(e.Weaknesses) ? "—" : e.Weaknesses); sb.AppendLine();
        sb.AppendLine("COMENTARIOS:"); sb.AppendLine(string.IsNullOrWhiteSpace(e.Comments) ? "—" : e.Comments);
        MessageBox.Show(sb.ToString(), $"Evaluación del {e.EvaluationDate:dd/MM/yyyy}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void BtnPdf_Click(object? s, EventArgs e)
    {
        if (DevId < 0) { MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!_report.ConverterAvailable(out var diag))
        {
            // El arreglo a un clic del error: el desarrollador no puede abrir Configuración (es
            // del admin), así que aquí mismo se le ofrece decir dónde quedó SU LibreOffice.
            if (MessageBox.Show(
                    $"{diag}\n\n¿Quieres indicar ahora dónde está LibreOffice en esta computadora?",
                    "Generar PDF", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            using var cfg = new LibreOfficePathForm();
            if (cfg.ShowDialog(FindForm()) != DialogResult.OK || !cfg.Guardado) return;
            if (!_report.ConverterAvailable(out diag))
            { MessageBox.Show(diag, "Generar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            // La ruta quedó bien: se sigue de largo con la generación.
        }

        using var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = $"Mi_Reporte_{DateTime.Now:yyyyMMdd}.pdf" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var pdf = await _report.BuildPdfAsync(DevId);
            File.WriteAllBytes(dlg.FileName, pdf);
            if (MessageBox.Show($"PDF generado:\n{dlg.FileName}\n\n¿Abrirlo ahora?", "Listo", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo generar el PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static string Resumen(DeveloperEvaluation e)
    {
        var partes = new[] { e.Strengths, e.Weaknesses }.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Replace("\r", " ").Replace("\n", " "));
        return Recorte(string.Join("   ·   ", partes), 130);
    }

    private static string Estrellas(int? r) =>
        r is null or < 1 ? "Sin calificar"
        : new string('★', Math.Min(5, r.Value)) + new string('☆', Math.Max(0, 5 - r.Value)) + $"  ({r}/5)";

    private static string Recorte(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? "—" : (s.Length <= max ? s : s[..max] + "…");

    private static string KindLabel(MilestoneKind k) => k switch
    {
        MilestoneKind.Logro => "🏆 Logro", MilestoneKind.Proyecto => "📁 Proyecto",
        MilestoneKind.Certificacion => "🎓 Certificación", MilestoneKind.Reconocimiento => "⭐ Reconocimiento", _ => "• Otro"
    };
}
