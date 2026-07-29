using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Selección DIRECTA de servidores destino (estilo Blobup): por cada servidor se decide si se
/// incluye en el despliegue y, por separado, si se respalda antes (el respaldo es POR SERVIDOR).
/// Opcionalmente se precargan los servidores de un perfil existente.
/// </summary>
public class DeployServersPickerForm : Form
{
    private readonly List<DeploymentTarget> _servers;
    private readonly HashSet<int> _incluir;
    private readonly HashSet<int> _respaldar;
    private readonly List<DeploymentProfile> _profiles;
    /// <summary>false cuando se usa para PROGRAMAR: ahí el respaldo no se elige por servidor (el
    /// despliegue programado respalda todo, opción segura para algo desatendido), así que la columna
    /// «Respaldar» se oculta para no mostrar un control que no tendría efecto.</summary>
    private readonly bool _conRespaldo;

    private DataGridView _grid = null!;
    private TextBox _txtFind = null!;
    private ComboBox _cbxProfile = null!;
    private Label _lblCount = null!;
    private bool _suppress;

    public List<int> SelectedTargetIds { get; private set; } = [];
    /// <summary>Ids de servidores (de entre los incluidos) que además se respaldarán antes.</summary>
    public HashSet<int> BackupTargetIds { get; private set; } = [];

    public DeployServersPickerForm(IReadOnlyList<DeploymentTarget> servers,
        IReadOnlyCollection<int> preIncluidos, IReadOnlyCollection<int> preRespaldar, IReadOnlyList<DeploymentProfile> profiles,
        bool conRespaldo = true)
    {
        _servers = servers.ToList();
        _incluir = new HashSet<int>(preIncluidos);
        // Por omisión se respaldan los incluidos (opción segura); si venía una preselección explícita, se respeta.
        _respaldar = preRespaldar.Count > 0 ? new HashSet<int>(preRespaldar) : new HashSet<int>(_incluir);
        _profiles = profiles.ToList();
        _conRespaldo = conRespaldo;
        BuildUI();
        Render();
    }

