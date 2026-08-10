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
/// </list>
/// </summary>
public enum PoolActivityStatus
{
    Disponible = 0,
    Tomada = 1,
    EnRevision = 2,
    Devuelta = 3,
    Aceptada = 4,
    Retirada = 5
}
