using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Centro de reportes: elige un reporte, período y uno o varios desarrolladores,
/// previsualiza en tabla y en dashboard (KPIs + gráfica), y exporta a Excel o correo.
/// </summary>
public class ReportsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly ReportService _report;
    private readonly EmailService _email;

    private ListBox _list = null!;
    private DateTimePicker _dtpFrom = null!, _dtpTo = null!;
    private Button _btnDevs = null!;
    private DataGridView _grid = null!;
    private Label _lblDesc = null!;
    private FlowLayoutPanel _kpis = null!;
    private SimpleBarChart _chart = null!;

    private readonly List<ReportDef> _reports;
    private List<Developer> _devs = [];
    private readonly HashSet<int> _selectedDevIds = []; // vacío = todos
    private string[] _lastHeaders = [];
    private List<object?[]> _lastRows = [];
    private string _lastName = "Reporte";

    // CatCol = columna de categoría; ValCol = columna numérica a sumar (-1 = contar filas).
    private sealed record ReportDef(string Name, string Description,
        Func<ReportParams, (string[] Headers, List<object?[]> Rows)> Run,
        int CatCol, int ValCol, string ChartTitle);
    private sealed record ReportParams(DateTime From, DateTime To, HashSet<int> DevIds);

    public ReportsControl(AppDbContext db, ReportService report, EmailService email)
    {
        _db = db; _report = report; _email = email;
        _reports =
        [
            new("Carga de trabajo por desarrollador", "Requerimientos activos, atrasados, horas y edad promedio por desarrollador.", RepWorkload, 0, 2, "Activos por desarrollador"),
            new("Requerimientos pendientes", "Todos los requerimientos sin entregar ni cancelar, con atraso y avance.", RepPending, 2, -1, "Pendientes por estado"),
            new("Avance de requerimientos", "Porcentaje de avance de cada requerimiento (KPI = avance promedio).", RepProgress, 1, 4, "Avance (%) por requerimiento"),
            new("Altas por período (tipo de item)", "Items dados de alta en el rango de fechas, por origen/tipo.", RepIntake, 2, -1, "Altas por origen/tipo"),
            new("Requerimientos por desarrollador", "Requerimientos asignados a las personas seleccionadas (o a todas).", RepByDeveloper, 0, -1, "Requerimientos por desarrollador"),
            new("Ciclo de vida de requerimientos", "Días vivo, días en el estado actual y lead time de cada requerimiento.", RepLifecycle, 2, -1, "Requerimientos por estado"),
            new("Entregas: a tiempo vs tardías", "Requerimientos entregados: compromiso vs entrega real y atraso.", RepDeliveries, 6, -1, "Entregas por resultado"),
            new("Tickets de DevOps por responsable", "Work items de Azure DevOps agrupados por persona asignada.", RepDevOpsByAssignee, 0, -1, "Tickets por responsable"),
            new("Requerimientos por origen", "Conteo de requerimientos por origen (Manual / DevOps / Correo).", RepBySource, 0, 1, "Requerimientos por origen"),
            new("Desempeño (puntos) por período", "Puntos individuales sumados en el rango de fechas.", RepPerformance, 0, 1, "Puntos por desarrollador"),
            new("Estimado vs. real (por requerimiento)", "Horas estimadas vs. horas realmente dedicadas (cronómetro) EN EL PERÍODO por requerimiento, con desviación.", RepEstimateVsActual, 1, 3, "Horas reales por requerimiento"),
            new("Estimado vs. real (por desarrollador)", "Horas estimadas (prorrateadas entre asignados) vs. horas realmente dedicadas EN EL PERÍODO por cada desarrollador.", RepEstimateVsActualByDev, 0, 2, "Horas reales por desarrollador"),
            new("Vacaciones por período", "Solicitudes de vacaciones cuyo inicio cae en el rango.", RepVacations, 0, 3, "Días de vacaciones por desarrollador"),
            new("Rotaciones de equipo por período", "Movimientos de desarrolladores entre equipos en el rango.", RepRotations, 3, -1, "Rotaciones por equipo destino"),
            new("Resumen por equipo", "Integrantes, requerimientos activos y puntos propios por equipo.", RepTeamSummary, 0, 2, "Requerimientos activos por equipo"),
        ];
        BuildUI();
        LoadDevs();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var leftTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(10, 10, 6, 10) };
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        leftTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        leftTbl.Controls.Add(new Label { Text = "📑 Reportes disponibles", Dock = DockStyle.Fill, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary }, 0, 0);
        _list = new ListBox { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        foreach (var r in _reports) _list.Items.Add(r.Name);
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedIndex >= 0) { _lblDesc.Text = _reports[_list.SelectedIndex].Description; Generate(); } };
        leftTbl.Controls.Add(_list, 0, 1);
        root.Controls.Add(leftTbl, 0, 0);

        var rightTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(6, 10, 10, 10) };
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        bar.Controls.Add(new Label { Text = "Del:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _dtpFrom = new DateTimePicker { Width = 110, Format = DateTimePickerFormat.Short, Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Margin = new Padding(0, 4, 8, 0) };
        _dtpFrom.ValueChanged += (_, _) => Generate();
        bar.Controls.Add(_dtpFrom);
        bar.Controls.Add(new Label { Text = "Al:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _dtpTo = new DateTimePicker { Width = 110, Format = DateTimePickerFormat.Short, Value = DateTime.Today, Margin = new Padding(0, 4, 12, 0) };
        _dtpTo.ValueChanged += (_, _) => Generate();
        bar.Controls.Add(_dtpTo);
        _btnDevs = AppTheme.MakeSecondaryButton("Devs: Todos  ▾", 170); _btnDevs.Margin = new Padding(0, 2, 12, 0); _btnDevs.Click += (_, _) => OpenDevPicker();
        bar.Controls.Add(_btnDevs);
        var btnGen = AppTheme.MakePrimaryButton("▶ Generar", 105); btnGen.Margin = new Padding(0, 2, 8, 0); btnGen.Click += (_, _) => Generate();
        var btnXls = AppTheme.MakeSecondaryButton("📊 Excel", 90); btnXls.Margin = new Padding(0, 2, 8, 0); btnXls.Click += (_, _) => ExportExcel();
        var btnMail = AppTheme.MakeSecondaryButton("✉ Enviar", 95); btnMail.Margin = new Padding(0, 2, 0, 0); btnMail.Click += (_, _) => EmailReport();
        bar.Controls.AddRange([btnGen, btnXls, btnMail]);
        rightTbl.Controls.Add(bar, 0, 0);

        _lblDesc = new Label { Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        rightTbl.Controls.Add(_lblDesc, 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        var tabData = new TabPage("  📋  Datos  ");
        _grid = AppTheme.MakeGrid();
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);
        tabData.Controls.Add(pnlGrid);

        var tabDash = new TabPage("  📊  Dashboard  ");
        var dashTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(4, 8, 4, 4) };
        dashTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
        dashTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        dashTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _kpis = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, BackColor = AppTheme.ContentBg };
        dashTbl.Controls.Add(_kpis, 0, 0);
        _chart = new SimpleBarChart { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 0) };
        dashTbl.Controls.Add(_chart, 0, 1);
        tabDash.Controls.Add(dashTbl);

        tabs.TabPages.AddRange([tabData, tabDash]);
        rightTbl.Controls.Add(tabs, 0, 2);
        root.Controls.Add(rightTbl, 1, 0);

        Controls.Add(root);
    }

    private void LoadDevs() => _devs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();

    private void UpdateDevButtonText()
    {
        _btnDevs.Text = _selectedDevIds.Count switch
        {
            0 => "Devs: Todos  ▾",
            1 => $"Dev: {_devs.FirstOrDefault(d => d.Id == _selectedDevIds.First())?.FullName ?? "1"}  ▾",
            _ => $"Devs: {_selectedDevIds.Count} seleccionados  ▾"
        };
    }

    private void OpenDevPicker()
    {
        var popup = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true, Size = new Size(250, 360), BackColor = AppTheme.Border, Padding = new Padding(1) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var chkAll = new CheckBox { Text = "(Todos)", Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, Checked = _selectedDevIds.Count == 0 };
        var clb = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Font = AppTheme.DefaultFont, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        foreach (var d in _devs) clb.Items.Add(d.FullName, _selectedDevIds.Contains(d.Id));

        bool sync = false;
        chkAll.CheckedChanged += (_, _) => { if (sync) return; if (chkAll.Checked) { sync = true; for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); sync = false; } };
        clb.ItemCheck += (_, ev) => { if (!sync && ev.NewValue == CheckState.Checked) { sync = true; chkAll.Checked = false; sync = false; } };

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var btnApply = AppTheme.MakePrimaryButton("Aplicar", 100, 28); btnApply.Margin = new Padding(4, 4, 0, 0);
        btnApply.Click += (_, _) =>
        {
            _selectedDevIds.Clear();
            if (!chkAll.Checked)
                for (int i = 0; i < clb.Items.Count; i++)
                    if (clb.GetItemChecked(i)) _selectedDevIds.Add(_devs[i].Id);
            UpdateDevButtonText();
            popup.Close();
            Generate();
        };
        btns.Controls.Add(btnApply);

        layout.Controls.Add(chkAll, 0, 0);
        layout.Controls.Add(clb, 0, 1);
        layout.Controls.Add(btns, 0, 2);
        popup.Controls.Add(layout);
        popup.Deactivate += (_, _) => { try { popup.Close(); } catch { } };

        var anchor = _btnDevs.PointToScreen(new Point(0, _btnDevs.Height));
        var screen = Screen.FromControl(_btnDevs).WorkingArea;
        if (anchor.X + popup.Width > screen.Right) anchor.X = screen.Right - popup.Width;
        if (anchor.Y + popup.Height > screen.Bottom) anchor.Y = screen.Bottom - popup.Height;
        popup.Location = anchor;
        popup.Show();
    }

    private void Generate()
    {
        if (_list.SelectedIndex < 0) return;
        var def = _reports[_list.SelectedIndex];
        var pars = new ReportParams(_dtpFrom.Value.Date, _dtpTo.Value.Date, _selectedDevIds);
        try
        {
            var (headers, rows) = def.Run(pars);
            _lastHeaders = headers; _lastRows = rows; _lastName = def.Name;
            RenderGrid(headers, rows);
            BuildDashboard(def, rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo generar el reporte:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderGrid(string[] headers, List<object?[]> rows)
    {
        _grid.Columns.Clear();
        foreach (var h in headers) _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, Name = "c" + _grid.Columns.Count });
        _grid.Rows.Clear();
        foreach (var r in rows) _grid.Rows.Add(r.Select(v => (object?)(v is DateTime dt ? dt.ToString("dd/MM/yyyy") : v?.ToString() ?? "")).ToArray());
    }

    private void BuildDashboard(ReportDef def, List<object?[]> rows)
    {
        var bars = rows.Where(r => def.CatCol < r.Length)
            .GroupBy(r => r[def.CatCol]?.ToString() ?? "—")
            .Select(g => (Label: g.Key, Value: def.ValCol < 0 ? g.Count() : g.Sum(r => ToDouble(def.ValCol < r.Length ? r[def.ValCol] : null))))
            .OrderByDescending(x => Math.Abs(x.Value))
            .Take(14)
            .ToList();
        _chart.SetData(def.ChartTitle, bars);

        _kpis.Controls.Clear();
        _kpis.Controls.Add(Kpi("Filas", rows.Count.ToString(), AppTheme.SidebarActive));
        if (def.ValCol >= 0)
        {
            var vals = rows.Select(r => ToDouble(def.ValCol < r.Length ? r[def.ValCol] : null)).ToList();
            _kpis.Controls.Add(Kpi("Total", Fmt(vals.Sum()), AppTheme.Success));
            _kpis.Controls.Add(Kpi("Promedio", Fmt(vals.Count > 0 ? vals.Average() : 0), AppTheme.TextPrimary));
            _kpis.Controls.Add(Kpi("Máximo", Fmt(vals.Count > 0 ? vals.Max() : 0), AppTheme.Warning));
        }
        else
        {
            _kpis.Controls.Add(Kpi("Categorías", bars.Count.ToString(), AppTheme.Success));
            if (bars.Count > 0) _kpis.Controls.Add(Kpi("Mayor: " + Trunc(bars[0].Label, 14), Fmt(bars[0].Value), AppTheme.Warning));
        }
    }

    private static string Fmt(double v) => v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.#");
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    private static double ToDouble(object? v) => v switch
    {
        null => 0, int i => i, long l => l, double d => d, decimal m => (double)m,
        string s => double.TryParse(new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()), out var r) ? r : 0,
        _ => double.TryParse(v.ToString(), out var r) ? r : 0
    };

    private static Panel Kpi(string title, string value, Color color)
    {
        var card = new Panel { Width = 175, Height = 66, Margin = new Padding(0, 0, 10, 0), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = color });
        card.Controls.Add(new Label { Text = title, Location = new Point(12, 6), AutoSize = false, Size = new Size(155, 18), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, AutoEllipsis = true });
        card.Controls.Add(new Label { Text = value, Location = new Point(11, 26), AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", 18f, FontStyle.Bold) });
        return card;
    }

    private void ExportExcel()
    {
        if (_lastRows.Count == 0) { MessageBox.Show("Genera un reporte primero.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var path = _report.PromptSaveDialog(Sanitize(_lastName));
        if (path == null) return;
        _report.ExportToExcel(_lastRows, _lastHeaders, r => r, "Reporte", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EmailReport()
    {
        if (_lastRows.Count == 0) { MessageBox.Show("Genera un reporte primero.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!_email.IsConfigured(out var diag)) { MessageBox.Show(diag, "Correo", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var temp = Path.Combine(Path.GetTempPath(), $"{Sanitize(_lastName)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        _report.ExportToExcel(_lastRows, _lastHeaders, r => r, "Reporte", temp);
        using var frm = new EmailComposeForm(_email, null, $"Reporte: {_lastName}", temp);
        frm.ShowDialog(FindForm());
    }

    private static string Sanitize(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

    // ── Generadores (respetan el conjunto de desarrolladores) ────
    private List<Requirement> LoadReqs() =>
        _db.Requirements.Include(r => r.Assignments).ThenInclude(a => a.Developer).ToList();

    private static bool ForDev(Requirement r, HashSet<int> ids) => ids.Count == 0 || r.Assignments.Any(a => ids.Contains(a.DeveloperId));

    private (string[], List<object?[]>) RepWorkload(ReportParams p)
    {
        var today = DateTime.Today;
        var reqs = LoadReqs();
        var teams = _db.Teams.AsNoTracking().ToDictionary(t => t.Id, t => t.Name);
        var rows = _devs.Where(d => p.DevIds.Count == 0 || p.DevIds.Contains(d.Id)).Select(d =>
        {
            var mine = reqs.Where(r => r.Assignments.Any(a => a.DeveloperId == d.Id) && !Closed(r)).ToList();
            int overdue = mine.Count(r => r.CommittedDeliveryDate < today);
            decimal hrs = mine.Sum(r => r.EstimateHours ?? 0);
            int avg = mine.Count == 0 ? 0 : (int)mine.Average(r => (today - r.CreatedAt.Date).TotalDays);
            string team = d.TeamId != null && teams.TryGetValue(d.TeamId.Value, out var tn) ? tn : "—";
            return new object?[] { d.FullName, team, mine.Count, overdue, hrs, avg };
        }).OrderByDescending(r => (int)r[2]!).ToList();
        return (["Desarrollador", "Equipo", "Activos", "Atrasados", "Hrs estimadas", "Edad prom (días)"], rows);
    }

    private (string[], List<object?[]>) RepPending(ReportParams p)
    {
        var today = DateTime.Today;
        var rows = LoadReqs().Where(r => !Closed(r) && ForDev(r, p.DevIds))
            .OrderBy(r => r.CommittedDeliveryDate ?? DateTime.MaxValue)
            .Select(r => new object?[]
            {
                r.Id, r.Title, StatusLabel(r.Status), PriorityLabel(r.Priority),
                string.Join(", ", r.Assignments.Select(a => a.Developer.FullName)),
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                r.CommittedDeliveryDate < today ? (int)(today - r.CommittedDeliveryDate!.Value.Date).TotalDays : 0,
                $"{r.ProgressPercent}%", SourceLabel(r.Source)
            }).ToList();
        return (["ID", "Título", "Estado", "Prioridad", "Desarrolladores", "F. Compromiso", "Días atraso", "Avance", "Origen"], rows);
    }

    private (string[], List<object?[]>) RepProgress(ReportParams p)
    {
        var rows = LoadReqs().Where(r => !Closed(r) && ForDev(r, p.DevIds))
            .OrderBy(r => r.ProgressPercent)
            .Select(r => new object?[]
            {
                r.Id, r.Title, StatusLabel(r.Status), $"{r.ProgressPercent}%", r.ProgressPercent,
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                string.Join(", ", r.Assignments.Select(a => a.Developer.FullName))
            }).ToList();
        return (["ID", "Título", "Estado", "Avance", "Avance %", "F. Compromiso", "Desarrolladores"], rows);
    }

    private (string[], List<object?[]>) RepIntake(ReportParams p)
    {
        var to = p.To.AddDays(1);
        var rows = LoadReqs()
            .Where(r => r.CreatedAt >= p.From && r.CreatedAt < to && ForDev(r, p.DevIds))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new object?[]
            {
                r.Id, r.Title, SourceLabel(r.Source), PriorityLabel(r.Priority), StatusLabel(r.Status),
                r.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                string.Join(", ", r.Assignments.Select(a => a.Developer.FullName))
            }).ToList();
        return (["ID", "Título", "Origen/Tipo", "Prioridad", "Estado", "Fecha de alta", "Desarrolladores"], rows);
    }

    private (string[], List<object?[]>) RepByDeveloper(ReportParams p)
    {
        var reqs = LoadReqs();
        var rows = new List<object?[]>();
        foreach (var d in _devs)
        {
            if (p.DevIds.Count > 0 && !p.DevIds.Contains(d.Id)) continue;
            foreach (var r in reqs.Where(r => r.Assignments.Any(a => a.DeveloperId == d.Id)).OrderBy(r => (int)r.Status))
                rows.Add([d.FullName, r.Id, r.Title, StatusLabel(r.Status), r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", $"{r.ProgressPercent}%"]);
        }
        return (["Desarrollador", "ID", "Título", "Estado", "F. Compromiso", "Avance"], rows);
    }

    private (string[], List<object?[]>) RepLifecycle(ReportParams p)
    {
        var today = DateTime.Today;
        var rows = LoadReqs().Where(r => ForDev(r, p.DevIds))
            .OrderByDescending(r => (r.ActualDeliveryDate ?? today).Date.Subtract(r.CreatedAt.Date).TotalDays)
            .Select(r =>
            {
                var end = r.ActualDeliveryDate ?? today;
                int alive = Math.Max(0, (int)(end.Date - r.CreatedAt.Date).TotalDays);
                int inStatus = Math.Max(0, (int)(today - (r.StatusChangedAt ?? r.CreatedAt).Date).TotalDays);
                string onTime = r.Status == RequirementStatus.Entregado && r.ActualDeliveryDate.HasValue && r.CommittedDeliveryDate.HasValue
                    ? (r.ActualDeliveryDate.Value.Date <= r.CommittedDeliveryDate.Value.Date ? "A tiempo" : "Tardía") : "—";
                return new object?[] { r.Id, r.Title, StatusLabel(r.Status), alive, inStatus,
                    r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", r.ActualDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", onTime };
            }).ToList();
        return (["ID", "Título", "Estado", "Días vivo", "Días en estado", "F. Compromiso", "F. Entrega", "¿A tiempo?"], rows);
    }

    private (string[], List<object?[]>) RepDeliveries(ReportParams p)
    {
        var rows = LoadReqs()
            .Where(r => r.Status == RequirementStatus.Entregado && r.ActualDeliveryDate.HasValue
                        && r.ActualDeliveryDate.Value.Date >= p.From && r.ActualDeliveryDate.Value.Date <= p.To
                        && ForDev(r, p.DevIds))
            .OrderBy(r => r.ActualDeliveryDate)
            .Select(r =>
            {
                int delay = r.CommittedDeliveryDate.HasValue ? (int)(r.ActualDeliveryDate!.Value.Date - r.CommittedDeliveryDate.Value.Date).TotalDays : 0;
                return new object?[] { r.Id, r.Title, string.Join(", ", r.Assignments.Select(a => a.Developer.FullName)),
                    r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", r.ActualDeliveryDate!.Value.ToString("dd/MM/yyyy"),
                    delay, delay <= 0 ? "A tiempo" : "Tardía" };
            }).ToList();
        return (["ID", "Título", "Desarrolladores", "F. Compromiso", "F. Entrega", "Atraso (días)", "Resultado"], rows);
    }

    private (string[], List<object?[]>) RepDevOpsByAssignee(ReportParams p)
    {
        var names = p.DevIds.Count == 0 ? null : _devs.Where(d => p.DevIds.Contains(d.Id)).Select(d => d.FullName).ToList();
        var rows = _db.DevOpsTickets.AsNoTracking()
            .OrderBy(t => t.AssignedTo).ThenBy(t => t.State)
            .AsEnumerable()
            .Where(t => names == null || names.Any(n => t.AssignedTo.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new object?[] { string.IsNullOrWhiteSpace(t.AssignedTo) ? "(sin asignar)" : t.AssignedTo, t.ExternalId, t.Title, t.WorkItemType, t.State, t.AreaPath })
            .ToList();
        return (["Responsable", "ID DevOps", "Título", "Tipo", "Estado", "Área"], rows);
    }

    private (string[], List<object?[]>) RepBySource(ReportParams p)
    {
        var reqs = LoadReqs().Where(r => ForDev(r, p.DevIds)).ToList();
        var rows = Enum.GetValues<RequirementSource>().Select(src =>
        {
            var g = reqs.Where(r => r.Source == src).ToList();
            return new object?[] { SourceLabel(src), g.Count, g.Count(r => !Closed(r)), g.Count(r => r.Status == RequirementStatus.Entregado) };
        }).ToList();
        return (["Origen", "Total", "Activos", "Entregados"], rows);
    }

    private (string[], List<object?[]>) RepPerformance(ReportParams p)
    {
        var from = p.From; var to = p.To.AddDays(1);
        var entries = _db.PointEntries.Include(e => e.Developer)
            .Where(e => e.Date >= from && e.Date < to && e.ApprovalStatus == PointApprovalStatus.Aprobado).ToList()
            .Where(e => p.DevIds.Count == 0 || p.DevIds.Contains(e.DeveloperId)).ToList();
        var rows = entries.GroupBy(e => e.Developer.FullName)
            .Select(g => new object?[] { g.Key, g.Sum(e => e.Points), g.Where(e => e.Points > 0).Sum(e => e.Points), g.Where(e => e.Points < 0).Sum(e => e.Points), g.Count() })
            .OrderByDescending(r => (int)r[1]!).ToList();
        return (["Desarrollador", "Total pts", "Premios", "Penalizaciones", "Entradas"], rows);
    }

    // Estimado (EstimateHours) vs. real (horas dedicadas según el cronómetro EN EL PERÍODO) por requerimiento.
    private (string[], List<object?[]>) RepEstimateVsActual(ReportParams p)
    {
        var now = DateTime.UtcNow;
        var from = p.From; var to = p.To.AddDays(1);
        // Horas reales = sesiones cuyo inicio cae en el rango (respeta Desde/Hasta de la UI).
        var realByReq = _db.WorkSessions
            .Where(w => w.StartedAt >= from && w.StartedAt < to)
            .AsNoTracking().AsEnumerable()
            .GroupBy(w => w.RequirementId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(now)) / 3600.0);

        var reqs = _db.Requirements.Include(r => r.Assignments).AsNoTracking()
            .Where(r => r.Status != RequirementStatus.Cancelado)
            .ToList()
            .Where(r => p.DevIds.Count == 0 || r.Assignments.Any(a => p.DevIds.Contains(a.DeveloperId)))
            .ToList();

        var rows = new List<object?[]>();
        foreach (var r in reqs)
        {
            double est = (double)(r.EstimateHours ?? 0);
            double real = realByReq.GetValueOrDefault(r.Id, 0);
            if (est <= 0 && real <= 0) continue;                 // sin estimación ni tiempo real: se omite
            double dev = real - est;
            string pct = est > 0 ? $"{dev / est * 100:+0;-0;0}%" : "—";
            rows.Add([r.Id, r.Title, Math.Round(est, 2), Math.Round(real, 2), Math.Round(dev, 2), pct]);
        }
        rows = rows.OrderByDescending(x => (double)x[4]!).ToList();  // mayor desviación primero
        return (["ID", "Requerimiento", "Estimado (h)", "Real (h)", "Desviación (h)", "Desv %"], rows);
    }

    // Estimado vs. real agregado por desarrollador (real acotado al período; estimado prorrateado entre asignados).
    private (string[], List<object?[]>) RepEstimateVsActualByDev(ReportParams p)
    {
        var now = DateTime.UtcNow;
        var from = p.From; var to = p.To.AddDays(1);

        // Real por dev = sesiones del período sobre requerimientos NO cancelados.
        var realByDev = _db.WorkSessions
            .Where(w => w.StartedAt >= from && w.StartedAt < to && w.Requirement.Status != RequirementStatus.Cancelado)
            .AsNoTracking().AsEnumerable()
            .GroupBy(w => w.DeveloperId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(now)) / 3600.0);

        // Estimación prorrateada: el EstimateHours de un requerimiento se reparte entre sus devs asignados
        // (así la suma por dev equivale al estimado total del requerimiento, no lo duplica).
        var assigns = _db.Assignments
            .Where(a => a.Requirement.Status != RequirementStatus.Cancelado)
            .Select(a => new { a.DeveloperId, a.RequirementId, Est = a.Requirement.EstimateHours })
            .Distinct().ToList();
        var devsPerReq = assigns.GroupBy(a => a.RequirementId).ToDictionary(g => g.Key, g => g.Count());
        var estByDev = assigns.GroupBy(a => a.DeveloperId)
            .ToDictionary(g => g.Key, g => g.Sum(a => (double)(a.Est ?? 0) / Math.Max(1, devsPerReq.GetValueOrDefault(a.RequirementId, 1))));

        var devs = _db.Developers.Where(d => d.IsActive)
            .Where(d => p.DevIds.Count == 0 || p.DevIds.Contains(d.Id))
            .OrderBy(d => d.FullName).AsNoTracking().ToList();

        var rows = new List<object?[]>();
        foreach (var d in devs)
        {
            double est = estByDev.GetValueOrDefault(d.Id, 0);
            double real = realByDev.GetValueOrDefault(d.Id, 0);
            if (est <= 0 && real <= 0) continue;
            double dev = real - est;
            string pct = est > 0 ? $"{dev / est * 100:+0;-0;0}%" : "—";
            rows.Add([d.FullName, Math.Round(est, 2), Math.Round(real, 2), Math.Round(dev, 2), pct]);
        }
        rows = rows.OrderByDescending(x => (double)x[2]!).ToList();  // más horas reales primero
        return (["Desarrollador", "Estimado (h)", "Real (h)", "Desviación (h)", "Desv %"], rows);
    }

    private (string[], List<object?[]>) RepVacations(ReportParams p)
    {
        var rows = _db.VacationRequests.Include(v => v.Developer).AsEnumerable()
            .Where(v => v.StartDate.Date >= p.From && v.StartDate.Date <= p.To && (p.DevIds.Count == 0 || p.DevIds.Contains(v.DeveloperId)))
            .OrderBy(v => v.StartDate)
            .Select(v => new object?[] { v.Developer.FullName, v.StartDate.ToString("dd/MM/yyyy"), v.EndDate.ToString("dd/MM/yyyy"), v.TotalDays, VacLabel(v.Status), v.Comment })
            .ToList();
        return (["Desarrollador", "Inicio", "Fin", "Días", "Estado", "Comentario"], rows);
    }

    private (string[], List<object?[]>) RepRotations(ReportParams p)
    {
        var to = p.To.AddDays(1);
        var rows = _db.TeamRotations.AsNoTracking()
            .Where(r => r.RotatedAt >= p.From && r.RotatedAt < to)
            .OrderByDescending(r => r.RotatedAt)
            .AsEnumerable()
            .Where(r => p.DevIds.Count == 0 || p.DevIds.Contains(r.DeveloperId))
            .Select(r => new object?[] { r.RotatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), r.DeveloperName, r.FromTeamName, r.ToTeamName, r.Note })
            .ToList();
        return (["Fecha", "Desarrollador", "De", "A", "Nota"], rows);
    }

    private (string[], List<object?[]>) RepTeamSummary(ReportParams p)
    {
        var teams = _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToList();
        var devs = _db.Developers.Where(d => d.IsActive).AsNoTracking().ToList();
        var onlyTeams = p.DevIds.Count == 0 ? null : devs.Where(d => p.DevIds.Contains(d.Id) && d.TeamId != null).Select(d => d.TeamId!.Value).ToHashSet();
        var reqs = LoadReqs();
        var from = p.From; var to = p.To.AddDays(1);
        var teamPts = _db.TeamPointEntries.AsNoTracking().Where(e => e.Date >= from && e.Date < to).ToList();
        var rows = teams.Where(t => onlyTeams == null || onlyTeams.Contains(t.Id)).Select(t =>
        {
            var memberIds = devs.Where(d => d.TeamId == t.Id).Select(d => d.Id).ToHashSet();
            int activeReqs = reqs.Count(r => !Closed(r) && r.Assignments.Any(a => memberIds.Contains(a.DeveloperId)));
            int own = teamPts.Where(e => e.TeamId == t.Id).Sum(e => e.Points);
            return new object?[] { t.Name, memberIds.Count, activeReqs, own };
        }).ToList();
        return (["Equipo", "Integrantes", "Reqs activos", "Pts propios (período)"], rows);
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static bool Closed(Requirement r) => r.Status is RequirementStatus.Entregado or RequirementStatus.Cancelado;

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar => "Por estimar", RequirementStatus.Estimado => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo", RequirementStatus.EnPruebas => "En pruebas",
        RequirementStatus.PorEntregar => "Por entregar", RequirementStatus.Entregado => "Entregado",
        RequirementStatus.Cancelado => "Cancelado", _ => s.ToString()
    };
    private static string PriorityLabel(RequirementPriority pr) => pr switch
    { RequirementPriority.Baja => "Baja", RequirementPriority.Media => "Media", RequirementPriority.Alta => "Alta", RequirementPriority.Critica => "Crítica", _ => pr.ToString() };
    private static string SourceLabel(RequirementSource s) => s switch { RequirementSource.AzureDevOps => "DevOps", RequirementSource.Email => "Correo", _ => "Manual" };
    private static string VacLabel(VacationStatus s) => s switch { VacationStatus.Aprobada => "Aprobada", VacationStatus.Rechazada => "Rechazada", VacationStatus.Cancelada => "Cancelada", _ => "Pendiente" };

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) { LoadDevs(); Generate(); } }
}
