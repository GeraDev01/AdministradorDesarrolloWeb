using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Controls;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms;

public class MainForm : ResponsiveForm
{
    private readonly CurrentUserContext _currentUser;
    private readonly AuthService _auth;
    private readonly IServiceProvider _sp;

    private Panel _pnlContent = null!;
    private Label _lblModuleTitle = null!;
    private Label _lblUserInfo = null!;
    private readonly List<(Button btn, string key)> _navButtons = [];
    private readonly List<(Button header, List<Button> items)> _navGroups = [];
    private List<Button>? _currentGroupItems;
    private UserControl? _currentControl;
    private string _currentKey = "";

    // Controles que CONSERVAN su estado al navegar (no se desechan): p. ej. un despliegue en curso.
    private readonly Dictionary<string, UserControl> _keepAlive = new();
    private static readonly HashSet<string> KeepAliveKeys = ["deployments"];
    private DeploymentControl? DeploymentCtrl => _keepAlive.TryGetValue("deployments", out var c) ? c as DeploymentControl : null;
    private System.Windows.Forms.Timer? _pendingTimer;
    private System.Windows.Forms.Timer? _slaTimer;
    private const string PerfNavBaseText  = "  🏆  Desempeño";
    private const string SlaNavBaseText   = "  ⏱  Mis SLA";
    private const string NotifNavBaseText = "  🔔  Avisos";
    private int _lastUnreadCount = -1;
    private string? _ultimoGloboKey;   // a dónde llevar al tocar el último globo de la bandeja

    // ── Bandeja del sistema ──────────────────────────────────────
    private NotifyIcon? _trayIcon;
    /// <summary>Distingue "cerrar de verdad" de "minimizar a la bandeja".</summary>
    private bool _salirDeVerdad;

    /// <summary>
    /// Cierre ordenado pedido desde fuera (aplicar una actualización, por ejemplo). Pasa por
    /// OnFormClosing, que es donde se avisa del despliegue en curso, se cierra la jornada y se
    /// consolidan los cronómetros. Devuelve false si el cierre se canceló.
    /// </summary>
    public bool CerrarOrdenadamente()
    {
        _salirDeVerdad = true;
        Close();
        if (IsDisposed) return true;
        _salirDeVerdad = false;   // alguien canceló: se deja como estaba
        return false;
    }

    // ── Estado de los avisos de SLA ──────────────────────────────
    /// <summary>Decide de qué SLA toca avisar en cada revisión (ver <see cref="SlaAlertTracker"/>).</summary>
    private readonly SlaAlertTracker _slaAlertas = new();

    private System.Windows.Forms.Timer? _scheduleTimer;
    /// <summary>Evita que dos ticks solapados lancen el mismo despliegue programado.</summary>
    private bool _despliegueProgramadoEnCurso;

    private System.Windows.Forms.Timer? _freshDeskTimer;
    /// <summary>Evita que dos ticks solapados hagan dos sincronizaciones de Freshdesk a la vez.</summary>
    private bool _freshDeskEnCurso;

    // ── Presencia ────────────────────────────────────────────────
    private System.Windows.Forms.Timer? _presenceTimer;
    private Button? _btnEstado;
    private PresenceService Presencia => (PresenceService)_sp.GetService(typeof(PresenceService))!;

    public MainForm(CurrentUserContext currentUser, AuthService auth, IServiceProvider sp)
    {
        _currentUser = currentUser; _auth = auth; _sp = sp;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Administrador de Desarrollo";
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;
        Size = new Size(1300, 820); MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // ── Sidebar ─────────────────────────────────────────────
        var sidebarTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = AppTheme.SidebarBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        sidebarTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
        sidebarTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        sidebarTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        sidebarTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var pnlLogo = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SidebarBg };
        pnlLogo.Controls.Add(new Label { Text = "⚙  Dev Admin", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleCenter, BackColor = AppTheme.SidebarBg });

