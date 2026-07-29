using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Nueva versión de un sistema. El paquete puede venir de dos formas:
///  · <b>Comprimir una carpeta local</b> (publish/build) → se genera el ZIP y se sube al blob.
///  · <b>Tomar un archivo ya existente en Blob Storage</b> → no se comprime ni se re-sube; se
///    registra ese blob como paquete de la versión (lo que pidió el flujo centrado en el blob).
/// </summary>
public class AppReleaseDetailForm : Form
{
    private readonly AppSystem _system;
    private readonly IReadOnlyList<string> _carpetas;
    private readonly BlobStorageService? _blob;
    private readonly List<string> _blobFiles = [];   // nombres completos de blob (paralelo al combo)

    private TextBox _txtVersion = null!;
    private RadioButton _rdoLocal = null!, _rdoBlob = null!;
    private ComboBox _cbxCarpeta = null!;
    private Label _lblLocal = null!, _lblBlob = null!;
    private FlowLayoutPanel _rowLocal = null!;
    private TextBox _txtSourceFolder = null!;
    private ComboBox _cbxBlobFile = null!;
    private RichTextBox _rtChangelog = null!;

    public string Version   => _txtVersion.Text.Trim();
    public string Changelog => _rtChangelog.Text.Trim();
    public string SourceFolder => _txtSourceFolder.Text.Trim();

    /// <summary>true si la versión se toma de un archivo del blob en vez de comprimir una carpeta local.</summary>
    public bool DesdeBlob => _rdoBlob.Checked;

    /// <summary>Subcarpeta de Blob Storage donde se guardará el ZIP (modo local) o de donde sale el paquete.</summary>
    public string? CarpetaDestino =>
        _cbxCarpeta.SelectedIndex <= 0 ? null : _cbxCarpeta.SelectedItem?.ToString();

    /// <summary>Nombre completo del blob elegido como paquete (solo en modo blob).</summary>
    public string? BlobSeleccionado =>
        DesdeBlob && _cbxBlobFile.SelectedIndex >= 0 && _cbxBlobFile.SelectedIndex < _blobFiles.Count
            ? _blobFiles[_cbxBlobFile.SelectedIndex] : null;

    public AppReleaseDetailForm(AppSystem system, IReadOnlyList<string> carpetasDestino, BlobStorageService? blob = null)
    {
        _system = system;
        _carpetas = carpetasDestino;
        _blob = blob is { IsConfigured: true } ? blob : null;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = $"Nueva versión — {_system.Name}"; Size = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = $"  📦  Nueva versión de {_system.Name}", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill };
        int y = 12;

