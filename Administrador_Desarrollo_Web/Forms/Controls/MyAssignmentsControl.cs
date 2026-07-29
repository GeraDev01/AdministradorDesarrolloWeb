using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class MyAssignmentsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly WorkSessionService _work;
    private readonly AzureDevOpsService _devops;

    private DataGridView _grid = null!;
    private ComboBox _cbxStatus = null!;
    private List<Requirement> _allReqs = [];

    // ── Cronómetro ───────────────────────────────────────────────
    private System.Windows.Forms.Timer _timer = null!;
    private Label _lblTimerItem = null!;
    private Label _lblTime = null!;
    private Label _lblTimerState = null!;
    private Label _lblDevopsSync = null!;
    private Button _btnStart = null!, _btnPause = null!, _btnStop = null!;
    private int? _selectedReqId;
    private bool _loading;   // silencia SelectionChanged espurios mientras se repuebla la grilla

    public MyAssignmentsControl(AppDbContext db, CurrentUserContext currentUser, WorkSessionService work, AzureDevOpsService devops)
    {
        _db = db; _currentUser = currentUser; _work = work; _devops = devops;
        BuildUI();
        SincronizarYCargar();
    }

    /// <summary>
    /// Trae automáticamente los tickets de DevOps asignados como requerimientos (para medirles el
    /// tiempo aquí) y luego carga la lista. Es lo que hace que aparezcan solos, sin importar a mano.
    /// </summary>
    private void SincronizarYCargar()
    {
        TraerMisTickets(silencioso: true);
        LoadData();
    }

    private void TraerMisTickets(bool silencioso)
    {
        if (_currentUser.DeveloperId is not int devId) return;
        try
        {
            var (nuevos, _) = _devops.MaterializarAsignados(devId);
            if (!silencioso)
                MessageBox.Show(
                    nuevos > 0 ? $"Se trajeron {nuevos} ticket(s) de DevOps a tus asignaciones." : "No hay tickets nuevos de DevOps asignados a ti.",
                    "Mis tickets de DevOps", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            // No romper la pantalla si falla (p. ej. sin permiso de escritura o sin tickets sincronizados).
            if (!silencioso)
                MessageBox.Show($"No se pudieron traer los tickets de DevOps:\n{ex.Message}", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };
        filterFlow.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cbxStatus = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxStatus.Items.Add("Todos");
        foreach (var s in Enum.GetValues<RequirementStatus>()) _cbxStatus.Items.Add(StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxStatus);
        var btnTraer = AppTheme.MakeSecondaryButton("🔄 Traer mis tickets DevOps", 215, 26);
        btnTraer.Margin = new Padding(12, 2, 0, 0);
        btnTraer.Click += (_, _) => { TraerMisTickets(silencioso: false); LoadData(); };
        filterFlow.Controls.Add(btnTraer);
        var btnDia = AppTheme.MakeSecondaryButton("📅 Tiempo por día", 165, 26);
        btnDia.Margin = new Padding(6, 2, 0, 0);
        btnDia.Click += (_, _) =>
        {
            if (_currentUser.DeveloperId is not int devId) { MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using var frm = new TiempoPorDiaForm(_work, devId);
            frm.ShowDialog(FindForm());
        };
        filterFlow.Controls.Add(btnDia);
        filterFlow.Controls.Add(new Label { Text = "   Selecciona un item para cronometrar el tiempo que le dedicas.", AutoSize = true, Margin = new Padding(8, 6, 0, 0), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        toolbar.Controls.Add(filterFlow, 0, 0);

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",            Name = "Id",       Width = 45,  FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",         Name = "Title",    FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",         Name = "Status",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Prioridad",      Name = "Priority", FillWeight = 9  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hrs Estimadas",  Name = "Hours",    FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "F. Compromiso",  Name = "Date",     FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Avance",         Name = "Progress", FillWeight = 7  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "⏱ Tiempo dedicado", Name = "Time",  FillWeight = 13 });
        _grid.SelectionChanged += (_, _) => OnRowSelected();

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(pnlGrid,   0, 1);
        tbl.Controls.Add(BuildTimerPanel(), 0, 2);
        Controls.Add(tbl);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => TickTime();
    }

    private Panel BuildTimerPanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(16, 8, 16, 8), Margin = new Padding(10, 0, 10, 8) };
        pnl.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = AppTheme.Border });

        _lblTimerItem = new Label { Location = new Point(16, 10), AutoSize = false, Size = new Size(520, 20), Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Text = "Selecciona un item…" };
        _lblTime = new Label { Location = new Point(16, 34), AutoSize = false, Size = new Size(230, 40), Font = AppTheme.TimerFont, ForeColor = AppTheme.TextPrimary, Text = "00m 00s", TextAlign = ContentAlignment.MiddleLeft };
        _lblTimerState = new Label { Location = new Point(250, 46), AutoSize = true, Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary, Text = "" };
        _lblDevopsSync = new Label { Location = new Point(16, 78), AutoSize = false, Size = new Size(440, 18), Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Text = "", AutoEllipsis = true };

        _btnStart = AppTheme.MakePrimaryButton("▶  Iniciar", 130, 40); _btnStart.Location = new Point(470, 34); _btnStart.Click += BtnStart_Click;
        _btnPause = AppTheme.MakeSecondaryButton("⏸  Pausar", 120, 40); _btnPause.Location = new Point(608, 34); _btnPause.Click += BtnPause_Click;
        _btnStop  = AppTheme.MakeDangerButton("⏹  Detener", 120, 40);   _btnStop.Location  = new Point(736, 34); _btnStop.Click  += BtnStop_Click;

        pnl.Controls.AddRange([_lblTimerItem, _lblTime, _lblTimerState, _lblDevopsSync, _btnStart, _btnPause, _btnStop]);
        return pnl;
    }

    private void LoadData()
    {
        var devId = _currentUser.DeveloperId;
        if (devId == null) { _allReqs = []; FilterGrid(); return; }

        _allReqs = _db.Requirements
            .Include(r => r.Assignments)
            .Where(r => r.Assignments.Any(a => a.DeveloperId == devId))
            .OrderByDescending(r => r.CommittedDeliveryDate)
            .AsNoTracking()
            .ToList();
        FilterGrid();
    }

    private void FilterGrid()
    {
        int? keepSel = _selectedReqId;
        _loading = true;
        _grid.Rows.Clear();
        var devId = _currentUser.DeveloperId;
        var q = _allReqs.AsEnumerable();
        if (_cbxStatus.SelectedIndex > 0)
            q = q.Where(r => (int)r.Status == _cbxStatus.SelectedIndex - 1);

        foreach (var r in q)
        {
            bool overdue = r.CommittedDeliveryDate < DateTime.Today
                && r.Status != RequirementStatus.Entregado
                && r.Status != RequirementStatus.Cancelado;
            string timeLabel = devId != null ? WorkSessionService.Format(_work.GetTotalSeconds(devId.Value, r.Id)) : "—";
            int i = _grid.Rows.Add(r.Id, r.Title, StatusLabel(r.Status), PriorityLabel(r.Priority),
                r.EstimateHours?.ToString("0.#") ?? "—",
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                $"{r.ProgressPercent}%", timeLabel);
            _grid.Rows[i].Cells["Status"].Style.ForeColor = AppTheme.StatusColor(r.Status);
            _grid.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
            if (overdue) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger;
        }

        // Localizar la fila previamente seleccionada (si sigue visible tras el filtro).
        DataGridViewRow? target = null;
        if (keepSel != null)
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Cells["Id"].Value is int id && id == keepSel) { target = row; break; }

        _loading = false;
        // CurrentCell mueve CurrentRow Y el resaltado, manteniendo sincronizada la selección
        // lógica con la visual (row.Selected NO mueve CurrentRow y desincroniza _selectedReqId).
        if (target != null) _grid.CurrentCell = target.Cells["Id"];
        OnRowSelected();
    }

    private void OnRowSelected()
    {
        if (_loading) return;   // ignora los SelectionChanged disparados durante la repoblación
        _selectedReqId = _grid.CurrentRow?.Cells["Id"].Value as int?;
        RefreshTimerPanel();
    }

    private void RefreshTimerPanel()
    {
        var devId = _currentUser.DeveloperId;
        if (devId == null)
        {
            _lblTimerItem.Text = "Tu cuenta no está vinculada a un desarrollador.";
            _lblTime.Text = "—"; _lblTimerState.Text = "";
            _btnStart.Enabled = _btnPause.Enabled = _btnStop.Enabled = false;
            _timer.Stop();
            return;
        }
        if (_selectedReqId is not int reqId)
        {
            _lblTimerItem.Text = "Selecciona un item…";
            _lblTime.Text = "00m 00s"; _lblTimerState.Text = "";
            _btnStart.Enabled = _btnPause.Enabled = _btnStop.Enabled = false;
            _timer.Stop();
            return;
        }

        var req = _allReqs.FirstOrDefault(r => r.Id == reqId);
        _lblTimerItem.Text = req != null ? $"#{req.Id}  {req.Title}" : $"Item #{reqId}";

        var open = _work.GetOpenSession(devId.Value, reqId);
        bool running = open?.Status == WorkSessionStatus.Activa;
        bool paused = open?.Status == WorkSessionStatus.Pausada;

        _btnStart.Enabled = !running;                 // Iniciar/Reanudar cuando no está corriendo
        _btnStart.Text = paused ? "▶  Reanudar" : "▶  Iniciar";
        _btnPause.Enabled = running;
        _btnStop.Enabled = running || paused;

        _lblTimerState.Text = running ? "● En curso" : paused ? "⏸ Pausado" : "○ Sin sesión activa";
        _lblTimerState.ForeColor = running ? AppTheme.Success : paused ? AppTheme.Warning : AppTheme.TextSecondary;

        UpdateTimeLabel(devId.Value, reqId);
        if (running) _timer.Start(); else _timer.Stop();
    }

    private void UpdateTimeLabel(int devId, int reqId)
    {
        _lblTime.Text = WorkSessionService.Format(_work.GetTotalSeconds(devId, reqId));
    }

    private void TickTime()
    {
        if (_currentUser.DeveloperId is not int devId || _selectedReqId is not int reqId) return;
        UpdateTimeLabel(devId, reqId);
        // Mantener sincronizada la celda "Tiempo dedicado" de la fila cronometrada con el label grande.
        if (_grid.CurrentRow?.Cells["Id"].Value is int id && id == reqId)
            _grid.CurrentRow.Cells["Time"].Value = WorkSessionService.Format(_work.GetTotalSeconds(devId, reqId));
    }

    private void BtnStart_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || _selectedReqId is not int reqId) return;
        _work.StartOrResume(devId, reqId);   // mueve el item a "En desarrollo" de forma atómica
        LoadData();
    }

    private void BtnPause_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || _selectedReqId is not int reqId) return;
        _work.Pause(devId, reqId);
        FilterGrid();
    }

    private async void BtnStop_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || _selectedReqId is not int reqId) return;
        _work.Stop(devId, reqId);
        FilterGrid();
        await ReportarTiempoDevOpsAsync(devId, reqId);
    }

    /// <summary>
    /// Si el administrador activó el reporte de tiempo, registra en el ticket de DevOps el tiempo del
    /// requerimiento al detener. Nunca rompe el flujo del cronómetro: cualquier fallo solo se muestra
    /// como aviso discreto en el panel, y el tiempo local ya quedó guardado.
    /// </summary>
    private async Task ReportarTiempoDevOpsAsync(int devId, int reqId)
    {
        try
        {
            int total = _work.GetTotalSeconds(devId, reqId);
            var (intentado, ok, mensaje) = await _devops.ReportarTiempoDevOpsAsync(reqId, total);
            if (!intentado) { _lblDevopsSync.Text = ""; return; }   // desactivado o no aplica: sin ruido

            _lblDevopsSync.ForeColor = ok ? AppTheme.Success : AppTheme.Warning;
            _lblDevopsSync.Text = ok ? $"✓ Tiempo registrado en DevOps ({mensaje})."
                                     : $"⚠ No se registró en DevOps: {mensaje}";
        }
        catch (Exception ex)
        {
            _lblDevopsSync.ForeColor = AppTheme.Warning;
            _lblDevopsSync.Text = $"⚠ No se registró en DevOps: {ex.Message}";
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) SincronizarYCargar(); else _timer.Stop(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "Por estimar",
        RequirementStatus.Estimado     => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas    => "En pruebas",
        RequirementStatus.PorEntregar  => "Por entregar",
        RequirementStatus.Entregado    => "Entregado",
        RequirementStatus.Cancelado    => "Cancelado",
        _                              => s.ToString()
    };

    private static string PriorityLabel(RequirementPriority p) => p switch
    {
        RequirementPriority.Baja    => "🔵 Baja",
        RequirementPriority.Media   => "🟡 Media",
        RequirementPriority.Alta    => "🟠 Alta",
        RequirementPriority.Critica => "🔴 Crítica",
        _                           => p.ToString()
    };
}
