using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La base de conocimiento dentro de la búsqueda global: que se encuentre lo publicado —también por
/// una palabra de dentro del texto— y que NO se encuentre nada más.
///
/// <para>Esa segunda mitad es la que de verdad importa. El buscador es la puerta lateral por la que
/// se cuelan los datos de las pantallas que sí filtran: un borrador es privado de quien lo escribe
/// —ni el líder lo lee— y lo devuelto es cosa del autor y del revisor, así que si aparecieran aquí,
/// la privacidad del borrador dependería de que nadie tecleara la palabra correcta en la caja de
/// arriba.</para>
/// </summary>
public class BuscadorDeConocimientoTests
{
    private static KnowledgeArticle Articulo(
        string titulo, KnowledgeStatus estado, string cuerpo = "texto de relleno", string? etiquetas = null,
        int autor = 7) => new()
    {
        Title = titulo,
        Body = cuerpo,
        Tags = etiquetas,
        Status = estado,
        AuthorUserId = autor,
        AuthorName = "Ana López",
        CreatedAtUtc = DateTime.UtcNow,
        PublishedAtUtc = estado == KnowledgeStatus.Publicado ? DateTime.UtcNow : null
    };

    [Fact]
    public async Task Buscar_encuentraUnArticuloPublicado_yDevuelveSuDireccionPropia()
    {
        var db = TestDb.New();
        var a = Articulo("Cómo se despliega el portal", KnowledgeStatus.Publicado);
        db.KnowledgeArticles.Add(a);
        db.SaveChanges();

        var hit = Assert.Single(await new SearchService(db).BuscarAsync("despliega"));

        Assert.Equal(SearchKind.Articulo, hit.Kind);
        Assert.Equal("Cómo se despliega el portal", hit.Texto);

        // La clave lleva el identificador: buscar un tema y aterrizar en la lista completa de la
        // documentación no sería encontrarlo, sería volver a empezar.
        Assert.Equal($"conocimiento/{a.Id}", hit.NavKey);
    }

    [Fact]
    public async Task Buscar_encuentraPorUnaPalabraDeDentroDelTexto()
    {
        // Es la diferencia con las demás categorías: a un requerimiento se llega por su título o su
        // número, pero a un artículo se llega por algo que alguien recuerda haber leído dentro.
        var db = TestDb.New();
        db.KnowledgeArticles.Add(Articulo("Guía de arranque", KnowledgeStatus.Publicado,
            cuerpo: "Si el servicio no levanta, revisa el certificado caducado."));
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("certificado");

        Assert.Contains(hits, h => h.Kind == SearchKind.Articulo && h.Texto == "Guía de arranque");
    }

    [Fact]
    public async Task Buscar_encuentraPorEtiqueta_yLaEnseñaEnElDetalle()
    {
        // Las etiquetas son el índice de la base —y el glosario—, así que tienen que buscar; y van en
        // el detalle porque son lo que distingue dos artículos que se llaman parecido.
        var db = TestDb.New();
        db.KnowledgeArticles.Add(Articulo("Respaldos", KnowledgeStatus.Publicado, etiquetas: "sql, respaldos"));
        db.SaveChanges();

        var hit = Assert.Single(await new SearchService(db).BuscarAsync("respaldos"));

        Assert.Contains("sql, respaldos", hit.Detalle);
    }

    [Fact]
    public async Task Buscar_noDevuelveNadaQueNoEstePublicado()
    {
        // El único caso que no se puede fallar: los tres estados que no son «publicado» tienen dueño,
        // y el buscador global no distingue quién pregunta.
        var db = TestDb.New();
        db.KnowledgeArticles.AddRange(
            Articulo("Bloqueos: borrador", KnowledgeStatus.Borrador),
            Articulo("Bloqueos: en la cola", KnowledgeStatus.PorRevisar),
            Articulo("Bloqueos: devuelto", KnowledgeStatus.Rechazado),
            Articulo("Bloqueos: publicado", KnowledgeStatus.Publicado));
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("Bloqueos");

        var unico = Assert.Single(hits.Where(h => h.Kind == SearchKind.Articulo));
        Assert.Equal("Bloqueos: publicado", unico.Texto);
    }

    [Fact]
    public async Task Buscar_lasMayusculasNoDecidenSiAlgoAparece()
    {
        var db = TestDb.New();
        db.KnowledgeArticles.Add(Articulo("Índice de scripts SQL", KnowledgeStatus.Publicado));
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("sql");

        Assert.Contains(hits, h => h.Kind == SearchKind.Articulo);
    }

    [Fact]
    public async Task Buscar_losArticulosTienenSuPropioTope_yNoSeComenALasDemasCategorias()
    {
        // El tope es POR TIPO justamente para esto: veinte artículos que hablan de despliegues no
        // pueden esconder el requerimiento que se llama igual.
        var db = TestDb.New();
        for (int i = 1; i <= SearchService.MaxPorTipo + 5; i++)
            db.KnowledgeArticles.Add(Articulo($"Despliegue #{i}", KnowledgeStatus.Publicado));
        db.Requirements.Add(new Requirement
        {
            Title = "Despliegue automático",
            Source = RequirementSource.Manual,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var hits = await new SearchService(db).BuscarAsync("Despliegue");

        Assert.Equal(SearchService.MaxPorTipo, hits.Count(h => h.Kind == SearchKind.Articulo));
        Assert.Contains(hits, h => h.Kind == SearchKind.Requerimiento);
    }
}
