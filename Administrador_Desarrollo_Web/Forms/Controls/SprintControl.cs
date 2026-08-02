using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Seguimiento del sprint, en dos vistas:
///
///  · <b>🏁 Sprint actual</b> — línea de tiempo tipo calendario (día por día, fines de semana
///    sombreados, marcador de HOY) con dos barras comparables —el tiempo consumido y el avance
///    real— y debajo la lista de lo comprometido. La lectura es una sola: si la barra de avance
///    va detrás de la de tiempo, vamos atrasados.
///  · <b>📈 Histórico</b> — cómo salió cada sprint y la VELOCIDAD del equipo (promedio de
///    entregados por sprint cerrado). Es la única base honesta para comprometer el siguiente:
///    sin ella, el compromiso es un deseo.
///
/// El DESARROLLADOR ve la misma pantalla en SOLO CONSULTA: sin botones de escritura, sin la
/// pestaña de histórico, con sus requerimientos marcados 👤 y un filtro «solo los míos». El avance
/// se calcula siempre sobre TODO el sprint aunque el filtro esconda filas: dos porcentajes
/// distintos para el mismo sprint serían dos verdades.
///
/// Todo el cálculo vive en <see cref="SprintService"/> (CalcularAvance, Historico, Velocidad);
/// aquí solo se dibuja.
/// </summary>
public class SprintControl : UserControl
{
    private readonly SprintService _sprints;
    private readonly AppDbContext _db;
    private readonly ReportService _report;
    private readonly bool _esAdmin;
    /// <summary>La cuenta está ligada a una ficha de desarrollador: sin ella no hay «lo mío».</summary>
    private readonly bool _tieneFicha;

    private ComboBox _cbxSprint = null!;
    private Panel _pnlTimeline = null!;
    private DataGridView _grid = null!;
    private Label _lblEstado = null!, _lblObjetivo = null!;
    private Label _kpiVeredicto = null!, _kpiAvance = null!, _kpiTiempo = null!, _kpiEntregados = null!, _kpiDias = null!;

    // Histórico
    private DataGridView _gridHist = null!;
    private Panel _pnlGrafica = null!;
    private Label _lblHist = null!, _kpiVelocidad = null!, _kpiCerrados = null!, _kpiCumplimiento = null!;
    private List<SprintResumen> _historico = [];
    private Button _btnExcel = null!;
    /// <summary>Los de escritura solo EXISTEN para el administrador (ver BuildSeguimientoTab).</summary>
    private Button? _btnEditar, _btnEliminar, _btnReqs;
    private CheckBox? _chkSoloMios;
    private HashSet<int> _mios = [];

    private List<Sprint> _lista = [];
    private Sprint? _sprint;
    private List<Requirement> _reqs = [];
    private SprintAvance? _avance;

    public SprintControl(SprintService sprints, AppDbContext db, ReportService report, ICurrentUser currentUser)
    {
        _sprints = sprints; _db = db; _report = report;
        _esAdmin = currentUser.IsAdmin;
        _tieneFicha = currentUser.DeveloperId != null;
        BuildUI();
        LoadSprints();
    }

