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
/// Cómo se lee y cómo se pinta el estado de un compromiso de SLA.
///
/// Van las DOS juntas y aquí, pegadas al DTO, porque ninguna de las dos sale del enum a secas: el
/// estado que ve quien mira es <see cref="SlaCompromisoDto.Estado"/> MÁS las dos banderas que el
/// servidor ya calculó contra el reloj. Separarlas es lo que pasó antes: cada pantalla de SLA se
/// escribió su copia, y las copias YA DIVERGIERON —«Mis SLA» pintaba «Fuera de plazo» en rojo y
/// conocía «Toca comentar»; «SLA y recordatorios» lo pintaba en ámbar y no conocía el otro—, o sea
/// que el mismo compromiso se veía más grave o menos según por qué puerta se entrara.
///
/// <para>La versión que queda es la de «Mis SLA», que es la que distingue las dos cosas. El
/// incumplimiento va al ROJO: ya se pasó el plazo y eso no es un aviso, es un hecho. «Toca comentar»
/// va al ámbar porque el plazo sigue vivo y lo que falta es dejar constancia en el ticket.</para>
///
/// <para>La palabra y el color se tocan A LA VEZ o dejan de decir lo mismo. Por eso están en la misma
/// clase y no cada una por su lado; el resto de colores de estado viven en <c>ColoresDeEstado</c>, y
/// allí hay una nota que apunta hacia aquí.</para>
///
/// <para>El color devuelve la cadena «var(--…)» literal, no un color: se interpola dentro de un
/// atributo <c>style</c> y lo resuelve el NAVEGADOR, que es lo que hace que se lea bien en los dos
/// temas. Ojo con usarlo fuera de una pantalla —en un correo o un PDF <c>var()</c> no resuelve y el
/// texto sale sin color, sin ningún error que lo delate—.</para>
///
/// <para>Que «En plazo» y «Cumplido» compartan el verde es correcto y deliberado: el color agrupa
/// («esto va bien») y la palabra identifica. La palabra va SIEMPRE al lado del punto.</para>
/// </summary>
public static class EtiquetasDeSla
{
    /// <summary>
    /// Un activo pasado de fecha se lee como «fuera de plazo» aunque su estado siga siendo Activo: se
    /// mide el plazo, no la etiqueta. Es la misma regla que aplica el reporte de cumplimiento.
    /// </summary>
    public static string EtiquetaDeSla(SlaCompromisoDto s) => s.Estado switch
    {
        SlaStatus.Activo   => s.FueraDePlazo ? "Fuera de plazo"
                            : s.TocaComentar ? "Toca comentar" : "En plazo",
        SlaStatus.Cumplido => "Cumplido",
        SlaStatus.Vencido  => "Vencido",
        _                  => "Cancelado"
    };

    /// <summary>El color del punto que acompaña a <see cref="EtiquetaDeSla"/>, rama por rama.</summary>
    public static string ColorDeSla(SlaCompromisoDto s) => s.Estado switch
    {
        SlaStatus.Activo   => s.FueraDePlazo ? "var(--rz-danger)"
                            : s.TocaComentar ? "var(--rz-warning)" : "var(--rz-success)",
        SlaStatus.Cumplido => "var(--rz-success)",
        SlaStatus.Vencido  => "var(--rz-danger)",
        _                  => "var(--rz-text-secondary-color)"   // Cancelado
    };
}

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
