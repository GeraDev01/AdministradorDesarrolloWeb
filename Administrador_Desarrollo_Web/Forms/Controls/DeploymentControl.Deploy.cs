using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private ComboBox _cbxSystem = null!, _cbxRelease = null!, _cbxProfile = null!;
    private RichTextBox _txtLog = null!;
    private Button _btnDeploy = null!, _btnCancelDeploy = null!;
    private Button _btnPickServers = null!, _btnClearLog = null!;
    private CheckBox? _chkBackup;   // solo Operaciones (respaldo global); Admin respalda POR SERVIDOR
    private Label _lblDeployStatus = null!, _lblServersCount = null!, _lblPct = null!;
    private ProgressBar _deployBar = null!;
    private CancellationTokenSource? _cts;

    private List<AppSystem> _deploySystems = [];
    private List<AppRelease> _deployReleases = [];
    private List<DeploymentProfile> _deployProfiles = [];
    // Selección directa de servidores (Admin): destino sin perfil, estilo Blobup.
    private List<int> _directTargetIds = [];
    private HashSet<int> _directBackupIds = [];

    /// <summary>true si hay un despliegue ejecutándose ahora mismo (para conservarlo y avisar al salir).</summary>
    public bool DeployEnCurso => _cts != null;

    /// <summary>Cancela el despliegue en curso, si lo hay (lo usa MainForm al cerrar sesión/salir).</summary>
    public void CancelarDespliegue() { try { _cts?.Cancel(); } catch { } }

    private TabPage BuildDeployTab()
    {
        var (tab, body) = NewTab("  🚀  Desplegar  ", 140);

        // ── Selección (panel superior) ───────────────────────────
        var sel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 4,
            Margin = Padding.Empty, Padding = new Padding(12, 10, 12, 6), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        sel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        // 36 y no 32: los botones miden 30 de alto y el FlowLayoutPanel les añade su propio
        // margen; con 32 quedaban recortados por abajo.
        sel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        // 250 y no 240: Desplegar (130) + separación (6) + Cancelar (100) = 236, más los márgenes
        // del panel. Con 240 el botón Cancelar se salía del borde derecho de la celda.
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));

        // Admin y Operaciones eligen los servidores DIRECTAMENTE (como Blobup). El selector permite,
        // además, aplicar un perfil como atajo, así que no se pierde la comodidad de los perfiles.
        bool directo = _currentUser.IsAdmin || _currentUser.IsOperaciones;

        sel.Controls.Add(new Label { Text = "Sistema / Aplicativo", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }, 0, 0);
        sel.Controls.Add(new Label { Text = "Versión", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }, 1, 0);
        sel.Controls.Add(new Label { Text = directo ? "Servidores destino" : "Perfil (servidores destino)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }, 2, 0);

        _cbxSystem = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 8, 0) };
        _cbxSystem.SelectedIndexChanged += (_, _) => LoadReleasesForSelectedSystem();
        _cbxRelease = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 8, 0) };
        _cbxProfile = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 8, 0) };
        sel.Controls.Add(_cbxSystem, 0, 1);
        sel.Controls.Add(_cbxRelease, 1, 1);

        if (directo)
        {
            // Botón que abre el selector de servidores + contador del destino actual.
            var destino = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 8, 0), Padding = Padding.Empty };
            _btnPickServers = AppTheme.MakeSecondaryButton("🖧 Elegir servidores…", 170, 30);
            _btnPickServers.Margin = new Padding(0, 0, 6, 0);
            _btnPickServers.Click += (_, _) => PickServers();
            _lblServersCount = new Label { Text = "0 servidores", AutoSize = false, Width = 96, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextSecondary };
            destino.Controls.AddRange([_btnPickServers, _lblServersCount]);
            sel.Controls.Add(destino, 2, 1);
        }
        else
        {
            sel.Controls.Add(_cbxProfile, 2, 1);
        }

        // Márgenes explícitos en todo: los que trae WinForms por omisión (3 px por lado) sumaban
        // ancho de más y descuadraban los botones respecto a los combos de la izquierda.
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
        };
        _btnDeploy = AppTheme.MakePrimaryButton("🚀 Desplegar", 130, 30);
        _btnDeploy.BackColor = AppTheme.Success;
        _btnDeploy.Margin = new Padding(0, 1, 6, 0);
        _btnDeploy.Click += BtnDeploy_Click;
        _btnCancelDeploy = AppTheme.MakeDangerButton("⏹ Cancelar", 100, 30);
        _btnCancelDeploy.Enabled = false;
        _btnCancelDeploy.Margin = new Padding(0, 1, 0, 0);
        _btnCancelDeploy.Click += (_, _) => _cts?.Cancel();
        btnPanel.Controls.AddRange([_btnDeploy, _btnCancelDeploy]);
        sel.Controls.Add(btnPanel, 3, 1);

        // Respaldo previo: con selección directa se decide POR SERVIDOR en el selector. La casilla
        // global solo aplicaría a un flujo por perfil (hoy no lo usan los roles con acceso al despliegue).
        if (!directo)
        {
            _chkBackup = new CheckBox
            {
                Text = "💾 Respaldar antes", Checked = true, AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(0, 0, 0, 2),
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
            };
            sel.Controls.Add(_chkBackup, 3, 0);
        }

        // status row + barra de avance + log
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(12, 0, 12, 12), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Renglón de estado + botón para LIMPIAR la consola. La celda del botón se dejó holgada
        // (130 px, 28 de alto) porque con 110×22 se veía recortado, sobre todo al escalar la pantalla.
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        statusRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _lblDeployStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextSecondary, Text = "Selecciona sistema, versión y destino para desplegar." };
        _btnClearLog = AppTheme.MakeSecondaryButton("🧹 Limpiar", 118, 26);
        _btnClearLog.Margin = new Padding(6, 1, 0, 1);
        _btnClearLog.Click += (_, _) => LimpiarConsola();
        statusRow.Controls.Add(_lblDeployStatus, 0, 0);
        statusRow.Controls.Add(_btnClearLog, 1, 0);
        bottom.Controls.Add(statusRow, 0, 0);

        // Barra de avance (0..100 %). Visible solo mientras hay despliegue.
        var progRow = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        progRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        progRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
        progRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _deployBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 2, 6, 2), Visible = false };
        _lblPct = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.BoldFont, ForeColor = AppTheme.SidebarActive, Text = "", Visible = false };
        progRow.Controls.Add(_deployBar, 0, 0);
        progRow.Controls.Add(_lblPct, 1, 0);
        bottom.Controls.Add(progRow, 0, 1);

        _txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 24, 33),
            ForeColor = Color.FromArgb(220, 230, 240), Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None, WordWrap = false
        };
        bottom.Controls.Add(_txtLog, 0, 2);

        body.Controls.Add(sel, 0, 0);
        body.Controls.Add(bottom, 0, 1);
        return tab;
    }

    private void RefreshDeployCombos()
    {
        // Durante un despliegue NO se recargan los combos: se conservan la selección y el estado en
        // curso (si no, al volver a la pestaña se reseteaba todo).
        if (DeployEnCurso) return;

        _deploySystems = _db.AppSystems.Where(s => s.IsActive).OrderBy(s => s.Name).ToList();
        _cbxSystem.Items.Clear();
        foreach (var s in _deploySystems) _cbxSystem.Items.Add(s.Name);
        if (_cbxSystem.Items.Count > 0 && _cbxSystem.SelectedIndex < 0) _cbxSystem.SelectedIndex = 0;
        else LoadReleasesForSelectedSystem();

        // Perfiles: Admin ve todos; Operaciones solo los permitidos
        _deployProfiles = _db.DeploymentProfiles
            .Where(p => !p.IsAdHoc && (_currentUser.IsAdmin || p.AllowedForOperaciones))
            .OrderBy(p => p.Name).ToList();
        _cbxProfile.Items.Clear();
        foreach (var p in _deployProfiles) _cbxProfile.Items.Add(p.Name);
        if (_cbxProfile.Items.Count > 0) _cbxProfile.SelectedIndex = 0;
    }

    private void LoadReleasesForSelectedSystem()
    {
        _cbxRelease.Items.Clear();
        _deployReleases = [];
        if (_cbxSystem.SelectedIndex < 0 || _cbxSystem.SelectedIndex >= _deploySystems.Count) return;
        var sysId = _deploySystems[_cbxSystem.SelectedIndex].Id;
        _deployReleases = _db.AppReleases.Where(r => r.AppSystemId == sysId).OrderByDescending(r => r.CreatedAt).ToList();
        foreach (var r in _deployReleases)
            _cbxRelease.Items.Add($"{r.Version}  ({r.CreatedAt.ToLocalTime():dd/MM/yyyy})");
        if (_cbxRelease.Items.Count > 0) _cbxRelease.SelectedIndex = 0;
    }

    private void PickServers()
    {
        var servers = _db.DeploymentTargets.Where(t => t.IsActive).OrderBy(t => t.Nombre).ToList();
        if (servers.Count == 0)
        {
            MessageBox.Show("No hay servidores activos. Créalos en la pestaña «Servidores».", "Sin servidores",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // Perfiles ofrecidos como ATAJO dentro del selector. Operaciones solo ve los que tiene permitidos.
        var profiles = _db.DeploymentProfiles.Where(p => !p.IsAdHoc && (_currentUser.IsAdmin || p.AllowedForOperaciones))
            .Include(p => p.ProfileTargets).OrderBy(p => p.Name).ToList();

        using var frm = new DeployServersPickerForm(servers, _directTargetIds, _directBackupIds, profiles);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        _directTargetIds = frm.SelectedTargetIds;
        _directBackupIds = frm.BackupTargetIds;
        _lblServersCount.Text = $"{_directTargetIds.Count} srv · {_directBackupIds.Count} c/resp.";
    }

    private async void BtnDeploy_Click(object? sender, EventArgs e)
    {
        if (_cbxRelease.SelectedIndex < 0 || _cbxRelease.SelectedIndex >= _deployReleases.Count)
        { MessageBox.Show("Selecciona una versión.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var release = _deployReleases[_cbxRelease.SelectedIndex];

        // Admin y Operaciones despliegan a la selección DIRECTA de servidores.
        bool directo = _currentUser.IsAdmin || _currentUser.IsOperaciones;
        DeploymentProfile? profile = null;
        int targetCount;
        string destinoDesc, respaldoDesc;
        IReadOnlySet<int>? respaldarTargets;   // null = respaldar todos; conjunto = solo esos
        if (directo)
        {
            if (_directTargetIds.Count == 0)
            { MessageBox.Show("Elige al menos un servidor destino (botón «🖧 Elegir servidores…»).", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            targetCount = _directTargetIds.Count;
            respaldarTargets = _directBackupIds;   // por servidor
            respaldoDesc = _directBackupIds.Count == targetCount ? "todos"
                         : _directBackupIds.Count == 0 ? "ninguno" : $"{_directBackupIds.Count} de {targetCount}";
            destinoDesc = $"{targetCount} servidor(es) seleccionados";
        }
        else
        {
            if (_cbxProfile.SelectedIndex < 0 || _cbxProfile.SelectedIndex >= _deployProfiles.Count)
            { MessageBox.Show("Selecciona un perfil.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            profile = _deployProfiles[_cbxProfile.SelectedIndex];
            targetCount = _db.DeploymentProfileTargets.Count(pt => pt.ProfileId == profile.Id);
            if (targetCount == 0) { MessageBox.Show("El perfil seleccionado no tiene servidores.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            bool resp = _chkBackup?.Checked ?? true;
            respaldarTargets = resp ? null : new HashSet<int>();   // todos o ninguno
            respaldoDesc = resp ? "todos" : "ninguno";
            destinoDesc = $"perfil '{profile.Name}' ({targetCount} servidor/es)";
        }

        // Checklist en vez de un «¿seguro?»: una confirmación de una línea se contesta que sí por
        // reflejo. Lo que se marque queda escrito con el despliegue como evidencia.
        var queSeDespliega = $"{release.AppSystem?.Name} v{release.Version}".Trim();
        string evidencia;
        using (var chk = new DeploymentChecklistForm(
                   queSeDespliega, destinoDesc, respaldoDesc, sinRespaldo: respaldoDesc == "ninguno"))
        {
            if (chk.ShowDialog(FindForm()) != DialogResult.OK) return;
            evidencia = DeploymentChecklist.Evidencia(
                _currentUser.User?.FullName ?? _currentUser.Username ?? "(sin nombre)",
                DateTime.Now, queSeDespliega, destinoDesc, respaldoDesc, chk.Marcados, chk.Nota);
        }

        _txtLog.Clear();
        SetDeployRunning(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog);
        var onStatus = new Progress<DeployStatus>(UpdateStatus);

        try
        {
            _lblDeployStatus.ForeColor = AppTheme.SidebarActive;
            _lblDeployStatus.Text = "⏳ Despliegue en curso...";
            var job = directo
                ? await _deploy.DeployToServersAsync(release.Id, _directTargetIds, progress, _cts.Token, respaldarTargets, onStatus, evidencia)
                : await _deploy.DeployAsync(release.Id, profile!.Id, progress, _cts.Token, respaldarTargets, onStatus, evidencia);
            _lblDeployStatus.ForeColor = job.TargetsFailed == 0 ? AppTheme.Success : AppTheme.Warning;
            _lblDeployStatus.Text = job.TargetsFailed == 0
                ? $"✅ Despliegue completado — {job.TargetsOk} servidor(es)."
                : $"⚠ Completado con errores — OK: {job.TargetsOk}, Fallidos: {job.TargetsFailed}.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("\n⏹  Despliegue cancelado por el usuario.");
            _lblDeployStatus.ForeColor = AppTheme.Warning;
            _lblDeployStatus.Text = "⏹ Despliegue cancelado.";
        }
        catch (Exception ex)
        {
            AppendLog($"\n❌  ERROR: {ex.Message}");
            _lblDeployStatus.ForeColor = AppTheme.Danger;
            _lblDeployStatus.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            SetDeployRunning(false);
            _cts?.Dispose(); _cts = null;
        }
    }

    private void SetDeployRunning(bool running)
    {
        _btnDeploy.Enabled = !running;
        _btnCancelDeploy.Enabled = running;
        _cbxSystem.Enabled = _cbxRelease.Enabled = _cbxProfile.Enabled = !running;
        if (_btnPickServers is not null) _btnPickServers.Enabled = !running;
        if (_chkBackup is not null) _chkBackup.Enabled = !running;

        if (_btnClearLog is not null) _btnClearLog.Enabled = !running;   // no limpiar a media corrida
        _deployBar.Visible = _lblPct.Visible = running;
        if (running) { _deployBar.Value = 0; _lblPct.Text = "0%"; }
    }

    private void UpdateStatus(DeployStatus st)
    {
        if (_deployBar.IsDisposed) return;
        int pct = Math.Clamp(st.Percent, 0, 100);
        _deployBar.Value = pct;
        _lblPct.Text = $"{pct}%";
        var detalle = string.IsNullOrEmpty(st.Server) ? st.Detail : $"[{st.Server}] {st.Detail}";
        _lblDeployStatus.ForeColor = AppTheme.SidebarActive;
        _lblDeployStatus.Text = $"⏳ {detalle}";
    }

    private void LimpiarConsola()
    {
        _txtLog.Clear();
        _deployBar.Value = 0;
        _lblPct.Text = "";
        _lblDeployStatus.ForeColor = AppTheme.TextSecondary;
        _lblDeployStatus.Text = "Consola limpia. Selecciona sistema, versión y destino para desplegar.";
    }

    private void AppendLog(string line)
    {
        if (_txtLog.IsDisposed) return;
        // Mientras el control está oculto (se navegó a otra pantalla con un despliegue en curso) no
        // hay handle: se acumula el texto sin colorear ni desplazar, y al volver se ve todo.
        if (!_txtLog.IsHandleCreated) { try { _txtLog.AppendText(line + Environment.NewLine); } catch { } return; }

        Color color = line switch
        {
            _ when line.Contains("✅") || line.Contains("✓") => Color.FromArgb(134, 239, 172),
            _ when line.Contains("❌") || line.Contains("ERROR") => Color.FromArgb(252, 165, 165),
            _ when line.Contains("⚠")  => Color.FromArgb(253, 224, 71),
            _ when line.Contains("🚀") || line.Contains("🌐") => Color.FromArgb(147, 197, 253),
            _ => Color.FromArgb(220, 230, 240)
        };
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.SelectionColor = color;
        _txtLog.AppendText(line + Environment.NewLine);
        _txtLog.ScrollToCaret();
    }
}
