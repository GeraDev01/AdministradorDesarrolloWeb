using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// El desarrollador registra una actividad y se autocalifica con puntos POSITIVOS.
/// La entrada nace en estado Pendiente y solo cuenta en el ranking cuando el jefe
/// la aprueba. No permite puntos negativos (esos son exclusivos del jefe).
/// </summary>
public class SelfPointEntryForm : Form
{
    private readonly int _developerId;
    private readonly List<ScoringCriterion> _criteria;
    private readonly List<Requirement> _reqs;

    private ComboBox _cbxCriterion = null!;
    private Label _lblPoints = null!;
    private Label _lblPointsHint = null!;
    private ComboBox _cbxMonth = null!;
    private NumericUpDown _nudYear = null!;
    private ComboBox _cbxReq = null!;
    private TextBox _txtComment = null!;
    private PictureBox _picShot = null!;
    private Label _lblShot = null!;

    private byte[]? _screenshot;
    private string? _screenshotName;

    public PointEntry? Result { get; private set; }

    public SelfPointEntryForm(AppDbContext db, int developerId)
    {
        _developerId = developerId;
        // Solo criterios POSITIVOS que aplican a individuos.
        _criteria = db.ScoringCriteria
            .Where(c => c.IsActive && c.Scope != CriterionScope.Equipo && c.DefaultPoints > 0)
            .OrderBy(c => c.Name).ToList();
        // Requerimientos asignados a este desarrollador (para vincular la actividad).
        _reqs = db.Requirements
            .Where(r => r.Assignments.Any(a => a.DeveloperId == developerId)
                     && r.Status != RequirementStatus.Cancelado && r.Status != RequirementStatus.Entregado)
            .OrderBy(r => r.Title).ToList();
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Registrar actividad"; Size = new Size(480, 720);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📝  Registrar actividad (autocalificación)", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        int y = 12;

        body.Controls.Add(new Label { Text = "Los puntos que registres quedan PENDIENTES hasta que el jefe los apruebe.", Location = new Point(25, y), AutoSize = true, ForeColor = AppTheme.Warning, Font = AppTheme.SmallFont }); y += 26;

        body.Controls.Add(new Label { Text = "Actividad / criterio *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxCriterion = new ComboBox { Location = new Point(25, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _criteria) _cbxCriterion.Items.Add($"{c.Name}  (+{c.DefaultPoints} pts)");
        if (_cbxCriterion.Items.Count > 0) _cbxCriterion.SelectedIndex = 0;
        _cbxCriterion.SelectedIndexChanged += CriterionChanged;
        body.Controls.Add(_cbxCriterion); y += 38;

        // El puntaje lo fija el criterio, no el desarrollador: se muestra, no se edita.
        // Antes era un NumericUpDown editable de 1 a 999, lo que contradecía la regla del módulo.
        body.Controls.Add(new Label { Text = "Puntos que otorga esta actividad:", Location = new Point(25, y), AutoSize = true }); y += 22;
        _lblPoints = new Label
        {
            Location = new Point(25, y), Size = new Size(120, 34),
            Font = AppTheme.KpiValueFont, ForeColor = AppTheme.Success,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle
        };
        body.Controls.Add(_lblPoints);
        _lblPointsHint = new Label
        {
            Location = new Point(155, y + 8), AutoSize = false, Size = new Size(290, 32),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont,
            Text = "Lo define el administrador en el criterio.\nSolo él puede ajustarlo al revisar."
        };
        body.Controls.Add(_lblPointsHint); y += 44;

        body.Controls.Add(new Label { Text = "Período:", Location = new Point(25, y), AutoSize = true }); y += 20;
        var row = new FlowLayoutPanel { Location = new Point(25, y), Width = 420, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _cbxMonth = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        var months = new[] { "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
        _cbxMonth.Items.AddRange(months); _cbxMonth.SelectedIndex = DateTime.Today.Month - 1;
        _nudYear = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year };
        row.Controls.AddRange([_cbxMonth, new Label { Text = "  Año:", AutoSize = true, Margin = new Padding(8, 6, 4, 0) }, _nudYear]);
        body.Controls.Add(row); y += 42;

        body.Controls.Add(new Label { Text = "Requerimiento (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxReq = new ComboBox { Location = new Point(25, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxReq.Items.Add("(Ninguno)");
        foreach (var r in _reqs) _cbxReq.Items.Add($"#{r.Id} {r.Title}");
        _cbxReq.SelectedIndex = 0;
        body.Controls.Add(_cbxReq); y += 38;

        body.Controls.Add(new Label { Text = "Comentario / evidencia:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtComment = new TextBox { Location = new Point(25, y), Width = 420, Height = 50, Multiline = true };
        body.Controls.Add(_txtComment); y += 58;

        body.Controls.Add(new Label { Text = "Captura de pantalla (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        var btnShotFile  = AppTheme.MakeSecondaryButton("📷 Adjuntar", 120); btnShotFile.Location = new Point(25, y); btnShotFile.Click += BtnAttachShot_Click;
        var btnShotPaste = AppTheme.MakeSecondaryButton("📋 Pegar", 100);   btnShotPaste.Location = new Point(150, y); btnShotPaste.Click += BtnPasteShot_Click;
        var btnShotClear = AppTheme.MakeSecondaryButton("✖ Quitar", 95);    btnShotClear.Location = new Point(255, y); btnShotClear.Click += (_, _) => SetScreenshot(null, null);
        body.Controls.AddRange([btnShotFile, btnShotPaste, btnShotClear]); y += 36;
        _picShot = new PictureBox { Location = new Point(25, y), Size = new Size(280, 100), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        body.Controls.Add(_picShot);
        _lblShot = new Label { Text = "— sin captura —", Location = new Point(315, y + 40), AutoSize = true, ForeColor = AppTheme.TextSecondary };
        body.Controls.Add(_lblShot);
        y += 112;

        body.AutoScrollMinSize = new Size(0, y);

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Enviar a revisión", 150); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
        CriterionChanged(null, EventArgs.Empty);
    }

    private void CriterionChanged(object? s, EventArgs e)
    {
        if (_cbxCriterion.SelectedIndex < 0 || _cbxCriterion.SelectedIndex >= _criteria.Count)
        {
            _lblPoints.Text = "—";
            return;
        }
        _lblPoints.Text = $"+{_criteria[_cbxCriterion.SelectedIndex].DefaultPoints}";
    }

    private void BtnAttachShot_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Seleccionar captura", Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 15 * 1024 * 1024) { MessageBox.Show("La imagen supera 15 MB.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetScreenshot(bytes, Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo cargar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnPasteShot_Click(object? s, EventArgs e)
    {
        if (!Clipboard.ContainsImage()) { MessageBox.Show("No hay una imagen en el portapapeles.\n(Usa Win+Shift+S para recortar la pantalla.)", "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            using var img = Clipboard.GetImage();
            if (img == null) return;
            using var bmp = new Bitmap(img);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            SetScreenshot(ms.ToArray(), $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo pegar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SetScreenshot(byte[]? bytes, string? name)
    {
        _picShot.Image?.Dispose();
        _picShot.Image = null;
        _screenshot = bytes;
        _screenshotName = name;
        if (bytes == null) { _lblShot.Text = "— sin captura —"; _lblShot.ForeColor = AppTheme.TextSecondary; }
        else
        {
            try { using var ms = new MemoryStream(bytes); _picShot.Image = new Bitmap(ms); } catch { }
            _lblShot.Text = name ?? "captura";
            _lblShot.ForeColor = AppTheme.Success;
        }
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (_cbxCriterion.SelectedIndex < 0 || _cbxCriterion.SelectedIndex >= _criteria.Count)
        { MessageBox.Show("Selecciona una actividad/criterio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        int? reqId = _cbxReq.SelectedIndex > 0 ? _reqs[_cbxReq.SelectedIndex - 1].Id : null;
        // Points, ApprovalStatus y los campos de revisión los fija
        // PerformanceScoringService.RegistrarAutocalificacion; aquí no se envían.
        Result = new PointEntry
        {
            DeveloperId = _developerId,
            CriterionId = _criteria[_cbxCriterion.SelectedIndex].Id,
            Month = _cbxMonth.SelectedIndex + 1,
            Year = (int)_nudYear.Value,
            Comment = string.IsNullOrWhiteSpace(_txtComment.Text) ? null : _txtComment.Text.Trim(),
            RequirementId = reqId,
            Screenshot = _screenshot,
            ScreenshotFileName = _screenshotName
        };
        DialogResult = DialogResult.OK; Close();
    }
}
