using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Una actividad del pool: trabajo con un valor en puntos fijado ANTES de que nadie lo tome.
///
/// Es lo que resuelve el problema del sistema anterior. Con la autocalificación libre, el
/// desarrollador elegía qué actividad registrar y cuántas veces, y el líder decidía después si lo
/// valía: dos juicios subjetivos sobre trabajo ya hecho. Aquí el valor sale de una matriz
/// (tipo × complejidad) que el líder configura una sola vez, y queda CONGELADO en
/// <see cref="Points"/> al crear la actividad. Cuando alguien la toma ya sabe exactamente cuánto
/// vale, y al verificarla no hay nada que negociar: solo comprobar que el trabajo está hecho.
///
/// No es un Requirement. Un requerimiento se asigna (a varias personas si hace
/// falta), vive en un sprint, se sincroniza con Azure DevOps y tiene su propio ciclo de estados.
/// Una actividad del pool la TOMA una sola persona, se completa contra un checklist y termina en
/// puntos. Mezclarlas obligaría a filtrar «las del pool» en todas las consultas de requerimientos
/// —métricas, sprint, capacidad, import de DevOps— y un filtro olvidado metería actividades del
/// pool en el backlog.
///
/// Tampoco es una DevActivity, aunque use una: al tomarla se crea una actividad libre
/// enlazada (<see cref="LinkedDevActivityId"/>) para que el cronómetro de siempre funcione sin
/// tocarlo.
/// </summary>
public class PoolActivity
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public PoolWorkType WorkType { get; set; }
    public PoolComplexity Complexity { get; set; }

    /// <summary>
    /// Puntos que vale, copiados de la matriz al crearla. CONGELADOS a propósito: si se leyeran de
    /// la matriz al aceptar, cambiar la tabla revaluaría hacia atrás todo lo ya trabajado, y quien
    /// tomó una actividad de 13 puntos podría acabar cobrando 5. Es el mismo patrón que
    /// <see cref="PointEntry.Points"/>.
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// Con qué urgencia hay que tomarla. Ordena y resalta la lista del pool; <b>no cambia los
    /// puntos</b>, porque si los cambiara, publicar «Crítica» sería la manera de regalarlos.
    /// </summary>
    public PoolPriority Priority { get; set; } = PoolPriority.Media;

    /// <summary>
    /// <b>OBSOLETA PARA LA WEB. NO LA LEAS NI LA ESCRIBAS DESDE AQUÍ.</b> Días concedidos para
    /// entregarla; la sustituye <see cref="HorasLimite"/>.
    ///
    /// <para>Sigue mapeada —y no marcada con <c>Ignore()</c>— porque la APLICACIÓN DE ESCRITORIO
    /// continúa en producción leyendo esta misma base hasta el día del corte, y es esta columna la
    /// que lee para saber el plazo. Ignorarla haría que <c>EnsureCreated</c> dejara de crearla en
    /// una base nueva y el escritorio apuntado ahí reventaría al primer SELECT; renombrarla lo
    /// rompería el mismo día del despliegue, y nadie lo relacionaría con este cambio.</para>
    ///
    /// <para><b>Se puede tirar DESPUÉS del corte</b>, junto con
    /// <see cref="PoolPointsMatrixEntry.DiasLimite"/>, que es su gemela en la otra tabla. Las dos, o
    /// ninguna: el escritorio las usa juntas —el plazo de la actividad cae al de su celda cuando
    /// está vacío— y quitarle solo una lo dejaría calculando plazos a medias.</para>
    /// </summary>
    public int? DiasLimite { get; set; }

    /// <summary>
    /// Horas concedidas para entregarla, decididas al publicarla. Nulo = usar las de la matriz.
    /// 0 = sin fecha límite.
    ///
    /// <para>En HORAS y no en días porque el plazo tiene que poder contrastarse con lo que miden los
    /// CRONÓMETROS, que registran horas. Una estimación en días no se compara con un cronómetro sin
    /// inventarse cuánto dura un día, y ese invento es justo lo que hacía incomparables los números.</para>
    ///
    /// <para>Es lo ÚNICO de la matriz que se puede ajustar por actividad, y a propósito: el plazo
    /// depende del trabajo concreto —un bug medio detrás de un cliente que espera no admite las
    /// mismas horas que uno cualquiera— mientras que los puntos deben depender solo del tipo y la
    /// complejidad. Aflojar el plazo no vale puntos; aflojar los puntos sí, y por eso ésos siguen
    /// sin poder tocarse.</para>
    ///
    /// <para>En un BUG este número es OBLIGATORIO y lo pone el líder: ahí la matriz no manda. En una
    /// tarea o un requerimiento es opcional y en blanco significa «la matriz».</para>
    ///
    /// <para>Se guarda el NÚMERO DE HORAS y no una fecha porque el plazo empieza a correr cuando
    /// alguien la toma, no cuando se publica: una actividad que espera dos semanas en el pool no
    /// debe llegar con el plazo ya consumido.</para>
    /// </summary>
    public decimal? HorasLimite { get; set; }

    /// <summary>
    /// Esfuerzo estimado en HORAS: cuánto trabajo se cree que cuesta, no cuándo hay que entregarlo.
    /// Es el número contra el que se contrasta el cronómetro.
    ///
    /// <para><b>Quién lo escribe depende del tipo</b>, y no hace falta guardarlo porque se deriva:
    /// en un BUG es siempre de quien la tiene tomada (<see cref="ClaimedByDeveloperId"/>) y se
    /// captura AL TOMARLA; en una tarea o un requerimiento es siempre del líder y se captura al
    /// publicarla. Esa derivación solo vale mientras se sostengan sus dos invariantes, y por eso
    /// están escritas donde se aplican, en <c>PoolActivityService</c>:</para>
    /// <list type="number">
    ///   <item>al CAMBIAR EL TIPO entre bug y no-bug se borra (<c>EditarAsync</c>): el número
    ///         quedaría atribuido a quien no lo escribió;</item>
    ///   <item>al SOLTAR EL RECLAMO de un bug se borra (<c>SoltarReclamo</c>, y con él devolver y
    ///         liberar): la estimación se va con quien la escribió, para que el siguiente no herede
    ///         un número ajeno ni dé por satisfecha la obligación de estimar.</item>
    /// </list>
    ///
    /// <para><b>UN solo campo y no dos</b> (uno del líder y otro de quien la toma) porque lo único
    /// que se hace con este número es restarlo de lo que midan las <c>WorkSession</c> de la
    /// actividad enlazada: eso es UN número contra UN número. Con dos columnas, cada sitio que
    /// compare tendría que escribir <c>DelLider ?? DeQuienLaTomo</c>, y el día que a alguien se le
    /// olvidara ese coalesce la comparación no fallaría: saldría vacía o con la mitad de las
    /// actividades. Un error que no se ve es peor que uno que se ve.</para>
    /// </summary>
    public decimal? HorasEstimadas { get; set; }

    /// <summary>
    /// Cuándo se capturó <see cref="HorasEstimadas"/>. Existe para poder AUDITAR que la estimación
    /// de un bug se escribió al tomarlo —sello ≈ <see cref="ClaimedAt"/>— y no a mitad del trabajo,
    /// cuando quien la escribe ya sabe lo que le costó y el número deja de servir para comparar.
    /// Sin este sello, «la estimación es honesta porque se pide al tomar» es una afirmación que
    /// nadie puede comprobar después.
    /// </summary>
    public DateTime? HorasEstimadasEnUtc { get; set; }

    public PoolActivityStatus Status { get; set; } = PoolActivityStatus.Disponible;

    /// <summary>Enlace al work item, ticket o incidencia que la origina. Solo http/https.</summary>
    public string? ExternalUrl { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Reclamo ──────────────────────────────────────────────────────────
    /// <summary>Quién la tomó. Uno solo: no es una asignación múltiple como la de los requerimientos.</summary>
    public int? ClaimedByDeveloperId { get; set; }
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// Hasta cuándo se espera que esté entregada, calculado AL TOMARLA sumando las HORAS del plazo
    /// —las de la actividad si las tiene, si no las de su celda de la matriz—.
    ///
    /// <para>Vencerla no dispara nada automático: se resalta y el líder decide si la libera. Liberar
    /// sola una actividad que alguien está trabajando ahora mismo sería peor que el problema.</para>
    ///
    /// <para><b>Los plazos ya escritos con la regla vieja (sumando DÍAS) NO se recalculan.</b> La
    /// migración a horas convierte la CONFIGURACIÓN —la matriz y el plazo de cada actividad—, nunca
    /// este instante ya materializado, por tres motivos: es un compromiso que quien la tomó ya vio y
    /// tiene en su pantalla, y moverlo es cambiar el trato a medio camino —lo mismo que
    /// <c>EditarAsync</c> ya se niega a hacer con los puntos—; el recálculo siempre iría hacia atrás
    /// y mucho (cinco días naturales pasan a cuarenta horas de reloj), así que la mañana siguiente
    /// al despliegue habría un puñado de actividades pintadas de vencidas señalando a gente que no
    /// hizo nada mal; y vencer no es cosmético: es lo que invita al líder a liberar la actividad, o
    /// sea a quitársela de las manos a quien la trabaja.</para>
    ///
    /// <para>Consecuencia aceptada: durante un tiempo conviven plazos calculados con las dos reglas.
    /// No hace falta distinguirlos —los dos son instantes y los dos se comparan igual—; quien
    /// quiera hacerlo, los separa por <see cref="ClaimedAt"/> respecto de la fecha del despliegue.</para>
    /// </summary>
    public DateTime? ClaimDeadlineAt { get; set; }

    /// <summary>
    /// Cuántas veces se devolvió al pool. Devolver no se castiga —penalizarlo haría que nadie se
    /// atreviera a tomar nada difícil—, pero el número queda a la vista del líder: una actividad
    /// devuelta cinco veces dice algo, de la actividad o de quien la toma.
    /// </summary>
    public int ReturnedCount { get; set; }

    // ── Entrega y verificación ───────────────────────────────────────────
    public DateTime? DeliveredAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Motivo de la última devolución. Solo la última: el hilo completo va en el historial.</summary>
    public string? ReviewComment { get; set; }

    /// <summary>Cuántas veces se ha vuelto a entregar tras una devolución.</summary>
    public int ReviewRound { get; set; }

    /// <summary>
    /// Bitácora del ida y vuelta con el líder, fechada y en orden. Existe por lo mismo que la de
    /// <see cref="PointEntry.ReviewHistory"/>: <see cref="ReviewComment"/> guarda solo la última
    /// decisión, así que sin esto la segunda devolución borraría el motivo de la primera.
    /// </summary>
    public string? ReviewHistory { get; set; }

    // ── Enlaces (sin FK a propósito) ─────────────────────────────────────
    /// <summary>
    /// Entrada de puntos que generó al aceptarse. Sirve para dos cosas: rastrear de dónde salieron
    /// esos puntos, e impedir que una segunda aceptación los duplique.
    ///
    /// Sin clave foránea, igual que WorkInterval: con FK habría dos rutas de borrado
    /// en cascada desde Developers (por PointEntries y por DevActivities) y SQL Server rechaza
    /// crear esas restricciones. El precio asumido: si la limpieza de datos purga las entradas de
    /// puntos, este número queda colgando — inocuo, es solo una traza.
    /// </summary>
    public int? PointEntryId { get; set; }

    /// <summary>
    /// Actividad libre creada al tomarla, para poder cronometrar el trabajo con el cronómetro que
    /// ya existe. Es lo que evita añadir un tercer objetivo al par requerimiento/actividad de
    /// WorkSession, que obligaría a rehacer la tabla y a tocar cada pantalla de
    /// tiempo. Sin FK por lo mismo que el campo anterior.
    /// </summary>
    public int? LinkedDevActivityId { get; set; }

    /// <summary>
    /// Sello de concurrencia optimista. Es nuevo de la web: en el escritorio una actividad la tocaba
    /// una persona a la vez, aquí el líder puede estar revisándola en un navegador mientras quien la
    /// tomó la entrega desde otro. Con el sello, el segundo en guardar recibe un error de
    /// concurrencia en vez de pisar en silencio lo que acaba de escribir el primero.
    /// Solo se mapea contra SQL Server; en SQLite se ignora.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public Developer? ClaimedBy { get; set; }

    /// <summary>
    /// Los criterios extra que se van a evaluar en esta actividad. Cada uno suma sus puntos SOLO si
    /// el líder lo da por cumplido al verificar la entrega.
    /// </summary>
    public ICollection<PoolActivityExtraCriterion> ExtraCriteria { get; set; } =
        new List<PoolActivityExtraCriterion>();

    /// <summary>Está en manos de alguien (tomada o devuelta para corregir).</summary>
    public bool EnCurso => Status is PoolActivityStatus.Tomada or PoolActivityStatus.Devuelta;

    /// <summary>Pasó su fecha límite y sigue sin entregarse.</summary>
    public bool Vencida => EnCurso && ClaimDeadlineAt is DateTime f && f < DateTime.UtcNow;
}

