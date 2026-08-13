using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// QUIÉN PUEDE LLEGAR A CADA PANTALLA SIN HABER ENTRADO.
///
/// <para><b>El fallo que hace que esto exista.</b> La raíz «/» dejó de ser una portada y pasó a ser
/// un desvío: una pantalla SIN MARCADO cuyo único trabajo es mandarte a donde empieza tu trabajo.
/// Se quedó sin <c>[Authorize]</c>, y con eso quien llegaba sin sesión —o sea, cualquiera que
/// teclease la dirección de la aplicación— se encontraba LA APLICACIÓN ENTERA EN BLANCO y para
/// siempre. Sin error, sin aviso y sin nada que mirar: el WebAssembly arrancaba completo, se
/// pintaba, y se quedaba en una página vacía.</para>
///
/// <para>El motivo es que la redirección al acceso vive en UN solo sitio —el <c>NotAuthorized</c> de
/// <c>App.razor</c>— y ese sitio solo mira las pantallas PROTEGIDAS. Las demás pantallas se salvaban
/// por accidente: todas llaman a la API, y el 401 las empuja al acceso desde
/// <c>ManejadorDeRespuestas</c>. La raíz no llama a nada, así que no había nada que la empujara.</para>
///
/// <para><b>Por qué una prueba y no un comentario.</b> Nada de esto lo ve el compilador, y no lo ve
/// ninguna prueba de servicio: la pantalla compila, sus cálculos están bien y la aplicación arranca.
/// Solo se ve abriendo la dirección sin sesión, que es justo lo que nadie hace al probar —porque
/// quien prueba ya entró—. Estuvo así en producción.</para>
///
/// <para><b>La lista de exentas es corta y cada una dice por qué.</b> Una lista de excepciones
/// envejece mal, así que aquí no cabe nada «por ahora»: quien añada una pantalla pública tiene que
/// escribir en esta lista qué se ve en ella sin sesión.</para>
/// </summary>
public class PuertaDeLasPantallasTests
{
    /// <summary>
    /// Las pantallas a las que se llega SIN sesión a propósito, con lo que se ve en cada una.
    ///
    /// <para>Comprobado en el navegador contra la aplicación desplegada: las cuatro pintan algo. Las
    /// dos de conocimiento no llevan <c>[Authorize]</c> y aun así acaban en el acceso, porque lo
    /// primero que hacen es pedirle datos a la API y el 401 las empuja. Es un camino más largo pero
    /// termina donde debe, y por eso no se tocan.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Exentas = new()
    {
        ["/acceso"] = "Es la pantalla de acceso. Sin ella no se entra.",
        ["/cambiar-contrasena"] = "Va después de acertar la contraseña, cuando toca cambiarla.",
        ["/segundo-factor"] = "El segundo tramo del acceso: aquí todavía no hay sesión.",
        ["/conocimiento"] = "No está protegida, pero pide datos a la API y el 401 la manda al acceso.",
        ["/conocimiento/{Id:int}"] = "Igual que la anterior: el 401 de la API la manda al acceso.",
    };

    /// <summary>Toda pantalla enrutable del cliente, con su plantilla de ruta.</summary>
    private static IEnumerable<(Type Pantalla, string Ruta)> Pantallas() =>
        typeof(AdminWeb.Client.App).Assembly
            .GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(
                t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>(),
                (t, r) => (Pantalla: t, Ruta: r.Template));

    [Fact]
    public void HAY_PANTALLAS_QUE_MIRAR()
    {
        // El cinturón contra el falso verde: las dos pruebas de abajo son de la forma «recorre las
        // pantallas y no encuentres ninguna mal», así que sobre una lista vacía pasan solas. Si el
        // recorte de ensamblados o un cambio de proyecto dejara esto sin encontrar nada, la suite se
        // pondría verde anunciando que todas las puertas están bien puestas.
        Assert.True(Pantallas().Count() > 20, "El descubrimiento de pantallas no está encontrando nada.");
    }

    [Fact]
    public void TODA_PANTALLA_O_ESTA_PROTEGIDA_O_ESTA_EN_LA_LISTA_CON_SU_MOTIVO()
    {
        var sueltas = Pantallas()
            .Where(p => !Exentas.ContainsKey(p.Ruta))
            .Where(p => p.Pantalla.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length == 0)
            .Select(p => $"{p.Pantalla.Name} ({p.Ruta})")
            .OrderBy(x => x)
            .ToList();

        Assert.True(sueltas.Count == 0,
            "Estas pantallas no llevan [Authorize] y no están en la lista de exentas. Sin sesión, la " +
            "redirección al acceso de App.razor NO las mira, así que o pintan algo por su cuenta o " +
            "quien llegue se queda con la página en blanco:\n  " + string.Join("\n  ", sueltas));
    }

    [Fact]
    public void LA_RAIZ_ESTA_PROTEGIDA_PORQUE_NO_TIENE_NADA_QUE_ENSENAR()
    {
        // Se comprueba aparte de la de arriba y a propósito. Aquella se contenta con que la raíz esté
        // en la lista de exentas; ésta dice que en la raíz eso NO vale, porque es la única pantalla
        // que no pinta nada por sí misma. Meterla en la lista de exentas sería documentar el fallo en
        // vez de arreglarlo, y es exactamente el atajo que alguien tomaría con la prisa de un martes.
        var raiz = Pantallas().SingleOrDefault(p => p.Ruta == "/");

        Assert.True(raiz.Pantalla is not null, "Ya no hay ninguna pantalla en «/».");
        Assert.True(
            raiz.Pantalla.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0,
            $"«{raiz.Pantalla!.Name}» atiende «/» y no lleva [Authorize]. Es un desvío sin marcado: " +
            "sin sesión no pinta nada, y al no estar protegida tampoco la mira el NotAuthorized de " +
            "App.razor. Resultado: la aplicación entera en blanco para cualquiera que teclee la " +
            "dirección sin haber entrado. Ya pasó, y en producción.");
    }
}
