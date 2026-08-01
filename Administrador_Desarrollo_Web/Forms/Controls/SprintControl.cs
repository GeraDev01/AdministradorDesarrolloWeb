using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Seguimiento del sprint, SOLO administrador: una línea de tiempo tipo calendario (día por día,
/// fines de semana sombreados, marcador de HOY) con dos barras comparables — el tiempo consumido
/// y el avance real de los requerimientos — y debajo la lista de lo comprometido.
///
/// La lectura es una sola: si la barra de avance va detrás de la de tiempo, vamos atrasados. Todo
/// el cálculo vive en <see cref="SprintService.CalcularAvance"/>; aquí solo se dibuja.
/// </summary>
public class SprintControl : UserControl
{
    private readonly SprintService _sprints;
    private readonly AppDbContext _db;

    private ComboBox _cbxSprint = null!;
    private Panel _pnlTimeline = null!;
    private DataGridView _grid = null!;
    private Label _lblEstado = null!, _lblObjetivo = null!;
    private Label _kpiVeredicto = null!, _kpiAvance = null!, _kpiTiempo = null!, _kpiEntregados = null!, _kpiDias = null!;
    private Button _btnEditar = null!, _btnEliminar = null!, _btnReqs = null!;

    private List<Sprint> _lista = [];
    private Sprint? _sprint;
    private List<Requirement> _reqs = [];
    private SprintAvance? _avance;

    public SprintControl(SprintService sprints, AppDbContext db)
    {
        _sprints = sprints; _db = db;
        BuildUI();
        LoadSprints();
    }

    // ── UI ───────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "Estado",  FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requerimiento", Name = "Titulo", FillWeight = 44 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "%",            Name = "Pct",     FillWeight = 8,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Compromiso",   Name = "Comp",    FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Entregado",    Name = "Entr",    FillWeight = 15 });
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
        Controls.Add(tbl);
    }

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
            _avance = SprintService.CalcularAvance(_sprint, _reqs, DateTime.Today);
        }
        catch (AuthorizationException ex) { _lblEstado.Text = ex.Message; return; }
        PintarSprint();
    }

    private void PintarSprint()
    {
        bool hay = _sprint != null;
        _btnEditar.Enabled = _btnEliminar.Enabled = _btnReqs.Enabled = hay;
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
        foreach (var r in _reqs)
        {
            int i = _grid.Rows.Add(
                EtiquetaEstado(r.Status),
                r.Title,
                r.Status == RequirementStatus.Entregado ? "100%" : $"{r.ProgressPercent}%",
                r.CommittedDeliveryDate?.ToString("dd/MM") ?? "—",
                r.ActualDeliveryDate?.ToString("dd/MM") ?? "—");

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

        _lblEstado.Text = _reqs.Count == 0
            ? "Este sprint no tiene requerimientos: 📋 Requerimientos… para colgarle trabajo."
            : $"{a.TotalRequerimientos} requerimiento(s)" +
              (a.Cancelados > 0 ? $" (+{a.Cancelados} cancelado(s), fuera del cálculo)" : "") +
              $"  ·  {a.EnCurso} en curso  ·  {a.SinEmpezar} sin empezar.  " +
              "En la línea de tiempo: ▲ entregado, △ compromiso, sombreado = fin de semana.";

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
        if (Visible && _lista.Count > 0) LoadSeguimiento();
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
