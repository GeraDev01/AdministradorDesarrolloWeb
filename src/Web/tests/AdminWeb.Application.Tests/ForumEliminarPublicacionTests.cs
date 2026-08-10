using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Borrado REAL de una publicación completa por un administrador.
///
/// Es la excepción a la regla del foro («nada se borra, se retira»), así que lo que hay que
/// asegurar es que la excepción esté acotada: que se lleve el hilo entero —comentarios a cualquier
/// profundidad, imágenes y «me gusta»— sin tocar los demás hilos, y que nadie más pueda hacerlo.
/// </summary>
public class ForumEliminarPublicacionTests
{
    private static ForumService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static ICurrentUser Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador) =>
        ForumServiceTests.Quien(userId, nombre, rol);

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
    private static async Task<(int raiz, int comentario, int respuesta)> HiloConRespuestas(AppDbContext db)
    {
        var ana = Quien(1, "Ana");
        var (_, _, post) = await Svc(db, ana).PublicarAsync("Migrar el reporte", "Bajó de 40 s a 1.2 s.", ForumTopic.Aprendizaje);
        var (_, _, com) = await Svc(db, Quien(2, "Beto")).ComentarAsync(post!.Id, "¿Qué índice usaste?");
        var (_, _, resp) = await Svc(db, ana).ComentarAsync(com!.Id, "Uno compuesto por fecha y cliente.");
        return (post.Id, com.Id, resp!.Id);
    }

    [Fact]
    public async Task Admin_EliminaLaPublicacionConTodoSuHilo()
    {
        var db = TestDb.New();
        var (raiz, comentario, respuesta) = await HiloConRespuestas(db);
        Adjuntar(db, raiz);
        Adjuntar(db, comentario);
        await Svc(db, Quien(3, "Caro")).MeGustaAsync(raiz);

        var (ok, mensaje) = await Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacionAsync(raiz);

        Assert.True(ok);
        Assert.Contains("2 comentario", mensaje);
        Assert.Empty(db.ForumPosts.AsNoTracking().Where(p => p.Id == raiz || p.Id == comentario || p.Id == respuesta));
        Assert.Empty(db.ForumAttachments.AsNoTracking());
        Assert.Empty(db.ForumLikes.AsNoTracking());
    }

    [Fact]
    public async Task Eliminar_NoTocaLosDemasHilos()
    {
        var db = TestDb.New();
        var (raiz, _, _) = await HiloConRespuestas(db);

        var (_, _, otra) = await Svc(db, Quien(1, "Ana")).PublicarAsync("Otro tema", "Cuerpo suficiente.", ForumTopic.Idea);
        await Svc(db, Quien(2, "Beto")).ComentarAsync(otra!.Id, "Coincido con esto.");
        Adjuntar(db, otra.Id);

        await Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacionAsync(raiz);

        Assert.Equal(2, db.ForumPosts.AsNoTracking().Count(p => p.RootId == otra.Id));
        Assert.Single(db.ForumAttachments.AsNoTracking());
    }

    /// <summary>Una publicación ya retirada es justo la que se puede querer borrar del todo.</summary>
    [Fact]
    public async Task Eliminar_FuncionaSobreUnaPublicacionYaRetirada()
    {
        var db = TestDb.New();
        var (raiz, _, _) = await HiloConRespuestas(db);
        var admin = Svc(db, Quien(9, "Jefa", UserRole.Admin));
        await admin.RetirarAsync(raiz);

        var (ok, _) = await admin.EliminarPublicacionAsync(raiz);

        Assert.True(ok);
        Assert.Empty(db.ForumPosts.AsNoTracking());
    }

    [Theory]
    [InlineData(UserRole.Desarrollador)]
    [InlineData(UserRole.Operaciones)]
    public async Task Eliminar_SoloAdministrador_NiSiquieraElAutor(UserRole rol)
    {
        var db = TestDb.New();
        var (raiz, _, _) = await HiloConRespuestas(db);

        // userId 1 es Ana, la AUTORA del hilo: retirar sí puede, borrar de verdad no.
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Quien(1, "Ana", rol)).EliminarPublicacionAsync(raiz));
        Assert.Equal(3, db.ForumPosts.AsNoTracking().Count());
    }

    [Fact]
    public async Task Eliminar_SeNiegaSobreUnComentario()
    {
        var db = TestDb.New();
        var (_, comentario, _) = await HiloConRespuestas(db);

        var (ok, mensaje) = await Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacionAsync(comentario);

        Assert.False(ok);
        Assert.Contains("comentario", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, db.ForumPosts.AsNoTracking().Count());
    }

    [Fact]
    public async Task Eliminar_PublicacionInexistente_NoRevienta()
    {
        var db = TestDb.New();
        var (ok, mensaje) = await Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacionAsync(4242);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Desaparece el contenido, no el hecho: la bitácora conserva título y autor para poder
    /// responder después «¿qué se borró y quién lo borró?».
    /// </summary>
    [Fact]
    public async Task Eliminar_DejaConstanciaEnLaBitacora()
    {
        var db = TestDb.New();
        var (raiz, _, _) = await HiloConRespuestas(db);

        await Svc(db, Quien(9, "Jefa", UserRole.Admin)).EliminarPublicacionAsync(raiz);

        var registro = db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "ForumPost" && l.EntityId == raiz.ToString())
            .OrderByDescending(l => l.Id)
            .First();

        Assert.Contains("Migrar el reporte", registro.Details);
        Assert.Contains("Ana", registro.Details);
    }
}
