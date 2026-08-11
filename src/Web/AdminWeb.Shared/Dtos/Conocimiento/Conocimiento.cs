using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Conocimiento;

/// <summary>
/// Qué clase de bloque es. Es la lista COMPLETA de lo que la base de conocimiento sabe pintar, y
/// que sea corta es la decisión, no una etapa: cada forma nueva es una forma más de equivocarse al
/// pintarla, y con títulos, listas, código y párrafos ya se documenta cualquier cosa.
/// </summary>
public enum TipoDeBloque
{
    Parrafo = 0,
    /// <summary>Título de sección. <c>Nivel</c> va de 1 a 3.</summary>
    Titulo = 1,
    /// <summary>Lista de puntos. Cada renglón es un punto; <c>Numerada</c> dice si van con número.</summary>
    Lista = 2,
    /// <summary>Bloque de código. Sus renglones son texto CRUDO: dentro no se interpreta nada.</summary>
    Codigo = 3
}

/// <summary>
/// Un trozo de una línea, ya decidido en el servidor: texto llano, texto en negrita, texto en
/// código, o texto que abre un enlace.
///
/// <para><b>Por qué el cuerpo viaja troceado y no como una cadena de marcado.</b> Lo escribe
/// cualquiera del equipo y lo lee todo el mundo: si se armara HTML con lo que alguien teclea, quien
/// publica ejecutaría código en el navegador de todo el que abra el artículo. Aquí no hay marcado
/// que construir — hay trozos con banderas—, así que no hay nada que escapar ni nada que sanear al
/// pintar.</para>
///
/// <para>Quien pinte esto debe emitir <see cref="Texto"/> como TEXTO (jamás <c>MarkupString</c> ni
/// <c>innerHTML</c>) y el enlace como un ancla con <c>rel="noopener noreferrer"</c>. La dirección ya
/// viene validada: solo http y https, decidido en el servidor con la misma comprobación que usa el
/// foro, porque el cliente corre en la máquina de quien lee y se puede manipular.</para>
/// </summary>
/// <param name="Url">Null en el texto normal; la dirección validada y normalizada en un enlace.</param>
/// <param name="Negrita">Se pinta destacado. Nunca coincide con <paramref name="Codigo"/>.</param>
/// <param name="Codigo">Se pinta en monoespaciado. Dentro no hay enlaces ni negritas: es código.</param>
public record ConocimientoSegmentoDto(string Texto, string? Url = null, bool Negrita = false, bool Codigo = false);

/// <summary>Una línea del artículo, ya troceada. Concatenar los textos devuelve lo que se lee.</summary>
public record ConocimientoRenglonDto(IReadOnlyList<ConocimientoSegmentoDto> Segmentos);

/// <summary>
/// Un bloque del artículo. La forma es la MISMA para los cuatro tipos —una lista de renglones— para
/// que quien lo pinte tenga un solo recorrido y no cuatro: un párrafo y un título traen un renglón,
/// una lista trae uno por punto y un bloque de código uno por línea.
/// </summary>
/// <param name="Nivel">1, 2 o 3 en un título. Cero en todo lo demás.</param>
/// <param name="Numerada">Solo en una lista: sus puntos van numerados en vez de con viñeta.</param>
/// <param name="Lenguaje">Solo en código, y solo si el autor lo escribió junto a las comillas.</param>
public record ConocimientoBloqueDto(
    TipoDeBloque Tipo,
    IReadOnlyList<ConocimientoRenglonDto> Renglones,
    int Nivel = 0,
    bool Numerada = false,
    string? Lenguaje = null);

/// <summary>
/// Un artículo tal como sale en una lista de resultados: sin el cuerpo entero, con un extracto de
/// texto llano. Traer el cuerpo completo de cada resultado convertiría una búsqueda en una descarga.
/// </summary>
/// <param name="EsMio">Lo escribió quien está consultando. Sirve para separar «lo mío» de lo demás.</param>
/// <param name="PuntosOtorgados">Cero mientras nadie le haya dado puntos. No todos los artículos puntúan.</param>
public record ConocimientoTarjetaDto(
    int Id,
    string Titulo,
    string Extracto,
    string? Etiquetas,
    KnowledgeStatus Estado,
    string EstadoEtiqueta,
    string Autor,
    bool EsMio,
    DateTime CreadoUtc,
    DateTime? ActualizadoUtc,
    DateTime? PublicadoUtc,
    int PuntosOtorgados,
    int VueltaDeRevision);

