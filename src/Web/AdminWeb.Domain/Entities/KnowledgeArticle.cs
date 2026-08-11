using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un artículo de la base de conocimiento: lo que el equipo escribe para que el siguiente no tenga
/// que adivinar.
///
/// <para><b>El glosario no es otra tabla.</b> Un término del glosario es un artículo corto con la
/// etiqueta que le corresponda; una guía de despliegue es un artículo largo con las suyas. Partirlo
/// en dos entidades habría obligado a buscar dos veces, revisar por dos caminos y decidir en cuál
/// de los dos va cada cosa cuando un término crece hasta convertirse en una explicación.</para>
///
/// <para><b>El cuerpo NO es HTML</b>, y eso no es una limitación: es la defensa. Lo escribe
/// cualquiera del equipo y lo lee todo el mundo, que es el vector clásico del XSS almacenado.
/// Se guarda como texto con un marcado mínimo y explícito —títulos, listas, negrita, código y
/// enlaces— que el SERVIDOR convierte en bloques y segmentos ya decididos; el navegador pinta lo
/// que recibe y no interpreta nada. Es el mismo camino que ya recorre el foro.</para>
///
/// <para><b>El autor se congela en el texto.</b> <see cref="AuthorName"/> guarda el nombre del día
/// en que se escribió, sin clave foránea a la cuenta: la documentación es histórica y tiene que
/// sobrevivir a que alguien se vaya del equipo y se le dé de baja la cuenta. Mismo criterio que el
/// foro y que la bitácora.</para>
/// </summary>
public class KnowledgeArticle
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    /// <summary>El texto tal como lo escribió su autor, con el marcado mínimo sin interpretar.</summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// Etiquetas separadas por coma. Son el índice de verdad de la base de conocimiento: con ellas
    /// se filtra, se arma el glosario y se relacionan artículos que hablan de lo mismo.
    /// </summary>
    public string? Tags { get; set; }

    public KnowledgeStatus Status { get; set; } = KnowledgeStatus.Borrador;

    // ── Autoría ──────────────────────────────────────────────────────────────
    public int AuthorUserId { get; set; }

    /// <summary>Nombre con el que firmarlo aunque después se borre la cuenta.</summary>
    public string AuthorName { get; set; } = "";

    /// <summary>
    /// Ficha de desarrollador del autor, si la tiene. Es a quien se le abonan los puntos cuando el
    /// líder decide que el artículo los merece; sin ficha se puede escribir y publicar igual, pero
    /// no hay a quién abonarle nada.
    /// </summary>
    public int? AuthorDeveloperId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // ── Revisión ─────────────────────────────────────────────────────────────
    /// <summary>Cuándo entró a la cola por última vez. Es la antigüedad que se le enseña al líder.</summary>
    public DateTime? SubmittedAtUtc { get; set; }

    public int? ReviewedByUserId { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>La ÚLTIMA decisión: el motivo del rechazo o la nota con la que se aprobó.</summary>
    public string? ReviewComment { get; set; }

    /// <summary>
    /// Cuántas veces se ha enviado a revisión. 1 es la primera. Le dice al líder de un vistazo si
    /// está ante algo nuevo o ante la tercera insistencia sobre lo mismo.
    /// </summary>
    public int ReviewRound { get; set; }

    /// <summary>
    /// Bitácora del ida y vuelta: cada envío, cada devolución con su motivo y cada aprobación, en
    /// orden y con fecha. Existe porque <see cref="ReviewComment"/> guarda solo la última decisión:
    /// sin esto, volver a enviar borraría el motivo del rechazo que se está atendiendo y la
    /// conversación quedaría sin la mitad que la explica. Mismo patrón que
    /// <see cref="PointEntry.ReviewHistory"/> y que el pool.
    /// </summary>
    public string? ReviewHistory { get; set; }

    /// <summary>Cuándo se publicó la PRIMERA vez. No se reescribe en las aprobaciones siguientes.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    // ── Puntos ───────────────────────────────────────────────────────────────
    /// <summary>
    /// La entrada de puntos que generó este artículo, si el líder decidió otorgarlos al aprobarlo.
    ///
    /// <para><b>Ésta es LA GUARDA contra el doble abono</b>, y por eso vive aquí y no en una
    /// comprobación del servicio: un artículo se puede aprobar, editar, volver a enviar y volver a
    /// aprobar tantas veces como haga falta, pero paga UNA sola vez en toda su vida. Mientras esta
    /// columna tenga valor, ninguna aprobación posterior crea otra entrada.</para>
    ///
    /// <para><b>Sin clave foránea a propósito</b>, igual que <see cref="PoolActivity.PointEntryId"/>:
    /// <c>PointEntries</c> ya cae en cascada desde <c>Developers</c>, y una segunda ruta hasta la
    /// misma tabla es justo lo que SQL Server rechaza al crear las restricciones.</para>
    /// </summary>
    public int? PointEntryId { get; set; }

    /// <summary>
    /// Cuántos puntos se otorgaron. Se guarda además de <see cref="PointEntryId"/> para poder
    /// enseñarlo en la lista sin ir a buscar la entrada de puntos de cada fila, y para que el dato
    /// siga contando la historia del artículo aunque la entrada se depure algún día.
    /// </summary>
    public int PointsAwarded { get; set; }

    /// <summary>
    /// Sello de concurrencia optimista, solo en SQL Server (en SQLite se ignora). El caso real es
    /// el autor corrigiendo su artículo en una pestaña mientras el líder lo resuelve en otra: sin
    /// sello, el segundo en guardar pisa en silencio lo que decidió el primero.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Ya pagó puntos y no puede volver a pagarlos, se apruebe las veces que se apruebe.</summary>
    public bool YaOtorgoPuntos => PointEntryId != null;

    /// <summary>Está a la vista de todo el que tenga sesión.</summary>
    public bool EsPublico => Status == KnowledgeStatus.Publicado;

    /// <summary>Su autor puede tocarlo y volverlo a mandar.</summary>
    public bool EnManosDelAutor => Status is KnowledgeStatus.Borrador or KnowledgeStatus.Rechazado;
}
