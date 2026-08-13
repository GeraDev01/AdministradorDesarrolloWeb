using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Documentos;

/// <summary>
/// Los tres documentos que la aplicación entrega en PDF.
///
/// <para><b>Existe una interfaz porque la librería de PDF es una decisión reversible.</b> Se usa
/// QuestPDF con su licencia Community, gratuita mientras la empresa facture por debajo del umbral
/// que ella misma fija. El día que dejara de aplicar, cambiar a PdfSharp/MigraDoc (MIT, sin
/// condición) es escribir otra implementación de esto: quien pide un documento no se entera.</para>
///
/// <para><b>Qué cambia respecto al escritorio.</b> Allí los tres se armaban como DOCX a partir de
/// una plantilla y se convertían con LibreOffice instalado en la máquina de cada quien. Eso se
/// descartó: obligaba a instalar un programa de escritorio en el servidor y era la única pieza de la
/// web que no podía correr sola. <b>La consecuencia aceptada es que la plantilla .docx deja de ser
/// editable por Recursos Humanos</b>: el diseño del documento pasa a estar en el código. A cambio,
/// el documento se genera igual en cualquier servidor y sin instalar nada.</para>
///
/// <para>Todos devuelven BYTES. Ninguno escribe en disco: en el escritorio quien llamaba abría un
/// diálogo de «guardar como», aquí el endpoint devuelve el archivo y decide el navegador de quien lo
/// descarga.</para>
/// </summary>
public interface IGeneradorDeDocumentos
{
    /// <summary>La solicitud de vacaciones, con la firma del jefe si ya la autorizó.</summary>
    byte[] SolicitudDeVacaciones(DatosDeVacaciones datos);

    /// <summary>La ficha de un desarrollador: sus evaluaciones, hitos e indicadores.</summary>
    byte[] FichaDeDesarrollador(DatosDeFicha datos);

    /// <summary>El organigrama de equipos, dibujado, para imprimirlo o repartirlo.</summary>
    byte[] OrganizacionDeEquipos(DatosDeEquipos datos);
}

// ── Solicitud de vacaciones ─────────────────────────────────────────────────────

/// <summary>
/// Lo que va impreso en la solicitud de vacaciones. Todo llega ya resuelto y formateado: el
/// generador maqueta, no calcula. Así las reglas —qué día se regresa, cuántos días quedan— se
/// prueban sin abrir un PDF.
/// </summary>
public record DatosDeVacaciones(
    string Nombre,
    string FechaSolicitud,
    string Departamento,
    string Puesto,
    string JefeDirecto,
    string FechaIngreso,
    string TotalDias,
    string Periodo,
    string FechaInicio,
    string FechaFin,
    string FechaRegreso,
    string DiasPendientes,
    bool Autorizada,
    bool Rechazada,
    string Observaciones,
    /// <summary>PNG de la firma del jefe. Null en un borrador todavía sin resolver.</summary>
    byte[]? FirmaDelJefe,
    /// <summary>
    /// PNG de la firma de quien pide las vacaciones. Null mientras no la haya firmado — o mientras la
    /// que hay no valga, porque la solicitud cambió después de firmarse.
    ///
    /// <para>Va con valor por omisión para que las dos salidas del documento —el PDF maquetado en
    /// código y el .docx de la plantilla— sigan construyéndose igual que antes cuando no hay firma del
    /// colaborador, que es el caso de todo lo que se pidió antes de que esto existiera.</para>
    /// </summary>
    byte[]? FirmaDelColaborador = null);

// ── Ficha de desarrollador ──────────────────────────────────────────────────────

public record EvaluacionImpresa(
    DateTime Fecha, string? Periodo, int? Calificacion,
    string? Fortalezas, string? Debilidades, string? Comentarios, string? Evaluador);

public record HitoImpreso(DateTime Fecha, MilestoneKind Tipo, string Titulo, string? Descripcion);

public record DatosDeFicha(
    string NombreCompleto,
    string? Correo,
    string? Telefono,
    string? Nivel,
    string? Equipo,
    string? RolEnElEquipo,
    DateTime? FechaDeIngreso,
    int PuntosAprobadosDelAnio,
    string TiempoTotal,
    int AsignacionesActivas,
    IReadOnlyList<EvaluacionImpresa> Evaluaciones,
    IReadOnlyList<HitoImpreso> Hitos,
    string GeneradoEl);

// ── Organigrama de equipos ──────────────────────────────────────────────────────

