namespace AdminWeb.Shared.Enums;

/// <summary>
/// Los colores con los que las pantallas pintan el punto que acompaña a cada estado.
///
/// Existe por lo mismo que <c>EtiquetasDeTrabajo.ColorDePrioridad</c>: al quitarse los emoji,
/// las etiquetas se quedaron con la palabra sola y en una rejilla larga eso obliga a LEER trece
/// renglones para encontrar el que reclama. El punto de color repone esa lectura de un vistazo, pero
/// pintado con una variable del tema —que obedece al modo claro y al oscuro— en vez de con un
/// símbolo que dibuja el sistema operativo a su manera.
///
/// Están AQUÍ y no en cada pantalla porque el mismo estado se enseña en varios sitios: permisos y
/// vacaciones se ven en «los míos» y en la pantalla del líder, las sugerencias en dos, el pool en
/// tres. Cuando cada pantalla se guardaba su propia copia, las copias divergían sin que nadie se
/// enterara —el pool pintaba «En revisión» de VERDE en la pantalla del líder, o sea celebraba como
/// un logro un trabajo que todavía nadie ha verificado—, y esa divergencia no la delata ninguna
/// prueba porque cada pantalla se mira por separado.
///
/// <para>ATENCIÓN: ESTOS MÉTODOS DEVUELVEN CSS, NO COLORES. Devuelven la cadena «var(--…)» literal,
/// que solo significa algo dentro de un atributo <c>style</c> de una página: quien la resuelve es el
/// NAVEGADOR, no .NET. Es lo que permite que el mismo estado se lea bien en los dos temas sin que
/// este archivo sepa cuál está puesto. Este archivo vive en el proyecto COMPARTIDO con el servidor y
/// hoy solo lo consumen las pantallas, así que es seguro; si mañana alguien lo usa para generar un
/// Word, un PDF o un correo, <c>var()</c> no resuelve ahí y el texto saldrá SIN COLOR y sin ningún
/// error que lo delate. En ese caso hace falta una tabla aparte con valores reales.</para>
///
/// <para>TRES REGLAS QUE VIENEN CON EL PATRÓN Y NO SON OPCIONALES. La primera: la PALABRA VA SIEMPRE
/// AL LADO del punto. Un punto de color solo no lo distingue quien no separa el rojo del verde, ni
/// nadie que imprima la tabla; el color agrupa («esto reclama», «esto ya terminó») y es el texto el
/// que identifica. La segunda: el borde gris del punto no es decoración —si un día falta la
/// variable, el punto se queda sin relleno pero SE SIGUE VIENDO, en vez de desaparecer sin que nadie
/// note que algo se rompió—. La tercera: el color sale del ENUM, nunca de comparar la cadena del
/// texto, porque comparar el texto se rompe en silencio en cuanto alguien cambia una etiqueta.</para>
///
/// <para>Casi todo el vocabulario de color son los CINCO cajones semánticos de la aplicación: éxito,
/// aviso, peligro, en curso y neutro. Que dos estados de una misma lista compartan cajón es normal y
/// deliberado —los distingue la palabra—; inventar tonos nuevos para no repetir sería volver a tener
/// una paleta que nadie sabe leer. Ninguno de estos valores se escribe en hex: un hex se ve bien en
/// un modo y queda ilegible en el otro, y nadie se entera hasta que cambia de modo semanas
/// después.</para>
///
/// <para>La ÚNICA que no sale de esos cinco cajones es <see cref="ColorDePresencia"/>, y se dice aquí
/// para que no parezca un descuido: en qué anda alguien no es un éxito ni un fallo, así que sale de la
/// escala de series —las variables <c>--adminweb-presencia-*</c>—. Está explicado en la propia
/// función.</para>
///
/// <para>El color del SLA no está aquí sino en <c>EtiquetasDeSla</c>, junto a
/// <c>SlaCompromisoDto</c>: no sale de un enum suelto sino del estado MÁS dos banderas del DTO, y su
/// palabra y su color tienen que decidirse en el mismo sitio o acabarán diciendo cosas distintas.</para>
/// </summary>
public static class ColoresDeEstado
{
    // ── Pool de actividades ──────────────────────────────────────────────────────

