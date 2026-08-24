using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Catálogo de criterios de puntuación con el que arranca la aplicación.
///
/// Los <b>individuales</b> están redactados en segunda persona, hablándole al desarrollador: es él
/// quien los elige al registrar una actividad, y «Propuesta o implementación de una mejora sin
/// solicitarse» no es una frase que nadie use para describir lo que hizo. Los de <b>equipo</b> se
/// quedan en tercera persona porque hablan del equipo entero, no de una persona.
///
/// <b>Por qué cada entrada guarda su nombre y su descripción ANTERIORES:</b> el sembrado es
/// idempotente por NOMBRE, así que al renombrarlos las bases ya existentes habrían acabado con las
/// dos versiones de cada criterio. <see cref="MigrarNomenclatura"/> renombra la fila vieja en lugar
/// de insertar una nueva, con lo que los <see cref="PointEntry"/> históricos siguen apuntando a su
/// criterio de siempre y ningún ranking cambia.
/// </summary>
public static class ScoringCriteriaSeed
{
    /// <summary>Nombre, descripción y puntos actuales, más los anteriores para poder migrar.</summary>
    public sealed record Criterio(
        string Nombre, string Descripcion, int Puntos,
        string NombreAnterior, string DescripcionAnterior)
    {
        /// <summary>
        /// Un criterio NUEVO, que nace con este nombre y nunca tuvo otro.
        ///
        /// <para>Existe para no tener que escribir el nombre dos veces al añadir uno: repetirlo a
        /// mano se lee como si viniera de un renombrado que se quedó a medias, y quien lo mire dentro
        /// de un año no sabría si es eso o un descuido. Así queda dicho en el propio constructor.</para>
        /// </summary>
        public Criterio(string nombre, string descripcion, int puntos)
            : this(nombre, descripcion, puntos, nombre, descripcion) { }

        /// <summary>
        /// Este criterio se llamaba de otra forma en la versión anterior del catálogo, así que hay
        /// una fila que renombrar en las bases que ya existen. Falso en los nuevos, que no tienen
        /// nada que migrar.
        /// </summary>
        public bool VieneDeOtroNombre =>
            !string.Equals(NombreAnterior, Nombre, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterios individuales. Los puntos NO cambian respecto a la versión anterior: tocarlos
    /// alteraría retroactivamente la comparación entre meses del histórico.
    /// </summary>
    public static readonly Criterio[] Individuales =
    [
        // ── Entrega y calidad ───────────────────────────────────────────────
        new("Entregaste a tiempo",
            "Cerraste el requerimiento en la fecha a la que te habías comprometido, sin pedir prórroga.", +10,
            "Entrega a tiempo", "El requerimiento se entregó en la fecha comprometida."),
        new("Entregaste antes de tiempo",
            "Terminaste antes de la fecha comprometida y sin recortar calidad para lograrlo.", +12,
            "Entrega anticipada", "Se entregó antes de la fecha comprometida sin sacrificar calidad."),
        new("Tu entrega no necesitó correcciones",
            "Lo que entregaste quedó bien a la primera: nadie tuvo que devolvértelo para arreglos.", +8,
            "Calidad / sin retrabajo", "La entrega no requirió correcciones significativas."),
        new("QA no te encontró ni un bug",
            "Tu entrega pasó todo el ciclo de pruebas sin que apareciera un solo defecto.", +12,
            "Cero bugs en QA", "El ciclo de pruebas no encontró defectos."),
        new("Corregiste un bug reportado",
            "Arreglaste un defecto que alguien había reportado, en QA o en producción.", +5,
            "Corrección de bug", "Se corrigió un defecto reportado en producción o QA."),
        new("Resolviste un incidente crítico",
            "Te tocó un problema urgente en producción y lo sacaste rápido.", +10,
            "Corrección de bug crítico", "Resolvió con rapidez un incidente crítico o de producción."),
        new("Entregaste tarde",
            "El requerimiento salió después de la fecha a la que te habías comprometido.", -10,
            "Entrega tardía", "El requerimiento se entregó después de la fecha comprometida."),
        new("Se te fue un bug a producción",
            "Apareció un defecto crítico en el ambiente productivo por algo que entregaste.", -15,
            "Bug en producción", "Se detectó un defecto crítico en el ambiente productivo."),
        new("Hubo que rehacer tu entrega",
            "Lo que entregaste necesitó correcciones importantes antes de poder usarse.", -8,
            "Retrabajo por calidad", "La entrega requirió correcciones importantes."),
        new("Se reabrió un bug que diste por cerrado",
            "Marcaste un defecto como resuelto y volvió a aparecer.", -8,
            "Reapertura de bug", "Un bug marcado como resuelto se reabrió."),
        new("Tu estimación se desvió mucho",
            "Lo que tomó y lo que estimaste no se parecieron, y no hubo una razón que lo explicara.", -5,
            "Estimación imprecisa", "Desviación considerable respecto a lo estimado sin justificación."),

        // ── Cómo trabajas ───────────────────────────────────────────────────
        new("Ayudaste a un compañero",
            "Dejaste lo tuyo un rato para sacar adelante el problema de alguien más del equipo.", +5,
            "Apoyo a compañeros", "Colaboración activa en la resolución de problemas del equipo."),
        new("Acompañaste a alguien nuevo",
            "Ayudaste a que un compañero recién llegado se integrara o aprendiera lo que necesitaba.", +8,
            "Mentoría / onboarding", "Ayudó a integrar o capacitar a un compañero nuevo."),
        new("Hiciste una buena revisión de código",
            "Revisaste el código de alguien a tiempo, con cuidado y dejando comentarios que sirvieron.", +5,
            "Buen code review", "Revisiones de código oportunas, cuidadosas y constructivas."),
        new("Dejaste la documentación al día",
            "Actualizaste la documentación técnica para que el siguiente no tenga que adivinar.", +5,
            "Documentación actualizada", "Se actualizó la documentación técnica del sistema."),
        // El que cierra el círculo con la base de conocimiento: aquél paga por ESCRIBIR una práctica,
        // éste por APLICARLA. Escribir se paga una vez y para siempre; aplicar se paga cada vez, que
        // es lo que se quiere premiar — un artículo que nadie usa no vale nada, y hasta ahora no había
        // forma de saber cuáles se usan.
        //
        // Vale MÁS que dejar la documentación al día (+5) y menos que traer algo nuevo: buscar antes
        // de inventar es la costumbre más cara de instalar y la que más ahorra cuando prende.
        new(PoolSeed.CriterioDePracticaDocumentada,
            "Resolviste algo aplicando una práctica que ya estaba escrita en la base de conocimiento, " +
            "en vez de inventarla otra vez. Se justifica y se dice cuál.", +8,
            "Aplicación de práctica documentada",
            "Aplicó una práctica registrada en la base de conocimiento, citando el artículo."),
        new("Agregaste pruebas automatizadas",
            "Escribiste pruebas que de verdad cubren algo, no solo para subir el número.", +8,
            "Cobertura de pruebas", "Agregó pruebas automatizadas significativas."),
        new("Automatizaste algo repetitivo",
            "Convertiste en script o en pipeline una tarea manual que se hacía una y otra vez.", +10,
            "Automatización", "Automatizó una tarea manual repetitiva (CI/CD, scripts, pruebas)."),
        new("Limpiaste código heredado",
            "Refactorizaste algo viejo y lo dejaste más fácil de mantener para quien venga detrás.", +10,
            "Reducción de deuda técnica", "Refactorizó código legado mejorando la mantenibilidad."),
        new("Llegaste al daily",
            "Asististe y participaste en la reunión diaria.", +3,
            "Cumplimiento del daily", "Asistencia y participación en la sesión diaria."),
        new("Seguiste los estándares del equipo",
            "Respetaste las guías de estilo y la arquitectura acordadas, sin inventar por tu cuenta.", +5,
            "Cumplimiento de estándares", "Siguió las guías de estilo y arquitectura del equipo."),
        new("Propusiste una mejora por tu cuenta",
            "Viste algo que se podía mejorar y lo propusiste —o lo hiciste— sin que nadie te lo pidiera.", +15,
            "Iniciativa / mejora", "Propuesta o implementación de una mejora sin solicitarse."),
        new("Trajiste algo nuevo que sirvió",
            "Introdujiste una tecnología o una práctica que terminó ayudando a todo el equipo.", +15,
            "Innovación técnica", "Introdujo una tecnología o práctica que benefició al equipo."),
        new("Atendiste bien a un cliente",
            "Resolviste la solicitud de un cliente y quedó conforme con cómo lo trataste.", +8,
            "Atención al cliente", "Resolvió una solicitud de cliente con excelente servicio."),
        new("Respondiste fuera de horario",
            "Te conectaste fuera de tu horario para apoyar en una contingencia o una guardia.", +8,
            "Disponibilidad en incidente", "Apoyó fuera de horario en una contingencia o guardia."),
        new("Terminaste una capacitación",
            "Completaste un curso o una certificación que tiene que ver con lo que haces.", +6,
            "Capacitación completada", "Terminó un curso o certificación relevante para su rol."),
        new("Faltaste al daily sin avisar",
            "No llegaste a la reunión diaria y nadie supo por qué.", -5,
            "Incumplimiento del daily", "Faltó o no participó en la reunión diaria sin aviso."),
        new("Entregaste sin documentar",
            "Cerraste sin dejar la documentación que hacía falta.", -5,
            "Falta de documentación", "Entregó sin la documentación requerida."),
        new("No seguiste los estándares",
            "Te saliste de las guías de estilo o de la arquitectura que el equipo había acordado.", -5,
            "Incumplimiento de estándares", "No siguió las guías de estilo/arquitectura acordadas."),

        // ── Git y Pull Requests ─────────────────────────────────────────────
        new("Abriste un PR pequeño y claro",
            "Tu Pull Request era acotado, enfocado y con una descripción que se entendía sola.", +5,
            "PR claro y pequeño", "Abrió un Pull Request pequeño, enfocado y con buena descripción."),
        new("Dejaste commits limpios",
            "Commits atómicos, ordenados y con mensajes que dicen de verdad qué cambió.", +4,
            "Historial de commits limpio", "Commits atómicos, ordenados y con mensajes descriptivos."),
        new("Desbloqueaste a alguien revisando su PR",
            "Revisaste a tiempo el Pull Request de un compañero que estaba esperando para avanzar.", +4,
            "Desbloqueó revisando un PR", "Revisó a tiempo el PR de un compañero para desbloquearlo."),
        new("No abriste el Pull Request",
            "Tus cambios se quedaron sin PR, así que nadie pudo revisarlos.", -8,
            "No subió el PR", "No creó el Pull Request de sus cambios para revisión."),
        new("Abriste un PR sin explicar nada",
            "El Pull Request no traía descripción ni contexto: quien revisa tiene que adivinar.", -4,
            "PR sin descripción", "Abrió un PR sin descripción ni contexto de los cambios."),
        new("Tu PR era imposible de revisar",
            "Metiste tantos cambios juntos que revisarlo bien de una sentada no era realista.", -4,
            "PR demasiado grande", "PR excesivamente grande y difícil de revisar en un solo cambio."),
        new("Planchaste cambios de otros",
            "Al hacer merge sobrescribiste trabajo de tus compañeros y se perdió código.", -15,
            "Planchó cambios en el merge", "Sobrescribió cambios de otros al hacer merge (pérdida de código)."),
        new("Resolviste mal unos conflictos",
            "Al resolver el merge dejaste algo roto.", -8,
            "Conflictos mal resueltos", "Resolvió conflictos de merge de forma incorrecta rompiendo algo."),
        new("Rompiste la rama principal",
            "Dejaste la rama principal con el build o el pipeline caído, y eso frena a todos.", -12,
            "Rompió la rama principal", "Dejó la rama principal con el build o el pipeline roto."),
        new("Subiste directo a una rama protegida",
            "Te saltaste la revisión subiendo cambios directo a una rama que no lo permite.", -8,
            "Commit directo a protegida", "Subió cambios directo a una rama protegida evitando la revisión."),
        new("Tus mensajes de commit no dicen nada",
            "Mensajes tipo «cambios» o «fix»: en un mes nadie va a saber qué tocaste ahí.", -3,
            "Mal mensaje de commit", "Mensajes de commit poco descriptivos o sin seguir la convención."),
        new("Mezclaste todo en un commit",
            "Juntaste en un mismo commit cambios que no tenían relación entre sí.", -3,
            "Commits no atómicos", "Mezcló múltiples cambios no relacionados en un solo commit."),
        new("Subiste secretos al repositorio",
            "Quedaron credenciales, llaves o secretos versionados. Eso hay que rotarlo, no solo borrarlo.", -20,
            "Subió secretos al repo", "Expuso credenciales, llaves o secretos en el control de versiones."),
        new("Versionaste archivos que no iban",
            "Subiste binarios, dependencias o temporales que no deberían estar en el repositorio.", -3,
            "Versionó archivos basura", "Incluyó binarios, dependencias o temporales que no debían versionarse."),
        new("Trabajaste sobre una rama vieja",
            "No actualizaste tu rama antes de empezar y provocaste conflictos que se podían evitar.", -3,
            "Trabajó sobre rama vieja", "No actualizó su rama antes de trabajar provocando conflictos evitables."),

        // ── Tickets y seguimiento ───────────────────────────────────────────
        new("Dejaste evidencias completas",
            "Adjuntaste al ticket capturas, pasos y resultados: quien lo lea después entiende qué pasó.", +5,
            "Evidencias completas", "Adjuntó evidencias claras (capturas, pasos, resultados) al ticket."),
        // NUEVO —de ahí el constructor de tres argumentos: nunca se llamó de otra forma—, y no es
        // un duplicado del de arriba: aquél premia que la evidencia EXISTA, éste que el ticket quede
        // explicado —qué se hizo, cómo se probó y con qué se respalda— en un comentario que alguien
        // de fuera pueda leer y entender sin preguntar. Es el criterio que se pide como extra en las
        // actividades del pool, y se puede cumplir sin salir de ellas: el panel del vínculo con
        // DevOps publica el comentario con sus capturas en el work item.
        new("Comentaste correctamente el ticket con evidencias",
            "Dejaste en el ticket un comentario que se entiende solo —qué hiciste, cómo lo probaste— " +
            "y con las capturas que lo respaldan.", +5),
        new("Mantuviste el ticket al día",
            "Fuiste actualizando el ticket con comentarios y con el estado real, sin que te lo pidieran.", +4,
            "Buen seguimiento del ticket", "Mantuvo el ticket actualizado con comentarios y estado real."),
        new("Reprodujiste y documentaste un bug",
            "Te tomaste el trabajo de reproducir un defecto y dejarlo documentado para quien lo corrija.", +5,
            "Reprodujo y documentó bug", "Reprodujo y documentó a detalle un defecto para su corrección."),
        new("Cerraste el ticket sin evidencias",
            "No adjuntaste nada que muestre la solución ni las pruebas que hiciste.", -5,
            "Sin evidencias en el ticket", "Cerró el ticket sin adjuntar evidencias de la solución o pruebas."),
        new("Dejaste el ticket mudo",
            "No registraste ni un comentario de avance: desde fuera no se sabía en qué ibas.", -4,
            "Sin comentarios en el ticket", "No registró comentarios ni actualizaciones de avance en el ticket."),
        new("El estado del ticket no reflejaba la realidad",
            "Lo dejaste en un estado que no correspondía a lo que de verdad estaba pasando.", -3,
            "Estado del ticket sin actualizar", "Dejó el ticket con un estado que no refleja el avance real."),
        new("No registraste tu tiempo",
            "El ticket quedó sin las horas que trabajaste en él.", -2,
            "No registró tiempo", "No registró el tiempo trabajado en el ticket."),

        // ── QA y sprint ─────────────────────────────────────────────────────
        new("Pasaste QA a la primera",
            "Tu entrega salió aprobada en la primera vuelta, sin una sola observación.", +10,
            "Aprobado por QA a la primera", "La entrega pasó QA a la primera sin observaciones."),
        new("Cumpliste todo lo que se pidió",
            "Tu entrega cumplía todos los criterios de aceptación desde la primera revisión.", +5,
            "Cumplió criterios de aceptación", "Cumplió todos los criterios de aceptación en la primera revisión."),
        new("No dejaste nada arrastrando",
            "Cerraste todo lo que te comprometiste en el sprint, sin pasar tareas al siguiente.", +6,
            "Sin carry over", "Cerró todos sus compromisos del sprint sin dejar arrastres."),
        new("Le facilitaste el trabajo a QA",
            "Preparaste datos o dejaste listo el ambiente para que QA pudiera validar sin pelearse.", +4,
            "Apoyo a QA", "Preparó datos o ambiente de prueba facilitando la validación de QA."),
        new("Dejaste tareas arrastrando",
            "Terminó el sprint con compromisos tuyos sin cerrar.", -6,
            "Carry over", "Dejó tareas comprometidas del sprint sin terminar (carry over)."),
        new("Arrastras tareas sprint tras sprint",
            "No es una vez: se ha vuelto costumbre pasar trabajo sin terminar al siguiente sprint.", -10,
            "Carry over recurrente", "Arrastra tareas sin terminar de forma recurrente entre sprints."),
        new("QA te rechazó la entrega",
            "No cumplía los criterios de aceptación y te la devolvieron.", -8,
            "Rechazo en QA", "La entrega fue rechazada por QA por no cumplir los criterios."),
        new("QA te encontró defectos",
            "Aparecieron bugs en el ciclo de pruebas de lo que entregaste.", -5,
            "Bug encontrado en QA", "QA encontró defectos en la entrega."),
        new("Entregaste sin probar",
            "Mandaste a QA algo que no habías probado ni una vez por tu cuenta.", -6,
            "Entregó sin probar", "Entregó a QA sin haber probado sus propios cambios."),
        new("Rompiste algo que ya funcionaba",
            "Tu cambio introdujo una regresión en funcionalidad que estaba bien.", -10,
            "Regresión introducida", "Introdujo una regresión que rompió funcionalidad existente."),

        // ── Seguridad, comunicación y proceso ───────────────────────────────
        new("Cuidaste la seguridad",
            "Validaste entradas, manejaste bien los secretos y no dejaste puertas abiertas.", +6,
            "Buenas prácticas de seguridad", "Aplicó buenas prácticas de seguridad (validación, manejo de secretos)."),
        new("Avisaste de un bloqueo a tiempo",
            "Levantaste la mano en cuanto viste el riesgo, y eso permitió reaccionar.", +4,
            "Comunicó bloqueo a tiempo", "Comunicó oportunamente un riesgo o bloqueo permitiendo mitigarlo."),
        new("Introdujiste una vulnerabilidad",
            "Tu cambio dejó una falla de seguridad conocida.", -10,
            "Vulnerabilidad introducida", "Introdujo una vulnerabilidad de seguridad conocida."),
        new("Te callaste un bloqueo",
            "No avisaste a tiempo de algo que te estaba frenando, y terminó afectando la entrega.", -5,
            "No comunicó un bloqueo", "No avisó a tiempo de un bloqueo que afectó la entrega."),
        new("Te saltaste el proceso",
            "No seguiste el flujo de trabajo que el equipo había acordado.", -4,
            "No siguió el proceso", "No siguió el flujo de trabajo o proceso acordado por el equipo."),
        new("Te ausentaste sin avisar",
            "No estuviste y nadie lo sabía, así que hubo que reacomodar todo sobre la marcha.", -6,
            "Ausencia sin aviso", "Se ausentó sin avisar, afectando la coordinación del equipo."),

        // ── Si eres Junior ──────────────────────────────────────────────────
        new("Resolviste una tarea casi solo (Junior)",
            "Sacaste adelante una tarea con muy poca ayuda. Se nota que ganaste autonomía.", +6,
            "Autonomía creciente (Junior)", "Junior que resolvió una tarea con mínima ayuda, mostrando autonomía."),
        new("Aprendiste rápido algo nuevo (Junior)",
            "Te pusiste al día con una tecnología del proyecto en poco tiempo.", +6,
            "Aprendizaje rápido (Junior)", "Junior que asimiló con rapidez una nueva tecnología del proyecto."),
        new("Preguntaste en el momento justo (Junior)",
            "Hiciste las preguntas correctas antes de avanzar, y eso evitó rehacer trabajo.", +3,
            "Buenas preguntas (Junior)", "Junior que hizo preguntas oportunas evitando retrabajo."),
        new("Necesitaste acompañamiento de más (Junior)",
            "Hubo que explicarte varias veces cosas que ya se habían visto.", -3,
            "Dependencia excesiva (Junior)", "Junior que requirió acompañamiento constante en tareas ya explicadas."),
        new("Te atascaste sin pedir ayuda (Junior)",
            "Pasaste demasiado tiempo trabado en algo antes de levantar la mano.", -3,
            "No pidió ayuda a tiempo (Junior)", "Se atascó demasiado tiempo sin pedir ayuda, retrasando la tarea."),

        // ── Si eres Mid ─────────────────────────────────────────────────────
        new("Te hiciste cargo de un módulo (Mid)",
            "Tomaste un módulo de principio a fin y respondiste por él.", +8,
            "Ownership de módulo (Mid)", "Mid que se hizo responsable de un módulo de principio a fin."),
        new("Tus estimaciones dieron en el blanco (Mid)",
            "Lo que estimaste se pareció mucho a lo que realmente tomó.", +6,
            "Estimaciones confiables (Mid)", "Mid cuyas estimaciones fueron consistentemente acertadas."),
        new("Resolviste sin escalar (Mid)",
            "Te topaste con un problema de tu nivel y lo sacaste sin subírselo a nadie.", +5,
            "Resolvió sin escalar (Mid)", "Mid que resolvió un problema propio de su nivel sin escalarlo."),
        new("Todavía necesitas supervisión (Mid)",
            "En tareas propias de tu nivel aún hizo falta que alguien estuviera encima.", -4,
            "Requiere supervisión (Mid)", "Mid que aún requiere supervisión en tareas propias de su nivel."),
        new("Entregaste sin aplicar criterio (Mid)",
            "Sacaste el trabajo sin el juicio técnico que se espera a tu nivel.", -4,
            "Entregó sin criterio (Mid)", "Mid que entregó sin aplicar el criterio esperado para su nivel."),

        // ── Si eres Senior ──────────────────────────────────────────────────
        new("Lideraste técnicamente (Senior)",
            "Guiaste las decisiones técnicas y destrabaste al equipo cuando hacía falta.", +12,
            "Liderazgo técnico (Senior)", "Senior que guió decisiones técnicas y destrabó al equipo."),
        new("Diseñaste una buena arquitectura (Senior)",
            "Propusiste un diseño sólido, que escala y que el equipo pudo seguir.", +12,
            "Diseño de arquitectura (Senior)", "Senior que propuso un diseño o arquitectura sólido y escalable."),
        new("Subiste el nivel del equipo (Senior)",
            "Con tu acompañamiento constante, los demás terminaron trabajando mejor.", +10,
            "Mentoría de nivel (Senior)", "Senior que elevó el nivel del equipo mediante mentoría constante."),
        new("Viste venir un riesgo (Senior)",
            "Anticipaste un problema técnico y lo mitigaste antes de que explotara.", +8,
            "Gestión de riesgos (Senior)", "Senior que anticipó y mitigó riesgos técnicos del proyecto."),
        new("Te volviste cuello de botella (Senior)",
            "Concentraste tanto trabajo en ti que el equipo tenía que esperarte.", -5,
            "Cuello de botella (Senior)", "Senior que centralizó el trabajo volviéndose cuello de botella."),
        new("No asumiste el liderazgo (Senior)",
            "El equipo esperaba que marcaras el rumbo técnico y eso no ocurrió.", -6,
            "Falta de liderazgo (Senior)", "Senior que no asumió el liderazgo técnico esperado para su nivel."),
        new("Tu decisión técnica costó retrabajo (Senior)",
            "Una decisión que tomaste obligó a rehacer una parte importante.", -8,
            "Decisión técnica deficiente (Senior)", "Senior cuya decisión técnica generó retrabajo significativo."),
    ];

    /// <summary>
    /// Criterios de EQUIPO. Se quedan en tercera persona: describen al equipo entero, y el «tú» de
    /// los individuales aquí sonaría a que se le atribuye a una persona lo que hizo el grupo.
    /// </summary>
    public static readonly (string Nombre, string Descripcion, int Puntos)[] DeEquipo =
    [
        ("Objetivo de sprint cumplido (equipo)",   "El equipo cumplió el objetivo comprometido del sprint.",     +15),
        ("Cero incidentes en producción (equipo)", "El equipo no tuvo incidentes en producción en el período.",  +12),
        ("Calidad del equipo",                     "Bajo índice de retrabajo y bugs a nivel de equipo.",         +10),
        ("Colaboración entre equipos",             "El equipo colaboró efectivamente con otras áreas.",          +8),
        ("Velocidad estable (equipo)",             "El equipo mantuvo una velocidad de entrega estable.",        +8),
        ("Clima y colaboración",                   "Buen ambiente y colaboración dentro del equipo.",            +6),
        ("Documentación del equipo al día",        "El equipo mantuvo su documentación actualizada.",            +5),
        ("Objetivo de sprint incumplido (equipo)", "El equipo no cumplió el objetivo del sprint.",               -12),
        ("Incidente de producción (equipo)",       "El equipo causó un incidente en producción.",                -15),
        ("Carry over del equipo",                  "El equipo arrastró trabajo sin terminar del sprint.",        -8),
    ];

    /// <summary>
    /// Todos los nombres que este sembrado conoce, los de ahora y los de antes.
    ///
    /// <para>Sirve para distinguir <b>lo que trajo la aplicación</b> de <b>lo que creó el líder a
    /// mano</b>, que es una distinción que hace falta en un sitio concreto: la lista de criterios que
    /// el pool ofrece como extra. Allí se recorta la opinión del catálogo —de 42 opciones positivas,
    /// muchas repetidas entre sí y muchas que no hablan de UNA actividad, a una decena que sí— y ese
    /// recorte solo puede aplicarse a lo que vino sembrado. Un criterio que el líder inventó no está
    /// en esta lista, así que ninguna decisión tomada aquí lo esconde de la suya.</para>
    ///
    /// <para>Incluye los nombres ANTERIORES porque una base que todavía no pasó por
    /// <see cref="MigrarNomenclaturaAsync"/> —o donde el renombrado se saltó una fila por haber ya
    /// una homónima— sigue teniendo criterios con el nombre viejo, y son igual de sembrados.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> NombresSembrados =
        Individuales.Select(c => c.Nombre)
            .Concat(Individuales.Select(c => c.NombreAnterior))
            .Concat(DeEquipo.Select(e => e.Nombre))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Renombra los criterios que vienen de la versión anterior del catálogo. Va ANTES de
    /// <see cref="SembrarAsync"/>: si corriera después, el sembrado ya habría insertado la versión
    /// nueva y la base acabaría con el criterio duplicado, uno con cada nombre.
    ///
    /// La descripción solo se reemplaza si sigue siendo la que se sembró. Si el administrador la
    /// escribió a su manera, se respeta y únicamente se cambia el nombre: su texto es una decisión
    /// suya, no un valor por omisión que nos toque pisar.
    /// </summary>
    public static async Task<int> MigrarNomenclaturaAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Una sola lectura: son ~90 criterios y hacer una consulta por cada uno son ~90 viajes a
        // una base que se lee por red.
        var porNombre = await db.ScoringCriteria.ToDictionaryAsync(c => c.Name, StringComparer.Ordinal, ct);
        int cambiados = 0;

        foreach (var nuevo in Individuales)
        {
            if (!nuevo.VieneDeOtroNombre) continue;
            if (!porNombre.TryGetValue(nuevo.NombreAnterior, out var fila)) continue;

            // Ya existe una fila con el nombre nuevo (alguien la creó a mano, o una migración a
            // medias): renombrar crearía dos criterios homónimos. Se deja como está.
            if (porNombre.ContainsKey(nuevo.Nombre)) continue;

            fila.Name = nuevo.Nombre;
            if (string.Equals(fila.Description?.Trim(), nuevo.DescripcionAnterior, StringComparison.Ordinal))
                fila.Description = nuevo.Descripcion;

            porNombre[nuevo.Nombre] = fila;
            cambiados++;
        }

        if (cambiados > 0) await db.SaveChangesAsync(ct);
        return cambiados;
    }

    /// <summary>
    /// Alta idempotente por nombre: agrega solo lo que falte, de modo que las bases ya sembradas
    /// también reciban los criterios nuevos sin duplicar los que ya tienen.
    /// </summary>
    public static async Task SembrarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existentes = (await db.ScoringCriteria.Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        bool agregado = false;

        foreach (var c in Individuales)
        {
            if (!existentes.Add(c.Nombre)) continue;
            db.ScoringCriteria.Add(new ScoringCriterion
            {
                Name = c.Nombre, Description = c.Descripcion, DefaultPoints = c.Puntos,
                IsActive = true, Scope = CriterionScope.Individual, CreatedAt = DateTime.UtcNow
            });
            agregado = true;
        }

        foreach (var (nombre, descripcion, puntos) in DeEquipo)
        {
            if (!existentes.Add(nombre)) continue;
            db.ScoringCriteria.Add(new ScoringCriterion
            {
                Name = nombre, Description = descripcion, DefaultPoints = puntos,
                IsActive = true, Scope = CriterionScope.Equipo, CreatedAt = DateTime.UtcNow
            });
            agregado = true;
        }

        if (agregado) await db.SaveChangesAsync(ct);
    }
}
