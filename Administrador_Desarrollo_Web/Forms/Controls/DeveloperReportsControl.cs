using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Reportes por desarrollador: el líder registra sus fortalezas/debilidades y calificación
/// (evaluaciones) y sus hitos, y genera un PDF con todo. Solo administración.
/// </summary>
public class DeveloperReportsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly DeveloperReportService _report;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;

    private ComboBox _cbDev = null!;
    private DataGridView _gridEval = null!, _gridHito = null!;
    private List<Developer> _devs = [];
    private List<DeveloperEvaluation> _evals = [];
    private List<DeveloperMilestone> _hitos = [];

    public DeveloperReportsControl(AppDbContext db, DeveloperReportService report, AuditService audit, CurrentUserContext currentUser)
    {
        _db = db; _report = report; _audit = audit; _currentUser = currentUser;
        BuildUI();
        LoadDevelopers();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ───────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        toolbar.Controls.Add(new Label { Text = "Desarrollador:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbDev = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbDev.SelectedIndexChanged += (_, _) => LoadSections();
        toolbar.Controls.Add(_cbDev);
        var btnPdf = AppTheme.MakePrimaryButton("📄 Generar PDF", 160); btnPdf.Margin = new Padding(0, 2, 8, 0); btnPdf.Click += BtnPdf_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(0, 2, 0, 0); btnReload.Click += (_, _) => { LoadDevelopers(); };
        toolbar.Controls.AddRange([btnPdf, btnReload]);

        // ── Evaluaciones ─────────────────────────────────────────
        _gridEval = AppTheme.MakeGrid(); _gridEval.MultiSelect = false;
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",  Name = "Date", FillWeight = 12 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Periodo", Name = "Per", FillWeight = 12 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Calif.", Name = "Rate", FillWeight = 10 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Evaluó", Name = "By",   FillWeight = 20 });
        _gridEval.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fortalezas / debilidades (resumen)", Name = "Sum", FillWeight = 46 });
        _gridEval.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) EvalEdit(); };
        var evalPanel = SectionPanel("Evaluaciones de líder (fortalezas y debilidades)",
            _gridEval, EvalNew, EvalEdit, EvalDelete);

        // ── Hitos ────────────────────────────────────────────────
        _gridHito = AppTheme.MakeGrid(); _gridHito.MultiSelect = false;
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Name = "Date",  FillWeight = 12 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",  Name = "Kind",  FillWeight = 16 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title", FillWeight = 30 });
        _gridHito.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc", FillWeight = 42 });
        _gridHito.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) HitoEdit(); };
        var hitoPanel = SectionPanel("Hitos", _gridHito, HitoNew, HitoEdit, HitoDelete);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(evalPanel, 0, 1);
        tbl.Controls.Add(hitoPanel, 0, 2);
        Controls.Add(tbl);
    }

    private static Panel SectionPanel(string title, DataGridView grid, Action nuevo, Action editar, Action eliminar)
    {
        var pnl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(10, 2, 10, 8) };
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        bar.Controls.Add(new Label { Text = title, AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Margin = new Padding(0, 6, 16, 0) });
        var bNew = AppTheme.MakePrimaryButton("➕ Nuevo", 100, 26); bNew.Margin = new Padding(0, 2, 6, 0); bNew.Click += (_, _) => nuevo();
        var bEdit = AppTheme.MakeSecondaryButton("✏ Editar", 95, 26); bEdit.Margin = new Padding(0, 2, 6, 0); bEdit.Click += (_, _) => editar();
        var bDel = AppTheme.MakeDangerButton("🗑 Eliminar", 100, 26); bDel.Margin = new Padding(0, 2, 0, 0); bDel.Click += (_, _) => eliminar();
        bar.Controls.AddRange([bNew, bEdit, bDel]);

        pnl.Controls.Add(bar, 0, 0);
        pnl.Controls.Add(grid, 0, 1);
        return pnl;
    }

    private void LoadDevelopers()
    {
        var sel = SelectedDevId();
        _devs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        _cbDev.Items.Clear();
        foreach (var d in _devs) _cbDev.Items.Add(d.FullName);
        if (_cbDev.Items.Count > 0)
            _cbDev.SelectedIndex = Math.Max(0, _devs.FindIndex(d => d.Id == sel));
        else LoadSections();
    }

    private int SelectedDevId() =>
        _cbDev.SelectedIndex >= 0 && _cbDev.SelectedIndex < _devs.Count ? _devs[_cbDev.SelectedIndex].Id : -1;

    private void LoadSections()
    {
        _gridEval.Rows.Clear(); _gridHito.Rows.Clear();
        _evals = []; _hitos = [];
        int devId = SelectedDevId();
        if (devId < 0) return;

        _evals = _db.DeveloperEvaluations.Where(e => e.DeveloperId == devId)
            .OrderByDescending(e => e.EvaluationDate).ThenByDescending(e => e.Id).ToList();
        foreach (var e in _evals)
        {
            var resumen = Join(e.Strengths, e.Weaknesses);
            _gridEval.Rows.Add(e.EvaluationDate.ToString("dd/MM/yyyy"), e.PeriodLabel ?? "—",
                e.OverallRating is >= 1 and <= 5 ? $"{e.OverallRating}/5" : "—", e.EvaluatorName ?? "—", resumen);
        }

        _hitos = _db.DeveloperMilestones.Where(m => m.DeveloperId == devId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToList();
        foreach (var m in _hitos)
            _gridHito.Rows.Add(m.Date.ToString("dd/MM/yyyy"), KindLabel(m.Kind), m.Title, Recorte(m.Description, 80));
    }

    // ── Evaluaciones ─────────────────────────────────────────────
    private void EvalNew()
    {
        int devId = SelectedDevId();
        if (devId < 0) { Aviso("Selecciona un desarrollador."); return; }
        using var frm = new DeveloperEvaluationForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        var ev = frm.Result;
        ev.DeveloperId = devId;
        ev.EvaluatorUserId = _currentUser.User?.Id;
        ev.EvaluatorName = _currentUser.User?.FullName;
        ev.CreatedAt = DateTime.UtcNow;
        _db.DeveloperEvaluations.Add(ev); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "DeveloperEvaluation", ev.Id.ToString(), $"Evaluación de {NombreDev(devId)}");
        LoadSections();
    }

    private void EvalEdit()
    {
        if (Seleccionado(_gridEval, _evals) is not { } ev) { Aviso("Selecciona una evaluación."); return; }
        using var frm = new DeveloperEvaluationForm(ev);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "DeveloperEvaluation", ev.Id.ToString(), $"Evaluación de {NombreDev(ev.DeveloperId)}");
        LoadSections();
    }

    private void EvalDelete()
    {
        if (Seleccionado(_gridEval, _evals) is not { } ev) { Aviso("Selecciona una evaluación."); return; }
        if (MessageBox.Show("¿Eliminar esta evaluación?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.DeveloperEvaluations.Remove(ev); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "DeveloperEvaluation", ev.Id.ToString(), $"Evaluación de {NombreDev(ev.DeveloperId)}");
        LoadSections();
    }

    // ── Hitos ────────────────────────────────────────────────────
    private void HitoNew()
    {
        int devId = SelectedDevId();
        if (devId < 0) { Aviso("Selecciona un desarrollador."); return; }
        using var frm = new DeveloperMilestoneForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        var m = frm.Result;
        m.DeveloperId = devId;
        m.CreatedByUserId = _currentUser.User?.Id;
        m.CreatedAt = DateTime.UtcNow;
        _db.DeveloperMilestones.Add(m); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "DeveloperMilestone", m.Id.ToString(), $"Hito de {NombreDev(devId)}: {m.Title}");
        LoadSections();
    }

    private void HitoEdit()
    {
        if (Seleccionado(_gridHito, _hitos) is not { } m) { Aviso("Selecciona un hito."); return; }
        using var frm = new DeveloperMilestoneForm(m);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "DeveloperMilestone", m.Id.ToString(), $"Hito de {NombreDev(m.DeveloperId)}: {m.Title}");
        LoadSections();
    }

    private void HitoDelete()
    {
        if (Seleccionado(_gridHito, _hitos) is not { } m) { Aviso("Selecciona un hito."); return; }
        if (MessageBox.Show("¿Eliminar este hito?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.DeveloperMilestones.Remove(m); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "DeveloperMilestone", m.Id.ToString(), $"Hito de {NombreDev(m.DeveloperId)}");
        LoadSections();
    }

    // ── PDF ──────────────────────────────────────────────────────
    private async void BtnPdf_Click(object? s, EventArgs e)
    {
        int devId = SelectedDevId();
        if (devId < 0) { Aviso("Selecciona un desarrollador."); return; }
        if (!_report.ConverterAvailable(out var diag))
        { MessageBox.Show(diag, "Generar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var nombre = NombreDev(devId);
        using var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = $"Reporte_{Sanea(nombre)}_{DateTime.Now:yyyyMMdd}.pdf" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var pdf = await _report.BuildPdfAsync(devId);
            File.WriteAllBytes(dlg.FileName, pdf);
            if (MessageBox.Show($"PDF generado:\n{dlg.FileName}\n\n¿Abrirlo ahora?", "Listo", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo generar el PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadDevelopers(); }

    // ── Helpers ──────────────────────────────────────────────────
    private static T? Seleccionado<T>(DataGridView grid, List<T> lista) where T : class
        => grid.CurrentRow is { Index: >= 0 } r && r.Index < lista.Count ? lista[r.Index] : null;

    private string NombreDev(int id) => _devs.FirstOrDefault(d => d.Id == id)?.FullName ?? $"#{id}";
    private static void Aviso(string m) => MessageBox.Show(m, "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static string Join(string? a, string? b)
    {
        var partes = new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Replace("\r", " ").Replace("\n", " "));
        return Recorte(string.Join("  ·  ", partes), 120);
    }

    private static string Recorte(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? "—" : (s.Length <= max ? s : s[..max] + "…");

    private static string Sanea(string s) => string.Join("_", s.Split(Path.GetInvalidFileNameChars()));

    private static string KindLabel(MilestoneKind k) => k switch
    {
        MilestoneKind.Logro => "🏆 Logro", MilestoneKind.Proyecto => "📁 Proyecto",
        MilestoneKind.Certificacion => "🎓 Certificación", MilestoneKind.Reconocimiento => "⭐ Reconocimiento", _ => "• Otro"
    };
}
