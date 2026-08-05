using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Redactar una publicación del foro, o editar una entrada propia. Trabaja sobre valores sueltos,
/// nunca sobre la entidad rastreada: el AppDbContext es un singleton compartido y editar aquí una
/// entidad viva escribiría en la base antes de pulsar Guardar.
///
/// Lo mismo vale para las imágenes: las que se adjuntan y las que se marcan para quitar se apuntan
/// aquí y no se tocan en la base hasta que quien llama pasa <see cref="ImagenesNuevas"/> y
/// <see cref="QuitarImagenes"/> al servicio. Cancelar tiene que dejar el hilo exactamente como
/// estaba.
/// </summary>
public class ForumPostForm : ResponsiveForm
{
    private ComboBox _cbxTema = null!;
    private TextBox _txtTitulo = null!, _txtEtiquetas = null!, _txtCuerpo = null!;
    private FlowLayoutPanel _pnlImagenes = null!;
    private Label _lblImagenes = null!;

    private readonly List<ForumImagen> _existentes = [];
    private readonly List<ForumImagenNueva> _nuevas = [];
    private readonly List<int> _quitar = [];

    private static readonly ForumTopic[] Temas = Enum.GetValues<ForumTopic>();

    public ForumTopic Tema => Temas[Math.Max(0, _cbxTema.SelectedIndex)];
    public string Titulo => _txtTitulo.Text.Trim();
    public string Cuerpo => _txtCuerpo.Text.Trim();
    public string Etiquetas => _txtEtiquetas.Text.Trim();

    /// <summary>Las imágenes que se adjuntaron en esta sesión de edición.</summary>
    public IReadOnlyList<ForumImagenNueva> ImagenesNuevas => _nuevas;

    /// <summary>Los ids de las imágenes que ya estaban y se marcaron para quitar.</summary>
    public IReadOnlyList<int> QuitarImagenes => _quitar;

    /// <summary>Publicación nueva.</summary>
    public ForumPostForm() => BuildUI(null);

    /// <summary>Edición de una entrada existente (publicación o comentario), con lo que ya llevaba.</summary>
    public ForumPostForm(ForumPost editar, IEnumerable<ForumImagen>? imagenes = null)
    {
        if (imagenes != null) _existentes.AddRange(imagenes);
        BuildUI(editar);
    }

    private int Cupo => ForumService.MaxImagenes - (_existentes.Count - _quitar.Count) - _nuevas.Count;

