using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class DashboardControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private FlowLayoutPanel _pnlCards = null!;
    private DataGridView _gridDeadlines = null!;
    private DataGridView? _gridWorkload;
    private DataGridView? _gridNotes;
    private DataGridView? _gridRanking;

    /// <summary>
    /// La carga por desarrollador, los recordatorios internos y el ranking de desempeño son datos
    /// del equipo: solo los ve un administrador. Se comprueba AQUÍ y no solo en el menú porque una
    /// pantalla que depende de que nadie la alcance está a una ruta nueva de filtrar datos ajenos.
    /// </summary>
    private bool VeDatosDeEquipo => _currentUser.IsAdmin;

    public DashboardControl(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db; _currentUser = currentUser;
        BuildUI();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _pnlCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg,
            Margin = Padding.Empty, Padding = new Padding(12, 10, 12, 10)
        };

        // 4-panel 2x2 grid
        var grid2x2 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2,
            Margin = Padding.Empty, Padding = new Padding(8, 4, 8, 8),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        grid2x2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid2x2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid2x2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        grid2x2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        var pDeadlines = MakeSection("⏰  Próximas entregas (7 días)");
        _gridDeadlines = AppTheme.MakeGrid();
        _gridDeadlines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requerimiento", FillWeight = 52 });
        _gridDeadlines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",         FillWeight = 26 });
        _gridDeadlines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",          FillWeight = 22 });
        pDeadlines.Controls.Add(_gridDeadlines, 0, 1);

        grid2x2.Controls.Add(pDeadlines, 0, 0);

        // Las tres secciones de datos del equipo NI SIQUIERA SE CONSTRUYEN si el rol no debe
        // verlas: sin grid no hay consulta que llenarlo, así que los datos no llegan a existir
        // en memoria. Ocultar un control que ya cargó la información no es lo mismo.
        if (VeDatosDeEquipo)
        {
            var pWorkload = MakeSection("👤  Carga por desarrollador");
            _gridWorkload = AppTheme.MakeGrid();
            _gridWorkload.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", FillWeight = 55 });
            _gridWorkload.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activos",        FillWeight = 22 });
            _gridWorkload.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Por entregar",   FillWeight = 23 });
            pWorkload.Controls.Add(_gridWorkload, 0, 1);

            var pNotes = MakeSection("📌  Recordatorios pendientes");
            _gridNotes = AppTheme.MakeGrid();
            _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nota / Pendiente", FillWeight = 52 });
            _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Prioridad",         FillWeight = 18 });
            _gridNotes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Recordatorio",      FillWeight = 30 });
            pNotes.Controls.Add(_gridNotes, 0, 1);

            var pRanking = MakeSection($"🏆  Top ranking — {DateTime.Today:MMMM yyyy}");
            _gridRanking = AppTheme.MakeGrid();
            _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pos.", Name = "Pos",   FillWeight = 12 });
            _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dev",  Name = "Dev",   FillWeight = 55 });
            _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pts",  Name = "Total", FillWeight = 18 });
            _gridRanking.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entradas", Name = "Cnt", FillWeight = 15 });
            pRanking.Controls.Add(_gridRanking, 0, 1);

            grid2x2.Controls.Add(pWorkload, 1, 0);
            grid2x2.Controls.Add(pNotes,    0, 1);
            grid2x2.Controls.Add(pRanking,  1, 1);
        }
        else
        {
            // Sin las secciones de equipo, «Próximas entregas» ocupa el ancho completo.
            grid2x2.SetColumnSpan(pDeadlines, 2);
        }

        tbl.Controls.Add(_pnlCards, 0, 0);
        tbl.Controls.Add(grid2x2,   0, 1);
        Controls.Add(tbl);
        LoadData();
    }

    // Sección con título arriba (fila fija) y el grid abajo (fila fill). Se usa TableLayoutPanel
    // para que el grid NO se solape con el título (con Dock el grid llenaba también el área del
    // título y tapaba el encabezado de columnas). El grid se agrega en la celda (0, 1).
    private static TableLayoutPanel MakeSection(string title)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = AppTheme.ContentBg, Padding = new Padding(4, 2, 4, 4), Margin = new Padding(4),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, BackColor = AppTheme.ContentBg, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        return t;
    }

    private void LoadData()
    {
        // Status cards
        _pnlCards.Controls.Clear();
        var cards = new[]
        {
            ("Por estimar",   _db.Requirements.Count(r => r.Status == RequirementStatus.PorEstimar),   AppTheme.TextSecondary),
            ("En desarrollo", _db.Requirements.Count(r => r.Status == RequirementStatus.EnDesarrollo), AppTheme.Warning),
            ("Por entregar",  _db.Requirements.Count(r => r.Status == RequirementStatus.PorEntregar),  Color.FromArgb(251, 146, 60)),
            ("Entregados",    _db.Requirements.Count(r => r.Status == RequirementStatus.Entregado),    AppTheme.Success),
            ("Cancelados",    _db.Requirements.Count(r => r.Status == RequirementStatus.Cancelado),    AppTheme.Danger),
            ("Pendientes",    _db.Notes.Count(n => !n.IsCompleted),                                     Color.FromArgb(139, 92, 246))
        };
        foreach (var (label, count, color) in cards)
        {
            var card = new Panel { Size = new Size(148, 96), Margin = new Padding(0, 0, 8, 0), BackColor = AppTheme.CardBg };
            var lc = color;
            card.Paint += (_, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(lc), 0, 0, 5, card.Height);
                using var pen = new Pen(Color.FromArgb(220, 220, 230), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            card.Controls.Add(new Label { Text = count.ToString(), Location = new Point(13, 8), AutoSize = false, Size = new Size(130, 44), Font = new Font("Segoe UI", 24f, FontStyle.Bold), ForeColor = lc, TextAlign = ContentAlignment.MiddleLeft });
            card.Controls.Add(new Label { Text = label, Location = new Point(13, 56), AutoSize = false, Size = new Size(130, 22), Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
            _pnlCards.Controls.Add(card);
        }

        // Upcoming deadlines
        _gridDeadlines.Rows.Clear();
        var deadline = DateTime.Today.AddDays(7);
        foreach (var r in _db.Requirements
            .Where(r => r.CommittedDeliveryDate <= deadline && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
            .OrderBy(r => r.CommittedDeliveryDate).Take(15).ToList())
        {
            bool overdue = r.CommittedDeliveryDate < DateTime.Today;
            int i = _gridDeadlines.Rows.Add(r.Title, StatusLbl(r.Status), r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—");
            if (overdue) { _gridDeadlines.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger; _gridDeadlines.Rows[i].DefaultCellStyle.Font = AppTheme.BoldFont; }
        }

        // Las tres consultas siguientes solo se EJECUTAN si el rol tiene derecho a esos datos.
        // No basta con no pintarlos: si la consulta corre, los datos del equipo ya salieron de la
        // base y viven en el proceso.
        if (!VeDatosDeEquipo) return;

        // Workload
        _gridWorkload!.Rows.Clear();
        foreach (var dev in _db.Developers.Where(d => d.IsActive).Include(d => d.Assignments).ThenInclude(a => a.Requirement).OrderBy(d => d.FullName).ToList())
        {
            int active = dev.Assignments.Count(a => a.Requirement.Status != RequirementStatus.Entregado && a.Requirement.Status != RequirementStatus.Cancelado);
            int deliver = dev.Assignments.Count(a => a.Requirement.Status == RequirementStatus.PorEntregar);
            _gridWorkload.Rows.Add(dev.FullName, active, deliver);
        }

        // Pending notes
        _gridNotes!.Rows.Clear();
        foreach (var n in _db.Notes.Where(n => !n.IsCompleted).OrderBy(n => n.ReminderDate).Take(10).ToList())
        {
            bool overdue = n.ReminderDate.HasValue && n.ReminderDate < DateTime.Today;
            string pLabel = n.Priority == NotePriority.Alta ? "🔴 Alta" : n.Priority == NotePriority.Media ? "🟡 Media" : "🔵 Baja";
            int i = _gridNotes.Rows.Add(n.Title, pLabel, n.ReminderDate?.ToString("dd/MM/yyyy") ?? "Sin fecha");
            if (overdue) _gridNotes.Rows[i].DefaultCellStyle.ForeColor = AppTheme.Danger;
        }

        // Top ranking (current month)
        _gridRanking!.Rows.Clear();
        int month = DateTime.Today.Month; int year = DateTime.Today.Year;
        // Solo cuentan los puntos aprobados (los pendientes/rechazados no influyen en el ranking).
        var pts = _db.PointEntries.Include(p => p.Developer).Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado).ToList();
        var ranking = pts.GroupBy(p => p.DeveloperId)
            .Select(g => (Dev: g.First().Developer.FullName, Total: g.Sum(p => p.Points), Count: g.Count()))
            .OrderByDescending(r => r.Total).Take(5).ToList();
        for (int i = 0; i < ranking.Count; i++)
        {
            var r = ranking[i];
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            int ri = _gridRanking.Rows.Add(medal, r.Dev, r.Total > 0 ? $"+{r.Total}" : r.Total.ToString(), r.Count);
            _gridRanking.Rows[ri].Cells["Total"].Style.ForeColor = r.Total >= 0 ? AppTheme.Success : AppTheme.Danger;
            _gridRanking.Rows[ri].Cells["Total"].Style.Font = AppTheme.BoldFont;
        }
        if (!ranking.Any()) _gridRanking.Rows.Add("—", "Sin datos este mes", "", "");
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static string StatusLbl(RequirementStatus s) => s switch
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
}
