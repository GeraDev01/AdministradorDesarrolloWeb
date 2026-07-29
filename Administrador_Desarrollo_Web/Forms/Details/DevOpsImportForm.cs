using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Selección filtrable de work items de DevOps a importar como Requerimientos. Cada item ya viene
/// pre-empatado a un desarrollador por identidad (correo/nombre); el que empata queda marcado por
/// omisión. La lista que recibe YA excluye los cerrados (Done/Removed), así que aquí solo se decide
/// el subconjunto a traer. La asignación al desarrollador es automática: aquí no se elige a mano.
/// </summary>
public class DevOpsImportForm : Form
{
    private sealed class Candidate
    {
        public required DevOpsWorkItem Item { get; init; }
        public Developer? Dev { get; init; }
        public string DevName => Dev?.FullName ?? "— sin desarrollador —";
    }

    private readonly List<Candidate> _all;
    private readonly HashSet<int> _checked = [];

    private DataGridView _grid = null!;
    private ComboBox _cbDev = null!;
    private ComboBox _cbState = null!;
    private TextBox _txtSearch = null!;
    private Label _lblCount = null!;

    /// <summary>Work items elegidos por el usuario (tras aceptar).</summary>
    public List<DevOpsWorkItem> SelectedItems { get; private set; } = [];

    public DevOpsImportForm(IEnumerable<DevOpsWorkItem> items, IReadOnlyList<Developer> devs)
    {
        _all = items.Select(i => new Candidate
        {
            Item = i,
            Dev  = DevOpsIdentityMatcher.Find(i.AssignedTo, i.AssignedToEmail, devs)
        }).ToList();

        // Por omisión se marcan los que SÍ empatan con un desarrollador: es lo que se pidió
        // (los asignados a un desarrollador se asignan a él). Los sin match quedan desmarcados.
        foreach (var c in _all.Where(c => c.Dev != null)) _checked.Add(c.Item.Id);

        BuildUI();
        RenderRows();
    }

