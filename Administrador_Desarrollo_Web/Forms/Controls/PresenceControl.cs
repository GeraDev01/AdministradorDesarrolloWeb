using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del ADMINISTRADOR: quién tiene la aplicación abierta ahora mismo y en qué anda, el
/// registro de jornadas por día, y la asistencia oficial que cada quien marcó a mano.
///
/// Las dos últimas pestañas miden cosas distintas y por eso están separadas. La jornada es lo que la
/// aplicación puede ver sola (un LATIDO, no el par inicio/cierre de sesión: cerrar con la X la deja
/// viva en la bandeja, y eso es estar conectado). La asistencia la declara la persona. Ninguna se
/// deduce de la otra: es justamente el contraste entre ambas lo que delata un olvido.
///
/// Los estados (comiendo, en un descanso…) son <b>del momento</b>: se ven aquí en vivo y no se
/// guardan minutados. Es deliberado — un histórico de las pausas de cada quien es vigilancia, no
/// asistencia. Lo que sí queda es la jornada: a qué hora entró y a qué hora salió.
/// </summary>
public class PresenceControl : UserControl
{
    private readonly PresenceService _presence;
    private readonly AttendanceService _attendance;
    private readonly ReportService _reports;

    private DataGridView _gridAhora = null!, _gridJornadas = null!, _gridAsistencia = null!;
    private DateTimePicker _dtpDia = null!, _dtpDiaAsistencia = null!;
    private Label _lblAhora = null!, _lblJornadas = null!, _lblAsistencia = null!;
    private System.Windows.Forms.Timer? _refresco;

    /// <summary>Las jornadas tal como están pintadas, para reconocer la fila seleccionada por su
    /// Id cuando el refresco de 30 s repuebla el grid.</summary>
    private List<WorkPresence> _jornadas = [];

    /// <summary>Lo mismo para la asistencia: el refresco no debe perder la fila seleccionada.</summary>
    private List<AsistenciaDelDiaFila> _asistencia = [];

    private const string ClaveColumnasAhora = "presence.ahora";
    private const string ClaveColumnasJornadas = "presence.jornadas";
    private const string ClaveColumnasAsistencia = "presence.asistencia";

