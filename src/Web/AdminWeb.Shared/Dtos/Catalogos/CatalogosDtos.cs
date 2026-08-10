using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Catalogos;

/// <summary>
/// Una fila de la lista de desarrolladores.
///
/// Lo que NO lleva es tan importante como lo que lleva: el salario y lo demás de la ficha de
/// desarrollo viven en <c>DeveloperProfile</c> y tienen su propia pantalla con su propio contrato.
/// Aquí ni siquiera aparecen, así que no hay nada que esconder en el cliente — que es manipulable.
/// </summary>
/// <param name="Asignados">Cuántos requerimientos tiene asignados. La columna «Asignados» del escritorio.</param>
/// <param name="TieneAcceso">Si existe una cuenta de la aplicación ligada a esta ficha.</param>
public record DesarrolladorDto(
    int Id,
    string Nombre,
    string? Correo,
    string? Telefono,
    string? Seniority,
    DateTime? FechaIngreso,
    string? Direccion,
    string? SerieEquipo,
    int DiasVacaciones,
    int Asignados,
    bool TieneAcceso,
    bool Activo,
    string? Equipo,
    TeamRole RolEquipo,
    string RolEquipoTexto,
    string? Notas);

/// <summary>Un integrante dentro de la columna de su equipo, en el organigrama.</summary>
public record IntegranteDeEquipoDto(
    int Id,
    string Nombre,
    TeamRole Rol,
    string RolTexto,
    string ColorDeRol,
    bool EsLider);

/// <summary>
/// Un equipo con su gente. <paramref name="Sistemas"/> y <paramref name="Proyectos"/> son los
/// contadores 🖥/📁 que el escritorio pintaba bajo el nombre de la columna.
/// </summary>
public record EquipoDto(
    int Id,
    string Nombre,
    string? Descripcion,
    string? ColorHex,
    string? Lider,
    int Sistemas,
    int Proyectos,
    IReadOnlyList<IntegranteDeEquipoDto> Integrantes);

/// <summary>
/// El organigrama completo. «Sin equipo» es una columna más en el escritorio y aquí también: es
/// donde se ve de un vistazo a quién falta ubicar.
/// </summary>
public record OrganizacionDto(
    IReadOnlyList<EquipoDto> Equipos,
    IReadOnlyList<IntegranteDeEquipoDto> SinEquipo);

/// <summary>Un contacto de la empresa.</summary>
public record ContactoDto(
    int Id,
    string Nombre,
    string? Puesto,
    string? Empresa,
    string? Correo,
    string? Telefono,
    string? EnlaceTeams,
    string? Notas);

/// <summary>
/// Un programa del inventario.
///
/// La CLAVE de licencia no viaja, y esa es una decisión, no un olvido: el escritorio tampoco la
/// mostraba en la rejilla, y es un secreto que en una web queda a un «ver código fuente» de
/// distancia si se manda «por si acaso». Cuando haga falta consultarla (fase 3) irá por un endpoint
/// propio que deje constancia en la bitácora de quién la miró.
/// </summary>
/// <param name="Vence">Vencimiento de la licencia. Null = sin vencimiento.</param>
/// <param name="Vencida">Ya venció, o el estado es Expirado. Se calcula en el servidor para que la
/// rejilla no tenga que repetir la regla ni depender del reloj del navegador.</param>
/// <param name="PorVencer">Vence dentro de los próximos 30 días. Es el aviso ámbar del escritorio.</param>
public record ProgramaDto(
    int Id,
    string Nombre,
    SoftwareCategory Categoria,
    string CategoriaTexto,
    SoftwareStatus Estado,
    string EstadoTexto,
    SoftwareLicenseType Licencia,
    string LicenciaTexto,
    string? Version,
    string? Fabricante,
    DateTime? Vence,
    bool Vencida,
    bool PorVencer,
    string? InstaladoEn,
    string? Url,
    string? Notas);

/// <summary>Un recurso de Azure del inventario.</summary>
public record RecursoAzureDto(
    int Id,
    string Nombre,
    AzureResourceType Tipo,
    string TipoTexto,
    AzureResourceStatus Estado,
    string EstadoTexto,
    AzureEnvironment Ambiente,
    string AmbienteTexto,
    string? GrupoDeRecursos,
    string? Suscripcion,
    string? Region,
    decimal? CostoMensual,
    string? Url,
    string? Notas);

