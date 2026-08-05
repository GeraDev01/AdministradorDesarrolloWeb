using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Limpieza de datos: borrar de un tirón los registros de prueba que se van acumulando en cada
/// apartado mientras se prepara la aplicación.
///
/// La alternativa era ir pantalla por pantalla borrando de a uno —cuando la pantalla tiene botón de
/// borrar, que no siempre— y descubrir a medias que una fila no se va porque otra tabla la
/// referencia, sin que nada diga cuál. Aquí cada apartado sabe qué arrastra y en qué orden.
///
/// La pantalla está construida para que sea difícil equivocarse: se ve el recuento antes de marcar,
/// lo que cada apartado se lleva por delante está escrito en su renglón, lo que ya está vacío no se
/// puede marcar, y confirmar exige escribir una palabra. Nada de esto sobra en una pantalla cuya
/// operación no tiene deshacer.
/// </summary>
public class DataCleanupControl : UserControl
{
    private readonly DataCleanupService _cleanup;

    private DataGridView _grid = null!;
    private TextBox _txtLog = null!;
    private Label _lblResumen = null!;
    private Button _btnRecontar = null!, _btnTodo = null!, _btnNada = null!, _btnBorrar = null!;
    private IReadOnlyDictionary<string, int> _cuentas = new Dictionary<string, int>();

