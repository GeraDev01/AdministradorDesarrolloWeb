using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Alta y edición de un permiso, en dos modos sobre el mismo formulario:
///
///  · <b>Administrador</b> — elige de quién es el permiso y puede anotar quién lo autorizó. Es la
///    captura de algo ya concedido.
///  · <b>Desarrollador</b> — el permiso es suyo y no elige a nadie más; lo que envía es una
///    SOLICITUD que queda pendiente de respuesta.
///
/// Un solo formulario y no dos porque los campos son los mismos; duplicarlo habría duplicado
/// también la captura del justificante y la validación de fechas.
/// </summary>
public class LeaveRequestDetailForm : ResponsiveForm
{
    private readonly List<Developer> _devs;
    private readonly bool _esAdmin;
    private readonly int? _developerFijo;
    private readonly bool _esEdicion;

    private ComboBox? _cbxDev;
    private ComboBox _cbxType = null!;
    private DateTimePicker _dtpDate = null!;
    private NumericUpDown _nudDays = null!;
    private Label _lblRango = null!;
    private TextBox _txtReason = null!;
    private TextBox? _txtApprovedBy;
    private TextBox _txtNotes = null!;
    private Label _lblFile = null!;
    private Button _btnAbrirAdjunto = null!;

    private byte[]? _attachment;
    private string? _attachmentName;

    public LeaveRequest Result { get; private set; } = new();

    /// <param name="esAdmin">Con true se puede elegir desarrollador y anotar quién autorizó.</param>
    /// <param name="developerFijo">Id del desarrollador cuando el permiso es suyo (modo solicitud).</param>
    public LeaveRequestDetailForm(AppDbContext db, bool esAdmin, int? developerFijo = null, LeaveRequest? leave = null)
    {
        _esAdmin = esAdmin;
        _developerFijo = developerFijo;
        _esEdicion = leave != null;
        _devs = [.. db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName)];

        // Al editar, el titular puede haberse dado de baja: se agrega para no reasignarlo en silencio.
        if (leave != null && _devs.All(d => d.Id != leave.DeveloperId))
        {
            var suyo = db.Developers.FirstOrDefault(d => d.Id == leave.DeveloperId);
            if (suyo != null) _devs.Insert(0, suyo);
        }