/// <summary>
/// Una persona dentro de una caja del organigrama.
///
/// <para><b>Llega en piezas y no como una cadena ya armada</b>, que es como venía antes
/// («Ana Pérez (Senior) — Backend Dev»). El cambio no es cosmético: mientras el documento era una
/// LISTA, una frase por renglón bastaba; en un diagrama cada dato ocupa su sitio —el nombre pesa,
/// el nivel y el rol van pequeños al lado, la función va debajo en su propia línea y puede partirse
/// en dos— y eso no se puede hacer con un texto ya concatenado sin volver a trocearlo aquí.</para>
/// </summary>
/// <param name="Nivel">El «Senior», «Junior»… de la ficha. Puede faltar y entonces no se imprime nada:
/// un paréntesis vacío detrás de un nombre parece un error de impresión.</param>
/// <param name="Funcion">Qué hace dentro del equipo. Opcional; sin ella la tarjeta se dibuja de una
/// sola línea en vez de dejar un renglón en blanco.</param>
/// <param name="EsLider">Se dibuja distinto. Quién lo es lo decide el servicio, no el papel.</param>
public record IntegranteImpreso(
    string Nombre, string? Nivel, string Rol, string? Funcion, bool EsLider);

/// <param name="EquipoPadre">El NOMBRE del equipo del que cuelga, o nulo si es un equipo raíz. El
/// nombre y no el identificador porque este contrato no conoce identificadores —el generador maqueta,
/// no decide— y porque lo que se imprime en la caja es un nombre; dos equipos no pueden llamarse
/// igual, así que también sirve para saber de quién cuelga.
///
/// <para><b>Con esto el documento arma el árbol</b>, y no solo imprime un renglón: de ahí sale qué
/// rama se lleva su propia hoja y cuánta sangría le toca a cada caja. Un nombre que no esté en la
/// lista se trata como si no hubiera padre —el equipo sale como raíz—, porque en un organigrama faltar
/// no es una caja menos: es un equipo que oficialmente no está en ninguna parte.</para></param>
public record EquipoImpreso(
    string Nombre, string? Descripcion, string? Lider, string? ColorHex,
    string? EquipoPadre,
    IReadOnlyList<IntegranteImpreso> Integrantes,
    IReadOnlyList<string> Sistemas, IReadOnlyList<string> Proyectos);

/// <param name="SinEquipo">Quien no está en ningún equipo. <b>Va en el diagrama como una caja más</b>,
/// no en una nota al pie: un organigrama que solo dibuja a quien tiene equipo miente por omisión, y
/// esa caja suele ser justo lo que se viene a mirar.</param>
/// <param name="TotalPersonas">Cuántas personas activas hay en total, con equipo y sin él. Va en el
/// nodo de arriba del diagrama; se cuenta en el servidor para que el papel no pueda contradecir a la
/// pantalla por sumar cada uno por su cuenta.</param>
/// <param name="Equipos">Ya vienen EN ORDEN DE DIBUJO —cada padre delante de su rama— y planos, igual
/// que los recibe la pantalla. El papel no los reordena: es el servidor quien decide el orden para que
/// los dos dibujantes lean lo mismo.</param>
public record DatosDeEquipos(
    IReadOnlyList<EquipoImpreso> Equipos,
    IReadOnlyList<IntegranteImpreso> SinEquipo,
    int TotalPersonas,
    string GeneradoEl);

// ── La solicitud de vacaciones en WORD, desde una plantilla editable ────────────

/// <summary>
/// Los marcadores que la plantilla de Word puede llevar dentro.
///
/// <para>Es UN solo catálogo a propósito: lo usan el validador de la subida y el que rellena el
/// documento. Si cada uno tuviera su lista, se podría aceptar una plantilla que después imprimiera
/// <c>{{FECHA_REGRESO}}</c> literal en el expediente de alguien.</para>
/// </summary>
public static class TokensDeVacaciones
{
    public const string FirmaDelJefe = "{{FIRMA_GERENTE}}";
    public const string FirmaDelColaborador = "{{FIRMA_COLABORADOR}}";

    /// <summary>
    /// Los que la plantilla DEBE tener. Sin uno de éstos el documento saldría incompleto y nadie se
    /// enteraría hasta tenerlo firmado.
    ///
    /// <para><see cref="FirmaDelJefe"/> está aquí: sin su ancla, la firma no tiene dónde entrar y el
    /// documento «firmado» saldría sin firma. <see cref="FirmaDelColaborador"/> sigue SIN estar,
    /// aunque ya se estampe: exigirlo rechazaría una plantilla que dibuje la raya sin poner el
    /// marcador —que es como venían las de antes—, y esa plantilla no está rota: el documento sale
    /// igual que siempre y la persona firma a mano sobre el papel. Lo que se pierde sin el ancla es
    /// la firma desde la web, no el documento.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Obligatorios =
    [
        "{{NOMBRE}}", "{{FECHA_SOLICITUD}}", "{{DEPARTAMENTO}}", "{{PUESTO}}", "{{JEFE_DIRECTO}}",
        "{{FECHA_INGRESO}}", "{{TOTAL_DIAS}}", "{{PERIODO}}", "{{FECHA_INICIO}}", "{{FECHA_FIN}}",
        "{{FECHA_REGRESO}}", "{{DIAS_PENDIENTES}}", "{{AUTORIZA_SI}}", "{{AUTORIZA_NO}}",
        "{{OBSERVACIONES}}", FirmaDelJefe
    ];

