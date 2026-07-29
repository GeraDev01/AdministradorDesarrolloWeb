using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Reporte de CUMPLIMIENTO de SLA (solo administrador): de los compromisos con fecha límite en el
/// período, cuántos se cumplieron a tiempo vs se vencieron, global y desglosado por desarrollador,
/// prioridad de DevOps, cliente (tag) o mes. El porcentaje se mide solo sobre los ya resueltos.
/// </summary>
public class SlaComplianceControl : UserControl
{
    private readonly AppDbContext _db;

    private DateTimePicker _dtDesde = null!, _dtHasta = null!;
    private ComboBox _cbxAgrupar = null!;
    private Panel _pnlKpis = null!;
    private DataGridView _grid = null!;
    private Label _lblStatus = null!;

    private List<SlaComplianceInput> _inputs = [];

    public SlaComplianceControl(AppDbContext db)
    {
        _db = db;
        BuildUI();
        Cargar();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));   // toolbar
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));   // KPIs
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));   // status
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // grid
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        bar.Controls.Add(new Label { Text = "Desde:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _dtDesde = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 4, 12, 0), Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) };
        _dtDesde.ValueChanged += (_, _) => Cargar();
        bar.Controls.Add(_dtDesde);
        bar.Controls.Add(new Label { Text = "Hasta:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _dtHasta = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 4, 12, 0), Value = DateTime.Today };
        _dtHasta.ValueChanged += (_, _) => Cargar();
        bar.Controls.Add(_dtHasta);
        bar.Controls.Add(new Label { Text = "Agrupar por:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbxAgrupar = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbxAgrupar.Items.AddRange(["Desarrollador", "Prioridad", "Cliente (tag)", "Mes"]);
        _cbxAgrupar.SelectedIndex = 0;
        _cbxAgrupar.SelectedIndexChanged += (_, _) => Pintar();
        bar.Controls.Add(_cbxAgrupar);
        var bCsv = AppTheme.MakeSecondaryButton("⬇ CSV", 90, 26); bCsv.Margin = new Padding(0, 2, 6, 0); bCsv.Click += Exportar;
        var bReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110, 26); bReload.Margin = new Padding(0, 2, 0, 0); bReload.Click += (_, _) => Cargar();
        bar.Controls.AddRange([bCsv, bReload]);

        _pnlKpis = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _lblStatus = new Label { Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };

        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Grupo", Name = "Grupo", Width = 240 });
        _grid.Columns.Add(Num("✓ Cumplidos", "Cumpl"));
        _grid.Columns.Add(Num("⚠ Vencidos", "Venc"));
        _grid.Columns.Add(Num("● En curso", "Curso"));
        _grid.Columns.Add(Num("Cancelados", "Canc"));
        _grid.Columns.Add(Num("Total", "Total"));
        _grid.Columns.Add(Num("% Cumplimiento", "Pct"));
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 10) };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(bar, 0, 0);
        tbl.Controls.Add(_pnlKpis, 0, 1);
        tbl.Controls.Add(_lblStatus, 0, 2);
        tbl.Controls.Add(pnlGrid, 0, 3);
        Controls.Add(tbl);
    }

    private void Cargar()
    {
        if (_dtHasta.Value.Date < _dtDesde.Value.Date) { _lblStatus.Text = "El «hasta» no puede ser anterior al «desde»."; return; }

        var desdeUtc = _dtDesde.Value.Date.ToUniversalTime();
        var hastaUtc = _dtHasta.Value.Date.AddDays(1).ToUniversalTime();   // fin de día inclusivo

        var slas = _db.SlaCommitments.Include(s => s.Developer)
            .Where(s => s.DueAtUtc >= desdeUtc && s.DueAtUtc < hastaUtc)
            .AsNoTracking()
            .ToList();

        // Prioridad y tags salen del ticket de DevOps ligado (los que tengan uno).
        var ids = slas.Where(s => s.DevOpsTicketExternalId != null)
                      .Select(s => s.DevOpsTicketExternalId!.Value).Distinct().ToList();
        var tickets = _db.DevOpsTickets.Where(t => ids.Contains(t.ExternalId))
            .Select(t => new { t.ExternalId, t.Priority, t.Tags })
            .AsNoTracking().ToList()
            .ToDictionary(t => t.ExternalId);

        _inputs = slas.Select(s =>
        {
            string? prio = null, tags = null;
            if (s.DevOpsTicketExternalId is int ext && tickets.TryGetValue(ext, out var tk)) { prio = tk.Priority; tags = tk.Tags; }
            return new SlaComplianceInput(s.DueAtUtc, s.Status, s.Developer?.FullName ?? "(sin asignar)", prio, tags);
        }).ToList();

        Pintar();
    }

    private void Pintar()
    {
        var now = DateTime.UtcNow;
        var rows = _cbxAgrupar.SelectedIndex switch
        {
            1 => SlaComplianceStats.PorPrioridad(_inputs, now),
            2 => SlaComplianceStats.PorTag(_inputs, now),
            3 => SlaComplianceStats.PorMes(_inputs, now),
            _ => SlaComplianceStats.PorDesarrollador(_inputs, now),
        };

        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = _grid.Rows.Add(r.Grupo, r.Cumplidos, r.Vencidos, r.EnCurso, r.Cancelados, r.Total,
                r.Resueltos == 0 ? "—" : $"{r.PorcentajeCumplimiento:0.#}%");
            if (r.Vencidos > 0) _grid.Rows[i].Cells["Venc"].Style.ForeColor = AppTheme.Danger;
            if (r.Resueltos > 0)
            {
                _grid.Rows[i].Cells["Pct"].Style.ForeColor = ColorPct(r.PorcentajeCumplimiento);
                _grid.Rows[i].Cells["Pct"].Style.Font = AppTheme.BoldFont;
            }
        }

        BuildKpis(now);
        var etiqueta = _cbxAgrupar.SelectedItem?.ToString() ?? "";
        _lblStatus.Text = _inputs.Count == 0
            ? "No hay compromisos de SLA con fecha límite en el período elegido."
            : $"{_inputs.Count} compromiso(s) con vencimiento en el período · agrupado por {etiqueta}.";
    }

    private void BuildKpis(DateTime now)
    {
        _pnlKpis.Controls.Clear();
        var g = SlaComplianceStats.Resumen(_inputs, now);

        var kpis = new (string, string, Color)[]
        {
            ("% Cumplimiento", g.Resueltos == 0 ? "—" : $"{g.PorcentajeCumplimiento:0.#}%", g.Resueltos == 0 ? AppTheme.TextSecondary : ColorPct(g.PorcentajeCumplimiento)),
            ("✓ Cumplidos", g.Cumplidos.ToString(), AppTheme.Success),
            ("⚠ Vencidos", g.Vencidos.ToString(), AppTheme.Danger),
            ("● En curso", g.EnCurso.ToString(), AppTheme.Warning),
            ("Cancelados", g.Cancelados.ToString(), AppTheme.TextSecondary),
            ("Total", g.Total.ToString(), AppTheme.SidebarActive),
        };
        int x = 0;
        foreach (var (lbl, val, color) in kpis)
        {
            var card = MakeCard(lbl, val, color);
            card.Location = new Point(x, 4);
            _pnlKpis.Controls.Add(card);
            x += 150;
        }
    }

    private void Exportar(object? s, EventArgs e)
    {
        if (_grid.Rows.Count == 0) { MessageBox.Show("No hay datos para exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"cumplimiento_sla_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var lines = new List<string> { "Grupo,Cumplidos,Vencidos,EnCurso,Cancelados,Total,PorcentajeCumplimiento" };
        foreach (DataGridViewRow r in _grid.Rows)
            lines.Add($"{Csv(r.Cells["Grupo"].Value)},{r.Cells["Cumpl"].Value},{r.Cells["Venc"].Value},{r.Cells["Curso"].Value},{r.Cells["Canc"].Value},{r.Cells["Total"].Value},{Csv(r.Cells["Pct"].Value)}");
        File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
        MessageBox.Show("Exportado.", "CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Cargar(); }

    /// <summary>Campo CSV entre comillas, duplicando las comillas internas (RFC 4180).</summary>
    private static string Csv(object? v) => $"\"{(v?.ToString() ?? "").Replace("\"", "\"\"")}\"";

    private static Color ColorPct(double pct) => pct >= 90 ? AppTheme.Success : pct >= 70 ? AppTheme.Warning : AppTheme.Danger;

    private static DataGridViewTextBoxColumn Num(string header, string name) => new()
    {
        HeaderText = header, Name = name, FillWeight = 12,
        DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
    };

    private static Panel MakeCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 140, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label { Text = value, Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = accent, Location = new Point(12, 8), Size = new Size(120, 36), AutoEllipsis = true });
        card.Controls.Add(new Label { Text = label, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Location = new Point(12, 50), Size = new Size(124, 20), AutoEllipsis = true });
        return card;
    }
}
