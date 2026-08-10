using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Bitacora;

/// <summary>
/// Una entrada de la bitácora.
/// </summary>
/// <param name="Fecha">En UTC, siempre. Es la única forma de que dos personas en husos distintos
/// lean la misma secuencia de hechos; quien la pinta la convierte a su hora local.</param>
/// <param name="Origen">De dónde salió. En el escritorio era «MÁQUINA\usuario» porque cada quien
/// corría su propio .exe; en la web todo sale del mismo servidor, así que lo que distingue al
/// cliente es su IP y su navegador.</param>
/// <param name="Correlacion">Agrupa las entradas de una misma operación larga (un despliegue
/// completo). Sirve para reconstruir el hilo cuando hay que explicar un incidente.</param>
public record EntradaBitacoraDto(
    int Id,
    DateTime Fecha,
    string Usuario,
    AuditAction Accion,
    string AccionTexto,
    string? Entidad,
    string? EntidadId,
    string? Detalles,
    AuditOutcome Resultado,
    string ResultadoTexto,
    string? Origen,
    string? Correlacion);

/// <summary>
/// Lo que la pantalla de la bitácora necesita para armar sus filtros: los usuarios que de verdad
/// aparecen en la tabla.
///
/// Se sirve aparte de la página de resultados y no dentro de ella: es una consulta cara
/// (DISTINCT sobre la tabla que más crece) y su respuesta no cambia entre una página y la
/// siguiente, así que pedirla en cada avance sería pagarla veinte veces por nada.
/// </summary>
public record OpcionesDeBitacoraDto(IReadOnlyList<string> Usuarios);
