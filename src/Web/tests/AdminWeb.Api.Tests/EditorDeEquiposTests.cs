using AdminWeb.Client.Servicios;
using AdminWeb.Shared.Dtos.Personas;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// EL DESPLEGABLE «CUELGA DE» del formulario de equipos: a quién se le puede ofrecer como padre.
///
/// <para>La regla de verdad —que un equipo no acabe colgando de sí mismo— está en el servidor y se
/// prueba allá (<c>SubequiposTests</c>), porque a esa dirección se la puede llamar sin pasar por la
/// pantalla. Lo que se comprueba AQUÍ es lo otro: que la pantalla no ofrezca una opción que el
/// servidor va a rechazar. Un desplegable que enseña puertas cerradas no rompe nada —el guardado
/// falla con su motivo—, pero convierte cada intento en una adivinanza.</para>
///
/// <para>Estas pruebas viven en el proyecto de la API porque es el único de los dos que ve
/// <c>AdminWeb.Client</c>, igual que las de los recorridos guiados. Y el filtro está en una clase
/// suelta, y no dentro del <c>@code</c> de la pantalla, justamente para poder llamarlo desde aquí:
/// «ni él ni sus nietos» es la clase de regla que se rompe sin que se note al mirar la pantalla.</para>
/// </summary>
public class EditorDeEquiposTests
{
    /// <summary>Un equipo del organigrama con lo único que este filtro mira: quién es, de quién cuelga y a qué altura.</summary>
    private static EquipoDelOrganigramaDto Equipo(int id, string nombre, int? padre = null, int nivel = 0) =>
        new(id, nombre, null, null, null, padre, nivel, [], [], []);

    /// <summary>
    /// Una cadena: 1 → 2 → 3 → 4, tal como la manda el servidor (cada padre delante de su rama y con
    /// su nivel ya calculado).
    /// </summary>
    private static List<EquipoDelOrganigramaDto> Cadena() =>
    [
        Equipo(1, "Uno"),
        Equipo(2, "Dos", padre: 1, nivel: 1),
        Equipo(3, "Tres", padre: 2, nivel: 2),
        Equipo(4, "Cuatro", padre: 3, nivel: 3)
    ];

    private static int[] Ofrecidos(IReadOnlyList<EquipoDelOrganigramaDto> equipos, int? enEdicion) =>
        [.. ArbolDeEquipos.PosiblesPadres(equipos, enEdicion).Select(o => o.Id)];

    private static string[] Etiquetas(IReadOnlyList<EquipoDelOrganigramaDto> equipos, int? enEdicion) =>
        [.. ArbolDeEquipos.PosiblesPadres(equipos, enEdicion).Select(o => o.Etiqueta)];

    [Fact]
    public void UN_EQUIPO_NO_SE_OFRECE_A_SI_MISMO_NI_OFRECE_SU_RAMA()
    {
        // El hijo se ve a la primera; el bisnieto es el que se cuela cuando alguien comprueba solo el
        // padre directo. En esta cadena todo cuelga del 1, así que no queda nada que ofrecerle.
        Assert.Empty(Ofrecidos(Cadena(), enEdicion: 1));
    }

    [Fact]
    public void DESDE_EL_MEDIO_DE_LA_CADENA_solo_se_ofrece_lo_que_esta_ENCIMA()
    {
        // Editando el 2: el 1 está encima y vale; el 3 y el 4 cuelgan de él y no.
        int[] soloElDeArriba = [1];

        Assert.Equal(soloElDeArriba, Ofrecidos(Cadena(), enEdicion: 2));
    }

    [Fact]
    public void LOS_HERMANOS_Y_LAS_OTRAS_RAMAS_SI_SE_OFRECEN()
    {
        // Un subequipo puede pasarse a otra rama: eso no es un círculo, es exactamente lo que se
        // viene a hacer a esta pantalla.
        List<EquipoDelOrganigramaDto> arbol =
        [
            Equipo(1, "Web"),
            Equipo(2, "Front", padre: 1, nivel: 1),
            Equipo(3, "Back", padre: 1, nivel: 1),
            Equipo(4, "Datos")
        ];
        int[] todosMenosElMismo = [1, 3, 4];

        Assert.Equal(todosMenosElMismo, Ofrecidos(arbol, enEdicion: 2));
    }

