using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// «Mi jornada»: la asistencia PROPIA, agrupada por día y con el total del rango.
///
/// De cada día se ven dos cosas distintas y en ese orden: primero lo OFICIAL —la entrada y la
/// salida que la persona marcó a mano, que es lo que cuenta como asistencia— y debajo lo que la
/// aplicación registró sola por el latido, que sirve de contraste.
///
/// Existe para que todo esto sea transparente y no unilateral: el administrador ya veía estas horas
/// en «Quién está»; que cada quien vea las suyas convierte el mismo dato en algo que ambos pueden
/// mirar juntos, y ahorra la discusión de «yo sí estuve».
///
/// Lo que NO se muestra, a propósito: el estado (comiendo, descanso). No se historiza — un
/// registro minutado de las pausas de alguien es vigilancia, no asistencia.
/// </summary>
public class MyPresenceControl : UserControl
{
    private readonly PresenceService _presence;
    private readonly AttendanceService _attendance;

    private DateTimePicker _dtpDesde = null!, _dtpHasta = null!;
    private DataGridView _grid = null!;
    private Label _lblResumen = null!;
    /// <summary>Mientras se mueven las dos fechas a la vez, sus eventos no recargan.</summary>
    private bool _suspendido;

    /// <summary>Id del registro oficial de cada fila que lo tenga, para «Solicitar corrección».</summary>
    private readonly Dictionary<int, int> _oficialPorFila = [];

    public MyPresenceControl(PresenceService presence, AttendanceService attendance)
    {
        _presence = presence; _attendance = attendance;
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

        // Corregir no es algo que cada quien pueda hacer sobre sus propias horas —si pudiera, el
        // registro no probaría nada—, pero sí puede decirlo aquí y que quede constancia.
        var btnCorreccion = AppTheme.MakeSecondaryButton("🙋 Solicitar corrección", 190, 28);
        btnCorreccion.Margin = new Padding(12, 3, 0, 0);
        btnCorreccion.Click += BtnSolicitarCorreccion_Click;
        barra.Controls.Add(btnCorreccion);

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
        List<AttendanceRecord> oficiales;
        try
        {
            jornadas = _presence.MisJornadas(desde, hasta);
            oficiales = _attendance.MisRegistros(desde, hasta);
        }
        catch (AuthorizationException ex) { _lblResumen.Text = ex.Message; return; }
        catch (Exception ex)
        {
            _lblResumen.Text = $"No se pudo leer tu registro ({ex.Message}).";
            return;
        }

        _grid.Rows.Clear();
        _oficialPorFila.Clear();

        // Agrupado por día LOCAL, no UTC: una jornada que empieza a las 19:00 caería en el día
        // siguiente si se agrupara por la fecha UTC, y el total por día mentiría.
        // Uno por día es la regla; si el líder dio de alta alguno a mano y hay dos, se muestra el
        // primero (cuando empezó la jornada de verdad). El total del pie usa EXACTAMENTE los que se
        // muestran: sumar los ocultos daría un número que no cuadra con lo que se ve en la tabla.
        var oficialPorDia = oficiales
            .GroupBy(a => a.CheckInUtc.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CheckInUtc).First());

