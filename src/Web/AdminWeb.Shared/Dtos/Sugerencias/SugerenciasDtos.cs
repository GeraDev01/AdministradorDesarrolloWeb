using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Sugerencias;

/// <summary>
/// Una sugerencia propia, tal como se ve en «Mis sugerencias».
///
/// No lleva autor, y no es un olvido: esta lista se arma con las del usuario que pregunta, así que
/// el autor es siempre él. Mandar su nombre de vuelta solo sería una copia más de un dato que no
/// hace falta.
/// </summary>
/// <param name="Anonima">
/// La mandó sin nombre. Se le dice porque es lo que eligió y tiene que poder comprobarlo, pero
/// enseñarlo aquí no rompe nada: quien lo lee es quien la escribió.
/// </param>
/// <param name="RespuestaDelLider">Lo que contestó el administrador al atenderla, si ya lo hizo.</param>
/// <param name="SePuedeEliminar">
/// Lo decide el servidor con el estado de la sugerencia: solo mientras siga «Nueva». Una vez que el
/// líder empezó a atenderla, borrarla dejaría su respuesta colgando de algo que ya no existe.
/// </param>
public record MiSugerenciaDto(
    int Id,
    string Titulo,
    string Cuerpo,
    SuggestionCategory Categoria,
    string CategoriaEtiqueta,
    SuggestionStatus Estado,
    string EstadoEtiqueta,
    string Alcance,
    bool Anonima,
    bool SePuedeVotar,
    int Votos,
    string? RespuestaDelLider,
    DateTime CreadaUtc,
    DateTime? RevisadaUtc,
    bool SePuedeEliminar);

/// <summary>
/// Una propuesta del tablero del equipo: lo que todos ven y pueden apoyar.
///
/// <b>El anonimato se resuelve aquí, no en la pantalla.</b> Cuando la sugerencia es anónima,
/// <see cref="Autor"/> llega en null porque el servidor no lo pone — no porque el cliente lo
/// esconda. Mandarlo «para que la pantalla no lo pinte» sería regalarlo: cualquiera que mire la
/// respuesta de la API en su navegador leería el nombre igual, y entonces el anonimato de esta
/// aplicación no valdría nada.
/// </summary>
/// <param name="EsMia">
/// La escribió quien está mirando. Se calcula por petición, así que decírselo no delata a nadie:
/// solo su propio autor recibe un true, y él ya sabe lo que escribió.
/// </param>
/// <param name="SePuedeVotar">
/// Su autor la abrió a la votación del equipo. Apagado, la propuesta se lee pero no se vota: hay
/// cosas que no son un concurso de popularidad y que aun así conviene plantear.
/// </param>
public record PropuestaDelEquipoDto(
    int Id,
    string Titulo,
    string Cuerpo,
    SuggestionCategory Categoria,
    string CategoriaEtiqueta,
    SuggestionStatus Estado,
    string EstadoEtiqueta,
    string? Autor,
    bool Anonima,
    bool EsMia,
    bool SePuedeVotar,
    int Votos,
    bool YoVote,
    string? RespuestaDelLider,
    DateTime CreadaUtc);

/// <summary>
/// Todo lo que necesita la pantalla de sugerencias, en una sola respuesta: las mías, las del equipo y
/// los desplegables. Encadenar cuatro peticiones desde el navegador para pintar una pantalla la
/// vuelve lenta justo donde más se nota, al abrirla.
/// </summary>
public record SugerenciasDto(
    IReadOnlyList<MiSugerenciaDto> Mias,
    IReadOnlyList<PropuestaDelEquipoDto> DelEquipo,
    IReadOnlyList<OpcionDto> Categorias,
    IReadOnlyList<OpcionDto> Visibilidades);

/// <summary>
/// Enviar una sugerencia nueva.
/// </summary>
/// <param name="Anonima">
/// Que ni el administrador vea quién la mandó. La ficha sigue ligada por dentro para que su autor le
/// dé seguimiento y para poder avisarle cuando la contesten; lo que se oculta es el nombre.
/// </param>
/// <param name="AbiertaAVotacion">
/// Si el equipo puede apoyarla. El servidor la apaga por su cuenta cuando la sugerencia es solo para
/// el administrador: lo que nadie más ve no se puede votar, y dejar la bandera encendida daría a
/// entender que hay una votación en marcha que el equipo ni siquiera puede mirar.
/// </param>
public record NuevaSugerenciaRequest(
    SuggestionCategory Categoria,
    string? Titulo,
    string? Cuerpo,
    bool Anonima,
    SuggestionVisibility Visibilidad,
    bool AbiertaAVotacion);

/// <summary>
/// Cómo quedó el apoyo después de pulsarlo. El total lo cuenta el servidor sobre lo que hay en la
/// base: sumar uno en la pantalla enseñaría un número distinto del real en cuanto otra persona vote
/// a la vez.
/// </summary>
public record VotoDto(bool Votado, int Total);