    /// <summary>
    /// Color del estado de una actividad del pool. Es el ciclo de vida de un encargo: se publica,
    /// alguien la toma, la entrega, el líder la verifica.
    ///
    /// <para>«En revisión» va al ÁMBAR y no al verde, que es lo que pintaba la pantalla del líder.
    /// Por verificar es trabajo PENDIENTE —espera a una persona, igual que «Por entregar» en
    /// requerimientos—, no un logro; el verde se gana al aceptarla. «Devuelta» va al rojo porque hay
    /// que rehacerla y volver a entregarla, que es lo mismo que dice el «Rechazado» de la
    /// autocalificación.</para>
    ///
    /// <para>Los dos neutros no significan lo mismo entre sí, y da igual: significan lo mismo PARA
    /// QUIEN MIRA, que es que esa fila no le reclama nada. «Disponible» todavía no es de nadie —el
    /// mismo cajón que «Por estimar»— y «Retirada» se quitó del pool a propósito.</para>
    /// </summary>
    public static string ColorDelPool(PoolActivityStatus e) => e switch
    {
        PoolActivityStatus.Disponible => "var(--rz-text-secondary-color)",  // libre, no es de nadie
        PoolActivityStatus.Tomada     => "var(--rz-info)",                  // en curso
        PoolActivityStatus.EnRevision => "var(--rz-warning)",               // espera al líder
        PoolActivityStatus.Devuelta   => "var(--rz-danger)",                // hay que rehacerla
        PoolActivityStatus.Aceptada   => "var(--rz-success)",
        // Por clasificar reclama al LÍDER, igual que «Por verificar»: es lo único de la rejilla que
        // no avanza hasta que él la mire. Comparte el ámbar por eso, no por parecido de ciclo.
        PoolActivityStatus.PorClasificar => "var(--rz-warning)",
        _                             => "var(--rz-text-secondary-color)"   // Retirada
    };

    // ── Ausencias ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Color del estado de una solicitud de permiso. Son los cuatro que ya usaban «Mis permisos» y la
    /// pantalla del líder, sin cambiarle el significado a ninguno: subirlos aquí es para que sigan
    /// siendo los mismos mañana, no para renegociarlos de paso.
    ///
    /// <para>El ámbar de «Cancelada» sí dice algo que el neutro perdería: distingue «la retiró quien
    /// la pidió» de «se la negaron», que son dos historias distintas de la misma fila.</para>
    /// </summary>
    public static string ColorDePermiso(LeaveStatus e) => e switch
    {
        LeaveStatus.Aprobada  => "var(--rz-success)",
        LeaveStatus.Rechazada => "var(--rz-danger)",
        LeaveStatus.Cancelada => "var(--rz-warning)",
        _                     => "var(--rz-text-secondary-color)"           // Pendiente
    };

    /// <summary>
    /// Color del estado de una solicitud de vacaciones. Mismos cuatro cajones que los permisos,
    /// porque para quien las pide es el mismo trámite.
    ///
    /// <para>Va como función HERMANA y no como una sola con conversión de por medio, aunque
    /// <see cref="VacationStatus"/> y <see cref="LeaveStatus"/> tengan hoy los mismos valores: son
    /// dos enumerados distintos y nada obliga a que sigan coincidiendo. Una conversión los ataría a
    /// coincidir para siempre, y el día que uno crezca —«Disfrutada», por ejemplo— el puente se
    /// rompería en silencio pintando el estado nuevo con el color del que ocupa ese número.</para>
    /// </summary>
    public static string ColorDeVacaciones(VacationStatus e) => e switch
    {
        VacationStatus.Aprobada  => "var(--rz-success)",
        VacationStatus.Rechazada => "var(--rz-danger)",
        VacationStatus.Cancelada => "var(--rz-warning)",
        _                        => "var(--rz-text-secondary-color)"        // Pendiente
    };

    // ── Sugerencias ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Color del estado de una sugerencia.
    ///
    /// Las dos pantallas decían ya lo mismo pero en IDIOMAS distintos: «Sugerencias del equipo» con
    /// variables del tema y «Mis sugerencias» con un <c>BadgeStyle</c> de Radzen. Lo segundo no se
    /// puede compartir desde aquí —este proyecto no referencia Radzen, y no debe—, así que la versión
    /// que sube es la de las variables, que además es la que sirve para pintar el punto.
    ///
    /// <para>«Aceptada» e «Implementada» comparten el verde a propósito: las dos son un sí y la
    /// palabra de al lado dice en cuál de los dos momentos va. «Nueva» va al neutro porque recién
    /// enviada no reclama todavía a nadie.</para>
    /// </summary>
    public static string ColorDeSugerencia(SuggestionStatus e) => e switch
    {
        SuggestionStatus.Aceptada or
        SuggestionStatus.Implementada => "var(--rz-success)",
        SuggestionStatus.EnRevision   => "var(--rz-warning)",
        SuggestionStatus.Rechazada    => "var(--rz-danger)",
        _                             => "var(--rz-text-secondary-color)"   // Nueva
    };

