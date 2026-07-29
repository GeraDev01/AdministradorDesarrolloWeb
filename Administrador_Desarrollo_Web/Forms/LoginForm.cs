using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms;

public class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly IServiceProvider _sp;

    private TextBox _txtUser = null!;
    private TextBox _txtPass = null!;
    private Label _lblError = null!;
    private Button _btnLogin = null!;

    public LoginForm(AuthService auth, IServiceProvider sp) { _auth = auth; _sp = sp; BuildUI(); }

    private void BuildUI()
    {
        Text = "Administrador de Desarrollo";
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;
        Size = new Size(460, 500);
        MinimumSize = new Size(460, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        // Outer table: banner row + form row
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Banner ─────────────────────────────────────────────
        var pnlBanner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SidebarBg, Margin = Padding.Empty };
        var bannerTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 10, 10, 10),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        bannerTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        bannerTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bannerTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lblIcon = new Label
        {
            Text = "⚙", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 38f), ForeColor = AppTheme.SidebarActive,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var pnlTitles = new Panel { Dock = DockStyle.Fill };
        pnlTitles.Controls.Add(new Label
        {
            Text = "Administrador de Desarrollo",
            Location = new Point(0, 15), AutoSize = false, Width = 330, Height = 36,
            Font = new Font("Segoe UI Semibold", 14f), ForeColor = Color.White
        });
        pnlTitles.Controls.Add(new Label
        {
            Text = "Sistema de gestión de equipo",
            Location = new Point(0, 55), AutoSize = true,
            Font = AppTheme.SmallFont, ForeColor = Color.FromArgb(148, 163, 184)
        });

        bannerTbl.Controls.Add(lblIcon, 0, 0);
        bannerTbl.Controls.Add(pnlTitles, 1, 0);
        pnlBanner.Controls.Add(bannerTbl);

        // ── Form area ─────────────────────────────────────────
        var pnlForm = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(40, 25, 40, 25),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // label usuario
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // textbox usuario
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // spacer
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // label pass
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // textbox pass
        pnlForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // rest (error + button)
        pnlForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        pnlForm.Controls.Add(new Label { Text = "Usuario", Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        _txtUser = new TextBox { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        pnlForm.Controls.Add(_txtUser, 0, 1);
        pnlForm.Controls.Add(new Panel { BackColor = AppTheme.ContentBg }, 0, 2); // spacer
        pnlForm.Controls.Add(new Label { Text = "Contraseña", Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.BottomLeft }, 0, 3);
        _txtPass = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Font = AppTheme.DefaultFont };
        pnlForm.Controls.Add(_txtPass, 0, 4);

        // Error + button in bottom panel
        var pnlBottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        pnlBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        pnlBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        pnlBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _lblError = new Label { Dock = DockStyle.Fill, ForeColor = AppTheme.Danger, Font = AppTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft };
        pnlBottom.Controls.Add(_lblError, 0, 0);

        _btnLogin = new Button
        {
            Text = "Iniciar sesión", Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, BackColor = AppTheme.SidebarActive,
            ForeColor = Color.White, Font = AppTheme.BoldFont,
            Cursor = Cursors.Hand, FlatAppearance = { BorderSize = 0 }
        };
        _btnLogin.Click += BtnLogin_Click;
        pnlBottom.Controls.Add(_btnLogin, 0, 1);

        pnlForm.Controls.Add(pnlBottom, 0, 5);
        outer.Controls.Add(pnlBanner, 0, 0);
        outer.Controls.Add(pnlForm, 0, 1);
        Controls.Add(outer);
        AcceptButton = _btnLogin;
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); _txtUser.Focus(); }

    private void BtnLogin_Click(object? s, EventArgs e)
    {
        _lblError.Text = "";
        _btnLogin.Enabled = false;
        _btnLogin.Text = "Verificando...";

        var (ok, msg, user) = _auth.Login(_txtUser.Text.Trim(), _txtPass.Text);
        _btnLogin.Enabled = true;
        _btnLogin.Text = "Iniciar sesión";

        if (!ok) { _lblError.Text = msg; _txtPass.Clear(); _txtPass.Focus(); return; }

        if (user!.MustChangePassword)
        {
            using var pwdFrm = new ChangePasswordForm(forced: true);
            if (pwdFrm.ShowDialog(this) == DialogResult.OK)
                _auth.ChangePassword(user.Id, pwdFrm.NewPassword);
        }

        var mainForm = (MainForm)_sp.GetService(typeof(MainForm))!;
        mainForm.Show();
        Hide();
        mainForm.FormClosed += (_, _) => Close();
    }
}
