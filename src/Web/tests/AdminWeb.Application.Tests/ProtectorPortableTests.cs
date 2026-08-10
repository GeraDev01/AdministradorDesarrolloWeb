using System.Text.RegularExpressions;
using AdminWeb.Infrastructure.Seguridad;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El cifrado de los secretos de configuración.
///
/// <b>Estas pruebas existen por una razón concreta: aquí la avería es silenciosa y tardía.</b> La web
/// y la aplicación de escritorio leen y escriben las MISMAS filas de configuración contra la MISMA
/// base, y lo harán hasta el corte. Si las dos dejaran de entenderse, nada fallaría al compilar ni al
/// guardar: fallaría semanas después, al desplegar, con un «530 User cannot log in» del servidor FTP
/// —porque se estaría mandando texto cifrado ilegible como contraseña—. Es exactamente el fallo que
/// el escritorio ya sufrió una vez con DPAPI y que este formato vino a arreglar.
/// </summary>
public class ProtectorPortableTests
{
    // ── Ida y vuelta ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("una-contraseña-cualquiera")]
    [InlineData("Server=x;Database=y;User Id=z;Password=w;")]
    [InlineData("con acentos, ñ y símbolos: €·@#")]
    [InlineData("x")]
    public void LoQueSeCifra_SeVuelveALeer(string secreto)
    {
        var cifrado = ProtectorPortable.Cifrar(secreto);

        Assert.NotEqual(secreto, cifrado);
        Assert.Equal(secreto, ProtectorPortable.Descifrar(cifrado));
    }

    [Fact]
    public void CadaCifradoEsDistinto_AunqueElSecretoSeaElMismo()
    {
        // El vector de inicialización es nuevo cada vez. Sin eso, dos filas con la misma contraseña
        // se verían idénticas en la base y bastaría comparar para saberlo.
        var uno = ProtectorPortable.Cifrar("misma");
        var otro = ProtectorPortable.Cifrar("misma");

        Assert.NotEqual(uno, otro);
        Assert.Equal("misma", ProtectorPortable.Descifrar(uno));
        Assert.Equal("misma", ProtectorPortable.Descifrar(otro));
    }

    // ── Lo que no se puede leer ─────────────────────────────────────────────────

    [Fact]
    public void UnValorHeredadoDeWindows_SeReconoceComoTal()
    {
        // Los de DPAPI no llevan prefijo. No se pueden descifrar fuera de la máquina que los escribió
        // —y menos en un servidor Linux—, así que lo único correcto es decir «hay que recapturarlo»
        // en vez de enseñarlo como si nunca se hubiera configurado.
        const string heredado = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA";

        Assert.True(ProtectorPortable.EsHeredadoDeWindows(heredado));
        Assert.False(ProtectorPortable.EsPortable(heredado));
        Assert.Null(ProtectorPortable.Descifrar(heredado));
    }

    [Theory]
    [InlineData("ADW1:no-es-base64-válido")]
    [InlineData("ADW1:AAAA")]           // demasiado corto para llevar siquiera el IV
    [InlineData("ADW1:")]
    public void UnSecretoCorrupto_DevuelveNada_EnVezDeBasura(string cifrado)
    {
        // Devolver basura acabaría mandándose como contraseña a un servidor de verdad.
        Assert.Null(ProtectorPortable.Descifrar(cifrado));
    }

    [Fact]
    public void NadaYVacio_NoRevientan()
    {
        Assert.Null(ProtectorPortable.Descifrar(null));
        Assert.Null(ProtectorPortable.Descifrar(""));
        Assert.False(ProtectorPortable.EsHeredadoDeWindows(null));
        Assert.False(ProtectorPortable.EsHeredadoDeWindows(""));
    }

    // ── La compatibilidad con el escritorio ─────────────────────────────────────

    [Fact]
    public void LaSemilla_ES_LA_MISMA_QUE_LA_DEL_ESCRITORIO()
    {
        // LA prueba de este archivo. No se puede referenciar el proyecto de escritorio —es
        // net10.0-windows y arrastra WinForms—, así que se lee su código fuente. Es el mismo recurso
        // que el propio escritorio usa para comprobar que su script de publicación cifra igual.
        //
        // Si esta prueba se pone roja, NO la ajustes: significa que alguien cambió la semilla en un
        // lado, y con eso las dos aplicaciones dejarían de leerse los secretos.
        var fuente = File.ReadAllText(RutaDelProtectorDeEscritorio());

        var semillaDelEscritorio = Regex.Match(fuente, @"KeySeed\s*=\s*""([^""]+)""").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(semillaDelEscritorio),
            "No se encontró KeySeed en el protector del escritorio: ¿cambió de nombre o de sitio?");
        Assert.Equal(ProtectorPortable.Semilla, semillaDelEscritorio);
    }

    [Fact]
    public void ElPrefijoDelFormato_ES_EL_MISMO_QUE_EL_DEL_ESCRITORIO()
    {
        // El prefijo es lo que distingue este formato del heredado de DPAPI. Si dejaran de coincidir,
        // cada aplicación creería que lo del otro es «de otro esquema» y no lo descifraría.
        var fuente = File.ReadAllText(RutaDelProtectorDeEscritorio());

        var prefijo = Regex.Match(fuente, @"Prefijo\s*=\s*""([^""]+)""").Groups[1].Value;

        Assert.Equal(ProtectorPortable.Prefijo, prefijo);
    }

    /// <summary>
    /// Sube desde la carpeta de salida de las pruebas hasta la raíz del repositorio. Se busca por
    /// marca —la existencia del archivo— y no con un número fijo de «..», que se rompería el día que
    /// cambie la estructura de carpetas de compilación.
    /// </summary>
    private static string RutaDelProtectorDeEscritorio()
    {
        const string relativa = @"Administrador_Desarrollo_Web/Security/SharedSecretProtector.cs";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato)) return candidato;
            carpeta = carpeta.Parent;
        }

        throw new FileNotFoundException(
            $"No se encontró «{relativa}» subiendo desde {AppContext.BaseDirectory}. " +
            "Si el escritorio se retiró del repositorio, esta prueba ya no aplica y hay que quitarla " +
            "junto con el cifrado compatible.");
    }
}
