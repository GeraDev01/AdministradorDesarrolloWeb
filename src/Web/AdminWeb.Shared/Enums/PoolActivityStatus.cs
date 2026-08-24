namespace AdminWeb.Shared.Enums;

/// <summary>
/// Estados de una actividad del pool.
/// <list type="bullet">
/// <item><b>Disponible</b>: en el pool, cualquiera puede tomarla.</item>
/// <item><b>Tomada</b>: alguien la está trabajando.</item>
/// <item><b>EnRevision</b>: entregada, esperando la verificación del líder.</item>
/// <item><b>Devuelta</b>: el líder la regresó con un motivo; sigue siendo de quien la tomó.</item>
/// <item><b>Aceptada</b>: verificada; ya generó sus puntos. Es terminal.</item>
/// <item><b>Retirada</b>: el líder la quitó del pool antes de que nadie la tomara.</item>
/// <item><b>PorClasificar</b>: entró sola desde un work item de Azure DevOps y todavía no tiene
///     tipo, complejidad ni horas, así que <b>no vale puntos y no se puede tomar</b>. Espera a que
///     el líder decida qué es; al hacerlo pasa a Disponible con el valor de la matriz.</item>
/// <item><b>Propia</b>: la hizo el líder. No pasó por el pool —nadie pudo tomarla— y <b>no abonó
///     puntos a nadie</b>. Es terminal, y existe para que el trabajo que el líder resolvió él mismo
///     quede contado como lo que fue y no como una actividad libre que nadie quiso.</item>
/// <item><b>Descuento</b>: no es trabajo. Es una penalización que el líder aplica a una persona,
///     tasada por un criterio del catálogo, que <b>nace ya pagada y cerrada</b>: nadie la toma,
///     nadie la cronometra y no hay nada que entregar ni que verificar. Terminal.</item>
/// </list>
///
/// <para><b>Disponible es el valor 0</b>, y conviene saberlo: toda fila que se cree sin fijar el
/// estado nace TOMABLE. Por eso el alta automática lo escribe siempre a mano.</para>
///
/// <para><b>El valor nuevo va al final</b> y no en el hueco que le tocaría por orden del ciclo: los
/// números están escritos en la columna de una base en producción, y renumerar convertiría cada
/// actividad aceptada en otra cosa. Dónde sale en pantalla lo decide el orden de presentación, no el
/// número.</para>
/// </summary>
public enum PoolActivityStatus
{
    Disponible = 0,
    Tomada = 1,
    EnRevision = 2,
    Devuelta = 3,
    Aceptada = 4,
    Retirada = 5,
    PorClasificar = 6,

    /// <summary>La hizo el líder: ni se tomó ni dio puntos. Terminal.</summary>
    Propia = 7,

    /// <summary>
    /// Penalización aplicada por el líder. Nace pagada, cerrada y con puntos negativos.
    ///
    /// <para><b>No es un <c>Retrabajo</c>, y la diferencia cabe en una línea: si hay algo que hacer,
    /// es retrabajo; si no hay nada que hacer, es un descuento.</b> El retrabajo se publica, se toma,
    /// se cronometra, se entrega y se verifica —hay trabajo real, aunque no debería haber hecho
    /// falta—, y su número sale de la MATRIZ. El descuento no lo toma nadie, y su número sale de un
    /// CRITERIO del catálogo que nombra el hecho. Fundirlos daría una fila que a veces tiene reclamo
    /// y cronómetro y a veces no, con el mismo tipo.</para>
    ///
    /// <para><b>Por qué un estado nuevo y no reutilizar <c>Aceptada</c>.</b> Reutilizarlo obligaría a
    /// que TODAS las consultas que hoy filtran por «aceptada» aprendieran a distinguir —el detalle
    /// del mes, los reportes del pool, las cuentas de entregas— y una lista de estados que alguien
    /// olvida es el modo de fallo que este modelo más teme. Un valor nuevo lo hace visible al
    /// compilador y a las pruebas; las traducciones tienen rama por omisión, así que degrada bien.</para>
    /// </summary>
    Descuento = 8
}
