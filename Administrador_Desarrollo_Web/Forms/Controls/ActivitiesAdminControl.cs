using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Vista del administrador sobre las actividades libres del equipo: el trabajo que los
/// desarrolladores registran fuera de sus requerimientos asignados. Sirve para responder
/// "¿en qué se fue el tiempo que no aparece en ningún requerimiento?".
/// </summary>
public class ActivitiesAdminControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly DevActivityService _activities;
    private readonly WorkSessionService _work;
    private readonly ReportService _report;

    private ComboBox _cbxDev = null!, _cbxStatus = null!;
    private DataGridView _grid = null!;
    private Label _kpiTotal = null!, _kpiAbiertas = null!, _kpiTiempo = null!;
    private List<DevActivity> _rows = [];
    private List<Developer> _devs = [];

    public ActivitiesAdminControl(AppDbContext db, DevActivityService activities, WorkSessionService work, ReportService report)
    {
        _db = db; _activities = activities; _work = work; _report = report;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Filtros + acciones ───────────────────────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410f));   // ancho de los botones + sus márgenes
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filtros = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        filtros.Controls.Add(new Label { Text = "Desarrollador:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _cbxDev = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        _cbxDev.SelectedIndexChanged += (_, _) => LoadData();
        filtros.Controls.Add(_cbxDev);

        filtros.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _cbxStatus = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 0, 0) };
        _cbxStatus.Items.AddRange(["Todas", "Abiertas", "Cerradas"]);
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => LoadData();
        filtros.Controls.Add(_cbxStatus);
        toolbar.Controls.Add(filtros, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        var btnSesiones = AppTheme.MakeSecondaryButton("🕑 Ver sesiones", 150); btnSesiones.Margin = new Padding(4, 2, 0, 0); btnSesiones.Click += BtnSesiones_Click;
        var btnExport   = AppTheme.MakeSecondaryButton("📊 Exportar", 120);     btnExport.Margin = new Padding(4, 2, 0, 0); btnExport.Click += BtnExport_Click;
        var btnReload   = AppTheme.MakeSecondaryButton("🔄 Recargar", 110);     btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadData();
        btns.Controls.AddRange([btnSesiones, btnExport, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        // ── KPIs ─────────────────────────────────────────────────
        var kpis = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        kpis.Controls.Add(MakeKpi("🧩 Actividades", out _kpiTotal, AppTheme.SidebarActive));
        kpis.Controls.Add(MakeKpi("🟢 Abiertas", out _kpiAbiertas, AppTheme.Success));
        kpis.Controls.Add(MakeKpi("⏱ Tiempo fuera de asignaciones", out _kpiTiempo, AppTheme.HeaderBg));

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",            Name = "Id",      Width = 45, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev",     FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actividad",     Name = "Title",   FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",        Name = "State",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Creada",        Name = "Created", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cerrada",       Name = "Closed",  FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "⏱ Tiempo",      Name = "Time",    FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción",   Name = "Desc",    FillWeight = 22 });
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnSesiones_Click(null, EventArgs.Empty); };

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(kpis,    0, 1);
        tbl.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(tbl);
    }

    private static Panel MakeKpi(string title, out Label value, Color accent)
    {
        var card = new Panel { Width = 250, Height = 80, Margin = new Padding(0, 4, 10, 4), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accent });
        var content = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(10, 6, 6, 6) };
        content.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = title, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft });
        value = new Label { Dock = DockStyle.Fill, Text = "—", Font = AppTheme.KpiValueFont, ForeColor = accent, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        content.Controls.Add(value);
        card.Controls.Add(content);
        return card;
    }

    private void LoadData()
    {
        // El combo se puebla una sola vez; recargarlo en cada filtro reiniciaría la selección.
        if (_cbxDev.Items.Count == 0)
        {
            _devs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
            _cbxDev.Items.Add("Todos");
            foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
            _cbxDev.SelectedIndex = 0;
        }

        int? devId = _cbxDev.SelectedIndex > 0 ? _devs[_cbxDev.SelectedIndex - 1].Id : null;
        DevActivityStatus? estado = _cbxStatus.SelectedIndex switch
        {
            1 => DevActivityStatus.Abierta,
            2 => DevActivityStatus.Cerrada,
            _ => null
        };

        _rows = _activities.TodasParaAdministrador(devId, estado);

        _grid.Rows.Clear();
        int totalSegundos = 0;
        foreach (var a in _rows)
        {
            int seg = _work.GetTotalSecondsByActivity(a.Id);
            totalSegundos += seg;
            int i = _grid.Rows.Add(a.Id, a.Developer?.FullName ?? "—", a.Title,
                a.Status == DevActivityStatus.Abierta ? "🟢 Abierta" : "⚪ Cerrada",
                a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                a.ClosedAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "—",
                WorkSessionService.Format(seg),
                a.Description ?? "");
            _grid.Rows[i].Cells["State"].Style.ForeColor = a.Status == DevActivityStatus.Abierta ? AppTheme.Success : AppTheme.TextSecondary;
            _grid.Rows[i].Cells["State"].Style.Font = AppTheme.BoldFont;
        }
        if (_rows.Count == 0) _grid.Rows.Add("", "", "(no hay actividades con ese filtro)", "", "", "", "", "");

        _kpiTotal.Text = _rows.Count.ToString();
        _kpiAbiertas.Text = _rows.Count(a => a.Status == DevActivityStatus.Abierta).ToString();
        _kpiTiempo.Text = WorkSessionService.Format(totalSegundos);
    }

    private DevActivity? Seleccionada() =>
        _grid.CurrentRow?.Cells["Id"].Value is int id ? _rows.FirstOrDefault(a => a.Id == id) : null;

    private void BtnSesiones_Click(object? s, EventArgs e)
    {
        if (Seleccionada() is not { } a)
        {
            MessageBox.Show("Selecciona una actividad.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sesiones = _activities.SesionesDe(a.Id);
        if (sesiones.Count == 0)
        {
            MessageBox.Show($"«{a.Title}» todavía no tiene tiempo registrado.", "Sesiones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var ahora = DateTime.UtcNow;
        var texto = string.Join(Environment.NewLine, sesiones.Select(w =>
            $"{w.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm}  →  " +
            $"{(w.EndedAt?.ToLocalTime().ToString("HH:mm") ?? "en curso"),-9}  " +
            $"{WorkSessionService.Format(w.LiveSeconds(ahora))}"));

        MessageBox.Show(
            $"{a.Developer?.FullName} — {a.Title}\n\n{texto}\n\n" +
            $"Total: {WorkSessionService.Format(_work.GetTotalSecondsByActivity(a.Id))} en {sesiones.Count} sesión(es).",
            "Sesiones de la actividad", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        if (_rows.Count == 0) { MessageBox.Show("No hay nada que exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var path = _report.PromptSaveDialog("ActividadesLibres");
        if (path == null) return;

        _report.ExportToExcel(_rows,
            ["ID", "Desarrollador", "Actividad", "Estado", "Creada", "Cerrada", "Tiempo", "Segundos", "Descripción"],
            a => [a.Id, a.Developer?.FullName ?? "", a.Title,
                  a.Status == DevActivityStatus.Abierta ? "Abierta" : "Cerrada",
                  a.CreatedAt.ToLocalTime(), a.ClosedAt?.ToLocalTime(),
                  WorkSessionService.Format(_work.GetTotalSecondsByActivity(a.Id)),
                  _work.GetTotalSecondsByActivity(a.Id),
                  a.Description ?? ""],
            "Actividades libres", path);

        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
