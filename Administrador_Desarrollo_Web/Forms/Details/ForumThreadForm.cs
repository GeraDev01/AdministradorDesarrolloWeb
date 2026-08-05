using Administrador_Desarrollo_Web.Forms.Controls;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Un hilo del foro: la publicación arriba y debajo sus comentarios anidados, cada uno sangrado
/// según a qué responde. Desde aquí se comenta, se responde a un comentario concreto, se da 👍 y se
/// retira lo propio.
///
/// La sangría se corta a los cinco niveles: más allá, la conversación deja de leerse y los
/// comentarios cuelgan del último nivel en vez de irse al margen derecho.
///
/// El cuerpo se pinta con <see cref="CuerpoForo"/>, que vuelve pulsables las direcciones escritas
/// en el texto. Las imágenes se enseñan como miniaturas —el original solo se trae al abrirlas—,
/// porque un hilo con diez capturas no puede costar diez descargas cada vez que se refresca.
/// </summary>
public class ForumThreadForm : ResponsiveForm
{
    private readonly ForumService _forum;
    private readonly ICurrentUser _currentUser;
    private readonly int _rootId;

    private Panel _pnlHilo = null!;
    private TextBox _txtComentario = null!;
    private Button _btnComentar = null!, _btnImagen = null!, _btnPegar = null!;
    private Label _lblRespondiendo = null!, _lblAdjuntas = null!;
    private FlowLayoutPanel _pnlAdjuntas = null!;
    private TableLayoutPanel _filaEscribir = null!;

    /// <summary>Imágenes de cada entrada del hilo, ya con su miniatura. Se rehace en cada recarga.</summary>
    private Dictionary<int, List<ForumImagen>> _imagenes = [];

    /// <summary>Lo adjuntado al comentario que se está escribiendo; se vacía al publicarlo.</summary>
    private readonly List<ForumImagenNueva> _adjuntas = [];

    /// <summary>A qué entrada responde el comentario que se está escribiendo; la raíz por omisión.</summary>
    private int _respondiendoA;
    private string? _respondiendoAQuien;

    public ForumThreadForm(ForumService forum, ICurrentUser currentUser, int rootId)
    {
        _forum = forum; _currentUser = currentUser; _rootId = rootId;
        _respondiendoA = rootId;
        BuildUI();
        Recargar();
    }

    private void BuildUI()
    {
        Text = "Hilo del foro";
        Size = new Size(900, 760);
        MinimumSize = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 200f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _pnlHilo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(12) };

