using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Jornada;

/// <summary>
/// El estado de la jornada de quien tiene la sesión: lo que hace falta para pintar «Mi jornada» de
/// una vez, sin encadenar cuatro peticiones.
/// </summary>
/// <param name="EntradaUtc">Cuándo marcó su entrada, o null si aún no la ha marcado hoy.</param>
/// <param name="NotaEntrada">Lo que escribió al entrar.</param>
/// <param name="PuedeMarcarEntrada">Falso si ya hay una entrada abierta o ya cerró el día.</param>
/// <param name="Cronometro">El cronómetro en marcha, si lo hay.</param>
public record MiJornadaDto(
    int? RegistroId,
    DateTime? EntradaUtc,
    string? NotaEntrada,
    bool PuedeMarcarEntrada,
    bool CorreccionSolicitada,
    CronometroDto? Cronometro,
    IReadOnlyList<DiaDeJornadaDto> Historial);

/// <summary>
/// Un día ya registrado. Las horas viajan en UTC y las pinta el navegador en la hora de quien mira:
/// mandarlas ya formateadas obligaría al servidor a adivinar la zona del cliente.
/// </summary>
public record DiaDeJornadaDto(
    int Id,
    DateTime EntradaUtc,
    DateTime? SalidaUtc,
    string? NotaEntrada,
    string? NotaSalida,
    string Cierre,
    bool CorreccionSolicitada,
    string? NotaCorreccion,
    int SegundosCronometrados);

/// <summary>
/// El cronómetro en marcha. <paramref name="AhoraUtc"/> es la hora del SERVIDOR en el momento de
/// responder, y es lo que hace que el contador de la pantalla sea correcto: si el navegador contara
/// desde su propio reloj, un equipo desajustado enseñaría horas que nadie trabajó.
/// </summary>
public record CronometroDto(
    int? RequerimientoId,
    int? ActividadId,
    string Titulo,
    DateTime InicioTramoUtc,
    int SegundosAcumulados,
    DateTime AhoraUtc);

/// <summary>
/// Lo mínimo para pintar el botón de la barra superior: si toca marcar entrada, salida, o ya está
/// todo hecho.
///
/// Va aparte de <see cref="MiJornadaDto"/> porque ese botón está en TODAS las pantallas y se
/// refresca solo: pedirle el historial de treinta días cada vez sería traer un mes de datos para
/// decidir el texto de un botón.
/// </summary>
public record EstadoDeMarcajeDto(DateTime? EntradaUtc, bool PuedeMarcarEntrada)
{
    public bool DentroDeJornada => EntradaUtc != null;
}

/// <summary>Marcar entrada o salida. La hora la pone el servidor: aquí solo viaja la nota.</summary>
public record MarcajeRequest(string? Nota);

/// <summary>Pedir al líder que corrija un registro propio.</summary>
public record CorreccionRequest(string Motivo);

/// <summary>
/// Arrancar o parar el cronómetro. Va uno de los dos identificadores, nunca los dos: el trabajo se
/// cuenta contra un requerimiento o contra una actividad libre.
/// </summary>
public record CronometroRequest(int? RequerimientoId, int? ActividadId);

/// <summary>
/// Mi estado de presencia y su nota, para el botón de la barra superior.
///
/// <para>Existe porque el botón arrancaba de una variable local en «Disponible» y no preguntaba
/// nunca: tras recargar decía eso aunque en el tablero del líder pusiera «En reunión», y la nota
/// —«vuelvo 15:30»— desaparecía de la vista de quien la había escrito.</para>
/// </summary>
public record MiPresenciaDto(PresenceState Estado, string EstadoTexto, string? Nota);
