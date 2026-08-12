using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AdminWeb.Client.Recorridos;

/// <summary>
/// Todos los recorridos de la aplicación, y qué recorrido corresponde a la dirección que se está
/// mirando.
///
/// <para>Los guiones se descubren por reflexión sobre este mismo ensamblado: toda clase que
/// implemente <see cref="IFuenteDeRecorridos"/> entra sola. Se puede hacer porque el recorte de
/// ensamblados está APAGADO en este proyecto (ver el comentario de AdminWeb.Client.csproj, donde
/// consta que encenderlo dejaba la aplicación colgada sin excepción). Si algún día se recuperara el
/// recorte, esto es de lo primero que dejaría de funcionar y habría que anclarlo con
/// TrimmerRootDescriptor.</para>
/// </summary>
public static class RegistroDeRecorridos
{
    private static IReadOnlyList<Recorrido>? _todos;

    /// <summary>Los recorridos escritos, ordenados por ruta para que la lista sea estable.</summary>
    public static IReadOnlyList<Recorrido> Todos => _todos ??= Descubrir(typeof(Recorrido).Assembly);

    /// <summary>
    /// Busca las fuentes de guiones de un ensamblado. Lo normal es no llamar a esto: existe separado
    /// de <see cref="Todos"/> para que la prueba pueda ejercitar el descubrimiento con sus propias
    /// fuentes en vez de depender de los guiones reales, que el día que se escribió esto todavía no
    /// existían.
    /// </summary>
    public static IReadOnlyList<Recorrido> Descubrir(Assembly ensamblado) =>
        Tipos(ensamblado)
            .Where(t => typeof(IFuenteDeRecorridos).IsAssignableFrom(t)
                        && t is { IsClass: true, IsAbstract: false }
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IFuenteDeRecorridos)Activator.CreateInstance(t)!)
            .SelectMany(f => f.Recorridos())
            .OrderBy(r => r.Ruta, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Los tipos del ensamblado, sobreviviendo a que alguno no se pueda cargar.
    ///
    /// <para>Sin esto, un solo tipo cuyo ensamblado de apoyo no esté a mano tumba
    /// <c>GetTypes()</c> entero y con él TODOS los recorridos. Pasa fuera del navegador —la prueba
    /// carga este ensamblado desde un proyecto de consola, no desde WebAssembly— y el precio de
    /// protegerse es esta media docena de líneas.</para>
    /// </summary>
    private static IEnumerable<Type> Tipos(Assembly ensamblado)
    {
        try
        {
            return ensamblado.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>
    /// El recorrido de la pantalla que se está mirando, o null si esa pantalla todavía no tiene.
    /// Admite la dirección tal como la da <c>NavigationManager</c>: con o sin barra inicial, con
    /// parámetros de consulta y con ancla.
    /// </summary>
    public static Recorrido? Para(string? direccion) => Para(direccion, Todos);

    /// <summary>La misma búsqueda contra una lista dada. Separada para poder probarla.</summary>
    public static Recorrido? Para(string? direccion, IReadOnlyList<Recorrido> recorridos)
    {
        var ruta = Normalizar(direccion);

        // Primero la coincidencia LITERAL, y ese orden no es cosmético: «/conocimiento/nuevo» y
        // «/conocimiento/{Id:int}» encajan las dos con la dirección «conocimiento/nuevo», y quien
        // manda es la literal — igual que en el enrutador de Blazor, que es lo que decide qué
        // pantalla se está viendo de verdad. Al revés, la pantalla de crear un artículo enseñaría el
        // recorrido de leer uno.
        foreach (var r in recorridos)
        {
            if (!TienePlaceholder(r.Ruta) && Normalizar(r.Ruta) == ruta) return r;
        }

        // Y después las que llevan parámetros, de menos a más: entre dos plantillas que encajen,
        // gana la que deja menos hueco al azar.
        return recorridos
            .Where(r => TienePlaceholder(r.Ruta) && Patron(r.Ruta).IsMatch(ruta))
            .OrderBy(r => Placeholders(r.Ruta))
            .FirstOrDefault();
    }

    /// <summary>
    /// Deja la dirección en su forma comparable: sin barra inicial ni final, sin lo que venga
    /// después de «?» o de «#», y en minúsculas. La portada («/») queda como cadena vacía, que es
    /// justo lo que devuelve <c>NavigationManager.ToBaseRelativePath</c> al estar en ella.
    /// </summary>
    private static string Normalizar(string? direccion)
    {
        var r = direccion ?? string.Empty;

        var corte = r.IndexOfAny(['?', '#']);
        if (corte >= 0) r = r[..corte];

        return r.Trim('/').ToLowerInvariant();
    }

    private static bool TienePlaceholder(string plantilla) => plantilla.Contains('{');

    private static int Placeholders(string plantilla) => plantilla.Count(c => c == '{');

    /// <summary>
    /// Convierte «/conocimiento/{Id:int}» en una expresión que reconozca «conocimiento/12».
    ///
    /// <para>Cada tramo entre barras que empiece por llave se sustituye por «un tramo cualquiera»;
    /// el resto se escapa literal. El comodín de cola (<c>{*resto}</c>) se traduce a «lo que
    /// quede» — hoy no hay ninguna ruta así, pero cuesta una línea y evita que el día que la haya
    /// el recorrido deje de encontrarse sin decir por qué.</para>
    /// </summary>
    private static Regex Patron(string plantilla)
    {
        // Se guardan hechas: esto se ejecuta en cada cambio de pantalla y compilar expresiones
        // regulares en WebAssembly no es gratis. Son un puñado y viven lo que viva la pestaña.
        if (_patrones.TryGetValue(plantilla, out var hecha)) return hecha;

        var tramos = Normalizar(plantilla).Split('/');

        var patron = string.Join("/", tramos.Select(t =>
            !t.StartsWith('{') ? Regex.Escape(t)
            : t.StartsWith("{*") ? ".*"
            : "[^/]+"));

        return _patrones[plantilla] =
            new Regex($"^{patron}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Concurrente y no un diccionario normal: en el navegador solo hay un hilo, pero esto mismo se
    // ejecuta desde las pruebas, donde xUnit reparte clases en paralelo. Un diccionario corriente
    // leído mientras otro hilo lo hace crecer no da un error: se queda dando vueltas para siempre.
    private static readonly ConcurrentDictionary<string, Regex> _patrones = new();
}
