namespace AdminWeb.Shared.Enums;

/// <summary>
/// Con qué urgencia hay que tomar una actividad del pool.
///
/// <para>Es enum propio y no <see cref="RequirementPriority"/> reutilizada, aunque hoy tengan los
/// mismos cuatro valores. El repositorio ya sigue ese criterio —<see cref="NotePriority"/> también
/// vive aparte—, y la razón es que son escalas de dos cosas distintas: la de un requerimiento la
/// negocia el área que lo pide, y ésta la decide el líder al publicar. Compartir el tipo ataría las
/// dos a moverse juntas el día que una necesite un valor más.</para>
///
/// <para><b>No influye en los puntos.</b> Los puntos salen de la matriz tipo × complejidad y nada
/// más: si la urgencia subiera el valor, publicar «Crítica» sería la forma de regalar puntos, y
/// bastaría con eso para vaciar de sentido la matriz. La prioridad ordena la lista y se resalta;
/// no paga.</para>
/// </summary>
public enum PoolPriority
{
    Baja = 0,
    Media = 1,
    Alta = 2,
    Critica = 3
}
