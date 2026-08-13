using AdminWeb.Client.Organigrama;
using AdminWeb.Shared.Dtos.Personas;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// EL ORGANIGRAMA DE LA PANTALLA: el árbol que se dibuja y las soltadas que se pueden aceptar.
///
/// <para>Las dos mitades de lo que se puede comprobar sin abrir un navegador. La primera es el
/// DIBUJO: de qué caja cuelga cada caja, que es lo que convierte una lista de equipos en un
/// organigrama y lo que deja de ser cierto en cuanto alguien pliega una rama o el árbol se hace
/// hondo. La segunda es la SOLTADA: qué cajas pueden recibir lo que se arrastra, que es la
/// comprobación que le ahorra a la gente soltar algo para que el servidor le conteste que no.</para>
///
/// <para>Viven en el proyecto de la API y no en el de Application por lo mismo que
/// <c>RecorridosGuiadosTests</c>: es el único de los dos que ve <c>AdminWeb.Client</c>, porque la API
/// sirve el cliente y por eso lo referencia.</para>
///
/// <para><b>Lo que estas pruebas NO alcanzan</b>, para que no se confunda con lo que sí: el gesto en
/// sí —que el navegador acepte la soltada, que la caja se resalte y que al soltar se llame a
/// <c>MoverPersonasAsync</c>— vive dentro del componente y solo se ve abriendo la pantalla. Tampoco
/// alcanzan las LÍNEAS que unen las cajas: en un organigrama clásico no las calcula nadie, las pinta
/// la hoja de estilo con <c>:first-child</c> y <c>:last-child</c> sobre el árbol anidado. Lo que se
/// puede aislar está aislado aquí a propósito, y es justo lo que se rompe en silencio.</para>
/// </summary>
public class OrganigramaEnPantallaTests
{
    // ── El árbol ─────────────────────────────────────────────────────────────
    //
    // Las cajas entran como (identificador, nivel) y en orden de dibujo, que es como las manda el
    // servidor. La caja de arriba —la de la organización, que en la pantalla es -1— va la primera y
    // en el nivel 0; los equipos cuelgan de ella, así que empiezan en el 1. Lo que sale es un árbol:
    // cada nodo con su rama dentro, que es lo que la pantalla recorre para dibujar el organigrama.

    private static IReadOnlyList<NodoDelOrganigrama> Armar(params (int Id, int Nivel)[] cajas) =>
        TrazadoDelOrganigrama.Armar([.. cajas.Select(c => new CajaPorTrazar(c.Id, c.Nivel))], new HashSet<int>());

    private static IReadOnlyList<NodoDelOrganigrama> ArmarPlegando(int[] plegados, params (int Id, int Nivel)[] cajas) =>
        TrazadoDelOrganigrama.Armar([.. cajas.Select(c => new CajaPorTrazar(c.Id, c.Nivel))], new HashSet<int>(plegados));

    private static NodoDelOrganigrama Caja(IReadOnlyList<NodoDelOrganigrama> arbol, int id) =>
        TrazadoDelOrganigrama.Recorrer(arbol).Single(c => c.Id == id);

    /// <summary>Los identificadores de los hijos DIRECTOS, en el orden en que se van a dibujar.</summary>
    private static int[] Hijos(IReadOnlyList<NodoDelOrganigrama> arbol, int id) =>
        [.. Caja(arbol, id).Hijos.Select(h => h.Id)];