    private void BuildUI()
    {
        Text = _conRespaldo ? "Elegir servidores destino y respaldo" : "Elegir servidores destino";
        Size = new Size(720, 580);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _txtFind = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Buscar servidor por nombre o host…" };
        _txtFind.TextChanged += (_, _) => Render();
        root.Controls.Add(_txtFind, 0, 0);

        var rowProf = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        rowProf.Controls.Add(new Label { Text = "Precargar de un perfil:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        _cbxProfile = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxProfile.Items.Add("(ninguno)");
        foreach (var p in _profiles) _cbxProfile.Items.Add(p.Name);
        _cbxProfile.SelectedIndex = 0;
        _cbxProfile.Enabled = _profiles.Count > 0;
        _cbxProfile.SelectedIndexChanged += CargarDePerfil;
        rowProf.Controls.Add(_cbxProfile);
        root.Controls.Add(rowProf, 0, 1);

        var rowBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        rowBtns.Controls.Add(Mini("✓ Incluir filtrados", () => SetVisibles(incluir: true, on: true)));
        rowBtns.Controls.Add(Mini("▢ Excluir filtrados", () => SetVisibles(incluir: true, on: false)));
        if (_conRespaldo)
        {
            rowBtns.Controls.Add(Mini("💾 Respaldar filtrados", () => SetVisibles(incluir: false, on: true)));
            rowBtns.Controls.Add(Mini("🚫 Sin respaldo filtrados", () => SetVisibles(incluir: false, on: false)));
        }
        root.Controls.Add(rowBtns, 0, 2);

        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = false;   // MakeGrid deja la rejilla de solo lectura; aquí sí se marcan casillas
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Incluir", Name = "Inc", Width = 60, Resizable = DataGridViewTriState.False });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Servidor", Name = "Name", FillWeight = 45, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Host", Name = "Host", FillWeight = 45, ReadOnly = true });
        if (_conRespaldo)
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Respaldar", Name = "Bck", Width = 78, Resizable = DataGridViewTriState.False });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += Grid_CellValueChanged;
        root.Controls.Add(_grid, 0, 3);

        var bottom = new Panel { Dock = DockStyle.Fill };
        _lblCount = new Label { Dock = DockStyle.Left, Width = 320, TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Aceptar", 120); btnOk.Click += BtnOk_Click;
        flow.Controls.AddRange([btnOk, btnCancel]);
        bottom.Controls.Add(flow);
        bottom.Controls.Add(_lblCount);
        root.Controls.Add(bottom, 0, 4);

        Controls.Add(root);
        AcceptButton = btnOk;
    }

    private static Button Mini(string text, Action onClick)
    {
        var b = AppTheme.MakeSecondaryButton(text, 165, 26);
        b.Margin = new Padding(0, 2, 6, 0);
        b.Click += (_, _) => onClick();
        return b;
    }

    private IEnumerable<DeploymentTarget> Visibles()
    {
        var q = _txtFind.Text.Trim();
        return q.Length == 0
            ? _servers
            : _servers.Where(t => t.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                               || (t.Host ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void Render()
    {
        _suppress = true;
        _grid.Rows.Clear();
        foreach (var t in Visibles())
        {
            int i = _conRespaldo
                ? _grid.Rows.Add(_incluir.Contains(t.Id), t.Nombre, t.Host, _respaldar.Contains(t.Id))
                : _grid.Rows.Add(_incluir.Contains(t.Id), t.Nombre, t.Host);
            _grid.Rows[i].Tag = t;
        }
        _suppress = false;
        UpdateCount();
    }

    private void Grid_CellValueChanged(object? s, DataGridViewCellEventArgs e)
    {
        if (_suppress || e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not DeploymentTarget t) return;
        var col = _grid.Columns[e.ColumnIndex].Name;
        if (col == "Inc")
        {
            bool on = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Inc"].Value);
            if (on) { _incluir.Add(t.Id); _respaldar.Add(t.Id); }   // al incluir, se respalda por omisión
            else { _incluir.Remove(t.Id); }
            // reflejar el respaldo por omisión en la casilla (si esa columna existe en este modo)
            if (_conRespaldo)
            { _suppress = true; _grid.Rows[e.RowIndex].Cells["Bck"].Value = _respaldar.Contains(t.Id) && on; _suppress = false; }
        }
        else if (col == "Bck")
        {
            bool on = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Bck"].Value);
            if (on) _respaldar.Add(t.Id); else _respaldar.Remove(t.Id);
        }
        UpdateCount();
    }

    private void SetVisibles(bool incluir, bool on)
    {
        foreach (var t in Visibles())
        {
            var set = incluir ? _incluir : _respaldar;
            if (on) { set.Add(t.Id); if (incluir) _respaldar.Add(t.Id); }
            else { set.Remove(t.Id); }
        }
        Render();
    }

    private void CargarDePerfil(object? s, EventArgs e)
    {
        if (_cbxProfile.SelectedIndex <= 0) return;
        var perfil = _profiles[_cbxProfile.SelectedIndex - 1];
        foreach (var pt in perfil.ProfileTargets) { _incluir.Add(pt.TargetId); _respaldar.Add(pt.TargetId); }
        Render();
    }

    private void UpdateCount()
    {
        if (_conRespaldo)
        {
            int backup = _respaldar.Count(id => _incluir.Contains(id));
            _lblCount.Text = $"{_incluir.Count} servidor(es) · {backup} con respaldo";
        }
        else
        {
            _lblCount.Text = $"{_incluir.Count} servidor(es)  ·  se respaldarán todos al desplegar";
        }
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        SelectedTargetIds = _incluir.ToList();
        BackupTargetIds = new HashSet<int>(_respaldar.Where(_incluir.Contains));
        if (SelectedTargetIds.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un servidor a incluir.", "Sin servidores", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
