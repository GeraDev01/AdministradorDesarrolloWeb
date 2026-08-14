using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Despliegues;

// ─────────────────────────────────────────────────────────────────────────────────
//  Lo que la pantalla de despliegues intercambia con la API.
//
//  UNA REGLA MANDA SOBRE TODAS LAS DEMÁS EN ESTE ARCHIVO: aquí no hay ni puede haber una
//  contraseña de servidor. En el escritorio el valor no salía del proceso, así que la caja de
//  contraseña podía enseñarse rellena; en la web, cualquier campo de un DTO se lee abriendo la
//  consola del navegador. Por eso <see cref="ServidorDto"/> no la lleva, ni siquiera cifrada, y por
//  eso al editar un servidor la contraseña vacía significa «déjala como está» en vez de «bórrala».
// ─────────────────────────────────────────────────────────────────────────────────

// ── Sistemas y versiones ─────────────────────────────────────────────────────────

/// <summary>Un sistema o aplicativo que se despliega.</summary>
public record SistemaDto(
    int Id,
    string Nombre,
    string? Descripcion,
    string? CarpetaPorOmision,
    bool Activo,
    int Versiones,
    // QUIÉN LO LLEVA. Sale del propio sistema, con el nombre ya resuelto para que la pantalla no
    // tenga que cruzar identificadores contra otra lista suya: son dos consultas y podrían llegar
    // desfasadas. Nulo es «sin equipo asignado», que hoy es la mayoría.
    int? EquipoId = null,
    string? Equipo = null);

/// <summary>
/// Una versión publicable de un sistema.
/// </summary>
/// <param name="EtiquetaBloqueada">
/// La versión ya se desplegó o está programada, así que su etiqueta es su identificador en el
/// historial: renombrarla reescribiría lo que dicen esos registros. Decisión del escritorio que se
/// conserva; la carpeta y el changelog sí se pueden corregir.
/// </param>
/// <param name="PaqueteDisponible">
/// El servidor puede leer hoy el paquete de esta versión. Importa porque quien despliega ya no es la
/// máquina que creó la versión: si el archivo no está donde el servidor puede alcanzarlo, más vale
/// saberlo antes de marcar el checklist que a mitad del despliegue.
/// </param>
public record VersionDto(
    int Id,
    int SistemaId,
    string Sistema,
    string Version,
    string? Changelog,
    string? CarpetaDestino,
    long TamanoBytes,
    string? Checksum,
    DateTime CreadaUtc,
    bool EtiquetaBloqueada,
    bool PaqueteDisponible);

/// <summary>Un archivo .zip que está en la carpeta de despliegue del servidor y todavía no es una versión.</summary>
public record PaqueteDisponibleDto(string Nombre, long TamanoBytes, DateTime ModificadoUtc);

// ── Servidores ───────────────────────────────────────────────────────────────────

/// <summary>
/// Un servidor de destino, tal como lo ve el navegador. <b>Sin contraseña, ni cifrada.</b>
/// </summary>
/// <param name="UltimoJobId">
/// Despliegue del que salió la versión que tiene hoy. Es el puente al historial: con esto se llega
/// al log completo y a la evidencia sin reconstruirla leyendo la bitácora entera.
/// </param>
public record ServidorDto(
    int Id,
    string Nombre,
    string Host,
    int Puerto,
    string Usuario,
    string RutaRemota,
    string? Url,
    bool Activo,
    bool TieneContrasena,
    DateTime? UltimoDespliegueUtc,
    string? VersionDesplegada,
    string? QuienDesplego,
    int? UltimoJobId);

/// <summary>
/// Alta o corrección de un servidor.
/// </summary>
/// <param name="Contrasena">
/// Vacía significa <b>conservar la que ya tiene</b>, no borrarla: como la contraseña nunca viaja de
/// vuelta al navegador, el formulario no puede reenviarla y sin esta regla cada corrección de la
/// ruta remota dejaría al servidor sin credenciales. En un alta sí es obligatoria.
/// </param>
public record GuardarServidorRequest(
    string Nombre,
    string Host,
    int Puerto,
    string Usuario,
    string? Contrasena,
    string RutaRemota,
    string? Url);

