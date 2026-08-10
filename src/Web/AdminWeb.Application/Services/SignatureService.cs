using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Firmas reutilizables (<see cref="SignatureProfile"/>). Una firma con
/// <see cref="SignatureProfile.OwnerDeveloperId"/> nulo es compartida: es la del jefe, la que va en
/// la solicitud de vacaciones que él autoriza.
///
/// <para>Portado del escritorio con tres cambios, todos obligados por estar en un servidor:</para>
/// <list type="bullet">
///   <item><b>Asíncrono</b>, como el resto de la capa: aquí cada consulta es una petición que no
///   debe bloquear un hilo del servidor.</item>
///   <item><b>Con guardas.</b> En el escritorio no había ninguna porque a esta pantalla solo se
///   llegaba desde el menú del administrador; aquí a la API se llega sin pasar por ningún menú, así
///   que la comprobación viaja pegada al dato: crear o borrar una firma COMPARTIDA es del líder, y
///   la propia solo la toca su dueño.</item>
///   <item><b>Devuelve (ok, mensaje)</b> en vez de void. El escritorio se callaba cuando la firma no
///   existía y quien pulsaba no se enteraba de nada; aquí el mensaje es lo que la pantalla enseña
///   tal cual.</item>
/// </list>
///
/// <para>La imagen NO se cifra, igual que en el escritorio: no es un secreto, y cifrarla con el
/// esquema por usuario rompería el compartir en cuanto la base es de todos.</para>
/// </summary>
public class SignatureService(AppDbContext db, ICurrentUser usuarioActual, AuditService auditoria)
{
    /// <summary>
    /// Tope de la imagen de una firma. Muy por debajo del de los adjuntos (15 MB) a propósito: un
    /// trazo recortado a lo dibujado son unos pocos KB, y esta imagen se lee entera cada vez que se
    /// genera un documento. Lo que llegue por encima no es una firma.
    /// </summary>
    public const int MaxBytes = 1 * 1024 * 1024;

