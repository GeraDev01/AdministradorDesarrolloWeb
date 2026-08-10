using System.Security.Cryptography;
using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Operaciones que un desarrollador puede hacer sobre SUS PROPIAS solicitudes de vacaciones:
/// cancelarlas, eliminarlas, colgarles el respaldo y FIRMARLAS. Las reglas viven aquí y no en la UI
/// para que la vista y la operación no puedan discrepar (un botón habilitado que luego el servicio
/// rechaza, o peor, al revés) y para que la guarda de pertenencia se aplique aunque la llamada venga
/// de otro lado.
/// </summary>
public class VacationRequestService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, SignatureService firmas)
{
    /// <summary>
    /// Cancelar tiene sentido mientras la solicitud siga viva: pendiente de revisión, o ya
    /// aprobada pero el desarrollador decide no tomarla. Una rechazada o ya cancelada no.
    /// </summary>
    public static bool PuedeCancelar(VacationStatus estado) =>
        estado is VacationStatus.Pendiente or VacationStatus.Aprobada;

    /// <summary>
    /// Solo se borra lo que nunca llegó a ser una decisión del administrador. Una solicitud
    /// aprobada o rechazada es historial: se cancela, no se borra.
    /// </summary>
    public static bool PuedeEliminar(VacationStatus estado) =>
        estado is VacationStatus.Pendiente or VacationStatus.Cancelada;

    /// <summary>
    /// El respaldo se cuelga mientras la solicitud siga esperando respuesta.
    ///
    /// Es la misma regla que <see cref="LeaveRequestService.PuedeEditar"/> le da al justificante de un
    /// permiso, y por el mismo motivo: una vez resuelta, lo que el líder aprobó o rechazó fue el
    /// respaldo que tenía delante, y cambiarlo por debajo dejaría su decisión hablando de otro
    /// documento.
    /// </summary>
    public static bool PuedeAdjuntar(VacationStatus estado) => estado == VacationStatus.Pendiente;

    /// <summary>
    /// Se firma mientras la solicitud siga esperando respuesta, y por lo mismo que el respaldo: lo
    /// que la persona firma es SU PETICIÓN, no la respuesta del jefe. Una vez resuelta ya no hay
    /// petición que respaldar — hay una decisión, y ésa la firma quien la tomó.
    /// </summary>
    public static bool PuedeFirmar(VacationStatus estado) => estado == VacationStatus.Pendiente;

    /// <summary>Cuántos documentos generados se perderían al eliminar (se borran en cascada).</summary>
    public Task<int> DocumentosAsociadosAsync(int requestId, CancellationToken ct = default) =>
        // La fila de la firma del colaborador NO cuenta: vive en esta misma tabla (ver la sección de
        // firma, más abajo) pero no es un documento, y contarla haría que la confirmación de borrado
        // avisara de un papel que nadie generó.
        db.VacationDocuments.CountAsync(
            d => d.VacationRequestId == requestId && d.FileName != MarcaDeLaFirmaDelColaborador, ct);