        // ── Caja de respuesta ────────────────────────────────────
        var pnlNuevo = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg, Padding = new Padding(12, 6, 12, 8) };

        _lblRespondiendo = new Label
        {
            Dock = DockStyle.Top, Height = 20, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        var fila = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
        fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        fila.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156f));
        fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // La tira de adjuntas ocupa cero mientras no haya ninguna: el cuadro de escribir se queda
        // con todo el alto y solo cede sitio cuando de verdad hay algo que enseñar.
        _filaEscribir = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 8, 0) };
        _filaEscribir.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _filaEscribir.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _filaEscribir.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));

        _txtComentario = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            MaxLength = ForumService.MaxCuerpo, Margin = Padding.Empty
        };
        // Ctrl+V con una captura en el portapapeles la adjunta: es el gesto natural tras Win+Shift+S.
        _txtComentario.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.V || Clipboard.ContainsText() || !Clipboard.ContainsImage()) return;
            e.SuppressKeyPress = true;
            PegarImagen();
        };

        _pnlAdjuntas = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = new Padding(0, 4, 0, 0),
            Visible = false
        };

        _filaEscribir.Controls.Add(_txtComentario, 0, 0);
        _filaEscribir.Controls.Add(_pnlAdjuntas,   0, 1);

        var derecha = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };

        _btnComentar = AppTheme.MakePrimaryButton("💬 Comentar", 150, 40);
        _btnComentar.Margin = new Padding(0, 0, 0, 6);
        _btnComentar.Click += (_, _) => Comentar();

        _btnImagen = AppTheme.MakeSecondaryButton("🖼 Adjuntar imagen", 150, 28);
        _btnImagen.Margin = new Padding(0, 0, 0, 4);
        _btnImagen.Click += (_, _) => AdjuntarImagen();

        _btnPegar = AppTheme.MakeSecondaryButton("📋 Pegar captura", 150, 28);
        _btnPegar.Margin = new Padding(0, 0, 0, 4);
        _btnPegar.Click += (_, _) => PegarImagen();

        _lblAdjuntas = new Label
        {
            AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            Margin = new Padding(2, 2, 0, 0)
        };

        derecha.Controls.AddRange([_btnComentar, _btnImagen, _btnPegar, _lblAdjuntas]);

        fila.Controls.Add(_filaEscribir, 0, 0);
        fila.Controls.Add(derecha,       1, 0);

        pnlNuevo.Controls.Add(fila);
        pnlNuevo.Controls.Add(_lblRespondiendo);

        tbl.Controls.Add(_pnlHilo, 0, 0);
        tbl.Controls.Add(pnlNuevo, 0, 1);
        Controls.Add(tbl);

        PintarAdjuntas();
    }

    private void Recargar()
    {
        List<ForumNodo> nodos;
        try
        {
            nodos = _forum.Hilo(_rootId);
            _imagenes = nodos.Count == 0 ? [] : _forum.ImagenesDe(nodos.Select(n => n.Post.Id).ToList());
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close(); return;
        }

        Vaciar(_pnlHilo);
        if (nodos.Count == 0) { Close(); return; }

        var raiz = nodos[0].Post;
        Text = $"💬 {raiz.Title}";
        bool cerrado = raiz.Locked;

        _txtComentario.Enabled = _btnComentar.Enabled = _btnImagen.Enabled = _btnPegar.Enabled = !cerrado;
        PintarRespondiendo(cerrado);

        int y = 0;
        var ahora = DateTime.UtcNow;
        foreach (var nodo in nodos)
        {
            // El ancho se decide ANTES de construir la tarjeta: el cuerpo y las imágenes se ajustan
            // a él. Fijándolo después, el texto quedaba envuelto al ancho por omisión de un Panel
            // (200 px) y se leía en una columna estrecha por muy ancha que estuviera la ventana.
            int ancho = Math.Max(260, _pnlHilo.ClientSize.Width - 30 - nodo.Nivel * 26);
            var tarjeta = ConstruirEntrada(nodo, ahora, cerrado, ancho);
            tarjeta.Location = new Point(nodo.Nivel * 26, y);
            tarjeta.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _pnlHilo.Controls.Add(tarjeta);
            y += tarjeta.Height + 8;
        }
        _pnlHilo.AutoScrollMinSize = new Size(0, y + 12);
    }

    private Panel ConstruirEntrada(ForumNodo nodo, DateTime ahora, bool hiloCerrado, int ancho)
    {
        var p = nodo.Post;
        bool esRaiz = nodo.Nivel == 0;
        bool mia = p.AuthorUserId == _currentUser.UserId;

        var caja = new Panel
        {
            Width = ancho,
            BackColor = esRaiz ? Color.FromArgb(239, 246, 255) : Color.FromArgb(247, 249, 251),
            Padding = new Padding(12), AutoSize = false
        };

        int y = 10;

        if (esRaiz && !string.IsNullOrWhiteSpace(p.Title))
        {
            caja.Controls.Add(new Label
            {
                Text = $"{ForumService.IconoTema(p.Topic)}  {p.Title}",
                Location = new Point(12, y), AutoSize = false, Width = ancho - 24, Height = 24,
                Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });
            y += 28;
        }

        var meta = $"🧑 {p.AuthorName}   ·   {ForumFilter.HaceCuanto(p.CreatedAtUtc, ahora)}";
        if (p.EditedAtUtc != null) meta += "   ·   (editado)";
        if (esRaiz && p.Pinned) meta += "   ·   📌 fijada";
        if (esRaiz && hiloCerrado) meta += "   ·   🔒 cerrado";
        if (!string.IsNullOrWhiteSpace(p.Tags)) meta += $"   ·   {p.Tags}";

        caja.Controls.Add(new Label
        {
            Text = meta, Location = new Point(12, y), AutoSize = true,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 22;

        // Una entrada puede ser solo una imagen: sin texto no se pinta el hueco de un párrafo vacío.
        if (!string.IsNullOrWhiteSpace(p.TextoVisible))
        {
            var cuerpo = CuerpoForo.Crear(
                p.TextoVisible, ancho - 30,
                p.Eliminado ? AppTheme.SmallFont : AppTheme.DefaultFont,
                p.Eliminado ? AppTheme.TextSecondary : AppTheme.TextPrimary);
            cuerpo.Location = new Point(12, y);
            caja.Controls.Add(cuerpo);
            y = cuerpo.Bottom + 8;
        }

        if (_imagenes.TryGetValue(p.Id, out var imgs) && imgs.Count > 0)
        {
            var tira = TiraDeImagenes(imgs, ancho - 30);
            tira.Location = new Point(12, y);
            caja.Controls.Add(tira);
            y = tira.Bottom + 8;
        }

        // Acciones
        var acciones = new FlowLayoutPanel
        {
            Location = new Point(8, y), AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent
        };

        if (!p.Eliminado)
        {
            var like = AppTheme.MakeSecondaryButton(
                nodo.YoDiMeGusta ? $"❤ {nodo.MeGusta}" : $"♡ {nodo.MeGusta}", 70, 26);
            like.Margin = new Padding(0, 0, 6, 0);
            like.ForeColor = nodo.YoDiMeGusta ? AppTheme.Danger : AppTheme.TextPrimary;
            like.Click += (_, _) => { _forum.MeGusta(p.Id); Recargar(); };
            acciones.Controls.Add(like);

            if (!hiloCerrado)
            {
                var responder = AppTheme.MakeSecondaryButton("↩ Responder", 110, 26);
                responder.Margin = new Padding(0, 0, 6, 0);
                responder.Click += (_, _) =>
                {
                    _respondiendoA = p.Id;
                    _respondiendoAQuien = p.AuthorName;
                    PintarRespondiendo(false);
                    _txtComentario.Focus();
                };
                acciones.Controls.Add(responder);
            }

            if (mia)
            {
                var editar = AppTheme.MakeSecondaryButton("✏ Editar", 90, 26);
                editar.Margin = new Padding(0, 0, 6, 0);
                editar.Click += (_, _) => Editar(p);
                acciones.Controls.Add(editar);
            }

            if (mia || _currentUser.IsAdmin)
            {
                var retirar = AppTheme.MakeSecondaryButton("🗑 Retirar", 95, 26);
                retirar.Margin = new Padding(0, 0, 6, 0);
                retirar.Click += (_, _) => Retirar(p);
                acciones.Controls.Add(retirar);
            }
        }

        caja.Controls.Add(acciones);
        caja.Height = acciones.Bottom + 12;
        return caja;
    }

    /// <summary>
    /// Las miniaturas de una entrada. Un clic abre el original con el visor del sistema; el menú
    /// derecho la guarda. El original NO se trae hasta ese momento.
    /// </summary>
    private FlowLayoutPanel TiraDeImagenes(List<ForumImagen> imagenes, int ancho)
    {
        const int AnchoMiniatura = 190, AltoMiniatura = 130, Separacion = 8;

        // La altura se calcula, no se deja en AutoSize: la tarjeta que contiene esto se maqueta a
        // mano con coordenadas, y ahí hace falta saber cuánto ocupa la tira ANTES de colocar los
        // botones de debajo.
        int porFila = Math.Max(1, ancho / (AnchoMiniatura + Separacion));
        int filas = (imagenes.Count + porFila - 1) / porFila;

        var tira = new FlowLayoutPanel
        {
            Width = ancho, Height = filas * (AltoMiniatura + Separacion),
            AutoSize = false, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            BackColor = Color.Transparent, Margin = Padding.Empty
        };

        var tip = new ToolTip();
        tira.Disposed += (_, _) => tip.Dispose();

        foreach (var img in imagenes)
        {
            var pic = new PictureBox
            {
                Size = new Size(AnchoMiniatura, AltoMiniatura), Margin = new Padding(0, 0, Separacion, Separacion),
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand,
                Image = ForumImagenes.Cargar(img.Miniatura)
            };
            tip.SetToolTip(pic, $"{img.NombreArchivo}  ·  {img.Ancho}×{img.Alto}  ·  {ForumMedia.Tamano(img.Bytes)}\n(clic para verla completa)");

            var menu = new ContextMenuStrip();
            menu.Items.Add("Ver completa", null, (_, _) => AbrirImagen(img));
            menu.Items.Add("Guardar como…", null, (_, _) => GuardarImagen(img));
            pic.ContextMenuStrip = menu;
            pic.Click += (_, e) => { if (e is not MouseEventArgs { Button: MouseButtons.Right }) AbrirImagen(img); };

            tira.Controls.Add(pic);
        }
        return tira;
    }

    private void AbrirImagen(ForumImagen img)
    {
        var (bytes, nombre, tipo) = _forum.BytesDeImagen(img.Id);
        ForumImagenes.Abrir(bytes, string.IsNullOrWhiteSpace(nombre) ? img.NombreArchivo : nombre, tipo, this);
    }

    private void GuardarImagen(ForumImagen img)
    {
        var (bytes, nombre, tipo) = _forum.BytesDeImagen(img.Id);
        if (bytes.Length == 0)
        {
            MessageBox.Show("Esa imagen ya no está disponible.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ForumImagenes.GuardarComo(bytes, string.IsNullOrWhiteSpace(nombre) ? img.NombreArchivo : nombre, tipo, this);
    }

    /// <summary>
    /// Vacía un panel soltando lo que WinForms no suelta solo: un PictureBox no libera su Image al
    /// destruirse, y aquí el hilo entero se vuelve a pintar en cada 👍 y en cada comentario. Sin
    /// esto, un hilo con capturas va dejando megas por el camino en cada refresco.
    /// </summary>
    private static void Vaciar(Control contenedor)
    {
        var hijos = contenedor.Controls.Cast<Control>().ToList();
        contenedor.Controls.Clear();
        foreach (var c in hijos) { SoltarImagenes(c); c.Dispose(); }
    }

    private static void SoltarImagenes(Control c)
    {
        if (c is PictureBox { Image: { } img } pic) { pic.Image = null; img.Dispose(); }
        foreach (Control hijo in c.Controls) SoltarImagenes(hijo);
    }

    // ── Adjuntar al comentario ───────────────────────────────────────────────────

    private void AdjuntarImagen()
    {
        var elegidas = ForumImagenes.Elegir(this, ForumService.MaxImagenes - _adjuntas.Count);
        if (elegidas.Count == 0) return;
        _adjuntas.AddRange(elegidas);
        PintarAdjuntas();
    }

    private void PegarImagen()
    {
        if (_adjuntas.Count >= ForumService.MaxImagenes)
        {
            MessageBox.Show($"Ya hay {ForumService.MaxImagenes} imágenes en este comentario.",
                "Sin sitio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var img = ForumImagenes.Pegar(this);
        if (img == null) return;
        _adjuntas.Add(img);
        PintarAdjuntas();
    }

    private void PintarAdjuntas()
    {
        Vaciar(_pnlAdjuntas);

        foreach (var img in _adjuntas.ToList())
        {
            var caja = new Panel { Size = new Size(96, 64), Margin = new Padding(0, 0, 6, 0), BackColor = AppTheme.ContentBg };
            var pic = new PictureBox
            {
                Location = new Point(0, 0), Size = new Size(96, 64), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Image = ForumImagenes.Cargar(img.Miniatura.Length > 0 ? img.Miniatura : img.Bytes)
            };
            var quitar = new Button
            {
                Text = "✖", Size = new Size(22, 20), Location = new Point(72, 1),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = AppTheme.Danger,
                Font = AppTheme.SmallFont, FlatAppearance = { BorderSize = 0 }
            };
            quitar.Click += (_, _) => { _adjuntas.Remove(img); PintarAdjuntas(); };

            caja.Controls.Add(quitar);
            caja.Controls.Add(pic);
            _pnlAdjuntas.Controls.Add(caja);
        }

        _pnlAdjuntas.Visible = _adjuntas.Count > 0;
        _filaEscribir.RowStyles[1].Height = _adjuntas.Count > 0 ? 74f : 0f;
        _lblAdjuntas.Text = _adjuntas.Count == 0 ? "" : $"{_adjuntas.Count} imagen(es)";
    }

    // ── Acciones ─────────────────────────────────────────────────────────────────

    private void PintarRespondiendo(bool cerrado)
    {
        if (cerrado)
        {
            _lblRespondiendo.Text = "🔒 El hilo está cerrado: ya no admite comentarios.";
            _lblRespondiendo.ForeColor = AppTheme.Warning;
            return;
        }
        _lblRespondiendo.ForeColor = AppTheme.TextSecondary;
        _lblRespondiendo.Text = _respondiendoA == _rootId
            ? "Comentando en el hilo.  Pega una dirección y quedará pulsable; Ctrl+V pega una captura."
            : $"Respondiendo a {_respondiendoAQuien}.  (Escribe y pulsa Comentar; para volver al hilo, ciérralo y ábrelo de nuevo.)";
    }

    private void Comentar()
    {
        var texto = _txtComentario.Text.Trim();
        if (texto.Length == 0 && _adjuntas.Count == 0) return;

        var (ok, mensaje, _) = _forum.Comentar(_respondiendoA, texto, _adjuntas.ToList());
        if (!ok)
        {
            MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _txtComentario.Clear();
        _adjuntas.Clear();
        PintarAdjuntas();
        _respondiendoA = _rootId;
        _respondiendoAQuien = null;
        Recargar();
        _pnlHilo.VerticalScroll.Value = _pnlHilo.VerticalScroll.Maximum;
    }

    private void Editar(ForumPost p)
    {
        using var frm = new ForumPostForm(p, _imagenes.TryGetValue(p.Id, out var imgs) ? imgs : []);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var (ok, mensaje) = _forum.Editar(p.Id, frm.Titulo, frm.Cuerpo, frm.ImagenesNuevas, frm.QuitarImagenes);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Recargar();
    }

    private void Retirar(ForumPost p)
    {
        bool conImagenes = _imagenes.TryGetValue(p.Id, out var imgs) && imgs.Count > 0;
        if (MessageBox.Show(
                "¿Retirar esta entrada?\n\nSe conserva el hueco en el hilo con un aviso, para que la " +
                "conversación se siga entendiendo. No se puede deshacer." +
                (conImagenes ? "\n\nSus imágenes dejarán de verse." : ""),
                "Retirar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var (ok, mensaje) = _forum.Retirar(p.Id);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Recargar();
    }
}
