using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Módulo de despliegues (Fase 3). Dividido en archivos parciales por pestaña:
/// .Deploy, .Systems, .Servers, .Profiles, .History, .Backup
/// </summary>
public partial class DeploymentControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly DeploymentService _deploy;
    private readonly BlobStorageService _blob;
    private readonly BackupService _backup;
    private readonly AuditService _audit;
    private readonly ReportService _report;
    private readonly CurrentUserContext _currentUser;
    private readonly DeploymentTargetService _targets;
    private readonly ServerStatusService _serverStatus;

    private TabControl _tabs = null!;

    public DeploymentControl(AppDbContext db, DeploymentService deploy, BlobStorageService blob,
        BackupService backup, AuditService audit, ReportService report, CurrentUserContext currentUser,
        DeploymentTargetService targets, ServerStatusService serverStatus)
    {
        _db = db; _deploy = deploy; _blob = blob; _backup = backup;
        _audit = audit; _report = report; _currentUser = currentUser; _targets = targets;
        _serverStatus = serverStatus;
        BuildUI();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        _tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(14, 5) };

        // Operaciones ve Desplegar, Sistemas y Versiones (solo consulta, incluido el changelog),
        // Servidores (solo para dar de alta) e Historial.
        // Perfiles y Respaldo BD siguen siendo exclusivos de Admin.
        _tabs.TabPages.Add(BuildDeployTab());
        _tabs.TabPages.Add(BuildSystemsTab());
        _tabs.TabPages.Add(BuildServersTab());
        if (_currentUser.IsAdmin) _tabs.TabPages.Add(BuildProfilesTab());
        // Estado va junto al Historial y lo ven los dos roles: es de consulta, y saber qué versión
        // tiene cada servidor es justo lo que necesita quien despliega, no solo quien administra.
        _tabs.TabPages.Add(BuildEstadoTab());
        _tabs.TabPages.Add(BuildHistoryTab());
        if (_currentUser.IsAdmin)
        {
            _tabs.TabPages.Add(BuildBlobsTab());
            _tabs.TabPages.Add(BuildBackupTab());
        }

        _tabs.SelectedIndexChanged += (_, _) => OnTabChanged();
        Controls.Add(_tabs);
    }

    private void OnTabChanged()
    {
        // Al mostrarse por primera vez, el TabControl todavía no tiene pestaña seleccionada realizada
        // (SelectedTab == null): se cae a la primera pestaña para que SÍ cargue de una vez, en vez de
        // quedar vacío hasta cambiar de pestaña.
        var tab = _tabs.SelectedTab ?? (_tabs.TabPages.Count > 0 ? _tabs.TabPages[0] : null);
        var text = tab?.Text ?? "";
        if (text.Contains("Desplegar"))      RefreshDeployCombos();
        else if (text.Contains("Sistemas"))  { LoadSystems(); }
        else if (text.Contains("Servidores")) LoadServers();
        else if (text.Contains("Perfiles"))  LoadProfiles();
        else if (text.Contains("Estado"))    LoadEstado();
        else if (text.Contains("Historial")) LoadHistory();
        // El explorador de blobs va contra la red: se puebla el combo al entrar, pero el listado
        // solo cuando el usuario lo pide, para no lanzar peticiones a Azure con cada clic de pestaña.
        else if (text.Contains("Blob")) _ = CargarCarpetasBlobAsync();
    }

    /// <summary>
    /// Carga la pestaña actual. La llama MainForm al ENTRAR a la pantalla (cada vez), porque
    /// OnVisibleChanged no se dispara en el primer Add —la propiedad Visible ya venía en true—, y por
    /// eso antes la lista no cargaba hasta cambiar de pestaña. Durante un despliegue en curso, las
    /// cargas que resetearían el estado (los combos de Desplegar) se saltan solas.
    /// </summary>
    public void RefrescarAlEntrar() => OnTabChanged();

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        // Primera vez que el control tiene handle (antes de pintarse): ya se puede cargar la pestaña.
        OnTabChanged();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) OnTabChanged();
    }

    // Helper: factory de pestaña con layout de barra-superior + cuerpo.
    //
    // El alto por omisión es 60 y no 52 porque una barra con botones necesita 49 px justos
    // (13 de relleno + 34 de botón + 2 de margen). Con 52 cabían por 3 px, margen que desaparece
    // en cuanto Windows aplica escalado de pantalla y deja los botones cortados por abajo.
    private static (TabPage tab, TableLayoutPanel body) NewTab(string title, int toolbarHeight = 60)
    {
        var tab = new TabPage(title) { BackColor = AppTheme.ContentBg };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, toolbarHeight));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tab.Controls.Add(body);
        return (tab, body);
    }
}
