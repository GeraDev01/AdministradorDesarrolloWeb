using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;

namespace Administrador_Desarrollo_Web.Services;

public class SettingsService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public static class Keys
    {
        public const string AzureBlobConnectionString = "AzureBlobConnectionString";
        public const string AzureSqlConnectionString  = "AzureSqlConnectionString";
        public const string DefaultDeployFolder       = "DefaultDeployFolder";
        public const string AzureBlobContainer        = "AzureBlobContainer";
        // Azure DevOps
        public const string AzureDevOpsOrgUrl   = "AzureDevOpsOrgUrl";
        public const string AzureDevOpsProject  = "AzureDevOpsProject";
        public const string AzureDevOpsPat      = "AzureDevOpsPat";
        public const string AzureDevOpsEnabled  = "AzureDevOpsEnabled";
        // Freshdesk
        public const string FreshDeskDomain     = "FreshDeskDomain";
        public const string FreshDeskApiKey     = "FreshDeskApiKey";
        public const string FreshDeskEnabled    = "FreshDeskEnabled";
        /// <summary>"true" = al sincronizar, incluir los tickets asignados a MÍ (mi agente vía /agents/me).</summary>
        public const string FreshDeskFilterMine = "FreshDeskFilterMine";
        /// <summary>Grupo/departamento (nombre como "Desarrollo Web" o su ID numérico) a incluir; vacío = ninguno.</summary>
        public const string FreshDeskGroup      = "FreshDeskGroup";
        // DevOps auto-sync
        public const string DevOpsSyncIntervalMinutes = "DevOpsSyncIntervalMinutes";
        // Documentos de vacaciones (plantilla + conversión a PDF)
        public const string VacationDepartamento = "VacationDepartamento";
        public const string VacationPuestoDefault = "VacationPuestoDefault";
        public const string VacationJefeDirecto   = "VacationJefeDirecto";
        public const string LibreOfficePath       = "LibreOfficePath";
        // Correo (SMTP + IMAP)
        public const string EmailEnabled        = "EmailEnabled";
        public const string EmailAddress        = "EmailAddress";
        public const string EmailDisplayName    = "EmailDisplayName";
        public const string EmailPassword       = "EmailPassword";       // secreto (contraseña de app)
        public const string EmailSmtpHost       = "EmailSmtpHost";
        public const string EmailSmtpPort       = "EmailSmtpPort";
        public const string EmailImapHost       = "EmailImapHost";
        public const string EmailImapPort       = "EmailImapPort";
        public const string EmailRequirementsFolder = "EmailRequirementsFolder"; // carpeta IMAP a ingerir

        /// <summary>Buzón del jefe que recibe los escalamientos de SLA vencido. Admite varios separados por «;».</summary>
        public const string SlaEscalationEmail = "SlaEscalationEmail";

        /// <summary>"false" desactiva el respaldo de la carpeta remota antes de desplegar.</summary>
        public const string DeployBackupEnabled = "DeployBackupEnabled";

        // Carpetas (prefijos) dentro del contenedor de Blob Storage. Vacío = valor por omisión.
        public const string AzureBlobReleasesPrefix     = "AzureBlobReleasesPrefix";
        public const string AzureBlobBackupsPrefix      = "AzureBlobBackupsPrefix";
        public const string AzureBlobDeployBackupsPrefix = "AzureBlobDeployBackupsPrefix";

        /// <summary>Subcarpetas de destino de las versiones, separadas por «;». Vacío = las de por omisión.</summary>
        public const string AzureBlobEnvironmentFolders = "AzureBlobEnvironmentFolders";
    }

    public SettingsService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public string? Get(string key)
    {
        var setting = _db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting == null) return null;

        if (setting.IsSecret && setting.Value != null)
        {
            if (SecretProtector.TryUnprotect(setting.Value, out var plain))
                return plain;
            return null;
        }
        return setting.Value;
    }

    public void Set(string key, string? value, bool isSecret = false, string? description = null)
    {
        var setting = _db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting == null)
        {
            setting = new AppSetting { Key = key, IsSecret = isSecret, Description = description };
            _db.AppSettings.Add(setting);
        }

        setting.Value = (isSecret && value != null) ? SecretProtector.Protect(value) : value;
        setting.IsSecret = isSecret;
        if (description != null) setting.Description = description;

        _db.SaveChanges();
        _audit.Record(AuditAction.ConfigChange, "AppSetting", key, $"Configuración actualizada: {key}");
    }

    public List<AppSetting> GetAll() =>
        _db.AppSettings.OrderBy(s => s.Key).ToList();
}
