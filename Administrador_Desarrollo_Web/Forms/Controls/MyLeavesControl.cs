using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Los permisos del propio desarrollador: los pide desde aquí y aquí ve en qué quedaron.
///
/// Antes esta pantalla no existía: el permiso se acordaba por fuera y el único registro era el
/// apunte del administrador, que el interesado no podía consultar. Con esto la solicitud y su
/// respuesta —incluido el motivo de un rechazo— quedan del lado de quien las necesita.
/// </summary>
public class MyLeavesControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly LeaveRequestService _leaves;
    private readonly CurrentUserContext _currentUser;

    private DataGridView _grid = null!;
    private Label _kpiPendientes = null!, _kpiAprobados = null!, _kpiDias = null!;
    private Label _lblEstado = null!;
    private List<LeaveRequest> _mios = [];
    private readonly ToolTip _tip = new() { AutoPopDelay = 20000 };

    public MyLeavesControl(AppDbContext db, LeaveRequestService leaves, CurrentUserContext currentUser)
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
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = AppTheme.ContentBg, Padding = new Padding(10, 8, 10, 5), Margin = Padding.Empty
        };

        var btnNew = AppTheme.MakePrimaryButton("➕ Solicitar permiso", 180);
        btnNew.Click += BtnNew_Click;
        var btnEdit = AppTheme.MakeSecondaryButton("✏ Corregir", 115);
        btnEdit.Click += BtnEdit_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("🚫 Cancelar", 120);
        btnCancel.Click += BtnCancel_Click;
        var btnFile = AppTheme.MakeSecondaryButton("📎 Justificante", 140);
        btnFile.Click += BtnFile_Click;
        var btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 110);
        btnDelete.Click += BtnDelete_Click;
        var btnReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 115);
        btnReload.Click += (_, _) => LoadData();

        foreach (var b in new[] { btnNew, btnEdit, btnCancel, btnFile, btnDelete, btnReload })
            b.Margin = new Padding(0, 2, 8, 0);

        _tip.SetToolTip(btnEdit, "Solo mientras siga pendiente. Una vez resuelta, ya no se puede cambiar.");
        _tip.SetToolTip(btnCancel, "Retira una solicitud pendiente, o avisa de que ya no tomarás una aprobada.");
        _tip.SetToolTip(btnDelete, "Solo lo que nunca llegó a resolverse. Lo aprobado o rechazado es historial: cancélalo.");

        toolbar.Controls.AddRange([btnNew, btnEdit, btnCancel, btnFile, btnDelete, btnReload]);

        // ── KPIs ───────────────────────────────────────────────────
        var kpiFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoScroll = true, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg
        };
        kpiFlow.Controls.Add(MakeKpi("⏳ Esperando respuesta", out _kpiPendientes, AppTheme.Warning));
        kpiFlow.Controls.Add(MakeKpi("✅ Aprobados (este año)", out _kpiAprobados, AppTheme.Success));
        kpiFlow.Controls.Add(MakeKpi("📅 Días aprobados (año)", out _kpiDias, AppTheme.SidebarActive));

        // ── Grid ───────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",      Name = "Id",     Width = 40, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",  Name = "Status", FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",    Name = "Type",   FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desde",   Name = "Date",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",    Name = "Days",   FillWeight = 6  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Motivo",  Name = "Reason", FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📎",      Name = "File",   FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Respuesta del líder", Name = "Review", FillWeight = 27 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        _lblEstado = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0)
        };

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(kpiFlow,   0, 1);
        tbl.Controls.Add(pnlGrid,   0, 2);
        tbl.Controls.Add(_lblEstado, 0, 3);
        Controls.Add(tbl);
    }

    private static Panel MakeKpi(string title, out Label value, Color accent)
    {
        var card = new Panel { Width = 220, Height = 84, Margin = new Padding(0, 4, 10, 4), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
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
        _grid.Rows.Clear();
        _mios = [];

        if (_currentUser.DeveloperId is not int devId)
        {
            _kpiPendientes.Text = _kpiAprobados.Text = _kpiDias.Text = "—";
            _lblEstado.Text = "Tu cuenta no está vinculada a un desarrollador. Contacta al líder.";
            _lblEstado.ForeColor = AppTheme.Warning;
            return;
        }

        try { _mios = _leaves.DeDesarrollador(devId); }
        catch (AuthorizationException ex)
        {
            _lblEstado.Text = ex.Message;
            _lblEstado.ForeColor = AppTheme.Danger;
            return;
        }

        foreach (var l in _mios)
        {
            int i = _grid.Rows.Add(
                l.Id,
                LeaveRequestService.Etiqueta(l.Status),
                LeaveRequestService.EtiquetaTipo(l.Type),
                l.Date.ToString("dd/MM/yyyy"),
                l.DaysCount,
                l.Reason ?? "—",
                l.AttachmentBytes is { Length: > 0 } ? "📎" : "",
                // El motivo del rechazo es lo que de verdad viene a leer aquí: si falta, se dice.
                string.IsNullOrWhiteSpace(l.ReviewComment)
                    ? (l.Status == LeaveStatus.Rechazada ? "(sin motivo registrado)" : "")
                    : l.ReviewComment);

            var fila = _grid.Rows[i];
            fila.Cells["Status"].Style.ForeColor = LeaveRequestDetailForm.EstadoColor(l.Status);
            fila.Cells["Status"].Style.Font = AppTheme.BoldFont;
            if (l.Status == LeaveStatus.Rechazada) fila.Cells["Review"].Style.ForeColor = AppTheme.Danger;

            fila.Cells["Date"].ToolTipText = l.DaysCount > 1
                ? $"Del {l.Date:dd/MM/yyyy} al {l.EndDate:dd/MM/yyyy}"
                : $"Solo el {l.Date:dd/MM/yyyy}";

            var detalle = l.Reason ?? "(sin motivo)";
            if (!string.IsNullOrWhiteSpace(l.Notes)) detalle += $"\n\nNotas:\n{l.Notes}";
            fila.Cells["Reason"].ToolTipText = detalle;

            if (!l.EsSolicitudDelDesarrollador)
                fila.Cells["Id"].ToolTipText = "Este permiso lo registró el líder; no se edita desde aquí.";
        }

        int año = DateTime.Today.Year;
        var aprobados = _mios.Where(l => l.Status == LeaveStatus.Aprobada && l.Date.Year == año).ToList();
        _kpiPendientes.Text = _mios.Count(l => l.Status == LeaveStatus.Pendiente).ToString();
        _kpiAprobados.Text = aprobados.Count.ToString();
        _kpiDias.Text = aprobados.Sum(l => l.DaysCount).ToString();

        _lblEstado.Text = _mios.Count == 0
            ? "Todavía no has solicitado ningún permiso.  Pulsa «➕ Solicitar permiso» para pedir el primero."
            : $"{_mios.Count} permiso(s) en tu historial.  Doble clic abre el detalle.";
        _lblEstado.ForeColor = AppTheme.TextSecondary;
    }

    private LeaveRequest? Seleccionado()
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _mios.FirstOrDefault(l => l.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        if (_currentUser.DeveloperId is not int devId)
        { MessageBox.Show("Tu cuenta no está vinculada a un desarrollador.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        using var frm = new LeaveRequestDetailForm(_db, esAdmin: false, developerFijo: devId);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (ok, mensaje, _) = _leaves.Solicitar(frm.Result);
            MessageBox.Show(mensaje, ok ? "Enviada" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            LoadData();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var l = Seleccionado();
        if (l == null) { MessageBox.Show("Selecciona un permiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (!LeaveRequestService.PuedeEditar(l.Status))
        {
            // Ya resuelta: en vez de un «no se puede» seco, se enseña lo que contestaron.
            MessageBox.Show(
                $"Estado: {LeaveRequestService.Etiqueta(l.Status)}\n\n" +
                $"{LeaveRequestService.EtiquetaTipo(l.Type)} — {l.Date:dd/MM/yyyy} ({l.DaysCount} día(s))\n\n" +
                $"Tu motivo:\n{l.Reason ?? "(sin motivo)"}\n\n" +
                (string.IsNullOrWhiteSpace(l.ReviewComment)
                    ? "El líder no dejó comentario."
                    : $"Respuesta del líder:\n{l.ReviewComment}"),
                "Detalle del permiso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new LeaveRequestDetailForm(_db, esAdmin: false, developerFijo: l.DeveloperId, leave: l);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (ok, mensaje) = _leaves.Editar(l.Id, frm.Result);
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void BtnCancel_Click(object? s, EventArgs e)
    {
        var l = Seleccionado();
        if (l == null) { MessageBox.Show("Selecciona un permiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (!LeaveRequestService.PuedeCancelar(l.Status))
        { MessageBox.Show($"Una solicitud «{LeaveRequestService.Etiqueta(l.Status)}» ya no se puede cancelar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (MessageBox.Show(
                $"¿Cancelar tu permiso del {l.Date:dd/MM/yyyy} ({l.DaysCount} día(s))?\n\n" +
                (l.Status == LeaveStatus.Aprobada
                    ? "Ya estaba aprobado: cancelarlo avisa de que no lo vas a tomar."
                    : "Se retira la solicitud antes de que la resuelvan."),
                "Cancelar permiso", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, mensaje) = _leaves.Cancelar(l.Id);
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void BtnFile_Click(object? s, EventArgs e)
    {
        var l = Seleccionado();
        if (l == null) { MessageBox.Show("Selecciona un permiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        try
        {
            var (bytes, nombre) = _leaves.Adjunto(l.Id);
            if (bytes.Length == 0)
            { MessageBox.Show("Ese permiso no trae justificante.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            var dir = Path.Combine(Path.GetTempPath(), "advweb_permiso_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, nombre);
            File.WriteAllBytes(path, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir el justificante:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var l = Seleccionado();
        if (l == null) { MessageBox.Show("Selecciona un permiso.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (!LeaveRequestService.PuedeEliminarElDesarrollador(l.Status))
        {
            MessageBox.Show(
                $"Una solicitud «{LeaveRequestService.Etiqueta(l.Status)}» es parte del historial y no se borra.\n\n" +
                "Si ya no la vas a tomar, cancélala.",
                "No se puede eliminar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show($"¿Eliminar tu solicitud del {l.Date:dd/MM/yyyy}?", "Confirmar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, mensaje) = _leaves.Eliminar(l.Id);
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
        catch (AuthorizationException ex) { MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
