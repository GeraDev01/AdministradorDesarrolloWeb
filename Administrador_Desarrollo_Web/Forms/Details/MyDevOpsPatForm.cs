using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Captura del PAT personal de Azure DevOps. Se guarda solo en este equipo, cifrado con la cuenta
/// de Windows de quien lo captura: nunca viaja a la base compartida ni lo puede leer nadie más.
/// </summary>
public class MyDevOpsPatForm : Form
{
    private readonly AzureDevOpsService _devops;

    private TextBox _txtPat = null!;
    private Label _lblEstado = null!;
    private Button _btnProbar = null!, _btnGuardar = null!;
    private bool _yaTenia;

    public MyDevOpsPatForm(AzureDevOpsService devops)
    {
        _devops = devops;
        BuildUI();
        CargarActual();
    }

    private void BuildUI()
    {
        Text = "Mi PAT de Azure DevOps";
        Size = new Size(620, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = "  🔑  Mi PAT de Azure DevOps", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill };
        int y = 14;

        body.Controls.Add(new Label
        {
            Text = "Los comentarios que la aplicación publique en los tickets quedarán firmados en\n" +
                   "DevOps con TU cuenta, por eso el token es personal y no se comparte.",
            Location = new Point(25, y), AutoSize = false, Size = new Size(550, 36),
            ForeColor = AppTheme.TextPrimary
        });
        y += 44;

        body.Controls.Add(new Label
        {
            Text = "Cómo obtenerlo:  en Azure DevOps → tu foto (arriba a la derecha) → Personal access tokens →\n" +
                   "New Token, con permiso «Work Items → Read & write».",
            Location = new Point(25, y), AutoSize = false, Size = new Size(550, 36),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 46;

        body.Controls.Add(new Label { Text = "Personal Access Token:", Location = new Point(25, y), AutoSize = true });
        y += 22;
        _txtPat = new TextBox { Location = new Point(25, y), Width = 470, UseSystemPasswordChar = true, PlaceholderText = "pega aquí tu token" };
        body.Controls.Add(_txtPat);
        var btnVer = AppTheme.MakeSecondaryButton("👁", 44);
        btnVer.Location = new Point(505, y - 1);
        btnVer.Click += (_, _) => _txtPat.UseSystemPasswordChar = !_txtPat.UseSystemPasswordChar;
        body.Controls.Add(btnVer);
        y += 40;

        body.Controls.Add(new Label
        {
            Text = "Se guarda solo en esta computadora, cifrado con tu cuenta de Windows.\n" +
                   "No se envía a la base de datos ni lo puede leer otra persona.",
            Location = new Point(25, y), AutoSize = false, Size = new Size(550, 34),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 44;

        _btnProbar = AppTheme.MakeSecondaryButton("🔍 Probar conexión", 180);
        _btnProbar.Location = new Point(25, y);
        _btnProbar.Click += BtnProbar_Click;
        body.Controls.Add(_btnProbar);

        var btnBorrar = AppTheme.MakeDangerButton("🗑 Quitar de este equipo", 200);
        btnBorrar.Location = new Point(215, y);
        btnBorrar.Click += BtnBorrar_Click;
        body.Controls.Add(btnBorrar);
        y += 46;

        _lblEstado = new Label
        {
            Location = new Point(25, y), AutoSize = false, Size = new Size(550, 54),
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextSecondary
        };
        body.Controls.Add(_lblEstado);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCerrar = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnGuardar = AppTheme.MakePrimaryButton("Guardar", 120);
        _btnGuardar.Click += BtnGuardar_Click;
        btns.Controls.AddRange([btnCerrar, _btnGuardar]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
    }

    private void CargarActual()
    {
        _yaTenia = LocalDevOpsConfig.Load().TienePat;
        if (_yaTenia)
        {
            // No se muestra el token guardado: se indica que existe y se permite reemplazarlo.
            _txtPat.PlaceholderText = "ya hay un token guardado — escribe uno nuevo para reemplazarlo";
            _lblEstado.ForeColor = AppTheme.Success;
            _lblEstado.Text = "✓  Ya tienes un token guardado en este equipo.";
        }
    }

    private async void BtnProbar_Click(object? s, EventArgs e)
    {
        // Se prueba con lo que esté escrito; si no hay nada, con lo ya guardado.
        var escrito = _txtPat.Text.Trim();
        LocalDevOpsConfig? respaldo = null;
        if (escrito.Length > 0)
        {
            respaldo = LocalDevOpsConfig.Load();
            var temporal = LocalDevOpsConfig.Load();
            temporal.Pat = escrito;
            temporal.Save();
        }

        _btnProbar.Enabled = false;
        _lblEstado.ForeColor = AppTheme.TextSecondary;
        _lblEstado.Text = "Probando...";
        _lblEstado.Refresh();
        try
        {
            var (ok, mensaje) = await _devops.ProbarCredencialesAsync();
            _lblEstado.ForeColor = ok ? AppTheme.Success : AppTheme.Danger;
            _lblEstado.Text = (ok ? "✓  " : "✗  ") + mensaje;

            // Si solo se estaba probando (no guardando), se restaura lo que había.
            if (!ok && respaldo != null) respaldo.Save();
        }
        finally { _btnProbar.Enabled = true; }
    }

    private void BtnGuardar_Click(object? s, EventArgs e)
    {
        var pat = _txtPat.Text.Trim();
        if (pat.Length == 0)
        {
            if (_yaTenia) { DialogResult = DialogResult.OK; Close(); return; }
            MessageBox.Show("Pega tu token antes de guardar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cfg = LocalDevOpsConfig.Load();
        cfg.Pat = pat;
        cfg.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BtnBorrar_Click(object? s, EventArgs e)
    {
        if (MessageBox.Show("¿Quitar tu token de este equipo?", "Confirmar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        LocalDevOpsConfig.Clear();
        _txtPat.Clear();
        _yaTenia = false;
        _txtPat.PlaceholderText = "pega aquí tu token";
        _lblEstado.ForeColor = AppTheme.TextSecondary;
        _lblEstado.Text = "Token eliminado de este equipo.";
    }
}
