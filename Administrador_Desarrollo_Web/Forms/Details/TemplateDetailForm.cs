using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Alta y edición de una plantilla de la biblioteca. Trabaja sobre valores sueltos
/// (<see cref="TemplateInput"/>), nunca sobre la entidad rastreada: el AppDbContext es un singleton
/// compartido y editar aquí una entidad viva escribiría en la base antes de pulsar Guardar.
/// </summary>
public class TemplateDetailForm : ResponsiveForm
{
    private ComboBox _cbxTipo = null!;
    private TextBox _txtTitulo = null!, _txtEtiquetas = null!, _txtDescripcion = null!, _txtCuerpo = null!;
    private Label _lblArchivo = null!, _lblMarcadores = null!;
    private Button _btnQuitarArchivo = null!;

    private byte[]? _archivoBytes;
    private string? _archivoNombre;

    private static readonly TemplateKind[] Tipos = Enum.GetValues<TemplateKind>();

    /// <summary>Lo capturado. Solo tiene sentido si el diálogo terminó en OK.</summary>
    public TemplateInput Result { get; private set; } =
        new(TemplateKind.Otro, null, null, null, null);

    public TemplateDetailForm(Template? plantilla = null, TemplateKind? tipoInicial = null)
    {
        BuildUI();
        if (plantilla != null) Poblar(plantilla);
        else if (tipoInicial is { } k) _cbxTipo.SelectedIndex = Array.IndexOf(Tipos, k);
        ActualizarMarcadores();
    }

