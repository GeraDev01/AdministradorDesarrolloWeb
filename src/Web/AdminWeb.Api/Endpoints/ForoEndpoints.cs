using System.Globalization;
using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Foro;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El foro del EQUIPO DE DESARROLLO: leerlo y escribir en él.
///
/// El muro y los hilos son del líder y de los desarrolladores, y por eso el grupo entero lleva
/// <c>AdminUDesarrollador</c>. Operaciones queda fuera: su alcance son los despliegues, no el trabajo
/// del equipo, y aquí se habla de ideas, dudas y aprendizajes de ese trabajo.
///
/// <para><b>Esto cambió, y conviene saberlo antes de «arreglarlo».</b> Hasta ahora el foro lo veía
/// todo el que tuviera sesión y este grupo no declaraba política propia; esa decisión se revirtió a
/// propósito. Si alguien encuentra un 403 de un operativo y lo toma por una regresión, esto es lo que
/// hay que leer: la barrera está puesta a mano y a conciencia, no heredada por descuido.</para>
///
/// <para><b>Falta la segunda barrera.</b> La casa protege dos veces —política en el endpoint y guarda
/// dentro del servicio— porque a los servicios se les puede llamar desde otro endpoint que no sea
/// éste. <see cref="ForumService"/> y <see cref="ForoQueryService"/> siguen exigiendo solo sesión
/// iniciada, así que hoy lo único que deja fuera a Operaciones es esta línea. Lo que falta es cambiar
/// <c>RequireLoggedIn</c> por <c>RequireAdminOrDesarrollador</c> en los métodos de lectura y escritura
/// de esos dos servicios (no en los que ya son <c>RequireAdmin</c>, que son más estrictos).</para>
///
/// La excepción más estricta es la <b>auditoría</b>, que además lleva
/// <c>RequireAuthorization("SoloAdmin")</c>: la vista consolidada del rastro de todo el foro es
/// supervisión, no participación. Las políticas del grupo y del endpoint se COMPONEN con Y —hay que
/// cumplir las dos—, y como el líder está en las dos, sigue entrando. La misma regla vive además
/// dentro de <see cref="ForoQueryService.AuditoriaAsync"/> — dos barreras, porque a la API se la
/// puede llamar sin pasar por el cliente.
///
/// <para><b>La puerta de atrás.</b> Los bytes de las capturas no salen por aquí sino por
/// <c>GET /api/adjuntos/foro/{id}</c>. Sin cerrar también esa ruta, cualquiera con sesión podría
/// seguir bajándose las imágenes del foro por número aunque el muro le conteste 403. Está cerrada en
/// <see cref="AdjuntosEndpoints"/>; si se toca una de las dos, hay que tocar la otra.</para>
///
/// <para><b>Quién puede tocar qué NO se decide aquí.</b> Editar es solo del autor, retirar es del
/// autor o del administrador, y comentar depende de que el hilo no esté cerrado: todo eso lo
/// comprueba <see cref="ForumService"/> sobre la fila de verdad. Repetir esas reglas en el endpoint
/// solo crearía un segundo sitio donde equivocarse; lo que sí hace el endpoint es traducir el
/// rechazo a un 400 con el mensaje del servicio.</para>
/// </summary>
public static class ForoEndpoints
{
    public static void MapForoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/foro").WithTags("Foro")
                       .RequireAuthorization("AdminUDesarrollador");

        grupo.MapGet("/muro", async (
            ForoQueryService foro,
            CancellationToken ct,
            string? texto = null, int? tema = null, string? autor = null, int? dias = null,
            bool soloMias = false, int pagina = 1, int tamano = ForoQueryService.TamanoPaginaPorOmision) =>
        {
            var filtro = new ForumFiltro(texto, ATema(tema), autor, dias, soloMias);
            return Results.Ok(await foro.MuroAsync(filtro, pagina, tamano, ct));
        })
        .WithSummary("El muro paginado, con filtros de texto, tema, autor y ventana de tiempo");

        grupo.MapGet("/opciones", async (ForoQueryService foro, CancellationToken ct) =>
            Results.Ok(await foro.OpcionesDelMuroAsync(ct)))
        .WithSummary("Temas y autores para los desplegables del muro");

