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

    /// <summary>La organización de equipos, para imprimirla o repartirla.</summary>
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
    byte[]? FirmaDelJefe);

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

// ── Organización de equipos ─────────────────────────────────────────────────────

public record EquipoImpreso(
    string Nombre, string? Descripcion, string? Lider, string? ColorHex,
    IReadOnlyList<string> Integrantes, IReadOnlyList<string> Sistemas, IReadOnlyList<string> Proyectos);

public record DatosDeEquipos(
    IReadOnlyList<EquipoImpreso> Equipos,
    IReadOnlyList<string> SinEquipo,
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
    /// documento «firmado» saldría sin firma. <see cref="FirmaDelColaborador"/> NO, porque el
    /// escritorio solo lo BORRA —la persona firma a mano sobre el papel— y exigirlo rechazaría una
    /// plantilla que dibuje la raya sin poner el marcador.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Obligatorios =
    [
        "{{NOMBRE}}", "{{FECHA_SOLICITUD}}", "{{DEPARTAMENTO}}", "{{PUESTO}}", "{{JEFE_DIRECTO}}",
        "{{FECHA_INGRESO}}", "{{TOTAL_DIAS}}", "{{PERIODO}}", "{{FECHA_INICIO}}", "{{FECHA_FIN}}",
        "{{FECHA_REGRESO}}", "{{DIAS_PENDIENTES}}", "{{AUTORIZA_SI}}", "{{AUTORIZA_NO}}",
        "{{OBSERVACIONES}}", FirmaDelJefe
    ];

    /// <summary>Qué texto sustituye a cada marcador. La firma del jefe NO está: es una imagen.</summary>
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
        ["{{OBSERVACIONES}}"]   = d.Observaciones,
        // Se BORRA, no se rellena: quien pide las vacaciones firma a mano sobre el papel impreso.
        [FirmaDelColaborador]   = ""
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
    /// <summary>Devuelve el .docx relleno. La firma va estampada en su ancla si viene.</summary>
    byte[] Rellenar(byte[] plantillaDocx, DatosDeVacaciones datos, FirmaEnPng? firmaDelJefe);

    /// <summary>Comprueba que lo subido sea un .docx de verdad y lleve los marcadores necesarios.</summary>
    ValidacionDePlantilla Validar(byte[] posibleDocx);

    /// <summary>La plantilla que trae la aplicación, para arrancar sin que nadie suba nada.</summary>
    byte[] DeFabrica();
}
