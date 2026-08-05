using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Toda la evidencia de una actividad autocalificada, en una sola pantalla: criterio y qué
/// significa, período, tiempo dedicado, comentario del desarrollador, enlace al item de DevOps y
/// captura de pantalla.
///
/// Antes el administrador solo podía abrir la captura, y el comentario se leía truncado en una
/// celda de la reja: para revisar algo había que adivinar el resto. Aprobar o rechazar sin ver la
/// evidencia completa era la norma, no la excepción.
/// </summary>
public class PointEntryEvidenceForm : ResponsiveForm
{
    private readonly PointEntry _entrada;
    private readonly ToolTip _tip = new();

    /// <summary>Null cuando la entrada no trae captura: ahí no se pinta el recuadro.</summary>
    private PictureBox? _picShot;

    public PointEntryEvidenceForm(PointEntry entrada)
    {
        _entrada = entrada;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Evidencia de la actividad";
        Size = new Size(720, 780);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  🔍  {_entrada.Developer?.FullName ?? "Desarrollador"} — {EstadoTexto(_entrada.ApprovalStatus)}",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 12, 20, 12), BackColor = AppTheme.ContentBg };
        int y = 8;

        y = AgregarCampo(body, y, "Criterio", $"{_entrada.Criterion?.Name ?? "—"}   ({(_entrada.Points >= 0 ? "+" : "")}{_entrada.Points} pts)", AppTheme.TextPrimary, negrita: true);

        // La descripción del criterio es lo que permite juzgar si la actividad encaja; el jefe la
        // escribió una vez y hasta ahora no se veía en la revisión.
        y = AgregarCampo(body, y, "Qué registra ese criterio",
            string.IsNullOrWhiteSpace(_entrada.Criterion?.Description)
                ? "(sin descripción configurada)"
                : _entrada.Criterion!.Description!,
            AppTheme.TextSecondary, alto: 46);

        y = AgregarCampo(body, y, "Período", $"{MesNombre(_entrada.Month)} {_entrada.Year}", AppTheme.TextPrimary);
        y = AgregarCampo(body, y, "Registrada el", _entrada.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), AppTheme.TextPrimary);

        y = AgregarCampo(body, y, "Tiempo dedicado (declarado)",
            _entrada.MinutesSpent is int m && m > 0 ? FormatoMinutos(m) : "— no lo capturó —",
            _entrada.MinutesSpent is > 0 ? AppTheme.Success : AppTheme.TextSecondary, negrita: _entrada.MinutesSpent is > 0);

        y = AgregarCampo(body, y, "Requerimiento vinculado",
            _entrada.Requirement != null ? $"#{_entrada.Requirement.Id}  {_entrada.Requirement.Title}" : "— ninguno —",
            _entrada.Requirement != null ? AppTheme.TextPrimary : AppTheme.TextSecondary);

        // ── Comentario ────────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Comentario del desarrollador", Location = new Point(0, y), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        y += 20;
        var txtComentario = new TextBox
        {
            Location = new Point(0, y), Size = new Size(650, 90),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(_entrada.Comment) ? "(sin comentario)" : _entrada.Comment
        };
        if (string.IsNullOrWhiteSpace(_entrada.Comment)) txtComentario.ForeColor = AppTheme.TextSecondary;
        body.Controls.Add(txtComentario); y += 100;

        // ── Ida y vuelta, si la entrada se replicó ─────────────────────────
        // Solo aparece cuando hubo discusión. En una entrada normal sería una caja vacía ocupando
        // el sitio de la captura, que es lo que sí se viene a ver.
        if (!string.IsNullOrWhiteSpace(_entrada.ReviewHistory))
        {
            body.Controls.Add(new Label
            {
                Text = $"Historial de la revisión  —  ha ido y vuelto {_entrada.ReviewRound} vez/veces",
                Location = new Point(0, y), AutoSize = true, ForeColor = AppTheme.Warning, Font = AppTheme.SmallFont
            });
            y += 20;
            body.Controls.Add(new TextBox
            {
                Location = new Point(0, y), Size = new Size(650, 110),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
                Text = _entrada.ReviewHistory!.Replace("\n", Environment.NewLine)
            });
            y += 120;
        }

