using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private DataGridView _gridHistory = null!;
    private RichTextBox _txtHistoryLog = null!;
    private List<DeploymentJob> _jobs = [];
    private Dictionary<int, string> _quienes = [];

    private TabPage BuildHistoryTab()
    {
        var tab = new TabPage("  📜  Historial  ") { BackColor = AppTheme.ContentBg };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            BorderStyle = BorderStyle.None, BackColor = AppTheme.ContentBg
        };

        // Top: jobs grid
        var topTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        topTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        topTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        topTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 6, 10, 2), BackColor = AppTheme.ContentBg };
        topBar.Controls.Add(new Label { Text = "Historial de despliegues (selecciona uno para ver su log)", AutoSize = true, Font = AppTheme.BoldFont, Margin = new Padding(0, 4, 16, 0) });
        var bRefresh = AppTheme.MakeSecondaryButton("🔄 Actualizar", 110, 26); bRefresh.Click += (_, _) => LoadHistory();
        bRefresh.Margin = new Padding(0, 0, 6, 0);
        topBar.Controls.Add(bRefresh);

        // Evidencia: lo que se pide cuando alguien pregunta «¿quién desplegó qué y cuándo?».
        var bEvidencia = AppTheme.MakeSecondaryButton("📄 Evidencia del seleccionado", 220, 26);
        bEvidencia.Margin = new Padding(0, 0, 6, 0);
        bEvidencia.Click += (_, _) => ExportarEvidencia();
        var bExcel = AppTheme.MakeSecondaryButton("📊 Historial a Excel", 170, 26);
        bExcel.Click += (_, _) => ExportarHistorialExcel();
        topBar.Controls.AddRange([bEvidencia, bExcel]);
        topTbl.Controls.Add(topBar, 0, 0);

        _gridHistory = AppTheme.MakeGrid();
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",       Name = "Id",   Width = 40, FillWeight = 4 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sistema",   Name = "Sys",  FillWeight = 18 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión",   Name = "Ver",  FillWeight = 13 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Perfil",    Name = "Prof", FillWeight = 16 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",    Name = "St",   FillWeight = 12 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "OK / Fall.", Name = "Res", FillWeight = 10 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Inicio",    Name = "St2",  FillWeight = 12 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración",  Name = "Dur",  FillWeight = 9 });
        // Quién lo lanzó: sin esta columna, la pregunta de auditoría obliga a abrir la bitácora.
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quién",     Name = "Quien", FillWeight = 14 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Checklist", Name = "Chk",  FillWeight = 8 });
        _gridHistory.SelectionChanged += (_, _) => LoadJobLog();
        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 6), BackColor = AppTheme.ContentBg }; pGrid.Controls.Add(_gridHistory);
        topTbl.Controls.Add(pGrid, 0, 1);
        split.Panel1.Controls.Add(topTbl);

        // Bottom: job log
        var botTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        botTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        botTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        botTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        botTbl.Controls.Add(new Label { Text = "📋  Log del despliegue seleccionado", Dock = DockStyle.Fill, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Padding = new Padding(10, 4, 0, 0) }, 0, 0);
        _txtHistoryLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 24, 33), ForeColor = Color.FromArgb(220, 230, 240), Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None };
        var pLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10), BackColor = AppTheme.ContentBg }; pLog.Controls.Add(_txtHistoryLog);
        botTbl.Controls.Add(pLog, 0, 1);
        split.Panel2.Controls.Add(botTbl);

        tab.Controls.Add(split);
        tab.Layout += (_, _) => { if (split.Height > 100) split.SplitterDistance = (int)(split.Height * 0.55); };
        return tab;
    }

    private void LoadHistory()
    {
        // AsNoTracking OBLIGATORIO: el AppDbContext es Singleton y el despliegue corre en su
        // propio contexto. Sin esto, la resolución de identidad devuelve las instancias que quedaron
        // rastreadas ANTES de desplegar y la evidencia exportada dice «En curso / 0 correctos» de un
        // despliegue que ya terminó bien.
        _jobs = _db.DeploymentJobs.AsNoTracking()
            .Include(j => j.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(j => j.Profile)
            .OrderByDescending(j => j.CreatedAt).Take(200).ToList();

        // Los nombres, en UNA consulta: con doscientos despliegues, resolver el usuario fila por
        // fila son doscientos viajes a una base remota.
        var ids = _jobs.Where(j => j.StartedById != null).Select(j => j.StartedById!.Value).Distinct().ToList();
        _quienes = _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToList()
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);

        _gridHistory.Rows.Clear();
        foreach (var j in _jobs)
        {
            string dur = (j.StartedAt != null && j.CompletedAt != null)
                ? $"{(j.CompletedAt.Value - j.StartedAt.Value).TotalSeconds:F0} s" : "—";
            int i = _gridHistory.Rows.Add(j.Id, j.AppRelease.AppSystem.Name, j.AppRelease.Version, j.Profile.Name,
                JobStatusLabel(j.Status), $"{j.TargetsOk} / {j.TargetsFailed}",
                j.StartedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—", dur,
                Quien(j), string.IsNullOrWhiteSpace(j.Notes) ? "—" : "✓");
            _gridHistory.Rows[i].Cells["St"].Style.ForeColor = JobStatusColor(j.Status);
            _gridHistory.Rows[i].Cells["St"].Style.Font = AppTheme.BoldFont;

            // Los despliegues anteriores al checklist no tienen evidencia: se marca en gris para
            // que la ausencia sea un dato visible y no una casilla que alguien olvidó.
            if (string.IsNullOrWhiteSpace(j.Notes))
            {
                var c = _gridHistory.Rows[i].Cells["Chk"].Style;
                c.ForeColor = AppTheme.TextSecondary; c.SelectionForeColor = AppTheme.TextSecondary;
            }
        }
        LoadJobLog();
    }

    private void LoadJobLog()
    {
        _txtHistoryLog.Clear();
        if (_gridHistory.CurrentRow?.Cells["Id"].Value is not int jobId) return;
        var entries = _db.DeploymentLogEntries.Where(l => l.JobId == jobId).OrderBy(l => l.Timestamp).ToList();
        foreach (var l in entries)
        {
            var color = l.Level switch
            {
                DeployLogLevel.Exito       => Color.FromArgb(134, 239, 172),
                DeployLogLevel.Error       => Color.FromArgb(252, 165, 165),
                DeployLogLevel.Advertencia => Color.FromArgb(253, 224, 71),
                _                          => Color.FromArgb(220, 230, 240)
            };
            _txtHistoryLog.SelectionStart = _txtHistoryLog.TextLength;
            _txtHistoryLog.SelectionColor = color;
            _txtHistoryLog.AppendText($"[{l.Timestamp.ToLocalTime():HH:mm:ss}] {(l.TargetName != null ? $"[{l.TargetName}] " : "")}{l.Message}{Environment.NewLine}");
        }
        if (entries.Count == 0) _txtHistoryLog.AppendText("(Sin entradas de log para este despliegue)");
    }

    private string Quien(DeploymentJob j) =>
        j.StartedById is int id && _quienes.TryGetValue(id, out var n) ? n : "—";

    // ── Evidencia ────────────────────────────────────────────────────────────────

    /// <summary>
    /// El expediente de UN despliegue: qué se desplegó, a dónde, quién lo lanzó, el checklist que
    /// confirmó y el log completo. Es lo que se entrega cuando alguien pregunta qué pasó — un
    /// archivo de texto, legible sin la aplicación y sin Excel.
    /// </summary>
    private void ExportarEvidencia()
    {
        if (_gridHistory.CurrentRow?.Cells["Id"].Value is not int jobId
            || _jobs.FirstOrDefault(j => j.Id == jobId) is not { } j)
        { MessageBox.Show("Selecciona un despliegue del historial.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("EVIDENCIA DE DESPLIEGUE");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Despliegue #  : {j.Id}");
        sb.AppendLine($"Sistema       : {j.AppRelease.AppSystem.Name}");
        sb.AppendLine($"Versión       : {j.AppRelease.Version}");
        sb.AppendLine($"Destino       : {j.Profile.Name}");
        sb.AppendLine($"Lanzado por   : {Quien(j)}");
        sb.AppendLine($"Inicio        : {j.StartedAt?.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Fin           : {j.CompletedAt?.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Resultado     : {JobStatusLabel(j.Status)}  —  {j.TargetsOk} correcto(s), {j.TargetsFailed} fallido(s) de {j.TargetsTotal}");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(j.Notes)
            // No se disimula: un despliegue anterior al checklist no tiene evidencia, y decirlo
            // vale más que un espacio en blanco que parece un descuido.
            ? "(Este despliegue es anterior al checklist previo: no hay confirmación registrada.)"
            : j.Notes);
        sb.AppendLine();
        sb.AppendLine("LOG COMPLETO");
        sb.AppendLine(new string('-', 60));
        foreach (var l in _db.DeploymentLogEntries.AsNoTracking()
                     .Where(l => l.JobId == jobId).OrderBy(l => l.Timestamp).ToList())
            sb.AppendLine($"[{l.Timestamp.ToLocalTime():dd/MM HH:mm:ss}] " +
                          $"{(l.Level == DeployLogLevel.Info ? "" : l.Level.ToString().ToUpperInvariant() + " ")}" +
                          $"{(l.TargetName != null ? $"[{l.TargetName}] " : "")}{l.Message}");

        using var dlg = new SaveFileDialog
        {
            Title = "Guardar evidencia del despliegue",
            FileName = $"Despliegue_{j.Id}_{j.AppRelease.Version}_{j.StartedAt?.ToLocalTime():yyyyMMdd}.txt",
            Filter = "Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            DefaultExt = "txt", OverwritePrompt = true
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try { File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8); }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo guardar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (MessageBox.Show($"Evidencia guardada:\n{dlg.FileName}\n\n¿Abrirla ahora?", "Listo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    /// <summary>Todo el historial a Excel: para revisar el período completo, no un despliegue.</summary>
    private void ExportarHistorialExcel()
    {
        if (_jobs.Count == 0)
        { MessageBox.Show("No hay despliegues en el historial.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var path = _report.PromptSaveDialog($"Despliegues_{DateTime.Now:yyyyMMdd}");
        if (path == null) return;

        try
        {
            _report.ExportToExcel(_jobs,
                ["#", "Sistema", "Versión", "Destino", "Estado", "Servidores OK", "Fallidos", "Total",
                 "Inicio", "Fin", "Duración (s)", "Lanzado por", "Checklist", "Evidencia"],
                j => new object?[]
                {
                    j.Id, j.AppRelease.AppSystem.Name, j.AppRelease.Version, j.Profile.Name,
                    JobStatusLabel(j.Status), j.TargetsOk, j.TargetsFailed, j.TargetsTotal,
                    j.StartedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    j.CompletedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    j.StartedAt != null && j.CompletedAt != null
                        ? Math.Round((j.CompletedAt.Value - j.StartedAt.Value).TotalSeconds) : null,
                    Quien(j),
                    string.IsNullOrWhiteSpace(j.Notes) ? "sin registrar" : "confirmado",
                    // El checklist entero en una celda: en una hoja de cálculo los saltos de línea
                    // parten la fila, así que se aplanan.
                    (j.Notes ?? "").Replace("\r\n", " | ").Replace("\n", " | ")
                },
                "Despliegues", path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (MessageBox.Show($"Exportado:\n{path}\n\n¿Abrirlo ahora?", "Listo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string JobStatusLabel(JobStatus s) => s switch
    {
        JobStatus.Pendiente  => "Pendiente",
        JobStatus.EnCurso    => "En curso",
        JobStatus.Completado => "✅ Completado",
        JobStatus.Fallido    => "❌ Fallido",
        JobStatus.Cancelado  => "⏹ Cancelado",
        _                    => s.ToString()
    };

    private static Color JobStatusColor(JobStatus s) => s switch
    {
        JobStatus.Completado => AppTheme.Success,
        JobStatus.Fallido    => AppTheme.Danger,
        JobStatus.Cancelado  => AppTheme.Warning,
        JobStatus.EnCurso    => AppTheme.SidebarActive,
        _                    => AppTheme.TextSecondary
    };
}
