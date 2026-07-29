using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Reporte de planeación (solo administrador): precisión de estimaciones (horas estimadas vs tiempo
/// real cronometrado) y capacidad del equipo (carga abierta + vacaciones) para saber a quién asignar.
/// </summary>
public class EstimationCapacityControl : UserControl
{
    private readonly AppDbContext _db;

    // Estimación
    private DataGridView _gridEst = null!;
    private Panel _pnlKpisEst = null!;
    private Label _lblEstStatus = null!;
    // Capacidad
    private DataGridView _gridCap = null!;
    private Panel _pnlKpisCap = null!;
    private NumericUpDown _numDias = null!;
    private Label _lblCapStatus = null!;

    public EstimationCapacityControl(AppDbContext db)
    {
        _db = db;
        BuildUI();
        CargarEstimacion();
        CargarCapacidad();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildEstimacionTab());
        tabs.TabPages.Add(BuildCapacidadTab());
        Controls.Add(tabs);
    }

    // ── Estimación vs real ───────────────────────────────────────
    private TabPage BuildEstimacionTab()
    {
        var tab = new TabPage("  🎯  Estimación vs real  ") { BackColor = AppTheme.ContentBg };
        var tbl = NewLayout();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        bar.Controls.Add(new Label { Text = "Solo requerimientos con horas estimadas.", AutoSize = true, Margin = new Padding(0, 8, 12, 0), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        var bCsv = AppTheme.MakeSecondaryButton("⬇ CSV", 90, 26); bCsv.Margin = new Padding(0, 2, 6, 0); bCsv.Click += (_, _) => ExportarGrid(_gridEst, "estimacion");
        var bReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110, 26); bReload.Margin = new Padding(0, 2, 0, 0); bReload.Click += (_, _) => CargarEstimacion();
        bar.Controls.AddRange([bCsv, bReload]);

        _pnlKpisEst = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _lblEstStatus = NewStatus();

        _gridEst = AppTheme.MakeGrid(); _gridEst.Dock = DockStyle.Fill;
        _gridEst.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Width = 50 });
        _gridEst.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título", Name = "Title", FillWeight = 34 });
        _gridEst.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", Name = "Estado", FillWeight = 12 });
        _gridEst.Columns.Add(Num("Estimadas (h)", "Est"));
        _gridEst.Columns.Add(Num("Reales (h)", "Real"));
        _gridEst.Columns.Add(Num("Δ (h)", "Delta"));
        _gridEst.Columns.Add(Num("Ratio", "Ratio"));
        _gridEst.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Clasificación", Name = "Clase", FillWeight = 20 });
        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 10) }; pGrid.Controls.Add(_gridEst);

        tbl.Controls.Add(bar, 0, 0); tbl.Controls.Add(_pnlKpisEst, 0, 1); tbl.Controls.Add(_lblEstStatus, 0, 2); tbl.Controls.Add(pGrid, 0, 3);
        tab.Controls.Add(tbl);
        return tab;
    }

    private void CargarEstimacion()
    {
        var now = DateTime.UtcNow;
        var segPorReq = _db.WorkSessions.AsNoTracking().Where(w => w.RequirementId != null).ToList()
            .GroupBy(w => w.RequirementId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(now)));

        var reqs = _db.Requirements.AsNoTracking().Where(r => r.EstimateHours != null && r.EstimateHours > 0).ToList();
        var filas = EstimationStats.Filas(reqs.Select(r => new EstimationInput(
            r.Id, r.Title, r.EstimateHours, segPorReq.TryGetValue(r.Id, out var s) ? s : 0, r.Status)));
        filas = filas.OrderByDescending(f => f.DeltaHrs).ToList();

        _gridEst.Rows.Clear();
        foreach (var f in filas)
        {
            int i = _gridEst.Rows.Add(f.ReqId, f.Title, StatusLabel(f.Estado),
                $"{f.EstimateHrs:0.#}", $"{f.ActualHrs:0.#}", $"{f.DeltaHrs:+0.#;-0.#;0}",
                f.Ratio?.ToString("0.00") ?? "—", EstimationStats.EtiquetaClase(f.Clase));
            _gridEst.Rows[i].Cells["Clase"].Style.ForeColor = ColorClase(f.Clase);
            _gridEst.Rows[i].Cells["Clase"].Style.Font = AppTheme.BoldFont;
        }

        var r = EstimationStats.Resumen(filas);
        _pnlKpisEst.Controls.Clear();
        var kpis = new (string, string, Color)[]
        {
            ("Ratio promedio", r.ConDatos == 0 ? "—" : $"{r.RatioPromedio:0.00}×", r.ConDatos == 0 ? AppTheme.TextSecondary : (r.RatioPromedio > 1.2 ? AppTheme.Danger : r.RatioPromedio < 0.8 ? AppTheme.Warning : AppTheme.Success)),
            ("✓ Precisos", r.Precisos.ToString(), AppTheme.Success),
            ("▲ Subestimados", r.Subestimados.ToString(), AppTheme.Danger),
            ("▼ Sobreestimados", r.Sobreestimados.ToString(), AppTheme.Warning),
            ("Horas estimadas", $"{r.HorasEstimadas:0.#}", AppTheme.SidebarActive),
            ("Horas reales", $"{r.HorasReales:0.#}", AppTheme.SidebarActive),
        };
        PintarKpis(_pnlKpisEst, kpis);
        _lblEstStatus.Text = filas.Count == 0
            ? "No hay requerimientos con horas estimadas."
            : $"{filas.Count} requerimiento(s) con estimación · {r.ConDatos} ya con tiempo medido.";
    }

    // ── Capacidad del equipo ─────────────────────────────────────
    private TabPage BuildCapacidadTab()
    {
        var tab = new TabPage("  👥  Capacidad del equipo  ") { BackColor = AppTheme.ContentBg };
        var tbl = NewLayout();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        bar.Controls.Add(new Label { Text = "Vacaciones en los próximos", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _numDias = new NumericUpDown { Width = 70, Minimum = 1, Maximum = 365, Value = 30, Margin = new Padding(0, 4, 4, 0) };
        _numDias.ValueChanged += (_, _) => CargarCapacidad();
        bar.Controls.Add(_numDias);
        bar.Controls.Add(new Label { Text = $"días.  «Sobrecargado» = más de {CapacityStats.CapacidadPorDefecto:0} h estimadas pendientes.", AutoSize = true, Margin = new Padding(0, 8, 12, 0), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont });
        var bCsv = AppTheme.MakeSecondaryButton("⬇ CSV", 90, 26); bCsv.Margin = new Padding(0, 2, 6, 0); bCsv.Click += (_, _) => ExportarGrid(_gridCap, "capacidad");
        var bReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110, 26); bReload.Margin = new Padding(0, 2, 0, 0); bReload.Click += (_, _) => CargarCapacidad();
        bar.Controls.AddRange([bCsv, bReload]);

        _pnlKpisCap = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _lblCapStatus = NewStatus();

        _gridCap = AppTheme.MakeGrid(); _gridCap.Dock = DockStyle.Fill;
        _gridCap.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev", FillWeight = 26 });
        _gridCap.Columns.Add(Num("Req. abiertos", "Abiertos"));
        _gridCap.Columns.Add(Num("Horas pendientes", "Pend"));
        _gridCap.Columns.Add(Num("Horas registradas", "Reg"));
        _gridCap.Columns.Add(Num("Vacaciones (días)", "Vac"));
        _gridCap.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Disponibilidad", Name = "Estado", FillWeight = 20 });
        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 10) }; pGrid.Controls.Add(_gridCap);

        tbl.Controls.Add(bar, 0, 0); tbl.Controls.Add(_pnlKpisCap, 0, 1); tbl.Controls.Add(_lblCapStatus, 0, 2); tbl.Controls.Add(pGrid, 0, 3);
        tab.Controls.Add(tbl);
        return tab;
    }

    private void CargarCapacidad()
    {
        int dias = (int)_numDias.Value;
        var hoy = DateTime.Today;
        // Rango inclusivo de EXACTAMENTE `dias` días (hoy incluido): con AddDays(dias) serían dias+1.
        var fin = hoy.AddDays(dias - 1);
        var now = DateTime.UtcNow;

        var segPorDev = _db.WorkSessions.AsNoTracking().ToList()
            .GroupBy(w => w.DeveloperId).ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(now)));
        var reqs = _db.Requirements.AsNoTracking().ToList().ToDictionary(r => r.Id);
        var assigns = _db.Assignments.AsNoTracking().ToList();
        var vacs = _db.VacationRequests.AsNoTracking().Where(v => v.Status == VacationStatus.Aprobada).ToList();
        var devs = _db.Developers.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();

        var rows = new List<CapacityRow>();
        foreach (var d in devs)
        {
            var abiertos = assigns.Where(a => a.DeveloperId == d.Id).Select(a => a.RequirementId).Distinct()
                .Where(id => reqs.TryGetValue(id, out var r) && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
                .ToList();
            double horasPend = abiertos.Sum(id => (double)(reqs[id].EstimateHours ?? 0));
            double horasReg = EstimationStats.SegundosAHoras(segPorDev.TryGetValue(d.Id, out var s) ? s : 0);
            var misVacs = vacs.Where(v => v.DeveloperId == d.Id).ToList();
            int diasVac = misVacs.Sum(v => CapacityStats.DiasVacacionEnRango(v.StartDate, v.EndDate, hoy, fin));
            bool deVac = misVacs.Any(v => CapacityStats.EnVacacion(v.StartDate, v.EndDate, hoy));
            var estado = CapacityStats.Clasificar(deVac, abiertos.Count, horasPend, CapacityStats.CapacidadPorDefecto);
            rows.Add(new CapacityRow(d.FullName, abiertos.Count, Math.Round(horasPend, 1), Math.Round(horasReg, 1), diasVac, estado));
        }

        _gridCap.Rows.Clear();
        foreach (var r in rows.OrderByDescending(x => x.Estado == Disponibilidad.Sobrecargado).ThenBy(x => x.Developer))
        {
            int i = _gridCap.Rows.Add(r.Developer, r.Abiertos, $"{r.HorasPendientes:0.#}", $"{r.HorasRegistradas:0.#}", r.DiasVacaciones, CapacityStats.EtiquetaEstado(r.Estado));
            _gridCap.Rows[i].Cells["Estado"].Style.ForeColor = ColorEstado(r.Estado);
            _gridCap.Rows[i].Cells["Estado"].Style.Font = AppTheme.BoldFont;
        }

        _pnlKpisCap.Controls.Clear();
        var kpis = new (string, string, Color)[]
        {
            ("🟢 Libres", rows.Count(x => x.Estado == Disponibilidad.Libre).ToString(), AppTheme.Success),
            ("🟡 Ocupados", rows.Count(x => x.Estado == Disponibilidad.Ocupado).ToString(), AppTheme.Warning),
            ("🔴 Sobrecargados", rows.Count(x => x.Estado == Disponibilidad.Sobrecargado).ToString(), AppTheme.Danger),
            ("🏖 De vacaciones", rows.Count(x => x.Estado == Disponibilidad.DeVacaciones).ToString(), AppTheme.SidebarActive),
        };
        PintarKpis(_pnlKpisCap, kpis);
        _lblCapStatus.Text = $"{rows.Count} desarrollador(es) activo(s) · vacaciones contadas en los próximos {dias} días.";
    }

    // ── Compartido ───────────────────────────────────────────────
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { CargarEstimacion(); CargarCapacidad(); }
    }

    private void ExportarGrid(DataGridView grid, string nombre)
    {
        if (grid.Rows.Count == 0) { MessageBox.Show("No hay datos para exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"{nombre}_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var lines = new List<string> { string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => Csv(c.HeaderText))) };
        foreach (DataGridViewRow row in grid.Rows)
            lines.Add(string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => Csv(row.Cells[c.Name].Value))));
        File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
        MessageBox.Show("Exportado.", "CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static TableLayoutPanel NewLayout()
    {
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return tbl;
    }

    private static Label NewStatus() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
    };

    private static void PintarKpis(Panel panel, (string lbl, string val, Color color)[] kpis)
    {
        int x = 0;
        foreach (var (lbl, val, color) in kpis)
        {
            var card = MakeCard(lbl, val, color);
            card.Location = new Point(x, 4);
            panel.Controls.Add(card);
            x += 158;
        }
    }

    private static string Csv(object? v) => $"\"{(v?.ToString() ?? "").Replace("\"", "\"\"")}\"";

    private static Color ColorClase(EstimationClass c) => c switch
    {
        EstimationClass.Preciso => AppTheme.Success,
        EstimationClass.Subestimado => AppTheme.Danger,
        EstimationClass.Sobreestimado => AppTheme.Warning,
        _ => AppTheme.TextSecondary
    };

    private static Color ColorEstado(Disponibilidad d) => d switch
    {
        Disponibilidad.Libre => AppTheme.Success,
        Disponibilidad.Ocupado => AppTheme.Warning,
        Disponibilidad.Sobrecargado => AppTheme.Danger,
        _ => AppTheme.SidebarActive
    };

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar => "Por estimar",
        RequirementStatus.Estimado => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas => "En pruebas",
        RequirementStatus.PorEntregar => "Por entregar",
        RequirementStatus.Entregado => "Entregado",
        RequirementStatus.Cancelado => "Cancelado",
        _ => s.ToString()
    };

    private static DataGridViewTextBoxColumn Num(string header, string name) => new()
    {
        HeaderText = header, Name = name, FillWeight = 12,
        DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
    };

    private static Panel MakeCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 150, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label { Text = value, Font = new Font("Segoe UI", 19f, FontStyle.Bold), ForeColor = accent, Location = new Point(12, 8), Size = new Size(130, 34), AutoEllipsis = true });
        card.Controls.Add(new Label { Text = label, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Location = new Point(12, 50), Size = new Size(134, 20), AutoEllipsis = true });
        return card;
    }
}
