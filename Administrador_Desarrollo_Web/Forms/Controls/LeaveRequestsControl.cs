using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class LeaveRequestsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbxDev = null!;
    private ComboBox _cbxType = null!;
    private List<LeaveRequest> _allLeaves = [];
    private List<Developer> _allDevs = [];

    public LeaveRequestsControl(AppDbContext db, AuditService audit)
    {
        _db = db; _audit = audit;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };

        _txtSearch = new TextBox { Width = 140, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 6, 0) };
        _txtSearch.TextChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_txtSearch);

        filterFlow.Controls.Add(new Label { Text = "Desarrollador:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxDev = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxDev.Items.Add("Todos");
        _cbxDev.SelectedIndex = 0;
        _cbxDev.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxDev);

        filterFlow.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxType = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxType.Items.Add("Todos");
        foreach (var t in Enum.GetValues<LeaveType>()) _cbxType.Items.Add(LeaveRequestDetailForm.TypeLabel(t));
        _cbxType.SelectedIndex = 0;
        _cbxType.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxType);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };

        var btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 110);
        btnDelete.Margin = new Padding(4, 2, 0, 0);
        btnDelete.Click += BtnDelete_Click;

        var btnEdit = AppTheme.MakeSecondaryButton("✏ Editar", 100);
        btnEdit.Margin = new Padding(4, 2, 0, 0);
        btnEdit.Click += BtnEdit_Click;

        var btnNew = AppTheme.MakePrimaryButton("➕ Registrar", 110);
        btnNew.Margin = new Padding(4, 2, 0, 0);
        btnNew.Click += BtnNew_Click;

        btnFlow.Controls.AddRange([btnDelete, btnEdit, btnNew]);
        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow,    1, 0);

        // ── Grid ───────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",            Name = "Id",       Width = 40, FillWeight = 3  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador",  Name = "Dev",      FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",           Name = "Type",     FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",          Name = "Date",     FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",           Name = "Days",     FillWeight = 5  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motivo",         Name = "Reason",   FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Autorizado por", Name = "Approved", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Registrado",     Name = "Created",  FillWeight = 10 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allLeaves = [.. _db.LeaveRequests
            .Include(l => l.Developer)
            .OrderByDescending(l => l.Date)];

        _allDevs = [.. _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName)];

        var prev = _cbxDev.SelectedIndex;
        _cbxDev.Items.Clear();
        _cbxDev.Items.Add("Todos");
        foreach (var d in _allDevs) _cbxDev.Items.Add(d.FullName);
        _cbxDev.SelectedIndex = prev >= 0 && prev < _cbxDev.Items.Count ? prev : 0;

        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allLeaves.AsEnumerable();

        var search = _txtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(l =>
                l.Developer.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (l.Reason?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.ApprovedBy?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (_cbxDev.SelectedIndex > 0)
        {
            var devName = _allDevs[_cbxDev.SelectedIndex - 1].FullName;
            q = q.Where(l => l.Developer.FullName == devName);
        }

        if (_cbxType.SelectedIndex > 0)
            q = q.Where(l => (int)l.Type == _cbxType.SelectedIndex - 1);

        foreach (var l in q)
        {
            _grid.Rows.Add(
                l.Id,
                l.Developer.FullName,
                LeaveRequestDetailForm.TypeLabel(l.Type),
                l.Date.ToString("dd/MM/yyyy"),
                l.DaysCount,
                l.Reason ?? "—",
                l.ApprovedBy ?? "—",
                l.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"));
        }
    }

    private LeaveRequest? SelectedLeave()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allLeaves.FirstOrDefault(l => l.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new LeaveRequestDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var leave = frm.Result;
        leave.CreatedAt = DateTime.UtcNow;
        _db.LeaveRequests.Add(leave);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "LeaveRequest", leave.Id.ToString(),
            $"{leave.Developer?.FullName ?? leave.DeveloperId.ToString()} — {LeaveRequestDetailForm.TypeLabel(leave.Type)}");
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var leave = SelectedLeave();
        if (leave == null) return;

        using var frm = new LeaveRequestDetailForm(_db, leave);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        _db.LeaveRequests.Update(leave);
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "LeaveRequest", leave.Id.ToString(), leave.Developer?.FullName ?? "");
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var leave = SelectedLeave();
        if (leave == null) return;

        if (MessageBox.Show($"¿Eliminar este registro de permiso?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _db.LeaveRequests.Remove(leave);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "LeaveRequest", leave.Id.ToString(), leave.Developer?.FullName ?? "");
        LoadData();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
