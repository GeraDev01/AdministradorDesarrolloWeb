using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdminWeb.Api.Arranque;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// Las decisiones del llavero cuando entra en juego un certificado: qué configuración se elige, cuál
/// gana si hay dos, y —lo que de verdad importa— qué pasa cuando un certificado no está.
///
/// <para><b>Por qué se prueba así y no levantando la API.</b> Lo que se está comprobando no es que
/// <c>Llavero</c> llame a <c>ProtectKeysWithCertificate</c>; eso no dice nada y se rompería solo al
/// renombrar un método. Lo que se comprueba es el criterio: con qué certificado se cifra, cuáles se
/// conservan para descifrar, qué se avisa y qué tumba el arranque. Ese criterio vive en
/// <see cref="Llavero.LeerHuellas"/> y <see cref="Llavero.Planificar"/>, y la segunda recibe la
/// búsqueda en el almacén como parámetro justamente para poder ejercitarla con certificados
/// fabricados en memoria — sin instalar nada en la máquina de quien corra las pruebas, que sería un
/// efecto secundario inaceptable en una suite.</para>
///
/// <para><b>Lo que estas pruebas protegen de verdad.</b> Un llavero mal decidido no falla: funciona
/// hasta el siguiente reinicio, que puede ser semanas después, y entonces se manifiesta como «tu
/// token de DevOps no sirve». Ninguna prueba de extremo a extremo lo vería, porque en el momento de
/// correr todo va bien. Aquí se fija por escrito, ahora, qué tiene que pasar en cada caso.</para>
/// </summary>
public class LlaveroConCertificadoTests
{
    private const string Vault = "https://un-vault.vault.azure.net/keys/llavero/abc";

    // ── Ayudas ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un certificado autofirmado con su llave privada, hecho al vuelo. No se instala en ningún
    /// almacén: solo se lo damos al buscador falso.
    /// </summary>
    private static X509Certificate2 Certificado(DateTimeOffset? caduca = null)
    {
        using var rsa = RSA.Create(2048);
        var peticion = new CertificateRequest(
            "CN=llavero-de-prueba", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var hasta = caduca ?? DateTimeOffset.UtcNow.AddYears(5);
        return peticion.CreateSelfSigned(hasta.AddYears(-1), hasta);
    }

    /// <summary>El mismo certificado pero solo con su parte pública, como quien sube un .cer.</summary>
    private static X509Certificate2 SoloLaPublica(X509Certificate2 completo) =>
        X509CertificateLoader.LoadCertificate(completo.RawData);

    /// <summary>Un almacén de mentira: encuentra exactamente los certificados que se le den.</summary>
    private static Func<string, X509Certificate2?> Almacen(params X509Certificate2[] certificados) =>
        huella => certificados.FirstOrDefault(c => c.Thumbprint == huella);

    private static Llavero.HuellasDelLlavero Huellas(string? actual, params string[] anteriores) =>
        new(actual, anteriores);

    private static IConfiguration Configuracion(params (string clave, string valor)[] ajustes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(ajustes.ToDictionary(a => a.clave, a => (string?)a.valor))
            .Build();

    // ── Leer la configuración ───────────────────────────────────────────────────

    [Fact]
    public void SinNadaConfigurado_NoHayNingunaHuella()
    {
        // Es el caso de la máquina de alguien, de docker-compose y de esta misma suite. Si algún día
        // esto devolviera algo, la aplicación se pondría a buscar certificados donde no hay ninguno.
        var huellas = Llavero.LeerHuellas(Configuracion());

        Assert.Null(huellas.Actual);
        Assert.Empty(huellas.Anteriores);
    }

    [Fact]
    public void LaHuellaSeAdmiteTalComoSaleDelDialogoDeWindows()
    {
        // Copiada del diálogo de certificados, una huella llega con espacios cada dos dígitos y con
        // una marca de dirección INVISIBLE delante (U+200E) que no se ve en ningún editor. Pegada tal
        // cual, el almacén no la encuentra y el mensaje resultante acusa al certificado de no estar.
        //
        // La marca se escribe con su código y no con el carácter por lo mismo: pegada aquí de verdad,
        // esta línea parecería no tener nada raro y la prueba dejaría de contar lo que cuenta.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "\u200Ea1 b2 c3 d4 e5 f6 07 18 29 3a")));

        Assert.Equal("A1B2C3D4E5F60718293A", huellas.Actual);
    }

    [Fact]
    public void LasHuellasConDosPuntosTambienValen()
    {
        // Otras herramientas las enseñan separadas por dos puntos. Da igual de dónde se copie.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "AA:BB:CC:DD")));

        Assert.Equal("AABBCCDD", huellas.Actual);
    }

    [Fact]
    public void LasAnterioresSeAdmitenEnUnaSolaLineaSeparadasPorComas()
    {
        // La configuración del App Service es plana: obligar al arreglo indexado
        // (…__CertificadosAnteriores__0) sería teclear tres claves para poner dos huellas.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "AAAA"),
            ("AdminWeb:Llavero:CertificadosAnteriores", "bbbb, cccc ; dddd")));

        Assert.Equal(["BBBB", "CCCC", "DDDD"], huellas.Anteriores);
    }

