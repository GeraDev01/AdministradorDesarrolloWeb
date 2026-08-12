using System.Text;
using System.Text.RegularExpressions;
using AdminWeb.Client.Recorridos;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// LA RED DE SEGURIDAD DE LOS RECORRIDOS GUIADOS.
///
/// <para>Esto es lo que hace sostenibles los recorridos de todas las pantallas. Cada paso de cada guion dice a
/// qué control señala mediante una marca (<c>data-recorrido="algo"</c>), y estas pruebas recorren
/// TODOS los guiones comprobando que esa marca sigue existiendo en el marcado de su pantalla.</para>
///
/// <para>El fallo que evitan no es teórico y es de los que no se ven venir. Alguien reordena una
/// pantalla y quita un botón; el recorrido de esa pantalla, escrito hace tres meses por otra
/// persona, se queda apuntando al vacío. En tiempo de ejecución el paso se salta en silencio —así
/// está hecho a propósito, para que a nadie se le quede el recorrido a medias porque su rol no ve
/// un control—, o sea que NADIE SE ENTERA. Se entera, semanas después, quien lanza el recorrido para
/// aprender la pantalla y ve que le explican tres cosas de cinco. Y un recorrido que señala lo que
/// ya no está es peor que no tener recorrido: rompe la confianza en todos los demás.</para>
///
/// <para>Estas pruebas viven en el proyecto de la API y no en el de Application porque es el único
/// de los dos que ve <c>AdminWeb.Client</c> —la API sirve el cliente, así que lo referencia—, y los
/// guiones están en C# dentro del cliente justamente para poder leerlos desde aquí.</para>
///
/// <para><b>Si una de estas pruebas se pone roja, no la ajustes.</b> El mensaje dice qué paso, de
/// qué recorrido y qué marca falta. Se arregla poniendo la marca en el control que el paso describe
/// o quitando el paso del guion.</para>
/// </summary>
public class RecorridosGuiadosTests
{
    // ── El contrato, comprobado ──────────────────────────────────────────────────

    [Fact]
    public void CadaPaso_APUNTA_A_UNA_MARCA_QUE_EXISTE_EN_EL_MARCADO()
    {
        // LA prueba de este archivo.
        var marcasCompartidas = MarcasDe(MarcadoCompartido());
        var problemas = new List<string>();

        foreach (var recorrido in RegistroDeRecorridos.Todos)
        {
            if (!PaginasPorRuta.TryGetValue(Normalizar(recorrido.Ruta), out var pagina)) continue;

            var disponibles = new HashSet<string>(MarcasDe([pagina]), StringComparer.Ordinal);
            disponibles.UnionWith(marcasCompartidas);

            for (var i = 0; i < recorrido.Pasos.Count; i++)
            {
                var paso = recorrido.Pasos[i];
                if (paso.EsPortada || disponibles.Contains(paso.Marca)) continue;

                var explicacion = new StringBuilder()
                    .AppendLine($"Recorrido {recorrido}, paso {i + 1} «{paso.Titulo}»:")
                    .AppendLine($"  la marca «{paso.Marca}» no está en el marcado de esa pantalla.")
                    .AppendLine($"  Se buscó {paso.Selector} en {Relativa(pagina)}")
                    .AppendLine("  y en el marcado compartido (Componentes/**, Layout/**).");

                if (DondeEsta(paso.Marca) is { } otroSitio)
                {
                    explicacion.AppendLine(
                        $"  Esa marca sí aparece en {otroSitio}: ¿el paso se copió de otro recorrido," +
                        " o el control cambió de pantalla?");
                }

                explicacion.Append(
                    $"  Se arregla poniendo {Paso.Atributo}=\"{paso.Marca}\" en el control que el paso" +
                    " describe, o quitando el paso del guion. No ajustes la prueba.");

                problemas.Add(explicacion.ToString());
            }
        }

        Assert.True(problemas.Count == 0,
            $"Hay {problemas.Count} paso(s) señalando controles que no existen:\n\n"
            + string.Join("\n\n", problemas));
    }

