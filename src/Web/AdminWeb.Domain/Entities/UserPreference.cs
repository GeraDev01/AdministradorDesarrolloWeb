namespace AdminWeb.Domain.Entities;

/// <summary>
/// Una preferencia de interfaz de una persona: qué columnas oculta en una rejilla, qué filtro deja
/// puesto, cosas así.
///
/// En el escritorio esto vivía en un archivo por máquina, con la consecuencia de que quien cambiaba
/// de computadora perdía su configuración y volvía a empezar. En una web se espera lo contrario: la
/// preferencia sigue a la persona. Por eso se guarda aquí y no en el navegador.
///
/// Se conserva una decisión del escritorio que no es obvia: se guarda lo que está OCULTO, no lo
/// visible. Así, cuando se agregue una columna nueva a una rejilla, aparece para todo el mundo en
/// lugar de quedar escondida para quien ya tenía preferencias guardadas.
/// </summary>
public class UserPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Qué se está configurando (por ejemplo la rejilla de una pantalla concreta).</summary>
    public string Clave { get; set; } = "";

    /// <summary>El valor, en JSON. Lo interpreta quien lo escribió; aquí solo se guarda.</summary>
    public string Json { get; set; } = "";

    public User? User { get; set; }
}
