using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Todo lo que se sabe de una actividad libre en una sola pantalla: su descripción completa, cómo
/// se acumuló el tiempo y la evidencia que la respalda.
///
/// Existe por las dos puntas del mismo problema. El administrador solo veía el título y una
/// descripción truncada en una celda, así que «¿en qué se fue ese tiempo?» se contestaba a medias.
/// Y el desarrollador no tenía dónde dejar la captura o el documento que justifica lo que hizo, de
/// modo que la justificación vivía en un chat o no existía.
///
/// La evidencia se lista SIN su contenido (<see cref="DevActivityService.EvidenciasDe"/> no trae
/// los bytes); el archivo solo viaja cuando alguien lo abre o lo guarda. Con capturas de varios MB
/// por actividad, traerlas todas al abrir la ficha se notaría en cada consulta.
/// </summary>
public class DevActivityDetailForm : ResponsiveForm
{
    private readonly DevActivityService _activities;
    private readonly WorkSessionService _work;
    private readonly DevActivity _actividad;
    private readonly bool _soloLectura;

    private ListView _lstEvidencias = null!;
    private PictureBox _preview = null!;
    private Label _lblPreview = null!;
    private Button _btnAdjuntar = null!, _btnPegar = null!, _btnAbrir = null!, _btnGuardar = null!, _btnQuitar = null!;
    private List<DevActivityAttachment> _evidencias = [];

    public DevActivityDetailForm(DevActivityService activities, WorkSessionService work,
                                 DevActivity actividad, bool soloLectura)
    {
        _activities = activities; _work = work; _actividad = actividad; _soloLectura = soloLectura;
        BuildUI();
        CargarEvidencias();
    }