    public async Task<(bool ok, string mensaje)> CancelarAsync(int requestId, string? motivo = null,
        CancellationToken ct = default)
    {
        var (v, error) = await ObtenerPropiaAsync(requestId, ct);
        if (v == null) return (false, error!);

        if (!PuedeCancelar(v.Status))
            return (false, $"No se puede cancelar una solicitud en estado «{v.Status}».");

        v.Status = VacationStatus.Cancelada;

        // Se anota en ReviewComment y no en ReviewedById/ReviewedAt: esos campos significan
        // "quién la revisó" y llenarlos aquí haría pasar una cancelación propia por una revisión
        // del administrador.
        var quien = currentUser.Username ?? "desarrollador";
        var nota = $"Cancelada por {quien} el {DateTime.Now:dd/MM/yyyy HH:mm}"
                 + (string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo.Trim()}");
        v.ReviewComment = string.IsNullOrWhiteSpace(v.ReviewComment) ? nota : $"{nota}\n{v.ReviewComment}";

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "VacationRequest", v.Id.ToString(),
            $"Cancelada por el desarrollador: {v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy}", ct);
        return (true, "Solicitud cancelada.");
    }

    public async Task<(bool ok, string mensaje)> EliminarAsync(int requestId, CancellationToken ct = default)
    {
        var (v, error) = await ObtenerPropiaAsync(requestId, ct);
        if (v == null) return (false, error!);

        if (!PuedeEliminar(v.Status))
            return (false,
                $"No se puede eliminar una solicitud «{v.Status}»: es parte del historial. " +
                "Si ya no la vas a tomar, cancélala.");

        var descripcion = $"{v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy} ({v.Status})";

        // La IMAGEN de la firma se borra a mano. Su fila de enlace cae en cascada con la solicitud,
        // pero el SignatureProfile cuelga del desarrollador y no de la solicitud: sin esto quedaría
        // una firma suelta, invisible en toda la aplicación y guardada para siempre.
        var firmaId = await FirmaGuardadaAsync(v.Id, ct);

        db.VacationRequests.Remove(v);   // VacationDocuments cae en cascada por configuración del modelo
        await db.SaveChangesAsync(ct);

        if (firmaId is int id) await firmas.EliminarAsync(id, ct);

        await audit.RecordAsync(AuditAction.Delete, "VacationRequest", requestId.ToString(),
            $"Eliminada por el desarrollador: {descripcion}", ct);
        return (true, "Solicitud eliminada.");
    }

    // ── Documento de respaldo ────────────────────────────────────────────────────
    //
    // Es el mismo trato que el justificante de un permiso: los BYTES viven en la propia solicitud
    // (AttachmentBytes/AttachmentFileName), suben por /api/ausencias y bajan por /api/adjuntos. Nunca
    // viajan dentro de una lista.

    /// <summary>
    /// El documento de respaldo de una solicitud. Vacío si no lo tiene o si ya no existe.
    ///
    /// Lo ve su dueño o el líder, nadie más. La guarda va aquí —y no solo en el endpoint— porque este
    /// es el único punto por el que salen esos bytes, y es donde está el dato que dice de quién son.
    /// Pedir uno ajeno lanza y la API responde 403; uno que no está vuelve vacío y responde 404.
    /// </summary>
    public async Task<(byte[] bytes, string nombre)> AdjuntoAsync(int requestId, CancellationToken ct = default)
    {
        var v = await db.VacationRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (v == null) return ([], "");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, v.DeveloperId);

        return v.AttachmentBytes is { Length: > 0 }
            ? (v.AttachmentBytes, NombreSeguro(v.AttachmentFileName))
            : ([], "");
    }

    /// <summary>
    /// Cuelga (o reemplaza) el documento de respaldo de una solicitud propia.
    /// </summary>
    /// <remarks>
    /// Los bytes llegan ya validados por <see cref="ArchivosSubidos.Validar"/> en el endpoint, que es
    /// donde se conoce el archivo que subió el navegador; aquí se decide lo que este servicio sí sabe:
    /// de quién es la solicitud y si todavía admite cambios.
    ///
    /// El escritorio dejaba adjuntar solo al crear la solicitud (<c>MyVacationRequestForm</c>); aquí
    /// también después, mientras siga pendiente, porque en la web el alta y la subida son dos
    /// peticiones y una puede fallar sin la otra — sin esto, una subida caída dejaría la solicitud sin
    /// respaldo y sin forma de arreglarlo.
    /// </remarks>
    public async Task<(bool ok, string mensaje)> AdjuntarRespaldoAsync(
        int requestId, byte[] contenido, string nombre, CancellationToken ct = default)
    {
        var (v, error) = await ObtenerPropiaAsync(requestId, ct);
        if (v == null) return (false, error!);

        if (!PuedeAdjuntar(v.Status))
            return (false, $"No se puede cambiar el respaldo de una solicitud «{v.Status}»: " +
                           "solo se adjunta mientras siga esperando respuesta.");

        if (contenido.Length == 0) return (false, "El archivo llegó vacío.");

        v.AttachmentBytes = contenido;
        v.AttachmentFileName = NombreSeguro(nombre);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "VacationRequest", v.Id.ToString(),
            $"Respaldo adjuntado: {v.AttachmentFileName} ({contenido.Length / 1024} KB) " +
            $"a las vacaciones del {v.StartDate:dd/MM/yyyy} al {v.EndDate:dd/MM/yyyy}", ct);

        return (true, "Documento de respaldo adjuntado.");
    }

    // ── La firma del colaborador ─────────────────────────────────────────────────
    //
    // QUÉ SE FIRMA Y CUÁNDO. Se firma AL SOLICITAR, porque lo que la persona firma es su PETICIÓN y
    // no la respuesta del jefe. El documento definitivo, con las dos firmas, sale cuando el líder
    // resuelve; hasta entonces la firma del colaborador ya está puesta y se ve en su documento.
    //
    // DÓNDE SE GUARDA. La imagen va donde ya van todas las firmas: la tabla SignatureProfiles, por
    // SignatureService, con OwnerDeveloperId puesto —una firma CON dueño es suya, la compartida sin
    // dueño es la del jefe—. Así hereda el tope de tamaño, la validación de medidas y la bitácora
    // sin escribir un segundo camino que se desincronizaría del primero.
    //
    // Y EL ENLACE con la solicitud va en una fila de VacationDocuments marcada con
    // MarcaDeLaFirmaDelColaborador. No es la tabla más obvia, y la razón de que sea ésa es que es la
    // única que ya ata una solicitud con una firma, y el esquema NO SE PUEDE AMPLIAR mientras el
    // escritorio siga leyendo esta misma base: una columna nueva en VacationRequests obligaría a
    // tocar el migrador que las dos aplicaciones comparten. La fila lleva estado Borrador y no tiene
    // bytes, así que el escritorio —que solo mira SignedPdfBytes— no enseña nada de más.
    //
    // Lo que sí puede pasar mientras las dos aplicaciones convivan: si el líder firma el documento
    // DESDE EL ESCRITORIO, aquel toma la primera fila de la solicitud y la reescribe como documento
    // firmado, así que se lleva por delante el enlace. Se degrada al caso «sin firmar», que es el
    // lado seguro: nunca produce una firma que valga sin deberlo.

    /// <summary>
    /// Marca de la fila que enlaza una solicitud con la firma de quien la pidió.
    ///
    /// Va en <c>FileName</c> y no en <c>Status</c> porque el estado Borrador ya lo usaban los
    /// documentos que el escritorio dejó a medias: distinguir por él confundiría un documento viejo
    /// con una firma.
    /// </summary>
    public const string MarcaDeLaFirmaDelColaborador = "firma-del-colaborador";

    /// <summary>
    /// La huella de LO QUE SE FIRMÓ, y la pieza de la que depende que esto no sea un papel falso.
    ///
    /// <para><b>Una firma pegada a unas fechas que ya no son las que se firmaron es un documento
    /// falso</b>, y de los peores: nadie lo mira hasta que hay un problema. Por eso al firmar se
    /// guarda esta huella y al leer la firma se vuelve a calcular: si no coincide, la solicitud
    /// cambió después de firmarse y la firma <b>deja de valer sola</b>, sin que ninguna ruta de
    /// edición tenga que acordarse de avisar. Es a propósito: la protección no depende de que quien
    /// escriba mañana un «corregir solicitud» se acuerde de invalidarla.</para>
    ///
    /// <para>Entra lo que la persona pidió y el papel imprime: de quién es, desde cuándo, hasta
    /// cuándo y el motivo. Los días totales y el día de regreso no se añaden porque salen de esas dos
    /// fechas. <b>No entran</b> el estado, la respuesta del líder ni el respaldo: ninguno es la
    /// petición. Que el jefe apruebe y escriba su observación es justamente el paso siguiente del
    /// trámite; invalidar ahí la firma haría imposible el documento con las dos.</para>
    /// </summary>
    public static string HuellaDeLaPeticion(VacationRequest v) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            v.DeveloperId.ToString(),
            v.StartDate.ToString("yyyy-MM-dd"),
            v.EndDate.ToString("yyyy-MM-dd"),
            (v.Comment ?? "").Trim()))));

    /// <summary>
    /// Firma una solicitud propia con el trazo recién capturado.
    ///
    /// <para>Firmar por otro NO se permite <b>ni siendo líder</b>, y es la única operación de este
    /// servicio donde ser administrador no basta: el resto son gestiones que el líder hace sobre lo
    /// ajeno, y ésta es una declaración personal. Un líder que pudiera firmar por alguien produciría
    /// exactamente el documento que este trabajo existe para evitar.</para>
    ///
    /// <para>Volver a firmar reemplaza lo anterior: la firma vieja se borra en lugar de acumularse,
    /// porque la que vale es la última y guardar el histórico dejaría imágenes que ya no responden
    /// por nada.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> FirmarAsync(
        int requestId, byte[] png, int ancho, int alto, CancellationToken ct = default)
    {
        var (v, error) = await ObtenerPropiaAsync(requestId, ct);
        if (v == null) return (false, error!);

        if (currentUser.DeveloperId != v.DeveloperId)
            return (false, "Una solicitud la firma quien la pidió. Nadie puede firmar por otra persona, " +
                           "ni siquiera el líder: el papel dice que esa persona pidió esos días.");

        if (!PuedeFirmar(v.Status))
            return (false, $"No se puede firmar una solicitud «{v.Status}»: se firma la petición " +
                           "mientras espera respuesta, no la decisión que ya se tomó.");

        // Primero se retira la anterior. Si lo que viene después se rechaza (una imagen vacía, sin
        // medidas), la solicitud queda SIN firma en vez de con la vieja: es el lado seguro, porque
        // quien acaba de volver a firmar cree que lo que vale es el trazo nuevo.
        await BorrarFirmaGuardadaAsync(v.Id, ct);

        var (ok, mensaje, firmaId) = await firmas.CrearAsync(
            $"Firma de la solicitud de vacaciones #{v.Id}", png, ancho, alto,
            duenoDeveloperId: v.DeveloperId, predeterminada: false, ct);
        if (!ok) return (false, mensaje);

        db.VacationDocuments.Add(new VacationDocument
        {
            VacationRequestId  = v.Id,
            Source             = VacationDocSource.Generado,
            Status             = VacationDocStatus.Borrador,
            FileName           = MarcaDeLaFirmaDelColaborador,
            SignatureProfileId = firmaId,
            SignedByUserId     = currentUser.UserId,
            SignedAtUtc        = DateTime.UtcNow,
            PdfChecksum        = HuellaDeLaPeticion(v),
            CreatedAtUtc       = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "VacationRequest", v.Id.ToString(),
            $"Solicitud de vacaciones firmada por quien la pidió: " +
            $"{v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy}", ct);

        return (true, "Solicitud firmada. Tu firma sale ya en el documento; si cambian las fechas o " +
                      "el motivo tendrás que volver a firmarla.");
    }

    /// <summary>
    /// Los papeles de cada una de esas solicitudes: su firma, si el líder ya archivó el documento y
    /// cuántos documentos cuelgan de ella.
    ///
    /// <para>Las tres cosas salen de la MISMA tabla y por eso se resuelven de una vez: pedirlas por
    /// separado serían tres consultas para pintar un renglón. Y se resuelve en bloque —y no solicitud
    /// a solicitud— porque «Mis vacaciones» las pinta todas juntas.</para>
    ///
    /// <para>Devuelve una entrada por CADA identificador pedido, exista o no algo suyo en la tabla:
    /// así quien lo use no tiene que decidir qué significa una ausencia, que es donde se cuela el
    /// «esta no tiene firma» cuando en realidad no se consultó. Lo AJENO se responde como si no
    /// tuviera nada, en vez de lanzar: quien pregunte por identificadores que no son suyos no
    /// aprende de la respuesta si existen —y aquí no se está sacando ningún dato, solo pintando una
    /// columna—.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<int, PapelesDeUnaSolicitud>> PapelesDeAsync(
        IEnumerable<int> requestIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var ids = requestIds.Distinct().ToList();
        var vacio = ids.ToDictionary(id => id, id => PapelesDeUnaSolicitud.Ninguno(id));
        if (ids.Count == 0) return vacio;

        // Se sacan a variables locales para que la consulta las lleve como parámetros: dejadas como
        // accesos a la sesión, cualquier cambio en esa interfaz podría convertirlas en algo que EF
        // intenta traducir a SQL y no puede.
        bool esLider = currentUser.IsAdmin;
        int? mio = currentUser.DeveloperId;

        var filas = await db.VacationDocuments.AsNoTracking()
            .Where(d => ids.Contains(d.VacationRequestId)
                     && (esLider || d.VacationRequest.DeveloperId == mio))
            .Select(d => new
            {
                d.VacationRequestId, d.FileName, d.Status,
                d.SignatureProfileId, d.SignedAtUtc, d.PdfChecksum
            })
            .ToListAsync(ct);

        if (filas.Count == 0) return vacio;

        // Las solicitudes se releen para recalcular la huella: comparar contra la que se guardó al
        // firmar es lo que descubre que alguien cambió las fechas por debajo.
        var solicitudes = await db.VacationRequests.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var resultado = new Dictionary<int, PapelesDeUnaSolicitud>(vacio);
        foreach (var grupo in filas.GroupBy(f => f.VacationRequestId))
        {
            var enlace = grupo.FirstOrDefault(f => f.FileName == MarcaDeLaFirmaDelColaborador);

            bool firmada = enlace is not null;
            bool sigueValiendo = false;
            if (enlace is not null && solicitudes.TryGetValue(grupo.Key, out var v))
                // Sin imagen no hay firma: la referencia queda en nulo si alguien borró el trazo desde
                // el gestor de firmas, y un enlace huérfano no puede pasar por una firma puesta.
                sigueValiendo = enlace.SignatureProfileId != null
                             && enlace.PdfChecksum == HuellaDeLaPeticion(v);

            resultado[grupo.Key] = new PapelesDeUnaSolicitud(
                grupo.Key,
                firmada,
                sigueValiendo ? enlace!.SignatureProfileId : null,
                enlace?.SignedAtUtc,
                sigueValiendo,
                grupo.Any(f => f.FileName != MarcaDeLaFirmaDelColaborador
                            && f.Status == VacationDocStatus.Firmado),
                grupo.Count(f => f.FileName != MarcaDeLaFirmaDelColaborador));
        }
        return resultado;
    }

    /// <summary>
    /// La firma que HOY vale para una solicitud, o null si no la firmaron o si dejó de valer.
    ///
    /// Es el único punto por el que el documento consigue la firma del colaborador, y por eso la
    /// comprobación de la huella vive dentro: cualquier camino que pinte el papel —el Word, el PDF,
    /// el que se archiva— pasa por aquí y obtiene la misma respuesta.
    /// </summary>
    public async Task<int?> FirmaVigenteAsync(int requestId, CancellationToken ct = default)
    {
        var papeles = await PapelesDeAsync([requestId], ct);
        return papeles.TryGetValue(requestId, out var p) ? p.FirmaId : null;
    }

    /// <summary>El identificador de la firma guardada, valga o no. Para poder borrarla.</summary>
    private Task<int?> FirmaGuardadaAsync(int requestId, CancellationToken ct) =>
        db.VacationDocuments.AsNoTracking()
            .Where(d => d.VacationRequestId == requestId && d.FileName == MarcaDeLaFirmaDelColaborador)
            .Select(d => d.SignatureProfileId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Quita la firma de una solicitud: el enlace y la imagen. No falla si no había ninguna.
    /// </summary>
    private async Task BorrarFirmaGuardadaAsync(int requestId, CancellationToken ct)
    {
        var enlace = await db.VacationDocuments
            .FirstOrDefaultAsync(d => d.VacationRequestId == requestId
                                   && d.FileName == MarcaDeLaFirmaDelColaborador, ct);
        if (enlace is null) return;

        var firmaId = enlace.SignatureProfileId;
        db.VacationDocuments.Remove(enlace);
        await db.SaveChangesAsync(ct);

        // La imagen se borra DESPUÉS del enlace: al revés, la clave foránea la dejaría en nulo y la
        // fila sobreviviría un instante pareciendo una firma sin trazo.
        if (firmaId is int id) await firmas.EliminarAsync(id, ct);
    }

    /// <summary>
    /// El nombre lo eligió quien subió el archivo: se limpia antes de que viaje en la cabecera
    /// <c>Content-Disposition</c> y acabe en el disco de quien lo descarga. La limpieza es la de
    /// <see cref="ArchivosSubidos.NombreSeguro"/>, común a todo lo que se sube.
    /// </summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = ArchivosSubidos.NombreSeguro(nombre);
        return n.Length == 0 ? "respaldo" : n;
    }

    /// <summary>Carga la solicitud verificando que la sesión tenga derecho a tocarla.</summary>
    private async Task<(VacationRequest? v, string? error)> ObtenerPropiaAsync(int requestId, CancellationToken ct)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var v = await db.VacationRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (v == null) return (null, "La solicitud ya no existe. Actualiza la lista.");

        // Lanza AuthorizationException si no es dueño ni administrador.
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, v.DeveloperId);
        return (v, null);
    }
}

/// <summary>
/// Los papeles de una solicitud, ya resueltos: su firma y sus documentos.
///
/// <para><see cref="FirmaId"/> viene en nulo cuando la firma no vale, aunque exista guardada: quien
/// pinta el documento solo tiene que preguntar por él y no puede estampar una firma caducada por
/// descuido. <see cref="Firmada"/> junto a <see cref="SigueValiendo"/> es lo que distingue «no ha
/// firmado» de «firmó y dejó de valer», que es justo lo que la pantalla tiene que decir con todas las
/// letras.</para>
/// </summary>
/// <param name="DocumentosGenerados">Sin contar el enlace de la firma, que comparte tabla con ellos
/// pero no es un documento.</param>
public record PapelesDeUnaSolicitud(
    int SolicitudId,
    bool Firmada,
    int? FirmaId,
    DateTime? FirmadaUtc,
    bool SigueValiendo,
    bool DocumentoDelLiderArchivado,
    int DocumentosGenerados)
{
    /// <summary>Una solicitud sin firma y sin documentos. Evita repetir siete valores por omisión.</summary>
    public static PapelesDeUnaSolicitud Ninguno(int solicitudId) =>
        new(solicitudId, false, null, null, false, false, 0);
}
