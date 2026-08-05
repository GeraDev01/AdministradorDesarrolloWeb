using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Borrado REAL de una publicación completa por un administrador.
///
/// Es la excepción a la regla del foro («nada se borra, se retira»), así que lo que hay que
/// asegurar es que la excepción esté acotada: que se lleve el hilo entero —comentarios a cualquier
/// profundidad, imágenes y «me gusta»— sin tocar los demás hilos, y que nadie más pueda hacerlo.
/// </summary>
public class ForumEliminarPublicacionTests
{
    private static ForumService Svc(AppDbContext db, CurrentUserContext cu) => new(db, cu, new AuditService(db, cu));

    private static CurrentUserContext Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador)
    {
        var cu = new CurrentUserContext();
        cu.SetUser(new User { Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre, Role = rol, IsActive = true });
        return cu;
    }

    /// <summary>Imagen insertada a mano: aquí interesa que se BORRE, no cómo se valida al subirla.</summary>
    private static void Adjuntar(AppDbContext db, int postId)
    {
        db.ForumAttachments.Add(new ForumAttachment
        {
            PostId = postId, FileName = "captura.png", ContentType = "image/png",
            Bytes = [1, 2, 3], Thumb = [1], SizeBytes = 3, Width = 10, Height = 10,
            UploadedByUserId = 1, CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    /// <summary>Un hilo con raíz, un comentario y una respuesta al comentario (dos niveles).</summary>
    private static (int raiz, int comentario, int respuesta) HiloConRespuestas(AppDbContext db)
    {
        var ana = Quien(1, "Ana");
        var (_, _, post) = Svc(db, ana).Publicar("Migrar el reporte", "Bajó de 40 s a 1.2 s.", ForumTopic.Aprendizaje);
        var (_, _, com) = Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "¿Qué índice usaste?");
        var (_, _, resp) = Svc(db, ana).Comentar(com!.Id, "Uno compuesto por fecha y cliente.");
        return (post.Id, com.Id, resp!.Id);
    }

    [Fact]
    public void Admin_EliminaLaPublicacionConTodoSuHilo()
    {
        var db = TestDb.New();
        var (raiz, comentario, respuesta) = HiloConRespuestas(db);
        Adjuntar(db, raiz);
        Adjuntar(db, comentario);
        Svc(db, Quien(3, "Caro")).MeGusta(raiz);

        var (ok, mensaje) = Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacion(raiz);

        Assert.True(ok);
        Assert.Contains("2 comentario", mensaje);
        Assert.Empty(db.ForumPosts.AsNoTracking().Where(p => p.Id == raiz || p.Id == comentario || p.Id == respuesta));
        Assert.Empty(db.ForumAttachments.AsNoTracking());
        Assert.Empty(db.ForumLikes.AsNoTracking());
    }

    [Fact]
    public void Eliminar_NoTocaLosDemasHilos()
    {
        var db = TestDb.New();
        var (raiz, _, _) = HiloConRespuestas(db);

        var (_, _, otra) = Svc(db, Quien(1, "Ana")).Publicar("Otro tema", "Cuerpo suficiente.", ForumTopic.Idea);
        Svc(db, Quien(2, "Beto")).Comentar(otra!.Id, "Coincido con esto.");
        Adjuntar(db, otra.Id);

        Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacion(raiz);

        Assert.Equal(2, db.ForumPosts.AsNoTracking().Count(p => p.RootId == otra.Id));
        Assert.Single(db.ForumAttachments.AsNoTracking());
    }

    /// <summary>Una publicación ya retirada es justo la que se puede querer borrar del todo.</summary>
    [Fact]
    public void Eliminar_FuncionaSobreUnaPublicacionYaRetirada()
    {
        var db = TestDb.New();
        var (raiz, _, _) = HiloConRespuestas(db);
        var admin = Svc(db, Quien(9, "Jefa", UserRole.Admin));
        admin.Retirar(raiz);

        var (ok, _) = admin.EliminarPublicacion(raiz);

        Assert.True(ok);
        Assert.Empty(db.ForumPosts.AsNoTracking());
    }

    [Theory]
    [InlineData(UserRole.Desarrollador)]
    [InlineData(UserRole.Operaciones)]
    public void Eliminar_SoloAdministrador_NiSiquieraElAutor(UserRole rol)
    {
        var db = TestDb.New();
        var (raiz, _, _) = HiloConRespuestas(db);

        // userId 1 es Ana, la AUTORA del hilo: retirar sí puede, borrar de verdad no.
        Assert.Throws<AuthorizationException>(() => Svc(db, Quien(1, "Ana", rol)).EliminarPublicacion(raiz));
        Assert.Equal(3, db.ForumPosts.AsNoTracking().Count());
    }

    [Fact]
    public void Eliminar_SeNiegaSobreUnComentario()
    {
        var db = TestDb.New();
        var (_, comentario, _) = HiloConRespuestas(db);

        var (ok, mensaje) = Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacion(comentario);

        Assert.False(ok);
        Assert.Contains("comentario", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, db.ForumPosts.AsNoTracking().Count());
    }

    [Fact]
    public void Eliminar_PublicacionInexistente_NoRevienta()
    {
        var db = TestDb.New();
        var (ok, mensaje) = Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacion(4242);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Desaparece el contenido, no el hecho: la bitácora conserva título y autor para poder
    /// responder después «¿qué se borró y quién lo borró?».
    /// </summary>
    [Fact]
    public void Eliminar_DejaConstanciaEnLaBitacora()
    {
        var db = TestDb.New();
        var (raiz, _, _) = HiloConRespuestas(db);

        Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacion(raiz);

        var registro = db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "ForumPost" && l.EntityId == raiz.ToString())
            .OrderByDescending(l => l.Id)
            .First();

        Assert.Contains("Migrar el reporte", registro.Details);
        Assert.Contains("Ana", registro.Details);
    }
}
