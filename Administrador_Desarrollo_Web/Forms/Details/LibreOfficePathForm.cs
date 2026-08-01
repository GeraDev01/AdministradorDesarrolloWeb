using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// «¿Dónde está LibreOffice en esta computadora?» — el diálogo con el que cada quien fija SU ruta
/// de soffice.exe, guardada por usuario en %APPDATA% (ver <see cref="LibreOfficeLocalConfig"/>).
///
/// Existe porque la pantalla de Configuración es solo del administrador (contiene secretos) y su
/// campo escribe la ruta COMPARTIDA del equipo: un desarrollador con LibreOffice en una ruta rara
/// no tenía dónde decirlo. Mismo patrón que «Mi PAT» de Azure DevOps: un diálogo pequeño junto a
/// la función que lo necesita.
/// </summary>
public class LibreOfficePathForm : ResponsiveForm
{
    private TextBox _txtRuta = null!;
    private Label _lblEstado = null!;

    /// <summary>True si se guardó una ruta válida (para que quien llamó reintente su PDF).</summary>
    public bool Guardado { get; private set; }

    public LibreOfficePathForm() => BuildUI();

    private void BuildUI()
    {
        Text = "LibreOffice en esta computadora";
        Size = new Size(560, 260);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        Controls.Add(new Label
        {
            Text = "Para generar PDF hace falta LibreOffice. Indica dónde quedó instalado\n" +
                   "en ESTA computadora (normalmente C:\\Program Files\\LibreOffice\\program\\soffice.exe).",
            Location = new Point(20, 14), AutoSize = true, ForeColor = AppTheme.TextSecondary
        });

        Controls.Add(new Label { Text = "Ruta de soffice.exe", Location = new Point(20, 62), AutoSize = true, Font = AppTheme.BoldFont });
        _txtRuta = new TextBox
        {
            Location = new Point(20, 84), Width = 420,
            PlaceholderText = @"C:\Program Files\LibreOffice\program\soffice.exe",
            Text = LibreOfficeLocalConfig.Cargar().SofficePath ?? ""
        };
        Controls.Add(_txtRuta);

        var btnBuscar = AppTheme.MakeSecondaryButton("📂", 44);
        btnBuscar.Location = new Point(448, 82);
        btnBuscar.Click += (_, _) => Buscar();
        Controls.Add(btnBuscar);

        _lblEstado = new Label
        {
            Location = new Point(20, 118), AutoSize = false, Size = new Size(500, 36),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        };
        Controls.Add(_lblEstado);

        var btnProbar = AppTheme.MakeSecondaryButton("🔍 Probar", 110);
        btnProbar.Location = new Point(20, 165);
        btnProbar.Click += (_, _) => Probar(avisarExito: true);

        var btnGuardar = AppTheme.MakePrimaryButton("Guardar", 110);
        btnGuardar.Location = new Point(310, 165);
        btnGuardar.Click += (_, _) => Guardar();

        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancelar.Location = new Point(426, 165);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([btnProbar, btnGuardar, btnCancelar]);
        AcceptButton = btnGuardar; CancelButton = btnCancelar;
    }

    private void Buscar()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "¿Dónde está soffice.exe?",
            // soffice.com también sirve (es el lanzador de consola); cualquier otro .exe no.
            Filter = "LibreOffice (soffice.exe;soffice.com)|soffice.exe;soffice.com|Ejecutables (*.exe;*.com)|*.exe;*.com",
            FileName = "soffice.exe",
            InitialDirectory = @"C:\Program Files"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) { _txtRuta.Text = dlg.FileName; Probar(avisarExito: false); }
    }

    private bool Probar(bool avisarExito)
    {
        var ruta = _txtRuta.Text.Trim();
        if (LibreOfficeLocalConfig.EsSofficeValido(ruta))
        {
            _lblEstado.Text = "✓ El archivo existe y es soffice. Guarda para usarlo en esta computadora.";
            _lblEstado.ForeColor = AppTheme.Success;
            if (avisarExito) MessageBox.Show("La ruta es válida.", "LibreOffice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        _lblEstado.ForeColor = AppTheme.Danger;
        _lblEstado.Text = ruta.Length == 0
            ? "Escribe o busca la ruta de soffice.exe."
            : !File.Exists(ruta)
                ? "✗ Ese archivo no existe en esta computadora."
                : "✗ Ese archivo no es soffice.exe/soffice.com: elegir otro programa no genera PDF.";
        return false;
    }

    private void Guardar()
    {
        if (!Probar(avisarExito: false)) return;

        var cfg = LibreOfficeLocalConfig.Cargar();
        cfg.SofficePath = _txtRuta.Text.Trim();
        try { cfg.Guardar(); }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo guardar la configuración:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Guardado = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
