using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Foro;

/// <summary>
/// Un trozo del cuerpo de una entrada, YA analizado en el servidor: texto llano (<c>Url</c> nula) o
/// texto que abre un enlace (<c>Url</c> con una dirección http/https validada).
///
/// <b>Esta es la razón de que el cuerpo viaje troceado y no como una cadena suelta.</b> En el
/// escritorio los enlaces del foro se reconocían al pintar y se abrían con el programa asociado, así
/// que validar el esquema evitaba convertir una publicación en un lanzador de programas. En la web
/// el MISMO dato ataca por otro lado: si el cuerpo se renderizara como HTML sin sanear, quien
/// publica ejecutaría código en el navegador de todo el que lea el hilo (XSS almacenado), y con
/// <c>javascript:</c> o <c>data:text/html</c> bastaría un <c>href</c> para lo mismo.
///
/// Por eso el análisis y la validación ocurren EN EL SERVIDOR —<c>ForumRichText</c>— y aquí solo
/// llegan trozos ya decididos: el cliente no tiene que interpretar nada, y no puede equivocarse al
/// hacerlo. Quien pinte esto debe emitir <see cref="Texto"/> como TEXTO (jamás
/// <c>MarkupString</c> ni <c>innerHTML</c>) y el enlace como un ancla con
/// <c>rel="noopener noreferrer"</c> y <c>target="_blank"</c>.
///
/// Concatenar los <see cref="Texto"/> en orden devuelve exactamente lo que el lector ve.
/// </summary>
public record ForoSegmentoDto(string Texto, string? Url);

/// <summary>
/// Una publicación tal como se pinta en el muro: con sus contadores ya resueltos y el cuerpo
/// recortado a un extracto de texto llano (en la tarjeta no se pintan enlaces; el hilo sí).
///
/// Las <b>imágenes no viajan</b>: solo su número. Basta para saber que hay algo que ver, y traer las
/// miniaturas de todas las tarjetas movería megas por la red en cada refresco del muro.
/// </summary>
/// <param name="Retirada">
/// La entrada está retirada. No se borra ni desaparece del muro —eso rompería los hilos que le
/// contestan—, pero su <see cref="Extracto"/> es ya el aviso de contenido eliminado: el texto
/// original NO sale de la base.
/// </param>
public record ForoTarjetaDto(
    int Id,
    string Titulo,
    string Extracto,
    ForumTopic Tema,
    string TemaIcono,
    string TemaEtiqueta,
    string? Etiquetas,
    string Autor,
    bool EsMia,
    DateTime CreadaUtc,
    DateTime UltimaActividadUtc,
    bool Fijada,
    bool Cerrada,
    bool Retirada,
    int Comentarios,
    int MeGusta,
    bool YoDiMeGusta,
    int Imagenes);

/// <summary>
/// Una entrada dentro de un hilo (la publicación o cualquiera de sus comentarios), con el nivel de
/// sangría ya calculado.
/// </summary>
/// <param name="Nivel">
/// Con cuánta sangría pintarla. Viene topado a cinco niveles a propósito: más allá la conversación
/// se va al margen derecho y deja de leerse, así que los comentarios más profundos cuelgan del
/// último nivel. Es la misma decisión que tomó el escritorio.
/// </param>
/// <param name="Cuerpo">
/// El texto ya troceado (véase <see cref="ForoSegmentoDto"/>). En una entrada retirada es un solo
/// trozo con el aviso: el texto original no se devuelve.
/// </param>
public record ForoEntradaDto(
    int Id,
    int? PadreId,
    int Nivel,
    bool EsPublicacion,
    string? Titulo,
    IReadOnlyList<ForoSegmentoDto> Cuerpo,
    string Autor,
    bool EsMia,
    DateTime CreadaUtc,
    DateTime? EditadaUtc,
    bool Retirada,
    int MeGusta,
    bool YoDiMeGusta,
    int Imagenes);

/// <summary>
/// Un hilo completo: la publicación y sus comentarios en ORDEN DE LECTURA —cada respuesta justo
/// debajo de aquello a lo que contesta—, que es lo que hace que una conversación se lea como una
/// conversación y no como una lista suelta de mensajes.
/// </summary>
public record ForoHiloDto(
    int Id,
    string Titulo,
    ForumTopic Tema,
    string TemaIcono,
    string TemaEtiqueta,
    string? Etiquetas,
    bool Fijada,
    bool Cerrada,
    bool Retirada,
    int Comentarios,
    IReadOnlyList<ForoEntradaDto> Entradas);

/// <summary>
/// Una fila de la rejilla de auditoría del foro: quién dijo qué, cuándo, en qué hilo, si se editó y
/// si se retiró. Es la vista para reconstruir una conversación, no para participar, y por eso su
/// endpoint es SOLO del administrador.
/// </summary>
/// <param name="Texto">
/// Extracto de texto llano. En una entrada retirada es el aviso, nunca el original. Si la entrada
/// llevaba imágenes se marca con su número: una captura sin texto saldría en blanco, y en una
/// rejilla de auditoría «no dijo nada» y «puso una imagen» no pueden verse igual.
/// </param>
public record ForoAuditoriaFilaDto(
    int Id,
    int HiloId,
    DateTime CreadaUtc,
    string Autor,
    bool EsPublicacion,
    string Tipo,
    string Hilo,
    string Texto,
    string Estado,
    bool Retirada,
    bool Editada);

/// <summary>
/// Lo que necesitan los desplegables de filtro: los temas con su icono y los autores que de verdad
/// han escrito (armar el combo con una lista fija dejaría nombres que no aparecen en ninguna parte).
/// </summary>
public record ForoOpcionesDto(IReadOnlyList<OpcionDto> Temas, IReadOnlyList<string> Autores);

/// <summary>
/// Una imagen adjunta a una entrada del foro, <b>sin sus bytes</b>: lo que viaja es el identificador
/// con el que pedirla a <c>/api/adjuntos/foro/{Id}</c>, que ya la sirve con el tipo deducido de los
/// bytes y con <c>nosniff</c>.
///
/// Que la imagen no venga dentro del DTO no es un detalle de peso: un hilo con veinte capturas sería
/// una respuesta de decenas de megas que se descargaría entera cada vez que alguien recarga la
/// conversación. Pedidas por su dirección, el navegador las cachea y solo trae las que se están
/// mirando.
/// </summary>
/// <param name="Ancho">Medidas reales del archivo. Se guardan al subirlo para que quien la pinte
/// pueda reservarle sitio antes de que llegue, en lugar de dar un salto cuando termina de cargar.</param>
public record ForoImagenDto(int Id, int EntradaId, string Nombre, int Ancho, int Alto);

/// <summary>
/// Cómo quedó el ❤ después de pulsarlo. Lo decide el servidor y no el cliente: el total sube o baja
/// según lo que hubiera en la base, que no tiene por qué ser lo que esta pantalla creía tener.
/// </summary>
public record ForoMeGustaDto(bool YoDiMeGusta, int Total);

/// <summary>
/// Lo que devuelve publicar: el mensaje del servicio y el hilo recién creado, para poder abrirlo sin
/// tener que buscarlo en el muro.
/// </summary>
public record ForoPublicadaDto(int Id, string Mensaje);
