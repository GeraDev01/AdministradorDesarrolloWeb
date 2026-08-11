using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Conocimiento;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Lo elegido en el buscador de la base de conocimiento. Null o falso = no filtrar.</summary>
public record ConocimientoFiltro(
    string? Texto = null,
    string? Etiqueta = null,
    KnowledgeStatus? Estado = null,
    bool SoloMios = false);

/// <summary>
/// La base de conocimiento: escribir documentación, revisarla, publicarla y encontrarla.
///
/// <para><b>El glosario no es una cosa aparte.</b> Todo es un artículo con etiquetas; un término del
/// glosario es un artículo corto etiquetado. Por eso aquí hay un solo camino para escribir, uno para
/// revisar y uno para buscar, y no dos de cada uno que habría que mantener en paralelo.</para>
///
/// <b>QUIÉN VE QUÉ</b>, comprobado AQUÍ y no solo en el endpoint —a un servicio se le puede llamar
/// desde otro endpoint que nazca abierto sin que quien lo escriba se entere—:
/// <list type="bullet">
/// <item><b>Borrador</b>: solo su autor. <b>Ni siquiera el líder</b>, y es a propósito: si el líder
/// pudiera leer lo que está a medias, «borrador» dejaría de significar nada y la gente escribiría en
/// otro sitio hasta tenerlo presentable — con lo que la base de conocimiento se quedaría sin la
/// mitad de lo que debería contener.</item>
/// <item><b>Por revisar</b> y <b>Rechazado</b>: su autor y el líder. Son las dos caras de la misma
/// conversación entre ellos dos.</item>
/// <item><b>Publicado</b>: cualquiera con sesión.</item>
/// </list>
///
/// <para><b>Operaciones SÍ entra, y solo a leer lo publicado.</b> Del foro se les dejó fuera porque
/// aquello es la conversación del equipo de desarrollo; esto es otra cosa. Un artículo publicado es
/// documentación de trabajo —cómo se despliega un sistema, qué significa un término, qué se hace
/// cuando falla algo—, y dejar fuera precisamente a quien despliega convertiría la base de
/// conocimiento en un sitio donde no se puede escribir lo que más falta hace. Escribir, revisar y
/// ver lo que aún no está publicado sigue siendo del líder y de los desarrolladores: la cola de
/// revisión es el proceso del equipo, y los puntos que se otorgan al aprobar cuelgan de una ficha de
/// desarrollador. Las dos reglas están escritas en POSITIVO por rol, nunca por descarte, para que un
/// rol que se invente mañana no herede nada en silencio.</para>
///
/// <para><b>El cuerpo NUNCA sale en crudo hacia el navegador.</b> Sale ya analizado en bloques y
/// segmentos por <see cref="ConocimientoTexto"/>, con los enlaces validados en el servidor. La única
/// excepción es <c>Fuente</c>, el texto con su marcado sin interpretar, y solo va a quien puede
/// editarlo — porque es lo que se le pone en el formulario.</para>
/// </summary>
public class ConocimientoService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, NotificationService notifications)
{
    /// <summary>Ámbito con el que hablan las guardas, para que el 403 diga de qué se le está echando.</summary>
    internal const string Ambito = "de la base de conocimiento";

    public const int MaxTitulo = 200;
    public const int MinTitulo = 5;
    public const int MaxEtiquetas = 300;
    public const int MaxMotivo = 1000;

    /// <summary>
    /// Lo mínimo para mandar algo a revisar. Un borrador puede estar como sea —para eso es un
    /// borrador—, pero hacer que el líder abra tres renglones sueltos es la forma más rápida de que
    /// deje de abrir la cola.
    /// </summary>
    public const int MinCuerpoParaRevisar = 40;

    /// <summary>
    /// Tope de puntos por artículo. No es una regla de negocio, es un pasamanos: los criterios del
    /// catálogo se mueven entre 2 y 15, y un cero de más al teclear ordenaría el ranking del año.
    /// </summary>
    public const int MaxPuntos = 100;

    /// <summary>Tope de la cola que se devuelve de una vez. Si hay más, el problema no es la pantalla.</summary>
    public const int TopeDeCola = 200;

    public const int TamanoPaginaPorOmision = 10;
    public const int TamanoPaginaMaximo = 50;

    // ── Escribir ─────────────────────────────────────────────────────────────────

