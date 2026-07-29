using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Gestiona las reglas de auto-asignación de work items de Azure DevOps.</summary>
public class DevOpsRulesForm : Form
{
    private readonly AppDbContext _db;
    private readonly List<Developer> _devs;
    private DataGridView _grid = null!;
    private List<DevOpsAssignmentRule> _rules = [];

    public DevOpsRulesForm(AppDbContext db)
    {
        _db = db;
        _devs = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
        BuildUI(); LoadData();
    }

    private void BuildUI()
    {
        Text = "Reglas de auto-asignación (DevOps)";
        Size = new Size(780, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = false; MinimumSize = new Size(640, 360);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  ⚙  Reglas de auto-asignación", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 6), BackColor = AppTheme.ContentBg };
        var btnNew = AppTheme.MakePrimaryButton("➕ Nueva regla", 140); btnNew.Margin = new Padding(0, 0, 6, 0); btnNew.Click += (_, _) => Edit(null);
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Editar", 100); btnEdit.Margin = new Padding(0, 0, 6, 0); btnEdit.Click += (_, _) => { if (Selected() is { } r) Edit(r); };
        var btnDel = AppTheme.MakeDangerButton("🗑 Eliminar", 110); btnDel.Margin = new Padding(0, 0, 6, 0); btnDel.Click += BtnDel_Click;
        toolbar.Controls.AddRange([btnNew, btnEdit, btnDel]);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Orden", Name = "Order", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Condición", Name = "Match", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Valor", Name = "Value", FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Asignar a", Name = "Dev", FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activa", Name = "Active", FillWeight = 10 });
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0 && Selected() is { } r) Edit(r); };
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(toolbar, 0, 1); outer.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        _rules = _db.DevOpsAssignmentRules.Include(r => r.Developer).OrderBy(r => r.Order).ThenBy(r => r.Id).ToList();
        _grid.Rows.Clear();
        foreach (var r in _rules)
        {
            int i = _grid.Rows.Add(r.Id, r.Order, MatchLabel(r.Match), r.MatchValue, r.Developer?.FullName ?? "—", r.IsActive ? "✓" : "✗");
            if (!r.IsActive) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
        }
    }

    private DevOpsAssignmentRule? Selected() =>
        _grid.CurrentRow?.Cells["Id"].Value is int id ? _rules.FirstOrDefault(r => r.Id == id) : null;

    private void Edit(DevOpsAssignmentRule? rule)
    {
        if (_devs.Count == 0) { MessageBox.Show("Primero registra desarrolladores.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        bool isNew = rule == null;
        var tracked = isNew ? new DevOpsAssignmentRule { CreatedAt = DateTime.UtcNow, DeveloperId = _devs[0].Id } : _db.DevOpsAssignmentRules.Find(rule!.Id)!;
        using var frm = new DevOpsRuleEditForm(_devs, tracked);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        if (isNew) _db.DevOpsAssignmentRules.Add(tracked);
        _db.SaveChanges();
        LoadData();
    }

    private void BtnDel_Click(object? s, EventArgs e)
    {
        var r = Selected();
        if (r == null) { MessageBox.Show("Selecciona una regla.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show("¿Eliminar la regla?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var tracked = _db.DevOpsAssignmentRules.Find(r.Id);
        if (tracked != null) { _db.DevOpsAssignmentRules.Remove(tracked); _db.SaveChanges(); }
        LoadData();
    }

    public static string MatchLabel(DevOpsRuleMatch m) => m switch
    {
        DevOpsRuleMatch.AreaPathContiene => "Área contiene",
        DevOpsRuleMatch.TipoEsIgual => "Tipo es igual a",
        DevOpsRuleMatch.TituloContiene => "Título contiene",
        DevOpsRuleMatch.TagContiene => "Tag contiene",
        DevOpsRuleMatch.AsignadoAContiene => "Asignado-a contiene",
        _ => m.ToString()
    };
}

/// <summary>Edición de una regla de auto-asignación.</summary>
public class DevOpsRuleEditForm : Form
{
    private readonly List<Developer> _devs;
    private readonly DevOpsAssignmentRule _rule;
    private ComboBox _cbxMatch = null!, _cbxDev = null!;
    private TextBox _txtValue = null!;
    private NumericUpDown _nudOrder = null!;
    private CheckBox _chkActive = null!;

    public DevOpsRuleEditForm(List<Developer> devs, DevOpsAssignmentRule rule)
    {
        _devs = devs; _rule = rule;
        BuildUI(); Populate();
    }

    private void BuildUI()
    {
        Text = "Regla de auto-asignación"; Size = new Size(460, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  ⚙  Regla", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 8), Margin = Padding.Empty };
        int y = 4;
        body.Controls.Add(new Label { Text = "Si el work item cumple:", Location = new Point(0, y), AutoSize = true }); y += 22;
        _cbxMatch = new ComboBox { Location = new Point(0, y), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var m in Enum.GetValues<DevOpsRuleMatch>()) _cbxMatch.Items.Add(DevOpsRulesForm.MatchLabel(m));
        _cbxMatch.SelectedIndex = 0; body.Controls.Add(_cbxMatch); y += 40;

        body.Controls.Add(new Label { Text = "Valor (texto a buscar / tipo):", Location = new Point(0, y), AutoSize = true }); y += 22;
        _txtValue = new TextBox { Location = new Point(0, y), Width = 400 }; body.Controls.Add(_txtValue); y += 40;

        body.Controls.Add(new Label { Text = "Asignar a:", Location = new Point(0, y), AutoSize = true }); y += 22;
        _cbxDev = new ComboBox { Location = new Point(0, y), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
        _cbxDev.SelectedIndex = 0; body.Controls.Add(_cbxDev); y += 40;

        body.Controls.Add(new Label { Text = "Orden de evaluación:", Location = new Point(0, y), AutoSize = true }); y += 22;
        _nudOrder = new NumericUpDown { Location = new Point(0, y), Width = 100, Minimum = 0, Maximum = 999 }; body.Controls.Add(_nudOrder); y += 40;

        _chkActive = new CheckBox { Text = "Regla activa", Location = new Point(0, y), Checked = true, AutoSize = true }; body.Controls.Add(_chkActive);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
    }

    private void Populate()
    {
        _cbxMatch.SelectedIndex = (int)_rule.Match;
        _txtValue.Text = _rule.MatchValue;
        var idx = _devs.FindIndex(d => d.Id == _rule.DeveloperId);
        _cbxDev.SelectedIndex = idx >= 0 ? idx : 0;
        _nudOrder.Value = Math.Clamp(_rule.Order, 0, 999);
        _chkActive.Checked = _rule.IsActive;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtValue.Text)) { MessageBox.Show("Indica el valor a buscar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _rule.Match = (DevOpsRuleMatch)_cbxMatch.SelectedIndex;
        _rule.MatchValue = _txtValue.Text.Trim();
        _rule.DeveloperId = _devs[_cbxDev.SelectedIndex].Id;
        _rule.Order = (int)_nudOrder.Value;
        _rule.IsActive = _chkActive.Checked;
        DialogResult = DialogResult.OK; Close();
    }
}
