using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Agenda de despliegues. Deja explícito en pantalla el límite del enfoque: los ejecuta una
/// aplicación abierta, así que si a esa hora no hay ninguna corriendo el despliegue se pierde.
/// </summary>
public class ScheduledDeploymentsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly DeploymentScheduleService _schedule;
    private readonly CurrentUserContext _currentUser;

    private DataGridView _grid = null!;
    private CheckBox _chkSoloPendientes = null!;
    private List<ScheduledDeployment> _rows = [];

    public ScheduledDeploymentsControl(AppDbContext db, DeploymentScheduleService schedule, CurrentUserContext currentUser)
    {
        _db = db; _schedule = schedule; _currentUser = currentUser;
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
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470f));   // ancho de los botones + sus márgenes
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _chkSoloPendientes = new CheckBox { Text = "Solo pendientes", AutoSize = true, Checked = true, Margin = new Padding(0, 7, 0, 0) };
        _chkSoloPendientes.CheckedChanged += (_, _) => LoadData();
        var izq = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        izq.Controls.Add(_chkSoloPendientes);
        toolbar.Controls.Add(izq, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        var btnNew = AppTheme.MakePrimaryButton("🗓 Programar despliegue", 210); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNew_Click;
        var btnCancel = AppTheme.MakeDangerButton("✖ Cancelar", 120); btnCancel.Margin = new Padding(4, 2, 0, 0); btnCancel.Click += BtnCancel_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110); btnReload.Margin = new Padding(4, 2, 0, 0); btnReload.Click += (_, _) => LoadData();
        btns.Controls.AddRange([btnNew, btnCancel, btnReload]);
        toolbar.Controls.Add(btns, 1, 0);

        var aviso = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.Warning,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0),
            Text = "⚠  Los ejecuta una aplicación abierta (basta con dejarla en la bandeja del sistema). " +
                   "Si a la hora programada no hay ninguna corriendo, el despliegue se marca como Perdido y NO se ejecuta después."
        };

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",        Name = "Id",      Width = 45, FillWeight = 4 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cuándo",    Name = "When",    FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sistema",   Name = "System",  FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión",   Name = "Version", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Perfil",    Name = "Profile", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",    Name = "State",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ejecutó",   Name = "Claimed", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Resultado", Name = "Result",  FillWeight = 16 });

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(aviso,   0, 1);
        tbl.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        try { _rows = _schedule.Listar(soloPendientes: _chkSoloPendientes.Checked); }
        catch (AuthorizationException) { _rows = []; }

        _grid.Rows.Clear();
        foreach (var s in _rows)
        {
            int i = _grid.Rows.Add(
                s.Id,
                s.ScheduledAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                s.AppRelease?.AppSystem?.Name ?? "—",
                s.AppRelease?.Version ?? "—",
                s.Profile?.Name ?? "—",
                EstadoLabel(s.Status),
                s.ClaimedBy ?? "—",
                s.ResultMessage ?? "");
            _grid.Rows[i].Cells["State"].Style.Font = AppTheme.BoldFont;
            _grid.Rows[i].Cells["State"].Style.ForeColor = EstadoColor(s.Status);
        }
        if (_rows.Count == 0) _grid.Rows.Add("", "", "(no hay despliegues programados)", "", "", "", "", "");
    }

    private static string EstadoLabel(ScheduledDeploymentStatus s) => s switch
    {
        ScheduledDeploymentStatus.Programado  => "🕓 Programado",
        ScheduledDeploymentStatus.EnEjecucion => "▶ En ejecución",
        ScheduledDeploymentStatus.Completado  => "✅ Completado",
        ScheduledDeploymentStatus.Fallido     => "❌ Fallido",
        ScheduledDeploymentStatus.Cancelado   => "⚪ Cancelado",
        _                                     => "⚠ Perdido"
    };

    private static Color EstadoColor(ScheduledDeploymentStatus s) => s switch
    {
        ScheduledDeploymentStatus.Completado  => AppTheme.Success,
        ScheduledDeploymentStatus.Fallido     => AppTheme.Danger,
        ScheduledDeploymentStatus.Perdido     => AppTheme.Warning,
        ScheduledDeploymentStatus.EnEjecucion => AppTheme.SidebarActive,
        _                                     => AppTheme.TextSecondary
    };

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new ScheduleDeploymentForm(_db, _currentUser);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() =>
        {
            var (ok, msg, _) = frm.PorServidores
                ? _schedule.ProgramarServidores(frm.ReleaseId, frm.TargetIds, frm.CuandoLocal, frm.ToleranciaMinutos, frm.Notas)
                : _schedule.Programar(frm.ReleaseId, frm.ProfileId, frm.CuandoLocal, frm.ToleranciaMinutos, frm.Notas);
            return (ok, msg);
        });
    }

    private void BtnCancel_Click(object? s, EventArgs e)
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una programación.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show("¿Cancelar este despliegue programado?", "Confirmar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Ejecutar(() => _schedule.Cancelar(id));
    }

    private void Ejecutar(Func<(bool ok, string mensaje)> operacion)
    {
        try
        {
            var (ok, mensaje) = operacion();
            LoadData();
            MessageBox.Show(mensaje, ok ? "Listo" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
