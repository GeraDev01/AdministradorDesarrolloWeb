using System.Globalization;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Reporte de tiempo por día: cuánto se dedicó a cada requerimiento/actividad en cada fecha, a
/// partir de los tramos registrados. Exacto por día aunque una sesión abarque varios.
/// </summary>
public class TiempoPorDiaForm : Form
{
    private readonly WorkSessionService _work;
    private readonly int _devId;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    private DateTimePicker _desde = null!, _hasta = null!;
    private DataGridView _grid = null!;
    private Label _lblTotal = null!;

    public TiempoPorDiaForm(WorkSessionService work, int devId)
    {
        _work = work; _devId = devId;
        BuildUI();
        Cargar();
    }

    private void BuildUI()
    {
        Text = "Tiempo por día";
        Size = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        bar.Controls.Add(new Label { Text = "Desde:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _desde = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-29) };
        _desde.ValueChanged += (_, _) => Cargar();
        bar.Controls.Add(_desde);
        bar.Controls.Add(new Label { Text = "Hasta:", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
        _hasta = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
        _hasta.ValueChanged += (_, _) => Cargar();
        bar.Controls.Add(_hasta);
        var btnHoy = AppTheme.MakeSecondaryButton("Hoy", 70, 26); btnHoy.Margin = new Padding(12, 2, 0, 0);
        btnHoy.Click += (_, _) => { _desde.Value = DateTime.Today; _hasta.Value = DateTime.Today; };
        var btnMes = AppTheme.MakeSecondaryButton("Últimos 30 días", 140, 26); btnMes.Margin = new Padding(6, 2, 0, 0);
        btnMes.Click += (_, _) => { _desde.Value = DateTime.Today.AddDays(-29); _hasta.Value = DateTime.Today; };
        bar.Controls.AddRange([btnHoy, btnMes]);
        root.Controls.Add(bar, 0, 0);

        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Día",    Name = "Day",  FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item",   Name = "Item", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tiempo", Name = "Time", FillWeight = 19 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 4) };
        pnl.Controls.Add(_grid);
        root.Controls.Add(pnl, 0, 1);

        _lblTotal = new Label { Dock = DockStyle.Fill, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(_lblTotal, 0, 2);

        Controls.Add(root);
    }

    private void Cargar()
    {
        _grid.Rows.Clear();
        if (_desde.Value.Date > _hasta.Value.Date) { _lblTotal.Text = "El rango de fechas está invertido."; return; }

        var filas = _work.ResumenPorDia(_devId, _desde.Value, _hasta.Value);
        int granTotal = 0;
        foreach (var dia in filas.GroupBy(f => f.Date).OrderByDescending(g => g.Key))
        {
            int totalDia = dia.Sum(f => f.Seconds);
            granTotal += totalDia;

            int h = _grid.Rows.Add($"📅 {dia.Key:dd/MM/yyyy} ({Es.DateTimeFormat.GetAbbreviatedDayName(dia.Key.DayOfWeek)})",
                "", WorkSessionService.Format(totalDia));
            _grid.Rows[h].DefaultCellStyle.Font = AppTheme.BoldFont;
            _grid.Rows[h].DefaultCellStyle.BackColor = Color.FromArgb(238, 242, 248);

            foreach (var f in dia.OrderByDescending(x => x.Seconds))
                _grid.Rows.Add("", f.Target, WorkSessionService.Format(f.Seconds));
        }

        if (filas.Count == 0)
        {
            _grid.Rows.Add("(sin tiempo registrado en este rango)", "", "");
            _lblTotal.Text = "Total del rango: 00m 00s";
        }
        else
            _lblTotal.Text = $"Total del rango ({_desde.Value:dd/MM/yyyy} – {_hasta.Value:dd/MM/yyyy}): {WorkSessionService.Format(granTotal)}";
    }
}