// ── Perfiles ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Un perfil de despliegue: un destino con nombre.
/// </summary>
/// <param name="TieneHistorial">
/// El perfil ya aparece en despliegues pasados y por eso no se puede eliminar: hacerlo dejaría el
/// historial apuntando a un identificador inexistente.
/// </param>
public record PerfilDto(
    int Id,
    string Nombre,
    string? Descripcion,
    bool PermitidoParaOperaciones,
    IReadOnlyList<int> ServidorIds,
    string Servidores,
    bool TieneHistorial);

public record GuardarPerfilRequest(
    string Nombre,
    string? Descripcion,
    bool PermitidoParaOperaciones,
    IReadOnlyList<int> ServidorIds);

// ── Checklist previo ─────────────────────────────────────────────────────────────

/// <summary>Un punto del checklist previo al despliegue.</summary>
/// <param name="Ayuda">Por qué importa. Un checklist que no explica se marca sin leer.</param>
public record PuntoDeChecklistDto(string Clave, string Texto, string Ayuda);

/// <summary>
/// Lo que la pantalla necesita saber al abrirse: qué puede hacer quien mira y con qué reglas.
/// </summary>
/// <param name="RespaldoPrevioActivo">
/// El interruptor de configuración. Cuando está apagado se despliega sin red de seguridad, y el
/// checklist lo dice en voz alta en vez de dejarlo escondido en otra pantalla.
/// </param>
/// <param name="CarpetaDeDespliegueConfigurada">
/// Si el servidor tiene carpeta de despliegue. Sin ella no se pueden registrar versiones nuevas ni
/// guardar respaldos, y es mejor decirlo al entrar que al fallar.
/// </param>
public record OpcionesDeDesplieguesDto(
    IReadOnlyList<PuntoDeChecklistDto> Checklist,
    int MinimoNota,
    bool RespaldoPrevioActivo,
    bool CarpetaDeDespligueConfigurada,
    bool PuedeAdministrarCatalogo,
    bool PuedeCrearServidores,
    bool PuedeEditarServidores);

// ── Lanzar un despliegue ─────────────────────────────────────────────────────────

/// <summary>
/// La orden de desplegar.
/// </summary>
/// <param name="ServidorIds">
/// Selección DIRECTA de servidores, estilo Blobup. Es la vía normal para Admin y Operaciones.
/// </param>
/// <param name="PerfilId">
/// Alternativa a la selección directa: desplegar un perfil guardado tal cual. Si vienen los dos,
/// manda la selección directa.
/// </param>
/// <param name="RespaldarServidorIds">
/// Cuáles de los elegidos se respaldan antes de sobrescribir. <c>null</c> significa «todos», que es
/// el valor seguro: quien no se pronuncia obtiene la red de seguridad, no su ausencia.
/// </param>
/// <param name="Marcados">Las claves del checklist que se confirmaron.</param>
/// <param name="Nota">La justificación escrita. Obligatoria; el servidor la vuelve a exigir.</param>
public record LanzarDespliegueRequest(
    int VersionId,
    IReadOnlyList<int> ServidorIds,
    int? PerfilId,
    IReadOnlyList<int>? RespaldarServidorIds,
    IReadOnlyList<string> Marcados,
    string? Nota);

/// <summary>Lo que responde el lanzamiento: el trabajo que se acaba de poner en marcha.</summary>
public record DespliegueLanzadoDto(int JobId, string Mensaje);

// ── Seguimiento en vivo ──────────────────────────────────────────────────────────

/// <summary>
/// Avance del despliegue: porcentaje global, servidor actual y qué está haciendo. Es el
/// <c>DeployStatus</c> del escritorio, viajando ahora por el canal en vivo en vez de por un
/// <c>IProgress</c> dentro del mismo proceso.
/// </summary>
public record AvanceDeDespliegueDto(int JobId, int Porcentaje, string Servidor, string Detalle);

/// <summary>Un renglón de la bitácora del despliegue.</summary>
public record RenglonDeBitacoraDto(DateTime Utc, string? Servidor, string Mensaje, DeployLogLevel Nivel);

/// <summary>Cómo terminó el despliegue.</summary>
public record FinDeDespliegueDto(
    int JobId,
    JobStatus Estado,
    string EstadoTexto,
    int Ok,
    int Fallidos,
    int SinIntentar,
    string Resumen);

