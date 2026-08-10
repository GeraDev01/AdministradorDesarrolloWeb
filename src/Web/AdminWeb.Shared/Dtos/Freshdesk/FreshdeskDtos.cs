namespace AdminWeb.Shared.Dtos.Freshdesk;

// ── Pantalla de Freshdesk ────────────────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña la pantalla de Freshdesk de una vez: si la integración está en pie, con qué
/// filtro se sincroniza, el resumen por estado y los tickets que hay guardados aquí.
///
/// <para><b>La clave de API no aparece por ninguna parte y nunca puede aparecer.</b> Lo que se puede
/// saber desde el navegador es si la integración está utilizable (<paramref name="Habilitada"/>) y,
/// si no lo está, qué falta —en palabras, no con el valor—. Una respuesta de la API que llevara la
/// clave dejaría de estar protegida en cuanto alguien abre la consola del navegador.</para>
/// </summary>
/// <param name="MotivoDeNoDisponible">Por qué no se puede sincronizar todavía (apagada, sin dominio,
/// sin clave). Nulo cuando sí se puede. Se enseña tal cual: decir «no se pudo» sin más obligaría a
/// abrir Configuración a adivinar qué falta.</param>
/// <param name="UltimaSincronizacionUtc">Cuándo se trajo lo más reciente. Nulo si nunca se ha
/// sincronizado; es el dato que dice si lo que se está mirando es de hoy o de hace un mes.</param>
/// <param name="Coincidencias">Cuántos tickets cumplen la búsqueda, ANTES del recorte. Es lo que
/// permite decir «300 de 812» sin mentir.</param>
/// <param name="Truncada">La lista se recortó porque la búsqueda dejaba demasiadas filas. Se dice en
/// pantalla: recortar en silencio esconde justo lo que se venía a buscar.</param>
public record PantallaDeFreshdeskDto(
    bool Habilitada,
    string? MotivoDeNoDisponible,
    FiltroDeSincronizacionDto Filtro,
    ResumenDeFreshdeskDto Resumen,
    DateTime? UltimaSincronizacionUtc,
    int Coincidencias,
    bool Truncada,
    IReadOnlyList<TicketDeFreshdeskDto> Tickets);

/// <summary>
/// Con qué criterio se traen los tickets: los asignados a mi agente y/o los de un grupo.
///
/// El grupo se guarda como lo escribió el líder —nombre o ID numérico— porque las dos formas son
/// legítimas: el nombre solo se puede resolver si la clave de API puede listar grupos, y con una
/// clave de agente eso responde 403. Escribir el ID es la salida cuando eso pasa.
/// </summary>
public record FiltroDeSincronizacionDto(bool PorAgente, string Grupo)
{
    /// <summary>El filtro está activo si al menos uno de los dos criterios está puesto.</summary>
    public bool Activo => PorAgente || !string.IsNullOrWhiteSpace(Grupo);
}

/// <summary>Las tarjetas de arriba de la pantalla: cuántos tickets hay de cada estado.</summary>
public record ResumenDeFreshdeskDto(
    int Total, int Abiertos, int Pendientes, int Resueltos, int Cerrados, int Urgentes);

/// <summary>
/// Un ticket de Freshdesk tal como se guardó aquí.
///
/// <para>Los estados y las prioridades viajan CRUDOS y con su etiqueta al lado. El número lo pide el
/// filtro de la rejilla (que compara enteros, no textos) y la etiqueta la pinta la pantalla sin tener
/// que repetir en el cliente la tabla de equivalencias que ya vive en el servidor.</para>
///
/// <para><b>El asunto y la descripción los escribe gente de fuera del equipo</b> —quien abre el
/// ticket—, así que se pintan siempre como TEXTO. Pasarlos por <c>MarkupString</c> convertiría
/// cualquier ticket en la vía de entrada de un XSS, y es el caso más claro que hay en esta
/// aplicación.</para>
/// </summary>
/// <param name="Numero">El número del ticket en Freshdesk (el que sale en su URL), no la llave de
/// esta base. Es lo que la gente dice en voz alta: «el 4821».</param>
/// <param name="Vinculos">Cuántos work items de DevOps tiene enlazados. Se cuenta en el servidor de
/// una sola consulta: preguntarlo por fila sería un viaje de red por ticket.</param>
public record TicketDeFreshdeskDto(
    int Id,
    long Numero,
    string Asunto,
    int Estado,
    string EstadoTexto,
    int Prioridad,
    string PrioridadTexto,
    string? Tipo,
    string FuenteTexto,
    string Agente,
    string Grupo,
    string Solicitante,
    string CorreoDelSolicitante,
    string Etiquetas,
    string? Descripcion,
    DateTime? CreadoUtc,
    DateTime? ActualizadoUtc,
    DateTime SincronizadoUtc,
    string Url,
    int Vinculos);

