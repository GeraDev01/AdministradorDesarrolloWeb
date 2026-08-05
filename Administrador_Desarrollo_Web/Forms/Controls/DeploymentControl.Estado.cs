using System.Diagnostics;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pestaña «Estado»: qué versión tiene cada servidor, quién se la puso y cuándo.
///
/// El Historial contesta «¿qué se desplegó?» ordenado por despliegue. Esta contesta la otra mitad,
/// que es la que se pregunta en caliente: «¿qué hay AHORA en cada servidor y a cuál le falta?».
/// Reconstruirlo desde el historial obliga a leerlo entero hacia atrás y cruzar perfiles, y con
/// selección directa ni siquiera basta con eso.
/// </summary>
public partial class DeploymentControl
{
    private DataGridView _gridEstado = null!;
    private Label _lblEstadoResumen = null!;
    private CheckBox _chkEstadoSoloAtrasados = null!;
    private CheckBox _chkEstadoIncluirBajas = null!;
    private List<EstadoServidor> _estado = [];

    private TabPage BuildEstadoTab()
    {
        var (tab, body) = NewTab("  📊  Estado  ");

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(10, 8, 10, 5), BackColor = AppTheme.ContentBg
        };

        var bRefrescar = AppTheme.MakeSecondaryButton("🔄 Actualizar", 110);
        bRefrescar.Margin = new Padding(0, 2, 6, 0);
        bRefrescar.Click += (_, _) => LoadEstado();

        var bHistorial = AppTheme.MakeSecondaryButton("📜 Ver ese despliegue", 175);
        bHistorial.Margin = new Padding(0, 2, 6, 0);
        bHistorial.Click += (_, _) => EstadoVerEnHistorial();

        var bAbrir = AppTheme.MakeSecondaryButton("🌍 Abrir URL", 120);
        bAbrir.Margin = new Padding(0, 2, 6, 0);
        bAbrir.Click += (_, _) => EstadoAbrirUrl();

        var bExcel = AppTheme.MakeSecondaryButton("📊 Excel", 95);
        bExcel.Margin = new Padding(0, 2, 16, 0);
        bExcel.Click += (_, _) => EstadoExportar();

        _chkEstadoSoloAtrasados = new CheckBox
        {
            Text = "Solo atrasados", AutoSize = true, Margin = new Padding(0, 9, 14, 0)
        };
        _chkEstadoSoloAtrasados.CheckedChanged += (_, _) => PintarEstado();

        _chkEstadoIncluirBajas = new CheckBox
        {
            Text = "Incluir dados de baja", AutoSize = true, Margin = new Padding(0, 9, 0, 0)
        };
        _chkEstadoIncluirBajas.CheckedChanged += (_, _) => LoadEstado();

        toolbar.Controls.AddRange([bRefrescar, bHistorial, bAbrir, bExcel,
                                   _chkEstadoSoloAtrasados, _chkEstadoIncluirBajas]);

