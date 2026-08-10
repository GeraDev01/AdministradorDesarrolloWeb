using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Operaciones que un desarrollador puede hacer sobre SUS PROPIAS solicitudes de vacaciones:
/// cancelarlas o eliminarlas. Las reglas viven aquí y no en la UI para que la vista y la
/// operación no puedan discrepar (un botón habilitado que luego el servicio rechaza, o peor,
/// al revés) y para que la guarda de pertenencia se aplique aunque la llamada venga de otro lado.
/// </summary>
public class VacationRequestService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
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

    /// <summary>Cuántos documentos generados se perderían al eliminar (se borran en cascada).</summary>
    public Task<int> DocumentosAsociadosAsync(int requestId, CancellationToken ct = default) =>
        db.VacationDocuments.CountAsync(d => d.VacationRequestId == requestId, ct);

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
        db.VacationRequests.Remove(v);   // VacationDocuments cae en cascada por configuración del modelo
        await db.SaveChangesAsync(ct);

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
