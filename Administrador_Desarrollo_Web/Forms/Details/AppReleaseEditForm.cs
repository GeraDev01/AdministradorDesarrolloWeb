using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Edita los DATOS de una versión ya creada: su etiqueta, su carpeta de destino (informativa) y el
/// changelog. No re-sube ni mueve el paquete: solo cambia la ficha de la versión. Para cambiar el
/// contenido del paquete se crea una versión nueva.
/// </summary>
public class AppReleaseEditForm : Form
{
    private readonly AppRelease _release;
    private readonly bool _versionBloqueada;
    private TextBox _txtVersion = null!;
    private ComboBox _cbxCarpeta = null!;
    private RichTextBox _rtChangelog = null!;

    public string Version => _txtVersion.Text.Trim();
    public string Changelog => _rtChangelog.Text.Trim();

    /// <summary>Subcarpeta de destino (etiqueta). Vacío = raíz de versiones.</summary>
    public string? CarpetaDestino =>
        string.IsNullOrWhiteSpace(_cbxCarpeta.Text) ? null : _cbxCarpeta.Text.Trim();

    /// <param name="versionBloqueada">La versión ya se desplegó o está programada: su etiqueta no se
    /// puede cambiar (rompería el historial), solo la carpeta y el changelog.</param>
    public AppReleaseEditForm(AppRelease release, IReadOnlyList<string> carpetas, bool versionBloqueada = false)
    {
        _release = release;
        _versionBloqueada = versionBloqueada;
        BuildUI(carpetas);
    }

    private void BuildUI(IReadOnlyList<string> carpetas)
    {
        Text = $"Editar versión — {_release.Version}"; Size = new Size(560, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = $"  ✏  Editar versión de {_release.AppSystem?.Name ?? "sistema"}", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill };
        int y = 14;

        body.Controls.Add(new Label { Text = "Versión / Etiqueta *  (ej: v1.2.3, 2024-06-01-hotfix)", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtVersion = new TextBox { Location = new Point(25, y), Width = 500, Text = _release.Version, ReadOnly = _versionBloqueada };
        if (_versionBloqueada) _txtVersion.BackColor = AppTheme.ContentBg;
        body.Controls.Add(_txtVersion); y += _versionBloqueada ? 22 : 40;
        if (_versionBloqueada)
        {
            body.Controls.Add(new Label
            {
                Text = "🔒 Esta versión ya se desplegó o está programada: su etiqueta no se puede cambiar para no " +
                       "alterar el historial. Sí puedes editar la carpeta y el changelog.",
                Location = new Point(25, y), AutoSize = false, Size = new Size(500, 32),
                Font = AppTheme.SmallFont, ForeColor = AppTheme.Warning
            });
            y += 36;
        }

        body.Controls.Add(new Label { Text = "Carpeta de destino (etiqueta del entorno)", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxCarpeta = new ComboBox { Location = new Point(25, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (var c in carpetas) _cbxCarpeta.Items.Add(c);
        _cbxCarpeta.Text = _release.TargetFolder ?? "";
        body.Controls.Add(_cbxCarpeta);
        body.Controls.Add(new Label
        {
            Text = "Solo es una etiqueta: no mueve ni re-sube el paquete ya guardado.",
            Location = new Point(25, y + 30), AutoSize = false, Size = new Size(500, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 56;

        body.Controls.Add(new Label { Text = "Changelog / Notas de la versión", Location = new Point(25, y), AutoSize = true }); y += 20;
        _rtChangelog = new RichTextBox { Location = new Point(25, y), Width = 500, Height = 150, BorderStyle = BorderStyle.FixedSingle, Text = _release.Changelog ?? "" };
        body.Controls.Add(_rtChangelog);

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar cambios", 150); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave; CancelButton = btnCancel;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtVersion.Text))
        {
            MessageBox.Show("La versión es obligatoria.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtVersion.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }
}
