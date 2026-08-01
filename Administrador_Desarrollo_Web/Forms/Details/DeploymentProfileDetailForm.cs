using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class DeploymentProfileDetailForm : ResponsiveForm
{
    private readonly List<DeploymentTarget> _allTargets;
    private TextBox _txtName = null!, _txtDesc = null!;
    private CheckBox _chkOperaciones = null!;
    private CheckedListBox _lstTargets = null!;

    public DeploymentProfile Result { get; private set; } = new();
    public List<int> SelectedTargetIds { get; private set; } = [];

    public DeploymentProfileDetailForm(AppDbContext db, DeploymentProfile? profile = null)
    {
        _allTargets = db.DeploymentTargets.Where(t => t.IsActive).OrderBy(t => t.Nombre).ToList();
        BuildUI();
        if (profile != null) Populate(profile);
    }

    private void BuildUI()
    {
        Text = "Perfil de Despliegue"; Size = new Size(480, 530);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🔧  Perfil de Despliegue", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 12;
        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtName = new TextBox { Location = new Point(25, y), Width = 420 }; body.Controls.Add(_txtName); y += 38;
        body.Controls.Add(new Label { Text = "Descripción", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtDesc = new TextBox { Location = new Point(25, y), Width = 420 }; body.Controls.Add(_txtDesc); y += 38;
        _chkOperaciones = new CheckBox { Text = "Permitir ejecución al rol Operaciones", Location = new Point(25, y), AutoSize = true }; body.Controls.Add(_chkOperaciones); y += 30;
        body.Controls.Add(new Label { Text = "Servidores incluidos en este perfil:", Location = new Point(25, y), Font = AppTheme.BoldFont, AutoSize = true }); y += 24;
        _lstTargets = new CheckedListBox { Location = new Point(25, y), Width = 420, Height = 180, CheckOnClick = true };
        foreach (var t in _allTargets) _lstTargets.Items.Add($"{t.Nombre} ({t.Host})");
        body.Controls.Add(_lstTargets); y += 188;
        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnC = AppTheme.MakeSecondaryButton("Cancelar", 100); btnC.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnS = AppTheme.MakePrimaryButton("Guardar", 100); btnS.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnC, btnS]);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(outer); AcceptButton = btnS;
    }

    private void Populate(DeploymentProfile p)
    {
        Result = p; _txtName.Text = p.Name; _txtDesc.Text = p.Description ?? ""; _chkOperaciones.Checked = p.AllowedForOperaciones;
        var assignedIds = p.ProfileTargets.Select(pt => pt.TargetId).ToHashSet();
        for (int i = 0; i < _allTargets.Count; i++)
            if (assignedIds.Contains(_allTargets[i].Id)) _lstTargets.SetItemChecked(i, true);
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Name = _txtName.Text.Trim(); Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        Result.AllowedForOperaciones = _chkOperaciones.Checked;
        SelectedTargetIds = [];
        for (int i = 0; i < _lstTargets.Items.Count; i++)
            if (_lstTargets.GetItemChecked(i)) SelectedTargetIds.Add(_allTargets[i].Id);
        DialogResult = DialogResult.OK; Close();
    }
}
