using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// El foro del equipo, en dos vistas de los MISMOS datos:
///
///  · <b>🗂 Muro</b> — tarjetas con scroll, ❤ y contador de comentarios; al abrir una, su hilo con
///    los comentarios anidados. Es la vista para el día a día, la que invita a leer y a responder.
///  · <b>📋 Auditoría</b> — rejilla con filtros y columnas: quién dijo qué, cuándo, en qué hilo, si
///    se editó y si se retiró. Es la vista para reconstruir una conversación, no para participar.
///    SOLO la ve el administrador: es supervisión, no participación — el rastro consolidado de
///    retiradas y ediciones de todo el equipo no es asunto de cada quien. Dentro de un hilo, el
///    hueco «(contenido eliminado…)» y la marca «(editado)» sí los ve cualquiera: eso es contexto
///    de la conversación, no un expediente.
///
/// El MURO lo ve todo el que tenga sesión: el punto es compartir. Cada quien manda sobre lo suyo;
/// el administrador además puede fijar, cerrar y retirar cualquier cosa.
/// </summary>
public class ForumControl : UserControl
{
    private readonly ForumService _forum;
    private readonly ICurrentUser _currentUser;

    // Muro
    private Panel _pnlMuro = null!;
    private TextBox _txtBuscarMuro = null!;
    private ComboBox _cbxTemaMuro = null!, _cbxDiasMuro = null!;
    private CheckBox _chkMiosMuro = null!;
    private Label _lblMuro = null!;
    private List<ForumTarjeta> _tarjetas = [];

    // Auditoría
    private DataGridView _grid = null!;
    private TextBox _txtBuscarAud = null!;
    private ComboBox _cbxTemaAud = null!, _cbxAutorAud = null!;
    private Label _lblAud = null!;
    private List<ForumPost> _auditoria = [];

    private bool _suspender;
    private const string ClaveColumnas = "forum.auditoria";

    private static readonly (string Etiqueta, int? Dias)[] Ventanas =
    [
        ("Todo", null), ("Últimos 7 días", 7), ("Últimos 30 días", 30), ("Últimos 90 días", 90),
    ];