        body.Controls.Add(new Label { Text = "Versión / Etiqueta *  (ej: v1.2.3, 2024-06-01-hotfix)", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtVersion = new TextBox { Location = new Point(25, y), Width = 500 };
        body.Controls.Add(_txtVersion); y += 36;

        // ── Origen del paquete ───────────────────────────────────
        _rdoLocal = new RadioButton { Text = "Comprimir carpeta local", Location = new Point(25, y), AutoSize = true, Checked = true };
        _rdoBlob  = new RadioButton { Text = "Tomar archivo del Blob Storage", Location = new Point(230, y), AutoSize = true, Enabled = _blob != null };
        _rdoLocal.CheckedChanged += (_, _) => AplicarModo();
        _rdoBlob.CheckedChanged  += (_, _) => AplicarModo();
        body.Controls.AddRange([_rdoLocal, _rdoBlob]);
        if (_blob == null)
            body.Controls.Add(new Label { Text = "(configura Blob Storage para tomar el paquete del blob)", Location = new Point(230, y + 20), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        y += 34;

        body.Controls.Add(new Label { Text = "Carpeta en Blob Storage", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxCarpeta = new ComboBox { Location = new Point(25, y), Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxCarpeta.Items.Add("(raíz de versiones)");
        foreach (var c in _carpetas) _cbxCarpeta.Items.Add(c);
        int idxAsociada = string.IsNullOrWhiteSpace(_system.DefaultBlobFolder) ? 0 : _cbxCarpeta.Items.IndexOf(_system.DefaultBlobFolder);
        _cbxCarpeta.SelectedIndex = idxAsociada > 0 ? idxAsociada : 0;
        _cbxCarpeta.SelectedIndexChanged += (_, _) => { if (DesdeBlob) _ = CargarArchivosBlobAsync(); };
        body.Controls.Add(_cbxCarpeta);
        body.Controls.Add(new Label
        {
            Text = "Modo local: destino del ZIP.\nModo blob: carpeta de donde sale el paquete.",
            Location = new Point(315, y - 2), AutoSize = false, Size = new Size(215, 32),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 40;

        // ── Fila LOCAL: carpeta de publish/build ─────────────────
        _lblLocal = new Label { Text = "Carpeta de origen (publish/build) *", Location = new Point(25, y), AutoSize = true };
        body.Controls.Add(_lblLocal);
        _rowLocal = new FlowLayoutPanel { Location = new Point(25, y + 20), Width = 500, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _txtSourceFolder = new TextBox { Width = 458, Text = _system.DefaultSourceFolder ?? "" };
        var btnFolder = new Button { Text = "📁", Width = 34, Height = 28, FlatStyle = FlatStyle.Flat };
        btnFolder.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Carpeta de publish/build", UseDescriptionForTitle = true };
            if (!string.IsNullOrEmpty(_txtSourceFolder.Text) && Directory.Exists(_txtSourceFolder.Text)) dlg.InitialDirectory = _txtSourceFolder.Text;
            if (dlg.ShowDialog() == DialogResult.OK) _txtSourceFolder.Text = dlg.SelectedPath;
        };
        _rowLocal.Controls.AddRange([_txtSourceFolder, btnFolder]);
        body.Controls.Add(_rowLocal);

        // ── Fila BLOB: archivo del paquete (misma posición, se alterna) ──
        _lblBlob = new Label { Text = "Archivo del paquete en el blob *", Location = new Point(25, y), AutoSize = true, Visible = false };
        body.Controls.Add(_lblBlob);
        _cbxBlobFile = new ComboBox { Location = new Point(25, y + 20), Width = 500, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
        body.Controls.Add(_cbxBlobFile);
        y += 60;

        body.Controls.Add(new Label { Text = "Changelog / Notas de la versión", Location = new Point(25, y), AutoSize = true }); y += 20;
        _rtChangelog = new RichTextBox { Location = new Point(25, y), Width = 500, Height = 110, BorderStyle = BorderStyle.FixedSingle };
        body.Controls.Add(_rtChangelog);

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Crear versión", 130); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
    }

    private void AplicarModo()
    {
        bool blob = DesdeBlob;
        _lblLocal.Visible = _rowLocal.Visible = !blob;
        _lblBlob.Visible = _cbxBlobFile.Visible = blob;
        if (blob) _ = CargarArchivosBlobAsync();
    }

    private async Task CargarArchivosBlobAsync()
    {
        if (_blob == null) return;
        _cbxBlobFile.Items.Clear(); _blobFiles.Clear();
        _cbxBlobFile.Items.Add("Cargando…"); _cbxBlobFile.SelectedIndex = 0; _cbxBlobFile.Enabled = false;

        var folder = CarpetaDestino;
        try
        {
            var prefijo = _blob.RutaVersiones(folder) + "/";
            var items = await _blob.ListarDetalladoAsync(prefijo);
            // Solo archivos DIRECTOS de la carpeta (sin descender a subcarpetas) y sin marcadores.
            var directos = items
                .Where(i => !i.Name[prefijo.Length..].Contains('/'))
                .OrderBy(i => i.ShortName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cbxBlobFile.Items.Clear();
            foreach (var it in directos)
            {
                _blobFiles.Add(it.Name);
                _cbxBlobFile.Items.Add($"{it.ShortName}   ({it.SizeLegible})");
            }
            _cbxBlobFile.Enabled = _cbxBlobFile.Items.Count > 0;
            if (_cbxBlobFile.Items.Count == 0) { _cbxBlobFile.Items.Add("(no hay archivos en esta carpeta)"); _cbxBlobFile.SelectedIndex = 0; }
            else _cbxBlobFile.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _cbxBlobFile.Items.Clear();
            _cbxBlobFile.Items.Add($"(error al listar: {ex.Message})");
            _cbxBlobFile.SelectedIndex = 0; _cbxBlobFile.Enabled = false;
        }
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtVersion.Text)) { MessageBox.Show("La versión es obligatoria.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        if (DesdeBlob)
        {
            if (BlobSeleccionado == null) { MessageBox.Show("Selecciona el archivo del blob que será el paquete de esta versión.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_txtSourceFolder.Text) || !Directory.Exists(_txtSourceFolder.Text))
            { MessageBox.Show("Selecciona una carpeta de origen válida.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }

        DialogResult = DialogResult.OK; Close();
    }
}
