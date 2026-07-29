using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class AuditLogControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly ReportService _report;

    private DataGridView _grid = null!;
    private DateTimePicker _dtpFrom = null!;
    private DateTimePicker _dtpTo = null!;
    private ComboBox _cbxUser = null!;
    private ComboBox _cbxAction = null!;
    private TextBox _txtSearch = null!;

    public AuditLogControl(AppDbContext db, ReportService report) { _db = db; _report = report; BuildUI(); LoadData(); }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Filter toolbar
        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg,
            Margin = Padding.Empty, Padding = new Padding(10, 10, 10, 5)
        };

        Lbl(filterFlow, "Desde:");
        _dtpFrom = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-30), Margin = new Padding(2, 2, 12, 0) };
        filterFlow.Controls.Add(_dtpFrom);

        Lbl(filterFlow, "Hasta:");
        _dtpTo = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Value = DateTime.Today, Margin = new Padding(2, 2, 12, 0) };
        filterFlow.Controls.Add(_dtpTo);

        Lbl(filterFlow, "Usuario:");
        _cbxUser = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 12, 0) };
        filterFlow.Controls.Add(_cbxUser);

        Lbl(filterFlow, "Acción:");
        _cbxAction = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 12, 0) };
        _cbxAction.Items.Add("Todas");
        foreach (var a in Enum.GetValues<AuditAction>()) _cbxAction.Items.Add(a.ToString());
        _cbxAction.SelectedIndex = 0;
        filterFlow.Controls.Add(_cbxAction);

        Lbl(filterFlow, "Texto:");
        _txtSearch = new TextBox { Width = 150, PlaceholderText = "En detalles...", Margin = new Padding(2, 2, 12, 0) };
        filterFlow.Controls.Add(_txtSearch);

        var btnSearch = AppTheme.MakePrimaryButton("🔍 Buscar", 100);
        btnSearch.Margin = new Padding(0, 2, 6, 0);
        btnSearch.Click += (_, _) => LoadData();
        var btnExp = AppTheme.MakeSecondaryButton("📊 Excel", 100);
        btnExp.Margin = new Padding(0, 2, 0, 0);
        btnExp.Click += BtnExport_Click;
        filterFlow.Controls.AddRange([btnSearch, btnExp]);

        // Grid
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha/Hora", Name = "Ts",     FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usuario",     Name = "User",   FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Acción",      Name = "Action", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entidad",     Name = "Entity", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",          Name = "EId",    FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Detalles",    Name = "Detail", FillWeight = 37 });

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(filterFlow, 0, 0);
        tbl.Controls.Add(pnlGrid,    0, 1);
        Controls.Add(tbl);
    }

    private static void Lbl(FlowLayoutPanel parent, string text)
        => parent.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = AppTheme.DefaultFont });

    private void LoadData()
    {
        var from = _dtpFrom.Value.Date;
        var to   = _dtpTo.Value.Date.AddDays(1);
        var userName = _cbxUser.SelectedIndex > 0 ? _cbxUser.SelectedItem?.ToString() : null;
        var hasAction = _cbxAction.SelectedIndex > 0;
        var actionVal = hasAction ? (AuditAction)(_cbxAction.SelectedIndex - 1) : default;
        var search = _txtSearch.Text.Trim().ToLower();

        var q = _db.AuditLogs.Where(l => l.Timestamp >= from && l.Timestamp < to).AsEnumerable();
        if (userName != null)       q = q.Where(l => l.UserName == userName);
        if (hasAction)              q = q.Where(l => l.Action == actionVal);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(l => (l.Details?.ToLower().Contains(search) ?? false) || (l.EntityType?.ToLower().Contains(search) ?? false));
        var logs = q.OrderByDescending(l => l.Timestamp).ToList();

        var sel = _cbxUser.SelectedIndex;
        _cbxUser.Items.Clear();
        _cbxUser.Items.Add("Todos");
        foreach (var u in _db.AuditLogs.Select(l => l.UserName).Distinct().OrderBy(u => u).ToList())
            _cbxUser.Items.Add(u);
        _cbxUser.SelectedIndex = (sel >= 0 && sel < _cbxUser.Items.Count) ? sel : 0;

        _grid.Rows.Clear();
        foreach (var l in logs)
            _grid.Rows.Add(l.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                l.UserName, l.Action.ToString(), l.EntityType ?? "—", l.EntityId ?? "—", l.Details ?? "—");
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Bitacora");
        if (path == null) return;
        var rows = new List<object?[]>();
        foreach (DataGridViewRow row in _grid.Rows)
            rows.Add([row.Cells[0].Value, row.Cells[1].Value, row.Cells[2].Value,
                      row.Cells[3].Value, row.Cells[4].Value, row.Cells[5].Value]);
        _report.ExportToExcel(rows, ["Fecha/Hora", "Usuario", "Acción", "Entidad", "ID", "Detalles"], r => r, "Bitácora", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