    public DataCleanupControl(DataCleanupService cleanup)
    {
        _cleanup = cleanup;
        BuildUI();
        Load += async (_, _) => await RecontarAsync();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 132f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Encabezado: qué es esto y qué NO toca ────────────────────────────────────────
        var cab = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 0), BackColor = AppTheme.ContentBg };
        // Dock = Top apila al revés (el último agregado queda arriba), así que se agregan en orden
        // inverso al que se leen.
        //
        // Y el segundo renglón dice lo que la pantalla NO hace: quien viene a «dejar la base limpia»
        // necesita saber que la configuración sigue ahí, y que si busca borrarla, no es aquí.
        cab.Controls.Add(new Label
        {
            Text = "No se incluyen la configuración ni los secretos guardados: no son registros que se "
                 + "acumulen probando, y sin ellos la aplicación se queda sin conexión ni credenciales.",
            Dock = DockStyle.Top, AutoSize = false, Height = 32,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        cab.Controls.Add(new Label
        {
            Text = "Borra TODOS los registros de los apartados que marques — no solo los de prueba. "
                 + "No hay deshacer ni papelera.",
            Dock = DockStyle.Top, AutoSize = false, Height = 20,
            Font = AppTheme.BoldFont, ForeColor = AppTheme.Danger
        });
        tbl.Controls.Add(cab, 0, 0);

        // ── Acciones ─────────────────────────────────────────────────────────────────────
        var barra = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg
        };
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));

        var izq = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        _btnRecontar = AppTheme.MakeSecondaryButton("🔄 Recontar", 120, 28);
        _btnRecontar.Margin = new Padding(0, 1, 6, 0);
        _btnRecontar.Click += async (_, _) => await RecontarAsync();
        _btnTodo = AppTheme.MakeSecondaryButton("Marcar lo que tenga datos", 200, 28);
        _btnTodo.Margin = new Padding(0, 1, 6, 0);
        _btnTodo.Click += (_, _) => MarcarTodo(true);
        _btnNada = AppTheme.MakeSecondaryButton("Desmarcar todo", 140, 28);
        _btnNada.Margin = new Padding(0, 1, 12, 0);
        _btnNada.Click += (_, _) => MarcarTodo(false);
        _lblResumen = new Label { AutoSize = true, Margin = new Padding(0, 7, 0, 0), ForeColor = AppTheme.TextSecondary };
        izq.Controls.AddRange([_btnRecontar, _btnTodo, _btnNada, _lblResumen]);
        barra.Controls.Add(izq, 0, 0);

        var der = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = AppTheme.ContentBg };
        _btnBorrar = AppTheme.MakeDangerButton("🧹 Borrar lo seleccionado", 210, 28);
        _btnBorrar.Margin = new Padding(0, 1, 0, 0);
        _btnBorrar.Click += async (_, _) => await BorrarAsync();
        der.Controls.Add(_btnBorrar);
        barra.Controls.Add(der, 1, 0);
        tbl.Controls.Add(barra, 0, 1);

        // ── La lista de apartados ────────────────────────────────────────────────────────
        _grid = AppTheme.MakeGrid();
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        // MakeGrid() deja la rejilla de solo lectura; aquí sí se marcan casillas, así que se habilita
        // a nivel rejilla y las columnas de texto se dejan de solo lectura una por una.
        _grid.ReadOnly = false;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Name = "Sel", Width = 34, Resizable = DataGridViewTriState.False, FillWeight = 4 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Grupo", Name = "Grupo", FillWeight = 13, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Apartado", Name = "Area", FillWeight = 22, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Además se lleva", Name = "Mas", FillWeight = 48, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Registros", Name = "Num", FillWeight = 13, ReadOnly = true });
        _grid.Columns["Num"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        // Sin esto la casilla no se da por marcada hasta que el foco cambia de renglón, y el
        // contador de abajo va siempre un clic por detrás.
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) PintarResumen(); };

        var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 6), BackColor = AppTheme.ContentBg };
        marco.Controls.Add(_grid);
        tbl.Controls.Add(marco, 0, 2);

        // ── Resultado ────────────────────────────────────────────────────────────────────
        var abajo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 8), BackColor = AppTheme.ContentBg };
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = AppTheme.MonoFont, BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle
        };
        abajo.Controls.Add(_txtLog);
        tbl.Controls.Add(abajo, 0, 3);

        Controls.Add(tbl);
        LlenarFilas();
    }

    /// <summary>Un renglón por área, en el orden del catálogo (que ya viene agrupado como el menú).</summary>
    private void LlenarFilas()
    {
        _grid.Rows.Clear();
        foreach (var area in DataCleanupService.Areas)
        {
            int i = _grid.Rows.Add(false, area.Grupo, area.Nombre, TextoArrastra(area), "…");
            _grid.Rows[i].Tag = area;
            if (area.Advertencia != null)
            {
                _grid.Rows[i].Cells["Area"].Style.ForeColor = AppTheme.Danger;
                _grid.Rows[i].Cells["Area"].Style.Font = AppTheme.BoldFont;
            }
        }
        PintarResumen();
    }

    private static string TextoArrastra(AreaLimpieza area) =>
        area.Advertencia == null ? area.Arrastra : $"{area.Arrastra}  —  ⚠ {area.Advertencia}";

    private async Task RecontarAsync()
    {
        Habilitar(false);
        try
        {
            _cuentas = await _cleanup.ContarAsync();
            foreach (DataGridViewRow fila in _grid.Rows)
            {
                if (fila.Tag is not AreaLimpieza area) continue;
                int cuantos = _cuentas.TryGetValue(area.Clave, out var n) ? n : -1;

                fila.Cells["Num"].Value = cuantos < 0 ? "?" : cuantos.ToString("N0");

                // Lo que ya está vacío no se puede marcar: un apartado en cero no tiene nada que
                // borrar, y dejarlo marcable solo sirve para inflar el recuento de la confirmación.
                bool vacia = cuantos == 0;
                fila.Cells["Sel"].ReadOnly = vacia;
                if (vacia) fila.Cells["Sel"].Value = false;
                var gris = vacia ? AppTheme.TextSecondary : AppTheme.TextPrimary;
                fila.Cells["Grupo"].Style.ForeColor = gris;
                fila.Cells["Mas"].Style.ForeColor = vacia ? AppTheme.TextSecondary : AppTheme.TextPrimary;
                fila.Cells["Num"].Style.ForeColor = cuantos > 0 ? AppTheme.TextPrimary : AppTheme.TextSecondary;
                if (area.Advertencia == null) fila.Cells["Area"].Style.ForeColor = gris;
            }
            PintarResumen();
        }
        catch (Exception ex)
        {
            Escribir($"No se pudo contar: {ex.Message}");
        }
        finally { Habilitar(true); }
    }

    private void MarcarTodo(bool marcar)
    {
        foreach (DataGridViewRow fila in _grid.Rows)
        {
            if (fila.Cells["Sel"].ReadOnly) continue;   // las vacías se quedan como están
            fila.Cells["Sel"].Value = marcar;
        }
        PintarResumen();
    }

    private List<(AreaLimpieza area, int cuantos)> Seleccion()
    {
        var elegidas = new List<(AreaLimpieza, int)>();
        foreach (DataGridViewRow fila in _grid.Rows)
        {
            if (fila.Tag is not AreaLimpieza area) continue;
            if (fila.Cells["Sel"].Value is not true) continue;
            elegidas.Add((area, _cuentas.TryGetValue(area.Clave, out var n) ? n : -1));
        }
        return elegidas;
    }

    private void PintarResumen()
    {
        var sel = Seleccion();
        int registros = sel.Sum(s => Math.Max(0, s.cuantos));
        _lblResumen.Text = sel.Count == 0
            ? "Nada seleccionado."
            : $"{sel.Count} apartado(s) seleccionado(s) — {registros:N0} registro(s).";
        _btnBorrar.Enabled = sel.Count > 0;
    }

    private async Task BorrarAsync()
    {
        var sel = Seleccion();
        if (sel.Count == 0) return;

        using (var dlg = new DataCleanupConfirmForm(sel))
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        Habilitar(false);
        _txtLog.Clear();
        Escribir($"Limpieza iniciada — {sel.Count} apartado(s).");
        try
        {
            var progreso = new Progress<string>(Escribir);
            var resultados = await _cleanup.LimpiarAsync(sel.Select(s => s.area.Clave).ToList(), progreso);

            int filas = resultados.Sum(r => r.Filas);
            int fallidas = resultados.Count(r => !r.Ok);
            Escribir("");
            Escribir(fallidas == 0
                ? $"Listo: {filas} fila(s) borradas en {resultados.Count} apartado(s)."
                : $"Terminado con problemas: {filas} fila(s) borradas, {fallidas} apartado(s) fallaron (ver arriba).");
        }
        catch (Exception ex)
        {
            Escribir($"La limpieza se detuvo: {ex.Message}");
        }
        finally
        {
            Habilitar(true);
            MarcarTodo(false);
            await RecontarAsync();
        }
    }

    private void Escribir(string linea) =>
        _txtLog.AppendText(linea + Environment.NewLine);

    private void Habilitar(bool si)
    {
        _grid.Enabled = si;
        _btnRecontar.Enabled = si; _btnTodo.Enabled = si; _btnNada.Enabled = si;
        _btnBorrar.Enabled = si && Seleccion().Count > 0;
        Cursor = si ? Cursors.Default : Cursors.WaitCursor;
    }
}