    private void BuildUI()
    {
        Text = "Plantilla";
        Size = new Size(880, 720);
        MinimumSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));    // encabezado
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 210f));   // datos
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // cuerpo
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));    // botones
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = "  📚  Plantilla", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        // ── Datos ────────────────────────────────────────────────
        var datos = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4,
            Padding = new Padding(14, 10, 14, 4), BackColor = AppTheme.ContentBg
        };
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        datos.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // tipo / etiquetas
        datos.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // título
        datos.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));   // cuándo usarla (varias líneas)
        datos.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // archivo adjunto

        _cbxTipo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 3) };
        foreach (var k in Tipos) _cbxTipo.Items.Add($"{TemplateService.IconoTipo(k)}  {TemplateService.EtiquetaTipo(k)}");
        _cbxTipo.SelectedIndex = 0;

        _txtTitulo    = new TextBox { Dock = DockStyle.Fill, MaxLength = TemplateService.MaxTitulo, Margin = new Padding(0, 3, 12, 3) };
        _txtEtiquetas = new TextBox { Dock = DockStyle.Fill, MaxLength = TemplateService.MaxEtiquetas, Margin = new Padding(0, 3, 12, 3), PlaceholderText = "cierre, cliente, urgente" };

        datos.Controls.Add(Etiqueta("Tipo *"),      0, 0); datos.Controls.Add(_cbxTipo,      1, 0);
        datos.Controls.Add(Etiqueta("Etiquetas"),   2, 0); datos.Controls.Add(_txtEtiquetas, 3, 0);
        datos.Controls.Add(Etiqueta("Título *"),    0, 1); datos.Controls.Add(_txtTitulo,    1, 1);
        datos.SetColumnSpan(_txtTitulo, 3);

        datos.Controls.Add(Etiqueta("Cuándo usarla"), 0, 2);
        _txtDescripcion = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            MaxLength = TemplateService.MaxDescripcion, Margin = new Padding(0, 3, 12, 3)
        };
        datos.Controls.Add(_txtDescripcion, 1, 2);
        datos.SetColumnSpan(_txtDescripcion, 3);

        // Archivo adjunto (el .docx o .xlsx con el que se entrega una estimación).
        var pnlArchivo = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        var btnAdjuntar = AppTheme.MakeSecondaryButton("📎 Adjuntar archivo…", 170, 28);
        btnAdjuntar.Margin = new Padding(0, 0, 8, 0);
        btnAdjuntar.Click += (_, _) => Adjuntar();
        _btnQuitarArchivo = AppTheme.MakeSecondaryButton("Quitar", 80, 28);
        _btnQuitarArchivo.Margin = new Padding(0, 0, 8, 0);
        _btnQuitarArchivo.Click += (_, _) => { _archivoBytes = null; _archivoNombre = null; PintarArchivo(); };
        _lblArchivo = new Label { AutoSize = true, ForeColor = AppTheme.TextSecondary, Margin = new Padding(0, 6, 0, 0) };
        pnlArchivo.Controls.AddRange([btnAdjuntar, _btnQuitarArchivo, _lblArchivo]);

        datos.Controls.Add(Etiqueta("Archivo"), 0, 3);
        datos.Controls.Add(pnlArchivo, 1, 3);
        datos.SetColumnSpan(pnlArchivo, 3);

        // ── Cuerpo ───────────────────────────────────────────────
        var pnlCuerpo = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Padding = new Padding(14, 0, 14, 4), BackColor = AppTheme.ContentBg
        };
        pnlCuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        pnlCuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlCuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        pnlCuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        pnlCuerpo.Controls.Add(new Label
        {
            Text = "Contenido *   —   escribe {{así}} donde haya que rellenar algo al usarla",
            Dock = DockStyle.Fill, AutoSize = false, ForeColor = AppTheme.TextPrimary
        }, 0, 0);

        _txtCuerpo = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = AppTheme.MonoFont, MaxLength = TemplateService.MaxCuerpo,
            BackColor = Color.White
        };
        _txtCuerpo.TextChanged += (_, _) => ActualizarMarcadores();
        pnlCuerpo.Controls.Add(_txtCuerpo, 0, 1);

        _lblMarcadores = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlCuerpo.Controls.Add(_lblMarcadores, 0, 2);

        // ── Botones ──────────────────────────────────────────────
        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnGuardar = AppTheme.MakePrimaryButton("Guardar", 110);
        btnGuardar.Click += Guardar;
        btns.Controls.AddRange([btnCancelar, btnGuardar]);

        tbl.Controls.Add(hdr,       0, 0);
        tbl.Controls.Add(datos,     0, 1);
        tbl.Controls.Add(pnlCuerpo, 0, 2);
        tbl.Controls.Add(btns,      0, 3);
        Controls.Add(tbl);

        CancelButton = btnCancelar;
        // AcceptButton se deja sin asignar a propósito: el contenido es multilínea y Enter debe
        // insertar un salto de línea, no cerrar el diálogo.
        PintarArchivo();
    }

    private static Label Etiqueta(string texto) =>
        new() { Text = texto, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };

    private void Poblar(Template t)
    {
        Text = $"Plantilla #{t.Id}";
        _cbxTipo.SelectedIndex = Math.Max(0, Array.IndexOf(Tipos, t.Kind));
        _txtTitulo.Text = t.Title;
        _txtEtiquetas.Text = t.Tags ?? "";
        _txtDescripcion.Text = t.Description ?? "";
        // El cuerpo se guarda con \n; el TextBox de WinForms necesita \r\n para mostrar los saltos.
        _txtCuerpo.Text = t.Body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        _archivoBytes = t.FileBytes;
        _archivoNombre = t.FileName;
        PintarArchivo();
    }

    private void Adjuntar()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Archivo de la plantilla",
            Filter = "Documentos (*.docx;*.xlsx;*.pdf;*.md;*.txt)|*.docx;*.xlsx;*.pdf;*.md;*.txt|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var info = new FileInfo(dlg.FileName);
            if (info.Length > TemplateService.MaxArchivoBytes)
            {
                MessageBox.Show(
                    $"El archivo pesa {info.Length / (1024.0 * 1024.0):N1} MB y el máximo son " +
                    $"{TemplateService.MaxArchivoBytes / (1024 * 1024)} MB.\n\n" +
                    "La plantilla vive en la base de datos del equipo; para algo más grande usa Blob Storage y deja aquí el enlace.",
                    "Archivo demasiado grande", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _archivoBytes = File.ReadAllBytes(dlg.FileName);
            _archivoNombre = info.Name;
            PintarArchivo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo leer el archivo:\n{ex.Message}", "Archivo",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PintarArchivo()
    {
        bool hay = _archivoBytes is { Length: > 0 };
        _btnQuitarArchivo.Enabled = hay;
        _lblArchivo.Text = hay
            ? $"📎 {_archivoNombre}  ({_archivoBytes!.Length / 1024.0:N0} KB)"
            : "Sin archivo. Opcional: el .docx o .xlsx que se entrega ya formateado.";
    }

    private void ActualizarMarcadores()
    {
        var marcadores = TemplateService.Marcadores(_txtCuerpo.Text);
        _lblMarcadores.Text = marcadores.Count == 0
            ? "Sin marcadores. Los integrados {{fecha}}, {{hora}}, {{fechahora}}, {{anio}} y {{usuario}} se rellenan solos."
            : $"Se preguntarán al copiarla ({marcadores.Count}): {string.Join(" · ", marcadores.Take(12))}"
              + (marcadores.Count > 12 ? " …" : "");
    }

    private void Guardar(object? sender, EventArgs e)
    {
        var titulo = _txtTitulo.Text.Trim();
        if (titulo.Length < 3)
        {
            MessageBox.Show("Escribe un título (al menos 3 caracteres).", "Validación",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTitulo.Focus();
            return;
        }
        if (_txtCuerpo.Text.Trim().Length == 0 && _archivoBytes is not { Length: > 0 })
        {
            MessageBox.Show("Escribe el contenido de la plantilla o adjunta un archivo.", "Validación",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtCuerpo.Focus();
            return;
        }

        Result = new TemplateInput(
            Tipos[_cbxTipo.SelectedIndex],
            titulo,
            _txtDescripcion.Text,
            _txtCuerpo.Text,
            _txtEtiquetas.Text,
            _archivoBytes,
            _archivoNombre);

        DialogResult = DialogResult.OK;
        Close();
    }
}
