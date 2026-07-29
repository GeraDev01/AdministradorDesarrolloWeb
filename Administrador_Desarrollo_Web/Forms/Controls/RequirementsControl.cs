using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class RequirementsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ReportService _report;
    private readonly AzureDevOpsService _devOps;
    private readonly RequirementAttachmentService _attachments;
    private readonly NotificationService _notifications;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbxStatus = null!;
    private ComboBox _cbxDev = null!;
    private Button _btnDevOpsImport = null!;
    private Button _btnDevOpsOpen  = null!;
    private Button _btnDevOpsRules = null!;
    private List<Requirement> _allReqs = [];
    private List<Developer> _allDevs = [];
    private Dictionary<int, int> _attCounts = [];

    public RequirementsControl(AppDbContext db, AuditService audit, ReportService report, AzureDevOpsService devOps,
        RequirementAttachmentService attachments, NotificationService notifications)
    {
        _db = db; _audit = audit; _report = report; _devOps = devOps; _attachments = attachments;
        _notifications = notifications;
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
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar: filters left | buttons right ────────────────
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1050f)); // 9 buttons
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };
        _txtSearch = new TextBox { Width = 175, Height = 28, PlaceholderText = "Buscar...", Margin = new Padding(0, 2, 8, 0) };
        _txtSearch.TextChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = AppTheme.DefaultFont });
        _cbxStatus = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 8, 0), Height = 28 };
        _cbxStatus.Items.Add("Todos");
        foreach (var s in Enum.GetValues<RequirementStatus>()) _cbxStatus.Items.Add(StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        _cbxStatus.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.Add(new Label { Text = "Dev:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = AppTheme.DefaultFont });
        _cbxDev = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 0), Height = 28 };
        _cbxDev.SelectedIndexChanged += (_, _) => FilterGrid();
        filterFlow.Controls.AddRange([_txtSearch, _cbxStatus, _cbxDev]);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg, Margin = Padding.Empty
        };
        var btnNew    = AppTheme.MakePrimaryButton("➕ Nuevo", 108);
        var btnEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 108);
        var btnDel    = AppTheme.MakeDangerButton("🗑 Eliminar", 108);
        var btnAssign = AppTheme.MakeSecondaryButton("👥 Asignar", 108);
        var btnAttach = AppTheme.MakeSecondaryButton("📎 Adjuntos", 115);
        var btnExp    = AppTheme.MakeSecondaryButton("📊 Excel", 95);
        _btnDevOpsImport = AppTheme.MakeSecondaryButton("🔄 Importar DevOps", 158);
        _btnDevOpsOpen   = AppTheme.MakeSecondaryButton("🔗 Abrir en DevOps", 155);
        _btnDevOpsRules  = AppTheme.MakeSecondaryButton("⚙ Reglas", 100);
        foreach (var b in new[] { btnNew, btnEdit, btnDel, btnAssign, btnAttach, btnExp, _btnDevOpsImport, _btnDevOpsOpen, _btnDevOpsRules })
            b.Margin = new Padding(4, 2, 0, 0);
        btnNew.Click    += BtnNew_Click;
        btnEdit.Click   += BtnEdit_Click;
        btnDel.Click    += BtnDelete_Click;
        btnAssign.Click += BtnAssign_Click;
        btnAttach.Click += BtnAttach_Click;
        btnExp.Click    += BtnExport_Click;
        _btnDevOpsImport.Click += BtnDevOpsImport_Click;
        _btnDevOpsOpen.Click   += BtnDevOpsOpen_Click;
        _btnDevOpsRules.Click  += (_, _) => { using var f = new DevOpsRulesForm(_db); f.ShowDialog(this); };
        btnFlow.Controls.AddRange([btnNew, btnEdit, btnDel, btnAssign, btnAttach, btnExp, _btnDevOpsImport, _btnDevOpsOpen, _btnDevOpsRules]);
        UpdateDevOpsButtons();

        toolbar.Controls.Add(filterFlow, 0, 0);
        toolbar.Controls.Add(btnFlow,    1, 0);

        // ── Grid ────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",             Name = "Id",       Width = 45, FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",          Name = "Title",    FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",          Name = "Status",   FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Prioridad",       Name = "Priority", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrolladores", Name = "Devs",     FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hrs",             Name = "Hours",    FillWeight = 6  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "F. Compromiso",   Name = "Date",     FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Avance",          Name = "Progress", FillWeight = 7  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Origen",          Name = "Source",   FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📎",              Name = "Att",      FillWeight = 6  });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(pnlGrid,   0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allReqs = _db.Requirements.Include(r => r.Assignments).ThenInclude(a => a.Developer)
            .OrderByDescending(r => r.CreatedAt).ToList();
        _allDevs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        _attCounts = _attachments.CountsByRequirement();

        var sel = _cbxDev.SelectedIndex;
        _cbxDev.Items.Clear();
        _cbxDev.Items.Add("Todos los devs");
        foreach (var d in _allDevs) _cbxDev.Items.Add(d.FullName);
        _cbxDev.SelectedIndex = (sel >= 0 && sel < _cbxDev.Items.Count) ? sel : 0;
        FilterGrid();
    }

    private void FilterGrid()
    {
        _grid.Rows.Clear();
        var q = _allReqs.AsEnumerable();
        if (_cbxStatus.SelectedIndex > 0) q = q.Where(r => (int)r.Status == _cbxStatus.SelectedIndex - 1);
        if (_cbxDev.SelectedIndex > 0)
        {
            var devName = _cbxDev.SelectedItem?.ToString() ?? "";
            q = q.Where(r => r.Assignments.Any(a => a.Developer.FullName == devName));
        }
        var s = _txtSearch.Text.Trim().ToLower();
        if (!string.IsNullOrEmpty(s)) q = q.Where(r => r.Title.ToLower().Contains(s) || (r.Description?.ToLower().Contains(s) ?? false));

        foreach (var r in q)
        {
            var devNames = string.Join(", ", r.Assignments.Select(a => a.Developer.FullName));
            bool overdue = r.CommittedDeliveryDate < DateTime.Today && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado;
            var sourceLabel = r.Source switch { RequirementSource.AzureDevOps => "DevOps", RequirementSource.Email => "Correo", _ => "Manual" };
            var attCount = _attCounts.TryGetValue(r.Id, out var c) ? c : 0;
            int i = _grid.Rows.Add(r.Id, r.Title, StatusLabel(r.Status), PriorityLabel(r.Priority),
                devNames, r.EstimateHours?.ToString("0.#") ?? "—",
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", $"{r.ProgressPercent}%",
                sourceLabel, attCount > 0 ? $"📎 {attCount}" : "");
            _grid.Rows[i].Cells["Status"].Style.ForeColor = AppTheme.StatusColor(r.Status);
            _grid.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
            if (overdue) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger;
            if (r.Source == RequirementSource.AzureDevOps)
                _grid.Rows[i].Cells["Source"].Style.ForeColor = AppTheme.SidebarActive;
        }
    }

    private Requirement? SelectedReq()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allReqs.FirstOrDefault(r => r.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new RequirementDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var req = frm.Result; req.CreatedAt = DateTime.UtcNow; req.StatusChangedAt = req.CreatedAt;
        _db.Requirements.Add(req); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Requirement", req.Id.ToString(), req.Title);
        LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var req = SelectedReq();
        if (req == null) { MessageBox.Show("Selecciona un requerimiento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var oldStatus = req.Status;
        using var frm = new RequirementDetailForm(req);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (req.Status != oldStatus) req.StatusChangedAt = DateTime.UtcNow;
            _db.SaveChanges();
            _audit.Record(AuditAction.Update, "Requirement", req.Id.ToString(), req.Title);
            LoadData();
        }
        catch (DbUpdateConcurrencyException)
        {
            MessageBox.Show("Otro usuario modificó este registro. Recarga e intenta de nuevo.", "Conflicto de concurrencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var req = SelectedReq();
        if (req == null) { MessageBox.Show("Selecciona un requerimiento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Cancelar '{req.Title}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            req.Status = RequirementStatus.Cancelado;
            _db.SaveChanges();
            _audit.Record(AuditAction.Delete, "Requirement", req.Id.ToString(), $"Cancelado: {req.Title}");
            LoadData();
        }
        catch (DbUpdateConcurrencyException)
        {
            MessageBox.Show("Otro usuario modificó este registro. Recarga e intenta de nuevo.", "Conflicto de concurrencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
    }

    private void BtnAssign_Click(object? s, EventArgs e)
    {
        var req = SelectedReq();
        if (req == null) { MessageBox.Show("Selecciona un requerimiento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var currentIds = req.Assignments.Select(a => a.DeveloperId).ToList();
        using var frm = new AssignDevelopersForm(_allDevs, currentIds);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var old = _db.Assignments.Where(a => a.RequirementId == req.Id).ToList();
        _db.Assignments.RemoveRange(old);
        foreach (var devId in frm.SelectedDevIds)
            _db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = devId, AssignedAt = DateTime.UtcNow });
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Assignment", req.Id.ToString(), $"{frm.SelectedDevIds.Count} devs asignados a '{req.Title}'");

        // Avisar solo a quienes NO estaban asignados antes (evita re-notificar en cada guardado).
        foreach (var devId in frm.SelectedDevIds.Except(currentIds))
            _notifications.NotifyDeveloper(devId, NotificationKind.RequirementAssigned,
                "Nuevo requerimiento asignado", $"#{req.Id} — {req.Title}", req.ExternalUrl);

        LoadData();
    }

    private void BtnAttach_Click(object? s, EventArgs e)
    {
        var req = SelectedReq();
        if (req == null) { MessageBox.Show("Selecciona un requerimiento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new RequirementAttachmentsForm(_attachments, req.Id, req.Title);
        frm.ShowDialog(this);
        _attCounts = _attachments.CountsByRequirement();
        FilterGrid();
    }

    private void UpdateDevOpsButtons()
    {
        _btnDevOpsImport.Visible = _devOps.IsEnabled;
        _btnDevOpsOpen.Visible   = _devOps.IsEnabled;
        _btnDevOpsRules.Visible  = _devOps.IsEnabled;
    }

    private async void BtnDevOpsImport_Click(object? s, EventArgs e)
    {
        _btnDevOpsImport.Enabled = false;
        var textoPrevio = _btnDevOpsImport.Text;
        _btnDevOpsImport.Text = "Consultando…";
        try
        {
            // Solo los ASIGNADOS y NO cerrados (Done/Removed): son los candidatos a dar seguimiento.
            var items = await Task.Run(() => _devOps.QueryAssignedOpenAsync());
            if (items.Count == 0)
            {
                MessageBox.Show("No hay work items asignados y abiertos para importar.\n" +
                                "(Se excluyen los que están en Done o Removed.)",
                    "Azure DevOps", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var devs = _db.Developers.Where(d => d.IsActive).ToList();

            // Diálogo filtrable: el usuario elige el subconjunto; la asignación al desarrollador es
            // automática por identidad (correo/nombre), no se elige a mano.
            using var dlg = new DevOpsImportForm(items, devs);
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

            var result = _devOps.ImportAsRequirements(dlg.SelectedItems);
            LoadData();

            int asignados = result.NewItems.Count(x => x.AssignedTo != null);
            string msg = $"Importados: {result.Added} nuevos  ·  {result.Updated} actualizados\n" +
                         $"Auto-asignados a su desarrollador: {asignados}";
            if (result.NewItems.Count > 0)
            {
                var lines = result.NewItems.Take(15).Select(x => $"  • {x.Title}{(x.AssignedTo != null ? $"  → {x.AssignedTo}" : "  (sin asignar)")}");
                msg += "\n\n" + string.Join("\n", lines);
                if (result.NewItems.Count > 15) msg += $"\n  … y {result.NewItems.Count - 15} más";
            }
            MessageBox.Show(msg, "Importación de Azure DevOps", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al importar: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _btnDevOpsImport.Enabled = true; _btnDevOpsImport.Text = textoPrevio; }
    }

    private void BtnDevOpsOpen_Click(object? s, EventArgs e)
    {
        var req = SelectedReq();
        if (req == null) { MessageBox.Show("Selecciona un requerimiento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (string.IsNullOrEmpty(req.ExternalUrl)) { MessageBox.Show("Este requerimiento no tiene URL de Azure DevOps.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(req.ExternalUrl) { UseShellExecute = true });
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Requerimientos");
        if (path == null) return;
        _report.ExportToExcel(_allReqs,
            ["ID", "Título", "Estado", "Prioridad", "Desarrolladores", "Hrs Estimadas", "F. Solicitud", "F. Compromiso", "F. Entrega Real", "Avance %"],
            r => [r.Id, r.Title, StatusLabel(r.Status), PriorityLabel(r.Priority),
                  string.Join(", ", r.Assignments.Select(a => a.Developer.FullName)),
                  r.EstimateHours, r.RequestDate, r.CommittedDeliveryDate, r.ActualDeliveryDate, r.ProgressPercent],
            "Requerimientos", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) { UpdateDevOpsButtons(); LoadData(); } }

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
