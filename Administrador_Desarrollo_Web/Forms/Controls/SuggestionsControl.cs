using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del ADMINISTRADOR para revisar las sugerencias y propuestas del equipo: filtrar, leer,
/// cambiarles el estado y responderlas. Las enviadas como anónimas no muestran el nombre del autor.
/// </summary>
public class SuggestionsControl : UserControl
{
    private readonly SuggestionService _suggestions;

    private ComboBox _cbxEstado = null!, _cbxCategoria = null!;
    private CheckBox _chkTopVotos = null!;
    private DataGridView _grid = null!;
    private Button _btnAtender = null!, _btnView = null!, _btnDelete = null!;
    private Label _lblStatus = null!;
    private List<Suggestion> _rows = [];

    public SuggestionsControl(SuggestionService suggestions)
    {
        _suggestions = suggestions;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 8, 10, 4), BackColor = AppTheme.ContentBg
        };
        var btnAtender = AppTheme.MakePrimaryButton("📝 Atender / responder", 200); btnAtender.Margin = new Padding(0, 0, 8, 0);
        btnAtender.Click += (_, _) => Atender();
        _btnAtender = btnAtender;
        _btnView = AppTheme.MakeSecondaryButton("👁 Ver", 100); _btnView.Margin = new Padding(0, 0, 8, 0);
        _btnView.Click += (_, _) => Ver();
        _btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 120); _btnDelete.Margin = new Padding(0, 0, 16, 0);
        _btnDelete.Click += (_, _) => Eliminar();

        _cbxEstado = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 8, 0) };
        _cbxEstado.Items.AddRange(["Todos los estados", "Nueva", "En revisión", "Aceptada", "Rechazada", "Implementada"]);
        _cbxEstado.SelectedIndex = 0;
        _cbxEstado.SelectedIndexChanged += (_, _) => LoadData();
        _cbxCategoria = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxCategoria.Items.AddRange(["Todas las categorías", "Producto", "Departamento", "Otro"]);
        _cbxCategoria.SelectedIndex = 0;
        _cbxCategoria.SelectedIndexChanged += (_, _) => LoadData();

        _chkTopVotos = new CheckBox { Text = "Más votadas primero", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
        _chkTopVotos.CheckedChanged += (_, _) => LoadData();

        toolbar.Controls.AddRange([btnAtender, _btnView, _btnDelete, _cbxEstado, _cbxCategoria, _chkTopVotos]);

        var pnlBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",     Name = "Fecha",  FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "De",         Name = "Autor",  FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Categoría",  Name = "Cat",    FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",     Name = "Titulo", FillWeight = 36 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "👍",         Name = "Votos",  FillWeight = 7, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",     Name = "Estado", FillWeight = 14 });
        // El mapeo fila→entidad usa el índice de _rows: si se permitiera ordenar por encabezado, la
        // fila visible dejaría de coincidir con _rows y se atendería/eliminaría la sugerencia equivocada.
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) Atender(); };
        pnlBody.Controls.Add(_grid);

        _lblStatus = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
        };

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlBody, 0, 1);
        tbl.Controls.Add(_lblStatus, 0, 2);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        SuggestionStatus? estado = _cbxEstado.SelectedIndex switch
        {
            1 => SuggestionStatus.Nueva, 2 => SuggestionStatus.EnRevision, 3 => SuggestionStatus.Aceptada,
            4 => SuggestionStatus.Rechazada, 5 => SuggestionStatus.Implementada, _ => null
        };
        SuggestionCategory? cat = _cbxCategoria.SelectedIndex switch
        {
            1 => SuggestionCategory.Producto, 2 => SuggestionCategory.Departamento, 3 => SuggestionCategory.Otro, _ => null
        };

        _rows = _suggestions.Todas(estado, cat);
        var votos = _suggestions.ContarVotos();
        int Votos(Suggestion s) => votos.TryGetValue(s.Id, out var n) ? n : 0;
        if (_chkTopVotos.Checked)
            _rows = _rows.OrderByDescending(Votos).ThenByDescending(s => s.CreatedAt).ToList();

        _grid.Rows.Clear();
        foreach (var s in _rows)
        {
            int i = _grid.Rows.Add(
                s.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                Autor(s),
                SuggestionService.EtiquetaCategoria(s.Category),
                s.Title,
                Votos(s),
                SuggestionService.EtiquetaEstado(s.Status));
            _grid.Rows[i].Cells["Estado"].Style.ForeColor = ColorEstado(s.Status);
            _grid.Rows[i].Cells["Estado"].Style.Font = AppTheme.BoldFont;
            if (s.Status == SuggestionStatus.Nueva) _grid.Rows[i].Cells["Titulo"].Style.Font = AppTheme.BoldFont;
            if (Votos(s) > 0) _grid.Rows[i].Cells["Votos"].Style.Font = AppTheme.BoldFont;
        }
        int nuevas = _rows.Count(s => s.Status == SuggestionStatus.Nueva);
        _lblStatus.Text = _rows.Count == 0
            ? "No hay sugerencias con ese filtro."
            : $"{_rows.Count} sugerencia(s)  ·  {nuevas} sin atender.";
        UpdateButtons();
    }

    private static string Autor(Suggestion s) =>
        s.Anonymous ? "Anónima" : s.Developer?.FullName ?? $"Usuario #{s.CreatedByUserId}";

    private Suggestion? Seleccionada() =>
        _grid.CurrentRow is { Index: >= 0 } r && r.Index < _rows.Count ? _rows[r.Index] : null;

    private void UpdateButtons()
    {
        bool hay = Seleccionada() != null;
        _btnAtender.Enabled = _btnView.Enabled = _btnDelete.Enabled = hay;
    }

    private void Atender()
    {
        if (Seleccionada() is not { } s) return;
        using var frm = new SuggestionResponseForm(s, Autor(s));
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (ok, mensaje) = _suggestions.Responder(s.Id, frm.NuevoEstado, frm.Respuesta);
            LoadData();
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Ver()
    {
        if (Seleccionada() is not { } s) return;
        using var frm = new SuggestionForm(s);
        frm.ShowDialog(FindForm());
    }

    private void Eliminar()
    {
        if (Seleccionada() is not { } s) return;
        if (MessageBox.Show($"¿Eliminar la sugerencia «{s.Title}»?\n\nEsta acción no se puede deshacer.", "Eliminar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, mensaje) = _suggestions.Eliminar(s.Id);
            LoadData();
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static Color ColorEstado(SuggestionStatus s) => s switch
    {
        SuggestionStatus.Aceptada or SuggestionStatus.Implementada => AppTheme.Success,
        SuggestionStatus.Rechazada => AppTheme.Danger,
        SuggestionStatus.EnRevision => AppTheme.Warning,
        _ => AppTheme.TextSecondary
    };
}
