using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Foro del equipo: publicaciones, comentarios anidados y auditoría.
///
/// Lo que más importa aquí no es publicar sino que la CONVERSACIÓN se conserve entendible: que un
/// hilo se lea en orden, que retirar algo deje su hueco, y que cerrar un hilo lo cierre entero y no
/// solo su primer mensaje.
/// </summary>
public class ForumServiceTests
{
    private static ForumService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    /// <summary>
    /// Una identidad con nombre propio. El escritorio metía aquí un <c>User</c> entero porque su
    /// CurrentUserContext lo tenía cargado; con ICurrentUser basta el nombre, que es lo único que
    /// el foro necesita para firmar.
    /// </summary>
    internal static ICurrentUser Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador) =>
        new UsuarioDePrueba
        {
            UserId = userId, Username = nombre.ToLowerInvariant(), FullName = nombre, Role = rol
        };

    // ── Publicar y comentar ──────────────────────────────────────────────────────

    [Fact]
    public async Task Publicar_GuardaYSeVuelveRaizDeSuPropioHilo()
    {
        var db = TestDb.New();
        var (ok, _, _) = await Svc(db, Quien(1, "Ana"))
            .PublicarAsync("Migrar el reporte mensual", "Bajó de 40 s a 1.2 s con una vista indexada.", ForumTopic.Aprendizaje, "sql, rendimiento");

        Assert.True(ok);
        var g = db.ForumPosts.AsNoTracking().Single();
        Assert.Null(g.ParentId);
        Assert.Equal(g.Id, g.RootId);   // sin esto, traer su hilo no encontraría nada
        Assert.Equal(0, g.Depth);
        Assert.Equal("Ana", g.AuthorName);
        Assert.Equal("sql, rendimiento", g.Tags);
    }

    [Theory]
    [InlineData("ab", "cuerpo suficiente")]   // título corto
    [InlineData("Título válido", "x")]        // cuerpo corto
    public async Task Publicar_RechazaLoIncompleto(string titulo, string cuerpo)
    {
        var db = TestDb.New();
        var (ok, _, _) = await Svc(db, Quien(1, "Ana")).PublicarAsync(titulo, cuerpo, ForumTopic.Idea);
        Assert.False(ok);
        Assert.Empty(db.ForumPosts);
    }

    [Fact]
    public async Task Comentar_CuelgaDelHiloCorrecto()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Un tema", "cuerpo del tema", ForumTopic.Pregunta);

        var (ok, _, c) = await Svc(db, Quien(2, "Beto")).ComentarAsync(post!.Id, "Yo lo resolví así");

        Assert.True(ok);
        Assert.Equal(post.Id, c!.ParentId);
        Assert.Equal(post.Id, c.RootId);
        Assert.Equal(1, c.Depth);
    }

    [Fact]
    public async Task Comentar_UnComentario_CreaElSubhilo()
    {
        var db = TestDb.New();
        var (_, _, post) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = await Svc(db, Quien(2, "Beto")).ComentarAsync(post!.Id, "primer comentario");
        var (_, _, c2) = await Svc(db, Quien(3, "Caro")).ComentarAsync(c1!.Id, "respuesta al comentario");

        Assert.Equal(c1.Id, c2!.ParentId);
        Assert.Equal(post.Id, c2.RootId);   // sigue siendo del mismo hilo
        Assert.Equal(2, c2.Depth);
    }

    [Fact]
    public async Task LaSangriaSeCorta_ParaQueLaConversacionSigaLeyendose()
    {
        // Sin tope, una cadena larga de respuestas se va al margen derecho y deja de leerse.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);

        int padre = post!.Id;
        for (int i = 0; i < ForumService.ProfundidadMaxima + 4; i++)
        {
            var (_, _, c) = await ana.ComentarAsync(padre, $"nivel {i}");
            padre = c!.Id;
        }

        Assert.All(db.ForumPosts.AsNoTracking().ToList(),
            p => Assert.True(p.Depth <= ForumService.ProfundidadMaxima));
    }

    // ── Cerrar un hilo ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CerrarUnHilo_LoCierraEntero_NoSoloSuPrimerMensaje()
    {
        // Si solo bloqueara la raíz, se seguiría respondiendo por dentro a un hilo dado por cerrado.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = await Svc(db, Quien(2, "Beto")).ComentarAsync(post!.Id, "un comentario");

        await Svc(db, Quien(99, "Jefa", UserRole.Admin)).CerrarAsync(post.Id, true);

        Assert.False((await Svc(db, Quien(2, "Beto")).ComentarAsync(post.Id, "otra cosa")).ok);
        Assert.False((await Svc(db, Quien(3, "Caro")).ComentarAsync(c1!.Id, "por la puerta de atrás")).ok);
    }

    [Fact]
    public async Task Reabrir_VuelveAAdmitirComentarios()
    {
        var db = TestDb.New();
        var (_, _, post) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var admin = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        await admin.CerrarAsync(post!.Id, true);
        await admin.CerrarAsync(post.Id, false);

        Assert.True((await Svc(db, Quien(2, "Beto")).ComentarAsync(post.Id, "ya se puede")).ok);
    }

    [Fact]
    public async Task FijarYCerrar_SonSoloDelAdministrador()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);

        await Assert.ThrowsAsync<AuthorizationException>(() => ana.FijarAsync(post!.Id, true));
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.CerrarAsync(post!.Id, true));
    }

    // ── Retirar: nada se borra ───────────────────────────────────────────────────

    [Fact]
    public async Task Retirar_ConservaLaFilaYElHueco()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = await Svc(db, Quien(2, "Beto")).ComentarAsync(post!.Id, "algo que se arrepintió de decir");

        await Svc(db, Quien(2, "Beto")).RetirarAsync(c1!.Id);

        // La fila sigue: un hilo con respuestas que contestan a algo inexistente no se entiende.
        Assert.Equal(2, db.ForumPosts.Count());
        var g = db.ForumPosts.AsNoTracking().Single(p => p.Id == c1.Id);
        Assert.True(g.Eliminado);
        Assert.Contains("eliminado", g.TextoVisible);
    }

    [Fact]
    public async Task Retirar_LoAjeno_SoloPuedeElAdministrador()
    {
        var db = TestDb.New();
        var (_, _, post) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.False((await Svc(db, Quien(2, "Beto")).RetirarAsync(post!.Id)).ok);
        Assert.True((await Svc(db, Quien(99, "Jefa", UserRole.Admin)).RetirarAsync(post.Id)).ok);
    }

    [Fact]
    public async Task NoSePuedeResponderAAlgoRetirado()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = await ana.ComentarAsync(post!.Id, "un comentario");
        await ana.RetirarAsync(c1!.Id);

        Assert.False((await Svc(db, Quien(2, "Beto")).ComentarAsync(c1.Id, "respuesta tardía")).ok);
    }

    [Fact]
    public async Task Editar_SoloLoPropio_YDejaConstancia()
    {
        var db = TestDb.New();
        var (_, _, post) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Título", "cuerpo original", ForumTopic.Idea);

        Assert.False((await Svc(db, Quien(2, "Beto")).EditarAsync(post!.Id, "Otro título", "cuerpo ajeno")).ok);

        Assert.True((await Svc(db, Quien(1, "Ana")).EditarAsync(post.Id, "Título corregido", "cuerpo corregido")).ok);
        var g = db.ForumPosts.AsNoTracking().Single();
        Assert.Equal("cuerpo corregido", g.Body);
        Assert.NotNull(g.EditedAtUtc);   // un foro auditable no edita en silencio
    }

    // ── Me gusta ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MeGusta_AlternaYCuenta()
    {
        var db = TestDb.New();
        var (_, _, post) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var beto = Svc(db, Quien(2, "Beto"));

        var (ok, dado, total) = await beto.MeGustaAsync(post!.Id);
        Assert.True(ok); Assert.True(dado); Assert.Equal(1, total);

        var (_, dado2, total2) = await beto.MeGustaAsync(post.Id);
        Assert.False(dado2); Assert.Equal(0, total2);
    }

    [Fact]
    public async Task MeGusta_NoSeDaALoRetirado()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        await ana.RetirarAsync(post!.Id);

        Assert.False((await Svc(db, Quien(2, "Beto")).MeGustaAsync(post.Id)).ok);
    }

    // ── Muro ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Muro_TraeSoloPublicaciones_ConSusContadores()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var beto = Svc(db, Quien(2, "Beto"));
        await beto.ComentarAsync(post!.Id, "uno");
        await beto.ComentarAsync(post.Id, "dos");
        await beto.MeGustaAsync(post.Id);

        var muro = await beto.MuroAsync();

        Assert.Single(muro);   // los comentarios no son tarjetas del muro
        Assert.Equal(2, muro[0].Comentarios);
        Assert.Equal(1, muro[0].MeGusta);
        Assert.True(muro[0].YoDiMeGusta);
    }

    [Fact]
    public async Task Muro_UnHiloQueRevive_VuelveASubir()
    {
        // Ordenar por fecha de creación dejaría enterrada una conversación que sigue viva.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, viejo) = await ana.PublicarAsync("El viejo", "cuerpo del viejo", ForumTopic.Idea);
        await ana.PublicarAsync("El nuevo", "cuerpo del nuevo", ForumTopic.Idea);

        Assert.Equal("El nuevo", (await ana.MuroAsync())[0].Post.Title);

        await Svc(db, Quien(2, "Beto")).ComentarAsync(viejo!.Id, "esto sigue vigente");

        Assert.Equal("El viejo", (await ana.MuroAsync())[0].Post.Title);
    }

    [Fact]
    public async Task Muro_LasFijadasVanPrimero()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, vieja) = await ana.PublicarAsync("Fijada", "cuerpo de la fijada", ForumTopic.Anuncio);
        await ana.PublicarAsync("Reciente", "cuerpo de la reciente", ForumTopic.Idea);
        await Svc(db, Quien(99, "Jefa", UserRole.Admin)).FijarAsync(vieja!.Id, true);

        Assert.Equal("Fijada", (await ana.MuroAsync())[0].Post.Title);
    }

    [Fact]
    public async Task Muro_LosComentariosRetiradosNoCuentan()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = await ana.ComentarAsync(post!.Id, "uno");
        await ana.ComentarAsync(post.Id, "dos");
        await ana.RetirarAsync(c1!.Id);

        Assert.Equal(1, (await ana.MuroAsync())[0].Comentarios);
    }

    // ── Hilo en orden de lectura ─────────────────────────────────────────────────

    [Fact]
    public async Task Hilo_SeLeeEnOrdenDeConversacion()
    {
        // Cada respuesta justo debajo de aquello a lo que contesta. Sin esto, un hilo es una lista
        // suelta de mensajes y se pierde el sentido de lo que se dijo.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Raíz", "cuerpo raíz", ForumTopic.Pregunta);
        var (_, _, a) = await ana.ComentarAsync(post!.Id, "A");
        var (_, _, b) = await ana.ComentarAsync(post.Id, "B");
        var (_, _, a1) = await ana.ComentarAsync(a!.Id, "A.1");
        await ana.ComentarAsync(a1!.Id, "A.1.1");
        await ana.ComentarAsync(b!.Id, "B.1");

        var hilo = await ana.HiloAsync(post.Id);

        Assert.Equal(
            ["cuerpo raíz", "A", "A.1", "A.1.1", "B", "B.1"],
            hilo.Select(n => n.Post.Body).ToArray());
        Assert.Equal([0, 1, 2, 3, 1, 2], hilo.Select(n => n.Nivel).ToArray());
    }

    [Fact]
    public async Task Hilo_IncluyeLasEntradasRetiradas_ConSuHueco()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Raíz", "cuerpo raíz", ForumTopic.Idea);
        var (_, _, a) = await ana.ComentarAsync(post!.Id, "A");
        await ana.ComentarAsync(a!.Id, "respuesta a A");
        await ana.RetirarAsync(a.Id);

        var hilo = await ana.HiloAsync(post.Id);

        Assert.Equal(3, hilo.Count);   // la retirada sigue ahí sosteniendo a su respuesta
        Assert.Contains(hilo, n => n.Post.Eliminado);
        Assert.Equal(2, hilo.Last().Nivel);
    }

    // ── Auditoría y búsqueda ─────────────────────────────────────────────────────

    [Fact]
    public async Task Auditoria_TraePublicacionesYComentarios()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        await ana.ComentarAsync(post!.Id, "un comentario");

        // La consulta la hace la jefa: la auditoría es del administrador, no de quien publica.
        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Equal(2, (await jefa.AuditoriaAsync()).Count);
        Assert.Single(await ana.MuroAsync());   // el muro solo cuenta publicaciones
    }

    [Fact]
    public async Task Auditoria_EsSoloDelAdministrador()
    {
        // La vista consolidada del rastro (retiradas y ediciones de TODO el foro, con búsqueda)
        // es supervisión. Ni el desarrollador ni Operaciones — guarda positiva, no por descarte.
        var db = TestDb.New();
        await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Quien(1, "Ana")).AuditoriaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Quien(3, "Ops", UserRole.Operaciones)).AuditoriaAsync());
        Assert.Equal(1, (await Svc(db, Quien(99, "Jefa", UserRole.Admin)).AuditoriaAsync()).Count);
    }

    [Fact]
    public async Task ElRastro_DentroDelHilo_SigueSiendoDeTodos()
    {
        // Lo que el desarrollador NO pierde: el hueco del retirado y el hilo completo. Eso es
        // contexto de la conversación, no el expediente consolidado.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, com) = await ana.ComentarAsync(post!.Id, "me arrepiento de esto");
        await ana.RetirarAsync(com!.Id);

        var hilo = await Svc(db, Quien(2, "Beto")).HiloAsync(post.Id);
        Assert.Contains(hilo, n => n.Post.Eliminado);   // el hueco se ve
    }

    [Fact]
    public async Task Busqueda_MiraTituloCuerpoEtiquetasYAutor()
    {
        var db = TestDb.New();
        await Svc(db, Quien(1, "Ana")).PublicarAsync("Índices en SQL", "cómo bajamos el reporte a un segundo", ForumTopic.Aprendizaje, "sql, rendimiento");
        await Svc(db, Quien(2, "Beto")).PublicarAsync("Otra cosa", "nada que ver", ForumTopic.Otro);

        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Single(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "Índices")));
        Assert.Single(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "un segundo")));
        Assert.Single(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "rendimiento")));
        Assert.Single(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "Beto")));
    }

    [Fact]
    public async Task Busqueda_NoEncuentraElTextoDeLoRetirado()
    {
        // Si se pudiera encontrar por su contenido, retirarlo no serviría de nada.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = await ana.PublicarAsync("Tema normal", "una barbaridad irrepetible", ForumTopic.Otro);
        await ana.RetirarAsync(post!.Id);

        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Empty(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "barbaridad")));
        Assert.Single(await jefa.AuditoriaAsync(new ForumFiltro(Texto: "Tema normal")));   // el título sí, para poder auditarla
    }

    [Fact]
    public async Task Filtros_PorTemaYSoloMias()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        await ana.PublicarAsync("De Ana", "cuerpo de Ana", ForumTopic.Idea);
        await Svc(db, Quien(2, "Beto")).PublicarAsync("De Beto", "cuerpo de Beto", ForumTopic.Anuncio);

        Assert.Single(await ana.MuroAsync(new ForumFiltro(Tema: ForumTopic.Anuncio)));
        Assert.Single(await ana.MuroAsync(new ForumFiltro(SoloMios: true)));
        Assert.Equal("De Ana", (await ana.MuroAsync(new ForumFiltro(SoloMios: true)))[0].Post.Title);
    }

    [Fact]
    public async Task ElForoLoVeCualquieraConSesion()
    {
        // El punto del foro es compartir: si solo lo viera una parte del equipo, no sería un foro.
        var db = TestDb.New();
        await Svc(db, Quien(1, "Ana")).PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.Single(await Svc(db, Quien(2, "Beto", UserRole.Desarrollador)).MuroAsync());
        Assert.Single(await Svc(db, Quien(3, "Ops", UserRole.Operaciones)).MuroAsync());
        Assert.Single(await Svc(db, Quien(99, "Jefa", UserRole.Admin)).MuroAsync());
    }

    [Fact]
    public async Task SinSesion_NoSeVeNiSePublica()
    {
        var db = TestDb.New();
        var anonimo = Svc(db, UsuarioDePrueba.Anonimo());

        await Assert.ThrowsAsync<AuthorizationException>(() => anonimo.MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => anonimo.PublicarAsync("Tema", "cuerpo del tema", ForumTopic.Idea));
    }
}
