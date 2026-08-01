using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms;

public class LoginForm : Form
{
    private readonly IServiceProvider _sp;
    private readonly DbConnectionState _conexion;
    private readonly ToolTip _tip = new();

    private TextBox _txtUser = null!;
    private TextBox _txtPass = null!;
    private Label _lblError = null!;
    private Button _btnLogin = null!;
    private Label _lblDot = null!;
    private Label _lblConnTitulo = null!;
    private Label _lblConnDetalle = null!;
    private LinkLabel _lnkReintentar = null!;

    /// <summary>
    /// AuthService toca la base en su primer uso, así que se resuelve al pulsar «Iniciar sesión» y no
    /// al construir la ventana: sin conexión con la base del equipo no hay <c>AppDbContext</c> que
    /// inyectar, y esta ventana tiene que poder abrirse igual para poder DECIR que no se pudo conectar.
    /// </summary>
    private AuthService Auth => (AuthService)_sp.GetService(typeof(AuthService))!;

    /// <summary>
    /// El estado de la conexión se toma del estado COMPARTIDO de la aplicación, no de quien
    /// construya la ventana. Esta pantalla se crea también al cerrar sesión, y cuando dependía de
    /// que alguien la configurara desde fuera esa segunda vez se quedaba con el ● en gris.
    /// </summary>
    public LoginForm(IServiceProvider sp, DbConnectionState conexion)
    {
        _sp = sp; _conexion = conexion;
        BuildUI();
        PintarEstado(_conexion.Estado);
    }

    private void BuildUI()
    {
        Text = "Administrador de Desarrollo";
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;
        // Alto suficiente para que el motivo de un fallo de conexión quepa sin recortarse.
        Size = new Size(460, 560);
        MinimumSize = new Size(460, 560);
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

        // ── Indicador de conexión ─────────────────────────────
        // El login responde lo mismo ("Usuario o contraseña incorrectos") cuando el usuario no existe
        // en la base que cuando la contraseña está mal. Este punto es lo único que distingue
        // "escribí mal la contraseña" de "mi aplicación no está hablando con la base del equipo".
        var pnlEstado = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Margin = new Padding(0, 10, 0, 0), Padding = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        pnlEstado.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18f));
        pnlEstado.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlEstado.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));  // punto + título
        pnlEstado.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // motivo
        pnlEstado.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));  // reintentar

        _lblDot = new Label
        {
            Text = "●", Dock = DockStyle.Fill, Margin = Padding.Empty,
            Font = new Font("Segoe UI", 10f), ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblConnTitulo = new Label
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, AutoEllipsis = true,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblConnDetalle = new Label
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft
        };
        _lnkReintentar = new LinkLabel
        {
            Text = "Reintentar la conexión", AutoSize = true, Visible = false,
            Margin = Padding.Empty, Font = AppTheme.SmallFont,
            LinkColor = AppTheme.SidebarActive, ActiveLinkColor = AppTheme.SidebarActive
        };
        _lnkReintentar.LinkClicked += async (_, _) => await ReintentarAsync();

        pnlEstado.Controls.Add(_lblDot, 0, 0);
        pnlEstado.Controls.Add(_lblConnTitulo, 1, 0);
        pnlEstado.Controls.Add(_lblConnDetalle, 1, 1);
        pnlEstado.Controls.Add(_lnkReintentar, 1, 2);
        pnlBottom.Controls.Add(pnlEstado, 0, 2);

        pnlForm.Controls.Add(pnlBottom, 0, 5);
        outer.Controls.Add(pnlBanner, 0, 0);
        outer.Controls.Add(pnlForm, 0, 1);
        Controls.Add(outer);
        AcceptButton = _btnLogin;
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); _txtUser.Focus(); }

    /// <summary>
    /// Pinta el estado de la conexión con la base del equipo. El enlace de reintentar solo aparece
    /// si hay una cadena que probar: con un ejecutable sin conexión incrustada, en un equipo sin
    /// ninguna configurada, reintentar no puede cambiar nada.
    /// </summary>
    private void PintarEstado(DbConnectionStatus estado)
    {
        var color = estado.Conectado ? AppTheme.Success : AppTheme.Danger;
        _lblDot.ForeColor        = color;
        _lblConnTitulo.ForeColor = color;
        _lblConnTitulo.Text      = estado.Titulo;
        _lblConnDetalle.Text     = estado.Detalle;

        var ayuda = string.IsNullOrEmpty(estado.Detalle) ? estado.Titulo : $"{estado.Titulo}\n\n{estado.Detalle}";
        _tip.SetToolTip(_lblDot, ayuda);
        _tip.SetToolTip(_lblConnTitulo, ayuda);

        // Sin la base del equipo no se intenta iniciar sesión: la consulta fallaría con un error
        // técnico, o —lo que costó este cambio— tendría éxito contra una base que no es la del equipo.
        _txtUser.Enabled = _txtPass.Enabled = _btnLogin.Enabled = estado.Conectado;
        _lnkReintentar.Visible = !estado.Conectado && _conexion.SePuedeReintentar;
        if (!estado.Conectado) _lblError.Text = "";
    }

    private async Task ReintentarAsync()
    {
        if (!_conexion.SePuedeReintentar) return;

        _lnkReintentar.Enabled = false;
        _lblDot.ForeColor = AppTheme.Warning;
        _lblConnTitulo.ForeColor = AppTheme.TextSecondary;
        _lblConnTitulo.Text = DbConnectionStatus.Comprobando.Titulo;
        _lblConnDetalle.Text = "";
        UseWaitCursor = true;
        try
        {
            // Reintentar a través del estado compartido: así el resultado queda guardado y la
            // siguiente pantalla de inicio de sesión no vuelve a partir del estado viejo.
            PintarEstado(await _conexion.ReintentarAsync());
        }
        finally
        {
            UseWaitCursor = false;
            _lnkReintentar.Enabled = true;
        }
    }

    private void BtnLogin_Click(object? s, EventArgs e)
    {
        _lblError.Text = "";
        _btnLogin.Enabled = false;
        _btnLogin.Text = "Verificando...";

        var (ok, msg, user) = Auth.Login(_txtUser.Text.Trim(), _txtPass.Text);
        _btnLogin.Enabled = true;
        _btnLogin.Text = "Iniciar sesión";

        if (!ok) { _lblError.Text = msg; _txtPass.Clear(); _txtPass.Focus(); return; }

        if (user!.MustChangePassword)
        {
            using var pwdFrm = new ChangePasswordForm(forced: true);
            if (pwdFrm.ShowDialog(this) == DialogResult.OK)
                Auth.ChangePassword(user.Id, pwdFrm.NewPassword);
        }

        var mainForm = (MainForm)_sp.GetService(typeof(MainForm))!;
        mainForm.Show();
        Hide();
        mainForm.FormClosed += (_, _) => Close();
    }
}