    public const int MaxNombre = 80;

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>Todas las firmas, para el gestor del líder. Sin los bytes de las imágenes.</summary>
    public async Task<List<SignatureProfile>> TodasAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);
        return await SinImagenAsync(db.SignatureProfiles.OrderBy(s => s.DisplayName), ct);
    }

    /// <summary>
    /// Las firmas visibles en un contexto: las compartidas más las del desarrollador indicado, con
    /// la predeterminada primero. Sin los bytes de las imágenes.
    /// </summary>
    public async Task<List<SignatureProfile>> VisiblesAsync(int? duenoDeveloperId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);
        if (duenoDeveloperId is int dueno) AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, dueno);

        return await SinImagenAsync(
            db.SignatureProfiles
                .Where(s => s.OwnerDeveloperId == null || s.OwnerDeveloperId == duenoDeveloperId)
                .OrderByDescending(s => s.IsDefault).ThenBy(s => s.DisplayName),
            ct);
    }

    /// <summary>
    /// La firma predeterminada compartida (la del jefe), o la última que se creó si ninguna está
    /// marcada. Es la que el escritorio ofrecía ya seleccionada al abrir el documento.
    /// </summary>
    public async Task<SignatureProfile?> PredeterminadaCompartidaAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var firmas = await SinImagenAsync(
            db.SignatureProfiles.Where(s => s.OwnerDeveloperId == null)
                .OrderByDescending(s => s.IsDefault).ThenByDescending(s => s.Id),
            ct);
        return firmas.FirstOrDefault();
    }

    /// <summary>
    /// El PNG de una firma, para pintarla o para pegarla en un documento. Vacío si no existe.
    ///
    /// Es el ÚNICO camino por el que salen esos bytes: no viajan en ninguna lista, porque en una
    /// pantalla con diez firmas guardadas se descargarían las diez para dibujar una.
    /// </summary>
    public async Task<(byte[] png, int ancho, int alto)> ImagenAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        var firma = await db.SignatureProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (firma == null) return ([], 0, 0);

        // Una firma con dueño es suya; la compartida la ve cualquiera con sesión porque es la que
        // acompaña a los documentos que el equipo recibe.
        if (firma.OwnerDeveloperId is int dueno) AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, dueno);

        return (firma.PngBytes, firma.WidthPx, firma.HeightPx);
    }

    // ── Escritura ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda una firma recién trazada.
    ///
    /// Las MEDIDAS se guardan junto a la imagen porque el documento las usa para colocarla sin
    /// deformarla, y esa columna la comparten la web y el escritorio hasta el corte.
    /// </summary>
    public async Task<(bool ok, string mensaje, int id)> CrearAsync(
        string? nombre, byte[] png, int ancho, int alto, int? duenoDeveloperId, bool predeterminada,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        // Una firma SIN dueño es la compartida: la que se estampa en los documentos que autoriza el
        // jefe. Crearla es del líder; si cualquiera pudiera, cualquiera firmaría por él.
        if (duenoDeveloperId is int dueno) AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, dueno);
        else AuthorizationGuard.RequireAdmin(usuarioActual);

        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "Ponle un nombre a la firma para poder distinguirla.", 0);
        if (nombre.Length > MaxNombre) return (false, $"El nombre no puede pasar de {MaxNombre} caracteres.", 0);

        if (png.Length == 0) return (false, "No se dibujó ninguna firma.", 0);
        if (png.Length > MaxBytes)
            return (false, $"La imagen de la firma pasa de {MaxBytes / 1024} KB; eso no es un trazo.", 0);
        // Las medidas SÍ se usan: el documento en Word estampa la imagen a su proporción, acotada a
        // la misma altura que el PDF. Sin ellas no hay forma de saber cuánto ocupa el trazo y la
        // firma saldría de un píxel — que es exactamente lo que hacía el escritorio al caer a su
        // valor de respaldo.
        if (ancho <= 0 || alto <= 0)
            return (false, "La firma llegó sin medidas y el documento las necesita para estamparla " +
                           "a su tamaño. Vuelve a trazarla o a subirla.", 0);

        if (predeterminada) await QuitarPredeterminadaAsync(duenoDeveloperId, ct);

        var firma = new SignatureProfile
        {
            DisplayName = nombre,
            PngBytes = png,
            WidthPx = ancho,
            HeightPx = alto,
            OwnerDeveloperId = duenoDeveloperId,
            IsDefault = predeterminada,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.SignatureProfiles.Add(firma);
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Create, "SignatureProfile", firma.Id.ToString(),
            $"Firma guardada: {firma.DisplayName}", ct);
        return (true, "Firma guardada.", firma.Id);
    }

    public async Task<(bool ok, string mensaje)> RenombrarAsync(int id, string? nombre,
        CancellationToken ct = default)
    {
        var (firma, error) = await ObtenerParaEscribirAsync(id, ct);
        if (firma == null) return (false, error!);

        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "Ponle un nombre a la firma para poder distinguirla.");
        if (nombre.Length > MaxNombre) return (false, $"El nombre no puede pasar de {MaxNombre} caracteres.");

        firma.DisplayName = nombre;
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Update, "SignatureProfile", id.ToString(),
            $"Firma renombrada: {firma.DisplayName}", ct);
        return (true, "Firma renombrada.");
    }

    /// <summary>
    /// Marca la firma como la predeterminada de su ámbito y apaga la que lo estuviera. El ámbito es
    /// el dueño: la compartida y la de cada persona tienen cada una la suya.
    /// </summary>
    public async Task<(bool ok, string mensaje)> MarcarPredeterminadaAsync(int id, CancellationToken ct = default)
    {
        var (firma, error) = await ObtenerParaEscribirAsync(id, ct);
        if (firma == null) return (false, error!);

        await QuitarPredeterminadaAsync(firma.OwnerDeveloperId, ct);
        firma.IsDefault = true;
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Update, "SignatureProfile", id.ToString(),
            $"Firma predeterminada: {firma.DisplayName}", ct);
        return (true, $"«{firma.DisplayName}» quedó como predeterminada.");
    }

    /// <summary>
    /// Borra la firma.
    ///
    /// Los documentos ya firmados con ella NO se tocan: el modelo deja su referencia en nulo
    /// (<c>SetNull</c>) y el PDF archivado conserva la imagen dentro, que es lo que importa — un
    /// documento firmado es una prueba y no puede deshacerse porque alguien depure el gestor.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        var (firma, error) = await ObtenerParaEscribirAsync(id, ct);
        if (firma == null) return (false, error!);

        var nombre = firma.DisplayName;
        db.SignatureProfiles.Remove(firma);
        await db.SaveChangesAsync(ct);

        await auditoria.RecordAsync(AuditAction.Delete, "SignatureProfile", id.ToString(),
            $"Firma eliminada: {nombre}", ct);
        return (true, "Firma eliminada.");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────

    /// <summary>Carga la firma comprobando que la sesión tenga derecho a cambiarla.</summary>
    private async Task<(SignatureProfile? firma, string? error)> ObtenerParaEscribirAsync(
        int id, CancellationToken ct)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        var firma = await db.SignatureProfiles.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (firma == null) return (null, "Esa firma ya no existe. Actualiza la lista.");

        if (firma.OwnerDeveloperId is int dueno) AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, dueno);
        else AuthorizationGuard.RequireAdmin(usuarioActual);

        return (firma, null);
    }

    private async Task QuitarPredeterminadaAsync(int? duenoDeveloperId, CancellationToken ct)
    {
        foreach (var otra in await db.SignatureProfiles
                     .Where(s => s.OwnerDeveloperId == duenoDeveloperId && s.IsDefault)
                     .ToListAsync(ct))
            otra.IsDefault = false;
    }

    /// <summary>
    /// Lee las firmas dejando el BLOB fuera del SELECT.
    ///
    /// La proyección anónima es lo que garantiza que la imagen no entre en la consulta; el mapeo a
    /// la entidad se hace ya en memoria, igual que en <see cref="DevActivityService"/>. Sin esto,
    /// una lista de diez firmas se traería las diez imágenes para acabar enseñando diez nombres.
    /// </summary>
    private static async Task<List<SignatureProfile>> SinImagenAsync(
        IQueryable<SignatureProfile> origen, CancellationToken ct) =>
        (await origen.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.DisplayName, s.WidthPx, s.HeightPx,
                s.OwnerDeveloperId, s.IsDefault, s.CreatedAtUtc
            })
            .ToListAsync(ct))
        .Select(s => new SignatureProfile
        {
            Id = s.Id,
            DisplayName = s.DisplayName,
            WidthPx = s.WidthPx,
            HeightPx = s.HeightPx,
            OwnerDeveloperId = s.OwnerDeveloperId,
            IsDefault = s.IsDefault,
            CreatedAtUtc = s.CreatedAtUtc,
            PngBytes = []   // la imagen solo viaja por ImagenAsync
        })
        .ToList();
}
