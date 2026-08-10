using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Administracion;

// ═══ Configuración ═══════════════════════════════════════════════════════════════
//
// Ninguno de estos tipos lleva el valor de un secreto, y no es una omisión: el servicio de
// configuración devuelve null en los secretos a propósito. Esconderlos solo en la pantalla no
// serviría de nada, porque quien abra la consola del navegador lee la respuesta entera.

/// <summary>
/// Con qué control se captura una clave. Lo decide el SERVIDOR, que es quien conoce el significado
/// de cada clave; la pantalla solo elige el control. Si lo decidiera la pantalla, cada clave nueva
/// obligaría a tocar el cliente además del servidor.
/// </summary>
public enum TipoDeCampoDeConfiguracion
{
    Texto,
    TextoLargo,
    Numero,

    /// <summary>Un «true»/«false» de los que el escritorio pintaba como casilla.</summary>
    Interruptor,

    /// <summary>Se puede escribir y reemplazar, nunca leer.</summary>
    Secreto
}

/// <summary>
/// Una clave de configuración tal como la ve la pantalla.
/// </summary>
/// <param name="Etiqueta">Cómo se lee la clave. Sale del servidor y no de la pantalla para que el
/// texto viva en un solo sitio.</param>
/// <param name="Ayuda">La línea de abajo: qué se espera ahí, o qué pasa si se deja vacía.</param>
/// <param name="Valor"><b>Siempre null en los secretos.</b> Se sabe si están puestos, no cuáles son.</param>
/// <param name="Configurado">Tiene valor guardado. En un secreto es lo único que se puede saber de él.</param>
/// <param name="RequiereRecaptura">El secreto quedó cifrado con el esquema viejo de Windows y el
/// servidor no puede leerlo. No falta: hay que volver a capturarlo. Decirlo evita el diagnóstico
/// equivocado de «se borró solo».</param>
public record ClaveDeConfiguracionDto(
    string Clave,
    string Etiqueta,
    string? Ayuda,
    string? Valor,
    bool EsSecreto,
    bool Configurado,
    bool RequiereRecaptura,
    TipoDeCampoDeConfiguracion Tipo);

/// <summary>
/// Qué integración se puede comprobar desde la pantalla de configuración.
///
/// <para><b>Es lo único que viaja de una prueba, y a propósito.</b> La petición dice QUÉ probar, no
/// CON QUÉ: los valores salen de lo que está guardado. Aceptar una cadena de conexión por el cuerpo
/// —como hacía el escritorio, que probaba antes de guardar— convertiría el endpoint en un probador de
/// credenciales ajenas al alcance de cualquiera con una sesión de líder.</para>
/// </summary>
public enum PruebaDeConexion
{
    /// <summary>La cuenta y el contenedor de Azure Blob Storage.</summary>
    Blob,

    /// <summary>La base de SQL Server / Azure SQL que sigue usando el escritorio.</summary>
    Sql,

    /// <summary>La cuenta de correo: SMTP, y el IMAP con su carpeta de ingesta si está puesto.</summary>
    Correo
}

/// <summary>Comprobar una integración con lo que YA está guardado.</summary>
public record ProbarConexionRequest(PruebaDeConexion Que);

/// <summary>Un apartado de la pantalla de configuración, con el mismo corte que el escritorio.</summary>
/// <param name="Nota">Advertencia del apartado entero, cuando la hay.</param>
/// <param name="Prueba">
/// El apartado se puede comprobar contra el servicio real. Lo declara el SERVIDOR y no la pantalla:
/// es él quien sabe qué claves componen una conexión, y una lista escrita en el cliente se
/// desincronizaría el día que un apartado cambie de nombre.
/// </param>
public record GrupoDeConfiguracionDto(
    string Titulo,
    string? Nota,
    IReadOnlyList<ClaveDeConfiguracionDto> Claves,
    PruebaDeConexion? Prueba);

/// <summary>Toda la configuración visible, ya agrupada y ordenada.</summary>
public record ConfiguracionDto(IReadOnlyList<GrupoDeConfiguracionDto> Grupos);

