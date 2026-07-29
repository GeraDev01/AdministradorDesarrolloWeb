using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Tablero de equipos tipo organigrama: cada equipo es una columna con el líder
/// arriba y los integrantes agrupados por rol. Permite arrastrar desarrolladores
/// entre equipos, asignar roles (líder, frontend, backend, …) y registrar rotaciones.
/// </summary>
public class TeamsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly TeamRosterService _roster;
    private readonly CurrentUserContext _currentUser;

    private Panel _boardHost = null!;
    private FlowLayoutPanel _board = null!;
    private Button _btnPdf = null!;
    private ContextMenuStrip _roleMenu = null!;
    private int? _menuDevId;
    private readonly List<TableLayoutPanel> _columns = [];

    private const int ColWidth = 244;
    private const int CardWidth = 210;

    private static readonly TeamRole[] RoleOrder =
        [TeamRole.Frontend, TeamRole.Backend, TeamRole.Fullstack, TeamRole.QA, TeamRole.DevOps, TeamRole.UX, TeamRole.Otro, TeamRole.SinRol];

    public TeamsControl(AppDbContext db, AuditService audit, TeamRosterService roster, CurrentUserContext currentUser)
    {
        _db = db; _audit = audit; _roster = roster; _currentUser = currentUser;
        BuildUI();
        LoadBoard();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;
        BuildRoleMenu();

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 690f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        toolbar.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Arrastra para reorganizar · clic derecho en una tarjeta para asignar rol.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnNew  = AppTheme.MakePrimaryButton("➕ Nuevo equipo", 150); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNewTeam_Click;
        var btnRot  = AppTheme.MakeSecondaryButton("🔄 Rotación", 120); btnRot.Margin = new Padding(4, 2, 0, 0); btnRot.Click += (_, _) => OpenRotation(null);
        var btnHist = AppTheme.MakeSecondaryButton("📜 Historial", 115); btnHist.Margin = new Padding(4, 2, 0, 0); btnHist.Click += (_, _) => { using var f = new TeamRotationHistoryForm(_db); f.ShowDialog(FindForm()); };
        _btnPdf = AppTheme.MakeSecondaryButton("📄 Generar PDF", 145); _btnPdf.Margin = new Padding(4, 2, 0, 0); _btnPdf.Click += BtnPdf_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadBoard();
        btns.Controls.AddRange([btnNew, btnRot, btnHist, _btnPdf, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        _boardHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 6, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        _board = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        _boardHost.Controls.Add(_board);
        _boardHost.Resize += (_, _) => ResizeColumns();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(_boardHost, 0, 1);
        Controls.Add(tbl);
    }

    private void BuildRoleMenu()
    {
        _roleMenu = new ContextMenuStrip();
        foreach (var r in new[] { TeamRole.Lider, TeamRole.Frontend, TeamRole.Backend, TeamRole.Fullstack, TeamRole.QA, TeamRole.DevOps, TeamRole.UX, TeamRole.Otro, TeamRole.SinRol })
        {
            var item = new ToolStripMenuItem((r == TeamRole.Lider ? "👑 " : "") + RoleLabel(r)) { Tag = r };
            item.Click += RoleItem_Click;
            _roleMenu.Items.Add(item);
        }
        _roleMenu.Items.Add(new ToolStripSeparator());
        var rotate = new ToolStripMenuItem("🔄 Rotar a otro equipo...");
        rotate.Click += (_, _) => { if (_menuDevId is int id) OpenRotation(id); };
        _roleMenu.Items.Add(rotate);
        var remove = new ToolStripMenuItem("❌ Quitar del equipo");
        remove.Click += (_, _) => { if (_menuDevId is int id) { MoveDeveloper(id, null, "Quitado del equipo"); LoadBoard(); } };
        _roleMenu.Items.Add(remove);
        _roleMenu.Opening += (_, _) => _menuDevId = _roleMenu.SourceControl?.Tag as int?;
    }

    private void RoleItem_Click(object? s, EventArgs e)
    {
        if (_menuDevId is int id && s is ToolStripItem { Tag: TeamRole role }) SetRole(id, role);
    }

    private int ColHeight => Math.Max(340, _boardHost.ClientSize.Height - 16);
    private void ResizeColumns() { foreach (var c in _columns) c.Height = ColHeight; }

    private void LoadBoard()
    {
        _board.SuspendLayout();
        _board.Controls.Clear();
        _columns.Clear();

        var teams = _db.Teams.OrderBy(t => t.Name).AsNoTracking().ToList();
        var devs  = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();

        var sysCounts  = _db.AppSystems.Where(s => s.TeamId != null).GroupBy(s => s.TeamId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionary(x => x.Key, x => x.C);
        var projCounts = _db.Projects.Where(p => p.TeamId != null).GroupBy(p => p.TeamId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionary(x => x.Key, x => x.C);

        foreach (var team in teams)
            _board.Controls.Add(MakeColumn(team, devs.Where(d => d.TeamId == team.Id).ToList(),
                sysCounts.GetValueOrDefault(team.Id), projCounts.GetValueOrDefault(team.Id)));
        _board.Controls.Add(MakeColumn(null, devs.Where(d => d.TeamId == null).ToList()));

        _board.ResumeLayout();
        ResizeColumns();
    }

    private TableLayoutPanel MakeColumn(Team? team, List<Developer> members, int sysCount = 0, int projCount = 0)
    {
        var color = ParseColor(team?.ColorHex) ?? (team == null ? Color.FromArgb(148, 163, 184) : AppTheme.SidebarActive);

        var col = new TableLayoutPanel
        {
            Width = ColWidth, Height = ColHeight, AutoSize = false,
            RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 12, 0),
            BackColor = AppTheme.CardBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        col.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        col.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Margin = Padding.Empty };
        header.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 6, BackColor = color });
        header.Controls.Add(new Label { Text = team?.Name ?? "Sin equipo", Location = new Point(14, 7), AutoSize = false, Size = new Size(team == null ? 200 : 120, 22), Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary });
        string sub = $"{members.Count} integrante(s)";
        if (team != null) sub += $"  ·  🖥 {sysCount}  📁 {projCount}";
        header.Controls.Add(new Label { Text = sub, Location = new Point(14, 31), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        if (team != null)
        {
            var btnAssets = AppTheme.MakeSecondaryButton("🗂", 26, 24); btnAssets.Location = new Point(ColWidth - 92, 8); btnAssets.Click += (_, _) => OpenAssets(team);
            var btnEdit = AppTheme.MakeSecondaryButton("✏", 26, 24); btnEdit.Location = new Point(ColWidth - 62, 8); btnEdit.Click += (_, _) => EditTeam(team);
            var btnDel  = AppTheme.MakeDangerButton("🗑", 26, 24);    btnDel.Location  = new Point(ColWidth - 32, 8); btnDel.Click  += (_, _) => DeleteTeam(team);
            header.Controls.AddRange([btnAssets, btnEdit, btnDel]);
        }

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(6, 6, 6, 6), Margin = Padding.Empty, BackColor = AppTheme.ContentBg
        };
        WireDrop(flow, team?.Id);
        WireDrop(header, team?.Id);

        if (team != null)
        {
            var lead = members.FirstOrDefault(m => m.TeamRole == TeamRole.Lider)
                       ?? members.FirstOrDefault(m => m.Id == team.LeadDeveloperId);
            var rest = members.Where(m => lead == null || m.Id != lead.Id).ToList();

            if (lead != null)
            {
                flow.Controls.Add(MakeCard(lead, team.Id, isLead: true));
                flow.Controls.Add(MakeConnector());
            }
            foreach (var role in RoleOrder)
            {
                var group = rest.Where(m => m.TeamRole == role).ToList();
                if (group.Count == 0) continue;
                flow.Controls.Add(MakeGroupHeader(RoleLabel(role), RoleColor(role)));
                foreach (var m in group) flow.Controls.Add(MakeCard(m, team.Id, isLead: false));
            }
        }
        else
        {
            foreach (var m in members) flow.Controls.Add(MakeCard(m, null, isLead: false));
        }

        col.Controls.Add(header, 0, 0);
        col.Controls.Add(flow,   0, 1);
        _columns.Add(col);
        return col;
    }

    private static Label MakeGroupHeader(string text, Color color) => new()
    {
        Text = "  " + text.ToUpperInvariant(), Width = CardWidth, Height = 22,
        Margin = new Padding(0, 6, 0, 2), Font = AppTheme.SmallFont, ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Panel MakeConnector()
    {
        var p = new Panel { Width = CardWidth, Height = 14, Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        p.Controls.Add(new Panel { Location = new Point(CardWidth / 2 - 1, 0), Size = new Size(2, 14), BackColor = AppTheme.Border });
        return p;
    }

    private Panel MakeCard(Developer d, int? teamId, bool isLead)
    {
        var roleColor = isLead ? RoleColor(TeamRole.Lider) : RoleColor(d.TeamRole);
        var card = new Panel
        {
            Width = CardWidth, Height = 48, Margin = new Padding(0, 0, 0, 6),
            BackColor = isLead ? Color.FromArgb(255, 250, 235) : Color.White,
            BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.SizeAll, Tag = d.Id
        };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = roleColor });

        var lblName = new Label { Text = d.FullName, Location = new Point(12, 5), AutoSize = false, Size = new Size(CardWidth - 20, 20), Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Tag = d.Id };
        string roleText = isLead ? "👑 Líder" : RoleLabel(d.TeamRole);
        string sub = string.IsNullOrWhiteSpace(d.Seniority) ? roleText : $"{roleText}  ·  {d.Seniority}";
        var lblSub = new Label { Text = sub, Location = new Point(12, 25), AutoSize = false, Size = new Size(CardWidth - 20, 18), Font = AppTheme.SmallFont, ForeColor = roleColor, Tag = d.Id };
        card.Controls.AddRange([lblName, lblSub]);

        foreach (var c in new Control[] { card, lblName, lblSub })
        {
            c.MouseDown += Card_MouseDown;
            WireDrop(c, teamId);
            if (teamId != null) c.ContextMenuStrip = _roleMenu;
        }
        return card;
    }

    private void Card_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (s is Control c && c.Tag is int devId)
            c.DoDragDrop(new DataObject("devId", devId), DragDropEffects.Move);
    }

    private void WireDrop(Control c, int? teamId)
    {
        c.AllowDrop = true;
        c.DragEnter += (_, e) => { if (e.Data?.GetDataPresent("devId") == true) e.Effect = DragDropEffects.Move; };
        c.DragOver  += (_, e) => { if (e.Data?.GetDataPresent("devId") == true) e.Effect = DragDropEffects.Move; };
        c.DragDrop  += (_, e) => { if (e.Data?.GetData("devId") is int devId) { MoveDeveloper(devId, teamId, null); LoadBoard(); } };
    }

    // ── Movimiento / rotación ────────────────────────────────────
    private void MoveDeveloper(int devId, int? targetTeamId, string? note)
    {
        var dev = _db.Developers.Find(devId);
        if (dev == null || dev.TeamId == targetTeamId) return;

        int? fromTeamId = dev.TeamId;
        string fromName = fromTeamId != null ? (_db.Teams.Find(fromTeamId.Value)?.Name ?? "Sin equipo") : "Sin equipo";
        string toName   = targetTeamId != null ? (_db.Teams.Find(targetTeamId.Value)?.Name ?? "Sin equipo") : "Sin equipo";

        // Si era líder del equipo de origen, liberar el cargo.
        if (fromTeamId != null)
        {
            var oldTeam = _db.Teams.Find(fromTeamId.Value);
            if (oldTeam != null && oldTeam.LeadDeveloperId == dev.Id) oldTeam.LeadDeveloperId = null;
        }

        dev.TeamId = targetTeamId;
        dev.TeamRole = TeamRole.SinRol; // el rol se reasigna en el nuevo equipo

        _db.TeamRotations.Add(new TeamRotation
        {
            DeveloperId = dev.Id, DeveloperName = dev.FullName,
            FromTeamId = fromTeamId, FromTeamName = fromName,
            ToTeamId = targetTeamId, ToTeamName = toName,
            Note = note, RotatedByUserId = _currentUser.User?.Id, RotatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Developer", dev.Id.ToString(), $"{dev.FullName}: {fromName} → {toName}");
    }

    private void SetRole(int devId, TeamRole role)
    {
        var dev = _db.Developers.Find(devId);
        if (dev == null || dev.TeamId == null) return;

        if (role == TeamRole.Lider)
        {
            foreach (var m in _db.Developers.Where(d => d.TeamId == dev.TeamId && d.TeamRole == TeamRole.Lider && d.Id != dev.Id))
                m.TeamRole = TeamRole.SinRol;
            dev.TeamRole = TeamRole.Lider;
            var team = _db.Teams.Find(dev.TeamId.Value);
            if (team != null) team.LeadDeveloperId = dev.Id;
        }
        else
        {
            dev.TeamRole = role;
            var team = _db.Teams.Find(dev.TeamId.Value);
            if (team != null && team.LeadDeveloperId == dev.Id) team.LeadDeveloperId = null;
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Developer", dev.Id.ToString(), $"{dev.FullName}: rol {RoleLabel(role)}");
        LoadBoard();
    }

    private void OpenRotation(int? preselectDevId)
    {
        if (!_db.Teams.Any()) { MessageBox.Show("Primero crea al menos un equipo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new TeamRotationForm(_db, preselectDevId);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        foreach (var devId in frm.DevIds) MoveDeveloper(devId, frm.TargetTeamId, frm.Note);
        LoadBoard();
    }

    // ── Equipos ──────────────────────────────────────────────────
    private void BtnNewTeam_Click(object? s, EventArgs e)
    {
        using var frm = new TeamDetailForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        var t = frm.Result; t.CreatedAt = DateTime.UtcNow;
        _db.Teams.Add(t); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Team", t.Id.ToString(), t.Name);
        LoadBoard();
    }

    private void EditTeam(Team team)
    {
        var tracked = _db.Teams.Find(team.Id);
        if (tracked == null) return;
        using var frm = new TeamDetailForm(tracked);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Team", tracked.Id.ToString(), tracked.Name);
        LoadBoard();
    }

    private void OpenAssets(Team team)
    {
        using var frm = new TeamAssetsForm(_db, _audit, team.Id, team.Name);
        frm.ShowDialog(FindForm());
        if (frm.Changed) LoadBoard();
    }

    private void DeleteTeam(Team team)
    {
        var tracked = _db.Teams.Find(team.Id);
        if (tracked == null) return;
        if (MessageBox.Show($"¿Eliminar el equipo '{tracked.Name}'?\nSus integrantes quedarán sin equipo.", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        foreach (var m in _db.Developers.Where(d => d.TeamId == tracked.Id)) { m.TeamId = null; m.TeamRole = TeamRole.SinRol; }
        _db.Teams.Remove(tracked);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "Team", tracked.Id.ToString(), tracked.Name);
        LoadBoard();
    }

    // ── PDF ──────────────────────────────────────────────────────
    private async void BtnPdf_Click(object? s, EventArgs e)
    {
        if (!_roster.ConverterAvailable(out var diag))
        { MessageBox.Show(diag, "Generar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var (rosters, unassigned) = GatherRosters();
        if (rosters.Count == 0 && unassigned.Count == 0)
        { MessageBox.Show("No hay equipos ni desarrolladores para generar el PDF.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        using var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = $"Organizacion_Equipos_{DateTime.Now:yyyyMMdd}.pdf" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        _btnPdf.Enabled = false; Cursor = Cursors.WaitCursor;
        try
        {
            var pdf = await _roster.BuildPdfAsync(rosters, unassigned, DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            File.WriteAllBytes(dlg.FileName, pdf);
            if (MessageBox.Show($"PDF generado:\n{dlg.FileName}\n\n¿Abrirlo ahora?", "Listo", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo generar el PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _btnPdf.Enabled = true; Cursor = Cursors.Default; }
    }

    private (List<TeamRoster> rosters, List<string> unassigned) GatherRosters()
    {
        var teams = _db.Teams.OrderBy(t => t.Name).AsNoTracking().ToList();
        var devs  = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
        var systems  = _db.AppSystems.Where(s => s.TeamId != null).OrderBy(s => s.Name).AsNoTracking().ToList();
        var projects = _db.Projects.Where(p => p.TeamId != null).OrderBy(p => p.Name).AsNoTracking().ToList();

        var rosters = teams.Select(t =>
        {
            var teamMembers = devs.Where(d => d.TeamId == t.Id).ToList();
            var lead = teamMembers.FirstOrDefault(m => m.TeamRole == TeamRole.Lider) ?? teamMembers.FirstOrDefault(m => m.Id == t.LeadDeveloperId);
            var memberLabels = teamMembers
                .OrderBy(m => m.TeamRole == TeamRole.Lider ? 0 : 1).ThenBy(m => (int)m.TeamRole).ThenBy(m => m.FullName)
                .Select(MemberLabel).ToList();
            return new TeamRoster(t.Name, t.Description, lead?.FullName, t.ColorHex, memberLabels)
            {
                Systems = systems.Where(s => s.TeamId == t.Id).Select(s => s.Name).ToList(),
                Projects = projects.Where(p => p.TeamId == t.Id)
                    .Select(p => p.Client is null ? p.Name : $"{p.Name} — {p.Client}").ToList()
            };
        }).ToList();

        var unassigned = devs.Where(d => d.TeamId == null).Select(d => DevBase(d)).ToList();
        return (rosters, unassigned);
    }

    private static string DevBase(Developer d) => string.IsNullOrWhiteSpace(d.Seniority) ? d.FullName : $"{d.FullName} ({d.Seniority})";
    private static string MemberLabel(Developer d) => d.TeamRole == TeamRole.SinRol ? DevBase(d) : $"{DevBase(d)} — {RoleLabel(d.TeamRole)}";

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return ColorTranslator.FromHtml(hex); } catch { return null; }
    }

    public static string RoleLabel(TeamRole r) => r switch
    {
        TeamRole.Lider => "Líder",
        TeamRole.Frontend => "Frontend Dev",
        TeamRole.Backend => "Backend Dev",
        TeamRole.Fullstack => "Fullstack",
        TeamRole.QA => "QA",
        TeamRole.DevOps => "DevOps",
        TeamRole.UX => "UX / Diseño",
        TeamRole.Otro => "Otro",
        _ => "Sin rol"
    };

    private static Color RoleColor(TeamRole r) => r switch
    {
        TeamRole.Lider => Color.FromArgb(202, 138, 4),
        TeamRole.Frontend => Color.FromArgb(37, 99, 235),
        TeamRole.Backend => Color.FromArgb(22, 163, 74),
        TeamRole.Fullstack => Color.FromArgb(124, 58, 237),
        TeamRole.QA => Color.FromArgb(234, 88, 12),
        TeamRole.DevOps => Color.FromArgb(13, 148, 136),
        TeamRole.UX => Color.FromArgb(219, 39, 119),
        TeamRole.Otro => Color.FromArgb(100, 116, 139),
        _ => Color.FromArgb(148, 163, 184)
    };

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadBoard();
    }
}
