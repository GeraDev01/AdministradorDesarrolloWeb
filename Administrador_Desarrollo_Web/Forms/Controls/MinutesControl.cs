using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class MinutesControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ReportService _report;

    private DataGridView _grid = null!;
    private ComboBox _cbxType = null!;
    private DateTimePicker _dtpFrom = null!;
    private DateTimePicker _dtpTo = null!;
    private List<Minute> _all = [];

    public MinutesControl(AppDbContext db, AuditService audit, ReportService report)
    {
        _db = db; _audit = audit; _report = report;
        BuildUI(); LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        filterFlow.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxType = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 12, 0) };
        _cbxType.Items.AddRange(["Todos", "Daily", "Sesión", "Otro"]);
        _cbxType.SelectedIndex = 0;
        _cbxType.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxType);
        filterFlow.Controls.Add(new Label { Text = "Desde:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpFrom = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddMonths(-3), Margin = new Padding(0, 2, 12, 0) };
        filterFlow.Controls.Add(_dtpFrom);
        filterFlow.Controls.Add(new Label { Text = "Hasta:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpTo = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1), Margin = new Padding(0, 2, 0, 0) };
        filterFlow.Controls.Add(_dtpTo);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        var btnNew  = AppTheme.MakePrimaryButton("➕ Nueva", 108); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNew_Click;
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Abrir", 108); btnEdit.Margin = new Padding(4, 2, 0, 0); btnEdit.Click += BtnEdit_Click;
        var btnDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 108); btnDel.Margin = new Padding(4, 2, 0, 0); btnDel.Click += BtnDelete_Click;
        var btnExp  = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnExp.Margin = new Padding(4, 2, 0, 0); btnExp.Click += BtnExport_Click;
        btnFlow.Controls.AddRange([btnNew, btnEdit, btnDel, btnExp]);

        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow, 1, 0);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",    Name = "Id",    Width = 45, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",   Name = "Type",  FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",  Name = "Date",  FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title", FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Items",  Name = "Items", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pendientes", Name = "Pending", FillWeight = 13 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _all = _db.Minutes.Include(m => m.ActionItems).ThenInclude(i => i.ResponsibleDeveloper)
            .OrderByDescending(m => m.Date).ToList();
        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _all.AsEnumerable();
        if (_cbxType.SelectedIndex > 0) q = q.Where(m => (int)m.Type == _cbxType.SelectedIndex - 1);
        var from = _dtpFrom.Value.Date;
        var to   = _dtpTo.Value.Date.AddDays(1);
        q = q.Where(m => m.Date >= from && m.Date < to);
        foreach (var m in q)
        {
            int pending = m.ActionItems.Count(i => !i.IsCompleted);
            _grid.Rows.Add(m.Id, TypeLabel(m.Type), m.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                m.Title, m.ActionItems.Count, pending > 0 ? $"⚠ {pending}" : "—");
            if (pending > 0) _grid.Rows[_grid.Rows.Count - 1].Cells["Pending"].Style.ForeColor = AppTheme.Warning;
        }
    }

    private Minute? SelectedMinute()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _all.FirstOrDefault(m => m.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new MinuteDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var m = frm.Result; m.CreatedAt = DateTime.UtcNow;
        _db.Minutes.Add(m); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Minute", m.Id.ToString(), m.Title);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var m = SelectedMinute();
        if (m == null) { MessageBox.Show("Selecciona una minuta.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new MinuteDetailForm(_db, m);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        // Sync action items: remove deleted, update existing, add new
        var existingIds = m.ActionItems.Select(i => i.Id).ToHashSet();
        var resultIds   = frm.Result.ActionItems.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();
        foreach (var del in _db.MinuteActionItems.Where(i => i.MinuteId == m.Id && !resultIds.Contains(i.Id)).ToList())
            _db.MinuteActionItems.Remove(del);
        foreach (var item in frm.Result.ActionItems)
        {
            if (item.Id == 0) { item.MinuteId = m.Id; _db.MinuteActionItems.Add(item); }
            else
            {
                var ex = _db.MinuteActionItems.Find(item.Id);
                if (ex != null) { ex.Description = item.Description; ex.ResponsibleDeveloperId = item.ResponsibleDeveloperId; ex.DueDate = item.DueDate; ex.IsCompleted = item.IsCompleted; }
            }
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Minute", m.Id.ToString(), m.Title);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var m = SelectedMinute();
        if (m == null) { MessageBox.Show("Selecciona una minuta.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar la minuta '{m.Title}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _db.Minutes.Remove(m); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "Minute", m.Id.ToString(), m.Title);
        LoadData();
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Minutas");
        if (path == null) return;
        _report.ExportToExcel(_all,
            ["ID", "Tipo", "Fecha", "Título", "Contenido", "# Items", "Pendientes"],
            m => [m.Id, TypeLabel(m.Type), m.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                  m.Title, m.Content, m.ActionItems.Count, m.ActionItems.Count(i => !i.IsCompleted)],
            "Minutas", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
    private static string TypeLabel(MinuteType t) => t switch { MinuteType.Daily => "Daily", MinuteType.Sesion => "Sesión", _ => "Otro" };
}
