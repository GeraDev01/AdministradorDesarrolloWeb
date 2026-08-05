using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// El desarrollador registra una actividad y se autocalifica con puntos POSITIVOS.
/// La entrada nace en estado Pendiente y solo cuenta en el ranking cuando el jefe
/// la aprueba. No permite puntos negativos (esos son exclusivos del jefe).
///
/// El mismo formulario sirve para CORREGIR una entrada que sigue pendiente: se construye con
/// <paramref name="existente"/> y solo cambian el título, el botón y los valores precargados.
/// Duplicarlo en un segundo formulario habría duplicado también la captura de evidencia.
/// </summary>
public class SelfPointEntryForm : ResponsiveForm
{
    private readonly int _developerId;
    private readonly PointEntry? _existente;
    private readonly List<ScoringCriterion> _criteria;
    private readonly List<Requirement> _reqs;

    private ComboBox _cbxCriterion = null!;
    private Label _lblCriterionDesc = null!;
    private Label _lblPoints = null!;
    private Label _lblPointsHint = null!;
    private ComboBox _cbxMonth = null!;
    private NumericUpDown _nudYear = null!;
    private ComboBox _cbxReq = null!;
    private TextBox _txtComment = null!;
    private NumericUpDown _nudHoras = null!, _nudMinutos = null!;
    private Label _lblTiempoResumen = null!;
    private TextBox _txtLink = null!;
    private Label _lblLinkEstado = null!;
    private PictureBox _picShot = null!;
    private Label _lblShot = null!;

