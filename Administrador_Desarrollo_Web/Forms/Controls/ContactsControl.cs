using System.Diagnostics;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>Contactos de personas relevantes en la empresa (correo / Teams).</summary>
public class ContactsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ReportService _report;
    private readonly EmailService _email;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private List<Contact> _all = [];

    public ContactsControl(AppDbContext db, AuditService audit, ReportService report, EmailService email)
    {
        _db = db; _audit = audit; _report = report; _email = email;
        BuildUI(); LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 660f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _txtSearch = new TextBox { Width = 220, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 0, 0) };
        _txtSearch.TextChanged += (_, _) => FillGrid();
        left.Controls.Add(_txtSearch);
        toolbar.Controls.Add(left, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnNew   = AppTheme.MakePrimaryButton("➕ Nuevo", 100); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNew_Click;
        var btnEdit  = AppTheme.MakeSecondaryButton("✏ Editar", 100); btnEdit.Margin = new Padding(4, 2, 0, 0); btnEdit.Click += BtnEdit_Click;
        var btnDel   = AppTheme.MakeDangerButton("🗑 Eliminar", 105); btnDel.Margin = new Padding(4, 2, 0, 0); btnDel.Click += BtnDelete_Click;
        var btnMail  = AppTheme.MakeSecondaryButton("✉ Escribir", 105); btnMail.Margin = new Padding(4, 2, 0, 0); btnMail.Click += BtnMail_Click;
        var btnTeams = AppTheme.MakeSecondaryButton("💬 Teams", 95); btnTeams.Margin = new Padding(4, 2, 0, 0); btnTeams.Click += BtnTeams_Click;
        var btnExp   = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnExp.Margin = new Padding(4, 2, 0, 0); btnExp.Click += BtnExport_Click;
        btns.Controls.AddRange([btnNew, btnEdit, btnDel, btnMail, btnTeams, btnExp]);
        toolbar.Controls.Add(btns, 1, 0);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre",  Name = "Name",    FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puesto",  Name = "Title",   FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Empresa/Área", Name = "Company", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Correo",  Name = "Email",   FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Teléfono", Name = "Phone",  FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Teams",   Name = "Teams",   FillWeight = 6  });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _all = _db.Contacts.OrderBy(c => c.Name).ToList();
        FillGrid();
    }

    private void FillGrid()
    {
        _grid.Rows.Clear();
        var s = _txtSearch.Text.Trim().ToLower();
        foreach (var c in _all)
        {
            if (s.Length > 0 && !($"{c.Name} {c.JobTitle} {c.Company} {c.Email}".ToLower().Contains(s))) continue;
            _grid.Rows.Add(c.Id, c.Name, c.JobTitle ?? "—", c.Company ?? "—", c.Email ?? "—", c.Phone ?? "—",
                string.IsNullOrWhiteSpace(c.TeamsLink) ? "" : "💬");
        }
    }

    private Contact? Selected()
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _all.FirstOrDefault(c => c.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new ContactDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var c = frm.Result; c.CreatedAt = DateTime.UtcNow;
        _db.Contacts.Add(c); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Contact", c.Id.ToString(), c.Name);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var c = Selected();
        if (c == null) { MessageBox.Show("Selecciona un contacto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var tracked = _db.Contacts.Find(c.Id);
        if (tracked == null) return;
        using var frm = new ContactDetailForm(tracked);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Contact", tracked.Id.ToString(), tracked.Name);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var c = Selected();
        if (c == null) { MessageBox.Show("Selecciona un contacto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar a '{c.Name}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var tracked = _db.Contacts.Find(c.Id);
        if (tracked != null) { _db.Contacts.Remove(tracked); _db.SaveChanges(); _audit.Record(AuditAction.Delete, "Contact", tracked.Id.ToString(), tracked.Name); }
        LoadData();
    }

    private void BtnMail_Click(object? s, EventArgs e)
    {
        var c = Selected();
        if (c == null || string.IsNullOrWhiteSpace(c.Email)) { MessageBox.Show("El contacto no tiene correo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_email.IsConfigured(out _))
        {
            using var frm = new EmailComposeForm(_email, c.Email);
            frm.ShowDialog(this);
        }
        else
        {
            try { Process.Start(new ProcessStartInfo($"mailto:{c.Email}") { UseShellExecute = true }); } catch { }
        }
    }

    private void BtnTeams_Click(object? s, EventArgs e)
    {
        var c = Selected();
        if (c == null || string.IsNullOrWhiteSpace(c.TeamsLink)) { MessageBox.Show("El contacto no tiene enlace de Teams.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try { Process.Start(new ProcessStartInfo(c.TeamsLink) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir Teams:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Contactos");
        if (path == null) return;
        _report.ExportToExcel(_all, ["Nombre", "Puesto", "Empresa", "Correo", "Teléfono", "Teams"],
            c => [c.Name, c.JobTitle, c.Company, c.Email, c.Phone, c.TeamsLink], "Contactos", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