        grupo.MapGet("/hilos/{id:int}", async (int id, ForoQueryService foro, CancellationToken ct) =>
        {
            var hilo = await foro.HiloAsync(id, ct);
            // 404 y no una respuesta vacía: a un hilo se llega por una dirección que alguien pudo
            // compartir, y «ya no existe» tiene que distinguirse de «existe y está vacío».
            return hilo == null
                ? Results.NotFound(new { Detail = "Esa publicación ya no existe. Actualiza el muro." })
                : Results.Ok(hilo);
        })
        .WithSummary("Un hilo completo con sus comentarios anidados");

        // ── Auditoría: SOLO administrador ────────────────────────────────────────
        grupo.MapGet("/auditoria", async (
            ForoQueryService foro,
            CancellationToken ct,
            string? texto = null, int? tema = null, string? autor = null, int? dias = null,
            int pagina = 1, int tamano = 20) =>
        {
            var filtro = new ForumFiltro(texto, ATema(tema), autor, dias);
            return Results.Ok(await foro.AuditoriaAsync(filtro, pagina, tamano, ct));
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Rastro de todo el foro, paginado (solo administrador)");

        grupo.MapGet("/auditoria/opciones", async (ForoQueryService foro, CancellationToken ct) =>
            Results.Ok(await foro.OpcionesDeAuditoriaAsync(ct)))
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Temas y autores para los filtros de la auditoría (solo administrador)");

        // ── Las capturas de un hilo ──────────────────────────────────────────────
        //
        // Va aparte del hilo, y en una sola llamada para todas sus entradas, por lo mismo que el muro
        // no trae miniaturas: un hilo con capturas se recarga cada vez que alguien comenta, y meter
        // los binarios dentro del DTO haría que se volvieran a bajar enteros en cada refresco. Aquí
        // viajan solo los identificadores; los bytes los pide el navegador a /api/adjuntos/foro/{id},
        // que ya existe y los cachea.
        grupo.MapGet("/imagenes", async (
            string? entradas, ForumService foro, CancellationToken ct) =>
        {
            var ids = Numeros(entradas, MaxEntradasPorConsulta);
            if (ids.Count == 0) return Results.Ok(Array.Empty<ForoImagenDto>());

            var porEntrada = await foro.ImagenesDeAsync(ids, ct);
            return Results.Ok(porEntrada.Values
                .SelectMany(imagenes => imagenes)
                .Select(i => new ForoImagenDto(i.Id, i.PostId, i.NombreArchivo, i.Ancho, i.Alto))
                .ToList());
        })
        .WithSummary("Las capturas de un conjunto de entradas, sin los bytes (van por /api/adjuntos)");

        // ── Escritura ────────────────────────────────────────────────────────────
        //
        // Publicar, comentar y editar van por multipart SIEMPRE, lleven o no imágenes. Podrían ir en
        // JSON cuando no las llevan, pero entonces la misma operación tendría dos formas y habría que
        // acertar con cuál mandar; una sola forma es una menos donde equivocarse.
        //
        // Y llevan el testigo ANTIFALSIFICACIÓN, que es justo la razón de que multipart sea distinto
        // del resto. Una escritura en JSON no puede provocarse desde otro sitio —el tipo de contenido
        // obliga al navegador a preguntar antes y no hay política CORS que lo permita—, pero un
        // formulario alojado en cualquier página sí llegaría aquí con la cookie de sesión puesta.
        // La cookie es SameSite=Strict y eso ya lo frena, pero esta es la barrera que no depende de
        // que un navegador implemente bien SameSite.
        //
        // El testigo lo entrega /api/auth/antiforgery y lo adjunta ClienteApi.SubirAsync sin que
        // ninguna pantalla tenga que acordarse: una que se olvidara recibiría un 400 sin explicación,
        // y quien lo depurara acabaría desactivando la protección para que «funcione».

        grupo.MapPost("/publicaciones", async (
            IFormCollection formulario, ForumService foro, CancellationToken ct) =>
        {
            var (imagenes, error) = await LeerImagenesAsync(formulario.Files, ct);
            if (error != null) return Results.BadRequest(new ResultadoDto(false, error));

            var (ok, mensaje, post) = await foro.PublicarAsync(
                formulario["titulo"], formulario["cuerpo"], TemaAlEscribir(formulario["tema"]),
                formulario["etiquetas"], imagenes, ct);

            return ok && post != null
                ? Results.Ok(new ForoPublicadaDto(post.Id, mensaje))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Publica en el muro, con sus capturas adjuntas");

        grupo.MapPost("/entradas/{id:int}/comentarios", async (
            int id, IFormCollection formulario, ForumService foro, CancellationToken ct) =>
        {
            var (imagenes, error) = await LeerImagenesAsync(formulario.Files, ct);
            if (error != null) return Results.BadRequest(new ResultadoDto(false, error));

            var (ok, mensaje, _) = await foro.ComentarAsync(id, formulario["cuerpo"], imagenes, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Responde a una entrada del hilo");

        // Editar es un POST y no un PUT, que sería lo propio: el envío lleva archivos y el único
        // camino del cliente para mandarlos —ClienteApi.SubirAsync— habla POST. Inventar un PUT que
        // nadie puede llamar no haría la API más correcta, solo más difícil de usar.
        grupo.MapPost("/entradas/{id:int}/editar", async (
            int id, IFormCollection formulario, ForumService foro, CancellationToken ct) =>
        {
            var (imagenes, error) = await LeerImagenesAsync(formulario.Files, ct);
            if (error != null) return Results.BadRequest(new ResultadoDto(false, error));

            var (ok, mensaje) = await foro.EditarAsync(
                id, formulario["titulo"], formulario["cuerpo"],
                imagenes, Numeros(formulario["quitar"], ForumService.MaxImagenes), ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Edita el texto de una entrada propia y añade o quita sus capturas");

        grupo.MapPost("/entradas/{id:int}/retirar", async (
            int id, ForumService foro, CancellationToken ct) =>
        {
            var (ok, mensaje) = await foro.RetirarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Retira una entrada: se oculta su texto y sus imágenes, pero conserva su hueco");

        grupo.MapPost("/entradas/{id:int}/me-gusta", async (
            int id, ForumService foro, CancellationToken ct) =>
        {
            var (ok, meGusta, total) = await foro.MeGustaAsync(id, ct);

            // Es la única operación del foro cuyo rechazo no trae texto: el servicio devuelve solo
            // (false, …) porque el caso es siempre el mismo —la entrada ya no está o la retiraron
            // mientras se leía el hilo—. El mensaje se escribe aquí en lugar de enseñar un «no se
            // pudo» genérico, que no le diría a nadie que basta con recargar.
            return ok
                ? Results.Ok(new ForoMeGustaDto(meGusta, total))
                : Results.BadRequest(new ResultadoDto(false,
                    "Esa entrada ya no está disponible. Actualiza el hilo."));
        })
        .WithSummary("Pone o quita el ❤ propio y devuelve el total");
    }

    /// <summary>Tope de entradas por consulta de imágenes: es un hilo, no el foro entero.</summary>
    private const int MaxEntradasPorConsulta = 200;

    /// <summary>
    /// El tema llega como número y se comprueba contra el enum. Un valor que no existe se trata como
    /// «sin filtrar» en lugar de reventar: un parámetro mal escrito en una dirección compartida no
    /// debe devolver un error, solo el muro completo.
    /// </summary>
    private static ForumTopic? ATema(int? tema) =>
        tema is int t && Enum.IsDefined(typeof(ForumTopic), t) ? (ForumTopic)t : null;

    /// <summary>
    /// El tema con el que se guarda una publicación. Al filtrar, un valor imposible significa «no
    /// filtres»; al escribir no puede significar nada parecido, porque el valor se queda en la base.
    /// Cae en <see cref="ForumTopic.Otro"/>, que es exactamente el cajón que el enum tiene para lo
    /// que no encaja, en vez de rechazar la publicación entera por un desplegable.
    /// </summary>
    private static ForumTopic TemaAlEscribir(string? tema) =>
        int.TryParse(tema, CultureInfo.InvariantCulture, out var n) && Enum.IsDefined(typeof(ForumTopic), n)
            ? (ForumTopic)n
            : ForumTopic.Otro;

    /// <summary>
    /// Una lista de identificadores separados por comas. Lo que no sea un número positivo se descarta
    /// en silencio: son parámetros que arma la propia pantalla, y un valor roto no debe tumbar la
    /// petición entera.
    /// </summary>
    private static List<int> Numeros(string? lista, int tope) =>
        (lista ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .Take(tope)
            .ToList();

    /// <summary>
    /// Pasa los archivos subidos a lo que espera <see cref="ForumService"/>.
    ///
    /// El número y el peso se miran ANTES de leer nada: cargar en memoria veinte archivos de cuatro
    /// megas para que el servicio los rechace después sería regalarle a cualquiera una forma barata
    /// de tumbar el servidor. La comprobación que cuenta —que los bytes sean de verdad una imagen—
    /// sigue estando en el servicio, que es por donde pasa todo lo que se guarda.
    ///
    /// <para>La MINIATURA la genera el navegador y llega en una colección aparte, alineada por
    /// posición con la de los originales. En el escritorio se reescalaba con GDI+, que en el servidor
    /// no existe; hacerlo aquí obligaría a meter una librería de imágenes, y el navegador ya tiene la
    /// imagen decodificada en la mano. No es opcional tenerla: la miniatura es lo que pinta el muro
    /// —y también el escritorio, que sigue leyendo esa columna en producción—, así que guardar el
    /// original en su lugar significa volver a bajarse los originales enteros en cada refresco. Lo
    /// que manda el cliente se comprueba en <see cref="MiniaturaValidadaAsync"/>.</para>
    /// </summary>
    private static async Task<(IReadOnlyList<ForumImagenNueva> imagenes, string? error)> LeerImagenesAsync(
        IFormFileCollection archivos, CancellationToken ct)
    {
        var originales = archivos.GetFiles("imagenes");
        var pequenas = archivos.GetFiles("miniaturas");

        if (originales.Count == 0) return ([], null);

        if (originales.Count > ForumService.MaxImagenes)
            return ([], $"No se pueden poner más de {ForumService.MaxImagenes} imágenes en una entrada.");

        var imagenes = new List<ForumImagenNueva>(originales.Count);
        for (int i = 0; i < originales.Count; i++)
        {
            var archivo = originales[i];
            if (archivo.Length > ForumService.MaxBytesImagen)
                return ([], $"«{archivo.FileName}» pesa {ForumMedia.Tamano(archivo.Length)} " +
                            $"y el tope es {ForumMedia.Tamano(ForumService.MaxBytesImagen)}.");

            var bytes = await ABytesAsync(archivo, ct);
            var (ancho, alto) = MedidasDeImagen(bytes);

            imagenes.Add(new ForumImagenNueva(
                archivo.FileName, bytes, await MiniaturaValidadaAsync(pequenas, i, ct), ancho, alto));
        }

        return (imagenes, null);
    }

    /// <summary>
    /// La miniatura que mandó el navegador, si sirve.
    ///
    /// <b>Se comprueba, no se cree.</b> La genera el cliente —que es el único que ya tiene la imagen
    /// decodificada, y hacerlo aquí obligaría a meter una librería de imágenes en el servidor—, pero
    /// el cliente corre en la máquina de cada persona y a esta ruta se puede llamar sin pasar por él.
    /// Sin esta comprobación, cualquiera podría meter cuatro megas de lo que fuera en la columna que
    /// el muro pinta en cada refresco.
    ///
    /// Devuelve vacío si no cuadra, y eso NO es un error: <see cref="ForumService"/> guarda entonces
    /// el original como miniatura, que es lo que hacía antes de existir esto. Rechazar la publicación
    /// entera por una miniatura sería desproporcionado.
    /// </summary>
    private static async Task<byte[]> MiniaturaValidadaAsync(
        IReadOnlyList<IFormFile> pequenas, int posicion, CancellationToken ct)
    {
        // Las dos colecciones vienen ALINEADAS por posición: el cliente manda una parte por imagen,
        // vacía cuando no pudo generarla (ver EnvioDeImagenes.Adjuntar).
        if (posicion >= pequenas.Count) return [];

        var parte = pequenas[posicion];
        if (parte.Length <= 0 || parte.Length > MaxBytesMiniatura) return [];

        var bytes = await ABytesAsync(parte, ct);

        // Que sea de verdad una imagen lo dicen sus BYTES. Una miniatura se sirve después por HTTP
        // igual que el original, así que colar aquí marcado sería el mismo problema.
        return ForumMedia.TipoDeImagen(bytes) == null ? [] : bytes;
    }

    private static async Task<byte[]> ABytesAsync(IFormFile archivo, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }

    /// <summary>
    /// Tope de una miniatura. Es holgado para lo que debería pesar —unas decenas de KB— y aun así
    /// deja fuera el caso que importa: que alguien mande el original disfrazado de miniatura y
    /// devuelva el problema que la miniatura existe para resolver.
    /// </summary>
    private const long MaxBytesMiniatura = 300 * 1024;

    /// <summary>
    /// Las medidas de una imagen leídas de su cabecera, sin decodificarla.
    ///
    /// Hacen falta porque la MISMA fila la lee la aplicación de escritorio, que sigue en producción
    /// contra esta base y las usa para reservarle sitio a la captura: guardar ceros dejaría las
    /// imágenes subidas desde la web pintándose mal allá. Se leen de la cabecera y no con una
    /// librería de imágenes a propósito — son unos pocos bytes en una posición fija de cada formato,
    /// y meter una dependencia de imagen en la API para esto sería desproporcionado.
    ///
    /// Devuelve (0, 0) si no reconoce el formato; quien lo llama no decide nada con eso, porque quien
    /// dice si los bytes son una imagen es <see cref="ForumMedia.TipoDeImagen"/>.
    /// </summary>
    private static (int Ancho, int Alto) MedidasDeImagen(byte[] bytes)
    {
        // PNG: el bloque IHDR es siempre el primero y sus dos enteros van en el orden de la red.
        if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
            return (EnteroGrande(bytes, 16), EnteroGrande(bytes, 20));

        // GIF: ancho y alto en la pantalla lógica, dos enteros de 16 bits al revés.
        if (bytes.Length >= 10 && bytes[0] == 0x47 && bytes[1] == 0x49)
            return (bytes[6] | (bytes[7] << 8), bytes[8] | (bytes[9] << 8));

        // BMP: el alto viene negativo cuando las filas se guardan de arriba abajo; es orientación,
        // no medida, así que se toma el valor absoluto.
        if (bytes.Length >= 26 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return (Math.Abs(EnteroPequeno(bytes, 18)), Math.Abs(EnteroPequeno(bytes, 22)));

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return MedidasDeJpeg(bytes);

        return (0, 0);
    }

    /// <summary>
    /// El JPEG no tiene las medidas en un sitio fijo: hay que recorrer sus segmentos hasta dar con el
    /// que describe la trama (SOF). Se saltan los marcadores sin carga y los de tabla, y se para al
    /// primer SOF, que es el que manda.
    /// </summary>
    private static (int Ancho, int Alto) MedidasDeJpeg(byte[] bytes)
    {
        int i = 2;
        while (i + 9 < bytes.Length)
        {
            if (bytes[i] != 0xFF) { i++; continue; }

            byte marcador = bytes[i + 1];

            // 0xFF repetido es relleno entre segmentos; D0..D9 y 01 no llevan longitud detrás.
            if (marcador == 0xFF) { i++; continue; }
            if (marcador == 0x01 || (marcador >= 0xD0 && marcador <= 0xD9)) { i += 2; continue; }

            int largo = (bytes[i + 2] << 8) | bytes[i + 3];
            if (largo < 2) break;

            // C0..CF son las tramas, menos C4 (Huffman), C8 (reservado) y CC (aritmética).
            if (marcador >= 0xC0 && marcador <= 0xCF &&
                marcador != 0xC4 && marcador != 0xC8 && marcador != 0xCC)
                return ((bytes[i + 7] << 8) | bytes[i + 8], (bytes[i + 5] << 8) | bytes[i + 6]);

            i += 2 + largo;
        }
        return (0, 0);
    }

    private static int EnteroGrande(byte[] b, int i) =>
        (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

    private static int EnteroPequeno(byte[] b, int i) =>
        b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«el hilo está cerrado», «solo puedes editar lo que
    /// tú escribiste»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