        var pnlNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            BackColor = AppTheme.SidebarBg, Padding = new Padding(0, 8, 0, 0),
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };

        // Avisos: para todos los roles (te asignaron un ticket de DevOps o un requerimiento).
        AddNav(pnlNav, "🔔", "Avisos", "notifications");

        // El foro es de todo el equipo: si solo lo viera una parte, dejaría de ser un foro.
        AddNav(pnlNav, "💬", "Foro", "forum");

        // El Dashboard expone carga del equipo, recordatorios internos y ranking de desempeño:
        // nada de eso le corresponde a Operaciones, cuyo alcance son los despliegues.
        if (!_currentUser.IsOperaciones)
            AddNav(pnlNav, "📊", "Dashboard",       "dashboard");

        if (_currentUser.IsAdmin)
        {
            AddGroup(pnlNav, "Equipo");
            AddNav(pnlNav, "👤", "Desarrolladores",  "developers");
            AddNav(pnlNav, "🟢", "Quién está",       "presence");
            AddNav(pnlNav, "🌱", "Perfil y desarrollo", "dev-profiles");
            AddNav(pnlNav, "📢", "Comunicados",      "announcements");
            AddNav(pnlNav, "👥", "Equipos",          "teams");
            AddNav(pnlNav, "📇", "Contactos",        "contacts");

            AddGroup(pnlNav, "Trabajo");
            AddNav(pnlNav, "📋", "Requerimientos",   "requirements");
            AddNav(pnlNav, "🏁", "Sprint",           "sprint");
            AddNav(pnlNav, "📈", "Métricas",         "metrics");
            AddNav(pnlNav, "📑", "Reportes",         "reports");
            AddNav(pnlNav, "🎯", "Estimación y capacidad", "estimation");
            AddNav(pnlNav, "📝", "Minutas",          "minutes");
            AddNav(pnlNav, "🏖", "Vacaciones/Notas", "vacations");
            AddNav(pnlNav, "📋", "Permisos",          "leaves");
            AddNav(pnlNav, "🏆", "Desempeño",        "performance");
            AddNav(pnlNav, "📄", "Evaluaciones",     "dev-reports");
            AddNav(pnlNav, "🧩", "Actividades libres", "activities-admin");
            AddNav(pnlNav, "⏱", "SLA y recordatorios", "sla-admin");
            AddNav(pnlNav, "📊", "Cumplimiento SLA",  "sla-compliance");
            AddNav(pnlNav, "💡", "Sugerencias",       "suggestions");

            AddGroup(pnlNav, "Despliegue e Infraestructura");
            AddNav(pnlNav, "🚀", "Despliegues",      "deployments");
            AddNav(pnlNav, "🗓", "Programados",      "scheduled-deployments");
            AddNav(pnlNav, "☁",  "Recursos Azure",   "azure-resources");
            AddNav(pnlNav, "🛠", "Programas",        "software");

            AddGroup(pnlNav, "Integraciones y Correo");
            AddNav(pnlNav, "✉",  "Correo",            "email");
            AddNav(pnlNav, "🔷", "Azure DevOps",      "devops-tickets");
            AddNav(pnlNav, "📊", "Dashboard por tag",  "devops-tags");
            AddNav(pnlNav, "🎫", "Freshdesk",          "freshdesk-tickets");
            AddNav(pnlNav, "🔗", "Vínculos tickets",   "ticket-links");

            // Una cuenta Admin puede tener también un desarrollador vinculado; entonces tiene
            // compromisos propios que atender, no solo los del equipo.
            if (_currentUser.DeveloperId != null)
                AddNav(pnlNav, "⏱", "Mis SLA",          "my-sla");

            AddGroup(pnlNav, "Administración");
            AddNav(pnlNav, "📚", "Plantillas",         "templates");
            AddNav(pnlNav, "🔐", "Usuarios",           "users");
            AddNav(pnlNav, "📋", "Bitácora",            "audit");
            AddNav(pnlNav, "⚙",  "Configuración",       "config");
            // Va al final del grupo, después de Configuración: es la única pantalla cuya operación
            // no tiene deshacer, y no debe quedar a un resbalón de las que se usan a diario.
            AddNav(pnlNav, "🧹", "Limpieza de datos",   "data-cleanup");

            _currentGroupItems = null; // fin de grupos
        }
        else if (_currentUser.IsDesarrollador)
        {
            AddNav(pnlNav, "📊", "Mi Panel",         "my-performance");
            AddNav(pnlNav, "📋", "Mis Asignaciones", "my-assignments");
            AddNav(pnlNav, "🔷", "Mis tickets DevOps", "my-devops-tickets");
            AddNav(pnlNav, "🧩", "Mis Actividades",  "my-activities");
            AddNav(pnlNav, "📄", "Mis Evaluaciones", "my-evaluations");
            AddNav(pnlNav, "⏱", "Mis SLA",          "my-sla");
            // Su propio registro de asistencia: el mismo dato que ve el administrador en «Quién
            // está». Que cada quien vea el suyo lo vuelve transparente en vez de unilateral.
            AddNav(pnlNav, "🕒", "Mi jornada",       "my-presence");
            // El sprint del equipo, en solo consulta: la misma pantalla del administrador sin los
            // botones de escritura. Va con lo demás «suyo» porque es su trabajo comprometido.
            AddNav(pnlNav, "🏁", "Sprint",           "sprint");
            NavSep(pnlNav);
            // Herramienta, no compromiso propio: por eso va después del separador. Es la misma
            // pantalla del administrador en modo consulta (el servicio filtra qué tipos ve).
            AddNav(pnlNav, "📚", "Plantillas",       "templates");
            AddNav(pnlNav, "🏖", "Mis Vacaciones",   "my-vacations");
            // Junto a vacaciones: para quien lo usa es el mismo trámite, pedir tiempo fuera.
            AddNav(pnlNav, "🙋", "Mis Permisos",     "my-leaves");
            AddNav(pnlNav, "💡", "Sugerencias",      "my-suggestions");
        }
        else if (_currentUser.IsOperaciones)
        {
            AddNav(pnlNav, "🚀", "Despliegues",       "deployments");
            AddNav(pnlNav, "🗓", "Programados",       "scheduled-deployments");
        }
        // Cualquier otro rol (o una cuenta sin rol reconocido) se queda sin menú de forma
        // deliberada: antes caía en el 'else' y heredaba los permisos de Operaciones en silencio.

        var pnlLogout = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SidebarBg };
        var btnLogout = new Button
        {
            Text = "🚪  Cerrar sesión", Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, BackColor = AppTheme.SidebarBg,
            ForeColor = Color.FromArgb(148, 163, 184), Font = AppTheme.NavFont,
            Cursor = Cursors.Hand, FlatAppearance = { BorderSize = 0 }
        };
        btnLogout.MouseEnter += (_, _) => btnLogout.BackColor = AppTheme.Danger;
        btnLogout.MouseLeave += (_, _) => btnLogout.BackColor = AppTheme.SidebarBg;
        btnLogout.Click += BtnLogout_Click;
        pnlLogout.Controls.Add(btnLogout);

        sidebarTbl.Controls.Add(pnlLogo,   0, 0);
        sidebarTbl.Controls.Add(pnlNav,    0, 1);
        sidebarTbl.Controls.Add(pnlLogout, 0, 2);

        // ── Right ────────────────────────────────────────────────
        var rightTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        rightTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var topbarTbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = AppTheme.CardBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        topbarTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topbarTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
        topbarTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // AutoEllipsis: en una pantalla estrecha el título del módulo y el nombre del usuario dejan
        // de caber. Sin esto, el texto se dibuja igual y se monta encima del botón de estado; con
        // esto se corta con «…» y además el texto completo sale en el tooltip.
        _lblModuleTitle = new Label { Dock = DockStyle.Fill, Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(20, 0, 0, 0), AutoEllipsis = true };

        // Estado + identidad, a la derecha. El estado va aquí y no dentro de una pantalla porque se
        // cambia de paso, sin ir a buscarlo: si cuesta marcarlo, nadie lo marca y el tablero miente.
        var derecha = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = AppTheme.CardBg
        };
        derecha.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        derecha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        derecha.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _btnEstado = AppTheme.MakeSecondaryButton("🟢 Disponible", 142, 28);
        _btnEstado.Margin = new Padding(0, 13, 8, 13);
        _btnEstado.Click += (_, _) => MostrarMenuEstado();

        _lblUserInfo = new Label { Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 20, 0), AutoEllipsis = true };

        derecha.Controls.Add(_btnEstado,  0, 0);
        derecha.Controls.Add(_lblUserInfo, 1, 0);

        topbarTbl.Controls.Add(_lblModuleTitle, 0, 0);
        topbarTbl.Controls.Add(derecha,         1, 0);
        topbarTbl.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = AppTheme.Border });

        _pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        rightTbl.Controls.Add(topbarTbl, 0, 0);
        rightTbl.Controls.Add(_pnlContent, 0, 1);

        root.Controls.Add(sidebarTbl, 0, 0);
        root.Controls.Add(rightTbl,   1, 0);
        Controls.Add(root);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Cerrar con la X esconde a la bandeja en vez de terminar: así siguen corriendo los
        // recordatorios de SLA. Para salir de verdad está "Salir" en el menú de la bandeja.
        // Windows apagándose o cerrando sesión NO se intercepta: eso sí debe terminar.
        if (!_salirDeVerdad && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(6_000, "Sigo aquí",
                "La aplicación quedó en la bandeja vigilando tus SLA. Para cerrarla del todo, clic derecho → Salir.",
                ToolTipIcon.Info);
            return;
        }

        // Salir con un despliegue en curso: avisar (solo si lo pide el usuario, no en un apagado).
        if (e.CloseReason == CloseReason.UserClosing && DeploymentCtrl is { DeployEnCurso: true })
        {
            var r = MessageBox.Show(
                "Hay un DESPLIEGUE EN CURSO. Si cierras la aplicación se cancela.\n\n¿Cerrar de todos modos?",
                "Despliegue en curso", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) { e.Cancel = true; _salirDeVerdad = false; return; }
            DeploymentCtrl.CancelarDespliegue();
        }

        // Salir con la actualización a medio bajar: se pierde y hay que empezar de cero.
        if (e.CloseReason == CloseReason.UserClosing
            && _sp.GetService(typeof(UpdateService)) is UpdateService upd && upd.DescargaEnCurso)
        {
            var r = MessageBox.Show(
                "Se está DESCARGANDO una actualización. Si cierras ahora, la descarga se pierde." +
                Environment.NewLine + Environment.NewLine + "¿Cerrar de todos modos?",
                "Descarga en curso", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) { e.Cancel = true; _salirDeVerdad = false; return; }
        }

        // La jornada se cierra al salir de verdad. Con la X (que solo esconde a la bandeja) NO se
        // cierra: la aplicación sigue viva vigilando SLA, y eso es seguir conectado.
        TerminarPresencia();

        _pendingTimer?.Stop();
        _pendingTimer?.Dispose();
        _slaTimer?.Stop();
        _slaTimer?.Dispose();
        _scheduleTimer?.Stop();
        _scheduleTimer?.Dispose();
        _freshDeskTimer?.Stop();
        _freshDeskTimer?.Dispose();
        foreach (var c in _keepAlive.Values) { try { c.Dispose(); } catch { } }
        _keepAlive.Clear();
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
        // Cierre limpio: consolidar el tiempo real de cualquier cronómetro activo
        // para no perderlo ni dejar sesiones huérfanas que inflen el tiempo.
        try { ((WorkSessionService)_sp.GetService(typeof(WorkSessionService))!).PauseAllActive(); } catch { }

        // La actualización descargada se arma AQUÍ y no al terminar la descarga: el updater de
        // Velopack solo espera 60 segundos a ver morir este proceso, y entre bajarla y salir de
        // verdad pasan horas. Va al final, después de cerrar la jornada y consolidar los
        // cronómetros: lo último que ocurre es el reemplazo de archivos.
        try { ((UpdateService)_sp.GetService(typeof(UpdateService))!).AplicarAlSalir(); } catch { }

        base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var roleLabel = _currentUser.Role switch
        {
            UserRole.Admin        => "Admin",
            UserRole.Operaciones  => "Operaciones",
            UserRole.Desarrollador => "Desarrollador",
            _                     => "—"
        };
        _lblUserInfo.Text = $"👤 {_currentUser.User?.FullName}  ({roleLabel})";
        var defaultKey = _currentUser.IsDesarrollador ? "my-performance"
                       : _currentUser.IsOperaciones  ? "deployments"
                       : "dashboard";
        Navigate(defaultKey);

        // Presencia: se abre la jornada y se late. El latido es lo que distingue «sigue ahí» de
        // «se le colgó la aplicación»; sin él, un cuelgue dejaría a esa persona marcada como
        // conectada para siempre.
        IniciarPresencia();

        // Badge de autocalificaciones pendientes (solo el jefe/Admin lo ve).
        if (_currentUser.IsAdmin)
        {
            UpdatePendingBadge();
            _pendingTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _pendingTimer.Tick += (_, _) => UpdatePendingBadge();
            _pendingTimer.Start();
        }

        // Vigilancia de SLA: badge en el menú y aviso en cuanto algo requiera atención.
        ConfigurarBandeja();
        _slaTimer = new System.Windows.Forms.Timer { Interval = 5 * 60_000 };
        _slaTimer.Tick += (_, _) => { RevisarSla(primeraVez: false); RevisarCompromisos(); RevisarNotificaciones(silencioso: false); RevisarTareasAutomaticas(); };
        _slaTimer.Start();
        RevisarSla(primeraVez: true);
        RevisarCompromisos();
        RevisarNotificaciones(silencioso: true);   // primera vez: solo fija el contador, sin globo
        RevisarTareasAutomaticas();                // respaldo/resumen automáticos, una vez por período
        RevisarVersion();                          // una sola vez por arranque, no en cada ciclo

        // Despliegues programados: se revisan cada minuto porque una cita a las 02:00 debe
        // arrancar cerca de las 02:00, no hasta cinco minutos después.
        if (_currentUser.IsAdmin || _currentUser.IsOperaciones)
        {
            _scheduleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _scheduleTimer.Tick += async (_, _) => await RevisarProgramadosAsync();
            _scheduleTimer.Start();
            _ = RevisarProgramadosAsync();
        }

        // Freshdesk: avisar cuando me asignen un ticket. Solo Admin (es quien tiene esa pantalla y la
        // API key configurada, así que «asignado a mí» = al dueño de la key). Corre en segundo plano
        // con su propio contexto; la primera pasada solo fija la línea base. Cada 10 min para no
        // saturar la API de Freshdesk.
        if (_currentUser.IsAdmin)
        {
            _freshDeskTimer = new System.Windows.Forms.Timer { Interval = 10 * 60_000 };
            _freshDeskTimer.Tick += (_, _) => _ = RevisarFreshDeskAsync();
            _freshDeskTimer.Start();
            _ = RevisarFreshDeskAsync();
        }
    }

    /// <summary>
    /// Ícono en la bandeja para que la aplicación siga vigilando los SLA con la ventana cerrada.
    /// Sin esto, cerrar la ventana mataba el proceso y con él los recordatorios y el escalamiento
    /// al jefe: nadie se enteraba de un vencimiento hasta que alguien volviera a abrir la app.
    /// </summary>
    private void ConfigurarBandeja()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => RestaurarDesdeBandeja());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => { _salirDeVerdad = true; Close(); });

        _trayIcon = new NotifyIcon
        {
            Icon = AppTheme.AppIcon ?? SystemIcons.Application,
            Text = "Administrador de Desarrollo",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestaurarDesdeBandeja();
        // Tocar el globo lleva directo a lo que lo provocó (avisos o SLA, según el último globo).
        _trayIcon.BalloonTipClicked += (_, _) =>
        {
            RestaurarDesdeBandeja();
            var target = _ultimoGloboKey;
            if (target == null || !PuedeVer(target)) target = PuedeVer("my-sla") ? "my-sla" : "notifications";
            Navigate(target);
        };
    }

    /// <summary>
    /// Saca la ventana de la bandeja y le da el foco. Antes forzaba el maximizado, y eso le
    /// cambiaba el tamaño a quien la había dejado a media pantalla; ahora vuelve como estaba.
    /// </summary>
    private void RestaurarDesdeBandeja() => WindowActivation.Traer(this);

    // ── Presencia ────────────────────────────────────────────────

    private void IniciarPresencia()
    {
        try
        {
            Presencia.Entrar();
            PintarEstado();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Presencia: {ex.Message}"); }

        _presenceTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)PresenceService.IntervaloLatido.TotalMilliseconds
        };
        _presenceTimer.Tick += (_, _) =>
        {
            // Un fallo de red no debe tumbar la aplicación ni parar el latido siguiente.
            try { Presencia.Latir(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Latido: {ex.Message}"); }
        };
        _presenceTimer.Start();
    }

    private void MostrarMenuEstado()
    {
        var menu = new ContextMenuStrip();
        foreach (var estado in Enum.GetValues<PresenceState>())
        {
            // «Ausente» no se elige a mano: es lo que dice el sistema de quien no está.
            if (estado == PresenceState.Ausente) continue;
            var e = estado;
            var item = menu.Items.Add($"{PresenceService.Icono(e)}  {PresenceService.Etiqueta(e)}", null,
                (_, _) => CambiarEstado(e, null));
            // El mismo código de color que el botón: si se aprende «verde = disponible» en un
            // sitio, se tiene que reconocer en el otro.
            item.ForeColor = AppTheme.PresenceColor(e);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✏  Estado con nota…", null, (_, _) => CambiarEstadoConNota());

        if (_btnEstado != null) menu.Show(_btnEstado, new Point(0, _btnEstado.Height));
    }

    private void CambiarEstadoConNota()
    {
        var (estadoActual, notaActual) = Presencia.MiEstado();
        using var frm = new PresenceNoteForm(estadoActual, notaActual);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        CambiarEstado(frm.Estado, frm.Nota);
    }

    private void CambiarEstado(PresenceState estado, string? nota)
    {
        try
        {
            Presencia.CambiarEstado(estado, nota);
            PintarEstado();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar tu estado:\n{ex.Message}", "Estado",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PintarEstado()
    {
        if (_btnEstado == null) return;
        try
        {
            var (estado, nota) = Presencia.MiEstado();
            _btnEstado.Text = $"{PresenceService.Icono(estado)} {PresenceService.Etiqueta(estado)}";

            // Botón RELLENO con el color del estado y texto blanco. Antes pasaba lo contrario de
            // lo pedido: Disponible era el único estado sin color, y los otros cinco compartían el
            // mismo azul, o sea que no se distinguían entre sí. Con texto blanco los seis fondos
            // dan contraste ≥ 4.7:1 (hay prueba que lo afirma).
            var color = AppTheme.PresenceColor(estado);
            _btnEstado.BackColor = color;
            _btnEstado.ForeColor = Color.White;
            _btnEstado.Font = AppTheme.BoldFont;
            _btnEstado.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.15f);
            new ToolTip().SetToolTip(_btnEstado,
                string.IsNullOrWhiteSpace(nota)
                    ? "Tu estado, visible para el líder. Clic para cambiarlo."
                    : $"{PresenceService.Etiqueta(estado)} — {nota}");
        }
        catch { /* sin base, el botón se queda como esté */ }
    }

    /// <summary>
    /// Avisa si el administrador publicó una versión más nueva que la que corre. Una sola vez por
    /// arranque y una sola vez por versión (la memoria vive en %APPDATA%): un aviso que sale en
    /// cada ciclo se cierra por reflejo y deja de leerse.
    /// </summary>
    private async void RevisarVersion()
    {
        try
        {
            if (_sp.GetService(typeof(UpdateService)) is not UpdateService updates) return;

            var estado = UpdateNoticeState.Cargar();
            // Consulta el feed de Velopack si esta copia se instaló con él; si es portátil, cae al
            // aviso manual de AppSettings. Va con await para no bloquear el arranque mientras
            // responde la red.
            var oferta = await updates.BuscarAsync(estado.UltimaVersionAvisada);
            if (oferta == null || IsDisposed) return;

            // Se anota ANTES de mostrar: si la ventana falla al abrirse, es preferible perder un
            // aviso que repetirlo en cada arranque. Solo en el modo manual — en el automático el
            // feed es la fuente de verdad y volver a ofrecerlo no molesta: el botón dirá
            // «Reiniciar» si ya está descargada.
            if (oferta.Modo == ModoActualizacion.AvisoManual)
            {
                estado.UltimaVersionAvisada = oferta.VersionTexto;
                try { estado.Guardar(); } catch { /* sin memoria, a lo sumo se repite */ }
            }

            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                _trayIcon?.ShowBalloonTip(10_000, $"Versión {oferta.VersionTexto} disponible",
                    oferta.Modo == ModoActualizacion.Automatica
                        ? "Ábrela para instalarla; se aplica al cerrar."
                        : "Ábrela cuando puedas para ver cómo actualizar.", ToolTipIcon.Info);
                return;
            }

            // Show y no ShowDialog: es un aviso, no un trámite para empezar a trabajar.
            new UpdateNoticeForm(oferta, updates).Show(this);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Aviso de versión: {ex.Message}"); }
    }

    /// <summary>
    /// Avisa a cada desarrollador de sus fechas comprometidas que se acercan. Va ANTES de
    /// RevisarNotificaciones para que los avisos que cree se cuenten en el badge de esta misma
    /// vuelta en vez de esperar cinco minutos.
    /// </summary>
    private void RevisarCompromisos()
    {
        try
        {
            // Solo la sesión del administrador barre: escribe avisos para TODO el equipo, así que
            // un único escritor evita que N instancias abiertas compitan por crear lo mismo. El
            // respaldo definitivo es el índice único UX_Notif_Dedupe.
            if (_currentUser.IsAdmin
                && _sp.GetService(typeof(CommitmentAlertService)) is CommitmentAlertService svc)
                svc.RevisarYAvisar();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Compromisos: {ex.Message}"); }
    }

    private void TerminarPresencia()
    {
        _presenceTimer?.Stop();
        _presenceTimer?.Dispose();
        _presenceTimer = null;
        try { Presencia.Salir(); } catch { /* cerrando: no vale tumbar la salida por esto */ }
    }

    /// <summary>
    /// Revisa los SLA cada ciclo: escala los incumplimientos si la sesión es de administración, y
    /// avisa a la persona de sus propios compromisos.
    ///
    /// El aviso NO se repite en cada ciclo (un recordatorio que sale cada cinco minutos deja de
    /// leerse y se cierra por reflejo), pero SÍ vuelve a salir en cuanto aparece un compromiso
    /// nuevo o uno se pasa de la fecha límite. Antes solo avisaba al iniciar sesión: quien dejaba
    /// la aplicación abierta todo el día no se enteraba de nada hasta el día siguiente.
    /// </summary>
    private void RevisarSla(bool primeraVez)
    {
        try
        {
            // Administración: escalar lo vencido. Independiente de lo de abajo, porque una cuenta
            // Admin puede tener también un desarrollador vinculado y entonces le toca lo de ambos.
            if (_currentUser.IsAdmin)
            {
                var notify = (SlaNotificationService)_sp.GetService(typeof(SlaNotificationService))!;
                _ = notify.EscalarIncumplimientosAsync();
            }

            if (_currentUser.DeveloperId is not int devId) return;

            var sla = (SlaService)_sp.GetService(typeof(SlaService))!;
            var pendientes = sla.PendientesDeAviso(devId);
            UpdateSlaBadge(pendientes.Count);

            var ahora = DateTime.UtcNow;
            var nuevos = _slaAlertas.Nuevos(pendientes, ahora);
            if (nuevos.Count == 0) return;

            MostrarAvisoSla(nuevos, pendientes.Count, ahora, primeraVez);

            // El correo es complemento, no sustituto: si falla, el aviso de arriba ya se dio.
            var notifyDev = (SlaNotificationService)_sp.GetService(typeof(SlaNotificationService))!;
            _ = notifyDev.AvisarDesarrolladorAsync(devId);
        }
        catch (Exception ex)
        {
            // Un problema al revisar SLA no debe impedir usar la aplicación.
            System.Diagnostics.Debug.WriteLine($"Revisión de SLA fallida: {ex.Message}");
        }
    }

    /// <summary>
    /// Diálogo si la ventana está a la vista; globo de la bandeja si está oculta. Un MessageBox
    /// con la app minimizada se queda esperando detrás de todo sin que nadie lo vea.
    /// </summary>
    private void MostrarAvisoSla(List<SlaCommitment> nuevos, int totalPendientes, DateTime ahora, bool primeraVez)
    {
        int vencidos = nuevos.Count(p => p.EstaVencido(ahora));
        var titulo = vencidos > 0 ? "SLA fuera de plazo" : "Recordatorio de SLA";

        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            var resumen = nuevos.Count == 1
                ? SlaService.DescribirObjetivo(nuevos[0])
                : $"{nuevos.Count} compromisos requieren un comentario en su ticket.";
            _ultimoGloboKey = "my-sla";
            _trayIcon?.ShowBalloonTip(10_000, titulo, resumen,
                vencidos > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
            return;
        }

        var detalle = string.Join("\n", nuevos.Take(5)
            .Select(p => $"  • {SlaService.DescribirObjetivo(p)}  —  vence {p.DueAtUtc.ToLocalTime():dd/MM HH:mm}"));
        if (nuevos.Count > 5) detalle += $"\n  … y {nuevos.Count - 5} más";

        var encabezado = primeraVez
            ? $"Tienes {totalPendientes} compromiso(s) que requieren un comentario en su ticket"
            : $"{nuevos.Count} compromiso(s) acaban de requerir tu atención";

        MessageBox.Show(
            encabezado + (vencidos > 0 ? $", {vencidos} ya fuera de plazo" : "") + ":\n\n" + detalle +
            "\n\nEntra a «Mis SLA» para comentarlos.",
            titulo, MessageBoxButtons.OK,
            vencidos > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    /// <summary>
    /// Ejecuta los despliegues cuya hora llegó. Corre también con la ventana en la bandeja: es lo
    /// que hace que "programar para las 2 a.m." signifique algo sin instalar un servicio.
    ///
    /// La toma de la cita es condicional en la base, así que si hay varias aplicaciones abiertas
    /// solo una la ejecuta. La bandera local evita además que dos ticks del mismo equipo se solapen
    /// durante un despliegue que dure más de un minuto.
    /// </summary>
    private async Task RevisarProgramadosAsync()
    {
        if (_despliegueProgramadoEnCurso) return;
        _despliegueProgramadoEnCurso = true;
        try
        {
            var schedule = (DeploymentScheduleService)_sp.GetService(typeof(DeploymentScheduleService))!;

            // Primero se descartan las citas cuya tolerancia ya expiró: una cita de anoche no debe
            // dispararse porque alguien abrió la aplicación por la mañana.
            foreach (var perdida in schedule.MarcarPerdidas())
                _trayIcon?.ShowBalloonTip(10_000, "Despliegue programado perdido",
                    $"«{perdida.Profile?.Name ?? "?"}» no se ejecutó: no había ninguna aplicación abierta a su hora.",
                    ToolTipIcon.Warning);

            var cita = schedule.TomarSiguiente();
            if (cita == null) return;

            var nombre = $"{cita.AppRelease?.AppSystem?.Name} v{cita.AppRelease?.Version} → {cita.Profile?.Name}";
            _trayIcon?.ShowBalloonTip(10_000, "Despliegue programado iniciado", nombre, ToolTipIcon.Info);

            // Diálogo de progreso solo si hay alguien viendo; en la bandeja corre en silencio.
            ProgressDialog? dlg = null;
            IProgress<string> progress;
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                dlg = new ProgressDialog($"Despliegue programado — {nombre}");
                dlg.Show(this);
                progress = new Progress<string>(dlg.Append);
            }
            else progress = new Progress<string>(_ => { });

            try
            {
                var (ok, mensaje) = await schedule.EjecutarAsync(cita.Id, progress);
                _trayIcon?.ShowBalloonTip(15_000,
                    ok ? "Despliegue programado completado" : "Despliegue programado con problemas",
                    $"{nombre}\n{mensaje}", ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            }
            finally { dlg?.MarkDone(); }
        }
        catch (Exception ex)
        {
            // Nunca debe tumbar la aplicación ni el temporizador.
            System.Diagnostics.Debug.WriteLine($"Revisión de programados fallida: {ex.Message}");
        }
        finally { _despliegueProgramadoEnCurso = false; }
    }

    /// <summary>
    /// Qué secciones puede abrir cada rol. Es la lista blanca que respalda al menú: el menú decide
    /// qué se ve, esto decide qué se puede abrir. Los servicios validan por su cuenta de todos modos.
    /// </summary>
    private bool PuedeVer(string key)
    {
        if (_currentUser.IsAdmin) return true;

        // El foro es de todo el equipo, así que entra en las listas de los dos roles.
        if (_currentUser.IsOperaciones)
            return key is "notifications" or "forum" or "deployments" or "scheduled-deployments";

        if (_currentUser.IsDesarrollador)
            return key is "notifications" or "forum" or "dashboard" or "my-performance" or "my-assignments"
                       or "my-devops-tickets" or "my-activities" or "my-evaluations" or "my-sla"
                       or "my-vacations" or "my-leaves" or "my-suggestions" or "templates" or "sprint" or "my-presence";

        return false;
    }

    /// <summary>Contador de SLA pendientes sobre el botón "Mis SLA" del menú.</summary>
    private void UpdateSlaBadge(int cuantos)
    {
        var pair = _navButtons.FirstOrDefault(x => x.key == "my-sla");
        if (pair.btn == null) return;
        pair.btn.Text = cuantos > 0 ? $"{SlaNavBaseText}   ({cuantos})" : SlaNavBaseText;
        pair.btn.ForeColor = cuantos > 0 ? AppTheme.Warning : AppTheme.SidebarText;
    }

    /// <summary>Contador de avisos sin leer sobre el botón "Avisos" del menú.</summary>
    private void UpdateNotifBadge(int unread)
    {
        var pair = _navButtons.FirstOrDefault(x => x.key == "notifications");
        if (pair.btn == null) return;
        pair.btn.Text = unread > 0 ? $"{NotifNavBaseText}   ({unread})" : NotifNavBaseText;
        pair.btn.ForeColor = (unread > 0 && _currentKey != "notifications") ? AppTheme.Warning : AppTheme.SidebarText;
    }

    /// <summary>
    /// Detecta tickets de DevOps recién asignados a mí (crea sus avisos) y refresca el contador de
    /// avisos. Muestra un globo en la bandeja cuando el total sin leer creció, cubriendo también los
    /// avisos que otra persona generó (p. ej. el administrador me asignó un requerimiento).
    /// </summary>
    private void RevisarNotificaciones(bool silencioso)
    {
        if (_currentUser.User is not { } user) return;
        try
        {
            var notif = (NotificationService)_sp.GetService(typeof(NotificationService))!;

            if (_currentUser.DeveloperId is int devId)
            {
                var db = (AppDbContext)_sp.GetService(typeof(AppDbContext))!;
                var dev = db.Developers.Find(devId);
                var devops = (AzureDevOpsService)_sp.GetService(typeof(AzureDevOpsService))!;
                if (dev != null && devops.IsEnabled) devops.DetectarAsignadosNuevos(user.Id, dev);
            }

            int unread = notif.CountUnread(user.Id);
            UpdateNotifBadge(unread);
            if (!silencioso && _lastUnreadCount >= 0 && unread > _lastUnreadCount)
            {
                _ultimoGloboKey = "notifications";
                _trayIcon?.ShowBalloonTip(8_000, "Tienes avisos nuevos",
                    $"{unread} aviso(s) sin leer. Ábrelos en el menú «Avisos».", ToolTipIcon.Info);
            }
            _lastUnreadCount = unread;
        }
        catch { /* red o permisos: un aviso nunca debe tumbar la app */ }
    }

    /// <summary>
    /// Sincroniza Freshdesk en segundo plano y, si me asignaron tickets nuevos, refresca el contador
    /// de avisos y suelta el globo de la bandeja. Nunca bloquea la UI ni tumba la app por un error de
    /// red. Reentrancia protegida para que dos ticks no sincronicen a la vez.
    /// </summary>
    private async Task RevisarFreshDeskAsync()
    {
        if (_freshDeskEnCurso) return;
        if (_currentUser.User is not { } user) return;
        _freshDeskEnCurso = true;
        try
        {
            var fd = (FreshDeskService)_sp.GetService(typeof(FreshDeskService))!;
            if (!fd.IsEnabled) return;
            int nuevos = await fd.SincronizarYDetectarAsignadosAsync(user.Id);
            if (nuevos > 0 && !IsDisposed)
                BeginInvoke(() => RevisarNotificaciones(silencioso: false));
        }
        catch { /* red o permisos: Freshdesk nunca debe tumbar la app */ }
        finally { _freshDeskEnCurso = false; }
    }

    /// <summary>Actualiza el contador de autocalificaciones pendientes en el botón "Desempeño" del sidebar.</summary>
    private void UpdatePendingBadge()
    {
        if (!_currentUser.IsAdmin) return;
        var pair = _navButtons.FirstOrDefault(x => x.key == "performance");
        if (pair.btn == null) return;
        int pending;
        try
        {
            var db = (AppDbContext)_sp.GetService(typeof(AppDbContext))!;
            pending = db.PointEntries.Count(p => p.ApprovalStatus == PointApprovalStatus.Pendiente);
        }
        catch { return; }

        pair.btn.Text = pending > 0 ? $"{PerfNavBaseText}   🔴 {pending}" : PerfNavBaseText;
        // Resaltar cuando hay pendientes, salvo que sea el módulo activo.
        pair.btn.ForeColor = (pending > 0 && _currentKey != "performance") ? AppTheme.Warning : AppTheme.SidebarText;
    }

    private void AddNav(FlowLayoutPanel parent, string icon, string label, string key)
    {
        var btn = new Button
        {
            Text = $"  {icon}  {label}", Width = 225, Height = 42,
            FlatStyle = FlatStyle.Flat, BackColor = AppTheme.SidebarBg,
            ForeColor = AppTheme.SidebarText, Font = AppTheme.NavFont,
            TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
            Margin = Padding.Empty, FlatAppearance = { BorderSize = 0 }
        };
        btn.MouseEnter += (_, _) => { if (_currentKey != key) btn.BackColor = AppTheme.SidebarHover; };
        btn.MouseLeave += (_, _) => { if (_currentKey != key) btn.BackColor = AppTheme.SidebarBg; };
        btn.Click += (_, _) => Navigate(key);
        parent.Controls.Add(btn);
        _navButtons.Add((btn, key));
        _currentGroupItems?.Add(btn); // pertenece al grupo (colapsable) en construcción
    }

    private static void NavSep(FlowLayoutPanel parent) =>
        parent.Controls.Add(new Panel { Width = 225, Height = 1, BackColor = AppTheme.SidebarHover, Margin = new Padding(0, 6, 0, 6) });

    // ── Grupos colapsables (acordeón) ────────────────────────────
    private void AddGroup(FlowLayoutPanel parent, string title)
    {
        var header = new Button
        {
            Width = 225, Height = 34, Tag = title,
            FlatStyle = FlatStyle.Flat, BackColor = AppTheme.SidebarBg,
            ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI Semibold", 9f),
            TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
            Margin = new Padding(0, 6, 0, 0), FlatAppearance = { BorderSize = 0 }
        };
        var items = new List<Button>();
        header.Click += (_, _) => ToggleGroup(header, items);
        header.MouseEnter += (_, _) => header.ForeColor = Color.White;
        header.MouseLeave += (_, _) => header.ForeColor = Color.FromArgb(148, 163, 184);
        parent.Controls.Add(header);
        _navGroups.Add((header, items));
        _currentGroupItems = items;
        SetHeaderArrow(header, false);
    }

    private void ToggleGroup(Button header, List<Button> items)
    {
        bool wasCollapsed = items.Count == 0 || !items[0].Visible;
        CollapseAllGroups();
        if (wasCollapsed)
        {
            foreach (var b in items) b.Visible = true;
            SetHeaderArrow(header, true);
        }
    }

    private void CollapseAllGroups()
    {
        foreach (var (h, its) in _navGroups)
        {
            foreach (var b in its) b.Visible = false;
            SetHeaderArrow(h, false);
        }
    }

    private void ExpandGroupForKey(string key)
    {
        if (_navGroups.Count == 0) return;
        var pair = _navButtons.FirstOrDefault(x => x.key == key);
        if (pair.btn == null) { CollapseAllGroups(); return; }
        foreach (var (h, its) in _navGroups)
        {
            bool has = its.Contains(pair.btn);
            foreach (var b in its) b.Visible = has;
            SetHeaderArrow(h, has);
        }
    }

    private static void SetHeaderArrow(Button header, bool expanded) =>
        header.Text = $"  {(expanded ? "▾" : "▸")}  {(header.Tag as string ?? "").ToUpperInvariant()}";

    /// <summary>
    /// Dispara las tareas de fondo que corren una vez por período (respaldo automático de la BD y
    /// resumen por correo). Solo admin. No bloquea la UI: cada tarea corre en segundo plano y se
    /// autolimita por su propia marca de última corrida; los fallos solo se registran.
    /// </summary>
    private void RevisarTareasAutomaticas()
    {
        if (!_currentUser.IsAdmin) return;
        try
        {
            var backup = (AutoBackupService)_sp.GetService(typeof(AutoBackupService))!;
            var digest = (DigestService)_sp.GetService(typeof(DigestService))!;
            _ = SafeRunAsync(() => backup.RevisarYRespaldarAsync());
            _ = SafeRunAsync(() => digest.RevisarYEnviarAsync());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tareas automáticas: {ex.Message}"); }
    }

    private static async Task SafeRunAsync(Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tarea automática falló: {ex.Message}"); }
    }

    /// <summary>Ctrl+K abre la búsqueda global (solo administrador) para saltar a cualquier pantalla.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K) && _currentUser.IsAdmin)
        {
            AbrirBusquedaGlobal();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void AbrirBusquedaGlobal()
    {
        var search = (SearchService)_sp.GetService(typeof(SearchService))!;
        using var frm = new GlobalSearchForm(search);
        if (frm.ShowDialog(this) == DialogResult.OK && frm.SelectedNavKey is string key)
            Navigate(key);
    }

    private void Navigate(string key)
    {
        _currentKey = key;
        ExpandGroupForKey(key);
        foreach (var (btn, k) in _navButtons)
        {
            btn.BackColor = k == key ? AppTheme.SidebarActive : AppTheme.SidebarBg;
            btn.Font = k == key ? AppTheme.NavFontBold : AppTheme.NavFont;
        }

        // Segunda barrera: Navigate no comprobaba nada, así que una clave alcanzada por otra vía
        // (un enlace interno, el caso por defecto) abría pantallas que el rol no debería ver.
        if (!PuedeVer(key))
        {
            MessageBox.Show("No tienes acceso a esa sección.", "Sin permiso",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Los controles «keep-alive» NO se desechan al salir de ellos: se sacan del panel pero se
        // conservan (con su despliegue en curso, su log y su barra de avance intactos).
        if (_currentControl != null && !_keepAlive.ContainsValue(_currentControl))
            _currentControl.Dispose();
        _pnlContent.Controls.Clear();

        UserControl ctrl;
        if (KeepAliveKeys.Contains(key) && _keepAlive.TryGetValue(key, out var vivo))
            ctrl = vivo;
        else
        {
        ctrl = key switch
        {
            "dashboard"       => (UserControl)_sp.GetService(typeof(DashboardControl))!,
            "scheduled-deployments" => (UserControl)_sp.GetService(typeof(ScheduledDeploymentsControl))!,
            "developers"      => (UserControl)_sp.GetService(typeof(DevelopersControl))!,
            "dev-profiles"    => (UserControl)_sp.GetService(typeof(DeveloperProfilesControl))!,
            "announcements"   => (UserControl)_sp.GetService(typeof(AnnouncementsControl))!,
            "teams"           => (UserControl)_sp.GetService(typeof(TeamsControl))!,
            "contacts"        => (UserControl)_sp.GetService(typeof(ContactsControl))!,
            "metrics"         => (UserControl)_sp.GetService(typeof(MetricsControl))!,
            "reports"         => (UserControl)_sp.GetService(typeof(ReportsControl))!,
            "estimation"      => (UserControl)_sp.GetService(typeof(EstimationCapacityControl))!,
            "email"           => (UserControl)_sp.GetService(typeof(EmailControl))!,
            "requirements"    => (UserControl)_sp.GetService(typeof(RequirementsControl))!,
            "sprint"          => (UserControl)_sp.GetService(typeof(SprintControl))!,
            "minutes"         => (UserControl)_sp.GetService(typeof(MinutesControl))!,
            "vacations"       => (UserControl)_sp.GetService(typeof(VacationsControl))!,
            "leaves"          => (UserControl)_sp.GetService(typeof(LeaveRequestsControl))!,
            "performance"     => (UserControl)_sp.GetService(typeof(PerformanceControl))!,
            "dev-reports"     => (UserControl)_sp.GetService(typeof(DeveloperReportsControl))!,
            "deployments"     => (UserControl)_sp.GetService(typeof(DeploymentControl))!,
            "azure-resources" => (UserControl)_sp.GetService(typeof(AzureResourcesControl))!,
            "software"        => (UserControl)_sp.GetService(typeof(SoftwareControl))!,
            "devops-tickets"  => (UserControl)_sp.GetService(typeof(AzureDevOpsControl))!,
            "devops-tags"     => (UserControl)_sp.GetService(typeof(DevOpsTagsDashboardControl))!,
            "freshdesk-tickets" => (UserControl)_sp.GetService(typeof(FreshDeskControl))!,
            "ticket-links"    => (UserControl)_sp.GetService(typeof(TicketLinkControl))!,
            "forum"           => (UserControl)_sp.GetService(typeof(ForumControl))!,
            "presence"        => (UserControl)_sp.GetService(typeof(PresenceControl))!,
            "templates"       => (UserControl)_sp.GetService(typeof(TemplatesControl))!,
            "users"           => (UserControl)_sp.GetService(typeof(UserManagementControl))!,
            "audit"           => (UserControl)_sp.GetService(typeof(AuditLogControl))!,
            "config"          => (UserControl)_sp.GetService(typeof(ConfigurationControl))!,
            "data-cleanup"    => (UserControl)_sp.GetService(typeof(DataCleanupControl))!,
            "activities-admin" => (UserControl)_sp.GetService(typeof(ActivitiesAdminControl))!,
            "sla-admin"       => (UserControl)_sp.GetService(typeof(SlaAdminControl))!,
            "sla-compliance"  => (UserControl)_sp.GetService(typeof(SlaComplianceControl))!,
            "suggestions"     => (UserControl)_sp.GetService(typeof(SuggestionsControl))!,
            "my-suggestions"  => (UserControl)_sp.GetService(typeof(MySuggestionsControl))!,
            "my-sla"          => (UserControl)_sp.GetService(typeof(MySlaControl))!,
            "my-assignments"  => (UserControl)_sp.GetService(typeof(MyAssignmentsControl))!,
            "my-devops-tickets" => (UserControl)_sp.GetService(typeof(MyDevOpsTicketsControl))!,
            "my-presence"     => (UserControl)_sp.GetService(typeof(MyPresenceControl))!,
            "notifications"   => (UserControl)_sp.GetService(typeof(NotificationsControl))!,
            "my-performance"  => (UserControl)_sp.GetService(typeof(MyDevPerformanceControl))!,
            "my-activities"   => (UserControl)_sp.GetService(typeof(MyActivitiesControl))!,
            "my-evaluations"  => (UserControl)_sp.GetService(typeof(MyEvaluationsControl))!,
            "my-vacations"    => (UserControl)_sp.GetService(typeof(MyVacationsControl))!,
            "my-leaves"       => (UserControl)_sp.GetService(typeof(MyLeavesControl))!,
            _                 => (UserControl)_sp.GetService(typeof(DashboardControl))!
        };
            if (KeepAliveKeys.Contains(key)) _keepAlive[key] = ctrl;
        }

        _lblModuleTitle.Text = key switch
        {
            "dashboard"       => "📊  Dashboard",
            "developers"      => "👤  Desarrolladores",
            "dev-profiles"    => "🌱  Perfil y desarrollo del equipo",
            "announcements"   => "📢  Comunicados al equipo",
            "teams"           => "👥  Equipos",
            "contacts"        => "📇  Contactos",
            "metrics"         => "📈  Métricas de Tiempo de Vida",
            "reports"         => "📑  Reportes",
            "estimation"      => "🎯  Estimación y capacidad",
            "email"           => "✉  Correo",
            "requirements"    => "📋  Requerimientos",
            "sprint"          => "🏁  Seguimiento del Sprint",
            "minutes"         => "📝  Minutas",
            "vacations"       => "🏖  Vacaciones y Notas",
            "leaves"          => "📋  Permisos",
            "performance"     => "🏆  Desempeño y Ranking",
            "dev-reports"     => "📄  Evaluaciones y reportes de desarrollador",
            "deployments"     => "🚀  Despliegues",
            "azure-resources" => "☁  Recursos Azure",
            "software"        => "🛠  Programas y Utilerías",
            "devops-tickets"    => "🔷  Azure DevOps — Tickets",
            "devops-tags"       => "📊  Dashboard de tickets por tag",
            "freshdesk-tickets" => "🎫  Freshdesk — Tickets",
            "ticket-links"      => "🔗  Vínculos y Estadísticas",
            "forum"             => "💬  Foro del equipo",
            "presence"          => "🟢  Quién está y registro de jornadas",
            "templates"         => "📚  Plantillas y scripts",
            "users"             => "🔐  Gestión de Usuarios",
            "audit"             => "📋  Bitácora de Auditoría",
            "config"            => "⚙  Configuración",
            "data-cleanup"      => "🧹  Limpieza de datos",
            "activities-admin" => "🧩  Actividades libres del equipo",
            "sla-admin"       => "⏱  SLA y recordatorios",
            "sla-compliance"  => "📊  Cumplimiento de SLA",
            "suggestions"     => "💡  Sugerencias y propuestas",
            "my-suggestions"  => "💡  Sugerencias",
            "my-sla"          => "⏱  Mis SLA",
            "my-presence"     => "🕒  Mi jornada",
            "my-assignments"  => "📋  Mis Asignaciones",
            "notifications"   => "🔔  Avisos",
            "my-devops-tickets" => "🔷  Mis tickets de DevOps",
            "my-performance"  => "📊  Mi Panel",
            "my-activities"   => "🧩  Mis Actividades",
            "my-evaluations"  => "📄  Mis Evaluaciones",
            "my-vacations"    => "🏖  Mis Vacaciones",
            "my-leaves"       => "🙋  Mis Permisos",
            _                 => ""
        };

        // Refrescar el badge de pendientes al instante cuando el jefe aprueba/rechaza.
        if (ctrl is PerformanceControl perf)
            perf.PendingCountChanged += UpdatePendingBadge;

        // Refrescar el contador de avisos al marcar leído.
        if (ctrl is NotificationsControl notif)
            notif.UnreadChanged += () => RevisarNotificaciones(silencioso: true);

        _currentControl = ctrl;
        ctrl.Dock = DockStyle.Fill;
        _pnlContent.Controls.Add(ctrl);

        // Despliegues carga sus listas al entrar (su OnVisibleChanged no basta en el primer Add, y
        // como es keep-alive tampoco al reusarlo): se le pide explícitamente en cada navegación.
        if (ctrl is DeploymentControl dc) dc.RefrescarAlEntrar();

        UpdatePendingBadge();
    }

    private void BtnLogout_Click(object? s, EventArgs e)
    {
        if (MessageBox.Show("¿Cerrar sesión?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        // Cerrar sesión con un despliegue en curso lo cancela: avisar antes.
        if (DeploymentCtrl is { DeployEnCurso: true })
        {
            var r = MessageBox.Show(
                "Hay un DESPLIEGUE EN CURSO. Si cierras sesión se cancela (lo ya subido se queda como esté).\n\n¿Cerrar sesión de todos modos?",
                "Despliegue en curso", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            DeploymentCtrl.CancelarDespliegue();
        }

        // Al cerrar sesión se apaga la vigilancia: sin usuario no hay a quién avisar, y dejar el
        // ícono en la bandeja haría creer que sigue vigilando.
        _slaTimer?.Stop();
        _pendingTimer?.Stop();
        TerminarPresencia();       // cerrar sesión cierra la jornada de esta persona
        _slaAlertas.Reiniciar();   // el siguiente usuario empieza con avisos limpios
        // Se libera, no solo se oculta: al volver a iniciar sesión se crea otro MainForm con su
        // propio ícono, y dos NotifyIcon vivos acabarían acumulándose en cada ciclo.
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }

        _auth.Logout();
        foreach (var c in _keepAlive.Values) { try { c.Dispose(); } catch { } }
        _keepAlive.Clear();
        _currentControl?.Dispose(); Hide();
        var login = (LoginForm)_sp.GetService(typeof(LoginForm))!;
        login.Show();
        // Cierre programático: no pasa por la rama de "esconder a la bandeja".
        login.FormClosed += (_, _) => { _salirDeVerdad = true; Close(); };
    }
}