    [Fact]
    public void UN_EQUIPO_NUEVO_PUEDE_COLGAR_DE_CUALQUIERA()
    {
        // Todavía no existe, así que nada cuelga de él y no hay ningún círculo posible.
        int[] todos = [1, 2, 3, 4];

        Assert.Equal(todos, Ofrecidos(Cadena(), enEdicion: null));
    }

    [Fact]
    public void SIN_JERARQUIA_SE_OFRECEN_TODOS_LOS_DEMAS_Y_SIN_SANGRIA()
    {
        // La foto de hoy: todos los equipos son raíz. Ni una sangría, y la lista es la de siempre
        // menos el que se está editando.
        List<EquipoDelOrganigramaDto> planos = [Equipo(1, "Alfa"), Equipo(2, "Beta"), Equipo(3, "Gama")];
        string[] losOtrosDos = ["Alfa", "Gama"];

        Assert.Equal(losOtrosDos, Etiquetas(planos, enEdicion: 2));
    }

    [Fact]
    public void LA_ETIQUETA_SANGRA_UNA_VEZ_POR_NIVEL()
    {
        // Sin esto la lista sale plana y no hay forma de ver que el equipo que estás a punto de
        // elegir ya cuelga de otro — o sea, que estás metiendo un tercer nivel sin querer.
        string[] conSuSangria = ["Uno", "· Dos", "· · Tres", "· · · Cuatro"];

        Assert.Equal(conSuSangria, Etiquetas(Cadena(), enEdicion: null));
    }

    [Fact]
    public void QUITAR_UNA_RAMA_NO_DESCOLOCA_LAS_SANGRIAS_DE_LAS_DEMAS()
    {
        // Lo que se quita es un bloque contiguo, y lo que queda sigue siendo un árbol completo: si un
        // equipo no cuelga del que se edita, su padre tampoco, así que ninguna sangría se queda sin
        // la caja de la que cuelga.
        List<EquipoDelOrganigramaDto> arbol =
        [
            Equipo(1, "Web"),
            Equipo(2, "Front", padre: 1, nivel: 1),
            Equipo(3, "Front QA", padre: 2, nivel: 2),
            Equipo(4, "Zeta")
        ];
        string[] loQueQueda = ["Web", "Zeta"];

        Assert.Equal(loQueQueda, Etiquetas(arbol, enEdicion: 2));
    }

    [Fact]
    public void UN_CIRCULO_ESCRITO_A_MANO_NO_DEJA_LA_PANTALLA_DANDO_VUELTAS()
    {
        // Esta aplicación no puede crear un ciclo —el servidor lo rechaza—, pero se puede escribir a
        // mano contra la base. Si llega, la lista podrá salir rara; lo que no puede es no salir: sin
        // el conjunto de visitados, el navegador se queda colgado sin ningún error que leer.
        List<EquipoDelOrganigramaDto> circulo =
        [
            Equipo(1, "Uno", padre: 2),
            Equipo(2, "Dos", padre: 1)
        ];

        Assert.Equal(2, ArbolDeEquipos.PosiblesPadres(circulo, equipoEnEdicion: 3).Count);
    }

    [Fact]
    public void UN_PADRE_QUE_YA_NO_ESTA_EN_LA_LISTA_NO_CORTA_EL_RECORRIDO()
    {
        // El servidor ya devuelve como raíz al que apunta a un equipo que no existe, pero si por lo
        // que sea llegara el identificador crudo, subir por los padres tiene que terminar igual.
        List<EquipoDelOrganigramaDto> huerfano = [Equipo(1, "Solo", padre: 77), Equipo(2, "Otro")];
        int[] elHuerfano = [1];

        Assert.Equal(elHuerfano, Ofrecidos(huerfano, enEdicion: 2));
    }
}
