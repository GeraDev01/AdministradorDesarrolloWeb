using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private DataGridView _gridHistory = null!;
    private RichTextBox _txtHistoryLog = null!;
    private List<DeploymentJob> _jobs = [];

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
        topBar.Controls.Add(bRefresh);
        topTbl.Controls.Add(topBar, 0, 0);

        _gridHistory = AppTheme.MakeGrid();
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",       Name = "Id",   Width = 40, FillWeight = 4 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sistema",   Name = "Sys",  FillWeight = 18 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión",   Name = "Ver",  FillWeight = 13 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Perfil",    Name = "Prof", FillWeight = 16 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",    Name = "St",   FillWeight = 12 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "OK / Fall.", Name = "Res", FillWeight = 10 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Inicio",    Name = "St2",  FillWeight = 14 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración",  Name = "Dur",  FillWeight = 13 });
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
        _jobs = _db.DeploymentJobs
            .Include(j => j.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(j => j.Profile)
            .OrderByDescending(j => j.CreatedAt).Take(200).ToList();
        _gridHistory.Rows.Clear();
        foreach (var j in _jobs)
        {
            string dur = (j.StartedAt != null && j.CompletedAt != null)
                ? $"{(j.CompletedAt.Value - j.StartedAt.Value).TotalSeconds:F0} s" : "—";
            int i = _gridHistory.Rows.Add(j.Id, j.AppRelease.AppSystem.Name, j.AppRelease.Version, j.Profile.Name,
                JobStatusLabel(j.Status), $"{j.TargetsOk} / {j.TargetsFailed}",
                j.StartedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—", dur);
            _gridHistory.Rows[i].Cells["St"].Style.ForeColor = JobStatusColor(j.Status);
            _gridHistory.Rows[i].Cells["St"].Style.Font = AppTheme.BoldFont;
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
