using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// «Mi jornada»: el registro de asistencia PROPIO, agrupado por día y con el total del rango.
///
/// Existe para que la presencia sea transparente y no unilateral: el administrador ya veía estas
/// horas en «Quién está»; que cada quien vea las suyas convierte el mismo dato en algo que ambos
/// pueden mirar juntos, y ahorra la discusión de «yo sí estuve conectado».
///
/// Lo que NO se muestra, a propósito: el estado (comiendo, descanso). No se historiza — un
/// registro minutado de las pausas de alguien es vigilancia, no asistencia.
/// </summary>
public class MyPresenceControl : UserControl
{
    private readonly PresenceService _presence;

    private DateTimePicker _dtpDesde = null!, _dtpHasta = null!;
    private DataGridView _grid = null!;
    private Label _lblResumen = null!;
    /// <summary>Mientras se mueven las dos fechas a la vez, sus eventos no recargan.</summary>
    private bool _suspendido;

    public MyPresenceControl(PresenceService presence)
    {
        _presence = presence;
        BuildUI();
        EstaSemana();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 6, 10, 2), BackColor = AppTheme.ContentBg
        };
        barra.Controls.Add(new Label { Text = "Del:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _dtpDesde = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 3, 8, 0) };
        _dtpDesde.ValueChanged += Recargar;
        barra.Controls.Add(_dtpDesde);

        barra.Controls.Add(new Label { Text = "al:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _dtpHasta = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 3, 12, 0) };
        _dtpHasta.ValueChanged += Recargar;
        barra.Controls.Add(_dtpHasta);

        // Botones de rango: además de la comodidad, acotan por interfaz lo que se trae a memoria.
        barra.Controls.Add(Rapido("Hoy", () => Rango(DateTime.Today, DateTime.Today)));
        barra.Controls.Add(Rapido("Esta semana", EstaSemana));
        barra.Controls.Add(Rapido("Este mes", () =>
        {
            var primero = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Rango(primero, DateTime.Today);
        }));

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Día",       Name = "Dia",      FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entrada",   Name = "Entrada",  FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Salida",    Name = "Salida",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración",  Name = "Duracion", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cierre",    Name = "Cierre",   FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipo",    Name = "Equipo",   FillWeight = 18 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(_grid);

        _lblResumen = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0)
        };

        tbl.Controls.Add(barra,       0, 0);
        tbl.Controls.Add(pnl,         0, 1);
        tbl.Controls.Add(_lblResumen, 0, 2);
        Controls.Add(tbl);
    }

    private static Button Rapido(string texto, Action accion)
    {
        var b = AppTheme.MakeSecondaryButton(texto, 110, 28);
        b.Margin = new Padding(0, 3, 6, 0);
        b.Click += (_, _) => accion();
        return b;
    }

    /// <summary>De lunes a hoy. Es el rango por omisión: la pregunta habitual es «¿cómo voy esta semana?».</summary>
    private void EstaSemana()
    {
        int desdeElLunes = ((int)DateTime.Today.DayOfWeek + 6) % 7;   // domingo = 6, no 0
        Rango(DateTime.Today.AddDays(-desdeElLunes), DateTime.Today);
    }

    private void Rango(DateTime desde, DateTime hasta)
    {
        // Las dos fechas se mueven como UNA sola operación. Con una bandera y no desenganchando
        // el manejador: mover solo «del» dispararía una consulta con el «al» todavía viejo, y en
        // los rangos hacia atrás eso pinta por un instante «el al es anterior al del».
        _suspendido = true;
        try { _dtpDesde.Value = desde; _dtpHasta.Value = hasta; }
        finally { _suspendido = false; }
        LoadData();
    }

    private void Recargar(object? s, EventArgs e) { if (!_suspendido) LoadData(); }

    private void LoadData()
    {
        var desde = _dtpDesde.Value.Date;
        var hasta = _dtpHasta.Value.Date;
        if (hasta < desde)
        {
            _grid.Rows.Clear();
            _lblResumen.Text = "El «al» es anterior al «del»: corrige el rango.";
            return;
        }

        List<MiJornada> jornadas;
        try { jornadas = _presence.MisJornadas(desde, hasta); }
        catch (AuthorizationException ex) { _lblResumen.Text = ex.Message; return; }
        catch (Exception ex)
        {
            _lblResumen.Text = $"No se pudo leer tu registro ({ex.Message}).";
            return;
        }

        _grid.Rows.Clear();

        // Agrupado por día LOCAL, no UTC: una jornada que empieza a las 19:00 caería en el día
        // siguiente si se agrupara por la fecha UTC, y el total por día mentiría.
        var porDia = jornadas
            .GroupBy(j => j.InicioUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var dia in porDia)
        {
            var totalDia = TimeSpan.FromTicks(dia.Sum(j => j.Duracion.Ticks));
            int cab = _grid.Rows.Add($"{dia.Key:dddd dd/MM}", "", "", PresenceService.Duracion(totalDia), "", "");
            _grid.Rows[cab].DefaultCellStyle.Font = AppTheme.BoldFont;
            _grid.Rows[cab].DefaultCellStyle.BackColor = AppTheme.GridAlt;
            _grid.Rows[cab].DefaultCellStyle.SelectionBackColor = AppTheme.GridAlt;
            _grid.Rows[cab].DefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;

            foreach (var j in dia.OrderBy(x => x.InicioUtc))
            {
                int i = _grid.Rows.Add(
                    "",
                    j.InicioUtc.ToLocalTime().ToString("HH:mm"),
                    j.FinUtc is DateTime f ? f.ToLocalTime().ToString("HH:mm") : "— en curso",
                    PresenceService.Duracion(j.Duracion),
                    j.Cierre switch
                    {
                        PresenceEnd.CierreNormal => "Cerraste sesión",
                        PresenceEnd.SinLatido    => "⚠ Sin señales",
                        _                        => "En curso"
                    },
                    j.Equipo ?? "");

                if (j.Cierre == PresenceEnd.SinLatido)
                {
                    var c = _grid.Rows[i].Cells["Cierre"].Style;
                    c.ForeColor = AppTheme.Warning; c.SelectionForeColor = AppTheme.Warning;
                }
                if (j.Cierre == null)
                {
                    var c = _grid.Rows[i].Cells["Salida"].Style;
                    c.ForeColor = AppTheme.Success; c.SelectionForeColor = AppTheme.Success;
                }
            }
        }

        var total = TimeSpan.FromTicks(jornadas.Sum(j => j.Duracion.Ticks));
        int caidas = jornadas.Count(j => j.Cierre == PresenceEnd.SinLatido);
        _lblResumen.Text = jornadas.Count == 0
            ? "No hay jornadas tuyas en ese rango."
            : $"{jornadas.Count} jornada(s) en {porDia.Count()} día(s)  ·  {PresenceService.Duracion(total)} en total." +
              (caidas > 0
                  ? $"  ⚠ {caidas} cerró sin señales: en esas, la salida es tu última señal (hasta " +
                    $"{PresenceService.ToleranciaSinLatido.TotalMinutes:0} min menos de lo trabajado), no una hora real de salida."
                  : "");
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadData();
    }
}
