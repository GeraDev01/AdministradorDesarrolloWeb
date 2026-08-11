using System.Text;
using AdminWeb.Domain.Security;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El cálculo del segundo factor, probado contra el estándar y no contra sí mismo.
///
/// <para><b>Por qué los vectores del RFC son imprescindibles aquí.</b> Una implementación de TOTP
/// escrita a mano puede ser internamente coherente y estar mal: si el contador se escribe con los
/// bytes al revés, o se toman los bits de otra posición, el código sigue siendo de seis dígitos y
/// sigue cambiando cada treinta segundos. Comprobarlo contra sí mismo pasaría en verde y ningún
/// teléfono del mundo mostraría lo mismo que el servidor. Los vectores del RFC 6238 son la única
/// prueba de que esto habla el idioma de las aplicaciones del teléfono.</para>
/// </summary>
public class TotpYBase32Tests
{
    /// <summary>
    /// El secreto de los vectores del RFC 6238: los caracteres «12345678901234567890» en ASCII, que
    /// son exactamente los 20 bytes que pide HMAC-SHA1.
    /// </summary>
    private static byte[] SecretoDelRfc => Encoding.ASCII.GetBytes("12345678901234567890");

    // ── Base32 ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los vectores del RFC 4648, sin el relleno con «=» que aquí no se emite (ver Base32).
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_CoincideConLosVectoresDelEstandar(string entrada, string esperado)
    {
        Assert.Equal(esperado, Base32.Codificar(Encoding.ASCII.GetBytes(entrada)));
    }

    [Fact]
    public void Base32_DaLaVueltaSinPerderNiUnByte()
    {
        // Se prueban todas las longitudes de 1 a 40: los errores de reagrupar bits de 8 en 5 salen
        // justo en las longitudes que no son múltiplo de 5, no en las cómodas.
        for (int largo = 1; largo <= 40; largo++)
        {
            var original = new byte[largo];
            for (int i = 0; i < largo; i++) original[i] = (byte)(i * 7 + largo);

            var vuelta = Base32.Decodificar(Base32.Codificar(original));

            Assert.NotNull(vuelta);
            Assert.Equal(original, vuelta);
        }
    }

    [Fact]
    public void Base32_ToleraLoQueTecleaUnaPersona()
    {
        // Minúsculas, espacios, guiones y el relleno: todo eso llega cuando alguien copia el secreto
        // de una pantalla. Rechazarlo sería rechazar un secreto que está bien escrito.
        var esperado = Base32.Decodificar("MZXW6YTBOI");

        Assert.Equal(esperado, Base32.Decodificar("mzxw6ytboi"));
        Assert.Equal(esperado, Base32.Decodificar("MZXW 6YTB OI"));
        Assert.Equal(esperado, Base32.Decodificar("MZXW-6YTB-OI"));
        Assert.Equal(esperado, Base32.Decodificar("MZXW6YTBOI======"));
    }

    [Fact]
    public void Base32_LoQueNoEsBase32_SeRechazaEnVezDeInventarBytes()
    {
        // El 0, el 1, la I y la O no están en el alfabeto. Devolver bytes «aproximados» acabaría en
        // un secreto silenciosamente distinto del que la persona tiene en el teléfono.
        Assert.Null(Base32.Decodificar("MZXW0YTB"));
        Assert.Null(Base32.Decodificar("MZXW1YTB"));
        Assert.Null(Base32.Decodificar("¿qué?"));
        Assert.Null(Base32.Decodificar(null));
        Assert.Null(Base32.Decodificar("   "));
    }

    // ── TOTP contra el estándar ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Los vectores del apéndice B del RFC 6238, recortados a seis dígitos: el RFC los publica con
    /// ocho, y los seis de la derecha son los que muestra la aplicación del teléfono.
    /// </summary>
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Totp_CoincideConLosVectoresDelRfc6238(long segundosUnix, string codigoEsperado)
    {
        var instante = DateTimeOffset.FromUnixTimeSeconds(segundosUnix);

        Assert.Equal(codigoEsperado, Totp.Calcular(SecretoDelRfc, Totp.VentanaDe(instante)));
    }

