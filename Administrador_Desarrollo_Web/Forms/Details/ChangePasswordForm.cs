using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class ChangePasswordForm : Form
{
    private TextBox _txtNew = null!;
    private TextBox _txtConfirm = null!;
    private Label _lblError = null!;

    public string NewPassword { get; private set; } = "";

    public ChangePasswordForm(bool forced = false)
    {
        BuildUI(forced);
    }

    private void BuildUI(bool forced)
    {
        Text = forced ? "Cambio de contraseña requerido" : "Cambiar contraseña";
        Size = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (forced) ControlBox = false;

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        var lblTitle = new Label
        {
            Text = "  🔑  " + Text,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlHeader.Controls.Add(lblTitle);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty };

        if (forced)
        {
            var lblNote = new Label
            {
                Text = "Debes cambiar tu contraseña temporal antes de continuar.",
                ForeColor = AppTheme.Warning,
                Font = AppTheme.BoldFont,
                AutoSize = false,
                Height = 40,
                Width = 360,
                Location = new Point(30, 20)
            };
            pnl.Controls.Add(lblNote);
        }

        int y = forced ? 65 : 20;

        AddField(pnl, "Nueva contraseña:", ref _txtNew, y, password: true);
        y += 55;
        AddField(pnl, "Confirmar contraseña:", ref _txtConfirm, y, password: true);
        y += 55;

        _lblError = new Label
        {
            ForeColor = AppTheme.Danger,
            AutoSize = false,
            Height = 30,
            Width = 360,
            Location = new Point(30, y),
            Font = AppTheme.SmallFont
        };
        pnl.Controls.Add(_lblError);

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            BackColor = AppTheme.ContentBg
        };

        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100);
        btnSave.Click += BtnSave_Click;
        pnlBtns.Controls.Add(btnSave);

        if (!forced)
        {
            var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
            btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlBtns.Controls.Add(btnCancel);
        }

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        outer.Controls.Add(pnlHeader, 0, 0);
        outer.Controls.Add(pnl,       0, 1);
        outer.Controls.Add(pnlBtns,   0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private static void AddField(Panel parent, string label, ref TextBox box, int y, bool password = false)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(30, y), AutoSize = true });
        box = new TextBox
        {
            Location = new Point(30, y + 22),
            Width = 360,
            UseSystemPasswordChar = password
        };
        parent.Controls.Add(box);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        _lblError.Text = "";
        if (_txtNew.Text.Length < 8)
        { _lblError.Text = "La contraseña debe tener al menos 8 caracteres."; return; }
        if (_txtNew.Text != _txtConfirm.Text)
        { _lblError.Text = "Las contraseñas no coinciden."; return; }

        NewPassword = _txtNew.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
