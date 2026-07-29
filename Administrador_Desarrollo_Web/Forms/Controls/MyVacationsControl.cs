using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class MyVacationsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly AuditService _audit;
    private readonly VacationRequestService _vacations;

    private DataGridView _grid = null!;
    private Button _btnView = null!, _btnCancel = null!, _btnDelete = null!;
    private Label _lblSaldo = null!, _lblSaldoDetalle = null!;
    private List<VacRow> _rows = [];

    private sealed record VacRow(int Id, DateTime Start, DateTime End, VacationStatus Status, string? Comment, string? Review, bool HasAttachment);

    public MyVacationsControl(AppDbContext db, CurrentUserContext currentUser, AuditService audit, VacationRequestService vacations)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _vacations = vacations;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));   // alto para el saldo (2 líneas) sin cortarse
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 570f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        var btnNew = AppTheme.MakePrimaryButton("➕ Nueva solicitud", 150); btnNew.Margin = new Padding(4, 2, 0, 0); btnNew.Click += BtnNew_Click;
        _btnView = AppTheme.MakeSecondaryButton("📎 Ver adjunto", 130); _btnView.Margin = new Padding(4, 2, 0, 0); _btnView.Enabled = false; _btnView.Click += BtnViewAttachment_Click;
        _btnCancel = AppTheme.MakeSecondaryButton("🚫 Cancelar", 120); _btnCancel.Margin = new Padding(4, 2, 0, 0); _btnCancel.Enabled = false; _btnCancel.Click += BtnCancel_Click;
        _btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 120); _btnDelete.Margin = new Padding(4, 2, 0, 0); _btnDelete.Enabled = false; _btnDelete.Click += BtnDelete_Click;
        btnFlow.Controls.AddRange([btnNew, _btnView, _btnCancel, _btnDelete]);

        // Saldo de vacaciones del desarrollador (a la izquierda de la barra).
        var pnlSaldo = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        _lblSaldo = new Label { Location = new Point(6, 4), AutoSize = true, Font = AppTheme.HeaderFont, ForeColor = AppTheme.SidebarActive };
        _lblSaldoDetalle = new Label { Location = new Point(8, 36), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        pnlSaldo.Controls.AddRange([_lblSaldo, _lblSaldoDetalle]);

        toolbar.Controls.Add(pnlSaldo, 0, 0);
        toolbar.Controls.Add(btnFlow, 1, 0);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",          Name = "Id",      Width = 45, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Inicio",       Name = "Start",   FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fin",          Name = "End",     FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",         Name = "Days",    FillWeight = 7  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "Status",  FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Adjunto",      Name = "Attach",  FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mi comentario",           Name = "Comment", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Respuesta / motivo del jefe", Name = "Review",  FillWeight = 22 });
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnViewAttachment_Click(null, EventArgs.Empty); };
        _grid.SelectionChanged += (_, _) => UpdateViewButton();

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(pnlGrid,   0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        var devId = _currentUser.User?.DeveloperId;
        ActualizarSaldo(devId);
        if (devId == null) { _rows = []; RefreshGrid(); return; }

        // Proyección ligera: NO carga los BLOB de los adjuntos (se obtienen bajo demanda al verlos).
        _rows = _db.VacationRequests
            .Where(v => v.DeveloperId == devId)
            .OrderByDescending(v => v.StartDate)
            .Select(v => new VacRow(v.Id, v.StartDate, v.EndDate, v.Status, v.Comment, v.ReviewComment, v.AttachmentFileName != null))
            .ToList();
        RefreshGrid();
    }

    /// <summary>
    /// Muestra los días de vacaciones DISPONIBLES = días asignados (RH, en la ficha) menos los días
    /// ya tomados (aprobados) en el año en curso; el descuento es automático. Como contexto, muestra
    /// los asignados, los tomados y los pendientes de aprobación. No cambia la ficha: solo la lee.
    /// </summary>
    private void ActualizarSaldo(int? devId)
    {
        if (devId == null) { _lblSaldo.Text = ""; _lblSaldoDetalle.Text = ""; return; }

        // AsNoTracking: el DbContext es de larga vida (singleton) y Find serviría el Developer cacheado,
        // dejando los «días asignados» congelados aunque RH los cambie. Se relee fresco cada vez.
        var dev = _db.Developers.AsNoTracking().FirstOrDefault(d => d.Id == devId.Value);
        int asignados = dev?.VacationDaysLeft ?? 0;
        int anio = DateTime.Now.Year;

        int Dias(VacationStatus estado, bool soloEsteAnio) =>
            _db.VacationRequests
               .Where(v => v.DeveloperId == devId && v.Status == estado && (!soloEsteAnio || v.StartDate.Year == anio))
               .Select(v => new { v.StartDate, v.EndDate }).ToList()
               .Sum(x => (x.EndDate.Date - x.StartDate.Date).Days + 1);

        int tomados = Dias(VacationStatus.Aprobada, soloEsteAnio: true);
        int pendientes = Dias(VacationStatus.Pendiente, soloEsteAnio: false);
        int disponibles = asignados - tomados;   // descuento automático de los aprobados del año

        _lblSaldo.Text = $"🌴 Días de vacaciones disponibles: {disponibles}";
        _lblSaldo.ForeColor = disponibles <= 0 ? AppTheme.Danger
                            : disponibles <= 3 ? AppTheme.Warning
                            : AppTheme.SidebarActive;
        _lblSaldoDetalle.Text =
            $"De {asignados} asignado(s) · {tomados} tomado(s) este año · {pendientes} pendiente(s) de aprobación"
            + (dev?.HireDate is DateTime h ? $"  ·  Ingreso: {h:dd/MM/yyyy}" : "");
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var v in _rows)
        {
            int days = (v.End.Date - v.Start.Date).Days + 1;
            int i = _grid.Rows.Add(v.Id,
                v.Start.ToString("dd/MM/yyyy"),
                v.End.ToString("dd/MM/yyyy"),
                days,
                StatusLabel(v.Status),
                v.HasAttachment ? "📎 Sí" : "—",
                v.Comment ?? "",
                v.Review ?? "");
            _grid.Rows[i].Cells["Status"].Style.ForeColor = StatusColor(v.Status);
            _grid.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
        }
        UpdateViewButton();
    }

    private VacRow? SelectedRow() =>
        _grid.CurrentRow?.Cells["Id"].Value is int id ? _rows.FirstOrDefault(r => r.Id == id) : null;

    private void UpdateViewButton()
    {
        var sel = SelectedRow();
        _btnView.Enabled   = sel?.HasAttachment == true;
        // Se consulta al servicio: si la regla cambia, el botón cambia con ella.
        _btnCancel.Enabled = sel != null && VacationRequestService.PuedeCancelar(sel.Status);
        _btnDelete.Enabled = sel != null && VacationRequestService.PuedeEliminar(sel.Status);
    }

    private void BtnCancel_Click(object? s, EventArgs e)
    {
        if (SelectedRow() is not { } v) { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var aviso = v.Status == VacationStatus.Aprobada
            ? "Esta solicitud YA FUE APROBADA. Al cancelarla dejarás de tomar esos días.\n\n"
            : "";
        if (MessageBox.Show(
                $"{aviso}¿Cancelar la solicitud del {v.Start:dd/MM/yyyy} al {v.End:dd/MM/yyyy}?",
                "Cancelar solicitud", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Ejecutar(() => _vacations.Cancelar(v.Id));
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        if (SelectedRow() is not { } v) { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // Los documentos generados cuelgan de la solicitud y se borran en cascada: hay que decirlo.
        int docs = _vacations.DocumentosAsociados(v.Id);
        var aviso = docs > 0
            ? $"\n\nSe eliminarán también {docs} documento(s) generado(s) de esta solicitud."
            : "";

        if (MessageBox.Show(
                $"¿Eliminar definitivamente la solicitud del {v.Start:dd/MM/yyyy} al {v.End:dd/MM/yyyy}?{aviso}\n\nEsta acción no se puede deshacer.",
                "Eliminar solicitud", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Ejecutar(() => _vacations.Eliminar(v.Id));
    }

    /// <summary>Ejecuta la operación traduciendo el resultado (y la falta de permiso) a un aviso.</summary>
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
        catch (DbUpdateException ex)
        {
            MessageBox.Show($"La base de datos rechazó la operación:\n{ex.InnerException?.Message ?? ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        var devId = _currentUser.User?.DeveloperId;
        if (devId == null) { MessageBox.Show("No se encontró el desarrollador vinculado a tu usuario.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        using var frm = new MyVacationRequestForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        var vr = frm.Result;
        vr.DeveloperId = devId.Value;
        vr.Status = VacationStatus.Pendiente;
        vr.CreatedAt = DateTime.UtcNow;
        _db.VacationRequests.Add(vr);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "VacationRequest", vr.Id.ToString(),
            $"Solicitud de vacaciones: {vr.StartDate:dd/MM/yyyy} — {vr.EndDate:dd/MM/yyyy}" + (vr.AttachmentFileName != null ? " (con adjunto)" : ""));
        LoadData();
    }

    private void BtnViewAttachment_Click(object? s, EventArgs e)
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        VacationAttachment.Open(_db, id, this);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static string StatusLabel(VacationStatus s) => s switch
    {
        VacationStatus.Pendiente => "⏳ Pendiente",
        VacationStatus.Aprobada  => "✅ Aprobada",
        VacationStatus.Rechazada => "❌ Rechazada",
        VacationStatus.Cancelada => "🚫 Cancelada",
        _                        => s.ToString()
    };

    private static Color StatusColor(VacationStatus s) => s switch
    {
        VacationStatus.Aprobada  => AppTheme.Success,
        VacationStatus.Rechazada => AppTheme.Danger,
        VacationStatus.Cancelada => AppTheme.Warning,
        _                        => AppTheme.TextSecondary
    };
}
