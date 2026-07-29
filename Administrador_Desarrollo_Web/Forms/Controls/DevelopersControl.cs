using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class DevelopersControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ReportService _report;
    private readonly EmailService _email;
    private readonly AuthService _auth;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private CheckBox _chkOnlyActive = null!;
    private List<Developer> _allDevs = [];
    private HashSet<int> _withAccount = [];

    public DevelopersControl(AppDbContext db, AuditService audit, ReportService report, EmailService email, AuthService auth)
    {
        _db = db; _audit = audit; _report = report; _email = email; _auth = auth;
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
            Margin = Padding.Empty, Padding = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ─────────────────────────────────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 690f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };
        _txtSearch = new TextBox { Width = 190, Height = 28, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 8, 0) };
        _txtSearch.TextChanged += (_, _) => FilterGrid();
        _chkOnlyActive = new CheckBox { Text = "Solo activos", Checked = true, AutoSize = true, Font = AppTheme.DefaultFont, Margin = new Padding(0, 5, 0, 0) };
        _chkOnlyActive.CheckedChanged += (_, _) => FilterGrid();
        filterFlow.Controls.AddRange([_txtSearch, _chkOnlyActive]);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };
        var btnNew  = AppTheme.MakePrimaryButton("➕ Nuevo", 108);
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Editar", 108);
        var btnDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 108);
        var btnAcc  = AppTheme.MakeSecondaryButton("🔐 Acceso", 105);
        var btnMail = AppTheme.MakeSecondaryButton("✉ Correo", 105);
        var btnExp  = AppTheme.MakeSecondaryButton("📊 Excel", 95);
        foreach (var b in new[] { btnNew, btnEdit, btnDel, btnAcc, btnMail, btnExp })
            b.Margin = new Padding(4, 2, 0, 0);
        btnNew.Click  += BtnNew_Click;
        btnEdit.Click += BtnEdit_Click;
        btnDel.Click  += BtnDelete_Click;
        btnAcc.Click  += BtnAccess_Click;
        btnMail.Click += BtnMail_Click;
        btnExp.Click  += BtnExport_Click;
        btnFlow.Controls.AddRange([btnNew, btnEdit, btnDel, btnAcc, btnMail, btnExp]);

        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow, 1, 0);

        // ── Grid ────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",         Name = "Id",       Width = 45, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre",     Name = "Name",     FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email",      Name = "Email",    FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Teléfono",   Name = "Phone",    FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Seniority",  Name = "Senior",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "F. Ingreso", Name = "Hire",     FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "N.° Serie",  Name = "Serial",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vacaciones", Name = "Vacation", FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Asignados",  Name = "Assigned", FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Acceso",     Name = "Access",   FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activo",     Name = "Active",   FillWeight = 4  });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(pnlGrid,   0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allDevs = _db.Developers.Include(d => d.Assignments).OrderBy(d => d.FullName).ToList();
        _withAccount = _auth.GetDeveloperIdsWithAccount();
        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allDevs.AsEnumerable();
        if (_chkOnlyActive.Checked) q = q.Where(d => d.IsActive);
        var s = _txtSearch.Text.Trim().ToLower();
        if (!string.IsNullOrEmpty(s))
            q = q.Where(d => d.FullName.ToLower().Contains(s)
                          || (d.Email?.ToLower().Contains(s) ?? false)
                          || (d.Phone?.ToLower().Contains(s) ?? false));
        foreach (var d in q)
        {
            bool hasAccount = _withAccount.Contains(d.Id);
            int i = _grid.Rows.Add(d.Id, d.FullName, d.Email ?? "—", d.Phone ?? "—", d.Seniority ?? "—",
                d.HireDate.HasValue ? d.HireDate.Value.ToString("dd/MM/yyyy") : "—",
                string.IsNullOrWhiteSpace(d.EquipmentSerial) ? "—" : d.EquipmentSerial,
                $"{d.VacationDaysLeft} días",
                d.Assignments.Count, hasAccount ? "🔓 Sí" : "—", d.IsActive ? "✓" : "✗");
            if (!d.IsActive) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            _grid.Rows[i].Cells["Access"].Style.ForeColor = hasAccount ? AppTheme.Success : AppTheme.TextSecondary;
        }
    }

    private Developer? SelectedDev()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allDevs.FirstOrDefault(d => d.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new DeveloperDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var dev = frm.Result; dev.CreatedAt = DateTime.UtcNow;
        _db.Developers.Add(dev); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Developer", dev.Id.ToString(), dev.FullName);
        LoadData();
    }

    private void BtnAccess_Click(object? s, EventArgs e)
    {
        var dev = SelectedDev();
        if (dev == null) { MessageBox.Show("Selecciona un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var existingUser = _auth.GetDeveloperAccountUsername(dev.Id);
        if (existingUser != null)
        {
            var ans = MessageBox.Show(
                $"'{dev.FullName}' ya tiene la cuenta '{existingUser}'.\n\n¿Restablecer su contraseña (genera una nueva temporal)?",
                "Cuenta existente", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;

            var (ok, msg, username, temp) = _auth.ResetDeveloperAccountPassword(dev.Id);
            if (!ok || username == null || temp == null) { MessageBox.Show(msg, "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using var dlgR = new AccountCredentialsForm(dev.FullName, username, temp);
            dlgR.ShowDialog(this);
            LoadData();
            return;
        }

        if (MessageBox.Show($"¿Crear la cuenta de acceso (rol Desarrollador) para '{dev.FullName}'?",
                "Crear acceso", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var (created, message, alreadyExisted, user, tempPass) = _auth.ProvisionDeveloperAccount(dev.Id);
        if (!created || user == null || tempPass == null)
        {
            MessageBox.Show(message, alreadyExisted ? "Cuenta existente" : "No se pudo crear", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new AccountCredentialsForm(dev.FullName, user, tempPass);
        dlg.ShowDialog(this);
        LoadData();
    }

    private void BtnMail_Click(object? s, EventArgs e)
    {
        var dev = SelectedDev();
        if (dev == null || string.IsNullOrWhiteSpace(dev.Email)) { MessageBox.Show("El desarrollador no tiene correo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_email.IsConfigured(out _))
        {
            using var frm = new EmailComposeForm(_email, dev.Email);
            frm.ShowDialog(this);
        }
        else
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"mailto:{dev.Email}") { UseShellExecute = true }); } catch { }
        }
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var dev = SelectedDev();
        if (dev == null) { MessageBox.Show("Selecciona un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new DeveloperDetailForm(dev);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Developer", dev.Id.ToString(), dev.FullName);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var dev = SelectedDev();
        if (dev == null) { MessageBox.Show("Selecciona un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Desactivar a '{dev.FullName}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        dev.IsActive = false; _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "Developer", dev.Id.ToString(), $"Desactivado: {dev.FullName}");
        LoadData();
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Desarrolladores");
        if (path == null) return;
        var devs = _allDevs.Where(d => !_chkOnlyActive.Checked || d.IsActive).ToList();
        _report.ExportToExcel(devs,
            ["ID", "Nombre", "Email", "Teléfono", "Seniority", "F. Ingreso", "Dirección", "N.° Serie equipo", "Vacaciones", "Activo", "Notas", "Alta"],
            d => [d.Id, d.FullName, d.Email, d.Phone, d.Seniority,
                  d.HireDate?.ToString("dd/MM/yyyy"), d.Address, d.EquipmentSerial, d.VacationDaysLeft, d.IsActive, d.Notes, d.CreatedAt],
            "Desarrolladores", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
