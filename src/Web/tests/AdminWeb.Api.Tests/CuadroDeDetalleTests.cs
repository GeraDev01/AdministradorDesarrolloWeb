using System.Text.RegularExpressions;
using AdminWeb.Client.Componentes;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// EL CUADRO DE DETALLE DE UNA FILA: lo que se puede comprobar sin navegador.
///
/// <para>Lo que se prueba aquí es el CONTENIDO —<see cref="DetalleDeFila"/>—, que es donde de verdad
/// se puede meter la pata sin que se note: un campo vacío que sale en blanco y se lee como si el
/// cuadro se hubiera roto, o un recorte que vuelva a esconder justo el texto que obligó a abrirlo.
/// El pintado no se prueba —haría falta un navegador— pero sí se comprueban por marcado las dos
/// reglas del cuadro que nada más protege: que es de LECTURA y que el doble clic nunca es la única
/// forma de llegar a él.</para>
///
/// <para>Viven en el proyecto de la API y no en el de Application por lo mismo que
/// <c>RecorridosGuiadosTests</c>: es el único de los dos que ve <c>AdminWeb.Client</c>.</para>
/// </summary>
public class CuadroDeDetalleTests
{
    // ── El contenido ─────────────────────────────────────────────────────────────

    [Fact]
    public void LosCampos_CONSERVAN_SU_ORDEN_Y_SE_SEPARAN_EN_CORTOS_Y_LARGOS()
    {
        var d = new DetalleDeFila("Actividad del pool", "Arreglar el informe mensual")
            .Campo("Tipo", "Bug")
            .Texto("Detalle", "Tres párrafos.")
            .Campo("Puntos", "10")
            .Texto("Notas", "Otro texto largo.");

        Assert.Equal("Actividad del pool", d.Encabezado);
        Assert.Equal("Arreglar el informe mensual", d.Titulo);

        // El orden de declaración se respeta dentro de cada grupo: el cuadro los agrupa para
        // pintarlos, no los reordena.
        Assert.Equal(["Tipo", "Puntos"], d.Cortos.Select(c => c.Etiqueta));
        Assert.Equal(["Detalle", "Notas"], d.Largos.Select(c => c.Etiqueta));

        // Y la lista completa sigue en el orden en que se declaró, sin agrupar.
        Assert.Equal(["Tipo", "Detalle", "Puntos", "Notas"], d.Campos.Select(c => c.Etiqueta));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void UnCampoVacio_SE_PINTA_CON_EL_GUION_Y_QUEDA_MARCADO(string? valor)
    {
        // Vacío se ENSEÑA, no se esconde: el cuadro de una misma rejilla tiene siempre la misma
        // forma y así se aprende dónde mirar. Y va marcado para poder atenuarlo, porque seis guiones
        // a todo color se leen como si dijeran algo.
        var d = new DetalleDeFila("Fila").Campo("Quién la tiene", valor).Texto("Detalle", valor);

        Assert.All(d.Campos, c =>
        {
            Assert.Equal(DetalleDeFila.SinDato, c.Valor);
            Assert.True(c.EstaVacio);
        });
    }

    [Fact]
    public void UnValorQUE_YA_LLEGA_ESCRITO_COMO_EL_GUION_TAMBIEN_ES_UN_HUECO()
    {
        // El caso que se cuela por el diseño del cuadro. Los valores llegan YA ESCRITOS desde los
        // mismos ayudantes que pintan las celdas, y varios de ellos devuelven el guion cuando no hay
        // dato: HorasDelPool.Esfuerzo(null) y HorasDelPool.Limite(null) son exactamente «—».
        //
        // Si eso no contara como hueco, el cuadro de una actividad del pool que nadie ha tomado
        // saldría con «Quién la tiene: —» atenuado y «Esfuerzo: —» a plena tinta: tres huecos con dos
        // pesos distintos, que es justo lo que la marca de vacío existe para evitar. Y falla en
        // silencio — se ve mal, no se rompe nada.
        var d = new DetalleDeFila("Actividad del pool")
            .Campo("Quién la tiene", null)
            .Campo("Esfuerzo", DetalleDeFila.SinDato)
            .Texto("Detalle", $"  {DetalleDeFila.SinDato}  ");

        Assert.All(d.Campos, c =>
        {
            Assert.Equal(DetalleDeFila.SinDato, c.Valor);
            Assert.True(c.EstaVacio);
        });
    }

    [Fact]
    public void UnCampoConDato_NO_SE_MARCA_COMO_VACIO()
    {
        // La comprobación al revés, que es la que evita atenuar un dato de verdad. El «0» tiene que
        // pasar: es un número, no un hueco, y tratarlo como vacío pintaría un guion donde alguien
        // decidió que no había devoluciones.
        var d = new DetalleDeFila("Fila").Campo("Devoluciones", "0").Campo("Estado", "Disponible");

        Assert.All(d.Campos, c => Assert.False(c.EstaVacio));
        Assert.Equal(["0", "Disponible"], d.Campos.Select(c => c.Valor));
    }

    [Fact]
    public void UnTextoMUY_LARGO_SALE_ENTERO_Y_SIN_RECORTAR()
    {
        // LA prueba de este archivo. El cuadro existe porque la celda recorta; si recortara también
        // él, no habría resuelto nada — y sería un defecto invisible: el texto se vería «casi
        // completo» y nadie sabría que falta el final.
        var largo = string.Join(" ", Enumerable.Range(0, 2000).Select(i => $"palabra{i}"));

        var campo = new DetalleDeFila("Fila").Texto("Detalle", largo).Largos.Single();

        Assert.Equal(largo, campo.Valor);
        Assert.EndsWith("palabra1999", campo.Valor);
        Assert.DoesNotContain("…", campo.Valor);
    }

    [Fact]
    public void LosSaltosDeLinea_DEL_TEXTO_LARGO_SE_CONSERVAN()
    {
        // Son la mitad del problema: una descripción de tres párrafos con los saltos comidos vuelve
        // a leerse como un bloque corrido, que es casi tan malo como no verla. Quien la pinta pone
        // el white-space:pre-wrap; aquí lo que se garantiza es que el texto llega con ellos.
        const string tresParrafos = "Primero.\n\nSegundo, con un\nsalto dentro.\n\nTercero.";

        var campo = new DetalleDeFila("Fila").Texto("Detalle", tresParrafos).Largos.Single();

        Assert.Equal(tresParrafos, campo.Valor);
        Assert.Equal(5, campo.Valor.Count(c => c == '\n'));
    }

    [Fact]
    public void SoloSE_QUITAN_LOS_ESPACIOS_DE_LOS_EXTREMOS()
    {
        // Se limpia lo de fuera —un párrafo que llega con dos renglones en blanco delante abriría el
        // cuadro con un hueco— y no se toca NADA de dentro: la sangría de una lista pegada en el
        // detalle es parte de lo que se quiere leer.
        var campo = new DetalleDeFila("Fila")
            .Texto("Detalle", "\n  Uno\n    · dos con sangría\n  tres  \n\n")
            .Largos.Single();

        Assert.Equal("Uno\n    · dos con sangría\n  tres", campo.Valor);
    }

    [Fact]
    public void UnaFilaSIN_TITULAR_NO_INVENTA_NINGUNO()
    {
        // Hay rejillas cuya fila no tiene un campo que la nombre. El cuadro simplemente no pinta el
        // titular; lo que NO puede hacer es rellenarlo con «(sin título)», que se leería como si la
        // fila tuviera un título vacío guardado.
        Assert.Equal("", new DetalleDeFila("Entrada de la bitácora").Titulo);
        Assert.Equal("", new DetalleDeFila("Entrada de la bitácora", "   ").Titulo);
        Assert.Equal("Arreglar el informe", new DetalleDeFila("Fila", "  Arreglar el informe  ").Titulo);
    }

    [Fact]
    public void UnDetalleSIN_CAMPOS_NO_REVIENTA()
    {
        // Un cuadro vacío es una pantalla pobre, no una excepción: pasa en cuanto una pantalla nueva
        // llame al componente antes de declarar sus campos.
        var d = new DetalleDeFila("Fila", "Titular");

        Assert.Empty(d.Campos);
        Assert.Empty(d.Cortos);
        Assert.Empty(d.Largos);
    }

    // ── Las dos reglas que solo protege el marcado ───────────────────────────────

    [Fact]
    public void ElDobleClic_NUNCA_ES_LA_UNICA_FORMA_DE_LLEGAR_AL_DETALLE()
    {
        // Quien navega con el teclado no tiene doble clic, y un doble clic tampoco se ve: sin un
        // control a la vista, la función existe para quien se lo contaron y para nadie más.
        //
        // Falla en silencio y por eso se prueba: una rejilla con doble clic y sin botón funciona
        // perfectamente con el ratón, así que nada la delata hasta que alguien no puede abrirla.
        //
        // Se cuentan SOLO los dobles clic que abren el detalle, no todos. En esta aplicación el
        // doble clic sobre una fila ya significaba otra cosa en seis rejillas —abrir la edición en
        // los catálogos, abrir el hilo en la auditoría del foro—, y ésas ni tienen cuadro de detalle
        // ni tienen por qué tenerlo.
        //
        // Y se pide «al menos uno», no exactamente uno: una rejilla cuyo doble clic ya esté ocupado
        // puede ofrecer el botón del ojo por su cuenta, y eso es correcto — es justamente la salida
        // para los catálogos.
        var descuadres = new List<string>();

        foreach (var archivo in Pantallas())
        {
            var texto = File.ReadAllText(archivo);
            var dobles = Regex.Matches(texto, @"RowDoubleClick\s*=\s*""[^""]*VerDetalleAsync").Count;
            var botones = Regex.Matches(texto, "<BotonDeDetalle").Count;

            if (dobles > botones)
                descuadres.Add($"  {Relativa(archivo)}: {dobles} rejilla(s) abren el detalle con " +
                               $"doble clic y solo hay {botones} botón(es) de detalle");
        }

        Assert.True(descuadres.Count == 0,
            "Cada rejilla que abre el detalle con doble clic tiene que ofrecer también el botón del " +
            "ojo (<BotonDeDetalle>), que es el camino de quien navega con el teclado:\n"
            + string.Join("\n", descuadres));
    }

    [Fact]
    public void ElBotonDelOjo_NO_SE_PONE_SIN_ARMAR_EL_DETALLE()
    {
        // La comprobación al revés. El botón se pinta con un DetalleDeFila que arma la pantalla; si
        // alguien lo copia a una rejilla nueva y no escribe ese armado, lo que se abre es el cuadro
        // por omisión —vacío, con «Detalle» por título— y no falla nada: sale una ventana en blanco.
        var mudas = Pantallas()
            .Where(a => File.ReadAllText(a).Contains("<BotonDeDetalle")
                        && !File.ReadAllText(a).Contains("new DetalleDeFila")
                        // Salvo que se sirva de un armador compartido, como el de los tickets, que
                        // es lo que evita dos cuadros distintos para la misma fila.
                        && !Regex.IsMatch(File.ReadAllText(a), @"Detalle\w*\.De\("))
            .Select(Relativa)
            .ToList();

        Assert.True(mudas.Count == 0,
            "Estas pantallas pintan el botón del detalle pero no arman ninguno, así que abren un " +
            "cuadro vacío:\n  " + string.Join("\n  ", mudas));
    }

    [Fact]
    public void ElCuadro_ES_DE_LECTURA_Y_NO_LLEVA_BOTONES()
    {
        // Editar, retirar, liberar y todo lo demás viven en la fila y en la barra de su pantalla.
        // Repetirlos aquí dejaría dos sitios desde los que disparar la misma acción, y uno de los dos
        // se apagaría el día que cambie la regla de cuándo se puede — sin que nada fallara.
        var cuadro = File.ReadAllText(Path.Combine(RaizDelCliente, "Componentes", "CuadroDeDetalle.razor"));

        // Se mira el MARCADO, no los comentarios: la explicación de arriba nombra los botones.
        var sinComentarios = Regex.Replace(cuadro, @"@\*.*?\*@", "", RegexOptions.Singleline);

        Assert.DoesNotContain("<RadzenButton", sinComentarios);
        Assert.DoesNotContain("@onclick", sinComentarios);
    }

    [Fact]
    public void ElCuadro_SE_CIERRA_CON_ESCAPE_Y_PINCHANDO_FUERA()
    {
        // Las dos son requisitos del cuadro y solo una es el valor de fábrica de Radzen: el clic
        // fuera viene APAGADO y hay que pedirlo. Quitar cualquiera de las dos líneas compila,
        // funciona y deja un cuadro del que solo se sale buscando el aspa.
        var avisos = File.ReadAllText(Path.Combine(RaizDelCliente, "Servicios", "AvisosDeInterfaz.cs"));

        Assert.Matches(@"CloseDialogOnEsc\s*=\s*true", avisos);
        Assert.Matches(@"CloseDialogOnOverlayClick\s*=\s*true", avisos);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sube desde la carpeta de salida hasta encontrar el proyecto del cliente, buscando por marca
    /// —que el archivo exista— y no contando «..». Es el mismo camino que usan
    /// <c>RecorridosGuiadosTests</c> y <c>ProtectorPortableTests</c>.
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

    /// <summary>
    /// Las pantallas. Solo ellas: el botón y el cuadro viven en <c>Componentes</c> y nombran
    /// <c>VerDetalleAsync</c> por dentro, así que contarlos ahí daría un descuadre de mentira.
    /// </summary>
    private static IEnumerable<string> Pantallas() =>
        Directory.EnumerateFiles(Path.Combine(RaizDelCliente, "Paginas"), "*.razor",
                                 SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Relativa(string archivo) =>
        Path.GetRelativePath(RaizDelCliente, archivo).Replace('\\', '/');
}