    private void BuildUI()
    {
        Text = $"Actividad #{_actividad.Id} — {_actividad.Title}";
        Size = new Size(940, 760);
        MinimumSize = new Size(760, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  🧩  {_actividad.Title}",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        });

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };
        tabs.TabPages.Add(TabResumen());
        tabs.TabPages.Add(TabEvidencia());
        tabs.TabPages.Add(TabSesiones());

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCerrar = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        btns.Controls.Add(btnCerrar);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(tabs, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnCerrar;
    }

    // ── Resumen ──────────────────────────────────────────────────────────────

    private TabPage TabResumen()
    {
        var tab = new TabPage("  📄  Resumen  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(16, 12, 16, 12) };
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.ContentBg };

        int y = 4;
        const int ancho = 850;

        y = Campo(body, y, "Desarrollador", _actividad.Developer?.FullName ?? "—", ancho);
        y = Campo(body, y, "Estado",
            _actividad.Status == DevActivityStatus.Abierta ? "🟢 Abierta" : "⚪ Cerrada", ancho,
            _actividad.Status == DevActivityStatus.Abierta ? AppTheme.Success : AppTheme.TextSecondary);
        y = Campo(body, y, "Creada", _actividad.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), ancho);
        y = Campo(body, y, "Cerrada",
            _actividad.ClosedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "— sigue abierta —", ancho);

        int segundos = _work.GetTotalSecondsByActivity(_actividad.Id);
        y = Campo(body, y, "Tiempo dedicado (cronómetro)", WorkSessionService.Format(segundos), ancho,
            segundos > 0 ? AppTheme.Success : AppTheme.TextSecondary, negrita: segundos > 0);

        // La descripción es justo lo que la rejilla del administrador recortaba. Aquí va entera y
        // seleccionable, que es lo que permite copiarla a un correo o a una minuta.
        body.Controls.Add(new Label
        {
            Text = "Descripción", Location = new Point(0, y), AutoSize = true,
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 20;
        var txtDesc = new TextBox
        {
            Location = new Point(0, y), Size = new Size(ancho, 220),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(_actividad.Description)
                ? "(esta actividad no tiene descripción)"
                : _actividad.Description
        };
        if (string.IsNullOrWhiteSpace(_actividad.Description)) txtDesc.ForeColor = AppTheme.TextSecondary;
        body.Controls.Add(txtDesc);
        y += 232;

        body.AutoScrollMinSize = new Size(0, y);
        tab.Controls.Add(body);
        return tab;
    }

    private static int Campo(Control padre, int y, string etiqueta, string valor, int ancho,
                             Color? color = null, bool negrita = false)
    {
        padre.Controls.Add(new Label
        {
            Text = etiqueta, Location = new Point(0, y), AutoSize = true,
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 18;
        padre.Controls.Add(new Label
        {
            Text = valor, Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 22),
            ForeColor = color ?? AppTheme.TextPrimary,
            Font = negrita ? AppTheme.BoldFont : AppTheme.DefaultFont
        });
        return y + 30;
    }

    // ── Evidencia ────────────────────────────────────────────────────────────

    private TabPage TabEvidencia()
    {
        var tab = new TabPage("  📎  Evidencia  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(12, 10, 12, 10) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var barra = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _btnAdjuntar = AppTheme.MakeSecondaryButton("📎 Adjuntar archivo", 165, 28);
        _btnAdjuntar.Click += BtnAdjuntar_Click;
        _btnPegar = AppTheme.MakeSecondaryButton("📋 Pegar captura", 150, 28);
        _btnPegar.Click += BtnPegar_Click;
        _btnAbrir = AppTheme.MakeSecondaryButton("👁 Abrir", 95, 28);
        _btnAbrir.Click += (_, _) => AbrirSeleccionada();
        _btnGuardar = AppTheme.MakeSecondaryButton("💾 Guardar como", 155, 28);
        _btnGuardar.Click += (_, _) => GuardarSeleccionada();
        _btnQuitar = AppTheme.MakeDangerButton("🗑 Quitar", 105, 28);
        _btnQuitar.Click += (_, _) => QuitarSeleccionada();

        foreach (var b in new[] { _btnAdjuntar, _btnPegar, _btnAbrir, _btnGuardar, _btnQuitar })
            b.Margin = new Padding(0, 4, 8, 0);

        // Al administrador que mira una actividad ajena se le deja consultar y llevarse el archivo,
        // no cambiar la evidencia de otro: eso es del dueño de la actividad.
        _btnAdjuntar.Visible = _btnPegar.Visible = _btnQuitar.Visible = !_soloLectura;

        barra.Controls.AddRange([_btnAdjuntar, _btnPegar, _btnAbrir, _btnGuardar, _btnQuitar]);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _lstEvidencias = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.FixedSingle
        };
        _lstEvidencias.Columns.Add("Archivo", 230);
        _lstEvidencias.Columns.Add("Tamaño", 80, HorizontalAlignment.Right);
        _lstEvidencias.Columns.Add("Nota", 180);
        _lstEvidencias.Columns.Add("Adjuntada", 120);
        _lstEvidencias.SelectedIndexChanged += (_, _) => PintarPreview();
        _lstEvidencias.DoubleClick += (_, _) => AbrirSeleccionada();

        var pnlPreview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0), BackColor = AppTheme.ContentBg };
        _preview = new PictureBox
        {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White
        };
        _lblPreview = new Label
        {
            Dock = DockStyle.Bottom, Height = 40, ForeColor = AppTheme.TextSecondary,
            Font = AppTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft
        };
        pnlPreview.Controls.Add(_preview);
        pnlPreview.Controls.Add(_lblPreview);

        split.Controls.Add(_lstEvidencias, 0, 0);
        split.Controls.Add(pnlPreview,     1, 0);

        var pie = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = _soloLectura
                ? "Evidencia que adjuntó el desarrollador para justificar la actividad."
                : $"Hasta {DevActivityService.MaxEvidenciasPorActividad} archivos de {DevActivityService.MaxEvidenciaBytes / (1024 * 1024)} MB. " +
                  "Win+Shift+S recorta la pantalla y «📋 Pegar captura» la adjunta."
        };

        tbl.Controls.Add(barra, 0, 0);
        tbl.Controls.Add(split, 0, 1);
        tbl.Controls.Add(pie,   0, 2);
        tab.Controls.Add(tbl);
        return tab;
    }

