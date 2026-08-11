using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lectura del foro para la web: el muro paginado, el hilo y la auditoría.
///
/// Se prueban tres cosas, y ninguna es cosmética:
///
///  1. Que las consultas las EJECUTE el motor. El muro filtra, ordena y recorta en SQL; si algo de
///     eso dejara de traducirse, EF no avisa en compilación —revienta en producción, o peor, se
///     trae la tabla entera en silencio—. Correrlas contra un motor real es la única forma de
///     saberlo.
///  2. Que el cuerpo llegue TROCEADO y con los enlaces ya validados: es lo que impide que una
///     publicación ejecute código en el navegador de quien la lee.
///  3. Que el texto de lo retirado NO salga en ningún DTO, ni para el administrador.
/// </summary>
public class ForoQueryServiceTests
{
    private static ForumService Escritura(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static ForoQueryService Lectura(AppDbContext db, ICurrentUser cu) => new(db, cu);

    private static ICurrentUser Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador) =>
        ForumServiceTests.Quien(userId, nombre, rol);

    // ── El muro ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Muro_PaginaEnElServidorYDevuelveElTotalCompleto()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        for (int i = 1; i <= 7; i++)
            await Escritura(db, ana).PublicarAsync($"Publicación {i}", "Cuerpo de prueba suficiente.", ForumTopic.Idea);

        var primera = await Lectura(db, ana).MuroAsync(pagina: 1, tamano: 3);
        var tercera = await Lectura(db, ana).MuroAsync(pagina: 3, tamano: 3);

