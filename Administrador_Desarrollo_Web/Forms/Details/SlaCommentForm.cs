namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Redacción del comentario que se publicará en el work item de Azure DevOps. El texto lo escribe
/// la persona: se ofrece un borrador con el tiempo dedicado, pero nadie publica en el ticket del
/// cliente sin haberlo leído.
/// </summary>
public class SlaCommentForm : ResponsiveForm
{
    private TextBox _txt = null!;
    private Label _lblAttach = null!;
    private readonly List<(byte[] bytes, string fileName)> _evidencias = [];

    public string Comentario => _txt.Text.Trim();

    /// <summary>Capturas/imágenes que se adjuntarán como evidencia en el comentario de DevOps.</summary>
    public IReadOnlyList<(byte[] bytes, string fileName)> Evidencias => _evidencias;

    public SlaCommentForm(string objetivo, int ticketId, string borrador)
    {
        BuildUI(objetivo, ticketId, borrador);
    }

    private void BuildUI(string objetivo, int ticketId, string borrador)
    {
        Text = $"Comentar el ticket #{ticketId}";
        Size = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = $"  💬  Comentario para el work item #{ticketId}",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 8) };
        body.Controls.Add(new Label
        {
            Text = objetivo, Dock = DockStyle.Top, Height = 22,
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, AutoEllipsis = true
        });
        body.Controls.Add(new Label
        {
            Text = "Se publicará tal cual en Azure DevOps y quedará registrado como constancia del SLA.",
            Dock = DockStyle.Top, Height = 30, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        // Fila de evidencias (capturas): adjuntar archivo o pegar del portapapeles.
        var pnlAdj = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAttach = AppTheme.MakeSecondaryButton("📎  Imagen…", 110);
        btnAttach.Location = new Point(0, 4);
        btnAttach.Click += (_, _) => AgregarDesdeArchivo();
        var btnPaste = AppTheme.MakeSecondaryButton("📋  Pegar captura", 130);
        btnPaste.Location = new Point(116, 4);
        btnPaste.Click += (_, _) => AgregarDesdePortapapeles();
        _lblAttach = new Label { Location = new Point(256, 10), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont };
        _lblAttach.Click += (_, _) => { if (_evidencias.Count > 0) { _evidencias.Clear(); ActualizarAdjuntos(); } };
        pnlAdj.Controls.AddRange([btnAttach, btnPaste, _lblAttach]);
        body.Controls.Add(pnlAdj);

        _txt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = borrador };
        body.Controls.Add(_txt);
        // El orden de Dock es inverso al de inserción: el fill se agrega primero visualmente.
        _txt.BringToFront();
        ActualizarAdjuntos();

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Publicar en DevOps", 180);
        btnOk.Click += (_, _) =>
        {
            if (Comentario.Length == 0 && _evidencias.Count == 0)
            {
                MessageBox.Show("Escribe el comentario o adjunta una evidencia.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txt.Focus();
                return;
            }
            DialogResult = DialogResult.OK; Close();
        };
        btns.Controls.AddRange([btnCancel, btnOk]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnOk;
    }

    private void ActualizarAdjuntos()
    {
        _lblAttach.Text = _evidencias.Count == 0
            ? "Sin evidencias adjuntas."
            : $"📎 {_evidencias.Count} evidencia(s): {string.Join(", ", _evidencias.Select(e => e.fileName))}  (clic para quitar)";
    }

    private void AgregarDesdeArchivo()
    {
        using var ofd = new OpenFileDialog
        {
            Multiselect = true, Title = "Adjuntar evidencia",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Todos los archivos|*.*"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in ofd.FileNames)
            _evidencias.Add((System.IO.File.ReadAllBytes(f), System.IO.Path.GetFileName(f)));
        ActualizarAdjuntos();
    }

    private void AgregarDesdePortapapeles()
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show("No hay una imagen en el portapapeles.\nToma una captura (Win + Shift + S) y vuelve a pulsar «Pegar captura».",
                "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var img = Clipboard.GetImage();
        if (img == null)
        {
            MessageBox.Show("No se pudo leer la imagen del portapapeles. Vuelve a copiarla e inténtalo de nuevo.",
                "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var ms = new System.IO.MemoryStream();
        img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        _evidencias.Add((ms.ToArray(), $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
        ActualizarAdjuntos();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _txt.Focus();
        _txt.SelectionStart = _txt.TextLength;   // cursor al final del borrador
    }
}