/// <summary>
/// La foto completa de un despliegue en curso, para <b>retomar la vista</b>.
///
/// Es la mejora que distingue a la web del escritorio: allí, cerrar la aplicación a media subida
/// mataba el despliegue. Aquí el despliegue sigue en el servidor y esta foto —avance, bitácora
/// acumulada y, si ya acabó, el resultado— es lo que permite volver a abrir la pestaña y encontrarse
/// donde iba, en lugar de ante una consola vacía.
/// </summary>
public record TrabajoDeDespliegueDto(
    int JobId,
    string Sistema,
    string Version,
    string Destino,
    string QuienLoLanzo,
    DateTime InicioUtc,
    bool Mio,
    AvanceDeDespliegueDto Avance,
    IReadOnlyList<RenglonDeBitacoraDto> Bitacora,
    FinDeDespliegueDto? Fin);

// ── Historial ────────────────────────────────────────────────────────────────────

/// <summary>
/// Un despliegue del historial.
/// </summary>
/// <param name="TieneChecklist">
/// Los despliegues anteriores a que el checklist fuera obligatorio no tienen evidencia. No se
/// disimula: la ausencia es un dato, y decirlo vale más que un hueco que parece un descuido.
/// </param>
public record DespliegueDelHistorialDto(
    int JobId,
    string Sistema,
    string Version,
    string Destino,
    JobStatus Estado,
    string EstadoTexto,
    int Ok,
    int Fallidos,
    int Total,
    DateTime? InicioUtc,
    DateTime? FinUtc,
    double? DuracionSegundos,
    string QuienLoLanzo,
    bool TieneChecklist);

/// <summary>El expediente de un despliegue: sus datos, el checklist confirmado y el log completo.</summary>
public record ExpedienteDeDespliegueDto(
    DespliegueDelHistorialDto Cabecera,
    string? Checklist,
    IReadOnlyList<RenglonDeBitacoraDto> Bitacora);

// ── Catálogo ─────────────────────────────────────────────────────────────────────

/// <summary>
/// El alta y la edición de un sistema.
/// </summary>
/// <param name="EquipoId">Qué equipo se encarga de él, o nulo para dejarlo sin asignar.
///
/// <para><b>Esto no se podía escribir desde la aplicación.</b> La columna existía y el organigrama la
/// leía —de ahí el «2 sistema(s)» de cada caja—, pero ni el alta ni la edición la tocaban: lo que hay
/// en producción lo escribió el ejecutable de escritorio, que ya no se usa. O sea que el dato estaba
/// a la vista y se iba quedando viejo sin que nadie pudiera corregirlo.</para>
///
/// <para>Un sistema lo lleva UN equipo. No hay ni un dato ni una petición que pida lo contrario, y
/// una tabla intermedia cambiaría a la vez el organigrama, el catálogo, la limpieza de datos y el
/// PDF. Asignar es quitárselo a quien lo tuviera: la pantalla lo dice antes de hacerlo.</para></param>
public record GuardarSistemaRequest(
    string Nombre, string? Descripcion, string? CarpetaPorOmision, int? EquipoId = null);

/// <summary>Registra como versión un paquete que ya está en la carpeta de despliegue del servidor.</summary>
public record CrearVersionRequest(int SistemaId, string Version, string? Changelog, string Paquete);

/// <summary>
/// Registrar una versión a partir de un paquete que ya está en el ALMACÉN, que es de donde salen los
/// artefactos de la compilación automática.
/// </summary>
/// <param name="Blob">
/// El nombre del blob dentro de la carpeta de versiones. Se comprueba contra lo que hay en el
/// almacén, nunca se concatena a una ruta.
/// </param>
public record CrearVersionDesdeAlmacenRequest(int SistemaId, string Version, string? Changelog, string Blob);

public record EditarVersionRequest(string Version, string? Changelog, string? CarpetaDestino);

/// <summary>Un motivo escrito. Se pide donde la operación afecta a otros.</summary>
public record MotivoDespliegueRequest(string? Motivo);

/// <summary>
/// Un servidor tal como viene en el JSON del alta masiva.
///
/// <para>Los nombres coinciden con los del escritorio a propósito: el archivo que el área ya tiene
/// preparado tiene que servir tal cual, sin reescribirlo. La lectura es indiferente a mayúsculas,
/// así que «Nombre» y «nombre» valen igual.</para>
///
/// <para>La contraseña llega EN CLARO en el archivo —no hay otra forma de importarla— y se cifra en
/// cuanto entra a la base. Ese archivo no debería quedarse en ningún disco compartido.</para>
/// </summary>
public record ServidorImportadoDto(
    string? Nombre,
    string? Host,
    int Puerto,
    string? Usuario,
    string? Contrasena,
    string? RutaRemota,
    string? Url);
