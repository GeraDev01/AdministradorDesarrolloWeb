using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

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
/// Una imagen lista para adjuntar. La preparan los formularios (recortar, reescalar y sacar la
/// miniatura son cosa de System.Drawing); el servicio solo comprueba que sea lo que dice ser.
/// </summary>
public record ForumImagenNueva(string NombreArchivo, byte[] Bytes, byte[] Miniatura, int Ancho, int Alto);

/// <summary>
/// Una imagen ya publicada, SIN el original: solo la miniatura. El original se pide aparte con
/// <see cref="ForumService.BytesDeImagen"/> y solo cuando alguien la abre.
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
/// alguien tiene que poder parar un hilo que se descarrila. La excepción es <see cref="Auditoria"/>
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
/// </summary>
public class ForumService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public ForumService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

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

    public (bool ok, string mensaje, ForumPost? post) Publicar(
        string? titulo, string? cuerpo, ForumTopic tema, string? etiquetas = null,
        IReadOnlyList<ForumImagenNueva>? imagenes = null)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

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
            AuthorDeveloperId = _currentUser.DeveloperId,
            Title             = titulo,
            Body              = cuerpo,
            Topic             = tema,
            Tags              = NormalizarEtiquetas(etiquetas),
            CreatedAtUtc      = DateTime.UtcNow
        };
        _db.ForumPosts.Add(post);
        _db.SaveChanges();

        // RootId apunta a sí misma: es la raíz de su propio hilo.
        post.RootId = post.Id;
        _db.SaveChanges();

        GuardarImagenes(post.Id, imagenes, 0);

        _audit.Record(AuditAction.Create, "ForumPost", post.Id.ToString(),
            $"Publicación en el foro: {titulo}" + (cuantasImagenes > 0 ? $" ({cuantasImagenes} imagen/es)" : ""));
        return (true, "Publicado.", post);
    }

    public (bool ok, string mensaje, ForumPost? comentario) Comentar(
        int parentId, string? cuerpo, IReadOnlyList<ForumImagenNueva>? imagenes = null)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        cuerpo = NormalizarCuerpo(cuerpo);
        int cuantasImagenes = imagenes?.Count ?? 0;

        // Una captura sola es un comentario legítimo: «así se ve el error».
        if (cuerpo.Length < 1 && cuantasImagenes == 0) return (false, "Escribe tu comentario o adjunta una imagen.", null);
        if (cuerpo.Length > MaxCuerpo) return (false, $"El comentario no puede pasar de {MaxCuerpo:N0} caracteres.", null);

        var (imgOk, imgMensaje) = ValidarImagenes(imagenes, 0);
        if (!imgOk) return (false, imgMensaje, null);

        var padre = _db.ForumPosts.AsNoTracking().FirstOrDefault(p => p.Id == parentId);
        if (padre == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.", null);

        // Se comprueba sobre la RAÍZ: cerrar un hilo tiene que cerrarlo entero, no solo su primer
        // mensaje. Si no, se seguiría respondiendo por dentro a un hilo dado por cerrado.
        var raiz = padre.RootId == padre.Id ? padre : _db.ForumPosts.AsNoTracking().FirstOrDefault(p => p.Id == padre.RootId);
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
            AuthorDeveloperId = _currentUser.DeveloperId,
            Body              = cuerpo,
            Topic             = padre.Topic,
            CreatedAtUtc      = DateTime.UtcNow
        };
        _db.ForumPosts.Add(comentario);
        _db.SaveChanges();

        GuardarImagenes(comentario.Id, imagenes, 0);

        _audit.Record(AuditAction.Create, "ForumPost", comentario.Id.ToString(),
            $"Comentario en el hilo #{padre.RootId}" + (cuantasImagenes > 0 ? $" ({cuantasImagenes} imagen/es)" : ""));
        return (true, "Comentario publicado.", comentario);
    }

    // ── Editar, retirar, fijar, cerrar ───────────────────────────────────────────

    public (bool ok, string mensaje) Editar(
        int postId, string? titulo, string? cuerpo,
        IReadOnlyList<ForumImagenNueva>? imagenesNuevas = null, IReadOnlyList<int>? quitarImagenes = null)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var post = Fresco(postId);
        if (post == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.");
        if (post.Eliminado) return (false, "Esa entrada está retirada.");
        if (post.AuthorUserId != _currentUser.UserId)
            return (false, "Solo puedes editar lo que tú escribiste.");

        cuerpo = NormalizarCuerpo(cuerpo);

        // Las que se quitan son solo las de ESTA entrada: un id de otra publicación se ignora en vez
        // de dejar que alguien borre por ahí las imágenes de un tercero.
        var aQuitar = quitarImagenes is { Count: > 0 }
            ? _db.ForumAttachments.Where(a => a.PostId == postId && quitarImagenes.Contains(a.Id)).ToList()
            : [];

        int yaTiene = _db.ForumAttachments.Count(a => a.PostId == postId) - aQuitar.Count;
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
        if (aQuitar.Count > 0) _db.ForumAttachments.RemoveRange(aQuitar);
        _db.SaveChanges();

        // Max() sobre int? y no DefaultIfEmpty(-1): esa forma no la sabe traducir EF y reventaba al
        // editar CUALQUIER entrada, llevara imágenes o no.
        int siguiente = (_db.ForumAttachments.Where(a => a.PostId == postId).Max(a => (int?)a.Orden) ?? -1) + 1;
        GuardarImagenes(postId, imagenesNuevas, siguiente);

        var detalle = "Entrada del foro editada";
        if (aQuitar.Count > 0) detalle += $"; {aQuitar.Count} imagen/es quitada/s";
        if (imagenesNuevas is { Count: > 0 }) detalle += $"; {imagenesNuevas.Count} imagen/es añadida/s";
        _audit.Record(AuditAction.Update, "ForumPost", post.Id.ToString(), detalle);
        return (true, "Editado.");
    }

    /// <summary>
    /// Retira una entrada: se conserva la fila y el hueco en el hilo, pero deja de mostrarse el
    /// texto. Su autor siempre; el administrador, cualquiera.
    /// </summary>
    public (bool ok, string mensaje) Retirar(int postId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var post = Fresco(postId);
        if (post == null) return (false, "Esa entrada ya no existe. Actualiza el hilo.");
        if (post.Eliminado) return (true, "Esa entrada ya estaba retirada.");
        if (post.AuthorUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return (false, "Solo puedes retirar lo que tú escribiste.");

        post.DeletedAtUtc = DateTime.UtcNow;
        post.DeletedByUserId = _currentUser.UserId;
        _db.SaveChanges();

        _audit.Record(AuditAction.Delete, "ForumPost", post.Id.ToString(),
            post.AuthorUserId == _currentUser.UserId
                ? "Entrada del foro retirada por su autor"
                : $"Entrada del foro retirada por un administrador (autor: {post.AuthorName})");
        return (true, "Entrada retirada. El hilo conserva el hueco para que se siga entendiendo.");
    }

    /// <summary>Fija o suelta una publicación. Solo administrador: es el muro de todos.</summary>
    public (bool ok, string mensaje) Fijar(int postId, bool fijar)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var post = Fresco(postId);
        if (post == null) return (false, "Esa publicación ya no existe.");
        if (!post.EsPublicacion) return (false, "Solo se fijan publicaciones, no comentarios.");

        post.Pinned = fijar;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "ForumPost", post.Id.ToString(), fijar ? "Publicación fijada" : "Publicación soltada");
        return (true, fijar ? "Fijada arriba del muro." : "Ya no está fijada.");
    }

    /// <summary>Cierra o reabre un hilo. Solo administrador.</summary>
    public (bool ok, string mensaje) Cerrar(int postId, bool cerrar)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var post = Fresco(postId);
        if (post == null) return (false, "Esa publicación ya no existe.");
        if (!post.EsPublicacion) return (false, "Se cierra el hilo completo, no un comentario suelto.");

        post.Locked = cerrar;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "ForumPost", post.Id.ToString(), cerrar ? "Hilo cerrado" : "Hilo reabierto");
        return (true, cerrar ? "Hilo cerrado: ya no admite comentarios." : "Hilo reabierto.");
    }

    /// <summary>Alterna el «me gusta» propio. Devuelve si quedó dado y el total.</summary>
    public (bool ok, bool meGusta, int total) MeGusta(int postId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return (false, false, 0);
        if (!_db.ForumPosts.Any(p => p.Id == postId && p.DeletedAtUtc == null)) return (false, false, 0);

        var existente = _db.ForumLikes.FirstOrDefault(l => l.PostId == postId && l.UserId == userId);
        bool dado;
        if (existente != null) { _db.ForumLikes.Remove(existente); dado = false; }
        else { _db.ForumLikes.Add(new ForumLike { PostId = postId, UserId = userId, CreatedAtUtc = DateTime.UtcNow }); dado = true; }
        _db.SaveChanges();

        return (true, dado, _db.ForumLikes.Count(l => l.PostId == postId));
    }

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// El muro: publicaciones con sus contadores. Las fijadas primero; el resto, por última
    /// actividad —no por fecha de creación—, para que un hilo que revive vuelva a subir.
    /// </summary>
    public List<ForumTarjeta> Muro(ForumFiltro? filtro = null, int tope = 100)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        var userId = _currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();

        var publicaciones = _db.ForumPosts.AsNoTracking()
            .Where(p => p.ParentId == null)
            .ToList();

        publicaciones = ForumFilter.Aplicar(publicaciones, filtro, userId, DateTime.UtcNow);
        if (publicaciones.Count == 0) return [];

        var raices = publicaciones.Select(p => p.Id).ToHashSet();

        // Un solo viaje por los contadores en vez de tres consultas por tarjeta.
        var porHilo = _db.ForumPosts.AsNoTracking()
            .Where(p => raices.Contains(p.RootId))
            .Select(p => new { p.RootId, p.ParentId, p.CreatedAtUtc, p.DeletedAtUtc })
            .ToList();

        var conteoComentarios = porHilo
            .Where(p => p.ParentId != null && p.DeletedAtUtc == null)
            .GroupBy(p => p.RootId)
            .ToDictionary(g => g.Key, g => g.Count());

        var ultimaActividad = porHilo
            .GroupBy(p => p.RootId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAtUtc));

        var likes = _db.ForumLikes.AsNoTracking()
            .Where(l => raices.Contains(l.PostId))
            .Select(l => new { l.PostId, l.UserId })
            .ToList();
        var conteoLikes = likes.GroupBy(l => l.PostId).ToDictionary(g => g.Key, g => g.Count());
        var mios = likes.Where(l => l.UserId == userId).Select(l => l.PostId).ToHashSet();

        // Solo el número: las miniaturas del muro se piden aparte y únicamente para lo que se ve.
        var conteoImagenes = _db.ForumAttachments.AsNoTracking()
            .Where(a => raices.Contains(a.PostId))
            .GroupBy(a => a.PostId)
            .Select(g => new { g.Key, N = g.Count() })
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
    public List<ForumNodo> Hilo(int rootId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        var userId = _currentUser.UserId ?? -1;

        var entradas = _db.ForumPosts.AsNoTracking()
            .Where(p => p.RootId == rootId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToList();
        if (entradas.Count == 0) return [];

        var likes = _db.ForumLikes.AsNoTracking()
            .Where(l => entradas.Select(e => e.Id).Contains(l.PostId))
            .Select(l => new { l.PostId, l.UserId })
            .ToList();
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
    /// <see cref="Hilo"/>: eso es contexto de la conversación.
    /// </summary>
    public List<ForumPost> Auditoria(ForumFiltro? filtro = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var userId = _currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();

        var todas = _db.ForumPosts.AsNoTracking().ToList();
        return ForumFilter.Aplicar(todas, filtro, userId, DateTime.UtcNow)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToList();
    }

    public ForumPost? Obtener(int postId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        return _db.ForumPosts.AsNoTracking().FirstOrDefault(p => p.Id == postId);
    }

    // ── Imágenes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las imágenes de un conjunto de entradas, agrupadas por entrada y CON LA MINIATURA, no con el
    /// original: es lo que se pinta en el hilo. Las entradas retiradas no devuelven ninguna.
    /// </summary>
    public Dictionary<int, List<ForumImagen>> ImagenesDe(IEnumerable<int> postIds)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var ids = postIds as IReadOnlyCollection<int> ?? postIds.ToList();
        if (ids.Count == 0) return [];

        // Solo las de entradas vivas: si una captura se siguiera viendo después de retirar la
        // entrada, retirarla no serviría de nada — la misma razón por la que se oculta el texto.
        var vivas = _db.ForumPosts.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.DeletedAtUtc == null)
            .Select(p => p.Id)
            .ToHashSet();
        if (vivas.Count == 0) return [];

        return _db.ForumAttachments.AsNoTracking()
            .Where(a => vivas.Contains(a.PostId))
            .OrderBy(a => a.PostId).ThenBy(a => a.Orden).ThenBy(a => a.Id)
            .Select(a => new ForumImagen(a.Id, a.PostId, a.FileName, a.ContentType, a.SizeBytes, a.Width, a.Height, a.Thumb))
            .ToList()
            .GroupBy(a => a.PostId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Las imágenes de una sola entrada, para el formulario de edición.</summary>
    public List<ForumImagen> ImagenesDeEntrada(int postId) =>
        ImagenesDe([postId]).TryGetValue(postId, out var l) ? l : [];

    /// <summary>
    /// Cuántas imágenes tiene cada entrada, sin traer ni miniaturas. Es lo que necesita una rejilla
    /// —la de auditoría— donde solo hay que saber que las hay, no enseñarlas.
    /// </summary>
    public Dictionary<int, int> ConteoImagenes(IEnumerable<int> postIds)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var ids = postIds as IReadOnlyCollection<int> ?? postIds.ToList();
        if (ids.Count == 0) return [];

        return _db.ForumAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.PostId))
            .GroupBy(a => a.PostId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionary(x => x.Key, x => x.N);
    }

    /// <summary>
    /// El original de una imagen, para abrirla o guardarla. Vacío si no existe o si su entrada está
    /// retirada — el mismo criterio que <see cref="ImagenesDe"/>, comprobado también aquí porque
    /// este es el camino por el que salen los bytes de verdad.
    /// </summary>
    public (byte[] bytes, string nombre, string tipo) BytesDeImagen(int imagenId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var img = _db.ForumAttachments.AsNoTracking()
            .Where(a => a.Id == imagenId)
            .Select(a => new { a.Bytes, a.FileName, a.ContentType, a.PostId })
            .FirstOrDefault();
        if (img == null) return ([], "", "");

        bool viva = _db.ForumPosts.AsNoTracking().Any(p => p.Id == img.PostId && p.DeletedAtUtc == null);
        return viva ? (img.Bytes, img.FileName, img.ContentType) : ([], "", "");
    }

    /// <summary>
    /// Comprueba que lo adjuntado sea de verdad una imagen y quepa. El tipo se decide por los BYTES:
    /// con la extensión bastaría llamar «captura.png» a un ejecutable para colarlo, y estas cosas se
    /// acaban volcando a un archivo temporal que se abre con el programa asociado.
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

    private void GuardarImagenes(int postId, IReadOnlyList<ForumImagenNueva>? imagenes, int desdeOrden)
    {
        if (imagenes == null || imagenes.Count == 0) return;

        for (int i = 0; i < imagenes.Count; i++)
        {
            var img = imagenes[i];
            _db.ForumAttachments.Add(new ForumAttachment
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
                UploadedByUserId = _currentUser.UserId ?? 0,
                CreatedAtUtc     = DateTime.UtcNow
            });
        }
        _db.SaveChanges();
    }

    /// <summary>El nombre se vuelca a disco al abrir la imagen: nada de rutas ni de caracteres raros.</summary>
    private static string NombreLimpio(string? nombre)
    {
        var n = Path.GetFileName((nombre ?? "").Trim());
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        if (n.Length > 120) n = n[..120];
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

    private string NombreAutor() =>
        _currentUser.User?.FullName is { Length: > 0 } n ? n
        : _currentUser.Username ?? $"Usuario #{_currentUser.UserId}";

    private static string NormalizarCuerpo(string? cuerpo) =>
        (cuerpo ?? "").Replace("\r\n", "\n").Trim();

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

    /// <summary>
    /// Trae la entrada FRESCA soltando el rastreo previo: el AppDbContext es un singleton compartido
    /// y una entidad ya rastreada devolvería valores viejos.
    /// </summary>
    private ForumPost? Fresco(int id)
    {
        var rastreada = _db.ChangeTracker.Entries<ForumPost>().FirstOrDefault(e => e.Entity.Id == id);
        if (rastreada != null) rastreada.State = EntityState.Detached;
        return _db.ForumPosts.FirstOrDefault(p => p.Id == id);
    }
}
