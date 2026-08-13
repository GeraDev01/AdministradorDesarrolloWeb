using AdminWeb.Domain.Equipos;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL ÁRBOL DE EQUIPOS, sin base de datos de por medio.
///
/// <para>Esto se prueba aparte de los servicios porque es la pieza de la que cuelga todo lo demás: el
/// organigrama de la pantalla, el del papel, el ranking por rama y el «es de mi equipo». Si el
/// recorrido está mal, los cuatro se equivocan a la vez y cada uno de una forma distinta, que es lo
/// que hace que un fallo así tarde días en localizarse.</para>
///
/// <para>Y se prueba con DATOS ROTOS a propósito —un padre que ya no existe, un círculo escrito a
/// mano contra la base—, no porque la aplicación pueda crearlos, sino porque el día que aparezca uno
/// la diferencia entre «una caja sale donde no toca» y «la pantalla de equipos no carga para nadie»
/// se decide justo aquí.</para>
/// </summary>
public class JerarquiaDeEquiposTests
{
    /// <summary>
    /// El árbol de ejemplo, que es el que pidió el dueño con otros nombres:
    /// <code>
    ///   1 Web
    ///     ├── 2 Front
    ///     │     └── 4 Diseño
    ///     └── 3 Back
    ///   5 Datos (raíz, sin nada debajo)
    /// </code>
    /// </summary>
    private static JerarquiaDeEquipos Arbol() => JerarquiaDeEquipos.De(
    [
        (1, (int?)null),
        (2, 1),
        (3, 1),
        (4, 2),
        (5, null)
    ]);

    // ── El subárbol ──────────────────────────────────────────────────────────────

    [Fact]
    public void ElSubarbol_traeElEquipoYTodoLoQueCuelgaDeEl()
    {
        // De arriba abajo y con los hermanos en el orden en que llegaron: Front antes que Back
        // porque así venían, y Diseño pegado a Front porque cuelga de él.
        int[] esperado = [1, 2, 4, 3];
        Assert.Equal(esperado, Arbol().Subarbol(1));
    }

    [Fact]
    public void ElSubarbol_deUnaHoja_esSoloElla()
    {
        int[] soloDiseno = [4];
        int[] soloDatos = [5];
        Assert.Equal(soloDiseno, Arbol().Subarbol(4));
        Assert.Equal(soloDatos, Arbol().Subarbol(5));
    }

    [Fact]
    public void ElSubarbol_deUnEquipoQueNoExiste_vieneVacio()
    {
        // Vacío y no «una lista con él dentro»: un identificador viejo no puede parecer un equipo de
        // una sola caja, porque quien sume esa rama estaría sumando algo que no está.
        Assert.Empty(Arbol().Subarbol(99));
    }

    [Fact]
    public void ElSubarbol_deUnaCadenaDeCuatro_lasTraeTodasEnOrden()
    {
        var cadena = JerarquiaDeEquipos.De([(1, (int?)null), (2, 1), (3, 2), (4, 3)]);

        int[] entera = [1, 2, 3, 4];
        int[] laMitadDeAbajo = [3, 4];
        Assert.Equal(entera, cadena.Subarbol(1));
        Assert.Equal(laMitadDeAbajo, cadena.Subarbol(3));
    }

    // ── Subir por la cadena ──────────────────────────────────────────────────────

    [Fact]
    public void LosAncestros_vanDelMasCercanoAlMasLejano_ySinElPropioEquipo()
    {
        int[] haciaArriba = [2, 1];
        Assert.Equal(haciaArriba, Arbol().Ancestros(4));
        Assert.Empty(Arbol().Ancestros(1));
    }

    [Fact]
    public void ElNivel_esLaAlturaALaQueCuelga()
    {
        var arbol = Arbol();

        Assert.Equal(0, arbol.Nivel(1));
        Assert.Equal(1, arbol.Nivel(2));
        Assert.Equal(2, arbol.Nivel(4));
        Assert.Equal(0, arbol.Nivel(99));   // lo que no existe no cuelga de nada
    }

    [Fact]
    public void ElPadre_esNuloEnLaRaiz_yEnElQueApuntaAUnEquipoQueYaNoEsta()
    {
        var arbol = Arbol();
        Assert.Null(arbol.PadreDe(1));
        Assert.Equal(1, arbol.PadreDe(2));

        // Un padre que se borró fuera de la aplicación: para el dibujo es raíz, porque publicar ese
        // identificador mandaría a quien dibuja a buscar una caja que no está en la lista.
        var huerfano = JerarquiaDeEquipos.De([(1, (int?)7)]);
        Assert.Null(huerfano.PadreDe(1));
    }

