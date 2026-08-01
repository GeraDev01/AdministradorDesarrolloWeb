using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del ADMINISTRADOR: quién tiene la aplicación abierta ahora mismo y en qué anda, más el
/// registro de jornadas por día.
///
/// Lo que dice «conectado» es un LATIDO, no el par inicio/cierre de sesión: cerrar con la X deja la
/// aplicación viva en la bandeja —y eso es estar conectado—, pero un cuelgue o un apagón no avisan.
/// Sin latido, esa persona se quedaría marcada como conectada para siempre.
///
/// Los estados (comiendo, en un descanso…) son <b>del momento</b>: se ven aquí en vivo y no se
/// guardan minutados. Es deliberado — un histórico de las pausas de cada quien es vigilancia, no
/// asistencia. Lo que sí queda es la jornada: a qué hora entró y a qué hora salió.
/// </summary>
public class PresenceControl : UserControl
{
    private readonly PresenceService _presence;

    private DataGridView _gridAhora = null!, _gridJornadas = null!;
    private DateTimePicker _dtpDia = null!;
    private Label _lblAhora = null!, _lblJornadas = null!;
    private System.Windows.Forms.Timer? _refresco;

    /// <summary>Las jornadas tal como están pintadas, para reconocer la fila seleccionada por su
    /// Id cuando el refresco de 30 s repuebla el grid.</summary>
    private List<WorkPresence> _jornadas = [];

    private const string ClaveColumnasAhora = "presence.ahora";
    private const string ClaveColumnasJornadas = "presence.jornadas";

    public PresenceControl(PresenceService presence)
    {
        _presence = presence;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildAhoraTab());
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

    private static Label NuevaLineaEstado() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0)
    };

    // ── Datos ────────────────────────────────────────────────────────────────────

    private void LoadData() { LoadAhora(); LoadJornadas(); }

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
        };
        return t;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresco?.Stop(); _refresco?.Dispose(); _refresco = null; }
        base.Dispose(disposing);
    }
}
