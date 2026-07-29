using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.Data.SqlClient;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class ConfigurationControl : UserControl
{
    private readonly SettingsService _settings;
    private readonly DataMigrationService _dataMigration;
    private readonly AppDbContext _db;
    private readonly IDocxToPdfConverter _converter;
    private readonly SignatureService _sig;
    private readonly EmailService _email;

    // Azure Blob
    private TextBox _txtBlobConn = null!;
    private TextBox _txtBlobContainer = null!;
    private TextBox _txtBlobReleases = null!, _txtBlobBackups = null!, _txtBlobDeployBackups = null!;
    private Button _btnTestBlob = null!;
    private Label _lblBlobStatus = null!;
    // Rutas
    private TextBox _txtDeployFolder = null!;
    // Base de datos
    private ComboBox _cbxProvider = null!;
    private TextBox _txtSqlConn = null!;
    private Label _lblSqlConn = null!;
    private Label _lblSqlHint = null!;
    private Button _btnToggleSqlConn = null!;
    private Button _btnTestConn = null!;
    private Label _lblDbStatus = null!;
    /// <summary>Hay cambios sin guardar en la connection string: no se debe pisar al recargar.</summary>
    private bool _sqlConnDirty;
    // Azure DevOps
    private TextBox _txtDevOpsOrg = null!;
    private TextBox _txtDevOpsProject = null!;
    private TextBox _txtDevOpsPat = null!;
    private CheckBox _chkDevOpsEnabled = null!;
    // SLA automático por prioridad (una fila por prioridad 1..4 de DevOps)
    private readonly CheckBox[] _slaChk = new CheckBox[4];
    private readonly NumericUpDown[] _slaHoras = new NumericUpDown[4];
    private readonly NumericUpDown[] _slaRecord = new NumericUpDown[4];
    // Reporte de tiempo cronometrado → DevOps
    private CheckBox _chkTimeReport = null!;
    private ComboBox _cbxTimeMode = null!;
    private CheckBox _chkTimeReduce = null!;
    // Tareas automáticas (respaldo de BD + resumen por correo)
    private CheckBox _chkAutoBackup = null!;
    private ComboBox _cbxBackupFreq = null!;
    private CheckBox _chkDigest = null!;
    private ComboBox _cbxDigestFreq = null!;
    private TextBox _txtDigestRecipients = null!;
    // Freshdesk
    private TextBox _txtFreshDeskDomain = null!;
    private TextBox _txtFreshDeskApiKey = null!;
    private CheckBox _chkFreshDeskEnabled = null!;
    // Documentos de vacaciones
    private TextBox _txtVacDepto = null!;
    private TextBox _txtVacPuesto = null!;
    private TextBox _txtVacJefe = null!;
    private TextBox _txtLibreOffice = null!;
    private Label _lblLibreStatus = null!;
    // Correo
    private CheckBox _chkEmailEnabled = null!;
    private TextBox _txtEmailAddress = null!, _txtEmailName = null!, _txtEmailPassword = null!;
    private TextBox _txtSmtpHost = null!, _txtSmtpPort = null!, _txtImapHost = null!, _txtImapPort = null!, _txtReqFolder = null!;
    private TextBox _txtSlaEmail = null!;
    private Label _lblEmailStatus = null!;
    // Common
    private Label _lblStatus = null!;

    public ConfigurationControl(SettingsService settings, DataMigrationService dataMigration, AppDbContext db,
        IDocxToPdfConverter converter, SignatureService sig, EmailService email)
    {
        _settings = settings;
        _dataMigration = dataMigration;
        _db = db;
        _converter = converter;
        _sig = sig;
        _email = email;
        BuildUI();
        LoadValues();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 20;

        // ── Azure Blob Storage ─────────────────────────────────────
        AddSection(scroll, "☁  Azure Blob Storage", ref y);
        AddField(scroll, "Connection string (cifrada con DPAPI):", ref _txtBlobConn, ref y, true,
            "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net");
        AddField(scroll, "Nombre del contenedor:", ref _txtBlobContainer, ref y, false, "despliegues");

        scroll.Controls.Add(new Label
        {
            Text = "Carpetas dentro del contenedor (déjalas vacías para usar las de por omisión):",
            Location = new Point(30, y), AutoSize = false, Size = new Size(750, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 22;
        AddField(scroll, "Carpeta de versiones:", ref _txtBlobReleases, ref y, false, BlobStorageService.PrefijoVersionesPorDefecto);
        AddField(scroll, "Carpeta de respaldos de la base de datos:", ref _txtBlobBackups, ref y, false, BlobStorageService.PrefijoRespaldosBdPorDefecto);
        AddField(scroll, "Carpeta de respaldos previos al despliegue:", ref _txtBlobDeployBackups, ref y, false, BlobStorageService.PrefijoRespaldosDesplieguePorDefecto);
        scroll.Controls.Add(new Label
        {
            Text = "Cambiarlas no mueve lo ya guardado: lo anterior se queda donde está y lo nuevo va a la carpeta nueva.",
            Location = new Point(30, y - 34), AutoSize = false, Size = new Size(750, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.Warning
        });
        y += 10;

        _btnTestBlob = AppTheme.MakeSecondaryButton("🔍 Probar conexión con Azure", 240);
        _btnTestBlob.Location = new Point(30, y);
        _btnTestBlob.Click += BtnTestBlob_Click;
        scroll.Controls.Add(_btnTestBlob);
        y += 42;

        _lblBlobStatus = new Label
        {
            Location = new Point(30, y), AutoSize = false, Size = new Size(750, 40),
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextSecondary
        };
        scroll.Controls.Add(_lblBlobStatus);
        y += 46;

        // ── Rutas ──────────────────────────────────────────────────
        AddSection(scroll, "📁  Rutas por defecto", ref y);
        AddField(scroll, "Carpeta de despliegue:", ref _txtDeployFolder, ref y, false, @"C:\builds\publish");

        // ── Base de datos ──────────────────────────────────────────
        AddSection(scroll, "🗄  Base de datos", ref y);
        scroll.Controls.Add(new Label { Text = "Proveedor:", Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        y += 22;
        _cbxProvider = new ComboBox
        {
            Location = new Point(30, y), Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cbxProvider.Items.AddRange(["SQLite local (predeterminado)", "Azure SQL Server"]);
        _cbxProvider.SelectedIndex = 0;
        _cbxProvider.SelectedIndexChanged += CbxProvider_Changed;
        scroll.Controls.Add(_cbxProvider);
        y += 40;

        _lblSqlConn = new Label
        {
            Text = "Connection string de SQL Server / Azure SQL (se guarda cifrada en disco):",
            Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont
        };
        scroll.Controls.Add(_lblSqlConn);
        y += 22;
        // Visible en claro: es un dato que el usuario necesita revisar y corregir. El cifrado (DPAPI)
        // aplica al guardarla en dbprovider.json, no a lo que se muestra aquí.
        _txtSqlConn = new TextBox
        {
            Location = new Point(30, y), Width = 700, UseSystemPasswordChar = false,
            PlaceholderText = "server = mi-srv.database.windows.net; uid = usuario; pwd = clave; database = MI_BD"
        };
        _txtSqlConn.TextChanged += (_, _) => _sqlConnDirty = true;
        scroll.Controls.Add(_txtSqlConn);
        _btnToggleSqlConn = AppTheme.MakeSecondaryButton("🙈", 44);
        _btnToggleSqlConn.Location = new Point(736, y - 1);
        _btnToggleSqlConn.Click += BtnToggleSqlConn_Click;
        scroll.Controls.Add(_btnToggleSqlConn);
        y += 32;

        _lblSqlHint = new Label
        {
            Text = "Se aceptan formatos abreviados (server / uid / pwd / database) con o sin espacios; al probar o guardar se normaliza.",
            Location = new Point(30, y), AutoSize = false, Size = new Size(750, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        };
        scroll.Controls.Add(_lblSqlHint);
        y += 26;

        var btnDbFlow = new FlowLayoutPanel { Location = new Point(30, y), AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        _btnTestConn    = AppTheme.MakeSecondaryButton("🔍 Probar conexión", 170);
        var btnSaveDb   = AppTheme.MakePrimaryButton("💾 Guardar BD",       140);
        var btnMigrate  = AppTheme.MakeDangerButton("📤 Migrar SQLite → Azure SQL", 230);
        foreach (var b in new[] { _btnTestConn, btnSaveDb, btnMigrate }) b.Margin = new Padding(0, 0, 8, 0);
        _btnTestConn.Click += BtnTestConn_Click;
        btnSaveDb.Click   += BtnSaveDb_Click;
        btnMigrate.Click  += BtnMigrate_Click;
        btnDbFlow.Controls.AddRange([_btnTestConn, btnSaveDb, btnMigrate]);
        scroll.Controls.Add(btnDbFlow);
        y += 46;

        // Alto para 3 renglones: los errores de SQL Server son largos y antes se cortaban.
        _lblDbStatus = new Label { Location = new Point(30, y), AutoSize = false, Size = new Size(750, 54), Font = AppTheme.BoldFont };
        scroll.Controls.Add(_lblDbStatus);
        y += 62;

        // ── Azure DevOps ───────────────────────────────────────────
        AddSection(scroll, "🔷  Azure DevOps (integración opcional)", ref y);
        _chkDevOpsEnabled = new CheckBox { Text = "Habilitar integración con Azure DevOps", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkDevOpsEnabled);
        y += 30;
        AddField(scroll, "URL de organización:", ref _txtDevOpsOrg, ref y, false, "https://dev.azure.com/mi-org");
        AddField(scroll, "Proyecto:", ref _txtDevOpsProject, ref y, false, "MiProyecto");
        AddField(scroll, "PAT (Personal Access Token, cifrado):", ref _txtDevOpsPat, ref y, true, "");

        // ── SLA automático por prioridad ───────────────────────────
        AddSection(scroll, "⏱  SLA automático por prioridad de DevOps", ref y);
        scroll.Controls.Add(new Label
        {
            Text = "Al traer un ticket nuevo de DevOps, se le asigna un SLA según su prioridad. Activa las que quieras;\n" +
                   "«Horas para vencer» es el plazo desde que se trae el ticket y «Recordar cada» avisa al desarrollador (0 = solo al vencer).",
            Location = new Point(30, y), AutoSize = false, Size = new Size(750, 34),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 40;
        // Cabecera de columnas
        scroll.Controls.Add(new Label { Text = "Prioridad", Location = new Point(30, y), AutoSize = false, Size = new Size(230, 18), Font = AppTheme.BoldFont });
        scroll.Controls.Add(new Label { Text = "Horas para vencer", Location = new Point(270, y), AutoSize = false, Size = new Size(150, 18), Font = AppTheme.BoldFont });
        scroll.Controls.Add(new Label { Text = "Recordar cada (h)", Location = new Point(470, y), AutoSize = false, Size = new Size(150, 18), Font = AppTheme.BoldFont });
        y += 24;
        for (int i = 0; i < SlaPolicyStore.Prioridades.Length; i++)
        {
            int prioridad = SlaPolicyStore.Prioridades[i];
            _slaChk[i] = new CheckBox
            {
                Text = $"Prioridad {prioridad} — {SlaPolicyStore.NombrePrioridad(prioridad)}",
                Location = new Point(30, y + 2), AutoSize = false, Size = new Size(235, 24)
            };
            _slaHoras[i] = new NumericUpDown { Location = new Point(270, y), Width = 90, Minimum = 1, Maximum = 8760, Value = 24 };
            _slaRecord[i] = new NumericUpDown { Location = new Point(470, y), Width = 90, Minimum = 0, Maximum = 720, Value = 24 };
            scroll.Controls.Add(_slaChk[i]);
            scroll.Controls.Add(_slaHoras[i]);
            scroll.Controls.Add(_slaRecord[i]);
            y += 32;
        }
        y += 10;

        // ── Registrar tiempo cronometrado en DevOps ────────────────
        AddSection(scroll, "⏱  Registrar tiempo cronometrado en Azure DevOps", ref y);
        _chkTimeReport = new CheckBox { Text = "Al detener el cronómetro, registrar el tiempo en el ticket de DevOps", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkTimeReport);
        y += 28;
        scroll.Controls.Add(new Label
        {
            Text = "Cada desarrollador reporta con su PAT personal. Solo aplica a requerimientos que son tickets de DevOps.",
            Location = new Point(30, y), AutoSize = false, Size = new Size(750, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 24;
        scroll.Controls.Add(new Label { Text = "Cómo registrarlo:", Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        _cbxTimeMode = new ComboBox { Location = new Point(160, y - 2), Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxTimeMode.Items.AddRange(["Comentario con el tiempo (funciona en cualquier ticket)",
                                     "Campos Completed Work (solo Task/Bug)",
                                     "Ambos"]);
        _cbxTimeMode.SelectedIndex = 0;
        scroll.Controls.Add(_cbxTimeMode);
        y += 30;
        _chkTimeReduce = new CheckBox { Text = "Bajar también el «Remaining Work» por las horas registradas", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkTimeReduce);
        y += 34;

        // ── Freshdesk ──────────────────────────────────────────────
        AddSection(scroll, "🎫  Freshdesk (integración opcional)", ref y);
        _chkFreshDeskEnabled = new CheckBox { Text = "Habilitar integración con Freshdesk", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkFreshDeskEnabled);
        y += 30;
        AddField(scroll, "Dominio (sin .freshdesk.com):", ref _txtFreshDeskDomain, ref y, false, "miempresa");
        AddField(scroll, "API Key (cifrada):", ref _txtFreshDeskApiKey, ref y, true, "");

        // ── Documentos de vacaciones y firmas ─────────────────────
        AddSection(scroll, "📄  Documentos de vacaciones y firmas", ref y);
        AddField(scroll, "Departamento (por defecto en la solicitud):", ref _txtVacDepto, ref y, false, "DESARROLLO");
        AddField(scroll, "Puesto (por defecto):", ref _txtVacPuesto, ref y, false, "Desarrollador Web");
        AddField(scroll, "Jefe directo (nombre que firma la autorización):", ref _txtVacJefe, ref y, false, "GERARDO TELLEZ");

        scroll.Controls.Add(new Label { Text = "Ruta de LibreOffice (soffice.exe) — se autodetecta si está instalado:", Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        y += 22;
        _txtLibreOffice = new TextBox { Location = new Point(30, y), Width = 660, PlaceholderText = @"C:\Program Files\LibreOffice\program\soffice.exe" };
        scroll.Controls.Add(_txtLibreOffice);
        var btnBrowseLo = AppTheme.MakeSecondaryButton("📂", 40); btnBrowseLo.Location = new Point(700, y - 1); btnBrowseLo.Click += BtnBrowseLibre_Click;
        scroll.Controls.Add(btnBrowseLo);
        y += 40;

        var loFlow = new FlowLayoutPanel { Location = new Point(30, y), AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        var btnTestLo = AppTheme.MakeSecondaryButton("🔍 Probar LibreOffice", 180);
        var btnFirmas = AppTheme.MakePrimaryButton("🖊 Gestionar firmas", 180);
        btnTestLo.Margin = new Padding(0, 0, 8, 0); btnFirmas.Margin = new Padding(0, 0, 8, 0);
        btnTestLo.Click += BtnTestLibre_Click;
        btnFirmas.Click += (_, _) => { using var f = new SignatureManagerForm(_sig); f.ShowDialog(FindForm()); };
        loFlow.Controls.AddRange([btnTestLo, btnFirmas]);
        scroll.Controls.Add(loFlow);
        y += 44;

        _lblLibreStatus = new Label { Location = new Point(30, y), AutoSize = false, Size = new Size(750, 24), Font = AppTheme.BoldFont };
        scroll.Controls.Add(_lblLibreStatus);
        y += 34;

        // ── Correo (SMTP + IMAP) ──────────────────────────────────
        AddSection(scroll, "✉  Correo (SMTP + IMAP)", ref y);
        _chkEmailEnabled = new CheckBox { Text = "Habilitar correo (envío y lectura de carpetas)", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkEmailEnabled); y += 30;
        AddField(scroll, "Dirección de correo:", ref _txtEmailAddress, ref y, false, "tucorreo@empresa.com");
        AddField(scroll, "Nombre para mostrar:", ref _txtEmailName, ref y, false, "Tu Nombre");
        AddField(scroll, "Contraseña de aplicación (cifrada):", ref _txtEmailPassword, ref y, true, "");
        AddField(scroll, "Servidor SMTP:", ref _txtSmtpHost, ref y, false, "smtp.office365.com  /  smtp.gmail.com");
        AddField(scroll, "Puerto SMTP:", ref _txtSmtpPort, ref y, false, "587 (STARTTLS) o 465 (SSL)");
        AddField(scroll, "Servidor IMAP:", ref _txtImapHost, ref y, false, "outlook.office365.com  /  imap.gmail.com");
        AddField(scroll, "Puerto IMAP:", ref _txtImapPort, ref y, false, "993");
        AddField(scroll, "Carpeta para ingerir requerimientos:", ref _txtReqFolder, ref y, false, "INBOX");
        AddField(scroll, "Correo del jefe para escalamientos de SLA vencido:", ref _txtSlaEmail, ref y, false,
            "jefe@empresa.com   (varios separados por ; )");
        scroll.Controls.Add(new Label
        {
            Text = "Si se deja vacío, los avisos de SLA vencido llegan a la propia cuenta de la aplicación.",
            Location = new Point(30, y - 34), AutoSize = false, Size = new Size(750, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 6;
        var btnTestEmail = AppTheme.MakeSecondaryButton("🔍 Probar correo", 160); btnTestEmail.Location = new Point(30, y); btnTestEmail.Click += BtnTestEmail_Click;
        scroll.Controls.Add(btnTestEmail); y += 46;
        _lblEmailStatus = new Label { Location = new Point(30, y), AutoSize = false, Size = new Size(750, 24), Font = AppTheme.BoldFont };
        scroll.Controls.Add(_lblEmailStatus); y += 34;

        // ── Tareas automáticas ─────────────────────────────────────
        AddSection(scroll, "🗓  Tareas automáticas (respaldo y resumen)", ref y);
        _chkAutoBackup = new CheckBox { Text = "Respaldar la base a Blob Storage automáticamente (solo aplica a la base SQLite local)", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkAutoBackup);
        y += 28;
        scroll.Controls.Add(new Label { Text = "Frecuencia del respaldo:", Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        _cbxBackupFreq = new ComboBox { Location = new Point(210, y - 2), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxBackupFreq.Items.AddRange(["Diario", "Semanal"]);
        _cbxBackupFreq.SelectedIndex = 0;
        scroll.Controls.Add(_cbxBackupFreq);
        y += 40;

        _chkDigest = new CheckBox { Text = "Enviar un resumen del equipo por correo (requiere el correo configurado arriba)", Location = new Point(30, y), AutoSize = true };
        scroll.Controls.Add(_chkDigest);
        y += 28;
        scroll.Controls.Add(new Label { Text = "Frecuencia del resumen:", Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        _cbxDigestFreq = new ComboBox { Location = new Point(210, y - 2), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxDigestFreq.Items.AddRange(["Diario", "Semanal"]);
        _cbxDigestFreq.SelectedIndex = 0;
        scroll.Controls.Add(_cbxDigestFreq);
        y += 28;
        AddField(scroll, "Destinatarios del resumen (correos separados por ; ). Vacío = usa el de escalamiento de SLA o la propia cuenta:",
            ref _txtDigestRecipients, ref y, false, "jefe@empresa.com; lider@empresa.com");

        // ── Guardar general ───────────────────────────────────────
        y += 10;
        _lblStatus = new Label { Location = new Point(30, y), AutoSize = false, Size = new Size(750, 28), Font = AppTheme.BoldFont };
        scroll.Controls.Add(_lblStatus);
        y += 38;

        var btnSave = AppTheme.MakePrimaryButton("💾  Guardar configuración general", 250, 38);
        btnSave.Location = new Point(30, y);
        btnSave.Click += BtnSave_Click;
        scroll.Controls.Add(btnSave);
        scroll.AutoScrollMinSize = new Size(0, y + 70);

        Controls.Add(scroll);
    }

    private void CbxProvider_Changed(object? s, EventArgs e)
    {
        bool isSqlServer = _cbxProvider.SelectedIndex == 1;
        _lblSqlConn.Visible = isSqlServer;
        _txtSqlConn.Visible = isSqlServer;
        _lblSqlHint.Visible = isSqlServer;
        _btnToggleSqlConn.Visible = isSqlServer;
    }

    private void BtnToggleSqlConn_Click(object? s, EventArgs e)
    {
        _txtSqlConn.UseSystemPasswordChar = !_txtSqlConn.UseSystemPasswordChar;
        _btnToggleSqlConn.Text = _txtSqlConn.UseSystemPasswordChar ? "👁" : "🙈";
    }

    private static void AddSection(Panel parent, string title, ref int y)
    {
        parent.Controls.Add(new Label { Text = title, Location = new Point(30, y), AutoSize = false, Width = 750, Height = 30, Font = AppTheme.HeaderFont, ForeColor = AppTheme.SidebarActive });
        parent.Controls.Add(new Panel { Location = new Point(30, y + 32), Size = new Size(750, 1), BackColor = AppTheme.Border });
        y += 45;
    }

    private static void AddField(Panel parent, string label, ref TextBox box, ref int y, bool secret, string hint = "")
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(30, y), AutoSize = true, Font = AppTheme.DefaultFont });
        y += 22;
        box = new TextBox { Location = new Point(30, y), Width = 750, UseSystemPasswordChar = secret, PlaceholderText = hint };
        parent.Controls.Add(box);
        y += 40;
    }

    private void LoadValues()
    {
        _txtBlobConn.Text      = _settings.Get(SettingsService.Keys.AzureBlobConnectionString) ?? "";
        _txtBlobContainer.Text = _settings.Get(SettingsService.Keys.AzureBlobContainer) ?? "";
        _txtBlobReleases.Text      = _settings.Get(SettingsService.Keys.AzureBlobReleasesPrefix) ?? "";
        _txtBlobBackups.Text       = _settings.Get(SettingsService.Keys.AzureBlobBackupsPrefix) ?? "";
        _txtBlobDeployBackups.Text = _settings.Get(SettingsService.Keys.AzureBlobDeployBackupsPrefix) ?? "";
        _txtDeployFolder.Text  = _settings.Get(SettingsService.Keys.DefaultDeployFolder) ?? "";

        // DB provider from file
        var dbCfg = DbProviderConfig.Load();
        _cbxProvider.SelectedIndex = dbCfg.Provider == DbProvider.SqlServer ? 1 : 0;
        // Solo se repuebla si el usuario no tiene cambios pendientes; recargar al volver a la
        // pestaña borraba la cadena recién capturada.
        if (!_sqlConnDirty)
        {
            _txtSqlConn.Text = dbCfg.SqlServerConnection ?? "";
            _sqlConnDirty = false;
        }
        CbxProvider_Changed(null, EventArgs.Empty);

        // Azure DevOps
        _chkDevOpsEnabled.Checked = _settings.Get(SettingsService.Keys.AzureDevOpsEnabled) == "true";
        _txtDevOpsOrg.Text     = _settings.Get(SettingsService.Keys.AzureDevOpsOrgUrl) ?? "";
        _txtDevOpsProject.Text = _settings.Get(SettingsService.Keys.AzureDevOpsProject) ?? "";
        _txtDevOpsPat.Text     = _settings.Get(SettingsService.Keys.AzureDevOpsPat) ?? "";
        // SLA automático por prioridad
        var politicasSla = SlaPolicyStore.Parse(_settings.Get(SlaPolicyStore.SettingKey));
        for (int i = 0; i < SlaPolicyStore.Prioridades.Length; i++)
        {
            var pol = politicasSla.First(p => p.Priority == SlaPolicyStore.Prioridades[i]);
            _slaChk[i].Checked = pol.Enabled;
            _slaHoras[i].Value  = Math.Clamp(pol.Hours, (int)_slaHoras[i].Minimum, (int)_slaHoras[i].Maximum);
            _slaRecord[i].Value = Math.Clamp(pol.ReminderEveryHours, (int)_slaRecord[i].Minimum, (int)_slaRecord[i].Maximum);
        }
        // Reporte de tiempo → DevOps
        _chkTimeReport.Checked = _settings.Get(DevOpsTimeReport.KeyEnabled) == "true";
        _cbxTimeMode.SelectedIndex = (int)DevOpsTimeReport.ParseMode(_settings.Get(DevOpsTimeReport.KeyMode));
        _chkTimeReduce.Checked = _settings.Get(DevOpsTimeReport.KeyReduceRemaining) == "true";
        // Tareas automáticas
        _chkAutoBackup.Checked = _settings.Get(AutoBackupService.KeyEnabled) == "true";
        _cbxBackupFreq.SelectedIndex = _settings.Get(AutoBackupService.KeyFrequency) == "7" ? 1 : 0;
        _chkDigest.Checked = _settings.Get(DigestService.KeyEnabled) == "true";
        _cbxDigestFreq.SelectedIndex = _settings.Get(DigestService.KeyFrequency) == "7" ? 1 : 0;
        _txtDigestRecipients.Text = _settings.Get(DigestService.KeyRecipients) ?? "";
        // Freshdesk
        _chkFreshDeskEnabled.Checked = _settings.Get(SettingsService.Keys.FreshDeskEnabled) == "true";
        _txtFreshDeskDomain.Text = _settings.Get(SettingsService.Keys.FreshDeskDomain) ?? "";
        _txtFreshDeskApiKey.Text = _settings.Get(SettingsService.Keys.FreshDeskApiKey) ?? "";
        // Documentos de vacaciones
        _txtVacDepto.Text    = _settings.Get(SettingsService.Keys.VacationDepartamento) ?? "";
        _txtVacPuesto.Text   = _settings.Get(SettingsService.Keys.VacationPuestoDefault) ?? "";
        _txtVacJefe.Text     = _settings.Get(SettingsService.Keys.VacationJefeDirecto) ?? "";
        _txtLibreOffice.Text = _settings.Get(SettingsService.Keys.LibreOfficePath) ?? "";
        // Correo
        _chkEmailEnabled.Checked = _settings.Get(SettingsService.Keys.EmailEnabled) == "true";
        _txtEmailAddress.Text  = _settings.Get(SettingsService.Keys.EmailAddress) ?? "";
        _txtEmailName.Text     = _settings.Get(SettingsService.Keys.EmailDisplayName) ?? "";
        _txtEmailPassword.Text = _settings.Get(SettingsService.Keys.EmailPassword) ?? "";
        _txtSmtpHost.Text      = _settings.Get(SettingsService.Keys.EmailSmtpHost) ?? "";
        _txtSmtpPort.Text      = _settings.Get(SettingsService.Keys.EmailSmtpPort) ?? "587";
        _txtImapHost.Text      = _settings.Get(SettingsService.Keys.EmailImapHost) ?? "";
        _txtImapPort.Text      = _settings.Get(SettingsService.Keys.EmailImapPort) ?? "993";
        _txtReqFolder.Text     = _settings.Get(SettingsService.Keys.EmailRequirementsFolder) ?? "INBOX";
        _txtSlaEmail.Text      = _settings.Get(SettingsService.Keys.SlaEscalationEmail) ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        _lblStatus.Text = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(_txtBlobConn.Text))
                _settings.Set(SettingsService.Keys.AzureBlobConnectionString, _txtBlobConn.Text.Trim(), true, "Azure Blob Storage connection string");
            if (!string.IsNullOrWhiteSpace(_txtBlobContainer.Text))
                _settings.Set(SettingsService.Keys.AzureBlobContainer, _txtBlobContainer.Text.Trim(), false, "Contenedor blob");

            // Las carpetas SÍ se guardan aunque queden vacías: vaciarlas es la forma de volver al
            // valor por omisión, y si solo se guardaran cuando tienen texto no habría vuelta atrás.
            foreach (var (caja, clave, etiqueta) in new[]
                     {
                         (_txtBlobReleases,      SettingsService.Keys.AzureBlobReleasesPrefix,      "Carpeta de versiones"),
                         (_txtBlobBackups,       SettingsService.Keys.AzureBlobBackupsPrefix,       "Carpeta de respaldos de BD"),
                         (_txtBlobDeployBackups, SettingsService.Keys.AzureBlobDeployBackupsPrefix, "Carpeta de respaldos de despliegue"),
                     })
            {
                var error = BlobStorageService.ValidarPrefijo(caja.Text);
                if (error != null)
                {
                    _lblStatus.ForeColor = AppTheme.Danger;
                    _lblStatus.Text = $"✗  {etiqueta}: {error}";
                    return;
                }
                _settings.Set(clave, caja.Text.Trim(), false, etiqueta);
            }


            if (!string.IsNullOrWhiteSpace(_txtDeployFolder.Text))
                _settings.Set(SettingsService.Keys.DefaultDeployFolder, _txtDeployFolder.Text.Trim(), false, "Carpeta despliegue");

            // Azure DevOps
            _settings.Set(SettingsService.Keys.AzureDevOpsEnabled, _chkDevOpsEnabled.Checked ? "true" : "false", false, "Azure DevOps habilitado");
            if (!string.IsNullOrWhiteSpace(_txtDevOpsOrg.Text))
                _settings.Set(SettingsService.Keys.AzureDevOpsOrgUrl, _txtDevOpsOrg.Text.Trim(), false, "Azure DevOps org URL");
            if (!string.IsNullOrWhiteSpace(_txtDevOpsProject.Text))
                _settings.Set(SettingsService.Keys.AzureDevOpsProject, _txtDevOpsProject.Text.Trim(), false, "Azure DevOps project");
            if (!string.IsNullOrWhiteSpace(_txtDevOpsPat.Text))
                _settings.Set(SettingsService.Keys.AzureDevOpsPat, _txtDevOpsPat.Text.Trim(), true, "Azure DevOps PAT");
            // SLA automático por prioridad (siempre se guarda, para poder desactivar todo)
            var politicasSla = new List<SlaPolicy>();
            for (int i = 0; i < SlaPolicyStore.Prioridades.Length; i++)
                politicasSla.Add(new SlaPolicy(
                    SlaPolicyStore.Prioridades[i],
                    _slaChk[i].Checked,
                    (int)_slaHoras[i].Value,
                    (int)_slaRecord[i].Value));
            _settings.Set(SlaPolicyStore.SettingKey, SlaPolicyStore.Serialize(politicasSla), false, "Políticas de SLA automático por prioridad");
            // Reporte de tiempo → DevOps
            _settings.Set(DevOpsTimeReport.KeyEnabled, _chkTimeReport.Checked ? "true" : "false", false, "Registrar tiempo cronometrado en DevOps");
            _settings.Set(DevOpsTimeReport.KeyMode, DevOpsTimeReport.ModeToString((DevOpsTimeMode)_cbxTimeMode.SelectedIndex), false, "Modo de reporte de tiempo a DevOps");
            _settings.Set(DevOpsTimeReport.KeyReduceRemaining, _chkTimeReduce.Checked ? "true" : "false", false, "Bajar Remaining Work al reportar tiempo");
            // Tareas automáticas
            _settings.Set(AutoBackupService.KeyEnabled, _chkAutoBackup.Checked ? "true" : "false", false, "Respaldo automático de la BD");
            _settings.Set(AutoBackupService.KeyFrequency, _cbxBackupFreq.SelectedIndex == 1 ? "7" : "1", false, "Frecuencia del respaldo (días)");
            _settings.Set(DigestService.KeyEnabled, _chkDigest.Checked ? "true" : "false", false, "Resumen por correo");
            _settings.Set(DigestService.KeyFrequency, _cbxDigestFreq.SelectedIndex == 1 ? "7" : "1", false, "Frecuencia del resumen (días)");
            _settings.Set(DigestService.KeyRecipients, _txtDigestRecipients.Text.Trim(), false, "Destinatarios del resumen");
            // Freshdesk
            _settings.Set(SettingsService.Keys.FreshDeskEnabled, _chkFreshDeskEnabled.Checked ? "true" : "false", false, "Freshdesk habilitado");
            if (!string.IsNullOrWhiteSpace(_txtFreshDeskDomain.Text))
                _settings.Set(SettingsService.Keys.FreshDeskDomain, _txtFreshDeskDomain.Text.Trim(), false, "Freshdesk dominio");
            if (!string.IsNullOrWhiteSpace(_txtFreshDeskApiKey.Text))
                _settings.Set(SettingsService.Keys.FreshDeskApiKey, _txtFreshDeskApiKey.Text.Trim(), true, "Freshdesk API Key");

            // Documentos de vacaciones (se guardan aunque estén vacíos para permitir limpiar)
            _settings.Set(SettingsService.Keys.VacationDepartamento, _txtVacDepto.Text.Trim(), false, "Departamento en solicitud de vacaciones");
            _settings.Set(SettingsService.Keys.VacationPuestoDefault, _txtVacPuesto.Text.Trim(), false, "Puesto por defecto en solicitud");
            _settings.Set(SettingsService.Keys.VacationJefeDirecto, _txtVacJefe.Text.Trim(), false, "Jefe directo que autoriza");
            _settings.Set(SettingsService.Keys.LibreOfficePath, _txtLibreOffice.Text.Trim(), false, "Ruta de soffice.exe (LibreOffice)");

            // Correo
            _settings.Set(SettingsService.Keys.EmailEnabled, _chkEmailEnabled.Checked ? "true" : "false", false, "Correo habilitado");
            _settings.Set(SettingsService.Keys.EmailAddress, _txtEmailAddress.Text.Trim(), false, "Dirección de correo");
            _settings.Set(SettingsService.Keys.EmailDisplayName, _txtEmailName.Text.Trim(), false, "Nombre para mostrar");
            if (!string.IsNullOrWhiteSpace(_txtEmailPassword.Text))
                _settings.Set(SettingsService.Keys.EmailPassword, _txtEmailPassword.Text.Trim(), true, "Contraseña de aplicación de correo");
            _settings.Set(SettingsService.Keys.EmailSmtpHost, _txtSmtpHost.Text.Trim(), false, "SMTP host");
            _settings.Set(SettingsService.Keys.EmailSmtpPort, _txtSmtpPort.Text.Trim(), false, "SMTP port");
            _settings.Set(SettingsService.Keys.EmailImapHost, _txtImapHost.Text.Trim(), false, "IMAP host");
            _settings.Set(SettingsService.Keys.EmailImapPort, _txtImapPort.Text.Trim(), false, "IMAP port");
            _settings.Set(SettingsService.Keys.EmailRequirementsFolder, string.IsNullOrWhiteSpace(_txtReqFolder.Text) ? "INBOX" : _txtReqFolder.Text.Trim(), false, "Carpeta de ingesta");
            _settings.Set(SettingsService.Keys.SlaEscalationEmail, _txtSlaEmail.Text.Trim(), false, "Correo del jefe para escalamientos de SLA");

            _lblStatus.ForeColor = AppTheme.Success;
            _lblStatus.Text = "✓  Configuración guardada correctamente.";
        }
        catch (Exception ex)
        {
            _lblStatus.ForeColor = AppTheme.Danger;
            _lblStatus.Text = $"✗  Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Valida y normaliza lo escrito en el cuadro de la connection string. Si es válida deja el
    /// texto normalizado a la vista para que el usuario sepa exactamente qué se va a usar.
    /// </summary>
    private bool TryGetSqlConnection(out string conn)
    {
        conn = "";
        var raw = _txtSqlConn.Text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            SetDbStatus(AppTheme.Warning, _cbxProvider.SelectedIndex == 1
                ? "⚠  Escribe la connection string de SQL Server."
                : "⚠  Selecciona «Azure SQL Server» en Proveedor para capturar la connection string.");
            return false;
        }

        if (!SqlConnectionStringHelper.TryNormalize(raw, out var normalized, out var error))
        {
            SetDbStatus(AppTheme.Danger, "✗  " + error);
            MessageBox.Show(error, "Connection string inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!string.Equals(raw, normalized, StringComparison.Ordinal))
            _txtSqlConn.Text = normalized;

        conn = normalized;
        return true;
    }

    private void SetDbStatus(Color color, string text)
    {
        _lblDbStatus.ForeColor = color;
        _lblDbStatus.Text = text;
        _lblDbStatus.Refresh();
    }

    private async void BtnTestConn_Click(object? s, EventArgs e)
    {
        if (!TryGetSqlConnection(out var conn)) return;

        // La prueba corre fuera del hilo de UI: antes CanConnect() bloqueaba la ventana hasta
        // agotar el timeout, así que el botón parecía no hacer nada.
        _btnTestConn.Enabled = false;
        var originalText = _btnTestConn.Text;
        _btnTestConn.Text = "⏳ Probando...";
        SetDbStatus(AppTheme.TextSecondary, "Conectando con el servidor, espera un momento...");
        UseWaitCursor = true;

        try
        {
            var info = await Task.Run(async () =>
            {
                await using var sql = new SqlConnection(conn);
                await sql.OpenAsync();
                await using var cmd = sql.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), SUSER_SNAME(), @@VERSION";
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return ("", "", "");
                var version = r.GetString(2);
                var firstLine = version.Split('\n')[0].Trim();
                return (r.GetString(0), r.GetString(1), firstLine);
            });

            SetDbStatus(AppTheme.Success,
                $"✓  Conexión exitosa.\nBase de datos: {info.Item1}   ·   Usuario: {info.Item2}\n{info.Item3}");
            MessageBox.Show(
                $"Conexión exitosa.\n\nBase de datos: {info.Item1}\nUsuario: {info.Item2}\nServidor: {info.Item3}",
                "Probar conexión", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var detail = SqlConnectionStringHelper.Explain(ex);
            SetDbStatus(AppTheme.Danger, "✗  " + detail.Split('\n')[0]);
            MessageBox.Show($"No se pudo conectar.\n\n{detail}", "Probar conexión",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _btnTestConn.Text = originalText;
            _btnTestConn.Enabled = true;
        }
    }

    private void BtnSaveDb_Click(object? s, EventArgs e)
    {
        bool isSqlServer = _cbxProvider.SelectedIndex == 1;
        string conn;
        if (isSqlServer)
        {
            if (!TryGetSqlConnection(out conn)) return;
        }
        else
        {
            // Con SQLite no se valida, pero se conserva lo capturado para no perderlo al volver.
            conn = SqlConnectionStringHelper.NormalizeOrOriginal(_txtSqlConn.Text);
        }

        var cfg = new DbProviderConfig
        {
            Provider = isSqlServer ? DbProvider.SqlServer : DbProvider.Sqlite,
            SqlServerConnection = conn
        };
        DbProviderConfig.Save(cfg);
        _sqlConnDirty = false;
        SetDbStatus(AppTheme.Success, "✓  Proveedor guardado. Reinicia la aplicación para aplicar el cambio.");
    }

    private async void BtnMigrate_Click(object? s, EventArgs e)
    {
        if (!TryGetSqlConnection(out var conn)) return;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdministradorDesarrolloWeb");
        var sqlitePath = Path.Combine(appData, "app.db");
        if (!File.Exists(sqlitePath))
        {
            MessageBox.Show("No se encontró la base de datos SQLite local.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // El diálogo NO va en un `using`: al ser async, el using lo destruía al llegar al primer
        // await pendiente y el resultado desaparecía de la pantalla. Se cierra a mano.
        var dlg = new ProgressDialog("Migración SQLite → SQL Server");
        var btn = (Button)s!;
        btn.Enabled = false;   // sin esto, DoEvents del log permitía lanzar una segunda migración
        dlg.Show(FindForm());

        var progress = new Progress<string>(dlg.Append);
        try
        {
            var reporte = await _dataMigration.MigrateSqliteToSqlServerAsync(
                sqlitePath, conn, MigrationMode.AbortarSiHayDatos, progress, dlg.Token);

            // Destino con datos: se pregunta y, si aceptan, se repite reemplazando de verdad
            // (la versión anterior ofrecía "sobreescribir" y no borraba nada).
            if (!reporte.Success && reporte.NonEmptyTables is { Count: > 0 })
            {
                var lista = string.Join(", ", reporte.NonEmptyTables.Take(8));
                if (reporte.NonEmptyTables.Count > 8) lista += $" y {reporte.NonEmptyTables.Count - 8} más";

                var confirma = MessageBox.Show(
                    $"La base destino ya tiene datos en {reporte.NonEmptyTables.Count} tabla(s):\n{lista}\n\n" +
                    "¿Borrar TODO el contenido del destino y reemplazarlo con la base local?\n" +
                    "Esta acción no se puede deshacer.",
                    "El destino no está vacío", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

                if (confirma == DialogResult.Yes)
                {
                    dlg.Append("");
                    dlg.Append("Reemplazando el contenido del destino...");
                    reporte = await _dataMigration.MigrateSqliteToSqlServerAsync(
                        sqlitePath, conn, MigrationMode.ReemplazarDestino, progress, dlg.Token);
                }
                else dlg.Append("Cancelado por el usuario. No se modificó el destino.");
            }

            MostrarReporte(dlg, reporte);
        }
        catch (Exception ex)
        {
            dlg.Append("");
            dlg.Append($"✗ Error inesperado: {ex.Message}");
            _lblDbStatus.ForeColor = AppTheme.Danger;
            _lblDbStatus.Text = "✗  La migración falló. Revisa el detalle en la ventana de progreso.";
        }
        finally
        {
            dlg.MarkDone();
            btn.Enabled = true;
        }
    }

    /// <summary>Resumen honesto: cuenta filas reales y no declara éxito si no copió nada.</summary>
    private void MostrarReporte(ProgressDialog dlg, MigrationReport reporte)
    {
        dlg.Append("");
        dlg.Append(new string('─', 52));

        if (!reporte.Success)
        {
            dlg.Append($"✗ MIGRACIÓN FALLIDA{(reporte.FailedTable is { Length: > 0 } t ? $" en la tabla [{t}]" : "")}");
            if (reporte.Error is { Length: > 0 }) dlg.Append($"  {reporte.Error}");
            dlg.Append("  El destino quedó sin cambios.");
            _lblDbStatus.ForeColor = AppTheme.Danger;
            _lblDbStatus.Text = "✗  La migración falló. Revisa el detalle en la ventana de progreso.";
            return;
        }

        var conDatos = reporte.Tables.Where(t => t.Rows > 0).ToList();
        var vacias   = reporte.Tables.Count - conDatos.Count;

        if (reporte.TotalRows == 0)
        {
            // Terminó sin error pero no copió nada: eso no es un éxito, es una señal de alarma.
            dlg.Append("⚠ No se copió ninguna fila: la base local está vacía.");
            _lblDbStatus.ForeColor = AppTheme.Warning;
            _lblDbStatus.Text = "⚠  La migración no copió ninguna fila.";
            return;
        }

        dlg.Append($"✓ MIGRACIÓN COMPLETADA — {reporte.TotalRows} fila(s) en {conDatos.Count} tabla(s).");
        if (vacias > 0) dlg.Append($"  ({vacias} tabla(s) sin datos en el origen.)");
        dlg.Append("");
        dlg.Append("  Ahora selecciona «Azure SQL Server» en Proveedor, pulsa «Guardar BD»");
        dlg.Append("  y REINICIA la aplicación para que empiece a usar SQL Server.");

        _lblDbStatus.ForeColor = AppTheme.Success;
        _lblDbStatus.Text = $"✓  Migradas {reporte.TotalRows} filas. Guarda el proveedor y reinicia la aplicación.";
    }

    /// <summary>
    /// Prueba la conexión con Azure Blob Storage usando lo que hay ESCRITO en pantalla, no lo
    /// guardado: así se detecta una cadena equivocada antes de guardarla.
    /// </summary>
    private async void BtnTestBlob_Click(object? s, EventArgs e)
    {
        var conn = _txtBlobConn.Text.Trim();
        if (conn.Length == 0)
        {
            _lblBlobStatus.ForeColor = AppTheme.Warning;
            _lblBlobStatus.Text = "⚠  Escribe la connection string de Azure Blob Storage.";
            return;
        }

        _btnTestBlob.Enabled = false;
        var textoOriginal = _btnTestBlob.Text;
        _btnTestBlob.Text = "⏳ Probando...";
        _lblBlobStatus.ForeColor = AppTheme.TextSecondary;
        _lblBlobStatus.Text = "Conectando con Azure, espera un momento...";
        _lblBlobStatus.Refresh();
        UseWaitCursor = true;

        try
        {
            // Fuera del hilo de UI: si la red tarda, la ventana no debe quedarse congelada.
            var blob = new BlobStorageService(_settings);
            var (ok, mensaje) = await Task.Run(() =>
                blob.ProbarConexionAsync(conn, _txtBlobContainer.Text.Trim()));

            _lblBlobStatus.ForeColor = ok ? AppTheme.Success : AppTheme.Danger;
            _lblBlobStatus.Text = (ok ? "✓  " : "✗  ") + mensaje;

            MessageBox.Show(mensaje, ok ? "Conexión con Azure" : "No se pudo conectar",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            _lblBlobStatus.ForeColor = AppTheme.Danger;
            _lblBlobStatus.Text = "✗  " + ex.Message;
        }
        finally
        {
            UseWaitCursor = false;
            _btnTestBlob.Text = textoOriginal;
            _btnTestBlob.Enabled = true;
        }
    }

    private void BtnBrowseLibre_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "soffice.exe|soffice.exe|Ejecutables (*.exe)|*.exe", Title = "Seleccionar soffice.exe de LibreOffice" };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _txtLibreOffice.Text = dlg.FileName;
    }

    private void BtnTestLibre_Click(object? s, EventArgs e)
    {
        // Guardar la ruta escrita para que el convertidor la considere al probar.
        _settings.Set(SettingsService.Keys.LibreOfficePath, _txtLibreOffice.Text.Trim(), false, "Ruta de soffice.exe (LibreOffice)");
        if (_converter.IsAvailable(out var diag))
        {
            _lblLibreStatus.ForeColor = AppTheme.Success;
            _lblLibreStatus.Text = "✓  LibreOffice disponible. La conversión a PDF funcionará.";
        }
        else
        {
            _lblLibreStatus.ForeColor = AppTheme.Danger;
            _lblLibreStatus.Text = "✗  " + diag;
        }
    }

    private async void BtnTestEmail_Click(object? s, EventArgs e)
    {
        BtnSave_Click(this, EventArgs.Empty); // guarda para que el servicio use los valores actuales
        _lblEmailStatus.ForeColor = AppTheme.TextSecondary; _lblEmailStatus.Text = "Probando conexión...";
        Application.DoEvents();
        try
        {
            var msg = await _email.TestConnectionAsync();
            _lblEmailStatus.ForeColor = AppTheme.Success; _lblEmailStatus.Text = "✓  " + msg;
        }
        catch (Exception ex)
        {
            _lblEmailStatus.ForeColor = AppTheme.Danger; _lblEmailStatus.Text = "✗  " + ex.Message;
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadValues(); }
}
