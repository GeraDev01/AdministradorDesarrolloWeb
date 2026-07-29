using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class TicketLinkControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly AuditService _audit;

    // Tab: Vínculos
    private DataGridView _gridLinks = null!;
    private TextBox _txtSearchLinks = null!;
    private Label _lblLinksStatus = null!;

    // Tab: Sin vincular
    private DataGridView _gridUnlinkedDO = null!;
    private DataGridView _gridUnlinkedFD = null!;
    private Label _lblDevOpsSearch = null!, _lblFDSearch = null!;
    private TextBox _txtSearchDO = null!, _txtSearchFD = null!;
    private Button _btnLink = null!;

    // Stats panel
    private Panel _pnlStats = null!;

    private List<TicketLink> _links = [];
    private List<DevOpsTicket> _allDO = [];
    private List<FreshDeskTicket> _allFD = [];

    public TicketLinkControl(AppDbContext db, CurrentUserContext currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.ContentBg;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(16),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));   // stats
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // tabs
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _pnlStats = new Panel { Dock = DockStyle.Fill };

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildLinksTab());
        tabs.TabPages.Add(BuildLinkCreatorTab());
        tabs.TabPages.Add(BuildStatsTab());

        outer.Controls.Add(_pnlStats, 0, 0);
        outer.Controls.Add(tabs,      0, 1);
        Controls.Add(outer);
    }

    // ── TAB 1: Vínculos existentes ────────────────────────────────
    private TabPage BuildLinksTab()
    {
        var page = new TabPage("🔗  Vínculos") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 6, 0, 6), AutoSize = false
        };
        _txtSearchLinks = new TextBox { Width = 260, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar en vínculos..." };
        _txtSearchLinks.TextChanged += (_, _) => FilterLinks();

        var btnDelete = AppTheme.MakeDangerButton("🗑  Desvincular", 130);
        btnDelete.Click += BtnDeleteLink_Click;

        toolbar.Controls.Add(_txtSearchLinks);
        toolbar.Controls.Add(new Panel { Width = 10, Height = 1 });
        toolbar.Controls.Add(btnDelete);

        _lblLinksStatus = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        _gridLinks = AppTheme.MakeGrid();
        _gridLinks.Dock = DockStyle.Fill;
        _gridLinks.Columns.AddRange(
            TxtCol("DO_ID",       "ID DevOps",   75),
            TxtCol("DO_Type",     "Tipo",        100),
            TxtCol("DO_Title",    "Título DevOps", 240),
            TxtCol("DO_State",    "Estado DO",    90),
            TxtCol("FD_ID",       "ID FD",        70),
            TxtCol("FD_Subject",  "Asunto Freshdesk", 240),
            TxtCol("FD_Status",   "Estado FD",    90),
            TxtCol("FD_Agent",    "Agente",       130),
            TxtCol("LinkedAt",    "Vinculado",    110),
            TxtCol("LinkedBy",    "Por",           100),
            TxtCol("Notes",       "Notas",         150)
        );
        _gridLinks.Columns["LinkedAt"]!.DefaultCellStyle.Format = "dd/MM/yyyy";

        tbl.Controls.Add(toolbar,         0, 0);
        tbl.Controls.Add(_lblLinksStatus, 0, 1);
        tbl.Controls.Add(_gridLinks,      0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── TAB 2: Crear vínculos ─────────────────────────────────────
    private TabPage BuildLinkCreatorTab()
    {
        var page = new TabPage("➕  Vincular tickets") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1MinSize = 50;
        split.Panel2MinSize = 50;
        // Centrar el splitter una vez que el control tenga tamaño real
        split.SizeChanged += (s, _) =>
        {
            if (split.Width > 110 && !split.IsDisposed)
                try { split.SplitterDistance = split.Width / 2; } catch { }
        };

        // Panel izquierdo: DevOps
        var leftTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        leftTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _lblDevOpsSearch = new Label { Text = "Work Item Azure DevOps:", Font = AppTheme.BoldFont, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtSearchDO = new TextBox { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar work item..." };
        _txtSearchDO.TextChanged += (_, _) => FilterUnlinkedDO();

        _gridUnlinkedDO = AppTheme.MakeGrid();
        _gridUnlinkedDO.Dock = DockStyle.Fill;
        _gridUnlinkedDO.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridUnlinkedDO.Columns.AddRange(
            TxtCol("ExternalId", "#", 60),
            TxtCol("WorkItemType", "Tipo", 100),
            TxtCol("Title", "Título", 230),
            TxtCol("State", "Estado", 90),
            TxtCol("AssignedTo", "Asignado", 130)
        );

        leftTbl.Controls.Add(_lblDevOpsSearch,  0, 0);
        leftTbl.Controls.Add(_txtSearchDO,       0, 1);
        leftTbl.Controls.Add(_gridUnlinkedDO,    0, 2);
        split.Panel1.Controls.Add(leftTbl);

        // Panel derecho: Freshdesk
        var rightTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _lblFDSearch = new Label { Text = "Ticket Freshdesk:", Font = AppTheme.BoldFont, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtSearchFD = new TextBox { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar ticket..." };
        _txtSearchFD.TextChanged += (_, _) => FilterUnlinkedFD();

        _gridUnlinkedFD = AppTheme.MakeGrid();
        _gridUnlinkedFD.Dock = DockStyle.Fill;
        _gridUnlinkedFD.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridUnlinkedFD.Columns.AddRange(
            TxtCol("ExternalId", "#", 70),
            TxtCol("Subject", "Asunto", 230),
            TxtCol("StatusLabel", "Estado", 90),
            TxtCol("PriorityLabel", "Prioridad", 80),
            TxtCol("AgentName", "Agente", 130)
        );

        rightTbl.Controls.Add(_lblFDSearch,   0, 0);
        rightTbl.Controls.Add(_txtSearchFD,    0, 1);
        rightTbl.Controls.Add(_gridUnlinkedFD, 0, 2);
        split.Panel2.Controls.Add(rightTbl);

        // Botón vincular — barra inferior con FlowLayout
        _btnLink = AppTheme.MakePrimaryButton("🔗  Vincular selección", 190);
        _btnLink.Click += BtnLink_Click;
        _btnLink.Margin = new Padding(0, 0, 12, 0);

        var instrLabel = new Label
        {
            Text = "Selecciona un work item y un ticket, luego haz clic en Vincular.",
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            AutoSize = true, Margin = new Padding(0, 9, 0, 0)
        };

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(8, 7, 8, 7), BackColor = AppTheme.ContentBg
        };
        btnFlow.Controls.Add(_btnLink);
        btnFlow.Controls.Add(instrLabel);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.Controls.Add(split,    0, 0);
        outer.Controls.Add(btnFlow,  0, 1);
        page.Controls.Add(outer);
        return page;
    }

    // ── TAB 3: Estadísticas ───────────────────────────────────────
    private TabPage BuildStatsTab()
    {
        var page = new TabPage("📊  Estadísticas") { BackColor = AppTheme.ContentBg, Padding = new Padding(16) };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        page.Controls.Add(scroll);
        // Se rellena dinámicamente en RefreshStats()
        _statsScrollPanel = scroll;
        return page;
    }

    private Panel _statsScrollPanel = null!;

    private void RefreshStats()
    {
        var panel = _statsScrollPanel;
        panel.Controls.Clear();

        int doTotal  = _db.DevOpsTickets.Count();
        int fdTotal  = _db.FreshDeskTickets.Count();
        int links    = _db.TicketLinks.Count();
        int doLinked = _db.TicketLinks.Select(l => l.DevOpsTicketId).Distinct().Count();
        int fdLinked = _db.TicketLinks.Select(l => l.FreshDeskTicketId).Distinct().Count();

        int y = 0;
        AddStatRow(panel, "Work items Azure DevOps (total)", doTotal.ToString(),  ref y);
        AddStatRow(panel, "Tickets Freshdesk (total)",       fdTotal.ToString(),  ref y);
        AddStatRow(panel, "Vínculos totales",                links.ToString(),    ref y);
        AddStatRow(panel, "Work items vinculados",           $"{doLinked} / {doTotal}", ref y);
        AddStatRow(panel, "Tickets FD vinculados",           $"{fdLinked} / {fdTotal}", ref y);

        y += 12;
        AddSectionHeader(panel, "Azure DevOps — por estado", ref y);
        foreach (var g in _db.DevOpsTickets.AsEnumerable().GroupBy(t => t.State).OrderByDescending(g => g.Count()))
            AddStatRow(panel, g.Key, g.Count().ToString(), ref y);

        y += 12;
        AddSectionHeader(panel, "Azure DevOps — por tipo", ref y);
        foreach (var g in _db.DevOpsTickets.AsEnumerable().GroupBy(t => t.WorkItemType).OrderByDescending(g => g.Count()))
            AddStatRow(panel, g.Key, g.Count().ToString(), ref y);

        y += 12;
        AddSectionHeader(panel, "Freshdesk — por estado", ref y);
        foreach (var g in _db.FreshDeskTickets.AsEnumerable().GroupBy(t => FreshDeskTicket.StatusLabel(t.Status)).OrderByDescending(g => g.Count()))
            AddStatRow(panel, g.Key, g.Count().ToString(), ref y);

        y += 12;
        AddSectionHeader(panel, "Freshdesk — por prioridad", ref y);
        foreach (var g in _db.FreshDeskTickets.AsEnumerable().GroupBy(t => FreshDeskTicket.PriorityLabel(t.Priority)).OrderByDescending(g => g.Count()))
            AddStatRow(panel, g.Key, g.Count().ToString(), ref y);

        y += 12;
        AddSectionHeader(panel, "Freshdesk — por agente", ref y);
        foreach (var g in _db.FreshDeskTickets.Where(t => t.AgentName != "")
            .AsEnumerable().GroupBy(t => t.AgentName).OrderByDescending(g => g.Count()).Take(10))
            AddStatRow(panel, g.Key, g.Count().ToString(), ref y);
    }

    private static void AddStatRow(Panel p, string label, string value, ref int y)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(0, y), AutoSize = true, Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextPrimary });
        p.Controls.Add(new Label { Text = value, Location = new Point(320, y), AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.SidebarActive });
        y += 22;
    }

    private static void AddSectionHeader(Panel p, string text, ref int y)
    {
        p.Controls.Add(new Label
        {
            Text = text, Location = new Point(0, y), AutoSize = true,
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary
        });
        y += 24;
    }

    // ── Carga y filtros ────────────────────────────────────────────
    private void LoadData()
    {
        _links = _db.TicketLinks
            .Include(l => l.DevOpsTicket)
            .Include(l => l.FreshDeskTicket)
            .OrderByDescending(l => l.LinkedAt)
            .ToList();

        _allDO = _db.DevOpsTickets.OrderByDescending(t => t.UpdatedAtExternal).ToList();
        _allFD = _db.FreshDeskTickets.OrderByDescending(t => t.UpdatedAtExternal).ToList();

        FilterLinks();
        FilterUnlinkedDO();
        FilterUnlinkedFD();
        BuildTopStats();
        RefreshStats();
    }

    private void BuildTopStats()
    {
        _pnlStats.Controls.Clear();
        int doTotal  = _allDO.Count;
        int fdTotal  = _allFD.Count;
        int links    = _links.Count;
        int doLinked = _links.Select(l => l.DevOpsTicketId).Distinct().Count();
        int fdLinked = _links.Select(l => l.FreshDeskTicketId).Distinct().Count();
        double pctDO = doTotal > 0 ? Math.Round(100.0 * doLinked / doTotal, 0) : 0;
        double pctFD = fdTotal > 0 ? Math.Round(100.0 * fdLinked / fdTotal, 0) : 0;

        var stats = new (string label, string value, Color color)[]
        {
            ("DevOps total",      doTotal.ToString(),          AppTheme.SidebarActive),
            ("FD total",          fdTotal.ToString(),          Color.FromArgb(20, 184, 166)),
            ("Vínculos",          links.ToString(),            Color.FromArgb(139, 92, 246)),
            ($"DO vinculados",    $"{doLinked} ({pctDO}%)",   AppTheme.Success),
            ($"FD vinculados",    $"{fdLinked} ({pctFD}%)",   AppTheme.Success),
            ("Sin vincular",      (doTotal - doLinked + fdTotal - fdLinked).ToString(), AppTheme.Warning),
        };

        int x = 0;
        foreach (var (lbl, val, color) in stats)
        {
            var card = MakeStatCard(lbl, val, color);
            card.Location = new Point(x, 2);
            _pnlStats.Controls.Add(card);
            x += 145;
        }
    }

    private void FilterLinks()
    {
        var search = _txtSearchLinks.Text.Trim().ToLower();
        var filtered = _links.Where(l =>
            string.IsNullOrEmpty(search)
            || l.DevOpsTicket.Title.ToLower().Contains(search)
            || l.FreshDeskTicket.Subject.ToLower().Contains(search)
            || l.DevOpsTicket.ExternalId.ToString().Contains(search)
            || l.FreshDeskTicket.ExternalId.ToString().Contains(search)
            || (l.Notes ?? "").ToLower().Contains(search)).ToList();

        _gridLinks.DataSource = filtered.Select(l => new
        {
            DO_ID      = l.DevOpsTicket.ExternalId,
            DO_Type    = l.DevOpsTicket.WorkItemType,
            DO_Title   = l.DevOpsTicket.Title,
            DO_State   = l.DevOpsTicket.State,
            FD_ID      = l.FreshDeskTicket.ExternalId,
            FD_Subject = l.FreshDeskTicket.Subject,
            FD_Status  = FreshDeskTicket.StatusLabel(l.FreshDeskTicket.Status),
            FD_Agent   = l.FreshDeskTicket.AgentName,
            LinkedAt   = l.LinkedAt.ToLocalTime(),
            LinkedBy   = l.LinkedByUser ?? "",
            Notes      = l.Notes ?? "",
            LinkId     = l.Id
        }).ToList<dynamic>();

        _lblLinksStatus.Text = $"{filtered.Count} vínculos";
    }

    private void FilterUnlinkedDO()
    {
        var search = _txtSearchDO.Text.Trim().ToLower();
        // Mostrar todos, no solo sin vincular (puede haber varios vínculos)
        var filtered = _allDO.Where(t =>
            string.IsNullOrEmpty(search)
            || t.Title.ToLower().Contains(search)
            || t.ExternalId.ToString().Contains(search)
            || t.AssignedTo.ToLower().Contains(search)).ToList();

        _gridUnlinkedDO.DataSource = filtered;
    }

    private void FilterUnlinkedFD()
    {
        var search = _txtSearchFD.Text.Trim().ToLower();
        var filtered = _allFD.Where(t =>
            string.IsNullOrEmpty(search)
            || t.Subject.ToLower().Contains(search)
            || t.ExternalId.ToString().Contains(search)
            || t.RequesterName.ToLower().Contains(search)).ToList();

        _gridUnlinkedFD.DataSource = filtered.Select(t => new
        {
            t.ExternalId,
            t.Subject,
            StatusLabel   = FreshDeskTicket.StatusLabel(t.Status),
            PriorityLabel = FreshDeskTicket.PriorityLabel(t.Priority),
            t.AgentName,
            t.Id
        }).ToList<dynamic>();
    }

    // ── Acciones ───────────────────────────────────────────────────
    private void BtnLink_Click(object? s, EventArgs e)
    {
        if (_gridUnlinkedDO.SelectedRows.Count == 0 || _gridUnlinkedFD.SelectedRows.Count == 0)
        {
            MessageBox.Show("Selecciona un work item de DevOps y un ticket de Freshdesk.",
                "Selección requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var doTicket = (DevOpsTicket?)_gridUnlinkedDO.SelectedRows[0].DataBoundItem;
        var fdRow    = _gridUnlinkedFD.SelectedRows[0].DataBoundItem as dynamic;
        if (doTicket == null || fdRow == null) return;

        int fdId = (int)fdRow.Id;

        // Verificar que no existe ya
        if (_db.TicketLinks.Any(l => l.DevOpsTicketId == doTicket.Id && l.FreshDeskTicketId == fdId))
        {
            MessageBox.Show("Este vínculo ya existe.", "Duplicado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var notesForm = new Form
        {
            Text = "Notas del vínculo (opcional)", Size = new Size(420, 180),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false
        };
        var txtNotes = new TextBox { Multiline = true, Location = new Point(10, 10), Size = new Size(390, 80), ScrollBars = ScrollBars.Vertical };
        var btnOk    = new Button { Text = "Vincular", DialogResult = DialogResult.OK, Location = new Point(10, 100), Width = 100 };
        notesForm.Controls.AddRange([txtNotes, btnOk]);
        notesForm.AcceptButton = btnOk;

        if (notesForm.ShowDialog() != DialogResult.OK) return;

        _db.TicketLinks.Add(new TicketLink
        {
            DevOpsTicketId    = doTicket.Id,
            FreshDeskTicketId = fdId,
            Notes             = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim(),
            LinkedAt          = DateTime.UtcNow,
            LinkedByUser      = _currentUser.User?.FullName
        });
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "TicketLink", null,
            $"Vínculo creado: DevOps #{doTicket.ExternalId} ↔ FD #{fdRow.ExternalId}");

        LoadData();
        MessageBox.Show("Vínculo creado exitosamente.", "Vincular", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnDeleteLink_Click(object? s, EventArgs e)
    {
        if (_gridLinks.SelectedRows.Count == 0) return;
        var row = _gridLinks.SelectedRows[0].DataBoundItem as dynamic;
        if (row == null) return;

        int linkId = (int)row.LinkId;
        if (MessageBox.Show("¿Eliminar este vínculo?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var link = _db.TicketLinks.Find(linkId);
        if (link == null) return;

        _db.TicketLinks.Remove(link);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "TicketLink", linkId.ToString(), "Vínculo eliminado");
        LoadData();
    }

    // ── Helpers ────────────────────────────────────────────────────
    private static DataGridViewTextBoxColumn TxtCol(string prop, string header, int w) => new()
    {
        HeaderText = header, DataPropertyName = prop, Width = w, ReadOnly = true, Name = prop
    };

    private static Panel MakeStatCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 137, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            e.Graphics.DrawRectangle(new Pen(AppTheme.Border), 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label
        {
            Text = value, Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = accent, AutoSize = false,
            Location = new Point(12, 10), Size = new Size(120, 30),
            TextAlign = ContentAlignment.MiddleLeft
        });
        card.Controls.Add(new Label
        {
            Text = label, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, AutoSize = false,
            Location = new Point(12, 44), Size = new Size(120, 28),
            TextAlign = ContentAlignment.TopLeft
        });
        return card;
    }
}