    [Fact]
    public void CadaRecorrido_SE_CUELGA_DE_UNA_RUTA_QUE_EXISTE()
    {
        // Una ruta mal copiada no da ningún error: el botón se queda apagado, que es exactamente lo
        // que se ve en una pantalla que todavía no tiene recorrido. O sea que el recorrido está
        // escrito, revisado y no se lanza NUNCA, y nada lo delata.
        var huerfanos = RegistroDeRecorridos.Todos
            .Where(r => !PaginasPorRuta.ContainsKey(Normalizar(r.Ruta)))
            .Select(r => $"  {r}  →  ninguna pantalla declara @page \"{r.Ruta}\"")
            .ToList();

        Assert.True(huerfanos.Count == 0,
            "Estos recorridos apuntan a rutas que no existen (cópialas TAL CUAL de la directiva " +
            "@page de la pantalla, con sus parámetros):\n" + string.Join("\n", huerfanos));
    }

    [Fact]
    public void NoHayDosRecorridos_PARA_LA_MISMA_PANTALLA()
    {
        // Con dos, uno de los dos no se lanza jamás y quien lo escribió no tiene forma de saber cuál.
        var repetidas = RegistroDeRecorridos.Todos
            .GroupBy(r => Normalizar(r.Ruta))
            .Where(g => g.Count() > 1)
            .Select(g => $"  {g.Key}: {string.Join(", ", g.Select(r => $"«{r.Titulo}»"))}")
            .ToList();

        Assert.True(repetidas.Count == 0,
            "Una pantalla, un recorrido. Estas tienen más de uno:\n" + string.Join("\n", repetidas));
    }

    [Fact]
    public void TodoRecorrido_EMPIEZA_POR_UNA_PORTADA()
    {
        // Dos motivos, y el segundo es mecánico.
        //
        // La pregunta que trae a alguien a pedir ayuda es «¿qué es esto?». Un recorrido que arranca
        // explicando el tercer botón de la barra de filtros contesta a una que nadie hizo.
        //
        // Y los pasos cuyo control no está en pantalla se saltan, así que un recorrido hecho solo de
        // controles puede quedarse sin NINGÚN paso según el rol de quien lo lanza o según si hay
        // datos. La portada no señala nada, así que no se puede saltar: es lo que garantiza que
        // pulsar el botón siempre enseñe algo.
        var malos = RegistroDeRecorridos.Todos
            .Where(r => r.Pasos.Count == 0 || !r.Pasos[0].EsPortada)
            .Select(r => $"  {r}: {(r.Pasos.Count == 0 ? "no tiene pasos" : $"empieza por «{r.Pasos[0].Titulo}», que señala «{r.Pasos[0].Marca}»")}")
            .ToList();

        Assert.True(malos.Count == 0,
            "El primer paso de todo recorrido tiene que ser Paso.Portada(titulo, texto) — qué es " +
            "esta pantalla y para qué se entra en ella:\n" + string.Join("\n", malos));
    }

    [Fact]
    public void CadaPaso_DICE_ALGO()
    {
        var vacios = new List<string>();

        foreach (var r in RegistroDeRecorridos.Todos)
        {
            for (var i = 0; i < r.Pasos.Count; i++)
            {
                var p = r.Pasos[i];
                if (string.IsNullOrWhiteSpace(p.Titulo))
                    vacios.Add($"  {r}, paso {i + 1}: sin título");
                // El mínimo no es un capricho de longitud: «Filtros.» no explica nada, y el coste de
                // un recorrido con pasos así es que la gente deja de lanzarlos.
                else if (p.Texto.Trim().Length < 25)
                    vacios.Add($"  {r}, paso {i + 1} «{p.Titulo}»: el texto es demasiado corto " +
                               $"para explicar algo («{p.Texto}»)");
            }
        }

        Assert.True(vacios.Count == 0,
            "Pasos que no explican nada:\n" + string.Join("\n", vacios));
    }