/// <summary>
/// Una clave que se quiere guardar. <paramref name="Valor"/> vacío BORRA el valor.
///
/// La pantalla manda únicamente las claves que se tocaron. Es importante en los secretos: su caja
/// empieza siempre vacía —no se puede leer lo guardado—, y mandarla sin más borraría la contraseña
/// que ya estaba puesta.
/// </summary>
public record CambioDeConfiguracionDto(string Clave, string? Valor);

/// <summary>Las claves editadas en una sola pasada, como el «Guardar configuración general» del escritorio.</summary>
public record GuardarConfiguracionRequest(IReadOnlyList<CambioDeConfiguracionDto> Cambios);

/// <summary>Cómo le fue a una clave. El <paramref name="Mensaje"/> lo escribió el servicio y se enseña tal cual.</summary>
public record ResultadoDeClaveDto(string Clave, bool Ok, string Mensaje);

/// <summary>El resultado clave por clave: una que falle no invalida a las demás.</summary>
public record ResultadoDeConfiguracionDto(IReadOnlyList<ResultadoDeClaveDto> Resultados);

// ═══ Limpieza de datos ═══════════════════════════════════════════════════════════

/// <summary>
/// Un apartado de la aplicación visto desde la limpieza.
/// </summary>
/// <param name="Arrastra">Qué más se lleva por delante. Es la mitad importante de la ficha: nadie
/// espera que borrar «Requerimientos» borre también las horas trabajadas, y enterarse después no
/// sirve de nada.</param>
/// <param name="Advertencia">Si el área merece que se piense dos veces, por qué.</param>
/// <param name="Registros">Cuántos hay hoy. <c>-1</c> es «no se pudo contar»: se enseña como
/// desconocido en vez de aparentar que está vacío.</param>
public record AreaDeLimpiezaDto(
    string Clave,
    string Grupo,
    string Nombre,
    string Arrastra,
    string? Advertencia,
    int Registros);

/// <summary>
/// La pantalla de limpieza completa.
/// </summary>
/// <param name="FraseConfirmacion">Lo que hay que escribir para confirmar. Lo manda el servidor
/// porque es él quien la exige; escribirla también en la pantalla dejaría dos copias de la misma
/// palabra, y la segunda se desincronizaría el día que cambie.</param>
public record LimpiezaDto(string FraseConfirmacion, IReadOnlyList<AreaDeLimpiezaDto> Areas);

/// <summary>
/// La petición de borrado.
///
/// Lleva la contraseña porque la reautenticación es del lado del SERVIDOR: comprobarla solo en la
/// pantalla no valdría de nada, ya que a la API se la puede llamar sin pasar por el navegador.
/// </summary>
public record LimpiarRequest(IReadOnlyList<string> Claves, string Confirmacion, string Contrasena);

