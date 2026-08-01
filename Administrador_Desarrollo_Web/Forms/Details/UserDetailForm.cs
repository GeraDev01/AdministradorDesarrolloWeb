using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class UserDetailForm : ResponsiveForm
{
    private TextBox _txtUsername   = null!;
    private TextBox _txtFullName   = null!;
    private TextBox _txtPassword   = null!;
    private TextBox _txtConfirm    = null!;
    private ComboBox _cbxRole      = null!;
    private ComboBox _cbxDeveloper = null!;
    private Label _lblDeveloper    = null!;
    private CheckBox _chkActive    = null!;

    private readonly bool _isEdit;
    private readonly List<Developer> _developers;

    public User Result { get; private set; } = new();
    public string NewPassword { get; private set; } = "";

    public UserDetailForm(AppDbContext db, User? user = null)
    {
        _isEdit     = user != null;
        _developers = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
        BuildUI();
        if (user != null) Populate(user);
    }

    private void BuildUI()
    {
        Text = _isEdit ? "Editar Usuario" : "Nuevo Usuario";
        Size = new Size(460, 560);
        StartPosition  = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        // ── Outer shell: header row + content row ────────────────
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        pnlHeader.Controls.Add(new Label
        {
            Text = "  👤  " + Text, Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // ── Content grid: fields + button row at the bottom ──────
        // Rows: username(2), fullname(2), rol(2), dev(2), active(1), pwd(2), confirm(2), spacer, btnrow
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 18,
            Padding = new Padding(24, 16, 24, 8),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // fixed heights per row  (label=22, control=30, gap=8, chk=26, btn=52)
        int[] rh = { 22, 30, 8,  // Nombre de usuario
                     22, 30, 8,  // Nombre completo
                     22, 30, 8,  // Rol
                     22, 30, 8,  // Desarrollador vinculado (hidden when not dev)
                     26, 8,      // Activo
                     22, 30,     // Contraseña
                     0,          // expand filler
                     52 };       // button row
        foreach (var h in rh)
        {
            var sz = h == 0
                ? new RowStyle(SizeType.Percent, 100f)
                : new RowStyle(SizeType.Absolute, h);
            grid.RowStyles.Add(sz);
        }
        grid.RowCount = rh.Length;

        // Row 0-1: Nombre de usuario
        grid.Controls.Add(Lbl("Nombre de usuario *"), 0, 0);
        _txtUsername = Ctrl<TextBox>(); grid.Controls.Add(_txtUsername, 0, 1);

        // Row 3-4: Nombre completo
        grid.Controls.Add(Lbl("Nombre completo *"), 0, 3);
        _txtFullName = Ctrl<TextBox>(); grid.Controls.Add(_txtFullName, 0, 4);

        // Row 6-7: Rol
        grid.Controls.Add(Lbl("Rol"), 0, 6);
        _cbxRole = Ctrl<ComboBox>();
        _cbxRole.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbxRole.Items.AddRange(["Admin", "Operaciones", "Desarrollador"]);
        _cbxRole.SelectedIndex = 1;
        _cbxRole.SelectedIndexChanged += (_, _) => UpdateDevVisibility();
        grid.Controls.Add(_cbxRole, 0, 7);

        // Row 9-10: Desarrollador vinculado
        _lblDeveloper = Lbl("Desarrollador vinculado *");
        grid.Controls.Add(_lblDeveloper, 0, 9);
        _cbxDeveloper = Ctrl<ComboBox>();
        _cbxDeveloper.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbxDeveloper.Items.Add("(Seleccionar...)");
        foreach (var d in _developers) _cbxDeveloper.Items.Add(d.FullName);
        _cbxDeveloper.SelectedIndex = 0;
        grid.Controls.Add(_cbxDeveloper, 0, 10);

        // Row 12: Activo
        _chkActive = new CheckBox { Text = "Activo", Checked = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        grid.Controls.Add(_chkActive, 0, 12);

        // Row 14-15: Contraseña + confirmación (combined into one label showing both)
        var pwdHint = _isEdit ? "Contraseña (dejar en blanco para no cambiar):" : "Contraseña *";
        grid.Controls.Add(Lbl(pwdHint, AppTheme.SmallFont, AppTheme.TextSecondary), 0, 14);
        _txtPassword = Ctrl<TextBox>(); _txtPassword.UseSystemPasswordChar = true;
        grid.Controls.Add(_txtPassword, 0, 15);

        // Insert two extra rows for confirm (after row 15)
        // We add them after the fact since RowCount can be extended
        // Instead, let's put confirm label+box inline below pwd using a sub-panel
        // Simpler: use the filler row (16) for confirm label+box via a Panel
        var pnlConfirm = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        if (!_isEdit)
        {
            var lblC = new Label
            {
                Text = "Confirmar contraseña *",
                Location = new Point(0, 0), AutoSize = true,
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
            };
            _txtConfirm = new TextBox { Location = new Point(0, 20), Width = 390, UseSystemPasswordChar = true };
            pnlConfirm.Controls.AddRange([lblC, _txtConfirm]);
        }
        else
        {
            _txtConfirm = new TextBox { Visible = false };
            pnlConfirm.Controls.Add(_txtConfirm);
        }
        grid.Controls.Add(pnlConfirm, 0, 16);

        // Row 17: Buttons
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty
        };
        var btnSave   = AppTheme.MakePrimaryButton("Guardar",   100);
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.AddRange([btnSave, btnCancel]);
        grid.Controls.Add(btnRow, 0, 17);

        outer.Controls.Add(pnlHeader, 0, 0);
        outer.Controls.Add(grid,      0, 1);
        Controls.Add(outer);
        AcceptButton = btnSave;

        UpdateDevVisibility();
    }

    private static Label Lbl(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font      = font  ?? AppTheme.DefaultFont,
        ForeColor = color ?? AppTheme.TextPrimary,
        Margin    = new Padding(0)
    };

    private static T Ctrl<T>() where T : Control, new() => new T { Dock = DockStyle.Fill, Margin = new Padding(0) };

    private void UpdateDevVisibility()
    {
        bool isDev = _cbxRole.SelectedIndex == (int)UserRole.Desarrollador;
        _lblDeveloper.Visible  = isDev;
        _cbxDeveloper.Visible  = isDev;
    }

    private void Populate(User u)
    {
        Result = u;
        _txtUsername.Text      = u.Username;
        _txtFullName.Text      = u.FullName;
        _cbxRole.SelectedIndex = (int)u.Role;
        _chkActive.Checked     = u.IsActive;

        if (u.DeveloperId.HasValue)
        {
            var idx = _developers.FindIndex(d => d.Id == u.DeveloperId.Value);
            _cbxDeveloper.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        UpdateDevVisibility();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtUsername.Text) || string.IsNullOrWhiteSpace(_txtFullName.Text))
        { MessageBox.Show("Usuario y nombre son obligatorios.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var role = (UserRole)_cbxRole.SelectedIndex;

        if (role == UserRole.Desarrollador && _cbxDeveloper.SelectedIndex == 0)
        { MessageBox.Show("Selecciona el desarrollador vinculado.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        if (!_isEdit || !string.IsNullOrEmpty(_txtPassword.Text))
        {
            if (_txtPassword.Text.Length < 8)
            { MessageBox.Show("La contraseña debe tener al menos 8 caracteres.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!_isEdit && _txtPassword.Text != _txtConfirm.Text)
            { MessageBox.Show("Las contraseñas no coinciden.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            NewPassword = _txtPassword.Text;
        }

        Result.Username    = _txtUsername.Text.Trim();
        Result.FullName    = _txtFullName.Text.Trim();
        Result.Role        = role;
        Result.IsActive    = _chkActive.Checked;
        Result.DeveloperId = role == UserRole.Desarrollador && _cbxDeveloper.SelectedIndex > 0
            ? _developers[_cbxDeveloper.SelectedIndex - 1].Id
            : null;

        DialogResult = DialogResult.OK;
        Close();
    }
}
