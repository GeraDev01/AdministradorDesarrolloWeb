using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Relaciona sistemas y proyectos con un equipo: en "Sistemas" marcas los sistemas que
/// pertenecen al equipo; en "Proyectos" gestionas los proyectos del equipo.
/// </summary>
public class TeamAssetsForm : ResponsiveForm
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly int _teamId;
    private readonly string _teamName;

    private CheckedListBox _clbSystems = null!;
    private DataGridView _gridProjects = null!;
    private List<AppSystem> _systems = [];
    private List<Project> _projects = [];
    private bool _suppress;

    public bool Changed { get; private set; }

    public TeamAssetsForm(AppDbContext db, AuditService audit, int teamId, string teamName)
    {
        _db = db; _audit = audit; _teamId = teamId; _teamName = teamName;
        BuildUI();
        LoadSystems();
        LoadProjects();
    }

    private void BuildUI()
    {
        Text = $"Sistemas y proyectos — {_teamName}";
        Size = new Size(640, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = false; MinimumSize = new Size(520, 400);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = $"  🗂  Sistemas y proyectos — {_teamName}", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        // ── Tab Sistemas ─────────────────────────────────────────
        var tabSys = new TabPage("  🖥  Sistemas  ");
        var sysTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(10) };
        sysTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        sysTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        sysTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        sysTbl.Controls.Add(new Label { Text = "Marca los sistemas que pertenecen a este equipo:", Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }, 0, 0);
        _clbSystems = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Font = AppTheme.DefaultFont, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        _clbSystems.ItemCheck += ClbSystems_ItemCheck;
        sysTbl.Controls.Add(_clbSystems, 0, 1);
        tabSys.Controls.Add(sysTbl);

        // ── Tab Proyectos ────────────────────────────────────────
        var tabProj = new TabPage("  📁  Proyectos  ");
        var projTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(10, 8, 10, 10) };
        projTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        projTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        projTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var projBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnNew = AppTheme.MakePrimaryButton("➕ Nuevo proyecto", 155); btnNew.Margin = new Padding(0, 4, 6, 0); btnNew.Click += BtnProjNew_Click;
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Editar", 100); btnEdit.Margin = new Padding(0, 4, 6, 0); btnEdit.Click += BtnProjEdit_Click;
        var btnDel = AppTheme.MakeDangerButton("🗑 Eliminar", 110); btnDel.Margin = new Padding(0, 4, 0, 0); btnDel.Click += BtnProjDel_Click;
        projBtns.Controls.AddRange([btnNew, btnEdit, btnDel]);
        projTbl.Controls.Add(projBtns, 0, 0);
        _gridProjects = AppTheme.MakeGrid();
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Proyecto", Name = "Name", FillWeight = 34 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cliente", Name = "Client", FillWeight = 26 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Status", FillWeight = 18 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc", FillWeight = 22 });
        _gridProjects.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnProjEdit_Click(null, EventArgs.Empty); };
        projTbl.Controls.Add(_gridProjects, 0, 1);
        tabProj.Controls.Add(projTbl);

        tabs.TabPages.AddRange([tabSys, tabProj]);
        outer.Controls.Add(hdr, 0, 0);
        outer.Controls.Add(tabs, 0, 1);
        Controls.Add(outer);
    }

    // ── Sistemas ─────────────────────────────────────────────────
    private void LoadSystems()
    {
        _systems = _db.AppSystems.OrderBy(s => s.Name).ToList();
        _suppress = true;
        _clbSystems.Items.Clear();
        foreach (var s in _systems)
        {
            string suffix = s.TeamId != null && s.TeamId != _teamId ? "  (otro equipo)" : "";
            _clbSystems.Items.Add(s.Name + suffix, s.TeamId == _teamId);
        }
        _suppress = false;
    }

    private void ClbSystems_ItemCheck(object? s, ItemCheckEventArgs e)
    {
        if (_suppress) return;
        var sys = _db.AppSystems.Find(_systems[e.Index].Id);
        if (sys == null) return;
        if (e.NewValue == CheckState.Checked) sys.TeamId = _teamId;
        else if (sys.TeamId == _teamId) sys.TeamId = null;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "AppSystem", sys.Id.ToString(), $"{sys.Name}: {(sys.TeamId == null ? "sin equipo" : _teamName)}");
        Changed = true;
    }

    // ── Proyectos ────────────────────────────────────────────────
    private void LoadProjects()
    {
        _projects = _db.Projects.Where(p => p.TeamId == _teamId).OrderBy(p => p.Name).ToList();
        _gridProjects.Rows.Clear();
        foreach (var p in _projects)
            _gridProjects.Rows.Add(p.Id, p.Name, p.Client ?? "—", StatusLabel(p.Status), p.Description ?? "—");
    }

    private Project? SelectedProject()
    {
        if (_gridProjects.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _projects.FirstOrDefault(p => p.Id == id);
    }

    private void BtnProjNew_Click(object? s, EventArgs e)
    {
        using var frm = new ProjectDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var p = frm.Result; p.TeamId = _teamId; p.CreatedAt = DateTime.UtcNow;
        _db.Projects.Add(p); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Project", p.Id.ToString(), p.Name);
        Changed = true; LoadProjects();
    }

    private void BtnProjEdit_Click(object? s, EventArgs e)
    {
        var p = SelectedProject();
        if (p == null) { MessageBox.Show("Selecciona un proyecto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var tracked = _db.Projects.Find(p.Id);
        if (tracked == null) return;
        using var frm = new ProjectDetailForm(tracked);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Project", tracked.Id.ToString(), tracked.Name);
        Changed = true; LoadProjects();
    }

    private void BtnProjDel_Click(object? s, EventArgs e)
    {
        var p = SelectedProject();
        if (p == null) { MessageBox.Show("Selecciona un proyecto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar el proyecto '{p.Name}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var tracked = _db.Projects.Find(p.Id);
        if (tracked != null) { _db.Projects.Remove(tracked); _db.SaveChanges(); _audit.Record(AuditAction.Delete, "Project", tracked.Id.ToString(), tracked.Name); }
        Changed = true; LoadProjects();
    }

    public static string StatusLabel(ProjectStatus s) => s switch
    { ProjectStatus.Activo => "🟢 Activo", ProjectStatus.EnPausa => "🟡 En pausa", ProjectStatus.Terminado => "✅ Terminado", _ => "🚫 Cancelado" };
}
