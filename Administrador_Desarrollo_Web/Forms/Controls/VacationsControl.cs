using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class VacationsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;
    private readonly ReportService _report;
    private readonly VacationDocumentService _docSvc;
    private readonly IDocxToPdfConverter _converter;
    private readonly SignatureService _sig;
    private readonly SettingsService _settings;

    private DataGridView _gridVac = null!;
    private DataGridView _gridNotes = null!;
    private List<VacRow> _vacRows = [];
    private List<Note> _allNotes = [];

    private sealed record VacRow(int Id, string Dev, DateTime Start, DateTime End, VacationStatus Status, bool HasAttachment, string? Comment);

    public VacationsControl(AppDbContext db, AuditService audit, CurrentUserContext currentUser, ReportService report,
        VacationDocumentService docSvc, IDocxToPdfConverter converter, SignatureService sig, SettingsService settings)
    {
        _db = db; _audit = audit; _currentUser = currentUser; _report = report;
        _docSvc = docSvc; _converter = converter; _sig = sig; _settings = settings;
        BuildUI(); LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        // ── TAB 1: Vacaciones ────────────────────────────────────
        var tabVac = new TabPage("  🏖  Solicitudes de Vacaciones  ");
        var vacTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        vacTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        vacTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        vacTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var vacToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = tabVac.BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        vacToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        vacToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 770f));
        vacToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        vacToolbar.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Gestión de solicitudes de vacaciones de los desarrolladores.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }, 0, 0);

        var vacBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnVacNew     = AppTheme.MakePrimaryButton("➕ Nueva", 100); btnVacNew.Margin = new Padding(4, 2, 0, 0); btnVacNew.Click += BtnVacNew_Click;
        var btnVacEdit    = AppTheme.MakeSecondaryButton("✏ Editar", 100); btnVacEdit.Margin = new Padding(4, 2, 0, 0); btnVacEdit.Click += BtnVacEdit_Click;
        var btnVacApprove = AppTheme.MakePrimaryButton("✅ Aprobar", 108); btnVacApprove.Margin = new Padding(4, 2, 0, 0); btnVacApprove.BackColor = AppTheme.Success; btnVacApprove.Click += (_, _) => ChangeVacStatus(VacationStatus.Aprobada);
        var btnVacReject  = AppTheme.MakeDangerButton("❌ Rechazar", 108); btnVacReject.Margin = new Padding(4, 2, 0, 0); btnVacReject.Click += (_, _) => ChangeVacStatus(VacationStatus.Rechazada);
        var btnVacCancel  = AppTheme.MakeSecondaryButton("🚫 Cancelar", 108); btnVacCancel.Margin = new Padding(4, 2, 0, 0); btnVacCancel.Click += (_, _) => ChangeVacStatus(VacationStatus.Cancelada);
        var btnVacDoc     = AppTheme.MakeSecondaryButton("📄 Documento / Firmar", 170); btnVacDoc.Margin = new Padding(4, 2, 0, 0); btnVacDoc.Click += BtnVacDoc_Click;
        var btnVacExp     = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnVacExp.Margin = new Padding(4, 2, 0, 0); btnVacExp.Click += BtnVacExport_Click;
        vacBtns.Controls.AddRange([btnVacNew, btnVacEdit, btnVacApprove, btnVacReject, btnVacCancel, btnVacDoc, btnVacExp]);
        vacToolbar.Controls.Add(vacBtns, 1, 0);

        _gridVac = AppTheme.MakeGrid();
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",           Name = "Id",      Width = 40, FillWeight = 4  });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev",     FillWeight = 22 });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Inicio",        Name = "Start",   FillWeight = 13 });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fin",           Name = "End",     FillWeight = 13 });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",          Name = "Days",    FillWeight = 7  });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",        Name = "Status",  FillWeight = 12 });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Adjunto",       Name = "Attach",  FillWeight = 7  });
        _gridVac.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentario",    Name = "Comment", FillWeight = 22 });
        _gridVac.CellDoubleClick += (_, _) => BtnVacEdit_Click(null, EventArgs.Empty);

        var pnlVacGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty };
        pnlVacGrid.Controls.Add(_gridVac);
        vacTbl.Controls.Add(vacToolbar, 0, 0);
        vacTbl.Controls.Add(pnlVacGrid, 0, 1);
        tabVac.Controls.Add(vacTbl);

        // ── TAB 2: Notas / Pendientes ────────────────────────────
        var tabNotes = new TabPage("  📌  Notas y Pendientes  ");
        var notesTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        notesTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        notesTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        notesTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var notesToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = tabNotes.BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        notesToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        notesToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460f));
        notesToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        notesToolbar.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Pendientes y recordatorios que te comentan los desarrolladores.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }, 0, 0);

        var notesBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnNoteNew    = AppTheme.MakePrimaryButton("➕ Nueva", 108); btnNoteNew.Margin = new Padding(4, 2, 0, 0); btnNoteNew.Click += BtnNoteNew_Click;
        var btnNoteEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 108); btnNoteEdit.Margin = new Padding(4, 2, 0, 0); btnNoteEdit.Click += BtnNoteEdit_Click;
        var btnNoteCheck  = AppTheme.MakePrimaryButton("✅ Completar", 108); btnNoteCheck.Margin = new Padding(4, 2, 0, 0); btnNoteCheck.BackColor = AppTheme.Success; btnNoteCheck.Click += BtnNoteComplete_Click;
        var btnNoteDel    = AppTheme.MakeDangerButton("🗑 Eliminar", 108); btnNoteDel.Margin = new Padding(4, 2, 0, 0); btnNoteDel.Click += BtnNoteDelete_Click;
        var btnNoteExp    = AppTheme.MakeSecondaryButton("📊 Excel", 95); btnNoteExp.Margin = new Padding(4, 2, 0, 0); btnNoteExp.Click += BtnNotesExport_Click;
        notesBtns.Controls.AddRange([btnNoteNew, btnNoteEdit, btnNoteCheck, btnNoteDel, btnNoteExp]);
        notesToolbar.Controls.Add(notesBtns, 1, 0);

        _gridNotes = AppTheme.MakeGrid();
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",          Name = "Id",         Width = 40, FillWeight = 4  });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",       Name = "Title",      FillWeight = 30 });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dev (origen)", Name = "Dev",        FillWeight = 18 });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Prioridad",    Name = "Priority",   FillWeight = 10 });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Recordatorio", Name = "Reminder",   FillWeight = 14 });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "Status",     FillWeight = 12 });
        _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Alta",         Name = "Created",    FillWeight = 12 });
        _gridNotes.CellDoubleClick += (_, _) => BtnNoteEdit_Click(null, EventArgs.Empty);

        var pnlNotesGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), Margin = Padding.Empty };
        pnlNotesGrid.Controls.Add(_gridNotes);
        notesTbl.Controls.Add(notesToolbar, 0, 0);
        notesTbl.Controls.Add(pnlNotesGrid, 0, 1);
        tabNotes.Controls.Add(notesTbl);

        tabs.TabPages.AddRange([tabVac, tabNotes]);
        Controls.Add(tabs);
    }

    private void LoadData()
    {
        // Proyección ligera SIN los BLOB de adjuntos (se cargan bajo demanda al editar/ver).
        _vacRows = _db.VacationRequests.Include(v => v.Developer).OrderByDescending(v => v.CreatedAt)
            .Select(v => new VacRow(v.Id, v.Developer.FullName, v.StartDate, v.EndDate, v.Status, v.AttachmentFileName != null, v.Comment))
            .ToList();
        _allNotes = _db.Notes.Include(n => n.Developer).OrderByDescending(n => n.CreatedAt).ToList();
        FillVacGrid(); FillNotesGrid();
    }

    private void FillVacGrid()
    {
        _gridVac.Rows.Clear();
        foreach (var v in _vacRows)
        {
            int days = (v.End.Date - v.Start.Date).Days + 1;
            int i = _gridVac.Rows.Add(v.Id, v.Dev, v.Start.ToString("dd/MM/yyyy"), v.End.ToString("dd/MM/yyyy"), days, VacStatusLabel(v.Status),
                v.HasAttachment ? "📎 Sí" : "—", v.Comment ?? "");
            _gridVac.Rows[i].Cells["Status"].Style.ForeColor = VacStatusColor(v.Status);
            _gridVac.Rows[i].Cells["Status"].Style.Font = AppTheme.BoldFont;
        }
    }

    private void FillNotesGrid()
    {
        _gridNotes.Rows.Clear();
        foreach (var n in _allNotes)
        {
            bool overdue = !n.IsCompleted && n.ReminderDate.HasValue && n.ReminderDate < DateTime.Today;
            int i = _gridNotes.Rows.Add(n.Id, n.Title, n.Developer?.FullName ?? "—", PriorityLabel(n.Priority),
                n.ReminderDate?.ToString("dd/MM/yyyy") ?? "—", n.IsCompleted ? "✓ Completado" : "Pendiente",
                n.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"));
            if (n.IsCompleted) { _gridNotes.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary; }
            else if (overdue) { _gridNotes.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger; }
        }
    }

    // ── Vacation handlers ────────────────────────────────────────
    private VacationRequest? SelectedVac()
    {
        if (_gridVac.CurrentRow?.Cells["Id"].Value is not int id) return null;
        // Carga la entidad rastreada (con el adjunto) solo de la fila seleccionada.
        return _db.VacationRequests.Include(v => v.Developer).FirstOrDefault(v => v.Id == id);
    }

    private void BtnVacNew_Click(object? s, EventArgs e)
    {
        using var frm = new VacationRequestDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var v = frm.Result; v.CreatedAt = DateTime.UtcNow;
        _db.VacationRequests.Add(v); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "VacationRequest", v.Id.ToString(), $"Solicitud dev {v.DeveloperId}");
        LoadData();
    }

    private void BtnVacEdit_Click(object? s, EventArgs e)
    {
        var v = SelectedVac();
        if (v == null) { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new VacationRequestDetailForm(_db, v);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "VacationRequest", v.Id.ToString(), v.Developer.FullName);
        LoadData();
    }

    private void ChangeVacStatus(VacationStatus status)
    {
        var v = SelectedVac();
        if (v == null) { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string verb = status == VacationStatus.Aprobada ? "aprobar" : status == VacationStatus.Rechazada ? "rechazar" : "cancelar";
        if (MessageBox.Show($"¿Deseas {verb} la solicitud de {v.Developer.FullName}?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        v.Status = status; v.ReviewedById = _currentUser.User?.Id; v.ReviewedAt = DateTime.UtcNow;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "VacationRequest", v.Id.ToString(), $"{status}: {v.Developer.FullName}");
        LoadData();
    }

    private void BtnVacDoc_Click(object? s, EventArgs e)
    {
        var v = SelectedVac();
        if (v == null) { MessageBox.Show("Selecciona una solicitud.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new VacationDocumentForm(_db, _docSvc, _converter, _sig, _settings, _currentUser, _audit, v);
        frm.ShowDialog(this);
        LoadData();
    }

    private void BtnVacExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Vacaciones");
        if (path == null) return;
        _report.ExportToExcel(_vacRows, ["ID", "Desarrollador", "Inicio", "Fin", "Días", "Estado", "Adjunto", "Comentario"],
            v => [v.Id, v.Dev, v.Start, v.End, (v.End.Date - v.Start.Date).Days + 1, VacStatusLabel(v.Status), v.HasAttachment ? "Sí" : "No", v.Comment], "Vacaciones", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Notes handlers ───────────────────────────────────────────
    private Note? SelectedNote()
    {
        if (_gridNotes.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _allNotes.FirstOrDefault(n => n.Id == id);
    }

    private void BtnNoteNew_Click(object? s, EventArgs e)
    {
        using var frm = new NoteDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var n = frm.Result; n.CreatedAt = DateTime.UtcNow;
        _db.Notes.Add(n); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Note", n.Id.ToString(), n.Title);
        LoadData();
    }

    private void BtnNoteEdit_Click(object? s, EventArgs e)
    {
        var n = SelectedNote();
        if (n == null) { MessageBox.Show("Selecciona una nota.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new NoteDetailForm(_db, n);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Note", n.Id.ToString(), n.Title);
        LoadData();
    }

    private void BtnNoteComplete_Click(object? s, EventArgs e)
    {
        var n = SelectedNote();
        if (n == null) { MessageBox.Show("Selecciona una nota.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        n.IsCompleted = true; _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Note", n.Id.ToString(), $"Completada: {n.Title}");
        LoadData();
    }

    private void BtnNoteDelete_Click(object? s, EventArgs e)
    {
        var n = SelectedNote();
        if (n == null) { MessageBox.Show("Selecciona una nota.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar '{n.Title}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _db.Notes.Remove(n); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "Note", n.Id.ToString(), n.Title);
        LoadData();
    }

    private void BtnNotesExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Notas");
        if (path == null) return;
        _report.ExportToExcel(_allNotes, ["ID", "Título", "Dev origen", "Prioridad", "Recordatorio", "Completado", "Alta"],
            n => [n.Id, n.Title, n.Developer?.FullName, PriorityLabel(n.Priority), n.ReminderDate, n.IsCompleted, n.CreatedAt], "Notas", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static string VacStatusLabel(VacationStatus st) => st switch { VacationStatus.Pendiente => "⏳ Pendiente", VacationStatus.Aprobada => "✅ Aprobada", VacationStatus.Rechazada => "❌ Rechazada", _ => "🚫 Cancelada" };
    private static Color VacStatusColor(VacationStatus st) => st switch { VacationStatus.Pendiente => AppTheme.Warning, VacationStatus.Aprobada => AppTheme.Success, VacationStatus.Rechazada => AppTheme.Danger, _ => AppTheme.TextSecondary };
    private static string PriorityLabel(NotePriority p) => p switch { NotePriority.Alta => "🔴 Alta", NotePriority.Media => "🟡 Media", _ => "🔵 Baja" };
}