        _gridEstado = AppTheme.MakeGrid();
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Servidor",           Name = "Srv",    FillWeight = 20 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sistema",            Name = "Sis",    FillWeight = 15 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión desplegada", Name = "Ver",    FillWeight = 14 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Última publicada",   Name = "UltVer", FillWeight = 13 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",             Name = "Est",    FillWeight = 12 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Últ. actualización", Name = "Cuando", FillWeight = 15 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hace",               Name = "Hace",   FillWeight = 11 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quién lo desplegó",  Name = "Quien",  FillWeight = 18 });
        _gridEstado.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Despliegue #",       Name = "Job",    FillWeight = 9 });
        _gridEstado.CellDoubleClick += (_, _) => EstadoVerEnHistorial();

        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pGrid.Controls.Add(_gridEstado);

        _lblEstadoResumen = new Label
        {
            Dock = DockStyle.Top, Height = 22, AutoSize = false, AutoEllipsis = true,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pGrid.Controls.Add(_lblEstadoResumen);
        _gridEstado.BringToFront();   // el Dock=Fill de la rejilla va DEBAJO del resumen, no encima

        body.Controls.Add(toolbar, 0, 0);
        body.Controls.Add(pGrid, 0, 1);
        return tab;
    }

    private void LoadEstado()
    {
        _estado = _serverStatus.Obtener(incluirDadosDeBaja: _chkEstadoIncluirBajas.Checked);
        PintarEstado();
    }

    private void PintarEstado()
    {
        var filas = _chkEstadoSoloAtrasados.Checked
            ? _estado.Where(e => e.Atrasado).ToList()
            : _estado;

        _gridEstado.Rows.Clear();
        foreach (var e in filas)
        {
            var (etiqueta, color) = EstadoDeServidor(e);
            int i = _gridEstado.Rows.Add(
                e.Servidor,
                e.Sistema ?? "—",
                e.Version ?? "—",
                e.UltimaVersionDelSistema ?? "—",
                etiqueta,
                e.DesplegadoUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Nunca",
                e.Antiguedad,
                e.Quien ?? "—",
                e.JobId?.ToString() ?? "—");

            var fila = _gridEstado.Rows[i];
            fila.Cells["Est"].Style.ForeColor = color;
            fila.Cells["Est"].Style.Font = AppTheme.BoldFont;
            if (!e.Activo) fila.DefaultCellStyle.ForeColor = AppTheme.TextSecondary;

            // Sin registro de quién: son los despliegues anteriores a que se empezara a guardar. Se
            // deja en gris para que la ausencia se lea como «no se registró» y no como un descuido.
            if (e.Quien == null)
            {
                var c = fila.Cells["Quien"].Style;
                c.ForeColor = AppTheme.TextSecondary; c.SelectionForeColor = AppTheme.TextSecondary;
            }
        }

        int atrasados = _estado.Count(e => e.Atrasado);
        int nunca     = _estado.Count(e => e.NuncaDesplegado);
        int alDia     = _estado.Count - atrasados - nunca;
        _lblEstadoResumen.Text =
            $"{_estado.Count} servidor(es)   ·   ✅ {alDia} al día   ·   ⚠ {atrasados} atrasado(s)   ·   ○ {nunca} sin desplegar nunca"
            + (_chkEstadoSoloAtrasados.Checked ? $"      (mostrando {filas.Count})" : "");
    }

    private static (string etiqueta, Color color) EstadoDeServidor(EstadoServidor e)
    {
        if (e.NuncaDesplegado)               return ("○ Sin desplegar", AppTheme.TextSecondary);
        if (e.Atrasado)                      return ("⚠ Atrasado",      AppTheme.Warning);
        if (e.UltimaVersionDelSistema == null) return ("Desplegado",     AppTheme.TextPrimary);
        return ("✅ Al día", AppTheme.Success);
    }

    private EstadoServidor? EstadoSeleccionado()
    {
        if (_gridEstado.CurrentRow?.Cells["Srv"].Value is not string nombre) return null;
        return _estado.FirstOrDefault(e => e.Servidor == nombre);
    }

    /// <summary>Salta al Historial con ese despliegue ya seleccionado: de «quién y cuándo» al log completo.</summary>
    private void EstadoVerEnHistorial()
    {
        var e = EstadoSeleccionado();
        if (e == null) { MessageBox.Show("Selecciona un servidor.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (e.JobId is not int jobId)
        {
            MessageBox.Show(
                e.NuncaDesplegado
                    ? $"«{e.Servidor}» no ha recibido ningún despliegue todavía."
                    : $"El último despliegue de «{e.Servidor}» es anterior a que se guardara esa referencia.\n\n" +
                      "Puedes buscarlo en el Historial por fecha.",
                "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var tabHistorial = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text.Contains("Historial"));
        if (tabHistorial == null) return;

        _tabs.SelectedTab = tabHistorial;   // dispara OnTabChanged → LoadHistory()
        SeleccionarEnHistorial(jobId);
    }

    private void SeleccionarEnHistorial(int jobId)
    {
        foreach (DataGridViewRow fila in _gridHistory.Rows)
        {
            if (fila.Cells["Id"].Value is not int id || id != jobId) continue;
            fila.Selected = true;
            _gridHistory.CurrentCell = fila.Cells["Sys"];
            return;
        }
        // El historial trae los 200 más recientes: un despliegue viejo puede quedar fuera, y decirlo
        // es mejor que dejar la pestaña en otra fila como si nada hubiera pasado.
        MessageBox.Show($"El despliegue #{jobId} ya no está entre los más recientes del historial.",
            "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EstadoAbrirUrl()
    {
        var e = EstadoSeleccionado();
        if (e == null) { MessageBox.Show("Selecciona un servidor.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (e.Url == null) { MessageBox.Show($"«{e.Servidor}» no tiene URL configurada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try { Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir la URL:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void EstadoExportar()
    {
        if (_estado.Count == 0)
        { MessageBox.Show("No hay servidores que exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var path = _report.PromptSaveDialog($"EstadoServidores_{DateTime.Now:yyyyMMdd}");
        if (path == null) return;

        try
        {
            _report.ExportToExcel(_estado,
                ["Servidor", "Host", "URL", "Activo", "Sistema", "Versión desplegada", "Última publicada",
                 "Estado", "Última actualización", "Quién lo desplegó", "Despliegue #"],
                e => new object?[]
                {
                    e.Servidor, e.Host, e.Url, e.Activo ? "Sí" : "No", e.Sistema, e.Version,
                    e.UltimaVersionDelSistema, EstadoDeServidor(e).etiqueta,
                    e.DesplegadoUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    e.Quien, e.JobId
                },
                "Estado de servidores", path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (MessageBox.Show($"Exportado:\n{path}\n\n¿Abrirlo ahora?", "Listo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
