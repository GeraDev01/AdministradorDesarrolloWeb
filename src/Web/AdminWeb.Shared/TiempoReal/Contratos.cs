namespace AdminWeb.Shared.TiempoReal;

/// <summary>
/// La dirección del canal en vivo. Vive en Shared para que el cliente y el servidor no puedan
/// discrepar: un nombre mal escrito en un lado deja de recibir avisos y no falla al compilar.
/// </summary>
public static class RutasDeTiempoReal
{
    public const string Hub = "/hubs/app";
}

/// <summary>
/// Nombres de los eventos que el servidor envía al navegador. Por lo mismo que las rutas: aquí los
/// nombres se escriben una vez.
/// </summary>
public static class Eventos
{
    /// <summary>Llegó un aviso nuevo para este usuario.</summary>
    public const string AvisoNuevo = "AvisoNuevo";

    /// <summary>Cambiaron los contadores del menú (avisos, pendientes de revisar…).</summary>
    public const string ContadoresCambiaron = "ContadoresCambiaron";

    /// <summary>Alguien entró, salió o cambió su estado: el tablero de presencia debe repintarse.</summary>
    public const string PresenciaCambiada = "PresenciaCambiada";

    // ── Despliegues ──────────────────────────────────────────────────────────────
    //
    // El despliegue ya no corre en la máquina de quien pulsa el botón sino en el servidor, así que
    // la pantalla no puede enterarse de lo que pasa por el simple hecho de estar ejecutándolo: se lo
    // tienen que contar. Estos tres eventos son ese relato, y son exactamente los dos IProgress del
    // escritorio (uno de bitácora, otro de porcentaje) más el cierre, que allí se sabía porque el
    // método volvía.

    /// <summary>Cambió el porcentaje, el servidor o el detalle del despliegue que se está mirando.</summary>
    public const string DespliegueAvanzo = "DespliegueAvanzo";

    /// <summary>Un renglón nuevo de la bitácora del despliegue.</summary>
    public const string DespliegueRegistro = "DespliegueRegistro";

    /// <summary>El despliegue terminó (bien, a medias, fallido o cancelado).</summary>
    public const string DespliegueTermino = "DespliegueTermino";
}

/// <summary>Métodos que el navegador invoca en el servidor.</summary>
public static class Acciones
{
    public const string LatidoPresencia = "LatidoPresencia";
    public const string LatidoCronometro = "LatidoCronometro";
    public const string CambiarEstado = "CambiarEstado";

    /// <summary>Empieza a recibir el avance de un despliegue concreto.</summary>
    public const string SeguirDespliegue = "SeguirDespliegue";

    /// <summary>Deja de recibirlo. Lo dice la pantalla al cerrarse; NO cancela el despliegue.</summary>
    public const string DejarDespliegue = "DejarDespliegue";
}

/// <summary>
/// Los grupos de difusión. Se avisa a un grupo y no a todo el mundo: los contadores de una persona
/// no le importan a nadie más, y el tablero de presencia solo lo mira quien lo tiene abierto.
/// </summary>
public static class Grupos
{
    public static string Usuario(int userId) => $"usuario-{userId}";

    public const string Administradores = "administradores";

    /// <summary>
    /// Quienes están mirando un despliegue concreto.
    ///
    /// Un grupo POR TRABAJO y no uno solo para todos los despliegues: por la bitácora de un
    /// despliegue pasan rutas y respuestas de servidores ajenos, y no tienen por qué llegarle a
    /// quien está mirando otro. Además permite que dos personas sigan dos despliegues distintos a la
    /// vez, que es algo que en el escritorio no podía ocurrir.
    ///
    /// Pertenecer al grupo es solo MIRAR: entrar y salir de él no arranca ni cancela nada. Es lo que
    /// hace que cerrar la pestaña no mate el despliegue.
    /// </summary>
    public static string Despliegue(int jobId) => $"despliegue-{jobId}";
}
