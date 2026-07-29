using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class PerformanceControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;
    private readonly ReportService _report;
    private readonly EmailService _email;
    private readonly PerformanceScoringService _scoring;

    /// <summary>Se dispara al aprobar/rechazar para que el shell refresque el badge de pendientes.</summary>
    public event Action? PendingCountChanged;

    private DataGridView _gridRanking = null!;
    private DataGridView _gridCriteria = null!;
    private DataGridView _gridEntries = null!;
    private ComboBox _cbxMonth = null!;
    private NumericUpDown _nudYear = null!;
    private ComboBox _cbxMonthE = null!;
    private NumericUpDown _nudYearE = null!;
    private DataGridView _gridTeamRank = null!;
    private ComboBox _cbxMonthT = null!;
    private NumericUpDown _nudYearT = null!;
    private DataGridView _gridPending = null!;
    private List<PointEntry> _entries = [];
    private List<PointEntry> _pending = [];
    private List<RankRow> _rankingRows = [];
    private List<TeamRankRow> _teamRankRows = [];

    private sealed record RankRow(string Medal, string Dev, int Total, int Pos, int Neg, int Count, List<PointEntry> Entries);
    private sealed record TeamRankRow(string Medal, int TeamId, string Team, int MembersSum, int TeamOwn, int Total, int MemberCount);

    public PerformanceControl(AppDbContext db, AuditService audit, CurrentUserContext currentUser, ReportService report, EmailService email, PerformanceScoringService scoring)
    {
        _db = db; _audit = audit; _currentUser = currentUser; _report = report; _email = email; _scoring = scoring;
        BuildUI(); LoadRanking();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        // ── TAB 1: Ranking mensual ───────────────────────────────
        var tabRanking = new TabPage("  🏅  Ranking Mensual  ");
        var rankTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        rankTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        rankTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rankTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var rankToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = tabRanking.BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        rankToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rankToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 580f));
        rankToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var periodFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        periodFlow.Controls.Add(new Label { Text = "Mes:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxMonth = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 10, 0) };
        string[] months = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
        _cbxMonth.Items.AddRange(months); _cbxMonth.SelectedIndex = DateTime.Today.Month - 1;
        periodFlow.Controls.Add(_cbxMonth);
        periodFlow.Controls.Add(new Label { Text = "Año:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _nudYear = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year, Margin = new Padding(0, 2, 10, 0) };
        periodFlow.Controls.Add(_nudYear);
        var btnLoad = AppTheme.MakePrimaryButton("🔄 Cargar", 95); btnLoad.Margin = new Padding(0, 2, 0, 0); btnLoad.Click += (_, _) => LoadRanking();
        periodFlow.Controls.Add(btnLoad);
        rankToolbar.Controls.Add(periodFlow, 0, 0);

        var rankBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnDetail    = AppTheme.MakeSecondaryButton("👁 Ver detalle", 130); btnDetail.Margin = new Padding(4, 2, 0, 0); btnDetail.Click += (_, _) => OpenSelectedDetail();
        var btnAssignPts = AppTheme.MakePrimaryButton("🏅 Asignar puntos", 145); btnAssignPts.Margin = new Padding(4, 2, 0, 0); btnAssignPts.Click += BtnAssignPoints_Click;
        var btnDelPts    = AppTheme.MakeDangerButton("🗑 Borrar todas del dev", 175); btnDelPts.Margin = new Padding(4, 2, 0, 0); btnDelPts.Click += BtnDelPoints_Click;
        var btnExpRank   = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnExpRank.Margin = new Padding(4, 2, 0, 0); btnExpRank.Click += BtnExportRanking_Click;
        rankBtns.Controls.AddRange([btnDetail, btnAssignPts, btnDelPts, btnExpRank]);
        rankToolbar.Controls.Add(rankBtns, 1, 0);

        _gridRanking = AppTheme.MakeGrid();
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pos.",          Name = "Pos",   Width = 50, FillWeight = 6 });
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador",  Name = "Dev",   FillWeight = 34 });
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Total Pts",      Name = "Total", FillWeight = 14 });
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "+ Premio",       Name = "Pos2",  FillWeight = 14 });
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "- Penaliz.",     Name = "Neg",   FillWeight = 14 });
        _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entradas",       Name = "Count", FillWeight = 12 });
        _gridRanking.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) OpenSelectedDetail(); };

        var pnlRankGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlRankGrid.Controls.Add(_gridRanking);
        rankTbl.Controls.Add(rankToolbar, 0, 0);
        rankTbl.Controls.Add(pnlRankGrid, 0, 1);
        tabRanking.Controls.Add(rankTbl);

        // ── TAB 2: Criterios ─────────────────────────────────────
        var tabCriteria = new TabPage("  ⚙  Criterios de Evaluación  ");
        var critTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        critTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        critTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        critTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var critBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 8, 10, 5)
        };
        var btnCNew    = AppTheme.MakePrimaryButton("➕ Nuevo criterio", 140); btnCNew.Margin = new Padding(0, 2, 8, 0); btnCNew.Click += BtnCriteriaNew_Click;
        var btnCEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 108); btnCEdit.Margin = new Padding(0, 2, 8, 0); btnCEdit.Click += BtnCriteriaEdit_Click;
        var btnCToggle = AppTheme.MakeSecondaryButton("⚡ Activar/Desact.", 150); btnCToggle.Margin = new Padding(0, 2, 0, 0); btnCToggle.Click += BtnCriteriaToggle_Click;
        critBtns.Controls.AddRange([btnCNew, btnCEdit, btnCToggle]);

        _gridCriteria = AppTheme.MakeGrid();
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",         Name = "Id",     Width = 45, FillWeight = 4  });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Criterio",    Name = "Name",   FillWeight = 32 });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pts default", Name = "Pts",    FillWeight = 14 });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",        Name = "Type",   FillWeight = 11 });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ámbito",      Name = "Scope",  FillWeight = 11 });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc",   FillWeight = 24 });
        _gridCriteria.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activo",      Name = "Active", FillWeight = 8  });
        _gridCriteria.CellDoubleClick += (_, _) => BtnCriteriaEdit_Click(null, EventArgs.Empty);

        var pnlCritGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty };
        pnlCritGrid.Controls.Add(_gridCriteria);
        critTbl.Controls.Add(critBtns, 0, 0);
        critTbl.Controls.Add(pnlCritGrid, 0, 1);
        tabCriteria.Controls.Add(critTbl);

        var tabPending = BuildPendingTab();
        var tabTeamRanking = BuildTeamRankingTab();
        var tabEntries = BuildEntriesTab();

        tabs.TabPages.AddRange([tabRanking, tabPending, tabTeamRanking, tabEntries, tabCriteria]);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            switch (tabs.SelectedIndex)
            {
                case 1: LoadPending(); break;
                case 2: LoadTeamRanking(); break;
                case 3: LoadEntries(); break;
                case 4: LoadCriteria(); break;
            }
        };
        Controls.Add(tabs);
    }

    private TabPage BuildTeamRankingTab()
    {
        var tab = new TabPage("  👥  Ranking por Equipo  ");
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
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var periodFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        periodFlow.Controls.Add(new Label { Text = "Mes:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxMonthT = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 10, 0) };
        string[] months = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
        _cbxMonthT.Items.AddRange(months); _cbxMonthT.SelectedIndex = DateTime.Today.Month - 1;
        _cbxMonthT.SelectedIndexChanged += (_, _) => LoadTeamRanking();
        periodFlow.Controls.Add(_cbxMonthT);
        periodFlow.Controls.Add(new Label { Text = "Año:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _nudYearT = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year, Margin = new Padding(0, 2, 10, 0) };
        _nudYearT.ValueChanged += (_, _) => LoadTeamRanking();
        periodFlow.Controls.Add(_nudYearT);
        periodFlow.Controls.Add(new Label { Text = "  Total = puntos de integrantes + puntos propios del equipo", AutoSize = true, Margin = new Padding(6, 6, 0, 0), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        toolbar.Controls.Add(periodFlow, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnDetail = AppTheme.MakeSecondaryButton("👁 Ver detalle", 130); btnDetail.Margin = new Padding(4, 2, 0, 0); btnDetail.Click += (_, _) => OpenSelectedTeamDetail();
        var btnAssign = AppTheme.MakePrimaryButton("🏅 Asignar al equipo", 165); btnAssign.Margin = new Padding(4, 2, 0, 0); btnAssign.Click += BtnAssignTeamPoints_Click;
        var btnExp    = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnExp.Margin = new Padding(4, 2, 0, 0); btnExp.Click += BtnExportTeamRanking_Click;
        btns.Controls.AddRange([btnDetail, btnAssign, btnExp]);
        toolbar.Controls.Add(btns, 1, 0);

        _gridTeamRank = AppTheme.MakeGrid();
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pos.",         Name = "Pos",     Width = 50, FillWeight = 6 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipo",        Name = "Team",    FillWeight = 30 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Total",         Name = "Total",   FillWeight = 14 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pts integrantes",Name = "Members",FillWeight = 18 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pts equipo",     Name = "Own",    FillWeight = 16 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "# Miembros",     Name = "Count",  FillWeight = 12 });
        _gridTeamRank.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TeamId", Name = "Id", Visible = false });
        _gridTeamRank.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) OpenSelectedTeamDetail(); };

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_gridTeamRank);
        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        tab.Controls.Add(tbl);
        return tab;
    }

    private void LoadTeamRanking()
    {
        if (_gridTeamRank == null) return;
        int month = _cbxMonthT.SelectedIndex + 1;
        int year  = (int)_nudYearT.Value;

        // Ranking por equipo (integrantes aprobados + puntos propios) desde el servicio central.
        _teamRankRows = _scoring.TeamRanking(year, month)
            .Select(r => new TeamRankRow("", r.TeamId, r.Name, r.MembersSum, r.TeamOwn, r.Total, r.MemberCount))
            .ToList();

        _gridTeamRank.Rows.Clear();
        for (int i = 0; i < _teamRankRows.Count; i++)
        {
            var r = _teamRankRows[i];
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            _teamRankRows[i] = r with { Medal = medal };
            int rowIdx = _gridTeamRank.Rows.Add(medal, r.Team, r.Total,
                (r.MembersSum >= 0 ? "+" : "") + r.MembersSum, (r.TeamOwn >= 0 ? "+" : "") + r.TeamOwn, r.MemberCount, r.TeamId);
            if (r.Total > 0)      _gridTeamRank.Rows[rowIdx].Cells["Total"].Style.ForeColor = AppTheme.Success;
            else if (r.Total < 0) _gridTeamRank.Rows[rowIdx].Cells["Total"].Style.ForeColor = AppTheme.Danger;
            _gridTeamRank.Rows[rowIdx].Cells["Total"].Style.Font = AppTheme.BoldFont;
            if (i < 3 && r.Total > 0)
                _gridTeamRank.Rows[rowIdx].DefaultCellStyle.BackColor =
                    i == 0 ? Color.FromArgb(255, 248, 220) : i == 1 ? Color.FromArgb(240, 248, 255) : Color.FromArgb(240, 255, 240);
        }
    }

    private void OpenSelectedTeamDetail()
    {
        if (_gridTeamRank.CurrentRow?.Cells["Id"].Value is not int teamId) { MessageBox.Show("Selecciona un equipo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var row = _teamRankRows.FirstOrDefault(r => r.TeamId == teamId);
        if (row == null) return;
        using var frm = new TeamPointsDetailForm(_db, _audit, teamId, row.Team, _cbxMonthT.SelectedIndex + 1, (int)_nudYearT.Value);
        frm.ShowDialog(this);
        if (frm.Changed) LoadTeamRanking();
    }

    private void BtnAssignTeamPoints_Click(object? s, EventArgs e)
    {
        if (!_db.Teams.Any()) { MessageBox.Show("No hay equipos. Créalos en la pantalla de Equipos.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!_db.ScoringCriteria.Any(c => c.IsActive && c.Scope != CriterionScope.Individual))
        { MessageBox.Show("No hay criterios de equipo activos. Crea uno con 'Aplica a: Equipo' en la pestaña Criterios.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        using var frm = new TeamPointEntryForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK || frm.Results.Count == 0) return;
        foreach (var tp in frm.Results) { tp.AssignedByUserId = _currentUser.User?.Id; _db.TeamPointEntries.Add(tp); }
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "TeamPointEntry", "", $"{frm.Results.Count} entrada(s) de equipo asignada(s)");
        LoadTeamRanking();
    }

    private void BtnExportTeamRanking_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog($"RankingEquipos_{_cbxMonthT.SelectedItem}_{(int)_nudYearT.Value}");
        if (path == null) return;
        _report.ExportToExcel(_teamRankRows,
            ["Pos.", "Equipo", "Total", "Pts integrantes", "Pts equipo", "# Miembros"],
            r => [r.Medal, r.Team, r.Total, r.MembersSum, r.TeamOwn, r.MemberCount],
            "RankingEquipos", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private TabPage BuildEntriesTab()
    {
        var tabEntries = new TabPage("  📋  Entradas de Puntos  ");
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
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 540f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var periodFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        periodFlow.Controls.Add(new Label { Text = "Mes:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxMonthE = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 10, 0) };
        string[] months = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
        _cbxMonthE.Items.AddRange(months); _cbxMonthE.SelectedIndex = DateTime.Today.Month - 1;
        _cbxMonthE.SelectedIndexChanged += (_, _) => LoadEntries();
        periodFlow.Controls.Add(_cbxMonthE);
        periodFlow.Controls.Add(new Label { Text = "Año:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _nudYearE = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year, Margin = new Padding(0, 2, 10, 0) };
        _nudYearE.ValueChanged += (_, _) => LoadEntries();
        periodFlow.Controls.Add(_nudYearE);
        periodFlow.Controls.Add(new Label { Text = "  (marca una o varias filas)", AutoSize = true, Margin = new Padding(6, 6, 0, 0), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        toolbar.Controls.Add(periodFlow, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnAssign = AppTheme.MakePrimaryButton("🏅 Asignar puntos", 145); btnAssign.Margin = new Padding(4, 2, 0, 0); btnAssign.Click += BtnAssignPoints_Click;
        var btnView   = AppTheme.MakeSecondaryButton("👁 Ver captura", 140); btnView.Margin = new Padding(4, 2, 0, 0); btnView.Click += BtnViewShot_Click;
        var btnDel    = AppTheme.MakeDangerButton("🗑 Eliminar seleccionadas", 190); btnDel.Margin = new Padding(4, 2, 0, 0); btnDel.Click += BtnDelEntries_Click;
        btns.Controls.AddRange([btnAssign, btnView, btnDel]);
        toolbar.Controls.Add(btns, 1, 0);

        _gridEntries = AppTheme.MakeGrid();
        _gridEntries.MultiSelect = true;
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador",  Name = "Dev",     FillWeight = 22 });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Criterio",       Name = "Crit",    FillWeight = 24 });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",         Name = "Pts",     FillWeight = 8  });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",         Name = "State",   FillWeight = 11 });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",          Name = "Date",    FillWeight = 12 });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requerimiento",  Name = "Req",     FillWeight = 12 });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📷",             Name = "Shot",    FillWeight = 5  });
        _gridEntries.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentario",     Name = "Comment", FillWeight = 18 });

        _gridEntries.CellDoubleClick += (_, _) => BtnViewShot_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty };
        pnlGrid.Controls.Add(_gridEntries);
        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        tabEntries.Controls.Add(tbl);
        return tabEntries;
    }

    private void BtnViewShot_Click(object? s, EventArgs e)
    {
        if (_gridEntries.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una entrada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var pe = _entries.FirstOrDefault(p => p.Id == id);
        if (pe?.Screenshot == null || pe.Screenshot.Length == 0)
        { MessageBox.Show("Esta entrada no tiene captura.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrWhiteSpace(pe.ScreenshotFileName) ? "captura.png" : pe.ScreenshotFileName!;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, pe.Screenshot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadEntries()
    {
        if (_gridEntries == null) return;
        int month = _cbxMonthE.SelectedIndex + 1;
        int year  = (int)_nudYearE.Value;

        _entries = _db.PointEntries
            .Include(p => p.Developer)
            .Include(p => p.Criterion)
            .Include(p => p.Requirement)
            .Where(p => p.Year == year && p.Month == month)
            .OrderBy(p => p.Developer.FullName).ThenByDescending(p => p.Date)
            .ToList();

        _gridEntries.Rows.Clear();
        foreach (var pe in _entries)
        {
            int i = _gridEntries.Rows.Add(pe.Id, pe.Developer.FullName, pe.Criterion.Name,
                (pe.Points >= 0 ? "+" : "") + pe.Points,
                EntryStateLabel(pe.ApprovalStatus),
                pe.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                pe.Requirement != null ? $"#{pe.Requirement.Id}" : "—",
                pe.Screenshot != null ? "📷" : "",
                pe.Comment ?? "");
            _gridEntries.Rows[i].Cells["Pts"].Style.ForeColor = pe.Points >= 0 ? AppTheme.Success : AppTheme.Danger;
            _gridEntries.Rows[i].Cells["Pts"].Style.Font = AppTheme.BoldFont;
            _gridEntries.Rows[i].Cells["State"].Style.ForeColor = pe.ApprovalStatus switch
            { PointApprovalStatus.Aprobado => AppTheme.Success, PointApprovalStatus.Rechazado => AppTheme.Danger, _ => AppTheme.Warning };
        }
    }

    private void BtnDelEntries_Click(object? s, EventArgs e)
    {
        var ids = _gridEntries.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Cells["Id"].Value)
            .OfType<int>()
            .ToList();
        if (ids.Count == 0) { MessageBox.Show("Selecciona al menos una entrada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar {ids.Count} entrada(s) de puntos?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var toRemove = _db.PointEntries.Where(p => ids.Contains(p.Id)).ToList();
        _db.PointEntries.RemoveRange(toRemove);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "PointEntry", string.Join(",", ids), $"{ids.Count} entradas eliminadas");
        LoadEntries();
        LoadRanking();
    }

    // ── Pendientes de aprobación (autocalificación de desarrolladores) ─
    private TabPage BuildPendingTab()
    {
        var tab = new TabPage("  ⏳  Pendientes de aprobación  ");
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
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 660f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        toolbar.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Puntos que los desarrolladores se autoasignaron. El puntaje lo fija el criterio: solo tú puedes ajustarlo aquí antes de aprobar.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnApprove = AppTheme.MakePrimaryButton("✅ Aprobar", 120); btnApprove.Margin = new Padding(4, 2, 0, 0); btnApprove.Click += BtnApprovePending_Click;
        var btnReject  = AppTheme.MakeDangerButton("❌ Rechazar", 120); btnReject.Margin = new Padding(4, 2, 0, 0); btnReject.Click += BtnRejectPending_Click;
        // El desarrollador ya no puede escribir el puntaje: esta es la única vía para cambiarlo.
        var btnAdjust  = AppTheme.MakeSecondaryButton("✏ Ajustar puntos", 150); btnAdjust.Margin = new Padding(4, 2, 0, 0); btnAdjust.Click += BtnAdjustPendingPoints_Click;
        var btnShot    = AppTheme.MakeSecondaryButton("👁 Ver captura", 140); btnShot.Margin = new Padding(4, 2, 0, 0); btnShot.Click += BtnPendingShot_Click;
        var btnReload  = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadPending();
        btns.Controls.AddRange([btnApprove, btnReject, btnAdjust, btnShot, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        _gridPending = AppTheme.MakeGrid();
        _gridPending.MultiSelect = true;
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev",   FillWeight = 18 });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actividad / criterio", Name = "Crit", FillWeight = 22 });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos", Name = "Pts",   FillWeight = 7  });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Período", Name = "Period", FillWeight = 9 });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Name = "Date",   FillWeight = 10 });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Req.", Name = "Req",     FillWeight = 7  });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📷", Name = "Shot",      FillWeight = 4  });
        _gridPending.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentario", Name = "Comment", FillWeight = 23 });
        _gridPending.CellDoubleClick += (_, _) => BtnPendingShot_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_gridPending);
        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        tab.Controls.Add(tbl);
        return tab;
    }

    private static readonly string[] MonthNames =
        ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];

    private static string EntryStateLabel(PointApprovalStatus s) => s switch
    {
        PointApprovalStatus.Aprobado => "✅ Aprobado",
        PointApprovalStatus.Rechazado => "❌ Rechazado",
        _ => "⏳ Pendiente"
    };

    private void LoadPending()
    {
        if (_gridPending == null) return;
        _pending = _db.PointEntries
            .Include(p => p.Developer)
            .Include(p => p.Criterion)
            .Include(p => p.Requirement)
            .Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente)
            .OrderBy(p => p.Date)
            .ToList();

        _gridPending.Rows.Clear();
        foreach (var p in _pending)
        {
            string period = p.Month >= 1 && p.Month <= 12 ? $"{MonthNames[p.Month - 1]} {p.Year}" : $"{p.Month}/{p.Year}";
            int i = _gridPending.Rows.Add(p.Id, p.Developer.FullName, p.Criterion.Name,
                (p.Points >= 0 ? "+" : "") + p.Points, period,
                p.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                p.Requirement != null ? $"#{p.Requirement.Id}" : "—",
                p.Screenshot != null ? "📷" : "",
                p.Comment ?? "");
            _gridPending.Rows[i].Cells["Pts"].Style.ForeColor = AppTheme.Success;
            _gridPending.Rows[i].Cells["Pts"].Style.Font = AppTheme.BoldFont;
        }
    }

    private List<PointEntry> SelectedPending() =>
        _gridPending.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Cells["Id"].Value).OfType<int>()
            .Select(id => _pending.FirstOrDefault(p => p.Id == id))
            .OfType<PointEntry>().ToList();

    private async void BtnApprovePending_Click(object? s, EventArgs e)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var sel = SelectedPending();
        if (sel.Count == 0) { MessageBox.Show("Selecciona al menos una entrada pendiente.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var ids = sel.Select(p => p.Id).ToList();
        var rows = _db.PointEntries.Where(p => ids.Contains(p.Id)).ToList();
        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            r.ApprovalStatus = PointApprovalStatus.Aprobado;
            r.ReviewedByUserId = _currentUser.User?.Id;
            r.ReviewedAt = now;
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "PointEntry", string.Join(",", ids), $"{rows.Count} autocalificación(es) aprobada(s)");
        LoadPending(); LoadRanking(); LoadEntries();
        PendingCountChanged?.Invoke();
        await NotifyDevelopersAsync(sel, approved: true, reason: null);
    }

    /// <summary>
    /// Ajusta el puntaje de una autocalificación pendiente. Es la contraparte necesaria de haber
    /// dejado el campo de puntos fijo para el desarrollador: alguien tiene que poder corregirlo,
    /// y ese alguien es solo el administrador. La entrada sigue pendiente tras el ajuste.
    /// </summary>
    private void BtnAdjustPendingPoints_Click(object? s, EventArgs e)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var sel = SelectedPending();
        if (sel.Count != 1)
        {
            MessageBox.Show("Selecciona exactamente una entrada para ajustar sus puntos.", "Aviso",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entrada = _db.PointEntries.Include(p => p.Criterion).FirstOrDefault(p => p.Id == sel[0].Id);
        if (entrada == null) { MessageBox.Show("La entrada ya no existe.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); LoadPending(); return; }

        var texto = Prompt(
            $"Criterio: {entrada.Criterion.Name}\n" +
            $"Valor del criterio: {entrada.Criterion.DefaultPoints} pts   ·   Actual: {entrada.Points} pts\n\n" +
            "Nuevo puntaje (puede ser negativo):",
            "Ajustar puntos");

        if (string.IsNullOrWhiteSpace(texto)) return;
        if (!int.TryParse(texto.Trim(), out int nuevos))
        {
            MessageBox.Show("Escribe un número entero.", "Valor inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (nuevos == entrada.Points) return;

        int anteriores = entrada.Points;
        entrada.Points = nuevos;
        _db.SaveChanges();

        _audit.Record(AuditAction.Update, "PointEntry", entrada.Id.ToString(),
            $"Puntos ajustados por el administrador: {anteriores} → {nuevos} ({entrada.Criterion.Name})");
        LoadPending(); LoadRanking(); LoadEntries();
        MessageBox.Show($"Puntaje ajustado de {anteriores} a {nuevos}. La entrada sigue pendiente de aprobación.",
            "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void BtnRejectPending_Click(object? s, EventArgs e)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var sel = SelectedPending();
        if (sel.Count == 0) { MessageBox.Show("Selecciona al menos una entrada pendiente.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string reason = Prompt("Motivo del rechazo (opcional):", "Rechazar autocalificación");
        var ids = sel.Select(p => p.Id).ToList();
        var rows = _db.PointEntries.Where(p => ids.Contains(p.Id)).ToList();
        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            r.ApprovalStatus = PointApprovalStatus.Rechazado;
            r.ReviewedByUserId = _currentUser.User?.Id;
            r.ReviewedAt = now;
            r.ReviewComment = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "PointEntry", string.Join(",", ids), $"{rows.Count} autocalificación(es) rechazada(s)");
        LoadPending(); LoadRanking(); LoadEntries();
        PendingCountChanged?.Invoke();
        await NotifyDevelopersAsync(sel, approved: false, reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    /// <summary>
    /// Avisa por correo a cada desarrollador afectado (un correo-resumen por persona).
    /// Best-effort: solo si el correo está configurado y el dev tiene email; los errores no interrumpen.
    /// </summary>
    private async Task NotifyDevelopersAsync(List<PointEntry> entries, bool approved, string? reason)
    {
        if (!_email.IsConfigured(out _)) return;
        var groups = entries
            .Where(p => p.Developer != null && !string.IsNullOrWhiteSpace(p.Developer.Email))
            .GroupBy(p => p.DeveloperId)
            .Select(g => new { g.First().Developer.Email, Name = g.First().Developer.FullName, Count = g.Count(), Points = g.Sum(x => x.Points) })
            .ToList();
        if (groups.Count == 0) return;

        int sent = 0;
        foreach (var g in groups)
        {
            try
            {
                string subject = approved
                    ? "Tus puntos de desempeño fueron aprobados"
                    : "Tus puntos de desempeño fueron revisados";
                string body = approved
                    ? $"Hola {g.Name}:\n\nSe aprobaron {g.Count} de tus registros de desempeño (+{g.Points} pts en total). Ya cuentan en tu ranking.\n\n— Administrador de Desarrollo"
                    : $"Hola {g.Name}:\n\nSe rechazaron {g.Count} de tus registros de desempeño."
                        + (string.IsNullOrWhiteSpace(reason) ? "" : $"\nMotivo: {reason}")
                        + "\n\nPuedes volver a registrarlos con la evidencia adecuada.\n\n— Administrador de Desarrollo";
                await _email.SendAsync([g.Email!], subject, body);
                sent++;
            }
            catch { /* best-effort: no interrumpir el flujo de aprobación */ }
        }
        if (sent > 0 && !IsDisposed)
            MessageBox.Show($"Se notificó por correo a {sent} desarrollador(es).", "Notificación", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnPendingShot_Click(object? s, EventArgs e)
    {
        if (_gridPending.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una entrada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var pe = _pending.FirstOrDefault(p => p.Id == id);
        if (pe?.Screenshot == null || pe.Screenshot.Length == 0)
        { MessageBox.Show("Esta entrada no tiene captura.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrWhiteSpace(pe.ScreenshotFileName) ? "captura.png" : pe.ScreenshotFileName!;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, pe.Screenshot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string Prompt(string text, string caption)
    {
        using var f = new Form { Text = caption, Size = new Size(440, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = AppTheme.ContentBg, Font = AppTheme.DefaultFont };
        f.Controls.Add(new Label { Text = text, Location = new Point(16, 14), AutoSize = true });
        var txt = new TextBox { Location = new Point(16, 40), Width = 390, Multiline = true, Height = 50 };
        var ok = AppTheme.MakePrimaryButton("Aceptar", 100); ok.Location = new Point(196, 100); ok.DialogResult = DialogResult.OK;
        var cancel = AppTheme.MakeSecondaryButton("Cancelar", 100); cancel.Location = new Point(306, 100); cancel.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange([txt, ok, cancel]); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? txt.Text : "";
    }

    // ── Ranking ──────────────────────────────────────────────────
    private void LoadRanking()
    {
        if (_gridRanking == null) return;
        int month = _cbxMonth.SelectedIndex + 1;
        int year  = (int)_nudYear.Value;

        // Ranking individual (solo aprobados) desde el servicio central de puntuación.
        _rankingRows = _scoring.IndividualRanking(year, month)
            .Select(r => new RankRow("", r.FullName, r.Total, r.Positive, r.Negative, r.Count, r.Entries))
            .ToList();

        _gridRanking.Rows.Clear();
        for (int i = 0; i < _rankingRows.Count; i++)
        {
            var r = _rankingRows[i];
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            _rankingRows[i] = r with { Medal = medal };

            int rowIdx = _gridRanking.Rows.Add(medal, r.Dev, r.Total,
                r.Pos > 0 ? $"+{r.Pos}" : "—", r.Neg < 0 ? r.Neg.ToString() : "—", r.Count);

            if (r.Total > 0)      _gridRanking.Rows[rowIdx].Cells["Total"].Style.ForeColor = AppTheme.Success;
            else if (r.Total < 0) _gridRanking.Rows[rowIdx].Cells["Total"].Style.ForeColor = AppTheme.Danger;
            _gridRanking.Rows[rowIdx].Cells["Total"].Style.Font = AppTheme.BoldFont;
            if (i < 3 && r.Total > 0)
                _gridRanking.Rows[rowIdx].DefaultCellStyle.BackColor =
                    i == 0 ? Color.FromArgb(255, 248, 220) : i == 1 ? Color.FromArgb(240, 248, 255) : Color.FromArgb(240, 255, 240);
        }
    }

    private void OpenSelectedDetail()
    {
        if (_gridRanking.CurrentRow?.Cells["Dev"].Value is not string devName)
        { MessageBox.Show("Selecciona un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var row = _rankingRows.FirstOrDefault(r => r.Dev == devName);
        if (row == null) return;
        int month = _cbxMonth.SelectedIndex + 1; int year = (int)_nudYear.Value;
        using var frm = new DeveloperPointsDetailForm(row.Dev, month, year, row.Total, row.Pos, row.Neg, row.Entries);
        frm.ShowDialog(this);
    }

    private void BtnAssignPoints_Click(object? s, EventArgs e)
    {
        if (!_db.ScoringCriteria.Any(c => c.IsActive))
        { MessageBox.Show("No hay criterios activos. Ve a la pestaña Criterios y crea algunos.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new PointEntryForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK || frm.Results.Count == 0) return;
        foreach (var pe in frm.Results)
        {
            pe.AssignedByUserId = _currentUser.User?.Id;
            _db.PointEntries.Add(pe);
        }
        _db.SaveChanges();
        var first = frm.Results[0];
        _audit.Record(AuditAction.Create, "PointEntry", "", $"{frm.Results.Count} entrada(s) de {first.Points:+#;-#;0} pts asignada(s)");
        LoadRanking();
        LoadEntries();
    }

    private void BtnDelPoints_Click(object? s, EventArgs e)
    {
        if (_gridRanking.CurrentRow?.Cells["Dev"].Value is not string devName)
        { MessageBox.Show("Selecciona un desarrollador en el ranking.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        int month = _cbxMonth.SelectedIndex + 1; int year = (int)_nudYear.Value;
        var dev = _db.Developers.FirstOrDefault(d => d.FullName == devName);
        if (dev == null) return;
        var entries = _db.PointEntries.Where(p => p.DeveloperId == dev.Id && p.Year == year && p.Month == month).ToList();
        if (!entries.Any()) { MessageBox.Show("El desarrollador no tiene entradas en este período.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar TODAS las {entries.Count} entradas de {devName} en {month}/{year}?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.PointEntries.RemoveRange(entries); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "PointEntry", "", $"{entries.Count} entradas de {devName} eliminadas ({month}/{year})");
        LoadRanking(); LoadEntries();
    }

    private void BtnExportRanking_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog($"Ranking_{_cbxMonth.SelectedItem}_{(int)_nudYear.Value}");
        if (path == null) return;
        _report.ExportToExcel(_rankingRows,
            ["Pos.", "Desarrollador", "Total Pts", "+ Premio", "- Penaliz.", "Entradas", "Detalle"],
            r => [r.Medal, r.Dev, r.Total, r.Pos, r.Neg, r.Count,
                  string.Join(", ", r.Entries.Select(x => $"{x.Criterion.Name}({(x.Points >= 0 ? "+" : "")}{x.Points})"))],
            "Ranking", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Criteria ─────────────────────────────────────────────────
    private void LoadCriteria()
    {
        _gridCriteria.Rows.Clear();
        foreach (var c in _db.ScoringCriteria.OrderBy(c => c.Name).ToList())
        {
            var scopeLabel = c.Scope switch { CriterionScope.Equipo => "👥 Equipo", CriterionScope.Ambos => "Ambos", _ => "👤 Individual" };
            int i = _gridCriteria.Rows.Add(c.Id, c.Name, (c.DefaultPoints >= 0 ? "+" : "") + c.DefaultPoints,
                c.DefaultPoints >= 0 ? "✅ Premio" : "⛔ Penalización", scopeLabel, c.Description ?? "—", c.IsActive ? "✓" : "✗");
            if (!c.IsActive) _gridCriteria.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            _gridCriteria.Rows[i].Cells["Pts"].Style.ForeColor = c.DefaultPoints >= 0 ? AppTheme.Success : AppTheme.Danger;
            _gridCriteria.Rows[i].Cells["Pts"].Style.Font = AppTheme.BoldFont;
        }
    }

    private ScoringCriterion? SelectedCriterion()
    {
        if (_gridCriteria.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _db.ScoringCriteria.Find(id);
    }

    private void BtnCriteriaNew_Click(object? s, EventArgs e)
    {
        using var frm = new ScoringCriterionDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var c = frm.Result; c.CreatedAt = DateTime.UtcNow;
        _db.ScoringCriteria.Add(c); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "ScoringCriterion", c.Id.ToString(), c.Name);
        LoadCriteria();
    }

    private void BtnCriteriaEdit_Click(object? s, EventArgs e)
    {
        var c = SelectedCriterion();
        if (c == null) { MessageBox.Show("Selecciona un criterio.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new ScoringCriterionDetailForm(c);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "ScoringCriterion", c.Id.ToString(), c.Name);
        LoadCriteria();
    }

    private void BtnCriteriaToggle_Click(object? s, EventArgs e)
    {
        var c = SelectedCriterion();
        if (c == null) { MessageBox.Show("Selecciona un criterio.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        c.IsActive = !c.IsActive; _db.SaveChanges(); LoadCriteria();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) { LoadRanking(); LoadPending(); LoadTeamRanking(); LoadEntries(); LoadCriteria(); } }
}