// ── Lo que se manda al GUARDAR ──────────────────────────────────────────────────
//
// Son records aparte de los de lectura, y no los mismos reutilizados, por dos razones que se ven
// mejor juntas: los de lectura llevan campos CALCULADOS en el servidor (Asignados, TieneAcceso,
// Vencida, los *Texto de cada enum) que un cliente no debe poder dictar, y llevan un Id que en un
// alta todavía no existe. Aceptar el DTO de lectura obligaría a ignorar la mitad de sus campos en
// silencio — justo el tipo de cosa que un día se deja de ignorar por accidente.

/// <summary>Alta o edición de un desarrollador. Con <c>Id</c> nulo es alta.</summary>
/// <param name="DiasVacaciones">Los días que le quedan. El botón de LFT de la pantalla solo
/// SUGIERE este número; el líder puede ajustarlo, igual que en el escritorio.</param>
public record GuardarDesarrolladorRequest(
    int? Id,
    string Nombre,
    string? Correo,
    string? Telefono,
    string? Seniority,
    DateTime? FechaIngreso,
    string? Direccion,
    string? SerieEquipo,
    int DiasVacaciones,
    bool Activo,
    string? Notas);

/// <summary>Alta o edición de un contacto. Con <c>Id</c> nulo es alta.</summary>
public record GuardarContactoRequest(
    int? Id,
    string Nombre,
    string? Puesto,
    string? Empresa,
    string? Correo,
    string? Telefono,
    string? EnlaceTeams,
    string? Notas);

/// <summary>
/// Alta o edición de un programa. Con <c>Id</c> nulo es alta.
///
/// <c>ClaveDeLicencia</c> es el único campo que viaja en un sentido y no en el otro: se puede
/// ESCRIBIR aquí pero no sale en <see cref="ProgramaDto"/>. Para verla hay un endpoint propio que
/// deja constancia de quién la miró. Y va como <c>string?</c> con significado de «no lo toques»
/// cuando es nulo: si fuera «bórralo», editar cualquier otro campo desde una pantalla que no
/// enseña la clave la borraría sin que nadie lo pidiera.
/// </summary>
public record GuardarProgramaRequest(
    int? Id,
    string Nombre,
    SoftwareCategory Categoria,
    SoftwareStatus Estado,
    SoftwareLicenseType Licencia,
    string? Version,
    string? Fabricante,
    string? ClaveDeLicencia,
    DateTime? Vence,
    string? InstaladoEn,
    string? Url,
    string? Notas);

/// <summary>Alta o edición de un recurso de Azure. Con <c>Id</c> nulo es alta.</summary>
public record GuardarRecursoAzureRequest(
    int? Id,
    string Nombre,
    AzureResourceType Tipo,
    AzureResourceStatus Estado,
    AzureEnvironment Ambiente,
    string? GrupoDeRecursos,
    string? Suscripcion,
    string? Region,
    decimal? CostoMensual,
    string? Url,
    string? Notas);

/// <summary>
/// El resultado de crear o restablecer la cuenta de acceso de un desarrollador.
///
/// La contraseña temporal viaja UNA sola vez, en esta respuesta, y no se guarda en claro en ningún
/// sitio: en la base queda su hash. Si quien la pidió cierra el diálogo sin copiarla, la salida es
/// restablecerla otra vez, no ir a buscarla.
/// </summary>
public record CredencialesDto(string Usuario, string ContrasenaTemporal, string Mensaje);

/// <summary>Lo que le corresponde a alguien por ley, para la nota junto al campo de vacaciones.</summary>
public record SugerenciaLftDto(int Dias, string Nota);

/// <summary>
/// La clave de licencia de un programa, servida de una en una y solo cuando alguien la pide.
/// Va en su propio tipo, y no como un <c>string</c> suelto, para que sea imposible añadirla por
/// descuido a <see cref="ProgramaDto"/> creyendo que se está añadiendo un campo más de la rejilla.
/// </summary>
public record ClaveDeLicenciaDto(string Clave);

