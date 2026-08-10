using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// El pool de actividades, del lado del desarrollador: lo que hay libre para tomar y lo que ya se
/// tomó.
///
/// Los puntos se ven ANTES de tomar nada, junto al título. Es la diferencia con la autocalificación
/// libre: aquí nadie trabaja sin saber cuánto vale lo que va a hacer, ni tiene que argumentar
/// después para que se lo reconozcan. El checklist tampoco es un trámite: es exactamente lo que hay
/// que cumplir para entregarla, escrito de antemano.
/// </summary>
public class MyPoolControl : UserControl
{
    private readonly PoolActivityService _pool;
    private readonly ICurrentUser _currentUser;

    private DataGridView _gridDisponibles = null!, _gridMias = null!, _gridChecklist = null!;
    private ComboBox _cbxTipo = null!;
    private Label _lblDisponibles = null!, _lblMias = null!, _lblDetalle = null!;

    private List<PoolActivity> _disponibles = [];
    private List<PoolActivity> _mias = [];
    private List<PoolActivityChecklistItem> _checklist = [];

    private const string ClaveColumnasDisponibles = "mypool.disponibles";
    private const string ClaveColumnasMias = "mypool.mias";

    public MyPoolControl(PoolActivityService pool, ICurrentUser currentUser)
    {
        _pool = pool; _currentUser = currentUser;
        BuildUI();
        LoadData();
    }