    [Fact]
    public void LasAnterioresTambienSeAdmitenComoArreglo()
    {
        // Es la forma que sale de un appsettings.json, y la que se usa para ensayar en local.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "AAAA"),
            ("AdminWeb:Llavero:CertificadosAnteriores:0", "bbbb"),
            ("AdminWeb:Llavero:CertificadosAnteriores:1", "cccc")));

        Assert.Equal(["BBBB", "CCCC"], huellas.Anteriores);
    }

    [Fact]
    public void SiVienenDeLasDosFormas_MandaLaLinea()
    {
        // Precedencia deliberada: la cadena suelta es la que alguien acaba de escribir a mano en el
        // portal, y el arreglo suele venir heredado del archivo. Callarse una de las dos sin decir
        // cuál gana dejaría un certificado fuera de la lista de descifrado sin que nadie lo note.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:CertificadosAnteriores", "1111"),
            ("AdminWeb:Llavero:CertificadosAnteriores:0", "2222")));

        Assert.Equal(["1111"], huellas.Anteriores);
    }

    [Fact]
    public void UnaListaVaciaOSoloConSeparadores_NoAportaNada()
    {
        // Una lista puesta y vacía es lo normal antes de la primera rotación. No puede convertirse en
        // una huella en blanco que luego se busque en el almacén y produzca un aviso falso.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "AAAA"),
            ("AdminWeb:Llavero:CertificadosAnteriores", " , ; ")));

        Assert.Empty(huellas.Anteriores);
    }

    [Fact]
    public void NiLasRepetidas_NiLaPROPIAActual_SeCuentanDosVeces()
    {
        // Tras una rotación descuidada es fácil que la actual se quede también en la lista de
        // anteriores, o que la misma huella aparezca dos veces con distinto formato. Repetirla haría
        // que el aviso de «no aparece» saliera duplicado y pareciera que faltan dos certificados.
        var huellas = Llavero.LeerHuellas(Configuracion(
            ("AdminWeb:Llavero:Certificado", "aaaa"),
            ("AdminWeb:Llavero:CertificadosAnteriores", "AA:AA, bbbb, BBBB, aaaa")));

        Assert.Equal("AAAA", huellas.Actual);
        Assert.Equal(["BBBB"], huellas.Anteriores);
    }

    // ── Decidir qué se usa ──────────────────────────────────────────────────────

    [Fact]
    public void SinHuellas_NoSeCifraYNiSiquieraSeMiraElAlmacen()
    {
        // Lo que no puede romperse: sin certificado configurado todo se comporta como antes de que
        // esto existiera. Se cuenta si se consultó el almacén porque abrir el almacén del usuario en
        // un contenedor Linux es exactamente el tipo de cosa que no debe ocurrir «por si acaso».
        var consultas = 0;

        var plan = Llavero.Planificar(llaveDeKeyVault: null, Huellas(null), _ => { consultas++; return null; });

        Assert.Equal(Llavero.ModoDeCifrado.Ninguno, plan.Cifrado);
        Assert.Null(plan.ParaCifrar);
        Assert.Empty(plan.ParaDescifrar);
        Assert.Empty(plan.Avisos);
        Assert.Equal(0, consultas);
    }

    [Fact]
    public void ConCertificado_SeCifraConElYTambienSeApuntaParaDescifrar()
    {
        // El actual va en las dos listas a propósito: si solo estuviera en la de cifrado, leer las
        // llaves dependería de que Data Protection volviera a buscarlo por su cuenta en el almacén.
        var certificado = Certificado();

        var plan = Llavero.Planificar(null, Huellas(certificado.Thumbprint), Almacen(certificado));

        Assert.Equal(Llavero.ModoDeCifrado.Certificado, plan.Cifrado);
        Assert.Same(certificado, plan.ParaCifrar);
        Assert.Same(certificado, Assert.Single(plan.ParaDescifrar));
        Assert.Empty(plan.Avisos);
    }

    [Fact]
    public void LaROTACION_PasoAPaso_ElNuevoCifraYElViejoSigueDescifrando()
    {
        // Esta es la prueba por la que existe todo el diseño de «actual + anteriores».
        //
        // El viejo se fabrica CADUCADO porque así es como llega una rotación de verdad: se rota
        // porque caducó. Si algún día alguien filtrara los certificados por validez —o buscara con
        // validOnly— esta prueba caería, y sin ella el fallo sería silencioso: el llavero seguiría
        // cifrando tan contento y todo lo escrito con el viejo quedaría ilegible para siempre.
        var viejo = Certificado(caduca: DateTimeOffset.UtcNow.AddDays(-1));
        var nuevo = Certificado();

        // Antes de rotar.
        var antes = Llavero.Planificar(null, Huellas(viejo.Thumbprint), Almacen(viejo, nuevo));
        Assert.Same(viejo, antes.ParaCifrar);

        // Después: la huella del viejo pasa a la lista de anteriores y NO se borra el certificado.
        var despues = Llavero.Planificar(
            null, Huellas(nuevo.Thumbprint, viejo.Thumbprint), Almacen(viejo, nuevo));

        Assert.Same(nuevo, despues.ParaCifrar);
        Assert.Contains(viejo, despues.ParaDescifrar);
        Assert.Contains(nuevo, despues.ParaDescifrar);

        // Y que el anterior esté caducado no genera ni un aviso: es lo esperado, no un problema.
        Assert.Empty(despues.Avisos);
    }

    [Fact]
    public void SiFaltaElCertificadoConElQueSeCIFRA_LaAplicacionNoArranca()
    {
        // La decisión razonada del archivo: aquí sí se prefiere no arrancar. Seguir escribiría llaves
        // nuevas en claro junto a otras que ya no se pueden abrir, y encima con la configuración
        // afirmando que el llavero está cifrado.
        var huella = Certificado().Thumbprint;

        var fallo = Assert.Throws<InvalidOperationException>(
            () => Llavero.Planificar(null, Huellas(huella), Almacen()));

        // El mensaje tiene que llevar la huella: sin ella, quien lo lea no sabe cuál de los
        // certificados subidos es el que falta.
        Assert.Contains(huella, fallo.Message);
    }

    [Fact]
    public void UnaHuellaConErrata_NoDesaparece_TumbaElArranqueYSeenalaLaErrata()
    {
        // Tentación evitada: descartar en silencio lo que no parece una huella. Descartarla dejaría
        // el llavero SIN cifrar creyendo que está cifrado, que es el peor final posible. Se deja
        // pasar, no se encuentra, y el mensaje dice que el valor no tiene pinta de huella.
        var fallo = Assert.Throws<InvalidOperationException>(
            () => Llavero.Planificar(null, Huellas("la-huella-va-aqui"), Almacen()));

        Assert.Contains("hexadecimales", fallo.Message);
    }

    [Fact]
    public void SiElCertificadoQueCIFRANoTraeLlavePrivada_LaAplicacionNoArranca()
    {
        // El caso más traicionero de todos: cifrar solo necesita la parte pública, así que sin esta
        // comprobación el arranque diría «cifrado con certificado», todo funcionaría durante días y
        // el siguiente reinicio no podría descifrar ni una de las llaves escritas mientras tanto.
        var publica = SoloLaPublica(Certificado());

        var fallo = Assert.Throws<InvalidOperationException>(
            () => Llavero.Planificar(null, Huellas(publica.Thumbprint), Almacen(publica)));

        Assert.Contains("llave privada", fallo.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SiFaltaUnCertificadoANTERIOR_SeArrancaAvisandoYLosDemasSiguenValiendo()
    {
        // La otra mitad de la decisión: un anterior que no está NO tumba nada. El actual está, lo
        // nuevo se cifra y la promesa se cumple; y retirar un certificado agotado es a veces
        // exactamente lo correcto. Pero se dice, con su huella, porque este es el único momento en
        // que alguien puede darse cuenta antes de que a un compañero le falle el PAT.
        var actual = Certificado();
        var presente = Certificado();
        var perdido = Certificado().Thumbprint;

        var plan = Llavero.Planificar(
            null, Huellas(actual.Thumbprint, presente.Thumbprint, perdido),
            Almacen(actual, presente));

        Assert.Equal(Llavero.ModoDeCifrado.Certificado, plan.Cifrado);
        Assert.Same(actual, plan.ParaCifrar);
        Assert.Equal(2, plan.ParaDescifrar.Count);
        Assert.Contains(perdido, Assert.Single(plan.Avisos));
    }

    [Fact]
    public void UnANTERIORSinLlavePrivada_NoEntraEnLaListaDeDescifrado()
    {
        // Un .cer subido donde hacía falta un .pfx. No sirve para descifrar nada, así que meterlo en
        // la lista solo serviría para que el fallo posterior no señalara a ningún sitio.
        var actual = Certificado();
        var publica = SoloLaPublica(Certificado());

        var plan = Llavero.Planificar(
            null, Huellas(actual.Thumbprint, publica.Thumbprint), Almacen(actual, publica));

        Assert.Same(actual, Assert.Single(plan.ParaDescifrar));
        Assert.Contains(publica.Thumbprint, Assert.Single(plan.Avisos));
    }

    [Fact]
    public void ElCertificadoActualCADUCADO_CifraIgualPeroSeAvisa()
    {
        // Cifrar con un certificado caducado funciona: es RSA y la fecha no le importa. Negarse sería
        // un apagón autoinfligido. Pero el aviso llega en el arranque, y un arranque puede tardar
        // semanas en repetirse, así que cuando llega tiene que decirlo.
        var caducado = Certificado(caduca: DateTimeOffset.UtcNow.AddDays(-1));

        var plan = Llavero.Planificar(null, Huellas(caducado.Thumbprint), Almacen(caducado));

        Assert.Equal(Llavero.ModoDeCifrado.Certificado, plan.Cifrado);
        Assert.Same(caducado, plan.ParaCifrar);
        Assert.Contains("CADUC", Assert.Single(plan.Avisos));
    }

    [Fact]
    public void UnCertificadoQueCaducaPronto_SeAvisaConTiempo()
    {
        var casi = Certificado(caduca: DateTimeOffset.UtcNow.AddDays(10));

        var plan = Llavero.Planificar(null, Huellas(casi.Thumbprint), Almacen(casi));

        Assert.Contains("caduca el", Assert.Single(plan.Avisos));
    }

    // ── Precedencia entre Key Vault y certificado ───────────────────────────────

    [Fact]
    public void KeyVaultGanaAlCertificado_YLosCertificadosSeQuedanSoloParaDESCIFRAR()
    {
        // Aplicar los dos no es una opción: la segunda llamada a ProtectKeysWith… pisa a la primera
        // en silencio, y cuál gana dependería del orden del código. Se elige explícitamente Key Vault
        // —la llave privada no sale del vault y no caduca— y los certificados se conservan para poder
        // leer lo que ya estaba cifrado con ellos. Esa es, de hecho, la migración de uno al otro.
        var certificado = Certificado();

        var plan = Llavero.Planificar(Vault, Huellas(certificado.Thumbprint), Almacen(certificado));

        Assert.Equal(Llavero.ModoDeCifrado.KeyVault, plan.Cifrado);
        Assert.Null(plan.ParaCifrar);
        Assert.Same(certificado, Assert.Single(plan.ParaDescifrar));

        // Y no se calla: tener los dos puestos casi siempre es un resto de una migración a medias.
        Assert.Contains("Key Vault", Assert.Single(plan.Avisos));
    }

    [Fact]
    public void ConKeyVault_QueFalteUnCertificadoYaNoTumbaNada()
    {
        // La regla es «solo es mortal no encontrar el certificado con el que se va a CIFRAR». Con Key
        // Vault cifrando, un certificado ausente es un problema de lectura de lo antiguo: grave, pero
        // no una mentira sobre la seguridad de lo que se escribe ahora.
        var huella = Certificado().Thumbprint;

        var plan = Llavero.Planificar(Vault, Huellas(huella), Almacen());

        Assert.Equal(Llavero.ModoDeCifrado.KeyVault, plan.Cifrado);
        Assert.Empty(plan.ParaDescifrar);
        Assert.Contains(plan.Avisos, a => a.Contains(huella));
    }

    [Fact]
    public void AnterioresSinActualNiKeyVault_NoArranca()
    {
        // El error de quien borra la huella actual creyendo que así «se desactiva el cifrado». No se
        // desactiva: parte el llavero en dos mitades que ya no se hablan —lo nuevo en claro, lo viejo
        // cifrado— y ninguna sirve para lo que se espera.
        var viejo = Certificado();

        Assert.Throws<InvalidOperationException>(
            () => Llavero.Planificar(null, Huellas(null, viejo.Thumbprint), Almacen(viejo)));
    }

    [Fact]
    public void AnterioresSinActualPeroConKeyVault_EsLaMigracionYArrancaBien()
    {
        // Mismo estado de configuración que la prueba anterior, y aquí es legítimo: cifra el vault y
        // los certificados solo quedan para leer lo de antes. Es el final de la migración, cuando ya
        // no tiene sentido que ninguno sea «el actual».
        var viejo = Certificado();

        var plan = Llavero.Planificar(Vault, Huellas(null, viejo.Thumbprint), Almacen(viejo));

        Assert.Equal(Llavero.ModoDeCifrado.KeyVault, plan.Cifrado);
        Assert.Same(viejo, Assert.Single(plan.ParaDescifrar));
        Assert.Empty(plan.Avisos);
    }
}
