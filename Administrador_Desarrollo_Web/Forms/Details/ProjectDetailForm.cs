using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de un proyecto.</summary>
public class ProjectDetailForm : Form
{
    private TextBox _txtName = null!, _txtClient = null!, _txtDesc = null!;
    private ComboBox _cbxStatus = null!;

    public Project Result { get; private set; } = new();

    public ProjectDetailForm(Project? project = null)
    {
        BuildUI();
        if (project != null) Populate(project);
    }

    private void BuildUI()
    {
        Text = "Proyecto"; Size = new Size(460, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📁  Proyecto", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        const int x = 24, w = 400; int y = 14;

        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(x, y), AutoSize = true }); y += 22;
        _txtName = new TextBox { Location = new Point(x, y), Width = w }; body.Controls.Add(_txtName); y += 40;

        body.Controls.Add(new Label { Text = "Cliente", Location = new Point(x, y), AutoSize = true }); y += 22;
        _txtClient = new TextBox { Location = new Point(x, y), Width = w }; body.Controls.Add(_txtClient); y += 40;

        body.Controls.Add(new Label { Text = "Estado", Location = new Point(x, y), AutoSize = true }); y += 22;
        _cbxStatus = new ComboBox { Location = new Point(x, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxStatus.Items.AddRange(["Activo", "En pausa", "Terminado", "Cancelado"]);
        _cbxStatus.SelectedIndex = 0; body.Controls.Add(_cbxStatus); y += 40;

        body.Controls.Add(new Label { Text = "Descripción", Location = new Point(x, y), AutoSize = true }); y += 22;
        _txtDesc = new TextBox { Location = new Point(x, y), Width = w, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical }; body.Controls.Add(_txtDesc); y += 82;
        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
    }

    private void Populate(Project p)
    {
        Result = p;
        _txtName.Text = p.Name; _txtClient.Text = p.Client ?? ""; _txtDesc.Text = p.Description ?? "";
        _cbxStatus.SelectedIndex = (int)p.Status;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Name = _txtName.Text.Trim();
        Result.Client = string.IsNullOrWhiteSpace(_txtClient.Text) ? null : _txtClient.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        Result.Status = (ProjectStatus)_cbxStatus.SelectedIndex;
        DialogResult = DialogResult.OK; Close();
    }
}
