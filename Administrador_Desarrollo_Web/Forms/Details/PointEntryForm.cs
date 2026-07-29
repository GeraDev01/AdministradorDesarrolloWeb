using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Asigna un criterio de puntos a uno o varios desarrolladores a la vez.
/// Devuelve una entrada (PointEntry) por cada desarrollador marcado.
/// </summary>
public class PointEntryForm : Form
{
    private readonly List<Developer> _devs;
    private readonly List<ScoringCriterion> _criteria;
    private readonly List<Requirement> _reqs;

    private CheckedListBox _clbDevs = null!;
    private CheckBox _chkAllDevs = null!;
    private ComboBox _cbxCriterion = null!;
    private NumericUpDown _nudPoints = null!;
    private NumericUpDown _nudVeces = null!;
    private ComboBox _cbxMonth = null!;
    private NumericUpDown _nudYear = null!;
    private ComboBox _cbxReq = null!;
    private TextBox _txtComment = null!;
    private Label _lblPointsHint = null!;
    private PictureBox _picShot = null!;
    private Label _lblShot = null!;
    private bool _syncingAll;

    private byte[]? _screenshot;
    private string? _screenshotName;

    /// <summary>Una entrada por cada desarrollador seleccionado.</summary>
    public List<PointEntry> Results { get; } = new();

