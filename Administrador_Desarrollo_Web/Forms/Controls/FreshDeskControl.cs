using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class FreshDeskControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly FreshDeskService _freshDesk;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cmbSearchField = null!;
    private ComboBox _cmbStatus = null!;
    private ComboBox _cmbPriority = null!;
    private Label _lblStatus = null!;
    private Panel _pnlStats = null!;
    private Button _btnSync = null!;
    private CheckBox _chkSoloMios = null!;
    private TextBox _txtGrupoFiltro = null!;

    private List<FreshDeskTicket> _all = [];
    private CancellationTokenSource? _cts;
    private string? _sortProp;   // columna por la que se ordena al hacer clic en el encabezado
    private bool _sortAsc = true;

    public FreshDeskControl(AppDbContext db, FreshDeskService freshDesk,
        AuditService audit, CurrentUserContext currentUser)
    {
        _db = db; _freshDesk = freshDesk; _audit = audit; _currentUser = currentUser;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.ContentBg;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(16),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));   // toolbar
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));   // filtro de sincronización
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));   // tarjetas
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));   // barra de estado
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // rejilla
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ───────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 6, 0, 6)
        };

        _btnSync = AppTheme.MakePrimaryButton("⟳  Sincronizar", 140);
        _btnSync.Click += BtnSync_Click;

        _cmbSearchField = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.DefaultFont };
        _cmbSearchField.Items.AddRange(["Todos los campos", "ID", "Asunto", "Estado", "Prioridad", "Tipo", "Agente", "Solicitante", "Email", "Fuente", "Tags"]);
        _cmbSearchField.SelectedIndex = 0;
        _cmbSearchField.SelectedIndexChanged += (_, _) => Filter();

        _txtSearch = new TextBox { Width = 200, Height = 28, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar..." };
        _txtSearch.TextChanged += (_, _) => Filter();

        _cmbStatus = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.DefaultFont };
        _cmbStatus.Items.AddRange(["Todos los estados", "Abierto", "Pendiente", "Resuelto", "Cerrado"]);
        _cmbStatus.SelectedIndex = 0;
        _cmbStatus.SelectedIndexChanged += (_, _) => Filter();

        _cmbPriority = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.DefaultFont };
        _cmbPriority.Items.AddRange(["Todas las prioridades", "Baja", "Media", "Alta", "Urgente"]);
        _cmbPriority.SelectedIndex = 0;
        _cmbPriority.SelectedIndexChanged += (_, _) => Filter();

        var btnExport = AppTheme.MakeSecondaryButton("⬇  Exportar", 110);
        btnExport.Click += BtnExport_Click;

        foreach (Control c in new Control[] { _btnSync, Spacer(10), _cmbSearchField, Spacer(4), _txtSearch, Spacer(8), _cmbStatus, Spacer(8), _cmbPriority, Spacer(8), btnExport })
            toolbar.Controls.Add(c);

        // ── Filtro de sincronización: por agente (yo) y/o por grupo ─
        var filtroBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 4, 0, 4)
        };
        var f = _freshDesk.LeerFiltro();
        _chkSoloMios = new CheckBox { Text = "Solo asignados a mí", AutoSize = true, Checked = f.PorAgente, Font = AppTheme.DefaultFont, Margin = new Padding(0, 4, 0, 0) };
        _txtGrupoFiltro = new TextBox { Width = 170, Font = AppTheme.DefaultFont, Text = f.Grupo, PlaceholderText = "nombre o ID" };
        // Se guardan al cambiar (los usa también el sync de fondo). Se enganchan DESPUÉS de fijar el valor.
        _chkSoloMios.CheckedChanged += (_, _) => GuardarFiltro();
        _txtGrupoFiltro.Leave      += (_, _) => GuardarFiltro();

        foreach (Control c in new Control[]
        {
            new Label { Text = "Sincronizar filtrando por:", AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.DefaultFont, Margin = new Padding(0, 6, 8, 0) },
            _chkSoloMios, Spacer(14),
            new Label { Text = "Grupo/depto:", AutoSize = true, Font = AppTheme.DefaultFont, Margin = new Padding(0, 6, 4, 0) },
            _txtGrupoFiltro,
            new Label { Text = "(vacío = sin grupo)", AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, Margin = new Padding(8, 8, 0, 0) },
        })
            filtroBar.Controls.Add(c);

        // ── Stats cards ───────────────────────────────────────────
        _pnlStats = new Panel { Dock = DockStyle.Fill };

        // ── Status bar ────────────────────────────────────────────
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        // ── Grid ─────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;   // columnas manuales estables al re-vincular (orden/glyph)
        _grid.Columns.AddRange(
            Col("ID",           "ExternalId",     70),
            Col("Asunto",       "Subject",        300),
            Col("Estado",       "Status",         90),
            Col("Prioridad",    "Priority",       80),
            Col("Tipo",         "Type",           90),
            Col("Agente",       "AgentName",      140),
            Col("Solicitante",  "RequesterName",  140),
            Col("Fuente",       "Source",         75),
            Col("Creado",       "CreatedAtExternal", 120),
            Col("Actualizado",  "UpdatedAtExternal", 120)
        );
        // El orden lo hace nuestro Grid_ColumnHeaderClick (los List no admiten el orden nativo, que
        // lanzaría excepción). Programmatic desactiva el orden interno pero deja poner la flechita.
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.Columns["CreatedAtExternal"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        _grid.Columns["UpdatedAtExternal"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        _grid.CellFormatting  += Grid_CellFormatting;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.MouseDown       += Grid_MouseDown;
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderClick;   // ordenar al hacer clic en el encabezado

        outer.Controls.Add(toolbar,    0, 0);
        outer.Controls.Add(filtroBar,  0, 1);
        outer.Controls.Add(_pnlStats,  0, 2);
        outer.Controls.Add(_lblStatus, 0, 3);
        outer.Controls.Add(_grid,      0, 4);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        // AsNoTracking: la sincronización de segundo plano escribe con un contexto propio; sin esto, el
        // _db singleton serviría los valores rastreados (viejos) y la rejilla mostraría estados obsoletos.
        _all = _db.FreshDeskTickets.AsNoTracking().OrderByDescending(t => t.UpdatedAtExternal).ToList();
        Filter();
        BuildStats();
    }

    private void Filter()
    {
        var search    = _txtSearch.Text.Trim().ToLower();
        var field     = _cmbSearchField.SelectedIndex;
        int? status   = _cmbStatus.SelectedIndex switch   { 1 => 2, 2 => 3, 3 => 4, 4 => 5, _ => null };
        int? priority = _cmbPriority.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => null };

        IEnumerable<FreshDeskTicket> q = _all
            .Where(t =>
                (string.IsNullOrEmpty(search) || MatchFDField(t, search, field))
                && (status   == null || t.Status   == status)
                && (priority == null || t.Priority == priority));

        // Orden por la columna elegida al hacer clic en su encabezado (los List no ordenan solos).
        if (_sortProp is { } prop && typeof(FreshDeskTicket).GetProperty(prop) is { } pi)
            q = _sortAsc ? q.OrderBy(t => pi.GetValue(t)) : q.OrderByDescending(t => pi.GetValue(t));

        var filtered = q.ToList();
        _grid.DataSource = filtered;
        _lblStatus.Text = $"{filtered.Count} de {_all.Count} tickets | Última sync: {_all.MaxBy(t => t.SyncedAt)?.SyncedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
    }

    private static bool MatchFDField(FreshDeskTicket t, string q, int field) => field switch
    {
        1  => t.ExternalId.ToString().Contains(q),
        2  => t.Subject.ToLower().Contains(q),
        3  => FreshDeskTicket.StatusLabel(t.Status).ToLower().Contains(q),
        4  => FreshDeskTicket.PriorityLabel(t.Priority).ToLower().Contains(q),
        5  => (t.Type ?? "").ToLower().Contains(q),
        6  => t.AgentName.ToLower().Contains(q),
        7  => t.RequesterName.ToLower().Contains(q),
        8  => t.RequesterEmail.ToLower().Contains(q),
        9  => FreshDeskTicket.SourceLabel(t.Source).ToLower().Contains(q),
        10 => t.Tags.ToLower().Contains(q),
        _  => t.ExternalId.ToString().Contains(q)
           || t.Subject.ToLower().Contains(q)
           || t.AgentName.ToLower().Contains(q)
           || t.RequesterName.ToLower().Contains(q)
           || t.RequesterEmail.ToLower().Contains(q)
           || (t.Type ?? "").ToLower().Contains(q)
           || t.Tags.ToLower().Contains(q)
           || FreshDeskTicket.StatusLabel(t.Status).ToLower().Contains(q)
           || FreshDeskTicket.PriorityLabel(t.Priority).ToLower().Contains(q)
           || FreshDeskTicket.SourceLabel(t.Source).ToLower().Contains(q)
    };

    private void BuildStats()
    {
        _pnlStats.Controls.Clear();
        if (_all.Count == 0) return;

        var stats = new (string label, string value, Color color)[]
        {
            ("Total",     _all.Count.ToString(),                          AppTheme.SidebarActive),
            ("Abiertos",  _all.Count(t => t.Status == 2).ToString(),     AppTheme.SidebarActive),
            ("Pendientes",_all.Count(t => t.Status == 3).ToString(),     AppTheme.Warning),
            ("Resueltos", _all.Count(t => t.Status == 4).ToString(),     AppTheme.Success),
            ("Cerrados",  _all.Count(t => t.Status == 5).ToString(),     Color.Gray),
            ("Urgentes",  _all.Count(t => t.Priority == 4).ToString(),   AppTheme.Danger),
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

    private async void BtnSync_Click(object? s, EventArgs e)
    {
        if (!_freshDesk.IsEnabled)
        {
            MessageBox.Show("Freshdesk no está habilitado. Configure la integración en Configuración.",
                "Sin configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        GuardarFiltro();   // aplica lo que esté en la barra de filtro antes de sincronizar

        _btnSync.Enabled = false;
        _btnSync.Text    = "⟳  Sincronizando...";
        _lblStatus.Text  = "Conectando con Freshdesk...";

        _cts = new CancellationTokenSource();
        try
        {
            var r = await _freshDesk.SyncToLocalAsync(_cts.Token);
            LoadData();
            var resumen = $"Sincronización completada.\n{r.Added} nuevos, {r.Updated} actualizados" +
                          (r.Removed > 0 ? $", {r.Removed} quitados (fuera del filtro)" : "") + ".";
            if (!string.IsNullOrEmpty(r.Aviso)) resumen += $"\n\n⚠ {r.Aviso}";
            MessageBox.Show(resumen, "Freshdesk", MessageBoxButtons.OK,
                string.IsNullOrEmpty(r.Aviso) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // EF envuelve el error real del proveedor (columna que falta, restricción, etc.) en
            // InnerException; sin esto el diálogo solo mostraba «See the inner exception for details».
            MessageBox.Show($"Error al sincronizar:\n\n{DetalleError(ex)}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSync.Enabled = true;
            _btnSync.Text    = "⟳  Sincronizar";
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Persiste el filtro (por agente / por grupo) para que lo use también el sync de fondo.</summary>
    private void GuardarFiltro() => _freshDesk.GuardarFiltro(_chkSoloMios.Checked, _txtGrupoFiltro.Text);

    /// <summary>Aplana la cadena de excepciones: EF anida el error real del proveedor en InnerException.</summary>
    private static string DetalleError(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException)
            sb.AppendLine(e.Message);
        return sb.ToString().Trim();
    }

    private void Grid_CellFormatting(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        var prop = _grid.Columns[e.ColumnIndex].DataPropertyName;

        if (prop == "Status" && e.Value is int status)
        {
            var label = FreshDeskTicket.StatusLabel(status);
            e.Value = label;
            e.FormattingApplied = true;
            e.CellStyle.Font = AppTheme.BoldFont;
            e.CellStyle.ForeColor = label switch
            {
                "Abierto"   => AppTheme.SidebarActive,
                "Pendiente" => AppTheme.Warning,
                "Resuelto"  => AppTheme.Success,
                "Cerrado"   => Color.Gray,
                _           => AppTheme.TextPrimary
            };
        }
        else if (prop == "Priority" && e.Value is int priority)
        {
            var label = FreshDeskTicket.PriorityLabel(priority);
            e.Value = label;
            e.FormattingApplied = true;
            e.CellStyle.ForeColor = label switch
            {
                "Urgente" => AppTheme.Danger,
                "Alta"    => AppTheme.Warning,
                "Media"   => AppTheme.SidebarActive,
                _         => AppTheme.TextSecondary
            };
        }
        else if (prop == "Source" && e.Value is int source)
        {
            e.Value = FreshDeskTicket.SourceLabel(source);
            e.FormattingApplied = true;
        }
    }

    private void Grid_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is FreshDeskTicket ticket)
            ShowDetail(ticket);
    }

    /// <summary>Ordena por la columna del encabezado (clic de nuevo = invierte el sentido).</summary>
    private void Grid_ColumnHeaderClick(object? s, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        var prop = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (string.IsNullOrEmpty(prop)) return;
        if (_sortProp == prop) _sortAsc = !_sortAsc; else { _sortProp = prop; _sortAsc = true; }
        Filter();
        foreach (DataGridViewColumn c in _grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
        _grid.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection =
            _sortAsc ? SortOrder.Ascending : SortOrder.Descending;
    }

    private void Grid_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) return;
        _grid.ClearSelection();
        _grid.Rows[hit.RowIndex].Selected = true;

        if (_grid.Rows[hit.RowIndex].DataBoundItem is not FreshDeskTicket ticket) return;

        var menu = new ContextMenuStrip();
        menu.Items.Add("🔗  Abrir en Freshdesk", null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(ticket.Url))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ticket.Url) { UseShellExecute = true });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📋  Ver detalle", null, (_, _) => ShowDetail(ticket));
        menu.Show(_grid, e.Location);
    }

    private void ShowDetail(FreshDeskTicket ticket)
    {
        var links = _db.TicketLinks.AsNoTracking().Where(l => l.FreshDeskTicketId == ticket.Id)
            .Include(l => l.DevOpsTicket).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ID: #{ticket.ExternalId}");
        sb.AppendLine($"Estado: {FreshDeskTicket.StatusLabel(ticket.Status)}");
        sb.AppendLine($"Prioridad: {FreshDeskTicket.PriorityLabel(ticket.Priority)}");
        if (!string.IsNullOrEmpty(ticket.Type))
            sb.AppendLine($"Tipo: {ticket.Type}");
        sb.AppendLine($"Fuente: {FreshDeskTicket.SourceLabel(ticket.Source)}");
        sb.AppendLine($"Agente: {ticket.AgentName}");
        if (!string.IsNullOrEmpty(ticket.GroupName))
            sb.AppendLine($"Grupo: {ticket.GroupName}");
        sb.AppendLine($"Solicitante: {ticket.RequesterName} <{ticket.RequesterEmail}>");
        if (!string.IsNullOrEmpty(ticket.Tags))
            sb.AppendLine($"Tags: {ticket.Tags}");
        sb.AppendLine($"Creado: {ticket.CreatedAtExternal?.ToLocalTime():dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Actualizado: {ticket.UpdatedAtExternal?.ToLocalTime():dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine("──────────  Descripción  ──────────");
        sb.AppendLine(string.IsNullOrWhiteSpace(ticket.Description) ? "(sin descripción)" : ticket.Description.Trim());
        sb.AppendLine();
        if (links.Count > 0)
        {
            sb.AppendLine($"Work items DevOps vinculados ({links.Count}):");
            foreach (var l in links)
                sb.AppendLine($"  • #{l.DevOpsTicket.ExternalId} [{l.DevOpsTicket.WorkItemType}] — {l.DevOpsTicket.Title}");
        }
        else
        {
            sb.AppendLine("Sin work items DevOps vinculados.");
        }

        // Ventana propia (no MessageBox): la descripción puede ser larga, así se lee sin ir al navegador.
        using var frm = new Form
        {
            Text = $"Freshdesk #{ticket.ExternalId} — {ticket.Subject}",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(720, 560), MinimumSize = new Size(480, 360),
            BackColor = AppTheme.ContentBg, Font = AppTheme.DefaultFont,
            ShowIcon = false, MaximizeBox = true, MinimizeBox = false
        };
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0), BackColor = AppTheme.ContentBg };
        var txt = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
            Font = AppTheme.DefaultFont, Text = sb.ToString()
        };
        pad.Controls.Add(txt);

        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnClose = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnClose.Click += (_, _) => frm.Close();
        var btnOpen = AppTheme.MakePrimaryButton("🔗 Abrir en Freshdesk", 190);
        btnOpen.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(ticket.Url))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ticket.Url) { UseShellExecute = true }); } catch { }
        };
        pnlBtns.Controls.AddRange([btnClose, btnOpen]);

        frm.Controls.Add(pad);        // el Fill se agrega primero para que ocupe lo que deja el panel inferior
        frm.Controls.Add(pnlBtns);
        frm.CancelButton = btnClose;
        frm.Shown += (_, _) => { txt.Select(0, 0); btnClose.Focus(); };
        frm.ShowDialog(this);
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"freshdesk_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var lines = new List<string> { "ID,Asunto,Estado,Prioridad,Tipo,Agente,Solicitante,Email,Fuente,Creado,Actualizado,URL" };
        foreach (var t in _all)
            lines.Add($"{t.ExternalId},\"{t.Subject.Replace("\"","'")}\",\"{FreshDeskTicket.StatusLabel(t.Status)}\",\"{FreshDeskTicket.PriorityLabel(t.Priority)}\",\"{t.Type}\",\"{t.AgentName}\",\"{t.RequesterName}\",\"{t.RequesterEmail}\",\"{FreshDeskTicket.SourceLabel(t.Source)}\",{t.CreatedAtExternal?.ToLocalTime():dd/MM/yyyy},{t.UpdatedAtExternal?.ToLocalTime():dd/MM/yyyy},\"{t.Url}\"");

        File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
        MessageBox.Show("Exportación completada.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.Dispose(disposing);
    }

    // ── Helpers ────────────────────────────────────────────────────
    private static DataGridViewTextBoxColumn Col(string header, string prop, int w) => new()
    {
        HeaderText = header, DataPropertyName = prop, Width = w, ReadOnly = true, Name = prop
    };

    private static DataGridViewTextBoxColumn ColFn(string name, int w) => new()
    {
        HeaderText = name, DataPropertyName = name, Width = w, ReadOnly = true, Name = name
    };

    private static Panel Spacer(int w) => new() { Width = w, Height = 1 };

    private static Panel MakeStatCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 122, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            e.Graphics.DrawRectangle(new Pen(AppTheme.Border), 0, 0, card.Width - 1, card.Height - 1);
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