/// <summary>
/// Un criterio extra que se evalúa en UNA actividad concreta: trabajo que no cambia lo que la
/// actividad vale de base, pero que suma si se hace.
///
/// <para><b>Para qué existe.</b> La matriz fija lo que vale un bug medio, y eso está bien: es lo que
/// impide negociar puntos sobre trabajo ya hecho. Pero deja fuera lo que sí varía de una actividad a
/// otra —«ésta además quiero que venga con pruebas», «de ésta necesito documentación»—. Sin esto, la
/// única forma de pedir ese extra era inflar la complejidad, que es mentir en la matriz para
/// conseguir puntos. Con esto, la base sigue siendo la que dice la matriz y lo adicional se pide en
/// voz alta, se ve antes de tomar la actividad y se cobra solo si se entrega.</para>
///
/// <para><b>Por qué guarda su propia copia de nombre y puntos.</b> Por lo mismo que
/// <see cref="PoolActivity.Points"/> están congelados: quien toma una actividad viendo «+5 por
/// pruebas automatizadas» tiene que cobrar 5, aunque el líder baje ese criterio a 2 en el catálogo
/// mientras la trabaja. Referenciar el catálogo en vivo permitiría revaluar hacia atrás.</para>
///
/// <para><b>Por qué se decide al revisar y no al aprobar en bloque.</b> Se llaman «criterios a
/// evaluar»: al publicar se anuncia qué se va a mirar, y al verificar se dice cuáles se cumplieron.
/// Darlos por buenos automáticamente los convertiría en un aumento de puntos disfrazado, que es
/// justo lo que la matriz existe para evitar.</para>
/// </summary>
public class PoolActivityExtraCriterion
{
    public int Id { get; set; }
    public int PoolActivityId { get; set; }