    // ── Las marcas del marcado ───────────────────────────────────────────────────

    [Fact]
    public void LasMarcas_SIGUEN_EL_FORMATO_ACORDADO()
    {
        // minúsculas, números y guiones. Nada de mayúsculas ni acentos ni espacios: la marca acaba
        // dentro de un selector de CSS, y el día que alguien escriba data-recorrido="Filtros de
        // búsqueda" el paso se salta sin más explicación.
        var formato = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        var malas = new List<string>();

        foreach (var archivo in TodoElMarcado())
        {
            foreach (var marca in MarcasDe([archivo]))
            {
                if (!formato.IsMatch(marca))
                    malas.Add($"  {Relativa(archivo)}: «{marca}»");
            }
        }

        foreach (var r in RegistroDeRecorridos.Todos)
        {
            foreach (var p in r.Pasos.Where(p => !p.EsPortada && !formato.IsMatch(p.Marca)))
                malas.Add($"  {r}: el paso «{p.Titulo}» señala «{p.Marca}»");
        }

        Assert.True(malas.Count == 0,
            "Las marcas van en minúsculas, con números y guiones (pool-asignar, jornada-cronometro):\n"
            + string.Join("\n", malas));
    }

    [Fact]
    public void LasMarcas_NO_SE_REPITEN_DENTRO_DE_UNA_PANTALLA()
    {
        // Solo se señala la primera que aparezca en el documento, así que con dos iguales el paso
        // apunta a una de las dos al azar — y al azar de hoy, que cambia al reordenar el marcado.
        var repetidas = new List<string>();

        foreach (var archivo in TodoElMarcado())
        {
            var grupos = MarcasDe([archivo], unicas: false)
                .GroupBy(m => m, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            foreach (var g in grupos)
                repetidas.Add($"  {Relativa(archivo)}: «{g.Key}» aparece {g.Count()} veces");
        }

        Assert.True(repetidas.Count == 0,
            "Una marca, un control:\n" + string.Join("\n", repetidas));
    }

    [Fact]
    public void LasMarcas_NO_SE_CALCULAN_EN_TIEMPO_DE_EJECUCION()
    {
        // data-recorrido="@algo" compila, se ve razonable y deja la marca fuera del alcance de esta
        // prueba: lo que la comprobación puede leer es el literal «@algo», y en el navegador saldrá
        // otra cosa. Con eso, el paso ya no está protegido por nada.
        var dinamicas = new List<string>();

        foreach (var archivo in TodoElMarcado())
        {
            var texto = File.ReadAllText(archivo);

            foreach (var marca in MarcasDe([archivo], unicas: false).Where(m => m.Contains('@')))
                dinamicas.Add($"  {Relativa(archivo)}: «{marca}»");

            // Y cualquier uso del atributo que no sea «= \"literal\"»: sin comillas, con llaves,
            // repartido por @attributes… Si la prueba no lo puede leer, no lo puede proteger.
            var usos = Regex.Matches(texto, Regex.Escape(Paso.Atributo)).Count;
            var legibles = MarcasDe([archivo], unicas: false).Count;
            if (usos != legibles)
                dinamicas.Add($"  {Relativa(archivo)}: {usos} uso(s) del atributo pero solo " +
                              $"{legibles} legible(s) como literal entre comillas");
        }

        Assert.True(dinamicas.Count == 0,
            "Las marcas se escriben literales en el marcado: data-recorrido=\"pool-asignar\".\n"
            + string.Join("\n", dinamicas));
    }

    [Fact]
    public void NoSobranMarcas_EN_LAS_PANTALLAS_QUE_YA_TIENEN_RECORRIDO()
    {
        // La comprobación al revés: una marca puesta en el marcado que ningún paso usa. No rompe
        // nada, y por eso se queda ahí para siempre — hasta que alguien la lee, la toma por viva y
        // reordena el marcado alrededor de ella. Casi siempre es el rastro de un paso que se quitó
        // del guion o de una marca escrita con el nombre a medio decidir.
        //
        // Solo se mira en pantallas QUE YA TIENEN recorrido: dejar las marcas puestas antes de
        // escribir el guion es una forma perfectamente razonable de trabajar, y esta prueba no está
        // para estorbarla.
        var sobrantes = new List<string>();

        foreach (var (ruta, pagina) in PaginasPorRuta)
        {
            var recorrido = RegistroDeRecorridos.Para(ruta);
            if (recorrido is null) continue;

            var usadas = RegistroDeRecorridos.Todos
                .Where(r => RutasDe(pagina).Any(x => Normalizar(x) == Normalizar(r.Ruta)))
                .SelectMany(r => r.Pasos)
                .Where(p => !p.EsPortada)
                .Select(p => p.Marca)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var marca in MarcasDe([pagina]).Where(m => !usadas.Contains(m)))
                sobrantes.Add($"  {Relativa(pagina)}: «{marca}» — ningún paso de {recorrido} la usa");
        }

        Assert.True(sobrantes.Count == 0,
            "Marcas puestas en el marcado que ningún paso señala. Quítalas, o escribe el paso que " +
            "les faltaba:\n" + string.Join("\n", sobrantes));
    }

