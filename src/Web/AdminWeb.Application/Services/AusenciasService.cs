using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Ausencias;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma de una sola vez lo que enseñan «Mis vacaciones» y «Mis permisos» al desarrollador que tiene
/// la sesión: el saldo del año, los indicadores y las dos listas.
///
/// En el escritorio cada una de esas pantallas consultaba la base por su cuenta, tres o cuatro veces
/// seguidas; allí era gratis porque la base estaba al lado. Aquí cada consulta es un viaje de red y
/// una pantalla que se pinta en cuatro tandas parpadea.
///
/// <para>Las REGLAS no viven aquí: cancelar, eliminar y resolver siguen siendo de
/// <see cref="VacationRequestService"/> y <see cref="LeaveRequestService"/>, que son quienes deciden
/// y quienes dejan rastro. Esto solo los junta para pintar, y añade lo único que ninguno de los dos
/// trae: el alta de una solicitud de vacaciones (ver
/// <see cref="SolicitarVacacionesAsync"/>).</para>
/// </summary>
public class AusenciasService(
    AppDbContext db,
    ICurrentUser usuarioActual,
    LeaveRequestService permisos,
    AuditService auditoria)
{
    // ── Vacaciones ───────────────────────────────────────────────────────────

    /// <summary>El saldo del año y las solicitudes propias, ordenadas de la más reciente hacia atrás.</summary>
    public async Task<MisVacacionesDto> MisVacacionesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        // Sin ficha no hay vacaciones que contar. Se responde vacío en vez de fallar porque a esta
        // pantalla también llega el administrador, cuya cuenta no siempre tiene ficha.
        if (usuarioActual.DeveloperId is not int devId)
            return new MisVacacionesDto(
                new SaldoDeVacacionesDto(false, 0, 0, 0, null), [], ArchivosSubidos.MaxBytes);

        // Proyección deliberadamente ligera: NO trae los BLOB del respaldo, solo si lo hay. Es la
        // decisión que el escritorio ya tomó en su rejilla, y aquí pesa más: esos bytes cruzarían la
        // red hacia el navegador en cada carga sin que nadie los abra.
        var filas = await db.VacationRequests.AsNoTracking()
            .Where(v => v.DeveloperId == devId)
            .OrderByDescending(v => v.StartDate).ThenByDescending(v => v.Id)
            .Select(v => new
            {
                v.Id,
                v.StartDate,
                v.EndDate,
                v.Status,
                v.Comment,
                v.ReviewComment,
                TieneRespaldo = v.AttachmentFileName != null
            })
            .ToListAsync(ct);

        // Cuántos documentos generados cuelgan de cada solicitud: es lo que se pierde al eliminarla,
        // y hay que poder avisarlo antes de que la persona confirme.
        //
        // La FILA DE LA FIRMA del colaborador queda fuera del conteo, y ésa es toda la corrección:
        // vive en esta misma tabla —VacationDocuments es también donde se guarda el enlace a la firma,
        // ver VacationRequestService— pero no es un documento que nadie haya generado. Contándola, la
        // confirmación de borrado avisaba de un papel de más en cuanto la persona firmaba su
        // solicitud. Se filtra AQUÍ, en la consulta, y no restando uno después: restar uno acierta
        // por casualidad mientras solo haya una fila rara, y deja de acertar el día que haya dos.
        var documentos = (await db.VacationDocuments.AsNoTracking()
            .Where(d => d.VacationRequest.DeveloperId == devId
                     && d.FileName != VacationRequestService.MarcaDeLaFirmaDelColaborador)
            .GroupBy(d => d.VacationRequestId)
            .Select(g => new { Solicitud = g.Key, Cuantos = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Solicitud, x => x.Cuantos);

        var solicitudes = filas
            .Select(v => new SolicitudDeVacacionesDto(
                v.Id,
                v.StartDate,
                v.EndDate,
                Dias(v.StartDate, v.EndDate),
                v.Status,
                Etiqueta(v.Status),
                v.Comment,
                v.ReviewComment,
                v.TieneRespaldo,
                documentos.GetValueOrDefault(v.Id),
                VacationRequestService.PuedeCancelar(v.Status),
                VacationRequestService.PuedeAdjuntar(v.Status),
                VacationRequestService.PuedeEliminar(v.Status)))
            .ToList();

        var ficha = await db.Developers.AsNoTracking()
            .Where(d => d.Id == devId)
            .Select(d => new { d.VacationDaysLeft, d.HireDate })
            .FirstOrDefaultAsync(ct);

        int anio = DateTime.Today.Year;

        // El descuento es automático y solo cuenta lo APROBADO de este año, que es el criterio del
        // escritorio. Los pendientes se suman de todos los años a propósito: una solicitud vieja sin
        // responder sigue siendo tiempo comprometido y esconderla haría creer que hay más margen.
        int tomados = filas.Where(v => v.Status == VacationStatus.Aprobada && v.StartDate.Year == anio)
                           .Sum(v => Dias(v.StartDate, v.EndDate));
        int pendientes = filas.Where(v => v.Status == VacationStatus.Pendiente)
                              .Sum(v => Dias(v.StartDate, v.EndDate));

        var saldo = new SaldoDeVacacionesDto(
            TieneFicha: true,
            Asignados: ficha?.VacationDaysLeft ?? 0,
            TomadosEsteAnio: tomados,
            PendientesDeAprobacion: pendientes,
            FechaDeIngreso: ficha?.HireDate);

        // El tope del archivo es el de ArchivosSubidos y no una constante propia, por lo mismo que en
        // los permisos: es ArchivosSubidos quien valida la subida, así que el formulario tiene que
        // enseñar el número que de verdad va a aplicarse.
        return new MisVacacionesDto(saldo, solicitudes, ArchivosSubidos.MaxBytes);
    }

    /// <summary>
    /// El desarrollador pide vacaciones para sí mismo. Nace Pendiente: nadie se autoriza solo.
    /// </summary>
    /// <remarks>
    /// El alta no está en <see cref="VacationRequestService"/> y no es un olvido: ese servicio se
    /// portó tal cual del escritorio, donde crear la solicitud lo hacía la propia pantalla
    /// escribiendo contra la base local. Aquí la pantalla es un navegador y no puede escribir, así
    /// que el alta baja al servidor. Queda en un servicio y no en el endpoint para que la validación
    /// y el rastro vivan donde viven las demás reglas.
    /// </remarks>
    public async Task<(bool ok, string mensaje, int id)> SolicitarVacacionesAsync(
        DateTime inicio, DateTime fin, string? comentario, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        if (usuarioActual.DeveloperId is not int devId)
            return (false, "Tu cuenta no tiene ficha de desarrollador, así que no puede pedir vacaciones.", 0);

        if (fin.Date < inicio.Date)
            return (false, "La fecha de fin debe ser posterior o igual a la de inicio.", 0);

        // El saldo NO bloquea, igual que en el escritorio: se enseña para que la persona decida, pero
        // quien concede o niega es el líder, y hay casos que la ficha no conoce (días arrastrados del
        // año anterior, acuerdos particulares). Bloquear aquí convertiría un dato informativo en un
        // veto que nadie decidió dar.
        var solicitud = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = inicio.Date,
            EndDate = fin.Date,
            Status = VacationStatus.Pendiente,
            Comment = Limpiar(comentario),
            CreatedAt = DateTime.UtcNow
        };

        db.VacationRequests.Add(solicitud);
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Create, "VacationRequest", solicitud.Id.ToString(),
            $"Solicitud de vacaciones: {solicitud.StartDate:dd/MM/yyyy} — {solicitud.EndDate:dd/MM/yyyy}", ct);

        return (true, "Solicitud enviada. Queda pendiente de que el líder la resuelva.", solicitud.Id);
    }

    // ── Permisos ─────────────────────────────────────────────────────────────

    /// <summary>Los indicadores del año y los permisos propios, con los catálogos del formulario.</summary>
    public async Task<MisPermisosDto> MisPermisosAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        var tipos = Enum.GetValues<LeaveType>()
            .Select(t => new OpcionDto((int)t, LeaveRequestService.EtiquetaTipo(t)))
            .ToList();

        if (usuarioActual.DeveloperId is not int devId)
            return new MisPermisosDto(false, new ResumenDePermisosDto(0, 0, 0, 0m), [], tipos,
                LeaveRequestService.MaxDias, ArchivosSubidos.MaxBytes,
                LeaveRequestService.HorasDeLaJornada);

        // No se reusa LeaveRequestService.DeDesarrolladorAsync, y el motivo es el tamaño: devuelve la
        // entidad completa, con los bytes del justificante dentro. En el escritorio eso era una
        // lectura local; aquí serían hasta 15 MB por fila cargados en la API cada vez que alguien
        // abre la pantalla, para acabar mandando un icono de clip. La guarda de pertenencia que ese
        // método aplica sobra aquí: el identificador sale de la sesión, no de la petición.
        var filas = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.DeveloperId == devId)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.Type,
                l.Date,
                l.DaysCount,
                l.HoraInicio,
                l.HoraFin,
                l.Reason,
                l.Notes,
                l.Status,
                l.ReviewComment,
                l.RequestedByDeveloperId,
                // El nombre basta como señal de que hay justificante: el servicio deja los dos campos
                // a null cuando no hay bytes, así que uno sin el otro no existe.
                TieneJustificante = l.AttachmentFileName != null
            })
            .ToListAsync(ct);

        var solicitudes = filas
            .Select(l => new SolicitudDePermisoDto(
                l.Id,
                l.Type,
                LeaveRequestService.EtiquetaTipo(l.Type),
                l.Date,
                l.Date.Date.AddDays(Math.Max(1, l.DaysCount) - 1),
                l.DaysCount,
                LeaveRequestService.EsPorHoras(l.HoraInicio, l.HoraFin),
                l.HoraInicio,
                l.HoraFin,
                LeaveRequestService.Horas(l.HoraInicio, l.HoraFin),
                LeaveRequestService.Duracion(l.DaysCount, l.HoraInicio, l.HoraFin),
                l.Status,
                LeaveRequestService.Etiqueta(l.Status),
                l.Reason,
                l.Notes,
                l.ReviewComment,
                l.TieneJustificante,
                LoRegistroElLider: l.RequestedByDeveloperId == null,
                LeaveRequestService.PuedeCancelar(l.Status),
                LeaveRequestService.PuedeEditar(l.Status),
                LeaveRequestService.PuedeEliminarElDesarrollador(l.Status)))
            .ToList();

        int anio = DateTime.Today.Year;
        var aprobados = filas.Where(l => l.Status == LeaveStatus.Aprobada && l.Date.Year == anio).ToList();

        // Los DÍAS y las HORAS se cuentan por separado y no se mezclan. Sumarle a los días la
        // fracción de jornada de cada tramo exigiría decidir cuánto dura la jornada de cada persona
        // —dato que la ficha no tiene— y, sobre todo, dejaría el contador de días sin poder
        // compararse con el de los años anteriores, que solo tuvo días completos. Se enseñan los dos
        // números juntos en la cabecera; ninguno de los dos miente por su cuenta.
        var resumen = new ResumenDePermisosDto(
            EsperandoRespuesta: filas.Count(l => l.Status == LeaveStatus.Pendiente),
            AprobadosEsteAnio: aprobados.Count,
            DiasAprobadosEsteAnio: aprobados
                .Where(l => !LeaveRequestService.EsPorHoras(l.HoraInicio, l.HoraFin))
                .Sum(l => l.DaysCount),
            HorasAprobadasEsteAnio: aprobados
                .Sum(l => LeaveRequestService.Horas(l.HoraInicio, l.HoraFin)));

        // El tope del archivo es el de ArchivosSubidos y no el del servicio de permisos, porque es
        // ArchivosSubidos quien valida la subida: si algún día dejaran de coincidir, el formulario
        // tiene que enseñar el número que de verdad va a aplicarse.
        return new MisPermisosDto(true, resumen, solicitudes, tipos,
            LeaveRequestService.MaxDias, ArchivosSubidos.MaxBytes,
            LeaveRequestService.HorasDeLaJornada);
    }

    /// <summary>
    /// El desarrollador pide un permiso para sí mismo. Quien decide sigue siendo
    /// <see cref="LeaveRequestService.SolicitarAsync"/>: aquí solo se arma el borrador.
    /// </summary>
    /// <remarks>
    /// El borrador se monta en esta capa y no en el endpoint para que la entidad de EF no salga de
    /// aquí. El desarrollador se toma de la sesión y no de la petición: si viajara en el cuerpo,
    /// habría que comprobar en cada llamada que es el propio, y el día que se olvidara sería «pide
    /// un permiso a nombre de otro».
    /// </remarks>
    /// <param name="horaInicio">El tramo, cuando se pide POR HORAS. Los dos en nulo son un permiso de
    /// días completos, que es lo que llega de cualquier cliente que no conozca el tramo.</param>
    public async Task<(bool ok, string mensaje, int id)> SolicitarPermisoAsync(
        LeaveType tipo, DateTime desde, int dias, string? motivo, string? notas,
        TimeOnly? horaInicio = null, TimeOnly? horaFin = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        if (usuarioActual.DeveloperId is not int devId)
            return (false, "Tu cuenta no tiene ficha de desarrollador, así que no puede pedir permisos.", 0);

        var borrador = new LeaveRequest
        {
            DeveloperId = devId,
            Type = tipo,
            Date = desde.Date,
            DaysCount = dias,
            HoraInicio = horaInicio,
            HoraFin = horaFin,
            Reason = motivo,
            Notes = notas
        };

        var (ok, mensaje, solicitud) = await permisos.SolicitarAsync(borrador, ct);
        return (ok, mensaje, solicitud?.Id ?? 0);
    }

    /// <summary>
    /// Cuelga el justificante de una solicitud de permiso propia.
    /// </summary>
    /// <remarks>
    /// Pasa por <see cref="LeaveRequestService.EditarAsync"/> en vez de escribir la fila: ahí viven
    /// la guarda de pertenencia y la regla de que una solicitud ya resuelta no se toca. Ese método
    /// reemplaza la solicitud entera, así que los demás campos se releen y se vuelven a mandar tal
    /// cual; mandarlos vacíos la dejaría sin motivo y sin fecha.
    /// </remarks>
    public async Task<(bool ok, string mensaje)> AdjuntarJustificanteAsync(
        int permisoId, byte[] contenido, string nombre, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        // Sin los bytes actuales: no hacen falta —se reemplazan— y traerlos sería leer 15 MB para
        // tirarlos. Quién es el dueño lo comprueba EditarAsync, que es donde tiene que decidirse.
        var actual = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.Id == permisoId)
            .Select(l => new
            {
                l.DeveloperId, l.Type, l.Date, l.DaysCount, l.HoraInicio, l.HoraFin, l.Reason, l.Notes
            })
            .FirstOrDefaultAsync(ct);

        if (actual == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        var cambios = new LeaveRequest
        {
            DeveloperId = actual.DeveloperId,
            Type = actual.Type,
            Date = actual.Date,
            DaysCount = actual.DaysCount,
            // El tramo se reenvía como los demás campos: EditarAsync reemplaza la solicitud entera,
            // así que omitirlo convertiría en día completo un permiso de horas al colgarle la receta.
            HoraInicio = actual.HoraInicio,
            HoraFin = actual.HoraFin,
            Reason = actual.Reason,
            Notes = actual.Notes,
            AttachmentBytes = contenido,
            AttachmentFileName = nombre
        };

        return await permisos.EditarAsync(permisoId, cambios, ct);
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────

    /// <summary>Días que cubre un rango, contando el primero y el último.</summary>
    private static int Dias(DateTime inicio, DateTime fin) => (fin.Date - inicio.Date).Days + 1;

    /// <summary>
    /// Etiqueta del estado de una solicitud de vacaciones, con la PALABRA SOLA.
    ///
    /// <para>Vive aquí y no en <see cref="VacationRequestService"/> porque ese servicio se porta sin
    /// tocar; el COLOR sigue siendo cosa de la pantalla, como en los permisos —«Mis vacaciones» ya
    /// recibe <see cref="VacationStatus"/> en el mismo DTO y colorea a partir de él, no de esta
    /// palabra.</para>
    ///
    /// <para>Traía delante el símbolo del escritorio (⏳ ✅ ❌ 🚫) y se fue, por lo mismo que en los
    /// permisos: lo dibuja EL SISTEMA OPERATIVO, sale distinto en cada equipo, NO hereda el color del
    /// texto y donde no hay fuente de emoji instalada sale como un CUADRO VACÍO, cosa que ya se vio
    /// en una captura. Ésta, a diferencia de la de permisos, no se guarda en ninguna parte: solo
    /// viaja al DTO de la pantalla.</para>
    ///
    /// <para>OJO: <c>DocumentoDeVacacionesService</c> tiene su PROPIA copia de estas cuatro etiquetas
    /// —la que sale impresa en el documento de vacaciones— y no es ésta. Si algún día se unifican, hay
    /// que unificarlas a conciencia: aquélla acaba en un PDF que se firma y se archiva.</para>
    /// </summary>
    private static string Etiqueta(VacationStatus estado) => estado switch
    {
        VacationStatus.Pendiente => "Pendiente",
        VacationStatus.Aprobada => "Aprobada",
        VacationStatus.Rechazada => "Rechazada",
        _ => "Cancelada"
    };

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
