using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Dashboard por TAG de los tickets de DevOps: qué cliente/categoría tiene más bugs, tareas,
/// solicitudes, etc. Se apoya en los tags del work item (cliente «Bepensa», tipo «Bug»…) sobre los
/// tickets YA sincronizados. Puedes «entrar» a un tag para ver el desglose dentro de él.
/// </summary>
public class DevOpsTagsDashboardControl : UserControl
{
    private readonly AppDbContext _db;

    private ComboBox _cbxTag = null!, _cbxOrden = null!;
    private CheckBox _chkAbiertos = null!;
    private TextBox _txtBuscar = null!;
    private DataGridView _grid = null!;
    private Panel _pnlKpis = null!;
    private Label _lblStatus = null!;

    private List<TagStatInput> _tickets = [];
    private List<TagRow> _rows = [];

    public DevOpsTagsDashboardControl(AppDbContext db)
    {
        _db = db;
        BuildUI();
        CargarTickets();
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
        bar.Controls.Add(new Label { Text = "Entrar al tag:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbxTag = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbxTag.SelectedIndexChanged += (_, _) => Recalcular();
        bar.Controls.Add(_cbxTag);
        _chkAbiertos = new CheckBox { Text = "Solo abiertos", AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
        _chkAbiertos.CheckedChanged += (_, _) => Recalcular();
        bar.Controls.Add(_chkAbiertos);
        bar.Controls.Add(new Label { Text = "Ordenar por:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbxOrden = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 12, 0) };
        _cbxOrden.Items.AddRange(["Total", "Bugs", "Tareas", "User Stories", "Abiertos"]);
        _cbxOrden.SelectedIndex = 0;
        _cbxOrden.SelectedIndexChanged += (_, _) => Pintar();
        bar.Controls.Add(_cbxOrden);
        _txtBuscar = new TextBox { Width = 160, PlaceholderText = "Buscar tag…", Margin = new Padding(0, 4, 12, 0) };
        _txtBuscar.TextChanged += (_, _) => Pintar();
        bar.Controls.Add(_txtBuscar);
        var bExcel = AppTheme.MakeSecondaryButton("⬇ CSV", 90, 26); bExcel.Margin = new Padding(0, 2, 6, 0); bExcel.Click += Exportar;
        var bReload = AppTheme.MakeSecondaryButton("🔄 Recargar", 110, 26); bReload.Margin = new Padding(0, 2, 0, 0); bReload.Click += (_, _) => CargarTickets();
        bar.Controls.AddRange([bExcel, bReload]);

        _pnlKpis = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        _lblStatus = new Label { Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };

        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add(Col("Tag", "Tag", 240));
        _grid.Columns.Add(Num("Tickets", "Total"));
        _grid.Columns.Add(Num("Abiertos", "Abiertos"));
        _grid.Columns.Add(Num("Bugs", "Bugs"));
        _grid.Columns.Add(Num("Tareas", "Tareas"));
        _grid.Columns.Add(Num("User Stories", "US"));
        _grid.Columns.Add(Num("Otros", "Otros"));
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 10) };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(bar, 0, 0);
        tbl.Controls.Add(_pnlKpis, 0, 1);
        tbl.Controls.Add(_lblStatus, 0, 2);
        tbl.Controls.Add(pnlGrid, 0, 3);
        Controls.Add(tbl);
    }

    private void CargarTickets()
    {
        _tickets = _db.DevOpsTickets
            .Select(t => new { t.Tags, t.WorkItemType, t.State })
            .AsEnumerable()
            .Select(t => new TagStatInput(t.Tags, t.WorkItemType, t.State))
            .ToList();

        var tags = DevOpsTagStats.TagsDistintos(_tickets);
        var sel = _cbxTag.SelectedItem as string;
        _cbxTag.Items.Clear();
        _cbxTag.Items.Add("(todos los tags)");
        foreach (var t in tags) _cbxTag.Items.Add(t);
        _cbxTag.SelectedIndex = Math.Max(0, sel == null ? 0 : _cbxTag.Items.IndexOf(sel));

        Recalcular();
    }

    private string? TagFiltro => _cbxTag.SelectedIndex <= 0 ? null : _cbxTag.SelectedItem as string;

    private void Recalcular()
    {
        _rows = DevOpsTagStats.Aggregate(_tickets, TagFiltro, _chkAbiertos.Checked);
        Pintar();
    }

    private void Pintar()
    {
        var q = _txtBuscar.Text.Trim();
        var rows = string.IsNullOrEmpty(q) ? _rows
            : _rows.Where(r => r.Tag.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        rows = (_cbxOrden.SelectedIndex switch
        {
            1 => rows.OrderByDescending(r => r.Bugs),
            2 => rows.OrderByDescending(r => r.Tareas),
            3 => rows.OrderByDescending(r => r.UserStories),
            4 => rows.OrderByDescending(r => r.Abiertos),
            _ => rows.OrderByDescending(r => r.Total)
        }).ThenBy(r => r.Tag, StringComparer.OrdinalIgnoreCase).ToList();

        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = _grid.Rows.Add(r.Tag, r.Total, r.Abiertos, r.Bugs, r.Tareas, r.UserStories, r.Otros);
            if (r.Bugs > 0) _grid.Rows[i].Cells["Bugs"].Style.ForeColor = AppTheme.Danger;
        }

        BuildKpis();
        _lblStatus.Text = TagFiltro == null
            ? $"{rows.Count} tag(s) sobre {_tickets.Count} tickets sincronizados."
            : $"Dentro de «{TagFiltro}»: {rows.Count} tag(s) que coexisten.";
    }

    private void BuildKpis()
    {
        _pnlKpis.Controls.Clear();
        // KPIs del subconjunto actual (respetando «entrar al tag» y «solo abiertos»).
        var sub = _tickets.Where(t =>
            (!_chkAbiertos.Checked || !DevOpsTagStats.EsCerrado(t.State)) &&
            (TagFiltro == null || DevOpsTagStats.SplitTags(t.Tags).Any(x => x.Equals(TagFiltro, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        int bugs = 0, tareas = 0, us = 0;
        foreach (var t in sub)
        {
            var ty = (t.WorkItemType ?? "").ToLowerInvariant();
            var tags = DevOpsTagStats.SplitTags(t.Tags);
            bool esBug = ty.Contains("bug") || tags.Any(x => x.Equals("bug", StringComparison.OrdinalIgnoreCase));
            bool esTarea = ty.Contains("task") || ty.Contains("tarea") || tags.Any(x => x.Equals("tarea", StringComparison.OrdinalIgnoreCase) || x.Equals("task", StringComparison.OrdinalIgnoreCase));
            bool esUs = ty.Contains("story") || ty.Contains("backlog") || ty.Contains("historia");
            if (esBug) bugs++; else if (esTarea) tareas++; else if (esUs) us++;
        }

        var kpis = new (string, string, Color)[]
        {
            (TagFiltro == null ? "Tickets" : $"Tickets «{TagFiltro}»", sub.Count.ToString(), AppTheme.SidebarActive),
            ("Bugs", bugs.ToString(), AppTheme.Danger),
            ("Tareas", tareas.ToString(), AppTheme.Warning),
            ("User Stories", us.ToString(), Color.FromArgb(139, 92, 246)),
            ("Tags distintos", _rows.Count.ToString(), AppTheme.TextSecondary),
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
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"devops_tags_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var lines = new List<string> { "Tag,Tickets,Abiertos,Bugs,Tareas,UserStories,Otros" };
        foreach (DataGridViewRow r in _grid.Rows)
            lines.Add($"\"{r.Cells["Tag"].Value}\",{r.Cells["Total"].Value},{r.Cells["Abiertos"].Value},{r.Cells["Bugs"].Value},{r.Cells["Tareas"].Value},{r.Cells["US"].Value},{r.Cells["Otros"].Value}");
        File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
        MessageBox.Show("Exportado.", "CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) CargarTickets(); }

    private static DataGridViewTextBoxColumn Col(string header, string name, int w) => new() { HeaderText = header, Name = name, Width = w };
    private static DataGridViewTextBoxColumn Num(string header, string name) => new() { HeaderText = header, Name = name, FillWeight = 12, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } };

    private static Panel MakeCard(string label, string value, Color accent)
    {
        var card = new Panel { Width = 140, Height = 78, BackColor = Color.White };
        card.Paint += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(accent), 0, 0, 4, card.Height);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label { Text = value, Font = new Font("Segoe UI", 22f, FontStyle.Bold), ForeColor = accent, Location = new Point(12, 8), Size = new Size(120, 36), AutoEllipsis = true });
        card.Controls.Add(new Label { Text = label, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Location = new Point(12, 50), Size = new Size(124, 20), AutoEllipsis = true });
        return card;
    }
}
