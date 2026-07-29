using System.Diagnostics;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Muestra el detalle de puntos (tipo "calificaciones") de un desarrollador en un
/// período: cada criterio con sus puntos, fecha, requerimiento, captura y comentario.
/// </summary>
public class DeveloperPointsDetailForm : Form
{
    private static readonly string[] Months =
        ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];

    private readonly List<PointEntry> _entries;
    private DataGridView _grid = null!;

    public DeveloperPointsDetailForm(string devName, int month, int year, int total, int pos, int neg, List<PointEntry> entries)
    {
        _entries = entries;
        BuildUI(devName, month, year, total, pos, neg);
        LoadData();
    }

    private void BuildUI(string devName, int month, int year, int total, int pos, int neg)
    {
        Text = $"Detalle de puntos — {devName}";
        Size = new Size(880, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MinimumSize = new Size(720, 380);
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  📊  Calificaciones — {devName}", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(12, 8, 10, 5), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280f));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        string monthName = month >= 1 && month <= 12 ? Months[month - 1] : month.ToString();
        var lblSummary = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Text = $"{monthName} {year}   ·   Total {(total >= 0 ? "+" : "")}{total} pts   ·   premio +{pos} / penaliz {neg}   ·   {_entries.Count} entrada(s)",
            Font = AppTheme.BoldFont,
            ForeColor = total > 0 ? AppTheme.Success : total < 0 ? AppTheme.Danger : AppTheme.TextPrimary
        };
        bar.Controls.Add(lblSummary, 0, 0);

        var barBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var btnClose = AppTheme.MakeSecondaryButton("Cerrar", 100); btnClose.Margin = new Padding(4, 1, 0, 0); btnClose.Click += (_, _) => Close();
        var btnShot  = AppTheme.MakeSecondaryButton("👁 Ver captura", 140); btnShot.Margin = new Padding(4, 1, 0, 0); btnShot.Click += BtnViewShot_Click;
        barBtns.Controls.AddRange([btnClose, btnShot]);
        bar.Controls.Add(barBtns, 1, 0);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Criterio",      Name = "Crit",    FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción",   Name = "Desc",    FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",        Name = "Pts",     FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",         Name = "Date",    FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requerimiento", Name = "Req",     FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📷",            Name = "Shot",    FillWeight = 4  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentario",    Name = "Comment", FillWeight = 21 });
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnViewShot_Click(null, EventArgs.Empty); };

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(bar,     0, 1);
        outer.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnClose;
    }

    private void LoadData()
    {
        _grid.Rows.Clear();
        foreach (var pe in _entries)
        {
            int i = _grid.Rows.Add(pe.Id, pe.Criterion.Name, pe.Criterion.Description ?? "",
                (pe.Points >= 0 ? "+" : "") + pe.Points,
                pe.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                pe.Requirement != null ? $"#{pe.Requirement.Id} {pe.Requirement.Title}" : "—",
                pe.Screenshot != null ? "📷" : "",
                pe.Comment ?? "");
            _grid.Rows[i].Cells["Pts"].Style.ForeColor = pe.Points >= 0 ? AppTheme.Success : AppTheme.Danger;
            _grid.Rows[i].Cells["Pts"].Style.Font = AppTheme.BoldFont;
        }
        if (_entries.Count == 0)
            _grid.Rows.Add("", "(sin puntos en este período)", "", "", "", "", "", "");
    }

    private void BtnViewShot_Click(object? s, EventArgs e)
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una entrada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var pe = _entries.FirstOrDefault(p => p.Id == id);
        if (pe?.Screenshot == null || pe.Screenshot.Length == 0)
        { MessageBox.Show("Esta entrada no tiene captura.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrWhiteSpace(pe.ScreenshotFileName) ? "captura.png" : pe.ScreenshotFileName!;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, pe.Screenshot);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