    // ── Autocalificación ─────────────────────────────────────────────────────────

    /// <summary>
    /// Color de la aprobación de unos puntos: los que el desarrollador se registra nacen pendientes y
    /// solo cuentan cuando el jefe los aprueba.
    ///
    /// <para>Devuelve la variable SOLA y no la declaración entera («color:var(--…)»), que es como
    /// estaba en la pantalla. La diferencia importa: eso se interpolaba dentro de un atributo
    /// <c>style</c> y no vale para el <c>background</c> del punto, así que había que elegir entre
    /// tener color de texto o tener punto. Quien la use para el texto pone el <c>color:</c> delante,
    /// que es la parte trivial.</para>
    ///
    /// <para>El caso por defecto cierra en NEUTRO y no en la cadena vacía. Un <c>background</c> vacío
    /// deja un punto hueco, y un punto hueco no se lee como «neutro»: se lee como que el pintado
    /// falló.</para>
    ///
    /// <para>Es la misma paleta que la bitácora da a Éxito/Falló/Denegado porque es el mismo tipo de
    /// dato: cómo acabó algo que se intentó.</para>
    /// </summary>
    public static string ColorDeAprobacion(PointApprovalStatus e) => e switch
    {
        PointApprovalStatus.Aprobado  => "var(--rz-success)",
        PointApprovalStatus.Pendiente => "var(--rz-warning)",
        PointApprovalStatus.Rechazado => "var(--rz-danger)",
        _                             => "var(--rz-text-secondary-color)"
    };

    // ── Despliegues ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Color del estado de un despliegue ya lanzado.
    ///
    /// Cubre los SEIS valores del enumerado. La versión de la pantalla dejaba «En curso» y
    /// «Pendiente» sin color —caían en la cadena vacía—, y esos dos huecos son justo los que más se
    /// miran: son las filas de lo que está pasando ahora mismo.
    ///
    /// <para>«Parcial» va al ámbar junto a «Cancelado» y no al verde: unos servidores sí y otros no
    /// es media instalación, que es precisamente lo que hay que ir a revisar. Antes esto se reportaba
    /// como «Completado», y esa mentira es la razón de que el valor exista.</para>
    /// </summary>
    public static string ColorDeDespliegue(JobStatus e) => e switch
    {
        JobStatus.Completado => "var(--rz-success)",
        JobStatus.Fallido    => "var(--rz-danger)",
        JobStatus.Cancelado  => "var(--rz-warning)",
        JobStatus.Parcial    => "var(--rz-warning)",                        // media instalación
        JobStatus.EnCurso    => "var(--rz-info)",                           // pasando ahora
        _                    => "var(--rz-text-secondary-color)"            // Pendiente
    };

    /// <summary>
    /// Color de la cita de un despliegue programado: lo que todavía no se ha lanzado, o lo que se
    /// lanzó a su hora.
    ///
    /// <para>«Perdido» va al ROJO, igual que «Fallido», y ésa es la razón de ser de la pantalla: pasó
    /// su hora sin que ninguna aplicación estuviera abierta para ejecutarlo. Es el fallo silencioso,
    /// el que no deja rastro en ningún otro sitio, y el único que si se pinta en neutro no lo
    /// encuentra nadie. «Cancelado» sí es neutro porque se dejó sin efecto a propósito: no reclama
    /// nada.</para>
    /// </summary>
    public static string ColorDeCitaDeDespliegue(ScheduledDeploymentStatus e) => e switch
    {
        ScheduledDeploymentStatus.Programado  => "var(--rz-info)",          // agendado y en orden
        ScheduledDeploymentStatus.EnEjecucion => "var(--rz-warning)",       // en manos de alguien
        ScheduledDeploymentStatus.Completado  => "var(--rz-success)",
        ScheduledDeploymentStatus.Fallido     => "var(--rz-danger)",
        ScheduledDeploymentStatus.Perdido     => "var(--rz-danger)",        // nadie lo ejecutó
        _                                     => "var(--rz-text-secondary-color)"  // Cancelado
    };

    // ── Recursos de Azure ────────────────────────────────────────────────────────
    //
    // Estas dos llegan de RecursosAzure.razor SIN CAMBIAR NI UN VALOR: ya estaban bien y solo les
    // faltaba el sitio. Vivían en la pantalla con la nota de que ahí se quedaban «mientras no las use
    // nadie más», que es una condición que caduca sola: en cuanto aparezca una segunda vista de
    // recursos se copian, y a partir de esa copia empieza la divergencia. Sus etiquetas
    // (EstadoDeRecurso y Ambiente) ya viven en EtiquetasDeCatalogo, así que ahora la palabra y el
    // color se deciden en el mismo proyecto.

