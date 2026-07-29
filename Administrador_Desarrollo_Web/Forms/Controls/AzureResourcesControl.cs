using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class AzureResourcesControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbxType = null!;
    private ComboBox _cbxStatus = null!;
    private ComboBox _cbxEnv = null!;
    private List<AzureResource> _allResources = [];

    public AzureResourcesControl(AppDbContext db, AuditService audit)
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

        _txtSearch = new TextBox { Width = 160, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 6, 0) };
        _txtSearch.TextChanged += (_, _) => FilterGrid();

        filterFlow.Controls.Add(_txtSearch);

        filterFlow.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxType = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxType.Items.Add("Todos");
        foreach (var t in Enum.GetValues<AzureResourceType>()) _cbxType.Items.Add(AzureResourceDetailForm.TypeLabel(t));
        _cbxType.SelectedIndex = 0;
        _cbxType.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxType);

        filterFlow.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxStatus = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxStatus.Items.Add("Todos");
        foreach (var s in Enum.GetValues<AzureResourceStatus>()) _cbxStatus.Items.Add(AzureResourceDetailForm.StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxStatus);

        filterFlow.Controls.Add(new Label { Text = "Ambiente:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxEnv = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxEnv.Items.Add("Todos");
        foreach (var e in Enum.GetValues<AzureEnvironment>()) _cbxEnv.Items.Add(AzureResourceDetailForm.EnvLabel(e));
        _cbxEnv.SelectedIndex = 0;
        _cbxEnv.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxEnv);

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

        var btnNew = AppTheme.MakePrimaryButton("➕ Nuevo", 100);
        btnNew.Margin = new Padding(4, 2, 0, 0);
        btnNew.Click += BtnNew_Click;

        btnFlow.Controls.AddRange([btnDelete, btnEdit, btnNew]);
        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow,    1, 0);

        // ── Grid ───────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",           Name = "Id",      Width = 40, FillWeight = 3  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre",        Name = "Name",    FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",          Name = "Type",    FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",        Name = "Status",  FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ambiente",      Name = "Env",     FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Resource Group",Name = "RG",      FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Región",        Name = "Region",  FillWeight = 9  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Costo/mes",     Name = "Cost",    FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notas",         Name = "Notes",   FillWeight = 16 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allResources = [.. _db.AzureResources.OrderBy(r => r.Name)];
        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allResources.AsEnumerable();

        var search = _txtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (r.ResourceGroup?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Url?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (_cbxType.SelectedIndex > 0)
            q = q.Where(r => (int)r.ResourceType == _cbxType.SelectedIndex - 1);

        if (_cbxStatus.SelectedIndex > 0)
            q = q.Where(r => (int)r.Status == _cbxStatus.SelectedIndex - 1);

        if (_cbxEnv.SelectedIndex > 0)
            q = q.Where(r => (int)r.Environment == _cbxEnv.SelectedIndex - 1);

        foreach (var r in q)
        {
            int i = _grid.Rows.Add(
                r.Id,
                r.Name,
                AzureResourceDetailForm.TypeLabel(r.ResourceType),
                AzureResourceDetailForm.StatusLabel(r.Status),
                AzureResourceDetailForm.EnvLabel(r.Environment),
                r.ResourceGroup ?? "—",
                r.Region ?? "—",
                r.MonthlyCostEstimate.HasValue ? $"${r.MonthlyCostEstimate.Value:0.00}" : "—",
                r.Notes ?? "");

            _grid.Rows[i].Cells["Status"].Style.ForeColor = StatusColor(r.Status);
            _grid.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
        }
    }

    private AzureResource? SelectedResource()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allResources.FirstOrDefault(r => r.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new AzureResourceDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var res = frm.Result;
        res.CreatedAt = DateTime.UtcNow;
        _db.AzureResources.Add(res);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "AzureResource", res.Id.ToString(), res.Name);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var res = SelectedResource();
        if (res == null) return;

        using var frm = new AzureResourceDetailForm(res);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        _db.AzureResources.Update(res);
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "AzureResource", res.Id.ToString(), res.Name);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var res = SelectedResource();
        if (res == null) return;

        if (MessageBox.Show($"¿Eliminar el recurso «{res.Name}»?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _db.AzureResources.Remove(res);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "AzureResource", res.Id.ToString(), res.Name);
        LoadData();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static Color StatusColor(AzureResourceStatus s) => s switch
    {
        AzureResourceStatus.EnUso    => AppTheme.Success,
        AzureResourceStatus.EnPrueba => AppTheme.Warning,
        AzureResourceStatus.Archivado => AppTheme.Danger,
        _                            => AppTheme.TextSecondary
    };
}
