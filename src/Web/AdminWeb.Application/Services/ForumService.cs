using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Una publicación tal como se pinta en el muro: con sus contadores ya resueltos.</summary>
public record ForumTarjeta(
    ForumPost Post,
    int Comentarios,
    int MeGusta,
    bool YoDiMeGusta,
    DateTime UltimaActividadUtc,
    int Imagenes = 0);

/// <summary>Una entrada del hilo, con su nivel de anidamiento ya calculado para pintarla.</summary>
public record ForumNodo(ForumPost Post, int Nivel, int MeGusta, bool YoDiMeGusta);

/// <summary>
/// Una imagen lista para adjuntar. La prepara quien recibe la subida (recortar, reescalar y sacar
/// la miniatura son cosa del endpoint o del navegador, no de este servicio); aquí solo se comprueba
/// que sea lo que dice ser.
/// </summary>
public record ForumImagenNueva(string NombreArchivo, byte[] Bytes, byte[] Miniatura, int Ancho, int Alto);

/// <summary>
/// Una imagen ya publicada, SIN el original: solo la miniatura. El original se pide aparte con
/// <see cref="ForumService.BytesDeImagenAsync"/> y solo cuando alguien la abre.
/// </summary>
public record ForumImagen(
    int Id, int PostId, string NombreArchivo, string TipoContenido,
    long Bytes, int Ancho, int Alto, byte[] Miniatura);

/// <summary>Lo elegido en los filtros del foro. null = no filtrar.</summary>
public record ForumFiltro(
    string? Texto = null,
    ForumTopic? Tema = null,
    string? Autor = null,
    int? UltimosDias = null,
    bool SoloMios = false);