    // ── UI ───────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildSeguimientoTab());
        // El histórico es del administrador (Historico() exige ese rol): construirlo para el
        // desarrollador sería una pestaña que solo sabe decir «sin permiso».
        if (_esAdmin) tabs.TabPages.Add(BuildHistoricoTab());
        Controls.Add(tabs);
    }

    private TabPage BuildSeguimientoTab()
    {
        var page = new TabPage("  🏁  Sprint actual  ") { BackColor = AppTheme.ContentBg };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));    // toolbar
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));    // objetivo
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));    // KPIs
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));   // timeline
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // grid
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));    // estado
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar ────────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 6, 10, 2), BackColor = AppTheme.ContentBg
        };
        toolbar.Controls.Add(new Label { Text = "Sprint:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxSprint = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        _cbxSprint.SelectedIndexChanged += (_, _) => LoadSeguimiento();
        toolbar.Controls.Add(_cbxSprint);

        // Filtrar a lo propio es lo primero que quiere el desarrollador; el administrador no lo
        // necesita (para él son todos) y por eso solo aparece con ficha ligada.
        if (_tieneFicha && !_esAdmin)
        {
            _chkSoloMios = new CheckBox { Text = "Solo los míos", AutoSize = true, Margin = new Padding(0, 7, 12, 0) };
            _chkSoloMios.CheckedChanged += (_, _) => PintarSprint();
            toolbar.Controls.Add(_chkSoloMios);
        }

        _btnExcel = AppTheme.MakeSecondaryButton("📊 Excel", 100);
        _btnExcel.Margin = new Padding(0, 2, 6, 0);
        _btnExcel.Click += (_, _) => ExportarExcel();
        toolbar.Controls.Add(_btnExcel);

        // Los botones de escritura solo EXISTEN para el administrador: uno gris invita a preguntar
        // por qué; uno ausente, no. El servicio impide de todos modos.
        if (_esAdmin)
        {
            var btnNuevo = AppTheme.MakePrimaryButton("➕ Nuevo sprint", 140);
            btnNuevo.Margin = new Padding(0, 2, 6, 0);
            btnNuevo.Click += (_, _) => Nuevo();

            _btnEditar = AppTheme.MakeSecondaryButton("✏ Fechas y nombre", 160);
            _btnEditar.Margin = new Padding(0, 2, 6, 0);
            _btnEditar.Click += (_, _) => Editar();

            _btnReqs = AppTheme.MakeSecondaryButton("📋 Requerimientos…", 170);
            _btnReqs.Margin = new Padding(0, 2, 6, 0);
            _btnReqs.Click += (_, _) => ElegirRequerimientos();

            _btnEliminar = AppTheme.MakeDangerButton("🗑 Eliminar", 110);
            _btnEliminar.Margin = new Padding(0, 2, 0, 0);
            _btnEliminar.Click += (_, _) => Eliminar();

            toolbar.Controls.AddRange([btnNuevo, _btnEditar, _btnReqs, _btnEliminar]);
        }
        else
        {
            toolbar.Controls.Add(new Label
            {
                Text = "Solo consulta — el sprint lo arma el administrador",
                AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont,
                Margin = new Padding(4, 10, 0, 0)
            });
        }

        _lblObjetivo = new Label
        {
            Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0)
        };

        // ── KPIs ───────────────────────────────────────────────────
        var kpis = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg
        };
        kpis.Controls.Add(Kpi("Estado del sprint", out _kpiVeredicto, 190));
        kpis.Controls.Add(Kpi("Avance real", out _kpiAvance, 130));
        kpis.Controls.Add(Kpi("Tiempo consumido", out _kpiTiempo, 150));
        kpis.Controls.Add(Kpi("Entregados", out _kpiEntregados, 130));
        kpis.Controls.Add(Kpi("Días restantes", out _kpiDias, 130));

        // ── Timeline ───────────────────────────────────────────────
        _pnlTimeline = new PanelSinParpadeo { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Margin = new Padding(10, 2, 10, 2) };
        _pnlTimeline.Paint += PintarTimeline;
        _pnlTimeline.Resize += (_, _) => _pnlTimeline.Invalidate();
        var pnlTl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg };
        pnlTl.Controls.Add(_pnlTimeline);

        // ── Grid de requerimientos ─────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.Margin = new Padding(10, 2, 10, 2);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "",             Name = "Mio",     FillWeight = 5 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "Estado",  FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requerimiento", Name = "Titulo", FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "%",            Name = "Pct",     FillWeight = 8,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Compromiso",   Name = "Comp",    FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entregado",    Name = "Entr",    FillWeight = 13 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);

        _lblEstado = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
        };

        tbl.Controls.Add(toolbar,      0, 0);
        tbl.Controls.Add(_lblObjetivo, 0, 1);
        tbl.Controls.Add(kpis,         0, 2);
        tbl.Controls.Add(pnlTl,        0, 3);
        tbl.Controls.Add(pnlGrid,      0, 4);
        tbl.Controls.Add(_lblEstado,   0, 5);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Histórico y velocidad ────────────────────────────────────────────────────

    private TabPage BuildHistoricoTab()
    {
        var page = new TabPage("  📈  Histórico  ") { BackColor = AppTheme.ContentBg };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));    // KPIs
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));   // gráfica
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // grid
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));    // estado
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var kpis = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 6, 10, 2), BackColor = AppTheme.ContentBg
        };
        kpis.Controls.Add(Kpi("Velocidad (entregados/sprint)", out _kpiVelocidad, 230));
        kpis.Controls.Add(Kpi("Sprints cerrados", out _kpiCerrados, 150));
        kpis.Controls.Add(Kpi("Cumplimiento promedio", out _kpiCumplimiento, 200));

        _pnlGrafica = new PanelSinParpadeo { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Margin = new Padding(10, 2, 10, 2) };
        _pnlGrafica.Paint += PintarGrafica;
        var pnlG = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg };
        pnlG.Controls.Add(_pnlGrafica);

        _gridHist = AppTheme.MakeGrid();
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sprint",     Name = "Nombre", FillWeight = 26 });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Periodo",    Name = "Periodo", FillWeight = 22 });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días",       Name = "Dias",   FillWeight = 8,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comprometidos", Name = "Total", FillWeight = 13,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entregados", Name = "Entr",   FillWeight = 12,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cumplido",   Name = "Pct",    FillWeight = 11,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _gridHist.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",     Name = "Cerrado", FillWeight = 12 });
        foreach (DataGridViewColumn c in _gridHist.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        var pnlGr = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2), BackColor = AppTheme.ContentBg };
        pnlGr.Controls.Add(_gridHist);

        _lblHist = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
        };

        tbl.Controls.Add(kpis,     0, 0);
        tbl.Controls.Add(pnlG,     0, 1);
        tbl.Controls.Add(pnlGr,    0, 2);
        tbl.Controls.Add(_lblHist, 0, 3);
        page.Controls.Add(tbl);
        return page;
    }

    private void LoadHistorico()
    {
        try { _historico = _sprints.Historico(); }
        catch (AuthorizationException ex) { _lblHist.Text = ex.Message; return; }

        var (velocidad, cerrados) = SprintService.Velocidad(_historico);
        _kpiVelocidad.Text  = cerrados == 0 ? "—" : velocidad.ToString("0.#");
        _kpiCerrados.Text   = cerrados.ToString();
        var conTrabajo = _historico.Where(h => h.Cerrado && h.Total > 0).ToList();
        _kpiCumplimiento.Text = conTrabajo.Count == 0 ? "—"
            : $"{Math.Round(conTrabajo.Average(h => h.CompletadoPct), MidpointRounding.AwayFromZero):0}%";

        _gridHist.Rows.Clear();
        // El más reciente arriba en la tabla (se lee de arriba abajo), aunque la gráfica va al
        // revés: ahí el tiempo tiene que correr de izquierda a derecha.
        foreach (var h in Enumerable.Reverse(_historico))
        {
            int i = _gridHist.Rows.Add(
                h.Name,
                $"{h.StartDate:dd/MM/yy} – {h.EndDate:dd/MM/yy}",
                h.DiasTotales,
                h.Total + (h.Cancelados > 0 ? $" (+{h.Cancelados} canc.)" : ""),
                h.Entregados,
                h.Total == 0 ? "—" : $"{h.CompletadoPct}%",
                h.Cerrado ? "Cerrado" : "En curso");

            var color = !h.Cerrado ? AppTheme.SidebarActive : ColorCumplimiento(h.CompletadoPct);
            var celda = _gridHist.Rows[i].Cells["Pct"].Style;
            celda.ForeColor = color; celda.SelectionForeColor = color; celda.Font = AppTheme.BoldFont;
        }

        _lblHist.Text = _historico.Count == 0
            ? "Todavía no hay sprints. El histórico se llena solo conforme cierres sprints."
            : cerrados == 0
                ? $"{_historico.Count} sprint(s), ninguno cerrado aún: la velocidad aparece cuando cierre el primero."
                : $"{_historico.Count} sprint(s), {cerrados} cerrado(s).  La velocidad es el promedio de entregados " +
                  "por sprint CERRADO — úsala para comprometer el siguiente.";

        _pnlGrafica.Invalidate();
    }

    /// <summary>
    /// Barras de entregados por sprint, en orden cronológico. Se dibuja a mano por lo mismo que la
    /// línea de tiempo: no hay librería de gráficas en el proyecto y esto son dos rectángulos.
    /// </summary>
    private void PintarGrafica(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AppTheme.CardBg);

        if (_historico.Count == 0)
        {
            TextRenderer.DrawText(g, "Sin sprints todavía", AppTheme.DefaultFont,
                _pnlGrafica.ClientRectangle, AppTheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var area = _pnlGrafica.ClientRectangle;
        int margenIzq = 34, margenAbajo = 30, margenArriba = 14;
        int alto = Math.Max(40, area.Height - margenAbajo - margenArriba);
        int ancho = Math.Max(40, area.Width - margenIzq - 14);

        // Escala: el máximo entre comprometidos y entregados de todo el histórico, mínimo 1 para
        // no dividir por cero cuando aún no hay nada entregado.
        int tope = Math.Max(1, _historico.Max(h => Math.Max(h.Total, h.Entregados)));
        float pasoX = (float)ancho / _historico.Count;
        float anchoBarra = Math.Max(3, Math.Min(46, pasoX * 0.62f));

        using var plumaEje = new Pen(AppTheme.Border);
        using var brComprometido = new SolidBrush(Color.FromArgb(203, 213, 225));
        using var brEntregado = new SolidBrush(Color.FromArgb(21, 128, 61));
        using var plumaVel = new Pen(AppTheme.SidebarActive, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };

        int yBase = margenArriba + alto;
        g.DrawLine(plumaEje, margenIzq, yBase, margenIzq + ancho, yBase);

        // Escala vertical: solo 0 y el tope; más marcas serían ruido en 190 px.
        TextRenderer.DrawText(g, "0", AppTheme.SmallFont, new Rectangle(0, yBase - 8, margenIzq - 4, 16),
            AppTheme.TextSecondary, TextFormatFlags.Right);
        TextRenderer.DrawText(g, tope.ToString(), AppTheme.SmallFont,
            new Rectangle(0, margenArriba - 8, margenIzq - 4, 16), AppTheme.TextSecondary, TextFormatFlags.Right);

        for (int i = 0; i < _historico.Count; i++)
        {
            var h = _historico[i];
            float cx = margenIzq + pasoX * (i + 0.5f);

            // Barra clara = comprometido; barra oscura encima = entregado. La diferencia visible
            // ES el dato: lo que se prometió y no salió.
            float hComp = alto * h.Total / (float)tope;
            float hEnt  = alto * h.Entregados / (float)tope;
            g.FillRectangle(brComprometido, cx - anchoBarra / 2, yBase - hComp, anchoBarra, hComp);
            g.FillRectangle(brEntregado, cx - anchoBarra / 2, yBase - hEnt, anchoBarra, hEnt);

            if (pasoX >= 26)
                TextRenderer.DrawText(g, Abreviar(h.Name, pasoX), AppTheme.SmallFont,
                    new Rectangle((int)(cx - pasoX / 2), yBase + 4, (int)pasoX, 14),
                    h.Cerrado ? AppTheme.TextSecondary : AppTheme.SidebarActive,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }

        // Línea de velocidad: contra ella se lee si un sprint salió del promedio.
        var (velocidad, cerrados) = SprintService.Velocidad(_historico);
        if (cerrados >= 2)
        {
            float y = yBase - alto * (float)velocidad / tope;
            g.DrawLine(plumaVel, margenIzq, y, margenIzq + ancho, y);
            TextRenderer.DrawText(g, $"velocidad {velocidad:0.#}", AppTheme.SmallFont,
                new Rectangle(margenIzq + 4, (int)y - 15, 140, 14), AppTheme.SidebarActive, TextFormatFlags.Left);
        }
    }

    private static string Abreviar(string nombre, float ancho) =>
        nombre.Length <= 10 || ancho >= 70 ? nombre : nombre[..9] + "…";

    private static Color ColorCumplimiento(int pct) => pct switch
    {
        >= 90 => Color.FromArgb(21, 128, 61),
        >= 70 => Color.FromArgb(180, 83, 9),
        _     => Color.FromArgb(220, 38, 38)
    };

    private static Panel Kpi(string titulo, out Label valor, int ancho)
    {
        var card = new Panel { Width = ancho, Height = 74, BackColor = AppTheme.CardBg, Margin = new Padding(0, 0, 10, 0) };
        card.Controls.Add(new Label
        {
            Text = titulo, Dock = DockStyle.Top, Height = 24, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(10, 0, 0, 0)
        });
        valor = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.KpiValueFont, ForeColor = AppTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 6), AutoEllipsis = true
        };
        card.Controls.Add(valor);
        card.Controls.SetChildIndex(valor, 0);
        return card;
    }

    /// <summary>Panel con doble búfer: la línea de tiempo se redibuja entera en cada refresco.</summary>
    private sealed class PanelSinParpadeo : Panel
    {
        public PanelSinParpadeo() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    // ── Datos ────────────────────────────────────────────────────────────────────

    private void LoadSprints(int? seleccionar = null)
    {
        try { _lista = _sprints.Listar(); }
        catch (AuthorizationException ex) { _lblEstado.Text = ex.Message; return; }

        // El histórico depende de los mismos datos: crear, editar o borrar un sprint lo cambia.
        if (_esAdmin) LoadHistorico();

        _cbxSprint.BeginUpdate();
        _cbxSprint.Items.Clear();
        foreach (var s in _lista)
            _cbxSprint.Items.Add($"{s.Name}   ({s.StartDate:dd/MM} – {s.EndDate:dd/MM/yyyy})");
        _cbxSprint.EndUpdate();

        if (_lista.Count == 0)
        {
            _sprint = null; _reqs = []; _avance = null;
            PintarSprint();
            return;
        }

        // Por omisión, el sprint que corre HOY; si no hay ninguno en curso, el más reciente.
        int idx = seleccionar is int id ? _lista.FindIndex(s => s.Id == id) : -1;
        if (idx < 0) idx = _lista.FindIndex(s => s.StartDate.Date <= DateTime.Today && DateTime.Today <= s.EndDate.Date);
        if (idx < 0) idx = 0;
        _cbxSprint.SelectedIndex = idx;   // dispara LoadSeguimiento
    }

    private void LoadSeguimiento()
    {
        if (_cbxSprint.SelectedIndex < 0 || _cbxSprint.SelectedIndex >= _lista.Count) return;
        _sprint = _lista[_cbxSprint.SelectedIndex];
        try
        {
            _reqs = _sprints.Requerimientos(_sprint.Id);
            _mios = _sprints.MisRequerimientos(_sprint.Id);
            // El avance se calcula SIEMPRE sobre todo el sprint, nunca sobre lo filtrado: dos
            // porcentajes distintos para el mismo sprint serían dos verdades.
            _avance = SprintService.CalcularAvance(_sprint, _reqs, DateTime.Today);
        }
        catch (AuthorizationException ex) { _lblEstado.Text = ex.Message; return; }
        PintarSprint();
    }

    private void PintarSprint()
    {
        bool hay = _sprint != null;
        // Nulos en modo consulta: se creó solo lo que el rol puede usar.
        if (_btnEditar   != null) _btnEditar.Enabled   = hay;
        if (_btnEliminar != null) _btnEliminar.Enabled = hay;
        if (_btnReqs     != null) _btnReqs.Enabled     = hay;
        _btnExcel.Enabled = hay;
        _lblObjetivo.Text = _sprint?.Goal is { Length: > 0 } g ? $"🎯 {g}" : "";

        if (_sprint == null || _avance == null)
        {
            _kpiVeredicto.Text = _kpiAvance.Text = _kpiTiempo.Text = _kpiEntregados.Text = _kpiDias.Text = "—";
            _kpiVeredicto.ForeColor = AppTheme.TextPrimary;
            _grid.Rows.Clear();
            _pnlTimeline.Invalidate();
            _lblEstado.Text = "No hay sprints todavía. Crea el primero con ➕ y cuélgale requerimientos.";
            return;
        }

        var a = _avance;
        _kpiVeredicto.Text = a.Veredicto;
        _kpiVeredicto.ForeColor = ColorVeredicto(a.Veredicto);
        _kpiAvance.Text = $"{a.AvanceRealPct}%";
        _kpiTiempo.Text = $"{a.TiempoPct}%";
        _kpiEntregados.Text = $"{a.Entregados} / {a.TotalRequerimientos}";
        _kpiDias.Text = a.DiasRestantes.ToString();

        _grid.Rows.Clear();
        bool soloMios = _chkSoloMios?.Checked == true;
        int ocultos = 0;
        foreach (var r in _reqs)
        {
            bool mio = _mios.Contains(r.Id);
            if (soloMios && !mio) { ocultos++; continue; }

            int i = _grid.Rows.Add(
                mio ? "👤" : "",
                EtiquetaEstado(r.Status),
                r.Title,
                r.Status == RequirementStatus.Entregado ? "100%" : $"{r.ProgressPercent}%",
                r.CommittedDeliveryDate?.ToString("dd/MM") ?? "—",
                r.ActualDeliveryDate?.ToString("dd/MM") ?? "—");

            // Lo mío, resaltado por FONDO —no por color de texto—: el texto ya lleva el color del
            // estado y el rojo del compromiso vencido, y pisarlos borraría la señal importante.
            // SelectionBackColor explícito o el resaltado se apaga justo al seleccionar la fila.
            if (mio)
            {
                var fila = _grid.Rows[i].DefaultCellStyle;
                fila.BackColor = Color.FromArgb(254, 249, 195);
                fila.SelectionBackColor = Color.FromArgb(253, 230, 138);
                fila.SelectionForeColor = AppTheme.TextPrimary;
                _grid.Rows[i].Cells["Titulo"].Style.Font = AppTheme.BoldFont;
            }

            // El color del estado, legible también en la fila seleccionada (gotcha de MakeGrid).
            var c = AppTheme.StatusColor(r.Status);
            var celda = _grid.Rows[i].Cells["Estado"].Style;
            celda.ForeColor = c; celda.SelectionForeColor = c; celda.Font = AppTheme.BoldFont;

            // Compromiso vencido y sin entregar: eso es lo que el administrador vino a ver.
            if (r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado
                && r.CommittedDeliveryDate is { } comp && comp.Date < DateTime.Today)
            {
                var cc = _grid.Rows[i].Cells["Comp"].Style;
                cc.ForeColor = AppTheme.Danger; cc.SelectionForeColor = AppTheme.Danger; cc.Font = AppTheme.BoldFont;
            }
        }

        if (_reqs.Count == 0)
        {
            _lblEstado.Text = _esAdmin
                ? "Este sprint no tiene requerimientos: 📋 Requerimientos… para colgarle trabajo."
                : "Este sprint todavía no tiene requerimientos.";
        }
        else
        {
            var partes = new List<string>
            {
                $"{a.TotalRequerimientos} requerimiento(s)" +
                (a.Cancelados > 0 ? $" (+{a.Cancelados} cancelado(s), fuera del cálculo)" : "") +
                $"  ·  {a.EnCurso} en curso  ·  {a.SinEmpezar} sin empezar."
            };
            if (soloMios) partes.Add($"Mostrando solo los míos ({_mios.Count}); {ocultos} oculto(s).");
            // Sin ficha ligada no hay «lo mío» que resaltar: decirlo evita que la persona crea
            // que el sprint no trae trabajo suyo.
            else if (!_esAdmin && !_tieneFicha) partes.Add("Tu cuenta no está ligada a una ficha de desarrollador: no se resalta nada como tuyo.");
            else if (!_esAdmin) partes.Add($"👤 = asignado a ti ({_mios.Count}).");
            partes.Add("En la línea de tiempo: ▲ entregado, △ compromiso, sombreado = fin de semana.");
            _lblEstado.Text = string.Join("  ", partes);
        }

        _pnlTimeline.Invalidate();
    }

    // ── La línea de tiempo ───────────────────────────────────────────────────────

    private void PintarTimeline(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AppTheme.CardBg);

        if (_sprint == null || _avance == null)
        {
            TextRenderer.DrawText(g, "Sin sprint seleccionado", AppTheme.DefaultFont,
                _pnlTimeline.ClientRectangle, AppTheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var s = _sprint; var a = _avance;
        int dias = a.DiasTotales;
        var area = _pnlTimeline.ClientRectangle;
        int x0 = 14, ancho = Math.Max(60, area.Width - 28);
        float cellW = (float)ancho / dias;

        int yCal = 8, hCal = 40;        // franja de calendario
        int yT = yCal + hCal + 14;      // barra de tiempo
        int yA = yT + 26;               // barra de avance
        const int hBarra = 14;

        using var brFinde = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var brBorde = new Pen(AppTheme.Border);
        using var brTiempo = new SolidBrush(Color.FromArgb(147, 197, 253));
        using var brAvance = new SolidBrush(ColorVeredicto(a.Veredicto));

        // Franja día por día: sombrear fin de semana; número de día si cabe; el mes al cambiar.
        for (int d = 0; d < dias; d++)
        {
            var fecha = s.StartDate.AddDays(d);
            var rc = new RectangleF(x0 + d * cellW, yCal, cellW, hCal);
            if (fecha.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                g.FillRectangle(brFinde, rc);
            if (cellW >= 8) g.DrawLine(brBorde, rc.Left, yCal, rc.Left, yCal + hCal);

            if (cellW >= 17)
                TextRenderer.DrawText(g, fecha.Day.ToString(), AppTheme.SmallFont,
                    Rectangle.Round(new RectangleF(rc.X, yCal + 18, cellW, 18)),
                    AppTheme.TextSecondary, TextFormatFlags.HorizontalCenter);

            if ((d == 0 || fecha.Day == 1) && cellW * 3 >= 30)
                TextRenderer.DrawText(g, fecha.ToString("MMM"), AppTheme.SmallFont,
                    new Rectangle((int)rc.X + 2, yCal + 2, 60, 16), AppTheme.TextPrimary, TextFormatFlags.Left);
        }
        g.DrawRectangle(brBorde, x0, yCal, ancho, hCal);

        // Marcas de entregas y compromisos, sobre el día que les toca.
        foreach (var r in _reqs)
        {
            if (r.ActualDeliveryDate is { } ent && EnRango(ent))
                Marcador(g, x0 + (float)((ent.Date - s.StartDate.Date).Days + 0.5) * cellW, yCal + hCal, relleno: true);
            else if (r.CommittedDeliveryDate is { } comp && EnRango(comp)
                     && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
                Marcador(g, x0 + (float)((comp.Date - s.StartDate.Date).Days + 0.5) * cellW, yCal + hCal, relleno: false);
        }

        // Barras comparables: si la de abajo (avance) va detrás de la de arriba (tiempo), mal.
        Barra(g, "Tiempo", x0, yT, ancho, hBarra, a.TiempoPct, brTiempo);
        Barra(g, "Avance", x0, yA, ancho, hBarra, a.AvanceRealPct, brAvance);

        // HOY: la línea vertical que cruza todo, al final del día consumido. Solo mientras el
        // sprint corre: en uno ya terminado, «hoy» no está en esta línea de tiempo.
        if (a.DiasTranscurridos > 0 && a.DiasTranscurridos <= dias && DateTime.Today <= s.EndDate.Date)
        {
            float xHoy = x0 + a.DiasTranscurridos * cellW;
            using var pluma = new Pen(AppTheme.Danger, 2);
            g.DrawLine(pluma, xHoy, yCal - 4, xHoy, yA + hBarra + 4);
            TextRenderer.DrawText(g, "HOY", AppTheme.SmallFont,
                new Rectangle((int)xHoy - 40, yA + hBarra + 2, 80, 14), AppTheme.Danger,
                TextFormatFlags.HorizontalCenter);
        }

        bool EnRango(DateTime f) => f.Date >= s.StartDate.Date && f.Date <= s.EndDate.Date;
    }

    private void Barra(Graphics g, string etiqueta, int x, int y, int ancho, int alto, int pct, Brush relleno)
    {
        using var fondo = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var borde = new Pen(AppTheme.Border);
        g.FillRectangle(fondo, x, y, ancho, alto);
        g.FillRectangle(relleno, x, y, (int)(ancho * Math.Clamp(pct, 0, 100) / 100.0), alto);
        g.DrawRectangle(borde, x, y, ancho, alto);
        TextRenderer.DrawText(g, $"{etiqueta} {pct}%", AppTheme.SmallFont,
            new Rectangle(x + 4, y, 160, alto), AppTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private static void Marcador(Graphics g, float x, int y, bool relleno)
    {
        PointF[] tri = [new(x, y + 2), new(x - 5, y + 10), new(x + 5, y + 10)];
        if (relleno)
        {
            using var br = new SolidBrush(Color.FromArgb(21, 128, 61));
            g.FillPolygon(br, tri);
        }
        else
        {
            using var pl = new Pen(Color.FromArgb(180, 83, 9), 1.6f);
            g.DrawPolygon(pl, tri);
        }
    }

    private static Color ColorVeredicto(string v) => v switch
    {
        "Adelantado" or "Terminado ✓" => Color.FromArgb(21, 128, 61),
        "Al día"                      => AppTheme.SidebarActive,
        "Atrasado"                    => Color.FromArgb(220, 38, 38),
        "Terminó incompleto"          => Color.FromArgb(180, 83, 9),
        _                             => AppTheme.TextSecondary
    };

    private static string EtiquetaEstado(RequirementStatus s) => s switch
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

    // ── Acciones ─────────────────────────────────────────────────────────────────

    private void Nuevo()
    {
        using var frm = new SprintDetailForm();
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        Ejecutar(() =>
        {
            var (ok, mensaje, sprint) = _sprints.Crear(frm.Nombre, frm.Objetivo, frm.Inicio, frm.Fin);
            if (!ok) { Avisar(mensaje); return; }
            LoadSprints(sprint!.Id);
            _lblEstado.Text = mensaje;
        });
    }

    private void Editar()
    {
        if (_sprint == null) return;
        using var frm = new SprintDetailForm(_sprint);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        Ejecutar(() =>
        {
            var (ok, mensaje) = _sprints.Actualizar(_sprint.Id, frm.Nombre, frm.Objetivo, frm.Inicio, frm.Fin);
            if (!ok) { Avisar(mensaje); return; }
            LoadSprints(_sprint.Id);
            _lblEstado.Text = mensaje;
        });
    }

    private void ElegirRequerimientos()
    {
        if (_sprint == null) return;
        using var frm = new SprintRequirementsPickerForm(_db, _sprint.Id, _sprint.Name);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        Ejecutar(() =>
        {
            var (ok, mensaje) = _sprints.FijarRequerimientos(_sprint.Id, frm.Seleccionados);
            if (!ok) { Avisar(mensaje); return; }
            LoadSeguimiento();
            _lblEstado.Text = mensaje;
        });
    }

    private void ExportarExcel()
    {
        if (_sprint == null || _avance == null) return;

        var path = _report.PromptSaveDialog($"Sprint_{Limpiar(_sprint.Name)}_{_sprint.StartDate:yyyyMMdd}");
        if (path == null) return;

        var a = _avance;
        // La cabecera del sprint va como filas antes del detalle: quien abra el archivo dentro de
        // tres meses tiene que poder saber de qué sprint hablamos y cómo iba AL EXPORTARLO — el
        // avance es una foto del día, no un dato del requerimiento.
        var filas = new List<object?[]>
        {
            new object?[] { "Sprint",           _sprint.Name },
            new object?[] { "Objetivo",         _sprint.Goal ?? "—" },
            new object?[] { "Periodo",          $"{_sprint.StartDate:dd/MM/yyyy} – {_sprint.EndDate:dd/MM/yyyy}  ({a.DiasTotales} días)" },
            new object?[] { "Estado",           a.Veredicto },
            new object?[] { "Avance real",      $"{a.AvanceRealPct}%" },
            new object?[] { "Tiempo consumido", $"{a.TiempoPct}%  ({a.DiasTranscurridos} de {a.DiasTotales} días)" },
            new object?[] { "Entregados",       $"{a.Entregados} de {a.TotalRequerimientos}" },
            new object?[] { "En curso",         a.EnCurso },
            new object?[] { "Sin empezar",      a.SinEmpezar },
            new object?[] { "Cancelados",       a.Cancelados },
            new object?[] { "Exportado",        DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
            new object?[] { null, null },
            new object?[] { "Estado", "Requerimiento", "% avance", "Compromiso", "Entregado", "Estimado (h)" },
        };
        foreach (var r in _reqs)
            filas.Add(new object?[]
            {
                EtiquetaEstado(r.Status),
                r.Title,
                r.Status == RequirementStatus.Entregado ? 100 : r.ProgressPercent,
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "",
                r.ActualDeliveryDate?.ToString("dd/MM/yyyy") ?? "",
                r.EstimateHours
            });

        try
        {
            // Encabezado de dos columnas porque la primera mitad del archivo son pares
            // dato/valor; el detalle trae su propia fila de títulos.
            _report.ExportToExcel(filas, ["Sprint", _sprint.Name], f => f, "Sprint", path);
        }
        catch (Exception ex)
        {
            Avisar($"No se pudo exportar:\n{ex.Message}");
            return;
        }

        _lblEstado.Text = $"Exportado a {path}";
        if (MessageBox.Show($"Exportado:\n{path}\n\n¿Abrirlo ahora?", "Listo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Quita del nombre lo que Windows no admite en un archivo.</summary>
    private static string Limpiar(string nombre) =>
        string.Concat(nombre.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');

    private void Eliminar()
    {
        if (_sprint == null) return;
        if (MessageBox.Show(
                $"¿Eliminar el sprint «{_sprint.Name}»?\n\nSus requerimientos NO se borran: vuelven al backlog.",
                "Eliminar sprint", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Ejecutar(() =>
        {
            var (ok, mensaje) = _sprints.Eliminar(_sprint.Id);
            if (!ok) { Avisar(mensaje); return; }
            LoadSprints();
            _lblEstado.Text = mensaje;
        });
    }

    // El seguimiento envejece con el día: al volver a la pantalla se recalcula contra HOY.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) return;
        if (_lista.Count > 0) LoadSeguimiento();
        if (_esAdmin) LoadHistorico();   // «cerrado» depende de la fecha de hoy: envejece solo
    }

    private void Ejecutar(Action accion)
    {
        try { accion(); }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void Avisar(string mensaje) =>
        MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
