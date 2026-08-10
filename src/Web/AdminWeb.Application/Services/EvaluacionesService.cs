using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Evaluaciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las evaluaciones de líder (fortalezas, debilidades, calificación) y los hitos de cada
/// desarrollador. Es el port de <c>DeveloperReportsControl</c> —donde el líder las escribe— y de
/// <c>MyEvaluationsControl</c> —donde el desarrollador las lee.
///
/// <para><b>Escribir es del líder; leer lo propio, de cada quien.</b> Las dos mitades viven en el
/// mismo servicio porque comparten el modelo, pero cada método declara su guarda: las de escritura
/// exigen administrador y <see cref="MisAsync"/> no recibe identificador de desarrollador, lo
/// resuelve de la sesión. Un id por parámetro convertiría «mis evaluaciones» en «las de cualquiera»
/// en cuanto alguien probara otro número, y aquí van las debilidades que le escribieron a una
/// persona.</para>
///
/// <para>El PDF NO se arma aquí: lo reúne <see cref="FichaDeDesarrolladorQueryService"/> y lo maqueta
/// el generador de documentos. Este servicio es el CRUD.</para>
/// </summary>
public class EvaluacionesService(AppDbContext db, ICurrentUser actual, AuditService bitacora)
{
    /// <summary>
    /// Escala de la calificación global. Es la de la ficha del escritorio, que se pinta como cinco
    /// estrellas; el tope vive aquí para que el control de la pantalla se construya con él y no
    /// pueda ofrecer un valor que el servidor va a rechazar.
    /// </summary>
    public const int MaxCalificacion = 5;

    /// <summary>Tope de los campos largos. Da para una evaluación seria, no para un expediente.</summary>
    public const int MaxTextoLargo = 4000;