/// <summary>
/// Cómo salió una sincronización.
/// </summary>
/// <param name="Quitados">Tickets que se retiraron de la base porque se comprobó que YA NO cumplen
/// el filtro. Nunca son tickets «que no vinieron»: la API solo devuelve los del último mes, y borrar
/// por ausencia se llevaría el histórico por delante.</param>
/// <param name="Aviso">El filtro no se pudo aplicar del todo (no se identificó el agente, no se
/// resolvió el grupo). No es un error —la sincronización sí ocurrió— pero cambia lo que se está
/// viendo, así que se enseña.</param>
public record ResultadoDeSincronizacionDto(
    int Nuevos, int Actualizados, int Quitados, string Mensaje, string? Aviso);

/// <summary>Guarda el filtro de sincronización. <c>Grupo</c> vacío significa «sin grupo».</summary>
public record GuardarFiltroRequest(bool PorAgente, string? Grupo);

// ── Pantalla de vínculos ─────────────────────────────────────────────────────────

/// <summary>
/// La pantalla de vínculos completa: lo ya enlazado y las dos listas desde las que se enlaza.
///
/// Las tres cosas viajan juntas porque las tres se miran a la vez —se decide qué enlazar comparando
/// una lista con la otra, y el resumen dice cuánto queda por hacer—; partirlo obligaría al navegador
/// a encadenar tres peticiones para pintar una sola pantalla.
/// </summary>
public record PantallaDeVinculosDto(
    ResumenDeVinculosDto Resumen,
    IReadOnlyList<VinculoDto> Vinculos,
    ListaDeWorkItemsDto DevOps,
    ListaDeTicketsDto Freshdesk);

/// <summary>Cuánto hay de cada cosa y cuánto está ya enlazado, para las tarjetas de arriba.</summary>
public record ResumenDeVinculosDto(
    int WorkItems, int Tickets, int Vinculos, int WorkItemsVinculados, int TicketsVinculados);

/// <summary>
/// Un vínculo existente, con los datos de los dos extremos ya resueltos.
///
/// Van los dos títulos y no solo los identificadores: la lista se lee para reconocer de qué trataba
/// el enlace, y una tabla de números obligaría a abrir los dos sistemas para saberlo.
/// </summary>
public record VinculoDto(
    int Id,
    int NumeroDevOps,
    string TipoDevOps,
    string TituloDevOps,
    string EstadoDevOps,
    string? UrlDevOps,
    long NumeroFreshdesk,
    string AsuntoFreshdesk,
    string EstadoFreshdesk,
    string AgenteFreshdesk,
    string? UrlFreshdesk,
    DateTime VinculadoUtc,
    string? Por,
    string? Notas);

/// <summary>
/// El panel de work items del vinculador: lo que cumple el filtro, cómo se resume y con qué valores
/// se pueden llenar sus desplegables.
///
/// <para>Las OPCIONES las manda el servidor y salen de los datos que REALMENTE hay. Estado, tipo y
/// asignado dependen de la plantilla de proceso del proyecto de DevOps, así que escribirlos en la
/// pantalla sería ofrecer callejones sin salida el día que el proyecto cambie.</para>
/// </summary>
/// <param name="Truncada">La lista se recortó porque el filtro dejaba demasiadas filas. Se dice en
/// pantalla: una lista recortada en silencio esconde justo lo que se venía a buscar.</param>
public record ListaDeWorkItemsDto(
    IReadOnlyList<WorkItemParaVincularDto> Filas,
    string Resumen,
    bool Truncada,
    IReadOnlyList<string> Estados,
    IReadOnlyList<string> Tipos,
    IReadOnlyList<string> Asignados);

/// <summary>
/// El panel de tickets del vinculador.
///
/// Estado y prioridad son un dominio CERRADO en Freshdesk, pero las opciones se arman igual con lo
/// que hay en los datos, para no ofrecer «Resuelto» cuando no hay ninguno resuelto.
/// </summary>
public record ListaDeTicketsDto(
    IReadOnlyList<TicketParaVincularDto> Filas,
    string Resumen,
    bool Truncada,
    IReadOnlyList<string> Estados,
    IReadOnlyList<string> Prioridades,
    IReadOnlyList<string> Agentes);

/// <summary>Un work item de DevOps en la lista desde la que se enlaza.</summary>
/// <param name="Vinculado">Ya tiene al menos un vínculo. Se enseña —no se esconde— porque un work
/// item puede tener varios tickets detrás; quien quiera solo lo pendiente marca la casilla.</param>
public record WorkItemParaVincularDto(
    int Id, int Numero, string Tipo, string Titulo, string Estado, string Asignado,
    bool Vinculado, string? Url);

/// <summary>Un ticket de Freshdesk en la lista desde la que se enlaza.</summary>
public record TicketParaVincularDto(
    int Id, long Numero, string Asunto, string Estado, string Prioridad, string Agente,
    bool Vinculado, string? Url);

/// <summary>
/// Crea un vínculo entre un work item y un ticket. Los identificadores son los de ESTA base (los que
/// vienen en las dos listas), no los externos: son los que el servidor puede comprobar sin salir a
/// preguntar a nadie.
/// </summary>
public record CrearVinculoRequest(int WorkItemId, int TicketId, string? Notas);
