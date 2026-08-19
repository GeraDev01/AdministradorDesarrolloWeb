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
    Propia = 7
}