    private void BuildUI(ForumPost? editar)
    {
        bool esComentario = editar is { ParentId: not null };
        Text = editar == null ? "Nueva publicación" : esComentario ? "Editar comentario" : "Editar publicación";
        Size = new Size(700, esComentario ? 560 : 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, esComentario ? 0f : 120f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = esComentario ? "  💬  Editar comentario" : "  💬  Publicación del foro",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Datos de la publicación (un comentario no lleva ni título ni tema propio)
        var datos = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(14, 8, 14, 4), BackColor = AppTheme.ContentBg,
            Visible = !esComentario
        };
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        datos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 3; i++) datos.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        _cbxTema = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 0, 3) };
        foreach (var t in Temas) _cbxTema.Items.Add($"{ForumService.IconoTema(t)}  {ForumService.EtiquetaTema(t)}");
        _cbxTema.SelectedIndex = editar != null ? Math.Max(0, Array.IndexOf(Temas, editar.Topic)) : 0;

        _txtTitulo = new TextBox { Dock = DockStyle.Fill, MaxLength = ForumService.MaxTitulo, Margin = new Padding(0, 3, 0, 3) };
        if (editar?.Title is { } t0) _txtTitulo.Text = t0;

        _txtEtiquetas = new TextBox
        {
            Dock = DockStyle.Fill, MaxLength = ForumService.MaxEtiquetas, Margin = new Padding(0, 3, 0, 3),
            PlaceholderText = "sql, rendimiento, reportes"
        };
        if (editar?.Tags is { } tg) _txtEtiquetas.Text = tg;

        datos.Controls.Add(Etiqueta("Tema"),      0, 0); datos.Controls.Add(_cbxTema,      1, 0);
        datos.Controls.Add(Etiqueta("Título *"),  0, 1); datos.Controls.Add(_txtTitulo,    1, 1);
        datos.Controls.Add(Etiqueta("Etiquetas"), 0, 2); datos.Controls.Add(_txtEtiquetas, 1, 2);

        var pnlCuerpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4), BackColor = AppTheme.ContentBg };
        _txtCuerpo = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            MaxLength = ForumService.MaxCuerpo, BackColor = Color.White
        };
        if (editar != null)
            _txtCuerpo.Text = editar.Body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        // Ctrl+V con una captura en el portapapeles la adjunta en vez de no hacer nada. Es el gesto
        // natural después de Win+Shift+S y ahorra el rodeo de guardarla en el disco para subirla.
        _txtCuerpo.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.V || Clipboard.ContainsText() || !Clipboard.ContainsImage()) return;
            e.SuppressKeyPress = true;
            AdjuntarPegada();
        };

        var pistaEnlaces = new Label
        {
            Dock = DockStyle.Bottom, Height = 18, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            Text = "Las direcciones que escribas se vuelven pulsables. Para ponerles nombre: [ver el ticket](https://…)"
        };
        pnlCuerpo.Controls.Add(_txtCuerpo);
        pnlCuerpo.Controls.Add(pistaEnlaces);

        tbl.Controls.Add(hdr,             0, 0);
        tbl.Controls.Add(datos,           0, 1);
        tbl.Controls.Add(pnlCuerpo,       0, 2);
        tbl.Controls.Add(ZonaImagenes(),  0, 3);
        tbl.Controls.Add(Botones(editar, esComentario), 0, 4);
        Controls.Add(tbl);

        PintarImagenes();
    }

    private Panel ZonaImagenes()
    {
        var zona = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 2, 14, 4), BackColor = AppTheme.ContentBg };

        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg
        };

        var btnAdjuntar = AppTheme.MakeSecondaryButton("🖼 Adjuntar imagen", 165, 28);
        btnAdjuntar.Margin = new Padding(0, 2, 6, 0);
        btnAdjuntar.Click += (_, _) =>
        {
            var elegidas = ForumImagenes.Elegir(this, Cupo);
            if (elegidas.Count == 0) return;
            _nuevas.AddRange(elegidas);
            PintarImagenes();
        };

        var btnPegar = AppTheme.MakeSecondaryButton("📋 Pegar captura", 145, 28);
        btnPegar.Margin = new Padding(0, 2, 6, 0);
        btnPegar.Click += (_, _) => AdjuntarPegada();

        _lblImagenes = new Label
        {
            AutoSize = true, Margin = new Padding(4, 9, 0, 0),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        };

        barra.Controls.AddRange([btnAdjuntar, btnPegar, _lblImagenes]);

        _pnlImagenes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, BackColor = Color.White, Padding = new Padding(6), Margin = Padding.Empty
        };

        zona.Controls.Add(_pnlImagenes);
        zona.Controls.Add(barra);
        return zona;
    }

    private void AdjuntarPegada()
    {
        if (Cupo <= 0)
        {
            MessageBox.Show($"Ya hay {ForumService.MaxImagenes} imágenes en esta entrada.",
                "Sin sitio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var img = ForumImagenes.Pegar(this);
        if (img == null) return;
        _nuevas.Add(img);
        PintarImagenes();
    }

    /// <summary>
    /// Repinta la tira de miniaturas: primero las que ya estaban (menos las marcadas para quitar) y
    /// después las recién adjuntadas. Es el mismo orden en el que se verán en el hilo.
    /// </summary>
    private void PintarImagenes()
    {
        // Un PictureBox no libera su Image al destruirse y esta tira se rehace en cada adjuntar o
        // quitar; sin soltarlas a mano, editar una entrada con capturas va dejando memoria atrás.
        var previas = _pnlImagenes.Controls.Cast<Control>().ToList();
        _pnlImagenes.Controls.Clear();
        foreach (var c in previas)
        {
            foreach (var pic in c.Controls.OfType<PictureBox>()) { var i = pic.Image; pic.Image = null; i?.Dispose(); }
            c.Dispose();
        }

        foreach (var img in _existentes.Where(i => !_quitar.Contains(i.Id)))
            _pnlImagenes.Controls.Add(Miniatura(
                ForumImagenes.Cargar(img.Miniatura), img.NombreArchivo,
                () => { _quitar.Add(img.Id); PintarImagenes(); }));

        foreach (var img in _nuevas.ToList())
            _pnlImagenes.Controls.Add(Miniatura(
                ForumImagenes.Cargar(img.Miniatura.Length > 0 ? img.Miniatura : img.Bytes), img.NombreArchivo,
                () => { _nuevas.Remove(img); PintarImagenes(); }));

        int puestas = ForumService.MaxImagenes - Cupo;
        _lblImagenes.Text = puestas == 0
            ? $"Sin imágenes.  Hasta {ForumService.MaxImagenes}; también puedes pegar con Ctrl+V sobre el texto."
            : $"{puestas} de {ForumService.MaxImagenes} imagen(es).";
    }

    private static Panel Miniatura(Image? imagen, string nombre, Action quitar)
    {
        var caja = new Panel
        {
            Size = new Size(132, 122), Margin = new Padding(4),
            BackColor = AppTheme.ContentBg, BorderStyle = BorderStyle.FixedSingle
        };

        var pic = new PictureBox
        {
            Location = new Point(4, 4), Size = new Size(122, 84),
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Image = imagen
        };

        var lbl = new Label
        {
            Text = nombre, Location = new Point(4, 90), Size = new Size(88, 26),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, AutoEllipsis = true
        };

        var btn = new Button
        {
            Text = "✖", Location = new Point(94, 90), Size = new Size(32, 24),
            FlatStyle = FlatStyle.Flat, ForeColor = AppTheme.Danger, BackColor = Color.White,
            Font = AppTheme.SmallFont, FlatAppearance = { BorderSize = 0 }
        };
        btn.Click += (_, _) => quitar();

        var tip = new ToolTip();
        tip.SetToolTip(pic, nombre);
        tip.SetToolTip(lbl, nombre);
        tip.SetToolTip(btn, "Quitar esta imagen");
        caja.Disposed += (_, _) => tip.Dispose();

        caja.Controls.AddRange([pic, lbl, btn]);
        return caja;
    }

    private FlowLayoutPanel Botones(ForumPost? editar, bool esComentario)
    {
        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var guardar = AppTheme.MakePrimaryButton(editar == null ? "Publicar 💬" : "Guardar", 130);
        guardar.Click += (_, _) => Guardar(esComentario);
        btns.Controls.AddRange([cancelar, guardar]);

        CancelButton = cancelar;
        // Sin AcceptButton: el cuerpo es multilínea y Enter debe insertar un salto, no publicar.
        return btns;
    }

    private static Label Etiqueta(string texto) =>
        new() { Text = texto, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };

    private void Guardar(bool esComentario)
    {
        if (!esComentario && Titulo.Length < 3)
        {
            MessageBox.Show("Escribe un título (al menos 3 caracteres).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTitulo.Focus(); return;
        }

        // Con imágenes, el texto puede quedarse corto o vacío: una captura con su título ya dice algo.
        bool hayImagenes = ForumService.MaxImagenes - Cupo > 0;
        if (!hayImagenes && Cuerpo.Length < (esComentario ? 1 : 5))
        {
            MessageBox.Show("Escribe algo que compartir, o adjunta una imagen.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtCuerpo.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }
}