        // ── Enlace al item ────────────────────────────────────────────────
        body.Controls.Add(new Label { Text = "Enlace al item de DevOps (PR / work item / ticket)", Location = new Point(0, y), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        y += 20;
        bool hayEnlace = !string.IsNullOrWhiteSpace(_entrada.EvidenceUrl);
        var txtEnlace = new TextBox
        {
            Location = new Point(0, y), Size = new Size(530, 24), ReadOnly = true,
            BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
            // Seleccionable a propósito: si el navegador falla, el jefe copia la URL a mano.
            Text = hayEnlace ? _entrada.EvidenceUrl! : "(no capturó enlace)"
        };
        if (!hayEnlace) txtEnlace.ForeColor = AppTheme.TextSecondary;
        var btnAbrirEnlace = AppTheme.MakeSecondaryButton("↗ Abrir", 105);
        btnAbrirEnlace.Location = new Point(542, y - 2);
        btnAbrirEnlace.Enabled = hayEnlace;
        btnAbrirEnlace.Click += (_, _) => AbrirEnlace();
        body.Controls.AddRange([txtEnlace, btnAbrirEnlace]); y += 36;

        // ── Captura ───────────────────────────────────────────────────────
        bool hayCaptura = _entrada.Screenshot is { Length: > 0 };
        body.Controls.Add(new Label
        {
            Text = hayCaptura ? $"Captura de pantalla — {_entrada.ScreenshotFileName ?? "captura"}" : "Captura de pantalla",
            Location = new Point(0, y), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 20;

        // Sin captura NO se pinta el recuadro: un rectángulo blanco vacío se lee como «la imagen no
        // cargó». Un aviso de una línea dice lo que pasa y deja sitio a lo demás.
        if (hayCaptura)
        {
            _picShot = new PictureBox
            {
                Location = new Point(0, y), Size = new Size(650, 260),
                SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, Cursor = Cursors.Hand
            };
            try { using var ms = new MemoryStream(_entrada.Screenshot!); _picShot.Image = new Bitmap(ms); }
            catch { /* imagen corrupta: queda el recuadro y los botones siguen sirviendo */ }
            _picShot.Click += (_, _) => AbrirCaptura();
            _tip.SetToolTip(_picShot, "Clic para abrirla a tamaño completo");
            body.Controls.Add(_picShot);
            y += 268;
        }
        else
        {
            body.Controls.Add(new Label
            {
                Text = "— esta actividad no trae captura —", Location = new Point(0, y),
                AutoSize = true, ForeColor = AppTheme.TextSecondary
            });
            y += 30;
        }

        body.AutoScrollMinSize = new Size(0, y + 10);

        // ── Botones ───────────────────────────────────────────────────────
        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCerrar = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var btnGuardar = AppTheme.MakeSecondaryButton("💾 Guardar captura", 170);
        btnGuardar.Enabled = hayCaptura; btnGuardar.Click += (_, _) => GuardarCaptura();
        var btnAbrirShot = AppTheme.MakeSecondaryButton("📷 Abrir captura", 160);
        btnAbrirShot.Enabled = hayCaptura; btnAbrirShot.Click += (_, _) => AbrirCaptura();
        btnsPnl.Controls.AddRange([btnCerrar, btnGuardar, btnAbrirShot]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnCerrar;
    }

    /// <summary>Una etiqueta pequeña arriba y el valor debajo. Devuelve la siguiente Y libre.</summary>
    private static int AgregarCampo(Control padre, int y, string etiqueta, string valor, Color color,
                                    bool negrita = false, int alto = 22)
    {
        padre.Controls.Add(new Label { Text = etiqueta, Location = new Point(0, y), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        y += 18;
        padre.Controls.Add(new Label
        {
            Text = valor, Location = new Point(0, y), AutoSize = false, Size = new Size(650, alto),
            ForeColor = color, Font = negrita ? AppTheme.BoldFont : AppTheme.DefaultFont
        });
        return y + alto + 8;
    }

    private void AbrirEnlace()
    {
        if (string.IsNullOrWhiteSpace(_entrada.EvidenceUrl)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_entrada.EvidenceUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AbrirCaptura()
    {
        if (_entrada.Screenshot is not { Length: > 0 }) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, NombreSeguro());
            File.WriteAllBytes(path, _entrada.Screenshot!);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void GuardarCaptura()
    {
        if (_entrada.Screenshot is not { Length: > 0 }) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Guardar captura", FileName = NombreSeguro(),
            Filter = "Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dlg.FileName, _entrada.Screenshot!); }
        catch (Exception ex) { MessageBox.Show($"No se pudo guardar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>El nombre viene de lo que subió el desarrollador: se limpia antes de tocar el disco.</summary>
    private string NombreSeguro()
    {
        var name = string.IsNullOrWhiteSpace(_entrada.ScreenshotFileName) ? "captura.png" : _entrada.ScreenshotFileName!;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string FormatoMinutos(int minutos) =>
        minutos >= 60 ? $"{minutos / 60}h {minutos % 60:00}m  ({minutos} minutos)" : $"{minutos} minutos";

    private static string MesNombre(int mes) =>
        mes is >= 1 and <= 12
            ? new[] { "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" }[mes - 1]
            : mes.ToString();

    private static string EstadoTexto(PointApprovalStatus s) => s switch
    {
        PointApprovalStatus.Aprobado  => "✅ Aprobado",
        PointApprovalStatus.Rechazado => "❌ Rechazado",
        _                             => "⏳ Pendiente de aprobación"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _picShot?.Image?.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
