using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del DESARROLLADOR de sugerencias, con dos pestañas: «Mis sugerencias» (enviar y dar
/// seguimiento a las propias) y «Propuestas del equipo» (ver y apoyar con votos las de todos, para
/// que las más pedidas suban).
/// </summary>
public class MySuggestionsControl : UserControl
{
    private readonly SuggestionService _suggestions;

    // Mis sugerencias
    private DataGridView _grid = null!;
    private Button _btnView = null!, _btnDelete = null!;
    private Label _lblStatus = null!;
    private List<Suggestion> _rows = [];

    // Propuestas del equipo
    private DataGridView _gridEq = null!;
    private Button _btnVotar = null!;
    private Label _lblEqStatus = null!;
    private List<SuggestionConVotos> _equipo = [];

    public MySuggestionsControl(SuggestionService suggestions)
    {
        _suggestions = suggestions;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildMisTab());
        tabs.TabPages.Add(BuildEquipoTab());
        Controls.Add(tabs);
    }

    // ── Mis sugerencias ──────────────────────────────────────────
    private TabPage BuildMisTab()
    {
        var tab = new TabPage("  💡  Mis sugerencias  ") { BackColor = AppTheme.ContentBg };
        var tbl = NewLayout();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4), BackColor = AppTheme.ContentBg };
        var btnNew = AppTheme.MakePrimaryButton("➕ Nueva sugerencia", 190); btnNew.Margin = new Padding(0, 0, 8, 0); btnNew.Click += (_, _) => Nueva();
        _btnView = AppTheme.MakeSecondaryButton("👁 Ver", 110); _btnView.Margin = new Padding(0, 0, 8, 0); _btnView.Enabled = false; _btnView.Click += (_, _) => Ver();
        _btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 120); _btnDelete.Margin = new Padding(0, 0, 8, 0); _btnDelete.Enabled = false; _btnDelete.Click += (_, _) => Eliminar();
        var btnRefresh = AppTheme.MakeSecondaryButton("🔄 Recargar", 120); btnRefresh.Click += (_, _) => LoadData();
        toolbar.Controls.AddRange([btnNew, _btnView, _btnDelete, btnRefresh]);

        var pnlBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Name = "Fecha", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Categoría", Name = "Cat", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Titulo", FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Estado", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Respuesta", Name = "Resp", FillWeight = 24 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) Ver(); };
        pnlBody.Controls.Add(_grid);

        _lblStatus = NewStatus();
        tbl.Controls.Add(toolbar, 0, 0); tbl.Controls.Add(pnlBody, 0, 1); tbl.Controls.Add(_lblStatus, 0, 2);
        tab.Controls.Add(tbl);
        return tab;
    }

    // ── Propuestas del equipo (votos) ────────────────────────────
    private TabPage BuildEquipoTab()
    {
        var tab = new TabPage("  👍  Propuestas del equipo  ") { BackColor = AppTheme.ContentBg };
        var tbl = NewLayout();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4), BackColor = AppTheme.ContentBg };
        _btnVotar = AppTheme.MakePrimaryButton("👍 Votar", 150); _btnVotar.Margin = new Padding(0, 0, 8, 0); _btnVotar.Enabled = false; _btnVotar.Click += (_, _) => Votar();
        var btnVerEq = AppTheme.MakeSecondaryButton("👁 Ver", 110); btnVerEq.Margin = new Padding(0, 0, 8, 0); btnVerEq.Click += (_, _) => VerEquipo();
        var btnRefresh = AppTheme.MakeSecondaryButton("🔄 Recargar", 120); btnRefresh.Click += (_, _) => LoadEquipo();
        toolbar.Controls.AddRange([_btnVotar, btnVerEq, btnRefresh]);

        var pnlBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        _gridEq = AppTheme.MakeGrid();
        _gridEq.MultiSelect = false;
        _gridEq.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "👍", Name = "Votos", FillWeight = 7, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _gridEq.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Categoría", Name = "Cat", FillWeight = 13 });
        _gridEq.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Titulo", FillWeight = 38 });
        _gridEq.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Estado", FillWeight = 14 });
        _gridEq.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "De", Name = "Autor", FillWeight = 18 });
        foreach (DataGridViewColumn c in _gridEq.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _gridEq.SelectionChanged += (_, _) => UpdateEquipoButtons();
        _gridEq.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) VerEquipo(); };
        pnlBody.Controls.Add(_gridEq);

        _lblEqStatus = NewStatus();
        tbl.Controls.Add(toolbar, 0, 0); tbl.Controls.Add(pnlBody, 0, 1); tbl.Controls.Add(_lblEqStatus, 0, 2);
        tab.Controls.Add(tbl);
        return tab;
    }

    private void LoadData() { LoadMis(); LoadEquipo(); }

    private void LoadMis()
    {
        _rows = _suggestions.Mias();
        _grid.Rows.Clear();
        foreach (var s in _rows)
        {
            int i = _grid.Rows.Add(s.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                SuggestionService.EtiquetaCategoria(s.Category), s.Title,
                SuggestionService.EtiquetaEstado(s.Status), s.AdminResponse ?? "");
            _grid.Rows[i].Cells["Estado"].Style.ForeColor = ColorEstado(s.Status);
            _grid.Rows[i].Cells["Estado"].Style.Font = AppTheme.BoldFont;
        }
        _lblStatus.Text = _rows.Count == 0
            ? "Todavía no has enviado sugerencias. Pulsa «➕ Nueva sugerencia» para proponer una mejora."
            : $"{_rows.Count} sugerencia(s) enviada(s).";
        UpdateButtons();
    }

    private void LoadEquipo()
    {
        _equipo = _suggestions.Equipo();
        _gridEq.Rows.Clear();
        foreach (var e in _equipo)
        {
            int i = _gridEq.Rows.Add(e.Votos, SuggestionService.EtiquetaCategoria(e.Sug.Category), e.Sug.Title,
                SuggestionService.EtiquetaEstado(e.Sug.Status),
                e.Sug.Anonymous ? "Anónima" : e.Sug.Developer?.FullName ?? "—");
            _gridEq.Rows[i].Cells["Estado"].Style.ForeColor = ColorEstado(e.Sug.Status);
            if (e.YoVote)
            {
                _gridEq.Rows[i].Cells["Votos"].Style.ForeColor = AppTheme.Success;
                _gridEq.Rows[i].Cells["Votos"].Style.Font = AppTheme.BoldFont;
            }
        }
        _lblEqStatus.Text = _equipo.Count == 0
            ? "No hay propuestas todavía."
            : $"{_equipo.Count} propuesta(s) del equipo · ordenadas por más votadas. Selecciona una y pulsa «👍 Votar».";
        UpdateEquipoButtons();
    }

    private Suggestion? Seleccionada() =>
        _grid.CurrentRow is { Index: >= 0 } r && r.Index < _rows.Count ? _rows[r.Index] : null;

    private SuggestionConVotos? SeleccionadaEquipo() =>
        _gridEq.CurrentRow is { Index: >= 0 } r && r.Index < _equipo.Count ? _equipo[r.Index] : null;

    private void UpdateButtons()
    {
        var s = Seleccionada();
        _btnView.Enabled = s != null;
        _btnDelete.Enabled = s != null && SuggestionService.PuedeEliminar(s.Status);
    }

    private void UpdateEquipoButtons()
    {
        var e = SeleccionadaEquipo();
        _btnVotar.Enabled = e != null;
        _btnVotar.Text = e?.YoVote == true ? "✓ Quitar voto" : "👍 Votar";
    }

    private void Nueva()
    {
        using var frm = new SuggestionForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        var (ok, mensaje, _) = _suggestions.Enviar(frm.Categoria, frm.Titulo, frm.Cuerpo, frm.Anonima);
        LoadData();
        MessageBox.Show(mensaje, ok ? "Enviada" : "No se pudo enviar", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void Ver()
    {
        if (Seleccionada() is not { } s) return;
        using var frm = new SuggestionForm(s);
        frm.ShowDialog(FindForm());
    }

    private void VerEquipo()
    {
        if (SeleccionadaEquipo() is not { } e) return;
        using var frm = new SuggestionForm(e.Sug);
        frm.ShowDialog(FindForm());
    }

    private void Votar()
    {
        if (SeleccionadaEquipo() is not { } e) return;
        try
        {
            var (ok, _, _) = _suggestions.Votar(e.Sug.Id);
            if (!ok) { MessageBox.Show("No se pudo registrar el voto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int idx = _gridEq.CurrentRow?.Index ?? -1;
            LoadEquipo();
            // Reordena por votos: se reselecciona la misma propuesta para no perder al usuario.
            for (int r = 0; r < _gridEq.Rows.Count; r++)
                if (r < _equipo.Count && _equipo[r].Sug.Id == e.Sug.Id) { _gridEq.CurrentCell = _gridEq.Rows[r].Cells["Titulo"]; break; }
            _ = idx;
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void Eliminar()
    {
        if (Seleccionada() is not { } s) return;
        if (MessageBox.Show($"¿Eliminar tu sugerencia «{s.Title}»?", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            var (ok, mensaje) = _suggestions.Eliminar(s.Id);
            LoadData();
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static TableLayoutPanel NewLayout()
    {
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return tbl;
    }

    private static Label NewStatus() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
    };

    private static Color ColorEstado(SuggestionStatus s) => s switch
    {
        SuggestionStatus.Aceptada or SuggestionStatus.Implementada => AppTheme.Success,
        SuggestionStatus.Rechazada => AppTheme.Danger,
        SuggestionStatus.EnRevision => AppTheme.Warning,
        _ => AppTheme.TextSecondary
    };
}