    private int? DevId => _currentUser.DeveloperId;

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildMiasTab());          // primero lo propio: es a lo que se vuelve a diario
        tabs.TabPages.Add(BuildDisponiblesTab());
        Controls.Add(tabs);
    }

    // ── Mis actividades ──────────────────────────────────────────────────────────
    private TabPage BuildMiasTab()
    {
        var page = new TabPage("  🧑‍💻  Mis actividades del pool  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = Tabla();
        var toolbar = Toolbar();
        toolbar.Controls.Add(Boton("✔ Marcar punto", 145, BtnMarcar_Click, primario: true));
        toolbar.Controls.Add(Boton("✖ Desmarcar", 130, BtnDesmarcar_Click));
        toolbar.Controls.Add(Boton("📤 Entregar", 130, BtnEntregar_Click));
        toolbar.Controls.Add(Boton("↩ Devolver al pool", 165, BtnDevolver_Click));
        toolbar.Controls.Add(Boton("🔄 Recargar", 115, (_, _) => LoadData()));

        _gridMias = AppTheme.MakeGrid();
        _gridMias.Columns.Add(Col("Actividad", "Titulo", 34));
        _gridMias.Columns.Add(Col("Tipo", "Tipo", 11));
        _gridMias.Columns.Add(Col("Puntos", "Puntos", 9));
        _gridMias.Columns.Add(Col("Estado", "Estado", 18));
        _gridMias.Columns.Add(Col("Entregar antes de", "Limite", 14));
        _gridMias.Columns.Add(Col("Checklist", "Avance", 14));
        GridColumns.Habilitar(_gridMias, ClaveColumnasMias);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridMias, ClaveColumnasMias));
        _gridMias.SelectionChanged += (_, _) => PintarChecklist();

        _gridChecklist = AppTheme.MakeGrid();
        _gridChecklist.Columns.Add(Col("", "Hecho", 6));
        _gridChecklist.Columns.Add(Col("Qué hay que cumplir", "Texto", 52));
        _gridChecklist.Columns.Add(Col("¿Exige enlace?", "Exige", 12));
        _gridChecklist.Columns.Add(Col("Evidencia", "Evidencia", 30));

        _lblDetalle = new Label
        {
            Dock = DockStyle.Top, Height = 42, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, Padding = new Padding(4, 6, 4, 0)
        };

        var partido = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = AppTheme.ContentBg
        };
        partido.Panel1.Controls.Add(_gridMias);
        partido.Panel1.Padding = new Padding(0, 4, 0, 4);
        partido.Panel2.Controls.Add(_gridChecklist);
        partido.Panel2.Controls.Add(_lblDetalle);
        partido.Panel2.Padding = new Padding(0, 4, 0, 4);

        _lblMias = LineaEstado();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(partido, 0, 1);
        tbl.Controls.Add(_lblMias, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Disponibles ──────────────────────────────────────────────────────────────
    private TabPage BuildDisponiblesTab()
    {
        var page = new TabPage("  🎯  Disponibles  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = Tabla();
        var toolbar = Toolbar();

        toolbar.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxTipo = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        _cbxTipo.Items.Add("Todos");
        foreach (var t in Enum.GetValues<PoolWorkType>()) _cbxTipo.Items.Add(PoolSeed.Etiqueta(t));
        _cbxTipo.SelectedIndex = 0;
        _cbxTipo.SelectedIndexChanged += (_, _) => LoadDisponibles();
        toolbar.Controls.Add(_cbxTipo);

        toolbar.Controls.Add(Boton("🙋 Tomar esta", 145, BtnTomar_Click, primario: true));
        toolbar.Controls.Add(Boton("🔗 Ver el ticket", 150, BtnAbrirEnlace_Click));
        toolbar.Controls.Add(Boton("🔄 Recargar", 115, (_, _) => LoadDisponibles()));

        _gridDisponibles = AppTheme.MakeGrid();
        _gridDisponibles.Columns.Add(Col("Actividad", "Titulo", 42));
        _gridDisponibles.Columns.Add(Col("Tipo", "Tipo", 13));
        _gridDisponibles.Columns.Add(Col("Complejidad", "Complejidad", 14));
        _gridDisponibles.Columns.Add(Col("Puntos", "Puntos", 10));
        _gridDisponibles.Columns.Add(Col("Detalle", "Detalle", 21));
        GridColumns.Habilitar(_gridDisponibles, ClaveColumnasDisponibles);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridDisponibles, ClaveColumnasDisponibles));

        _lblDisponibles = LineaEstado();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(Envolver(_gridDisponibles), 0, 1);
        tbl.Controls.Add(_lblDisponibles, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private static TableLayoutPanel Tabla()
    {
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return tbl;
    }

    private static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        Padding = new Padding(0, 6, 0, 6), BackColor = AppTheme.ContentBg, AutoScroll = true
    };

    private static Button Boton(string texto, int ancho, EventHandler alPulsar, bool primario = false)
    {
        var b = primario ? AppTheme.MakePrimaryButton(texto, ancho, 28) : AppTheme.MakeSecondaryButton(texto, ancho, 28);
        b.Margin = new Padding(0, 3, 6, 0);
        b.Click += alPulsar;
        return b;
    }

    private static DataGridViewTextBoxColumn Col(string encabezado, string nombre, int peso) =>
        new() { HeaderText = encabezado, Name = nombre, FillWeight = peso, SortMode = DataGridViewColumnSortMode.NotSortable };

    private static Panel Envolver(Control c)
    {
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(c);
        return pnl;
    }

    private static Label LineaEstado() => new()
    {
        Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0)
    };

    // ── Datos ────────────────────────────────────────────────────────────────────

    private void LoadData() { LoadDisponibles(); LoadMias(); }

    private void LoadDisponibles()
    {
        PoolWorkType? tipo = _cbxTipo.SelectedIndex > 0 ? (PoolWorkType)(_cbxTipo.SelectedIndex - 1) : null;

        try { _disponibles = _pool.Disponibles(tipo); }
        catch (AuthorizationException ex) { _lblDisponibles.Text = ex.Message; return; }
        catch (Exception ex) { _lblDisponibles.Text = $"No se pudo leer el pool ({ex.Message})."; return; }

        _gridDisponibles.Rows.Clear();
        foreach (var a in _disponibles)
        {
            int i = _gridDisponibles.Rows.Add(
                a.Title, PoolSeed.Etiqueta(a.WorkType), PoolSeed.Etiqueta(a.Complexity),
                a.Points, Resumir(a.Description));

            // Los puntos en verde y en negrita: es el dato por el que se elige qué tomar.
            var celda = _gridDisponibles.Rows[i].Cells["Puntos"].Style;
            celda.Font = AppTheme.BoldFont;
            celda.ForeColor = AppTheme.Success;
            celda.SelectionForeColor = AppTheme.Success;
        }

        _lblDisponibles.Text = _disponibles.Count == 0
            ? "No hay actividades libres en el pool ahora mismo."
            : $"{_disponibles.Count} actividad(es) libres por {_disponibles.Sum(a => a.Points)} puntos en total. " +
              "Al tomar una, el valor queda fijado: es lo que se te abona al aceptarla.";
    }

    private void LoadMias()
    {
        if (DevId is not int dev)
        {
            _lblMias.Text = "Tu cuenta no tiene ficha de desarrollador ligada: pídeselo al líder.";
            return;
        }

        try { _mias = _pool.MisDelPool(dev); }
        catch (AuthorizationException ex) { _lblMias.Text = ex.Message; return; }
        catch (Exception ex) { _lblMias.Text = $"No se pudo leer ({ex.Message})."; return; }

        _gridMias.Rows.Clear();
        foreach (var a in _mias)
        {
            var avance = a.EnCurso ? Avance(a.Id) : "";
            int i = _gridMias.Rows.Add(
                a.Title,
                PoolSeed.Etiqueta(a.WorkType),
                a.Points,
                PoolSeed.Etiqueta(a.Status),
                a.ClaimDeadlineAt is DateTime f ? f.ToLocalTime().ToString("dd/MM/yyyy") : "",
                avance);

            if (a.Vencida) Pintar(_gridMias.Rows[i], "Limite", AppTheme.Warning);
            if (a.Status == PoolActivityStatus.Devuelta) Pintar(_gridMias.Rows[i], "Estado", AppTheme.Warning);
            if (a.Status == PoolActivityStatus.Aceptada)
            {
                Pintar(_gridMias.Rows[i], "Puntos", AppTheme.Success);
                _gridMias.Rows[i].Cells["Puntos"].Style.Font = AppTheme.BoldFont;
            }
        }

        int ganados = _mias.Where(a => a.Status == PoolActivityStatus.Aceptada).Sum(a => a.Points);
        int enJuego = _mias.Where(a => a.Status != PoolActivityStatus.Aceptada).Sum(a => a.Points);
        int enCurso = _mias.Count(a => a.EnCurso);
        _lblMias.Text = _mias.Count == 0
            ? "Todavía no has tomado ninguna actividad del pool. Búscalas en la pestaña «Disponibles»."
            : $"{enCurso} en curso  ·  {enJuego} punto(s) por ganar  ·  {ganados} ya aceptados.";

        PintarChecklist();
    }

    private string Avance(int actividadId)
    {
        var items = _pool.ChecklistDe(actividadId);
        if (items.Count == 0) return "";
        return $"{items.Count(c => c.IsDone)}/{items.Count}";
    }

    private void PintarChecklist()
    {
        _gridChecklist.Rows.Clear();
        _checklist = [];

        var actividad = Seleccionada(_gridMias, _mias);
        if (actividad == null)
        {
            _lblDetalle.Text = "Selecciona una actividad para ver qué le falta.";
            return;
        }

        _checklist = _pool.ChecklistDe(actividad.Id);
        foreach (var c in _checklist)
            _gridChecklist.Rows.Add(c.IsDone ? "✔" : "○", c.Text,
                c.RequiereEvidencia ? "Sí" : "", c.EvidenceUrl ?? "");

        // Si el líder la devolvió, su motivo es lo primero que hay que leer.
        _lblDetalle.Text = actividad.Status == PoolActivityStatus.Devuelta && !string.IsNullOrWhiteSpace(actividad.ReviewComment)
            ? $"↩ Te la devolvieron: {actividad.ReviewComment}"
            : $"{actividad.Title} — {actividad.Points} pts. " +
              (actividad.Status == PoolActivityStatus.EnRevision
                  ? "Entregada, esperando la verificación del líder."
                  : actividad.Status == PoolActivityStatus.Aceptada
                      ? "Aceptada: los puntos ya están en tu ranking."
                      : "Completa todos los puntos para poder entregarla.");
    }

    private static void Pintar(DataGridViewRow fila, string columna, Color color)
    {
        var celda = fila.Cells[columna].Style;
        celda.ForeColor = color;
        celda.SelectionForeColor = color;
    }

    private static T? Seleccionada<T>(DataGridView grid, List<T> origen) where T : class =>
        grid.CurrentRow is { Index: >= 0 } fila && fila.Index < origen.Count ? origen[fila.Index] : null;

    private static string Resumir(string? texto)
    {
        texto = (texto ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return texto.Length <= 80 ? texto : texto[..80] + "…";
    }

    // ── Acciones ─────────────────────────────────────────────────────────────────

    private void BtnTomar_Click(object? sender, EventArgs e)
    {
        if (DevId is not int dev) { Avisar("Tu cuenta no tiene ficha de desarrollador ligada."); return; }

        var actividad = Seleccionada(_gridDisponibles, _disponibles);
        if (actividad == null) { Avisar("Selecciona una actividad del pool."); return; }

        if (MessageBox.Show(
                $"¿Tomar «{actividad.Title}»?\n\n" +
                $"Vale {actividad.Points} puntos y queda a tu nombre. Puedes devolverla al pool " +
                "si no vas a poder con ella; eso no te resta nada.",
                "Tomar actividad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Ejecutar(() => _pool.Tomar(actividad.Id, dev));
    }

    private void BtnMarcar_Click(object? sender, EventArgs e) => MarcarPunto(hecho: true);
    private void BtnDesmarcar_Click(object? sender, EventArgs e) => MarcarPunto(hecho: false);

    private void MarcarPunto(bool hecho)
    {
        if (DevId is not int dev) { Avisar("Tu cuenta no tiene ficha de desarrollador ligada."); return; }

        var actividad = Seleccionada(_gridMias, _mias);
        var item = Seleccionada(_gridChecklist, _checklist);
        if (actividad == null || item == null) { Avisar("Selecciona el punto del checklist."); return; }

        string? evidencia = item.EvidenceUrl;
        if (hecho && item.RequiereEvidencia)
        {
            evidencia = EntradaDeTextoSimple.Pedir(FindForm(), "Enlace de evidencia",
                $"«{item.Text}» necesita un enlace que lo respalde " +
                "(el pull request, el work item o el ticket).",
                item.EvidenceUrl ?? "");
            if (evidencia == null) return;
        }

        Ejecutar(() => _pool.MarcarItem(item.Id, dev, hecho, evidencia), avisarExito: false);
    }

    private void BtnEntregar_Click(object? sender, EventArgs e)
    {
        if (DevId is not int dev) { Avisar("Tu cuenta no tiene ficha de desarrollador ligada."); return; }

        var actividad = Seleccionada(_gridMias, _mias);
        if (actividad == null) { Avisar("Selecciona una actividad tuya."); return; }

        Ejecutar(() => _pool.Entregar(actividad.Id, dev));
    }

    private void BtnDevolver_Click(object? sender, EventArgs e)
    {
        if (DevId is not int dev) { Avisar("Tu cuenta no tiene ficha de desarrollador ligada."); return; }

        var actividad = Seleccionada(_gridMias, _mias);
        if (actividad == null) { Avisar("Selecciona una actividad tuya."); return; }

        var motivo = EntradaDeTextoSimple.Pedir(FindForm(), "Devolver al pool",
            $"«{actividad.Title}» vuelve al pool y cualquiera podrá tomarla.\n\n" +
            "¿Por qué la devuelves? (opcional)");
        if (motivo == null) return;

        Ejecutar(() => _pool.Devolver(actividad.Id, dev, motivo));
    }

    private void BtnAbrirEnlace_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridDisponibles, _disponibles);
        if (string.IsNullOrWhiteSpace(actividad?.ExternalUrl))
        {
            Avisar("Esa actividad no trae enlace.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(actividad.ExternalUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Avisar($"No se pudo abrir el enlace: {ex.Message}"); }
    }

    private void Ejecutar(Func<(bool ok, string mensaje)> operacion, bool avisarExito = true)
    {
        try
        {
            var (ok, mensaje) = operacion();
            if (!ok || avisarExito)
                MessageBox.Show(mensaje, ok ? "Listo" : "No se pudo",
                    MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok) LoadData();
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo completar la operación:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Avisar(string mensaje) =>
        MessageBox.Show(mensaje, "Pool de actividades", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadData();
    }
}
