using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// QUIÉN PUEDE CONVERTIR TRABAJO EN PUNTOS. Es una lista cerrada, y ésta es la prueba que la cierra.
///
/// <para><b>Por qué existe, y por qué es la más importante de todo el lote.</b> Cuando se decidió
/// simplificar el desempeño, el sistema tenía CUATRO caminos por los que entraban puntos —la
/// autocalificación, aceptar una actividad del pool, calificar una actividad libre y publicar un
/// artículo—, cada uno con sus propias reglas sobre qué criterios valen, quién decide y en qué estado
/// nace la entrada. Ninguno de los cuatro se añadió con mala intención: cada uno era razonable por su
/// cuenta, y juntos produjeron la pregunta que había que eliminar — «esto que acabo de hacer, ¿dónde
/// lo registro?».</para>
///
/// <para>Reducirlos es un trabajo que se hace una vez. <b>Mantenerlos reducidos no.</b> Sin algo que
/// lo vigile, dentro de dos años vuelve a haber cuatro por el mismo camino por el que llegaron los de
/// hoy: alguien necesita abonar unos puntos desde una pantalla nueva, escribe un
/// <c>new PointEntry</c>, y nada falla. «Un solo camino» pasa a ser una frase de un documento.</para>
///
/// <para><b>Cómo lo vigila.</b> Leyendo el código fuente y comparando el conjunto de archivos que
/// INSERTAN una <c>PointEntry</c> contra la lista de abajo. No mide cuántas veces aparece —eso
/// cambiaría con cualquier refactor honesto— sino EN QUÉ ARCHIVOS aparece. Añadir un productor rompe
/// la prueba; quitar uno también, porque una lista que sobra miente igual que una que falta.</para>
///
/// <para><b>Se busca la INSERCIÓN y no el <c>new</c></b>, y la diferencia no es cosmética: costó un
/// falso verde al escribir esta prueba. El endpoint de la autocalificación arma su borrador con
/// <c>new()</c> de tipo inferido —<c>private static PointEntry ABorrador(…) =&gt; new() { … }</c>—, que
/// ningún rastreo de «new PointEntry» encuentra. Y da igual: <b>un objeto en memoria no es una fila</b>.
/// Lo que crea puntos es meterlo en el <c>DbSet</c>, y eso solo se escribe de una forma. Se conserva
/// además el rastreo del <c>new</c>, para que un archivo que construya la entidad y la inserte por
/// otro camino tampoco pase inadvertido.</para>
///
/// <para><b>Un «APAGADO» en la lista no es una puerta abierta.</b> Dos de los cuatro caminos se
/// retiraron con una guarda en la primera línea del método y el código de abajo se conservó —lo
/// comparten operaciones que siguen vivas, y borrarlo obligaría a reescribirlo si algún día se
/// reabre—, así que el rastreo los sigue encontrando. Se quedan en la lista porque quitarlos la
/// pondría roja por el otro extremo, y llevan el motivo escrito para que la lista no se lea como
/// «aquí hay cuatro maneras de pagar». Si algún día se borra ese código, se borra también su
/// entrada, y la prueba avisará sola si se olvida.</para>
///
/// <para>Se recorren los DOS proyectos donde podría aparecer —la capa de aplicación y la API— y no
/// solo los servicios: si algún día un endpoint insertara la fila él mismo, ése es exactamente el
/// caso que hay que cazar.</para>
///
/// <para>Hay precedente de prueba que lee archivos del disco: <c>RecorridosCoberturaTests</c> recorre
/// las pantallas para exigir que cada una tenga su recorrido guiado. Mismo mecanismo y mismo motivo:
/// hay cosas que no se pueden comprobar ejecutando código, solo mirándolo.</para>
/// </summary>
public class ProductoresDePuntosTests
{
    /// <summary>
    /// Los ÚNICOS sitios donde se puede construir una <c>PointEntry</c>, con el porqué de cada uno.
    ///
    /// <para>Añadir una entrada aquí no es un trámite: es la decisión de abrir un quinto camino de
    /// puntos. Si hace falta, se toma a propósito y se escribe al lado por qué ese trabajo no cabe en
    /// una actividad del pool.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Permitidos = new()
    {
        ["Services/PoolActivityService.cs"] =
            "EL CAMINO. Aceptar la entrega de una actividad del pool abona los puntos que la matriz " +
            "congeló antes de que nadie la tomara. Es el único que reparte trabajo encargado.",

        ["Services/ConocimientoService.cs"] =
            "LA EXCEPCIÓN DECLARADA. Publicar un artículo paga una vez y para siempre. No pasa por el " +
            "pool a propósito: la invariante del pool es que el precio se fija ANTES de trabajar, y un " +
            "artículo no se encarga —«escribe sobre X, vale 8»—, lo que vale es el artículo. No tiene " +
            "reclamo, ni plazo, ni checklist, ni cronómetro, y no debe crecerlos.",

        ["Services/PerformanceScoringService.cs"] =
            "APAGADO: la autocalificación. El desarrollador registraba algo ya hecho y el líder lo " +
            "aprobaba; hoy RegistrarAutocalificacionAsync rechaza en su primera línea y manda a " +
            "proponer al pool. El código de abajo se conserva sin usar —corregir y replicar siguen " +
            "vivos y comparten su validación— así que el rastreo lo sigue encontrando. Está en la " +
            "lista para que la prueba no se ponga roja por algo que ya no puede pagar, y con el " +
            "«APAGADO» delante para que nadie lo lea como una puerta abierta.",

        ["Services/DevActivityService.cs"] =
            "APAGADO: la actividad libre calificada. El líder ponía puntos a un trabajo que no venía " +
            "de ningún encargo, mirando el tiempo medido y las evidencias; hoy CalificarAsync rechaza " +
            "en su primera línea. Lo que había que reconocer se publica al pool; lo que había que " +
            "penalizar se aplica como descuento. Mismo caso que el de arriba: el código se conserva y " +
            "por eso el archivo sigue declarado.",

        ["Demo/DatosDeDemostracion.cs"] =
            "NO ES UN CAMINO: es la siembra de la demostración, que replica a mano lo que harían los " +
            "servicios para que la aplicación se pueda enseñar con datos que se parecen a los reales.",
    };

