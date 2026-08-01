using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta de una solicitud de vacaciones por el propio desarrollador, con documento de respaldo opcional.</summary>
public class MyVacationRequestForm : ResponsiveForm
{
    private DateTimePicker _dtpStart = null!;
    private DateTimePicker _dtpEnd = null!;
    private TextBox _txtComment = null!;
    private Label _lblDays = null!;
    private Label _lblFile = null!;

    private byte[]? _attachment;
    private string? _attachmentName;

    public VacationRequest Result { get; } = new();

    public MyVacationRequestForm()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Nueva Solicitud de Vacaciones";
        Size = new Size(480, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🏖  Solicitud de Vacaciones", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        const int x = 24; int y = 16;

        body.Controls.Add(new Label { Text = "Fechas *", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 24;
        var dateRow = new FlowLayoutPanel { Location = new Point(x, y), Size = new Size(420, 30), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        dateRow.Controls.Add(new Label { Text = "Inicio:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpStart = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 2, 14, 0), Value = DateTime.Today };
        _dtpStart.ValueChanged += UpdateDays; dateRow.Controls.Add(_dtpStart);
        dateRow.Controls.Add(new Label { Text = "Fin:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpEnd = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 2, 10, 0), Value = DateTime.Today };
        _dtpEnd.ValueChanged += UpdateDays; dateRow.Controls.Add(_dtpEnd);
        _lblDays = new Label { AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.SidebarActive, Margin = new Padding(0, 6, 0, 0) };
        dateRow.Controls.Add(_lblDays);
        body.Controls.Add(dateRow); y += 46;

        body.Controls.Add(new Label { Text = "Comentario (opcional)", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 24;
        _txtComment = new TextBox { Location = new Point(x, y), Width = 420, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtComment); y += 102;

        body.Controls.Add(new Label { Text = "Documento de respaldo (opcional)", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 24;
        var btnAttach = AppTheme.MakeSecondaryButton("📎 Adjuntar archivo", 160); btnAttach.Location = new Point(x, y); btnAttach.Click += BtnAttach_Click;
        var btnClear = AppTheme.MakeSecondaryButton("✖ Quitar", 100); btnClear.Location = new Point(x + 168, y); btnClear.Click += (_, _) => SetAttachment(null, null);
        body.Controls.AddRange([btnAttach, btnClear]); y += 40;
        _lblFile = new Label { Text = "— sin adjunto —", Location = new Point(x, y), AutoSize = false, Size = new Size(420, 20), ForeColor = AppTheme.TextSecondary };
        body.Controls.Add(_lblFile); y += 30;
        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Solicitar", 110); btnSave.Click += BtnSave_Click;
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
        UpdateDays(null, EventArgs.Empty);
    }

    private void UpdateDays(object? s, EventArgs e)
    {
        int days = Math.Max(0, (_dtpEnd.Value.Date - _dtpStart.Value.Date).Days + 1);
        _lblDays.Text = $"({days} día(s))";
        _lblDays.ForeColor = _dtpEnd.Value < _dtpStart.Value ? AppTheme.Danger : AppTheme.SidebarActive;
    }

    private void BtnAttach_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Seleccionar documento", Filter = "Documentos e imágenes (*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg)|*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg|Todos los archivos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 15 * 1024 * 1024) { MessageBox.Show("El archivo supera 15 MB.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetAttachment(bytes, Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo cargar el archivo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SetAttachment(byte[]? bytes, string? name)
    {
        _attachment = bytes; _attachmentName = name;
        _lblFile.Text = bytes == null ? "— sin adjunto —" : $"📎 {name}  ({bytes.Length / 1024} KB)";
        _lblFile.ForeColor = bytes == null ? AppTheme.TextSecondary : AppTheme.Success;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (_dtpEnd.Value.Date < _dtpStart.Value.Date)
        { MessageBox.Show("La fecha fin debe ser posterior o igual a la fecha inicio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.StartDate = _dtpStart.Value.Date;
        Result.EndDate = _dtpEnd.Value.Date;
        Result.Comment = string.IsNullOrWhiteSpace(_txtComment.Text) ? null : _txtComment.Text.Trim();
        Result.AttachmentBytes = _attachment;
        Result.AttachmentFileName = _attachmentName;
        DialogResult = DialogResult.OK;
        Close();
    }
}