        Assert.Equal(3, primera.Filas.Count);
        Assert.Equal(7, primera.Total);          // el total es del filtro, no de la página
        Assert.Equal(3, primera.TotalPaginas);
        Assert.Single(tercera.Filas);            // la última trae el resto, no una página llena
        Assert.Empty(primera.Filas.Select(f => f.Id).Intersect(tercera.Filas.Select(f => f.Id)));
    }

    [Fact]
    public async Task Muro_TopeaElTamanoDePaginaAunqueLoPidanEnorme()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        await Escritura(db, ana).PublicarAsync("Una", "Cuerpo de prueba suficiente.", ForumTopic.Idea);

        // Sin el tope, «?tamano=100000» anularía la paginación desde el navegador.
        var pagina = await Lectura(db, ana).MuroAsync(tamano: 100_000);
        Assert.Equal(ForoQueryService.TamanoPaginaMaximo, pagina.TamanoPagina);
    }

    [Fact]
    public async Task Muro_OrdenaFijadasPrimeroYLuegoPorUltimaActividad()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var lider = Quien(2, "Lider", UserRole.Admin);

        var (_, _, vieja) = await Escritura(db, ana).PublicarAsync("Vieja", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        var (_, _, nueva) = await Escritura(db, ana).PublicarAsync("Nueva", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        var (_, _, fijada) = await Escritura(db, lider).PublicarAsync("Fijada", "Cuerpo de prueba suficiente.", ForumTopic.Anuncio);
        await Escritura(db, lider).FijarAsync(fijada!.Id, true);

        // Un comentario revive el hilo viejo: tiene que volver a subir por encima del nuevo.
        await Escritura(db, ana).ComentarAsync(vieja!.Id, "Sigo con esto.");

        var muro = await Lectura(db, ana).MuroAsync();
        Assert.Equal([fijada.Id, vieja.Id, nueva!.Id], muro.Filas.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task Muro_CuentaComentariosVivosMeGustaEImagenes()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var beto = Quien(2, "Beto");

        var (_, _, post) = await Escritura(db, ana).PublicarAsync("Con actividad", "Cuerpo de prueba suficiente.", ForumTopic.Pregunta);
        await Escritura(db, beto).ComentarAsync(post!.Id, "Primer comentario.");
        var (_, _, segundo) = await Escritura(db, beto).ComentarAsync(post.Id, "Segundo comentario.");
        await Escritura(db, beto).RetirarAsync(segundo!.Id);
        await Escritura(db, beto).MeGustaAsync(post.Id);

        var deAna = (await Lectura(db, ana).MuroAsync()).Filas.Single();
        var deBeto = (await Lectura(db, beto).MuroAsync()).Filas.Single();

        Assert.Equal(1, deAna.Comentarios);       // el retirado no se cuenta como conversación viva
        Assert.Equal(1, deAna.MeGusta);
        Assert.False(deAna.YoDiMeGusta);          // el ❤ es de Beto
        Assert.True(deBeto.YoDiMeGusta);
        Assert.True(deAna.EsMia);
        Assert.False(deBeto.EsMia);
    }

    [Fact]
    public async Task Muro_FiltraPorTextoTemaAutorYSoloMias()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var beto = Quien(2, "Beto");

        await Escritura(db, ana).PublicarAsync("Índices en SQL", "Bajó de 40 s a 1.2 s.", ForumTopic.Aprendizaje, "sql");
        await Escritura(db, beto).PublicarAsync("Comida del viernes", "¿Alguien se apunta?", ForumTopic.Otro);

        var lectura = Lectura(db, ana);
        // Los términos van sin acentos a propósito: estas pruebas corren sobre SQLite, cuyo LOWER()
        // solo cubre ASCII, así que «indices» no encontraría «Índices». En SQL Server —que es
        // Unicode y es la base de verdad— sí lo haría. Lo que sí se comprueba aquí es que no
        // distingue mayúsculas y que mira en los cuatro sitios.
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Texto: "en sql"))).Filas);     // en el título
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Texto: "40 s"))).Filas);       // en el cuerpo
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Texto: "SQL"))).Filas);        // en las etiquetas
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Texto: "Beto"))).Filas);       // y en el autor
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Tema: ForumTopic.Otro))).Filas);
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(Autor: "Beto"))).Filas);
        Assert.Single((await lectura.MuroAsync(new ForumFiltro(SoloMios: true))).Filas);
        Assert.Empty((await lectura.MuroAsync(new ForumFiltro(Texto: "no existe eso"))).Filas);
    }

    [Fact]
    public async Task Muro_NoEncuentraElTextoDeUnaEntradaRetiradaNiLoDevuelve()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var (_, _, post) = await Escritura(db, ana)
            .PublicarAsync("Se me escapó", "La contraseña del servidor es zanahoria.", ForumTopic.Otro);
        await Escritura(db, ana).RetirarAsync(post!.Id);

        var lectura = Lectura(db, ana);

        // Si se pudiera encontrar por su texto, retirarla no serviría de nada.
        Assert.Empty((await lectura.MuroAsync(new ForumFiltro(Texto: "zanahoria"))).Filas);

        // Y sigue en el muro —no desaparece—, pero sin el original.
        var tarjeta = (await lectura.MuroAsync()).Filas.Single();
        Assert.True(tarjeta.Retirada);
        Assert.DoesNotContain("zanahoria", tarjeta.Extracto);
        Assert.Equal("Se me escapó", tarjeta.Titulo);
    }

    // ── El hilo ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hilo_DevuelveLaConversacionEnOrdenDeLecturaYConSuSangria()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var beto = Quien(2, "Beto");

        var (_, _, post) = await Escritura(db, ana).PublicarAsync("Raíz", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        var (_, _, uno) = await Escritura(db, beto).ComentarAsync(post!.Id, "Respuesta uno.");
        await Escritura(db, ana).ComentarAsync(uno!.Id, "Respuesta a la respuesta.");
        await Escritura(db, beto).ComentarAsync(post.Id, "Respuesta dos.");

        var hilo = await Lectura(db, ana).HiloAsync(post.Id);

        Assert.NotNull(hilo);
        // Cada respuesta justo debajo de aquello a lo que contesta, no en orden de llegada.
        Assert.Equal([0, 1, 2, 1], hilo!.Entradas.Select(e => e.Nivel).ToArray());
        Assert.Equal(3, hilo.Comentarios);
        Assert.True(hilo.Entradas[0].EsPublicacion);
        Assert.Equal("Raíz", hilo.Entradas[0].Titulo);
        Assert.All(hilo.Entradas.Skip(1), e => Assert.Null(e.Titulo));
    }

    [Fact]
    public async Task Hilo_NoDevuelveElTextoDeLoRetiradoPeroConservaSuHueco()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var beto = Quien(2, "Beto");

        var (_, _, post) = await Escritura(db, ana).PublicarAsync("Raíz", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        var (_, _, comentario) = await Escritura(db, beto).ComentarAsync(post!.Id, "Esto no debía decirlo.");
        await Escritura(db, ana).ComentarAsync(comentario!.Id, "¿Seguro?");
        await Escritura(db, beto).RetirarAsync(comentario.Id);

        var hilo = await Lectura(db, ana).HiloAsync(post.Id);

        // El hueco se queda: sin él, la respuesta de abajo contestaría a algo que ya no está.
        Assert.Equal(3, hilo!.Entradas.Count);
        var retirado = hilo.Entradas.Single(e => e.Id == comentario.Id);
        Assert.True(retirado.Retirada);
        Assert.DoesNotContain("no debía decirlo", string.Concat(retirado.Cuerpo.Select(s => s.Texto)));
        Assert.All(retirado.Cuerpo, s => Assert.Null(s.Url));
    }

    [Fact]
    public async Task Hilo_TroceaElCuerpoYSoloDejaEnlazableLoQueSeaHttpOHttps()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var (_, _, post) = await Escritura(db, ana).PublicarAsync(
            "Enlaces",
            "Mira https://ejemplo.com/guia y ojo con javascript:alert(1) y con [esto](javascript:alert(2)).",
            ForumTopic.Aprendizaje);

        var entrada = (await Lectura(db, ana).HiloAsync(post!.Id))!.Entradas.Single();

        // Concatenar los trozos devuelve exactamente lo que el lector ve.
        Assert.Contains("javascript:alert(1)", string.Concat(entrada.Cuerpo.Select(s => s.Texto)));

        var enlaces = entrada.Cuerpo.Where(s => s.Url != null).Select(s => s.Url!).ToList();
        Assert.Single(enlaces);
        Assert.StartsWith("https://ejemplo.com/guia", enlaces[0]);
        // Lo que no es http/https se queda como texto: en la web un javascript: en un href se
        // ejecuta en el navegador de quien lee, con su sesión.
        Assert.DoesNotContain(enlaces, u => u.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Hilo_DevuelveNuloCuandoLaPublicacionYaNoExiste()
    {
        var db = TestDb.New();
        Assert.Null(await Lectura(db, Quien(1, "Ana")).HiloAsync(12345));
    }

    // ── Auditoría ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Auditoria_EsSoloDelAdministrador()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        await Escritura(db, ana).PublicarAsync("Algo", "Cuerpo de prueba suficiente.", ForumTopic.Idea);

        await Assert.ThrowsAsync<AuthorizationException>(() => Lectura(db, ana).AuditoriaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Lectura(db, ana).OpcionesDeAuditoriaAsync());

        var lider = Quien(9, "Lider", UserRole.Admin);
        Assert.Equal(1, (await Lectura(db, lider).AuditoriaAsync()).Total);
    }

    [Fact]
    public async Task Auditoria_TraePublicacionesYComentariosConSuEstadoYSinElTextoRetirado()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var lider = Quien(9, "Lider", UserRole.Admin);

        var (_, _, post) = await Escritura(db, ana).PublicarAsync("Raíz", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        var (_, _, comentario) = await Escritura(db, ana).ComentarAsync(post!.Id, "Un secreto: zanahoria.");
        await Escritura(db, ana).RetirarAsync(comentario!.Id);

        var filas = (await Lectura(db, lider).AuditoriaAsync()).Filas;

        Assert.Equal(2, filas.Count);
        var retirada = filas.Single(f => f.Id == comentario.Id);
        Assert.True(retirada.Retirada);
        // Sin el bote de basura que llevaba delante: los pictogramas los dibuja el sistema operativo
        // y donde no hay fuente de emoji salen como un cuadro vacío. Cae la MARCA, no la palabra, así
        // que la columna sigue diciendo lo mismo.
        Assert.Equal("Retirada", retirada.Estado);
        // Ni siquiera al administrador: la vista de auditoría dice QUÉ pasó, no devuelve el original.
        Assert.DoesNotContain("zanahoria", retirada.Texto);
        // Un comentario no sale huérfano: lleva el título de su hilo.
        Assert.Equal("Raíz", retirada.Hilo);
    }

    // ── Opciones de los filtros ──────────────────────────────────────────────────

    [Fact]
    public async Task Opciones_DelMuroSoloOfreceAutoresQueHayanPublicado()
    {
        var db = TestDb.New();
        var ana = Quien(1, "Ana");
        var beto = Quien(2, "Beto");

        var (_, _, post) = await Escritura(db, ana).PublicarAsync("Raíz", "Cuerpo de prueba suficiente.", ForumTopic.Idea);
        await Escritura(db, beto).ComentarAsync(post!.Id, "Solo comento.");

        var delMuro = await Lectura(db, ana).OpcionesDelMuroAsync();
        // Ofrecer a Beto dejaría un filtro que siempre devuelve el muro vacío.
        Assert.Equal(["Ana"], delMuro.Autores);
        Assert.Equal(Enum.GetValues<ForumTopic>().Length, delMuro.Temas.Count);

        var deAuditoria = await Lectura(db, Quien(9, "Lider", UserRole.Admin)).OpcionesDeAuditoriaAsync();
        Assert.Equal(["Ana", "Beto"], deAuditoria.Autores);
    }

    [Fact]
    public async Task SinSesion_NoSeLeeElForo()
    {
        var db = TestDb.New();
        var anonimo = UsuarioDePrueba.Anonimo();

        await Assert.ThrowsAsync<AuthorizationException>(() => Lectura(db, anonimo).MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Lectura(db, anonimo).HiloAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() => Lectura(db, anonimo).OpcionesDelMuroAsync());
    }
}
