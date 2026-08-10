using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Foro;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lectura del foro para la web: el muro paginado, un hilo completo y la rejilla de auditoría.
///
/// Existe aparte de <see cref="ForumService"/> —que sigue siendo el dueño de publicar, comentar,
/// editar y retirar— por dos motivos:
///
///  1. <b>Paginación en el servidor.</b> El muro del escritorio se traía TODAS las publicaciones y
///     filtraba en memoria: con un foro de un mes funciona y con el de un año deja de hacerlo, y en
///     la web cada refresco es tráfico de red. Aquí el filtro, el orden y el recorte los hace SQL, y
///     los contadores (comentarios, ❤, imágenes) se piden solo para las tarjetas de la página que se
///     está viendo.
///  2. <b>Nada de entidades hacia el cliente.</b> Este servicio devuelve DTOs, y esa frontera es la
///     que garantiza las dos reglas de abajo.
///
/// <b>DOS COSAS QUE NO SE PUEDEN PERDER:</b>
///
///  · <b>El cuerpo sale troceado, no en crudo.</b> Se analiza aquí con <see cref="ForumRichText"/> y
///    solo los enlaces http/https válidos llegan al cliente como enlace; el resto viaja como texto.
///    En el escritorio esa validación evitaba que una publicación abriera cualquier programa; en la
///    web evita que ejecute código en el navegador de todo el que lea el hilo. El cliente no
///    interpreta nada: pinta lo que recibe.
///  · <b>Lo retirado no desaparece, pero su texto no sale.</b> Se sigue viendo el hueco en el hilo
///    (si desapareciera, las respuestas que le contestan dejarían de tener sentido) con el aviso de
///    contenido eliminado en lugar del original — que no se devuelve nunca, ni al administrador.
///    Sus imágenes tampoco se anuncian, por la misma razón por la que se ocultan en
///    <see cref="ForumService.ImagenesDeAsync"/>.
///
/// Solo lectura: aquí no se escribe nada.
/// </summary>
public class ForoQueryService(AppDbContext db, ICurrentUser currentUser)
{
    public const int TamanoPaginaPorOmision = 10;

    /// <summary>Tope duro del tamaño de página. Sin él, <c>?tamano=100000</c> anula la paginación.</summary>
    public const int TamanoPaginaMaximo = 50;

    /// <summary>Lo que cabe en una tarjeta del muro sin convertirla en un muro de texto.</summary>
    private const int LargoExtracto = 220;

