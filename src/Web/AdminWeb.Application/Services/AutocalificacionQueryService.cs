using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Autocalificacion;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que lee la pantalla «Mis actividades»: las autocalificaciones del desarrollador, sus
/// actividades libres y los catálogos de los dos formularios, en una sola respuesta.
///
/// No decide nada de negocio. Registrar, corregir y replicar siguen siendo de
/// <see cref="PerformanceScoringService"/>, y las actividades libres con su evidencia, de
/// <see cref="DevActivityService"/>. Lo que aporta esto es el RECORTE —ningún tipo de EF cruza al
/// navegador— y juntar en un viaje lo que en el escritorio eran consultas locales gratis.
///
/// Vive aparte de esos dos servicios por lo mismo que <see cref="DesempenoQueryService"/>: aquellos
/// son lógica compartida con la pantalla del administrador; esto es la vista de una pantalla
/// concreta y depende de los DTOs del contrato web.
/// </summary>
public class AutocalificacionQueryService(
    AppDbContext db,
    ICurrentUser actual,
    DevActivityService actividades)
{
    /// <summary>
    /// Cuántos meses de entradas ya cerradas se traen.
    ///
    /// Es lo único que cambia respecto al escritorio, y no había alternativa: allí la rejilla traía
    /// TODAS las entradas del desarrollador porque la consulta era local y no costaba nada. Aquí cada
    /// fila viaja por red con su comentario y su historial de revisión, y una lista que solo crece
    /// pesaría más cada mes hasta volver lenta la pantalla que más se usa. Un año cubre el ciclo
    /// completo de evaluación, que es hasta donde alguien mira hacia atrás.
    /// </summary>
    public const int MesesDeHistorial = 12;

    /// <summary>
    /// Todo lo que la pantalla necesita para pintarse.
    ///
    /// Sin ficha de desarrollador se devuelve igualmente, con las listas vacías y
    /// <c>TieneFicha</c> en falso: es lo que hace <see cref="DesempenoQueryService.MiPanelAsync"/> y
    /// es mejor que una pantalla en blanco sin explicación.
    /// </summary>
    public async Task<MisActividadesDto> MisActividadesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);

        var limites = new LimitesDeCapturaDto(
            PerformanceScoringService.MaxMinutosDeclarados,
            PerformanceScoringService.MaxArgumento,
            DevActivityService.MaxEvidenciasPorActividad,
            DevActivityService.MaxEvidenciaBytes);

        if (actual.DeveloperId is not int devId)
            return new MisActividadesDto(false, null, [], [], [], [], limites);

        var nivel = await db.Developers.AsNoTracking()
            .Where(d => d.Id == devId)
            .Select(d => d.Seniority)
            .FirstOrDefaultAsync(ct);

        var entradas = await EntradasAsync(devId, ct);

        return new MisActividadesDto(
            TieneFicha: true,
            Nivel: string.IsNullOrWhiteSpace(nivel) ? null : nivel.Trim(),
            Autocalificaciones: entradas,
            ActividadesLibres: await ActividadesLibresAsync(devId, ct),
            Criterios: await CriteriosAsync(entradas, ct),
            Requerimientos: await RequerimientosAsync(devId, entradas, ct),
            Limites: limites);
    }

    /// <summary>
    /// Evidencia de una actividad libre, sin contenido. La lista y la guarda de pertenencia son de
    /// <see cref="DevActivityService.EvidenciasDeAsync"/>; aquí solo se recorta al contrato web.
    /// </summary>
    public async Task<IReadOnlyList<EvidenciaDto>> EvidenciasDeAsync(int actividadId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);

        return (await actividades.EvidenciasDeAsync(actividadId, ct))
            .Select(e => new EvidenciaDto(
                e.Id, e.FileName, e.ContentType, e.SizeBytes, e.Description, e.CreatedAtUtc))
            .ToList();
    }

    /// <summary>
    /// La captura que respalda una entrada de puntos.
    ///
    /// Sirve a dos usos y por eso devuelve los bytes en vez de una respuesta HTTP: enseñarla, y
    /// conservarla cuando se corrige la entrada sin tocar la imagen. La comprobación de pertenencia
    /// es la misma en los dos casos, que es la razón de que viva aquí y no en cada endpoint.
    ///
    /// Vacío significa «no hay»: quien llama decide si eso es un 404 o «déjala como estaba».
    /// </summary>
    public async Task<(byte[] bytes, string nombre)> CapturaDeAsync(int entryId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);

        var entrada = await db.PointEntries.AsNoTracking()
            .Where(p => p.Id == entryId)
            .Select(p => new { p.DeveloperId, p.Screenshot, p.ScreenshotFileName })
            .FirstOrDefaultAsync(ct);
        if (entrada == null) return ([], "");

        AuthorizationGuard.RequireOwnershipOrAdmin(actual, entrada.DeveloperId);
        if (entrada.Screenshot is not { Length: > 0 }) return ([], "");

        var nombre = ArchivosSubidos.NombreSeguro(entrada.ScreenshotFileName);
        return (entrada.Screenshot, nombre.Length == 0 ? "captura.png" : nombre);
    }

    // ── Piezas ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las entradas del desarrollador dentro de la ventana, más TODAS las que sigan sin aprobarse
    /// aunque sean más viejas: son las únicas sobre las que todavía se puede actuar, y dejar fuera
    /// del historial un rechazo por antiguo sería quitarle al desarrollador la única vía de
    /// replicarlo dentro de la aplicación.
    ///
    /// Se proyecta en vez de traer la entidad porque <see cref="PointEntry.Screenshot"/> es un BLOB:
    /// sin proyección entraría en el SELECT una imagen por fila para acabar mandando solo un
    /// «sí/no».
    /// </summary>
    private async Task<List<AutocalificacionDto>> EntradasAsync(int devId, CancellationToken ct)
    {
        var hoy = DateTime.UtcNow;
        int desde = hoy.Year * 12 + hoy.Month - (MesesDeHistorial - 1);

        var filas = await db.PointEntries.AsNoTracking()
            .Where(p => p.DeveloperId == devId
                     && (p.Year * 12 + p.Month >= desde || p.ApprovalStatus != PointApprovalStatus.Aprobado))
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Date, p.Year, p.Month, p.CriterionId,
                Criterio = p.Criterion.Name,
                DescripcionCriterio = p.Criterion.Description,
                p.Points, p.MinutesSpent, p.EvidenceUrl, p.Comment,
                p.RequirementId,
                Requerimiento = p.Requirement != null ? p.Requirement.Title : null,
                // Solo si la hay: los bytes se piden aparte, al abrirla.
                TieneCaptura = p.Screenshot != null,
                p.ApprovalStatus, p.SubmittedByDeveloperId, p.ReviewRound, p.ReviewComment, p.ReviewHistory
            })
            .ToListAsync(ct);

        return filas.Select(p => new AutocalificacionDto(
            p.Id, p.Date, p.Year, p.Month, p.CriterionId, p.Criterio, p.DescripcionCriterio,
            p.Points, p.MinutesSpent, p.EvidenceUrl, p.Comment,
            p.RequirementId, p.Requerimiento, p.TieneCaptura,
            p.ApprovalStatus,
            EsAutocalificacion: p.SubmittedByDeveloperId != null,
            Vueltas: p.ReviewRound,
            ComentarioDeRevision: p.ReviewComment,
            HistorialDeRevision: p.ReviewHistory))
            .ToList();
    }

    /// <summary>
    /// Las actividades libres con su tiempo y su cuenta de evidencias.
    ///
    /// El tiempo se suma de los TRAMOS en una sola consulta y con
    /// <see cref="WorkSession.LiveSeconds"/>, que es la misma cuenta que hace
    /// <see cref="WorkSessionService"/>. Preguntárselo actividad por actividad —como hacía el
    /// escritorio, donde la base era local— serían veinte viajes a la base para pintar una tabla.
    /// </summary>
    private async Task<List<ActividadLibreDto>> ActividadesLibresAsync(int devId, CancellationToken ct)
    {
        var libres = await actividades.DeDesarrolladorAsync(devId, incluirCerradas: true, ct);
        if (libres.Count == 0) return [];

        var ids = libres.Select(a => a.Id).ToList();
        var evidencias = await actividades.ConteoEvidenciasAsync(ids, ct);

        var ahora = DateTime.UtcNow;
        var segundos = (await db.WorkSessions.AsNoTracking()
                .Where(w => w.ActivityId != null && ids.Contains(w.ActivityId.Value))
                .ToListAsync(ct))
            .GroupBy(w => w.ActivityId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)));

        return libres.Select(a => new ActividadLibreDto(
            a.Id, a.Title, a.Description, a.Status, a.CreatedAt, a.ClosedAt,
            SegundosCronometrados: segundos.GetValueOrDefault(a.Id),
            Evidencias: evidencias.GetValueOrDefault(a.Id)))
            .ToList();
    }

    /// <summary>
    /// Lo que el desarrollador puede registrar: criterios activos, individuales y con puntos
    /// positivos. Los negativos no aparecen porque los descuentos los aplica el líder.
    ///
    /// Se añaden además, marcados como no disponibles, los criterios que ya usa alguna entrada suya
    /// aunque hoy no se puedan elegir. Es una decisión del escritorio: si desaparecieran de la
    /// lista, corregir esa entrada la guardaría con OTRO criterio sin avisar; conservándolos, si
    /// sigue elegido al guardar es el servicio quien lo rechaza y lo explica.
    /// </summary>
    private async Task<List<CriterioDto>> CriteriosAsync(List<AutocalificacionDto> entradas, CancellationToken ct)
    {
        // NO HAY NINGUNO ELEGIBLE, y la lista vacía es la respuesta correcta y no un fallo de la
        // consulta: la autocalificación se retiró y no hay nada nuevo que registrar. La pantalla ya
        // sabe leer esto —esconde el botón cuando no hay elegibles— así que apagar la oferta aquí
        // apaga el formulario entero sin tocar una línea de marcado.
        //
        // Lo que SÍ sigue viajando son los CONSERVADOS: los criterios que ya usa alguna entrada suya.
        // Corregir y replicar siguen vivos mientras la cola pendiente se drena, y sin su criterio esas
        // dos pantallas no sabrían ni cómo se llama lo que están corrigiendo. Antes se conservaban
        // los que se habían caído del catálogo; ahora son todos, que es el mismo mecanismo llevado al
        // extremo — y por eso no hubo que cambiarlo.
        //
        // EL CATÁLOGO NO SE DESACTIVA. Apagar la oferta es una decisión de consulta y se revierte
        // borrando estas líneas; poner IsActive = false sería un cambio de datos que dejaría el
        // histórico ilegible —cada entrada aprobada seguiría apuntando a un criterio retirado— y no
        // se desharía sin volver a tocar la base.
        List<CriterioDto> elegibles = [];

        var yaElegibles = elegibles.Select(c => c.Id).ToHashSet();
        var usados = entradas.Select(e => e.CriterioId).Where(id => !yaElegibles.Contains(id)).Distinct().ToList();

        var conservados = usados.Count == 0
            ? []
            : await db.ScoringCriteria.AsNoTracking()
                .Where(c => usados.Contains(c.Id))
                .Select(c => new CriterioDto(c.Id, c.Name, c.Description, c.DefaultPoints, false))
                .ToListAsync(ct);

        return [.. elegibles.Concat(conservados).OrderBy(c => c.Nombre)];
    }

    /// <summary>
    /// Requerimientos a los que se puede colgar una actividad: los asignados que siguen vivos.
    ///
    /// Mismo criterio que con los criterios: el que ya use una entrada suya se conserva aunque esté
    /// entregado o cancelado, porque en su día era el correcto y corregir la entrada no debería
    /// cambiárselo por sorpresa.
    /// </summary>
    private async Task<List<OpcionDto>> RequerimientosAsync(
        int devId, List<AutocalificacionDto> entradas, CancellationToken ct)
    {
        var vivos = await db.Requirements.AsNoTracking()
            .Where(r => r.Assignments.Any(a => a.DeveloperId == devId)
                     && r.Status != RequirementStatus.Cancelado
                     && r.Status != RequirementStatus.Entregado)
            .Select(r => new { r.Id, r.Title })
            .ToListAsync(ct);

        var yaEstan = vivos.Select(r => r.Id).ToHashSet();
        var usados = entradas.Select(e => e.RequerimientoId)
            .Where(id => id is int r && !yaEstan.Contains(r))
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var conservados = usados.Count == 0
            ? []
            : await db.Requirements.AsNoTracking()
                .Where(r => usados.Contains(r.Id))
                .Select(r => new { r.Id, r.Title })
                .ToListAsync(ct);

        return [.. vivos.Concat(conservados)
            .Select(r => new OpcionDto(r.Id, $"#{r.Id} {r.Title}"))
            .OrderBy(o => o.Texto)];
    }
}