    public ForumControl(ForumService forum, ICurrentUser currentUser)
    {
        _forum = forum; _currentUser = currentUser;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildMuroTab());
        // La pestaña de auditoría ni se construye para quien no es admin: una pestaña que solo
        // mostrara «sin permiso» en cada refresco sería ruido, no seguridad. La barrera real está
        // en ForumService.Auditoria (RequireAdmin); esto es solo no enseñar la puerta.
        if (_currentUser.IsAdmin) tabs.TabPages.Add(BuildAuditoriaTab());
        Controls.Add(tabs);
    }

    // ── Muro ─────────────────────────────────────────────────────────────────────
    private TabPage BuildMuroTab()
    {
        var page = new TabPage("  🗂  Muro  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg
        };

        var btnNueva = AppTheme.MakePrimaryButton("➕ Publicar", 130);
        btnNueva.Margin = new Padding(0, 2, 10, 2);
        btnNueva.Click += (_, _) => Publicar();

        _txtBuscarMuro = new TextBox { Width = 230, Margin = new Padding(0, 5, 8, 2), PlaceholderText = "Buscar en el foro…" };
        _txtBuscarMuro.TextChanged += (_, _) => LoadMuro();

        _cbxTemaMuro = ComboTema();
        _cbxTemaMuro.SelectedIndexChanged += (_, _) => LoadMuro();

        _cbxDiasMuro = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 8, 2) };
        foreach (var (e, _) in Ventanas) _cbxDiasMuro.Items.Add(e);
        _cbxDiasMuro.SelectedIndex = 0;
        _cbxDiasMuro.SelectedIndexChanged += (_, _) => LoadMuro();

        _chkMiosMuro = new CheckBox { Text = "Solo mías", AutoSize = true, Margin = new Padding(4, 8, 8, 0) };
        _chkMiosMuro.CheckedChanged += (_, _) => LoadMuro();

        var btnRecargar = AppTheme.MakeSecondaryButton("🔄 Recargar", 120);
        btnRecargar.Margin = new Padding(0, 2, 0, 2);
        btnRecargar.Click += (_, _) => LoadData();

        barra.Controls.AddRange([btnNueva, _txtBuscarMuro, _cbxTemaMuro, _cbxDiasMuro, _chkMiosMuro, btnRecargar]);

        _pnlMuro = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.ContentBg, Padding = new Padding(4) };
        _lblMuro = NuevaLinea();

        tbl.Controls.Add(barra,    0, 0);
        tbl.Controls.Add(_pnlMuro, 0, 1);
        tbl.Controls.Add(_lblMuro, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Auditoría ────────────────────────────────────────────────────────────────
    private TabPage BuildAuditoriaTab()
    {
        var page = new TabPage("  📋  Auditoría  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg
        };

        _txtBuscarAud = new TextBox { Width = 240, Margin = new Padding(0, 5, 8, 2), PlaceholderText = "Buscar en todo el foro…" };
        _txtBuscarAud.TextChanged += (_, _) => LoadAuditoria();

        _cbxTemaAud = ComboTema();
        _cbxTemaAud.SelectedIndexChanged += (_, _) => LoadAuditoria();

        _cbxAutorAud = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 8, 2) };
        _cbxAutorAud.Items.Add("Todos los autores");
        _cbxAutorAud.SelectedIndex = 0;
        _cbxAutorAud.SelectedIndexChanged += (_, _) => LoadAuditoria();

        // El borrado real vive AQUÍ y no en el muro: la auditoría es la vista desde la que el
        // administrador ya está revisando qué se publicó, y desde la que puede ver de un vistazo
        // cuántas entradas cuelgan del hilo que va a eliminar.
        var btnEliminar = AppTheme.MakeDangerButton("🗑 Eliminar publicación", 200);
        btnEliminar.Margin = new Padding(8, 3, 0, 2);
        btnEliminar.Click += (_, _) => EliminarDesdeAuditoria();

        barra.Controls.AddRange([_txtBuscarAud, _cbxTemaAud, _cbxAutorAud, btnEliminar]);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cuándo",  Name = "Cuando", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Autor",   Name = "Autor",  FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",    Name = "Tipo",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hilo",    Name = "Hilo",   FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Texto",   Name = "Texto",  FillWeight = 32 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",  Name = "Estado", FillWeight = 14 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) AbrirDesdeAuditoria(e.RowIndex); };
        GridColumns.Habilitar(_grid, ClaveColumnas);
        barra.Controls.Add(GridColumns.CrearBoton(_grid, ClaveColumnas));

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(_grid);

        _lblAud = NuevaLinea();

        tbl.Controls.Add(barra,  0, 0);
        tbl.Controls.Add(pnl,    0, 1);
        tbl.Controls.Add(_lblAud, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    private static ComboBox ComboTema()
    {
        var cbx = new ComboBox { Width = 165, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 8, 2) };
        cbx.Items.Add("Todos los temas");
        foreach (var t in Enum.GetValues<ForumTopic>())
            cbx.Items.Add($"{ForumService.IconoTema(t)}  {ForumService.EtiquetaTema(t)}");
        cbx.SelectedIndex = 0;
        return cbx;
    }

    private static Label NuevaLinea() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0)
    };

    // ── Datos ────────────────────────────────────────────────────────────────────

    // Sin la condición, un no-admin reventaría: los controles de la pestaña de auditoría ni
    // siquiera se construyeron para él.
    private void LoadData() { LoadMuro(); if (_currentUser.IsAdmin) LoadAuditoria(); }

    private void LoadMuro()
    {
        if (_suspender) return;
        try
        {
            _tarjetas = _forum.Muro(new ForumFiltro(
                _txtBuscarMuro.Text,
                _cbxTemaMuro.SelectedIndex > 0 ? (ForumTopic)(_cbxTemaMuro.SelectedIndex - 1) : null,
                null,
                _cbxDiasMuro.SelectedIndex >= 0 ? Ventanas[_cbxDiasMuro.SelectedIndex].Dias : null,
                _chkMiosMuro.Checked));
        }
        catch (AuthorizationException ex) { _lblMuro.Text = ex.Message; return; }

        _pnlMuro.Controls.Clear();
        int y = 4;
        var ahora = DateTime.UtcNow;
        foreach (var t in _tarjetas)
        {
            var card = ConstruirTarjeta(t, ahora);
            card.Location = new Point(6, y);
            card.Width = Math.Max(320, _pnlMuro.ClientSize.Width - 24);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _pnlMuro.Controls.Add(card);
            y += card.Height + 10;
        }
        _pnlMuro.AutoScrollMinSize = new Size(0, y + 12);

        _lblMuro.Text = _tarjetas.Count == 0
            ? "No hay publicaciones con ese filtro.  Pulsa ➕ Publicar para empezar una conversación."
            : $"{_tarjetas.Count} publicación(es).  Doble clic abre el hilo.";
    }

    private Panel ConstruirTarjeta(ForumTarjeta t, DateTime ahora)
    {
        var p = t.Post;
        var card = new Panel { BackColor = AppTheme.CardBg, Padding = new Padding(14), Cursor = Cursors.Hand };

        var titulo = new Label
        {
            Text = $"{ForumService.IconoTema(p.Topic)}  {(p.Pinned ? "📌 " : "")}{p.Title}",
            Location = new Point(14, 12), AutoSize = false, Height = 24,
            Width = card.Width - 28, Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary,
            AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var meta = $"🧑 {p.AuthorName}   ·   {ForumFilter.HaceCuanto(t.UltimaActividadUtc, ahora)}";
        if (p.Locked) meta += "   ·   🔒 cerrado";
        if (!string.IsNullOrWhiteSpace(p.Tags)) meta += $"   ·   {p.Tags}";

        var lblMeta = new Label
        {
            Text = meta, Location = new Point(14, 40), AutoSize = true,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        };

        var extracto = new Label
        {
            Text = Extracto(p.TextoVisible),
            Location = new Point(14, 62), AutoSize = false, Height = 38,
            Width = card.Width - 28, ForeColor = AppTheme.TextPrimary,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // Las miniaturas NO se pintan aquí a propósito: el muro se recarga en cada tecla del
        // buscador, y traer las de cien publicaciones sería mover megas por la red para adornar.
        // El número basta para saber que hay algo que ver, y el hilo ya las enseña.
        var pieTexto = $"{(t.YoDiMeGusta ? "❤" : "♡")} {t.MeGusta}      💬 {t.Comentarios} comentario(s)";
        if (t.Imagenes > 0) pieTexto += $"      🖼 {t.Imagenes} imagen(es)";

        var pie = new Label
        {
            Text = pieTexto,
            Location = new Point(14, 104), AutoSize = true,
            Font = AppTheme.SmallFont, ForeColor = t.YoDiMeGusta ? AppTheme.Danger : AppTheme.TextSecondary
        };

        card.Controls.AddRange([titulo, lblMeta, extracto, pie]);
        card.Height = 138;

        // El clic en cualquier parte de la tarjeta abre el hilo: en un muro, la tarjeta ES el botón.
        void Abrir(object? s, EventArgs e) => AbrirHilo(p.Id);
        card.Click += Abrir;
        foreach (Control c in card.Controls) { c.Click += Abrir; c.Cursor = Cursors.Hand; }

        return card;
    }

    private static string Extracto(string cuerpo)
    {
        var plano = cuerpo.Replace("\r", " ").Replace("\n", " ").Trim();
        return plano.Length <= 220 ? plano : plano[..220] + "…";
    }

    private void LoadAuditoria()
    {
        if (_suspender) return;
        try
        {
            _auditoria = _forum.Auditoria(new ForumFiltro(
                _txtBuscarAud.Text,
                _cbxTemaAud.SelectedIndex > 0 ? (ForumTopic)(_cbxTemaAud.SelectedIndex - 1) : null,
                _cbxAutorAud.SelectedIndex > 0 ? _cbxAutorAud.SelectedItem as string : null));
        }
        catch (AuthorizationException ex) { _lblAud.Text = ex.Message; return; }

        // El combo de autores se arma con quien de verdad ha escrito.
        _suspender = true;
        try
        {
            var previo = _cbxAutorAud.SelectedIndex > 0 ? _cbxAutorAud.SelectedItem as string : null;
            _cbxAutorAud.BeginUpdate();
            _cbxAutorAud.Items.Clear();
            _cbxAutorAud.Items.Add("Todos los autores");
            foreach (var a in ForumFilter.Autores(_forum.Auditoria())) _cbxAutorAud.Items.Add(a);
            _cbxAutorAud.EndUpdate();
            int idx = previo != null ? _cbxAutorAud.Items.IndexOf(previo) : 0;
            _cbxAutorAud.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally { _suspender = false; }

        // Título del hilo de cada entrada, para que un comentario no salga huérfano en la rejilla.
        var titulos = _auditoria.Where(p => p.EsPublicacion).ToDictionary(p => p.Id, p => p.Title ?? "");

        // Solo el número: una entrada que era una captura sin texto saldría en blanco, y en una
        // rejilla de auditoría «no dijo nada» y «puso una imagen» no pueden verse igual.
        var conImagenes = _forum.ConteoImagenes(_auditoria.Select(p => p.Id).ToList());

        _grid.Rows.Clear();
        foreach (var p in _auditoria)
        {
            var texto = Extracto(p.TextoVisible);
            if (!p.Eliminado && conImagenes.TryGetValue(p.Id, out var n) && n > 0)
                texto = texto.Length == 0 ? $"🖼 {n} imagen(es)" : $"🖼 {n}  ·  {texto}";

            int i = _grid.Rows.Add(
                p.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                p.AuthorName,
                p.EsPublicacion ? $"{ForumService.IconoTema(p.Topic)} Publicación" : "↩ Comentario",
                p.EsPublicacion ? (p.Title ?? "") : (titulos.TryGetValue(p.RootId, out var t) ? t : $"#{p.RootId}"),
                texto,
                p.Eliminado ? "🗑 Retirada" : p.EditedAtUtc != null ? "✏ Editada" : "");

            if (p.Eliminado) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            if (p.EditedAtUtc != null) _grid.Rows[i].Cells["Estado"].Style.ForeColor = AppTheme.Warning;
        }

        int publicaciones = _auditoria.Count(p => p.EsPublicacion);
        int retiradas = _auditoria.Count(p => p.Eliminado);
        _lblAud.Text = _auditoria.Count == 0
            ? "No hay entradas con ese filtro."
            : $"{_auditoria.Count} entrada(s)  ·  {publicaciones} publicación(es)  ·  {retiradas} retirada(s).  " +
              "Doble clic abre el hilo completo.  Nada se borra: lo retirado conserva su hueco.";
    }

    private void AbrirDesdeAuditoria(int fila)
    {
        if (fila < 0 || fila >= _auditoria.Count) return;
        AbrirHilo(_auditoria[fila].RootId);
    }

    /// <summary>
    /// Elimina de verdad la publicación completa a la que pertenece la fila seleccionada. Se actúa
    /// siempre sobre la RAÍZ del hilo aunque lo seleccionado sea un comentario: no hay forma de
    /// borrar «media publicación», y decírselo antes evita que crea que solo quitó el comentario.
    /// </summary>
    private void EliminarDesdeAuditoria()
    {
        int fila = _grid.CurrentRow?.Index ?? -1;
        if (fila < 0 || fila >= _auditoria.Count)
        {
            MessageBox.Show("Selecciona una entrada de la lista.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var seleccionada = _auditoria[fila];
        // El filtro pudo dejar fuera la raíz aunque el comentario sí se vea: se pide a la base.
        var raiz = _auditoria.FirstOrDefault(p => p.Id == seleccionada.RootId) ?? _forum.Obtener(seleccionada.RootId);
        if (raiz == null)
        {
            MessageBox.Show("No se encontró la publicación de esa entrada. Recarga la lista.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int comentarios = _auditoria.Count(p => p.RootId == raiz.Id && p.Id != raiz.Id);
        string aviso = seleccionada.Id == raiz.Id
            ? ""
            : "\n\nOJO: seleccionaste un COMENTARIO. Se eliminará la publicación entera a la que pertenece.";

        if (MessageBox.Show(
                $"¿Eliminar por completo la publicación «{raiz.Title}» de {raiz.AuthorName}?\n\n" +
                (comentarios == 0 ? "No tiene comentarios." : $"Se irán también sus {comentarios} comentario(s).") +
                aviso +
                "\n\nEsto NO es «Retirar»: el contenido y sus imágenes se borran de la base de datos y " +
                "no se puede deshacer. En la bitácora quedará constancia con título y autor.",
                "Eliminar publicación completa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, mensaje) = _forum.EliminarPublicacion(raiz.Id);
            MessageBox.Show(mensaje, ok ? "Eliminada" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AbrirHilo(int rootId)
    {
        using var frm = new ForumThreadForm(_forum, _currentUser, rootId);
        frm.ShowDialog(FindForm());
        LoadData();
    }

    private void Publicar()
    {
        using var frm = new ForumPostForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        var (ok, mensaje, post) = _forum.Publicar(frm.Titulo, frm.Cuerpo, frm.Tema, frm.Etiquetas, frm.ImagenesNuevas);
        if (!ok)
        {
            MessageBox.Show(mensaje, "No se pudo publicar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadData();
        if (post != null) AbrirHilo(post.Id);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