    private void CargarEvidencias()
    {
        try { _evidencias = _activities.EvidenciasDe(_actividad.Id); }
        catch (AuthorizationException) { _evidencias = []; }

        _lstEvidencias.BeginUpdate();
        _lstEvidencias.Items.Clear();
        foreach (var ev in _evidencias)
        {
            var item = new ListViewItem((ev.EsImagen ? "🖼 " : "📄 ") + ev.FileName) { Tag = ev.Id };
            item.SubItems.Add(FormatoTamaño(ev.SizeBytes));
            item.SubItems.Add(ev.Description ?? "");
            item.SubItems.Add(ev.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            _lstEvidencias.Items.Add(item);
        }
        _lstEvidencias.EndUpdate();

        if (_lstEvidencias.Items.Count > 0) _lstEvidencias.Items[0].Selected = true;
        PintarPreview();
    }

    private static string FormatoTamaño(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private DevActivityAttachment? Seleccionada()
    {
        if (_lstEvidencias.SelectedItems.Count == 0) return null;
        var id = (int)_lstEvidencias.SelectedItems[0].Tag!;
        return _evidencias.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>
    /// Previsualiza la imagen seleccionada. Solo aquí se piden los bytes, y solo de UNA: cargar
    /// todas al abrir la ficha traería decenas de MB por la red para enseñar un recuadro.
    /// </summary>
    private void PintarPreview()
    {
        _preview.Image?.Dispose();
        _preview.Image = null;

        var ev = Seleccionada();
        bool hay = ev != null;
        _btnAbrir.Enabled = _btnGuardar.Enabled = hay;
        _btnQuitar.Enabled = hay && !_soloLectura;

        if (ev == null)
        {
            _lblPreview.Text = _evidencias.Count == 0
                ? "Esta actividad todavía no tiene evidencia."
                : "Selecciona un archivo de la lista.";
            return;
        }

        _lblPreview.Text = $"{ev.FileName}\n{FormatoTamaño(ev.SizeBytes)} · {ev.ContentType}";

        if (!ev.EsImagen) return;
        try
        {
            var (bytes, _, _) = _activities.BytesDeEvidencia(ev.Id);
            if (bytes.Length == 0) return;
            using var ms = new MemoryStream(bytes);
            _preview.Image = new Bitmap(ms);
        }
        catch { /* imagen ilegible: se queda el recuadro vacío y los botones siguen sirviendo */ }
    }

    private void BtnAdjuntar_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Adjuntar evidencia",
            Filter = "Imágenes y documentos (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.pdf;*.docx;*.doc;*.xlsx;*.txt)" +
                     "|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.pdf;*.docx;*.doc;*.xlsx;*.txt|Todos los archivos (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var errores = new List<string>();
        foreach (var ruta in dlg.FileNames)
        {
            try
            {
                var bytes = File.ReadAllBytes(ruta);
                var (ok, mensaje, _) = _activities.AgregarEvidencia(_actividad.Id, Path.GetFileName(ruta), bytes);
                if (!ok) errores.Add($"{Path.GetFileName(ruta)}: {mensaje}");
            }
            catch (AuthorizationException ex) { errores.Add(ex.Message); break; }
            catch (Exception ex) { errores.Add($"{Path.GetFileName(ruta)}: {ex.Message}"); }
        }

        CargarEvidencias();
        if (errores.Count > 0)
            MessageBox.Show("No se pudo adjuntar:\n\n" + string.Join("\n", errores),
                "Con incidencias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

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

            var (ok, mensaje, _) = _activities.AgregarEvidencia(
                _actividad.Id, $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png", ms.ToArray());
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            CargarEvidencias();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show($"No se pudo pegar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AbrirSeleccionada()
    {
        var ev = Seleccionada();
        if (ev == null) return;
        try
        {
            var (bytes, nombre, _) = _activities.BytesDeEvidencia(ev.Id);
            if (bytes.Length == 0)
            { MessageBox.Show("Ese archivo ya no está disponible.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            var dir = Path.Combine(Path.GetTempPath(), "advweb_evidencia_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, nombre);
            File.WriteAllBytes(path, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void GuardarSeleccionada()
    {
        var ev = Seleccionada();
        if (ev == null) return;
        try
        {
            var (bytes, nombre, _) = _activities.BytesDeEvidencia(ev.Id);
            if (bytes.Length == 0)
            { MessageBox.Show("Ese archivo ya no está disponible.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            using var dlg = new SaveFileDialog { Title = "Guardar evidencia", FileName = nombre, Filter = "Todos los archivos (*.*)|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dlg.FileName, bytes);
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show($"No se pudo guardar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void QuitarSeleccionada()
    {
        var ev = Seleccionada();
        if (ev == null) return;

        if (MessageBox.Show($"¿Quitar «{ev.FileName}» de la evidencia?\n\nNo se puede deshacer.",
                "Quitar evidencia", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, mensaje) = _activities.EliminarEvidencia(ev.Id);
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            CargarEvidencias();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    // ── Sesiones ─────────────────────────────────────────────────────────────

    private TabPage TabSesiones()
    {
        var tab = new TabPage("  🕑  Sesiones  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(12, 10, 12, 10) };

        var grid = AppTheme.MakeGrid();
        grid.MultiSelect = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Inicio", Name = "Ini",  FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fin",    Name = "Fin",  FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración", Name = "Dur", FillWeight = 25 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Est",  FillWeight = 15 });

        List<WorkSession> sesiones;
        try { sesiones = _activities.SesionesDe(_actividad.Id); }
        catch (AuthorizationException) { sesiones = []; }

        var ahora = DateTime.UtcNow;
        foreach (var w in sesiones)
            grid.Rows.Add(
                w.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                w.EndedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "— en curso —",
                WorkSessionService.Format(w.LiveSeconds(ahora)),
                w.Status.ToString());

        if (sesiones.Count == 0) grid.Rows.Add("(sin tiempo registrado todavía)", "", "", "");

        var pie = new Label
        {
            Dock = DockStyle.Bottom, Height = 24, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Total: {WorkSessionService.Format(_work.GetTotalSecondsByActivity(_actividad.Id))} en {sesiones.Count} sesión(es)."
        };

        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(grid);
        pnl.Controls.Add(pie);
        tab.Controls.Add(pnl);
        return tab;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview?.Image?.Dispose();
        base.Dispose(disposing);
    }
}