/// <summary>
/// Un artículo abierto, con el cuerpo ya analizado en bloques.
/// </summary>
/// <param name="Fuente">
/// El texto CRUDO, con su marcado sin interpretar, para poder editarlo. Va nulo cuando quien lee no
/// puede editar: no es un secreto —el mismo contenido está en <paramref name="Cuerpo"/>— pero
/// mandarlo a quien no lo va a usar invita a que alguna pantalla acabe pintando la fuente en lugar
/// de los bloques, que es exactamente lo que este diseño evita.
/// </param>
/// <param name="NotaDeRevision">El motivo del rechazo o la nota con que se aprobó. Solo para el autor y el líder.</param>
/// <param name="Historial">El ida y vuelta completo, fechado. Solo para el autor y el líder.</param>
public record ConocimientoArticuloDto(
    int Id,
    string Titulo,
    IReadOnlyList<ConocimientoBloqueDto> Cuerpo,
    string? Fuente,
    string? Etiquetas,
    KnowledgeStatus Estado,
    string EstadoEtiqueta,
    string Autor,
    bool EsMio,
    DateTime CreadoUtc,
    DateTime? ActualizadoUtc,
    DateTime? PublicadoUtc,
    string? Revisor,
    DateTime? RevisadoUtc,
    string? NotaDeRevision,
    string? Historial,
    int VueltaDeRevision,
    int PuntosOtorgados,
    bool PuedoEditar,
    bool PuedoEnviar,
    bool PuedoRevisar);

/// <summary>
/// Una fila de la cola del líder. Lleva los días esperando ya calculados porque son la razón de que
/// la cola exista: lo que lleva nueve días parado es lo que hace que su autor no vuelva a escribir.
/// </summary>
public record ConocimientoColaFilaDto(
    int Id, string Titulo, string Autor, DateTime EnviadoUtc, int DiasEsperando, int Vuelta);

/// <summary>
/// Los contadores que la pantalla necesita para ENSEÑAR lo pendiente sin que nadie vaya a buscarlo.
///
/// <para>Existe por lo que mata a una cola de revisión: nadie la abre. Con estos números el menú
/// puede llevar su marca, y la lleva para los dos lados —lo que al líder le falta revisar y lo que
/// al autor le devolvieron—, porque una cola se muere igual por arriba que por abajo.</para>
/// </summary>
/// <param name="PorRevisar">Cuántos esperan al líder. Cero para quien no revisa.</param>
/// <param name="DiasDelMasAntiguo">Cuánto lleva esperando el más viejo de la cola. Cero si no hay.</param>
/// <param name="MisBorradores">Los que quien consulta tiene a medias.</param>
/// <param name="MisDevueltos">Los que le devolvieron y todavía no ha vuelto a mandar.</param>
public record ConocimientoPendientesDto(
    int PorRevisar, int DiasDelMasAntiguo, int MisBorradores, int MisDevueltos);

/// <summary>Una etiqueta con cuántos artículos publicados la llevan. Es el índice de la base.</summary>
public record ConocimientoEtiquetaDto(string Etiqueta, int Articulos);

/// <summary>Lo que devuelve crear: el mensaje del servicio y el identificador para poder abrirlo.</summary>
public record ConocimientoCreadoDto(int Id, string Mensaje);

// ── Lo que manda la pantalla ─────────────────────────────────────────────────────

/// <summary>Alta o edición de un artículo. Va en JSON: aquí no se suben archivos.</summary>
public record ConocimientoEscrituraDto(string? Titulo, string? Cuerpo, string? Etiquetas);

/// <summary>
/// La aprobación del líder, con los puntos DENTRO.
///
/// <para><b>Van en la misma petición a propósito.</b> Aprobar y otorgar son dos decisiones pero un
/// solo momento: si se separan en dos pasos, el segundo se olvida — y unos puntos que se otorgan
/// tres días después ya no premian nada, solo confunden el mes al que pertenecen.</para>
///
/// <para>Los tres campos son opcionales porque <b>no todos los artículos puntúan</b>: aprobar sin
/// puntos es un caso normal y frecuente, no un descuido.</para>
/// </summary>
/// <param name="CriterioId">Criterio del catálogo de puntuación, el mismo con el que se puntúa todo lo demás.</param>
/// <param name="Puntos">Cuántos. Null o cero significa que este artículo no lleva puntos.</param>
/// <param name="Nota">Por qué se le dieron —o comentario de la aprobación—. Queda en el historial.</param>
public record ConocimientoAprobacionDto(int? CriterioId, int? Puntos, string? Nota);

/// <summary>La devolución del líder. El motivo NO es opcional: devolver sin decir por qué no enseña nada.</summary>
public record ConocimientoRechazoDto(string? Motivo);