    private void BuildUI()
    {
        Text = "Importar y asignar desde Azure DevOps";
        Size = new Size(940, 640);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));   // filtros
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));   // botones
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = "  🔷  Importar y asignar desde Azure DevOps", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        // ── Filtros ───────────────────────────────────────────────
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(12, 10, 12, 6), BackColor = AppTheme.ContentBg };

        filters.Controls.Add(new Label { Text = "Desarrollador:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbDev = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbDev.Items.Add("Todos");
        foreach (var name in _all.Select(c => c.DevName).Distinct().OrderBy(n => n)) _cbDev.Items.Add(name);
        _cbDev.SelectedIndex = 0;
        _cbDev.SelectedIndexChanged += (_, _) => RenderRows();
        filters.Controls.Add(_cbDev);

        filters.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbState = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbState.Items.Add("Todos");
        foreach (var st in _all.Select(c => c.Item.State).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s)) _cbState.Items.Add(st);
        _cbState.SelectedIndex = 0;
        _cbState.SelectedIndexChanged += (_, _) => RenderRows();
        filters.Controls.Add(_cbState);

        _txtSearch = new TextBox { Width = 200, Margin = new Padding(0, 4, 12, 0), PlaceholderText = "Buscar título / ID…" };
        _txtSearch.TextChanged += (_, _) => RenderRows();
        filters.Controls.Add(_txtSearch);

        var btnAll = AppTheme.MakeSecondaryButton("✓ Marcar filtrados", 150);
        btnAll.Margin = new Padding(0, 2, 4, 0);
        btnAll.Click += (_, _) => SetCheckedForVisible(true);
        var btnNone = AppTheme.MakeSecondaryButton("▢ Desmarcar filtrados", 170);
        btnNone.Margin = new Padding(0, 2, 0, 0);
        btnNone.Click += (_, _) => SetCheckedForVisible(false);
        filters.Controls.AddRange([btnAll, btnNone]);

        // ── Grid ──────────────────────────────────────────────────
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 2, 12, 2), BackColor = AppTheme.ContentBg };
        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        // MakeGrid() deja la rejilla de solo lectura; aquí SÍ debe poder marcarse el checkbox, así que
        // se habilita a nivel rejilla y las columnas de texto se dejan de solo lectura una por una.
        _grid.ReadOnly = false;

        var chk = new DataGridViewCheckBoxColumn { HeaderText = "", Name = "Sel", Width = 34, Resizable = DataGridViewTriState.False };
        _grid.Columns.Add(chk);
        _grid.Columns.Add(TextCol("ID", "Id", 60));
        _grid.Columns.Add(TextCol("Tipo", "Type", 100));
        _grid.Columns.Add(TextCol("Título", "Title", 300));
        _grid.Columns.Add(TextCol("Estado", "State", 100));
        _grid.Columns.Add(TextCol("Asignado (DevOps)", "AssignedTo", 170));
        _grid.Columns.Add(TextCol("→ Desarrollador", "DevName", 170));
        _grid.Columns.Add(TextCol("Área", "Area", 150));
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        // Confirmar el clic del checkbox de inmediato para que dispare CellValueChanged.
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += Grid_CellValueChanged;
        _grid.CellFormatting += Grid_CellFormatting;
        pnlGrid.Controls.Add(_grid);

        // ── Botones ───────────────────────────────────────────────
        var bottom = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg };
        _lblCount = new Label { Dock = DockStyle.Left, Width = 380, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0), ForeColor = AppTheme.TextSecondary };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 10) };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 110);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Importar y asignar seleccionados", 260);
        btnOk.Click += BtnOk_Click;
        flow.Controls.AddRange([btnOk, btnCancel]);
        bottom.Controls.Add(flow);
        bottom.Controls.Add(_lblCount);

        root.Controls.Add(hdr,     0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(pnlGrid, 0, 2);
        root.Controls.Add(bottom,  0, 3);
        Controls.Add(root);
        AcceptButton = btnOk;
    }

    private IEnumerable<Candidate> Visible()
    {
        var dev = _cbDev.SelectedIndex > 0 ? _cbDev.SelectedItem as string : null;
        var st  = _cbState.SelectedIndex > 0 ? _cbState.SelectedItem as string : null;
        var q   = _txtSearch.Text.Trim();
        return _all.Where(c =>
            (dev == null || c.DevName == dev)
            && (st == null || string.Equals(c.Item.State, st, StringComparison.OrdinalIgnoreCase))
            && (q.Length == 0 || c.Item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Item.Id.ToString().Contains(q)));
    }

    private void RenderRows()
    {
        _grid.CellValueChanged -= Grid_CellValueChanged;   // evitar reentradas al repoblar
        _grid.Rows.Clear();
        foreach (var c in Visible())
        {
            int i = _grid.Rows.Add(_checked.Contains(c.Item.Id), c.Item.Id, c.Item.Type, c.Item.Title,
                c.Item.State, c.Item.AssignedTo, c.DevName, c.Item.AreaPath);
            _grid.Rows[i].Tag = c;
        }
        _grid.CellValueChanged += Grid_CellValueChanged;
        UpdateCount();
    }

    private void Grid_CellFormatting(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not Candidate c) return;
        if (_grid.Columns[e.ColumnIndex].Name == "DevName" && c.Dev == null)
            e.CellStyle.ForeColor = AppTheme.TextSecondary;
    }

    private void Grid_CellValueChanged(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Sel") return;
        if (_grid.Rows[e.RowIndex].Tag is not Candidate c) return;
        bool on = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Sel"].Value);
        if (on) _checked.Add(c.Item.Id); else _checked.Remove(c.Item.Id);
        UpdateCount();
    }

    private void SetCheckedForVisible(bool on)
    {
        foreach (var c in Visible())
            if (on) _checked.Add(c.Item.Id); else _checked.Remove(c.Item.Id);
        RenderRows();
    }

    private void UpdateCount()
    {
        int sinMatch = _checked.Count(id => _all.First(c => c.Item.Id == id).Dev == null);
        _lblCount.Text = sinMatch > 0
            ? $"{_checked.Count} seleccionados  ({sinMatch} sin desarrollador: quedarán sin asignar)"
            : $"{_checked.Count} seleccionados";
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        SelectedItems = _all.Where(c => _checked.Contains(c.Item.Id)).Select(c => c.Item).ToList();
        if (SelectedItems.Count == 0)
        {
            MessageBox.Show("No hay items seleccionados.", "Nada que importar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static DataGridViewTextBoxColumn TextCol(string header, string name, int w) => new()
    {
        HeaderText = header, Name = name, Width = w, ReadOnly = true
    };
}
