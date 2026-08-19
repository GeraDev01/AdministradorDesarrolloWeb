using Radzen;

namespace AdminWeb.Client.Componentes;

/// <summary>
/// LOS AJUSTES DEL PIE DE PÁGINA DE LAS REJILLAS, EN UN SOLO SITIO.
///
/// <para>Se reparten con <c>@attributes</c> —el «splatting» de Blazor, que enlaza cada clave del
/// diccionario con el parámetro del mismo nombre— y no atributo por atributo. Son DOCE parámetros
/// por rejilla y hay más de sesenta rejillas paginadas: escritos a mano serían más de setecientas
/// cadenas repetidas, y la número setecientos uno diría otra cosa. Aquí se cambian una vez y se
/// enteran todas.</para>
///
/// <para><b>Por qué hay que ponerlos.</b> Radzen no tiene ningún gancho de localización: los rótulos
/// de las flechas vienen en inglés de fábrica («First page», «Go to page {0}.») y el resumen también
/// («Page 1 of 3 (45 items)»). Son el ÚNICO texto que explica qué hace una flecha sin etiqueta, así
/// que en una aplicación entera en español no pueden quedarse como están.</para>
///
/// <para><b>Y por qué el resumen se enciende.</b> Sin él, el pie son siete botones y ningún dato:
/// no dice en qué página estás ni cuántas hay, y con el filtro puesto tampoco cuántas filas quedaron.
/// «Página 2 de 7 · 98 en total» contesta las tres cosas en un renglón. El texto va en {0}, {1} y {2}
/// —página, total de páginas y total de filas— que es lo que Radzen sustituye, en ese orden.</para>
///
/// <para>La otra mitad del arreglo del pie NO está aquí sino en <c>css/tema.css</c>, y es donde tiene
/// que estar: que los botones vayan juntos en vez de repartidos a lo ancho de la tabla es cosa de dos
/// márgenes automáticos de Radzen, y se anula con CSS para las rejillas de hoy y para las de mañana
/// sin tocar ninguna.</para>
/// </summary>
public static class Paginador
{
    /// <summary>
    /// Los rótulos en español y el resumen. Es lo que lleva CUALQUIER cosa que pagine, incluidos los
    /// dos <c>RadzenPager</c> sueltos que no cuelgan de una rejilla.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, object> EnEspanol = Rotulos();

    /// <summary>
    /// Lo mismo, más el paginador REPETIDO ARRIBA de la tabla.
    ///
    /// <para>Es para las rejillas de página larga —quince filas o más—, y arregla la mitad del
    /// problema que el CSS no puede tocar. La aplicación entera vive dentro de un
    /// <c>RadzenLayout</c>, que mide exactamente el alto de la ventana y no desborda; lo que
    /// desplaza es el cuerpo. Con quince filas, más los filtros y los avisos que van encima de la
    /// tabla, el pie cae por debajo del corte de la pantalla: para cambiar de página hay que
    /// desplazarse hasta el final, y al llegar a la página siguiente hay que volver a hacerlo. Con
    /// una copia arriba, el control está donde ya estás mirando.</para>
    ///
    /// <para><b>No aparecen dos barras cuando sobra una.</b> <c>RadzenPager</c> no se pinta si todo
    /// cabe en una página, así que en una lista corta no se ve ninguno. La excepción es una rejilla
    /// con selector de tamaño de página —hoy solo la bitácora—: ésa pinta el pie siempre, para que el
    /// selector siga a mano, y por eso se queda con <see cref="EnEspanol"/> y su paginador solo
    /// abajo.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, object> ArribaYAbajo =
        Rotulos(d => d["PagerPosition"] = PagerPosition.TopAndBottom);

    private static Dictionary<string, object> Rotulos(Action<Dictionary<string, object>>? extra = null)
    {
        var d = new Dictionary<string, object>
        {
            ["ShowPagingSummary"]   = true,
            ["PagingSummaryFormat"] = "Página {0} de {1} · {2} en total",

            // Los cuatro rótulos del globo que sale al posarse encima…
            ["FirstPageTitle"] = "Primera página",
            ["PrevPageTitle"]  = "Página anterior",
            ["NextPageTitle"]  = "Página siguiente",
            ["LastPageTitle"]  = "Última página",
            ["PageTitleFormat"] = "Página {0}",

            // …y los cuatro que lee un lector de pantalla, que NO son los mismos textos a propósito:
            // el globo nombra el destino («Primera página») y la etiqueta accesible describe la
            // acción («Ir a la primera página.»), que es lo que se espera de un botón. El punto final
            // también es de Radzen: sus valores de fábrica lo llevan y sin él la frase se pega a la
            // siguiente que anuncie el lector.
            ["FirstPageAriaLabel"] = "Ir a la primera página.",
            ["PrevPageAriaLabel"]  = "Ir a la página anterior.",
            ["NextPageAriaLabel"]  = "Ir a la página siguiente.",
            ["LastPageAriaLabel"]  = "Ir a la última página.",
            ["PageAriaLabelFormat"] = "Ir a la página {0}.",
        };

        extra?.Invoke(d);
        return d;
    }
}
