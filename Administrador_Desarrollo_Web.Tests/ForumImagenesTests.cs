using System.Drawing;
using System.Drawing.Imaging;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Imágenes incrustadas en el foro.
///
/// Dos cosas se prueban por encima del resto: que solo entre lo que de verdad ES una imagen —esos
/// bytes acaban en un archivo temporal que se abre con el programa asociado— y que retirar una
/// entrada se lleve por delante también sus imágenes. Si una captura se siguiera viendo después de
/// retirar la entrada, retirar no querría decir nada.
/// </summary>
public class ForumImagenesTests
{
    private static ForumService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static CurrentUserContext Quien(int userId, string nombre, UserRole rol = UserRole.Desarrollador)
    {
        var cu = new CurrentUserContext();
        cu.SetUser(new User { Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre, Role = rol, IsActive = true });
        return cu;
    }

    /// <summary>Un PNG de verdad, para que pase el reconocimiento por bytes.</summary>
    private static byte[] Png(int lado = 8)
    {
        using var bmp = new Bitmap(lado, lado);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static ForumImagenNueva Imagen(string nombre = "captura.png") =>
        new(nombre, Png(), Png(4), 8, 8);

    // ── Adjuntar ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Publicar_ConImagen_LaGuardaColgandoDeSuEntrada()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));

        var (ok, _, post) = ana.Publicar("El error del reporte", "Se ve así:", ForumTopic.Pregunta, null, [Imagen()]);

        Assert.True(ok);
        var guardada = db.ForumAttachments.AsNoTracking().Single();
        Assert.Equal(post!.Id, guardada.PostId);
        Assert.Equal("image/png", guardada.ContentType);   // decidido por los bytes, no por el nombre
        Assert.True(guardada.Bytes.Length > 0);
        Assert.True(guardada.Thumb.Length > 0);
    }

    [Fact]
    public void UnaCapturaSola_YaEsUnaPublicacion()
    {
        // Con título y una imagen ya se dice algo; exigir además cinco caracteres de texto sobraría.
        var db = TestDb.New();
        var (ok, _, _) = Svc(db, Quien(1, "Ana")).Publicar("Así se ve el error", "", ForumTopic.Pregunta, null, [Imagen()]);
        Assert.True(ok);
    }

    [Fact]
    public void SinTextoNiImagen_NoHayPublicacion()
    {
        var db = TestDb.New();
        Assert.False(Svc(db, Quien(1, "Ana")).Publicar("Un título válido", "", ForumTopic.Idea).ok);
    }