/// <summary>
/// Foro del equipo: publicaciones con comentarios anidados.
///
/// Lo ve todo el que tenga sesión — es el punto: compartir ideas. Cada quien manda sobre lo suyo
/// (editar, retirar); el administrador además puede fijar, cerrar y retirar cualquier cosa, porque
/// alguien tiene que poder parar un hilo que se descarrila. La excepción es <see cref="AuditoriaAsync"/>
/// (la vista consolidada del rastro): esa es solo del administrador.
///
/// <b>Nada se borra de verdad.</b> Retirar marca la entrada y sustituye el texto por un aviso: un
/// hilo con respuestas que contestan a algo que ya no existe es peor de auditar que ver un «mensaje
/// eliminado». Lo mismo vale para editar, que deja constancia de que se editó y cuándo.
///
/// Una entrada puede llevar <b>imágenes</b> incrustadas y <b>enlaces</b> en su texto. Los enlaces no
/// se guardan aparte: se reconocen al pintar (véase <see cref="ForumRichText"/>), así que buscar
/// sigue encontrando la dirección dentro del cuerpo. Las imágenes sí son filas propias
/// (<see cref="ForumAttachment"/>), y siguen la misma regla que el texto: al retirar una entrada
/// dejan de servirse: si la captura se siguiera viendo, retirar no querría decir nada.
///
/// <b>AVISO DE SEGURIDAD (nuevo de la web).</b> El cuerpo de una entrada lo escribe cualquiera con
/// sesión y lo leen todos los demás: es el vector clásico de XSS almacenado. El HTML del foro DEBE
/// sanearse EN EL SERVIDOR al guardarse y también al renderizarse — el cliente Blazor corre en la
/// máquina de quien lee y se puede manipular, y la API se puede llamar sin pasar por él, así que
/// nada de lo que haga el cliente cuenta como defensa. Mientras no haya librería de saneado, el
/// cuerpo se pinta como TEXTO (nunca <c>MarkupString</c> ni <c>innerHTML</c>) y los enlaces se
/// construyen desde los segmentos ya validados de <see cref="ForumRichText.Analizar"/>. El punto
/// único donde enchufar el saneador es <see cref="ForumRichText.Sanear"/>, por el que ya pasa todo
/// cuerpo que entra aquí.
/// </summary>
public class ForumService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    public const int MaxTitulo = 200;
    public const int MaxCuerpo = 20_000;
    public const int MaxEtiquetas = 300;

    /// <summary>Imágenes por entrada. Más que esto deja de ser una publicación y pasa a ser un álbum.</summary>
    public const int MaxImagenes = 6;

    /// <summary>
    /// Tope por imagen YA reescalada. La base es compartida y se lee por red: una captura de
    /// pantalla normal ronda los 200 KB, así que 4 MB deja sitio de sobra sin que un hilo con
    /// fotos de móvil convierta el muro en una descarga.
    /// </summary>
    public const long MaxBytesImagen = 4L * 1024 * 1024;

    /// <summary>Hasta dónde se puede responder a una respuesta. Más allá, la sangría se come la pantalla
    /// y la conversación deja de leerse; los comentarios más profundos cuelgan del último nivel.</summary>
    public const int ProfundidadMaxima = 5;

    // ── Publicar y comentar ──────────────────────────────────────────────────────

    public async Task<(bool ok, string mensaje, ForumPost? post)> PublicarAsync(
        string? titulo, string? cuerpo, ForumTopic tema, string? etiquetas = null,
        IReadOnlyList<ForumImagenNueva>? imagenes = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        titulo = (titulo ?? "").Trim();
        cuerpo = NormalizarCuerpo(cuerpo);
        int cuantasImagenes = imagenes?.Count ?? 0;

        if (titulo.Length < 3) return (false, "Escribe un título (al menos 3 caracteres).", null);
        if (titulo.Length > MaxTitulo) return (false, $"El título no puede pasar de {MaxTitulo} caracteres.", null);
        // Con imágenes, el texto puede ser corto o no estar: una captura con su título ya dice algo.
        if (cuantasImagenes == 0 && cuerpo.Length < 5) return (false, "Escribe algo que compartir (al menos 5 caracteres).", null);
        if (cuerpo.Length > MaxCuerpo) return (false, $"La publicación no puede pasar de {MaxCuerpo:N0} caracteres.", null);

        var (imgOk, imgMensaje) = ValidarImagenes(imagenes, 0);
        if (!imgOk) return (false, imgMensaje, null);

        var post = new ForumPost
        {
            ParentId          = null,
            Depth             = 0,
            AuthorUserId      = userId,
            AuthorName        = NombreAutor(),
            AuthorDeveloperId = currentUser.DeveloperId,
            Title             = titulo,
            Body              = cuerpo,
            Topic             = tema,
            Tags              = NormalizarEtiquetas(etiquetas),
            CreatedAtUtc      = DateTime.UtcNow
        };
        db.ForumPosts.Add(post);
        await db.SaveChangesAsync(ct);

        // RootId apunta a sí misma: es la raíz de su propio hilo.
        post.RootId = post.Id;
        await db.SaveChangesAsync(ct);

        await GuardarImagenesAsync(post.Id, imagenes, 0, ct);

        await audit.RecordAsync(AuditAction.Create, "ForumPost", post.Id.ToString(),
            $"Publicación en el foro: {titulo}" + (cuantasImagenes > 0 ? $" ({cuantasImagenes} imagen/es)" : ""), ct);
        return (true, "Publicado.", post);
    }

    public async Task<(bool ok, string mensaje, ForumPost? comentario)> ComentarAsync(
        int parentId, string? cuerpo, IReadOnlyList<ForumImagenNueva>? imagenes = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        cuerpo = NormalizarCuerpo(cuerpo);
        int cuantasImagenes = imagenes?.Count ?? 0;

        // Una captura sola es un comentario legítimo: «así se ve el error».
        if (cuerpo.Length < 1 && cuantasImagenes == 0) return (false, "Escribe tu comentario o adjunta una imagen.", null);
        if (cuerpo.Length > MaxCuerpo) return (false, $"El comentario no puede pasar de {MaxCuerpo:N0} caracteres.", null);

        var (imgOk, imgMensaje) = ValidarImagenes(imagenes, 0);
        if (!imgOk) return (false, imgMensaje, null);

        var padre = await db.ForumPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentId, ct);
        if (padre == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.", null);

        // Se comprueba sobre la RAÍZ: cerrar un hilo tiene que cerrarlo entero, no solo su primer
        // mensaje. Si no, se seguiría respondiendo por dentro a un hilo dado por cerrado.
        var raiz = padre.RootId == padre.Id
            ? padre
            : await db.ForumPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == padre.RootId, ct);
        if (raiz is { Locked: true }) return (false, "El hilo está cerrado: ya no admite comentarios.", null);
        if (padre.DeletedAtUtc != null) return (false, "No se puede responder a una entrada retirada.", null);

        var comentario = new ForumPost
        {
            ParentId          = parentId,
            RootId            = padre.RootId,
            // Pasado el tope, el comentario cuelga del mismo nivel en vez de seguir sangrando.
            Depth             = Math.Min(padre.Depth + 1, ProfundidadMaxima),
            AuthorUserId      = userId,
            AuthorName        = NombreAutor(),
            AuthorDeveloperId = currentUser.DeveloperId,
            Body              = cuerpo,
            Topic             = padre.Topic,
            CreatedAtUtc      = DateTime.UtcNow
        };
        db.ForumPosts.Add(comentario);
        await db.SaveChangesAsync(ct);

        await GuardarImagenesAsync(comentario.Id, imagenes, 0, ct);

        await audit.RecordAsync(AuditAction.Create, "ForumPost", comentario.Id.ToString(),
            $"Comentario en el hilo #{padre.RootId}" + (cuantasImagenes > 0 ? $" ({cuantasImagenes} imagen/es)" : ""), ct);
        return (true, "Comentario publicado.", comentario);
    }

    // ── Editar, retirar, fijar, cerrar ───────────────────────────────────────────

    public async Task<(bool ok, string mensaje)> EditarAsync(
        int postId, string? titulo, string? cuerpo,
        IReadOnlyList<ForumImagenNueva>? imagenesNuevas = null, IReadOnlyList<int>? quitarImagenes = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var post = await db.ForumPosts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.");
        if (post.Eliminado) return (false, "Esa entrada está retirada.");
        if (post.AuthorUserId != currentUser.UserId)
            return (false, "Solo puedes editar lo que tú escribiste.");

        cuerpo = NormalizarCuerpo(cuerpo);

        // Las que se quitan son solo las de ESTA entrada: un id de otra publicación se ignora en vez
        // de dejar que alguien borre por ahí las imágenes de un tercero.
        var aQuitar = quitarImagenes is { Count: > 0 }
            ? await db.ForumAttachments.Where(a => a.PostId == postId && quitarImagenes.Contains(a.Id)).ToListAsync(ct)
            : [];

        int yaTiene = await db.ForumAttachments.CountAsync(a => a.PostId == postId, ct) - aQuitar.Count;
        int quedaran = yaTiene + (imagenesNuevas?.Count ?? 0);

        if (cuerpo.Length < 1 && quedaran == 0) return (false, "El texto no puede quedar vacío.");
        if (cuerpo.Length > MaxCuerpo) return (false, $"El texto no puede pasar de {MaxCuerpo:N0} caracteres.");

        var (imgOk, imgMensaje) = ValidarImagenes(imagenesNuevas, yaTiene);
        if (!imgOk) return (false, imgMensaje);

        if (post.EsPublicacion)
        {
            titulo = (titulo ?? "").Trim();
            if (titulo.Length < 3) return (false, "Escribe un título (al menos 3 caracteres).");
            if (titulo.Length > MaxTitulo) return (false, $"El título no puede pasar de {MaxTitulo} caracteres.");
            post.Title = titulo;
        }

        post.Body = cuerpo;
        post.EditedAtUtc = DateTime.UtcNow;   // queda constancia: un foro auditable no edita en silencio

        // La imagen que se quita se borra de verdad, no se marca. Editar ya sustituye el cuerpo
        // anterior sin conservarlo; guardar en cambio los MB de una captura que su autor retiró del
        // texto sería incoherente y caro. Lo que queda constancia es de que se editó y cuándo.
        if (aQuitar.Count > 0) db.ForumAttachments.RemoveRange(aQuitar);
        await db.SaveChangesAsync(ct);

        // Max() sobre int? y no DefaultIfEmpty(-1): esa forma no la sabe traducir EF y reventaba al
        // editar CUALQUIER entrada, llevara imágenes o no.
        int siguiente = (await db.ForumAttachments.Where(a => a.PostId == postId)
            .MaxAsync(a => (int?)a.Orden, ct) ?? -1) + 1;
        await GuardarImagenesAsync(postId, imagenesNuevas, siguiente, ct);

        var detalle = "Entrada del foro editada";
        if (aQuitar.Count > 0) detalle += $"; {aQuitar.Count} imagen/es quitada/s";
        if (imagenesNuevas is { Count: > 0 }) detalle += $"; {imagenesNuevas.Count} imagen/es añadida/s";
        await audit.RecordAsync(AuditAction.Update, "ForumPost", post.Id.ToString(), detalle, ct);
        return (true, "Editado.");
    }

    /// <summary>
    /// Retira una entrada: se conserva la fila y el hueco en el hilo, pero deja de mostrarse el
    /// texto. Su autor siempre; el administrador, cualquiera.
    /// </summary>
    public async Task<(bool ok, string mensaje)> RetirarAsync(int postId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var post = await db.ForumPosts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.");
        if (post.Eliminado) return (true, "Esa entrada ya estaba retirada.");
        if (post.AuthorUserId != currentUser.UserId && !currentUser.IsAdmin)
            return (false, "Solo puedes retirar lo que tú escribiste.");

        post.DeletedAtUtc = DateTime.UtcNow;
        post.DeletedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "ForumPost", post.Id.ToString(),
            post.AuthorUserId == currentUser.UserId
                ? "Entrada del foro retirada por su autor"
                : $"Entrada del foro retirada por un líder (autor: {post.AuthorName})", ct);
        return (true, "Entrada retirada. El hilo conserva el hueco para que se siga entendiendo.");
    }

    /// <summary>
    /// Borra una publicación ENTERA y de verdad: la entrada raíz, todos sus comentarios (a
    /// cualquier profundidad), sus imágenes y sus «me gusta». Solo administrador.
    ///
    /// Es la excepción deliberada a la regla de «nada se borra» que rige el resto del foro.
    /// <see cref="RetirarAsync"/> deja el hueco porque quitar un mensaje del medio de una conversación
    /// la vuelve ilegible; aquí no queda conversación que proteger — se va el hilo completo, así
    /// que no hay respuestas huérfanas. Es la vía para lo que no debería haberse publicado nunca
    /// (algo confidencial, un desahogo, contenido subido por error), donde dejar el aviso de
    /// «contenido eliminado» y el título a la vista sigue señalando lo que se quiso quitar.
    ///
    /// El rastro NO se pierde: antes de borrar se guarda en la bitácora una instantánea con el
    /// título, el autor, las fechas y cuánto se llevó por delante. Desaparece el contenido, no el
    /// hecho de que existió y de quién lo retiró.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarPublicacionAsync(int rootId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var raiz = await db.ForumPosts.FirstOrDefaultAsync(p => p.Id == rootId, ct);
        if (raiz == null) return (false, "Esa publicación ya no existe. Actualiza el muro.");
        if (!raiz.EsPublicacion)
            return (false, "Eso es un comentario, no una publicación. Para quitarlo usa «Retirar».");

        // Todo el hilo cuelga de RootId, incluida la propia raíz (su RootId es su Id).
        var delHilo = await db.ForumPosts.Where(p => p.RootId == rootId).ToListAsync(ct);
        if (delHilo.All(p => p.Id != raiz.Id)) delHilo.Add(raiz);   // hilo viejo sin RootId bien puesto

        var ids = delHilo.Select(p => p.Id).ToList();
        int comentarios = delHilo.Count(p => !p.EsPublicacion);

        var imagenes = await db.ForumAttachments.Where(a => ids.Contains(a.PostId)).ToListAsync(ct);
        var likes = await db.ForumLikes.Where(l => ids.Contains(l.PostId)).ToListAsync(ct);

        // Instantánea ANTES de borrar: es lo único que quedará de la publicación.
        var instantanea = new
        {
            raiz.Id, raiz.Title, raiz.AuthorName, raiz.AuthorUserId,
            Tema = raiz.Topic.ToString(), raiz.Tags,
            raiz.CreatedAtUtc, raiz.EditedAtUtc,
            Comentarios = comentarios, Imagenes = imagenes.Count, MeGusta = likes.Count
        };

        // El orden importa: la autorreferencia ParentId es NoAction (SQL Server rechaza una cascada
        // sobre la misma tabla), así que los comentarios más profundos se van primero y la raíz al
        // final. Las imágenes y los «me gusta» se borran explícitamente en lugar de confiar en la
        // cascada: las tablas creadas por DatabaseMigrator no siempre la traen.
        db.ForumAttachments.RemoveRange(imagenes);
        db.ForumLikes.RemoveRange(likes);
        db.ForumPosts.RemoveRange(delHilo.OrderByDescending(p => p.Depth).ThenByDescending(p => p.Id));
        await db.SaveChangesAsync(ct);

        await audit.RecordDetailedAsync(AuditAction.Delete, "ForumPost", rootId.ToString(),
            $"Publicación del foro ELIMINADA por completo por un líder: «{raiz.Title}» " +
            $"(autor: {raiz.AuthorName}) — {comentarios} comentario(s), {imagenes.Count} imagen(es).",
            AuditOutcome.Exito, oldValues: instantanea, ct: ct);

        return (true, comentarios == 0
            ? "Publicación eliminada."
            : $"Publicación eliminada junto con sus {comentarios} comentario(s).");
    }

    /// <summary>Fija o suelta una publicación. Solo administrador: es el muro de todos.</summary>
    public async Task<(bool ok, string mensaje)> FijarAsync(int postId, bool fijar, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var post = await db.ForumPosts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return (false, "Esa publicación ya no existe.");
        if (!post.EsPublicacion) return (false, "Solo se fijan publicaciones, no comentarios.");

        post.Pinned = fijar;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "ForumPost", post.Id.ToString(),
            fijar ? "Publicación fijada" : "Publicación soltada", ct);
        return (true, fijar ? "Fijada arriba del muro." : "Ya no está fijada.");
    }

    /// <summary>Cierra o reabre un hilo. Solo administrador.</summary>
    public async Task<(bool ok, string mensaje)> CerrarAsync(int postId, bool cerrar, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var post = await db.ForumPosts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return (false, "Esa publicación ya no existe.");
        if (!post.EsPublicacion) return (false, "Se cierra el hilo completo, no un comentario suelto.");

        post.Locked = cerrar;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "ForumPost", post.Id.ToString(),
            cerrar ? "Hilo cerrado" : "Hilo reabierto", ct);
        return (true, cerrar ? "Hilo cerrado: ya no admite comentarios." : "Hilo reabierto.");
    }

    /// <summary>Alterna el «me gusta» propio. Devuelve si quedó dado y el total.</summary>
    public async Task<(bool ok, bool meGusta, int total)> MeGustaAsync(int postId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, false, 0);
        if (!await db.ForumPosts.AnyAsync(p => p.Id == postId && p.DeletedAtUtc == null, ct)) return (false, false, 0);

        var existente = await db.ForumLikes.FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId, ct);
        bool dado;
        if (existente != null) { db.ForumLikes.Remove(existente); dado = false; }
        else { db.ForumLikes.Add(new ForumLike { PostId = postId, UserId = userId, CreatedAtUtc = DateTime.UtcNow }); dado = true; }
        await db.SaveChangesAsync(ct);

        return (true, dado, await db.ForumLikes.CountAsync(l => l.PostId == postId, ct));
    }

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// El muro: publicaciones con sus contadores. Las fijadas primero; el resto, por última
    /// actividad —no por fecha de creación—, para que un hilo que revive vuelva a subir.
    /// </summary>
    public async Task<List<ForumTarjeta>> MuroAsync(ForumFiltro? filtro = null, int tope = 100,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();

        var publicaciones = await db.ForumPosts.AsNoTracking()
            .Where(p => p.ParentId == null)
            .ToListAsync(ct);

        publicaciones = ForumFilter.Aplicar(publicaciones, filtro, userId, DateTime.UtcNow);
        if (publicaciones.Count == 0) return [];

        var raices = publicaciones.Select(p => p.Id).ToHashSet();

        // Un solo viaje por los contadores en vez de tres consultas por tarjeta.
        var porHilo = await db.ForumPosts.AsNoTracking()
            .Where(p => raices.Contains(p.RootId))
            .Select(p => new { p.RootId, p.ParentId, p.CreatedAtUtc, p.DeletedAtUtc })
            .ToListAsync(ct);

        var conteoComentarios = porHilo
            .Where(p => p.ParentId != null && p.DeletedAtUtc == null)
            .GroupBy(p => p.RootId)
            .ToDictionary(g => g.Key, g => g.Count());

        var ultimaActividad = porHilo
            .GroupBy(p => p.RootId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAtUtc));

        var likes = await db.ForumLikes.AsNoTracking()
            .Where(l => raices.Contains(l.PostId))
            .Select(l => new { l.PostId, l.UserId })
            .ToListAsync(ct);
        var conteoLikes = likes.GroupBy(l => l.PostId).ToDictionary(g => g.Key, g => g.Count());
        var mios = likes.Where(l => l.UserId == userId).Select(l => l.PostId).ToHashSet();

        // Solo el número: las miniaturas del muro se piden aparte y únicamente para lo que se ve.
        var conteoImagenes = (await db.ForumAttachments.AsNoTracking()
            .Where(a => raices.Contains(a.PostId))
            .GroupBy(a => a.PostId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);

        return publicaciones
            .Select(p => new ForumTarjeta(
                p,
                conteoComentarios.TryGetValue(p.Id, out var c) ? c : 0,
                conteoLikes.TryGetValue(p.Id, out var m) ? m : 0,
                mios.Contains(p.Id),
                ultimaActividad.TryGetValue(p.Id, out var u) ? u : p.CreatedAtUtc,
                // Una entrada retirada no enseña sus imágenes ni las anuncia.
                p.Eliminado ? 0 : conteoImagenes.TryGetValue(p.Id, out var i) ? i : 0))
            .OrderByDescending(t => t.Post.Pinned)
            .ThenByDescending(t => t.UltimaActividadUtc)
            .Take(tope)
            .ToList();
    }

    /// <summary>
    /// Un hilo completo, aplanado en orden de lectura: cada comentario justo debajo de aquello a lo
    /// que responde, y los hermanos por fecha. Es lo que hace que una conversación se lea como una
    /// conversación y no como una lista suelta de mensajes.
    /// </summary>
    public async Task<List<ForumNodo>> HiloAsync(int rootId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;

        var entradas = await db.ForumPosts.AsNoTracking()
            .Where(p => p.RootId == rootId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        if (entradas.Count == 0) return [];

        var ids = entradas.Select(e => e.Id).ToList();
        var likes = await db.ForumLikes.AsNoTracking()
            .Where(l => ids.Contains(l.PostId))
            .Select(l => new { l.PostId, l.UserId })
            .ToListAsync(ct);
        var conteo = likes.GroupBy(l => l.PostId).ToDictionary(g => g.Key, g => g.Count());
        var mios = likes.Where(l => l.UserId == userId).Select(l => l.PostId).ToHashSet();

        return ForumFilter.Aplanar(entradas)
            .Select(x => new ForumNodo(
                x.Post, x.Nivel,
                conteo.TryGetValue(x.Post.Id, out var n) ? n : 0,
                mios.Contains(x.Post.Id)))
            .ToList();
    }

    /// <summary>
    /// Todas las entradas (publicaciones y comentarios) para la pestaña de auditoría. SOLO
    /// administrador: la vista consolidada del rastro —quién retiró qué, qué se editó, en todo el
    /// foro y con búsqueda— es una herramienta de supervisión, no de participación. El rastro
    /// DENTRO de un hilo (el hueco del retirado, la marca de editado) sigue siendo de todos vía
    /// <see cref="HiloAsync"/>: eso es contexto de la conversación.
    /// </summary>
    public async Task<List<ForumPost>> AuditoriaAsync(ForumFiltro? filtro = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        var userId = currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();

        var todas = await db.ForumPosts.AsNoTracking().ToListAsync(ct);
        return ForumFilter.Aplicar(todas, filtro, userId, DateTime.UtcNow)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToList();
    }

    public Task<ForumPost?> ObtenerAsync(int postId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return db.ForumPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId, ct);
    }

    // ── Imágenes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las imágenes de un conjunto de entradas, agrupadas por entrada y CON LA MINIATURA, no con el
    /// original: es lo que se pinta en el hilo. Las entradas retiradas no devuelven ninguna.
    /// </summary>
    public async Task<Dictionary<int, List<ForumImagen>>> ImagenesDeAsync(
        IEnumerable<int> postIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var ids = postIds as IReadOnlyCollection<int> ?? postIds.ToList();
        if (ids.Count == 0) return [];

        // Solo las de entradas vivas: si una captura se siguiera viendo después de retirar la
        // entrada, retirarla no serviría de nada — la misma razón por la que se oculta el texto.
        var vivas = (await db.ForumPosts.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.DeletedAtUtc == null)
            .Select(p => p.Id)
            .ToListAsync(ct))
            .ToHashSet();
        if (vivas.Count == 0) return [];

        return (await db.ForumAttachments.AsNoTracking()
            .Where(a => vivas.Contains(a.PostId))
            .OrderBy(a => a.PostId).ThenBy(a => a.Orden).ThenBy(a => a.Id)
            .Select(a => new ForumImagen(a.Id, a.PostId, a.FileName, a.ContentType, a.SizeBytes, a.Width, a.Height, a.Thumb))
            .ToListAsync(ct))
            .GroupBy(a => a.PostId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Las imágenes de una sola entrada, para el formulario de edición.</summary>
    public async Task<List<ForumImagen>> ImagenesDeEntradaAsync(int postId, CancellationToken ct = default) =>
        (await ImagenesDeAsync([postId], ct)).TryGetValue(postId, out var l) ? l : [];

    /// <summary>
    /// Cuántas imágenes tiene cada entrada, sin traer ni miniaturas. Es lo que necesita una rejilla
    /// —la de auditoría— donde solo hay que saber que las hay, no enseñarlas.
    /// </summary>
    public async Task<Dictionary<int, int>> ConteoImagenesAsync(
        IEnumerable<int> postIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var ids = postIds as IReadOnlyCollection<int> ?? postIds.ToList();
        if (ids.Count == 0) return [];

        return (await db.ForumAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.PostId))
            .GroupBy(a => a.PostId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
    }

    /// <summary>
    /// El original de una imagen, para abrirla o descargarla. Vacío si no existe o si su entrada está
    /// retirada — el mismo criterio que <see cref="ImagenesDeAsync"/>, comprobado también aquí porque
    /// este es el camino por el que salen los bytes de verdad.
    ///
    /// Devuelve los BYTES y no escribe nada: en la web quien los sirve es el endpoint, que debe
    /// mandarlos con el <c>tipo</c> devuelto y con <c>X-Content-Type-Options: nosniff</c>.
    /// </summary>
    public async Task<(byte[] bytes, string nombre, string tipo)> BytesDeImagenAsync(
        int imagenId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var img = await db.ForumAttachments.AsNoTracking()
            .Where(a => a.Id == imagenId)
            .Select(a => new { a.Bytes, a.FileName, a.ContentType, a.PostId })
            .FirstOrDefaultAsync(ct);
        if (img == null) return ([], "", "");

        bool viva = await db.ForumPosts.AsNoTracking().AnyAsync(p => p.Id == img.PostId && p.DeletedAtUtc == null, ct);
        return viva ? (img.Bytes, img.FileName, img.ContentType) : ([], "", "");
    }

    /// <summary>
    /// Comprueba que lo adjuntado sea de verdad una imagen y quepa. El tipo se decide por los BYTES:
    /// con la extensión bastaría llamar «captura.png» a un HTML para colarlo, y estas cosas se acaban
    /// sirviendo desde el mismo origen que la aplicación, donde un archivo que el navegador
    /// interprete como marcado se ejecuta con la sesión de quien lo abre.
    /// </summary>
    private static (bool ok, string mensaje) ValidarImagenes(IReadOnlyList<ForumImagenNueva>? imagenes, int yaTiene)
    {
        if (imagenes == null || imagenes.Count == 0) return (true, "");

        if (yaTiene + imagenes.Count > MaxImagenes)
            return (false, $"No se pueden poner más de {MaxImagenes} imágenes en una entrada.");

        foreach (var i in imagenes)
        {
            if (i.Bytes.Length == 0) return (false, $"«{i.NombreArchivo}» está vacía.");
            if (i.Bytes.Length > MaxBytesImagen)
                return (false, $"«{i.NombreArchivo}» pesa {ForumMedia.Tamano(i.Bytes.Length)} y el tope es {ForumMedia.Tamano(MaxBytesImagen)}.");
            if (ForumMedia.TipoDeImagen(i.Bytes) == null)
                return (false, $"«{i.NombreArchivo}» no es una imagen (se admiten PNG, JPG, GIF y BMP).");
        }
        return (true, "");
    }

    private async Task GuardarImagenesAsync(int postId, IReadOnlyList<ForumImagenNueva>? imagenes,
        int desdeOrden, CancellationToken ct)
    {
        if (imagenes == null || imagenes.Count == 0) return;

        for (int i = 0; i < imagenes.Count; i++)
        {
            var img = imagenes[i];
            db.ForumAttachments.Add(new ForumAttachment
            {
                PostId           = postId,
                FileName         = NombreLimpio(img.NombreArchivo),
                ContentType      = ForumMedia.TipoDeImagen(img.Bytes) ?? "image/png",
                Bytes            = img.Bytes,
                // Sin miniatura, la del hilo sería el original: se pinta el original y ya está.
                Thumb            = img.Miniatura.Length > 0 ? img.Miniatura : img.Bytes,
                SizeBytes        = img.Bytes.Length,
                Width            = img.Ancho,
                Height           = img.Alto,
                Orden            = desdeOrden + i,
                UploadedByUserId = currentUser.UserId ?? 0,
                CreatedAtUtc     = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// El nombre viaja en la cabecera Content-Disposition al descargar la imagen y acaba en el disco
    /// de quien la guarda: nada de rutas ni de caracteres raros. Una barra o unos puntos dobles
    /// aquí serían un intento de escribir fuera de la carpeta de descargas.
    ///
    /// La limpieza es la de <see cref="ArchivosSubidos.NombreSeguro"/>, común a todo lo que se sube:
    /// tener dos versiones era pedir que una se quedara atrás de la otra.
    /// </summary>
    private static string NombreLimpio(string? nombre)
    {
        var n = ArchivosSubidos.NombreSeguro(nombre);
        return n.Length == 0 ? "imagen.png" : n;
    }

    // ── Utilidades ───────────────────────────────────────────────────────────────

    public static string EtiquetaTema(ForumTopic t) => t switch
    {
        ForumTopic.Idea        => "Idea",
        ForumTopic.Pregunta    => "Pregunta",
        ForumTopic.Aprendizaje => "Aprendizaje",
        ForumTopic.Anuncio     => "Anuncio",
        _                      => "Otro"
    };

    public static string IconoTema(ForumTopic t) => t switch
    {
        ForumTopic.Idea        => "💡",
        ForumTopic.Pregunta    => "❓",
        ForumTopic.Aprendizaje => "📗",
        ForumTopic.Anuncio     => "📣",
        _                      => "💬"
    };

    /// <summary>
    /// Nombre con el que firma la entrada. El escritorio leía <c>_currentUser.User!.FullName</c>
    /// porque tenía la entidad cargada en memoria; aquí la identidad de la petición ya trae el
    /// nombre en los claims y no hace falta ir a la base para firmar un mensaje.
    /// </summary>
    private string NombreAutor() =>
        currentUser.FullName is { Length: > 0 } n ? n
        : currentUser.Username ?? $"Usuario #{currentUser.UserId}";

    /// <summary>
    /// Normaliza los saltos de línea y pasa el cuerpo por el punto de saneado. Hoy
    /// <see cref="ForumRichText.Sanear"/> devuelve el texto tal cual (no hay librería todavía): la
    /// llamada está aquí para que el día que se implemente, TODO cuerpo que entra al foro quede
    /// cubierto sin tener que buscar cada camino de escritura.
    /// </summary>
    private static string NormalizarCuerpo(string? cuerpo) =>
        ForumRichText.Sanear((cuerpo ?? "").Replace("\r\n", "\n").Trim());

    private static string? NormalizarEtiquetas(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limpias = tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Where(e => vistas.Add(e));
        var r = string.Join(", ", limpias);
        if (r.Length > MaxEtiquetas) r = r[..MaxEtiquetas];
        return r.Length == 0 ? null : r;
    }
}
