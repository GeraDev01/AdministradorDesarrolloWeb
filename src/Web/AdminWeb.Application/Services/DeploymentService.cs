using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La puerta del despliegue: quién puede desplegar qué, a dónde, con qué respaldo y con qué
/// checklist confirmado. Lo que pasa DESPUÉS —conectarse, respaldar, subir— es del
/// <see cref="EjecutorDeDespliegues"/>.
///
/// <para><b>Por qué está partido en dos.</b> En el escritorio todo esto era un solo método porque
/// todo ocurría dentro del mismo clic: se validaba y se desplegaba en la misma llamada, y la persona
/// se quedaba mirando. Aquí la validación tiene que contestar de inmediato —hay una petición HTTP
/// esperando— y el despliegue tiene que sobrevivirla. Este servicio decide y devuelve; el ejecutor
/// trabaja.</para>
///
/// <para><b>Las guardas se conservan y se refuerzan.</b> El escritorio ya había movido la
/// autorización del combo de la pantalla al servicio, después de que se descubriera que cualquier
/// otra ruta desplegaba lo que quisiera. Aquí eso es todavía más necesario: la API se puede llamar
/// sin pasar por el navegador. Y el checklist, que allí lo exigía el diálogo, se exige también
/// aquí.</para>
/// </summary>
public class DeploymentService(
    AppDbContext db,
    ICurrentUser quien,
    AuditService bitacora,
    EjecutorDeDespliegues ejecutor)
{
    /// <summary>
    /// ¿Puede este usuario desplegar ESTE perfil? Admin siempre; Operaciones solo los perfiles
    /// habilitados. Los perfiles internos <c>IsAdHoc</c> están exentos porque no son perfiles
    /// guardados: son la foto congelada de una selección directa que ya pasó por
    /// <see cref="AuthorizationGuard.RequireAdminOrOperaciones"/>.
    /// </summary>
    public static bool PuedeDesplegarPerfil(ICurrentUser usuario, DeploymentProfile perfil) =>
        usuario.IsAdmin || perfil.IsAdHoc || perfil.AllowedForOperaciones;

    /// <summary>
    /// Valida la orden, la deja registrada y la pone en marcha. Devuelve el identificador del
    /// trabajo, que es con el que la pantalla se suscribe a su avance.
    /// </summary>
    public async Task<(bool ok, string mensaje, int? jobId)> LanzarAsync(
        LanzarDespliegueRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var version = await db.AppReleases.AsNoTracking()
            .Include(r => r.AppSystem)
            .FirstOrDefaultAsync(r => r.Id == peticion.VersionId, ct);
        if (version == null) return (false, "Esa versión ya no existe. Actualiza la lista.", null);

        var (servidores, error) = await ResolverDestinoAsync(peticion, ct);
        if (error != null) return (false, error, null);

        // Dos despliegues simultáneos a la misma carpeta remota dejan una mezcla de dos versiones, y
        // lo peor es que no se nota. En el escritorio no podía pasar —desplegaba una persona en su
        // máquina—; aquí despliega el servidor y dos personas pueden pulsar el botón a la vez.
        var ocupado = servidores.FirstOrDefault(s => ejecutor.ServidorOcupado(s.Id));
        if (ocupado != null)
            return (false, $"«{ocupado.Nombre}» está recibiendo otro despliegue ahora mismo. " +
                           "Espera a que termine o quítalo de la selección.", null);

        // El checklist se exige AQUÍ además de en la pantalla. Allí lo pide un formulario que corre
        // en la máquina del usuario; esta es la barrera que no se puede saltar.
        var faltantes = DeploymentChecklist.Faltantes(peticion.Marcados ?? []);
        if (faltantes.Count > 0)
            return (false, "Falta confirmar el checklist previo: " +
                           string.Join("; ", faltantes.Select(p => p.Texto)) + ".", null);

        if (!DeploymentChecklist.NotaSuficiente(peticion.Nota))
            return (false, $"Escribe por qué se despliega ahora (al menos {DeploymentChecklist.MinimoNota} " +
                           "caracteres). Es lo único que contesta «¿por qué este despliegue?» un mes después.", null);

        // null = respaldar todos. Es el valor seguro por omisión: quien no se pronuncia obtiene la
        // red de seguridad, no su ausencia.
        var respaldar = peticion.RespaldarServidorIds == null
            ? servidores.Select(s => s.Id).ToHashSet()
            : peticion.RespaldarServidorIds.ToHashSet();

        var nombres = servidores.Select(s => s.Nombre).ToList();
        var destino = $"{servidores.Count} servidor(es): {string.Join(", ", nombres)}";
        var respaldoTexto = respaldar.Count == servidores.Count ? "todos"
                          : respaldar.Count == 0 ? "ninguno"
                          : $"{respaldar.Count} de {servidores.Count}";

        var perfilId = peticion.ServidorIds is { Count: > 0 }
            ? await CongelarSeleccionAsync(servidores, ct)
            : peticion.PerfilId!.Value;

        var queSeDespliega = $"{version.AppSystem.Name} v{version.Version}";
        var evidencia = DeploymentChecklist.Evidencia(
            quien.FullName ?? quien.Username ?? "(sin nombre)",
            DateTime.Now, queSeDespliega, destino, respaldoTexto,
            peticion.Marcados ?? [], peticion.Nota);

        var job = new DeploymentJob
        {
            AppReleaseId = version.Id,
            DeploymentProfileId = perfilId,
            Status = JobStatus.EnCurso,
            StartedAt = DateTime.UtcNow,
            StartedById = quien.UserId,
            TargetsTotal = servidores.Count,
            Notes = evidencia,
            CreatedAt = DateTime.UtcNow
        };
        db.DeploymentJobs.Add(job);
        await db.SaveChangesAsync(ct);

        ejecutor.Lanzar(new OrdenDeDespliegue(
            job.Id, version.Id, version.AppSystem.Name, version.Version, destino,
            [.. servidores.Select(s => s.Id)], respaldar, IdentidadDelDespliegue.De(quien)));

        return (true,
            $"Despliegue de {queSeDespliega} en marcha hacia {servidores.Count} servidor(es). " +
            "Corre en el servidor: puedes cerrar esta pestaña sin interrumpirlo.", job.Id);
    }

    /// <summary>Cancela un despliegue en curso. Es deliberado: cerrar la pestaña no cancela.</summary>
    public (bool ok, string mensaje) Cancelar(int jobId)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);
        return ejecutor.Cancelar(jobId, quien);
    }

    // ── Destino ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A qué servidores va, ya sea por selección directa o por perfil guardado. Devuelve el motivo
    /// exacto cuando no se puede, para que la pantalla lo enseñe tal cual.
    /// </summary>
    private async Task<(List<DeploymentTarget> servidores, string? error)> ResolverDestinoAsync(
        LanzarDespliegueRequest peticion, CancellationToken ct)
    {
        // Selección DIRECTA de servidores (estilo Blobup): es la vía normal para Admin y Operaciones.
        if (peticion.ServidorIds is { Count: > 0 })
        {
            var elegidos = await db.DeploymentTargets
                .Where(t => peticion.ServidorIds.Contains(t.Id) && t.IsActive)
                .ToListAsync(ct);

            // Un servidor desactivado ya no debe recibir despliegues; antes se le desplegaba igual.
            if (elegidos.Count == 0)
                return ([], "Ninguno de los servidores elegidos sigue activo. Actualiza la lista.");

            return ([.. elegidos.OrderBy(t => t.Nombre)], null);
        }

        if (peticion.PerfilId is not int perfilId)
            return ([], "Elige al menos un servidor destino.");

        var perfil = await db.DeploymentProfiles
            .Include(p => p.ProfileTargets).ThenInclude(pt => pt.Target)
            .FirstOrDefaultAsync(p => p.Id == perfilId, ct);
        if (perfil == null) return ([], "Ese perfil ya no existe. Actualiza la lista.");

        // La regla se revalida contra el perfil REAL, no contra lo que la pantalla haya mostrado.
        if (!PuedeDesplegarPerfil(quien, perfil))
        {
            await bitacora.RecordDeniedAsync(AuditAction.Deploy, "DeploymentProfile", perfil.Id.ToString(),
                $"Intento de desplegar el perfil «{perfil.Name}», no habilitado para Operaciones.", ct: ct);
            throw new AuthorizationException(
                $"El perfil «{perfil.Name}» no está habilitado para el rol Operaciones.");
        }

        var activos = perfil.ProfileTargets.OrderBy(pt => pt.Order)
            .Select(pt => pt.Target).Where(t => t.IsActive).ToList();

        return activos.Count == 0
            ? ([], "El perfil no tiene servidores activos asignados.")
            : (activos, null);
    }

    /// <summary>
    /// Crea el perfil interno CONGELADO que respalda una selección directa.
    ///
    /// <para><b>Cambia respecto al escritorio y por un motivo concreto.</b> Allí la selección directa
    /// reutilizaba un único perfil oculto («⚡ Selección directa») que se reescribía en cada
    /// despliegue; el propio código anotaba que por eso el historial no podía decir a qué servidores
    /// había ido, y lo compensaba congelando los nombres dentro de la evidencia. Con un solo usuario
    /// eso bastaba. Aquí no: dos personas desplegando a la vez se pisarían el perfil mientras el otro
    /// despliegue sigue corriendo, y el historial de uno acabaría señalando los servidores del otro.
    /// El escritorio ya tenía la solución para su caso multiusuario —los perfiles congelados de los
    /// despliegues programados— y aquí se aplica a todos.</para>
    /// </summary>
    private async Task<int> CongelarSeleccionAsync(List<DeploymentTarget> servidores, CancellationToken ct)
    {
        var nombres = string.Join(", ", servidores.Select(s => s.Nombre));
        if (nombres.Length > 160) nombres = nombres[..160] + "…";

        var perfil = new DeploymentProfile
        {
            Name = $"⚡ {nombres}",
            Description = "Servidores elegidos directamente para un despliegue (no es un perfil guardado).",
            AllowedForOperaciones = false,   // da igual: IsAdHoc lo oculta de todos los selectores
            IsAdHoc = true,
            CreatedAt = DateTime.UtcNow
        };
        db.DeploymentProfiles.Add(perfil);
        await db.SaveChangesAsync(ct);

        int orden = 0;
        foreach (var servidor in servidores)
            db.DeploymentProfileTargets.Add(new DeploymentProfileTarget
            {
                ProfileId = perfil.Id,
                TargetId = servidor.Id,
                Order = orden++
            });
        await db.SaveChangesAsync(ct);

        return perfil.Id;
    }
}