    // ── «Es de mi equipo» ────────────────────────────────────────────────────────

    [Fact]
    public void LaMismaRaiz_incluyeAlPropioEquipoYALaLineaCompletaEnLosDosSentidos()
    {
        var arbol = Arbol();

        Assert.True(arbol.MismaRaiz(1, 1));
        Assert.True(arbol.MismaRaiz(1, 4));   // el de arriba reconoce al de abajo
        Assert.True(arbol.MismaRaiz(4, 1));   // y el de abajo al de arriba
    }

    [Fact]
    public void LaMismaRaiz_INCLUYE_A_LOS_HERMANOS_que_es_el_punto_del_equipo_padre()
    {
        // Ésta es la regla que pidió el dueño, y lo que la justifica: el equipo padre existe para
        // que un área se reconozca entre sí. Dos personas de subequipos hermanos tienen el mismo
        // jefe de área y trabajan en lo mismo; dejarlas fuera de «mi equipo» sería lo contrario de
        // para lo que se creó el paraguas. Antes esto era «la misma rama» y devolvía false.
        var arbol = Arbol();

        Assert.True(arbol.MismaRaiz(2, 3));   // hermanos
        Assert.True(arbol.MismaRaiz(4, 3));   // un nieto y su tío: la misma raíz los junta
    }

    [Fact]
    public void LaMismaRaiz_DEJA_FUERA_A_QUIEN_CUELGA_DE_OTRA_RAIZ()
    {
        // El paraguas llega hasta donde llega: dos áreas distintas siguen siendo dos áreas. Si esto
        // se rompiera, la marca pasaría a ser «todo el mundo» y dejaría de decir nada.
        var arbol = Arbol();

        Assert.False(arbol.MismaRaiz(1, 5));
        Assert.False(arbol.MismaRaiz(4, 5));   // el más hondo de una raíz contra la otra raíz
    }

    [Fact]
    public void SinSubequipos_LaMismaRaiz_ES_LA_IGUALDAD_DE_SIEMPRE()
    {
        // Lo que había antes de que existieran los subequipos tiene que seguir comportándose igual:
        // sin padres, cada equipo es su propia raíz y la regla se reduce a «es el mismo equipo».
        var planos = JerarquiaDeEquipos.De([(1, (int?)null), (2, null), (3, null)]);

        Assert.True(planos.MismaRaiz(2, 2));
        Assert.False(planos.MismaRaiz(1, 2));
        Assert.False(planos.MismaRaiz(2, 3));
    }

    [Fact]
    public void LaRaiz_DE_UN_CIRCULO_NO_CUELGA_LA_CONSULTA()
    {
        // Un círculo solo puede llegar aquí escrito a mano contra la base, pero el resto de esta
        // clase se compromete por escrito a aguantarlo al leer. Raíz sube por los ancestros, así que
        // sin la guarda de visitados daría vueltas para siempre y colgaría la pantalla entera.
        var circulo = JerarquiaDeEquipos.De([(1, (int?)2), (2, 1)]);

        // Lo que se garantiza es que TERMINA y contesta algo de la propia rueda. Lo que NO se
        // garantiza es que la respuesta tenga sentido: en un círculo cada uno ve al otro como su
        // ancestro más alto, así que «la misma raíz» sale que no aunque estén enganchados. Eso está
        // bien: los datos ya eran mentira antes de llegar aquí, y lo único que esta clase promete
        // ante un círculo es no colgarse ni perder equipos por el camino. Quien lo arregla es
        // SeriaCiclo, que impide escribirlo.
        Assert.InRange(circulo.Raiz(1), 1, 2);
        Assert.InRange(circulo.Raiz(2), 1, 2);
    }

    // ── El orden de dibujo ───────────────────────────────────────────────────────

    [Fact]
    public void ElOrdenDeDibujo_poneCadaPadreDelanteDeSuRama()
    {
        int[] esperado = [1, 2, 4, 3, 5];
        Assert.Equal(esperado, Arbol().EnOrdenDeDibujo());
    }