    /// <summary>
    /// Qué texto sustituye a cada marcador. <b>NINGUNA de las dos firmas está aquí: son imágenes.</b>
    ///
    /// <para>La del colaborador sí estuvo, mapeada a cadena vacía, porque antes se BORRABA siempre
    /// —la persona firmaba a mano sobre el papel impreso—. Se quitó al poder firmarse desde la web:
    /// sustituirla por vacío aquí se ejecuta ANTES de estampar y le dejaba al estampado un ancla que
    /// ya no existía, así que la firma no se pegaba en ninguna parte y el documento salía sin ella
    /// sin que nada fallara. Ahora las dos anclas las resuelve el mismo paso, que es también quien
    /// las borra cuando no hay firma que poner.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Mapa(DatosDeVacaciones d) => new Dictionary<string, string>
    {
        ["{{NOMBRE}}"]          = d.Nombre,
        ["{{FECHA_SOLICITUD}}"] = d.FechaSolicitud,
        ["{{DEPARTAMENTO}}"]    = d.Departamento,
        ["{{PUESTO}}"]          = d.Puesto,
        ["{{JEFE_DIRECTO}}"]    = d.JefeDirecto,
        ["{{FECHA_INGRESO}}"]   = d.FechaIngreso,
        ["{{TOTAL_DIAS}}"]      = d.TotalDias,
        ["{{PERIODO}}"]         = d.Periodo,
        ["{{FECHA_INICIO}}"]    = d.FechaInicio,
        ["{{FECHA_FIN}}"]       = d.FechaFin,
        ["{{FECHA_REGRESO}}"]   = d.FechaRegreso,
        ["{{DIAS_PENDIENTES}}"] = d.DiasPendientes,
        ["{{AUTORIZA_SI}}"]     = d.Autorizada ? "X" : "",
        ["{{AUTORIZA_NO}}"]     = d.Rechazada ? "X" : "",
        ["{{OBSERVACIONES}}"]   = d.Observaciones
    };
}

/// <summary>Una firma ya lista para estampar: PNG con fondo transparente y sus medidas.</summary>
public record FirmaEnPng(byte[] Png, int Ancho, int Alto);

/// <summary>Qué le pasa a una plantilla que alguien acaba de subir.</summary>
/// <param name="TokensFaltantes">Los marcadores obligatorios que no aparecen. Se nombran todos para
/// que quien la editó los corrija de una vez y no de uno en uno.</param>
public record ValidacionDePlantilla(bool Ok, string Mensaje, IReadOnlyList<string> TokensFaltantes);

/// <summary>
/// Rellena la solicitud de vacaciones sobre una plantilla de Word que el área puede editar.
///
/// <para><b>Por qué vuelve el .docx.</b> Se había sustituido por maquetado en código con QuestPDF
/// como consecuencia de quitar LibreOffice, y con eso RH perdió poder cambiar una palabra del
/// formato sin recompilar. Pero LibreOffice nunca hizo falta para ESTO: solo para convertir el
/// resultado a PDF. Rellenar la plantilla es OpenXml, que es gratis y corre en cualquier sitio.</para>
///
/// <para>El PDF se conserva: son dos salidas del mismo documento y cada una tiene su momento — el
/// Word para editarlo o imprimirlo con el formato de RH, el PDF para archivarlo.</para>
/// </summary>
public interface IPlantillaDeVacacionesEnWord
{
    /// <summary>
    /// Devuelve el .docx relleno. Cada firma va estampada en SU ancla si viene, y el ancla se borra
    /// cuando no viene, para que el marcador no acabe impreso.
    /// </summary>
    /// <param name="firmaDelColaborador">La de quien pide las vacaciones, que firma al solicitar.
    /// Es opcional —y no un parámetro más— para que una solicitud sin firmar siga produciendo el
    /// mismo documento de siempre: el que se imprime y se firma a mano.</param>
    byte[] Rellenar(byte[] plantillaDocx, DatosDeVacaciones datos, FirmaEnPng? firmaDelJefe,
        FirmaEnPng? firmaDelColaborador = null);

    /// <summary>Comprueba que lo subido sea un .docx de verdad y lleve los marcadores necesarios.</summary>
    ValidacionDePlantilla Validar(byte[] posibleDocx);

    /// <summary>La plantilla que trae la aplicación, para arrancar sin que nadie suba nada.</summary>
    byte[] DeFabrica();
}
