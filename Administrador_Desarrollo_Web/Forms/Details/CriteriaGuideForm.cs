using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// La lista de actividades que se pueden registrar, con lo que significa cada una y cuánto vale.
///
/// Antes esto era un tooltip con TODO el catálogo dentro. Con casi cuarenta actividades el globo
/// tapaba media pantalla, no se podía recorrer y desaparecía al mover el ratón: justo lo que uno
/// necesita hacer para compararlas. Aquí se puede leer con calma, buscar, y elegir de una vez —el
/// doble clic la selecciona y cierra.
/// </summary>
public class CriteriaGuideForm : ResponsiveForm
{
    private readonly List<ScoringCriterion> _todos;
    private List<ScoringCriterion> _visibles = [];

    private TextBox _txtBuscar = null!;
    private ListView _lst = null!;
    private TextBox _txtDetalle = null!;
    private Label _lblResumen = null!;
    private Button _btnUsar = null!;

    /// <summary>Id de la actividad elegida con «Usar esta», o null si solo se consultó.</summary>
    public int? CriterioElegido { get; private set; }

    /// <param name="titulo">Encabezado; cambia según quién abre la ventana.</param>
    public CriteriaGuideForm(IEnumerable<ScoringCriterion> criterios, string titulo = "¿Qué puedo registrar?")
    {
        // Lo que más vale primero: es el orden en que uno busca «¿qué es lo mejor que hice?».
        _todos = criterios.OrderByDescending(c => c.DefaultPoints).ThenBy(c => c.Name).ToList();
        BuildUI(titulo);
        Repintar();
    }

    private void BuildUI(string titulo)
    {
        Text = titulo;
        Size = new Size(860, 660);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));    // encabezado
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));    // buscador
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // lista
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 118f));   // detalle
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));    // botones

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  📖  {titulo}", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var barra = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg, Padding = new Padding(14, 8, 14, 4) };
        _txtBuscar = new TextBox
        {
            Dock = DockStyle.Fill, PlaceholderText = "Escribe algo de lo que hiciste: «pruebas», «PR», «documentación»…"
        };
        _txtBuscar.TextChanged += (_, _) => Repintar();
        barra.Controls.Add(_txtBuscar);

        _lst = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.FixedSingle
        };
        _lst.Columns.Add("Actividad", 300);
        _lst.Columns.Add("Vale", 65, HorizontalAlignment.Right);
        _lst.Columns.Add("Cuándo registrarla", 440);
        _lst.SelectedIndexChanged += (_, _) => PintarDetalle();
        _lst.DoubleClick += (_, _) => Usar();

        var pnlLista = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4), BackColor = AppTheme.ContentBg };
        pnlLista.Controls.Add(_lst);

        // La columna recorta las descripciones largas; aquí se lee entera la seleccionada.
        var pnlDetalle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 6), BackColor = AppTheme.ContentBg };
        _txtDetalle = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle
        };
        _lblResumen = new Label
        {
            Dock = DockStyle.Bottom, Height = 20, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };
        pnlDetalle.Controls.Add(_txtDetalle);
        pnlDetalle.Controls.Add(_lblResumen);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCerrar = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnUsar = AppTheme.MakePrimaryButton("Usar esta", 130);
        _btnUsar.Click += (_, _) => Usar();
        btns.Controls.AddRange([btnCerrar, _btnUsar]);

        outer.Controls.Add(hdr,        0, 0);
        outer.Controls.Add(barra,      0, 1);
        outer.Controls.Add(pnlLista,   0, 2);
        outer.Controls.Add(pnlDetalle, 0, 3);
        outer.Controls.Add(btns,       0, 4);
        Controls.Add(outer);

        // Sin AcceptButton: con el foco en el buscador, un Enter para «buscar ya» elegiría la
        // actividad resaltada y cerraría la ventana.
        CancelButton = btnCerrar;
    }

    /// <summary>
    /// Coincide por nombre o por descripción, exigiendo TODAS las palabras: quien busca «pruebas
    /// QA» quiere lo que menciona ambas, no todo lo que mencione cualquiera de las dos.
    /// </summary>
    private static bool Coincide(ScoringCriterion c, string texto)
    {
        var campos = $"{c.Name} {c.Description}";
        foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!campos.Contains(palabra, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private void Repintar()
    {
        var texto = _txtBuscar.Text.Trim();
        _visibles = texto.Length == 0 ? _todos : _todos.Where(c => Coincide(c, texto)).ToList();

        _lst.BeginUpdate();
        _lst.Items.Clear();
        foreach (var c in _visibles)
        {
            var item = new ListViewItem(c.Name) { Tag = c.Id };
            item.SubItems.Add((c.DefaultPoints >= 0 ? "+" : "") + c.DefaultPoints);
            item.SubItems.Add(string.IsNullOrWhiteSpace(c.Description) ? "(sin descripción)" : c.Description);
            item.UseItemStyleForSubItems = false;
            item.SubItems[1].ForeColor = c.DefaultPoints >= 0 ? AppTheme.Success : AppTheme.Danger;
            _lst.Items.Add(item);
        }
        _lst.EndUpdate();

        if (_lst.Items.Count > 0) _lst.Items[0].Selected = true;
        PintarDetalle();
    }

    private ScoringCriterion? Seleccionado()
    {
        if (_lst.SelectedItems.Count == 0) return null;
        var id = (int)_lst.SelectedItems[0].Tag!;
        return _visibles.FirstOrDefault(c => c.Id == id);
    }

    private void PintarDetalle()
    {
        var c = Seleccionado();
        _btnUsar.Enabled = c != null;

        if (c == null)
        {
            _txtDetalle.Text = _todos.Count == 0
                ? "Todavía no hay actividades configuradas. Pídeselas al líder."
                : "No hay ninguna actividad que coincida con lo que escribiste.\n\n" +
                  "Prueba con menos palabras, o elige la más parecida y explica el resto en el comentario.";
            _lblResumen.Text = $"{_visibles.Count} de {_todos.Count} actividades";
            return;
        }

        _txtDetalle.Text =
            $"{c.Name}   ({(c.DefaultPoints >= 0 ? "+" : "")}{c.DefaultPoints} puntos)\r\n\r\n" +
            (string.IsNullOrWhiteSpace(c.Description)
                ? "Esta actividad no tiene descripción. Pídesela al líder."
                : c.Description);

        _lblResumen.Text = _visibles.Count == _todos.Count
            ? $"{_todos.Count} actividades  ·  doble clic para elegir la resaltada"
            : $"{_visibles.Count} de {_todos.Count} actividades  ·  doble clic para elegir la resaltada";
    }

    private void Usar()
    {
        if (Seleccionado() is not { } c) return;
        CriterioElegido = c.Id;
        DialogResult = DialogResult.OK;
        Close();
    }
}
