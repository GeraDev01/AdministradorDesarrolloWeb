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
    private Label _kpiTeam = null!, _kpiTickets = null!, _kpiActivityTime = null!, _kpiNivel = null!;
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };

    /// <summary>Entradas de «Mis actividades y puntos», para poder corregir la seleccionada.</summary>
    private List<PointEntry> _myPoints = [];

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
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 580f));
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
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Corregir", 110); btnEdit.Margin = new Padding(4, 2, 0, 0); btnEdit.Click += BtnEditEntry_Click;
        var btnReplicar = AppTheme.MakeSecondaryButton("🗣 Replicar", 115); btnReplicar.Margin = new Padding(4, 2, 0, 0); btnReplicar.Click += BtnReplicar_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadData();
        _toolTip.SetToolTip(btnEdit, "Corrige una actividad tuya de «Mis actividades y puntos».\n" +
                                     "Puedes mientras no esté aprobada — también si te la rechazaron.");
        _toolTip.SetToolTip(btnReplicar, "¿Te rechazaron una actividad y no estás de acuerdo?\n" +
                                         "Argumenta y vuelve a mandarla a revisión.");
        btns.Controls.AddRange([btnNew, btnEdit, btnReplicar, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        // ── KPIs (mis puntos) ────────────────────────────────────
        var kpiFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        kpiFlow.Controls.Add(MakeKpi("🎓 Mi nivel", out _kpiNivel, AppTheme.HeaderBg));
        kpiFlow.Controls.Add(MakeKpi("👥 Mi equipo", out _kpiTeam, AppTheme.SidebarActive));
        kpiFlow.Controls.Add(MakeKpi("✅ Aprobado (mes)", out _kpiApproved, AppTheme.Success));
        kpiFlow.Controls.Add(MakeKpi("⏳ En revisión", out _kpiPending, AppTheme.Warning));
        kpiFlow.Controls.Add(MakeKpi("❌ Rechazado", out _kpiRejected, AppTheme.Danger));
        kpiFlow.Controls.Add(MakeKpi("🏅 Mi posición", out _kpiRank, AppTheme.TextPrimary));
        kpiFlow.Controls.Add(MakeKpi("⏱ Tiempo dedicado (total)", out _kpiTime, AppTheme.HeaderBg));
        // Dos relojes distintos a propósito: el de arriba es lo que midió el cronómetro sobre
        // requerimientos y actividades; este es lo que el desarrollador DECLARÓ al registrar sus
        // actividades del mes. Sumarlos contaría dos veces el trabajo cronometrado.
        kpiFlow.Controls.Add(MakeKpi("🕒 Tiempo de actividades (mes)", out _kpiActivityTime, AppTheme.Warning));
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
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",       Name = "Id",     Width = 45, FillWeight = 5 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",    Name = "Date",   FillWeight = 10 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qué hiciste", Name = "Crit", FillWeight = 22 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",   Name = "Pts",    FillWeight = 7  });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "⏱ Tiempo", Name = "Time",   FillWeight = 10 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "🔗 Item",  Name = "Link",   FillWeight = 8  });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Origen",   Name = "Origin", FillWeight = 12 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",   Name = "State",  FillWeight = 11 });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "🔁",       Name = "Round",  FillWeight = 5  });
        _gridPoints.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motivo / comentario del líder", Name = "Review", FillWeight = 22 });
        // Doble clic sobre la fila: si tiene enlace lo abre; es el gesto que ya usa la reja de tickets.
        _gridPoints.CellDoubleClick += GridPoints_CellDoubleClick;

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
        _myPoints = [];   // si no, «Corregir» seguiría resolviendo contra la carga anterior
        if (devId == null)
        {
            _kpiApproved.Text = _kpiPending.Text = _kpiRejected.Text = _kpiRank.Text = _kpiTime.Text =
                _kpiTeam.Text = _kpiTickets.Text = _kpiActivityTime.Text = _kpiNivel.Text = "—";
            _gridMine.Rows.Add("", "Tu cuenta no está vinculada a un desarrollador. Contacta al líder.", "", "", "", "");
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
            .Select(d => new { d.TeamId, d.TeamRole, d.Seniority, NombreEquipo = d.Team != null ? d.Team.Name : null })
            .FirstOrDefault();

        // El nivel decide qué actividades le tocan («(Junior)», «(Mid)», «(Senior)») y si compite en
        // el ranking: tenerlo a la vista evita que se entere por un «— de N» sin explicación.
        _kpiNivel.Text = NivelDesarrolladorUi.Etiqueta(yo?.Seniority);
        _kpiNivel.ForeColor = NivelDesarrolladorUi.Color(yo?.Seniority);
        _toolTip.SetToolTip(_kpiNivel,
            NivelDesarrolladorUi.Explicacion(yo?.Seniority)
            ?? "Nivel registrado en tu ficha. Lo mantiene el líder.");

        if (yo?.TeamId == null || yo.NombreEquipo == null)
        {
            _kpiTeam.Text = "Sin equipo";
            _kpiTeam.ForeColor = AppTheme.TextSecondary;
            _toolTip.SetToolTip(_kpiTeam, "No estás asignado a ningún equipo. Contacta al líder.");
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
        // Delegado en el servicio: es la misma fuente que usa el administrador, así el desarrollador
        // no ve un total distinto del que se le revisa.
        var t = _scoring.DevMonthlyTotals(devId, year, month);

        _kpiApproved.Text = (t.Approved >= 0 ? "+" : "") + t.Approved;
        _kpiPending.Text  = "+" + t.Pending;
        _kpiRejected.Text = t.RejectedCount.ToString();
        _kpiTime.Text = WorkSessionService.Format(_work.GetTotalSecondsByDeveloper(devId));
        _toolTip.SetToolTip(_kpiTime,
            "Todo lo que ha medido el cronómetro sobre tus requerimientos y actividades libres,\n" +
            "desde siempre. No depende del mes elegido arriba.");

        int declarado = t.MinutesApproved + t.MinutesPending;
        _kpiActivityTime.Text = FormatoMinutos(declarado);
        _kpiActivityTime.ForeColor = declarado > 0 ? AppTheme.Warning : AppTheme.TextSecondary;
        _toolTip.SetToolTip(_kpiActivityTime,
            $"Tiempo que declaraste al registrar tus actividades de este mes.\n" +
            $"  • Ya aprobado: {FormatoMinutos(t.MinutesApproved)}\n" +
            $"  • En revisión: {FormatoMinutos(t.MinutesPending)}\n" +
            "Lo rechazado no se cuenta. Es independiente del cronómetro.");
    }

    /// <summary>Minutos como «3h 20m» / «45m» / «—». Los totales declarados se capturan en minutos.</summary>
    private static string FormatoMinutos(int minutos)
    {
        if (minutos <= 0) return "—";
        return minutos >= 60 ? $"{minutos / 60}h {minutos % 60:00}m" : $"{minutos}m";
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
            _gridTickets.Rows.Add("", "", "(no encontramos tickets de DevOps a tu nombre — revisa tu correo con el líder)", "", "");
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
        // Entidades completas (no una proyección): «Corregir» necesita precargar comentario,
        // tiempo, enlace y captura en el formulario, y el tooltip necesita la descripción del
        // criterio. AsNoTracking porque el AppDbContext es Singleton y esto es solo lectura.
        _myPoints = _db.PointEntries
            .Where(p => p.DeveloperId == devId)
            .Include(p => p.Criterion)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .AsNoTracking()
            .ToList();

        foreach (var p in _myPoints)
        {
            bool esAuto = p.SubmittedByDeveloperId != null;
            int i = _gridPoints.Rows.Add(
                p.Id,
                p.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                p.Criterion?.Name ?? "—",
                (p.Points >= 0 ? "+" : "") + p.Points,
                FormatoMinutos(p.MinutesSpent ?? 0),
                string.IsNullOrWhiteSpace(p.EvidenceUrl) ? "" : "🔗",
                esAuto ? "Autocalificación" : "Asignado por líder",
                ApprovalLabel(p.ApprovalStatus),
                p.ReviewRound > 0 ? $"×{p.ReviewRound}" : "",
                // El motivo importa sobre todo cuando se rechaza; si no hay, se deja vacío.
                string.IsNullOrWhiteSpace(p.ReviewComment) ? (p.ApprovalStatus == PointApprovalStatus.Rechazado ? "(sin motivo registrado)" : "") : p.ReviewComment);

            var fila = _gridPoints.Rows[i];
            var estadoCell = fila.Cells["State"];
            estadoCell.Style.ForeColor = ApprovalColor(p.ApprovalStatus);
            estadoCell.Style.Font = AppTheme.BoldFont;
            if (p.ApprovalStatus == PointApprovalStatus.Rechazado)
                fila.Cells["Review"].Style.ForeColor = AppTheme.Danger;

            // Qué significaba el criterio: es la misma duda de cuando se registra, pero ahora
            // mirando el histórico («¿por qué esto valió 3 y aquello 5?»).
            fila.Cells["Crit"].ToolTipText = string.IsNullOrWhiteSpace(p.Criterion?.Description)
                ? "(esta actividad no tiene descripción)"
                : p.Criterion!.Description!;

            if (!string.IsNullOrWhiteSpace(p.EvidenceUrl))
            {
                fila.Cells["Link"].ToolTipText = $"{p.EvidenceUrl}\n\n(doble clic en la fila para abrirlo)";
                fila.Cells["Link"].Style.ForeColor = AppTheme.SidebarActive;
            }
            if (!string.IsNullOrWhiteSpace(p.Comment))
                fila.Cells["Crit"].ToolTipText += $"\n\nTu comentario:\n{p.Comment}";

            if (esAuto)
                fila.Cells["Id"].ToolTipText = p.ApprovalStatus switch
                {
                    PointApprovalStatus.Pendiente => "En revisión. Todavía puedes corregirla con «✏ Corregir».",
                    PointApprovalStatus.Rechazado => "Rechazada. Puedes corregirla, y con «🗣 Replicar» argumentar y devolverla a revisión.",
                    _ => "Aprobada: ya no se puede modificar."
                };

            // Todo el ida y vuelta, que es lo que explica en qué quedó la discusión.
            if (!string.IsNullOrWhiteSpace(p.ReviewHistory))
            {
                fila.Cells["Review"].ToolTipText = p.ReviewHistory!.Replace("\n", Environment.NewLine);
                fila.Cells["Round"].ToolTipText = $"Ha ido y vuelto {p.ReviewRound} vez/veces.\n\n" +
                                                  p.ReviewHistory!.Replace("\n", Environment.NewLine);
                fila.Cells["Round"].Style.ForeColor = AppTheme.Warning;
            }
        }

        if (_myPoints.Count == 0)
            _gridPoints.Rows.Add("", "", "(no tienes actividades ni puntos registrados)", "", "", "", "", "", "", "");
    }

    private void GridPoints_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var entrada = EntradaSeleccionada();
        if (entrada == null) return;

        // Con enlace, abrirlo es lo que se espera del doble clic; sin él, lo útil es corregir.
        if (!string.IsNullOrWhiteSpace(entrada.EvidenceUrl)) AbrirEnlace(entrada.EvidenceUrl!);
        else BtnEditEntry_Click(null, EventArgs.Empty);
    }

    private static void AbrirEnlace(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private PointEntry? EntradaSeleccionada()
    {
        if (_gridPoints.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _myPoints.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Corrige una autocalificación propia que siga pendiente. Reabre el mismo formulario de
    /// registro con los valores ya capturados: el desarrollador no tiene que volver a adjuntar la
    /// captura ni reescribir el comentario para arreglar un dato.
    /// </summary>
    private void BtnEditEntry_Click(object? s, EventArgs e)
    {
        var entrada = EntradaSeleccionada();
        if (entrada == null)
        {
            MessageBox.Show("Selecciona una actividad en la pestaña «📝 Mis actividades y puntos».",
                "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Se avisa aquí además de validarlo en el servicio para no abrir un formulario que no va a
        // poder guardarse.
        if (entrada.SubmittedByDeveloperId == null)
        {
            MessageBox.Show("Esos puntos te los asignó el líder: no se corrigen desde aquí.",
                "No editable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (entrada.ApprovalStatus == PointApprovalStatus.Aprobado)
        {
            MessageBox.Show("La actividad ya fue aprobada y no se puede modificar.",
                "No editable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new SelfPointEntryForm(_db, entrada.DeveloperId, entrada);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK || frm.Result == null) return;

        try
        {
            var (ok, mensaje) = _scoring.EditarAutocalificacion(entrada.Id, frm.Result, _currentUser);
            if (!ok) { MessageBox.Show(mensaje, "No se pudo guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            _audit.Record(AuditAction.Update, "PointEntry", entrada.Id.ToString(), "Autocalificación corregida por el desarrollador");
            MessageBox.Show(mensaje, "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Réplica a un rechazo: el desarrollador argumenta y la actividad vuelve a revisión. Antes un
    /// rechazo era el final del camino y el desacuerdo se iba a un chat donde no queda constancia.
    /// </summary>
    private void BtnReplicar_Click(object? s, EventArgs e)
    {
        var entrada = EntradaSeleccionada();
        if (entrada == null)
        {
            MessageBox.Show("Selecciona una actividad rechazada en la pestaña «📝 Mis actividades y puntos».",
                "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Se avisa aquí además de validarlo en el servicio, para no abrir un diálogo inútil.
        if (!entrada.AdmiteReplica)
        {
            MessageBox.Show(entrada.ApprovalStatus switch
                {
                    PointApprovalStatus.Aprobado  => "Esta actividad ya fue aprobada: no hay nada que replicar.",
                    PointApprovalStatus.Pendiente => "Esta actividad ya está en revisión; espera la respuesta.",
                    _ => "Esos puntos te los asignó el líder, no son una autocalificación tuya."
                },
                "No se puede replicar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new PointEntryReplyForm(entrada);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (ok, mensaje) = _scoring.Replicar(entrada.Id, frm.Argumento, _currentUser);
            if (!ok) { MessageBox.Show(mensaje, "No se pudo enviar", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            _audit.Record(AuditAction.Update, "PointEntry", entrada.Id.ToString(),
                "Autocalificación replicada por el desarrollador y devuelta a revisión");
            MessageBox.Show(mensaje, "Enviada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        { MessageBox.Show("Todavía no hay actividades que puedas registrar. Pídeselas al líder.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

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
