using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// El pool de actividades, del lado del líder: publicar trabajo con su valor, verificar lo entregado
/// y configurar cuánto vale cada cosa.
///
/// La pestaña de configuración es la que define el sistema entero: la matriz tipo × complejidad y
/// los checklists por tipo. Se toca una vez y luego casi nunca — pero es lo que hace que publicar
/// una actividad no sea una decisión personal caso por caso.
/// </summary>
public class PoolAdminControl : UserControl
{
    private readonly PoolActivityService _pool;

    // Pool
    private DataGridView _gridPool = null!;
    private ComboBox _cbxEstado = null!, _cbxTipo = null!;
    private Label _lblPool = null!;
    private List<PoolActivity> _actividades = [];

    // Verificación
    private DataGridView _gridPendientes = null!, _gridChecklist = null!;
    private Label _lblPendientes = null!, _lblDetalle = null!;
    private List<PoolActivity> _pendientes = [];

    // Configuración
    private DataGridView _gridMatriz = null!, _gridPlantilla = null!;
    private ComboBox _cbxTipoPlantilla = null!;
    private Label _lblConfig = null!;
    private List<PoolPointsMatrixEntry> _matriz = [];
    private List<PoolChecklistTemplateItem> _plantilla = [];

    private const string ClaveColumnasPool = "pool.actividades";
    private const string ClaveColumnasPendientes = "pool.pendientes";

    /// <summary>Avisa cuando cambia cuántas esperan verificación, para el contador del menú.</summary>
    public event Action? PendingCountChanged;

