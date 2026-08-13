using AdminWeb.Client.Organigrama;
using AdminWeb.Shared.Dtos.Personas;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// EL ORGANIGRAMA DE LA PANTALLA: el trazado del árbol y las soltadas que se pueden aceptar.
///
/// <para>Las dos mitades de lo que se puede comprobar sin abrir un navegador. La primera es el
/// DIBUJO: dónde cae cada caja y qué líneas la unen a su rama, que es lo que convierte una lista de
/// equipos en un organigrama y lo que deja de ser cierto en cuanto alguien pliega una rama o el
/// árbol se hace hondo. La segunda es la SOLTADA: qué cajas pueden recibir lo que se arrastra, que
/// es la comprobación que le ahorra a la gente soltar algo para que el servidor le conteste que no.</para>
///
/// <para>Viven en el proyecto de la API y no en el de Application por lo mismo que
/// <c>RecorridosGuiadosTests</c>: es el único de los dos que ve <c>AdminWeb.Client</c>, porque la API
/// sirve el cliente y por eso lo referencia.</para>
///
/// <para><b>Lo que estas pruebas NO alcanzan</b>, para que no se confunda con lo que sí: el gesto en
/// sí —que el navegador acepte la soltada, que la caja se resalte y que al soltar se llame a
/// <c>MoverPersonasAsync</c>— vive dentro del componente y solo se ve abriendo la pantalla. Lo que se
/// puede aislar está aislado aquí a propósito, y es justo lo que se rompe en silencio.</para>
/// </summary>
public class OrganigramaEnPantallaTests
{
    // ── El trazado ───────────────────────────────────────────────────────────
    //
    // Las cajas entran como (identificador, nivel) y en orden de dibujo, que es como las manda el
    // servidor. La caja de arriba —la de la organización, que en la pantalla es -1— va la primera y
    // en el nivel 0; los equipos cuelgan de ella, así que empiezan en el 1.

    private static IReadOnlyList<CajaTrazada> Trazar(params (int Id, int Nivel)[] cajas) =>
        TrazadoDelOrganigrama.Trazar([.. cajas.Select(c => new CajaPorTrazar(c.Id, c.Nivel))], new HashSet<int>());

    private static IReadOnlyList<CajaTrazada> TrazarPlegando(int[] plegados, params (int Id, int Nivel)[] cajas) =>
        TrazadoDelOrganigrama.Trazar([.. cajas.Select(c => new CajaPorTrazar(c.Id, c.Nivel))], new HashSet<int>(plegados));

    private static CajaTrazada Caja(IReadOnlyList<CajaTrazada> trazado, int id) =>
        trazado.Single(c => c.Id == id);

