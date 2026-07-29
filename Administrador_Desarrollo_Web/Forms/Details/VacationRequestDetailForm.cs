using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class VacationRequestDetailForm : Form
{
    private readonly AppDbContext _db;
    private readonly List<Developer> _devs;
    private readonly int? _editingId;

    private ComboBox _cbxDev = null!;
    private DateTimePicker _dtpStart = null!;
    private DateTimePicker _dtpEnd = null!;
    private TextBox _txtComment = null!;
    private Label _lblDays = null!;
    private Label _lblFile = null!;
    private Button _btnView = null!;

    private byte[]? _attachment;
    private string? _attachmentName;
    private bool _attachmentChanged;

    public VacationRequest Result { get; private set; } = new();

    public VacationRequestDetailForm(AppDbContext db, VacationRequest? vr = null)
    {
        _db = db;
        _devs = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        _editingId = vr?.Id;
        BuildUI();
        if (vr != null) Populate(vr);
    }

    private void BuildUI()
    {
        Text = "Solicitud de Vacaciones";
        Size = new Size(480, 520);
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
        const int x = 24; int y = 14;

        body.Controls.Add(new Label { Text = "Desarrollador *", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        _cbxDev = new ComboBox { Location = new Point(x, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
        if (_cbxDev.Items.Count > 0) _cbxDev.SelectedIndex = 0;
        body.Controls.Add(_cbxDev); y += 40;

        body.Controls.Add(new Label { Text = "Fechas *", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        var dateRow = new FlowLayoutPanel { Location = new Point(x, y), Size = new Size(420, 30), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        dateRow.Controls.Add(new Label { Text = "Inicio:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpStart = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 2, 14, 0) };
        _dtpStart.ValueChanged += UpdateDays; dateRow.Controls.Add(_dtpStart);
        dateRow.Controls.Add(new Label { Text = "Fin:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _dtpEnd = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 2, 10, 0) };
        _dtpEnd.ValueChanged += UpdateDays; dateRow.Controls.Add(_dtpEnd);
        _lblDays = new Label { AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.SidebarActive, Margin = new Padding(0, 6, 0, 0) };
        dateRow.Controls.Add(_lblDays);
        body.Controls.Add(dateRow); y += 44;

        body.Controls.Add(new Label { Text = "Comentario", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        _txtComment = new TextBox { Location = new Point(x, y), Width = 420, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtComment); y += 82;

        body.Controls.Add(new Label { Text = "Documento de respaldo (opcional)", Location = new Point(x, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 24;
        var btnAttach = AppTheme.MakeSecondaryButton("📎 Adjuntar", 120); btnAttach.Location = new Point(x, y); btnAttach.Click += BtnAttach_Click;
        _btnView = AppTheme.MakeSecondaryButton("👁 Ver", 90); _btnView.Location = new Point(x + 128, y); _btnView.Enabled = false; _btnView.Click += (_, _) => { if (_editingId is int id) VacationAttachment.Open(_db, id, this); };
        var btnClear = AppTheme.MakeSecondaryButton("✖ Quitar", 90); btnClear.Location = new Point(x + 226, y); btnClear.Click += (_, _) => SetAttachment(null, null, changed: true);
        body.Controls.AddRange([btnAttach, _btnView, btnClear]); y += 38;
        _lblFile = new Label { Text = "— sin adjunto —", Location = new Point(x, y), AutoSize = false, Size = new Size(420, 20), ForeColor = AppTheme.TextSecondary };
        body.Controls.Add(_lblFile); y += 30;
        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Click += BtnSave_Click;
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
        UpdateDays(null, EventArgs.Empty);
    }

    private void UpdateDays(object? s, EventArgs e)
    {
        int days = Math.Max(0, (_dtpEnd.Value.Date - _dtpStart.Value.Date).Days + 1);
        _lblDays.Text = $"{days} día(s)";
        _lblDays.ForeColor = _dtpEnd.Value < _dtpStart.Value ? AppTheme.Danger : AppTheme.SidebarActive;
    }

    private void Populate(VacationRequest vr)
    {
        Result = vr;
        var idx = _devs.FindIndex(d => d.Id == vr.DeveloperId);
        if (idx >= 0) _cbxDev.SelectedIndex = idx;
        _dtpStart.Value = vr.StartDate;
        _dtpEnd.Value = vr.EndDate;
        _txtComment.Text = vr.Comment ?? "";
        if (!string.IsNullOrWhiteSpace(vr.AttachmentFileName))
        {
            _lblFile.Text = $"📎 {vr.AttachmentFileName}";
            _lblFile.ForeColor = AppTheme.Success;
            _btnView.Enabled = true;   // el BLOB se lee bajo demanda por Id
        }
    }

    private void BtnAttach_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Seleccionar documento", Filter = "Documentos e imágenes (*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg)|*.pdf;*.docx;*.doc;*.png;*.jpg;*.jpeg|Todos los archivos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 15 * 1024 * 1024) { MessageBox.Show("El archivo supera 15 MB.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetAttachment(bytes, Path.GetFileName(dlg.FileName), changed: true);
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo cargar el archivo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SetAttachment(byte[]? bytes, string? name, bool changed)
    {
        _attachment = bytes; _attachmentName = name; _attachmentChanged = changed;
        _lblFile.Text = bytes == null ? "— sin adjunto —" : $"📎 {name}  ({bytes.Length / 1024} KB)";
        _lblFile.ForeColor = bytes == null ? AppTheme.TextSecondary : AppTheme.Success;
        // "Ver" solo aplica al adjunto ya persistido (se lee por Id); uno recién elegido o quitado no.
        _btnView.Enabled = false;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (_cbxDev.SelectedIndex < 0) { MessageBox.Show("Selecciona un desarrollador.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_dtpEnd.Value < _dtpStart.Value) { MessageBox.Show("La fecha fin debe ser posterior a la fecha inicio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.DeveloperId = _devs[_cbxDev.SelectedIndex].Id;
        Result.StartDate = _dtpStart.Value.Date;
        Result.EndDate = _dtpEnd.Value.Date;
        Result.Comment = string.IsNullOrWhiteSpace(_txtComment.Text) ? null : _txtComment.Text.Trim();
        // Solo tocar el adjunto si el usuario lo cambió (así no se pierde el existente al editar).
        if (_attachmentChanged)
        {
            Result.AttachmentBytes = _attachment;
            Result.AttachmentFileName = _attachmentName;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
