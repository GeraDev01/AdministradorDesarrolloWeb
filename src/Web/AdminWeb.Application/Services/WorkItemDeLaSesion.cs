using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// A qué work item de Azure DevOps corresponde lo que se está cronometrando, si es que corresponde a
/// alguno.
///
/// <para>Vive aparte —y no dentro de quien publica— porque es la pieza que decide <b>en qué ticket
/// de un cliente se escribe</b>. Equivocarse aquí no es un error de formato: es un comentario en el
/// ticket de otro. Separado, se puede probar sin red y sin credenciales.</para>
///
/// <para><b>Y comprueba de quién es lo que se cronometra</b>, aunque suene a que eso ya lo hizo
/// alguien. El cronómetro acepta hoy arrancar sobre un requerimiento que no está asignado a quien lo
/// arranca —comportamiento viejo, con quince pruebas encima y pantallas que dependen de él—, así que
/// la pertenencia se comprueba en el único sitio donde todavía se puede cortar sin cambiar lo que ya
/// funciona: justo antes de que ese trabajo salga de la organización. Medir de más es un problema de
/// contabilidad interna; escribir de más es un problema del cliente.</para>
/// </summary>
public static class WorkItemDeLaSesion
{
    /// <summary>
    /// El número de work item al que avisar por esta sesión, o nulo si no hay a quién avisar.
    ///
    /// <para>Devuelve nulo, y no lanza, en todos los casos legítimos: una actividad libre de verdad
    /// (soporte, juntas) no tiene ticket; un requerimiento que no vino de DevOps tampoco; y una
    /// actividad del pool cuyo reclamo se soltó ya no tiene percha. Ninguno de ellos es un fallo del
    /// que haya que dejar constancia.</para>
    /// </summary>
    public static async Task<int?> ResolverAsync(
        AppDbContext db, int workSessionId, CancellationToken ct = default)
    {
        var sesion = await db.WorkSessions.AsNoTracking()
            .Where(w => w.Id == workSessionId)
            .Select(w => new { w.DeveloperId, w.RequirementId, w.ActivityId })
            .FirstOrDefaultAsync(ct);

        if (sesion is null) return null;

        if (sesion.RequirementId is int requerimientoId)
            return await DeRequerimientoAsync(db, requerimientoId, sesion.DeveloperId, ct);

        if (sesion.ActivityId is int actividadId)
            return await DePerchaDelPoolAsync(db, actividadId, sesion.DeveloperId, ct);

        return null;
    }

    /// <summary>
    /// El work item de un requerimiento, con la MISMA regla que usa el reporte de tiempo al detener
    /// (<see cref="DevOpsTimeReport.AplicaA"/>): tiene que venir de Azure DevOps y su identificador
    /// externo tiene que ser un número. Reutilizarla, y no escribir otra parecida, es lo que impide
    /// que un día el aviso de inicio y el reporte de tiempo acaben en tickets distintos.
    /// </summary>
    private static async Task<int?> DeRequerimientoAsync(
        AppDbContext db, int requerimientoId, int developerId, CancellationToken ct)
    {
        var req = await db.Requirements.AsNoTracking()
            .Where(r => r.Id == requerimientoId)
            .Select(r => new { r.Source, r.ExternalId })
            .FirstOrDefaultAsync(ct);

        if (req is null || !DevOpsTimeReport.AplicaA(req.Source, req.ExternalId, out int numero))
            return null;

        // La pertenencia. Un requerimiento no tiene dueño en su propia fila: lo dice su asignación.
        bool suyo = await db.Assignments.AsNoTracking()
            .AnyAsync(a => a.RequirementId == requerimientoId && a.DeveloperId == developerId, ct);

        return suyo ? numero : null;
    }

    /// <summary>
    /// El work item de una actividad del pool, llegando por la percha del cronómetro.
    ///
    /// <para>La percha es una <c>DevActivity</c> que el pool crea al tomar la actividad, y el enlace
    /// vive del lado del pool (<c>PoolActivity.LinkedDevActivityId</c>) porque no hay clave ajena:
    /// la limpieza de datos borra <c>DevActivities</c> enteras y una clave ajena arrastraría con
    /// ellas actividades del pool que no tienen nada que ver.</para>
    ///
    /// <para>De ahí salen las dos cautelas. <b>Se ordena por identificador descendente</b> porque sin
    /// clave ajena nada impide que dos filas apunten a la misma percha —un identificador reutilizado
    /// tras una limpieza—, y en ese caso hay que quedarse con la última, no con la que salga.
    /// <b>Y se exige un número mayor que cero</b> porque la columna es anulable y cero no es un work
    /// item: comentar en el «#0» es un 404 con suerte, y con mala suerte el ticket de otro.</para>
    /// </summary>
    private static async Task<int?> DePerchaDelPoolAsync(
        AppDbContext db, int actividadId, int developerId, CancellationToken ct)
    {
        // La percha tiene que ser de quien cronometra. La guarda del cronómetro ya lo comprueba al
        // arrancar, pero esta ruta también la recorre el barrido sobre sesiones que arrancaron en la
        // aplicación de escritorio, que no pasa por esa guarda.
        bool suya = await db.DevActivities.AsNoTracking()
            .AnyAsync(a => a.Id == actividadId && a.DeveloperId == developerId, ct);

        if (!suya) return null;

        var numero = await db.PoolActivities.AsNoTracking()
            .Where(p => p.LinkedDevActivityId == actividadId)
            .OrderByDescending(p => p.Id)
            .Select(p => p.DevOpsWorkItemId)
            .FirstOrDefaultAsync(ct);

        return numero is int n && n > 0 ? n : null;
    }
}