/// <summary>Cómo le fue a un área. <paramref name="Filas"/> incluye lo arrastrado, no solo lo principal.</summary>
public record AreaLimpiadaDto(string Clave, string Nombre, int Habia, int Filas, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>El resultado de la pasada, área por área, con su resumen ya redactado por el servicio.</summary>
public record ResultadoDeLimpiezaDto(string Mensaje, IReadOnlyList<AreaLimpiadaDto> Areas);

// ═══ Minutas ═════════════════════════════════════════════════════════════════════
//
// Las fechas de una minuta son DÍAS del calendario, no instantes: se guardan a medianoche y la
// pantalla las pinta sin convertir a hora local. Convertirlas restaría horas y en un huso al este
// de Greenwich enseñaría el día anterior, que es peor que cualquier desajuste de husos.

/// <summary>Una minuta en la lista. El contenido no viaja aquí: puede ser largo y la rejilla no lo enseña.</summary>
/// <param name="Pendientes">Compromisos sin cumplir. Es lo que hace que una minuta vieja siga importando.</param>
public record MinutaEnListaDto(
    int Id,
    MinuteType Tipo,
    string TipoTexto,
    DateTime Fecha,
    string Titulo,
    int Compromisos,
    int Pendientes);

/// <summary>Un compromiso adquirido en una minuta.</summary>
public record CompromisoDeMinutaDto(
    int Id,
    string Descripcion,
    int? ResponsableId,
    string? Responsable,
    DateTime? Limite,
    bool Cumplido);

/// <summary>Una minuta completa, para abrirla y editarla.</summary>
public record MinutaDto(
    int Id,
    MinuteType Tipo,
    DateTime Fecha,
    string Titulo,
    string? Contenido,
    IReadOnlyList<CompromisoDeMinutaDto> Compromisos);

/// <summary>Una opción del desplegable de tipos, con su texto puesto por el servidor.</summary>
public record TipoDeMinutaDto(MinuteType Valor, string Texto);

/// <summary>
/// Todo lo que la pantalla de minutas necesita de una vez: la lista filtrada, los tipos y a quién se
/// le puede encargar un compromiso.
/// </summary>
public record MinutasDto(
    IReadOnlyList<MinutaEnListaDto> Minutas,
    IReadOnlyList<TipoDeMinutaDto> Tipos,
    IReadOnlyList<OpcionDto> Responsables);

/// <summary>Un compromiso tal como llega de la pantalla. <c>Id</c> 0 es uno nuevo.</summary>
public record CompromisoRequest(
    int Id,
    string Descripcion,
    int? ResponsableId,
    DateTime? Limite,
    bool Cumplido);

/// <summary>
/// Alta o edición de una minuta, con sus compromisos. <c>Id</c> 0 es un alta.
///
/// Los compromisos vienen COMPLETOS y no como una lista de cambios: es lo que hacía el escritorio
/// (la ventana devolvía la minuta entera) y evita que el cliente tenga que llevar la cuenta de qué
/// se borró.
/// </summary>
public record GuardarMinutaRequest(
    int Id,
    MinuteType Tipo,
    DateTime Fecha,
    string Titulo,
    string? Contenido,
    IReadOnlyList<CompromisoRequest> Compromisos);

// ═══ Reportes ════════════════════════════════════════════════════════════════════

/// <summary>Un reporte de la lista, con la descripción que explica qué mide.</summary>
public record ReporteDisponibleDto(string Clave, string Nombre, string Descripcion);

/// <summary>
/// Lo que la pantalla de reportes necesita antes de generar nada: qué reportes hay y a quién se
/// puede filtrar.
/// </summary>
public record CatalogoDeReportesDto(
    IReadOnlyList<ReporteDisponibleDto> Reportes,
    IReadOnlyList<OpcionDto> Desarrolladores);

/// <summary>Una cifra grande del tablero. Ya viene formateada: quien la calcula sabe si lleva decimales.</summary>
public record IndicadorDto(string Titulo, string Valor);

/// <summary>Una barra de la gráfica.</summary>
public record BarraDeReporteDto(string Etiqueta, double Valor);

/// <summary>
/// Un reporte ya generado.
///
/// Las filas viajan como TEXTO ya formateado. Los tipos reales (fechas, decimales) se conservan en
/// el servidor para la exportación a Excel, que es donde importan; mandarlos al navegador como
/// <c>object</c> obligaría a la pantalla a adivinar cómo pintar cada celda, y ese es justo el
/// trabajo que la rejilla del escritorio ya hacía en el servidor.
/// </summary>
public record ReporteGeneradoDto(
    string Clave,
    string Nombre,
    string Descripcion,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<string>> Filas,
    IReadOnlyList<IndicadorDto> Indicadores,
    string TituloDeGrafica,
    IReadOnlyList<BarraDeReporteDto> Barras);

/// <summary>
/// Lo que hace falta para mandar un reporte por correo.
///
/// <b>Ni el reporte ni sus filas viajan aquí</b>: el reporte se identifica por su clave y el período
/// va en la dirección, y el servidor lo vuelve a generar. Mandar de vuelta lo que la pantalla ya
/// tiene pintado permitiría que el correo dijera algo distinto de lo que el reporte calcula.
/// </summary>
/// <param name="Destinatarios">Uno o varios, separados por «;» o «,».</param>
/// <param name="Nota">Opcional, lo que quiera decir quien lo manda. Va al principio del correo.</param>
public record EnviarReportePorCorreoRequest(string? Destinatarios, string? Nota);