    public PoolAdminControl(PoolActivityService pool)
    {
        _pool = pool;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont };
        tabs.TabPages.Add(BuildPoolTab());
        tabs.TabPages.Add(BuildVerificacionTab());
        tabs.TabPages.Add(BuildConfiguracionTab());
        Controls.Add(tabs);
    }

    // ── Pool ─────────────────────────────────────────────────────────────────────
    private TabPage BuildPoolTab()
    {
        var page = new TabPage("  🎯  Pool  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = Tabla();
        var toolbar = Toolbar();

        toolbar.Controls.Add(new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxEstado = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        _cbxEstado.Items.Add("Todos");
        foreach (var e in Enum.GetValues<PoolActivityStatus>()) _cbxEstado.Items.Add(PoolSeed.Etiqueta(e));
        _cbxEstado.SelectedIndex = 0;
        _cbxEstado.SelectedIndexChanged += (_, _) => LoadPool();
        toolbar.Controls.Add(_cbxEstado);

        toolbar.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxTipo = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        _cbxTipo.Items.Add("Todos");
        foreach (var t in Enum.GetValues<PoolWorkType>()) _cbxTipo.Items.Add(PoolSeed.Etiqueta(t));
        _cbxTipo.SelectedIndex = 0;
        _cbxTipo.SelectedIndexChanged += (_, _) => LoadPool();
        toolbar.Controls.Add(_cbxTipo);

        toolbar.Controls.Add(Boton("➕ Publicar", 130, BtnPublicar_Click, primario: true));
        toolbar.Controls.Add(Boton("✏ Editar", 110, BtnEditar_Click));
        toolbar.Controls.Add(Boton("🗑 Retirar", 110, BtnRetirar_Click));
        toolbar.Controls.Add(Boton("↩ Liberar", 110, BtnLiberar_Click));
        toolbar.Controls.Add(Boton("🔄 Recargar", 115, (_, _) => LoadData()));

        _gridPool = AppTheme.MakeGrid();
        _gridPool.Columns.Add(Col("Actividad", "Titulo", 30));
        _gridPool.Columns.Add(Col("Tipo", "Tipo", 11));
        _gridPool.Columns.Add(Col("Complejidad", "Complejidad", 12));
        _gridPool.Columns.Add(Col("Puntos", "Puntos", 8));
        _gridPool.Columns.Add(Col("Estado", "Estado", 13));
        _gridPool.Columns.Add(Col("Quién la tiene", "Dev", 16));
        _gridPool.Columns.Add(Col("Entrega esperada", "Limite", 13));
        _gridPool.Columns.Add(Col("Devoluciones", "Devoluciones", 10));
        GridColumns.Habilitar(_gridPool, ClaveColumnasPool);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridPool, ClaveColumnasPool));

        _lblPool = LineaEstado();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(Envolver(_gridPool), 0, 1);
        tbl.Controls.Add(_lblPool, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Verificación ─────────────────────────────────────────────────────────────
    private TabPage BuildVerificacionTab()
    {
        var page = new TabPage("  ✅  Verificación  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = Tabla();
        var toolbar = Toolbar();
        toolbar.Controls.Add(Boton("✅ Aceptar", 130, BtnAceptar_Click, primario: true));
        toolbar.Controls.Add(Boton("↩ Devolver", 130, BtnDevolver_Click));
        toolbar.Controls.Add(Boton("🔗 Abrir evidencia", 160, BtnAbrirEvidencia_Click));
        toolbar.Controls.Add(Boton("🔄 Recargar", 115, (_, _) => LoadPendientes()));

        _gridPendientes = AppTheme.MakeGrid();
        _gridPendientes.Columns.Add(Col("Actividad", "Titulo", 34));
        _gridPendientes.Columns.Add(Col("Tipo", "Tipo", 12));
        _gridPendientes.Columns.Add(Col("Puntos", "Puntos", 9));
        _gridPendientes.Columns.Add(Col("Quién la entregó", "Dev", 22));
        _gridPendientes.Columns.Add(Col("Entregada", "Entregada", 13));
        _gridPendientes.Columns.Add(Col("Vuelta", "Vuelta", 10));
        GridColumns.Habilitar(_gridPendientes, ClaveColumnasPendientes);
        toolbar.Controls.Add(GridColumns.CrearBoton(_gridPendientes, ClaveColumnasPendientes));
        _gridPendientes.SelectionChanged += (_, _) => PintarChecklist();

        // El checklist con su evidencia es LO que se verifica: por eso está siempre a la vista y no
        // detrás de un botón. Verificar es mirar esto, no discutir cuánto vale la actividad.
        _gridChecklist = AppTheme.MakeGrid();
        _gridChecklist.Columns.Add(Col("", "Hecho", 6));
        _gridChecklist.Columns.Add(Col("Punto del checklist", "Texto", 58));
        _gridChecklist.Columns.Add(Col("Evidencia", "Evidencia", 36));

        var partido = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 240, BackColor = AppTheme.ContentBg
        };
        partido.Panel1.Controls.Add(_gridPendientes);
        partido.Panel1.Padding = new Padding(0, 4, 0, 4);

        _lblDetalle = new Label
        {
            Dock = DockStyle.Top, Height = 40, Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextSecondary, Padding = new Padding(4, 6, 4, 0)
        };
        partido.Panel2.Controls.Add(_gridChecklist);
        partido.Panel2.Controls.Add(_lblDetalle);
        partido.Panel2.Padding = new Padding(0, 4, 0, 4);

        _lblPendientes = LineaEstado();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(partido, 0, 1);
        tbl.Controls.Add(_lblPendientes, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Configuración ────────────────────────────────────────────────────────────
    private TabPage BuildConfiguracionTab()
    {
        var page = new TabPage("  ⚙  Configuración  ") { BackColor = AppTheme.ContentBg, Padding = new Padding(8) };

        var tbl = Tabla();
        var toolbar = Toolbar();
        toolbar.Controls.Add(new Label
        {
            Text = "Cuánto vale cada actividad. Cambiarlo NO revalúa las ya publicadas.",
            AutoSize = true, Margin = new Padding(0, 8, 16, 0), ForeColor = AppTheme.TextSecondary
        });
        toolbar.Controls.Add(Boton("💾 Guardar matriz", 160, BtnGuardarMatriz_Click, primario: true));

        _gridMatriz = AppTheme.MakeGrid();
        _gridMatriz.ReadOnly = false;
        _gridMatriz.EditMode = DataGridViewEditMode.EditOnEnter;
        _gridMatriz.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo", Name = "Tipo", FillWeight = 26, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _gridMatriz.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Complejidad", Name = "Complejidad", FillWeight = 26, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _gridMatriz.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos", Name = "Puntos", FillWeight = 24, SortMode = DataGridViewColumnSortMode.NotSortable });
        _gridMatriz.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Días para entregar", Name = "Dias", FillWeight = 24, SortMode = DataGridViewColumnSortMode.NotSortable });

        var panelPlantilla = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg };
        var barraPlantilla = Toolbar();
        barraPlantilla.Dock = DockStyle.Top;
        barraPlantilla.Height = 44;
        barraPlantilla.Controls.Add(new Label { Text = "Checklist de:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxTipoPlantilla = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };
        foreach (var t in Enum.GetValues<PoolWorkType>()) _cbxTipoPlantilla.Items.Add(PoolSeed.Etiqueta(t));
        _cbxTipoPlantilla.SelectedIndex = 0;
        _cbxTipoPlantilla.SelectedIndexChanged += (_, _) => LoadPlantilla();
        barraPlantilla.Controls.Add(_cbxTipoPlantilla);
        barraPlantilla.Controls.Add(Boton("➕ Agregar punto", 155, BtnAgregarPunto_Click));
        barraPlantilla.Controls.Add(Boton("✏ Editar punto", 145, BtnEditarPunto_Click));
        barraPlantilla.Controls.Add(Boton("🚫 Activar/desactivar", 185, BtnDesactivarPunto_Click));

        _gridPlantilla = AppTheme.MakeGrid();
        _gridPlantilla.Columns.Add(Col("Qué hay que cumplir", "Texto", 58));
        _gridPlantilla.Columns.Add(Col("¿Exige enlace?", "Evidencia", 22));
        _gridPlantilla.Columns.Add(Col("Estado", "Activo", 20));

        panelPlantilla.Controls.Add(_gridPlantilla);
        panelPlantilla.Controls.Add(barraPlantilla);

        var partido = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = AppTheme.ContentBg
        };
        partido.Panel1.Controls.Add(_gridMatriz);
        partido.Panel1.Padding = new Padding(0, 4, 4, 4);
        partido.Panel2.Controls.Add(panelPlantilla);
        partido.Panel2.Padding = new Padding(4, 4, 0, 4);

        _lblConfig = LineaEstado();

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(partido, 0, 1);
        tbl.Controls.Add(_lblConfig, 0, 2);
        page.Controls.Add(tbl);
        return page;
    }

    // ── Andamiaje común ──────────────────────────────────────────────────────────

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

    private void LoadData() { LoadPool(); LoadPendientes(); LoadConfiguracion(); }

    private void LoadPool()
    {
        PoolActivityStatus? estado = _cbxEstado.SelectedIndex > 0 ? (PoolActivityStatus)(_cbxEstado.SelectedIndex - 1) : null;
        PoolWorkType? tipo = _cbxTipo.SelectedIndex > 0 ? (PoolWorkType)(_cbxTipo.SelectedIndex - 1) : null;

        if (!Cargar(() => _actividades = _pool.Todas(estado, tipo), _lblPool)) return;

        _gridPool.Rows.Clear();
        foreach (var a in _actividades)
        {
            int i = _gridPool.Rows.Add(
                a.Title,
                PoolSeed.Etiqueta(a.WorkType),
                PoolSeed.Etiqueta(a.Complexity),
                a.Points,
                PoolSeed.Etiqueta(a.Status),
                a.ClaimedBy?.FullName ?? "",
                a.ClaimDeadlineAt is DateTime f ? f.ToLocalTime().ToString("dd/MM/yyyy") : "",
                a.ReturnedCount > 0 ? a.ReturnedCount.ToString() : "");

            if (a.Vencida) Pintar(_gridPool.Rows[i], "Limite", AppTheme.Warning);
            if (a.Status == PoolActivityStatus.EnRevision) Pintar(_gridPool.Rows[i], "Estado", AppTheme.Success);
            if (a.Status is PoolActivityStatus.Aceptada or PoolActivityStatus.Retirada)
            {
                _gridPool.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
                _gridPool.Rows[i].DefaultCellStyle.SelectionForeColor = AppTheme.TextSecondary;
            }
        }

        int libres = _actividades.Count(a => a.Status == PoolActivityStatus.Disponible);
        int puntosLibres = _actividades.Where(a => a.Status == PoolActivityStatus.Disponible).Sum(a => a.Points);
        int vencidas = _actividades.Count(a => a.Vencida);
        _lblPool.Text = $"{_actividades.Count} actividad(es)  ·  {libres} libre(s) en el pool por {puntosLibres} puntos." +
                        (vencidas > 0 ? $"  ⚠ {vencidas} pasó su fecha de entrega." : "");
    }

    private void LoadPendientes()
    {
        if (!Cargar(() => _pendientes = _pool.PendientesDeVerificar(), _lblPendientes)) return;

        _gridPendientes.Rows.Clear();
        foreach (var a in _pendientes)
        {
            int i = _gridPendientes.Rows.Add(
                a.Title,
                PoolSeed.Etiqueta(a.WorkType),
                a.Points,
                a.ClaimedBy?.FullName ?? "",
                a.DeliveredAt is DateTime f ? f.ToLocalTime().ToString("dd/MM HH:mm") : "",
                a.ReviewRound > 0 ? $"🔁 {a.ReviewRound + 1}ª" : "1ª");

            // Una segunda o tercera vuelta no es lo mismo que una entrega nueva: conviene saberlo
            // antes de abrir el checklist.
            if (a.ReviewRound > 0) Pintar(_gridPendientes.Rows[i], "Vuelta", AppTheme.Warning);
        }

        _lblPendientes.Text = _pendientes.Count == 0
            ? "No hay nada por verificar."
            : $"{_pendientes.Count} actividad(es) esperando verificación por " +
              $"{_pendientes.Sum(a => a.Points)} puntos en total.";

        PintarChecklist();
        PendingCountChanged?.Invoke();
    }

    private void PintarChecklist()
    {
        _gridChecklist.Rows.Clear();
        var actividad = Seleccionada(_gridPendientes, _pendientes);
        if (actividad == null)
        {
            _lblDetalle.Text = "Selecciona una actividad para ver su checklist y su evidencia.";
            return;
        }

        foreach (var item in _pool.ChecklistDe(actividad.Id))
            _gridChecklist.Rows.Add(item.IsDone ? "✔" : "○", item.Text, item.EvidenceUrl ?? "");

        var historial = string.IsNullOrWhiteSpace(actividad.ReviewHistory)
            ? ""
            : "  ·  " + actividad.ReviewHistory.Split('\n').Last();
        _lblDetalle.Text = $"{actividad.Title} — {actividad.Points} pts, " +
                           $"{PoolSeed.Etiqueta(actividad.WorkType)}/{PoolSeed.Etiqueta(actividad.Complexity)}{historial}";
    }

    private void LoadConfiguracion() { LoadMatriz(); LoadPlantilla(); }

    private void LoadMatriz()
    {
        if (!Cargar(() => _matriz = _pool.ObtenerMatriz(), _lblConfig)) return;

        _gridMatriz.Rows.Clear();
        foreach (var m in _matriz)
            _gridMatriz.Rows.Add(PoolSeed.Etiqueta(m.WorkType), PoolSeed.Etiqueta(m.Complexity), m.Points, m.DiasLimite);

        _lblConfig.Text = "Edita los puntos y los días, luego «Guardar matriz». " +
                          "Las actividades ya publicadas conservan el valor con el que salieron.";
    }

    private void LoadPlantilla()
    {
        var tipo = (PoolWorkType)_cbxTipoPlantilla.SelectedIndex;
        if (!Cargar(() => _plantilla = _pool.Plantilla(tipo, incluirInactivos: true), _lblConfig)) return;

        _gridPlantilla.Rows.Clear();
        foreach (var t in _plantilla)
        {
            int i = _gridPlantilla.Rows.Add(t.Text, t.RequiereEvidencia ? "Sí" : "", t.IsActive ? "Activo" : "Desactivado");
            if (!t.IsActive)
            {
                _gridPlantilla.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
                _gridPlantilla.Rows[i].DefaultCellStyle.SelectionForeColor = AppTheme.TextSecondary;
            }
        }
    }

    /// <summary>Ejecuta una carga y deja el motivo en la línea de estado si falla.</summary>
    private static bool Cargar(Action carga, Label linea)
    {
        try { carga(); return true; }
        catch (AuthorizationException ex) { linea.Text = ex.Message; return false; }
        catch (Exception ex) { linea.Text = $"No se pudo leer ({ex.Message})."; return false; }
    }

    private static void Pintar(DataGridViewRow fila, string columna, Color color)
    {
        var celda = fila.Cells[columna].Style;
        celda.ForeColor = color;
        celda.SelectionForeColor = color;
    }

    private static T? Seleccionada<T>(DataGridView grid, List<T> origen) where T : class =>
        grid.CurrentRow is { Index: >= 0 } fila && fila.Index < origen.Count ? origen[fila.Index] : null;

    // ── Acciones del pool ────────────────────────────────────────────────────────

    private void BtnPublicar_Click(object? sender, EventArgs e)
    {
        if (_matriz.Count == 0) LoadMatriz();
        using var dlg = new PoolActivityForm(_matriz);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() => { var (ok, msg, _) = _pool.Crear(dlg.Resultado); return (ok, msg); }, LoadPool);
    }

    private void BtnEditar_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridPool, _actividades);
        if (actividad == null) { Avisar("Selecciona una actividad."); return; }

        using var dlg = new PoolActivityForm(_matriz, actividad);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() => _pool.Editar(actividad.Id, dlg.Resultado), LoadPool);
    }

    private void BtnRetirar_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridPool, _actividades);
        if (actividad == null) { Avisar("Selecciona una actividad."); return; }

        if (MessageBox.Show($"¿Retirar «{actividad.Title}» del pool?", "Retirar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Ejecutar(() => _pool.Retirar(actividad.Id), LoadPool);
    }

    private void BtnLiberar_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridPool, _actividades);
        if (actividad == null) { Avisar("Selecciona una actividad."); return; }

        var motivo = EntradaDeTextoSimple.Pedir(FindForm(), "Liberar al pool",
            $"«{actividad.Title}» vuelve al pool y deja de estar a nombre de " +
            $"{actividad.ClaimedBy?.FullName ?? "quien la tenía"}.\n\n¿Por qué? (opcional, se le avisa)");
        if (motivo == null) return;   // canceló

        Ejecutar(() => _pool.Liberar(actividad.Id, motivo), LoadData);
    }

    // ── Acciones de verificación ─────────────────────────────────────────────────

    private void BtnAceptar_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridPendientes, _pendientes);
        if (actividad == null) { Avisar("Selecciona una actividad entregada."); return; }

        if (MessageBox.Show(
                $"¿Aceptar «{actividad.Title}»?\n\n" +
                $"Se le abonan {actividad.Points} puntos a {actividad.ClaimedBy?.FullName} " +
                "y cuentan de inmediato en el ranking del mes.",
                "Aceptar actividad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Ejecutar(() => _pool.Aceptar(actividad.Id), LoadData);
    }

    private void BtnDevolver_Click(object? sender, EventArgs e)
    {
        var actividad = Seleccionada(_gridPendientes, _pendientes);
        if (actividad == null) { Avisar("Selecciona una actividad entregada."); return; }

        var motivo = EntradaDeTextoSimple.Pedir(FindForm(), "Devolver para corregir",
            "¿Qué falta? Es lo que va a leer para arreglarlo.");
        if (string.IsNullOrWhiteSpace(motivo)) return;

        Ejecutar(() => _pool.Rechazar(actividad.Id, motivo), LoadData);
    }

    private void BtnAbrirEvidencia_Click(object? sender, EventArgs e)
    {
        var fila = _gridChecklist.CurrentRow;
        var url = fila?.Cells["Evidencia"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            Avisar("Selecciona un punto del checklist que tenga enlace.");
            return;
        }

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Avisar($"No se pudo abrir el enlace: {ex.Message}"); }
    }

    // ── Acciones de configuración ────────────────────────────────────────────────

    private void BtnGuardarMatriz_Click(object? sender, EventArgs e)
    {
        _gridMatriz.EndEdit();

        var filas = new List<PoolPointsMatrixEntry>();
        for (int i = 0; i < _matriz.Count && i < _gridMatriz.Rows.Count; i++)
        {
            if (!int.TryParse(_gridMatriz.Rows[i].Cells["Puntos"].Value?.ToString(), out int puntos) ||
                !int.TryParse(_gridMatriz.Rows[i].Cells["Dias"].Value?.ToString(), out int dias))
            {
                Avisar($"Fila {i + 1}: los puntos y los días tienen que ser números enteros.");
                return;
            }

            filas.Add(new PoolPointsMatrixEntry
            {
                WorkType = _matriz[i].WorkType, Complexity = _matriz[i].Complexity,
                Points = puntos, DiasLimite = dias
            });
        }

        Ejecutar(() => _pool.GuardarMatriz(filas), LoadMatriz);
    }

    private void BtnAgregarPunto_Click(object? sender, EventArgs e)
    {
        var texto = EntradaDeTextoSimple.Pedir(FindForm(), "Nuevo punto del checklist",
            "¿Qué hay que cumplir para dar por terminada una actividad de este tipo?");
        if (string.IsNullOrWhiteSpace(texto)) return;

        bool evidencia = MessageBox.Show(
            "¿Este punto exige un enlace que lo respalde (PR, work item, ticket)?\n\n" +
            "Sin evidencia, marcarlo no cuesta nada y no hay qué verificar.",
            "¿Exige enlace?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        Ejecutar(() => _pool.GuardarPlantillaItem(new PoolChecklistTemplateItem
        {
            WorkType = (PoolWorkType)_cbxTipoPlantilla.SelectedIndex,
            Text = texto, RequiereEvidencia = evidencia
        }), LoadPlantilla);
    }

    private void BtnEditarPunto_Click(object? sender, EventArgs e)
    {
        var item = Seleccionada(_gridPlantilla, _plantilla);
        if (item == null) { Avisar("Selecciona un punto del checklist."); return; }

        var texto = EntradaDeTextoSimple.Pedir(FindForm(), "Editar punto", "Texto del punto:", item.Text);
        if (string.IsNullOrWhiteSpace(texto)) return;

        bool evidencia = MessageBox.Show("¿Exige un enlace que lo respalde?", "¿Exige enlace?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        Ejecutar(() => _pool.GuardarPlantillaItem(new PoolChecklistTemplateItem
        {
            Id = item.Id, WorkType = item.WorkType, Orden = item.Orden,
            Text = texto, RequiereEvidencia = evidencia
        }), LoadPlantilla);
    }

    private void BtnDesactivarPunto_Click(object? sender, EventArgs e)
    {
        var item = Seleccionada(_gridPlantilla, _plantilla);
        if (item == null) { Avisar("Selecciona un punto del checklist."); return; }

        Ejecutar(() => _pool.DesactivarPlantillaItem(item.Id), LoadPlantilla);
    }

    // ── Ejecución con mensajes ───────────────────────────────────────────────────

    private void Ejecutar(Func<(bool ok, string mensaje)> operacion, Action recargar)
    {
        try
        {
            var (ok, mensaje) = operacion();
            MessageBox.Show(mensaje, ok ? "Listo" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok) recargar();
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
