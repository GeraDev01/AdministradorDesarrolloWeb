using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Trabajo;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Los documentos que cuelgan de un requerimiento: el documento de requerimiento y el de estimación.
/// Es el port de <c>RequirementAttachmentService</c> del escritorio.
///
/// <para><b>Qué se conserva.</b> Los dos tipos y sus etiquetas, el contenido guardado inline como
/// BLOB —igual que el resto de documentos de la aplicación— y el rastro en bitácora de cada alta y
/// cada baja. También el reparto de responsabilidades: nadie más escribe en esa tabla.</para>
///
/// <para><b>Qué cambia, y por qué.</b> Allí <c>Add</c> recibía una RUTA y leía el disco con
/// <c>File.ReadAllBytes</c>; eso aquí no existe: el archivo llega por la red y quien lo valida es
/// <see cref="ArchivosSubidos"/> en el endpoint, que es donde se conoce lo que el navegador subió.
/// Con ello el tope baja de los 50 MB del escritorio a los 15 de
/// <see cref="ArchivosSubidos.MaxBytes"/>: allí el archivo iba del disco local a una base local, y
/// aquí cada byte cruza la red dos veces y lo paga todo el equipo que comparte la base.</para>
///
/// <para>Todo es del LÍDER, como la pantalla de requerimientos entera: la política del endpoint es
/// <c>SoloAdmin</c> y la guarda de este servicio es la segunda barrera, la que sigue en pie cuando
/// alguien llama a la API sin pasar por el navegador.</para>
/// </summary>
public class RequirementAttachmentService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// Cuántos documentos caben en un requerimiento.
    ///
    /// El escritorio no ponía tope porque la base era local. Aquí es la misma decisión que ya se tomó
    /// con la evidencia de una actividad: más allá de esto deja de ser documentación del requerimiento
    /// y pasa a ser un repositorio, y la base es compartida y se lee por red.
    /// </summary>
    public const int MaxPorRequerimiento = 20;

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los adjuntos de un requerimiento, <b>sin sus bytes</b>: es lo que necesita una lista, y con
    /// ellos dentro cada refresco de la pantalla se traería decenas de megas que casi nadie abre.
    /// El contenido se pide por <c>/api/adjuntos/requerimiento/{id}</c> cuando alguien pulsa.
    ///
    /// El orden es el del escritorio: primero por tipo —el documento de requerimiento antes que el de
    /// estimación— y luego por nombre.
    /// </summary>
    public async Task<IReadOnlyList<AdjuntoDeRequerimientoDto>> DeRequerimientoAsync(
        int requerimientoId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        // La proyección es la que garantiza que el BLOB no entre en el SELECT: pedir la entidad y
        // vaciar el campo después ya habría traído los bytes desde la base.
        return (await db.RequirementAttachments.AsNoTracking()
                .Where(a => a.RequirementId == requerimientoId)
                .OrderBy(a => a.Kind).ThenBy(a => a.FileName)
                .Select(a => new { a.Id, a.Kind, a.FileName, a.SizeBytes, a.UploadedAtUtc })
                .ToListAsync(ct))
            .Select(a => new AdjuntoDeRequerimientoDto(
                a.Id, a.Kind, EtiquetasDeTrabajo.TipoDeAdjunto(a.Kind), a.FileName, a.SizeBytes, a.UploadedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Cuántos adjuntos tiene cada requerimiento, para el indicador de la rejilla.
    ///
    /// De una sola consulta agrupada y no una por fila como en el escritorio: allí la base estaba al
    /// lado y aquí sería un viaje de red por requerimiento en cada carga de la pantalla.
    /// </summary>
    public async Task<Dictionary<int, int>> ConteoPorRequerimientoAsync(
        IEnumerable<int> requerimientoIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var ids = requerimientoIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return (await db.RequirementAttachments.AsNoTracking()
                .Where(a => ids.Contains(a.RequirementId))
                .GroupBy(a => a.RequirementId)
                .Select(g => new { g.Key, Cuantos = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Cuantos);
    }

    /// <summary>
    /// El contenido de un adjunto y el nombre con el que entregarlo. Vacío si ya no existe, para que
    /// la API responda 404; pedir uno sin ser el líder lanza y responde 403.
    /// </summary>
    public async Task<(byte[] bytes, string nombre)> BytesAsync(int adjuntoId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var a = await db.RequirementAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == adjuntoId, ct);

        return a?.FileBytes is { Length: > 0 }
            ? (a.FileBytes, NombreSeguro(a.FileName))
            : ([], "");
    }

    // ── Alta y baja ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cuelga un documento del requerimiento.
    /// </summary>
    /// <remarks>
    /// Los bytes llegan ya validados por <see cref="ArchivosSubidos.Validar"/> en el endpoint —tamaño,
    /// nombre y extensión—, que es donde se conoce el archivo que subió el navegador. Aquí se
    /// comprueba lo que este servicio sí sabe: que el requerimiento exista y que no se esté
    /// convirtiendo en un repositorio.
    /// </remarks>
    public async Task<(bool ok, string mensaje, int id)> AgregarAsync(
        int requerimientoId, RequirementAttachmentKind tipo, string nombre, byte[] contenido,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (contenido.Length == 0) return (false, "El archivo llegó vacío.", 0);

        var titulo = await db.Requirements.AsNoTracking()
            .Where(r => r.Id == requerimientoId).Select(r => r.Title).FirstOrDefaultAsync(ct);
        if (titulo == null) return (false, "Ese requerimiento ya no existe. Actualiza la lista.", 0);

        int yaHay = await db.RequirementAttachments.CountAsync(a => a.RequirementId == requerimientoId, ct);
        if (yaHay >= MaxPorRequerimiento)
            return (false, $"Ese requerimiento ya tiene {MaxPorRequerimiento} documentos, que es el máximo.", 0);

        var adjunto = new RequirementAttachment
        {
            RequirementId = requerimientoId,
            Kind = tipo,
            FileName = NombreSeguro(nombre),
            FileBytes = contenido,
            SizeBytes = contenido.LongLength,
            UploadedByUserId = currentUser.UserId,
            UploadedAtUtc = DateTime.UtcNow
        };

        db.RequirementAttachments.Add(adjunto);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "RequirementAttachment", adjunto.Id.ToString(),
            $"{EtiquetasDeTrabajo.TipoDeAdjunto(tipo)}: {adjunto.FileName} " +
            $"({contenido.Length / 1024} KB) en «{titulo}»", ct);

        return (true, $"Documento de {EtiquetasDeTrabajo.TipoDeAdjunto(tipo).ToLowerInvariant()} adjuntado.", adjunto.Id);
    }

    /// <summary>
    /// Quita un documento del requerimiento. Aquí eliminar SÍ borra —a diferencia de cancelar un
    /// requerimiento—: un archivo subido por equivocación no es historia que valga la pena conservar.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int adjuntoId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var adjunto = await db.RequirementAttachments.FirstOrDefaultAsync(a => a.Id == adjuntoId, ct);
        if (adjunto == null) return (false, "Ese documento ya no existe. Actualiza la lista.");

        var nombre = adjunto.FileName;
        db.RequirementAttachments.Remove(adjunto);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "RequirementAttachment", adjuntoId.ToString(),
            $"Documento eliminado del requerimiento #{adjunto.RequirementId}: {nombre}", ct);

        return (true, "Documento eliminado.");
    }

    /// <summary>
    /// El nombre lo eligió quien subió el archivo: se limpia antes de que viaje en la cabecera
    /// <c>Content-Disposition</c> y acabe en el disco de quien lo descarga. La limpieza es la de
    /// <see cref="ArchivosSubidos.NombreSeguro"/>, común a todo lo que se sube.
    /// </summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = ArchivosSubidos.NombreSeguro(nombre);
        return n.Length == 0 ? "documento" : n;
    }
}