        BuildUI();
        if (leave != null) Populate(leave);
    }

    private void BuildUI()
    {
        Text = _esAdmin ? "Registro de permiso" : (_esEdicion ? "Corregir solicitud de permiso" : "Solicitar permiso");
        Size = new Size(560, 720);
        MinimumSize = new Size(520, 560);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = _esAdmin ? "  📋  Registro de permiso" : "  🙋  Solicitud de permiso",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        // AutoScroll: el formulario creció con el justificante y las notas grandes, y a 100 % de
        // escalado entra justo. Con escalado al 125 % no entraría y algo quedaría fuera de vista.
        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        const int x = 25, ancho = 470;
        int y = 14;

        if (!_esAdmin)
        {
            body.Controls.Add(new Label
            {
                Text = "Tu solicitud queda PENDIENTE hasta que el líder la resuelva.",
                Location = new Point(x, y), AutoSize = true, ForeColor = AppTheme.Warning, Font = AppTheme.SmallFont
            });
            y += 26;
        }

        // ── Desarrollador: combo solo para el administrador ────────────────
        if (_esAdmin)
        {
            body.Controls.Add(new Label { Text = "Desarrollador *", Location = new Point(x, y), AutoSize = true }); y += 20;
            _cbxDev = new ComboBox { Location = new Point(x, y), Width = ancho, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
            if (_cbxDev.Items.Count > 0) _cbxDev.SelectedIndex = 0;
            body.Controls.Add(_cbxDev); y += 38;
        }

        // ── Tipo | Días ────────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Tipo de permiso *", Location = new Point(x, y), AutoSize = true });
        body.Controls.Add(new Label { Text = "Días", Location = new Point(x + 330, y), AutoSize = true });
        y += 20;
        _cbxType = new ComboBox { Location = new Point(x, y), Width = 315, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues<LeaveType>()) _cbxType.Items.Add(TypeLabel(t));
        _cbxType.SelectedIndex = 0;
        body.Controls.Add(_cbxType);
        _nudDays = new NumericUpDown { Location = new Point(x + 330, y), Width = 90, Minimum = 1, Maximum = LeaveRequestService.MaxDias, Value = 1 };
        _nudDays.ValueChanged += (_, _) => PintarRango();
        body.Controls.Add(_nudDays); y += 40;

        // ── Fecha ──────────────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Fecha de inicio *", Location = new Point(x, y), AutoSize = true }); y += 20;
        _dtpDate = new DateTimePicker { Location = new Point(x, y), Width = 175, Format = DateTimePickerFormat.Short };
        _dtpDate.ValueChanged += (_, _) => PintarRango();
        body.Controls.Add(_dtpDate);
        // Con «5 días» a partir de una fecha, saber hasta cuándo llega exige contar a mano — y ahí
        // es donde se cuela el error de un día.
        _lblRango = new Label
        {
            Location = new Point(x + 190, y + 4), AutoSize = false, Size = new Size(280, 20),
            ForeColor = AppTheme.SidebarActive, Font = AppTheme.BoldFont
        };
        body.Controls.Add(_lblRango); y += 40;

        // ── Motivo ─────────────────────────────────────────────────────────
        body.Controls.Add(new Label
        {
            Text = _esAdmin ? "Motivo / Descripción" : "Motivo *",
            Location = new Point(x, y), AutoSize = true
        }); y += 20;
        _txtReason = new TextBox
        {
            Location = new Point(x, y), Width = ancho, Height = 76,
            Multiline = true, ScrollBars = ScrollBars.Vertical
        };
        body.Controls.Add(_txtReason); y += 86;

        // ── Notas ──────────────────────────────────────────────────────────
        // Antes eran 44 px de alto: no cabía ni una línea y media, así que la caja se veía como un
        // campo de texto suelto y nadie escribía nada en ella.
        body.Controls.Add(new Label { Text = "Notas adicionales", Location = new Point(x, y), AutoSize = true }); y += 20;
        _txtNotes = new TextBox
        {
            Location = new Point(x, y), Width = ancho, Height = 110,
            Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true
        };
        body.Controls.Add(_txtNotes); y += 120;

        // ── Justificante ───────────────────────────────────────────────────
        body.Controls.Add(new Label
        {
            Text = "Justificante (captura o documento, opcional)", Location = new Point(x, y), AutoSize = true
        }); y += 20;

        var btnAdjuntar = AppTheme.MakeSecondaryButton("📎 Adjuntar", 120); btnAdjuntar.Location = new Point(x, y);
        btnAdjuntar.Click += BtnAdjuntar_Click;
        var btnPegar = AppTheme.MakeSecondaryButton("📋 Pegar captura", 150); btnPegar.Location = new Point(x + 128, y);
        btnPegar.Click += BtnPegar_Click;
        _btnAbrirAdjunto = AppTheme.MakeSecondaryButton("👁 Ver", 80); _btnAbrirAdjunto.Location = new Point(x + 286, y);
        _btnAbrirAdjunto.Click += (_, _) => AbrirAdjunto();
        var btnQuitar = AppTheme.MakeSecondaryButton("✖ Quitar", 90); btnQuitar.Location = new Point(x + 374, y);
        btnQuitar.Click += (_, _) => SetAdjunto(null, null);
        body.Controls.AddRange([btnAdjuntar, btnPegar, _btnAbrirAdjunto, btnQuitar]); y += 36;

        _lblFile = new Label
        {
            Location = new Point(x, y), AutoSize = false, Size = new Size(ancho, 20),
            ForeColor = AppTheme.TextSecondary
        };
        body.Controls.Add(_lblFile); y += 30;

        // ── Autorizado por (solo administrador) ────────────────────────────
        if (_esAdmin)
        {
            body.Controls.Add(new Label { Text = "Autorizado por (opcional)", Location = new Point(x, y), AutoSize = true }); y += 20;
            _txtApprovedBy = new TextBox { Location = new Point(x, y), Width = ancho };
            body.Controls.Add(_txtApprovedBy); y += 36;
        }

        body.AutoScrollMinSize = new Size(0, y + 10);

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), BackColor = AppTheme.ContentBg
        };
        var btnSave = AppTheme.MakePrimaryButton(_esAdmin ? "Guardar" : (_esEdicion ? "Guardar cambios" : "Enviar solicitud"), 160);
        btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        pnlBtns.Controls.AddRange([btnSave, btnCancel]);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(pnlBtns, 0, 2);
        Controls.Add(outer);
        // Sin AcceptButton: con el foco en una caja multilínea, Enter debe crear un salto de línea.
        CancelButton = btnCancel;

        SetAdjunto(null, null);
        PintarRango();
    }

    private void PintarRango()
    {
        int dias = (int)_nudDays.Value;
        var fin = _dtpDate.Value.Date.AddDays(Math.Max(1, dias) - 1);
        _lblRango.Text = dias <= 1 ? "(solo ese día)" : $"→ termina el {fin:dd/MM/yyyy}";
    }

    private void Populate(LeaveRequest l)
    {
        Result = l;
        if (_cbxDev != null)
        {
            var idx = _devs.FindIndex(d => d.Id == l.DeveloperId);
            _cbxDev.SelectedIndex = idx >= 0 ? idx : 0;
        }
        _cbxType.SelectedIndex = (int)l.Type;
        _dtpDate.Value = l.Date == default ? DateTime.Today : l.Date;
        _nudDays.Value = Math.Clamp(l.DaysCount, 1, LeaveRequestService.MaxDias);
        _txtReason.Text = l.Reason ?? "";
        _txtNotes.Text = l.Notes ?? "";
        if (_txtApprovedBy != null) _txtApprovedBy.Text = l.ApprovedBy ?? "";
        if (l.AttachmentBytes is { Length: > 0 }) SetAdjunto(l.AttachmentBytes, l.AttachmentFileName);
        PintarRango();
    }

    private void BtnAdjuntar_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Seleccionar justificante",
            Filter = "Documentos e imágenes (*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg)|*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > LeaveRequestService.MaxAdjuntoBytes)
            {
                MessageBox.Show($"El archivo supera {LeaveRequestService.MaxAdjuntoBytes / (1024 * 1024)} MB.",
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetAdjunto(bytes, Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo cargar el archivo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Win+Shift+S y pegar: es el gesto natural para justificar con una captura.</summary>
    private void BtnPegar_Click(object? s, EventArgs e)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show("No hay una imagen en el portapapeles.\n(Usa Win+Shift+S para recortar la pantalla.)",
                "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var img = Clipboard.GetImage();
            if (img == null) return;
            using var bmp = new Bitmap(img);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            SetAdjunto(ms.ToArray(), $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo pegar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SetAdjunto(byte[]? bytes, string? nombre)
    {
        _attachment = bytes;
        _attachmentName = nombre;
        _lblFile.Text = bytes == null ? "— sin justificante —" : $"📎 {nombre}  ({bytes.Length / 1024} KB)";
        _lblFile.ForeColor = bytes == null ? AppTheme.TextSecondary : AppTheme.Success;
        _btnAbrirAdjunto.Enabled = bytes != null;
    }

    private void AbrirAdjunto()
    {
        if (_attachment is not { Length: > 0 }) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_permiso_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, LeaveRequestService.NombreSeguro(_attachmentName));
            File.WriteAllBytes(path, _attachment);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el justificante:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        int developerId;
        if (_cbxDev != null)
        {
            if (_cbxDev.SelectedIndex < 0)
            { MessageBox.Show("Selecciona un desarrollador.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            developerId = _devs[_cbxDev.SelectedIndex].Id;
        }
        else if (_developerFijo is int fijo) developerId = fijo;
        else
        {
            MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Se valida aquí además de en el servicio para no cerrar el formulario y perder lo escrito.
        if (!_esAdmin && string.IsNullOrWhiteSpace(_txtReason.Text))
        {
            MessageBox.Show("Escribe el motivo: es lo que el líder va a leer para decidir.",
                "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtReason.Focus();
            return;
        }

        Result.DeveloperId = developerId;
        Result.Type = (LeaveType)_cbxType.SelectedIndex;
        Result.Date = _dtpDate.Value.Date;
        Result.DaysCount = (int)_nudDays.Value;
        Result.Reason = string.IsNullOrWhiteSpace(_txtReason.Text) ? null : _txtReason.Text.Trim();
        Result.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
        Result.AttachmentBytes = _attachment;
        Result.AttachmentFileName = _attachmentName;
        if (_txtApprovedBy != null)
            Result.ApprovedBy = string.IsNullOrWhiteSpace(_txtApprovedBy.Text) ? null : _txtApprovedBy.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Se mantiene aquí por compatibilidad: varias pantallas ya la llamaban por este nombre.</summary>
    internal static string TypeLabel(LeaveType t) => LeaveRequestService.EtiquetaTipo(t);

    /// <summary>Color del estado. Vive en la UI, no en el servicio, que no debe conocer el tema.</summary>
    internal static Color EstadoColor(LeaveStatus s) => s switch
    {
        LeaveStatus.Pendiente => AppTheme.Warning,
        LeaveStatus.Aprobada  => AppTheme.Success,
        LeaveStatus.Rechazada => AppTheme.Danger,
        _                     => AppTheme.TextSecondary
    };
}
