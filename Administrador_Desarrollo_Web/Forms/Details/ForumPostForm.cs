using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Redactar una publicación del foro, o editar una entrada propia. Trabaja sobre valores sueltos,
/// nunca sobre la entidad rastreada: el AppDbContext es un singleton compartido y editar aquí una
/// entidad viva escribiría en la base antes de pulsar Guardar.
/// </summary>
public class ForumPostForm : ResponsiveForm
{
    private ComboBox _cbxTema = null!;
    private TextBox _txtTitulo = null!, _txtEtiquetas = null!, _txtCuerpo = null!;

    private static readonly ForumTopic[] Temas = Enum.GetValues<ForumTopic>();

    public ForumTopic Tema => Temas[Math.Max(0, _cbxTema.SelectedIndex)];
    public string Titulo => _txtTitulo.Text.Trim();
    public string Cuerpo => _txtCuerpo.Text.Trim();
    public string Etiquetas => _txtEtiquetas.Text.Trim();

    /// <summary>Publicación nueva.</summary>
    public ForumPostForm() => BuildUI(null);

    /// <summary>Edición de una entrada existente (publicación o comentario).</summary>
    public ForumPostForm(ForumPost editar) => BuildUI(editar);

    private void BuildUI(ForumPost? editar)
    {
        bool esComentario = editar is { ParentId: not null };
        Text = editar == null ? "Nueva publicación" : esComentario ? "Editar comentario" : "Editar publicación";
        Size = new Size(640, esComentario ? 420 : 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, esComentario ? 0f : 120f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
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
        pnlCuerpo.Controls.Add(_txtCuerpo);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var guardar = AppTheme.MakePrimaryButton(editar == null ? "Publicar 💬" : "Guardar", 130);
        guardar.Click += (_, _) => Guardar(esComentario);
        btns.Controls.AddRange([cancelar, guardar]);

        tbl.Controls.Add(hdr,       0, 0);
        tbl.Controls.Add(datos,     0, 1);
        tbl.Controls.Add(pnlCuerpo, 0, 2);
        tbl.Controls.Add(btns,      0, 3);
        Controls.Add(tbl);

        CancelButton = cancelar;
        // Sin AcceptButton: el cuerpo es multilínea y Enter debe insertar un salto, no publicar.
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
        if (Cuerpo.Length < (esComentario ? 1 : 5))
        {
            MessageBox.Show("Escribe algo que compartir.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtCuerpo.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }
}
