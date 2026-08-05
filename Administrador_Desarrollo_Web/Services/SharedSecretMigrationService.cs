using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Microsoft.Extensions.Logging;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Qué encontró y qué pudo arreglar una pasada de migración.</summary>
public sealed record ResultadoMigracionSecretos(
    int Migrados,
    int YaPortables,
    IReadOnlyList<string> Ilegibles)
{
    public static readonly ResultadoMigracionSecretos Vacio = new(0, 0, []);

    public bool HuboCambios => Migrados > 0;
    public bool HayPendientes => Ilegibles.Count > 0;
}

/// <summary>
/// Pasa los secretos de la base al cifrado portable (<see cref="SharedSecretProtector"/>).
///
/// EL PROBLEMA QUE RESUELVE. Estos valores se guardaban con DPAPI, que ata el cifrado a la cuenta de
/// Windows que lo escribió. Como viajan en la base COMPARTIDA, la PC que los guardó era la única que
/// podía leerlos: en cualquier otra el Blob aparecía sin configurar y los despliegues morían con
/// «530 User cannot log in», porque se mandaba el texto cifrado como contraseña del FTP.
///
/// CÓMO. Solo puede convertir lo que este equipo alcanza a descifrar, así que hay que abrir la
/// aplicación en la PC que capturó cada valor. Lo que no logra leer NO se toca ni se borra: se
/// reporta para que un administrador lo recapture una vez, y a partir de ahí ya lo ve todo el
/// equipo. Es idempotente: correrla de nuevo no vuelve a tocar lo que ya está portable.
/// </summary>
public class SharedSecretMigrationService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ILogger<SharedSecretMigrationService> _log;

    public SharedSecretMigrationService(AppDbContext db, AuditService audit,
        ILogger<SharedSecretMigrationService> log)
    {
        _db = db; _audit = audit; _log = log;
    }

    /// <summary>
    /// Migra lo que se pueda. No lanza: un fallo aquí (típicamente un login sin permiso de escritura
    /// en AppSettings) no debe impedir que la aplicación abra — solo deja el aviso en el registro.
    /// </summary>
    public ResultadoMigracionSecretos EjecutarSeguro()
    {
        try
        {
            var r = Ejecutar();
            if (r.HuboCambios)
                _log.LogInformation("Secretos compartidos migrados al cifrado portable: {n}.", r.Migrados);
            if (r.HayPendientes)
                _log.LogWarning(
                    "Quedan {n} secretos que este equipo no puede descifrar (se guardaron en otra PC o con otra " +
                    "cuenta de Windows). Hay que recapturarlos una vez: {lista}",
                    r.Ilegibles.Count, string.Join(", ", r.Ilegibles));
            return r;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudieron migrar los secretos compartidos. Se continúa sin migrar.");
            return ResultadoMigracionSecretos.Vacio;
        }
    }

    public ResultadoMigracionSecretos Ejecutar()
    {
        int migrados = 0, yaPortables = 0;
        var ilegibles = new List<string>();

        // Configuración compartida (Blob, PAT de la organización, contraseña del correo, …).
        foreach (var s in _db.AppSettings.Where(s => s.IsSecret).ToList())
        {
            if (string.IsNullOrEmpty(s.Value)) continue;
            switch (Convertir(s.Value, out var nuevo))
            {
                case Estado.YaPortable: yaPortables++; break;
                case Estado.Convertido: s.Value = nuevo; migrados++; break;
                case Estado.Ilegible:   ilegibles.Add($"Configuración → {s.Key}"); break;
            }
        }

        // Contraseñas de los servidores FTP. Se incluyen los dados de baja a propósito: reactivar uno
        // no debería estrenar un problema de credenciales resuelto hace meses.
        foreach (var t in _db.DeploymentTargets.ToList())
        {
            if (string.IsNullOrEmpty(t.Contrasena)) continue;
            switch (Convertir(t.Contrasena, out var nuevo))
            {
                case Estado.YaPortable: yaPortables++; break;
                case Estado.Convertido: t.Contrasena = nuevo; migrados++; break;
                case Estado.Ilegible:   ilegibles.Add($"Servidor «{t.Nombre}»"); break;
            }
        }

        if (migrados > 0)
        {
            _db.SaveChanges();
            _audit.RecordSystem(AuditAction.ConfigChange,
                $"Secretos compartidos migrados al cifrado portable: {migrados}. " +
                $"Ya lo estaban: {yaPortables}. Sin poder descifrar en este equipo: {ilegibles.Count}.");
        }

        return new ResultadoMigracionSecretos(migrados, yaPortables, ilegibles);
    }

    private enum Estado { YaPortable, Convertido, Ilegible }

    private static Estado Convertir(string valor, out string nuevo)
    {
        nuevo = valor;
        if (SharedSecretProtector.EsPortable(valor)) return Estado.YaPortable;

        // Heredado: solo se puede leer en el equipo y la cuenta que lo cifraron.
        if (!SecretProtector.TryUnprotect(valor, out var plano)) return Estado.Ilegible;

        nuevo = SharedSecretProtector.Protect(plano);
        return Estado.Convertido;
    }
}