    public PointEntryForm(AppDbContext db)
    {
        _devs    = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        _criteria = db.ScoringCriteria.Where(c => c.IsActive && c.Scope != CriterionScope.Equipo).OrderBy(c => c.Name).ToList();
        _reqs    = db.Requirements.Where(r => r.Status != RequirementStatus.Cancelado && r.Status != RequirementStatus.Entregado)
                     .OrderBy(r => r.Title).ToList();
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Asignar Puntos"; Size = new Size(480, 790);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
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
        hdr.Controls.Add(new Label { Text = "  🏆  Asignar Puntos", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        int y = 12;

        // ── Desarrolladores (múltiple) ──────────────────────────────
        body.Controls.Add(new Label { Text = "Desarrolladores *  (marca uno o varios)", Location = new Point(25, y), AutoSize = true });
        _chkAllDevs = new CheckBox { Text = "Todos", Location = new Point(378, y - 2), AutoSize = true };
        _chkAllDevs.CheckedChanged += ChkAllDevs_Changed;
        body.Controls.Add(_chkAllDevs);
        y += 22;
        _clbDevs = new CheckedListBox { Location = new Point(25, y), Width = 420, Height = 120, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
        foreach (var d in _devs) _clbDevs.Items.Add(d.FullName);
        _clbDevs.ItemCheck += (_, _) => { if (!_syncingAll) BeginInvoke(SyncAllCheckbox); };
        body.Controls.Add(_clbDevs);
        y += 130;

        // ── Criterio ────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Criterio *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxCriterion = new ComboBox { Location = new Point(25, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _criteria) _cbxCriterion.Items.Add($"{c.Name}  ({(c.DefaultPoints >= 0 ? "+" : "")}{c.DefaultPoints} pts)");
        if (_cbxCriterion.Items.Count > 0) _cbxCriterion.SelectedIndex = 0;
        _cbxCriterion.SelectedIndexChanged += CriterionChanged;
        body.Controls.Add(_cbxCriterion); y += 38;

        // ── Puntos ──────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Puntos (ajustable):", Location = new Point(25, y), AutoSize = true });
        _lblPointsHint = new Label { Location = new Point(180, y), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont };
        body.Controls.Add(_lblPointsHint); y += 20;
        _nudPoints = new NumericUpDown { Location = new Point(25, y), Width = 120, Minimum = -999, Maximum = 999, Value = 0 };
        body.Controls.Add(_nudPoints);
        body.Controls.Add(new Label { Text = "×  veces:", Location = new Point(160, y + 3), AutoSize = true });
        _nudVeces = new NumericUpDown { Location = new Point(230, y), Width = 60, Minimum = 1, Maximum = 99, Value = 1 };
        body.Controls.Add(_nudVeces);
        body.Controls.Add(new Label { Text = "(crea N entradas del mismo criterio)", Location = new Point(300, y + 3), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        y += 38;

        // ── Período ─────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Período:", Location = new Point(25, y), AutoSize = true }); y += 20;
        var row = new FlowLayoutPanel { Location = new Point(25, y), Width = 420, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _cbxMonth = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        var months = new[] { "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
        _cbxMonth.Items.AddRange(months); _cbxMonth.SelectedIndex = DateTime.Today.Month - 1;
        _nudYear = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year };
        row.Controls.AddRange([_cbxMonth, new Label { Text = "  Año:", AutoSize = true, Margin = new Padding(8, 6, 4, 0) }, _nudYear]);
        body.Controls.Add(row); y += 42;

        // ── Requerimiento ───────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Requerimiento (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxReq = new ComboBox { Location = new Point(25, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxReq.Items.Add("(Ninguno)");
        foreach (var r in _reqs) _cbxReq.Items.Add($"#{r.Id} {r.Title}");
        _cbxReq.SelectedIndex = 0;
        body.Controls.Add(_cbxReq); y += 38;

        // ── Comentario ──────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Comentario:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtComment = new TextBox { Location = new Point(25, y), Width = 420, Height = 50, Multiline = true };
        body.Controls.Add(_txtComment); y += 58;

        // ── Captura de pantalla (opcional) ──────────────────────────
        body.Controls.Add(new Label { Text = "Captura de pantalla (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        var btnShotFile  = AppTheme.MakeSecondaryButton("📷 Adjuntar", 120); btnShotFile.Location = new Point(25, y); btnShotFile.Click += BtnAttachShot_Click;
        var btnShotPaste = AppTheme.MakeSecondaryButton("📋 Pegar", 100);   btnShotPaste.Location = new Point(150, y); btnShotPaste.Click += BtnPasteShot_Click;
        var btnShotClear = AppTheme.MakeSecondaryButton("✖ Quitar", 95);    btnShotClear.Location = new Point(255, y); btnShotClear.Click += (_, _) => SetScreenshot(null, null);
        body.Controls.AddRange([btnShotFile, btnShotPaste, btnShotClear]); y += 36;
        _picShot = new PictureBox { Location = new Point(25, y), Size = new Size(280, 110), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        body.Controls.Add(_picShot);
        _lblShot = new Label { Text = "— sin captura —", Location = new Point(315, y + 44), AutoSize = true, ForeColor = AppTheme.TextSecondary };
        body.Controls.Add(_lblShot);
        y += 120;

        body.AutoScrollMinSize = new Size(0, y);

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Asignar", 100); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
        CriterionChanged(null, EventArgs.Empty);
    }

    private void ChkAllDevs_Changed(object? s, EventArgs e)
    {
        if (_syncingAll) return;
        _syncingAll = true;
        for (int i = 0; i < _clbDevs.Items.Count; i++) _clbDevs.SetItemChecked(i, _chkAllDevs.Checked);
        _syncingAll = false;
    }

    private void SyncAllCheckbox()
    {
        _syncingAll = true;
        _chkAllDevs.Checked = _clbDevs.CheckedItems.Count == _clbDevs.Items.Count && _clbDevs.Items.Count > 0;
        _syncingAll = false;
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
        if (bytes == null)
        {
            _lblShot.Text = "— sin captura —";
            _lblShot.ForeColor = AppTheme.TextSecondary;
        }
        else
        {
            try { using var ms = new MemoryStream(bytes); _picShot.Image = new Bitmap(ms); } catch { }
            _lblShot.Text = name ?? "captura";
            _lblShot.ForeColor = AppTheme.Success;
        }
    }

    private void CriterionChanged(object? s, EventArgs e)
    {
        if (_cbxCriterion.SelectedIndex < 0 || _cbxCriterion.SelectedIndex >= _criteria.Count) return;
        var c = _criteria[_cbxCriterion.SelectedIndex];
        _nudPoints.Value = Math.Clamp(c.DefaultPoints, -999, 999);
        _lblPointsHint.Text = $"  (default del criterio: {(c.DefaultPoints >= 0 ? "+" : "")}{c.DefaultPoints})";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        var checkedIdx = _clbDevs.CheckedIndices.Cast<int>().ToList();
        if (checkedIdx.Count == 0 || _cbxCriterion.SelectedIndex < 0)
        { MessageBox.Show("Marca al menos un desarrollador y un criterio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        int criterionId = _criteria[_cbxCriterion.SelectedIndex].Id;
        int points = (int)_nudPoints.Value;
        int month = _cbxMonth.SelectedIndex + 1;
        int year = (int)_nudYear.Value;
        string? comment = string.IsNullOrWhiteSpace(_txtComment.Text) ? null : _txtComment.Text.Trim();
        int? reqId = _cbxReq.SelectedIndex > 0 ? _reqs[_cbxReq.SelectedIndex - 1].Id : null;
        var now = DateTime.UtcNow;

        int veces = (int)_nudVeces.Value;
        Results.Clear();
        foreach (var idx in checkedIdx)
            for (int n = 0; n < veces; n++)
            {
                Results.Add(new PointEntry
                {
                    DeveloperId = _devs[idx].Id,
                    CriterionId = criterionId,
                    Points = points,
                    Month = month,
                    Year = year,
                    Comment = comment,
                    RequirementId = reqId,
                    Date = now,
                    // cada entrada recibe su propia copia de los bytes de la captura
                    Screenshot = _screenshot == null ? null : (byte[])_screenshot.Clone(),
                    ScreenshotFileName = _screenshotName
                });
            }

        DialogResult = DialogResult.OK; Close();
    }
}
