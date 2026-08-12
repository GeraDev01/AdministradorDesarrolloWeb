using AdminWeb.Application.Manual;
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
/// El manual de uso sembrado en la base de conocimiento.
///
/// <para>Lo que aquí se cuida es UNA promesa por encima de todas: <b>el arranque no puede pisar una
/// corrección</b>. El manual está pensado para que la gente lo arregle, y un sembrador que devolviera
/// el texto de fábrica cada madrugada haría que arreglarlo no sirviera de nada. Cada forma de
/// «corregir» tiene aquí su prueba: reescribirlo, retitularlo, retirarlo y borrarlo.</para>
///
/// <para>La segunda mitad son las trampas del FORMATO, que no dan error de compilación ni fallan en
/// ejecución: solo se ven feas en pantalla el día que alguien abre el artículo. Un punto de lista
/// partido en dos renglones cierra la lista, y un párrafo partido a mano sale cortado por donde lo
/// partió el editor de código, porque el cuerpo se pinta respetando los saltos de línea.</para>
/// </summary>
public class ManualDeUsoTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    private AppDbContext Nueva()
    {
        var db = TestDb.New();
        _contextos.Add(db);
        return db;
    }

    /// <summary>
    /// Otro contexto contra la MISMA base. Cada arranque tiene el suyo, así que sembrar dos veces
    /// sobre el mismo contexto probaría un escenario que no existe: escondería si la segunda pasada
    /// está leyendo la base o lo que dejó rastreado la primera.
    /// </summary>
    private AppDbContext Otro(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int Total => ManualDeUso.Articulos.Count;

    private static ICurrentUser Desarrollador => new UsuarioDePrueba
    { UserId = 10, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador };

    private static ICurrentUser Lider => new UsuarioDePrueba
    { UserId = 1, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin };

    private static ICurrentUser Operaciones => new UsuarioDePrueba
    { UserId = 20, Username = "ops", FullName = "Ops", Role = UserRole.Operaciones };

    private ConocimientoService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = Otro(db);
        return new ConocimientoService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx));
    }

    // ── El catálogo, antes de tocar ninguna base ─────────────────────────────────

    [Fact]
    public void Catalogo_NoRepiteClavesNiTitulos()
    {
        var claves = ManualDeUso.Articulos.Select(a => a.Clave).ToList();
        Assert.Equal(claves.Count, claves.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Los títulos también, y no por estética: son la segunda comprobación contra los duplicados,
        // así que dos artículos con el mismo título harían que el segundo no se sembrara nunca.
        var titulos = ManualDeUso.Articulos.Select(a => a.Titulo).ToList();
        Assert.Equal(titulos.Count, titulos.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
    }

    [Fact]
    public void Catalogo_TitulosYEtiquetasCabenEnLaBase()
    {
        foreach (var a in ManualDeUso.Articulos)
        {
            Assert.InRange(a.Titulo.Length, ConocimientoService.MinTitulo, ConocimientoService.MaxTitulo);
            Assert.True(a.Etiquetas.Length <= ConocimientoService.MaxEtiquetas,
                $"Las etiquetas de «{a.Clave}» no caben en la columna.");
        }
    }

    /// <summary>
    /// Las etiquetas se escriben a mano porque el sembrador inserta la fila sin pasar por el
    /// servicio. Si una llevara mayúscula o un espacio de más, partiría en dos el índice de la base
    /// —«Manual» y «manual» serían dos temas— y nadie lo notaría hasta ver la nube de etiquetas.
    /// </summary>
    [Fact]
    public void Catalogo_EtiquetasYaNormalizadas()
    {
        foreach (var a in ManualDeUso.Articulos)
        {
            var sueltas = a.Etiquetas.Split(',');
            foreach (var e in sueltas)
            {
                Assert.Equal(e.Trim(), e.Trim().ToLower());
                Assert.NotEqual("", e.Trim());
            }

            var limpias = sueltas.Select(e => e.Trim()).ToList();
            Assert.Equal(string.Join(", ", limpias), a.Etiquetas);
            Assert.Equal(limpias.Count, limpias.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
        }
    }

    [Fact]
    public void Catalogo_TodoLlevaLaEtiquetaDelManual()
    {
        foreach (var a in ManualDeUso.Articulos)
            Assert.Contains(ManualDeUso.Etiqueta,
                a.Etiquetas.Split(',', StringSplitOptions.TrimEntries));

        // Y el glosario existe de verdad: son los que se consultan de pasada, y sin ellos el manual
        // deja de responder «¿qué quiere decir plazo?», que es la pregunta más frecuente del día uno.
        int terminos = ManualDeUso.Articulos.Count(
            a => a.Etiquetas.Split(',', StringSplitOptions.TrimEntries)
                  .Contains(ManualDeUso.EtiquetaDelGlosario));
        Assert.True(terminos >= 6, "El glosario se quedó corto.");
    }

    [Fact]
    public void Catalogo_CuerposDentroDeLosLimites()
    {
        foreach (var a in ManualDeUso.Articulos)
        {
            var cuerpo = ConocimientoTexto.Sanear(a.Cuerpo);

            // El mínimo es el que el propio servicio exige para mandar algo a revisar: un artículo
            // del manual no puede ser más flojo que lo que se le pide a cualquiera del equipo.
            Assert.True(cuerpo.Length >= ConocimientoService.MinCuerpoParaRevisar,
                $"«{a.Clave}» se quedó en nada.");
            Assert.True(cuerpo.Length <= ConocimientoTexto.MaxCuerpo,
                $"«{a.Clave}» no cabe: hay que partirlo en varios artículos.");
        }
    }

    /// <summary>
    /// La trampa del formato. El cuerpo se pinta con <c>white-space: pre-wrap</c>, así que los saltos
    /// de línea que se escriban aquí salen tal cual en pantalla: un párrafo partido a mano se lee
    /// cortado por donde lo partió el editor, y la continuación de un punto de lista —que empieza
    /// sangrada— CIERRA la lista y se convierte en un párrafo suelto debajo.
    ///
    /// <para>Nada de eso da error: solo se ve mal, y solo cuando alguien abre el artículo. Por eso se
    /// comprueba aquí: ninguna línea del manual puede empezar con espacio.</para>
    /// </summary>
    [Fact]
    public void Catalogo_NingunaLineaEmpiezaSangrada()
    {
        foreach (var a in ManualDeUso.Articulos)
            foreach (var linea in ConocimientoTexto.Sanear(a.Cuerpo).Split('\n'))
                Assert.False(linea.Length > 0 && (linea[0] == ' ' || linea[0] == '\t'),
                    $"«{a.Clave}» tiene una línea sangrada, y eso parte una lista o un párrafo: «{linea}»");
    }

    /// <summary>
    /// Que el marcado se entienda: cada artículo de área tiene que producir títulos y listas, no un
    /// muro de párrafos. Y ninguno puede rozar el tope de bloques, que dejaría el texto cortado con
    /// unos puntos suspensivos al final.
    /// </summary>
    [Fact]
    public void Catalogo_SeAnalizaEnBloquesDeVerdad()
    {
        foreach (var a in ManualDeUso.Articulos)
        {
            var bloques = ConocimientoTexto.Analizar(a.Cuerpo);
            Assert.NotEmpty(bloques);
            Assert.True(bloques.Count < ConocimientoTexto.MaxBloques, $"«{a.Clave}» tiene demasiados bloques.");
        }

        // Los de área llevan secciones; los del glosario son dos líneas y no deben llevarlas.
        var areas = ManualDeUso.Articulos.Where(a => !a.Clave.StartsWith("glosario-", StringComparison.Ordinal));
        foreach (var a in areas)
            Assert.Contains(ConocimientoTexto.Analizar(a.Cuerpo), b => b.Tipo == TipoDeBloque.Titulo);
    }

    // ── La siembra ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Siembra_EnBaseVacia_DejaTodoPublicadoYSinCuenta()
    {
        var db = Nueva();

        int agregados = await ManualDeUso.SembrarAsync(db);
        Assert.Equal(Total, agregados);

        var articulos = await Otro(db).KnowledgeArticles.AsNoTracking().ToListAsync();
        Assert.Equal(Total, articulos.Count);

        foreach (var a in articulos)
        {
            Assert.Equal(KnowledgeStatus.Publicado, a.Status);
            Assert.NotNull(a.PublishedAtUtc);
            Assert.Equal(ManualDeUso.Firma, a.AuthorName);
            Assert.Equal(ManualDeUso.SinCuenta, a.AuthorUserId);
            // Sin ficha: no hay a quién abonarle puntos por el manual, y el nulo es lo que lo impide.
            Assert.Null(a.AuthorDeveloperId);
            Assert.Equal(0, a.PointsAwarded);
            Assert.Null(a.PointEntryId);
        }
    }

    [Fact]
    public async Task Siembra_DosVeces_NoAgregaNadaLaSegunda()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        int segunda = await ManualDeUso.SembrarAsync(Otro(db));
        Assert.Equal(0, segunda);
        Assert.Equal(Total, await Otro(db).KnowledgeArticles.CountAsync());
    }

    /// <summary>
    /// La promesa central: una corrección sobrevive al arranque siguiente. Se corrige de la forma más
    /// hostil posible —cambiando también el TÍTULO—, que es justo lo que rompería un sembrador que
    /// reconociera los artículos por su nombre.
    /// </summary>
    [Fact]
    public async Task Siembra_NoPisaUnaCorreccion_AunqueLeCambienElTitulo()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var ctx = Otro(db);
        var articulo = await ctx.KnowledgeArticles.FirstAsync(a => a.Title.Contains("segundo factor"));
        articulo.Title        = "Segundo factor: lo que hay que hacer aquí, con nuestros teléfonos";
        articulo.Body         = "Lo reescribimos entero porque lo de fábrica no aplicaba a este equipo.";
        articulo.Tags         = "manual, acceso, segundo factor, nuestro";
        articulo.UpdatedAtUtc = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ManualDeUso.SembrarAsync(Otro(db)));

        var despues = await Otro(db).KnowledgeArticles.AsNoTracking().ToListAsync();
        Assert.Equal(Total, despues.Count);
        Assert.Contains(despues, a => a.Body.StartsWith("Lo reescribimos entero", StringComparison.Ordinal));
    }

    /// <summary>
    /// Retirar un artículo del manual es una decisión —el líder decidió que aquí no aplica— y el
    /// arranque no la deshace: no lo vuelve a publicar ni inserta una copia publicada al lado.
    /// </summary>
    [Fact]
    public async Task Siembra_NoVuelveAPublicarLoQueElLiderRetiro()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var ctx = Otro(db);
        var articulo = await ctx.KnowledgeArticles.FirstAsync();
        articulo.Status        = KnowledgeStatus.Rechazado;
        articulo.ReviewComment = "Este trámite aquí es otro.";
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ManualDeUso.SembrarAsync(Otro(db)));

        var final = await Otro(db).KnowledgeArticles.AsNoTracking().ToListAsync();
        Assert.Equal(Total, final.Count);
        Assert.Single(final, a => a.Status == KnowledgeStatus.Rechazado);
    }

    [Fact]
    public async Task Siembra_NoResucitaUnArticuloBorrado()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var ctx = Otro(db);
        ctx.KnowledgeArticles.Remove(await ctx.KnowledgeArticles.FirstAsync());
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ManualDeUso.SembrarAsync(Otro(db)));
        Assert.Equal(Total - 1, await Otro(db).KnowledgeArticles.CountAsync());
    }

    /// <summary>
    /// La red por debajo. El registro vive en una tabla que se enseña en Configuración, así que
    /// alguien puede vaciarla; sin la comprobación por título, eso insertaría el manual entero por
    /// segunda vez y dejaría cada artículo duplicado.
    /// </summary>
    [Fact]
    public async Task Siembra_SinElRegistro_NoDuplicaPorqueReconoceElTitulo()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var ctx = Otro(db);
        ctx.AppSettings.Remove(await ctx.AppSettings.FirstAsync(s => s.Key == ManualDeUso.ClaveDelRegistro));
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ManualDeUso.SembrarAsync(Otro(db)));
        Assert.Equal(Total, await Otro(db).KnowledgeArticles.CountAsync());

        // Y el registro queda repuesto, para que la próxima vez no dependa otra vez de los títulos.
        var registro = await Otro(db).AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == ManualDeUso.ClaveDelRegistro);
        Assert.NotNull(registro);
        Assert.Contains("primeros-pasos", registro!.Value);
    }

    /// <summary>
    /// Una base que ya tenía el manual y a la que llega una versión con un artículo más: se siembra
    /// ESE y nada más. Es el caso normal de cualquier actualización posterior.
    /// </summary>
    [Fact]
    public async Task Siembra_SoloAgregaElArticuloQueFalta()
    {
        var db = Nueva();
        var faltante = ManualDeUso.Articulos[^1];

        db.AppSettings.Add(new AppSetting
        {
            Key   = ManualDeUso.ClaveDelRegistro,
            Value = string.Join(",", ManualDeUso.Articulos.Select(a => a.Clave).Where(c => c != faltante.Clave))
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await ManualDeUso.SembrarAsync(Otro(db)));

        var articulos = await Otro(db).KnowledgeArticles.AsNoTracking().ToListAsync();
        Assert.Single(articulos);
        Assert.Equal(faltante.Titulo, articulos[0].Title);
    }

    /// <summary>
    /// Alguien del equipo ya había escrito un artículo con ese mismo título. No se pisa y no se
    /// duplica: se reconoce como puesto y se anota la clave.
    /// </summary>
    [Fact]
    public async Task Siembra_RespetaUnArticuloAjenoConElMismoTitulo()
    {
        var db = Nueva();
        var suyo = ManualDeUso.Articulos[0];

        db.KnowledgeArticles.Add(new KnowledgeArticle
        {
            Title        = suyo.Titulo,
            Body         = "Lo escribí yo mucho antes de que existiera el manual, y quiero que se quede.",
            Status       = KnowledgeStatus.Publicado,
            AuthorUserId = 10,
            AuthorName   = "Ana",
            CreatedAtUtc = DateTime.UtcNow.AddYears(-1)
        });
        await db.SaveChangesAsync();

        Assert.Equal(Total - 1, await ManualDeUso.SembrarAsync(Otro(db)));

        var mismos = await Otro(db).KnowledgeArticles.AsNoTracking()
            .Where(a => a.Title == suyo.Titulo).ToListAsync();
        Assert.Single(mismos);
        Assert.Equal("Ana", mismos[0].AuthorName);
    }

    /// <summary>
    /// El orden de lectura. La lista ordena por fecha descendente, así que sin escalonar las fechas
    /// el manual saldría en el orden en que la base repartió los identificadores.
    /// </summary>
    [Fact]
    public async Task Siembra_ElPrimerArticuloDelCatalogoQuedaArriba()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var primero = await Otro(db).KnowledgeArticles.AsNoTracking()
            .OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .FirstAsync();

        Assert.Equal(ManualDeUso.Articulos[0].Titulo, primero.Title);
    }

    // ── Cómo se ve desde la aplicación ───────────────────────────────────────────

    /// <summary>
    /// Que estén en la base no basta: tienen que ENCONTRARSE. Se comprueba con el buscador de verdad
    /// y desde la sesión con menos permisos que existe, que es la que más fácil se queda fuera.
    /// </summary>
    [Fact]
    public async Task Manual_LoEncuentraCualquieraPorSuEtiqueta()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var pagina = await Svc(db, Operaciones).BuscarAsync(
            new ConocimientoFiltro(Etiqueta: ManualDeUso.Etiqueta), 1, ConocimientoService.TamanoPaginaMaximo);

        Assert.Equal(Total, pagina.Total);

        var glosario = await Svc(db, Operaciones).BuscarAsync(
            new ConocimientoFiltro(Etiqueta: ManualDeUso.EtiquetaDelGlosario));
        Assert.True(glosario.Total >= 6);
    }

    /// <summary>
    /// De quién son, visto desde la pantalla: de nadie. No le salen a nadie como suyos, un
    /// desarrollador no puede reescribirlos por su cuenta y el líder sí puede corregirlos.
    /// </summary>
    [Fact]
    public async Task Manual_NoEsDeNadieYSoloElLiderLoCorrige()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        int id = await Otro(db).KnowledgeArticles.AsNoTracking().Select(a => a.Id).FirstAsync();

        var comoDesarrollador = await Svc(db, Desarrollador).LeerAsync(id);
        Assert.NotNull(comoDesarrollador);
        Assert.False(comoDesarrollador!.EsMio);
        Assert.False(comoDesarrollador.PuedoEditar);
        Assert.Equal(ManualDeUso.Firma, comoDesarrollador.Autor);

        var comoLider = await Svc(db, Lider).LeerAsync(id);
        Assert.True(comoLider!.PuedoEditar);
        Assert.False(comoLider.EsMio);
    }

    /// <summary>
    /// Y cuando el líder lo corrige, NO cae a la cola de revisión. Que el artículo no tenga cuenta
    /// detrás es lo que lo garantiza: volver a la cola solo le pasa a un autor que no es líder.
    /// </summary>
    [Fact]
    public async Task Manual_ElLiderLoCorrigeSinQueVuelvaALaCola()
    {
        var db = Nueva();
        await ManualDeUso.SembrarAsync(db);

        var articulo = await Otro(db).KnowledgeArticles.AsNoTracking().FirstAsync();

        var (ok, _) = await Svc(db, Lider).EditarAsync(
            articulo.Id, articulo.Title,
            articulo.Body + "\n\nAquí el trámite lleva además la firma del área de finanzas.",
            articulo.Tags);

        Assert.True(ok);

        var despues = await Otro(db).KnowledgeArticles.AsNoTracking().FirstAsync(a => a.Id == articulo.Id);
        Assert.Equal(KnowledgeStatus.Publicado, despues.Status);
        Assert.Contains("finanzas", despues.Body);
    }
}
