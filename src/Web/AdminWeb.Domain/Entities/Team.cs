namespace AdminWeb.Domain.Entities;

/// <summary>
/// Equipo de desarrollo. Cada desarrollador pertenece a lo sumo a un equipo
/// (Developer.TeamId); el tablero de organización mueve desarrolladores entre equipos.
///
/// <para>Los equipos forman un ÁRBOL: <see cref="EquipoPadreId"/> en nulo es un equipo raíz y
/// cualquier otro valor lo cuelga de otro equipo. Quien recorra esa estructura no debería hacerlo a
/// mano: <c>JerarquiaDeEquipos</c> resuelve el subárbol, los ancestros y el orden de dibujo una sola
/// vez y para todos.</para>
/// </summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Color del equipo en formato hex (ej. "#2563EB"), para el tablero y el PDF.</summary>
    public string? ColorHex { get; set; }
    public int? LeadDeveloperId { get; set; }

    /// <summary>
    /// De qué equipo cuelga este. <b>Nulo = equipo raíz</b>, y ese es el caso normal: hasta que
    /// alguien arme subequipos, todos los equipos son raíces y se comportan igual que siempre.
    ///
    /// <para><b>Ser líder de un subequipo NO da ninguna función extra.</b> Aquí quien decide qué
    /// puede hacer cada quien es el ROL de la cuenta (<c>UserRole</c>), y el equipo nunca ha
    /// decidido permisos: <see cref="LeadDeveloperId"/> es una etiqueta para dibujar el organigrama
    /// y ordenar a la gente dentro de su caja. Esta columna no cambia eso, y no debe cambiarlo: si
    /// mañana ser líder diera poderes, el líder de un subequipo —que sigue siendo un
    /// desarrollador— los ganaría de rebote, que es justo lo contrario de lo que se pidió.</para>
    ///
    /// <para>Lo que la jerarquía sí propaga es donde el equipo YA se usaba: agrupar el organigrama,
    /// sumar puntos por rama y marcar «es de mi equipo».</para>
    /// </summary>
    public int? EquipoPadreId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Developer? Lead { get; set; }
    public ICollection<Developer> Members { get; set; } = new List<Developer>();

    public Team? EquipoPadre { get; set; }
    /// <summary>Los equipos que cuelgan directamente de este. Solo los HIJOS, no toda la rama.</summary>
    public ICollection<Team> Subequipos { get; set; } = new List<Team>();
}