        var porDia = jornadas
            .GroupBy(j => j.InicioUtc.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Los días con marca pero sin telemetría también salen: trabajar sin abrir la aplicación es
        // legítimo, y si el día desapareciera de la lista parecería que no se marcó nada.
        var dias = porDia.Keys.Union(oficialPorDia.Keys).OrderByDescending(d => d).ToList();

        foreach (var dia in dias)
        {
            var delDia = porDia.TryGetValue(dia, out var lista) ? lista : [];
            var totalDia = TimeSpan.FromTicks(delDia.Sum(j => j.Duracion.Ticks));
            int cab = _grid.Rows.Add($"{dia:dddd dd/MM}", "", "", PresenceService.Duracion(totalDia), "", "");
            _grid.Rows[cab].DefaultCellStyle.Font = AppTheme.BoldFont;
            _grid.Rows[cab].DefaultCellStyle.BackColor = AppTheme.GridAlt;
            _grid.Rows[cab].DefaultCellStyle.SelectionBackColor = AppTheme.GridAlt;
            _grid.Rows[cab].DefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;

            // La fila OFICIAL va primero: es la que cuenta. Lo de abajo es el contraste.
            if (oficialPorDia.TryGetValue(dia, out var oficial))
            {
                int io = _grid.Rows.Add(
                    "OFICIAL",
                    oficial.CheckInUtc.ToLocalTime().ToString("HH:mm"),
                    oficial.CheckOutUtc is DateTime fo ? fo.ToLocalTime().ToString("HH:mm") : "— sin marcar",
                    oficial.Duracion is TimeSpan d ? PresenceService.Duracion(d) : "",
                    AttendanceService.EtiquetaCierre(oficial.CloseKind) +
                        (oficial.CorrectionRequestedAtUtc != null ? "  🙋 pediste corrección" : ""),
                    oficial.CheckInOrigin ?? "");

                _oficialPorFila[io] = oficial.Id;
                var estilo = _grid.Rows[io].DefaultCellStyle;
                estilo.Font = AppTheme.BoldFont;
                var color = oficial.CloseKind == AttendanceCloseKind.Olvido ? AppTheme.Warning : AppTheme.Success;
                estilo.ForeColor = color;
                estilo.SelectionForeColor = color;
            }
            else
            {
                int isin = _grid.Rows.Add("OFICIAL", "", "", "", "⚠ No marcaste ese día", "");
                var estilo = _grid.Rows[isin].DefaultCellStyle;
                estilo.Font = AppTheme.BoldFont;
                estilo.ForeColor = AppTheme.Warning;
                estilo.SelectionForeColor = AppTheme.Warning;
            }

            foreach (var j in delDia.OrderBy(x => x.InicioUtc))
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

        var mostrados = oficialPorDia.Values.ToList();
        var totalAuto = TimeSpan.FromTicks(jornadas.Sum(j => j.Duracion.Ticks));
        var totalOficial = TimeSpan.FromTicks(mostrados.Sum(a => (a.Duracion ?? TimeSpan.Zero).Ticks));
        int caidas = jornadas.Count(j => j.Cierre == PresenceEnd.SinLatido);
        int olvidos = mostrados.Count(a => a.CloseKind == AttendanceCloseKind.Olvido);
        int sinMarcar = dias.Count(d => !oficialPorDia.ContainsKey(d));

        if (dias.Count == 0)
        {
            _lblResumen.Text = "No hay nada tuyo en ese rango.";
            return;
        }

        // Los dos totales se muestran por separado y nunca sumados: miden cosas distintas, y
        // juntarlos daría un número que no significa nada.
        _lblResumen.Text =
            $"Oficial: {PresenceService.Duracion(totalOficial)} en {mostrados.Count} día(s) marcados  ·  " +
            $"Registro automático: {PresenceService.Duracion(totalAuto)} en {jornadas.Count} jornada(s)." +
            (sinMarcar > 0 ? $"  ⚠ {sinMarcar} día(s) sin marcar." : "") +
            (olvidos > 0
                ? $"  ⚠ {olvidos} sin salida marcada: la hora es una estimación, pide corrección si no cuadra."
                : "") +
            (caidas > 0
                ? $"  ⚠ {caidas} jornada(s) cerraron sin señales (la aplicación dejó de responder), " +
                  "no son una hora real de salida."
                : "");
    }

    private void BtnSolicitarCorreccion_Click(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow is not { Index: >= 0 } fila || !_oficialPorFila.TryGetValue(fila.Index, out int registroId))
        {
            MessageBox.Show(
                "Selecciona la fila OFICIAL del día que quieres corregir.\n\n" +
                "Si ese día no marcaste nada, díselo a tu líder: él puede darlo de alta.",
                "Elige un registro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var motivo = EntradaDeTextoSimple.Pedir(FindForm(), "Solicitar corrección",
            "¿Qué habría que corregir? Es lo que va a leer tu líder.",
            maxLength: AttendanceService.MaxMotivo);
        if (string.IsNullOrWhiteSpace(motivo)) return;

        try
        {
            var (ok, mensaje) = _attendance.SolicitarCorreccion(registroId, motivo);
            MessageBox.Show(mensaje, ok ? "Enviada" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok) LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadData();
    }
}
