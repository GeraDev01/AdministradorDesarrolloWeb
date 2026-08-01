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
/// </summary>
public class ForumThreadForm : ResponsiveForm
{
    private readonly ForumService _forum;
    private readonly ICurrentUser _currentUser;
    private readonly int _rootId;

    private Panel _pnlHilo = null!;
    private TextBox _txtComentario = null!;
    private Button _btnComentar = null!;
    private Label _lblRespondiendo = null!;

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
        Size = new Size(860, 720);
        MinimumSize = new Size(640, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _pnlHilo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(12) };

        // ── Caja de respuesta ────────────────────────────────────
        var pnlNuevo = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg, Padding = new Padding(12, 8, 12, 8) };

        _lblRespondiendo = new Label
        {
            Dock = DockStyle.Top, Height = 20, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        var fila = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
        fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        fila.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _txtComentario = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            MaxLength = ForumService.MaxCuerpo, Margin = new Padding(0, 0, 8, 0)
        };
        _btnComentar = AppTheme.MakePrimaryButton("💬 Comentar", 120);
        _btnComentar.Dock = DockStyle.Fill;
        _btnComentar.Margin = new Padding(0, 0, 0, 0);
        _btnComentar.Click += (_, _) => Comentar();

        fila.Controls.Add(_txtComentario, 0, 0);
        fila.Controls.Add(_btnComentar,   1, 0);

        pnlNuevo.Controls.Add(fila);
        pnlNuevo.Controls.Add(_lblRespondiendo);

        tbl.Controls.Add(_pnlHilo, 0, 0);
        tbl.Controls.Add(pnlNuevo, 0, 1);
        Controls.Add(tbl);
    }

    private void Recargar()
    {
        List<ForumNodo> nodos;
        try { nodos = _forum.Hilo(_rootId); }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close(); return;
        }

        _pnlHilo.Controls.Clear();
        if (nodos.Count == 0) { Close(); return; }

        var raiz = nodos[0].Post;
        Text = $"💬 {raiz.Title}";
        bool cerrado = raiz.Locked;

        _txtComentario.Enabled = _btnComentar.Enabled = !cerrado;
        PintarRespondiendo(cerrado);

        int y = 0;
        var ahora = DateTime.UtcNow;
        foreach (var nodo in nodos)
        {
            var tarjeta = ConstruirEntrada(nodo, ahora, cerrado);
            tarjeta.Location = new Point(nodo.Nivel * 26, y);
            tarjeta.Width = Math.Max(240, _pnlHilo.ClientSize.Width - 30 - nodo.Nivel * 26);
            tarjeta.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _pnlHilo.Controls.Add(tarjeta);
            y += tarjeta.Height + 8;
        }
        _pnlHilo.AutoScrollMinSize = new Size(0, y + 12);
    }

    private Panel ConstruirEntrada(ForumNodo nodo, DateTime ahora, bool hiloCerrado)
    {
        var p = nodo.Post;
        bool esRaiz = nodo.Nivel == 0;
        bool mia = p.AuthorUserId == _currentUser.UserId;

        var caja = new Panel
        {
            BackColor = esRaiz ? Color.FromArgb(239, 246, 255) : Color.FromArgb(247, 249, 251),
            Padding = new Padding(12), AutoSize = false
        };

        int y = 10;

        if (esRaiz && !string.IsNullOrWhiteSpace(p.Title))
        {
            caja.Controls.Add(new Label
            {
                Text = $"{ForumService.IconoTema(p.Topic)}  {p.Title}",
                Location = new Point(12, y), AutoSize = false, Width = caja.Width - 24, Height = 24,
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

        var cuerpo = new Label
        {
            Text = p.TextoVisible,
            Location = new Point(12, y), AutoSize = true,
            MaximumSize = new Size(caja.Width - 30, 0),
            ForeColor = p.Eliminado ? AppTheme.TextSecondary : AppTheme.TextPrimary,
            Font = p.Eliminado ? AppTheme.SmallFont : AppTheme.DefaultFont
        };
        caja.Controls.Add(cuerpo);
        y = cuerpo.Bottom + 8;

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
            ? "Comentando en el hilo."
            : $"Respondiendo a {_respondiendoAQuien}.  (Escribe y pulsa Comentar; para volver al hilo, ciérralo y ábrelo de nuevo.)";
    }

    private void Comentar()
    {
        var texto = _txtComentario.Text.Trim();
        if (texto.Length == 0) return;

        var (ok, mensaje, _) = _forum.Comentar(_respondiendoA, texto);
        if (!ok)
        {
            MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _txtComentario.Clear();
        _respondiendoA = _rootId;
        _respondiendoAQuien = null;
        Recargar();
        _pnlHilo.VerticalScroll.Value = _pnlHilo.VerticalScroll.Maximum;
    }

    private void Editar(ForumPost p)
    {
        using var frm = new ForumPostForm(p);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var (ok, mensaje) = _forum.Editar(p.Id, frm.Titulo, frm.Cuerpo);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Recargar();
    }

    private void Retirar(ForumPost p)
    {
        if (MessageBox.Show(
                "¿Retirar esta entrada?\n\nSe conserva el hueco en el hilo con un aviso, para que la " +
                "conversación se siga entendiendo. No se puede deshacer.",
                "Retirar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var (ok, mensaje) = _forum.Retirar(p.Id);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Recargar();
    }
}
