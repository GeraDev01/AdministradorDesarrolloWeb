using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Administración de los compromisos de atención (SLA): asignarlos sobre requerimientos o
/// actividades libres, y vigilar cuáles están por vencer o ya se vencieron sin comentario en
/// el ticket de Azure DevOps.
/// </summary>
public class SlaAdminControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly SlaService _sla;
    private readonly SlaNotificationService _notify;

    private ComboBox _cbxEstado = null!;
    private DataGridView _grid = null!;
    private Label _kpiActivos = null!, _kpiPorVencer = null!, _kpiVencidos = null!;
    private List<SlaCommitment> _rows = [];

    public SlaAdminControl(AppDbContext db, SlaService sla, SlaNotificationService notify)
    {
        _db = db; _sla = sla; _notify = notify;
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
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 590f));   // ancho de los botones + sus márgenes
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var izq = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        izq.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _cbxEstado = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 0, 0) };
        _cbxEstado.Items.AddRange(["Activos", "Vencidos", "Cumplidos", "Cancelados", "Todos"]);
        _cbxEstado.SelectedIndex = 0;
        _cbxEstado.SelectedIndexChanged += (_, _) => LoadData();
        izq.Controls.Add(_cbxEstado);
        toolbar.Controls.Add(izq, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        var btnNew      = AppTheme.MakePrimaryButton("➕ Asignar SLA", 150);  btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNew_Click;
        var btnDone     = AppTheme.MakeSecondaryButton("✔ Cumplido", 120);    btnDone.Margin = new Padding(4, 2, 0, 0); btnDone.Click += BtnDone_Click;
        var btnCancel   = AppTheme.MakeDangerButton("✖ Cancelar", 120);       btnCancel.Margin = new Padding(4, 2, 0, 0); btnCancel.Click += BtnCancel_Click;
        var btnRevisar  = AppTheme.MakeSecondaryButton("🔔 Revisar vencidos", 160); btnRevisar.Margin = new Padding(4, 2, 0, 0); btnRevisar.Click += BtnRevisar_Click;
        btns.Controls.AddRange([btnNew, btnDone, btnCancel, btnRevisar]);
        toolbar.Controls.Add(btns, 1, 0);

        var kpis = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        kpis.Controls.Add(MakeKpi("⏱ Activos", out _kpiActivos, AppTheme.SidebarActive));
        kpis.Controls.Add(MakeKpi("⚠ Vencen hoy", out _kpiPorVencer, AppTheme.Warning));
        kpis.Controls.Add(MakeKpi("❌ Vencidos", out _kpiVencidos, AppTheme.Danger));

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",            Name = "Id",       Width = 45, FillWeight = 4 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Responsable",   Name = "Dev",      FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Objetivo",      Name = "Target",   FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ticket",        Name = "Ticket",   FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vence",         Name = "Due",      FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",        Name = "State",    FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Recordatorio",  Name = "Reminder", FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentarios",   Name = "Comments", FillWeight = 10 });

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(kpis,    0, 1);
        tbl.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(tbl);
    }

    private static Panel MakeKpi(string title, out Label value, Color accent)
    {
        var card = new Panel { Width = 220, Height = 80, Margin = new Padding(0, 4, 10, 4), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accent });
        var content = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(10, 6, 6, 6) };
        content.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = title, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft });
        value = new Label { Dock = DockStyle.Fill, Text = "—", Font = AppTheme.KpiValueFont, ForeColor = accent, TextAlign = ContentAlignment.MiddleLeft };
        content.Controls.Add(value);
        card.Controls.Add(content);
        return card;
    }

    private void LoadData()
    {
        SlaStatus? filtro = _cbxEstado.SelectedIndex switch
        {
            0 => SlaStatus.Activo,
            1 => SlaStatus.Vencido,
            2 => SlaStatus.Cumplido,
            3 => SlaStatus.Cancelado,
            _ => null
        };
        _rows = _sla.Todos(filtro);

        var ahora = DateTime.UtcNow;
        _grid.Rows.Clear();
        foreach (var s in _rows)
        {
            var venceLocal = s.DueAtUtc.ToLocalTime();
            int i = _grid.Rows.Add(
                s.Id,
                s.Developer?.FullName ?? "—",
                SlaService.DescribirObjetivo(s),
                s.DevOpsTicketExternalId is int t ? $"#{t}" : "—",
                venceLocal.ToString("dd/MM/yyyy HH:mm"),
                EstadoLabel(s, ahora),
                s.NextReminderAtUtc is DateTime n ? n.ToLocalTime().ToString("dd/MM HH:mm") : "—",
                s.CommentCount == 0 ? "sin comentarios"
                    : $"{s.CommentCount} · {s.LastCommentAtUtc?.ToLocalTime():dd/MM HH:mm}");

            _grid.Rows[i].Cells["State"].Style.Font = AppTheme.BoldFont;
            _grid.Rows[i].Cells["State"].Style.ForeColor = ColorEstado(s, ahora);
            if (s.Status == SlaStatus.Activo && s.CommentCount == 0 && s.EstaVencido(ahora))
                _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
        }
        if (_rows.Count == 0) _grid.Rows.Add("", "", "(no hay compromisos con ese filtro)", "", "", "", "", "");

        var activos = _sla.Todos(SlaStatus.Activo);
        _kpiActivos.Text = activos.Count.ToString();
        _kpiPorVencer.Text = activos.Count(s => s.DueAtUtc.ToLocalTime().Date == DateTime.Today).ToString();
        _kpiVencidos.Text = (_sla.Todos(SlaStatus.Vencido).Count + activos.Count(s => s.EstaVencido(ahora))).ToString();
    }

    private static string EstadoLabel(SlaCommitment s, DateTime ahora) => s.Status switch
    {
        SlaStatus.Activo    => s.EstaVencido(ahora) ? "⚠ Fuera de plazo" : "🟢 En plazo",
        SlaStatus.Cumplido  => "✅ Cumplido",
        SlaStatus.Vencido   => "❌ Vencido",
        _                   => "⚪ Cancelado"
    };

    private static Color ColorEstado(SlaCommitment s, DateTime ahora) => s.Status switch
    {
        SlaStatus.Activo   => s.EstaVencido(ahora) ? AppTheme.Warning : AppTheme.Success,
        SlaStatus.Cumplido => AppTheme.Success,
        SlaStatus.Vencido  => AppTheme.Danger,
        _                  => AppTheme.TextSecondary
    };

    private SlaCommitment? Seleccionado() =>
        _grid.CurrentRow?.Cells["Id"].Value is int id ? _rows.FirstOrDefault(s => s.Id == id) : null;

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new SlaAssignForm(_db);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() =>
        {
            var (ok, msg, _) = _sla.Asignar(frm.Objetivo, frm.DeveloperId, frm.VenceLocal,
                frm.CadaHoras, frm.TicketId, frm.Url, frm.Notas);
            return (ok, msg);
        });
    }

    private void BtnDone_Click(object? s, EventArgs e)
    {
        if (Seleccionado() is not { } sla) { Avisa(); return; }
        Ejecutar(() => _sla.MarcarCumplido(sla.Id));
    }

    private void BtnCancel_Click(object? s, EventArgs e)
    {
        if (Seleccionado() is not { } sla) { Avisa(); return; }
        if (MessageBox.Show("¿Dejar sin efecto este SLA?", "Cancelar SLA",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Ejecutar(() => _sla.Cancelar(sla.Id));
    }

    /// <summary>Marca vencimientos y escala por correo. También corre solo desde MainForm.</summary>
    private async void BtnRevisar_Click(object? s, EventArgs e)
    {
        try
        {
            int n = await _notify.EscalarIncumplimientosAsync();
            LoadData();
            MessageBox.Show(n == 0
                    ? "No hay compromisos vencidos sin reportar."
                    : $"{n} compromiso(s) marcado(s) como vencido(s). Se notificó a administración si el correo está configurado.",
                "Revisión de SLA", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo completar la revisión:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Avisa() =>
        MessageBox.Show("Selecciona un compromiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
