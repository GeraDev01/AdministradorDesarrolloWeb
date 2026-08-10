using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Desempeno;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La cola de revisión del líder: las autocalificaciones que los desarrolladores registraron y que
/// esperan un sí o un no. Es el port de la pestaña «Pendientes de aprobación» de
/// <c>PerformanceControl</c>.
///
/// <para><b>Por qué es un servicio nuevo y no un método más de <see cref="PerformanceScoringService"/>.</b>
/// Aquel es la fuente única de la PUNTUACIÓN —cuánto vale cada cosa, cómo se suma el ranking— y lo
/// usan también el desarrollador y el pool. Esto es la decisión del líder sobre una entrada ajena:
/// otra autorización (siempre administrador), otro rastro en la bitácora y un aviso al interesado.
/// Mezclarlas dejaría un servicio en el que la mitad de los métodos exigen ser el dueño y la otra
/// mitad exigen no serlo.</para>
///
/// <para><b>La regla que no se negocia:</b> una entrada APROBADA no se vuelve a tocar. Ni para
/// rechazarla, ni para reabrirla, ni para cambiarle el puntaje. Es la misma que aplica
/// <see cref="PerformanceScoringService.EditarAutocalificacionAsync"/> del lado del desarrollador, y
/// vale igual para el líder: cambiar después del visto bueno aquello sobre lo que se dio el visto
/// bueno vaciaría de sentido la aprobación, y esos puntos ya están contados en un ranking que la
/// gente vio.</para>
/// </summary>
public class RevisionDePuntosService(
    AppDbContext db,
    ICurrentUser actual,
    AuditService bitacora,
    NotificationService avisos)
{
    /// <summary>
    /// Tope del motivo del rechazo. Da para explicar qué falta, que es para lo que sirve.
    /// </summary>
    public const int MaxMotivo = 1000;

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Todo lo que espera revisión, de lo más antiguo a lo más reciente: quien lleva más tiempo
    /// esperando una respuesta va primero, igual que en el escritorio.
    ///
    /// La captura NO viaja aquí. Se sabe si la hay y los bytes se piden aparte al abrirla: con una
    /// imagen por entrada, esta lista serían varios megabytes en cada recarga.
    /// </summary>
    public async Task<IReadOnlyList<PuntoPendienteDto>> PendientesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var filas = await db.PointEntries.AsNoTracking()
            .Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.DeveloperId,
                Desarrollador = p.Developer.FullName,
                Criterio = p.Criterion.Name,
                DescripcionCriterio = p.Criterion.Description,
                p.Points, p.Year, p.Month, p.Date,
                p.RequirementId,
                Requerimiento = p.Requirement != null ? p.Requirement.Title : null,
                p.ReviewRound, p.MinutesSpent, p.EvidenceUrl, p.Comment, p.ReviewHistory,
                TieneCaptura = p.Screenshot != null
            })
            .ToListAsync(ct);

        return filas.Select(p => new PuntoPendienteDto(
            p.Id, p.DeveloperId, p.Desarrollador, p.Criterio, p.DescripcionCriterio,
            p.Points, p.Year, p.Month, Periodo(p.Year, p.Month), p.Date,
            p.RequirementId, p.Requerimiento,
            Vueltas: p.ReviewRound,
            MinutosDeclarados: p.MinutesSpent,
            TiempoDeclarado: FormatoMinutos(p.MinutesSpent ?? 0),
            TieneCaptura: p.TieneCaptura,
            Enlace: p.EvidenceUrl,
            Comentario: p.Comment,
            HistorialDeRevision: p.ReviewHistory))
            .ToList();
    }

    // ── Escritura ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aprueba las autocalificaciones indicadas: sus puntos empiezan a contar en el ranking del mes.
    ///
    /// Se admiten varias de una vez porque revisar es un trabajo por tandas —así lo hacía la rejilla
    /// del escritorio, con selección múltiple—, pero cada entrada se comprueba por separado: las que
    /// ya no estén pendientes se saltan y se dice cuántas fueron. Rechazar la tanda entera porque una
    /// cambió de estado mientras se miraba obligaría a repetir el trabajo ya hecho.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AprobarAsync(
        IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var (entradas, error) = await SeleccionarPendientesAsync(ids, ct);
        if (error != null) return (false, error);

        var ahora = DateTime.UtcNow;
        foreach (var entrada in entradas)
        {
            entrada.ApprovalStatus = PointApprovalStatus.Aprobado;
            entrada.ReviewedByUserId = actual.UserId;
            entrada.ReviewedAt = ahora;

            // Si venía de una réplica, que quede escrito cómo terminó la discusión: el historial es
            // lo único que conserva las dos mitades de la conversación.
            if (entrada.ReviewRound > 0)
                PerformanceScoringService.AnotarEnHistorial(entrada,
                    $"Aprobada por {actual.Username ?? "el líder"} tras la réplica.");
        }

        await db.SaveChangesAsync(ct);

        int puntos = entradas.Sum(e => e.Points);
        await bitacora.RecordAsync(AuditAction.Update, "PointEntry",
            string.Join(",", entradas.Select(e => e.Id)),
            $"{entradas.Count} autocalificación(es) aprobada(s) (+{puntos} pts)", ct);

        await AvisarAsync(entradas.Select(e => (e.DeveloperId, e.Id, e.Points)).ToList(),
            aprobadas: true, motivo: null, ct);

        return (true,
            $"{entradas.Count} actividad(es) aprobada(s) (+{puntos} pts). Ya cuentan en el ranking del mes."
            + Omitidas(ids.Count, entradas.Count));
    }

    /// <summary>
    /// Rechaza las autocalificaciones indicadas con un motivo.
    ///
    /// <para><b>El motivo es obligatorio, y aquí cambia respecto al escritorio</b>, donde el cuadro de
    /// diálogo lo pedía «(opcional)» y aceptaba el vacío. Un rechazo sin motivo deja al desarrollador
    /// con una actividad devuelta y nada que corregir, así que o vuelve a mandar lo mismo o pregunta
    /// por fuera de la aplicación — y en los dos casos el trabajo se repite. Es además la regla que ya
    /// aplica el pool al devolver una entrega (<c>PoolActivityService.RechazarAsync</c>): dos
    /// pantallas que hacen lo mismo no pueden exigir cosas distintas.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> RechazarAsync(
        IReadOnlyList<int> ids, string? motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0)
            return (false, "Escribe por qué se rechaza: es lo que la persona va a leer para corregirlo.");
        if (motivo.Length > MaxMotivo)
            return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var (entradas, error) = await SeleccionarPendientesAsync(ids, ct);
        if (error != null) return (false, error);

        var ahora = DateTime.UtcNow;
        foreach (var entrada in entradas)
        {
            entrada.ApprovalStatus = PointApprovalStatus.Rechazado;
            entrada.ReviewedByUserId = actual.UserId;
            entrada.ReviewedAt = ahora;
            entrada.ReviewComment = motivo;

            // Solo a partir de la primera réplica: en un rechazo normal el historial estaría de más,
            // porque el motivo ya se ve en su propio campo. Desde que hay discusión, en cambio, hace
            // falta que las dos mitades queden juntas y en orden.
            if (entrada.ReviewRound > 0)
                PerformanceScoringService.AnotarEnHistorial(entrada,
                    $"Rechazada de nuevo por {actual.Username ?? "el líder"}: {motivo}");
        }

        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Update, "PointEntry",
            string.Join(",", entradas.Select(e => e.Id)),
            $"{entradas.Count} autocalificación(es) rechazada(s)", ct);

        await AvisarAsync(entradas.Select(e => (e.DeveloperId, e.Id, e.Points)).ToList(),
            aprobadas: false, motivo: motivo, ct);

        return (true,
            $"{entradas.Count} actividad(es) rechazada(s). Quien las registró ya tiene el motivo y puede replicar."
            + Omitidas(ids.Count, entradas.Count));
    }

    /// <summary>
    /// Corrige el puntaje de una autocalificación que sigue pendiente. La entrada NO se aprueba: sigue
    /// esperando decisión.
    ///
    /// Es la contraparte necesaria de que el desarrollador no fije su propio puntaje —lo pone el
    /// criterio y solo el líder lo ajusta—, y sigue siendo lo único que puede cambiarlo. Se conserva
    /// tal cual del escritorio, incluido que admita valores negativos: hay actividades que al
    /// revisarlas resultan ser un descuento.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AjustarPuntosAsync(
        int entryId, int puntos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(actual);

        var entrada = await db.PointEntries.Include(p => p.Criterion)
            .FirstOrDefaultAsync(p => p.Id == entryId, ct);
        if (entrada == null) return (false, "La actividad ya no existe. Actualiza la lista.");

        if (entrada.ApprovalStatus != PointApprovalStatus.Pendiente)
            return (false, entrada.ApprovalStatus == PointApprovalStatus.Aprobado
                ? "Esta actividad ya fue aprobada y su puntaje no se puede cambiar."
                : "Esta actividad ya fue rechazada; no hay puntaje que ajustar.");

        int anteriores = entrada.Points;
        if (puntos == anteriores)
            return (false, $"Ya vale {anteriores} pts: no hay nada que cambiar.");

        entrada.Points = puntos;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Update, "PointEntry", entrada.Id.ToString(),
            $"Puntos ajustados por el líder: {anteriores} → {puntos} ({entrada.Criterion.Name})", ct);

        return (true,
            $"Puntaje ajustado de {anteriores} a {puntos}. La actividad sigue pendiente de aprobación.");
    }

    /// <summary>Cuántas esperan revisión. Es el número del distintivo de la pantalla.</summary>
    public Task<int> CuentaPendientesAsync(CancellationToken ct = default) =>
        actual.IsAdmin
            ? db.PointEntries.CountAsync(p => p.ApprovalStatus == PointApprovalStatus.Pendiente, ct)
            : Task.FromResult(0);

    // ── Piezas ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las entradas de la selección que TODAVÍA están pendientes, rastreadas para poder modificarlas.
    ///
    /// Filtrar por estado en la consulta —y no comprobarlo después— es lo que impide que un id
    /// cualquiera mandado a mano resuelva una entrada ya aprobada: la que no está pendiente
    /// sencillamente no se carga.
    /// </summary>
    private async Task<(List<PointEntry> entradas, string? error)> SeleccionarPendientesAsync(
        IReadOnlyList<int> ids, CancellationToken ct)
    {
        var unicos = (ids ?? []).Where(id => id > 0).Distinct().ToList();
        if (unicos.Count == 0)
            return ([], "Selecciona al menos una actividad pendiente.");

        var entradas = await db.PointEntries
            .Where(p => unicos.Contains(p.Id) && p.ApprovalStatus == PointApprovalStatus.Pendiente)
            .ToListAsync(ct);

        if (entradas.Count == 0)
            return ([], "Ninguna de las actividades elegidas sigue pendiente: alguien las revisó antes. " +
                        "Actualiza la lista.");

        return (entradas, null);
    }

    /// <summary>La coletilla que explica por qué se resolvieron menos de las que se eligieron.</summary>
    private static string Omitidas(int pedidas, int resueltas) =>
        pedidas > resueltas
            ? $" Se omitieron {pedidas - resueltas}: ya no estaban pendientes."
            : "";

    /// <summary>
    /// Avisa a cada desarrollador afectado, un aviso por persona con el resumen de su tanda.
    ///
    /// <para><b>Cambia el medio respecto al escritorio</b>, no la intención: allí se mandaba un correo
    /// con <c>EmailService</c> y solo si el correo estaba configurado, de modo que en la práctica
    /// muchas veces no llegaba nada. Aquí es un aviso dentro de la aplicación, que es donde la persona
    /// va a corregirlo, y no depende de que nadie haya configurado un servidor de correo.</para>
    ///
    /// <para>Es «lo mejor que se pueda»: si un desarrollador no tiene cuenta activa no hay a quién
    /// avisar, y eso no debe deshacer una aprobación que ya está guardada.</para>
    /// </summary>
    private async Task AvisarAsync(
        List<(int DeveloperId, int EntryId, int Points)> resueltas, bool aprobadas, string? motivo,
        CancellationToken ct)
    {
        foreach (var grupo in resueltas.GroupBy(x => x.DeveloperId))
        {
            int cuantas = grupo.Count();
            int puntos = grupo.Sum(x => x.Points);

            var titulo = aprobadas
                ? "Tus puntos de desempeño fueron aprobados"
                : "Tus puntos de desempeño fueron revisados";

            var cuerpo = aprobadas
                ? $"Se aprobaron {cuantas} de tus registros (+{puntos} pts en total). Ya cuentan en tu ranking."
                : $"Se rechazaron {cuantas} de tus registros. Motivo: {motivo}. " +
                  "Puedes corregirlos y replicar desde «Mis Actividades».";

            try
            {
                // La clave incluye las entradas resueltas: si la misma persona recibe dos tandas
                // seguidas, la segunda no se confunde con la primera y no se pierde.
                var clave = $"puntos-{(aprobadas ? "aprobados" : "rechazados")}-" +
                            string.Join("-", grupo.Select(x => x.EntryId).Order());

                await avisos.NotifyDeveloperAsync(grupo.Key, NotificationKind.General, titulo, cuerpo,
                    url: "mis-actividades", dedupeKey: clave, ct: ct);
            }
            catch
            {
                // Que no se pueda avisar no deshace la decisión: ya está guardada y el desarrollador
                // la verá igual en su pantalla.
            }
        }
    }

    private static readonly string[] Meses =
        ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
         "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    /// <summary>«Julio 2026». Un mes fuera de rango se enseña crudo en vez de reventar.</summary>
    private static string Periodo(int anio, int mes) =>
        mes is >= 1 and <= 12 ? $"{Meses[mes - 1]} {anio}" : $"{mes}/{anio}";

    /// <summary>Minutos declarados como «3h 20m» / «45m» / «—», igual que en el escritorio.</summary>
    private static string FormatoMinutos(int minutos) =>
        minutos <= 0 ? "—"
        : minutos >= 60 ? $"{minutos / 60}h {minutos % 60:00}m"
        : $"{minutos}m";
}