    [Fact]
    public void Totp_ElCodigoSiempreTieneSeisDigitos_AunqueEmpiecePorCero()
    {
        // Uno de cada diez códigos empieza por cero. Sin relleno saldría de cinco dígitos y no
        // coincidiría con el del teléfono justo en ese diez por ciento de los casos, que es la
        // clase de fallo que se reporta como «a veces no funciona».
        var secreto = Totp.GenerarSecreto();

        for (long ventana = 0; ventana < 2000; ventana++)
        {
            var codigo = Totp.Calcular(secreto, ventana);
            Assert.Equal(Totp.Digitos, codigo.Length);
            Assert.All(codigo, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Totp_ElSecretoSonCientoSesentaBits()
    {
        Assert.Equal(20, Totp.GenerarSecreto().Length);

        // Y su forma tecleable son 32 caracteres exactos, sin relleno que escapar en el URI.
        var enBase32 = Totp.GenerarSecretoEnBase32();
        Assert.Equal(32, enBase32.Length);
        Assert.DoesNotContain('=', enBase32);
    }

    [Fact]
    public void Totp_DosSecretosSeguidosNoSeParecen()
    {
        Assert.NotEqual(Totp.GenerarSecretoEnBase32(), Totp.GenerarSecretoEnBase32());
    }

    // ── Tolerancia de reloj ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Totp_ElCodigoDeLaVentanaAnteriorSigueValiendo()
    {
        // El reloj del teléfono se atrasa. Sin esta tolerancia, la persona teclea bien y el sistema
        // le dice que no, que es un fallo imposible de diagnosticar por teléfono.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var codigoDeAntes = Totp.Calcular(secreto, Totp.VentanaDe(ahora) - 1);

        var resultado = Totp.Verificar(secreto, codigoDeAntes, null, ahora);

        Assert.True(resultado.Valido);
        Assert.Equal(Totp.VentanaDe(ahora) - 1, resultado.Ventana);
    }

    [Fact]
    public void Totp_ElCodigoDeLaVentanaSiguienteSigueValiendo()
    {
        // Y se adelanta, que pasa igual de a menudo.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var codigoDeDespues = Totp.Calcular(secreto, Totp.VentanaDe(ahora) + 1);

        Assert.True(Totp.Verificar(secreto, codigoDeDespues, null, ahora).Valido);
    }

    [Fact]
    public void Totp_MasDeUnaVentanaDeDesfase_YaNoVale()
    {
        // El límite es tan importante como la tolerancia: cada ventana de más alarga la vida de un
        // código robado. Con ±1 vive como mucho minuto y medio.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;

        Assert.False(Totp.Verificar(secreto, Totp.Calcular(secreto, Totp.VentanaDe(ahora) - 2), null, ahora).Valido);
        Assert.False(Totp.Verificar(secreto, Totp.Calcular(secreto, Totp.VentanaDe(ahora) + 2), null, ahora).Valido);
    }

    // ── Antirrepetición ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Totp_UnCodigoYaAceptado_NoVuelveAValer()
    {
        // Es LA protección contra quien mira por encima del hombro: sin ella, un código visto tiene
        // hasta minuto y medio de vida útil para quien lo copie.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var ventana = Totp.VentanaDe(ahora);
        var codigo = Totp.Calcular(secreto, ventana);

        var primera = Totp.Verificar(secreto, codigo, null, ahora);
        Assert.True(primera.Valido);

        // La segunda vez llega con la ventana ya anotada, que es lo que hace quien acepta el código.
        var segunda = Totp.Verificar(secreto, codigo, primera.Ventana, ahora);

        Assert.False(segunda.Valido);
        Assert.Equal(MotivoDelCodigo.YaSeUso, segunda.Motivo);
    }

    [Fact]
    public void Totp_UnCodigoViejo_TampocoValeAunqueEsteDentroDeLaTolerancia()
    {
        // El caso fino: se aceptó el de la ventana actual y llega el de la anterior, que la
        // tolerancia dejaría pasar. No debe pasar: es un código todavía más viejo que el ya usado.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var ventanaActual = Totp.VentanaDe(ahora);

        var resultado = Totp.Verificar(secreto, Totp.Calcular(secreto, ventanaActual - 1), ventanaActual, ahora);

        Assert.False(resultado.Valido);
        Assert.Equal(MotivoDelCodigo.YaSeUso, resultado.Motivo);
    }

    [Fact]
    public void Totp_TrasUnCodigoUsado_ElSiguienteSiVale()
    {
        // La antirrepetición no puede dejar la cuenta inservible: solo se rechaza lo ya usado y lo
        // anterior, nunca lo que viene.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var ventana = Totp.VentanaDe(ahora);

        var resultado = Totp.Verificar(secreto, Totp.Calcular(secreto, ventana + 1), ventana, ahora);

        Assert.True(resultado.Valido);
        Assert.Equal(ventana + 1, resultado.Ventana);
    }

    // ── Lo que teclea una persona ─────────────────────────────────────────────────────────────

    [Fact]
    public void Totp_ElCodigoConElEspacioDeEnMedio_SeAcepta()
    {
        // Las aplicaciones del teléfono muestran «123 456» y quien lo copia se lleva el espacio.
        var secreto = Totp.GenerarSecreto();
        var ahora = DateTimeOffset.UtcNow;
        var codigo = Totp.Calcular(secreto, Totp.VentanaDe(ahora));
        var conEspacio = codigo[..3] + " " + codigo[3..];

        Assert.True(Totp.Verificar(secreto, conEspacio, null, ahora).Valido);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]      // cinco dígitos
    [InlineData("1234567")]    // siete
    [InlineData("12345a")]     // una letra
    public void Totp_LoQueNoTieneFormaDeCodigo_SeRechazaPorFormato(string? tecleado)
    {
        var resultado = Totp.Verificar(Totp.GenerarSecreto(), tecleado, null, DateTimeOffset.UtcNow);

        Assert.False(resultado.Valido);
        Assert.Equal(MotivoDelCodigo.FormatoInvalido, resultado.Motivo);
    }

    [Fact]
    public void Totp_SinSecreto_NoValidaNada()
    {
        // Una cuenta sin secreto legible no puede aceptar ningún código: si esto devolviera cierto
        // alguna vez, el segundo factor sería un adorno.
        Assert.False(Totp.Verificar(null, "123456", null, DateTimeOffset.UtcNow).Valido);
        Assert.False(Totp.Verificar([], "123456", null, DateTimeOffset.UtcNow).Valido);
    }

    [Fact]
    public void Totp_ElCodigoDeOtroSecreto_NoVale()
    {
        var ahora = DateTimeOffset.UtcNow;
        var mio = Totp.GenerarSecreto();
        var ajeno = Totp.GenerarSecreto();

        var resultado = Totp.Verificar(mio, Totp.Calcular(ajeno, Totp.VentanaDe(ahora)), null, ahora);

        Assert.False(resultado.Valido);
        Assert.Equal(MotivoDelCodigo.NoCoincide, resultado.Motivo);
    }

    [Fact]
    public void Totp_LosSegundosQueLeQuedanAlCodigo_VanDeTreintaAUno()
    {
        // Lo usa la pantalla para decir «espera al siguiente» en lugar de dejar que la persona
        // teclee justo cuando el código cambia.
        var baseUnix = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 / 30 * 30);

        Assert.Equal(30, Totp.SegundosQueLeQuedanAlCodigo(baseUnix));
        Assert.Equal(1, Totp.SegundosQueLeQuedanAlCodigo(baseUnix.AddSeconds(29)));
    }

    // ── El URI del código QR ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Uri_TieneLaFormaQueEsperaLaAplicacionDelTelefono()
    {
        var secreto = Totp.GenerarSecretoEnBase32();

        var uri = UriDeSegundoFactor.Construir("ana", secreto);

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secreto}", uri);
        // El emisor va dos veces —en la etiqueta y como parámetro— porque unas aplicaciones leen
        // una cosa y otras la otra.
        Assert.Contains("issuer=", uri);
        Assert.Contains(":ana?", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Uri_UnUsuarioConCaracteresRaros_NoPartelaEtiqueta()
    {
        // Los dos puntos separan emisor de cuenta DENTRO de la etiqueta: sin escapar, un usuario
        // que los llevara haría que el teléfono leyera un emisor que no es.
        var uri = UriDeSegundoFactor.Construir("a:b c", Totp.GenerarSecretoEnBase32());

        Assert.Contains("%3Ab%20c?", uri);
    }

    // ── Códigos de rescate ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rescate_SonOchoYNoSeRepiten()
    {
        var codigos = CodigosDeRescate.Generar();

        Assert.Equal(8, codigos.Count);
        Assert.Equal(8, codigos.Distinct().Count());
        // Y dos tandas seguidas tampoco coinciden en nada.
        Assert.Empty(codigos.Intersect(CodigosDeRescate.Generar()));
    }

    [Fact]
    public void Rescate_NoLlevanCaracteresQueSeConfundenAlLeerlos()
    {
        // Se leen de un papel meses después: un uno y una ele ahí son una cuenta perdida.
        foreach (var codigo in CodigosDeRescate.Generar())
        {
            Assert.DoesNotContain('I', codigo);
            Assert.DoesNotContain('O', codigo);
            Assert.DoesNotContain('0', codigo);
            Assert.DoesNotContain('1', codigo);
        }
    }

    [Fact]
    public void Rescate_TienenOchentaBitsDeAzar()
    {
        // Dieciséis caracteres de un alfabeto de 32 son 80 bits, y ESE número es lo que justifica
        // que el hash sea rápido en lugar de lento. Si alguien acorta el código, este número deja de
        // sostener aquella decisión.
        foreach (var codigo in CodigosDeRescate.Generar())
            Assert.Equal(16, CodigosDeRescate.Normalizar(codigo)!.Length);
    }

    [Fact]
    public void Rescate_SeAceptanComoLosTecleaUnaPersona()
    {
        var codigo = CodigosDeRescate.Generar()[0];
        var canonico = CodigosDeRescate.Normalizar(codigo);

        Assert.Equal(canonico, CodigosDeRescate.Normalizar(codigo.ToLowerInvariant()));
        Assert.Equal(canonico, CodigosDeRescate.Normalizar(codigo.Replace("-", " ")));
        Assert.Equal(canonico, CodigosDeRescate.Normalizar(codigo.Replace("-", "")));
    }

    [Fact]
    public void Rescate_ElHashEsSiempreDeSesentaYCuatroCaracteres()
    {
        var hash = CodigosDeRescate.HashearLoTecleado(CodigosDeRescate.Generar()[0]);

        Assert.NotNull(hash);
        Assert.Equal(64, hash!.Length);
        Assert.All(hash, c => Assert.True(char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void Rescate_ElHashNoDejaVerElCodigo()
    {
        var codigo = CodigosDeRescate.Generar()[0];
        var hash = CodigosDeRescate.HashearLoTecleado(codigo)!;

        Assert.DoesNotContain(CodigosDeRescate.Normalizar(codigo)!, hash, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFGH")]                  // corto
    [InlineData("ABCD-EFGH-JKLM-NPQR-STUV")]   // largo
    [InlineData("ABCD-EFGH-JKLM-NPQ0")]        // lleva un cero, que no está en el alfabeto
    public void Rescate_LoQueNoTieneFormaDeCodigo_NoSeHashea(string? tecleado)
    {
        Assert.Null(CodigosDeRescate.Normalizar(tecleado));
        Assert.Null(CodigosDeRescate.HashearLoTecleado(tecleado));
    }
}
