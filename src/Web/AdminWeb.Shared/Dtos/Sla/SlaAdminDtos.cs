using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Sla;

/// <summary>
/// Un compromiso de atención tal como lo ven las dos pantallas de SLA.
///
/// Todo lo que depende del reloj —si está fuera de plazo, si toca comentar— lo resuelve el SERVIDOR
/// y viaja ya decidido: son las mismas reglas que aplican el trabajo de fondo y los correos, y
/// recalcularlas en el navegador sería tener dos versiones de la misma verdad esperando a separarse.
///
/// Las horas viajan en UTC, que es como están guardadas. La pantalla las convierte a la hora local
/// de quien mira: el servidor puede estar en cualquier zona y su «hoy» no es el de nadie.
/// </summary>
/// <param name="Objetivo">Texto ya armado del requerimiento o la actividad. La pantalla no arma frases.</param>
/// <param name="TocaComentar">Su recordatorio ya venció: hay que dejar constancia en el ticket.</param>
public record SlaCompromisoDto(
    int Id,
    int DesarrolladorId,
    string Desarrollador,
    string Objetivo,
    int? TicketExternalId,
    string? TicketUrl,
    DateTime VenceUtc,
    SlaStatus Estado,
    bool FueraDePlazo,
    bool TocaComentar,
    DateTime? ProximoRecordatorioUtc,
    int RecordatorioCadaHoras,
    int Comentarios,
    DateTime? UltimoComentarioUtc,
    string? Notas);

/// <summary>
/// Las tarjetas de la pantalla del líder.
/// </summary>
/// <param name="VencenEn24h">
/// <b>Cambia respecto al escritorio, y a propósito.</b> Allí la tarjeta decía «vencen hoy» y se
/// calculaba contra la fecha local de la máquina de quien miraba. En la web eso lo tendría que
/// decidir el servidor, cuyo «hoy» depende de dónde esté hospedado: el mismo compromiso caería
/// dentro o fuera del recuento según la zona del servidor. «En las próximas 24 horas» no depende de
/// ninguna zona horaria y, para un plazo que se mide en horas, dice lo mismo que se quería saber.
/// </param>
public record SlaResumenDto(int Activos, int VencenEn24h, int FueraDePlazo);

/// <summary>
/// Una política de SLA automático por prioridad de Azure DevOps: cuántas horas hay para atender el
/// ticket y cada cuántas recordar que hay que comentarlo.
/// </summary>
/// <param name="Prioridad">1 (la más alta) a 4 (la más baja), como en DevOps.</param>
/// <param name="RecordatorioCadaHoras">0 = avisar solo al vencer.</param>
public record PoliticaSlaDto(int Prioridad, string Nombre, bool Activa, int Horas, int RecordatorioCadaHoras);

/// <summary>
/// Todo lo que pinta la pantalla «SLA y recordatorios» del líder, en una sola respuesta.
/// </summary>
/// <param name="Escalamiento">
/// A dónde va el escalamiento de los incumplimientos, para que se vea sin ir a Configuración. Vacío
/// significa que no se configuró y que caerá en los líderes.
/// </param>
public record SlaAdminDto(
    IReadOnlyList<SlaCompromisoDto> Compromisos,
    SlaResumenDto Resumen,
    IReadOnlyList<PoliticaSlaDto> Politicas,
    string Escalamiento,
    string Mensaje);

/// <summary>
/// Un objetivo al que se le puede colgar un SLA, ya con lo que el formulario debe preseleccionar.
/// </summary>
/// <param name="DesarrolladorSugerido">
/// Quien ya lo tiene asignado (o el dueño de la actividad). Es la sugerencia del escritorio y se
/// conserva: en la práctica el responsable del SLA es siempre esa persona.
/// </param>
public record ObjetivoDeSlaDto(
    int Id, string Texto, int? DesarrolladorSugerido, int? TicketExternalId, string? TicketUrl);

/// <summary>Lo que necesita el formulario de alta: a quién y sobre qué se puede comprometer.</summary>
public record OpcionesDeSlaDto(
    IReadOnlyList<OpcionDto> Desarrolladores,
    IReadOnlyList<ObjetivoDeSlaDto> Requerimientos,
    IReadOnlyList<ObjetivoDeSlaDto> Actividades);

/// <summary>
/// La pantalla del desarrollador.
/// </summary>
/// <param name="TieneFicha">
/// La cuenta está vinculada a una ficha de desarrollador. Sin ella no hay compromisos que enseñar, y
/// decirlo es mejor que una tabla vacía sin explicación.
/// </param>
/// <param name="Urgentes">Cuántos están fuera de plazo o piden ya un comentario en su ticket.</param>
public record MisSlaDto(
    bool TieneFicha,
    IReadOnlyList<SlaCompromisoDto> Compromisos,
    int Urgentes,
    string Mensaje);

/// <summary>
/// Alta de un compromiso.
/// </summary>
/// <param name="EsRequerimiento">Falso = el objetivo es una actividad libre. Nunca los dos.</param>
/// <param name="VenceUtc">
/// Ya convertido a UTC por la pantalla. El servidor no interpreta horas locales: su reloj no es el de
/// quien captura, y una diferencia de zona movería el vencimiento varias horas.
/// </param>
public record AsignarSlaRequest(
    bool EsRequerimiento,
    int ObjetivoId,
    int DesarrolladorId,
    DateTime VenceUtc,
    int RecordatorioCadaHoras,
    int? TicketExternalId,
    string? TicketUrl,
    string? Notas);

/// <summary>Aplaza el recordatorio sin mover la fecha límite.</summary>
public record PosponerSlaRequest(int Horas);

/// <summary>Nota opcional al cerrar un compromiso a mano.</summary>
public record NotaDeSlaRequest(string? Nota);

/// <summary>Las cuatro políticas, tal como quedaron en la pantalla.</summary>
public record GuardarPoliticasSlaRequest(IReadOnlyList<PoliticaSlaDto> Politicas);
