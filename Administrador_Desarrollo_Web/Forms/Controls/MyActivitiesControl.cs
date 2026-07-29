using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Actividades libres del desarrollador: trabajo fuera de sus requerimientos asignados, con su
/// propio cronómetro. Comparte el mismo cronómetro que "Mis Asignaciones": iniciar aquí pausa
/// automáticamente lo que estuviera corriendo allá, y al revés.
/// </summary>
public class MyActivitiesControl : UserControl
{
    private readonly CurrentUserContext _currentUser;
    private readonly DevActivityService _activities;
    private readonly WorkSessionService _work;

    private DataGridView _grid = null!;
    private CheckBox _chkVerCerradas = null!;
    private Button _btnNew = null!, _btnEdit = null!, _btnClose = null!, _btnDelete = null!;
    private Button _btnStart = null!, _btnPause = null!, _btnStop = null!;
    private Label _lblTimerItem = null!, _lblTime = null!, _lblTimerState = null!;
    private System.Windows.Forms.Timer _timer = null!;

    private List<DevActivity> _rows = [];
    private int? _selectedId;
    private bool _loading;

    public MyActivitiesControl(CurrentUserContext currentUser, DevActivityService activities, WorkSessionService work)
    {
        _currentUser = currentUser; _activities = activities; _work = work;
        BuildUI();
        LoadData();
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
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var izq = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        _chkVerCerradas = new CheckBox { Text = "Ver cerradas", AutoSize = true, Checked = true, Margin = new Padding(0, 7, 0, 0) };
        _chkVerCerradas.CheckedChanged += (_, _) => LoadData();
        izq.Controls.Add(_chkVerCerradas);
        izq.Controls.Add(new Label
        {
            Text = "   Registra aquí el trabajo que no cae en tus requerimientos asignados.",
            AutoSize = true, Margin = new Padding(8, 8, 0, 0),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        toolbar.Controls.Add(izq, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        _btnNew    = AppTheme.MakePrimaryButton("➕ Nueva actividad", 165); _btnNew.Margin = new Padding(4, 2, 0, 0); _btnNew.Click += BtnNew_Click;
        _btnEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 100);        _btnEdit.Margin = new Padding(4, 2, 0, 0); _btnEdit.Click += BtnEdit_Click;
        _btnClose  = AppTheme.MakeSecondaryButton("✔ Cerrar", 110);        _btnClose.Margin = new Padding(4, 2, 0, 0); _btnClose.Click += BtnCloseOrReopen_Click;
        _btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 110);         _btnDelete.Margin = new Padding(4, 2, 0, 0); _btnDelete.Click += BtnDelete_Click;
        btns.Controls.AddRange([_btnNew, _btnEdit, _btnClose, _btnDelete]);
        toolbar.Controls.Add(btns, 1, 0);

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",       Name = "Id",    Width = 45, FillWeight = 5  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actividad", Name = "Title", FillWeight = 38 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",    Name = "State", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Creada",    Name = "Created", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "⏱ Tiempo dedicado", Name = "Time", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc", FillWeight = 18 });
        _grid.SelectionChanged += (_, _) => OnRowSelected();
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnEdit_Click(null, EventArgs.Empty); };

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnlGrid, 0, 1);
        tbl.Controls.Add(BuildTimerPanel(), 0, 2);
        Controls.Add(tbl);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => TickTime();
    }

    private Panel BuildTimerPanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(16, 8, 16, 8), Margin = new Padding(10, 0, 10, 8) };
        pnl.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = AppTheme.Border });

        _lblTimerItem  = new Label { Location = new Point(16, 10), AutoSize = false, Size = new Size(520, 20), Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Text = "Selecciona una actividad…" };
        _lblTime       = new Label { Location = new Point(16, 34), AutoSize = false, Size = new Size(230, 40), Font = AppTheme.TimerFont, ForeColor = AppTheme.TextPrimary, Text = "00m 00s", TextAlign = ContentAlignment.MiddleLeft };
        _lblTimerState = new Label { Location = new Point(250, 46), AutoSize = true, Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary, Text = "" };

        _btnStart = AppTheme.MakePrimaryButton("▶  Iniciar", 130, 40); _btnStart.Location = new Point(470, 34); _btnStart.Click += BtnStart_Click;
        _btnPause = AppTheme.MakeSecondaryButton("⏸  Pausar", 120, 40); _btnPause.Location = new Point(608, 34); _btnPause.Click += BtnPause_Click;
        _btnStop  = AppTheme.MakeDangerButton("⏹  Detener", 120, 40);   _btnStop.Location  = new Point(736, 34); _btnStop.Click  += BtnStop_Click;

        pnl.Controls.AddRange([_lblTimerItem, _lblTime, _lblTimerState, _btnStart, _btnPause, _btnStop]);
        return pnl;
    }

    private void LoadData()
    {
        var devId = _currentUser.DeveloperId;
        if (devId == null)
        {
            _rows = [];
            RefreshGrid();
            return;
        }
        _rows = _activities.DeDesarrollador(devId.Value, incluirCerradas: _chkVerCerradas.Checked);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        int? keep = _selectedId;
        _loading = true;
        _grid.Rows.Clear();

        foreach (var a in _rows)
        {
            int i = _grid.Rows.Add(a.Id, a.Title,
                a.Status == DevActivityStatus.Abierta ? "🟢 Abierta" : "⚪ Cerrada",
                a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                WorkSessionService.Format(_work.GetTotalSecondsByActivity(a.Id)),
                a.Description ?? "");
            _grid.Rows[i].Cells["State"].Style.ForeColor = a.Status == DevActivityStatus.Abierta ? AppTheme.Success : AppTheme.TextSecondary;
            _grid.Rows[i].Cells["State"].Style.Font = AppTheme.BoldFont;
        }

        DataGridViewRow? target = null;
        if (keep != null)
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Cells["Id"].Value is int id && id == keep) { target = row; break; }

        _loading = false;
        if (target != null) _grid.CurrentCell = target.Cells["Id"];
        OnRowSelected();
    }

    private DevActivity? Seleccionada() =>
        _selectedId is int id ? _rows.FirstOrDefault(a => a.Id == id) : null;

    private void OnRowSelected()
    {
        if (_loading) return;
        _selectedId = _grid.CurrentRow?.Cells["Id"].Value as int?;
        RefreshTimerPanel();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var a = Seleccionada();
        bool hay = a != null;
        bool abierta = a?.Status == DevActivityStatus.Abierta;

        _btnEdit.Enabled = hay && abierta;
        _btnDelete.Enabled = hay;
        _btnClose.Enabled = hay;
        _btnClose.Text = hay && !abierta ? "↩ Reabrir" : "✔ Cerrar";
    }

    private void RefreshTimerPanel()
    {
        var devId = _currentUser.DeveloperId;
        var a = Seleccionada();

        if (devId == null || a == null)
        {
            _lblTimerItem.Text = devId == null
                ? "Tu cuenta no está vinculada a un desarrollador."
                : "Selecciona una actividad…";
            _lblTime.Text = "00m 00s"; _lblTimerState.Text = "";
            _btnStart.Enabled = _btnPause.Enabled = _btnStop.Enabled = false;
            _timer.Stop();
            return;
        }

        _lblTimerItem.Text = $"🧩  {a.Title}";

        var target = WorkTarget.Actividad(a.Id);
        var open = _work.GetOpenSession(devId.Value, target);
        bool running = open?.Status == WorkSessionStatus.Activa;
        bool paused  = open?.Status == WorkSessionStatus.Pausada;
        bool cerrada = a.Status == DevActivityStatus.Cerrada;

        // Una actividad cerrada no se puede seguir cronometrando; sí se puede consultar su total.
        _btnStart.Enabled = !running && !cerrada;
        _btnStart.Text = paused ? "▶  Reanudar" : "▶  Iniciar";
        _btnPause.Enabled = running;
        _btnStop.Enabled = running || paused;

        _lblTimerState.Text = cerrada ? "✔ Actividad cerrada"
            : running ? "● En curso" : paused ? "⏸ Pausado" : "○ Sin sesión activa";
        _lblTimerState.ForeColor = running ? AppTheme.Success : paused ? AppTheme.Warning : AppTheme.TextSecondary;

        UpdateTimeLabel(a.Id);
        if (running) _timer.Start(); else _timer.Stop();
    }

    private void UpdateTimeLabel(int activityId) =>
        _lblTime.Text = WorkSessionService.Format(_work.GetTotalSecondsByActivity(activityId));

    private void TickTime()
    {
        if (_selectedId is not int id) return;
        UpdateTimeLabel(id);
        if (_grid.CurrentRow?.Cells["Id"].Value is int rowId && rowId == id)
            _grid.CurrentRow.Cells["Time"].Value = WorkSessionService.Format(_work.GetTotalSecondsByActivity(id));
    }

    // ── Acciones ────────────────────────────────────────────────────────────────

    private void BtnNew_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId)
        {
            MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var frm = new DevActivityForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() =>
        {
            var (ok, msg, actividad) = _activities.Crear(devId, frm.Titulo, frm.Descripcion);
            if (ok && actividad != null) _selectedId = actividad.Id;
            return (ok, msg);
        }, avisarExito: false);
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        if (Seleccionada() is not { } a) return;
        if (a.Status == DevActivityStatus.Cerrada)
        {
            MessageBox.Show("La actividad está cerrada. Reábrela para editarla.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var frm = new DevActivityForm(a);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        Ejecutar(() => _activities.Renombrar(a.Id, frm.Titulo, frm.Descripcion), avisarExito: false);
    }

    private void BtnCloseOrReopen_Click(object? s, EventArgs e)
    {
        if (Seleccionada() is not { } a) { MessageBox.Show("Selecciona una actividad.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (a.Status == DevActivityStatus.Cerrada)
        {
            Ejecutar(() => _activities.Reabrir(a.Id), avisarExito: false);
            return;
        }

        var total = WorkSessionService.Format(_work.GetTotalSecondsByActivity(a.Id));
        if (MessageBox.Show(
                $"¿Cerrar «{a.Title}»?\n\nTiempo registrado: {total}\nSi el cronómetro está corriendo, se detendrá.",
                "Cerrar actividad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Ejecutar(() => _activities.Cerrar(a.Id));
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        if (Seleccionada() is not { } a) { MessageBox.Show("Selecciona una actividad.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (MessageBox.Show($"¿Eliminar «{a.Title}»?\n\nSolo se puede si no tiene tiempo registrado.",
                "Eliminar actividad", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Ejecutar(() =>
        {
            var r = _activities.Eliminar(a.Id);
            if (r.ok) _selectedId = null;
            return r;
        });
    }

    private void BtnStart_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || Seleccionada() is not { } a) return;
        Ejecutar(() =>
        {
            _work.StartOrResume(devId, WorkTarget.Actividad(a.Id));
            return (true, "");
        }, avisarExito: false);
    }

    private void BtnPause_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || Seleccionada() is not { } a) return;
        Ejecutar(() => { _work.Pause(devId, WorkTarget.Actividad(a.Id)); return (true, ""); }, avisarExito: false);
    }

    private void BtnStop_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId || Seleccionada() is not { } a) return;
        Ejecutar(() => { _work.Stop(devId, WorkTarget.Actividad(a.Id)); return (true, ""); }, avisarExito: false);
    }

    private void Ejecutar(Func<(bool ok, string mensaje)> operacion, bool avisarExito = true)
    {
        try
        {
            var (ok, mensaje) = operacion();
            LoadData();
            if (!ok && mensaje.Length > 0)
                MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (ok && avisarExito && mensaje.Length > 0)
                MessageBox.Show(mensaje, "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadData(); else _timer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }
}
