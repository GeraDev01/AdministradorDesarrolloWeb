using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Foro del equipo: publicaciones, comentarios anidados y auditoría.
///
/// Lo que más importa aquí no es publicar sino que la CONVERSACIÓN se conserve entendible: que un
/// hilo se lea en orden, que retirar algo deje su hueco, y que cerrar un hilo lo cierre entero y no
/// solo su primer mensaje.
/// </summary>
public class ForumServiceTests
{
    private static ForumService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static CurrentUserContext Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador)
    {
        var cu = new CurrentUserContext();
        cu.SetUser(new User { Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre, Role = rol, IsActive = true });
        return cu;
    }

    // ── Publicar y comentar ──────────────────────────────────────────────────────

    [Fact]
    public void Publicar_GuardaYSeVuelveRaizDeSuPropioHilo()
    {
        var db = TestDb.New();
        var (ok, _, post) = Svc(db, Quien(1, "Ana"))
            .Publicar("Migrar el reporte mensual", "Bajó de 40 s a 1.2 s con una vista indexada.", ForumTopic.Aprendizaje, "sql, rendimiento");

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
    public void Publicar_RechazaLoIncompleto(string titulo, string cuerpo)
    {
        var db = TestDb.New();
        var (ok, _, _) = Svc(db, Quien(1, "Ana")).Publicar(titulo, cuerpo, ForumTopic.Idea);
        Assert.False(ok);
        Assert.Empty(db.ForumPosts);
    }

    [Fact]
    public void Comentar_CuelgaDelHiloCorrecto()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Un tema", "cuerpo del tema", ForumTopic.Pregunta);

        var (ok, _, c) = Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "Yo lo resolví así");

        Assert.True(ok);
        Assert.Equal(post.Id, c!.ParentId);
        Assert.Equal(post.Id, c.RootId);
        Assert.Equal(1, c.Depth);
    }

    [Fact]
    public void Comentar_UnComentario_CreaElSubhilo()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "primer comentario");
        var (_, _, c2) = Svc(db, Quien(3, "Caro")).Comentar(c1!.Id, "respuesta al comentario");

        Assert.Equal(c1.Id, c2!.ParentId);
        Assert.Equal(post.Id, c2.RootId);   // sigue siendo del mismo hilo
        Assert.Equal(2, c2.Depth);
    }

    [Fact]
    public void LaSangriaSeCorta_ParaQueLaConversacionSigaLeyendose()
    {
        // Sin tope, una cadena larga de respuestas se va al margen derecho y deja de leerse.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);

        int padre = post!.Id;
        for (int i = 0; i < ForumService.ProfundidadMaxima + 4; i++)
        {
            var (_, _, c) = ana.Comentar(padre, $"nivel {i}");
            padre = c!.Id;
        }

        Assert.All(db.ForumPosts.AsNoTracking().ToList(),
            p => Assert.True(p.Depth <= ForumService.ProfundidadMaxima));
    }

    // ── Cerrar un hilo ───────────────────────────────────────────────────────────

    [Fact]
    public void CerrarUnHilo_LoCierraEntero_NoSoloSuPrimerMensaje()
    {
        // Si solo bloqueara la raíz, se seguiría respondiendo por dentro a un hilo dado por cerrado.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "un comentario");

        Svc(db, Quien(99, "Jefa", UserRole.Admin)).Cerrar(post.Id, true);

        Assert.False(Svc(db, Quien(2, "Beto")).Comentar(post.Id, "otra cosa").ok);
        Assert.False(Svc(db, Quien(3, "Caro")).Comentar(c1!.Id, "por la puerta de atrás").ok);
    }

    [Fact]
    public void Reabrir_VuelveAAdmitirComentarios()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var admin = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        admin.Cerrar(post!.Id, true);
        admin.Cerrar(post.Id, false);

        Assert.True(Svc(db, Quien(2, "Beto")).Comentar(post.Id, "ya se puede").ok);
    }

    [Fact]
    public void FijarYCerrar_SonSoloDelAdministrador()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.Throws<AuthorizationException>(() => ana.Fijar(post!.Id, true));
        Assert.Throws<AuthorizationException>(() => ana.Cerrar(post!.Id, true));
    }

    // ── Retirar: nada se borra ───────────────────────────────────────────────────

    [Fact]
    public void Retirar_ConservaLaFilaYElHueco()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "algo que se arrepintió de decir");

        Svc(db, Quien(2, "Beto")).Retirar(c1!.Id);

        // La fila sigue: un hilo con respuestas que contestan a algo inexistente no se entiende.
        Assert.Equal(2, db.ForumPosts.Count());
        var g = db.ForumPosts.AsNoTracking().Single(p => p.Id == c1.Id);
        Assert.True(g.Eliminado);
        Assert.Contains("eliminado", g.TextoVisible);
    }

    [Fact]
    public void Retirar_LoAjeno_SoloPuedeElAdministrador()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.False(Svc(db, Quien(2, "Beto")).Retirar(post!.Id).ok);
        Assert.True(Svc(db, Quien(99, "Jefa", UserRole.Admin)).Retirar(post.Id).ok);
    }

    [Fact]
    public void NoSePuedeResponderAAlgoRetirado()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = ana.Comentar(post!.Id, "un comentario");
        ana.Retirar(c1!.Id);

        Assert.False(Svc(db, Quien(2, "Beto")).Comentar(c1.Id, "respuesta tardía").ok);
    }

    [Fact]
    public void Editar_SoloLoPropio_YDejaConstancia()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Título", "cuerpo original", ForumTopic.Idea);

        Assert.False(Svc(db, Quien(2, "Beto")).Editar(post!.Id, "Otro título", "cuerpo ajeno").ok);

        Assert.True(Svc(db, Quien(1, "Ana")).Editar(post.Id, "Título corregido", "cuerpo corregido").ok);
        var g = db.ForumPosts.AsNoTracking().Single();
        Assert.Equal("cuerpo corregido", g.Body);
        Assert.NotNull(g.EditedAtUtc);   // un foro auditable no edita en silencio
    }

    // ── Me gusta ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MeGusta_AlternaYCuenta()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var beto = Svc(db, Quien(2, "Beto"));

        var (ok, dado, total) = beto.MeGusta(post!.Id);
        Assert.True(ok); Assert.True(dado); Assert.Equal(1, total);

        var (_, dado2, total2) = beto.MeGusta(post.Id);
        Assert.False(dado2); Assert.Equal(0, total2);
    }

    [Fact]
    public void MeGusta_NoSeDaALoRetirado()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        ana.Retirar(post!.Id);

        Assert.False(Svc(db, Quien(2, "Beto")).MeGusta(post.Id).ok);
    }

    // ── Muro ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Muro_TraeSoloPublicaciones_ConSusContadores()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var beto = Svc(db, Quien(2, "Beto"));
        beto.Comentar(post!.Id, "uno");
        beto.Comentar(post.Id, "dos");
        beto.MeGusta(post.Id);

        var muro = beto.Muro();

        Assert.Single(muro);   // los comentarios no son tarjetas del muro
        Assert.Equal(2, muro[0].Comentarios);
        Assert.Equal(1, muro[0].MeGusta);
        Assert.True(muro[0].YoDiMeGusta);
    }

    [Fact]
    public void Muro_UnHiloQueRevive_VuelveASubir()
    {
        // Ordenar por fecha de creación dejaría enterrada una conversación que sigue viva.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, viejo) = ana.Publicar("El viejo", "cuerpo del viejo", ForumTopic.Idea);
        var (_, _, nuevo) = ana.Publicar("El nuevo", "cuerpo del nuevo", ForumTopic.Idea);

        Assert.Equal("El nuevo", ana.Muro()[0].Post.Title);

        Svc(db, Quien(2, "Beto")).Comentar(viejo!.Id, "esto sigue vigente");

        Assert.Equal("El viejo", ana.Muro()[0].Post.Title);
    }

    [Fact]
    public void Muro_LasFijadasVanPrimero()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, vieja) = ana.Publicar("Fijada", "cuerpo de la fijada", ForumTopic.Anuncio);
        ana.Publicar("Reciente", "cuerpo de la reciente", ForumTopic.Idea);
        Svc(db, Quien(99, "Jefa", UserRole.Admin)).Fijar(vieja!.Id, true);

        Assert.Equal("Fijada", ana.Muro()[0].Post.Title);
    }

    [Fact]
    public void Muro_LosComentariosRetiradosNoCuentan()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, c1) = ana.Comentar(post!.Id, "uno");
        ana.Comentar(post.Id, "dos");
        ana.Retirar(c1!.Id);

        Assert.Equal(1, ana.Muro()[0].Comentarios);
    }

    // ── Hilo en orden de lectura ─────────────────────────────────────────────────

    [Fact]
    public void Hilo_SeLeeEnOrdenDeConversacion()
    {
        // Cada respuesta justo debajo de aquello a lo que contesta. Sin esto, un hilo es una lista
        // suelta de mensajes y se pierde el sentido de lo que se dijo.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Raíz", "cuerpo raíz", ForumTopic.Pregunta);
        var (_, _, a) = ana.Comentar(post!.Id, "A");
        var (_, _, b) = ana.Comentar(post.Id, "B");
        var (_, _, a1) = ana.Comentar(a!.Id, "A.1");
        ana.Comentar(a1!.Id, "A.1.1");
        ana.Comentar(b!.Id, "B.1");

        var hilo = ana.Hilo(post.Id);

        Assert.Equal(
            ["cuerpo raíz", "A", "A.1", "A.1.1", "B", "B.1"],
            hilo.Select(n => n.Post.Body).ToArray());
        Assert.Equal([0, 1, 2, 3, 1, 2], hilo.Select(n => n.Nivel).ToArray());
    }

    [Fact]
    public void Hilo_IncluyeLasEntradasRetiradas_ConSuHueco()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Raíz", "cuerpo raíz", ForumTopic.Idea);
        var (_, _, a) = ana.Comentar(post!.Id, "A");
        ana.Comentar(a!.Id, "respuesta a A");
        ana.Retirar(a.Id);

        var hilo = ana.Hilo(post.Id);

        Assert.Equal(3, hilo.Count);   // la retirada sigue ahí sosteniendo a su respuesta
        Assert.Contains(hilo, n => n.Post.Eliminado);
        Assert.Equal(2, hilo.Last().Nivel);
    }

    // ── Auditoría y búsqueda ─────────────────────────────────────────────────────

    [Fact]
    public void Auditoria_TraePublicacionesYComentarios()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        ana.Comentar(post!.Id, "un comentario");

        // La consulta la hace la jefa: la auditoría es del administrador, no de quien publica.
        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Equal(2, jefa.Auditoria().Count);
        Assert.Single(ana.Muro());   // el muro solo cuenta publicaciones
    }

    [Fact]
    public void Auditoria_EsSoloDelAdministrador()
    {
        // La vista consolidada del rastro (retiradas y ediciones de TODO el foro, con búsqueda)
        // es supervisión. Ni el desarrollador ni Operaciones — guarda positiva, no por descarte.
        var db = TestDb.New();
        Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.Throws<AuthorizationException>(() => Svc(db, Quien(1, "Ana")).Auditoria());
        Assert.Throws<AuthorizationException>(() => Svc(db, Quien(3, "Ops", UserRole.Operaciones)).Auditoria());
        Assert.Equal(1, Svc(db, Quien(99, "Jefa", UserRole.Admin)).Auditoria().Count);
    }

    [Fact]
    public void ElRastro_DentroDelHilo_SigueSiendoDeTodos()
    {
        // Lo que el desarrollador NO pierde: el hueco del retirado y el hilo completo. Eso es
        // contexto de la conversación, no el expediente consolidado.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);
        var (_, _, com) = ana.Comentar(post!.Id, "me arrepiento de esto");
        ana.Retirar(com!.Id);

        var hilo = Svc(db, Quien(2, "Beto")).Hilo(post.Id);
        Assert.Contains(hilo, n => n.Post.Eliminado);   // el hueco se ve
    }

    [Fact]
    public void Busqueda_MiraTituloCuerpoEtiquetasYAutor()
    {
        var db = TestDb.New();
        Svc(db, Quien(1, "Ana")).Publicar("Índices en SQL", "cómo bajamos el reporte a un segundo", ForumTopic.Aprendizaje, "sql, rendimiento");
        Svc(db, Quien(2, "Beto")).Publicar("Otra cosa", "nada que ver", ForumTopic.Otro);

        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Single(jefa.Auditoria(new ForumFiltro(Texto: "Índices")));
        Assert.Single(jefa.Auditoria(new ForumFiltro(Texto: "un segundo")));
        Assert.Single(jefa.Auditoria(new ForumFiltro(Texto: "rendimiento")));
        Assert.Single(jefa.Auditoria(new ForumFiltro(Texto: "Beto")));
    }

    [Fact]
    public void Busqueda_NoEncuentraElTextoDeLoRetirado()
    {
        // Si se pudiera encontrar por su contenido, retirarlo no serviría de nada.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema normal", "una barbaridad irrepetible", ForumTopic.Otro);
        ana.Retirar(post!.Id);

        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Empty(jefa.Auditoria(new ForumFiltro(Texto: "barbaridad")));
        Assert.Single(jefa.Auditoria(new ForumFiltro(Texto: "Tema normal")));   // el título sí, para poder auditarla
    }

    [Fact]
    public void Filtros_PorTemaYSoloMias()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        ana.Publicar("De Ana", "cuerpo de Ana", ForumTopic.Idea);
        Svc(db, Quien(2, "Beto")).Publicar("De Beto", "cuerpo de Beto", ForumTopic.Anuncio);

        Assert.Single(ana.Muro(new ForumFiltro(Tema: ForumTopic.Anuncio)));
        Assert.Single(ana.Muro(new ForumFiltro(SoloMios: true)));
        Assert.Equal("De Ana", ana.Muro(new ForumFiltro(SoloMios: true))[0].Post.Title);
    }

    [Fact]
    public void ElForoLoVeCualquieraConSesion()
    {
        // El punto del foro es compartir: si solo lo viera una parte del equipo, no sería un foro.
        var db = TestDb.New();
        Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea);

        Assert.Single(Svc(db, Quien(2, "Beto", UserRole.Desarrollador)).Muro());
        Assert.Single(Svc(db, Quien(3, "Ops", UserRole.Operaciones)).Muro());
        Assert.Single(Svc(db, Quien(99, "Jefa", UserRole.Admin)).Muro());
    }

    [Fact]
    public void SinSesion_NoSeVeNiSePublica()
    {
        var db = TestDb.New();
        var anonimo = Svc(db, Ctx.Anonymous());

        Assert.Throws<AuthorizationException>(() => anonimo.Muro());
        Assert.Throws<AuthorizationException>(() => anonimo.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea));
    }
}