    /// <summary>Crea un artículo en BORRADOR. Nace privado: solo lo ve quien lo escribe.</summary>
    public async Task<(bool ok, string mensaje, KnowledgeArticle? articulo)> CrearAsync(
        string? titulo, string? cuerpo, string? etiquetas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        titulo = (titulo ?? "").Trim();
        cuerpo = ConocimientoTexto.Sanear(cuerpo);

        var (ok, mensaje) = ValidarTexto(titulo, cuerpo, paraRevisar: false);
        if (!ok) return (false, mensaje, null);

        var articulo = new KnowledgeArticle
        {
            Title             = titulo,
            Body              = cuerpo,
            Tags              = NormalizarEtiquetas(etiquetas),
            Status            = KnowledgeStatus.Borrador,
            AuthorUserId      = userId,
            AuthorName        = NombreDelUsuario(),
            AuthorDeveloperId = currentUser.DeveloperId,
            CreatedAtUtc      = DateTime.UtcNow
        };
        db.KnowledgeArticles.Add(articulo);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "KnowledgeArticle", articulo.Id.ToString(),
            $"Borrador de conocimiento: {titulo}", ct);
        return (true, "Guardado como borrador. Solo tú lo ves hasta que lo mandes a revisar.", articulo);
    }

    /// <summary>
    /// Cambia el texto de un artículo.
    ///
    /// <para><b>Editar algo YA PUBLICADO lo devuelve a la cola.</b> No es un capricho: lo publicado
    /// es lo que el equipo lee dando por hecho que alguien lo revisó, y si una edición posterior se
    /// colara sin pasar por ahí, la revisión no garantizaría nada — bastaría con publicar algo
    /// inocuo y cambiarlo después. El coste es real (una corrección de una errata deja el artículo
    /// fuera hasta que el líder la vea), y por eso <b>el líder sí puede editar lo publicado sin
    /// sacarlo</b>: su edición ya está revisada por definición, que es lo que significa que él sea el
    /// revisor.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EditarAsync(
        int id, string? titulo, string? cuerpo, string? etiquetas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);

        var articulo = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (articulo == null || !PuedeVer(articulo)) return (false, NoExiste);

        bool esAutor = articulo.AuthorUserId == currentUser.UserId;
        if (!esAutor && !currentUser.IsAdmin)
            return (false, "Solo puedes editar lo que tú escribiste.");

        titulo = (titulo ?? "").Trim();
        cuerpo = ConocimientoTexto.Sanear(cuerpo);

        // Se exige ya el mínimo de revisión si el artículo NO está en borrador: lo que está en la
        // cola o publicado no puede quedarse en tres renglones por una edición.
        var (ok, mensaje) = ValidarTexto(titulo, cuerpo, paraRevisar: articulo.Status != KnowledgeStatus.Borrador);
        if (!ok) return (false, mensaje);

        articulo.Title        = titulo;
        articulo.Body         = cuerpo;
        articulo.Tags         = NormalizarEtiquetas(etiquetas);
        articulo.UpdatedAtUtc = DateTime.UtcNow;

        bool vuelveALaCola = esAutor && !currentUser.IsAdmin && articulo.Status == KnowledgeStatus.Publicado;
        if (vuelveALaCola)
        {
            articulo.Status         = KnowledgeStatus.PorRevisar;
            articulo.SubmittedAtUtc = DateTime.UtcNow;
            articulo.ReviewRound++;
            Anotar(articulo, $"Editado por {NombreDelUsuario()} después de publicarse: vuelve a revisión (vuelta {articulo.ReviewRound}).");
        }
        else
        {
            Anotar(articulo, $"Editado por {NombreDelUsuario()}.");
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "KnowledgeArticle", id.ToString(),
            vuelveALaCola ? "Artículo editado tras publicarse: vuelve a revisión" : "Artículo de conocimiento editado", ct);

        if (vuelveALaCola)
        {
            await AvisarALosLideresAsync(articulo, ct);
            return (true, "Guardado. Como ya estaba publicado, vuelve a la cola de revisión.");
        }

        return (true, "Guardado.");
    }

    /// <summary>
    /// Manda el artículo a la cola del líder. Solo su autor, y solo desde borrador o desde una
    /// devolución. Cada envío es una VUELTA numerada, y avisa a los líderes.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EnviarARevisionAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);

        var articulo = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (articulo == null || !PuedeVer(articulo)) return (false, NoExiste);
        if (articulo.AuthorUserId != currentUser.UserId)
            return (false, "Solo su autor puede mandarlo a revisar.");

        if (articulo.Status == KnowledgeStatus.PorRevisar)
            return (false, "Ya está en la cola de revisión.");
        if (articulo.Status == KnowledgeStatus.Publicado)
            return (false, "Ya está publicado. Si quieres cambiarlo, edítalo y volverá solo a la cola.");

        var (ok, mensaje) = ValidarTexto(articulo.Title, articulo.Body, paraRevisar: true);
        if (!ok) return (false, mensaje);

        articulo.Status         = KnowledgeStatus.PorRevisar;
        articulo.SubmittedAtUtc = DateTime.UtcNow;
        articulo.ReviewRound++;
        Anotar(articulo, $"Enviado a revisión por {NombreDelUsuario()} (vuelta {articulo.ReviewRound}).");
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "KnowledgeArticle", id.ToString(),
            $"Enviado a revisión (vuelta {articulo.ReviewRound})", ct);
        await AvisarALosLideresAsync(articulo, ct);

        return (true, "Enviado. El líder lo va a revisar.");
    }

    // ── Revisar ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// El líder publica el artículo y, si quiere, otorga puntos EN LA MISMA OPERACIÓN.
    ///
    /// <para><b>Por qué van juntos.</b> Aprobar y puntuar son dos decisiones y un solo momento. Con
    /// dos pasos, el segundo se olvida: el artículo queda publicado, los puntos no se dan nunca y el
    /// incentivo que justificaba todo esto deja de existir en la práctica. Aquí el líder decide las
    /// dos cosas de una vez, y no otorgar es una respuesta perfectamente normal — <b>no todos los
    /// artículos puntúan</b>.</para>
    ///
    /// <para><b>Un artículo paga UNA sola vez en toda su vida</b>, y eso lo garantiza la base, no una
    /// comprobación previa. Dos cosas distintas lo sostienen:</para>
    /// <list type="number">
    /// <item><b>Dos líderes a la vez.</b> El paso a publicado se hace con un UPDATE CONDICIONAL que
    /// incluye <c>PointEntryId IS NULL</c>. Leer-y-después-escribir no bastaba: los dos leerían
    /// «por revisar», los dos pasarían la comprobación y se abonaría dos veces. Quien pierde la
    /// carrera no afecta ninguna fila y se va con un mensaje, no con un segundo abono.</item>
    /// <item><b>Aprobar otra vez tras una edición.</b> Un artículo publicado que se edita vuelve a la
    /// cola y se vuelve a aprobar, y eso puede pasar muchas veces. Como <c>PointEntryId</c> se queda
    /// puesto desde la primera vez, las aprobaciones siguientes publican pero <b>no vuelven a
    /// otorgar</b>. Si el líder pide puntos sobre uno ya pagado no se le rechaza la aprobación —sería
    /// absurdo, la aprobación es legítima—: se publica y se le dice que ya se pagaron y cuándo.</item>
    /// </list>
    /// </summary>
    public async Task<(bool ok, string mensaje)> AprobarAsync(
        int id, int? criterioId = null, int? puntos = null, string? nota = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var articulo = await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (articulo == null) return (false, NoExiste);
        if (articulo.Status == KnowledgeStatus.Publicado)
            return (false, "Ese artículo ya está publicado.");
        if (articulo.Status != KnowledgeStatus.PorRevisar)
            return (false, "Solo se aprueba lo que su autor mandó a revisar.");

        nota = Recortar(nota, MaxMotivo);

        // ── Qué se va a otorgar, si es que algo ─────────────────────────────
        ScoringCriterion? criterio = null;
        int aOtorgar = 0;
        string avisoDePuntos = "";

        if (criterioId is int cid)
        {
            criterio = await db.ScoringCriteria.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct);
            if (criterio == null) return (false, "Ese criterio ya no está en el catálogo. Actualiza la pantalla.");
            if (!criterio.IsActive) return (false, $"El criterio «{criterio.Name}» está desactivado.");
            if (criterio.Scope == CriterionScope.Equipo)
                return (false, $"«{criterio.Name}» es un criterio de equipo y esto lo escribió una persona.");

            // Sin cantidad se toma la del criterio: es lo que el catálogo dice que vale, y obligar a
            // teclearla otra vez solo invita a equivocarse.
            aOtorgar = puntos ?? criterio.DefaultPoints;

            // Solo positivos. Los criterios de castigo existen y tienen su propio camino; premiar un
            // artículo con puntos negativos no significa nada.
            if (aOtorgar <= 0)
                return (false, "Los puntos de un artículo aprobado tienen que ser positivos. Si no lleva puntos, apruébalo sin criterio.");
            if (aOtorgar > MaxPuntos)
                return (false, $"No se pueden otorgar más de {MaxPuntos} puntos por un artículo.");
        }
        else if (puntos is int p && p != 0)
        {
            return (false, "Elige el criterio con el que se otorgan esos puntos.");
        }

        bool vaAOtorgar = criterio != null;

        if (vaAOtorgar && articulo.YaOtorgoPuntos)
        {
            // No es un error: la aprobación sigue valiendo. Lo que no se hace es pagar dos veces.
            vaAOtorgar = false;
            avisoDePuntos = $" No se otorgaron puntos otra vez: este artículo ya recibió {articulo.PointsAwarded} en su primera aprobación.";
        }
        else if (vaAOtorgar && articulo.AuthorDeveloperId is null)
        {
            return (false, $"{articulo.AuthorName} no tiene ficha de desarrollador, así que no hay a quién abonarle los puntos. " +
                           "Puedes publicarlo sin puntos.");
        }

        var utc      = DateTime.UtcNow;
        var local    = DateTime.Now;   // el período se imputa al mes del calendario de la gente
        var revisor  = currentUser.UserId ?? 0;
        var nombre   = NombreDelUsuario();
        var historial = HistorialCon(articulo,
            vaAOtorgar
                ? $"Aprobado y publicado por {nombre}: +{aOtorgar} pts por «{criterio!.Name}»." + Coletilla(nota)
                : $"Aprobado y publicado por {nombre}, sin puntos." + Coletilla(nota));

        using var tx = await db.Database.BeginTransactionAsync(ct);

        // El UPDATE CONDICIONAL es la guarda. La condición de los puntos se añade solo cuando se van
        // a otorgar: una segunda aprobación sin puntos tiene que poder publicar igual.
        var condicion = db.KnowledgeArticles.Where(a => a.Id == id && a.Status == KnowledgeStatus.PorRevisar);
        if (vaAOtorgar) condicion = condicion.Where(a => a.PointEntryId == null);

        int ganadas = await condicion.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, KnowledgeStatus.Publicado)
            .SetProperty(a => a.ReviewedByUserId, revisor)
            .SetProperty(a => a.ReviewerName, nombre)
            .SetProperty(a => a.ReviewedAtUtc, utc)
            .SetProperty(a => a.ReviewComment, nota)
            .SetProperty(a => a.ReviewHistory, historial)
            // La fecha de publicación es la de la PRIMERA vez: reescribirla en cada reaprobación
            // haría que un artículo de hace un año pareciera nuevo cada vez que se corrige una coma.
            .SetProperty(a => a.PublishedAtUtc, a => a.PublishedAtUtc ?? utc), ct);

        if (ganadas == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "Alguien más lo resolvió mientras lo revisabas. Actualiza la cola.");
        }

        if (vaAOtorgar)
        {
            var entrada = new PointEntry
            {
                DeveloperId      = articulo.AuthorDeveloperId!.Value,
                CriterionId      = criterio!.Id,
                Points           = aOtorgar,
                Year             = local.Year,
                Month            = local.Month,
                Comment          = $"Conocimiento #{articulo.Id}: {articulo.Title}" + Coletilla(nota),
                AssignedByUserId = currentUser.UserId,
                ReviewedByUserId = currentUser.UserId,
                ReviewedAt       = utc,
                // Nace APROBADA porque la revisión es ESTA: mandarla además a la cola de
                // autocalificación sería revisar dos veces exactamente lo mismo.
                ApprovalStatus   = PointApprovalStatus.Aprobado,
                Date             = utc
            };
            db.PointEntries.Add(entrada);
            await db.SaveChangesAsync(ct);

            // La traza va en la MISMA transacción: si algo falla, no queda un artículo publicado con
            // unos puntos que nada justifica ni unos puntos sin la marca que impide repetirlos.
            await db.KnowledgeArticles.Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.PointEntryId, entrada.Id)
                    .SetProperty(a => a.PointsAwarded, aOtorgar), ct);
        }

        await tx.CommitAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "KnowledgeArticle", id.ToString(),
            vaAOtorgar
                ? $"Publicado con +{aOtorgar} pts por «{criterio!.Name}» a {articulo.AuthorName}"
                : "Publicado sin puntos", ct);

        await AvisarAlAutorAsync(articulo,
            vaAOtorgar ? "Se publicó tu artículo y ganaste puntos" : "Se publicó tu artículo",
            vaAOtorgar
                ? $"«{articulo.Title}» — +{aOtorgar} pts por «{criterio!.Name}»."
                : $"«{articulo.Title}» ya lo puede leer el equipo.",
            $"conocimiento-aprobado-{id}-{articulo.ReviewRound}", ct);

        return (true, (vaAOtorgar
            ? $"Publicado. Se le otorgaron {aOtorgar} puntos a {articulo.AuthorName}."
            : "Publicado.") + avisoDePuntos);
    }

    /// <summary>
    /// El líder devuelve el artículo con un motivo. Sirve también para RETIRAR algo ya publicado que
    /// dejó de ser cierto: deja de verse y queda dicho por qué.
    ///
    /// <para>El motivo no es opcional. Una devolución sin motivo no enseña nada, y lo que se busca es
    /// que el artículo vuelva mejor — no que su autor adivine.</para>
    ///
    /// <para><b>Los puntos ya otorgados NO se retiran aquí.</b> Quitarlos sería una segunda decisión,
    /// sobre el desempeño de alguien y sobre un mes que quizá ya se cerró, y tiene su propio sitio:
    /// la pantalla de puntos. Lo que sí queda es la marca —el artículo sigue apuntando a su entrada—
    /// para que quien lo mire vea que esto ya se pagó una vez.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> RechazarAsync(int id, string? motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 5) return (false, "Escribe el motivo: es lo que le dice a su autor qué corregir.");
        motivo = Recortar(motivo, MaxMotivo)!;

        var articulo = await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (articulo == null) return (false, NoExiste);
        if (articulo.Status == KnowledgeStatus.Rechazado)
            return (false, "Ese artículo ya estaba devuelto.");
        if (articulo.Status == KnowledgeStatus.Borrador)
            return (false, "Todavía es un borrador de su autor: no ha llegado a la cola.");

        bool estabaPublicado = articulo.Status == KnowledgeStatus.Publicado;

        var utc     = DateTime.UtcNow;
        var revisor = currentUser.UserId ?? 0;
        var nombre  = NombreDelUsuario();
        var historial = HistorialCon(articulo, estabaPublicado
            ? $"Retirado de publicación por {nombre}: {motivo}"
            : $"Devuelto por {nombre}: {motivo}");

        int ganadas = await db.KnowledgeArticles
            .Where(a => a.Id == id && a.Status == articulo.Status)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, KnowledgeStatus.Rechazado)
                .SetProperty(a => a.ReviewedByUserId, revisor)
                .SetProperty(a => a.ReviewerName, nombre)
                .SetProperty(a => a.ReviewedAtUtc, utc)
                .SetProperty(a => a.ReviewComment, motivo)
                .SetProperty(a => a.ReviewHistory, historial), ct);

        if (ganadas == 0) return (false, "Alguien más lo resolvió mientras lo revisabas. Actualiza la cola.");

        await audit.RecordAsync(AuditAction.Update, "KnowledgeArticle", id.ToString(),
            estabaPublicado ? $"Retirado de publicación: {motivo}" : $"Devuelto a su autor: {motivo}", ct);

        await AvisarAlAutorAsync(articulo,
            estabaPublicado ? "Se retiró tu artículo" : "Te devolvieron tu artículo",
            $"«{articulo.Title}» — {motivo}",
            $"conocimiento-devuelto-{id}-{articulo.ReviewRound}", ct);

        return (true, estabaPublicado
            ? "Retirado. Su autor puede corregirlo y volver a mandarlo."
            : "Devuelto con el motivo. Su autor ya lo tiene.");
    }

    /// <summary>
    /// Borra un artículo que nunca llegó a publicarse. Solo borradores y devueltos, y solo su autor
    /// (o el líder).
    ///
    /// <para>Lo publicado NO se borra: es conocimiento del equipo y alguien puede tenerlo enlazado.
    /// Para quitarlo de circulación está <see cref="RechazarAsync"/>, que deja dicho por qué. Y nada
    /// que haya generado puntos se borra tampoco, esté como esté: esa fila es lo que justifica un
    /// abono en el desempeño de alguien.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);

        var articulo = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (articulo == null || !PuedeVer(articulo)) return (false, NoExiste);
        if (articulo.AuthorUserId != currentUser.UserId && !currentUser.IsAdmin)
            return (false, "Solo puedes borrar lo que tú escribiste.");

        if (!articulo.EnManosDelAutor)
            return (false, articulo.Status == KnowledgeStatus.Publicado
                ? "Un artículo publicado no se borra: retíralo con un motivo y quedará constancia."
                : "Está en la cola de revisión. Espera la respuesta del líder.");

        if (articulo.YaOtorgoPuntos)
            return (false, $"Este artículo otorgó {articulo.PointsAwarded} puntos: no se puede borrar la evidencia de un abono.");

        var instantanea = new { articulo.Id, articulo.Title, articulo.AuthorName, articulo.Tags, articulo.CreatedAtUtc };
        db.KnowledgeArticles.Remove(articulo);
        await db.SaveChangesAsync(ct);

        await audit.RecordDetailedAsync(AuditAction.Delete, "KnowledgeArticle", id.ToString(),
            $"Artículo de conocimiento borrado: «{articulo.Title}»", AuditOutcome.Exito,
            oldValues: instantanea, ct: ct);

        return (true, "Borrado.");
    }

    // ── Buscar y leer ────────────────────────────────────────────────────────────

    /// <summary>
    /// El buscador: texto libre, etiqueta y estado, sobre lo que quien pregunta puede ver.
    ///
    /// <para>El filtro de visibilidad va DENTRO de la consulta, no aplicado después: si se colara un
    /// borrador ajeno y se descartara al armar la página, el total mentiría y la paginación dejaría
    /// huecos — y sobre todo, el dato ya habría salido de la base.</para>
    /// </summary>
    public async Task<PaginaDto<ConocimientoTarjetaDto>> BuscarAsync(
        ConocimientoFiltro? filtro = null, int pagina = 1, int tamano = TamanoPaginaPorOmision,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        filtro ??= new ConocimientoFiltro();
        (pagina, tamano) = Acotar(pagina, tamano);

        var consulta = Filtrar(Visibles(), filtro);

        // «Solo los míos» se aplica aquí y no en Filtrar porque necesita la identidad, y Filtrar es
        // estático a propósito: así no puede tomar decisiones de permisos por descuido — de eso se
        // encarga Visibles(), y solo Visibles().
        if (filtro.SoloMios)
        {
            int mio = currentUser.UserId ?? -1;
            consulta = consulta.Where(a => a.AuthorUserId == mio);
        }

        int total = await consulta.CountAsync(ct);
        if (total == 0) return new PaginaDto<ConocimientoTarjetaDto>([], 0, pagina, tamano);

        var filas = await consulta
            .OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        return new PaginaDto<ConocimientoTarjetaDto>(filas.Select(Tarjeta).ToList(), total, pagina, tamano);
    }

    /// <summary>Un artículo completo, con el cuerpo ya analizado. Null si no existe o no le toca verlo.</summary>
    public async Task<ConocimientoArticuloDto?> LeerAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var a = await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null || !PuedeVer(a)) return null;

        bool esMio    = a.AuthorUserId == currentUser.UserId;
        bool escribe  = currentUser.IsAdmin || currentUser.IsDesarrollador;
        bool edita    = escribe && (esMio || currentUser.IsAdmin);
        // El ida y vuelta con el líder es de los dos, y de nadie más: para el resto del equipo el
        // artículo publicado es el resultado, no el expediente de cómo se llegó a él.
        bool veLaRevision = esMio || currentUser.IsAdmin;

        return new ConocimientoArticuloDto(
            a.Id,
            a.Title,
            ConocimientoTexto.Analizar(a.Body),
            edita ? a.Body : null,
            a.Tags,
            a.Status,
            EtiquetaEstado(a.Status),
            a.AuthorName,
            esMio,
            a.CreatedAtUtc,
            a.UpdatedAtUtc,
            a.PublishedAtUtc,
            veLaRevision ? a.ReviewerName : null,
            veLaRevision ? a.ReviewedAtUtc : null,
            veLaRevision ? a.ReviewComment : null,
            veLaRevision ? a.ReviewHistory : null,
            a.ReviewRound,
            a.PointsAwarded,
            edita,
            esMio && escribe && a.EnManosDelAutor,
            currentUser.IsAdmin && a.Status == KnowledgeStatus.PorRevisar);
    }

    /// <summary>
    /// La cola del líder, con lo que lleva más tiempo esperando ARRIBA.
    ///
    /// <para>El orden es lo importante: por el más antiguo y no por el más reciente. Lo que lleva
    /// nueve días parado es exactamente lo que hace que su autor no vuelva a escribir nunca, y en una
    /// lista ordenada por fecha de llegada eso se queda al final, donde nadie lo mira.</para>
    /// </summary>
    public async Task<List<ConocimientoColaFilaDto>> ColaDeRevisionAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        var ahora = DateTime.UtcNow;

        return (await db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.Status == KnowledgeStatus.PorRevisar)
            .OrderBy(a => a.SubmittedAtUtc ?? a.CreatedAtUtc)
            .ThenBy(a => a.Id)
            .Take(TopeDeCola)
            .Select(a => new { a.Id, a.Title, a.AuthorName, Enviado = a.SubmittedAtUtc ?? a.CreatedAtUtc, a.ReviewRound })
            .ToListAsync(ct))
            .Select(a => new ConocimientoColaFilaDto(
                a.Id, a.Title, a.AuthorName, a.Enviado,
                Math.Max(0, (int)(ahora - a.Enviado).TotalDays),
                Math.Max(1, a.ReviewRound)))
            .ToList();
    }

    /// <summary>
    /// Los contadores para que la pantalla ENSEÑE lo pendiente sin que nadie tenga que ir a buscarlo.
    ///
    /// <para>Es la pieza que mantiene viva la funcionalidad. Una cola de revisión que hay que
    /// acordarse de abrir no se abre, y en cuanto dos artículos se quedan esperando, quien los
    /// escribió deja de escribir. Por eso cuenta los dos lados —lo que al líder le falta revisar y lo
    /// que a quien pregunta le devolvieron— y da la antigüedad del más viejo, que es el número que de
    /// verdad avergüenza.</para>
    ///
    /// <para>Solo sesión iniciada: quien no escribe ni revisa recibe ceros, y así el menú puede pedir
    /// esto siempre sin tener que saber de roles.</para>
    /// </summary>
    public async Task<ConocimientoPendientesDto> PendientesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        int userId = currentUser.UserId ?? -1;

        int porRevisar = 0, dias = 0;
        if (currentUser.IsAdmin)
        {
            var enCola = await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Status == KnowledgeStatus.PorRevisar)
                .Select(a => a.SubmittedAtUtc ?? a.CreatedAtUtc)
                .ToListAsync(ct);

            porRevisar = enCola.Count;
            if (porRevisar > 0) dias = Math.Max(0, (int)(DateTime.UtcNow - enCola.Min()).TotalDays);
        }

        // Los propios se cuentan para todos: si mañana Operaciones pudiera escribir, este número
        // seguiría siendo correcto sin tocar nada. Hoy le devuelve cero, que es lo que tiene.
        int borradores = await db.KnowledgeArticles.AsNoTracking()
            .CountAsync(a => a.AuthorUserId == userId && a.Status == KnowledgeStatus.Borrador, ct);
        int devueltos = await db.KnowledgeArticles.AsNoTracking()
            .CountAsync(a => a.AuthorUserId == userId && a.Status == KnowledgeStatus.Rechazado, ct);

        return new ConocimientoPendientesDto(porRevisar, dias, borradores, devueltos);
    }

    /// <summary>
    /// Las etiquetas de lo PUBLICADO con cuántos artículos lleva cada una: el índice de la base y,
    /// de paso, el glosario. Solo de lo publicado, porque una etiqueta que solo existe en el borrador
    /// de alguien llevaría a una lista vacía.
    /// </summary>
    public async Task<List<ConocimientoEtiquetaDto>> EtiquetasAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var crudas = await db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.Status == KnowledgeStatus.Publicado && a.Tags != null && a.Tags != "")
            .Select(a => a.Tags!)
            .ToListAsync(ct);

        return crudas
            .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(e => e, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ConocimientoEtiquetaDto(g.Key, g.Count()))
            .OrderByDescending(e => e.Articulos)
            .ThenBy(e => e.Etiqueta, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Los criterios con los que el líder puede otorgar puntos al aprobar. Son los del catálogo de
    /// siempre —no hay un catálogo aparte para esto—, filtrados a los que aplican a una persona.
    /// </summary>
    public async Task<List<OpcionDto>> CriteriosParaOtorgarAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        return (await db.ScoringCriteria.AsNoTracking()
            .Where(c => c.IsActive && c.Scope != CriterionScope.Equipo && c.DefaultPoints > 0)
            .Select(c => new { c.Id, c.Name, c.DefaultPoints })
            .ToListAsync(ct))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new OpcionDto(c.Id, $"{c.Name}  (+{c.DefaultPoints})"))
            .ToList();
    }

    // ── Visibilidad ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lo que la sesión actual puede ver, escrito de forma que lo ejecute SQL.
    ///
    /// <para>Es la MISMA regla que <see cref="PuedeVer"/>, y tienen que seguir siéndolo: una lista
    /// que enseñara de más o una lectura que dejara pasar lo que la lista oculta serían dos formas
    /// del mismo agujero.</para>
    /// </summary>
    private IQueryable<KnowledgeArticle> Visibles()
    {
        int userId  = currentUser.UserId ?? -1;
        bool esLider = currentUser.IsAdmin;

        return db.KnowledgeArticles.AsNoTracking().Where(a =>
            a.Status == KnowledgeStatus.Publicado
            || a.AuthorUserId == userId
            // El líder ve la cola y lo devuelto, pero NO los borradores ajenos.
            || (esLider && a.Status != KnowledgeStatus.Borrador));
    }

    private bool PuedeVer(KnowledgeArticle a) =>
        a.Status == KnowledgeStatus.Publicado
        || a.AuthorUserId == currentUser.UserId
        || (currentUser.IsAdmin && a.Status != KnowledgeStatus.Borrador);

    /// <summary>
    /// El filtro del buscador, traducible a SQL. La comparación va en minúsculas por las dos partes
    /// y no a la intercalación de la base: si no, buscar «índices» encontraría o no «Índices» según
    /// cómo esté creada la columna, que es de esas cosas que nadie ve venir. El coste es nulo, un
    /// LIKE con comodín delante no usa índice de todos modos.
    /// </summary>
    private static IQueryable<KnowledgeArticle> Filtrar(IQueryable<KnowledgeArticle> consulta, ConocimientoFiltro filtro)
    {
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var texto = filtro.Texto.Trim().ToLower();
            consulta = consulta.Where(a =>
                a.Title.ToLower().Contains(texto)
                || a.Body.ToLower().Contains(texto)
                || (a.Tags != null && a.Tags.ToLower().Contains(texto))
                || a.AuthorName.ToLower().Contains(texto));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Etiqueta))
        {
            // Se compara con los separadores puestos por los dos lados para que la etiqueta case
            // ENTERA: sin esto, filtrar por «sql» arrastraría también todo lo etiquetado «sql server».
            var etiqueta = ", " + filtro.Etiqueta.Trim().ToLower() + ",";
            consulta = consulta.Where(a => (", " + (a.Tags ?? "").ToLower() + ",").Contains(etiqueta));
        }

        if (filtro.Estado is KnowledgeStatus estado)
            consulta = consulta.Where(a => a.Status == estado);

        return consulta;
    }

    // ── Utilidades ───────────────────────────────────────────────────────────────

    private const string NoExiste = "Ese artículo ya no existe o no está disponible para ti.";

    public static string EtiquetaEstado(KnowledgeStatus estado) => estado switch
    {
        KnowledgeStatus.Borrador   => "Borrador",
        KnowledgeStatus.PorRevisar => "Por revisar",
        KnowledgeStatus.Publicado  => "Publicado",
        _                          => "Devuelto"
    };

    private ConocimientoTarjetaDto Tarjeta(KnowledgeArticle a) => new(
        a.Id,
        a.Title,
        ConocimientoTexto.Extracto(a.Body),
        a.Tags,
        a.Status,
        EtiquetaEstado(a.Status),
        a.AuthorName,
        a.AuthorUserId == currentUser.UserId,
        a.CreatedAtUtc,
        a.UpdatedAtUtc,
        a.PublishedAtUtc,
        a.PointsAwarded,
        a.ReviewRound);

    private static (bool ok, string mensaje) ValidarTexto(string? titulo, string? cuerpo, bool paraRevisar)
    {
        titulo = (titulo ?? "").Trim();
        cuerpo = cuerpo ?? "";

        if (titulo.Length < MinTitulo)
            return (false, $"Escribe un título de al menos {MinTitulo} caracteres: es por donde se va a encontrar.");
        if (titulo.Length > MaxTitulo)
            return (false, $"El título no puede pasar de {MaxTitulo} caracteres.");
        if (cuerpo.Length > ConocimientoTexto.MaxCuerpo)
            return (false, $"El artículo no puede pasar de {ConocimientoTexto.MaxCuerpo:N0} caracteres. " +
                           "Si no cabe, pártelo en varios artículos y enlázalos.");
        if (paraRevisar && cuerpo.Trim().Length < MinCuerpoParaRevisar)
            return (false, $"Para mandarlo a revisar escribe al menos {MinCuerpoParaRevisar} caracteres.");

        return (true, "");
    }

    /// <summary>
    /// Deja las etiquetas en minúsculas, sin repetidas y separadas por «, ».
    ///
    /// <para>En minúsculas a propósito: son el ÍNDICE de la base de conocimiento, y «SQL», «Sql» y
    /// «sql» escritas por tres personas distintas partirían en tres el mismo tema. Lo que se pierde
    /// —la mayúscula de un acrónimo— no vale lo que se gana: que filtrar por una etiqueta traiga
    /// todo lo que habla de ella.</para>
    /// </summary>
    private static string? NormalizarEtiquetas(string? etiquetas)
    {
        if (string.IsNullOrWhiteSpace(etiquetas)) return null;

        var vistas = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var limpias = etiquetas
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLower())
            .Where(e => e.Length > 0 && vistas.Add(e));

        var r = string.Join(", ", limpias);
        if (r.Length > MaxEtiquetas) r = r[..MaxEtiquetas];
        return r.Length == 0 ? null : r;
    }

    private string NombreDelUsuario() =>
        currentUser.FullName is { Length: > 0 } n ? n
        : currentUser.Username ?? $"Usuario #{currentUser.UserId}";

    private static string Coletilla(string? nota) =>
        string.IsNullOrWhiteSpace(nota) ? "" : $" — {nota.Trim()}";

    private static string? Recortar(string? texto, int tope)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return null;
        return t.Length <= tope ? t : t[..tope];
    }

    private static (int Pagina, int Tamano) Acotar(int pagina, int tamano) =>
        (Math.Max(1, pagina), Math.Clamp(tamano, 1, TamanoPaginaMaximo));

    /// <summary>Tope del historial: un ida y vuelta largo no debe crecer sin límite.</summary>
    private const int MaxHistorial = 8000;

    private static void Anotar(KnowledgeArticle a, string linea) => a.ReviewHistory = HistorialCon(a, linea);

    /// <summary>
    /// El historial que resultaría de anotar esa línea, sin tocar la entidad: lo necesitan las
    /// transiciones que se escriben con un UPDATE condicional, donde no hay entidad que mutar.
    /// Se recorta por el PRINCIPIO porque lo último que se dijo es lo que hace falta para decidir.
    /// </summary>
    private static string HistorialCon(KnowledgeArticle a, string linea)
    {
        var sello = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] {linea.Trim()}";
        var historial = string.IsNullOrWhiteSpace(a.ReviewHistory) ? sello : a.ReviewHistory + "\n" + sello;
        return historial.Length <= MaxHistorial ? historial : historial[^MaxHistorial..];
    }

    // ── Avisos ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Avisa a los líderes de que hay algo esperando.
    ///
    /// <para>La clave de deduplicación lleva la VUELTA: reintentar el mismo envío no vuelve a avisar,
    /// pero un artículo corregido y mandado otra vez sí — y tiene que hacerlo, porque es otra
    /// revisión distinta.</para>
    ///
    /// <para>Se traga sus fallos: el artículo ya está en la cola, y un aviso que no sale no puede
    /// tumbar el envío de quien se tomó el trabajo de escribirlo.</para>
    /// </summary>
    private async Task AvisarALosLideresAsync(KnowledgeArticle a, CancellationToken ct)
    {
        try
        {
            var lideres = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            foreach (var userId in lideres)
                await notifications.NotifyAsync(userId, NotificationKind.General,
                    "Hay un artículo por revisar",
                    $"«{a.Title}» — lo escribió {a.AuthorName}.",
                    dedupeKey: $"conocimiento-revision-{a.Id}-{a.ReviewRound}", ct: ct);
        }
        catch { /* el aviso es cortesía; el artículo ya está en la cola */ }
    }

    private async Task AvisarAlAutorAsync(
        KnowledgeArticle a, string titulo, string mensaje, string clave, CancellationToken ct)
    {
        try
        {
            await notifications.NotifyAsync(a.AuthorUserId, NotificationKind.General, titulo, mensaje,
                dedupeKey: clave, ct: ct);
        }
        catch { /* lo que decidió el líder ya está guardado */ }
    }
}