    [Fact]
    public void UnaCapturaSola_TambienEsUnComentario()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Pregunta);

        Assert.True(Svc(db, Quien(2, "Beto")).Comentar(post!.Id, "", [Imagen()]).ok);
        Assert.False(Svc(db, Quien(2, "Beto")).Comentar(post.Id, "").ok);   // ni texto ni imagen
    }

    [Fact]
    public void LoQueNoEsUnaImagen_NoEntra()
    {
        // El nombre dice .png pero los bytes son otra cosa. Esto acaba volcado a un temporal que se
        // abre con el programa asociado: colar aquí un ejecutable sería ejecutarlo al «verlo».
        var db = TestDb.New();
        var disfrazado = new ForumImagenNueva("captura.png", "MZ\0\0\0\0\0\0\0"u8.ToArray(), [], 0, 0);

        var (ok, mensaje, _) = Svc(db, Quien(1, "Ana"))
            .Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [disfrazado]);

        Assert.False(ok);
        Assert.Contains("no es una imagen", mensaje);
        Assert.Empty(db.ForumPosts);        // no queda la publicación a medias
        Assert.Empty(db.ForumAttachments);
    }

    [Fact]
    public void UnaImagenQueNoCabe_SeRechazaAntesDeGuardarla()
    {
        var db = TestDb.New();
        var enorme = new byte[ForumService.MaxBytesImagen + 1];
        Png().CopyTo(enorme, 0);   // cabecera PNG de verdad: lo que falla es el tamaño, no el tipo

        var (ok, mensaje, _) = Svc(db, Quien(1, "Ana"))
            .Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [new ForumImagenNueva("foto.png", enorme, [], 9000, 9000)]);

        Assert.False(ok);
        Assert.Contains("tope", mensaje);
        Assert.Empty(db.ForumAttachments);
    }

    [Fact]
    public void MasDeLoQuePermiteElTope_NoEntra()
    {
        var db = TestDb.New();
        var demasiadas = Enumerable.Range(0, ForumService.MaxImagenes + 1).Select(i => Imagen($"c{i}.png")).ToList();

        var (ok, mensaje, _) = Svc(db, Quien(1, "Ana"))
            .Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, demasiadas);

        Assert.False(ok);
        Assert.Contains($"{ForumService.MaxImagenes}", mensaje);
    }

    // ── Leer ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElHilo_TraeLaMiniatura_NoElOriginal()
    {
        // Un hilo se repinta en cada 👍 y en cada comentario. Si arrastrara los originales, abrir
        // una conversación con capturas costaría megas cada vez.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen()]);

        var imagenes = Svc(db, Quien(2, "Beto")).ImagenesDeEntrada(post!.Id);

        var img = Assert.Single(imagenes);
        Assert.True(img.Miniatura.Length > 0);
        Assert.Equal("captura.png", img.NombreArchivo);
        Assert.True(img.Bytes > 0);   // el peso del original se informa; los bytes no viajan
    }

    [Fact]
    public void ElOriginal_SoloSeTrae_CuandoSeAbre()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen()]);
        var img = ana.ImagenesDeEntrada(post!.Id).Single();

        var (bytes, nombre, tipo) = Svc(db, Quien(2, "Beto")).BytesDeImagen(img.Id);

        Assert.True(bytes.Length > 0);
        Assert.Equal("captura.png", nombre);
        Assert.Equal("image/png", tipo);
    }

    [Fact]
    public void ElMuro_DiceCuantasImagenesHay()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        ana.Publicar("Con capturas", "cuerpo", ForumTopic.Idea, null, [Imagen("a.png"), Imagen("b.png")]);

        Assert.Equal(2, ana.Muro()[0].Imagenes);
    }

    [Fact]
    public void SinSesion_NoSeVenLasImagenes()
    {
        var db = TestDb.New();
        var (_, _, post) = Svc(db, Quien(1, "Ana")).Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen()]);
        var anonimo = Svc(db, Ctx.Anonymous());

        Assert.Throws<AuthorizationException>(() => anonimo.ImagenesDeEntrada(post!.Id));
        Assert.Throws<AuthorizationException>(() => anonimo.BytesDeImagen(1));
    }

    // ── Retirar ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AlRetirarUnaEntrada_SusImagenesDejanDeVerse()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "algo de lo que se arrepintió", ForumTopic.Otro, null, [Imagen()]);
        var img = ana.ImagenesDeEntrada(post!.Id).Single();

        ana.Retirar(post.Id);

        Assert.Empty(ana.ImagenesDeEntrada(post.Id));
        Assert.Empty(ana.BytesDeImagen(img.Id).bytes);   // tampoco por el camino de abrirla
        Assert.Equal(0, ana.Muro()[0].Imagenes);         // ni se anuncia que las hubo
    }

    [Fact]
    public void NiSiquieraElAdministrador_VeLasImagenesDeLoRetirado()
    {
        // Misma regla que el texto: TextoVisible tapa el cuerpo para todo el mundo, incluida la
        // auditoría. Las imágenes no pueden ser la puerta de atrás a lo mismo.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Otro, null, [Imagen()]);
        var img = ana.ImagenesDeEntrada(post!.Id).Single();
        ana.Retirar(post.Id);

        var jefa = Svc(db, Quien(99, "Jefa", UserRole.Admin));
        Assert.Empty(jefa.ImagenesDeEntrada(post.Id));
        Assert.Empty(jefa.BytesDeImagen(img.Id).bytes);
    }

    [Fact]
    public void LaFilaDeLaImagen_SeConserva_AunqueNoSeSirva()
    {
        // Nada se borra de verdad en el foro: la entrada retirada conserva su cuerpo en la base y
        // sus imágenes también. Lo que cambia es que dejan de salir.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Otro, null, [Imagen()]);

        ana.Retirar(post!.Id);

        Assert.Single(db.ForumAttachments.AsNoTracking().ToList());
    }

    // ── Editar ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Editar_PuedeQuitarUnaImagenYAnadirOtra()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen("vieja.png")]);
        var vieja = ana.ImagenesDeEntrada(post!.Id).Single();

        var (ok, _) = ana.Editar(post.Id, "Tema", "cuerpo corregido", [Imagen("nueva.png")], [vieja.Id]);

        Assert.True(ok);
        var quedan = ana.ImagenesDeEntrada(post.Id);
        Assert.Equal("nueva.png", Assert.Single(quedan).NombreArchivo);
    }

    [Fact]
    public void Editar_NoPuedeQuitarLasImagenesDeOtraEntrada()
    {
        // El id de una imagen viaja desde el formulario; si no se acotara a la entrada que se está
        // editando, bastaría con mandar otro para borrar la captura de un tercero.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var beto = Svc(db, Quien(2, "Beto"));
        var (_, _, deAna) = ana.Publicar("De Ana", "cuerpo de Ana", ForumTopic.Idea, null, [Imagen("de-ana.png")]);
        var (_, _, deBeto) = beto.Publicar("De Beto", "cuerpo de Beto", ForumTopic.Idea, null, [Imagen("de-beto.png")]);
        var ajena = beto.ImagenesDeEntrada(deBeto!.Id).Single();

        ana.Editar(deAna!.Id, "De Ana", "cuerpo corregido", null, [ajena.Id]);

        Assert.Single(beto.ImagenesDeEntrada(deBeto.Id));   // la de Beto sigue donde estaba
    }

    [Fact]
    public void Editar_PuedeDejarElTextoVacio_SiQuedanImagenes()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen()]);

        Assert.True(ana.Editar(post!.Id, "Tema", "").ok);
    }

    [Fact]
    public void Editar_NoPuedeDejarloTodoVacio()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen()]);
        var img = ana.ImagenesDeEntrada(post!.Id).Single();

        Assert.False(ana.Editar(post.Id, "Tema", "", null, [img.Id]).ok);
        Assert.Single(db.ForumAttachments.AsNoTracking().ToList());   // y no se llevó la imagen por delante
    }

    [Fact]
    public void Editar_NoPasaDelTope_ContandoLasQueYaEstaban()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen("a.png")]);

        var muchas = Enumerable.Range(0, ForumService.MaxImagenes).Select(i => Imagen($"n{i}.png")).ToList();

        Assert.False(ana.Editar(post!.Id, "Tema", "cuerpo del tema", muchas).ok);
        Assert.Single(db.ForumAttachments.AsNoTracking().ToList());
    }

    [Fact]
    public void LasImagenesSeVenEnElOrdenEnQueSePusieron()
    {
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null, [Imagen("1.png"), Imagen("2.png")]);
        ana.Editar(post!.Id, "Tema", "cuerpo del tema", [Imagen("3.png")]);

        Assert.Equal(["1.png", "2.png", "3.png"],
            ana.ImagenesDeEntrada(post.Id).Select(i => i.NombreArchivo).ToArray());
    }

    // ── Reconocimiento por bytes ─────────────────────────────────────────────────

    [Fact]
    public void ElTipo_SaleDeLosBytes()
    {
        Assert.Equal("image/png", ForumMedia.TipoDeImagen(Png()));
        // Cabecera mínima de un GIF de verdad: firma GIF89a + descriptor de pantalla.
        Assert.Equal("image/gif", ForumMedia.TipoDeImagen([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0]));
        Assert.Equal("image/jpeg", ForumMedia.TipoDeImagen([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0 })]   // un .exe
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0, 0, 0 })] // un .zip
    [InlineData(new byte[] { 1, 2, 3 })]                                        // basura corta
    public void LoQueNoEsImagen_NoTieneTipo(byte[] bytes)
    {
        Assert.Null(ForumMedia.TipoDeImagen(bytes));
    }

    [Fact]
    public void ElNombreDeArchivo_SeLimpia_PorqueAcabaEnElDisco()
    {
        // La imagen se vuelca a un temporal para abrirla: un nombre con ruta dentro escribiría fuera
        // de la carpeta que le toca.
        var db = TestDb.New();
        var ana = Svc(db, Quien(1, "Ana"));
        var (_, _, post) = ana.Publicar("Tema", "cuerpo del tema", ForumTopic.Idea, null,
            [new ForumImagenNueva(@"..\..\Windows\System32\algo.png", Png(), Png(4), 8, 8)]);

        var guardada = ana.ImagenesDeEntrada(post!.Id).Single();
        Assert.Equal("algo.png", guardada.NombreArchivo);
    }
}