    /// <summary>
    /// El criterio del catálogo del que salió. Sin clave foránea y solo como traza: si alguien
    /// desactiva o depura un criterio, esta fila tiene que seguir explicando de dónde salieron los
    /// puntos que ya se cobraron.
    /// </summary>
    public int? ScoringCriterionId { get; set; }

    /// <summary>Copia del nombre en el momento de publicar la actividad.</summary>
    public string Name { get; set; } = "";

    /// <summary>Copia de los puntos en el momento de publicar. Congelados.</summary>
    public int Points { get; set; }

    /// <summary>
    /// Nulo mientras nadie lo ha evaluado; true/false cuando el líder verifica la entrega. Se
    /// distingue «todavía no se ha mirado» de «se miró y no se cumplió» a propósito: al devolver una
    /// entrega, lo segundo es información que quien la corrige necesita.
    /// </summary>
    public bool? IsMet { get; set; }

    public DateTime? EvaluatedAtUtc { get; set; }

    /// <summary>Por qué se dio por cumplido o no. Opcional, pero es lo que evita discutirlo dos veces.</summary>
    public string? Comment { get; set; }

    public PoolActivity Activity { get; set; } = null!;
}

/// <summary>
/// Una celda de la matriz de puntos: cuánto vale un tipo de trabajo con una complejidad dada, y en
/// cuántas HORAS se espera terminarlo.
///
/// Es una tabla y no un ajuste en JSON porque cada celda se edita, se consulta y se audita por
/// separado, y porque la unicidad de (tipo, complejidad) la debe garantizar la base y no la
/// disciplina de quien escriba el código que la lea. Es el mismo criterio por el que
/// <see cref="ScoringCriterion"/> es una tabla.
/// </summary>
public class PoolPointsMatrixEntry
{
    public int Id { get; set; }
    public PoolWorkType WorkType { get; set; }
    public PoolComplexity Complexity { get; set; }

