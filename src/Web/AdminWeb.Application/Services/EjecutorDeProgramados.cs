using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que la agenda usa para disparar de verdad un despliegue programado.
///
/// <para><b>Por qué no se usa <see cref="DeploymentService"/> directamente.</b> Ese servicio es la
/// puerta de la PANTALLA: exige sesión (<c>RequireAdminOrOperaciones</c>), exige el checklist
/// marcado y exige la nota que explica por qué se despliega ahora. Un despliegue programado no tiene
/// nada de eso —corre de madrugada, sin nadie delante— y darle una sesión de administrador falsa
/// para que pasara sería abrir justo la puerta trasera que esas guardas existen para cerrar.</para>
///
/// <para><b>Lo que NO se relaja.</b> El checklist no desaparece: se sustituye por la evidencia que la
/// agenda generó al programar la cita, que dice con todas sus letras que el despliegue fue
/// automático y quién lo dejó agendado. El expediente sigue contestando «¿por qué este despliegue?»
/// un mes después, que es para lo que existe.</para>
///
/// <para>La maquinaria es la MISMA que la de un despliegue a mano —mismo motor, mismo respaldo
/// previo, mismos avisos en vivo por si alguien abre la consola mientras corre—. Lo único distinto
/// es de dónde viene la orden y que aquí se espera al final, porque hay una cita que anotar.</para>
/// </summary>
public class EjecutorDeProgramados(AppDbContext db, EjecutorDeDespliegues ejecutor) : IEjecutorDeDespliegues
{
    /// <summary>
    /// Con qué identidad se firma un despliegue que nadie lanzó.
    ///
    /// Sin usuario y sin rol a propósito: la bitácora y la evidencia dirán «programado» y no el
    /// nombre de una persona que no estaba. Y como no es administrador ni operaciones, tampoco
    /// serviría de nada si alguien intentara colarla por otro camino.
    /// </summary>
    private static readonly IdentidadDelDespliegue Programado =
        new(null, "programado", "Despliegue programado", null, null);

    public async Task<ResultadoDeDespliegue> DesplegarAsync(
        int versionId, int perfilId, string evidenciaDeChecklist, CancellationToken ct = default)
    {
        var version = await db.AppReleases.AsNoTracking()
            .Include(r => r.AppSystem)
            .FirstOrDefaultAsync(r => r.Id == versionId, ct)
            ?? throw new KeyNotFoundException("La versión programada ya no existe.");

        var servidores = await db.DeploymentProfileTargets.AsNoTracking()
            .Where(pt => pt.ProfileId == perfilId && pt.Target!.IsActive)
            .OrderBy(pt => pt.Order)
            .Select(pt => pt.Target!)
            .ToListAsync(ct);

        // Un perfil cuyos servidores se desactivaron entre agendar y disparar no es un fallo del
        // despliegue: es que ya no hay a dónde desplegar, y hay que decirlo así en la cita.
        if (servidores.Count == 0)
            throw new InvalidOperationException(
                "Ninguno de los servidores de ese destino sigue activo. La programación no se ejecutó.");

        var destino = $"{servidores.Count} servidor(es): {string.Join(", ", servidores.Select(s => s.Nombre))}";

        var trabajo = new DeploymentJob
        {
            AppReleaseId = version.Id,
            DeploymentProfileId = perfilId,
            Status = JobStatus.EnCurso,
            StartedAt = DateTime.UtcNow,
            // Sin usuario que lo empezó: no lo empezó nadie. Guardar aquí a quien lo agendó haría
            // creer al historial que esa persona estaba delante.
            StartedById = null,
            TargetsTotal = servidores.Count,
            Notes = evidenciaDeChecklist,
            CreatedAt = DateTime.UtcNow
        };
        db.DeploymentJobs.Add(trabajo);
        await db.SaveChangesAsync(ct);

        // Se respaldan TODOS los servidores. Es el valor seguro por omisión y aquí importa más que en
        // un despliegue a mano: nadie va a estar mirando si algo sale mal.
        var vivo = await ejecutor.LanzarYEsperarAsync(new OrdenDeDespliegue(
            trabajo.Id, version.Id, version.AppSystem.Name, version.Version, destino,
            [.. servidores.Select(s => s.Id)],
            servidores.Select(s => s.Id).ToHashSet(),
            Programado));

        // El estado final se relee de la BASE y no del trabajo en memoria: es el motor quien lo
        // escribe al terminar, y es esa fila la que se va a consultar después en el historial.
        var final = await db.DeploymentJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == trabajo.Id, CancellationToken.None);

        return new ResultadoDeDespliegue(
            trabajo.Id,
            final?.Status ?? JobStatus.Fallido,
            final?.TargetsOk ?? 0,
            final?.TargetsFailed ?? vivo.Orden.ServidorIds.Count);
    }

    /// <summary>
    /// Congela una selección de servidores en un perfil interno, igual que hace la pantalla al
    /// desplegar a mano: <c>IsAdHoc</c> lo esconde de todos los selectores y el historial conserva
    /// para siempre a qué servidores fue esa cita, aunque después alguien cambie la selección.
    /// </summary>
    public async Task<int> CrearPerfilCongeladoAsync(
        IReadOnlyList<int> servidorIds, string etiqueta, CancellationToken ct = default)
    {
        var servidores = await db.DeploymentTargets
            .Where(t => servidorIds.Contains(t.Id) && t.IsActive)
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);

        if (servidores.Count == 0)
            throw new InvalidOperationException("Ninguno de los servidores elegidos sigue activo.");

        var nombres = string.Join(", ", servidores.Select(s => s.Nombre));
        if (nombres.Length > 160) nombres = nombres[..160] + "…";

        var perfil = new DeploymentProfile
        {
            Name = $"🗓 {etiqueta}: {nombres}",
            Description = "Servidores congelados al agendar un despliegue (no es un perfil guardado).",
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
