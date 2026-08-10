using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Programados;

// ── Agenda de despliegues ───────────────────────────────────────────────────────

/// <summary>
/// Una cita de la agenda, ya resuelta para pintarse.
///
/// <para>Las horas viajan en UTC y las convierte el navegador. En el escritorio se manejaban en hora
/// local porque quien agendaba y quien ejecutaba eran la misma máquina; aquí no: el servidor puede
/// estar en otra zona horaria que quien mira la pantalla, y mandar «las 18:00» sin decir de dónde
/// dispararía el despliegue a una hora que nadie pidió.</para>
/// </summary>
/// <param name="EstadoTexto">La etiqueta que escribió el servidor. Se enseña tal cual.</param>
/// <param name="TomadoPor">Qué instancia se llevó la ejecución. Sirve para explicar por qué una
/// cita no la ejecutó «la mía» el día que haya más de una.</param>
public record DespliegueProgramadoDto(
    int Id,
    DateTime CuandoUtc,
    string Sistema,
    string Version,
    string Perfil,
    ScheduledDeploymentStatus Estado,
    string EstadoTexto,
    int ToleranciaMinutos,
    string? TomadoPor,
    DateTime? TomadoUtc,
    string? Resultado,
    string? Notas,
    int? DespliegueId,
    string AgendadoPor,
    DateTime AgendadoUtc);

/// <summary>Una versión publicada, para elegirla al agendar.</summary>
public record VersionProgramableDto(int Id, string Sistema, string Version, DateTime CreadaUtc)
{
    public string Texto => $"{Sistema} — {Version}";
}

/// <summary>
/// Un perfil de despliegue elegible.
/// </summary>
/// <param name="PermitidoParaOperaciones">
/// Se manda para poder apagar en la lista lo que esa persona no va a poder agendar. Es comodidad:
/// quien decide de verdad es el servicio, que vuelve a comprobarlo y lo anota en la bitácora si
/// alguien lo intenta igualmente.
/// </param>
public record PerfilProgramableDto(int Id, string Nombre, string? Descripcion, bool PermitidoParaOperaciones, int Servidores);

/// <summary>Un servidor elegible para la selección directa.</summary>
public record ServidorProgramableDto(int Id, string Nombre, string Host, bool Activo);

/// <summary>Todo lo que necesita la pantalla de programados en una sola respuesta.</summary>
/// <param name="TrabajoDeFondoActivo">
/// Si el trabajo de fondo del servidor está encendido. Cuando NO lo está, la pantalla tiene que
/// decirlo: agendar algo que nadie va a ejecutar es peor que no poder agendarlo.
/// </param>
public record PantallaDeProgramadosDto(
    IReadOnlyList<DespliegueProgramadoDto> Citas,
    IReadOnlyList<VersionProgramableDto> Versiones,
    IReadOnlyList<PerfilProgramableDto> Perfiles,
    IReadOnlyList<ServidorProgramableDto> Servidores,
    bool TrabajoDeFondoActivo);

/// <summary>
/// Petición para agendar un despliegue.
///
/// Un solo tipo para las dos formas de agendar —por perfil o por servidores sueltos— porque son la
/// misma operación con distinto destino: separarlas en dos peticiones obligaba a repetir la fecha,
/// la tolerancia y las notas en ambas y a mantenerlas iguales.
/// </summary>
/// <param name="PerfilId">Perfil elegido, o null si se eligieron servidores directos.</param>
/// <param name="ServidorIds">Servidores elegidos, o vacío si se eligió un perfil.</param>
public record ProgramarDespliegueRequest(
    int VersionId,
    int? PerfilId,
    IReadOnlyList<int> ServidorIds,
    DateTime CuandoUtc,
    int ToleranciaMinutos,
    string? Notas);

// ── Almacenamiento (Azure Blob) ─────────────────────────────────────────────────

/// <summary>Un metadato de un blob, en la forma en que se edita: pares clave/valor.</summary>
public record MetadatoDeBlobDto(string Clave, string Valor);

