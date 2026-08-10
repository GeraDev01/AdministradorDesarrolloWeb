using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Notas;

// ═══ Notas y pendientes ══════════════════════════════════════════════════════════
//
// La fecha de recordatorio es un DÍA del calendario, no un instante: «acuérdame el jueves». Viaja
// tal cual y se pinta sin convertir a hora local, que es exactamente lo que hace el escritorio con
// esa misma columna. La fecha de alta sí es un instante —se guarda en UTC— y esa sí se convierte al
// pintarla, también igual que el escritorio.

/// <summary>
/// Una nota o pendiente tal como la ve la pantalla.
///
/// A diferencia de una minuta, aquí el contenido SÍ viaja con la lista: son un par de renglones de
/// contexto sobre un pendiente, la lista es la cola de trabajo de una sola persona y pedir el
/// detalle fila por fila costaría más viajes de los que ahorraría bytes. El servicio le pone tope
/// para que ese razonamiento siga siendo cierto.
/// </summary>
/// <param name="Contenido">Las «notas adicionales». Es texto escrito por una persona: se pinta como
/// TEXTO en todo el camino y no se interpreta como HTML en ningún punto.</param>
/// <param name="Desarrollador">Quién lo comentó, si se apuntó. Null es una nota propia del líder.</param>
/// <param name="PrioridadTexto">La etiqueta con su color, puesta por el servidor para que la
/// pantalla no lleve una segunda copia de la tabla de traducción.</param>
/// <param name="Atrasada">Pendiente y con el recordatorio ya pasado. Lo decide el SERVIDOR: si lo
/// calculara el navegador, «hoy» dependería del reloj de cada máquina y la misma nota saldría
/// atrasada en un equipo y al día en otro.</param>
public record NotaDto(
    int Id,
    string Titulo,
    string? Contenido,
    int? DesarrolladorId,
    string? Desarrollador,
    NotePriority Prioridad,
    string PrioridadTexto,
    DateTime? Recordatorio,
    bool Completada,
    bool Atrasada,
    DateTime Alta);

/// <summary>Una opción del desplegable de prioridades, con su texto puesto por el servidor.</summary>
public record PrioridadDeNotaDto(NotePriority Valor, string Texto);

/// <summary>
/// Todo lo que la pantalla necesita de una vez: la lista, las prioridades y a quién se le puede
/// atribuir una nota.
/// </summary>
/// <param name="Pendientes">Cuántas siguen abiertas EN TOTAL, se esté filtrando o no. Contarlas
/// sobre lo que se enseña haría que el propio filtro cambiara la cifra que sirve para decidir si
/// hay que mirarlo.</param>
/// <param name="Vencidas">De esas, cuántas tienen el recordatorio pasado.</param>
public record NotasDto(
    IReadOnlyList<NotaDto> Notas,
    IReadOnlyList<PrioridadDeNotaDto> Prioridades,
    IReadOnlyList<OpcionDto> Desarrolladores,
    int Pendientes,
    int Vencidas);

/// <summary>
/// Alta o edición de una nota. <c>Id</c> 0 es un alta.
/// </summary>
/// <param name="Recordatorio">Null = sin recordatorio, que es la casilla «Sin recordatorio» del
/// escritorio. Solo se conserva el día; la hora que traiga el selector se descarta.</param>
/// <param name="Completada">Se manda desde el formulario porque el escritorio también dejaba
/// marcarla y desmarcarla ahí. La acción rápida de la lista vive aparte.</param>
public record GuardarNotaRequest(
    int Id,
    string Titulo,
    string? Contenido,
    int? DesarrolladorId,
    NotePriority Prioridad,
    DateTime? Recordatorio,
    bool Completada);

/// <summary>
/// Cerrar o reabrir una nota sin pasar por el formulario.
///
/// Lleva el estado deseado y no es un «alternar»: dos pestañas abiertas sobre la misma lista
/// alternarían dos veces y la segunda desharía a la primera sin que nadie lo pidiera.
/// </summary>
public record CompletarNotaRequest(bool Completada);
