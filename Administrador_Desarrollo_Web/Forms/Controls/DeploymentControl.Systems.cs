using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private DataGridView _gridSystems = null!, _gridReleases = null!;
    private List<AppSystem> _systems = [];
    private List<AppRelease> _releases = [];

    private TabPage BuildSystemsTab()
    {
        var tab = new TabPage("  🖥  Sistemas y Versiones  ") { BackColor = AppTheme.ContentBg };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None, BackColor = AppTheme.ContentBg
        };

        // ── Left: sistemas ───────────────────────────────────────
        var leftTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
        leftTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        leftTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Operaciones ve el catálogo completo (necesita saber qué versiones hay y qué trae cada
        // una antes de desplegar), pero no puede alterarlo: los botones de escritura no se agregan.
        bool puedeEditar = _currentUser.IsAdmin;

        var leftTop = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(10, 8, 10, 4) };
        leftTop.Controls.Add(new Label { Text = "Sistemas / Aplicativos", Width = 300, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Margin = new Padding(0, 4, 0, 6) });
        leftTop.SetFlowBreak(leftTop.Controls[0], true);
        if (puedeEditar)
        {
            var bSysNew  = AppTheme.MakePrimaryButton("➕ Nuevo", 100, 28); bSysNew.Margin = new Padding(0, 0, 6, 0); bSysNew.Click += SysNew_Click;
            var bSysEdit = AppTheme.MakeSecondaryButton("✏ Editar", 95, 28); bSysEdit.Margin = new Padding(0, 0, 6, 0); bSysEdit.Click += SysEdit_Click;
            var bSysDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 100, 28); bSysDel.Margin = new Padding(0, 0, 0, 0); bSysDel.Click += SysDelete_Click;
            leftTop.Controls.AddRange([bSysNew, bSysEdit, bSysDel]);
        }
        else
            leftTop.Controls.Add(new Label
            {
                Text = "Solo consulta", Width = 300, AutoSize = false, Height = 24,
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Margin = Padding.Empty
            });

        _gridSystems = AppTheme.MakeGrid();
        _gridSystems.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",       Name = "Id",   Width = 40, FillWeight = 8 });
        _gridSystems.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sistema",   Name = "Name", FillWeight = 55 });
        _gridSystems.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versiones", Name = "Rel",  FillWeight = 22 });
        _gridSystems.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activo",    Name = "Act",  FillWeight = 15 });
        _gridSystems.SelectionChanged += (_, _) => LoadReleases();
        var pSysGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 6, 10) }; pSysGrid.Controls.Add(_gridSystems);

        leftTbl.Controls.Add(leftTop, 0, 0);
        leftTbl.Controls.Add(pSysGrid, 0, 1);
        split.Panel1.Controls.Add(leftTbl);

        // ── Right: versiones ─────────────────────────────────────
        var rightTbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var rightTop = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(6, 8, 10, 4) };
        rightTop.Controls.Add(new Label { Text = "Versiones del sistema seleccionado", Width = 320, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary, Margin = new Padding(0, 4, 0, 6) });
        rightTop.SetFlowBreak(rightTop.Controls[0], true);

        // El Changelog lo ve TODO el mundo: es la información que necesita quien despliega para
        // saber qué va a subir. Crear y borrar versiones sigue siendo exclusivo del administrador.
        if (puedeEditar)
        {
            var bRelNew = AppTheme.MakePrimaryButton("➕ Nueva versión", 140, 28); bRelNew.Margin = new Padding(0, 0, 6, 0); bRelNew.Click += RelNew_Click;
            var bRelEdit = AppTheme.MakeSecondaryButton("✏ Editar", 95, 28); bRelEdit.Margin = new Padding(0, 0, 6, 0); bRelEdit.Click += RelEdit_Click;
            rightTop.Controls.AddRange([bRelNew, bRelEdit]);
        }
        var bRelLog = AppTheme.MakeSecondaryButton("📝 Changelog", 120, 28); bRelLog.Margin = new Padding(0, 0, 6, 0); bRelLog.Click += RelChangelog_Click;
        rightTop.Controls.Add(bRelLog);
        if (puedeEditar)
        {
            var bRelDel = AppTheme.MakeDangerButton("🗑 Eliminar", 100, 28); bRelDel.Click += RelDelete_Click;
            rightTop.Controls.Add(bRelDel);
        }
        // Fuera del if: aplica con cualquier rol. Si los botones no caben aparece una barra de
        // desplazamiento en vez de perderse los últimos por la derecha.
        rightTop.AutoScroll = true;
        leftTop.AutoScroll = true;

        _gridReleases = AppTheme.MakeGrid();
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",      Name = "Id",   Width = 40, FillWeight = 7 });
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Versión", Name = "Ver",  FillWeight = 24 });
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tamaño",  Name = "Size", FillWeight = 14 });
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Azure",   Name = "Blob", FillWeight = 12 });
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Creada",  Name = "Date", FillWeight = 20 });
        _gridReleases.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Changelog", Name = "Log", FillWeight = 23 });
        _gridReleases.CellDoubleClick += (_, _) => RelChangelog_Click(null, EventArgs.Empty);
        var pRelGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 10, 10) }; pRelGrid.Controls.Add(_gridReleases);

        rightTbl.Controls.Add(rightTop, 0, 0);
        rightTbl.Controls.Add(pRelGrid, 0, 1);
        split.Panel2.Controls.Add(rightTbl);

        tab.Controls.Add(split);
        tab.Layout += (_, _) => { if (split.Width > 100) split.SplitterDistance = split.Width / 2; };
        return tab;
    }

    private void LoadSystems()
    {
        _systems = _db.AppSystems.Include(s => s.Releases).OrderBy(s => s.Name).ToList();
        _gridSystems.Rows.Clear();
        foreach (var s in _systems)
        {
            int i = _gridSystems.Rows.Add(s.Id, s.Name, s.Releases.Count, s.IsActive ? "✓" : "✗");
            if (!s.IsActive) _gridSystems.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
        }
        LoadReleases();
    }

    private AppSystem? SelectedSystem()
    {
        if (_gridSystems.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _systems.FirstOrDefault(s => s.Id == id);
    }

    private void LoadReleases()
    {
        _gridReleases.Rows.Clear();
        var sys = SelectedSystem();
        if (sys == null) { _releases = []; return; }
        _releases = _db.AppReleases.Where(r => r.AppSystemId == sys.Id).OrderByDescending(r => r.CreatedAt).ToList();
        foreach (var r in _releases)
        {
            var shortLog = string.IsNullOrWhiteSpace(r.Changelog) ? "—" : r.Changelog.Replace("\n", " ").Replace("\r", "");
            if (shortLog.Length > 60) shortLog = shortLog[..60] + "…";
            _gridReleases.Rows.Add(r.Id, r.Version, $"{r.ZipSizeBytes / 1024.0 / 1024.0:F1} MB",
                string.IsNullOrEmpty(r.ZipBlobUrl) ? "—" : "☁ Sí", r.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), shortLog);
        }
    }

    private AppRelease? SelectedRelease()
    {
        if (_gridReleases.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _releases.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>Subcarpetas que existen bajo la carpeta de versiones del contenedor. Silenciosa: si
    /// falla el listado, se ofrece la lista vacía y el usuario puede teclear la carpeta a mano.</summary>
    private async Task<List<string>> CarpetasVersionesAsync()
    {
        if (!_blob.IsConfigured) return [];
        try { return await _blob.ListarSubcarpetasAsync(_blob.PrefijoVersiones); }
        catch { return []; }
    }

    private async void SysNew_Click(object? s, EventArgs e)
    {
        var carpetas = await CarpetasVersionesAsync();
        using var frm = new AppSystemDetailForm(carpetas);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var sys = frm.Result; sys.CreatedAt = DateTime.UtcNow;
        _db.AppSystems.Add(sys); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "AppSystem", sys.Id.ToString(), sys.Name);
        LoadSystems();
    }

    private async void SysEdit_Click(object? s, EventArgs e)
    {
        var sys = SelectedSystem();
        if (sys == null) { MessageBox.Show("Selecciona un sistema.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var carpetas = await CarpetasVersionesAsync();
        using var frm = new AppSystemDetailForm(carpetas, sys);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "AppSystem", sys.Id.ToString(), sys.Name);
        LoadSystems();
    }

    private void SysDelete_Click(object? s, EventArgs e)
    {
        var sys = SelectedSystem();
        if (sys == null) { MessageBox.Show("Selecciona un sistema.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar '{sys.Name}' y TODAS sus versiones?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.AppSystems.Remove(sys); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "AppSystem", sys.Id.ToString(), sys.Name);
        LoadSystems();
    }

    private async void RelNew_Click(object? s, EventArgs e)
    {
        var sys = SelectedSystem();
        if (sys == null) { MessageBox.Show("Selecciona primero un sistema.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        // Las carpetas se leen del contenedor, no de una lista configurada: lo que se ofrece es
        // exactamente lo que existe, sin que puedan divergir.
        List<string> carpetas = [];
        if (_blob.IsConfigured)
        {
            try { carpetas = await _blob.ListarSubcarpetasAsync(_blob.PrefijoVersiones); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No se pudieron leer las carpetas de Blob Storage:\n{ex.Message}\n\n" +
                    "La versión se puede crear igual; quedará en la raíz de versiones.",
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        using var frm = new AppReleaseDetailForm(sys, carpetas, _blob);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        // Crear versión: comprimir carpeta local + subir, o tomar un archivo ya existente en el blob.
        using var prog = new Details.ProgressDialog($"Creando versión {frm.Version} de {sys.Name}");
        prog.Show(this);
        try
        {
            var progress = new Progress<string>(prog.Append);
            if (frm.DesdeBlob)
                await _deploy.CreateReleaseFromBlobAsync(sys.Id, frm.Version, frm.Changelog,
                    frm.BlobSeleccionado!, progress, prog.Token);
            else
                await _deploy.CreateReleaseAsync(sys.Id, frm.Version, frm.Changelog, frm.SourceFolder,
                    frm.CarpetaDestino, progress, prog.Token);
            prog.MarkDone();
        }
        catch (OperationCanceledException) { prog.Append("\n⏹ Cancelado."); prog.MarkDone(); }
        catch (Exception ex) { prog.Append($"\n❌ Error: {ex.Message}"); prog.MarkDone(); }
        LoadSystems();
    }

    private async void RelEdit_Click(object? s, EventArgs e)
    {
        var rel = SelectedRelease();
        if (rel == null) { MessageBox.Show("Selecciona una versión.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // La ETIQUETA de una versión que ya se desplegó o está programada es su identificador en el
        // historial y en las citas programadas (que muestran AppRelease.Version en vivo): renombrarla
        // reescribiría lo que dicen esos registros. Por eso, si ya se usó, la etiqueta queda bloqueada
        // (la carpeta y el changelog sí se pueden editar).
        bool versionBloqueada = _db.DeploymentJobs.Any(j => j.AppReleaseId == rel.Id)
                             || _db.ScheduledDeployments.Any(sd => sd.AppReleaseId == rel.Id);

        var carpetas = await CarpetasVersionesAsync();
        using var frm = new AppReleaseEditForm(rel, carpetas, versionBloqueada);
        if (frm.ShowDialog(this) != DialogResult.OK) return;

        // Dos versiones del MISMO sistema no deben compartir etiqueta: se volverían indistinguibles al desplegar.
        if (!versionBloqueada)
        {
            bool duplicada = _db.AppReleases.Any(r =>
                r.AppSystemId == rel.AppSystemId && r.Id != rel.Id &&
                r.Version == frm.Version);
            if (duplicada)
            {
                MessageBox.Show($"Ya existe otra versión «{frm.Version}» en este sistema. Usa una etiqueta distinta.",
                    "Versión duplicada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var previo = new { rel.Version, rel.TargetFolder, rel.Changelog };
        if (!versionBloqueada) rel.Version = frm.Version;   // etiqueta inmutable si ya se usó
        rel.TargetFolder = frm.CarpetaDestino;
        rel.Changelog = string.IsNullOrWhiteSpace(frm.Changelog) ? null : frm.Changelog;
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Update, "AppRelease", rel.Id.ToString(),
            $"Versión editada: {rel.Version}", Models.AuditOutcome.Exito,
            oldValues: previo, newValues: new { rel.Version, rel.TargetFolder, rel.Changelog });

        LoadSystems();
    }

    private void RelChangelog_Click(object? s, EventArgs e)
    {
        var rel = SelectedRelease();
        if (rel == null) { MessageBox.Show("Selecciona una versión.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // Quien no es administrador lo abre como visor. La comprobación va aquí y no solo en el
        // botón: el doble clic sobre la fila llega a este mismo método.
        bool soloLectura = !_currentUser.IsAdmin;

        using var frm = new Details.ChangelogEditForm(rel.Version, rel.Changelog, soloLectura);
        if (frm.ShowDialog(this) != DialogResult.OK || soloLectura) return;

        var previo = rel.Changelog;
        rel.Changelog = frm.Changelog;
        _db.SaveChanges();
        _audit.RecordDetailed(AuditAction.Update, "AppRelease", rel.Id.ToString(),
            $"Changelog v{rel.Version}", Models.AuditOutcome.Exito,
            oldValues: new { Changelog = previo }, newValues: new { rel.Changelog });
        LoadReleases();
    }

    private void RelDelete_Click(object? s, EventArgs e)
    {
        var rel = SelectedRelease();
        if (rel == null) { MessageBox.Show("Selecciona una versión.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar la versión '{rel.Version}'? (También se borra el ZIP local)", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { if (!string.IsNullOrEmpty(rel.ZipLocalPath) && File.Exists(rel.ZipLocalPath)) File.Delete(rel.ZipLocalPath); } catch { }
        _db.AppReleases.Remove(rel); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "AppRelease", rel.Id.ToString(), rel.Version);
        LoadSystems();
    }
}