    // ── La maquinaria ────────────────────────────────────────────────────────────

    [Fact]
    public void ElAtributo_SE_LLAMA_IGUAL_EN_CSHARP_QUE_EN_JAVASCRIPT()
    {
        // El mismo literal vive en los dos lados. Cambiarlo en uno solo no rompe la compilación ni
        // ninguna otra prueba: lo que pasa es que ningún paso encuentra su control, todos se saltan
        // y todos los recorridos se quedan enseñando únicamente la portada.
        var js = File.ReadAllText(Path.Combine(RaizDelCliente, "wwwroot", "js", "adminweb.js"));
        var declarado = Regex.Match(js, @"ATRIBUTO:\s*'(?<a>[^']+)'").Groups["a"].Value;

        Assert.False(string.IsNullOrEmpty(declarado),
            "No se encontró ATRIBUTO en adminweb.js: ¿cambió de nombre el envoltorio de recorridos?");
        Assert.Equal(Paso.Atributo, declarado);
    }

    [Fact]
    public void ElNavegador_CARGA_LO_QUE_HACE_FALTA_Y_EN_ORDEN()
    {
        var html = File.ReadAllText(Path.Combine(RaizDelCliente, "wwwroot", "index.html"));

        Assert.Contains("css/driver.css", html);
        Assert.Contains("css/recorridos.css", html);

        // recorridos.css va DESPUÉS de driver.css: es lo que pinta los globos con los colores del
        // tema en vez de con el blanco de fábrica del paquete.
        Assert.True(html.IndexOf("css/driver.css", StringComparison.Ordinal)
                    < html.IndexOf("css/recorridos.css", StringComparison.Ordinal),
            "recorridos.css tiene que cargarse después de driver.css o el globo se queda blanco.");

        // Y driver.js antes que adminweb.js, que es quien lo usa.
        Assert.True(html.IndexOf("js/driver.js", StringComparison.Ordinal)
                    < html.IndexOf("js/adminweb.js", StringComparison.Ordinal),
            "driver.js tiene que cargarse antes que adminweb.js.");
    }

    [Fact]
    public void ElRegistro_ENCUENTRA_LOS_GUIONES_SIN_QUE_NADIE_LOS_APUNTE()
    {
        // El descubrimiento por reflexión es lo que evita una lista central donde apuntarse — el
        // sitio donde, con cinco personas escribiendo recorridos, se pierde alguno resolviendo un
        // conflicto de fusión. Se prueba con fuentes de mentira de este mismo ensamblado, porque los
        // guiones de verdad se escribieron después de la maquinaria.
        var encontrados = RegistroDeRecorridos.Descubrir(typeof(RecorridosGuiadosTests).Assembly);

        Assert.Contains(encontrados, r => r.Ruta == "/de-mentira/uno");
        Assert.Contains(encontrados, r => r.Ruta == "/de-mentira/dos");
        Assert.Contains(encontrados, r => r.Ruta == "/de-mentira/{Id:int}");
    }

