using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Pide nombre (y si es predeterminada) para una firma importada.</summary>
public class SignaturePromptForm : Form
{
    private TextBox _txtName = null!;
    private CheckBox _chkDefault = null!;

    public string DisplayName => _txtName.Text.Trim();
    public bool IsDefault => _chkDefault.Checked;

    public SignaturePromptForm(string suggestedName, bool defaultChecked)
    {
        BuildUI();
        _txtName.Text = suggestedName;
        _chkDefault.Checked = defaultChecked;
    }

    private void BuildUI()
    {
        Text = "Nombre de la firma";
        Size = new Size(420, 230);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🖊  Nombre de la firma", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 8), Margin = Padding.Empty };
        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(0, 4), AutoSize = true });
        _txtName = new TextBox { Location = new Point(0, 26), Width = 350 };
        body.Controls.Add(_txtName);
        _chkDefault = new CheckBox { Text = "Marcar como firma predeterminada", Location = new Point(0, 66), AutoSize = true };
        body.Controls.Add(_chkDefault);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110);
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            { MessageBox.Show("Ponle un nombre a la firma.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK; Close();
        };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }
}
