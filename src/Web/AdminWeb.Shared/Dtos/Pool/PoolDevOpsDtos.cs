using AdminWeb.Shared.Dtos.DevOps;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Pool;

// ── El vínculo con Azure DevOps ──────────────────────────────────────────────────

/// <summary>
/// Ligar (o desligar) una actividad del pool con un work item de Azure DevOps.
///
/// <para><b>Los dos campos son opcionales y no es un descuido:</b> se puede escribir el número a
/// mano, o pegar la dirección del work item y dejar que el servidor saque el número de ella —que es
/// lo que la gente hace de verdad: copiar la barra del navegador—. Cuando vienen los dos y no
/// coinciden, se RECHAZA en vez de elegir uno: un formulario que dice «#1234» sobre un enlace que
/// apunta a «#5678» está mal de una de las dos maneras, y adivinar cuál es lo que acabaría
/// escribiendo el esfuerzo en el ticket equivocado.</para>
///
/// <para><b>Con los dos vacíos se DESLIGA.</b> Es la salida cuando el ticket resultó no ser el que
/// era, o cuando DevOps no admite lo que se le manda y el vínculo solo produce ruido. Que los dos
/// vengan vacíos es la ÚNICA forma de desligar: una dirección de la que no se puede sacar ningún
/// número se rechaza en vez de tomarse por un «quítalo», porque quien la pegó estaba ligando y
/// soltarle el vínculo sería hacer lo contrario de lo que pidió.</para>
/// </summary>
public record LigarConDevOpsRequest(int? WorkItem, string? Enlace);

/// <summary>
/// Cómo está el vínculo de UNA actividad con DevOps y qué falta por mandar.
///
/// <para>Viaja el número y APARTE lo que se sabe del ticket sincronizado, porque son dos cosas
/// distintas: el número siempre está si la actividad está ligada, y el título solo si el ticket se
/// ha traído alguna vez. Un vínculo a un ticket que todavía no se ha sincronizado es válido y hay
/// que poder enseñarlo sin fingir que se conoce su título.</para>
/// </summary>
/// <param name="SincronizadoAqui">Falso cuando no hay fila en <c>DevOpsTickets</c> para ese número.
/// No impide nada: se puede empujar y comentar igual, porque esas rutas van contra DevOps por número.
/// Solo significa que aquí no se sabe cómo se llama.</param>
/// <param name="AsignadoEnDevOps">A nombre de quién está el work item, según lo último que se sabe
/// aquí. Nulo cuando no está asignado o cuando el ticket no se ha sincronizado.</param>
/// <param name="QuienLaTiene">Cómo se llama quien tiene tomada la actividad, para poder decir a
/// nombre de quién TENDRÍA que estar. Nulo si nadie la ha tomado.</param>
/// <param name="EsfuerzoPendiente">El pool tiene un esfuerzo que DevOps todavía no. Deriva de
/// comparar las horas con la marca de agua, no de una bandera que alguien tuviera que acordarse de
/// bajar.</param>
/// <param name="AsignacionPendiente">El work item no está a nombre de quien tomó la actividad.
/// Soltar el reclamo NO deja esto pendiente: al devolver una actividad al pool no se desasigna el
/// ticket, porque vaciar allá un campo que quizá puso otra persona sería destruir información
/// ajena.</param>
/// <param name="EstadoPendiente">El work item todavía no se ha movido a «en progreso» para este
/// reclamo. Es de UNA VEZ al tomarla: un ticket que el equipo ya movió a «Resolved» no está
/// pendiente de nada, y arrastrarlo de vuelta sería pisar una decisión de alguien.</param>
/// <param name="UltimoError">Por qué falló el último intento. Nulo si el último terminó bien.</param>
public record VinculoDevOpsDto(
    int PoolActivityId,
    string Titulo,
    int? WorkItem,
    string? Enlace,
    bool SincronizadoAqui,
    string? TituloDelTicket,
    string? EstadoDelTicket,
    string? AsignadoEnDevOps,
    string? QuienLaTiene,
    decimal? HorasEstimadas,
    decimal? EsfuerzoEnviado,
    PoolPriority Prioridad,
    int PrioridadEnDevOps,
    int? PrioridadEnviada,
    bool EsfuerzoPendiente,
    bool PrioridadPendiente,
    bool AsignacionPendiente,
    bool EstadoPendiente,
    DateTime? UltimoIntentoUtc,
    string? UltimoError)
{
    /// <summary>Hay algo que el pool dice y DevOps todavía no.</summary>
    public bool Pendiente =>
        EsfuerzoPendiente || PrioridadPendiente || AsignacionPendiente || EstadoPendiente;
}

/// <summary>
/// Lo que quedó sin llegar a DevOps, para que el líder lo vea junto y pueda reintentarlo.
///
/// <para>Existe porque el aviso del momento no basta: quien publicó la actividad vio el mensaje una
/// vez y cerró la pestaña. Sin una lista, «el pool dice una cosa y DevOps otra» sería un hecho que
/// nadie puede consultar.</para>
/// </summary>
public record PendientesDeDevOpsDto(IReadOnlyList<VinculoDevOpsDto> Pendientes);

// ── Comentarios ──────────────────────────────────────────────────────────────────

/// <summary>
/// El hilo del work item ligado, más si quien lo pide puede escribir en él.
///
/// <para>Solo llega a quien gobierna la actividad —el líder, o quien la tenga tomada—: leer el hilo
/// sale a DevOps con el token de la instalación cuando quien pide no tiene el suyo, y sin esa regla
/// el token compartido sería una puerta trasera a tickets ajenos.</para>
/// </summary>
/// <param name="PuedoComentar">Falso cuando falta el token PERSONAL, que llegados aquí es lo único
/// que puede faltar. Viaja resuelto para que la pantalla no tenga que ofrecer un cuadro de texto que
/// va a fallar al pulsar «Enviar».</param>
/// <param name="PorQueNoPuedoComentar">El motivo, ya redactado y con a dónde ir. Hoy es el caso de
/// TODO el mundo el primer día, así que no puede ser un «no autorizado» seco.</param>
public record HiloDeDevOpsDto(
    int PoolActivityId,
    int WorkItem,
    string Titulo,
    IReadOnlyList<ComentarioDevOpsDto> Comentarios,
    bool PuedoComentar,
    string? PorQueNoPuedoComentar);

// El comentario NO tiene aquí su contrato, y no es un olvido: viaja como MULTIPART —«texto» más
// «imagenes»— igual que el de la pantalla de tickets, porque lleva capturas. Un registro de C# no
// puede describir eso, y dejarlo escrito aquí como si fuera JSON sería documentar una forma que ya
// no existe. La ruta que lo recibe explica por qué es multipart siempre, lleve o no evidencias.
//
// Que se admitan evidencias es un cambio de criterio deliberado: antes se argumentaba que la prueba
// de una actividad ya tenía su sitio en los enlaces del checklist. Pero el checklist se mira AQUÍ y
// el ticket se mira ALLÁ —quien lee el work item en DevOps no entra a esta aplicación—, así que un
// enlace del checklist no es evidencia para él. Ver PoolDevOpsService.ComentarAsync.