    [Fact]
    public void ElOrdenDeDibujo_noPierdeNiUnEquipo_aunqueSuPadreYaNoExista()
    {
        // En una lista, faltar es una fila menos. En un organigrama es un equipo que oficialmente no
        // está en ninguna parte, y nadie lo echa de menos hasta que alguien pregunta por él.
        var roto = JerarquiaDeEquipos.De([(1, (int?)null), (2, 99)]);

        int[] losDos = [1, 2];
        Assert.Equal(losDos, roto.EnOrdenDeDibujo());
    }

    [Fact]
    public void ElOrdenDeDibujo_conUnCirculoEscritoAMano_noSeCuelgaYLosSacaIgual()
    {
        // 2 y 3 se apuntan entre ellos: no cuelgan de ninguna raíz, así que el recorrido normal no
        // los alcanza. Salen al final, descolocados pero VISIBLES — y que se vean es lo que permite
        // que alguien note que hay algo mal.
        var circulo = JerarquiaDeEquipos.De([(1, (int?)null), (2, 3), (3, 2)]);

        var orden = circulo.EnOrdenDeDibujo();

        Assert.Equal(3, orden.Count);
        Assert.Contains(2, orden);
        Assert.Contains(3, orden);
    }

    // ── Los círculos ─────────────────────────────────────────────────────────────

    [Fact]
    public void SeriaCiclo_conElPropioEquipo()
    {
        Assert.True(Arbol().SeriaCiclo(1, 1));
    }

    [Fact]
    public void SeriaCiclo_conUnHijo_conUnNieto_yConElUltimoDeUnaCadenaDeCuatro()
    {
        var arbol = Arbol();
        Assert.True(arbol.SeriaCiclo(1, 2));   // colgar al padre de su hijo
        Assert.True(arbol.SeriaCiclo(1, 4));   // …o de su nieto, que es el que se cuela

        var cadena = JerarquiaDeEquipos.De([(1, (int?)null), (2, 1), (3, 2), (4, 3)]);
        Assert.True(cadena.SeriaCiclo(1, 4));
        Assert.True(cadena.SeriaCiclo(2, 4));
    }

    [Fact]
    public void NoEsCiclo_colgarDeUnEquipoDeOtraRama_niQuedarseSinPadre()
    {
        var arbol = Arbol();

        Assert.False(arbol.SeriaCiclo(5, 4));   // Datos pasa a colgar de Diseño: es un árbol válido
        Assert.False(arbol.SeriaCiclo(2, 3));   // Front pasa a colgar de su hermano Back
        Assert.False(arbol.SeriaCiclo(4, null));
    }

    [Fact]
    public void UnEquipoNuevo_puedeColgarDeCualquiera()
    {
        // Todavía no está en el árbol (su identificador es 0), así que no tiene nada debajo con lo
        // que cerrar un círculo.
        Assert.False(Arbol().SeriaCiclo(0, 4));
    }

    [Fact]
    public void EsSuPropioAncestro_veLosCirculosYaEscritos()
    {
        Assert.True(JerarquiaDeEquipos.De([(1, (int?)1)]).EsSuPropioAncestro(1));
        Assert.True(JerarquiaDeEquipos.De([(1, (int?)2), (2, 1)]).EsSuPropioAncestro(1));
        Assert.True(JerarquiaDeEquipos.De([(1, (int?)3), (2, 1), (3, 2)]).EsSuPropioAncestro(2));
        Assert.False(Arbol().EsSuPropioAncestro(4));
    }

    [Fact]
    public void UnArbolSano_noTieneANadieColgandoDeSiMismo()
    {
        var arbol = Arbol();
        Assert.All(new[] { 1, 2, 3, 4, 5 }, id => Assert.False(arbol.EsSuPropioAncestro(id)));
    }

    [Fact]
    public void UnEquipoQueCuelgaDeUnCirculoAjeno_noEsSuPropioAncestro()
    {
        // 3 sube a 2, 2 sube a 1 y 1 vuelve a 2: hay un círculo, pero 3 no está dentro. Sin el corte
        // por visitados esto sería una vuelta infinita, y con un corte mal puesto diría que sí.
        var enredado = JerarquiaDeEquipos.De([(1, (int?)2), (2, 1), (3, 2)]);

        Assert.False(enredado.EsSuPropioAncestro(3));
        Assert.True(enredado.EsSuPropioAncestro(1));
        Assert.True(enredado.EsSuPropioAncestro(2));
    }
}