    /// <summary>Puntos que otorga. Debe ser mayor que cero: una actividad que no suma no es trabajo.</summary>
    public int Points { get; set; }

    /// <summary>
    /// <b>OBSOLETA PARA LA WEB. NO LA LEAS NI LA ESCRIBAS DESDE AQUÍ.</b> Días desde que se toma
    /// hasta que se espera entregada; la sustituye <see cref="HorasLimite"/>.
    ///
    /// <para>Sigue mapeada por lo mismo que <see cref="PoolActivity.DiasLimite"/>: la aplicación de
    /// escritorio la lee en producción hasta el día del corte. <b>Se puede tirar DESPUÉS del
    /// corte</b>, a la vez que la de <c>PoolActivities</c> y no antes.</para>
    /// </summary>
    public int DiasLimite { get; set; }

    /// <summary>
    /// Horas desde que se toma hasta que se espera entregada. 0 = sin fecha límite.
    ///
    /// <para>Es la fuente del plazo de las TAREAS y los REQUERIMIENTOS; en los BUGS no manda, porque
    /// ahí el plazo lo fija el líder actividad por actividad.</para>
    ///
    /// <para>No es nulable: una celda sin plazo se escribe como 0, que ya significa «sin fecha
    /// límite». Con un nulo habría dos formas de decir lo mismo y cada consulta tendría que tratar
    /// las dos.</para>
    /// </summary>
    public decimal HorasLimite { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
}

/// <summary>
/// Un punto del checklist de un tipo de actividad: la definición de «terminado» que el líder
/// escribe una vez y vale para todas las actividades de ese tipo.
///
/// No hay tabla de cabecera para la plantilla porque hay exactamente UNA por tipo: la plantilla es
/// el conjunto de sus puntos activos.
/// </summary>
public class PoolChecklistTemplateItem
{
    public int Id { get; set; }
    public PoolWorkType WorkType { get; set; }
    public string Text { get; set; } = "";
    public int Orden { get; set; }

