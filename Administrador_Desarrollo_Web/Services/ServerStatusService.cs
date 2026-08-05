using Administrador_Desarrollo_Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Qué hay hoy en un servidor, quién lo puso y cuándo.</summary>
public sealed record EstadoServidor(
    int TargetId,
    string Servidor,
    string Host,
    string? Url,
    bool Activo,
    string? Sistema,
    string? Version,
    DateTime? DesplegadoUtc,
    string? Quien,
    int? JobId,
    /// <summary>Versión más reciente que existe de ESE sistema. Null si el servidor no tiene nada.</summary>
    string? UltimaVersionDelSistema)
{
    public bool NuncaDesplegado => DesplegadoUtc == null;

    /// <summary>
    /// true cuando el servidor tiene una versión y NO es la última publicada de su sistema. Es la
    /// pregunta que de verdad se hace alguien mirando esta pantalla: «¿a quién le falta?».
    /// </summary>
    public bool Atrasado =>
        Version != null && UltimaVersionDelSistema != null &&
        !string.Equals(Version, UltimaVersionDelSistema, StringComparison.OrdinalIgnoreCase);

    public string Antiguedad
    {
        get
        {
            if (DesplegadoUtc == null) return "—";
            var d = DateTime.UtcNow - DesplegadoUtc.Value;
            if (d.TotalMinutes < 1)  return "hace un momento";
            if (d.TotalMinutes < 60) return $"hace {(int)d.TotalMinutes} min";
            if (d.TotalHours   < 24) return $"hace {(int)d.TotalHours} h";
            if (d.TotalDays    < 30) return $"hace {(int)d.TotalDays} d";
            return $"hace {(int)(d.TotalDays / 30)} meses";
        }
    }
}

/// <summary>
/// Arma la foto de «qué versión tiene cada servidor». Es un servicio y no código de la pantalla para
/// que se pueda probar sin abrir la ventana: la parte delicada —resolver nombres y detectar
/// atrasados— es lógica, no dibujo.
///
/// TODO SE LEE CON AsNoTracking. El <c>AppDbContext</c> es Singleton y el despliegue escribe desde su
/// propio contexto: sin esto, la resolución de identidad devolvería las instancias rastreadas de
/// antes del despliegue y esta pantalla mostraría, con toda seriedad, la versión anterior.
/// </summary>
public class ServerStatusService
{
    private readonly AppDbContext _db;

    public ServerStatusService(AppDbContext db) => _db = db;

    public List<EstadoServidor> Obtener(bool incluirDadosDeBaja = false)
    {
        var targets = _db.DeploymentTargets.AsNoTracking()
            .Include(t => t.LastRelease).ThenInclude(r => r!.AppSystem)
            .Where(t => incluirDadosDeBaja || t.IsActive)
            .OrderBy(t => t.Nombre)
            .ToList();

        if (targets.Count == 0) return [];

        // Los nombres en UNA consulta: con veintitantos servidores, resolver el usuario fila por fila
        // son veintitantos viajes a una base remota.
        var userIds = targets.Where(t => t.LastDeployedById != null)
            .Select(t => t.LastDeployedById!.Value).Distinct().ToList();
        var quienes = _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToList()
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);

        var ultimas = UltimaVersionPorSistema(
            targets.Where(t => t.LastRelease != null).Select(t => t.LastRelease!.AppSystemId).Distinct().ToList());

        return targets.Select(t => new EstadoServidor(
            TargetId:      t.Id,
            Servidor:      t.Nombre,
            Host:          t.Host,
            Url:           string.IsNullOrWhiteSpace(t.URL) ? null : t.URL,
            Activo:        t.IsActive,
            Sistema:       t.LastRelease?.AppSystem.Name,
            Version:       t.LastRelease?.Version,
            DesplegadoUtc: t.LastDeployedAt,
            Quien:         t.LastDeployedById is int id && quienes.TryGetValue(id, out var n) ? n : null,
            JobId:         t.LastDeploymentJobId,
            UltimaVersionDelSistema: t.LastRelease != null && ultimas.TryGetValue(t.LastRelease.AppSystemId, out var v) ? v : null
        )).ToList();
    }

    /// <summary>
    /// Versión más reciente registrada de cada sistema, por fecha de alta.
    ///
    /// Por CreatedAt y no comparando el texto de la versión: «1.10» es posterior a «1.9» pero menor
    /// alfabéticamente, y no todos los sistemas numeran igual. La fecha de alta sí es un orden
    /// confiable con cualquier convención.
    /// </summary>
    private Dictionary<int, string> UltimaVersionPorSistema(List<int> systemIds)
    {
        if (systemIds.Count == 0) return [];

        return _db.AppReleases.AsNoTracking()
            .Where(r => systemIds.Contains(r.AppSystemId))
            .GroupBy(r => r.AppSystemId)
            .Select(g => new
            {
                AppSystemId = g.Key,
                Version = g.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                           .Select(r => r.Version).First()
            })
            .ToList()
            .ToDictionary(x => x.AppSystemId, x => x.Version);
    }
}