    private byte[]? _screenshot;
    private string? _screenshotName;
    private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100 };

    /// <summary>Nombre y nivel del desarrollador, para el encabezado.</summary>
    private readonly (string? Nombre, string? Nivel) _quienSoy;

    private bool EsEdicion => _existente != null;

    /// <summary>Id de la entrada que se está corrigiendo; 0 cuando es un alta.</summary>
    public int EntryId => _existente?.Id ?? 0;

    public PointEntry? Result { get; private set; }

    public SelfPointEntryForm(AppDbContext db, int developerId, PointEntry? existente = null)
    {
        _developerId = developerId;
        _existente = existente;

        // Nombre y nivel para el encabezado. AsNoTracking: el AppDbContext es Singleton y esto es
        // solo lectura de un dato que mantiene el administrador desde otra pantalla.
        _quienSoy = db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId)
            .Select(d => new { d.FullName, d.Seniority })
            .FirstOrDefault() is { } yo ? (yo.FullName, yo.Seniority) : (null, null);

        // Solo criterios POSITIVOS que aplican a individuos.
        _criteria = db.ScoringCriteria
            .Where(c => c.IsActive && c.Scope != CriterionScope.Equipo && c.DefaultPoints > 0)
            .OrderBy(c => c.Name).ToList();

        // Al corregir, el criterio elegido en su día pudo desactivarse. Se agrega a la lista para
        // no cambiárselo por sorpresa: si sigue ahí al guardar, el servicio lo rechaza y se le
        // explica; si lo quitáramos, la entrada se guardaría con OTRO criterio sin avisar.
        if (existente != null && _criteria.All(c => c.Id != existente.CriterionId))
        {
            var suyo = db.ScoringCriteria.FirstOrDefault(c => c.Id == existente.CriterionId);
            if (suyo != null) _criteria.Insert(0, suyo);
        }

        // Requerimientos asignados a este desarrollador (para vincular la actividad).
        _reqs = db.Requirements
            .Where(r => r.Assignments.Any(a => a.DeveloperId == developerId)
                     && r.Status != RequirementStatus.Cancelado && r.Status != RequirementStatus.Entregado)
            .OrderBy(r => r.Title).ToList();

        // Mismo criterio con el requerimiento vinculado: si ya se entregó, sigue siendo el correcto.
        if (existente?.RequirementId is int reqId && _reqs.All(r => r.Id != reqId))
        {
            var suyo = db.Requirements.FirstOrDefault(r => r.Id == reqId);
            if (suyo != null) _reqs.Insert(0, suyo);
        }

        BuildUI();
        if (existente != null) CargarExistente(existente);
    }

    private void BuildUI()
    {
        Text = EsEdicion ? "Corregir actividad" : "Registrar actividad";
        Size = new Size(500, 800);
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
        hdr.Controls.Add(new Label
        {
            Text = EsEdicion ? "  ✏  Corregir actividad pendiente" : "  📝  Registrar actividad (autocalificación)",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        int y = 12;

        body.Controls.Add(new Label
        {
            Text = EsEdicion
                ? "Puedes corregirla mientras siga pendiente. Una vez revisada ya no se podrá cambiar."
                : "Los puntos que registres quedan PENDIENTES hasta que el líder los apruebe.",
            Location = new Point(25, y), AutoSize = true, ForeColor = AppTheme.Warning, Font = AppTheme.SmallFont
        }); y += 24;

        // Quién eres y con qué nivel estás registrado. Varias actividades son «(Junior)», «(Mid)» o
        // «(Senior)»: sin tener el nivel a la vista no se sabe cuáles te tocan.
        var lblQuien = new Label
        {
            Text = $"{_quienSoy.Nombre ?? "Tú"}   ·   Tu nivel: {NivelDesarrolladorUi.Etiqueta(_quienSoy.Nivel)}",
            Location = new Point(25, y), AutoSize = true, Font = AppTheme.BoldFont,
            ForeColor = NivelDesarrolladorUi.Color(_quienSoy.Nivel)
        };
        body.Controls.Add(lblQuien);
        if (NivelDesarrolladorUi.Explicacion(_quienSoy.Nivel) is { } queSignifica)
            _tip.SetToolTip(lblQuien, queSignifica);
        y += 28;

        // «Criterio» es vocabulario del módulo de puntuación, no de quien registra lo que hizo. Al
        // desarrollador se le pregunta lo que de verdad se le está preguntando.
        body.Controls.Add(new Label { Text = "¿Qué hiciste? *", Location = new Point(25, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 20;
        _cbxCriterion = new ComboBox { Location = new Point(25, y), Width = 435, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _criteria)
            _cbxCriterion.Items.Add($"{c.Name}  (+{c.DefaultPoints} pts)" + (c.IsActive ? "" : "  — INACTIVO"));
        if (_cbxCriterion.Items.Count > 0) _cbxCriterion.SelectedIndex = 0;
        _cbxCriterion.SelectedIndexChanged += CriterionChanged;
        body.Controls.Add(_cbxCriterion); y += 34;

        // Antes esto era un tooltip con TODO el catálogo dentro: con casi cuarenta actividades el
        // globo tapaba media pantalla y desaparecía justo al mover el ratón para compararlas. Ahora
        // es una ventana con lista, buscador y descripción completa, de la que además se puede
        // elegir directamente.
        var btnGuia = AppTheme.MakeSecondaryButton("📖 Ver descripciones", 190, 28);
        btnGuia.Location = new Point(25, y);
        btnGuia.Click += BtnVerDescripciones_Click;
        body.Controls.Add(btnGuia);
        _tip.SetToolTip(btnGuia, "Abre la lista completa con lo que significa cada actividad y cuánto vale.\n" +
                                 "Puedes buscar y elegir desde ahí.");
        y += 36;

        // Qué significa lo seleccionado. Los nombres solos no siempre bastan, y tenerlo aquí evita
        // abrir la lista para confirmar que elegiste lo que creías.
        _lblCriterionDesc = new Label
        {
            Location = new Point(27, y), AutoSize = false, Size = new Size(433, 46),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        };
        body.Controls.Add(_lblCriterionDesc); y += 52;

        // El puntaje lo fija el criterio, no el desarrollador: se muestra, no se edita.
        // Antes era un NumericUpDown editable de 1 a 999, lo que contradecía la regla del módulo.
        body.Controls.Add(new Label { Text = "Esto vale:", Location = new Point(25, y), AutoSize = true }); y += 22;
        _lblPoints = new Label
        {
            Location = new Point(25, y), Size = new Size(120, 34),
            Font = AppTheme.KpiValueFont, ForeColor = AppTheme.Success,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle
        };
        body.Controls.Add(_lblPoints);
        _lblPointsHint = new Label
        {
            Location = new Point(155, y + 8), AutoSize = false, Size = new Size(305, 32),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont,
            Text = "El valor lo define el líder.\nSolo él puede ajustarlo al revisarlo."
        };
        body.Controls.Add(_lblPointsHint); y += 44;

        body.Controls.Add(new Label { Text = "Período:", Location = new Point(25, y), AutoSize = true }); y += 20;
        var row = new FlowLayoutPanel { Location = new Point(25, y), Width = 435, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _cbxMonth = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        var months = new[] { "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
        _cbxMonth.Items.AddRange(months); _cbxMonth.SelectedIndex = DateTime.Today.Month - 1;
        _nudYear = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year };
        row.Controls.AddRange([_cbxMonth, new Label { Text = "  Año:", AutoSize = true, Margin = new Padding(8, 6, 4, 0) }, _nudYear]);
        body.Controls.Add(row); y += 42;

        // ── Tiempo dedicado ────────────────────────────────────────────────
        // Es DECLARADO, no cronometrado: cubre el trabajo que ocurre sin la aplicación abierta
        // (una junta, un apoyo de pasillo), que es justo el que antes no quedaba en ningún lado.
        body.Controls.Add(new Label { Text = "Tiempo dedicado (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        var rowTiempo = new FlowLayoutPanel { Location = new Point(25, y), Width = 435, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        // El tope sale del que aplica el servicio, no de un número escrito aquí: con 744 h el
        // control dejaba capturar «744 h 30 m», que son 59 minutos MÁS de lo que se acepta, y el
        // registro se rechazaba al guardar sin que el campo hubiera avisado de nada.
        _nudHoras = new NumericUpDown
        {
            Width = 70, Minimum = 0, Value = 0,
            Maximum = (PerformanceScoringService.MaxMinutosDeclarados - 59) / 60
        };
        _nudMinutos = new NumericUpDown { Width = 70, Minimum = 0, Maximum = 59, Value = 0, Increment = 5 };
        _nudHoras.ValueChanged += (_, _) => ActualizarResumenTiempo();
        _nudMinutos.ValueChanged += (_, _) => ActualizarResumenTiempo();
        rowTiempo.Controls.AddRange([
            _nudHoras, new Label { Text = "h", AutoSize = true, Margin = new Padding(4, 6, 12, 0) },
            _nudMinutos, new Label { Text = "min", AutoSize = true, Margin = new Padding(4, 6, 0, 0) }]);
        body.Controls.Add(rowTiempo); y += 34;
        _lblTiempoResumen = new Label
        {
            Location = new Point(27, y), AutoSize = false, Size = new Size(433, 18),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        };
        body.Controls.Add(_lblTiempoResumen); y += 24;
        _tip.SetToolTip(_nudHoras, "Cuánto te llevó esta actividad. Déjalo en 0 si no aplica o no lo mediste.\n" +
                                   "Es independiente del cronómetro de requerimientos: aquí puedes anotar\n" +
                                   "trabajo hecho sin la aplicación abierta (juntas, apoyos, investigación).");
        _tip.SetToolTip(_nudMinutos, "Minutos, además de las horas de la izquierda.");

        // ── Enlace de evidencia ────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Enlace al item de DevOps (PR, work item o ticket):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtLink = new TextBox { Location = new Point(25, y), Width = 340, PlaceholderText = "https://dev.azure.com/…" };
        _txtLink.TextChanged += (_, _) => ActualizarEstadoEnlace();
        var btnProbar = AppTheme.MakeSecondaryButton("↗ Probar", 90); btnProbar.Location = new Point(372, y - 1);
        btnProbar.Click += BtnProbarEnlace_Click;
        body.Controls.AddRange([_txtLink, btnProbar]); y += 30;
        _lblLinkEstado = new Label
        {
            Location = new Point(27, y), AutoSize = false, Size = new Size(433, 18),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont,
            Text = "Copia la dirección desde la barra del navegador. El líder podrá abrirla al revisar."
        };
        body.Controls.Add(_lblLinkEstado); y += 26;
        _tip.SetToolTip(_txtLink, "Pega la URL completa del pull request, work item o ticket que respalda\n" +
                                  "la actividad. Debe empezar con http:// o https://.\n" +
                                  "Es la evidencia que el líder revisa junto con la captura.");

        body.Controls.Add(new Label { Text = "Comentario / evidencia:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtComment = new TextBox { Location = new Point(25, y), Width = 435, Height = 50, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtComment); y += 58;

        body.Controls.Add(new Label { Text = "Requerimiento (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxReq = new ComboBox { Location = new Point(25, y), Width = 435, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxReq.Items.Add("(Ninguno)");
        foreach (var r in _reqs) _cbxReq.Items.Add($"#{r.Id} {r.Title}");
        _cbxReq.SelectedIndex = 0;
        body.Controls.Add(_cbxReq); y += 38;

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
        var btnSave = AppTheme.MakePrimaryButton(EsEdicion ? "Guardar cambios" : "Enviar a revisión", 160); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
        CriterionChanged(null, EventArgs.Empty);
        ActualizarResumenTiempo();
    }

    /// <summary>
    /// Abre la lista completa de actividades con su descripción. Si se elige una desde ahí, queda
    /// seleccionada aquí: buscarla y luego tener que encontrarla otra vez en el desplegable sería
    /// hacer el trabajo dos veces.
    /// </summary>
    private void BtnVerDescripciones_Click(object? s, EventArgs e)
    {
        if (_criteria.Count == 0)
        {
            MessageBox.Show("Todavía no hay actividades configuradas. Pídeselas al líder.",
                "Sin actividades", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new CriteriaGuideForm(_criteria, "¿Qué puedo registrar?");
        if (frm.ShowDialog(this) != DialogResult.OK || frm.CriterioElegido is not int id) return;

        int idx = _criteria.FindIndex(c => c.Id == id);
        if (idx >= 0) _cbxCriterion.SelectedIndex = idx;
    }

    private void CargarExistente(PointEntry e)
    {
        int idx = _criteria.FindIndex(c => c.Id == e.CriterionId);
        if (idx >= 0) _cbxCriterion.SelectedIndex = idx;

        _cbxMonth.SelectedIndex = Math.Clamp(e.Month, 1, 12) - 1;
        _nudYear.Value = Math.Clamp(e.Year, (int)_nudYear.Minimum, (int)_nudYear.Maximum);
        _txtComment.Text = e.Comment ?? "";
        _txtLink.Text = e.EvidenceUrl ?? "";

        int minutos = Math.Clamp(e.MinutesSpent ?? 0, 0, (int)_nudHoras.Maximum * 60 + 59);
        _nudHoras.Value = minutos / 60;
        _nudMinutos.Value = minutos % 60;

        if (e.RequirementId is int reqId)
        {
            int r = _reqs.FindIndex(x => x.Id == reqId);
            if (r >= 0) _cbxReq.SelectedIndex = r + 1;   // +1 por «(Ninguno)»
        }

        if (e.Screenshot is { Length: > 0 }) SetScreenshot(e.Screenshot, e.ScreenshotFileName);

        ActualizarResumenTiempo();
        ActualizarEstadoEnlace();
    }

    private void ActualizarResumenTiempo()
    {
        int minutos = (int)_nudHoras.Value * 60 + (int)_nudMinutos.Value;
        _lblTiempoResumen.Text = minutos == 0
            ? "Sin tiempo declarado."
            : $"Se registrarán {minutos} minutos ({(int)_nudHoras.Value}h {(int)_nudMinutos.Value:00}m).";
        _lblTiempoResumen.ForeColor = minutos == 0 ? AppTheme.TextSecondary : AppTheme.Success;
    }

    private void ActualizarEstadoEnlace()
    {
        var (ok, error, _) = PerformanceScoringService.NormalizarEnlace(_txtLink.Text);
        if (_txtLink.Text.Trim().Length == 0)
        {
            _lblLinkEstado.Text = "Copia la dirección desde la barra del navegador. El líder podrá abrirla al revisar.";
            _lblLinkEstado.ForeColor = AppTheme.TextSecondary;
        }
        else
        {
            _lblLinkEstado.Text = ok ? "✓ Enlace válido." : error;
            _lblLinkEstado.ForeColor = ok ? AppTheme.Success : AppTheme.Danger;
        }
    }

    private void BtnProbarEnlace_Click(object? s, EventArgs e)
    {
        var (ok, error, url) = PerformanceScoringService.NormalizarEnlace(_txtLink.Text);
        if (!ok) { MessageBox.Show(error, "Enlace inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (url == null) { MessageBox.Show("Todavía no capturaste un enlace.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void CriterionChanged(object? s, EventArgs e)
    {
        if (_cbxCriterion.SelectedIndex < 0 || _cbxCriterion.SelectedIndex >= _criteria.Count)
        {
            _lblPoints.Text = "—";
            _lblCriterionDesc.Text = "";
            return;
        }

        var criterio = _criteria[_cbxCriterion.SelectedIndex];
        _lblPoints.Text = $"+{criterio.DefaultPoints}";
        _lblCriterionDesc.Text = string.IsNullOrWhiteSpace(criterio.Description)
            ? "(esta actividad no tiene descripción; pídesela al líder)"
            : criterio.Description;
        _lblCriterionDesc.ForeColor = string.IsNullOrWhiteSpace(criterio.Description)
            ? AppTheme.Warning : AppTheme.TextSecondary;
        // La descripción completa en el tooltip, por si no cupo en las dos líneas.
        _tip.SetToolTip(_lblCriterionDesc, criterio.Description ?? "");

        if (!criterio.IsActive)
        {
            _lblCriterionDesc.Text = "⚠ Esta actividad ya no está disponible: elige otra para poder guardar.\n" + _lblCriterionDesc.Text;
            _lblCriterionDesc.ForeColor = AppTheme.Danger;
        }
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
        { MessageBox.Show("Elige qué hiciste.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        // Se valida aquí además de en el servicio para no cerrar el formulario y perder lo escrito.
        var (enlaceOk, enlaceError, enlace) = PerformanceScoringService.NormalizarEnlace(_txtLink.Text);
        if (!enlaceOk)
        {
            MessageBox.Show(enlaceError, "Enlace inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtLink.Focus();
            return;
        }

        int minutos = (int)_nudHoras.Value * 60 + (int)_nudMinutos.Value;
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
            MinutesSpent = minutos > 0 ? minutos : null,
            EvidenceUrl = enlace,
            Screenshot = _screenshot,
            ScreenshotFileName = _screenshotName
        };
        DialogResult = DialogResult.OK; Close();
    }
}