    /// <summary>Tope del título de un hito y de la etiqueta de periodo.</summary>
    public const int MaxTextoCorto = 200;

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>Los desarrolladores activos, para el selector de la pantalla del líder.</summary>
    public async Task<IReadOnlyList<OpcionDto>> DesarrolladoresAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        return await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new OpcionDto(d.Id, d.FullName))
            .ToListAsync(ct);
    }

    /// <summary>Evaluaciones e hitos de un desarrollador, para la pantalla del líder.</summary>
    public async Task<EvaluacionesDeDesarrolladorDto?> DeDesarrolladorAsync(
        int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var nombre = await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId).Select(d => d.FullName)
            .FirstOrDefaultAsync(ct);
        if (nombre == null) return null;

        return new EvaluacionesDeDesarrolladorDto(
            developerId, nombre,
            await EvaluacionesDeAsync(developerId, ct),
            await HitosDeAsync(developerId, ct));
    }

    /// <summary>
    /// Lo que ve el desarrollador de sí mismo. Sin ficha ligada se devuelve igual, vacío y diciéndolo
    /// —es lo que hace <see cref="DesempenoQueryService.MiPanelAsync"/>— en vez de un error.
    /// </summary>
    public async Task<MisEvaluacionesDto> MisAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);

        if (actual.DeveloperId is not int devId)
            return new MisEvaluacionesDto(false, actual.FullName ?? "", [], []);

        var nombre = await db.Developers.AsNoTracking()
            .Where(d => d.Id == devId).Select(d => d.FullName)
            .FirstOrDefaultAsync(ct) ?? actual.FullName ?? "";

        return new MisEvaluacionesDto(
            true, nombre,
            await EvaluacionesDeAsync(devId, ct),
            await HitosDeAsync(devId, ct));
    }

    // ── Escritura: evaluaciones ──────────────────────────────────────────────────

    /// <summary>
    /// Alta o edición de una evaluación.
    ///
    /// Al EDITAR no se toca quién evaluó: el nombre que quedó grabado es el de quien hizo esa
    /// valoración y sustituirlo por el de quien corrige una errata haría que la evaluación pareciera
    /// suya. Es lo que hacía el escritorio, donde el formulario de edición no ofrecía ese campo.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarEvaluacionAsync(
        GuardarEvaluacionRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var (valido, error) = await ValidarEvaluacionAsync(peticion, ct);
        if (!valido) return (false, error);

        bool esAlta = peticion.Id is null or 0;
        DeveloperEvaluation evaluacion;

        if (esAlta)
        {
            evaluacion = new DeveloperEvaluation
            {
                DeveloperId = peticion.DesarrolladorId,
                EvaluatorUserId = actual.UserId,
                EvaluatorName = actual.FullName ?? actual.Username,
                CreatedAt = DateTime.UtcNow
            };
            db.DeveloperEvaluations.Add(evaluacion);
        }
        else
        {
            var existente = await db.DeveloperEvaluations.FirstOrDefaultAsync(e => e.Id == peticion.Id, ct);
            if (existente == null) return (false, "La evaluación ya no existe. Actualiza la lista.");

            // La evaluación no cambia de dueño desde el formulario: se conserva el desarrollador con
            // el que se guardó. Moverla de persona por un descuido del selector sería escribirle a
            // alguien las debilidades de otro.
            evaluacion = existente;
        }

        evaluacion.EvaluationDate = peticion.Fecha.Date;
        evaluacion.PeriodLabel = Limpiar(peticion.Periodo);
        evaluacion.OverallRating = peticion.Calificacion;
        evaluacion.Strengths = Limpiar(peticion.Fortalezas);
        evaluacion.Weaknesses = Limpiar(peticion.Debilidades);
        evaluacion.Comments = Limpiar(peticion.Comentarios);

        await db.SaveChangesAsync(ct);
        await bitacora.RecordAsync(
            esAlta ? AuditAction.Create : AuditAction.Update,
            "DeveloperEvaluation", evaluacion.Id.ToString(),
            // El TEXTO de la evaluación no se anota: es una valoración de una persona y la bitácora
            // la lee más gente de la que debería leer eso. Basta con que conste el hecho.
            $"Evaluación de {await NombreDeAsync(evaluacion.DeveloperId, ct)}", ct);

        return (true, esAlta ? "Evaluación registrada." : "Evaluación actualizada.");
    }

    /// <summary>Borra una evaluación.</summary>
    public async Task<(bool ok, string mensaje)> EliminarEvaluacionAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var evaluacion = await db.DeveloperEvaluations.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (evaluacion == null) return (false, "La evaluación ya no existe. Actualiza la lista.");

        db.DeveloperEvaluations.Remove(evaluacion);
        await db.SaveChangesAsync(ct);
        await bitacora.RecordAsync(AuditAction.Delete, "DeveloperEvaluation", id.ToString(),
            $"Evaluación de {await NombreDeAsync(evaluacion.DeveloperId, ct)}", ct);

        return (true, "Evaluación eliminada.");
    }

    // ── Escritura: hitos ─────────────────────────────────────────────────────────

    /// <summary>Alta o edición de un hito.</summary>
    public async Task<(bool ok, string mensaje)> GuardarHitoAsync(
        GuardarHitoRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var titulo = Limpiar(peticion.Titulo);
        if (titulo == null) return (false, "Escribe el título del hito: es lo que se lee en la ficha.");
        if (titulo.Length > MaxTextoCorto)
            return (false, $"El título no puede pasar de {MaxTextoCorto} caracteres.");
        if (Limpiar(peticion.Descripcion) is { Length: > MaxTextoLargo })
            return (false, $"La descripción no puede pasar de {MaxTextoLargo} caracteres.");
        if (!await db.Developers.AnyAsync(d => d.Id == peticion.DesarrolladorId, ct))
            return (false, "El desarrollador no existe.");

        bool esAlta = peticion.Id is null or 0;
        DeveloperMilestone hito;

        if (esAlta)
        {
            hito = new DeveloperMilestone
            {
                DeveloperId = peticion.DesarrolladorId,
                CreatedByUserId = actual.UserId,
                CreatedAt = DateTime.UtcNow
            };
            db.DeveloperMilestones.Add(hito);
        }
        else
        {
            var existente = await db.DeveloperMilestones.FirstOrDefaultAsync(m => m.Id == peticion.Id, ct);
            if (existente == null) return (false, "El hito ya no existe. Actualiza la lista.");
            hito = existente;
        }

        hito.Date = peticion.Fecha.Date;
        hito.Kind = peticion.Tipo;
        hito.Title = titulo;
        hito.Description = Limpiar(peticion.Descripcion);

        await db.SaveChangesAsync(ct);
        await bitacora.RecordAsync(
            esAlta ? AuditAction.Create : AuditAction.Update,
            "DeveloperMilestone", hito.Id.ToString(),
            $"Hito de {await NombreDeAsync(hito.DeveloperId, ct)}: {hito.Title}", ct);

        return (true, esAlta ? "Hito registrado." : "Hito actualizado.");
    }

    /// <summary>Borra un hito.</summary>
    public async Task<(bool ok, string mensaje)> EliminarHitoAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var hito = await db.DeveloperMilestones.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (hito == null) return (false, "El hito ya no existe. Actualiza la lista.");

        db.DeveloperMilestones.Remove(hito);
        await db.SaveChangesAsync(ct);
        await bitacora.RecordAsync(AuditAction.Delete, "DeveloperMilestone", id.ToString(),
            $"Hito de {await NombreDeAsync(hito.DeveloperId, ct)}: {hito.Title}", ct);

        return (true, "Hito eliminado.");
    }

    // ── Piezas ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las evaluaciones de un desarrollador, de la más reciente a la más antigua. El desempate por
    /// Id importa: varias del mismo día se capturaron en un orden y ese es el que se lee.
    /// </summary>
    private async Task<List<EvaluacionDto>> EvaluacionesDeAsync(int developerId, CancellationToken ct) =>
        await db.DeveloperEvaluations.AsNoTracking()
            .Where(e => e.DeveloperId == developerId)
            .OrderByDescending(e => e.EvaluationDate).ThenByDescending(e => e.Id)
            .Select(e => new EvaluacionDto(
                e.Id, e.EvaluationDate, e.PeriodLabel, e.OverallRating,
                e.Strengths, e.Weaknesses, e.Comments, e.EvaluatorName))
            .ToListAsync(ct);

    private async Task<List<HitoDto>> HitosDeAsync(int developerId, CancellationToken ct)
    {
        var filas = await db.DeveloperMilestones.AsNoTracking()
            .Where(m => m.DeveloperId == developerId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Select(m => new { m.Id, m.Date, m.Kind, m.Title, m.Description })
            .ToListAsync(ct);

        return filas
            .Select(m => new HitoDto(m.Id, m.Date, m.Kind,
                EtiquetasDeEvaluacion.TipoDeHito(m.Kind), m.Title, m.Description))
            .ToList();
    }

    /// <summary>Reglas comunes al alta y a la edición de una evaluación.</summary>
    private async Task<(bool ok, string error)> ValidarEvaluacionAsync(
        GuardarEvaluacionRequest p, CancellationToken ct)
    {
        if (p.Calificacion is int c && (c < 1 || c > MaxCalificacion))
            return (false, $"La calificación va de 1 a {MaxCalificacion}, o se deja sin calificar.");

        foreach (var texto in new[] { p.Fortalezas, p.Debilidades, p.Comentarios })
            if (Limpiar(texto) is { Length: > MaxTextoLargo })
                return (false, $"Fortalezas, debilidades y comentarios no pueden pasar de {MaxTextoLargo} caracteres.");

        if (Limpiar(p.Periodo) is { Length: > MaxTextoCorto })
            return (false, $"La etiqueta de periodo no puede pasar de {MaxTextoCorto} caracteres.");

        // Al alta hace falta que el desarrollador exista; al editar el dueño no se toca, pero se
        // comprueba igual porque una ficha pudo borrarse entre que se abrió el formulario y se guardó.
        if (!await db.Developers.AnyAsync(d => d.Id == p.DesarrolladorId, ct))
            return (false, "El desarrollador no existe.");

        return (true, "");
    }

    private async Task<string> NombreDeAsync(int developerId, CancellationToken ct) =>
        await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId).Select(d => d.FullName)
            .FirstOrDefaultAsync(ct) ?? $"#{developerId}";

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
