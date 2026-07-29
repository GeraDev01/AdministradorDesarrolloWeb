using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class SoftwareControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbxCategory = null!;
    private ComboBox _cbxStatus = null!;
    private List<Software> _allSoftware = [];

    public SoftwareControl(AppDbContext db, AuditService audit)
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

        filterFlow.Controls.Add(new Label { Text = "Categoría:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxCategory = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxCategory.Items.Add("Todas");
        foreach (var c in Enum.GetValues<SoftwareCategory>()) _cbxCategory.Items.Add(SoftwareDetailForm.CategoryLabel(c));
        _cbxCategory.SelectedIndex = 0;
        _cbxCategory.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxCategory);

        filterFlow.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxStatus = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxStatus.Items.Add("Todos");
        foreach (var s in Enum.GetValues<SoftwareStatus>()) _cbxStatus.Items.Add(SoftwareDetailForm.StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxStatus);

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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",          Name = "Id",        Width = 40, FillWeight = 3  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre",       Name = "Name",      FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Categoría",    Name = "Category",  FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "Status",    FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Licencia",     Name = "License",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión",      Name = "Version",   FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fabricante",   Name = "Publisher", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vence",        Name = "Expiry",    FillWeight = 9  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Instalado en", Name = "Where",     FillWeight = 16 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allSoftware = [.. _db.SoftwareItems.OrderBy(s => s.Name)];
        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allSoftware.AsEnumerable();

        var search = _txtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(s =>
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Publisher?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.InstalledOn?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (_cbxCategory.SelectedIndex > 0)
            q = q.Where(s => (int)s.Category == _cbxCategory.SelectedIndex - 1);

        if (_cbxStatus.SelectedIndex > 0)
            q = q.Where(s => (int)s.Status == _cbxStatus.SelectedIndex - 1);

        foreach (var s in q)
        {
            bool expiringSoon = s.LicenseExpiry.HasValue && s.LicenseExpiry.Value < DateTime.Today.AddDays(30);
            bool expired = s.Status == SoftwareStatus.Expirado ||
                           (s.LicenseExpiry.HasValue && s.LicenseExpiry.Value < DateTime.Today);

            int i = _grid.Rows.Add(
                s.Id,
                s.Name,
                SoftwareDetailForm.CategoryLabel(s.Category),
                SoftwareDetailForm.StatusLabel(s.Status),
                SoftwareDetailForm.LicenseLabel(s.LicenseType),
                s.Version ?? "—",
                s.Publisher ?? "—",
                s.LicenseExpiry.HasValue ? s.LicenseExpiry.Value.ToString("dd/MM/yyyy") : "Sin venc.",
                s.InstalledOn ?? "—");

            _grid.Rows[i].Cells["Status"].Style.ForeColor = StatusColor(s.Status);
            _grid.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;

            if (expired)
                _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger;
            else if (expiringSoon)
                _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Warning;
        }
    }

    private Software? SelectedSoftware()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allSoftware.FirstOrDefault(s => s.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new SoftwareDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var sw = frm.Result;
        sw.CreatedAt = DateTime.UtcNow;
        _db.SoftwareItems.Add(sw);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Software", sw.Id.ToString(), sw.Name);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var sw = SelectedSoftware();
        if (sw == null) return;

        using var frm = new SoftwareDetailForm(sw);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        _db.SoftwareItems.Update(sw);
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Software", sw.Id.ToString(), sw.Name);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var sw = SelectedSoftware();
        if (sw == null) return;

        if (MessageBox.Show($"¿Eliminar «{sw.Name}»?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _db.SoftwareItems.Remove(sw);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "Software", sw.Id.ToString(), sw.Name);
        LoadData();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static Color StatusColor(SoftwareStatus s) => s switch
    {
        SoftwareStatus.EnUso        => AppTheme.Success,
        SoftwareStatus.Instalado    => AppTheme.TextSecondary,
        SoftwareStatus.Expirado     => AppTheme.Warning,
        SoftwareStatus.Desinstalado => AppTheme.Danger,
        _                           => AppTheme.TextSecondary
    };
}
