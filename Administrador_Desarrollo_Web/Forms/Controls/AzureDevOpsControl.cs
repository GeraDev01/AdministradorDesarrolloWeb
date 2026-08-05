using System.Text.Json;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class AzureDevOpsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AzureDevOpsService _devOps;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;
    private readonly SettingsService _settings;
    private readonly NotificationService _notifications;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private TextBox _txtTitle = null!;
    private ComboBox _cmbAutoSync = null!;
    private ComboBox _cbxSaved = null!;
    private List<DevOpsSavedFilter> _savedFilters = [];
    private bool _suppressFilter;
    private Button _btnClearFilters = null!;
    private Label _lblStatus = null!;
    private Panel _pnlStats = null!;
    private Button _btnSync = null!;
    private Panel _notifBar = null!;
    private Label _lblNotifText = null!;
    private System.Windows.Forms.Timer _syncTimer = null!;
    private NotifyIcon _notifyIcon = null!;

    private List<DevOpsTicket> _all = [];
    private HashSet<int> _watchedIds = [];
    private CancellationTokenSource? _cts;

    // ── Estado de filtros estilo grid (por columna) ──────────────────
    private const string EmptyLabel = "(vacío)";
    private static readonly string[] FilterableCols =
        ["WorkItemType", "State", "AssignedTo", "Priority", "IterationPath", "Tags"];

    // Por columna: conjunto de valores PERMITIDOS (case-insensitive). Sin entrada = sin filtro.
    private readonly Dictionary<string, HashSet<string>> _columnFilters = new();
    // Texto base del encabezado (sin glifo) para reconstruirlo al cambiar el filtro.
    private readonly Dictionary<string, string> _baseHeaderText = new();
    // Selector de valor por columna (Tags se trata aparte por ser multivalor).
    private readonly Dictionary<string, Func<DevOpsTicket, string>> _colSelector = new()
    {
        ["WorkItemType"]  = t => t.WorkItemType,
        ["State"]         = t => t.State,
        ["AssignedTo"]    = t => t.AssignedTo,
        ["Priority"]      = t => t.Priority,
        ["IterationPath"] = t => t.IterationPath,
    };
    private Form? _activeFilterPopup;

    public AzureDevOpsControl(AppDbContext db, AzureDevOpsService devOps,
        AuditService audit, CurrentUserContext currentUser, SettingsService settings,
        NotificationService notifications)
    {
        _db = db; _devOps = devOps; _audit = audit;
        _currentUser = currentUser; _settings = settings; _notifications = notifications;
        BuildUI();
        LoadWatched();
        LoadSavedFilters();
        LoadData();
        LoadAutoSyncSetting();
    }

    private void BuildUI()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.ContentBg;

        // ── Barra de notificación (oculta inicialmente) ───────────
        _notifBar = new Panel
        {
            Dock = DockStyle.Top, Height = 0,
            BackColor = Color.FromArgb(254, 243, 199), Padding = new Padding(6, 0, 6, 0)
        };
        var btnCloseNotif = new Button
        {
            Text = "✕", Size = new Size(26, 26), Location = new Point(6, 7),
            FlatStyle = FlatStyle.Flat, Font = AppTheme.SmallFont, Cursor = Cursors.Hand
        };
        btnCloseNotif.FlatAppearance.BorderSize = 0;
        btnCloseNotif.Click += (_, _) => CollapseNotifBar();
        _lblNotifText = new Label
        {
            AutoSize = true, Location = new Point(38, 12),
            Font = AppTheme.DefaultFont, ForeColor = Color.FromArgb(92, 67, 0)
        };
        _notifBar.Controls.AddRange([btnCloseNotif, _lblNotifText]);

        // ── Layout principal ──────────────────────────────────────
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(16),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ───────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 6, 0, 6)
        };

        _btnSync = AppTheme.MakePrimaryButton("⟳  Sincronizar", 130);
        _btnSync.Click += BtnSync_Click;

        var btnSyncSel = AppTheme.MakeSecondaryButton("⚙ Selectiva…", 120);
        btnSyncSel.Click += BtnSyncSelectiva_Click;

        _cmbAutoSync = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.DefaultFont };
        _cmbAutoSync.Items.AddRange(["Sin auto-sync", "Cada 5 min", "Cada 15 min", "Cada 30 min", "Cada hora"]);
        _cmbAutoSync.SelectedIndex = 0;
        _cmbAutoSync.SelectedIndexChanged += CmbAutoSync_Changed;

        _txtTitle = new TextBox { Width = 175, Height = 28, Font = AppTheme.DefaultFont, PlaceholderText = "Título contiene..." };
        _txtTitle.TextChanged += (_, _) => { if (!_suppressFilter) Filter(); };

        _txtSearch = new TextBox { Width = 190, Height = 28, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar en todo..." };
        _txtSearch.TextChanged += (_, _) => { if (!_suppressFilter) Filter(); };

        _btnClearFilters = AppTheme.MakeSecondaryButton("🧹  Limpiar", 105);
        _btnClearFilters.Enabled = false;
        _btnClearFilters.Click += BtnClearFilters_Click;

        _cbxSaved = new ComboBox { Width = 165, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.DefaultFont };
        _cbxSaved.SelectedIndexChanged += CbxSaved_Changed;
        var btnSaveFilter = AppTheme.MakeSecondaryButton("💾 Guardar", 105);
        btnSaveFilter.Click += BtnSaveFilter_Click;
        var btnDelFilter = AppTheme.MakeSecondaryButton("🗑", 36);
        btnDelFilter.Click += BtnDelFilter_Click;

        var btnExport = AppTheme.MakeSecondaryButton("⬇  Exportar", 105);
        btnExport.Click += BtnExport_Click;

        foreach (Control c in new Control[]
            { _btnSync, Spacer(4), btnSyncSel, Spacer(6), _cmbAutoSync, Spacer(10), _txtTitle, Spacer(6), _txtSearch, Spacer(6),
              _btnClearFilters, Spacer(12), _cbxSaved, Spacer(4), btnSaveFilter, Spacer(4), btnDelFilter, Spacer(8), btnExport })
            toolbar.Controls.Add(c);

        // ── Stats cards (dashboard, recalculan con el filtro) ─────
        _pnlStats = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // ── Status bar ────────────────────────────────────────────
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        // ── Grid ─────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        // Columnas definidas manualmente: evitar que reasignar DataSource regenere/borre
        // columnas (glifos, SortMode, MinimumWidth) en cualquier versión de WinForms.
        _grid.AutoGenerateColumns = false;
        _grid.Columns.AddRange(
            Col("", "WatchIndicator", 26),
            Col("ID", "ExternalId", 65),
            Col("Tipo", "WorkItemType", 110),
            Col("Título", "Title", 260),
            Col("Estado", "State", 100),
            Col("Prioridad", "Priority", 90),
            Col("Asignado a", "AssignedTo", 150),
            Col("Iteración", "IterationPath", 150),
            Col("Tags", "Tags", 130),
            Col("Pts", "StoryPoints", 45),
            Col("💬", "CommentCount", 40),
            Col("Actualizado", "UpdatedAtExternal", 130)
        );
        _grid.Columns["UpdatedAtExternal"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        _grid.Columns["WatchIndicator"]!.DataPropertyName = "";

        // El mapeo fila→ticket usa el índice de la lista; deshabilitar orden evita desincronización.
        foreach (DataGridViewColumn c in _grid.Columns)
            c.SortMode = DataGridViewColumnSortMode.NotSortable;

        // Encabezados base + glifo de filtro en columnas filtrables.
        foreach (var col in FilterableCols)
            if (_grid.Columns[col] is { } gc)
            {
                _baseHeaderText[col] = gc.HeaderText;
                gc.MinimumWidth = 95;
            }
        RefreshAllHeaderGlyphs();

        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.MouseDown += Grid_MouseDown;

        outer.Controls.Add(toolbar,    0, 0);
        outer.Controls.Add(_pnlStats,  0, 1);
        outer.Controls.Add(_lblStatus, 0, 2);
        outer.Controls.Add(_grid,      0, 3);

        Controls.Add(outer);
        Controls.Add(_notifBar); // encima del outer (DockStyle.Top se procesa en orden inverso de Add)

        // ── Timer de auto-sync ────────────────────────────────────
        _syncTimer = new System.Windows.Forms.Timer();
        _syncTimer.Tick += async (_, _) => await DoSyncAsync(silent: true);

        // ── NotifyIcon ────────────────────────────────────────────
        _notifyIcon = new NotifyIcon { Icon = SystemIcons.Information };
    }

    private void LoadWatched()
    {
        var user = _currentUser.User?.Username ?? "";
        _watchedIds = [.. _db.WatchedTickets
            .Where(w => w.WatchedByUser == user)
            .Select(w => w.DevOpsTicketId)];
    }

    private void LoadData()
    {
        _all = _db.DevOpsTickets.OrderByDescending(t => t.UpdatedAtExternal).ToList();

        // Los filtros activos SOBREVIVEN a la recarga/sincronización; solo se podan
        // valores que ya no existen para no ocultar todo por un filtro "fantasma".
        PrunePhantomFilters();
        RefreshAllHeaderGlyphs();

        Filter(); // recalcula la rejilla y el dashboard con los filtros activos
    }

    private void PrunePhantomFilters()
    {
        foreach (var col in FilterableCols)
        {
            if (!_columnFilters.TryGetValue(col, out var set)) continue;
            var valid = new HashSet<string>(
                GetDistinctValues(col).Select(fv => fv.Value), StringComparer.OrdinalIgnoreCase);
            set.IntersectWith(valid);
            if (set.Count == 0) _columnFilters.Remove(col);
        }
    }

    private void LoadAutoSyncSetting()
    {
        var v = _settings.Get(SettingsService.Keys.DevOpsSyncIntervalMinutes);
        int minutes = int.TryParse(v, out var m) ? m : 0;
        _cmbAutoSync.SelectedIndex = minutes switch { 5 => 1, 15 => 2, 30 => 3, 60 => 4, _ => 0 };
    }

    private void CmbAutoSync_Changed(object? s, EventArgs e)
    {
        _syncTimer.Stop();
        int minutes = _cmbAutoSync.SelectedIndex switch { 1 => 5, 2 => 15, 3 => 30, 4 => 60, _ => 0 };
        _settings.Set(SettingsService.Keys.DevOpsSyncIntervalMinutes, minutes.ToString(), false, "Intervalo auto-sync DevOps");
        if (minutes > 0)
        {
            _syncTimer.Interval = minutes * 60_000;
            _syncTimer.Start();
        }
    }

    private void Filter()
    {
        var q = _txtSearch.Text.Trim().ToLower();
        var title = _txtTitle.Text.Trim();

        var filtered = _all
            .Where(t => MatchesColumnFilters(t)
                     && (string.IsNullOrEmpty(q) || MatchGlobalSearch(t, q))
                     && (title.Length == 0 || t.Title.Contains(title, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _grid.DataSource = filtered;
        UpdateClearButtonState();
        BuildStats(filtered); // el dashboard refleja SIEMPRE lo filtrado

        int activeFilters = _columnFilters.Count + (q.Length > 0 ? 1 : 0) + (title.Length > 0 ? 1 : 0);
        var note = activeFilters > 0 ? $"  |  🔎 {activeFilters} filtro(s) activo(s)" : "";

        // Lo que falta por priorizar. Se cuenta sobre los ABIERTOS: exigir prioridad de algo ya
        // cerrado no cambia ninguna decisión.
        int sinPrioridad = _all.Count(t => t.SinPrioridadDefinida && !AzureDevOpsService.EsCerrado(t.State));
        var pendiente = sinPrioridad > 0 ? $"  |  ⚠ {sinPrioridad} sin prioridad definida" : "";

        _lblStatus.Text = $"{filtered.Count} de {_all.Count} tickets  |  Última sync: {_all.MaxBy(t => t.SyncedAt)?.SyncedAt.ToLocalTime():dd/MM/yyyy HH:mm}  |  Vigilados: {_watchedIds.Count}{note}{pendiente}";
        _lblStatus.ForeColor = sinPrioridad > 0 ? AppTheme.Warning : AppTheme.TextSecondary;
    }

    // Búsqueda de texto global: recorre todos los campos relevantes (AND con los filtros de columna).
    private static bool MatchGlobalSearch(DevOpsTicket t, string q)
        => t.ExternalId.ToString().Contains(q)
        || t.Title.ToLower().Contains(q)
        || t.State.ToLower().Contains(q)
        || t.WorkItemType.ToLower().Contains(q)
        || t.AssignedTo.ToLower().Contains(q)
        || t.IterationPath.ToLower().Contains(q)
        || t.Tags.ToLower().Contains(q)
        || t.AreaPath.ToLower().Contains(q)
        || (t.Priority?.ToLower().Contains(q) ?? false);

    // AND entre columnas, OR dentro de una columna. Sin entrada en _columnFilters = sin filtro.
    private bool MatchesColumnFilters(DevOpsTicket t)
    {
        foreach (var (col, set) in _columnFilters)
        {
            if (set.Count == 0) continue;
            if (col == "Tags")
            {
                var segs = SplitTags(t.Tags);
                bool pass = segs.Count == 0 ? set.Contains("") : segs.Any(set.Contains);
                if (!pass) return false;
            }
            else
            {
                var val = Norm(_colSelector[col](t));
                if (!set.Contains(val)) return false;
            }
        }
        return true;
    }

    // ── Filtrado estilo grid: popup por columna ───────────────────
    private void Grid_ColumnHeaderMouseClick(object? s, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        var name = _grid.Columns[e.ColumnIndex].Name;
        if (Array.IndexOf(FilterableCols, name) >= 0)
            OpenColumnFilterPopup(e.ColumnIndex, name);
    }

    private void OpenColumnFilterPopup(int colIndex, string colName)
    {
        // Cerrar cualquier popup previo
        if (_activeFilterPopup is { IsDisposed: false })
        {
            try { _activeFilterPopup.Close(); } catch { }
        }

        var distinct = GetDistinctValues(colName);
        if (distinct.Count == 0) return;

        var currentFilter = _columnFilters.TryGetValue(colName, out var existing) ? existing : null;
        // Conjunto de trabajo: si no hay filtro previo, todo seleccionado (convención Excel).
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fv in distinct)
            if (currentFilter == null || currentFilter.Contains(fv.Value))
                selected.Add(fv.Value);

        var popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition   = FormStartPosition.Manual,
            ShowInTaskbar   = false,
            TopMost         = true,
            Size            = new Size(290, 400),
            BackColor       = AppTheme.Border,   // borde de 1px
            Padding         = new Padding(1)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = AppTheme.CardBg,
            RowCount = 5, ColumnCount = 1, Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var lblTitle = new Label
        {
            Text = $"Filtrar: {_baseHeaderText.GetValueOrDefault(colName, colName)}",
            Dock = DockStyle.Fill, Font = AppTheme.BoldFont,
            ForeColor = AppTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft
        };
        var txtFind = new TextBox { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, PlaceholderText = "Filtrar valores..." };
        var chkAll  = new CheckBox { Dock = DockStyle.Fill, Text = "(Seleccionar todo)", Font = AppTheme.DefaultFont, Checked = selected.Count == distinct.Count };
        var clb     = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Font = AppTheme.DefaultFont, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };

        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var btnApply = AppTheme.MakePrimaryButton("Aplicar", 90, 28);
        var btnClear = AppTheme.MakeSecondaryButton("Quitar filtro", 110, 28);
        btnApply.Margin = new Padding(4, 4, 0, 0);
        btnClear.Margin = new Padding(4, 4, 0, 0);
        pnlBtns.Controls.Add(btnApply);
        pnlBtns.Controls.Add(btnClear);

        bool suppress = false;

        void Populate()
        {
            var find = txtFind.Text.Trim();
            suppress = true;
            clb.BeginUpdate();
            clb.Items.Clear();
            foreach (var fv in distinct)
            {
                if (find.Length > 0 && fv.Display.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int idx = clb.Items.Add(fv);
                clb.SetItemChecked(idx, selected.Contains(fv.Value));
            }
            clb.EndUpdate();
            suppress = false;
        }

        clb.ItemCheck += (_, ev) =>
        {
            if (suppress) return;
            var fv = (FilterValue)clb.Items[ev.Index];
            if (ev.NewValue == CheckState.Checked) selected.Add(fv.Value);
            else selected.Remove(fv.Value);
        };

        chkAll.CheckedChanged += (_, _) =>
        {
            if (suppress) return;
            suppress = true;
            bool on = chkAll.Checked;
            for (int i = 0; i < clb.Items.Count; i++)
            {
                clb.SetItemChecked(i, on);
                var fv = (FilterValue)clb.Items[i];
                if (on) selected.Add(fv.Value); else selected.Remove(fv.Value);
            }
            suppress = false;
        };

        txtFind.TextChanged += (_, _) => Populate();

        btnApply.Click += (_, _) =>
        {
            ApplyColumnFilter(colName, selected);
            popup.Close();
        };
        btnClear.Click += (_, _) =>
        {
            _columnFilters.Remove(colName);
            UpdateHeaderGlyph(colName);
            Filter();
            popup.Close();
        };

        layout.Controls.Add(lblTitle, 0, 0);
        layout.Controls.Add(txtFind,  0, 1);
        layout.Controls.Add(chkAll,   0, 2);
        layout.Controls.Add(clb,      0, 3);
        layout.Controls.Add(pnlBtns,  0, 4);
        popup.Controls.Add(layout);

        popup.Deactivate += (_, _) => { try { popup.Close(); } catch { } };
        popup.FormClosed += (_, _) => { _activeFilterPopup = null; popup.Dispose(); };

        Populate();

        var rect = _grid.GetCellDisplayRectangle(colIndex, -1, false);
        var anchor = _grid.PointToScreen(new Point(rect.Left, rect.Bottom));
        // Mantener el popup dentro de la pantalla.
        var screen = Screen.FromControl(_grid).WorkingArea;
        if (anchor.X + popup.Width > screen.Right)  anchor.X = screen.Right  - popup.Width;
        if (anchor.Y + popup.Height > screen.Bottom) anchor.Y = screen.Bottom - popup.Height;
        popup.Location = anchor;

        _activeFilterPopup = popup;
        popup.Show();
        txtFind.Focus();
    }

    private void ApplyColumnFilter(string colName, HashSet<string> selected)
    {
        // Recalcular el total al aplicar: evita usar un conteo obsoleto si una
        // auto-sync agregó valores nuevos mientras el popup estaba abierto.
        int totalDistinct = GetDistinctValues(colName).Count;
        // Todo o nada seleccionado ⇒ sin filtro (evita la trampa de "rejilla vacía").
        if (selected.Count == 0 || selected.Count >= totalDistinct)
            _columnFilters.Remove(colName);
        else
            _columnFilters[colName] = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

        UpdateHeaderGlyph(colName);
        Filter();
    }

    private List<FilterValue> GetDistinctValues(string colName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (colName == "Tags")
        {
            foreach (var t in _all)
            {
                var segs = SplitTags(t.Tags);
                if (segs.Count == 0) values.Add("");
                else foreach (var seg in segs) values.Add(seg);
            }
        }
        else
        {
            var sel = _colSelector[colName];
            foreach (var t in _all) values.Add(Norm(sel(t)));
        }

        return values
            .OrderBy(v => v.Length == 0 ? 1 : 0)                 // vacíos al final
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterValue { Value = v, Display = v.Length == 0 ? EmptyLabel : v })
            .ToList();
    }

    private void RefreshAllHeaderGlyphs()
    {
        foreach (var col in FilterableCols) UpdateHeaderGlyph(col);
    }

    private void UpdateHeaderGlyph(string colName)
    {
        if (_grid.Columns[colName] is not { } c) return;
        var baseText = _baseHeaderText.GetValueOrDefault(colName, c.HeaderText);
        c.HeaderText = _columnFilters.TryGetValue(colName, out var set) && set.Count > 0
            ? $"{baseText}  ▼ ({set.Count})"
            : $"{baseText}  ▾";
    }

    private void UpdateClearButtonState()
        => _btnClearFilters.Enabled = _columnFilters.Count > 0 || _txtSearch.Text.Length > 0 || _txtTitle.Text.Length > 0;

    private void BtnClearFilters_Click(object? s, EventArgs e)
    {
        _suppressFilter = true;
        _columnFilters.Clear();
        _txtSearch.Clear();
        _txtTitle.Clear();
        if (_cbxSaved.Items.Count > 0) _cbxSaved.SelectedIndex = 0;
        _suppressFilter = false;
        RefreshAllHeaderGlyphs();
        Filter();
    }

    // ── Filtros predefinidos (guardados) ──────────────────────────
    private void LoadSavedFilters()
    {
        _savedFilters = _db.DevOpsSavedFilters.OrderBy(f => f.Name).ToList();
        _suppressFilter = true;
        _cbxSaved.Items.Clear();
        _cbxSaved.Items.Add("(Filtros guardados)");
        foreach (var f in _savedFilters) _cbxSaved.Items.Add(f.Name);
        _cbxSaved.SelectedIndex = 0;
        _suppressFilter = false;
    }

    private void CbxSaved_Changed(object? s, EventArgs e)
    {
        if (_suppressFilter || _cbxSaved.SelectedIndex <= 0) return;
        ApplySavedFilter(_savedFilters[_cbxSaved.SelectedIndex - 1]);
    }

    private void ApplySavedFilter(DevOpsSavedFilter f)
    {
        _suppressFilter = true;
        _txtSearch.Text = f.GlobalSearch ?? "";
        _txtTitle.Text  = f.TitleContains ?? "";
        _columnFilters.Clear();
        foreach (var kv in ColumnFiltersFromJson(f.ColumnFiltersJson))
            _columnFilters[kv.Key] = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
        PrunePhantomFilters();
        RefreshAllHeaderGlyphs();
        _suppressFilter = false;
        Filter();
    }

    private void BtnSaveFilter_Click(object? s, EventArgs e)
    {
        var name = Prompt("Nombre del filtro:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var existing = _db.DevOpsSavedFilters.FirstOrDefault(f => f.Name == name);
        if (existing == null) { existing = new DevOpsSavedFilter { Name = name.Trim(), CreatedAt = DateTime.UtcNow }; _db.DevOpsSavedFilters.Add(existing); }
        existing.GlobalSearch = _txtSearch.Text.Trim();
        existing.TitleContains = _txtTitle.Text.Trim();
        existing.ColumnFiltersJson = ColumnFiltersToJson();
        _db.SaveChanges();

        LoadSavedFilters();
        int idx = _cbxSaved.Items.IndexOf(name);
        if (idx >= 0) { _suppressFilter = true; _cbxSaved.SelectedIndex = idx; _suppressFilter = false; }
    }

    private void BtnDelFilter_Click(object? s, EventArgs e)
    {
        if (_cbxSaved.SelectedIndex <= 0) { MessageBox.Show("Selecciona un filtro guardado.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var f = _savedFilters[_cbxSaved.SelectedIndex - 1];
        if (MessageBox.Show($"¿Eliminar el filtro '{f.Name}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var t = _db.DevOpsSavedFilters.Find(f.Id);
        if (t != null) { _db.DevOpsSavedFilters.Remove(t); _db.SaveChanges(); }
        LoadSavedFilters();
    }

    private string ColumnFiltersToJson() =>
        JsonSerializer.Serialize(_columnFilters.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));

    private static Dictionary<string, List<string>> ColumnFiltersFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? []; }
        catch { return []; }
    }

    private string? Prompt(string label)
    {
        using var frm = new Form { Text = "Guardar filtro", Size = new Size(380, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = AppTheme.ContentBg, Font = AppTheme.DefaultFont };
        var lbl = new Label { Text = label, Location = new Point(16, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(16, 42), Width = 330 };
        var ok = AppTheme.MakePrimaryButton("Guardar", 100); ok.Location = new Point(150, 82); ok.DialogResult = DialogResult.OK;
        var cancel = AppTheme.MakeSecondaryButton("Cancelar", 90); cancel.Location = new Point(256, 82); cancel.DialogResult = DialogResult.Cancel;
        frm.Controls.AddRange([lbl, txt, ok, cancel]); frm.AcceptButton = ok; frm.CancelButton = cancel;
        return frm.ShowDialog(FindForm()) == DialogResult.OK ? txt.Text.Trim() : null;
    }

    private static string Norm(string? s) => (s ?? "").Trim();

    private static List<string> SplitTags(string? tags)
        => (tags ?? "")
            .Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private sealed class FilterValue
    {
        public string Display { get; init; } = "";
        public string Value { get; init; } = "";
        public override string ToString() => Display;
    }

    private void BuildStats(List<DevOpsTicket> data)
    {
        _pnlStats.Controls.Clear();

        string topAssignee = data.Where(t => !string.IsNullOrWhiteSpace(t.AssignedTo))
            .GroupBy(t => t.AssignedTo).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()}").FirstOrDefault() ?? "0";

        var stats = new (string label, string value, Color color)[]
        {
            ("Mostrados",   data.Count.ToString(),                                     AppTheme.SidebarActive),
            ("Activos",     data.Count(t => IsActive(t.State)).ToString(),             AppTheme.Warning),
            ("Resueltos",   data.Count(t => IsResolved(t.State)).ToString(),           AppTheme.Success),
            ("Bugs",        data.Count(t => TypeIs(t, "Bug")).ToString(),              AppTheme.Danger),
            ("User Stories",data.Count(t => TypeIs(t, "User Story")).ToString(),       Color.FromArgb(139, 92, 246)),
            ("Tareas",      data.Count(t => TypeIs(t, "Task")).ToString(),             AppTheme.TextSecondary),
            ("Story Points",data.Sum(t => t.StoryPoints ?? 0).ToString("0.#"),         Color.FromArgb(20, 184, 166)),
            ("Sin asignar", data.Count(t => string.IsNullOrWhiteSpace(t.AssignedTo)).ToString(), Color.FromArgb(234, 88, 12)),
            ("Máx x persona", topAssignee,                                             Color.FromArgb(37, 99, 235)),
        };

        int x = 0;
        foreach (var (lbl, val, color) in stats)
        {
            var card = MakeStatCard(lbl, val, color);
            card.Location = new Point(x, 2);
            _pnlStats.Controls.Add(card);
            x += 130;
        }
    }

    // ── Sync ──────────────────────────────────────────────────────
    private async void BtnSync_Click(object? s, EventArgs e) => await DoSyncAsync(silent: false);

    private async void BtnSyncSelectiva_Click(object? s, EventArgs e)
    {
        if (!_devOps.IsEnabled)
        {
            MessageBox.Show("Azure DevOps no está habilitado. Configúralo en Configuración.",
                "Sin configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var tipos    = _all.Select(t => t.WorkItemType).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();
        var estados  = _all.Select(t => t.State).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
        var devs = _db.Developers.Where(d => d.IsActive).ToList();
        using var dlg = new DevOpsSyncOptionsForm(tipos, estados, devs);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        await DoSyncAsync(silent: false, dlg.Filter);
    }

    private async Task DoSyncAsync(bool silent, DevOpsSyncFilter? filter = null)
    {
        if (!_devOps.IsEnabled)
        {
            if (!silent)
                MessageBox.Show("Azure DevOps no está habilitado. Configure la integración en Configuración.",
                    "Sin configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnSync.Enabled = false;
        _btnSync.Text    = "⟳  Sincronizando...";
        _lblStatus.Text  = "Conectando con Azure DevOps...";

        _cts = new CancellationTokenSource();
        try
        {
            var result = await _devOps.SyncToLocalAsync(_watchedIds, filter, _cts.Token);
            LoadData();

            if (result.WatchedChanges.Count > 0)
                ShowWatchedNotification(result.WatchedChanges);
            else if (!silent)
                MessageBox.Show($"Sincronización completada.\n{result.Added} nuevos, {result.Updated} actualizados.",
                    "Azure DevOps", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show($"Error al sincronizar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSync.Enabled = true;
            _btnSync.Text    = "⟳  Sincronizar";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ShowWatchedNotification(IReadOnlyList<(DevOpsTicket Ticket, string Reason)> changes)
    {
        var msg = changes.Count == 1
            ? $"🔔 #{changes[0].Ticket.ExternalId} — {changes[0].Reason}"
            : $"🔔 {changes.Count} tickets vigilados tienen cambios";

        // Banner in-app
        _lblNotifText.Text = msg;
        _notifBar.Height   = 40;

        // Notificación del sistema operativo
        try
        {
            _notifyIcon.Visible          = true;
            _notifyIcon.BalloonTipTitle  = "Azure DevOps — Cambios detectados";
            _notifyIcon.BalloonTipText   = string.Join("\n", changes.Select(c => $"#{c.Ticket.ExternalId}: {c.Reason}"));
            _notifyIcon.BalloonTipIcon   = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(8000);

            Task.Delay(9000).ContinueWith(_ =>
            {
                if (!IsDisposed)
                    try { Invoke(() => _notifyIcon.Visible = false); } catch { }
            });
        }
        catch { /* sin soporte de system tray en este entorno */ }
    }

    private void CollapseNotifBar() => _notifBar.Height = 0;

    // ── Watch / Unwatch ───────────────────────────────────────────
    private void ToggleWatch(DevOpsTicket ticket)
    {
        var user = _currentUser.User?.Username ?? "";
        if (_watchedIds.Contains(ticket.Id))
        {
            var w = _db.WatchedTickets.FirstOrDefault(x => x.DevOpsTicketId == ticket.Id && x.WatchedByUser == user);
            if (w != null) { _db.WatchedTickets.Remove(w); _db.SaveChanges(); }
            _watchedIds.Remove(ticket.Id);
        }
        else
        {
            try
            {
                _db.WatchedTickets.Add(new WatchedTicket
                    { DevOpsTicketId = ticket.Id, WatchedByUser = user, WatchedSince = DateTime.UtcNow });
                _db.SaveChanges();
                _watchedIds.Add(ticket.Id);
            }
            catch { /* ya existía */ }
        }
        _grid.Refresh();
        Filter(); // actualiza contador en status bar
    }

    // ── Comentarios ───────────────────────────────────────────────
    private async void ShowCommentsDialog(DevOpsTicket ticket)
    {
        var dlg = new Form
        {
            Text = $"Comentarios — #{ticket.ExternalId}: {ticket.Title}",
            Size = new Size(700, 560), StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(500, 400), BackColor = AppTheme.ContentBg,
            Font = AppTheme.DefaultFont
        };

        // Panel de comentarios (scrollable)
        var pnlComments = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = Color.White, Padding = new Padding(12)
        };
        var lblLoading = new Label
        {
            Text = "Cargando comentarios...", AutoSize = true,
            Location = new Point(12, 12), Font = AppTheme.DefaultFont,
            ForeColor = AppTheme.TextSecondary
        };
        pnlComments.Controls.Add(lblLoading);

        // Panel de nuevo comentario (abajo)
        var pnlNew = new Panel
        {
            Dock = DockStyle.Bottom, Height = 120,
            BackColor = AppTheme.ContentBg, Padding = new Padding(12, 8, 12, 8)
        };
        var lblNew = new Label
        {
            Text = "Nuevo comentario:", Location = new Point(12, 10),
            AutoSize = true, Font = AppTheme.BoldFont
        };
        var txtNew = new TextBox
        {
            Multiline = true, Location = new Point(12, 30),
            Size = new Size(dlg.ClientSize.Width - 140, 52),
            Font = AppTheme.DefaultFont, ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        var btnSend = AppTheme.MakePrimaryButton("Enviar 💬", 110);
        btnSend.Location = new Point(dlg.ClientSize.Width - 122, 30);
        btnSend.Anchor   = AnchorStyles.Right | AnchorStyles.Top;
        btnSend.Click += async (_, _) =>
        {
            var text = txtNew.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            btnSend.Enabled = false;
            btnSend.Text    = "Enviando...";
            try
            {
                await _devOps.PostCommentAsync(ticket.ExternalId, text);
                txtNew.Clear();
                // Recargar comentarios
                await RefreshComments(pnlComments, ticket.ExternalId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al enviar comentario:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { btnSend.Enabled = true; btnSend.Text = "Enviar 💬"; }
        };

        pnlNew.Controls.AddRange([lblNew, txtNew, btnSend]);
        dlg.Controls.AddRange([pnlComments, pnlNew]);
        dlg.Show(ParentForm);

        // Cargar comentarios después de mostrar el dialog
        await RefreshComments(pnlComments, ticket.ExternalId);
    }

    private async Task RefreshComments(Panel pnlComments, int externalId)
    {
        List<DevOpsComment> comments;
        try
        {
            comments = await _devOps.GetCommentsAsync(externalId);
        }
        catch (Exception ex)
        {
            pnlComments.Controls.Clear();
            pnlComments.Controls.Add(new Label
            {
                Text = $"Error al cargar comentarios: {ex.Message}",
                AutoSize = true, Location = new Point(12, 12),
                ForeColor = AppTheme.Danger, Font = AppTheme.DefaultFont
            });
            return;
        }

        pnlComments.Controls.Clear();

        if (comments.Count == 0)
        {
            pnlComments.Controls.Add(new Label
            {
                Text = "Sin comentarios todavía.", AutoSize = true,
                Location = new Point(12, 12), ForeColor = AppTheme.TextSecondary
            });
            return;
        }

        int y = 8;
        foreach (var c in comments.OrderBy(x => x.CreatedAt))
        {
            var bubble = new Panel
            {
                Location = new Point(8, y),
                Width = pnlComments.ClientSize.Width - 28,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10),
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            bubble.Paint += (s, e) =>
            {
                using var pen = new Pen(AppTheme.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, bubble.Width - 1, bubble.Height - 1);
            };
            var header = new Label
            {
                Text = $"🧑  {c.Author}   ·   {c.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
                AutoSize = true, Location = new Point(10, 8),
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
            };
            var body = new Label
            {
                Text = System.Text.RegularExpressions.Regex.Replace(c.Text, "<[^>]*>", ""),
                AutoSize = true, Location = new Point(10, 28),
                Font = AppTheme.DefaultFont, MaximumSize = new Size(bubble.Width - 20, 0)
            };
            bubble.Controls.AddRange([header, body]);
            bubble.Height = body.Bottom + 10;

            pnlComments.Controls.Add(bubble);
            y += bubble.Height + 8;
        }
        pnlComments.AutoScrollMinSize = new Size(0, y + 12);
    }

    // ── Eventos del grid ──────────────────────────────────────────
    private void Grid_CellFormatting(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Rows.Count <= e.RowIndex || e.RowIndex < 0) return;
        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[e.RowIndex];
        if (ticket == null) return;

        var col = _grid.Columns[e.ColumnIndex].Name;

        if (col == "WatchIndicator")
        {
            e.Value = _watchedIds.Contains(ticket.Id) ? "🔔" : "";
            e.FormattingApplied = true;
            return;
        }
        if (col == "State")
        {
            e.CellStyle.ForeColor = StateColor(ticket.State);
            e.CellStyle.Font = AppTheme.BoldFont;
        }
        if (col == "WorkItemType")
            e.CellStyle.ForeColor = TypeColor(ticket.WorkItemType);

        if (col == "CommentCount" && ticket.CommentCount == 0)
        {
            e.Value = "";
            e.FormattingApplied = true;
        }
    }

    private void Grid_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[e.RowIndex];
        if (ticket == null) return;

        // Doble clic en columna de comentarios → abrir comentarios
        if (_grid.Columns[e.ColumnIndex].Name == "CommentCount")
            ShowCommentsDialog(ticket);
        else
            ShowDetail(ticket);
    }

    private void Grid_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) return;
        _grid.ClearSelection();
        _grid.Rows[hit.RowIndex].Selected = true;

        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[hit.RowIndex];
        if (ticket == null) return;

        bool isWatched = _watchedIds.Contains(ticket.Id);

        var menu = new ContextMenuStrip();
        menu.Items.Add("🔗  Abrir en Azure DevOps", null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(ticket.Url))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ticket.Url) { UseShellExecute = true });
        });
        menu.Items.Add("💬  Ver / agregar comentarios", null, (_, _) => ShowCommentsDialog(ticket));
        menu.Items.Add("👤  Reasignar…", null, (_, _) => ReasignarAsync(ticket));

        // «Mover a estado» = cambiar System.State (la columna del tablero). Se ofrecen los estados
        // vistos en la organización, salvo el actual; DevOps valida la transición.
        var mover = new ToolStripMenuItem("↔  Mover a estado");
        foreach (var estado in _all.Select(t => t.State)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s))
        {
            if (string.Equals(estado, ticket.State, StringComparison.OrdinalIgnoreCase)) continue;
            var destino = estado;
            mover.DropDownItems.Add(destino, null, (_, _) => CambiarEstadoAsync(ticket, destino));
        }
        if (mover.DropDownItems.Count > 0) menu.Items.Add(mover);
        menu.Items.Add(ticket.SinPrioridadDefinida ? "🔧  Definir prioridad (pendiente)…" : "🔧  Cambiar prioridad…",
                       null, (_, _) => CambiarPrioridadAsync(ticket));
        menu.Items.Add("🔍  Ficha: regresiones y devoluciones…", null, (_, _) =>
        {
            using var frm = new DevOpsTicketFichaForm(_devOps, ticket);
            frm.ShowDialog(FindForm());
        });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            isWatched ? "🔕  Dejar de vigilar este ticket" : "🔔  Vigilar este ticket (notificaciones)",
            null, (_, _) => ToggleWatch(ticket));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📋  Ver detalle", null, (_, _) => ShowDetail(ticket));
        menu.Show(_grid, e.Location);
    }

    // ── Reasignar en DevOps ───────────────────────────────────────
    private async void ReasignarAsync(DevOpsTicket ticket)
    {
        var devs = _db.Developers.Where(d => d.IsActive).ToList();
        using var frm = new ReassignWorkItemForm(ticket.ExternalId, ticket.Title, ticket.AssignedTo, devs);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (display, _) = await _devOps.AssignWorkItemAsync(ticket.ExternalId, frm.SelectedEmail);
            _audit.Record(AuditAction.Update, "DevOpsTicket", ticket.ExternalId.ToString(),
                $"Reasignado en DevOps a «{(string.IsNullOrWhiteSpace(display) ? "(sin asignar)" : display)}»");

            // Aviso al desarrollador al que se le asignó (si tiene cuenta).
            if (frm.SelectedDeveloperId is int devId)
                _notifications.NotifyDeveloper(devId, NotificationKind.DevOpsAssigned,
                    $"Te asignaron el ticket #{ticket.ExternalId}",
                    $"{ticket.WorkItemType}: {ticket.Title}", ticket.Url,
                    dedupeKey: $"devops-assign:{ticket.ExternalId}");

            LoadData();
            MessageBox.Show(string.IsNullOrWhiteSpace(display)
                ? "Ticket desasignado en DevOps."
                : $"Ticket reasignado a «{display}» en DevOps.", "Reasignación",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo reasignar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Cambiar prioridad en DevOps ───────────────────────────────
    private async void CambiarPrioridadAsync(DevOpsTicket ticket)
    {
        using var frm = new PriorityPickerForm(ticket.ExternalId, ticket.Priority);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            await _devOps.ChangePriorityAsync(ticket.ExternalId, frm.SelectedPriority, _currentUser.UserId);
            _audit.Record(AuditAction.Update, "DevOpsTicket", ticket.ExternalId.ToString(),
                $"Prioridad cambiada a {frm.SelectedPriority} ({SlaPolicyStore.NombrePrioridad(frm.SelectedPriority)}) en DevOps");
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar la prioridad:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Cambiar de estado (mover de columna) en DevOps ────────────
    private async void CambiarEstadoAsync(DevOpsTicket ticket, string nuevoEstado)
    {
        try
        {
            var estado = await _devOps.ChangeStateAsync(ticket.ExternalId, nuevoEstado);
            _audit.Record(AuditAction.Update, "DevOpsTicket", ticket.ExternalId.ToString(),
                $"Estado cambiado a «{estado}» en DevOps");
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar el estado:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDetail(DevOpsTicket ticket)
    {
        var links = _db.TicketLinks.Where(l => l.DevOpsTicketId == ticket.Id)
            .Include(l => l.FreshDeskTicket).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ID: #{ticket.ExternalId}");
        sb.AppendLine($"Tipo: {ticket.WorkItemType}");
        sb.AppendLine($"Estado: {ticket.State}");
        sb.AppendLine($"Prioridad: {ticket.Priority}");
        sb.AppendLine($"Asignado a: {ticket.AssignedTo}");
        sb.AppendLine($"Área: {ticket.AreaPath}");
        sb.AppendLine($"Iteración: {ticket.IterationPath}");
        if (ticket.StoryPoints.HasValue) sb.AppendLine($"Story Points: {ticket.StoryPoints}");
        if (!string.IsNullOrEmpty(ticket.Tags)) sb.AppendLine($"Tags: {ticket.Tags}");
        sb.AppendLine($"Comentarios: {ticket.CommentCount}");
        sb.AppendLine($"Creado: {ticket.CreatedAtExternal?.ToLocalTime():dd/MM/yyyy}");
        sb.AppendLine($"Actualizado: {ticket.UpdatedAtExternal?.ToLocalTime():dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        if (links.Count > 0)
        {
            sb.AppendLine($"Tickets Freshdesk vinculados ({links.Count}):");
            foreach (var l in links)
                sb.AppendLine($"  • #{l.FreshDeskTicket.ExternalId} — {l.FreshDeskTicket.Subject}");
        }
        else sb.AppendLine("Sin tickets Freshdesk vinculados.");

        MessageBox.Show(sb.ToString(), $"DevOps #{ticket.ExternalId} — {ticket.Title}",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"azure_devops_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var rows = (_grid.DataSource as List<DevOpsTicket>) ?? _all;
        var lines = new List<string> { "ID,Tipo,Título,Estado,Prioridad,Asignado,Iteración,Tags,StoryPoints,Comentarios,Actualizado,URL" };
        foreach (var t in rows)
            lines.Add($"{t.ExternalId},\"{t.WorkItemType}\",\"{t.Title.Replace("\"", "'")}\",\"{t.State}\",\"{t.Priority}\",\"{t.AssignedTo}\",\"{t.IterationPath}\",\"{t.Tags}\",{t.StoryPoints},{t.CommentCount},{t.UpdatedAtExternal?.ToLocalTime():dd/MM/yyyy},\"{t.Url}\"");

        File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
        MessageBox.Show("Exportación completada.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _notifyIcon?.Dispose();
        if (_activeFilterPopup is { IsDisposed: false }) { try { _activeFilterPopup.Close(); } catch { } }
        base.Dispose(disposing);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { LoadWatched(); LoadData(); }  // los filtros de columna persisten
    }

    // ── Helpers ────────────────────────────────────────────────────
    private static bool TypeIs(DevOpsTicket t, string type)
        => string.Equals(Norm(t.WorkItemType), type, StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(string state) => Norm(state).ToLowerInvariant()
        is "active" or "in progress" or "doing" or "in development";
    private static bool IsResolved(string state) => Norm(state).ToLowerInvariant()
        is "resolved" or "closed" or "done" or "completed";

    private static Color StateColor(string state) => state.ToLower() switch
    {
        "active" or "in progress" or "doing" or "in development" => AppTheme.SidebarActive,
        "resolved" or "done" or "completed"                       => AppTheme.Success,
        "closed"                                                   => Color.Gray,
        "new" or "to do"                                           => AppTheme.TextSecondary,
        _                                                          => AppTheme.TextPrimary
    };

    private static Color TypeColor(string type) => type switch
    {
        "Bug"        => AppTheme.Danger,
        "User Story" => Color.FromArgb(139, 92, 246),
        "Task"       => AppTheme.SidebarActive,
        "Feature"    => Color.FromArgb(20, 184, 166),
        "Epic"       => Color.FromArgb(234, 88, 12),
        _            => AppTheme.TextPrimary
    };

    private static DataGridViewTextBoxColumn Col(string header, string prop, int w) => new()
    {
        HeaderText = header, DataPropertyName = prop, Name = prop, Width = w, ReadOnly = true
    };

    private static Panel Spacer(int w) => new() { Width = w, Height = 1 };

    private static Panel MakeStatCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 122, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label
        {
            Text = value, Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = accent, AutoSize = false,
            Location = new Point(12, 10), Size = new Size(100, 36),
            TextAlign = ContentAlignment.MiddleLeft
        });
        card.Controls.Add(new Label
        {
            Text = label, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, AutoSize = false,
            Location = new Point(12, 50), Size = new Size(108, 20)
        });
        return card;
    }
}
