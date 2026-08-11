using System.Globalization;
using System.Security.Cryptography;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// El lado del LÍDER en las vacaciones: ver lo que el equipo pide, resolverlo y emitir el documento
/// —el borrador sin firmar y el firmado que se archiva—.
///
/// <para><b>Qué se porta y qué no.</b> De <c>VacationDocumentService</c> del escritorio se porta
/// exactamente lo que decidía el CONTENIDO del papel: el día de regreso saltando el fin de semana,
/// las fechas largas en español y las dos casillas de autorización. Lo que no se porta es el armado
/// del DOCX desde una plantilla ni la conversión con LibreOffice: eso obligaba a instalar un
/// programa de escritorio en el servidor. La maquetación es ahora de
/// <see cref="IGeneradorDeDocumentos"/>, y aquí solo se reúnen los datos ya resueltos.</para>
///
/// <para><b>Por qué resolver y listar viven aquí.</b> <c>VacationRequestService</c> se portó tal
/// cual y solo cubre lo que el desarrollador hace con lo suyo (cancelar, eliminar); en el escritorio
/// aprobar y rechazar los escribía la propia pantalla contra la base local, y aquí la pantalla es un
/// navegador que no puede escribir. Quedan en este servicio y no en el endpoint porque la resolución
/// es justo lo que el documento imprime —la casilla marcada es el estado de la solicitud—: separarlas
/// haría posible firmar un papel que dijera algo distinto de lo que la base guarda.</para>
/// </summary>
/// <param name="avisos">Con qué se le pide al colaborador que vuelva a firmar. <b>Opcional a
/// propósito</b>, como en los comunicados: es la salida del bloqueo, no la barrera, así que las
/// pruebas que comprueban que el documento NO se archiva no tienen que montar el envío de avisos para
/// probar lo que están probando. En la aplicación siempre viene puesto.</param>
public class DocumentoDeVacacionesService(
    AppDbContext db,
    ICurrentUser usuarioActual,
    SettingsService configuracion,
    SignatureService firmas,
    IGeneradorDeDocumentos generador,
    IPlantillaDeVacacionesEnWord plantillaWord,
    AuditService auditoria,
    VacationRequestService solicitudes,
    NotificationService? avisos = null)
{
    /// <summary>
    /// Cultura de las fechas del documento. Fija y no la del servidor: el papel se archiva en el
    /// expediente de una persona y tiene que leerse igual se genere donde se genere.
    /// </summary>
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Aprobar o rechazar solo tiene sentido sobre lo que sigue esperando respuesta.</summary>
    public static bool SePuedeResolver(VacationStatus estado) => estado == VacationStatus.Pendiente;

    /// <summary>
    /// Si el documento definitivo se puede emitir y archivar. <b>La regla del bloqueo, en un solo
    /// sitio.</b>
    ///
    /// <para>Se prohíbe cuando hay una firma del colaborador que DEJÓ DE VALER, y no cuando falta:
    /// que nunca haya firmado es el trato de siempre —el escritorio ni siquiera guardaba su firma— y
    /// bloquearlo dejaría sin poder archivar todo el histórico. Lo que no puede pasar es archivar un
    /// papel del que la firma se cayó sin que nadie lo note: ahí hubo una firma, la solicitud cambió
    /// por debajo y el documento saldría con el hueco en blanco pareciendo normal.</para>
    ///
    /// <para>La usan la barrera de <see cref="FirmarAsync"/> y el dato que viaja a la pantalla, para
    /// que el botón que se ve y la operación que se permite no puedan discrepar. <b>La que protege es
    /// la del servicio</b>: la pantalla corre en la máquina de cada quien.</para>
    /// </summary>
    public static bool SePuedeArchivar(FirmaDelColaboradorLeida firma) => !firma.DejoDeValer;

    /// <summary>
    /// Cancelar alcanza también a lo <b>ya aprobado</b>: unas vacaciones concedidas que al final no
    /// se toman se cancelan, no se rechazan. Es la misma regla que
    /// <see cref="VacationRequestService.PuedeCancelar"/> le da al desarrollador sobre lo suyo, y se
    /// reusa a propósito para que el líder no pueda menos que la persona a la que administra.
    /// </summary>
    public static bool SePuedeCancelar(VacationStatus estado) => VacationRequestService.PuedeCancelar(estado);

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las solicitudes del equipo, con las pendientes primero.
    ///
    /// La proyección es deliberadamente ligera: <b>ni el respaldo ni el PDF firmado</b>, solo si los
    /// hay. Es lo que el escritorio ya hacía en su rejilla y aquí pesa más, porque esos bytes
    /// cruzarían la red hacia el navegador en cada carga sin que nadie los abra.
    /// </summary>
    public async Task<List<SolicitudDeVacacionesLeida>> SolicitudesAsync(
        int? developerId = null, VacationStatus? estado = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var q = db.VacationRequests.AsNoTracking().AsQueryable();
        if (developerId is int dev) q = q.Where(v => v.DeveloperId == dev);
        if (estado is VacationStatus e) q = q.Where(v => v.Status == e);

        var filas = await q
            // Las pendientes arriba: son las únicas que piden una acción del líder.
            .OrderByDescending(v => v.Status == VacationStatus.Pendiente)
            .ThenByDescending(v => v.StartDate).ThenByDescending(v => v.Id)
            .Select(v => new
            {
                v.Id,
                v.DeveloperId,
                v.Developer.FullName,
                v.StartDate,
                v.EndDate,
                v.Status,
                v.Comment,
                v.ReviewComment,
                TieneRespaldo = v.AttachmentFileName != null
            })
            .ToListAsync(ct);

        // Cuáles ya tienen documento firmado, en una consulta aparte: metida en la proyección de
        // arriba sería una subconsulta por fila, y aquí son dos conjuntos pequeños.
        var ids = filas.Select(f => f.Id).ToList();
        var firmados = (await db.VacationDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.VacationRequestId) && d.Status == VacationDocStatus.Firmado)
                .Select(d => new { d.VacationRequestId, d.SignedAtUtc })
                .ToListAsync(ct))
            .GroupBy(d => d.VacationRequestId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.SignedAtUtc));

        var firmasDelColaborador = await FirmasDelColaboradorAsync(
            filas.ToDictionary(f => f.Id, f => f.Status), ct);

        return filas
            .Select(v => new SolicitudDeVacacionesLeida(
                v.Id,
                v.DeveloperId,
                v.FullName,
                v.StartDate,
                v.EndDate,
                v.Status,
                v.Comment,
                v.ReviewComment,
                v.TieneRespaldo,
                firmados.ContainsKey(v.Id),
                firmados.GetValueOrDefault(v.Id),
                firmasDelColaborador.GetValueOrDefault(v.Id) ?? FirmaDelColaboradorLeida.Ninguna(v.Status)))
            .ToList();
    }

    /// <summary>
    /// En qué situación está la firma del colaborador en cada una de esas solicitudes.
    ///
    /// <para><b>Quién decide si una firma sigue valiendo es <see cref="VacationRequestService"/></b>,
    /// que recalcula la huella de la petición y la compara con la que se guardó al firmar. Aquí no se
    /// vuelve a razonar sobre eso —solo se traduce a las tres situaciones que la pantalla cuenta—
    /// para que no haya dos criterios que puedan discrepar: el día que discreparan, uno de los dos
    /// estaría diciendo que un papel vale cuando no.</para>
    ///
    /// <para><b>Y se le pregunta SOLO por las solicitudes que tienen firma</b>, no por la lista
    /// entera. <c>PapelesDeAsync</c> tiene que releer la solicitud COMPLETA para recalcular la huella,
    /// y la entidad lleva dentro los bytes del respaldo —hasta 15 MB por fila—. Sin este recorte,
    /// abrir esta pantalla sin filtro se traería el adjunto de cada solicitud del equipo a la memoria
    /// de la API para acabar pintando una palabra. La consulta de más sale mucho más barata, y las
    /// que no aparecen es porque nadie las firmó, que es exactamente lo que hay que enseñar de
    /// ellas.</para>
    /// </summary>
    /// <param name="estados">Las solicitudes por las que se pregunta, con el estado que ya se leyó.
    /// Viaja en vez de releerse porque quien llama lo tiene siempre en la mano, y una consulta más
    /// para recuperar un dato que ya está cargado es una consulta que sobra.</param>
    private async Task<Dictionary<int, FirmaDelColaboradorLeida>> FirmasDelColaboradorAsync(
        IReadOnlyDictionary<int, VacationStatus> estados, CancellationToken ct)
    {
        if (estados.Count == 0) return [];

        var ids = estados.Keys.ToList();
        var conFirma = await db.VacationDocuments.AsNoTracking()
            .Where(d => ids.Contains(d.VacationRequestId)
                     && d.FileName == VacationRequestService.MarcaDeLaFirmaDelColaborador)
            .Select(d => d.VacationRequestId)
            .ToListAsync(ct);

        if (conFirma.Count == 0) return [];

        var papeles = await solicitudes.PapelesDeAsync(conFirma, ct);

        return papeles.ToDictionary(
            p => p.Key,
            p => FirmaDelColaboradorLeida.De(p.Value, estados[p.Key]));
    }

    /// <summary>
    /// Las solicitudes en una hoja de cálculo.
    ///
    /// <para>Se exporta <b>lo que el filtro está enseñando</b>, que es la decisión que ya se tomó en
    /// minutas: el botón vive junto a los desplegables de persona y de estado, y bajar la tabla
    /// entera sorprende a quien acaba de acotar. Si hace falta el histórico completo, se quitan los
    /// filtros — que es exactamente lo que la pantalla enseña que va a bajar.</para>
    ///
    /// <para>No van ni el respaldo ni el PDF firmado, solo si los hay: una hoja de cálculo no es
    /// sitio para 15 MB por fila, y los dos se abren desde la pantalla por su ruta.</para>
    /// </summary>
    public async Task<byte[]> ExcelAsync(
        int? developerId = null, VacationStatus? estado = null, CancellationToken ct = default)
    {
        // La guarda la pone SolicitudesAsync, que es también quien decide el orden: bajar lo mismo
        // que se ve incluye el orden en que se ve.
        var solicitudes = await SolicitudesAsync(developerId, estado, ct);

        var filas = solicitudes.Select(v => new object?[]
        {
            v.Desarrollador,
            v.Inicio.ToString("dd/MM/yyyy"),
            v.Fin.ToString("dd/MM/yyyy"),
            Dias(v.Inicio, v.Fin),
            Etiqueta(v.Estado),
            v.Comentario,
            v.RespuestaDelLider,
            v.TieneRespaldo,
            v.DocumentoFirmado,
            // La hora del firmado sí se convierte: es un INSTANTE, no un día del calendario, y quien
            // lo consulta quiere saber a qué hora de su reloj quedó archivado.
            v.FirmadoUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            // La firma del colaborador va AL FINAL de la hoja, no junto a la del documento, y no es
            // capricho: quien ya tiene fórmulas o filtros montados sobre esta exportación los tiene
            // atados a la posición de cada columna, y meterla en medio se los correría todos.
            EstadoDeLaFirma(v.FirmaDelColaborador)
        }).ToList();

        return HojaDeCalculo.Escribir(
            ["Desarrollador", "Inicio", "Fin", "Días", "Estado", "Comentario", "Respuesta del líder",
             "Respaldo", "Documento firmado", "Firmado el", "Firma del colaborador"],
            filas, "Vacaciones");
    }

    /// <summary>Cuántas esperan respuesta. Se cuenta sobre todas, no sobre lo filtrado.</summary>
    public Task<int> PendientesCountAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);
        return db.VacationRequests.CountAsync(v => v.Status == VacationStatus.Pendiente, ct);
    }

    // El documento de RESPALDO que adjuntó quien pidió las vacaciones no se lee desde aquí: lo suelta
    // <see cref="VacationRequestService.AdjuntoAsync"/>, que es el mismo camino por el que el
    // desarrollador ve el suyo. Su guarda es «el dueño o el líder», así que el líder pasa igual, y con
    // un solo punto de salida no hay dos comprobaciones de permiso que puedan discrepar.

    // ── Resolución ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Aprueba, rechaza o cancela una solicitud.
    ///
    /// <b>El comentario es obligatorio al rechazar</b>, igual que en los permisos: es lo único que
    /// quien la pidió va a leer, y sin él la respuesta no le dice qué hacer. La regla vive aquí y no
    /// en la pantalla porque a esta operación se puede llegar sin pasar por el navegador.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ResolverAsync(
        int solicitudId, VacationStatus destino, string? comentario, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        if (destino is not (VacationStatus.Aprobada or VacationStatus.Rechazada or VacationStatus.Cancelada))
            return (false, "Una solicitud solo se puede aprobar, rechazar o cancelar.");

        if (destino == VacationStatus.Rechazada && string.IsNullOrWhiteSpace(comentario))
            return (false, "Escribe el motivo del rechazo: es lo único que el solicitante va a leer.");

        var v = await db.VacationRequests.Include(x => x.Developer)
            .FirstOrDefaultAsync(x => x.Id == solicitudId, ct);
        if (v == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        // Sigue haciendo falta comprobarlo aunque lo leído sea fresco: el solicitante pudo cancelarla
        // mientras esta pantalla estaba abierta, y resolver algo ya resuelto sería pisar su decisión.
        if (destino == VacationStatus.Cancelada)
        {
            if (!SePuedeCancelar(v.Status))
                return (false, $"No se puede cancelar una solicitud «{Etiqueta(v.Status)}».");
        }
        else if (!SePuedeResolver(v.Status))
        {
            return (false, $"Esa solicitud ya está «{Etiqueta(v.Status)}»; no hay nada que resolver.");
        }

        v.Status = destino;
        v.ReviewedById = usuarioActual.UserId;
        v.ReviewedAt = DateTime.UtcNow;
        v.ReviewComment = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();

        await db.SaveChangesAsync(ct);
        await auditoria.RecordAsync(AuditAction.Update, "VacationRequest", v.Id.ToString(),
            $"Vacaciones {Etiqueta(destino).ToLowerInvariant()}: {v.Developer.FullName} " +
            $"({v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy})", ct);

        return (true, destino switch
        {
            VacationStatus.Aprobada => "Solicitud aprobada.",
            VacationStatus.Rechazada => "Solicitud rechazada.",
            _ => "Solicitud cancelada."
        });
    }

    // ── El documento ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera el PDF de la solicitud <b>sin guardarlo</b>: el borrador para revisar antes de firmar,
    /// o una vista previa ya con la firma puesta.
    ///
    /// Que se pueda emitir antes de resolverse es del escritorio y se conserva: la solicitud existe
    /// desde que se pide, y el papel se lleva a firmar precisamente para resolverla.
    /// </summary>
    /// <remarks>
    /// La guarda ya no es «solo el líder»: la pone <see cref="ArmarAsync"/> y es «el dueño o el
    /// líder», la misma que el respaldo. <b>El desarrollador tiene que poder ver SU documento</b> —es
    /// lo que firma— y mandarlo a pedírselo al jefe para leer su propio papel no tenía defensa. La
    /// firma del jefe sigue siendo cosa del jefe: <c>firmaId</c> se ignora para quien no lo es.
    /// </remarks>
    public async Task<(bool ok, string mensaje, byte[] pdf, string nombre)> GenerarAsync(
        int solicitudId, int? firmaId, CancellationToken ct = default)
    {
        var (datos, nombreArchivo, _, _, error) = await ArmarAsync(solicitudId, firmaId, ct);
        if (datos == null) return (false, error!, [], "");

        return (true, "", generador.SolicitudDeVacaciones(datos), nombreArchivo!);
    }

    /// <summary>
    /// El mismo documento, pero en WORD y sobre la plantilla que el área puede editar.
    ///
    /// <para><b>Por qué existen los dos formatos.</b> El PDF lo maqueta el código: siempre sale
    /// igual, no depende de nadie y es lo que conviene archivar. El Word sale de una plantilla que
    /// RH mantiene, así que cambiar una palabra del formato no exige recompilar ni desplegar. Eso
    /// último se había perdido al quitar LibreOffice — pero LibreOffice nunca hizo falta para
    /// rellenar un .docx, solo para convertirlo a PDF.</para>
    ///
    /// <para>Las firmas se estampan igual que en el PDF: <b>las dos</b>, cada una en su hueco. La del
    /// colaborador la puso él al solicitar; la del jefe, si viene elegida.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, byte[] docx, string nombre)> GenerarWordAsync(
        int solicitudId, int? firmaId, CancellationToken ct = default)
    {
        var (datos, nombreArchivo, delJefe, delColaborador, error) =
            await ArmarAsync(solicitudId, firmaId, ct);
        if (datos == null) return (false, error!, [], "");

        var plantilla = await PlantillaVigenteAsync(ct);

        // Las MEDIDAS viajan con cada firma: la plantilla reserva un hueco concreto y estamparla sin
        // ellas la dejaba de un píxel. Vienen ya de ArmarAsync para no volver a leer la misma imagen.
        var docx = plantillaWord.Rellenar(plantilla, datos, delJefe, delColaborador);
        return (true, "", docx, Path.ChangeExtension(nombreArchivo!, ".docx"));
    }

    // ── La plantilla editable ────────────────────────────────────────────────────

    /// <summary>
    /// La plantilla que se está usando: la que subió el área, o la de fábrica si no hay ninguna.
    ///
    /// <para>Se guarda en la BASE y no en disco a propósito: el sistema de archivos del contenedor
    /// es efímero y con varias instancias cada una tendría la suya. Una plantilla que desaparece al
    /// reiniciar es peor que no poder subirla.</para>
    /// </summary>
    public async Task<byte[]> PlantillaVigenteAsync(CancellationToken ct = default)
    {
        var subida = await db.DocumentTemplates.AsNoTracking()
            .Where(p => p.Clave == PlantillaDeDocumento.SolicitudDeVacaciones)
            .Select(p => p.Contenido)
            .FirstOrDefaultAsync(ct);

        return subida is { Length: > 0 } ? subida : plantillaWord.DeFabrica();
    }

    /// <summary>De dónde sale hoy la plantilla y desde cuándo, para que la pantalla no adivine.</summary>
    public async Task<(bool esDeFabrica, string? quien, DateTime? cuando)> OrigenDeLaPlantillaAsync(
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var subida = await db.DocumentTemplates.AsNoTracking()
            .Where(p => p.Clave == PlantillaDeDocumento.SolicitudDeVacaciones)
            .Select(p => new { p.SubidaPor, p.SubidaEnUtc })
            .FirstOrDefaultAsync(ct);

        return subida is null
            ? (true, null, null)
            : (false, subida.SubidaPor, subida.SubidaEnUtc);
    }

    /// <summary>
    /// Guarda una plantilla nueva, después de comprobar que sirve.
    ///
    /// <para>Se valida ANTES de guardar y no al emitir: si se aceptara cualquier archivo, el fallo
    /// aparecería el día que alguien necesita el documento, y el síntoma sería un papel con
    /// «{{NOMBRE}}» impreso — o ninguno, si el archivo ni siquiera abre.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarPlantillaAsync(
        byte[] contenido, string nombreDeArchivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var validacion = plantillaWord.Validar(contenido);
        if (!validacion.Ok) return (false, validacion.Mensaje);

        var fila = await db.DocumentTemplates
            .FirstOrDefaultAsync(p => p.Clave == PlantillaDeDocumento.SolicitudDeVacaciones, ct);

        if (fila is null)
        {
            fila = new DocumentTemplate { Clave = PlantillaDeDocumento.SolicitudDeVacaciones };
            db.DocumentTemplates.Add(fila);
        }

        fila.Contenido       = contenido;
        fila.NombreDeArchivo = nombreDeArchivo;
        fila.SubidaPor       = usuarioActual.FullName ?? usuarioActual.Username;
        fila.SubidaEnUtc     = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // El CONTENIDO no va a la bitácora: es un documento entero y ahí no cabe. Se anota el hecho.
        await auditoria.RecordAsync(AuditAction.ConfigChange, "DocumentTemplate",
            PlantillaDeDocumento.SolicitudDeVacaciones,
            $"Plantilla de vacaciones actualizada ({nombreDeArchivo}, {contenido.Length / 1024} KB)", ct);

        return (true, validacion.Mensaje + " Se usará a partir del próximo documento que se emita.");
    }

    /// <summary>Vuelve a la plantilla de fábrica borrando la subida. La de fábrica no se puede perder.</summary>
    public async Task<(bool ok, string mensaje)> RestablecerPlantillaAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var fila = await db.DocumentTemplates
            .FirstOrDefaultAsync(p => p.Clave == PlantillaDeDocumento.SolicitudDeVacaciones, ct);

        if (fila is null) return (false, "Ya se está usando la plantilla de fábrica.");

        db.DocumentTemplates.Remove(fila);
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.ConfigChange, "DocumentTemplate",
            PlantillaDeDocumento.SolicitudDeVacaciones,
            "Plantilla de vacaciones restablecida a la de fábrica", ct);

        return (true, "Restablecida. Se volverá a usar la plantilla que trae la aplicación.");
    }

    /// <summary>
    /// Firma el documento con una firma guardada y lo <b>archiva</b> junto a la solicitud.
    ///
    /// Solo se firma lo ya resuelto: un papel firmado con las dos casillas en blanco no dice nada, y
    /// la firma del jefe es justo lo que convierte la decisión en un documento del expediente.
    ///
    /// <para><b>Y no se archiva con la firma del colaborador caída.</b> Ésta es la barrera, y va aquí
    /// —no en el botón— porque la pantalla corre en la máquina de cada quien y a esta operación se
    /// llega también sin navegador: deshabilitar un control es una cortesía, no una regla. El motivo
    /// de fondo es que el papel archivado es el que alguien lee dentro de un año, cuando ya nadie se
    /// acuerda de nada, y uno al que le falta una firma no se descubre hasta que hay un problema.
    /// </para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> FirmarAsync(int solicitudId, int firmaId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var solicitud = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Id == solicitudId)
            .Select(v => new { v.Status, v.Developer.FullName })
            .FirstOrDefaultAsync(ct);
        if (solicitud == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        if (solicitud.Status is VacationStatus.Pendiente)
            return (false, "Resuelve la solicitud antes de firmarla: el documento imprime la casilla " +
                           "de autorizada o rechazada, y sin decisión saldrían las dos en blanco.");

        var firmaDelColaborador = await FirmaDelColaboradorAsync(solicitudId, solicitud.Status, ct);
        if (!SePuedeArchivar(firmaDelColaborador))
            return (false,
                $"{solicitud.FullName} firmó esta solicitud, pero la solicitud cambió después: su " +
                "firma dejó de valer y el documento se archivaría con ese hueco en blanco. Tiene que " +
                "volver a firmarla —el botón «Pedir que vuelva a firmar» se lo avisa—, y eso solo " +
                "puede hacerlo mientras la solicitud siga pendiente de respuesta.");

        var (datos, nombreArchivo, _, _, error) = await ArmarAsync(solicitudId, firmaId, ct);
        if (datos == null) return (false, error!);
        if (datos.FirmaDelJefe is not { Length: > 0 })
            return (false, "Esa firma no tiene imagen guardada. Elige otra o vuelve a trazarla.");

        // El PDF que se archiva sale de los mismos datos, así que si el colaborador firmó su petición
        // el documento definitivo queda con LAS DOS firmas, que es lo que se lleva al expediente.
        var pdf = generador.SolicitudDeVacaciones(datos);

        // La fila de la firma del colaborador se excluye a propósito: vive en esta misma tabla y sin
        // este filtro sería la que se encontrara aquí, y el documento firmado la sobrescribiría —se
        // llevaría por delante la firma de quien pidió las vacaciones justo al archivar el papel.
        var documento = await db.VacationDocuments
            .FirstOrDefaultAsync(d => d.VacationRequestId == solicitudId
                && d.FileName != VacationRequestService.MarcaDeLaFirmaDelColaborador, ct);

        // Se reemplaza el documento en vez de acumular uno por firma: el que vale es el último, y
        // guardar la historia entera sería llenar la base de PDF idénticos salvo el trazo.
        documento ??= new VacationDocument { VacationRequestId = solicitudId, CreatedAtUtc = DateTime.UtcNow };

        documento.Source = VacationDocSource.Generado;
        documento.Status = VacationDocStatus.Firmado;
        documento.FileName = nombreArchivo!;
        documento.SignedPdfBytes = pdf;
        // El DOCX desaparece: ya no hay plantilla de Word que rellenar. La columna se deja en nulo en
        // vez de quitarla porque la tabla la comparten el escritorio y la web hasta el corte.
        documento.DocxBytes = null;
        documento.PdfChecksum = Convert.ToHexString(SHA256.HashData(pdf));
        documento.SignatureProfileId = firmaId;
        documento.SignedByUserId = usuarioActual.UserId;
        documento.SignedAtUtc = DateTime.UtcNow;

        if (documento.Id == 0) db.VacationDocuments.Add(documento);
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Update, "VacationDocument", documento.Id.ToString(),
            $"Documento de vacaciones firmado: {datos.Nombre} ({datos.FechaInicio} — {datos.FechaFin})", ct);

        return (true, "Documento firmado y archivado.");
    }

    // ── La salida del bloqueo ────────────────────────────────────────────────────

    /// <summary>
    /// Le pide al colaborador que firme —o que vuelva a firmar— su solicitud, con un aviso in-app.
    ///
    /// <para><b>Es la salida, y sin ella el bloqueo sobraría.</b> Descubrir que la firma no vale
    /// justo al ir a archivar deja al líder con un documento que no puede emitir; si además tuviera
    /// que salir de la pantalla a buscar a la persona por otro medio, lo que acabaría pasando es que
    /// nadie usara el documento firmado. Un bloqueo sin salida molesta más de lo que protege.</para>
    ///
    /// <para><b>Se niega en vez de mandar un aviso imposible</b> cuando la solicitud ya está
    /// resuelta: solo se firma la petición mientras espera respuesta, así que pedirle una firma que
    /// no puede dar sería mandarlo a una pantalla donde no hay botón. El mensaje dice entonces lo
    /// único que arregla eso de verdad.</para>
    ///
    /// <para><b>Sin clave de deduplicación</b>, al revés que los avisos automáticos: éste no lo
    /// dispara un proceso que puede repetirse solo, lo pulsa una persona a sabiendas. Con clave, el
    /// segundo recordatorio —el que se manda porque el primero no se atendió— desaparecería en
    /// silencio y el líder creería haber avisado.</para>
    /// </summary>
    /// <param name="nota">Lo que el líder quiera añadir. El aviso ya explica solo lo que pasó; esto
    /// es para el motivo, que solo lo sabe él.</param>
    public async Task<(bool ok, string mensaje)> PedirQueVuelvaAFirmarAsync(
        int solicitudId, string? nota = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var solicitud = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Id == solicitudId)
            .Select(v => new { v.DeveloperId, v.Developer.FullName, v.StartDate, v.EndDate, v.Status })
            .FirstOrDefaultAsync(ct);
        if (solicitud == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        var firma = await FirmaDelColaboradorAsync(solicitudId, solicitud.Status, ct);

        if (firma.Vigente)
            return (false, $"La firma de {solicitud.FullName} vale para esta solicitud tal como está " +
                           "hoy: no hay nada que pedirle. Si aun así quieres otra, tiene que volver a " +
                           "trazarla él desde «Mis vacaciones».");

        if (!firma.PuedeVolverAFirmar)
            return (false, $"Esa solicitud ya está «{Etiqueta(solicitud.Status)}» y lo que se firma es " +
                           "la PETICIÓN, mientras espera respuesta. Avisarle ahora no le daría dónde " +
                           "firmar: si el papel tiene que salir con su firma, la solicitud tiene que " +
                           "volver a pedirse.");

        var periodo = $"{solicitud.StartDate:dd/MM/yyyy} — {solicitud.EndDate:dd/MM/yyyy}";
        var quien = usuarioActual.FullName ?? usuarioActual.Username ?? "Tu líder";

        var cuerpo = (firma.DejoDeValer
                ? $"La solicitud del {periodo} cambió después de que la firmaras, así que tu firma dejó " +
                  "de valer y ya no sale en el documento. Vuelve a firmarla."
                : $"Falta tu firma en la solicitud del {periodo}. Sin ella el documento se archiva con " +
                  "el hueco en blanco.")
            + (string.IsNullOrWhiteSpace(nota) ? "" : $"\n\n{quien}: {nota.Trim()}");

        // El aviso se manda ANTES de anotar nada: si no llega a nadie no hubo petición que registrar,
        // y una bitácora que dijera «se le pidió» cuando no se le pidió es peor que no tenerla.
        var llego = avisos is not null && await avisos.NotifyDeveloperAsync(
            solicitud.DeveloperId, NotificationKind.General,
            firma.DejoDeValer ? "Tu firma de vacaciones dejó de valer" : "Falta tu firma en unas vacaciones",
            cuerpo, url: "mis-vacaciones", ct: ct);

        if (!llego)
            return (false, $"No se pudo avisar a {solicitud.FullName}: no tiene cuenta activa en la " +
                           "aplicación, y los avisos se entregan por cuenta. Pídeselo por otro medio.");

        await auditoria.RecordAsync(AuditAction.Update, "VacationRequest", solicitudId.ToString(),
            $"Se le pidió a {solicitud.FullName} que {(firma.DejoDeValer ? "vuelva a firmar" : "firme")} " +
            $"sus vacaciones ({periodo})", ct);

        return (true, firma.DejoDeValer
            ? $"Listo: {solicitud.FullName} tiene el aviso de que su firma dejó de valer. El documento " +
              "se podrá archivar en cuanto vuelva a firmar."
            : $"Listo: {solicitud.FullName} tiene el aviso de que falta su firma.");
    }

    /// <summary>
    /// La situación de la firma del colaborador en UNA solicitud. Mismo camino que el de la lista, y
    /// por lo mismo: la barrera y lo que la pantalla enseña tienen que salir del mismo sitio.
    /// </summary>
    private async Task<FirmaDelColaboradorLeida> FirmaDelColaboradorAsync(
        int solicitudId, VacationStatus estado, CancellationToken ct) =>
        (await FirmasDelColaboradorAsync(new Dictionary<int, VacationStatus> { [solicitudId] = estado }, ct))
            .GetValueOrDefault(solicitudId)
            ?? FirmaDelColaboradorLeida.Ninguna(estado);

    /// <summary>
    /// El PDF firmado que quedó archivado, si lo hay. Vacío si nunca se firmó.
    ///
    /// <b>Lo ve su dueño o el líder</b>, como el respaldo: es el papel resuelto de esa persona, y el
    /// documento que ella firmó al pedirlo va dentro. La guarda va aquí y no solo en el endpoint
    /// porque éste es el único sitio por donde salen esos bytes.
    /// </summary>
    public async Task<(byte[] pdf, string nombre)> DocumentoFirmadoAsync(int solicitudId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        var documento = await db.VacationDocuments.AsNoTracking()
            .Where(d => d.VacationRequestId == solicitudId && d.Status == VacationDocStatus.Firmado)
            .Select(d => new { d.SignedPdfBytes, d.FileName, d.VacationRequest.DeveloperId })
            .FirstOrDefaultAsync(ct);

        // Sin documento se vuelve vacío SIN comprobar de quién era: el endpoint lo traduce a «todavía
        // no está firmado», y contestar 403 aquí delataría a quién pertenece cada identificador.
        if (documento is null) return ([], "");

        AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, documento.DeveloperId);

        return documento.SignedPdfBytes is { Length: > 0 } pdf
            ? (pdf, ArchivosSubidos.NombreSeguro(documento.FileName))
            : ([], "");
    }

    // ── Los campos del papel ─────────────────────────────────────────────────────

    /// <summary>
    /// Reúne de la base y de la configuración todo lo que el documento imprime, y las DOS firmas.
    ///
    /// Los valores por omisión son los del escritorio: departamento «DESARROLLO», puesto el nivel de
    /// la ficha, y jefe directo el nombre de quien tiene la sesión. Que la configuración mande evita
    /// que el papel dependa de qué cuenta lo generó.
    ///
    /// <para><b>Aquí está la guarda de los dos generadores</b>, y es «el dueño o el líder»: el
    /// documento es de la persona a la que se refiere. Como el <c>firmaId</c> viaja en la petición, lo
    /// primero que se hace es descartarlo si quien pide no es el líder — si no, cualquiera podría
    /// pedir su propio documento estampado con la firma del jefe y tendría un papel autorizado que
    /// nadie autorizó.</para>
    /// </summary>
    private async Task<(DatosDeVacaciones? datos, string? nombreArchivo,
                        FirmaEnPng? delJefe, FirmaEnPng? delColaborador, string? error)> ArmarAsync(
        int solicitudId, int? firmaId, CancellationToken ct)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);
        if (!usuarioActual.IsAdmin) firmaId = null;

        var solicitud = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Id == solicitudId)
            .Select(v => new
            {
                v.DeveloperId,
                v.StartDate, v.EndDate, v.Status, v.Comment, v.ReviewComment,
                v.Developer.FullName, v.Developer.HireDate, v.Developer.Seniority,
                v.Developer.VacationDaysLeft
            })
            .FirstOrDefaultAsync(ct);

        if (solicitud == null) return (null, null, null, null, "La solicitud ya no existe. Actualiza la lista.");

        AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, solicitud.DeveloperId);

        FirmaEnPng? delJefe = null;
        if (firmaId is int id)
        {
            var (png, ancho, alto) = await firmas.ImagenAsync(id, ct);
            if (png.Length == 0) return (null, null, null, null, "Esa firma ya no existe. Actualiza la lista.");
            delJefe = new FirmaEnPng(png, ancho, alto);
        }

        // La del colaborador NO se elige: es la que él puso al firmar su petición, y solo entra si
        // HOY sigue valiendo. Quien decide eso es VacationRequestService, comparando la huella de lo
        // que se firmó con lo que la solicitud dice ahora; aquí no se vuelve a razonar sobre ello para
        // que no haya dos criterios que puedan discrepar.
        FirmaEnPng? delColaborador = null;
        if (await solicitudes.FirmaVigenteAsync(solicitudId, ct) is int suya)
        {
            var (png, ancho, alto) = await firmas.ImagenAsync(suya, ct);
            if (png.Length > 0) delColaborador = new FirmaEnPng(png, ancho, alto);
        }

        var departamento = await configuracion.ObtenerAsync(SettingsService.Claves.VacationDepartamento, ct);
        if (string.IsNullOrWhiteSpace(departamento)) departamento = "DESARROLLO";

        var puesto = await configuracion.ObtenerAsync(SettingsService.Claves.VacationPuestoDefault, ct);
        if (string.IsNullOrWhiteSpace(puesto)) puesto = solicitud.Seniority ?? "Desarrollador";

        var jefe = await configuracion.ObtenerAsync(SettingsService.Claves.VacationJefeDirecto, ct);
        if (string.IsNullOrWhiteSpace(jefe)) jefe = usuarioActual.FullName ?? "";

        var datos = Campos(
            nombre: solicitud.FullName,
            fechaDeIngreso: solicitud.HireDate,
            diasPendientes: solicitud.VacationDaysLeft,
            inicio: solicitud.StartDate,
            fin: solicitud.EndDate,
            estado: solicitud.Status,
            respuestaDelLider: solicitud.ReviewComment,
            comentario: solicitud.Comment,
            departamento: departamento,
            puesto: puesto,
            jefeDirecto: jefe,
            firmaDelJefe: delJefe?.Png,
            firmaDelColaborador: delColaborador?.Png);

        return (datos, NombreDelArchivo(solicitud.FullName, solicitud.StartDate),
                delJefe, delColaborador, null);
    }

    /// <summary>
    /// Los campos del documento, calculados. <b>Es el port literal de <c>BuildFields</c></b> del
    /// escritorio y es puro a propósito: sin base ni configuración, para que las reglas que aquí se
    /// deciden —qué día se regresa, cómo se escribe una fecha— se prueben sin generar un PDF.
    /// </summary>
    /// <param name="respuestaDelLider">Lo que se escribió al resolverla. <b>Manda sobre el comentario
    /// de quien la pidió</b>: si hay una decisión escrita, es lo que tiene que leerse en el papel.
    /// </param>
    /// <param name="comentario">Lo que escribió quien pidió las vacaciones. Solo sale impreso
    /// mientras nadie haya respondido nada.</param>
    /// <param name="hoy">La fecha de solicitud impresa. Se recibe en vez de leerse del reloj para que
    /// el resultado sea comprobable; por omisión, hoy.</param>
    /// <param name="firmaDelColaborador">El trazo con el que la persona firmó SU petición. Va al
    /// final y con valor por omisión para que las llamadas de siempre —que no la conocían— sigan
    /// escritas igual.</param>
    public static DatosDeVacaciones Campos(
        string nombre,
        DateTime? fechaDeIngreso,
        int diasPendientes,
        DateTime inicio,
        DateTime fin,
        VacationStatus estado,
        string? respuestaDelLider,
        string? comentario,
        string departamento,
        string puesto,
        string jefeDirecto,
        byte[]? firmaDelJefe = null,
        DateTime? hoy = null,
        byte[]? firmaDelColaborador = null)
    {
        var regreso = SiguienteDiaHabil(fin);

        return new DatosDeVacaciones(
            Nombre: nombre,
            FechaSolicitud: FechaLarga(hoy ?? DateTime.Today),
            Departamento: departamento,
            Puesto: puesto,
            JefeDirecto: jefeDirecto,
            FechaIngreso: fechaDeIngreso is DateTime ingreso ? FechaLarga(ingreso) : "—",
            TotalDias: Dias(inicio, fin).ToString(),
            Periodo: $"{DiaYMes(inicio)} al {FechaLarga(fin)}",
            FechaInicio: FechaLarga(inicio),
            FechaFin: FechaLarga(fin),
            // El día de la semana delante del regreso no es adorno: quien lee el papel comprueba de
            // un vistazo que no se le está citando un sábado.
            FechaRegreso: $"{Capitalizar(regreso.ToString("dddd", Espanol))} {FechaLarga(regreso)}",
            DiasPendientes: diasPendientes.ToString(),
            // Las DOS casillas se imprimen siempre y solo se marca la que toca; el generador ya lo
            // hace así. Con una sola, el papel no se parecería al que se firma a mano.
            Autorizada: estado == VacationStatus.Aprobada,
            Rechazada: estado == VacationStatus.Rechazada,
            Observaciones: respuestaDelLider ?? comentario ?? "",
            FirmaDelJefe: firmaDelJefe,
            FirmaDelColaborador: firmaDelColaborador);
    }

    /// <summary>
    /// El primer día laborable después del último de vacaciones.
    ///
    /// Salta sábado y domingo y nada más: los días festivos NO se contemplan, igual que en el
    /// escritorio. Meterlos ahora exigiría un calendario que la aplicación no tiene, y adivinarlos
    /// sería peor que la regla simple que todo el mundo ya conoce.
    /// </summary>
    public static DateTime SiguienteDiaHabil(DateTime desde)
    {
        var d = desde.Date.AddDays(1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    /// <summary>Una fecha como se escribe en el documento: «14 de Agosto de 2026».</summary>
    public static string FechaLarga(DateTime d) =>
        $"{d.Day} de {Capitalizar(d.ToString("MMMM", Espanol))} de {d.Year}";

    /// <summary>Día y mes, sin año: la primera mitad del período («10 de Agosto»).</summary>
    public static string DiaYMes(DateTime d) => $"{d.Day} de {Capitalizar(d.ToString("MMMM", Espanol))}";

    /// <summary>Días que cubre un rango, contando el primero y el último.</summary>
    public static int Dias(DateTime inicio, DateTime fin) => (fin.Date - inicio.Date).Days + 1;

    /// <summary>
    /// Etiqueta del estado, con la PALABRA SOLA. Vive aquí y no en
    /// <see cref="VacationRequestService"/> porque ese servicio se porta sin tocar; el COLOR sigue
    /// siendo cosa de la pantalla.
    ///
    /// <para>Traía delante el símbolo del escritorio (⏳ ✅ ❌ 🚫) y se fue, por lo mismo que en los
    /// permisos y en «Mis vacaciones»: lo dibuja EL SISTEMA OPERATIVO y no nosotros, así que sale
    /// distinto en cada equipo, NO hereda el color del texto —en el tema oscuro se quedaba con el
    /// suyo mientras la palabra de al lado cambiaba— y donde no hay fuente de emoji instalada sale
    /// como un CUADRO VACÍO. Eso último se vio en una captura; no es una precaución inventada. Era la
    /// última copia de las cuatro que quedaba con símbolo.</para>
    ///
    /// <para><b>Esta etiqueta no es solo adorno de rejilla: SE GUARDA.</b> Va en la descripción que
    /// <see cref="ResolverAsync"/> escribe en la BITÁCORA —«Vacaciones aprobada: …»—, en la columna
    /// «Estado» de la exportación a Excel y en dos mensajes de rechazo. Se cambia igualmente, y el
    /// motivo es que aquí solo cae el SÍMBOLO: la palabra —que es lo que alguien lee en un asiento
    /// viejo y lo que teclea si busca «rechazada»— no se toca. Los asientos anteriores dicen
    /// «Vacaciones ✅ aprobada» y los nuevos dirán «Vacaciones aprobada»; los dos se leen igual y una
    /// búsqueda por la palabra encuentra los dos.</para>
    ///
    /// <para>Nadie coteja esta cadena por igualdad: las decisiones se toman sobre
    /// <see cref="VacationStatus"/>, que viaja en el DTO al lado del texto. Que siga así.</para>
    /// </summary>
    public static string Etiqueta(VacationStatus estado) => estado switch
    {
        VacationStatus.Pendiente => "Pendiente",
        VacationStatus.Aprobada => "Aprobada",
        VacationStatus.Rechazada => "Rechazada",
        _ => "Cancelada"
    };

    /// <summary>
    /// Cómo se lee la situación de una firma en una hoja de cálculo, donde no hay color ni icono que
    /// la maticen: las tres palabras tienen que bastarse solas.
    /// </summary>
    private static string EstadoDeLaFirma(FirmaDelColaboradorLeida firma) =>
        firma.DejoDeValer ? "Dejó de valer" : firma.Vigente ? "Firmada" : "Sin firmar";

    private static string Capitalizar(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Espanol) + s[1..];

    /// <summary>
    /// El nombre con el que se descarga el PDF. Mismo patrón que el escritorio, y saneado antes de
    /// viajar en <c>Content-Disposition</c> porque lleva dentro un nombre que se teclea en una ficha.
    /// </summary>
    private static string NombreDelArchivo(string nombre, DateTime inicio) =>
        ArchivosSubidos.NombreSeguro($"Solicitud_Vacaciones_{nombre}_{inicio:yyyyMMdd}.pdf".Replace(' ', '_'));
}

/// <summary>
/// Una solicitud tal como sale de la base para la pantalla del líder: <b>sin los bytes</b> del
/// respaldo ni del documento, solo si los hay.
/// </summary>
/// <param name="DocumentoFirmado">El líder ya archivó el documento definitivo.</param>
/// <param name="FirmaDelColaborador">Otra cosa distinta: el trazo que puso quien pidió los días, que
/// es lo que ese documento lleva dentro. Confundirlos es exactamente el error que esta pantalla
/// cometía —enseñaba «Firmado» y nadie miraba si la firma de la persona seguía dentro—.</param>
public record SolicitudDeVacacionesLeida(
    int Id,
    int DesarrolladorId,
    string Desarrollador,
    DateTime Inicio,
    DateTime Fin,
    VacationStatus Estado,
    string? Comentario,
    string? RespuestaDelLider,
    bool TieneRespaldo,
    bool DocumentoFirmado,
    DateTime? FirmadoUtc,
    FirmaDelColaboradorLeida FirmaDelColaborador);

/// <summary>
/// La firma del colaborador reducida a lo que hay que contar de ella: <b>tres situaciones, no dos</b>.
///
/// <para>«Firmada» y «sin firmar» dejaban fuera la que importa —firmó y su firma se cayó— y la
/// escondían dentro de «sin firmar», que es la lectura más tranquilizadora posible de un problema:
/// parece que nunca hubo firma, cuando lo que pasó es que la solicitud cambió por debajo de una que
/// sí existió.</para>
///
/// <para>Es una traducción de <see cref="PapelesDeUnaSolicitud"/>, no una segunda verdad: quien
/// decide si una firma vale sigue siendo <see cref="VacationRequestService"/>, comparando la huella
/// de lo que se firmó con lo que la solicitud dice ahora.</para>
/// </summary>
/// <param name="Vigente">Firmó y su firma sirve HOY: es la que el documento estampa.</param>
/// <param name="DejoDeValer">Firmó y lo firmado ya no coincide con la solicitud. Es lo que bloquea el
/// archivado.</param>
/// <param name="PuedeVolverAFirmar">Si la solicitud admite firma ahora mismo. Sale de
/// <see cref="VacationRequestService.PuedeFirmar"/> y no de una copia de la regla, porque quien tiene
/// que poder firmar es la misma persona a la que aquella pantalla le enseña —o le esconde— el botón.
/// </param>
public record FirmaDelColaboradorLeida(
    bool Vigente,
    bool DejoDeValer,
    DateTime? FirmadaUtc,
    bool PuedeVolverAFirmar)
{
    public static FirmaDelColaboradorLeida De(PapelesDeUnaSolicitud papeles, VacationStatus estado) =>
        new(papeles.SigueValiendo,
            papeles.Firmada && !papeles.SigueValiendo,
            papeles.FirmadaUtc,
            VacationRequestService.PuedeFirmar(estado));

    /// <summary>Nadie firmó. Evita que cada llamada tenga que decidir qué significa una ausencia.</summary>
    public static FirmaDelColaboradorLeida Ninguna(VacationStatus estado) =>
        new(false, false, null, VacationRequestService.PuedeFirmar(estado));
}
