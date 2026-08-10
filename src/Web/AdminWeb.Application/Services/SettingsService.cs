using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La configuración compartida de la aplicación: las claves que el líder ajusta sin recompilar y los
/// secretos de las integraciones.
///
/// <para><b>Escribir aquí es cosa del administrador y solo suya.</b> Estas filas deciden a qué
/// servidor se despliega y con qué credenciales: quien las cambie puede desviar un despliegue
/// entero. La guarda está dentro del servicio, además de la política del endpoint.</para>
///
/// <para><b>Los secretos no salen nunca en claro hacia el navegador.</b> Se puede saber si una clave
/// está configurada y escribir una nueva, pero no leerla: una respuesta de la API que llevara la
/// contraseña del correo dejaría de estar protegida en el momento en que alguien abre la consola del
/// navegador. En el escritorio esto no se planteaba porque el valor no salía del proceso.</para>
/// </summary>
public class SettingsService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// Las claves conocidas. Están escritas y no se generan porque son un contrato: el mismo texto lo
    /// leen la aplicación de escritorio, este servicio y las filas que ya existen en la base.
    /// </summary>
    public static class Claves
    {
        public const string AzureBlobConnectionString = "AzureBlobConnectionString";
        public const string AzureSqlConnectionString = "AzureSqlConnectionString";
        public const string DefaultDeployFolder = "DefaultDeployFolder";
        public const string AzureBlobContainer = "AzureBlobContainer";

        public const string AzureDevOpsOrgUrl = "AzureDevOpsOrgUrl";
        public const string AzureDevOpsProject = "AzureDevOpsProject";
        public const string AzureDevOpsPat = "AzureDevOpsPat";
        public const string AzureDevOpsEnabled = "AzureDevOpsEnabled";
        public const string DevOpsSyncIntervalMinutes = "DevOpsSyncIntervalMinutes";

        /// <summary>Sellos de la última sincronización correcta. Los escribe la aplicación, no se
        /// teclean, y por eso están en <see cref="Ocultas"/>: no son configuración.</summary>
        public const string UltimaSincronizacionDevOps = "UltimaSincronizacionDevOps";
        public const string UltimaSincronizacionFreshdesk = "UltimaSincronizacionFreshdesk";

        public const string FreshDeskDomain = "FreshDeskDomain";
        public const string FreshDeskApiKey = "FreshDeskApiKey";
        public const string FreshDeskEnabled = "FreshDeskEnabled";
        public const string FreshDeskFilterMine = "FreshDeskFilterMine";
        public const string FreshDeskGroup = "FreshDeskGroup";

        public const string VacationDepartamento = "VacationDepartamento";
        public const string VacationPuestoDefault = "VacationPuestoDefault";
        public const string VacationJefeDirecto = "VacationJefeDirecto";

        public const string EmailEnabled = "EmailEnabled";
        public const string EmailAddress = "EmailAddress";
        public const string EmailDisplayName = "EmailDisplayName";
        public const string EmailPassword = "EmailPassword";
        public const string EmailSmtpHost = "EmailSmtpHost";
        public const string EmailSmtpPort = "EmailSmtpPort";
        public const string EmailImapHost = "EmailImapHost";
        public const string EmailImapPort = "EmailImapPort";
        public const string EmailRequirementsFolder = "EmailRequirementsFolder";

        public const string SlaEscalationEmail = "SlaEscalationEmail";
        public const string DeployBackupEnabled = "DeployBackupEnabled";

        public const string AzureBlobReleasesPrefix = "AzureBlobReleasesPrefix";
        public const string AzureBlobBackupsPrefix = "AzureBlobBackupsPrefix";
        public const string AzureBlobDeployBackupsPrefix = "AzureBlobDeployBackupsPrefix";
        public const string AzureBlobEnvironmentFolders = "AzureBlobEnvironmentFolders";

        /// <summary>Cuántas actividades del pool puede tener tomadas alguien a la vez.</summary>
        public const string PoolMaxTomadas = "pool.max-tomadas";
    }

    /// <summary>
    /// Claves que NO se enseñan en la pantalla de configuración de la web.
    ///
    /// <c>LibreOfficePath</c> señalaba un programa instalado en la máquina de cada quien, y la web
    /// genera los documentos por su cuenta. Enseñarla invitaría a configurar algo que ya no hace
    /// nada; la fila se deja intacta porque el escritorio la sigue usando hasta el corte.
    ///
    /// Los sellos de sincronización los escribe la aplicación y no se teclean: aparecen junto al
    /// botón de sincronizar de su pantalla, no en una caja de configuración.
    /// </summary>
    private static readonly HashSet<string> Ocultas = new(StringComparer.OrdinalIgnoreCase)
    {
        "LibreOfficePath",
        // Se declaró para «la base que usa el escritorio», pero el escritorio nunca lee esa fila
        // —su conexión sale de dbprovider.json y del recurso incrustado— y la web tampoco. La fila
        // se deja intacta en la base por si alguien la capturó alguna vez; simplemente deja de
        // ofrecerse, que es lo que evita seguir invitando a configurar algo que no hace nada.
        Claves.AzureSqlConnectionString,
        Claves.UltimaSincronizacionDevOps,
        Claves.UltimaSincronizacionFreshdesk
    };

    // ── Lectura ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El valor en claro de una clave, descifrándola si es secreta.
    ///
    /// <b>Es para uso INTERNO del servidor</b> —quien va a conectarse al correo o a Blob—, nunca para
    /// responderle al navegador. Devuelve null si no está configurada, si el secreto está corrupto o
    /// si quedó cifrado con el esquema viejo de Windows, que aquí no se puede leer.
    /// </summary>
    public async Task<string?> ObtenerAsync(string clave, CancellationToken ct = default)
    {
        var fila = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == clave, ct);
        if (fila?.Value == null) return null;

        return fila.IsSecret ? ProtectorPortable.Descifrar(fila.Value) : fila.Value;
    }

    /// <summary>Un entero de configuración, o el valor por omisión si falta o no se entiende.</summary>
    public async Task<int> ObtenerEnteroAsync(string clave, int porOmision, CancellationToken ct = default) =>
        int.TryParse(await ObtenerAsync(clave, ct), out var n) ? n : porOmision;

    /// <summary>Un interruptor de configuración. Lo que no diga «true» es false, como en el escritorio.</summary>
    public async Task<bool> ObtenerBooleanoAsync(string clave, CancellationToken ct = default) =>
        string.Equals(await ObtenerAsync(clave, ct), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Todo lo configurable, listo para pintar una pantalla. <b>Los secretos van sin valor</b>: solo
    /// se dice si están puestos y si se pueden leer.
    /// </summary>
    public async Task<List<ValorDeConfiguracion>> TodoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        return (await db.AppSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct))
            .Where(s => !Ocultas.Contains(s.Key))
            .Select(Describir)
            .ToList();
    }

    private static ValorDeConfiguracion Describir(AppSetting s) => new(
        s.Key,
        // El valor solo viaja cuando NO es secreto. Esconderlo en la pantalla no serviría de nada:
        // quien mire la respuesta de la API lo leería igual.
        s.IsSecret ? null : s.Value,
        s.IsSecret,
        Configurado: !string.IsNullOrEmpty(s.Value),
        RequiereRecaptura: s.IsSecret && ProtectorPortable.EsHeredadoDeWindows(s.Value),
        s.Description);

    // ── Escritura ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda una clave. Un secreto se cifra antes de tocar la base.
    ///
    /// <para>Se cifra con el esquema de la aplicación de ESCRITORIO a propósito, y no con el de
    /// Data Protection que sería más fuerte: hasta el corte, las dos aplicaciones leen estas mismas
    /// filas. Un secreto guardado desde la web con otro cifrado dejaría al escritorio sin poder
    /// leerlo —y el síntoma no sería un error claro sino un despliegue que falla con la contraseña
    /// equivocada—. Ver <see cref="ProtectorPortable"/>.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarAsync(
        string clave, string? valor, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        clave = (clave ?? "").Trim();
        if (clave.Length == 0) return (false, "Falta la clave.");
        if (Ocultas.Contains(clave)) return (false, "Esa clave ya no se configura desde la web.");

        var fila = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == clave, ct);
        if (fila == null)
        {
            fila = new AppSetting { Key = clave, IsSecret = EsSecreta(clave) };
            db.AppSettings.Add(fila);
        }

        // Vacío BORRA el valor en vez de guardar una cadena vacía: «sin configurar» y «configurado
        // con nada» se comportan igual en todas partes, y distinguirlos solo daría estados raros.
        valor = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        fila.IsSecret = EsSecreta(clave);
        fila.Value = fila.IsSecret && valor != null ? ProtectorPortable.Cifrar(valor) : valor;

        await db.SaveChangesAsync(ct);

        // El VALOR no se anota en la bitácora, ni siquiera cuando no es secreto: ahí acaban cadenas
        // de conexión y direcciones de servidores, y la bitácora la lee más gente que la que puede
        // tocar la configuración.
        await audit.RecordAsync(AuditAction.ConfigChange, "AppSetting", clave,
            valor == null ? $"Configuración borrada: {clave}" : $"Configuración actualizada: {clave}", ct);

        return (true, valor == null ? $"«{clave}» quedó sin valor." : $"«{clave}» guardada.");
    }

    // ── Sellos técnicos ─────────────────────────────────────────────────────────
    //
    // No son configuración: los escribe la aplicación, nadie los teclea y no se ven en la pantalla
    // de Configuración. Van en AppSettings porque es una tabla de pares clave-valor que ya existe y
    // crear una tabla para guardar dos fechas sería peor.

    /// <summary>
    /// Deja constancia de cuándo terminó bien una sincronización.
    ///
    /// <para>NO pasa por <see cref="GuardarAsync"/> a propósito, por dos razones: aquélla exige ser
    /// líder —y esto lo escribe el propio servicio, que ya comprobó permisos antes de sincronizar—
    /// y anota en la bitácora, que quedaría con una línea de «configuración actualizada» por cada
    /// sincronización. Un sello no es un cambio de configuración.</para>
    /// </summary>
    public async Task MarcarSincronizacionAsync(string clave, DateTime utc, CancellationToken ct = default)
    {
        var fila = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == clave, ct);
        if (fila == null)
        {
            fila = new AppSetting { Key = clave, IsSecret = false, Description = "Sello técnico: última sincronización correcta." };
            db.AppSettings.Add(fila);
        }

        // ISO 8601 con la «O»: se lee igual en cualquier idioma del servidor y vuelve a DateTime sin
        // ambigüedad. Guardar «07/08/2026 14:30» habría dependido de la cultura del proceso.
        fila.Value = utc.ToUniversalTime().ToString("O");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cuándo fue la última sincronización correcta, o null si nunca se ha hecho.
    /// Sin guarda: es un dato de estado que la pantalla enseña junto al botón de sincronizar.
    /// </summary>
    public async Task<DateTime?> UltimaSincronizacionAsync(string clave, CancellationToken ct = default)
    {
        var valor = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == clave).Select(s => s.Value).FirstOrDefaultAsync(ct);

        return DateTime.TryParse(valor, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var cuando)
            ? cuando.ToUniversalTime()
            : null;
    }

    /// <summary>
    /// Qué claves son secretas. Se decide por la CLAVE y no por lo que mande quien llama: si el
    /// cliente pudiera decir «esta no es secreta», bastaría con eso para guardar una contraseña en
    /// claro y que cualquiera con acceso a la base la leyera.
    /// </summary>
    private static bool EsSecreta(string clave) =>
        clave.EndsWith("Pat", StringComparison.OrdinalIgnoreCase)
        || clave.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase)
        || clave.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
        || clave.EndsWith("ConnectionString", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Una clave de configuración vista desde fuera. <paramref name="Valor"/> es null en los secretos:
/// se sabe si están puestos, no cuáles son.
/// </summary>
/// <param name="RequiereRecaptura">
/// El secreto está cifrado con el esquema viejo de Windows y el servidor no puede leerlo. No es que
/// falte: hay que volver a capturarlo. Decirlo evita el diagnóstico equivocado de «se borró solo».
/// </param>
public record ValorDeConfiguracion(
    string Clave,
    string? Valor,
    bool EsSecreto,
    bool Configurado,
    bool RequiereRecaptura,
    string? Descripcion);