    [Fact]
    public void UN_ARBOL_DE_VARIOS_NIVELES_cuelga_cada_caja_de_la_suya()
    {
        //          Organización
        //        ┌──────┴──────┬─────────────┐
        //      Web(1)       Datos(5)   Sin equipo(0)
        //     ┌───┴───┐
        //  Front(2) Back(4)
        //     │
        //   UX(3)
        var arbol = Armar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1), (0, 1));

        // Una sola raíz: la caja de arriba. Todo lo demás cuelga de ella, y por eso el dibujo tiene
        // una única cabeza aunque los equipos no tengan padre entre ellos.
        Assert.Equal(new[] { -1 }, arbol.Select(n => n.Id));

        Assert.Equal(new[] { 1, 5, 0 }, Hijos(arbol, -1));
        Assert.Equal(new[] { 2, 4 }, Hijos(arbol, 1));
        Assert.Equal(new[] { 3 }, Hijos(arbol, 2));
        Assert.Empty(Caja(arbol, 3).Hijos);
        Assert.Empty(Caja(arbol, 4).Hijos);
        Assert.Empty(Caja(arbol, 5).Hijos);
    }

    [Fact]
    public void LOS_HERMANOS_CONSERVAN_SU_ORDEN()
    {
        // El árbol se arma recorriendo la lista AL REVÉS —un nodo necesita a sus hijos ya hechos para
        // nacer—, así que el orden de los hermanos es justo lo que se puede invertir sin que nada más
        // se note. Y el orden importa: el servidor los manda alfabéticos y la pantalla los pinta de
        // izquierda a derecha tal cual.
        var arbol = Armar((-1, 0), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Hijos(arbol, -1));
    }

    [Fact]
    public void TIENE_RAMA_solo_quien_lleva_algo_colgando()
    {
        var arbol = Armar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1), (0, 1));

        Assert.True(Caja(arbol, -1).TieneRama);
        Assert.True(Caja(arbol, 1).TieneRama);
        Assert.True(Caja(arbol, 2).TieneRama);
        Assert.False(Caja(arbol, 3).TieneRama);
        Assert.False(Caja(arbol, 4).TieneRama);
        Assert.False(Caja(arbol, 5).TieneRama);
        Assert.False(Caja(arbol, 0).TieneRama);
    }

    [Fact]
    public void HONDO_Y_ESTRECHO_una_cadena_de_cinco_baja_de_uno_en_uno()
    {
        var arbol = Armar((-1, 0), (1, 1), (2, 2), (3, 3), (4, 4), (5, 5));

        Assert.Equal(6, TrazadoDelOrganigrama.Recorrer(arbol).Count());
        for (int id = 1; id <= 4; id++)
        {
            Assert.Equal(id, Caja(arbol, id).Nivel);
            Assert.Equal(new[] { id + 1 }, Hijos(arbol, id));
        }
        Assert.Empty(Caja(arbol, 5).Hijos);
    }

    [Fact]
    public void ANCHO_Y_PLANO_veinte_equipos_cuelgan_todos_de_arriba()
    {
        // Veinte equipos sin padre, que es la foto de hoy: veinte cajas en una fila. Es el caso que
        // en un organigrama clásico obliga a desplazar el lienzo de lado, y el que las dos
        // herramientas de la pantalla —el tamaño y el plegado— están para hacer manejable.
        var cajas = new List<(int, int)> { (-1, 0) };
        for (int id = 1; id <= 20; id++) cajas.Add((id, 1));

        var arbol = Armar([.. cajas]);

        Assert.Equal(21, TrazadoDelOrganigrama.Recorrer(arbol).Count());
        Assert.Equal(Enumerable.Range(1, 20), Hijos(arbol, -1));
        Assert.All(TrazadoDelOrganigrama.Recorrer(arbol).Where(c => c.Id > 0), c => Assert.False(c.TieneRama));
    }

    [Fact]
    public void PLEGAR_esconde_la_rama_ENTERA_y_dice_cuanta()
    {
        // Plegar «Web» (1) esconde a sus dos hijos y al nieto: tres cajas, no una.
        var arbol = ArmarPlegando([1], (-1, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1));

        Assert.Equal(new[] { -1, 1, 5 }, TrazadoDelOrganigrama.Recorrer(arbol).Select(c => c.Id));

        var web = Caja(arbol, 1);
        Assert.True(web.Plegada);
        Assert.Empty(web.Hijos);
        Assert.True(web.TieneRama);     // sigue teniendo rama: es lo único que la deja desplegarse
        Assert.Equal(3, web.Escondidos);
    }

    [Fact]
    public void PLEGAR_UNA_RAMA_no_toca_a_las_de_al_lado()
    {
        var arbol = ArmarPlegando([1], (-1, 0), (1, 1), (2, 2), (3, 2), (5, 1), (6, 2));

        Assert.Equal(new[] { 1, 5 }, Hijos(arbol, -1));
        Assert.Equal(2, Caja(arbol, 1).Escondidos);
        Assert.Equal(new[] { 6 }, Hijos(arbol, 5));   // la rama de al lado sigue entera
    }

    [Fact]
    public void PLEGAR_UNA_HOJA_no_esconde_a_nadie_ni_a_sus_hermanos()
    {
        var arbol = ArmarPlegando([2], (-1, 0), (1, 1), (2, 2), (3, 2));

        Assert.Equal(new[] { -1, 1, 2, 3 }, TrazadoDelOrganigrama.Recorrer(arbol).Select(c => c.Id));
        Assert.Equal(0, Caja(arbol, 2).Escondidos);

        // La caja se pliega IGUAL aunque no tenga subequipos: lo que esconde entonces es a su gente,
        // que es el motivo por el que se pliega la mayoría de las veces. Atarlo a tener rama las
        // dejaría con un interruptor que no hace nada, y esta prueba está para que no vuelva a
        // atarse: al reescribir el trazado como árbol se ató, y esto fue lo que lo cazó.
        Assert.True(Caja(arbol, 2).Plegada);
        Assert.False(Caja(arbol, 2).TieneRama);
    }

    [Fact]
    public void SIN_PLEGAR_NADA_no_se_pierde_ninguna_caja_ni_cambia_el_orden()
    {
        // Recorrer el árbol tiene que devolver exactamente la lista que entró: el anidamiento cambia
        // dónde se dibuja cada caja, no cuáles hay ni en qué orden se leen.
        var entradas = new (int, int)[] { (-1, 0), (7, 1), (3, 2), (9, 1), (0, 1) };
        var arbol = Armar(entradas);

        var recorrido = TrazadoDelOrganigrama.Recorrer(arbol).ToList();
        Assert.Equal(entradas.Select(e => e.Item1), recorrido.Select(c => c.Id));
        Assert.Equal(entradas.Select(e => e.Item2), recorrido.Select(c => c.Nivel));
    }

    [Fact]
    public void UN_NIVEL_IMPOSIBLE_no_descoloca_el_dibujo_ni_revienta()
    {
        // Un salto de tres alturas de golpe no lo puede producir el servidor; sí un círculo escrito a
        // mano contra la base. Se recorta al siguiente nivel posible: el dibujo sale raro —que es lo
        // que hay que ver— pero ninguna caja se queda colgando de un padre que no está.
        var arbol = Armar((-1, 0), (1, 4), (2, 9), (3, 1));

        Assert.Equal(4, TrazadoDelOrganigrama.Recorrer(arbol).Count());
        Assert.Equal(new[] { -1 }, arbol.Select(n => n.Id));
        Assert.Equal(1, Caja(arbol, 1).Nivel);
        Assert.Equal(2, Caja(arbol, 2).Nivel);
        Assert.Equal(new[] { 1, 3 }, Hijos(arbol, -1));
        Assert.Equal(new[] { 2 }, Hijos(arbol, 1));
    }

    [Fact]
    public void LA_PRIMERA_CAJA_siempre_arranca_arriba_del_todo()
    {
        // Aunque llegue con nivel propio: si empezara sangrada, colgaría de un padre que no está en
        // la lista y el dibujo se quedaría sin cabeza.
        var arbol = Armar((5, 3), (6, 4));

        Assert.Equal(new[] { 5 }, arbol.Select(n => n.Id));
        Assert.Equal(0, arbol[0].Nivel);
        Assert.Equal(new[] { 6 }, Hijos(arbol, 5));
    }

    [Fact]
    public void VARIAS_RAICES_se_dibujan_una_al_lado_de_otra()
    {
        // La pantalla manda siempre la caja de arriba primero, así que hoy hay una sola raíz. Se
        // comprueba igual porque el árbol no lo exige y el marcado tampoco: la fila de arriba del
        // todo admite varias cajas sin que ninguna cuelgue de nada.
        var arbol = Armar((1, 0), (2, 1), (3, 0), (4, 0));

        Assert.Equal(new[] { 1, 3, 4 }, arbol.Select(n => n.Id));
        Assert.Equal(new[] { 2 }, Hijos(arbol, 1));
    }

    [Fact]
    public void SIN_CAJAS_no_hay_arbol()
    {
        Assert.Empty(TrazadoDelOrganigrama.Armar([], new HashSet<int>()));
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
