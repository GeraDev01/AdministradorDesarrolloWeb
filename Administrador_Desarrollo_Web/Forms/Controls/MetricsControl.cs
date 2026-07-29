using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Medidor de tiempo de vida: antigüedad y ciclo de vida de cada requerimiento y
/// carga/atrasos por desarrollador. Todo derivado de las fechas existentes.
/// </summary>
public class MetricsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly ReportService _report;

    private DataGridView _gridReqs = null!;
    private DataGridView _gridDevs = null!;
    private FlowLayoutPanel _kpis = null!;
    private List<ReqMetric> _reqMetrics = [];
    private List<DevMetric> _devMetrics = [];

    private sealed record ReqMetric(int Id, string Title, string Status, int DaysAlive, int DaysInStatus, string Committed, int? DaysToCommit, bool Overdue, string Devs, int Progress);
    private sealed record DevMetric(string Dev, int Active, int Overdue, int AvgAge, int OldestAge);

    public MetricsControl(AppDbContext db, ReportService report)
    {
        _db = db; _report = report;
        BuildUI(); LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _kpis = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(12, 12, 12, 4), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(12, 4, 12, 4), BackColor = AppTheme.ContentBg };
        var btnReload = AppTheme.MakePrimaryButton("🔄 Recalcular", 120); btnReload.Margin = new Padding(0, 2, 8, 0); btnReload.Click += (_, _) => LoadData();
        var btnExpReq = AppTheme.MakeSecondaryButton("📊 Excel requerimientos", 190); btnExpReq.Margin = new Padding(0, 2, 8, 0); btnExpReq.Click += (_, _) => ExportReqs();
        var btnExpDev = AppTheme.MakeSecondaryButton("📊 Excel por desarrollador", 200); btnExpDev.Margin = new Padding(0, 2, 0, 0); btnExpDev.Click += (_, _) => ExportDevs();
        toolbar.Controls.AddRange([btnReload, btnExpReq, btnExpDev]);

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        var tabReqs = new TabPage("  📋  Requerimientos  ");
        _gridReqs = AppTheme.MakeGrid();
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Width = 45, FillWeight = 4 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title", FillWeight = 24 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Status", FillWeight = 12 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días vivo", Name = "Alive", FillWeight = 8 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días en estado", Name = "InStatus", FillWeight = 9 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Compromiso", Name = "Committed", FillWeight = 11 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días restantes/atraso", Name = "ToCommit", FillWeight = 12 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrolladores", Name = "Devs", FillWeight = 16 });
        _gridReqs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Avance", Name = "Progress", FillWeight = 6 });
        var pnlReqs = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pnlReqs.Controls.Add(_gridReqs); tabReqs.Controls.Add(pnlReqs);

        var tabDevs = new TabPage("  👤  Por Desarrollador  ");
        _gridDevs = AppTheme.MakeGrid();
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev", FillWeight = 34 });
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activos", Name = "Active", FillWeight = 14 });
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Atrasados", Name = "Overdue", FillWeight = 16 });
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Edad prom. (días)", Name = "Avg", FillWeight = 18 });
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Más antiguo (días)", Name = "Oldest", FillWeight = 18 });
        var pnlDevs = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pnlDevs.Controls.Add(_gridDevs); tabDevs.Controls.Add(pnlDevs);

        tabs.TabPages.AddRange([tabReqs, tabDevs]);

        tbl.Controls.Add(_kpis, 0, 0);
        tbl.Controls.Add(toolbar, 0, 1);
        tbl.Controls.Add(tabs, 0, 2);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        var today = DateTime.Today;
        var reqs = _db.Requirements.Include(r => r.Assignments).ThenInclude(a => a.Developer).ToList();

        _reqMetrics = reqs.Select(r =>
        {
            bool closed = r.Status is RequirementStatus.Entregado or RequirementStatus.Cancelado;
            var endRef = r.ActualDeliveryDate ?? today;
            int daysAlive = Math.Max(0, (int)(endRef.Date - r.CreatedAt.Date).TotalDays);
            int daysInStatus = Math.Max(0, (int)(today - (r.StatusChangedAt ?? r.CreatedAt).Date).TotalDays);
            int? toCommit = r.CommittedDeliveryDate.HasValue ? (int)(r.CommittedDeliveryDate.Value.Date - today).TotalDays : null;
            bool overdue = r.CommittedDeliveryDate < today && !closed;
            var devs = string.Join(", ", r.Assignments.Select(a => a.Developer.FullName));
            return new ReqMetric(r.Id, r.Title, StatusLabel(r.Status), daysAlive, daysInStatus,
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", closed ? null : toCommit, overdue, devs, r.ProgressPercent);
        }).OrderByDescending(m => m.Overdue).ThenByDescending(m => m.DaysAlive).ToList();

        // Per-developer
        var devsList = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        _devMetrics = devsList.Select(dev =>
        {
            var mine = reqs.Where(r => r.Assignments.Any(a => a.DeveloperId == dev.Id)
                                       && r.Status is not (RequirementStatus.Entregado or RequirementStatus.Cancelado)).ToList();
            int active = mine.Count;
            int overdue = mine.Count(r => r.CommittedDeliveryDate < today);
            int avg = mine.Count == 0 ? 0 : (int)mine.Average(r => (today.Date - r.CreatedAt.Date).TotalDays);
            int oldest = mine.Count == 0 ? 0 : mine.Max(r => (int)(today.Date - r.CreatedAt.Date).TotalDays);
            return new DevMetric(dev.FullName, active, overdue, avg, oldest);
        }).OrderByDescending(m => m.Overdue).ThenByDescending(m => m.Active).ToList();

        FillGrids();
        BuildKpis(reqs, today);
    }

    private void FillGrids()
    {
        _gridReqs.Rows.Clear();
        foreach (var m in _reqMetrics)
        {
            string toCommit = m.DaysToCommit == null ? "—" : m.DaysToCommit >= 0 ? $"faltan {m.DaysToCommit}" : $"⚠ atraso {-m.DaysToCommit.Value}";
            int i = _gridReqs.Rows.Add(m.Id, m.Title, m.Status, m.DaysAlive, m.DaysInStatus, m.Committed, toCommit, m.Devs, $"{m.Progress}%");
            if (m.Overdue) _gridReqs.Rows[i].Cells["ToCommit"].Style.ForeColor = AppTheme.Danger;
            if (m.Overdue) _gridReqs.Rows[i].Cells["ToCommit"].Style.Font = AppTheme.BoldFont;
        }

        _gridDevs.Rows.Clear();
        foreach (var m in _devMetrics)
        {
            int i = _gridDevs.Rows.Add(m.Dev, m.Active, m.Overdue, m.AvgAge, m.OldestAge);
            if (m.Overdue > 0) _gridDevs.Rows[i].Cells["Overdue"].Style.ForeColor = AppTheme.Danger;
            _gridDevs.Rows[i].Cells["Overdue"].Style.Font = AppTheme.BoldFont;
        }
    }

    private void BuildKpis(List<Requirement> reqs, DateTime today)
    {
        _kpis.Controls.Clear();
        var active = reqs.Where(r => r.Status is not (RequirementStatus.Entregado or RequirementStatus.Cancelado)).ToList();
        int overdue = active.Count(r => r.CommittedDeliveryDate < today);
        var deliveredWithCommit = reqs.Where(r => r.Status == RequirementStatus.Entregado && r.ActualDeliveryDate.HasValue && r.CommittedDeliveryDate.HasValue).ToList();
        int onTime = deliveredWithCommit.Count(r => r.ActualDeliveryDate!.Value.Date <= r.CommittedDeliveryDate!.Value.Date);
        int onTimePct = deliveredWithCommit.Count == 0 ? 0 : (int)Math.Round(100.0 * onTime / deliveredWithCommit.Count);
        int avgAge = active.Count == 0 ? 0 : (int)active.Average(r => (today.Date - r.CreatedAt.Date).TotalDays);

        _kpis.Controls.Add(Kpi("Activos", active.Count.ToString(), AppTheme.SidebarActive));
        _kpis.Controls.Add(Kpi("Atrasados", overdue.ToString(), overdue > 0 ? AppTheme.Danger : AppTheme.Success));
        _kpis.Controls.Add(Kpi("Entregados a tiempo", $"{onTimePct}%", onTimePct >= 70 ? AppTheme.Success : AppTheme.Warning));
        _kpis.Controls.Add(Kpi("Edad prom. activos", $"{avgAge} d", AppTheme.TextPrimary));
    }

    private static Panel Kpi(string title, string value, Color color)
    {
        var card = new Panel { Width = 210, Height = 74, Margin = new Padding(0, 0, 12, 0), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = color });
        card.Controls.Add(new Label { Text = title, Location = new Point(14, 8), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        card.Controls.Add(new Label { Text = value, Location = new Point(12, 28), AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", 20f, FontStyle.Bold) });
        return card;
    }

    private void ExportReqs()
    {
        var path = _report.PromptSaveDialog("Metricas_Requerimientos");
        if (path == null) return;
        _report.ExportToExcel(_reqMetrics, ["ID", "Título", "Estado", "Días vivo", "Días en estado", "Compromiso", "Días rest./atraso", "Desarrolladores", "Avance %"],
            m => [m.Id, m.Title, m.Status, m.DaysAlive, m.DaysInStatus, m.Committed, m.DaysToCommit, m.Devs, m.Progress], "Requerimientos", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportDevs()
    {
        var path = _report.PromptSaveDialog("Metricas_PorDesarrollador");
        if (path == null) return;
        _report.ExportToExcel(_devMetrics, ["Desarrollador", "Activos", "Atrasados", "Edad prom (días)", "Más antiguo (días)"],
            m => [m.Dev, m.Active, m.Overdue, m.AvgAge, m.OldestAge], "PorDesarrollador", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar => "Por estimar", RequirementStatus.Estimado => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo", RequirementStatus.EnPruebas => "En pruebas",
        RequirementStatus.PorEntregar => "Por entregar", RequirementStatus.Entregado => "Entregado",
        RequirementStatus.Cancelado => "Cancelado", _ => s.ToString()
    };

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