    /// <summary>Exige un enlace (al PR, al work item) para poder marcarlo. Es lo que hace
    /// verificable el checklist: sin evidencia, marcar una casilla no cuesta nada.</summary>
    public bool RequiereEvidencia { get; set; }

    /// <summary>
    /// Los puntos no se borran, se desactivan: las actividades en curso llevan su propia copia, pero
    /// borrar la plantilla haría perder qué se pedía cuando se hicieron las anteriores.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Un punto del checklist de UNA actividad concreta. Es una COPIA de la plantilla, hecha en el
/// momento de tomarla.
///
/// Copiada y no referenciada por la misma razón que los puntos están congelados: a quien tomó una
/// actividad se le puede exigir lo que se le pidió al tomarla, no lo que el líder agregue a la
/// plantilla mientras la trabaja. Y se copia al TOMARLA, no al crearla, para que el líder pueda
/// seguir afinando la plantilla mientras la actividad espera en el pool.
/// </summary>
public class PoolActivityChecklistItem
{
    public int Id { get; set; }
    public int PoolActivityId { get; set; }

    public string Text { get; set; } = "";
    public int Orden { get; set; }
    public bool RequiereEvidencia { get; set; }

    public bool IsDone { get; set; }
    public DateTime? DoneAtUtc { get; set; }

    /// <summary>Enlace que respalda este punto. Solo http/https, validado al marcarlo.</summary>
    public string? EvidenceUrl { get; set; }

    public PoolActivity Activity { get; set; } = null!;
}