    [Theory]
    // La portada.
    [InlineData("", "/", true)]
    // Con y sin barra, con parámetros de consulta y con ancla: todo eso llega desde
    // NavigationManager y todo tiene que dar igual.
    [InlineData("de-mentira/uno", "/de-mentira/uno", true)]
    [InlineData("/de-mentira/uno", "/de-mentira/uno", true)]
    [InlineData("de-mentira/uno/", "/de-mentira/uno", true)]
    [InlineData("De-Mentira/Uno", "/de-mentira/uno", true)]
    [InlineData("de-mentira/uno?filtro=abierto", "/de-mentira/uno", true)]
    [InlineData("de-mentira/uno#seccion", "/de-mentira/uno", true)]
    // Los parámetros.
    [InlineData("de-mentira/42", "/de-mentira/{Id:int}", true)]
    // Y lo que no encaja.
    [InlineData("de-mentira", "", false)]
    [InlineData("de-mentira/uno/mas", "", false)]
    public void ElEmparejador_ENTIENDE_LAS_DIRECCIONES(string direccion, string esperada, bool hay)
    {
        var encontrado = RegistroDeRecorridos.Para(direccion, RecorridosDeMentira);

        if (!hay) Assert.Null(encontrado);
        else Assert.Equal(esperada, encontrado?.Ruta);
    }