/// <summary>
/// Un archivo del contenedor.
///
/// <para><b>Aquí no viaja ninguna credencial.</b> Ni la cadena de conexión, ni la URL absoluta del
/// blob: el nombre relativo basta para todo lo que hace la pantalla, y una URL con la cuenta dentro
/// solo serviría para invitar a probar accesos.</para>
/// </summary>
public record ArchivoDeBlobDto(
    string Nombre,
    string NombreCorto,
    long Bytes,
    string TamanoLegible,
    DateTimeOffset? ModificadoUtc,
    IReadOnlyList<MetadatoDeBlobDto> Metadatos);

/// <summary>El contenido de una carpeta del contenedor, con su resumen ya redactado.</summary>
public record ListadoDeBlobsDto(
    string Carpeta,
    IReadOnlyList<ArchivoDeBlobDto> Archivos,
    long BytesTotales,
    string Resumen);

/// <summary>
/// El estado del almacenamiento y las carpetas que se ofrecen.
/// </summary>
/// <param name="Configurado">
/// Si hay cadena de conexión guardada. Es lo ÚNICO que se dice de ella: nunca su valor.
/// </param>
/// <param name="Aviso">Explicación para la pantalla cuando algo no se pudo leer. Se enseña tal cual.</param>
public record AlmacenamientoDto(
    bool Configurado,
    string Contenedor,
    IReadOnlyList<string> Carpetas,
    string CarpetaDeVersiones,
    string CarpetaDeRespaldosDeDespliegue,
    string? Aviso);

/// <summary>Alta de una carpeta en el contenedor.</summary>
public record CrearCarpetaEnBlobRequest(string Ruta);

/// <summary>
/// Reemplazo COMPLETO de los metadatos de un blob. Azure no hace mezcla: lo que se manda es lo que
/// queda, así que la pantalla envía siempre el conjunto entero.
/// </summary>
public record GuardarMetadatosRequest(string Blob, IReadOnlyList<MetadatoDeBlobDto> Metadatos);

/// <summary>Petición de un enlace temporal de descarga.</summary>
public record EnlaceDeDescargaRequest(string Blob, double Horas);

/// <summary>
/// El enlace temporal ya firmado.
///
/// <para>Es la única cosa parecida a una credencial que sale hacia el navegador, y sale a propósito:
/// es justo la función —dar acceso a UN archivo, de solo lectura y con caducidad, a alguien que no
/// entra a la aplicación—. La cadena de conexión, que abre el contenedor entero y no caduca, no sale
/// nunca. Cada enlace queda en la bitácora con quién lo pidió, para qué archivo y hasta cuándo
/// sirve.</para>
/// </summary>
public record EnlaceDeDescargaDto(string Url, DateTime CaducaUtc);

/// <summary>Nombre del blob sobre el que actúa una operación de un solo archivo.</summary>
public record BlobRequest(string Blob);

/// <summary>Carpeta sobre la que actúa una operación de carpeta.</summary>
public record CarpetaRequest(string Carpeta);

/// <summary>
/// Qué se llevaría por delante borrar una carpeta, para poder decirlo ANTES de preguntar.
/// </summary>
/// <param name="EsCarpetaBase">
/// La carpeta es (o contiene) una a donde escribe la aplicación: versiones o respaldos previos. El
/// aviso extra del escritorio se conserva porque borrarla se lleva todo el histórico.
/// </param>
public record AlcanceDeBorradoDto(string Carpeta, int Elementos, bool EsCarpetaBase);

// ── Estado de los servidores ────────────────────────────────────────────────────

/// <summary>Qué versión tiene hoy un servidor, quién se la puso y cuándo.</summary>
/// <param name="Atrasado">Tiene versión y NO es la última publicada de su sistema.</param>
/// <param name="Antiguedad">«hace 3 d», redactado por el servidor para que diga lo mismo en todas partes.</param>
public record EstadoDeServidorDto(
    int ServidorId,
    string Servidor,
    string Host,
    string? Url,
    bool Activo,
    string? Sistema,
    string? Version,
    string? UltimaVersionDelSistema,
    DateTime? DesplegadoUtc,
    string? Quien,
    int? DespliegueId,
    bool NuncaDesplegado,
    bool Atrasado,
    string Antiguedad,
    string EstadoTexto);

/// <summary>La foto completa con su resumen, tal como lo escribe el servidor.</summary>
public record EstadoDeServidoresDto(
    IReadOnlyList<EstadoDeServidorDto> Servidores,
    int AlDia,
    int Atrasados,
    int SinDesplegar,
    string Resumen);
