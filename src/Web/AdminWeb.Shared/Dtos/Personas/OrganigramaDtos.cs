using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Personas;

// ── El organigrama ───────────────────────────────────────────────────────────────
//
// Este es el ÚNICO contrato con el que se pinta la pantalla de equipos: la pestaña de siempre —donde
// se crea, se mueve y se asigna— y el diagrama nuevo leen exactamente lo mismo, y el PDF se maqueta
// desde la misma consulta del servidor.
//
// Es a propósito y no es ahorro de tráfico. Quién es el líder de un equipo se DEDUCE de dos datos
// que pueden discrepar (el rol marcado en la ficha y el LeadDeveloperId del equipo), así que cada
// sitio que resuelva esa regla por su cuenta es un sitio donde puede salir otro nombre. Con dos
// pestañas de la MISMA pantalla resolviéndola por separado, la contradicción se vería de un vistazo.

/// <summary>
/// Una persona en el organigrama.
/// </summary>
/// <param name="Nivel">El «Senior», «Junior»… de su ficha, si lo tiene.</param>
/// <param name="Rol">Su rol dentro del equipo. Al líder se le pone <see cref="TeamRole.Lider"/> aunque
/// su ficha diga otra cosa: si lo es porque el equipo le apunta como tal, el diagrama no puede
/// dibujarlo como «sin rol» y llamarlo líder dos centímetros más arriba.</param>
/// <param name="ColorDeRol">El hex del rol, que viene del escritorio. Es un dato del catálogo y no una
/// decisión de la pantalla; quien lo pinte decide dónde.</param>
/// <param name="Funcion">Qué hace dentro del equipo, en una frase. Opcional: la mayoría de las fichas
/// no la tendrán escrita el primer día y el diagrama no puede quedar cojo por eso.</param>
/// <param name="EsLider">Se dibuja distinto. Lo decide el servidor.</param>
public record PersonaDelOrganigramaDto(
    int Id,
    string Nombre,
    string? Nivel,
    TeamRole Rol,
    string RolTexto,
    string ColorDeRol,
    string? Funcion,
    bool EsLider);

/// <summary>
/// Un equipo con su gente, su descripción y su color.
/// </summary>
/// <param name="Descripcion">A qué se dedica el equipo. Opcional, igual que la función de cada
/// persona: un equipo recién creado no la tiene y el diagrama tiene que dibujarse igual.</param>
/// <param name="ColorHex">El color que alguien tecleó. <b>Puede venir vacío o mal escrito</b> —es una
/// caja de texto— y quien lo pinte tiene que contar con eso; ni el servidor ni el papel lo corrigen
/// por su cuenta, porque el dato es de quien lo escribió.</param>
/// <param name="EquipoPadreId">De qué equipo cuelga. Nulo = equipo raíz.</param>
/// <param name="Nivel">A qué altura cuelga: 0 los raíz, 1 sus subequipos, y así. Lo calcula el
/// servidor porque es una propiedad del ÁRBOL —hay que subir hasta la raíz para saberlo— y no del
/// equipo; con dos dibujantes contándolo cada uno por su cuenta, el papel podría sangrar una caja a
/// una altura distinta que la pantalla.</param>
/// <param name="Sistemas">Los sistemas que lleva el equipo, por nombre. Los NOMBRES y no un contador:
/// en el diagrama caben, y «3 sistemas» no le dice nada a quien lee el organigrama para saber a quién
/// preguntarle por uno.</param>
public record EquipoDelOrganigramaDto(
    int Id,
    string Nombre,
    string? Descripcion,
    string? ColorHex,
    string? Lider,
    int? EquipoPadreId,
    int Nivel,
    IReadOnlyList<PersonaDelOrganigramaDto> Integrantes,
    IReadOnlyList<string> Sistemas,
    IReadOnlyList<string> Proyectos);

/// <summary>
/// El organigrama completo.
/// </summary>
/// <param name="Equipos">Los equipos, PLANOS y en orden de dibujo: cada padre antes que su rama y los
/// hermanos por nombre. El árbol se reconstruye siguiendo <see cref="EquipoDelOrganigramaDto.EquipoPadreId"/>.
///
/// <para><b>Plano y no anidado, y esto es una decisión.</b> Hay DOS dibujantes y no reparten el sitio
/// igual: la pantalla dibuja el árbol de corrido en una superficie que se desplaza y se pliega, y el
/// PDF lo parte en hojas —una por rama— porque el papel no se desplaza pero sí pasa de hoja. Cada uno
/// recorre la estructura a su modo, así que un DTO anidado obligaría a los dos a rehacerla por su
/// cuenta —y con su propio orden—, que es donde el papel empieza a contradecir a la pantalla. Plano y
/// YA ORDENADO, los dos leen lo mismo en el mismo orden; el que necesite el árbol lo arma agrupando
/// por <see cref="EquipoDelOrganigramaDto.EquipoPadreId"/> sin tocar el orden de los hermanos, que es
/// lo que hace el PDF para repartir sus hojas.</para>
///
/// <para>Y hay una segunda razón, más práctica: anidar obligaría a que el DTO se refiriera a sí
/// mismo, y un dato mal grabado —un ciclo escrito a mano contra la base— dejaría de ser un dibujo
/// raro para convertirse en un serializador dando vueltas hasta que se acabe la memoria.</para></param>
/// <param name="SinEquipo">Quien no está en ningún equipo. Es una caja más del diagrama, no una nota
/// al pie: un organigrama que solo dibuja a quien tiene equipo miente por omisión.</param>
/// <param name="TotalPersonas">Todas las personas activas, con equipo y sin él. Se cuenta aquí para
/// que el nodo de arriba del diagrama y el del PDF digan el mismo número.</param>
/// <param name="GeneradoEl">Cuándo se armó, ya formateado en la zona del servidor. Un diagrama que se
/// reparte o se imprime sin fecha no se puede fechar después.</param>
public record OrganigramaDto(
    IReadOnlyList<EquipoDelOrganigramaDto> Equipos,
    IReadOnlyList<PersonaDelOrganigramaDto> SinEquipo,
    int TotalPersonas,
    string GeneradoEl);

/// <summary>
/// Anota (o borra, mandándola vacía) qué hace una persona dentro de su equipo.
///
/// Va aparte de <see cref="AsignarRolRequest"/> aunque se capturen en el mismo renglón: el rol es una
/// opción de un catálogo cerrado y se guarda en cuanto se elige; la función es texto que se escribe y
/// se guarda cuando quien lo escribe termina. Meterlos en la misma petición obligaría a mandar el rol
/// en cada tecleo o a perder lo escrito al cambiar el rol.
/// </summary>
public record GuardarFuncionRequest(int DeveloperId, string? Funcion);