    // ── El muro ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las publicaciones de primer nivel con sus contadores. Las fijadas primero; el resto, por
    /// última actividad —no por fecha de creación—, para que un hilo que revive vuelva a subir.
    /// </summary>
    public async Task<PaginaDto<ForoTarjetaDto>> MuroAsync(
        ForumFiltro? filtro = null, int pagina = 1, int tamano = TamanoPaginaPorOmision,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();
        (pagina, tamano) = Acotar(pagina, tamano);

        var consulta = Filtrar(db.ForumPosts.AsNoTracking().Where(p => p.ParentId == null), filtro, userId);

        int total = await consulta.CountAsync(ct);
        if (total == 0) return new PaginaDto<ForoTarjetaDto>([], 0, pagina, tamano);

        // La última actividad se calcula en SQL como el máximo del hilo (que incluye a la propia
        // raíz, porque su RootId es su Id). El «??» cubre los hilos viejos que quedaron sin RootId
        // bien puesto: sin él se ordenarían como si no existieran.
        var filas = await consulta
            .Select(p => new
            {
                Post = p,
                Ultima = db.ForumPosts.Where(c => c.RootId == p.Id).Max(c => (DateTime?)c.CreatedAtUtc)
            })
            .OrderByDescending(x => x.Post.Pinned)
            .ThenByDescending(x => x.Ultima ?? x.Post.CreatedAtUtc)
            .ThenByDescending(x => x.Post.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        var ids = filas.Select(f => f.Post.Id).ToList();
        var comentarios = await ComentariosPorHiloAsync(ids, ct);
        var (meGusta, mios) = await MeGustaAsync(ids, userId, ct);
        var imagenes = await ImagenesPorEntradaAsync(ids, ct);

        var tarjetas = filas.Select(f => new ForoTarjetaDto(
            f.Post.Id,
            f.Post.Title ?? "",
            Extracto(f.Post.TextoVisible),
            f.Post.Topic,
            ForumService.IconoTema(f.Post.Topic),
            ForumService.EtiquetaTema(f.Post.Topic),
            f.Post.Tags,
            f.Post.AuthorName,
            f.Post.AuthorUserId == userId,
            f.Post.CreatedAtUtc,
            f.Ultima ?? f.Post.CreatedAtUtc,
            f.Post.Pinned,
            f.Post.Locked,
            f.Post.Eliminado,
            comentarios.TryGetValue(f.Post.Id, out var c) ? c : 0,
            meGusta.TryGetValue(f.Post.Id, out var m) ? m : 0,
            mios.Contains(f.Post.Id),
            // Una entrada retirada no enseña sus imágenes ni las anuncia.
            f.Post.Eliminado ? 0 : imagenes.TryGetValue(f.Post.Id, out var i) ? i : 0))
            .ToList();

        return new PaginaDto<ForoTarjetaDto>(tarjetas, total, pagina, tamano);
    }

    // ── Un hilo ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un hilo completo, aplanado en orden de lectura. Devuelve null si la publicación no existe.
    ///
    /// No se pagina a propósito: un hilo es una conversación y se lee entera; cortarlo por la mitad
    /// dejaría respuestas colgando de un padre que no se ve. Lo que crece sin límite es el muro, y
    /// ese sí va paginado.
    /// </summary>
    public async Task<ForoHiloDto?> HiloAsync(int rootId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;

        var entradas = await db.ForumPosts.AsNoTracking()
            .Where(p => p.RootId == rootId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        // Red de seguridad para los hilos viejos que quedaron sin RootId: sin esto, abrir una
        // publicación anterior al cambio devolvería «no existe» aunque esté ahí.
        if (entradas.Count == 0)
        {
            var suelta = await db.ForumPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == rootId, ct);
            if (suelta == null) return null;
            entradas = [suelta];
        }

        var raiz = entradas.FirstOrDefault(p => p.Id == rootId)
                   ?? entradas.FirstOrDefault(p => p.ParentId == null)
                   ?? entradas[0];

        var ids = entradas.Select(p => p.Id).ToList();
        var (meGusta, mios) = await MeGustaAsync(ids, userId, ct);
        var imagenes = await ImagenesPorEntradaAsync(ids, ct);

        var lista = ForumFilter.Aplanar(entradas)
            .Select(x => new ForoEntradaDto(
                x.Post.Id,
                x.Post.ParentId,
                // La sangría se corta a los cinco niveles: más allá la conversación se va al margen
                // derecho y deja de leerse. Es la decisión que ya documentaba el escritorio.
                Math.Min(x.Nivel, ForumService.ProfundidadMaxima),
                x.Post.EsPublicacion,
                x.Post.EsPublicacion ? x.Post.Title : null,
                Cuerpo(x.Post),
                x.Post.AuthorName,
                x.Post.AuthorUserId == userId,
                x.Post.CreatedAtUtc,
                x.Post.EditedAtUtc,
                x.Post.Eliminado,
                meGusta.TryGetValue(x.Post.Id, out var m) ? m : 0,
                mios.Contains(x.Post.Id),
                x.Post.Eliminado ? 0 : imagenes.TryGetValue(x.Post.Id, out var i) ? i : 0))
            .ToList();

        return new ForoHiloDto(
            raiz.Id,
            raiz.Title ?? "",
            raiz.Topic,
            ForumService.IconoTema(raiz.Topic),
            ForumService.EtiquetaTema(raiz.Topic),
            raiz.Tags,
            raiz.Pinned,
            raiz.Locked,
            raiz.Eliminado,
            entradas.Count(p => p.ParentId != null && p.DeletedAtUtc == null),
            lista);
    }

    // ── Auditoría (solo administrador) ───────────────────────────────────────────

    /// <summary>
    /// Todas las entradas —publicaciones y comentarios— para la rejilla de auditoría.
    ///
    /// SOLO administrador, y aquí también, no solo en el endpoint: la vista consolidada del rastro
    /// (quién retiró qué, qué se editó, en todo el foro y con búsqueda) es una herramienta de
    /// supervisión, no de participación. El rastro DENTRO de un hilo —el hueco de lo retirado, la
    /// marca de editado— sigue siendo de todos vía <see cref="HiloAsync"/>: eso es contexto de la
    /// conversación, no un expediente.
    /// </summary>
    public async Task<PaginaDto<ForoAuditoriaFilaDto>> AuditoriaAsync(
        ForumFiltro? filtro = null, int pagina = 1, int tamano = 20, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        var userId = currentUser.UserId ?? -1;
        filtro ??= new ForumFiltro();
        (pagina, tamano) = Acotar(pagina, tamano);

        var consulta = Filtrar(db.ForumPosts.AsNoTracking(), filtro, userId);

        int total = await consulta.CountAsync(ct);
        if (total == 0) return new PaginaDto<ForoAuditoriaFilaDto>([], 0, pagina, tamano);

        var entradas = await consulta
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        // El título del hilo de cada fila, para que un comentario no salga huérfano en la rejilla.
        // Se piden aparte porque el filtro pudo dejar fuera la raíz aunque el comentario sí se vea.
        var hilos = entradas.Select(p => p.RootId).Distinct().ToList();
        var titulos = await db.ForumPosts.AsNoTracking()
            .Where(p => hilos.Contains(p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title ?? "", ct);

        var imagenes = await ImagenesPorEntradaAsync(entradas.Select(p => p.Id).ToList(), ct);

        var filas = entradas.Select(p =>
        {
            var texto = Extracto(p.TextoVisible);
            if (!p.Eliminado && imagenes.TryGetValue(p.Id, out var n) && n > 0)
                texto = texto.Length == 0 ? $"🖼 {n} imagen(es)" : $"🖼 {n}  ·  {texto}";

            return new ForoAuditoriaFilaDto(
                p.Id,
                p.RootId,
                p.CreatedAtUtc,
                p.AuthorName,
                p.EsPublicacion,
                p.EsPublicacion ? $"{ForumService.IconoTema(p.Topic)} Publicación" : "↩ Comentario",
                p.EsPublicacion ? (p.Title ?? "") : (titulos.TryGetValue(p.RootId, out var t) ? t : $"#{p.RootId}"),
                texto,
                p.Eliminado ? "🗑 Retirada" : p.EditedAtUtc != null ? "✏ Editada" : "",
                p.Eliminado,
                p.EditedAtUtc != null);
        }).ToList();

        return new PaginaDto<ForoAuditoriaFilaDto>(filas, total, pagina, tamano);
    }

    // ── Opciones de los filtros ──────────────────────────────────────────────────

    /// <summary>
    /// Temas y autores para los desplegables del muro. Los autores son los de las PUBLICACIONES:
    /// ofrecer a quien solo ha comentado dejaría un filtro que siempre devuelve el muro vacío.
    /// </summary>
    public async Task<ForoOpcionesDto> OpcionesDelMuroAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return new ForoOpcionesDto(Temas(), await AutoresAsync(soloPublicaciones: true, ct));
    }

    /// <summary>
    /// Lo mismo para la rejilla de auditoría, donde el combo de autores sí incluye a quien solo ha
    /// comentado: ahí lo que se busca es una entrada concreta, no un hilo.
    /// </summary>
    public async Task<ForoOpcionesDto> OpcionesDeAuditoriaAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        return new ForoOpcionesDto(Temas(), await AutoresAsync(soloPublicaciones: false, ct));
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// El mismo criterio que <see cref="ForumFilter.Aplicar"/>, pero escrito de forma que lo pueda
    /// ejecutar SQL: filtrar en memoria obligaría a traerse el foro entero antes de recortar la
    /// página, que es justo lo que la paginación viene a evitar.
    ///
    /// La comparación no puede llevar <c>IgnoreCase</c> —EF no lo traduce—, así que se comparan
    /// ambos lados en minúsculas. Podría dejarse a la intercalación de la base, que en SQL Server es
    /// insensible por omisión, pero entonces buscar «índices» encontraría o no «Índices» según cómo
    /// esté creada la columna, y eso es un comportamiento que no se ve venir hasta que alguien se
    /// queja. El coste es nulo: un <c>LIKE '%texto%'</c> no usa índice de todos modos.
    /// </summary>
    private static IQueryable<ForumPost> Filtrar(IQueryable<ForumPost> consulta, ForumFiltro filtro, int userId)
    {
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var texto = filtro.Texto.Trim().ToLower();
            consulta = consulta.Where(p =>
                (p.Title != null && p.Title.ToLower().Contains(texto))
                // El cuerpo de una entrada retirada NO se busca: si se pudiera encontrar por su
                // texto, retirarla no serviría de nada.
                || (p.DeletedAtUtc == null && p.Body.ToLower().Contains(texto))
                || (p.Tags != null && p.Tags.ToLower().Contains(texto))
                || p.AuthorName.ToLower().Contains(texto));
        }

        if (filtro.Tema is ForumTopic tema)
            consulta = consulta.Where(p => p.Topic == tema);

        if (!string.IsNullOrWhiteSpace(filtro.Autor))
        {
            // El autor sí se compara entero (viene de un desplegable, no lo teclea nadie), pero
            // también sin distinguir mayúsculas: el nombre se guarda tal como estaba el día que se
            // publicó y pudo cambiar de forma desde entonces.
            var autor = filtro.Autor.Trim().ToLower();
            consulta = consulta.Where(p => p.AuthorName.ToLower() == autor);
        }

        if (filtro.SoloMios)
            consulta = consulta.Where(p => p.AuthorUserId == userId);

        if (filtro.UltimosDias is int dias && dias > 0)
        {
            var desde = DateTime.UtcNow.AddDays(-dias);
            consulta = consulta.Where(p => p.CreatedAtUtc >= desde);
        }

        return consulta;
    }