    [Fact]
    public void ElEmparejador_PREFIERE_LA_RUTA_LITERAL_A_LA_QUE_LLEVA_PARAMETRO()
    {
        // «/conocimiento/nuevo» y «/conocimiento/{Id:int}» encajan las dos con «conocimiento/nuevo»,
        // igual que le pasa al enrutador de Blazor — que resuelve a favor de la literal. Si aquí se
        // resolviera al revés, la pantalla de escribir un artículo enseñaría el recorrido de leer
        // uno, y sería un fallo raro de encontrar porque el recorrido en sí funciona.
        var recorridos = new List<Recorrido>
        {
            new("/algo/{Id:int}", "Ver algo", Paso.Portada("Ver", "Lo que sea, con su explicación.")),
            new("/algo/nuevo", "Crear algo", Paso.Portada("Crear", "Lo que sea, con su explicación."))
        };

        Assert.Equal("/algo/nuevo", RegistroDeRecorridos.Para("algo/nuevo", recorridos)?.Ruta);
        Assert.Equal("/algo/{Id:int}", RegistroDeRecorridos.Para("algo/7", recorridos)?.Ruta);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private static readonly List<Recorrido> RecorridosDeMentira =
    [
        new("/", "La portada", Paso.Portada("Inicio", "La pantalla con la que se entra a todo.")),
        new("/de-mentira/uno", "Uno", Paso.Portada("Uno", "Una pantalla de mentira para la prueba.")),
        new("/de-mentira/{Id:int}", "Con parámetro",
            Paso.Portada("Ficha", "Una ficha de mentira para la prueba."))
    ];

    /// <summary>
    /// Sube desde la carpeta de salida hasta encontrar el proyecto del cliente. Se busca por marca
    /// —que el archivo exista— y no contando «..», que se rompe el día que cambie la estructura de
    /// carpetas de compilación. Es el mismo camino que usa ProtectorPortableTests.
    /// </summary>
    private static readonly string RaizDelCliente = LocalizarElCliente();

    private static string LocalizarElCliente()
    {
        const string relativa = "src/Web/AdminWeb.Client/AdminWeb.Client.csproj";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato)) return Path.GetDirectoryName(candidato)!;
            carpeta = carpeta.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No se encontró «{relativa}» subiendo desde {AppContext.BaseDirectory}.");
    }

    private static IEnumerable<string> Razor(string subcarpeta)
    {
        var carpeta = Path.Combine(RaizDelCliente, subcarpeta);
        if (!Directory.Exists(carpeta)) return [];

        return Directory.EnumerateFiles(carpeta, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Las pantallas.</summary>
    private static IEnumerable<string> Paginas() => Razor("Paginas");

    /// <summary>
    /// El marcado que está presente en TODAS las pantallas o que se reutiliza en varias: la barra
    /// superior, el menú y los componentes compartidos. Un paso puede señalar legítimamente algo de
    /// aquí —el botón de marcaje de la barra, el selector de columnas de una rejilla—, así que las
    /// marcas de estos archivos valen para cualquier recorrido.
    /// </summary>
    private static IEnumerable<string> MarcadoCompartido() =>
        Razor("Componentes").Concat(Razor("Layout")).Concat(Razor("Recorridos"));

    private static IEnumerable<string> TodoElMarcado() => Paginas().Concat(MarcadoCompartido());

    private static readonly Regex ExpresionMarca = new(
        Paso.Atributo + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.Compiled);

    private static readonly Regex ExpresionPagina = new(
        @"^\s*@page\s+""(?<r>[^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<string> MarcasDe(IEnumerable<string> archivos, bool unicas = true)
    {
        var marcas = archivos
            .SelectMany(a => ExpresionMarca.Matches(File.ReadAllText(a)))
            .Select(m => m.Groups["v"].Value);

        return (unicas ? marcas.Distinct(StringComparer.Ordinal) : marcas).ToList();
    }

    private static IEnumerable<string> RutasDe(string archivoRazor) =>
        ExpresionPagina.Matches(File.ReadAllText(archivoRazor)).Select(m => m.Groups["r"].Value);

    /// <summary>Ruta normalizada → archivo .razor que la declara.</summary>
    private static readonly Dictionary<string, string> PaginasPorRuta = ConstruirMapaDeRutas();

    private static Dictionary<string, string> ConstruirMapaDeRutas()
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var archivo in Paginas())
        {
            foreach (var ruta in RutasDe(archivo)) mapa[Normalizar(ruta)] = archivo;
        }

        return mapa;
    }

    /// <summary>Dónde más aparece una marca, para que el mensaje de fallo sea útil y no un «no está».</summary>
    private static string? DondeEsta(string marca)
    {
        foreach (var archivo in TodoElMarcado())
        {
            if (MarcasDe([archivo]).Contains(marca, StringComparer.Ordinal)) return Relativa(archivo);
        }

        return null;
    }

    private static string Relativa(string archivo) =>
        Path.GetRelativePath(RaizDelCliente, archivo).Replace('\\', '/');

    private static string Normalizar(string ruta)
    {
        var corte = ruta.IndexOfAny(['?', '#']);
        if (corte >= 0) ruta = ruta[..corte];

        return ruta.Trim('/').ToLowerInvariant();
    }
}

/// <summary>
/// Fuentes de mentira para probar el descubrimiento. Viven en el ensamblado de las pruebas, así que
/// el registro de verdad —que solo mira dentro de AdminWeb.Client— nunca las ve.
/// </summary>
public sealed class RecorridosDeMentiraUno : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        new Recorrido("/de-mentira/uno", "Uno",
            Paso.Portada("Uno", "Una pantalla de mentira, con su texto de portada.")),
        new Recorrido("/de-mentira/{Id:int}", "Con parámetro",
            Paso.Portada("Ficha", "Una ficha de mentira, con su texto de portada."))
    ];
}

public sealed class RecorridosDeMentiraDos : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        new Recorrido("/de-mentira/dos", "Dos",
            Paso.Portada("Dos", "Otra pantalla de mentira, con su texto de portada."))
    ];
}
