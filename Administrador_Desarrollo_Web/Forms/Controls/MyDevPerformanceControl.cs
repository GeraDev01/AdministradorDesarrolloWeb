using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Panel del desarrollador: muestra sus asignaciones y el ranking general (individual
/// y por equipo) mostrando SOLO los puntos, sin detalles. Los puntos son fijos (solo el
/// jefe los edita); el dev únicamente puede PROPONER actividades (quedan pendientes de
/// aprobación) con "Registrar actividad".
/// </summary>
public class MyDevPerformanceControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly AuditService _audit;
    private readonly WorkSessionService _work;
    private readonly PerformanceScoringService _scoring;

    private ComboBox _cbxMonth = null!;
    private NumericUpDown _nudYear = null!;
    private DataGridView _gridMine = null!, _gridIndiv = null!, _gridTeam = null!, _gridTickets = null!, _gridPoints = null!;
    private Label _kpiApproved = null!, _kpiPending = null!, _kpiRejected = null!, _kpiRank = null!, _kpiTime = null!;
    private Label _kpiTeam = null!, _kpiTickets = null!;
    private readonly ToolTip _toolTip = new();

    public MyDevPerformanceControl(AppDbContext db, CurrentUserContext currentUser, AuditService audit,
        WorkSessionService work, PerformanceScoringService scoring)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _work = work; _scoring = scoring;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ──────────────────────────────────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var periodFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        periodFlow.Controls.Add(new Label { Text = "Ranking del mes:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxMonth = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 10, 0) };
        string[] months = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
        _cbxMonth.Items.AddRange(months); _cbxMonth.SelectedIndex = DateTime.Today.Month - 1;
        _cbxMonth.SelectedIndexChanged += (_, _) => LoadData();
        periodFlow.Controls.Add(_cbxMonth);
        _nudYear = new NumericUpDown { Width = 90, Minimum = 2020, Maximum = 2099, Value = DateTime.Today.Year, Margin = new Padding(0, 2, 10, 0) };
        _nudYear.ValueChanged += (_, _) => LoadData();
        periodFlow.Controls.Add(_nudYear);
        toolbar.Controls.Add(periodFlow, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnNew = AppTheme.MakePrimaryButton("📝 Registrar actividad", 190); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnRegister_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadData();
        btns.Controls.AddRange([btnNew, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        // ── KPIs (mis puntos) ────────────────────────────────────
        var kpiFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        kpiFlow.Controls.Add(MakeKpi("👥 Mi equipo", out _kpiTeam, AppTheme.SidebarActive));
        kpiFlow.Controls.Add(MakeKpi("✅ Aprobado (mes)", out _kpiApproved, AppTheme.Success));
        kpiFlow.Controls.Add(MakeKpi("⏳ En revisión", out _kpiPending, AppTheme.Warning));
        kpiFlow.Controls.Add(MakeKpi("❌ Rechazado", out _kpiRejected, AppTheme.Danger));
        kpiFlow.Controls.Add(MakeKpi("🏅 Mi posición", out _kpiRank, AppTheme.TextPrimary));
        kpiFlow.Controls.Add(MakeKpi("⏱ Tiempo dedicado (total)", out _kpiTime, AppTheme.HeaderBg));
        kpiFlow.Controls.Add(MakeKpi("🔷 Tickets DevOps (abiertos)", out _kpiTickets, AppTheme.SidebarActive));

        // ── Tabs: asignaciones + rankings (solo puntos) ──────────
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        _gridMine = AppTheme.MakeGrid(); _gridMine.MultiSelect = false;
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",      Name = "Id",       Width = 45, FillWeight = 5  });
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",  Name = "Title",    FillWeight = 36 });
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",  Name = "Status",   FillWeight = 15 });
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Prioridad", Name = "Prio",   FillWeight = 12 });
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Avance",  Name = "Progress", FillWeight = 10 });
        _gridMine.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "⏱ Tiempo dedicado", Name = "Time", FillWeight = 16 });

        _gridIndiv = AppTheme.MakeGrid(); _gridIndiv.MultiSelect = false;
        _gridIndiv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pos.",         Name = "Pos", Width = 55, FillWeight = 10 });
        _gridIndiv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev", FillWeight = 70 });
        _gridIndiv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",        Name = "Pts", FillWeight = 20 });

        _gridTeam = AppTheme.MakeGrid(); _gridTeam.MultiSelect = false;
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pos.",   Name = "Pos",  Width = 55, FillWeight = 10 });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipo", Name = "Team", FillWeight = 70 });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos", Name = "Pts",  FillWeight = 20 });

        _gridTickets = AppTheme.MakeGrid(); _gridTickets.MultiSelect = false;
        _gridTickets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",     Name = "Id",     Width = 55, FillWeight = 7  });
        _gridTickets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",   Name = "Type",   FillWeight = 14 });
        _gridTickets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title",  FillWeight = 46 });
        _gridTickets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "State",  FillWeight = 16 });
        _gridTickets.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actualizado", Name = "Upd", FillWeight = 17 });
        _gridTickets.CellDoubleClick += GridTickets_CellDoubleClick;

        // Mis actividades/puntos: aquí el desarrollador ve el estado de sus autocalificaciones y de
        // los puntos que le asignó el jefe, con el MOTIVO cuando algo se rechaza.
        _gridPoints = AppTheme.MakeGrid(); _gridPoints.MultiSelect = false;
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",    Name = "Date",   FillWeight = 11 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Criterio", Name = "Crit",   FillWeight = 26 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",   Name = "Pts",    FillWeight = 8  });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Origen",   Name = "Origin", FillWeight = 13 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",   Name = "State",  FillWeight = 13 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motivo / comentario del jefe", Name = "Review", FillWeight = 29 });

        tabs.TabPages.Add(WrapTab("  📋  Mis asignaciones  ", _gridMine));
        tabs.TabPages.Add(WrapTab("  🔷  Mis tickets DevOps  ", _gridTickets));
        tabs.TabPages.Add(WrapTab("  📝  Mis actividades y puntos  ", _gridPoints));
        tabs.TabPages.Add(WrapTab("  🏅  Ranking individual  ", _gridIndiv));
        tabs.TabPages.Add(WrapTab("  👥  Ranking por equipo  ", _gridTeam));

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(kpiFlow, 0, 1);
        tbl.Controls.Add(tabs, 0, 2);
        Controls.Add(tbl);
    }

    private static TabPage WrapTab(string title, DataGridView grid)
    {
        var tab = new TabPage(title);
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(grid);
        tab.Controls.Add(pnl);
        return tab;
    }

    private static Panel MakeKpi(string title, out Label value, Color accent)
    {
        // Layout con dock (no posiciones fijas) para que el título/emoji nunca se recorte,
        // aunque el usuario tenga escalado de pantalla (DPI) activado.
        var card = new Panel { Width = 210, Height = 84, Margin = new Padding(0, 4, 10, 4), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accent });

        var content = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(10, 6, 6, 6) };
        content.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 26, Text = title, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        });
        value = new Label
        {
            Dock = DockStyle.Fill, Text = "—", Font = AppTheme.KpiValueFont, ForeColor = accent,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(value);
        card.Controls.Add(content);
        return card;
    }

    private void LoadData()
    {
        var devId = _currentUser.DeveloperId;
        _gridMine.Rows.Clear(); _gridIndiv.Rows.Clear(); _gridTeam.Rows.Clear(); _gridTickets.Rows.Clear(); _gridPoints.Rows.Clear();
        if (devId == null)
        {
            _kpiApproved.Text = _kpiPending.Text = _kpiRejected.Text = _kpiRank.Text = _kpiTime.Text = _kpiTeam.Text = _kpiTickets.Text = "—";
            _gridMine.Rows.Add("", "Tu cuenta no está vinculada a un desarrollador. Contacta al administrador.", "", "", "", "");
            return;
        }
        int month = _cbxMonth.SelectedIndex + 1;
        int year = (int)_nudYear.Value;

        LoadMyTeam(devId.Value);
        LoadKpis(devId.Value, year, month);
        LoadMyAssignments(devId.Value);
        LoadMyTickets(devId.Value);
        LoadMyPoints(devId.Value);
        LoadIndividualRanking(devId.Value, year, month);
        LoadTeamRanking(year, month);
    }

    /// <summary>Equipo al que está asignado hoy el desarrollador, y su rol dentro de él.</summary>
    private void LoadMyTeam(int devId)
    {
        var yo = _db.Developers
            .Where(d => d.Id == devId)
            .Select(d => new { d.TeamId, d.TeamRole, NombreEquipo = d.Team != null ? d.Team.Name : null })
            .FirstOrDefault();

        if (yo?.TeamId == null || yo.NombreEquipo == null)
        {
            _kpiTeam.Text = "Sin equipo";
            _kpiTeam.ForeColor = AppTheme.TextSecondary;
            _toolTip.SetToolTip(_kpiTeam, "No estás asignado a ningún equipo. Contacta al administrador.");
            return;
        }

        _kpiTeam.Text = yo.NombreEquipo;
        _kpiTeam.ForeColor = AppTheme.SidebarActive;
        // El nombre del equipo puede no caber en la tarjeta: el rol y el nombre completo van en el tooltip.
        _toolTip.SetToolTip(_kpiTeam, $"{yo.NombreEquipo} — {TeamRoleLabel(yo.TeamRole)}");
    }

    private static string TeamRoleLabel(TeamRole r) => r switch
    {
        TeamRole.Lider  => "Líder del equipo",
        TeamRole.SinRol => "Integrante",
        _               => r.ToString()
    };

    private void LoadKpis(int devId, int year, int month)
    {
        var mine = _db.PointEntries
            .Where(p => p.DeveloperId == devId && p.Year == year && p.Month == month)
            .Select(p => new { p.Points, p.ApprovalStatus })
            .ToList();
        int approved = mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Aprobado).Sum(p => p.Points);
        int pending  = mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente).Sum(p => p.Points);

        _kpiApproved.Text = (approved >= 0 ? "+" : "") + approved;
        _kpiPending.Text  = "+" + pending;
        _kpiRejected.Text = mine.Count(p => p.ApprovalStatus == PointApprovalStatus.Rechazado).ToString();
        _kpiTime.Text = WorkSessionService.Format(_work.GetTotalSecondsByDeveloper(devId));
    }

    private void LoadMyAssignments(int devId)
    {
        var reqs = _db.Requirements
            .Where(r => r.Assignments.Any(a => a.DeveloperId == devId))
            .OrderByDescending(r => r.CommittedDeliveryDate)
            .AsNoTracking()
            .ToList();
        foreach (var r in reqs)
        {
            int i = _gridMine.Rows.Add(r.Id, r.Title, StatusLabel(r.Status), PriorityLabel(r.Priority),
                $"{r.ProgressPercent}%", WorkSessionService.Format(_work.GetTotalSeconds(devId, r.Id)));
            _gridMine.Rows[i].Cells["Status"].Style.ForeColor = AppTheme.StatusColor(r.Status);
            _gridMine.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
        }
        if (reqs.Count == 0) _gridMine.Rows.Add("", "(no tienes requerimientos asignados)", "", "", "", "");
    }

    // Tickets de DevOps del desarrollador (empatados por identidad). Solo lectura; el detalle
    // completo, comentar y sincronizar viven en «Mis tickets DevOps».
    private void LoadMyTickets(int devId)
    {
        var dev = _db.Developers.Find(devId);
        var tickets = dev == null ? [] : DevOpsTicketQuery.ForDeveloper(_db, dev);
        int abiertos = tickets.Count(t => !AzureDevOpsService.EsCerrado(t.State));
        _kpiTickets.Text = abiertos.ToString();

        foreach (var t in tickets)
        {
            int i = _gridTickets.Rows.Add(t.ExternalId, t.WorkItemType, t.Title, t.State,
                t.UpdatedAtExternal?.ToLocalTime().ToString("dd/MM/yyyy") ?? "—");
            _gridTickets.Rows[i].Tag = t.Url;
            if (AzureDevOpsService.EsCerrado(t.State))
                _gridTickets.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
        }
        if (tickets.Count == 0)
            _gridTickets.Rows.Add("", "", "(no encontramos tickets de DevOps a tu nombre — revisa tu correo con el administrador)", "", "");
    }

    private void GridTickets_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_gridTickets.Rows[e.RowIndex].Tag is string url && !string.IsNullOrEmpty(url))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // Puntos del desarrollador: autocalificaciones (que él registró) y puntos que le asignó el jefe.
    // Muestra el MOTIVO/comentario cuando algo se rechaza, que es lo que el dev necesita ver.
    private void LoadMyPoints(int devId)
    {
        var pts = _db.PointEntries
            .Where(p => p.DeveloperId == devId)
            .Include(p => p.Criterion)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.Date, Criterio = p.Criterion.Name, p.Points, p.ApprovalStatus,
                EsAuto = p.SubmittedByDeveloperId != null, p.ReviewComment
            })
            .ToList();

        foreach (var p in pts)
        {
            int i = _gridPoints.Rows.Add(
                p.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                p.Criterio,
                (p.Points >= 0 ? "+" : "") + p.Points,
                p.EsAuto ? "Autocalificación" : "Asignado por jefe",
                ApprovalLabel(p.ApprovalStatus),
                // El motivo importa sobre todo cuando se rechaza; si no hay, se deja vacío.
                string.IsNullOrWhiteSpace(p.ReviewComment) ? (p.ApprovalStatus == PointApprovalStatus.Rechazado ? "(sin motivo registrado)" : "") : p.ReviewComment);

            var estadoCell = _gridPoints.Rows[i].Cells["State"];
            estadoCell.Style.ForeColor = ApprovalColor(p.ApprovalStatus);
            estadoCell.Style.Font = AppTheme.BoldFont;
            if (p.ApprovalStatus == PointApprovalStatus.Rechazado)
                _gridPoints.Rows[i].Cells["Review"].Style.ForeColor = AppTheme.Danger;
        }

        if (pts.Count == 0)
            _gridPoints.Rows.Add("", "(no tienes actividades ni puntos registrados)", "", "", "", "");
    }

    private static string ApprovalLabel(PointApprovalStatus s) => s switch
    {
        PointApprovalStatus.Aprobado  => "✅ Aprobado",
        PointApprovalStatus.Pendiente => "⏳ Pendiente",
        PointApprovalStatus.Rechazado => "❌ Rechazado",
        _                             => s.ToString()
    };

    private static Color ApprovalColor(PointApprovalStatus s) => s switch
    {
        PointApprovalStatus.Aprobado  => AppTheme.Success,
        PointApprovalStatus.Pendiente => AppTheme.Warning,
        PointApprovalStatus.Rechazado => AppTheme.Danger,
        _                             => AppTheme.TextSecondary
    };

    // Ranking general individual — delega en el servicio, que es quien sabe qué entra al ranking
    // (solo aprobados, sin líderes). Antes esta pantalla tenía SU PROPIA copia del cálculo y era
    // cuestión de tiempo que divergiera de la del administrador.
    private void LoadIndividualRanking(int devId, int year, int month)
    {
        var ranked = _scoring.IndividualRanking(year, month);

        // El nivel Lead no compite: decirlo es mejor que un «— de N» que se reporta como bug.
        bool soyNivelLead = _scoring.IdsConNivelLead().Contains(devId);
        _kpiRank.Text = soyNivelLead ? "Fuera de ranking (nivel Lead)"
                      : ranked.Count > 0 ? $"— de {ranked.Count}" : "—";
        // Incondicional: si le cambian el nivel y recarga, el gris no se puede quedar pegado.
        _kpiRank.ForeColor = soyNivelLead ? AppTheme.TextSecondary : AppTheme.TextPrimary;

        for (int i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i];
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            int row = _gridIndiv.Rows.Add(medal, r.FullName, (r.Total >= 0 ? "+" : "") + r.Total);
            _gridIndiv.Rows[row].Cells["Pts"].Style.Font = AppTheme.BoldFont;
            _gridIndiv.Rows[row].Cells["Pts"].Style.ForeColor = r.Total > 0 ? AppTheme.Success : r.Total < 0 ? AppTheme.Danger : AppTheme.TextSecondary;
            if (r.DeveloperId == devId)   // resaltar mi fila + fijar mi posición
            {
                _gridIndiv.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
                _kpiRank.Text = $"#{i + 1} de {ranked.Count}";
            }
        }
    }

    // Ranking general por equipo — misma delegación. Aquí el líder SÍ suma: es parte de su equipo.
    private void LoadTeamRanking(int year, int month)
    {
        int myTeamId = _currentUser.DeveloperId is int me
            ? _db.Developers.AsNoTracking().Where(d => d.Id == me).Select(d => d.TeamId).FirstOrDefault() ?? -1
            : -1;

        var ranked = _scoring.TeamRanking(year, month);
        for (int i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i];
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            int row = _gridTeam.Rows.Add(medal, r.Name, (r.Total >= 0 ? "+" : "") + r.Total);
            _gridTeam.Rows[row].Cells["Pts"].Style.Font = AppTheme.BoldFont;
            _gridTeam.Rows[row].Cells["Pts"].Style.ForeColor = r.Total > 0 ? AppTheme.Success : r.Total < 0 ? AppTheme.Danger : AppTheme.TextSecondary;
            if (r.TeamId == myTeamId)
                _gridTeam.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
        }
        if (ranked.Count == 0) _gridTeam.Rows.Add("", "(no hay equipos)", "");
    }

    private void BtnRegister_Click(object? s, EventArgs e)
    {
        var devId = _currentUser.DeveloperId;
        if (devId == null) { MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!_db.ScoringCriteria.Any(c => c.IsActive && c.Scope != CriterionScope.Equipo && c.DefaultPoints > 0))
        { MessageBox.Show("No hay criterios positivos disponibles. Pide al administrador que configure algunos.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        using var frm = new SelfPointEntryForm(_db, devId.Value);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK || frm.Result == null) return;

        try
        {
            // El servicio fija el puntaje desde el criterio; la pantalla no lo decide.
            var (ok, mensaje, entrada) = _scoring.RegistrarAutocalificacion(frm.Result, _currentUser);
            if (!ok)
            {
                MessageBox.Show(mensaje, "No se pudo registrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _audit.Record(AuditAction.Create, "PointEntry", entrada!.Id.ToString(),
                $"Autocalificación pendiente (+{entrada.Points} pts)");
            MessageBox.Show(mensaje, "Enviado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar => "Por estimar", RequirementStatus.Estimado => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo", RequirementStatus.EnPruebas => "En pruebas",
        RequirementStatus.PorEntregar => "Por entregar", RequirementStatus.Entregado => "Entregado",
        RequirementStatus.Cancelado => "Cancelado", _ => s.ToString()
    };

    private static string PriorityLabel(RequirementPriority p) => p switch
    {
        RequirementPriority.Baja => "🔵 Baja", RequirementPriority.Media => "🟡 Media",
        RequirementPriority.Alta => "🟠 Alta", RequirementPriority.Critica => "🔴 Crítica", _ => p.ToString()
    };

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
