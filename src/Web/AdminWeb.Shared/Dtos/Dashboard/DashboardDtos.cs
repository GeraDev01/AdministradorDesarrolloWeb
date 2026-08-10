using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Dashboard;

/// <summary>
/// Las seis tarjetas de conteo de la parte de arriba. Las cinco primeras son requerimientos por
/// estado; «Pendientes» son notas sin completar, que es otra cosa y por eso va aparte.
///
/// Los conteos vienen YA acotados a lo que le toca ver a quien pregunta: para un desarrollador
/// cuentan solo sus requerimientos y sus recordatorios. En el escritorio esto se arregló tarde —las
/// tarjetas contaban los de TODOS y un desarrollador leía «18 en desarrollo» sin que ninguno fuera
/// suyo—; aquí nace acotado desde el servidor.
/// </summary>
public record TarjetasDto(
    int PorEstimar,
    int EnDesarrollo,
    int PorEntregar,
    int Entregados,
    int Cancelados,
    int Pendientes);

/// <summary>
/// Un requerimiento cuya fecha comprometida cae dentro de los próximos 7 días (o ya pasó).
/// <paramref name="Atrasado"/> lo calcula el servidor: es la misma «hoy» con la que se filtró, y
/// dejárselo al navegador haría que un reloj mal puesto pintara de rojo lo que no lo está.
/// </summary>
public record EntregaProximaDto(
    int RequerimientoId,
    string Titulo,
    RequirementStatus Estado,
    string EstadoTexto,
    DateTime? FechaCompromiso,
    bool Atrasado);

/// <summary>Cuánto trabajo abierto tiene encima una persona. Dato de equipo: solo para el líder.</summary>
public record CargaDesarrolladorDto(
    int DesarrolladorId,
    string Nombre,
    int Activos,
    int PorEntregar);

/// <summary>Una nota sin completar. Dato de equipo: solo para el líder.</summary>
public record RecordatorioDto(
    int NotaId,
    string Titulo,
    NotePriority Prioridad,
    DateTime? FechaRecordatorio,
    bool Atrasado);

/// <summary>
/// Un puesto del podio del mes. La medalla la pone la pantalla a partir de <paramref name="Posicion"/>:
/// es adorno, no dato. Dato de equipo: solo para el líder.
/// </summary>
public record PuestoRankingDto(
    int Posicion,
    int DesarrolladorId,
    string Nombre,
    int Puntos,
    int Entradas);

/// <summary>
/// Todo el dashboard en una sola respuesta.
///
/// EL RECORTE POR ROL ESTÁ AQUÍ, en el contrato: a quien no es líder le llegan en null
/// <see cref="CargaPorDesarrollador"/>, <see cref="RecordatoriosPendientes"/> y
/// <see cref="TopRanking"/> — no es que la pantalla los esconda, es que no existen en su respuesta.
/// El escritorio ya lo tenía resuelto a su manera (ni construía esos grids, así que la consulta
/// nunca corría); en la web ese razonamiento se traslada al servidor porque el cliente es
/// manipulable y esconder un campo en el navegador no esconde nada.
/// </summary>
public record DashboardDto(
    TarjetasDto Tarjetas,
    IReadOnlyList<EntregaProximaDto> ProximasEntregas,
    IReadOnlyList<CargaDesarrolladorDto>? CargaPorDesarrollador,
    IReadOnlyList<RecordatorioDto>? RecordatoriosPendientes,
    IReadOnlyList<PuestoRankingDto>? TopRanking,
    string PeriodoRanking);
