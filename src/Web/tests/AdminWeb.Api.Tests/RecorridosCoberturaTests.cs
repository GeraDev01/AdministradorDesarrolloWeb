using System.Text.RegularExpressions;
using AdminWeb.Client.Recorridos;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// LA COBERTURA DE LOS RECORRIDOS: que no se quede ninguna pantalla sin el suyo.
///
/// <para>Esto es lo que <c>RecorridosGuiadosTests</c> no puede comprobar, y la diferencia importa.
/// Aquella comprueba que los recorridos ESCRITOS son correctos —que cada paso señala un control que
/// existe—; ésta comprueba que están escritos TODOS. Son dos fallos distintos: el primero es un
/// recorrido que miente, el segundo es un recorrido que nunca llegó a existir. El segundo no deja
/// ningún rastro, porque una pantalla sin recorrido se ve exactamente igual que una pantalla cuyo
/// recorrido nadie ha escrito todavía: el botón de la barra, apagado.</para>
///
/// <para><b>Qué pantalla está exenta, y por qué no es una lista escrita a mano.</b> Una lista de
/// excepciones envejece igual de mal que lo que pretende proteger: se le añade una pantalla «por
/// ahora» y ahí se queda. En lugar de eso, la exención se DEDUCE del marcado — está exenta la
/// pantalla cuyo armazón no lleva el botón que lanza los recorridos, porque en ella el recorrido
/// sería inalcanzable por definición. Hoy eso son las dos que van con <c>LayoutVacio</c>, el armazón
/// sin barra superior de la pantalla de acceso y la del segundo factor. El día que alguien le ponga
/// barra a ese armazón, esta prueba pedirá sus recorridos sola.</para>
/// </summary>
public class RecorridosCoberturaTests
{
    [Fact]
    public void ElRegistro_ENCUENTRA_DE_VERDAD_LOS_GUIONES_ESCRITOS()
    {
        // EL CINTURÓN CONTRA EL FALSO VERDE, y es el motivo principal de que exista este archivo.
        //
        // Las comprobaciones de RecorridosGuiadosTests son todas de la forma «recorre los recorridos
        // y no encuentres ningún problema». Sobre una lista VACÍA, las seis pasan — no encuentran
        // ningún paso roto porque no hay ningún paso. O sea que si el descubrimiento por reflexión
        // dejara de funcionar (se recupera el recorte de ensamblados, alguien renombra la interfaz,
        // los guiones cambian de proyecto), la suite entera se pondría verde anunciando que los
        // recorridos están todos perfectos, justo el día en que no queda ninguno.
        //
        // Sin número exacto a propósito: escribir aquí «61» obligaría a tocar esta prueba cada vez
        // que se añade una pantalla, que es la clase de fricción que acaba con alguien bajando el
        // número en vez de escribir el recorrido. Lo que hace falta saber es que el descubrimiento
        // encuentra algo; de que estén TODOS se encarga la prueba de abajo.
        Assert.NotEmpty(RegistroDeRecorridos.Todos);
    }

    [Fact]
    public void ElArmazonPorOmision_LLEVA_EL_BOTON_QUE_LANZA_LOS_RECORRIDOS()
    {
        // La prueba de cobertura da por hecho que una pantalla sin @layout propio SÍ puede lanzar su
        // recorrido, porque hereda el armazón principal. Si alguien quitara el botón de ahí, aquella
        // seguiría exigiendo recorridos para pantallas donde ya no habría forma de lanzarlos, y el
        // motivo real —que la ayuda desapareció de toda la aplicación— no lo diría ninguna prueba.
        var principal = Path.Combine(RaizDelCliente, "Layout", "MainLayout.razor");

        Assert.True(File.Exists(principal), $"No se encontró el armazón principal en {principal}.");
        Assert.Contains("<BotonDeRecorrido", File.ReadAllText(principal), StringComparison.Ordinal);
    }

    [Fact]
    public void TodaPantalla_ALCANZABLE_DESDE_LA_BARRA_TIENE_SU_RECORRIDO()
    {
        var sinRecorrido = new List<string>();

        foreach (var pagina in Paginas())
        {
            if (!LlevaBotonDeRecorrido(pagina)) continue;

            foreach (var ruta in RutasDe(pagina))
            {
                if (RegistroDeRecorridos.Para(ruta) is not null) continue;

                sinRecorrido.Add($"  {Relativa(pagina)}  →  @page \"{ruta}\" no tiene recorrido");
            }
        }

        Assert.True(sinRecorrido.Count == 0,
            $"Hay {sinRecorrido.Count} pantalla(s) cuyo botón de recorrido está apagado porque nadie " +
            "escribió su guion. Se arregla añadiendo el recorrido a una clase de " +
            "Recorridos/Guiones/ (cualquiera que implemente IFuenteDeRecorridos; se descubren solas):\n"
            + string.Join("\n", sinRecorrido));
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Si la pantalla puede enseñar el botón de los recorridos. Sin <c>@layout</c> propio usa el
    /// armazón principal, que lo lleva; con uno propio, depende de que ese armazón lo pinte.
    /// </summary>
    private static bool LlevaBotonDeRecorrido(string pagina)
    {
        var declarado = Regex.Match(
            File.ReadAllText(pagina), @"^\s*@layout\s+(?<l>[\w.]+)", RegexOptions.Multiline);

        if (!declarado.Success) return true;

        // «@layout Layout.LayoutVacio» → LayoutVacio.razor. El armazón se busca por su nombre simple
        // porque es como están puestos todos en la carpeta.
        var nombre = declarado.Groups["l"].Value.Split('.').Last();
        var archivo = Path.Combine(RaizDelCliente, "Layout", nombre + ".razor");

        // Un armazón que no se encuentra se trata como que SÍ lleva el botón: ante la duda, esta
        // prueba pide el recorrido en vez de callarse. Una exención silenciosa por una ruta mal
        // resuelta es justo lo que este archivo existe para impedir.
        return !File.Exists(archivo)
               || File.ReadAllText(archivo).Contains("<BotonDeRecorrido", StringComparison.Ordinal);
    }

    private static readonly Regex ExpresionPagina = new(
        @"^\s*@page\s+""(?<r>[^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

    private static IEnumerable<string> RutasDe(string pagina) =>
        ExpresionPagina.Matches(File.ReadAllText(pagina)).Select(m => m.Groups["r"].Value);

    private static IEnumerable<string> Paginas()
    {
        var carpeta = Path.Combine(RaizDelCliente, "Paginas");

        return Directory.EnumerateFiles(carpeta, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static string Relativa(string archivo) =>
        Path.GetRelativePath(RaizDelCliente, archivo).Replace('\\', '/');

    /// <summary>
    /// Sube desde la carpeta de salida hasta encontrar el proyecto del cliente, por marca y no
    /// contando «..». Mismo camino que usan RecorridosGuiadosTests y ProtectorPortableTests.
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
}