    [Fact]
    public void UN_ARBOL_DE_VARIOS_NIVELES_lleva_cada_linea_donde_toca()
    {
        // Organización
        // ├─ Web ──────── (1)
        // │  ├─ Front ─── (2)
        // │  │  └─ UX ─── (3)
        // │  └─ Back ──── (4)
        // ├─ Datos ────── (5)
        // └─ Sin equipo — (0)
        var trazado = Trazar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1), (0, 1));

        Assert.Empty(Caja(trazado, -1).Guias);

        // «Web» tiene a «Datos» debajo, así que su codo sigue bajando.
        Assert.Equal(new[] { GuiaDelArbol.Codo }, Caja(trazado, 1).Guias);

        // «Front» va sangrado una vez: por su columna de fuera pasa la vertical de «Web», que aún
        // tiene hermanos debajo, y su propio codo sigue porque después viene «Back».
        Assert.Equal(new[] { GuiaDelArbol.Linea, GuiaDelArbol.Codo }, Caja(trazado, 2).Guias);

        // «UX» cuelga de «Front» y es lo último de esa rama: dos verticales de paso y un codo final.
        Assert.Equal(new[] { GuiaDelArbol.Linea, GuiaDelArbol.Linea, GuiaDelArbol.CodoFinal }, Caja(trazado, 3).Guias);

        // «Back» cierra la rama de «Web», pero la de «Web» sigue: la vertical de fuera se mantiene.
        Assert.Equal(new[] { GuiaDelArbol.Linea, GuiaDelArbol.CodoFinal }, Caja(trazado, 4).Guias);

        Assert.Equal(new[] { GuiaDelArbol.Codo }, Caja(trazado, 5).Guias);
        Assert.Equal(new[] { GuiaDelArbol.CodoFinal }, Caja(trazado, 0).Guias);
    }

    [Fact]
    public void TIENE_RAMA_solo_quien_lleva_algo_colgando()
    {
        var trazado = Trazar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1), (0, 1));

        Assert.True(Caja(trazado, -1).TieneRama);
        Assert.True(Caja(trazado, 1).TieneRama);
        Assert.True(Caja(trazado, 2).TieneRama);
        Assert.False(Caja(trazado, 3).TieneRama);
        Assert.False(Caja(trazado, 4).TieneRama);
        Assert.False(Caja(trazado, 5).TieneRama);
        Assert.False(Caja(trazado, 0).TieneRama);
    }

    [Fact]
    public void HONDO_Y_ESTRECHO_cada_nivel_cuesta_una_sangria_y_ninguna_vertical()
    {
        // Una cadena de cinco: es el caso que en un dibujo de cajas colgando en horizontal deja la
        // pantalla vacía. Aquí son cinco renglones seguidos, cada uno una sangría más adentro.
        var trazado = Trazar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 4), (5, 5));

        Assert.Equal(6, trazado.Count);
        for (int nivel = 1; nivel <= 5; nivel++)
        {
            var caja = Caja(trazado, nivel);
            Assert.Equal(nivel, caja.Nivel);
            Assert.Equal(nivel, caja.Guias.Count);

            // Nadie tiene hermanos: por las columnas de fuera no pasa ninguna vertical y la de dentro
            // es un codo final.
            Assert.All(caja.Guias.Take(nivel - 1), g => Assert.Equal(GuiaDelArbol.Ninguna, g));
            Assert.Equal(GuiaDelArbol.CodoFinal, caja.Guias[^1]);
        }
    }

    [Fact]
    public void ANCHO_Y_PLANO_todos_al_mismo_nivel_y_solo_el_ultimo_cierra()
    {
        // Veinte equipos sin padre, que es la foto de hoy. Ninguno sangra más que otro y el diagrama
        // crece hacia abajo: no hay nada que quepa o deje de caber a lo ancho.
        var cajas = new List<(int, int)> { (-1, 0) };
        for (int id = 1; id <= 20; id++) cajas.Add((id, 1));

        var trazado = Trazar([.. cajas]);

        Assert.Equal(21, trazado.Count);
        for (int id = 1; id < 20; id++) Assert.Equal(new[] { GuiaDelArbol.Codo }, Caja(trazado, id).Guias);
        Assert.Equal(new[] { GuiaDelArbol.CodoFinal }, Caja(trazado, 20).Guias);
        Assert.All(trazado.Where(c => c.Id > 0), c => Assert.False(c.TieneRama));
    }

    [Fact]
    public void PLEGAR_esconde_la_rama_ENTERA_y_dice_cuanta()
    {
        // Plegar «Web» (1) esconde a sus dos hijos y al nieto: tres cajas, no una.
        var trazado = TrazarPlegando([1], (-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1));

        Assert.Equal(new[] { -1, 1, 5 }, trazado.Select(c => c.Id));

        var web = Caja(trazado, 1);
        Assert.True(web.Plegada);
        Assert.True(web.TieneRama);
        Assert.Equal(3, web.Escondidos);
    }

    [Fact]
    public void PLEGAR_recalcula_las_lineas_con_lo_que_QUEDA_a_la_vista()
    {
        // Sin plegar, «Front» (2) tiene a «Back» (3) debajo y su codo sigue bajando. Plegando a
        // «Web» (1), los dos desaparecen: si las líneas se calcularan con la lista completa, «Web»
        // seguiría dibujando un codo que baja hacia una caja que ya no está.
        var conTodo = Trazar((-1, 0), (1, 1), (2, 2), (3, 2));
        // La columna de fuera de «Front» va vacía y no con una vertical: «Web» es lo último que
        // cuelga de la organización, así que por ahí ya no baja ninguna línea.
        Assert.Equal(new[] { GuiaDelArbol.Ninguna, GuiaDelArbol.Codo }, Caja(conTodo, 2).Guias);
        Assert.Equal(new[] { GuiaDelArbol.CodoFinal }, Caja(conTodo, 1).Guias);

        var plegado = TrazarPlegando([1], (-1, 0), (1, 1), (2, 2), (3, 2));
        Assert.Equal(new[] { -1, 1 }, plegado.Select(c => c.Id));
        Assert.Equal(new[] { GuiaDelArbol.CodoFinal }, Caja(plegado, 1).Guias);
        Assert.Equal(2, Caja(plegado, 1).Escondidos);
    }

    [Fact]
    public void PLEGAR_UNA_HOJA_no_esconde_a_nadie_ni_a_sus_hermanos()
    {
        var trazado = TrazarPlegando([2], (-1, 0), (1, 1), (2, 2), (3, 2));

        Assert.Equal(new[] { -1, 1, 2, 3 }, trazado.Select(c => c.Id));
        Assert.Equal(0, Caja(trazado, 2).Escondidos);
        Assert.True(Caja(trazado, 2).Plegada);   // la caja se pliega igual: esconde a su gente
    }

    [Fact]
    public void SIN_PLEGAR_NADA_no_se_pierde_ninguna_caja_ni_cambia_el_orden()
    {
        var entradas = new (int, int)[] { (-1, 0), (7, 1), (3, 2), (9, 1), (0, 1) };
        var trazado = Trazar(entradas);

        Assert.Equal(entradas.Select(e => e.Item1), trazado.Select(c => c.Id));
        Assert.Equal(entradas.Select(e => e.Item2), trazado.Select(c => c.Nivel));
    }

    [Fact]
    public void UN_NIVEL_IMPOSIBLE_no_descoloca_el_dibujo_ni_revienta()
    {
        // Un salto de tres alturas de golpe no lo puede producir el servidor; sí un círculo escrito a
        // mano contra la base. Se recorta al siguiente nivel posible: el dibujo sale raro —que es lo
        // que hay que ver— pero ninguna caja se queda sin sus columnas ni desaparece.
        var trazado = Trazar((-1, 0), (1, 4), (2, 9), (3, 1));

        Assert.Equal(4, trazado.Count);
        Assert.Equal(1, Caja(trazado, 1).Nivel);
        Assert.Equal(2, Caja(trazado, 2).Nivel);
        Assert.All(trazado, c => Assert.Equal(c.Nivel, c.Guias.Count));
    }

    [Fact]
    public void LA_PRIMERA_CAJA_siempre_arranca_arriba_del_todo()
    {
        // Aunque llegue con nivel propio: si empezara sangrada, sus columnas apuntarían a un padre
        // que no está en la lista.
        var trazado = Trazar((5, 3), (6, 4));

        Assert.Equal(0, trazado[0].Nivel);
        Assert.Empty(trazado[0].Guias);
        Assert.Equal(1, trazado[1].Nivel);
    }

    [Fact]
    public void SIN_CAJAS_no_hay_trazado()
    {
        Assert.Empty(TrazadoDelOrganigrama.Trazar([], new HashSet<int>()));
    }

    // ── La soltada de un equipo ──────────────────────────────────────────────

    private static EquipoDelOrganigramaDto Equipo(int id, int? padre, int nivel) =>
        new(id, $"Equipo {id}", null, null, null, padre, nivel, [], [], []);

    /// <summary>El árbol de las pruebas: 1 → 2 → 3 → 4 por un lado y 5 → 6 por otro.</summary>
    private static readonly EquipoDelOrganigramaDto[] Arbol =
    [
        Equipo(1, null, 0), Equipo(2, 1, 1), Equipo(3, 2, 2), Equipo(4, 3, 3),
        Equipo(5, null, 0), Equipo(6, 5, 1)
    ];

    [Fact]
    public void UN_EQUIPO_NO_SE_CUELGA_DE_SI_MISMO()
    {
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 2, 2));
    }

    [Fact]
    public void UN_EQUIPO_NO_SE_CUELGA_DE_SU_PROPIO_SUBEQUIPO()
    {
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 1, 2));   // hijo
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 1, 3));   // nieto
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 1, 4));   // bisnieto
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 2, 4));   // desde el medio de la cadena
    }

    [Fact]
    public void SOLTARLO_DONDE_YA_CUELGA_no_es_un_movimiento()
    {
        // Ni el hijo sobre su padre, ni un equipo raíz sobre la caja de arriba: no habría nada que
        // guardar, y una escritura que no cambia nada deja su línea en la bitácora igual que las demás.
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 2, 1));
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 1, null));
    }

    [Fact]
    public void COLGARLO_DE_OTRA_RAMA_O_DEJARLO_RAIZ_si_se_puede()
    {
        Assert.True(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 2, 5));    // se lleva su rama con él
        Assert.True(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 6, 3));
        Assert.True(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 4, null)); // dejar de colgar de nadie
    }

    [Fact]
    public void UN_EQUIPO_QUE_YA_NO_ESTA_no_se_cuelga_de_ningun_sitio()
    {
        // La pantalla se quedó vieja: alguien borró el equipo mientras esta lo tenía dibujado.
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 99, 1));
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(Arbol, 99, null));
    }

    [Fact]
    public void UN_CIRCULO_EN_LOS_DATOS_no_deja_la_pantalla_dando_vueltas()
    {
        // Solo puede entrar escribiéndolo a mano contra la base, y lo que se lleva el golpe es esto:
        // sin conjunto de visitados, la comprobación no terminaría nunca y el navegador se quedaría
        // colgado sin ningún error que leer. Que conteste lo que conteste, pero que conteste.
        EquipoDelOrganigramaDto[] circulo = [Equipo(1, 2, 0), Equipo(2, 1, 0), Equipo(3, null, 0)];

        // Que conteste, y que conteste algo defendible: el 3 no está metido en el círculo, así que
        // colgarlo del 1 no lo empeora; y el 1 ya cuelga del 2, así que ahí no hay nada que guardar.
        Assert.True(TrazadoDelOrganigrama.SePuedeColgar(circulo, 3, 1));
        Assert.False(TrazadoDelOrganigrama.SePuedeColgar(circulo, 1, 2));
    }

    // ── La soltada de una persona ────────────────────────────────────────────

    [Fact]
    public void A_UNA_PERSONA_SE_LA_MUEVE_solo_a_donde_no_esta()
    {
        Assert.True(TrazadoDelOrganigrama.SePuedeMover(null, 3));    // de «sin equipo» a un equipo
        Assert.True(TrazadoDelOrganigrama.SePuedeMover(3, null));    // fuera de su equipo
        Assert.True(TrazadoDelOrganigrama.SePuedeMover(3, 4));       // de un equipo a otro
        Assert.False(TrazadoDelOrganigrama.SePuedeMover(3, 3));      // ya está ahí
        Assert.False(TrazadoDelOrganigrama.SePuedeMover(null, null));// ya no tiene equipo
    }
}
