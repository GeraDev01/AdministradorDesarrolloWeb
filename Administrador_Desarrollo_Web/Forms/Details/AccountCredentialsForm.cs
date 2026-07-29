using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Muestra las credenciales recién generadas de una cuenta de acceso para que el
/// jefe se las entregue al desarrollador. La contraseña es temporal: el usuario
/// deberá cambiarla en su primer inicio de sesión.
/// </summary>
public class AccountCredentialsForm : Form
{
    public AccountCredentialsForm(string devName, string username, string tempPassword)
    {
        Text = "Credenciales de acceso";
        Size = new Size(460, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🔐  Cuenta de acceso creada", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        const int x = 24, w = 400; int y = 14;

        body.Controls.Add(new Label { Text = $"Entrega estas credenciales a {devName}.", Location = new Point(x, y), AutoSize = true, ForeColor = AppTheme.TextSecondary }); y += 30;

        body.Controls.Add(new Label { Text = "Usuario", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        var txtUser = new TextBox { Location = new Point(x, y), Width = w, ReadOnly = true, Text = username, Font = AppTheme.MonoFont };
        body.Controls.Add(txtUser); y += 38;

        body.Controls.Add(new Label { Text = "Contraseña temporal", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        var txtPass = new TextBox { Location = new Point(x, y), Width = w, ReadOnly = true, Text = tempPassword, Font = AppTheme.MonoFont };
        body.Controls.Add(txtPass); y += 38;

        body.Controls.Add(new Label
        {
            Text = "⚠ El desarrollador deberá cambiarla al iniciar sesión.\nGuárdala en un lugar seguro; no volverá a mostrarse.",
            Location = new Point(x, y), AutoSize = false, Size = new Size(w, 40), ForeColor = AppTheme.Warning, Font = AppTheme.SmallFont
        });

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnClose = AppTheme.MakePrimaryButton("Cerrar", 100); btnClose.Click += (_, _) => Close();
        var btnCopy = AppTheme.MakeSecondaryButton("📋 Copiar todo", 140);
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText($"Usuario: {username}\r\nContraseña temporal: {tempPassword}"); btnCopy.Text = "✓ Copiado"; } catch { }
        };
        btns.Controls.AddRange([btnClose, btnCopy]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnClose;
    }
}