    /// <summary>
    /// Color del estado de un recurso de Azure. Es una escala de ESTADO, no de alarma: «Sin usar» no
    /// es un error, es un recurso que se está pagando sin que nadie lo use —que es justo lo que hay
    /// que mirar— y por eso va al ámbar y no al rojo. El rojo aquí está reservado a producción, que
    /// lo pinta <see cref="ColorDeAmbiente"/> en la columna de al lado.
    /// </summary>
    public static string ColorDeRecurso(AzureResourceStatus e) => e switch
    {
        AzureResourceStatus.EnUso    => "var(--rz-success)",
        AzureResourceStatus.EnPrueba => "var(--rz-info)",
        AzureResourceStatus.NoUsado  => "var(--rz-warning)",
        _                            => "var(--rz-text-secondary-color)"    // Archivado
    };

    /// <summary>
    /// Color del ambiente de un recurso. Éste SÍ es el semáforo, y es la escala de riesgo de tocar el
    /// recurso: producción reclama, staging avisa, compartido informa y desarrollo es donde no pasa
    /// nada.
    /// </summary>
    public static string ColorDeAmbiente(AzureEnvironment a) => a switch
    {
        AzureEnvironment.Produccion => "var(--rz-danger)",
        AzureEnvironment.Staging    => "var(--rz-warning)",
        AzureEnvironment.Compartido => "var(--rz-info)",
        _                           => "var(--rz-success)"                  // Desarrollo
    };

    // ── Presencia ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Color del punto de presencia: en qué anda quien tiene la aplicación abierta.
    ///
    /// <para>ES LA ÚNICA DE ESTE ARCHIVO QUE NO USA EL SEMÁFORO, y es a propósito. «Ocupado» no es un
    /// error ni «Disponible» un logro: si estos cinco salieran de éxito/peligro, el tablero estaría
    /// dando una alarma cada vez que alguien se pone a trabajar concentrado —que es literalmente lo
    /// que hacía la pantalla de Personas antes de esto—. Salen de la escala de SERIES, que existe
    /// justo para distinguir cosas que no están mejor ni peor unas que otras.</para>
    ///
    /// <para>Sube aquí porque la pintan DOS sitios: el botón de la barra superior
    /// (<c>Componentes/BotonDeEstado.razor</c>), donde cada quien elige el suyo, y la columna
    /// «Estado» de la pantalla de Personas, donde se ve el de los demás. Es el mismo dato visto por
    /// los dos lados y tiene que verse igual; mientras cada uno guardaba su copia de cinco líneas,
    /// coincidían —que es como empiezan las tablas que un día discrepan sin que ninguna prueba lo
    /// delate, porque cada pantalla se mira por separado—.</para>
    ///
    /// <para>«Ausente» y «Descanso» comparten el gris, y los separa la palabra: quien está en una
    /// pausa corta no reclama nada de quien mira, igual que quien lleva rato sin dar señales. Quien
    /// no está conectado se pinta con este mismo <see cref="PresenceState.Ausente"/> y no con su
    /// último estado —el servidor le escribe «Desconectado» de texto, así que un punto verde al lado
    /// sería el punto contradiciendo a la palabra—; esa decisión la toma la pantalla, que es la que
    /// sabe si hay conexión, y no esta tabla.</para>
    ///
    /// <para>DEPENDENCIA: las cinco variables <c>--adminweb-presencia-*</c> las declara
    /// <c>tema.css</c>. Mientras no estén, los puntos salen como un círculo con borde y sin relleno
    /// —feo pero legible, porque el nombre del estado va siempre al lado—. No las sustituyas por
    /// hexes para «arreglarlo»: eso ataría el color a un solo tema, que es el defecto del que se
    /// viene.</para>
    /// </summary>
    public static string ColorDePresencia(PresenceState e) => e switch
    {
        PresenceState.Disponible => "var(--adminweb-presencia-disponible)",
        PresenceState.Ocupado    => "var(--adminweb-presencia-ocupado)",
        PresenceState.EnReunion  => "var(--adminweb-presencia-reunion)",
        PresenceState.Comiendo   => "var(--adminweb-presencia-comida)",
        _                        => "var(--adminweb-presencia-ausente)"     // Ausente y Descanso
    };
}
