using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Conocimiento;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LAS IMÁGENES DE UN ARTÍCULO, y sobre todo lo que meterlas NO puede abrir.
///
/// <para>Un artículo lo escribe cualquiera del equipo y lo lee todo el mundo desde el mismo origen
/// que la aplicación y con la sesión de quien lo abre: es el vector clásico del XSS almacenado. El
/// cuerpo lo tenía cerrado por construcción —no se produce marcado en ningún punto del camino, se
/// producen bloques y segmentos— y una imagen es justo la forma que podría reabrirlo, porque una
/// imagen es una etiqueta con atributos y una dirección.</para>
///
/// <para><b>La defensa que aquí se comprueba</b> es que la marca no admite ninguna dirección: admite
/// el NÚMERO de una imagen ya guardada en el artículo, y el bloque que sale del servidor lleva ese
/// entero. Todo lo demás —un <c>onerror</c> colado en la etiqueta, un <c>javascript:</c>, un
/// <c>data:</c>, una dirección de otro sitio— no casa con el patrón, y lo que no casa es texto. Estas
/// pruebas lo intentan de verdad, no de mentira.</para>
///
/// <para>Y lo otro que cuidan son los límites que hacen que esto no se convierta en un almacén: el
/// tipo decidido por los BYTES y no por la extensión, el peso, cuántas caben, y que las capturas de
/// un borrador sean tan privadas como el borrador.</para>
/// </summary>
public class ImagenesDeConocimientoTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private const int UidAna = 10, UidBeto = 11, UidLider = 1, UidOps = 20;

    private static ICurrentUser Ana => new UsuarioDePrueba
    { UserId = UidAna, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador };

    private static ICurrentUser Beto => new UsuarioDePrueba
    { UserId = UidBeto, Username = "beto", FullName = "Beto", Role = UserRole.Desarrollador };

    private static ICurrentUser Lider => new UsuarioDePrueba
    { UserId = UidLider, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin };

    private static ICurrentUser Operaciones => new UsuarioDePrueba
    { UserId = UidOps, Username = "ops", FullName = "Ops", Role = UserRole.Operaciones };

    private const string Cuerpo =
        "# Cómo se despliega\n\nSe corre el paquete y se revisa el log antes de dar por bueno el cambio.";

    /// <summary>Cada llamada con su PROPIO contexto contra la misma base, como en producción.</summary>
    private ConocimientoService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = Otro(db);
        return new ConocimientoService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx));
    }

    private AppDbContext Otro(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private AppDbContext BaseConLider()
    {
        var db = TestDb.New();
        _contextos.Add(db);
        db.Users.Add(new User
        { Id = UidLider, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();
        return db;
    }

    /// <summary>Un PNG de verdad en lo único que se mira: su firma. El resto es relleno.</summary>
    private static byte[] Png(int relleno = 40) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[relleno]];

    private async Task<int> ArticuloDeAnaAsync(AppDbContext db)
    {
        var (_, _, a) = await Svc(db, Ana).CrearAsync("Despliegue del portal", Cuerpo, "despliegue");
        return a!.Id;
    }

    private async Task<int> PublicadoDeAnaAsync(AppDbContext db)
    {
        int id = await ArticuloDeAnaAsync(db);
        await Svc(db, Ana).EnviarARevisionAsync(id);
        await Svc(db, Lider).AprobarAsync(id);
        return id;
    }

    private static List<ConocimientoSegmentoDto> Segmentos(IEnumerable<ConocimientoBloqueDto> bloques) =>
        bloques.SelectMany(b => b.Renglones).SelectMany(r => r.Segmentos).ToList();

    private static string TextoDe(IEnumerable<ConocimientoBloqueDto> bloques) =>
        string.Concat(Segmentos(bloques).Select(s => s.Texto));

    // ── La marca: lo que SÍ es una imagen ────────────────────────────────────────

    [Fact]
    public void Imagen_SolaEnSuRenglon_EsUnBloqueConSuNumeroYSuDescripcion()
    {
        var bloques = ConocimientoTexto.Analizar("Antes\n\n![la cola de revisión](imagen:12)\n\nDespués");

        var imagen = Assert.Single(bloques, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.Equal(12, imagen.ImagenId);
        Assert.Equal("la cola de revisión", Assert.Single(Assert.Single(imagen.Renglones).Segmentos).Texto);

        // Y no se come lo de alrededor: sigue habiendo los dos párrafos.
        Assert.Equal(2, bloques.Count(b => b.Tipo == TipoDeBloque.Parrafo));
    }

    [Fact]
    public void Imagen_PuedeIrSinDescripcion()
    {
        var b = Assert.Single(ConocimientoTexto.Analizar("![](imagen:3)"));
        Assert.Equal(TipoDeBloque.Imagen, b.Tipo);
        Assert.Equal(3, b.ImagenId);
    }

    [Fact]
    public void Marca_LaEscribeElServidor_YAnalizarLaVuelveAReconocer()
    {
        // El viaje completo: la pantalla pega en el cuerpo lo que devuelve Marca, y lo que se pinta
        // sale de Analizar. Si los dos se separaran, el artículo enseñaría la marca en vez de la
        // imagen — y nadie lo notaría hasta que alguien abriera uno.
        var marca = ConocimientoTexto.Marca(7, "captura de la cola");

        var b = Assert.Single(ConocimientoTexto.Analizar(marca));
        Assert.Equal(TipoDeBloque.Imagen, b.Tipo);
        Assert.Equal(7, b.ImagenId);
        Assert.Equal("captura de la cola", TextoDe([b]));
    }

    [Fact]
    public void Marca_LimpiaLaDescripcionQueRomperiaLaPropiaMarca()
    {
        // El nombre viene de un archivo y puede traer cualquier cosa. Lo que rompería la marca —el
        // corchete de cierre y los saltos de línea— se quita en vez de rechazar la imagen.
        var marca = ConocimientoTexto.Marca(4, "raro]\n(imagen:99)");

        var b = Assert.Single(ConocimientoTexto.Analizar(marca));
        Assert.Equal(TipoDeBloque.Imagen, b.Tipo);
        // El 4 y no el 99: la descripción no puede colar otro número.
        Assert.Equal(4, b.ImagenId);
    }

    // ── La marca: lo que NO es una imagen ────────────────────────────────────────

    [Theory]
    // Una dirección donde va el número: ninguna de estas es una imagen, y las tres primeras no son
    // nada en absoluto porque su esquema no pasa la validación de enlaces del foro.
    [InlineData("![captura](javascript:alert(1))")]
    [InlineData("![captura](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("![captura](file://servidor/compartida/foto.png)")]
    // El número con algo pegado: es el intento de cerrar el atributo y abrir otro.
    [InlineData("![captura](imagen:3\" onerror=\"alert(1))")]
    [InlineData("![captura](imagen:3 onerror=alert(1))")]
    [InlineData("![captura](imagen:tres)")]
    [InlineData("![captura](imagen:)")]
    // Cero no es ninguna imagen: dejarlo pasar pintaría un hueco roto en mitad del artículo.
    [InlineData("![captura](imagen:0)")]
    // La etiqueta escrita a mano, que es por donde se empezaría si esto fuera HTML.
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<img src=\"api/adjuntos/conocimiento/1\" onerror=\"fetch('//fuera')\">")]
    public void Hostil_NoProduceNiImagenNiEnlace_SeQuedaEnTextoTalCual(string linea)
    {
        var bloques = ConocimientoTexto.Analizar(linea);

        Assert.DoesNotContain(bloques, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.All(Segmentos(bloques), s => Assert.Null(s.Url));

        // Y sigue leyéndose lo que se escribió: no se produce marcado en ningún punto del camino, así
        // que no hay nada que escapar — el texto sale tal cual y quien lo pinta lo emite como texto.
        Assert.Equal(linea, TextoDe(bloques));
    }

    [Theory]
    // ── Mayúsculas raras ────────────────────────────────────────────────────
    // El patrón de la marca es literal y en minúsculas; y aunque «IMAGEN:» se reconociera, seguiría
    // sin admitir más que dígitos detrás.
    [InlineData("![x](IMAGEN:3)")]
    [InlineData("![x](ImAgEn:3)")]
    // El esquema de un enlace sí se compara sin distinguir mayúsculas —Uri lo pasa a minúsculas
    // antes de mirarlo—, así que disfrazarlo no sirve de nada.
    [InlineData("[pulsa](JaVaScRiPt:alert(1))")]
    [InlineData("[pulsa](JAVASCRIPT:alert%281%29)")]
    [InlineData("[pulsa](DATA:text/html,<script>alert(1)</script>)")]
    [InlineData("[pulsa](VBScript:msgbox(1))")]
    // Sin esquema no hay dirección absoluta, y sin dirección absoluta no hay enlace.
    [InlineData("[pulsa](//fuera.example/robame)")]
    // ── Atributos con espacios y con tabuladores ────────────────────────────
    [InlineData("<img   src = x     onerror = alert(1) >")]
    [InlineData("<img\tsrc=x\tonerror=alert(1)>")]
    [InlineData("<IMG SRC=x ONERROR=alert(1)>")]
    // ── La etiqueta partida en varios renglones ─────────────────────────────
    // Es la forma clásica de colarse por un saneador que mira línea a línea. Aquí no hay saneador
    // de marcado al que confundir: no se produce marcado en ningún punto, y las tres líneas acaban
    // en un párrafo de texto.
    [InlineData("<img\nsrc=x\nonerror=alert(1)>")]
    [InlineData("![x](imagen:\n3)")]
    // ── Marcado anidado ─────────────────────────────────────────────────────
    [InlineData("![![x](imagen:1)](imagen:2)")]
    [InlineData("![x](imagen:1)](imagen:2)")]
    [InlineData("[![x](imagen:1)](javascript:alert(1))")]
    [InlineData("<img src=\"![x](imagen:1)\" onerror=alert(1)>")]
    // ── El número, con todo lo que no es un número ──────────────────────────
    // «\\d» reconoce también los dígitos árabes y los de ancho completo, que int.TryParse no lee:
    // no son ninguna imagen y se quedan en texto, como todo lo que no se reconoce.
    [InlineData("![x](imagen:٣)")]
    [InlineData("![x](imagen:３)")]
    [InlineData("![x](imagen:-1)")]
    [InlineData("![x](imagen:+3)")]
    [InlineData("![x](imagen:3.0)")]
    [InlineData("![x](imagen:1234567890)")]
    [InlineData("![x](imagen: 3)")]
    // Con algo pegado detrás, que es el intento de cerrar el atributo y abrir otro.
    [InlineData("![x](imagen:3)<script>alert(1)</script>")]
    [InlineData("![x](imagen:3)\" onload=\"alert(1)")]
    public void Hostil_NiMayusculas_NiEspacios_NiAnidado_ProducenImagenOEnlace(string entrada)
    {
        var bloques = ConocimientoTexto.Analizar(entrada);

        Assert.DoesNotContain(bloques, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.All(Segmentos(bloques), s => Assert.Null(s.Url));

        // Y se lee tal cual se escribió: como no se construye marcado, no hay nada que escapar ni
        // nada que se pueda perder por el camino.
        Assert.Equal(entrada, TextoDe(bloques));
    }

    [Fact]
    public void Hostil_LosInvisiblesNoRecomponenNadaQueLuegoSeInterprete()
    {
        // El truco es al revés que de costumbre: no meter un carácter para romper el patrón, sino
        // meterlo para que el saneador lo quite y lo que quede sí case. Funciona —el saneador
        // trabaja antes que el analizador, y para eso está— pero lo que queda sigue teniendo que
        // pasar por el mismo aro: «javascript:» sigue sin ser un esquema aceptable.
        var bloques = ConocimientoTexto.Analizar("[pulsa](java\u0000script:alert(1))");

        Assert.All(Segmentos(bloques), s => Assert.Null(s.Url));
        Assert.Equal("[pulsa](javascript:alert(1))", TextoDe(bloques));
    }

    [Fact]
    public void DireccionDeFuera_NoSeIncrusta_ComoMucho_QuedaEnEnlace()
    {
        // Deliberado: lo que ilustra un artículo revisado no puede cambiar de contenido después de la
        // revisión sin que nadie se entere, ni contarle a un tercero quién está leyendo.
        var bloques = ConocimientoTexto.Analizar("![captura](https://otro-sitio.example/foto.png)");

        Assert.DoesNotContain(bloques, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.Contains(Segmentos(bloques), s => s.Url == "https://otro-sitio.example/foto.png");
    }

    [Fact]
    public void AtributoColadoEnLaDescripcion_ViajaComoTEXTO_NoComoMarcado()
    {
        // Esta sí es una imagen: el número es un número. Lo que se cuela va en la DESCRIPCIÓN, que es
        // texto y acaba en el atributo alt — donde Blazor lo escapa. Lo que aquí se fija es que el
        // servidor no lo interprete ni lo parta: llega entero y como un solo segmento de texto.
        var b = Assert.Single(ConocimientoTexto.Analizar("![x\" onerror=\"alert(1)](imagen:3)"));

        Assert.Equal(TipoDeBloque.Imagen, b.Tipo);
        Assert.Equal(3, b.ImagenId);
        var s = Assert.Single(Assert.Single(b.Renglones).Segmentos);
        Assert.Equal("x\" onerror=\"alert(1)", s.Texto);
        Assert.Null(s.Url);
    }

    [Fact]
    public void DentroDeUnBloqueDeCodigo_LaMarcaEsUnEjemplo_NoUnaImagen()
    {
        var bloques = ConocimientoTexto.Analizar("```\n![así se pone](imagen:5)\n```");

        var codigo = Assert.Single(bloques);
        Assert.Equal(TipoDeBloque.Codigo, codigo.Tipo);
        Assert.Equal("![así se pone](imagen:5)", TextoDe([codigo]));
    }

    [Fact]
    public void EnMitadDeUnParrafo_LaMarcaNoEsImagen_PorqueUnaImagenOcupaSuRenglon()
    {
        var bloques = ConocimientoTexto.Analizar("mira ![esto](imagen:5) y sigue");

        Assert.DoesNotContain(bloques, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.Equal("mira ![esto](imagen:5) y sigue", TextoDe(bloques));
    }

    // ── El resumen del buscador ──────────────────────────────────────────────────

    [Fact]
    public void Extracto_NoEscupeLaMarcaDeUnaImagen_NiSuDescripcion()
    {
        var cuerpo = "# Despliegue\n\n![captura_2026-08-12T10-11-12-345Z](imagen:3)\n\n" +
                     "Se corre el paquete y se revisa el log.";

        var extracto = ConocimientoTexto.Extracto(cuerpo);

        Assert.Contains("Se corre el paquete", extracto);
        Assert.DoesNotContain("![", extracto);
        Assert.DoesNotContain("imagen:", extracto);
        Assert.DoesNotContain("](", extracto);
        // La descripción tampoco: el nombre con el que se pegó una captura no ayuda a decidir si
        // abrir el artículo, y ocupa el sitio de la frase que sí lo haría.
        Assert.DoesNotContain("captura_2026", extracto);
    }

    [Fact]
    public async Task Buscar_DevuelveElResumenSinMarcado_AunqueElCuerpoSeaCasiTodoImagenes()
    {
        var db = BaseConLider();
        var (_, _, a) = await Svc(db, Ana).CrearAsync(
            "Cómo se ve la cola",
            "![una](imagen:1)\n\n![otra](imagen:2)\n\nLo que hay que mirar es la fecha de la primera fila.",
            "cola");
        Assert.NotNull(a);

        var pagina = await Svc(db, Ana).BuscarAsync();
        var tarjeta = Assert.Single(pagina.Filas);

        Assert.Equal("Lo que hay que mirar es la fecha de la primera fila.", tarjeta.Extracto);
    }

    [Fact]
    public async Task Extracto_DeUnArticuloQueSonSOLOImagenes_NoSaleEnBlanco()
    {
        // El mínimo para mandar a revisar lo cumplen las marcas ellas solas, así que un artículo
        // puede ser tres capturas y ni una frase. Saltándose las imágenes —que es lo correcto— la
        // tarjeta del buscador se quedaba con el renglón del resumen VACÍO, que no se lee como «este
        // artículo son imágenes» sino como una pantalla rota.
        var db = BaseConLider();
        var (ok, mensaje, _) = await Svc(db, Ana).CrearAsync(
            "Cómo queda la pantalla",
            "![la lista](imagen:1)\n\n![el detalle](imagen:2)\n\n![el aviso](imagen:3)", "pantallas");
        Assert.True(ok, mensaje);

        var tarjeta = Assert.Single((await Svc(db, Ana).BuscarAsync()).Filas);

        Assert.Equal("3 imágenes, sin texto que resumir.", tarjeta.Extracto);
        // Y sigue sin escupir marcado, que era lo de siempre.
        Assert.DoesNotContain("![", tarjeta.Extracto);
        Assert.DoesNotContain("imagen:", tarjeta.Extracto);
    }

    [Fact]
    public async Task Buscar_UnTerminoQueSoloEstaEnLaDESCRIPCION_EncuentraElArticulo_YElResumenSigueLimpio()
    {
        // El buscador mira el cuerpo tal como está guardado, marcas incluidas, así que una palabra
        // que solo esté en la descripción de una imagen SÍ encuentra el artículo. Está bien que lo
        // encuentre —es texto que escribió su autor— y lo que hay que cuidar es lo otro: que el
        // resumen de la tarjeta no se convierta en la marca por eso.
        var db = BaseConLider();
        await Svc(db, Ana).CrearAsync(
            "Cómo queda la cola",
            "![el diagrama de la cola](imagen:1)\n\nLo que hay que mirar es la fecha de la primera fila.",
            "cola");

        var pagina = await Svc(db, Ana).BuscarAsync(new ConocimientoFiltro("diagrama"));

        var tarjeta = Assert.Single(pagina.Filas);
        Assert.Equal("Lo que hay que mirar es la fecha de la primera fila.", tarjeta.Extracto);
        Assert.DoesNotContain("diagrama", tarjeta.Extracto);
    }

    // ── El cuerpo guardado: que el saneado de siempre lo siga cubriendo ──────────

    [Fact]
    public async Task Guardar_UnCuerpoHostil_NiSeGuardaMarcado_NiSePintaComoTal()
    {
        // Esta es la prueba de que la sanitización que ya existía SIGUE cubriendo este cuerpo con las
        // imágenes dentro: se guarda por ConocimientoTexto.Sanear —que quita los controles invisibles
        // con los que se disfraza una dirección— y se lee analizado en bloques, sin que exista en
        // ningún punto una cadena de marcado que alguien pudiera olvidarse de escapar.
        var db = BaseConLider();
        var hostil = "<img src=x onerror=alert(1)>\n\n![captura](javascript:alert(1))\n\n" +
                     "![captura](imagen:2\" onerror=\"alert(1))\n\nTexto de relleno\u202E para el mínimo.";

        var (ok, _, a) = await Svc(db, Ana).CrearAsync("Un artículo con trampas", hostil, null);
        Assert.True(ok);

        // Sin los invisibles: el saneador de siempre —el mismo del foro— sigue pasando por aquí.
        var guardado = Otro(db).KnowledgeArticles.AsNoTracking().Single();
        Assert.DoesNotContain('\u0001', guardado.Body);
        Assert.DoesNotContain('\u202E', guardado.Body);

        var leido = await Svc(db, Ana).LeerAsync(a!.Id);
        Assert.NotNull(leido);

        // Ni una imagen, ni un enlace: todo lo de arriba es texto.
        Assert.DoesNotContain(leido!.Cuerpo, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.All(Segmentos(leido.Cuerpo), s => Assert.Null(s.Url));
        Assert.Contains("<img src=x onerror=alert(1)>", TextoDe(leido.Cuerpo));
    }

    [Fact]
    public async Task LoQueYaEstabaGuardado_SeSaneaAlLEERLO_NoSoloAlGuardarlo()
    {
        // La pregunta que hay que hacerle a todo saneador: ¿y lo que ya estaba en la base? Aquí el
        // cuerpo se limpia en los DOS momentos —Crear y Editar lo pasan por Sanear, y Analizar lo
        // vuelve a pasar antes de trocearlo—, así que una fila escrita antes de que esto existiera,
        // o metida por el escritorio que sigue apuntando a esta misma base, se lee igual de limpia.
        // Con el saneado solo al guardar, ese cuerpo saldría tal cual está.
        var db = BaseConLider();

        var ctx = Otro(db);
        var crudo = new KnowledgeArticle
        {
            Title          = "Lo que ya estaba escrito",
            Body           = "Antes de todo esto\u0001\n\n<img src=x onerror=alert(1)>\n\n" +
                             "![captura](javascript:alert(1))\n\njavascript\u202E:alert(1)",
            Status         = KnowledgeStatus.Publicado,
            AuthorUserId   = UidAna,
            AuthorName     = "Ana",
            CreatedAtUtc   = DateTime.UtcNow,
            PublishedAtUtc = DateTime.UtcNow
        };
        ctx.KnowledgeArticles.Add(crudo);
        ctx.SaveChanges();

        // En la base siguen estando los invisibles: nadie los quitó, porque nadie guardó por aquí.
        Assert.Contains('\u0001', Otro(db).KnowledgeArticles.AsNoTracking().Single().Body);

        var leido = await Svc(db, Operaciones).LeerAsync(crudo.Id);
        Assert.NotNull(leido);

        // Y aun así lo que se pinta sale limpio, sin imagen y sin enlace.
        var texto = TextoDe(leido!.Cuerpo);
        Assert.DoesNotContain('\u0001', texto);
        Assert.DoesNotContain('\u202E', texto);
        Assert.DoesNotContain(leido.Cuerpo, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.All(Segmentos(leido.Cuerpo), s => Assert.Null(s.Url));
        Assert.Contains("<img src=x onerror=alert(1)>", texto);
    }

    // ── La marca tiene que nombrar una imagen DE ESTE artículo ───────────────────

    [Fact]
    public async Task Marca_ConUnNumeroQueNoEsDeEsteArticulo_NoPintaImagen_SeQuedaAlaVista()
    {
        // Un número tecleado a mano, o heredado de un cuerpo copiado: sin comprobarlo, el artículo
        // salía con una etiqueta hacia una ruta que contesta 404 y el lector se quedaba con el
        // recuadro roto del navegador sin que nada le dijera qué pasaba. Como texto se lee y se
        // arregla.
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        await Svc(db, Ana).EditarAsync(id, "Despliegue del portal",
            Cuerpo + "\n\n![la que no existe](imagen:4321)", "despliegue");

        var leido = await Svc(db, Ana).LeerAsync(id);
        Assert.NotNull(leido);

        Assert.DoesNotContain(leido!.Cuerpo, b => b.Tipo == TipoDeBloque.Imagen);
        Assert.Contains("![la que no existe](imagen:4321)", TextoDe(leido.Cuerpo));
        Assert.All(Segmentos(leido.Cuerpo), s => Assert.Null(s.Url));
    }

    [Fact]
    public async Task Marca_ConLaCapturaDeOTROArticulo_TampocoSePinta()
    {
        // Este es el caso feo, y no es rebuscado: se escribe un borrador con capturas, se parte en
        // dos artículos y el segundo se lleva el cuerpo copiado. Su autora SÍ veía la imagen —es la
        // suya, aunque cuelgue del otro— así que publicaba dando por bueno lo que al resto del
        // equipo le salía roto. Ahora no se pinta para nadie, empezando por ella.
        var db = BaseConLider();
        int primero = await ArticuloDeAnaAsync(db);
        var (_, _, ajena) = await Svc(db, Ana).GuardarImagenAsync(primero, "captura.png", Png());

        var (_, _, segundo) = await Svc(db, Ana).CrearAsync("El mismo texto, otro artículo", Cuerpo, null);
        await Svc(db, Ana).EditarAsync(segundo!.Id, "El mismo texto, otro artículo",
            Cuerpo + "\n\n" + ajena!.Marca, null);

        var leido = await Svc(db, Ana).LeerAsync(segundo.Id);
        Assert.DoesNotContain(leido!.Cuerpo, b => b.Tipo == TipoDeBloque.Imagen);

        // Y en el artículo del que cuelga sí se pinta, con la MISMA marca: lo que se comprueba es de
        // quién es la imagen, no si el número existe en alguna parte.
        await Svc(db, Ana).EditarAsync(primero, "Despliegue del portal", Cuerpo + "\n\n" + ajena.Marca, null);
        var suyo = await Svc(db, Ana).LeerAsync(primero);
        Assert.Equal(ajena.Id, Assert.Single(suyo!.Cuerpo, b => b.Tipo == TipoDeBloque.Imagen).ImagenId);
    }

    // ── Subir: los límites ───────────────────────────────────────────────────────

    [Fact]
    public async Task Subir_GuardaLaImagenYDevuelveLaMarcaConLaQueNombrarla()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        var (ok, mensaje, imagen) = await Svc(db, Ana).GuardarImagenAsync(id, "diagrama de flujo.png", Png());

        Assert.True(ok, mensaje);
        Assert.NotNull(imagen);
        Assert.Equal("diagrama de flujo.png", imagen!.Nombre);

        // La marca viene armada del servidor, y es la que el editor mete en el texto tal cual.
        Assert.Equal($"![diagrama de flujo](imagen:{imagen.Id})", imagen.Marca);

        var fila = Otro(db).KnowledgeImages.AsNoTracking().Single();
        Assert.Equal(id, fila.ArticleId);
        Assert.Equal("image/png", fila.ContentType);
        Assert.Equal(UidAna, fila.UploadedByUserId);
    }

    [Fact]
    public async Task Subir_LoQueNoEsUnaImagen_SeRechazaPorLosBYTES_NoPorLaExtension()
    {
        // El nombre dice «.png» y el contenido es un guion. Si se creyera la extensión, esos bytes se
        // servirían después como imagen y se ejecutarían en el navegador de quien abra el artículo.
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>"u8.ToArray();
        var (ok, mensaje, imagen) = await Svc(db, Ana).GuardarImagenAsync(id, "diagrama.png", svg);

        Assert.False(ok);
        Assert.Null(imagen);
        Assert.Contains("no es una imagen", mensaje);
        Assert.Empty(Otro(db).KnowledgeImages.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Subir_LoQuePasaDelTope_SeRechazaDiciendoCuantoPesaYCuantoCabe()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        var gorda = Png((int)ConocimientoService.MaxBytesImagen + 1);
        var (ok, mensaje, _) = await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", gorda);

        Assert.False(ok);
        Assert.Contains("el tope por imagen es", mensaje);
        Assert.Empty(Otro(db).KnowledgeImages.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Subir_ElTopeLoAplicaElSERVIDOR_YCaeJustoDondeDice()
    {
        // Que el recuadro de pegar frene antes es cortesía, no barrera: la API se llama sin pasar
        // por él. Esta prueba entra por el servicio —que es por donde pasa TODO lo que se guarda— y
        // fija el borde: lo que cabe justo, cabe; un byte más, no.
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        int firma = Png(0).Length;
        var justa = Png((int)ConocimientoService.MaxBytesImagen - firma);
        Assert.Equal(ConocimientoService.MaxBytesImagen, justa.LongLength);

        Assert.True((await Svc(db, Ana).GuardarImagenAsync(id, "justa.png", justa)).ok);

        var pasada = Png((int)ConocimientoService.MaxBytesImagen - firma + 1);
        var (ok, mensaje, _) = await Svc(db, Ana).GuardarImagenAsync(id, "pasada.png", pasada);

        Assert.False(ok);
        Assert.Contains("el tope por imagen es", mensaje);
        Assert.Single(Otro(db).KnowledgeImages.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Subir_UnaVacia_SeRechaza()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        var (ok, mensaje, _) = await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", []);

        Assert.False(ok);
        Assert.Contains("vacía", mensaje);
    }

    [Fact]
    public async Task Subir_MasDeLasQueCaben_SeRechazaLaSiguiente()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        for (int i = 0; i < ConocimientoService.MaxImagenes; i++)
            Assert.True((await Svc(db, Ana).GuardarImagenAsync(id, $"c{i}.png", Png())).ok);

        var (ok, mensaje, _) = await Svc(db, Ana).GuardarImagenAsync(id, "una más.png", Png());

        Assert.False(ok);
        Assert.Contains($"{ConocimientoService.MaxImagenes} imágenes", mensaje);
        Assert.Equal(ConocimientoService.MaxImagenes, Otro(db).KnowledgeImages.AsNoTracking().Count());
    }

    // ── Subir: quién puede ───────────────────────────────────────────────────────

    [Fact]
    public async Task Subir_ALoAjeno_NoSePuede_AunqueEsteAlaVista()
    {
        var db = BaseConLider();
        int id = await PublicadoDeAnaAsync(db);

        var (ok, mensaje, _) = await Svc(db, Beto).GuardarImagenAsync(id, "captura.png", Png());

        Assert.False(ok);
        Assert.Contains("Solo puedes poner imágenes en lo que tú escribiste", mensaje);
    }

    [Fact]
    public async Task Subir_ANadaQueNoSeVea_ContestaLoMismoQueSiNoExistiera()
    {
        // Un borrador ajeno no se lee, y tampoco se le pueden colgar imágenes. La respuesta es la
        // misma que la de un artículo inexistente: decir otra cosa confirmaría que existe.
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);

        var (ok, mensaje, _) = await Svc(db, Beto).GuardarImagenAsync(id, "captura.png", Png());

        Assert.False(ok);
        Assert.Equal((await Svc(db, Beto).GuardarImagenAsync(9999, "captura.png", Png())).mensaje, mensaje);
    }

    [Fact]
    public async Task Subir_Operaciones_NoEscribe_NiConImagenes()
    {
        var db = BaseConLider();
        int id = await PublicadoDeAnaAsync(db);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, Operaciones).GuardarImagenAsync(id, "captura.png", Png()));
    }

    // ── Servir los bytes: la visibilidad es la del artículo ──────────────────────

    [Fact]
    public async Task Bytes_DeUnBorrador_NoLosSirveNadieMas_NiElLider()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);
        var (_, _, imagen) = await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", Png());

        // Su autora sí.
        var (mios, nombre) = await Svc(db, Ana).BytesDeImagenAsync(imagen!.Id);
        Assert.NotEmpty(mios);
        Assert.Equal("captura.png", nombre);

        // Nadie más. Si las capturas de un borrador se pudieran bajar por número, la privacidad de lo
        // que alguien está escribiendo dependería de que nadie probara números.
        Assert.Empty((await Svc(db, Beto).BytesDeImagenAsync(imagen.Id)).bytes);
        Assert.Empty((await Svc(db, Lider).BytesDeImagenAsync(imagen.Id)).bytes);
    }

    [Fact]
    public async Task Bytes_DeLoPublicado_LosVeCualquieraConSesion_IncluidaOperaciones()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);
        var (_, _, imagen) = await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", Png());

        await Svc(db, Ana).EnviarARevisionAsync(id);
        await Svc(db, Lider).AprobarAsync(id);

        Assert.NotEmpty((await Svc(db, Operaciones).BytesDeImagenAsync(imagen!.Id)).bytes);
    }

    [Fact]
    public async Task Bytes_DeLoRetirado_DejanDeServirse()
    {
        // Es la misma regla que el foro aplica a sus capturas: si la imagen se siguiera sirviendo,
        // retirar un artículo no querría decir nada.
        var db = BaseConLider();
        int id = await PublicadoDeAnaAsync(db);
        var (_, _, imagen) = await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", Png());

        Assert.NotEmpty((await Svc(db, Operaciones).BytesDeImagenAsync(imagen!.Id)).bytes);

        await Svc(db, Lider).RechazarAsync(id, "Dejó de ser cierto desde el cambio de servidor.");

        Assert.Empty((await Svc(db, Operaciones).BytesDeImagenAsync(imagen.Id)).bytes);
        // Para su autora y para el líder sigue estando: son quienes tienen que corregirlo.
        Assert.NotEmpty((await Svc(db, Ana).BytesDeImagenAsync(imagen.Id)).bytes);
        Assert.NotEmpty((await Svc(db, Lider).BytesDeImagenAsync(imagen.Id)).bytes);
    }

    [Fact]
    public async Task Bytes_DeUnNumeroQueNoExiste_VuelvenVacios_NoRevientan()
    {
        var db = BaseConLider();
        Assert.Empty((await Svc(db, Ana).BytesDeImagenAsync(9999)).bytes);
    }

    // ── El que PINTA ─────────────────────────────────────────────────────────────

    [Fact]
    public void ElQuePinta_NoConstruyeMarcado_YElSrcSaleDelNUMERO()
    {
        // Todo lo de arriba comprueba que el SERVIDOR no entrega ninguna dirección tecleada. Falta
        // la otra mitad, y ninguna prueba de servicio la ve: que quien pinta no se la invente. Sin
        // esto, alguien podía «mejorar» el componente para meter en un src lo que trae un segmento y
        // nada se pondría rojo — que es exactamente como vuelven los agujeros que ya estaban
        // cerrados. Se lee el marcado como TEXTO porque no hay forma de renderizar un componente
        // desde aquí, y no se va a añadir una librería para conseguirla.
        var pinta = MarcadoDelCliente("Paginas", "Conocimiento", "CuerpoDeConocimiento.razor");
        var edita = MarcadoDelCliente("Paginas", "Conocimiento", "EscribirArticulo.razor");

        foreach (var razor in new[] { pinta, edita })
        {
            // Se buscan los USOS y no la palabra: los comentarios de esos archivos la nombran
            // precisamente para decir que no se usa.
            Assert.DoesNotContain("new MarkupString", razor);
            Assert.DoesNotContain("(MarkupString)", razor);
            Assert.DoesNotContain(".innerHTML", razor);
        }

        // La ruta se arma con el entero, y con nada más.
        Assert.Contains("src=\"@Ruta(bloque)\"", pinta);
        Assert.Contains("$\"api/adjuntos/conocimiento/{bloque.ImagenId}\"", pinta);
        Assert.Contains("src=\"@Ruta(img)\"", edita);
        Assert.Contains("$\"api/adjuntos/conocimiento/{imagen.Id}\"", edita);

        // Y nunca con un trozo del cuerpo: ni el texto de un segmento ni su dirección.
        Assert.DoesNotContain("src=\"@s.", pinta);
        Assert.DoesNotContain("src=\"@bloque.", pinta);
    }

    /// <summary>
    /// Un archivo de marcado del cliente, buscando la raíz del repositorio hacia arriba. Es el mismo
    /// camino que usa <c>RecorridosGuiadosTests</c>, por la misma razón: la carpeta de compilación
    /// cuelga del proyecto y no se sabe a qué profundidad corre esto.
    /// </summary>
    private static string MarcadoDelCliente(params string[] partes)
    {
        const string relativa = "src/Web/AdminWeb.Client/AdminWeb.Client.csproj";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato))
                return File.ReadAllText(Path.Combine([Path.GetDirectoryName(candidato)!, .. partes]));
            carpeta = carpeta.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No se encontró «{relativa}» subiendo desde {AppContext.BaseDirectory}.");
    }

    // ── Listar y borrar en cascada ───────────────────────────────────────────────

    [Fact]
    public async Task Listar_DevuelveLasDelArticulo_ConSuMarca_YNoLasDeOtro()
    {
        var db = BaseConLider();
        int mio = await ArticuloDeAnaAsync(db);
        var (_, _, otroArticulo) = await Svc(db, Ana).CrearAsync("Otro artículo", Cuerpo, null);

        var (_, _, imagen) = await Svc(db, Ana).GuardarImagenAsync(mio, "captura.png", Png());
        await Svc(db, Ana).GuardarImagenAsync(otroArticulo!.Id, "ajena.png", Png());

        var lista = await Svc(db, Ana).ImagenesDeAsync(mio);

        var unica = Assert.Single(lista);
        Assert.Equal(imagen!.Id, unica.Id);
        Assert.Equal($"![captura](imagen:{imagen.Id})", unica.Marca);
    }

    [Fact]
    public async Task Listar_DeUnBorradorAjeno_VuelveVacio()
    {
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);
        await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", Png());

        Assert.Empty(await Svc(db, Beto).ImagenesDeAsync(id));
        Assert.Empty(await Svc(db, Lider).ImagenesDeAsync(id));
    }

    [Fact]
    public async Task BorrarElArticulo_SeLlevaSusImagenes()
    {
        // Sin la cascada quedarían megas guardados a los que ya no se puede llegar: el cuerpo que las
        // nombraba se fue con el artículo.
        var db = BaseConLider();
        int id = await ArticuloDeAnaAsync(db);
        await Svc(db, Ana).GuardarImagenAsync(id, "captura.png", Png());

        var (ok, mensaje) = await Svc(db, Ana).EliminarAsync(id);

        Assert.True(ok, mensaje);
        Assert.Empty(Otro(db).KnowledgeImages.AsNoTracking().ToList());
    }
}
