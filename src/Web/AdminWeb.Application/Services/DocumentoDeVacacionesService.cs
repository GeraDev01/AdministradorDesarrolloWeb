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
public class DocumentoDeVacacionesService(
    AppDbContext db,
    ICurrentUser usuarioActual,
    SettingsService configuracion,
    SignatureService firmas,
    IGeneradorDeDocumentos generador,
    IPlantillaDeVacacionesEnWord plantillaWord,
    AuditService auditoria)
{
    /// <summary>
    /// Cultura de las fechas del documento. Fija y no la del servidor: el papel se archiva en el
    /// expediente de una persona y tiene que leerse igual se genere donde se genere.
    /// </summary>
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Aprobar o rechazar solo tiene sentido sobre lo que sigue esperando respuesta.</summary>
    public static bool SePuedeResolver(VacationStatus estado) => estado == VacationStatus.Pendiente;

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
                firmados.GetValueOrDefault(v.Id)))
            .ToList();
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
            v.FirmadoUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        }).ToList();

        return HojaDeCalculo.Escribir(
            ["Desarrollador", "Inicio", "Fin", "Días", "Estado", "Comentario", "Respuesta del líder",
             "Respaldo", "Documento firmado", "Firmado el"],
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
    public async Task<(bool ok, string mensaje, byte[] pdf, string nombre)> GenerarAsync(
        int solicitudId, int? firmaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var (datos, nombreArchivo, error) = await ArmarAsync(solicitudId, firmaId, ct);
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
    /// <para>La firma se estampa igual que en el PDF: si viene una elegida, va dentro del Word.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, byte[] docx, string nombre)> GenerarWordAsync(
        int solicitudId, int? firmaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var (datos, nombreArchivo, error) = await ArmarAsync(solicitudId, firmaId, ct);
        if (datos == null) return (false, error!, [], "");

        var plantilla = await PlantillaVigenteAsync(ct);

        // Las medidas de la firma se conservan: la plantilla reserva un hueco concreto y estamparla
        // sin ellas la dejaba de un píxel.
        FirmaEnPng? firma = null;
        if (datos.FirmaDelJefe is { Length: > 0 } && firmaId is int id)
        {
            var (png, ancho, alto) = await firmas.ImagenAsync(id, ct);
            firma = new FirmaEnPng(png, ancho, alto);
        }

        var docx = plantillaWord.Rellenar(plantilla, datos, firma);
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
    /// </summary>
    public async Task<(bool ok, string mensaje)> FirmarAsync(int solicitudId, int firmaId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var estado = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Id == solicitudId).Select(v => (VacationStatus?)v.Status).FirstOrDefaultAsync(ct);
        if (estado == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        if (estado is VacationStatus.Pendiente)
            return (false, "Resuelve la solicitud antes de firmarla: el documento imprime la casilla " +
                           "de autorizada o rechazada, y sin decisión saldrían las dos en blanco.");

        var (datos, nombreArchivo, error) = await ArmarAsync(solicitudId, firmaId, ct);
        if (datos == null) return (false, error!);
        if (datos.FirmaDelJefe is not { Length: > 0 })
            return (false, "Esa firma no tiene imagen guardada. Elige otra o vuelve a trazarla.");

        var pdf = generador.SolicitudDeVacaciones(datos);

        var documento = await db.VacationDocuments
            .FirstOrDefaultAsync(d => d.VacationRequestId == solicitudId, ct);

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

    /// <summary>El PDF firmado que quedó archivado, si lo hay. Vacío si nunca se firmó.</summary>
    public async Task<(byte[] pdf, string nombre)> DocumentoFirmadoAsync(int solicitudId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var documento = await db.VacationDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.VacationRequestId == solicitudId
                                   && d.Status == VacationDocStatus.Firmado, ct);

        return documento?.SignedPdfBytes is { Length: > 0 } pdf
            ? (pdf, ArchivosSubidos.NombreSeguro(documento.FileName))
            : ([], "");
    }

    // ── Los campos del papel ─────────────────────────────────────────────────────

    /// <summary>
    /// Reúne de la base y de la configuración todo lo que el documento imprime.
    ///
    /// Los valores por omisión son los del escritorio: departamento «DESARROLLO», puesto el nivel de
    /// la ficha, y jefe directo el nombre de quien tiene la sesión. Que la configuración mande evita
    /// que el papel dependa de qué cuenta lo generó.
    /// </summary>
    private async Task<(DatosDeVacaciones? datos, string? nombreArchivo, string? error)> ArmarAsync(
        int solicitudId, int? firmaId, CancellationToken ct)
    {
        var solicitud = await db.VacationRequests.AsNoTracking()
            .Where(v => v.Id == solicitudId)
            .Select(v => new
            {
                v.StartDate, v.EndDate, v.Status, v.Comment, v.ReviewComment,
                v.Developer.FullName, v.Developer.HireDate, v.Developer.Seniority,
                v.Developer.VacationDaysLeft
            })
            .FirstOrDefaultAsync(ct);

        if (solicitud == null) return (null, null, "La solicitud ya no existe. Actualiza la lista.");

        byte[]? firma = null;
        if (firmaId is int id)
        {
            var (png, _, _) = await firmas.ImagenAsync(id, ct);
            if (png.Length == 0) return (null, null, "Esa firma ya no existe. Actualiza la lista.");
            firma = png;
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
            firmaDelJefe: firma);

        return (datos, NombreDelArchivo(solicitud.FullName, solicitud.StartDate), null);
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
        DateTime? hoy = null)
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
            FirmaDelJefe: firmaDelJefe);
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
    /// Etiqueta del estado, con los mismos emojis que el escritorio. Vive aquí y no en
    /// <see cref="VacationRequestService"/> porque ese servicio se porta sin tocar; el COLOR sigue
    /// siendo cosa de la pantalla.
    /// </summary>
    public static string Etiqueta(VacationStatus estado) => estado switch
    {
        VacationStatus.Pendiente => "⏳ Pendiente",
        VacationStatus.Aprobada => "✅ Aprobada",
        VacationStatus.Rechazada => "❌ Rechazada",
        _ => "🚫 Cancelada"
    };

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
    DateTime? FirmadoUtc);