    public PresenceControl(PresenceService presence, AttendanceService attendance, ReportService reports)
    {
        _presence = presence; _attendance = attendance; _reports = reports;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildAhoraTab());
        // La asistencia va antes que la jornada automática: es el registro oficial, lo que se
        // consulta a diario. La jornada es el contraste, se mira cuando algo no cuadra.
        tabs.TabPages.Add(BuildAsistenciaTab());
        tabs.TabPages.Add(BuildJornadasTab());
        Controls.Add(tabs);
    }

    // ── Quién está ahora ─────────────────────────────────────────────────────────
    private TabPage BuildAhoraTab()
    {
        var page = new TabPage("  🟢  Quién está  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg
        };
        var btnRefrescar = AppTheme.MakeSecondaryButton("🔄 Actualizar", 130);
        btnRefrescar.Margin = new Padding(0, 0, 8, 0);
        btnRefrescar.Click += (_, _) => LoadAhora();
        toolbar.Controls.Add(btnRefrescar);

        _gridAhora = AppTheme.MakeGrid();
        _gridAhora.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "",        Name = "Punto",   FillWeight = 5 });
        _gridAhora.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Persona", Name = "Persona", FillWeight = 30 });
        _gridAhora.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",  Name = "Estado",  FillWeight = 22 });
        _gridAhora.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nota",    Name = "Nota",    FillWeight = 25 });
        _gridAhora.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desde",   Name = "Desde",   FillWeight = 18 });
        foreach (DataGridViewColumn c in _gridAhora.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        GridColumns.Habilitar(_gridAhora, ClaveColumnasAhora);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridAhora, ClaveColumnasAhora));

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(_gridAhora);

        _lblAhora = NuevaLineaEstado();

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(pnl,       0, 1);
        tbl.Controls.Add(_lblAhora, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Registro de jornadas ─────────────────────────────────────────────────────
    private TabPage BuildJornadasTab()
    {
        var page = new TabPage("  📋  Registro de jornadas  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg
        };

        toolbar.Controls.Add(new Label { Text = "Día:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _dtpDia = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Short, Value = DateTime.Today, Margin = new Padding(0, 3, 8, 0) };
        _dtpDia.ValueChanged += (_, _) => LoadJornadas();

        var btnHoy = AppTheme.MakeSecondaryButton("Hoy", 70, 28);
        btnHoy.Margin = new Padding(0, 3, 8, 0);
        btnHoy.Click += (_, _) => _dtpDia.Value = DateTime.Today;

        toolbar.Controls.AddRange([_dtpDia, btnHoy]);

        _gridJornadas = AppTheme.MakeGrid();
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Persona",  Name = "Persona", FillWeight = 28 });
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entrada",  Name = "Entrada", FillWeight = 13 });
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Salida",   Name = "Salida",  FillWeight = 13 });
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración", Name = "Duracion", FillWeight = 15 });
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cierre",   Name = "Cierre",  FillWeight = 16 });
        _gridJornadas.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipo",   Name = "Origen",  FillWeight = 20 });
        foreach (DataGridViewColumn c in _gridJornadas.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        GridColumns.Habilitar(_gridJornadas, ClaveColumnasJornadas);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridJornadas, ClaveColumnasJornadas));

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(_gridJornadas);

        _lblJornadas = NuevaLineaEstado();

        tbl.Controls.Add(toolbar,      0, 0);
        tbl.Controls.Add(pnl,          0, 1);
        tbl.Controls.Add(_lblJornadas, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Asistencia oficial ───────────────────────────────────────────────────────
    private TabPage BuildAsistenciaTab()
    {
        var page = new TabPage("  🕘  Asistencia (oficial)  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg
        };

        toolbar.Controls.Add(new Label { Text = "Día:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _dtpDiaAsistencia = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Short, Value = DateTime.Today, Margin = new Padding(0, 3, 8, 0) };
        _dtpDiaAsistencia.ValueChanged += (_, _) => LoadAsistencia();

        var btnHoy = AppTheme.MakeSecondaryButton("Hoy", 70, 28);
        btnHoy.Margin = new Padding(0, 3, 8, 0);
        btnHoy.Click += (_, _) => _dtpDiaAsistencia.Value = DateTime.Today;

        var btnCorregir = AppTheme.MakePrimaryButton("✏ Corregir…", 140, 28);
        btnCorregir.Margin = new Padding(0, 3, 6, 0);
        btnCorregir.Click += BtnCorregir_Click;

        var btnAlta = AppTheme.MakeSecondaryButton("➕ Día olvidado…", 160, 28);
        btnAlta.Margin = new Padding(0, 3, 6, 0);
        btnAlta.Click += BtnAlta_Click;

        var btnExportar = AppTheme.MakeSecondaryButton("📤 Exportar", 120, 28);
        btnExportar.Margin = new Padding(0, 3, 8, 0);
        btnExportar.Click += BtnExportarAsistencia_Click;

        toolbar.Controls.AddRange([_dtpDiaAsistencia, btnHoy, btnCorregir, btnAlta, btnExportar]);

        _gridAsistencia = AppTheme.MakeGrid();
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Persona",       Name = "Persona",   FillWeight = 22 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entrada",       Name = "Entrada",   FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Salida",        Name = "Salida",    FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Horas",         Name = "Horas",     FillWeight = 11 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Δ entrada",     Name = "DeltaEnt",  FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Δ salida",      Name = "DeltaSal",  FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "1ª señal app",  Name = "AutoIni",   FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Últ. señal app", Name = "AutoFin",  FillWeight = 10 });
        _gridAsistencia.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",        Name = "Estado",    FillWeight = 17 });
        foreach (DataGridViewColumn c in _gridAsistencia.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        GridColumns.Habilitar(_gridAsistencia, ClaveColumnasAsistencia);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridAsistencia, ClaveColumnasAsistencia));

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(_gridAsistencia);

        _lblAsistencia = NuevaLineaEstado();

        tbl.Controls.Add(toolbar,        0, 0);
        tbl.Controls.Add(pnl,            0, 1);
        tbl.Controls.Add(_lblAsistencia, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    private static Label NuevaLineaEstado() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0)
    };

    // ── Datos ────────────────────────────────────────────────────────────────────

    private void LoadData() { LoadAhora(); LoadAsistencia(); LoadJornadas(); }

    private void LoadAhora()
    {
        List<PresenciaDeUsuario> filas;
        try { filas = _presence.Tablero(); }
        catch (AuthorizationException ex) { _lblAhora.Text = ex.Message; return; }
        catch (Exception ex)
        {
            // Tablero() ahora también ESCRIBE (barre jornadas caídas): un fallo de red no debe
            // escalar al manejador global — el tick de 30 s apilaría un diálogo modal por intento.
            // Se deja el tablero anterior en pantalla y el motivo en la línea de estado.
            _lblAhora.Text = $"Sin conexión con la base ({ex.Message}). Se reintenta en 30 s.";
            return;
        }

        _gridAhora.Rows.Clear();
        foreach (var p in filas)
        {
            var desde = p.Conectado && p.DesdeUtc is DateTime d
                ? $"{d.ToLocalTime():HH:mm}  ({PresenceService.Duracion(DateTime.UtcNow - d)})"
                : p.UltimoLatidoUtc == DateTime.MinValue
                    ? "nunca ha entrado"
                    : $"visto {p.UltimoLatidoUtc.ToLocalTime():dd/MM HH:mm}";

            int i = _gridAhora.Rows.Add(
                p.Conectado ? PresenceService.Icono(p.Estado) : "⚪",
                p.Nombre,
                p.Conectado ? PresenceService.Etiqueta(p.Estado) : "Desconectado",
                p.Nota ?? "",
                desde);

            if (!p.Conectado) _gridAhora.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            else
            {
                _gridAhora.Rows[i].Cells["Persona"].Style.Font = AppTheme.BoldFont;

                // El color va en la celda «Estado» (texto plano) y NO en la del punto: el emoji se
                // dibuja con su propia paleta e ignora el ForeColor. El SelectionForeColor es
                // obligatorio: MakeGrid lo fija a TextPrimary y, sin él, el color desaparece justo
                // en la fila que el administrador acaba de seleccionar.
                var color = AppTheme.PresenceColor(p.Estado);
                var celda = _gridAhora.Rows[i].Cells["Estado"].Style;
                celda.ForeColor = color;
                celda.SelectionForeColor = color;
                celda.Font = AppTheme.BoldFont;
            }
        }

        int conectados = filas.Count(p => p.Conectado);
        _lblAhora.Text = $"{conectados} de {filas.Count} conectado(s).  " +
                         $"Se da por desconectado a quien lleve {PresenceService.ToleranciaSinLatido.TotalMinutes:0} min sin dar señales.";
    }

    private void LoadJornadas()
    {
        List<WorkPresence> jornadas;
        try { jornadas = _presence.JornadasDelDia(_dtpDia.Value); }
        catch (AuthorizationException ex) { _lblJornadas.Text = ex.Message; return; }
        catch (Exception ex)
        {
            // Mismo motivo que en LoadAhora: esta consulta también barre (escribe) y la refresca
            // un tick; el registro anterior se queda en pantalla en vez de tumbar nada.
            _lblJornadas.Text = $"Sin conexión con la base ({ex.Message}). Se reintenta en 30 s.";
            return;
        }

        // El tick de 30 s repuebla este grid mientras el administrador lo está leyendo: sin
        // recordar la fila y el scroll, cada refresco lo devolvería a la primera fila.
        int? seleccionada = _gridJornadas.CurrentRow is { Index: >= 0 } fila && fila.Index < _jornadas.Count
            ? _jornadas[fila.Index].Id : null;
        int scroll = _gridJornadas.FirstDisplayedScrollingRowIndex;
        _jornadas = jornadas;

        _gridJornadas.Rows.Clear();
        foreach (var j in jornadas)
        {
            int i = _gridJornadas.Rows.Add(
                j.DisplayName,
                j.StartedAtUtc.ToLocalTime().ToString("HH:mm"),
                j.EndedAtUtc is DateTime f ? f.ToLocalTime().ToString("HH:mm") : "— en curso",
                PresenceService.Duracion(j.Duracion),
                j.EndReason switch
                {
                    PresenceEnd.CierreNormal => "Cerró sesión",
                    PresenceEnd.SinLatido    => "⚠ Sin señales",
                    _                        => "En curso"
                },
                j.Origin ?? "");

            if (j.EndReason == PresenceEnd.SinLatido)
                _gridJornadas.Rows[i].Cells["Cierre"].Style.ForeColor = AppTheme.Warning;
            if (j.Abierta) _gridJornadas.Rows[i].Cells["Salida"].Style.ForeColor = AppTheme.Success;
        }

        // Se restaura por Id, no por índice: entre refrescos pudo entrar una jornada nueva arriba.
        if (seleccionada is int selId)
        {
            int idx = jornadas.FindIndex(j => j.Id == selId);
            if (idx >= 0) _gridJornadas.CurrentCell = _gridJornadas.Rows[idx].Cells[0];
        }
        if (scroll >= 0 && scroll < _gridJornadas.Rows.Count)
            _gridJornadas.FirstDisplayedScrollingRowIndex = scroll;

        var total = TimeSpan.FromTicks(jornadas.Sum(j => j.Duracion.Ticks));
        _lblJornadas.Text = jornadas.Count == 0
            ? "Nadie abrió la aplicación ese día."
            : $"{jornadas.Count} jornada(s) de {jornadas.Select(j => j.UserId).Distinct().Count()} persona(s)  ·  " +
              $"{PresenceService.Duracion(total)} en total.  " +
              "«⚠ Sin señales» = la aplicación dejó de responder; la salida es su última señal, no una hora real de salida.";
    }

    // ── Asistencia oficial ───────────────────────────────────────────────────────

    private void LoadAsistencia()
    {
        List<AsistenciaDelDiaFila> filas;
        try { filas = _attendance.AsistenciaDelDia(_dtpDiaAsistencia.Value); }
        catch (AuthorizationException ex) { _lblAsistencia.Text = ex.Message; return; }
        catch (Exception ex)
        {
            _lblAsistencia.Text = $"Sin conexión con la base ({ex.Message}). Se reintenta en 30 s.";
            return;
        }

        int? seleccionado = FilaAsistenciaSeleccionada()?.UserId;
        int scroll = _gridAsistencia.FirstDisplayedScrollingRowIndex;
        _asistencia = filas;

        _gridAsistencia.Rows.Clear();
        foreach (var f in filas)
        {
            int i = _gridAsistencia.Rows.Add(
                f.Nombre,
                Hora(f.EntradaOficialUtc),
                f.SalidaOficialUtc is DateTime s ? Hora(s) : (f.RegistroId != null ? "— abierta" : ""),
                Horas(f),
                Delta(f.DeltaEntrada),
                Delta(f.DeltaSalida),
                Hora(f.PrimeraSenalAutoUtc),
                Hora(f.UltimaSenalAutoUtc),
                EstadoDeFila(f));

            var fila = _gridAsistencia.Rows[i];
            if (f.RegistroId == null)
            {
                // Quien no marcó nada: en gris si tampoco abrió la aplicación, resaltado si sí.
                var color = f.SinMarcar ? AppTheme.Warning : AppTheme.TextSecondary;
                fila.DefaultCellStyle.ForeColor = color;
                fila.DefaultCellStyle.SelectionForeColor = color;
            }
            else
            {
                fila.Cells["Persona"].Style.Font = AppTheme.BoldFont;
                if (f.Cierre == AttendanceCloseKind.Olvido) Pintar(fila, "Estado", AppTheme.Warning);
                if (f.CorreccionSolicitada)                 Pintar(fila, "Estado", AppTheme.Warning);
                if (f.DeltaEntrada is TimeSpan de && de.Duration() > AttendanceService.ToleranciaDiscrepancia)
                    Pintar(fila, "DeltaEnt", AppTheme.Warning);
                if (f.DeltaSalida is TimeSpan ds && ds.Duration() > AttendanceService.ToleranciaDiscrepancia)
                    Pintar(fila, "DeltaSal", AppTheme.Warning);
                if (f.SalidaOficialUtc == null) Pintar(fila, "Salida", AppTheme.Success);
            }
        }

        if (seleccionado is int userId)
        {
            int idx = filas.FindIndex(f => f.UserId == userId);
            if (idx >= 0) _gridAsistencia.CurrentCell = _gridAsistencia.Rows[idx].Cells[0];
        }
        if (scroll >= 0 && scroll < _gridAsistencia.Rows.Count)
            _gridAsistencia.FirstDisplayedScrollingRowIndex = scroll;

        int marcaron = filas.Count(f => f.RegistroId != null);
        int sinMarcar = filas.Count(f => f.SinMarcar);
        int olvidos = filas.Count(f => f.Cierre == AttendanceCloseKind.Olvido);
        int solicitudes = filas.Count(f => f.CorreccionSolicitada);

        _lblAsistencia.Text =
            $"{marcaron} de {filas.Count} marcaron su asistencia." +
            (sinMarcar > 0 ? $"  ⚠ {sinMarcar} usó la aplicación sin marcar." : "") +
            (olvidos > 0 ? $"  ⚠ {olvidos} sin salida marcada (la hora es una estimación, corrígela)." : "") +
            (solicitudes > 0 ? $"  🙋 {solicitudes} pidió corrección." : "") +
            $"  Se resalta a partir de {AttendanceService.ToleranciaDiscrepancia.TotalMinutes:0} min de diferencia con lo que vio la aplicación.";
    }

    private static void Pintar(DataGridViewRow fila, string columna, Color color)
    {
        // El SelectionForeColor es obligatorio: MakeGrid lo fija a TextPrimary y, sin él, el resalte
        // desaparece justo en la fila que el administrador acaba de seleccionar.
        var celda = fila.Cells[columna].Style;
        celda.ForeColor = color;
        celda.SelectionForeColor = color;
    }

    private static string Hora(DateTime? utc) => utc is DateTime d ? d.ToLocalTime().ToString("HH:mm") : "";

    private static string Horas(AsistenciaDelDiaFila f) =>
        f.EntradaOficialUtc is DateTime e && f.SalidaOficialUtc is DateTime s
            ? PresenceService.Duracion(s - e)
            : "";

    /// <summary>La diferencia con lo que vio la aplicación, con signo: «+12 min» = marcó después.</summary>
    private static string Delta(TimeSpan? delta)
    {
        if (delta is not TimeSpan d) return "";
        int minutos = (int)Math.Round(d.TotalMinutes);
        if (minutos == 0) return "0";
        return minutos > 0 ? $"+{minutos} min" : $"{minutos} min";
    }

    private static string EstadoDeFila(AsistenciaDelDiaFila f)
    {
        if (f.RegistroId == null) return f.SinMarcar ? "⚠ Sin marcar" : "Sin actividad";
        var estado = AttendanceService.EtiquetaCierre(f.Cierre);
        return f.CorreccionSolicitada ? $"🙋 Pide corrección · {estado}" : estado;
    }

    private AsistenciaDelDiaFila? FilaAsistenciaSeleccionada() =>
        _gridAsistencia.CurrentRow is { Index: >= 0 } fila && fila.Index < _asistencia.Count
            ? _asistencia[fila.Index] : null;

    private void BtnCorregir_Click(object? sender, EventArgs e)
    {
        var fila = FilaAsistenciaSeleccionada();
        if (fila?.RegistroId is not int registroId)
        {
            MessageBox.Show("Selecciona una persona que sí haya marcado. Si no marcó nada, usa «Día olvidado…».",
                "Nada que corregir", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new AttendanceEditForm(
            fila.Nombre,
            fila.EntradaOficialUtc!.Value.ToLocalTime(),
            fila.SalidaOficialUtc?.ToLocalTime(),
            fila.NotaCorreccion);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() => _attendance.CorregirRegistro(registroId, dlg.EntradaLocal, dlg.SalidaLocal, dlg.Motivo));
    }

    private void BtnAlta_Click(object? sender, EventArgs e)
    {
        var personas = _asistencia.Select(f => (f.UserId, f.Nombre)).ToList();
        if (personas.Count == 0)
        {
            MessageBox.Show("No hay cuentas activas que registrar.", "Sin personas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new AttendanceEditForm(personas);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() => _attendance.CrearRegistroManual(dlg.UserIdElegido, dlg.EntradaLocal, dlg.SalidaLocal, dlg.Motivo));
    }

    /// <summary>Ejecuta una operación del servicio y refresca; muestra el mensaje que devuelva.</summary>
    private void Ejecutar(Func<(bool ok, string mensaje)> operacion)
    {
        try
        {
            var (ok, mensaje) = operacion();
            MessageBox.Show(mensaje, ok ? "Listo" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok) LoadAsistencia();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BtnExportarAsistencia_Click(object? sender, EventArgs e)
    {
        if (_asistencia.Count == 0)
        {
            MessageBox.Show("No hay nada que exportar en ese día.", "Sin datos",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = _reports.PromptSaveDialog($"Asistencia_{_dtpDiaAsistencia.Value:yyyyMMdd}");
        if (path == null) return;

        try
        {
            // Las horas van como TEXTO: ExportToExcel formatea los DateTime como dd/MM/yyyy, sin
            // hora, y una columna «Entrada» sin hora no dice absolutamente nada.
            _reports.ExportToExcel(
                _asistencia,
                ["Persona", "Entrada", "Salida", "Horas", "Δ entrada", "Δ salida",
                 "1ª señal app", "Últ. señal app", "Estado", "Pidió corrección"],
                f => [f.Nombre, Hora(f.EntradaOficialUtc), Hora(f.SalidaOficialUtc), Horas(f),
                      Delta(f.DeltaEntrada), Delta(f.DeltaSalida),
                      Hora(f.PrimeraSenalAutoUtc), Hora(f.UltimaSenalAutoUtc),
                      EstadoDeFila(f), f.NotaCorreccion ?? ""],
                "Asistencia", path);

            MessageBox.Show($"Exportado a:\n{path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // El tablero envejece solo: sin refresco, «conectado» se queda congelado en pantalla.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            LoadData();
            _refresco ??= NuevoTemporizador();
            _refresco.Start();
        }
        else _refresco?.Stop();
    }

    private System.Windows.Forms.Timer NuevoTemporizador()
    {
        var t = new System.Windows.Forms.Timer { Interval = 30_000 };
        t.Tick += (_, _) =>
        {
            if (!Visible) return;
            LoadAhora();
            // El registro de HOY también envejece: una jornada que se cae a media mañana seguiría
            // diciendo «— en curso» hasta reabrir la pantalla. Los días pasados no cambian solos.
            if (_dtpDia.Value.Date == DateTime.Today) LoadJornadas();
            // Y la asistencia de hoy cambia sola conforme la gente marca: sin esto, el líder vería
            // «sin marcar» a quien acaba de llegar.
            if (_dtpDiaAsistencia.Value.Date == DateTime.Today) LoadAsistencia();
        };
        return t;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresco?.Stop(); _refresco?.Dispose(); _refresco = null; }
        base.Dispose(disposing);
    }
}