    /// <summary>La capa de aplicación, que es la referencia contra la que se nombran los archivos.</summary>
    private static readonly string RaizDeAplicacion = LocalizarProyecto("AdminWeb.Application");

    /// <summary>Y la API, porque un endpoint que insertara la fila él mismo es justo lo que hay que cazar.</summary>
    private static readonly string RaizDeLaApi = LocalizarProyecto("AdminWeb.Api");

    /// <summary>
    /// Sube desde la carpeta de salida hasta encontrar el proyecto, por marca y no contando «..».
    /// Mismo camino que usan RecorridosCoberturaTests y ProtectorPortableTests.
    /// </summary>
    private static string LocalizarProyecto(string nombre)
    {
        var relativa = $"src/Web/{nombre}/{nombre}.csproj";

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

    private static IEnumerable<string> Fuentes() =>
        new[] { RaizDeAplicacion, RaizDeLaApi }
            .SelectMany(raiz => Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Un archivo PRODUCE puntos si mete una entrada en el <c>DbSet</c>. «PointEntries.Add» alcanza
    /// también a <c>AddRange</c>, que es como siembra la demostración.
    /// </summary>
    private static bool ProducePuntos(string archivo)
    {
        var texto = File.ReadAllText(archivo);
        return texto.Contains("PointEntries.Add") || texto.Contains("new PointEntry");
    }

    /// <summary>
    /// El nombre con el que se declara el archivo en la lista: relativo a la capa de aplicación, y
    /// con el proyecto delante cuando viene de la API — así se distingue de un mismo nombre de
    /// archivo en las dos.
    /// </summary>
    private static string Relativa(string archivo) =>
        archivo.StartsWith(RaizDeLaApi, StringComparison.OrdinalIgnoreCase)
            ? "AdminWeb.Api/" + Path.GetRelativePath(RaizDeLaApi, archivo).Replace('\\', '/')
            : Path.GetRelativePath(RaizDeAplicacion, archivo).Replace('\\', '/');

    /// <summary>
    /// EL CINTURÓN CONTRA EL FALSO VERDE, y va primero por el mismo motivo que en
    /// <c>RecorridosCoberturaTests</c>: la comprobación de abajo es de la forma «no encuentres nada
    /// que no esté en la lista», y sobre un conjunto VACÍO pasa sola. Si el rastreo dejara de
    /// funcionar —cambia la ruta del proyecto, alguien mueve los servicios, la salida de las pruebas
    /// se reorganiza— la prueba se pondría verde anunciando que no hay ningún productor de puntos
    /// justo el día en que dejó de mirar.
    /// </summary>
    [Fact]
    public void ElRastreo_ENCUENTRA_DE_VERDAD_LOS_PRODUCTORES()
    {
        Assert.NotEmpty(Fuentes());
        Assert.Contains(Fuentes(), ProducePuntos);
    }

    /// <summary>
    /// Nadie más construye una <c>PointEntry</c>.
    ///
    /// <para>Si esta prueba se pone roja al añadir código, la pregunta no es «cómo la callo» sino
    /// «¿por qué este trabajo no cabe en una actividad del pool?». Casi siempre cabe: publicarla,
    /// que alguien la tome, verificarla. Cuando de verdad no cabe —el caso del artículo—, se añade a
    /// la lista con el motivo escrito.</para>
    /// </summary>
    [Fact]
    public void NadieMas_ConstruyeUnaEntradaDePuntos()
    {
        var encontrados = Fuentes()
            .Where(ProducePuntos)
            .Select(Relativa)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var esperados = Permitidos.Keys.OrderBy(f => f, StringComparer.Ordinal).ToList();

        // Los dos sentidos importan. Uno de más es un quinto camino de puntos que nadie decidió; uno
        // de menos es una lista que ya no describe el código y en la que nadie va a volver a confiar.
        var deMas = encontrados.Except(esperados).ToList();
        var deMenos = esperados.Except(encontrados).ToList();

        Assert.True(deMas.Count == 0,
            "Hay un camino de puntos NUEVO que no está declarado: " + string.Join(", ", deMas) +
            ". Antes de añadirlo a la lista de ProductoresDePuntosTests, contesta por qué ese trabajo " +
            "no cabe en una actividad del pool — que es el único camino que reparte trabajo encargado.");

        Assert.True(deMenos.Count == 0,
            "La lista de productores declara archivos que ya no construyen ninguna entrada de puntos: " +
            string.Join(", ", deMenos) + ". Quítalos: una lista que sobra miente igual que una que falta.");
    }

    /// <summary>
    /// Y cada permitido lleva su motivo escrito. Una lista de rutas sin explicación se convierte en
    /// un trámite —se le añade una línea y ya— que es justo lo que esta prueba viene a evitar.
    /// </summary>
    [Fact]
    public void CadaProductorPermitido_TieneSuMotivoEscrito()
    {
        Assert.All(Permitidos, par =>
        {
            Assert.False(string.IsNullOrWhiteSpace(par.Value), $"«{par.Key}» no dice por qué está aquí.");
            Assert.True(par.Value.Length >= 80,
                $"El motivo de «{par.Key}» es demasiado corto para explicar nada: {par.Value}");
        });
    }
}
