using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class ScoringCriterionDetailForm : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtDesc = null!;
    private NumericUpDown _nudPoints = null!;
    private ComboBox _cbxScope = null!;
    private CheckBox _chkActive = null!;

    public ScoringCriterion Result { get; private set; } = new();

    public ScoringCriterionDetailForm(ScoringCriterion? criterion = null)
    {
        BuildUI();
        if (criterion != null) Populate(criterion);
    }

    private void BuildUI()
    {
        Text = "Criterio de Evaluación"; Size = new Size(440, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🏅  Criterio de Evaluación", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;

        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _txtName = new TextBox { Location = new Point(25, y), Width = 380 };
        body.Controls.Add(_txtName); y += 38;

        body.Controls.Add(new Label { Text = "Descripción", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _txtDesc = new TextBox { Location = new Point(25, y), Width = 380, Height = 60, Multiline = true };
        body.Controls.Add(_txtDesc); y += 70;

        body.Controls.Add(new Label { Text = "Puntos (positivo = premio, negativo = penalización):", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _nudPoints = new NumericUpDown { Location = new Point(25, y), Width = 120, Minimum = -999, Maximum = 999, Value = 5 };
        body.Controls.Add(_nudPoints); y += 38;

        body.Controls.Add(new Label { Text = "Aplica a:", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _cbxScope = new ComboBox { Location = new Point(25, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxScope.Items.AddRange(["Individual (desarrollador)", "Equipo", "Ambos"]);
        _cbxScope.SelectedIndex = 0;
        body.Controls.Add(_cbxScope); y += 38;

        _chkActive = new CheckBox { Text = "Criterio activo", Location = new Point(25, y), Checked = true, AutoSize = true };
        body.Controls.Add(_chkActive); y += 32;

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
    }

    private void Populate(ScoringCriterion c)
    {
        Result = c;
        _txtName.Text = c.Name;
        _txtDesc.Text = c.Description ?? "";
        _nudPoints.Value = Math.Clamp(c.DefaultPoints, -999, 999);
        _cbxScope.SelectedIndex = (int)c.Scope;
        _chkActive.Checked = c.IsActive;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Name = _txtName.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        Result.DefaultPoints = (int)_nudPoints.Value;
        Result.Scope = (CriterionScope)_cbxScope.SelectedIndex;
        Result.IsActive = _chkActive.Checked;
        DialogResult = DialogResult.OK; Close();
    }
}