    private async Task<Dictionary<int, int>> ComentariosPorHiloAsync(List<int> raices, CancellationToken ct) =>
        raices.Count == 0 ? [] :
        (await db.ForumPosts.AsNoTracking()
            .Where(p => raices.Contains(p.RootId) && p.ParentId != null && p.DeletedAtUtc == null)
            .GroupBy(p => p.RootId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.N);

    /// <summary>
    /// El total de «me gusta» de cada entrada y cuáles llevan el propio. Van en dos consultas y no
    /// en una con condicional dentro del agrupado: la forma con condicional no siempre la traduce
    /// EF, y una excepción de traducción aquí dejaría el muro entero sin pintar.
    /// </summary>
    private async Task<(Dictionary<int, int> Totales, HashSet<int> Mios)> MeGustaAsync(
        List<int> ids, int userId, CancellationToken ct)
    {
        if (ids.Count == 0) return ([], []);

        var totales = (await db.ForumLikes.AsNoTracking()
            .Where(l => ids.Contains(l.PostId))
            .GroupBy(l => l.PostId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.N);

        var mios = (await db.ForumLikes.AsNoTracking()
            .Where(l => ids.Contains(l.PostId) && l.UserId == userId)
            .Select(l => l.PostId)
            .ToListAsync(ct))
            .ToHashSet();

        return (totales, mios);
    }

    /// <summary>
    /// Cuántas imágenes tiene cada entrada. Solo el número: en fase 1 el binario no viaja, y aun
    /// cuando viaje lo hará por su propio endpoint de descarga y nunca dentro de un DTO.
    /// </summary>
    private async Task<Dictionary<int, int>> ImagenesPorEntradaAsync(List<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] :
        (await db.ForumAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.PostId))
            .GroupBy(a => a.PostId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.N);

    private async Task<List<string>> AutoresAsync(bool soloPublicaciones, CancellationToken ct)
    {
        var consulta = db.ForumPosts.AsNoTracking().AsQueryable();
        if (soloPublicaciones) consulta = consulta.Where(p => p.ParentId == null);

        var nombres = await consulta
            .Select(p => p.AuthorName)
            .Distinct()
            .ToListAsync(ct);

        // El ordenado se hace aquí y no en SQL: el criterio cultural del escritorio (acentos, ñ) no
        // tiene por qué coincidir con la intercalación de la base.
        return nombres
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<OpcionDto> Temas() =>
        Enum.GetValues<ForumTopic>()
            .Select(t => new OpcionDto((int)t, $"{ForumService.IconoTema(t)}  {ForumService.EtiquetaTema(t)}"))
            .ToList();

    /// <summary>
    /// El cuerpo ya troceado. Una entrada retirada devuelve UN solo trozo con el aviso y sin
    /// analizar nada: su texto original no sale de aquí, ni siquiera para buscarle enlaces.
    /// </summary>
    private static IReadOnlyList<ForoSegmentoDto> Cuerpo(ForumPost p) =>
        p.Eliminado
            ? [new ForoSegmentoDto(p.TextoVisible, null)]
            : ForumRichText.Analizar(p.Body).Select(s => new ForoSegmentoDto(s.Texto, s.Url)).ToList();

    private static string Extracto(string cuerpo)
    {
        var plano = cuerpo.Replace("\r", " ").Replace("\n", " ").Trim();
        return plano.Length <= LargoExtracto ? plano : plano[..LargoExtracto] + "…";
    }

    private static (int Pagina, int Tamano) Acotar(int pagina, int tamano) =>
        (Math.Max(1, pagina), Math.Clamp(tamano, 1, TamanoPaginaMaximo));
}
