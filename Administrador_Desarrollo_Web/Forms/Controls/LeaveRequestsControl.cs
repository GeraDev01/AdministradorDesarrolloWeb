using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Permisos del equipo, desde el lado del administrador: su trabajo aquí es <b>resolver</b> —
/// aprobar o rechazar lo que el equipo solicita— y de paso consultar el histórico.
///
/// Sigue existiendo «➕ Registrar» para el permiso que se acordó fuera de la aplicación (una
/// llamada, un pasillo): esa fila nace ya aprobada porque registrarla ES concederla. Quitarlo
/// habría dejado sin lugar a la mitad de los permisos reales.
/// </summary>
public class LeaveRequestsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly LeaveRequestService _leaves;
    private readonly ICurrentUser _currentUser;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbxDev = null!;
    private ComboBox _cbxType = null!;
    private ComboBox _cbxStatus = null!;
    private Label _lblResumen = null!;
    private List<LeaveRequest> _allLeaves = [];
    private List<Developer> _allDevs = [];

    public LeaveRequestsControl(AppDbContext db, LeaveRequestService leaves, ICurrentUser currentUser)
    {
        _db = db; _leaves = leaves; _currentUser = currentUser;
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
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 610f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };

        _txtSearch = new TextBox { Width = 150, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 6, 0) };
        _txtSearch.TextChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_txtSearch);

        _cbxStatus = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxStatus.Items.Add("⏳ Solo pendientes");
        _cbxStatus.Items.Add("Todos los estados");
        foreach (var s in Enum.GetValues<LeaveStatus>()) _cbxStatus.Items.Add(LeaveRequestService.Etiqueta(s));
        // Arranca en pendientes: es lo que el administrador viene a hacer aquí.
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxStatus);

        _cbxDev = new ComboBox { Width = 165, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 6, 0) };
        _cbxDev.Items.Add("Todos");
        _cbxDev.SelectedIndex = 0;
        _cbxDev.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxDev);

        _cbxType = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0) };
        _cbxType.Items.Add("Todos los tipos");
        foreach (var t in Enum.GetValues<LeaveType>()) _cbxType.Items.Add(LeaveRequestService.EtiquetaTipo(t));
        _cbxType.SelectedIndex = 0;
        _cbxType.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(_cbxType);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };

        var btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 110);
        btnDelete.Click += BtnDelete_Click;
        var btnNew = AppTheme.MakeSecondaryButton("➕ Registrar", 120);
        btnNew.Click += BtnNew_Click;
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Editar", 95);
        btnEdit.Click += BtnEdit_Click;
        var btnFile = AppTheme.MakeSecondaryButton("📎 Justificante", 140);
        btnFile.Click += BtnFile_Click;
        var btnReject = AppTheme.MakeDangerButton("❌ Rechazar", 125);
        btnReject.Click += BtnReject_Click;
        var btnApprove = AppTheme.MakePrimaryButton("✅ Aprobar", 120);
        btnApprove.Click += BtnApprove_Click;

        foreach (var b in new[] { btnDelete, btnNew, btnEdit, btnFile, btnReject, btnApprove })
            b.Margin = new Padding(4, 2, 0, 0);

        btnFlow.Controls.AddRange([btnDelete, btnNew, btnEdit, btnFile, btnReject, btnApprove]);
        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow,    1, 0);

        // ── Grid ───────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = true;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",            Name = "Id",       Width = 40, FillWeight = 3  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",         Name = "Status",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador",  Name = "Dev",      FillWeight = 17 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",           Name = "Type",     FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desde",          Name = "Date",     FillWeight = 9  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",           Name = "Days",     FillWeight = 5  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motivo",         Name = "Reason",   FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📎",             Name = "File",     FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Origen",         Name = "Origin",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Resolvió",       Name = "Approved", FillWeight = 12 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        _lblResumen = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0)
        };

        tbl.Controls.Add(toolbar,    0, 0);
        tbl.Controls.Add(pnlGrid,    0, 1);
        tbl.Controls.Add(_lblResumen, 0, 2);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        try { _allLeaves = _leaves.Todas(); }
        catch (AuthorizationException) { _allLeaves = []; }

        _allDevs = [.. _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName)];

        var prev = _cbxDev.SelectedIndex;
        _cbxDev.Items.Clear();
        _cbxDev.Items.Add("Todos");
        foreach (var d in _allDevs) _cbxDev.Items.Add(d.FullName);
        _cbxDev.SelectedIndex = prev >= 0 && prev < _cbxDev.Items.Count ? prev : 0;

        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allLeaves.AsEnumerable();

        // Índice 0 = solo pendientes, 1 = todos, 2+ = un estado concreto.
        if (_cbxStatus.SelectedIndex == 0) q = q.Where(l => l.Status == LeaveStatus.Pendiente);
        else if (_cbxStatus.SelectedIndex > 1) q = q.Where(l => (int)l.Status == _cbxStatus.SelectedIndex - 2);

        var search = _txtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(l =>
                (l.Developer?.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.Reason?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.ApprovedBy?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (_cbxDev.SelectedIndex > 0)
        {
            var devName = _allDevs[_cbxDev.SelectedIndex - 1].FullName;
            q = q.Where(l => l.Developer?.FullName == devName);
        }

        if (_cbxType.SelectedIndex > 0)
            q = q.Where(l => (int)l.Type == _cbxType.SelectedIndex - 1);

        var filas = q.ToList();
        foreach (var l in filas)
        {
            int i = _grid.Rows.Add(
                l.Id,
                LeaveRequestService.Etiqueta(l.Status),
                l.Developer?.FullName ?? "—",
                LeaveRequestService.EtiquetaTipo(l.Type),
                l.Date.ToString("dd/MM/yyyy"),
                l.DaysCount,
                l.Reason ?? "—",
                l.AttachmentBytes is { Length: > 0 } ? "📎" : "",
                l.EsSolicitudDelDesarrollador ? "Solicitud" : "Registro",
                l.ApprovedBy ?? "—");

            var fila = _grid.Rows[i];
            fila.Cells["Status"].Style.ForeColor = LeaveRequestDetailForm.EstadoColor(l.Status);
            fila.Cells["Status"].Style.Font = AppTheme.BoldFont;

            // El motivo y las notas se truncan en la celda: el tooltip es lo que permite decidir
            // sin abrir el detalle de cada solicitud.
            var detalle = l.Reason ?? "(sin motivo)";
            if (!string.IsNullOrWhiteSpace(l.Notes)) detalle += $"\n\nNotas:\n{l.Notes}";
            if (!string.IsNullOrWhiteSpace(l.ReviewComment)) detalle += $"\n\nResolución:\n{l.ReviewComment}";
            fila.Cells["Reason"].ToolTipText = detalle;

            fila.Cells["Date"].ToolTipText = l.DaysCount > 1
                ? $"Del {l.Date:dd/MM/yyyy} al {l.EndDate:dd/MM/yyyy}"
                : $"Solo el {l.Date:dd/MM/yyyy}";

            if (l.AttachmentBytes is { Length: > 0 })
                fila.Cells["File"].ToolTipText = $"{l.AttachmentFileName}  ({l.AttachmentBytes.Length / 1024} KB)";
        }

        int pendientes = _allLeaves.Count(l => l.Status == LeaveStatus.Pendiente);
        _lblResumen.Text = pendientes == 0
            ? $"{filas.Count} permiso(s) en pantalla.  No hay solicitudes esperando respuesta."
            : $"{filas.Count} permiso(s) en pantalla.  ⏳ {pendientes} solicitud(es) esperando tu respuesta.";
        _lblResumen.ForeColor = pendientes > 0 ? AppTheme.Warning : AppTheme.TextSecondary;
    }

    private LeaveRequest? SelectedLeave()
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _allLeaves.FirstOrDefault(l => l.Id == id);
    }

    private List<LeaveRequest> SelectedLeaves() =>
        _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Cells["Id"].Value).OfType<int>()
            .Select(id => _allLeaves.FirstOrDefault(l => l.Id == id))
            .OfType<LeaveRequest>().ToList();

    // ── Resolver ───────────────────────────────────────────────────

    private void BtnApprove_Click(object? s, EventArgs e)
    {
        var sel = SelectedLeaves().Where(l => l.Status == LeaveStatus.Pendiente).ToList();
        if (sel.Count == 0)
        { MessageBox.Show("Selecciona al menos una solicitud pendiente.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        string comentario = Prompt("Comentario para el solicitante (opcional):", "Aprobar permiso");

        int ok = 0; var errores = new List<string>();
        foreach (var l in sel)
        {
            var (bien, mensaje) = _leaves.Aprobar(l.Id, comentario);
            if (bien) ok++; else errores.Add($"#{l.Id}: {mensaje}");
        }

        LoadData();
        MostrarResultado(ok, errores, "aprobada(s)");
    }

    private void BtnReject_Click(object? s, EventArgs e)
    {
        var sel = SelectedLeaves().Where(l => l.Status == LeaveStatus.Pendiente).ToList();
        if (sel.Count == 0)
        { MessageBox.Show("Selecciona al menos una solicitud pendiente.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        string motivo = Prompt("Motivo del rechazo (obligatorio):", "Rechazar permiso");
        if (string.IsNullOrWhiteSpace(motivo))
        {
            MessageBox.Show("Sin motivo no se rechaza: es lo único que el solicitante va a leer.",
                "Falta el motivo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int ok = 0; var errores = new List<string>();
        foreach (var l in sel)
        {
            var (bien, mensaje) = _leaves.Rechazar(l.Id, motivo);
            if (bien) ok++; else errores.Add($"#{l.Id}: {mensaje}");
        }

        LoadData();
        MostrarResultado(ok, errores, "rechazada(s)");
    }

    private static void MostrarResultado(int ok, List<string> errores, string verbo)
    {
        if (errores.Count == 0)
        {
            MessageBox.Show($"{ok} solicitud(es) {verbo}.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MessageBox.Show($"{ok} solicitud(es) {verbo}.\n\nNo se pudo con:\n" + string.Join("\n", errores),
            "Con incidencias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ── Alta / edición / borrado ───────────────────────────────────

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new LeaveRequestDetailForm(_db, esAdmin: true);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        var (ok, mensaje, _) = _leaves.RegistrarPorAdministrador(frm.Result);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var leave = SelectedLeave();
        if (leave == null) return;

        if (leave.Status != LeaveStatus.Pendiente)
        {
            // Una resuelta es historial: cambiarla después borraría el rastro de qué se aprobó.
            MessageBox.Show(
                $"Esa solicitud está «{LeaveRequestService.Etiqueta(leave.Status)}» y ya no se edita.\n\n" +
                (string.IsNullOrWhiteSpace(leave.ReviewComment) ? "" : $"Resolución:\n{leave.ReviewComment}\n\n") +
                (string.IsNullOrWhiteSpace(leave.Notes) ? "" : $"Notas:\n{leave.Notes}"),
                "Solo consulta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new LeaveRequestDetailForm(_db, esAdmin: true, developerFijo: leave.DeveloperId, leave: leave);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        var (ok, mensaje) = _leaves.Editar(leave.Id, frm.Result);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LoadData();
    }

    private void BtnFile_Click(object? s, EventArgs e)
    {
        var leave = SelectedLeave();
        if (leave == null) { MessageBox.Show("Selecciona un permiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var (bytes, nombre) = _leaves.Adjunto(leave.Id);
        if (bytes.Length == 0)
        { MessageBox.Show("Ese permiso no trae justificante.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_permiso_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, nombre);
            File.WriteAllBytes(path, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el justificante:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var leave = SelectedLeave();
        if (leave == null) return;

        if (MessageBox.Show(
                $"¿Eliminar el permiso de {leave.Developer?.FullName} del {leave.Date:dd/MM/yyyy}?\n\n" +
                "Desaparece del historial y no se puede deshacer.",
                "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var (ok, mensaje) = _leaves.Eliminar(leave.Id);
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LoadData();
    }

    /// <summary>Cuadro de texto multilínea para pedir un comentario o un motivo.</summary>
    private static string Prompt(string texto, string titulo)
    {
        using var f = new Form
        {
            Text = titulo, Size = new Size(460, 210), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = AppTheme.ContentBg, Font = AppTheme.DefaultFont
        };
        f.Controls.Add(new Label { Text = texto, Location = new Point(16, 14), AutoSize = true });
        var txt = new TextBox { Location = new Point(16, 40), Width = 410, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
        var ok = AppTheme.MakePrimaryButton("Aceptar", 100); ok.Location = new Point(216, 122); ok.DialogResult = DialogResult.OK;
        var cancel = AppTheme.MakeSecondaryButton("Cancelar", 100); cancel.Location = new Point(326, 122); cancel.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange([txt, ok, cancel]);
        f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
