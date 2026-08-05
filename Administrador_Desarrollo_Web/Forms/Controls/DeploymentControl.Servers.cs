using System.Diagnostics;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private DataGridView _gridServers = null!;
    private List<DeploymentTarget> _servers = [];

    private TabPage BuildServersTab()
    {
        var (tab, body) = NewTab("  🌐  Servidores  ");

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 8, 10, 5), BackColor = AppTheme.ContentBg
        };
        // Operaciones puede dar de alta un servidor, pero no modificarlo ni darlo de baja: eso
        // rompería despliegues de todo el equipo. Los botones se ocultan Y el servicio lo valida.
        bool puedeEditar = DeploymentTargetService.PuedeEditar(_currentUser);

        var bNew    = AppTheme.MakePrimaryButton("➕ Nuevo", 100); bNew.Margin = new Padding(0, 2, 6, 0); bNew.Click += SrvNew_Click;
        var bEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 100); bEdit.Margin = new Padding(0, 2, 6, 0); bEdit.Click += SrvEdit_Click; bEdit.Visible = puedeEditar;
        var bDel    = AppTheme.MakeDangerButton("🗑 Dar de baja", 130); bDel.Margin = new Padding(0, 2, 6, 0); bDel.Click += SrvDelete_Click; bDel.Visible = puedeEditar;
        var bImport = AppTheme.MakeSecondaryButton("📥 Importar JSON", 145); bImport.Margin = new Padding(0, 2, 6, 0); bImport.Click += SrvImport_Click; bImport.Visible = puedeEditar;
        var bOpen   = AppTheme.MakeSecondaryButton("🌍 Abrir URL", 120); bOpen.Margin = new Padding(0, 2, 6, 0); bOpen.Click += SrvOpenUrl_Click;
        var bExp    = AppTheme.MakeSecondaryButton("📊 Excel", 95); bExp.Margin = new Padding(0, 2, 0, 0); bExp.Click += SrvExport_Click;
        toolbar.Controls.AddRange([bNew, bEdit, bDel, bImport, bOpen, bExp]);
        // La barra no envuelve ni recorta en silencio: si algún día no caben los botones, aparece
        // una barra de desplazamiento en lugar de perderse los últimos por la derecha.
        toolbar.AutoScroll = true;

        _gridServers = AppTheme.MakeGrid();
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",     Name = "Id",     Width = 40, FillWeight = 4 });
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre", Name = "Name",   FillWeight = 24 });
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Host",   Name = "Host",   FillWeight = 24 });
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "URL",    Name = "Url",    FillWeight = 18 });
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Últ. actualización", Name = "Last", FillWeight = 16 });
        _gridServers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión desplegada", Name = "Ver", FillWeight = 14 });
        _gridServers.CellDoubleClick += (_, _) => SrvOpenUrl_Click(null, EventArgs.Empty);

        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pGrid.Controls.Add(_gridServers);

        // La nota va en su propia franja bajo la barra, no dentro de ella: metida entre los botones
        // los empujaba fuera del borde derecho y se perdían los últimos.
        if (!puedeEditar)
        {
            var nota = new Label
            {
                Dock = DockStyle.Top, Height = 20, AutoSize = false, AutoEllipsis = true,
                Text = "Puedes dar de alta servidores. Para corregir o dar de baja uno existente, pídeselo a un administrador.",
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pGrid.Controls.Add(nota);
            _gridServers.BringToFront();   // el Dock=Fill del grid va debajo de la nota, no encima
        }

        body.Controls.Add(toolbar, 0, 0);
        body.Controls.Add(pGrid, 0, 1);
        return tab;
    }

    private void LoadServers()
    {
        // AsNoTracking OBLIGATORIO, por lo mismo que en el Historial: el AppDbContext es Singleton y
        // el despliegue corre en su PROPIO contexto. Sin esto, la resolución de identidad devuelve
        // las instancias que quedaron rastreadas ANTES de desplegar, y las columnas «Últ.
        // actualización» y «Versión desplegada» se quedaban congeladas en lo que decían al abrir la
        // pantalla, aunque en la base ya estuvieran al día.
        //
        // Como quedan DESRASTREADAS, editar no puede trabajar sobre estas instancias: SrvEdit_Click
        // pide una rastreada y fresca con _targets.ParaEditar.
        _servers = _db.DeploymentTargets.AsNoTracking()
            .Include(t => t.LastRelease).ThenInclude(r => r!.AppSystem)
            .OrderBy(t => t.Nombre).ToList();
        _gridServers.Rows.Clear();
        foreach (var t in _servers)
        {
            var verText = t.LastRelease != null ? $"{t.LastRelease.AppSystem.Name} {t.LastRelease.Version}" : "—";
            int i = _gridServers.Rows.Add(t.Id, t.Nombre, t.Host, string.IsNullOrEmpty(t.URL) ? "—" : t.URL,
                t.LastDeployedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Nunca", verText);
            if (!t.IsActive) _gridServers.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            if (!string.IsNullOrEmpty(t.URL)) { _gridServers.Rows[i].Cells["Url"].Style.ForeColor = AppTheme.SidebarActive; }
        }
    }

    private DeploymentTarget? SelectedServer()
    {
        if (_gridServers.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _servers.FirstOrDefault(t => t.Id == id);
    }

    private void SrvNew_Click(object? s, EventArgs e)
    {
        using var frm = new DeploymentTargetDetailForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        EjecutarServidor(() =>
        {
            var (ok, msg, _) = _targets.Crear(frm.Result);
            return (ok, msg);
        });
    }

    private void SrvEdit_Click(object? s, EventArgs e)
    {
        var seleccionado = SelectedServer();
        if (seleccionado == null) { MessageBox.Show("Selecciona un servidor.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // La instancia de la rejilla viene sin rastrear (ver LoadServers): guardar sobre ella no
        // haría nada. Se pide la rastreada y al día, que además es lo correcto si alguien más la
        // cambió desde otro equipo mientras esta pantalla estaba abierta.
        var t = _targets.ParaEditar(seleccionado.Id);
        if (t == null)
        {
            MessageBox.Show("Ese servidor ya no existe: alguien lo eliminó.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadServers();
            return;
        }

        // El estado previo se captura ANTES de abrir el formulario: éste edita la misma instancia
        // rastreada por EF, así que después del diálogo los valores originales ya no existen.
        var previo = new { t.Id, t.Nombre, t.Host, t.Puerto, t.Usuario, t.RutaRemota, t.URL, t.IsActive };

        using var frm = new DeploymentTargetDetailForm(t);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        EjecutarServidor(() => _targets.Editar(t, previo));
    }

    private void SrvDelete_Click(object? s, EventArgs e)
    {
        var t = SelectedServer();
        if (t == null) { MessageBox.Show("Selecciona un servidor.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        if (!t.IsActive)
        {
            if (MessageBox.Show($"'{t.Nombre}' está dado de baja. ¿Reactivarlo?", "Confirmar",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            EjecutarServidor(() => _targets.Reactivar(t.Id));
            return;
        }

        if (MessageBox.Show(
                $"¿Dar de baja el servidor '{t.Nombre}'?\n\nDeja de recibir despliegues, pero se conserva " +
                "para que el historial siga siendo legible.",
                "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        EjecutarServidor(() => _targets.Desactivar(t.Id, motivo: null));
    }

    private void EjecutarServidor(Func<(bool ok, string mensaje)> operacion)
    {
        try
        {
            var (ok, mensaje) = operacion();
            LoadServers();
            if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SrvImport_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Title = "Importar servidores desde JSON" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var (added, updated) = _deploy.ImportTargetsFromJson(json);
            _audit.Record(AuditAction.Create, "DeploymentTarget", null, $"Importación JSON: {added} nuevos, {updated} actualizados");
            MessageBox.Show($"Importación completada.\n\nNuevos: {added}\nActualizados: {updated}\n\nLas contraseñas se guardaron cifradas en la base compartida: el resto del equipo puede desplegar con ellas sin volver a capturarlas.",
                "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadServers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al importar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SrvOpenUrl_Click(object? s, EventArgs e)
    {
        var t = SelectedServer();
        if (t == null) { MessageBox.Show("Selecciona un servidor.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (string.IsNullOrEmpty(t.URL)) { MessageBox.Show("Este servidor no tiene URL configurada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try { Process.Start(new ProcessStartInfo(t.URL) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir la URL:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SrvExport_Click(object? s, EventArgs e)
    {
        var path = _report.PromptSaveDialog("Servidores");
        if (path == null) return;
        _report.ExportToExcel(_servers, ["ID", "Nombre", "Host", "Puerto", "Usuario", "Ruta remota", "URL", "Últ. actualización", "Activo"],
            t => [t.Id, t.Nombre, t.Host, t.Puerto, t.Usuario, t.RutaRemota, t.URL, t.LastDeployedAt, t.IsActive], "Servidores", path);
        MessageBox.Show($"Exportado: {path}", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
