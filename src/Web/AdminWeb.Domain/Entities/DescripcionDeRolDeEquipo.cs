using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Qué hace, EN ESTE EQUIPO, quien tiene este rol.
///
/// <para><b>Para qué existe.</b> La función de cada persona se escribía a mano, una por una, y en la
/// práctica se tecleaba la misma frase tantas veces como gente hubiera con ese rol: en el organigrama
/// de la casa, los tres «Fullstack» de un subequipo decían exactamente lo mismo palabra por palabra,
/// y los tres del subequipo de al lado decían otra cosa, también repetida tres veces. Eso no es
/// información por persona: es la definición del puesto en ese equipo, copiada.</para>
///
/// <para><b>Por equipo Y por rol, no una sola por rol.</b> Es lo que se pidió, y tiene sentido: un
/// «Fullstack» del equipo de soporte no hace lo mismo que uno del de desarrollo, y una única frase
/// para toda la casa habría obligado a seguir escribiendo a mano la de cada subequipo — o sea, a no
/// arreglar nada.</para>
///
/// <para><b>No se copia a la ficha de nadie.</b> Se resuelve AL LEER: si la persona tiene función
/// propia escrita manda la suya, y si no, se enseña ésta. Copiarla al asignar el rol parecía más
/// simple y es peor: la función propia SE BORRA al mover a alguien de equipo y el rol NO, así que
/// nadie vuelve a pasar por la asignación de rol y el organigrama se quedaría mudo justo después de
/// una reorganización, que es cuando más se mira.</para>
///
/// <para>Sin clave foránea a nada más que al equipo: es texto de catálogo, no un dato de una persona.
/// Al borrar el equipo se van sus descripciones con él, que es lo correcto — describen un puesto
/// dentro de ese equipo y fuera de él no significan nada.</para>
/// </summary>
public class DescripcionDeRolDeEquipo
{
    public int Id { get; set; }

    /// <summary>De qué equipo. Cae en cascada con él.</summary>
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>
    /// Qué rol se describe.
    ///
    /// <para>Se guarda el ENTERO del enum, como en todas las demás tablas. «Líder de subequipo» no
    /// aparece por ningún lado y no es un olvido: es una etiqueta derivada de dónde cuelga el equipo,
    /// no un rol, así que la descripción del líder de un subequipo se guarda con
    /// <see cref="TeamRole.Lider"/> igual que la de cualquier otro.</para>
    /// </summary>
    public TeamRole Rol { get; set; }

    /// <summary>La frase. Nunca vacía: una descripción en blanco se borra, no se guarda.</summary>
    public string Descripcion { get; set; } = "";
}
