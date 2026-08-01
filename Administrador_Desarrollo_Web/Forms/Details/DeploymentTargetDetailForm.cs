using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class DeploymentTargetDetailForm : ResponsiveForm
{
    private TextBox _txtNombre = null!, _txtHost = null!, _txtUser = null!, _txtPass = null!, _txtRemote = null!, _txtUrl = null!;
    private NumericUpDown _nudPort = null!;
    private CheckBox _chkActive = null!;
    private readonly bool _isEdit;

    public DeploymentTarget Result { get; private set; } = new();

    public DeploymentTargetDetailForm(DeploymentTarget? target = null)
    {
        _isEdit = target != null;
        BuildUI();
        if (target != null) Populate(target);
    }

    private void BuildUI()
    {
        Text = _isEdit ? "Editar Servidor" : "Nuevo Servidor"; Size = new Size(500, 540);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🌐  Servidor FTP/FTPS", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = false };
        int y = 12;
        AddRow(body, "Nombre *", ref _txtNombre, ref y, 440);
        AddRow(body, "Host (incluye esquema: ftps:// o ftp://) *", ref _txtHost, ref y, 380);

        body.Controls.Add(new Label { Text = "Puerto:", Location = new Point(415, y - 30), AutoSize = true });
        _nudPort = new NumericUpDown { Location = new Point(415, y - 14), Width = 60, Minimum = 1, Maximum = 65535, Value = 21 };
        body.Controls.Add(_nudPort);

        AddRow(body, "Usuario *", ref _txtUser, ref y, 440);
        body.Controls.Add(new Label { Text = _isEdit ? "Contraseña (en blanco = no cambiar):" : "Contraseña *:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtPass = new TextBox { Location = new Point(25, y), Width = 440, UseSystemPasswordChar = true }; body.Controls.Add(_txtPass); y += 38;
        AddRow(body, "Ruta remota *", ref _txtRemote, ref y, 440);
        AddRow(body, "URL (opcional, para abrir en navegador)", ref _txtUrl, ref y, 440);
        _chkActive = new CheckBox { Text = "Activo", Location = new Point(25, y), Checked = true, AutoSize = true }; body.Controls.Add(_chkActive); y += 30;

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnC = AppTheme.MakeSecondaryButton("Cancelar", 100); btnC.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnS = AppTheme.MakePrimaryButton("Guardar", 100); btnS.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnC, btnS]);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(outer); AcceptButton = btnS;
    }

    private static void AddRow(Panel p, string label, ref TextBox box, ref int y, int w = 440)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(25, y), AutoSize = true }); y += 20;
        box = new TextBox { Location = new Point(25, y), Width = w }; p.Controls.Add(box); y += 38;
    }

    private void Populate(DeploymentTarget t)
    {
        Result = t; _txtNombre.Text = t.Nombre; _txtHost.Text = t.Host; _nudPort.Value = t.Puerto;
        _txtUser.Text = t.Usuario; _txtRemote.Text = t.RutaRemota; _txtUrl.Text = t.URL ?? ""; _chkActive.Checked = t.IsActive;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtNombre.Text) || string.IsNullOrWhiteSpace(_txtHost.Text) ||
            string.IsNullOrWhiteSpace(_txtUser.Text) || string.IsNullOrWhiteSpace(_txtRemote.Text))
        { MessageBox.Show("Nombre, Host, Usuario y Ruta remota son obligatorios.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!_isEdit && string.IsNullOrWhiteSpace(_txtPass.Text))
        { MessageBox.Show("La contraseña es obligatoria para un servidor nuevo.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Nombre = _txtNombre.Text.Trim(); Result.Host = _txtHost.Text.Trim(); Result.Puerto = (int)_nudPort.Value;
        Result.Usuario = _txtUser.Text.Trim(); Result.RutaRemota = _txtRemote.Text.Trim();
        Result.URL = string.IsNullOrWhiteSpace(_txtUrl.Text) ? null : _txtUrl.Text.Trim(); Result.IsActive = _chkActive.Checked;
        if (!string.IsNullOrWhiteSpace(_txtPass.Text))
            Result.Contrasena = SecretProtector.Protect(_txtPass.Text);
        DialogResult = DialogResult.OK; Close();
    }
}
