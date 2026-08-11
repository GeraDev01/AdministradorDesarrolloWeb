using System.Net.Http.Json;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Busqueda;
using AdminWeb.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// La documentación dentro de la búsqueda global, con la API levantada.
///
/// <para><b>Por qué esto no se puede comprobar solo en el servicio.</b> El servicio devuelve una
/// clave de navegación; quien la convierte en una dirección de la web es el borde de la API, y ahí
/// está el error que de verdad se cometería: una clave que nadie tradujo no falla, se va al inicio
/// en silencio. Un enlace que lleva a la portada es peor que uno roto, porque parece que la
/// aplicación funciona y quien buscaba un tema concreto se queda pensando que no está escrito.</para>
/// </summary>
public class BusquedaDeConocimientoTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    /// <summary>
    /// Una palabra que no aparece en ninguna otra siembra de estas pruebas. La base de la API es
    /// compartida por toda la clase, así que un término común haría que una prueba encontrara lo que
    /// sembró otra y el fallo aparecería un día sí y otro no.
    /// </summary>
    private const string Termino = "Zarandaja";

    [Fact]
    public async Task ElBuscadorGlobal_DevuelveLoPublicado_ConLaDireccionDelArticulo()
    {
        int id = await SembrarAsync($"{Termino} publicada", KnowledgeStatus.Publicado);
        var cliente = await api.ClienteAdminAsync();

        var resultados = await cliente.GetFromJsonAsync<List<ResultadoDeBusquedaDto>>(
            $"/api/busqueda?q={Termino}");

        var hit = Assert.Single(resultados!.Where(r => r.Tipo == "Artículo"));
        Assert.Equal($"{Termino} publicada", hit.Texto);

        // Lo que esta prueba existe para atrapar: la dirección lleva al artículo, no a la portada.
        Assert.Equal($"/conocimiento/{id}", hit.Ruta);
    }

    [Fact]
    public async Task ElBuscadorGlobal_NoEsLaRendijaPorLaQueSeLeeUnBorradorAjeno()
    {
        // El borrador es de su autor y de nadie más —ni del líder—, y aquí pregunta el líder. Si
        // apareciera, la privacidad de lo que alguien está escribiendo dependería de que nadie
        // tecleara la palabra correcta en la caja de arriba.
        await SembrarAsync($"{Termino} a medias", KnowledgeStatus.Borrador);
        await SembrarAsync($"{Termino} en la cola", KnowledgeStatus.PorRevisar);
        await SembrarAsync($"{Termino} devuelta", KnowledgeStatus.Rechazado);

        var cliente = await api.ClienteAdminAsync();

        var resultados = await cliente.GetFromJsonAsync<List<ResultadoDeBusquedaDto>>(
            $"/api/busqueda?q={Termino}");

        Assert.DoesNotContain(resultados!, r => r.Texto.Contains("a medias"));
        Assert.DoesNotContain(resultados!, r => r.Texto.Contains("en la cola"));
        Assert.DoesNotContain(resultados!, r => r.Texto.Contains("devuelta"));
    }

    private async Task<int> SembrarAsync(string titulo, KnowledgeStatus estado)
    {
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();

        var articulo = new KnowledgeArticle
        {
            Title = titulo,
            Body = "Texto de la prueba.",
            Status = estado,
            AuthorUserId = 4242,          // una cuenta que no es la de quien busca
            AuthorName = "Otra persona",
            CreatedAtUtc = DateTime.UtcNow,
            PublishedAtUtc = estado == KnowledgeStatus.Publicado ? DateTime.UtcNow : null
        };

        db.KnowledgeArticles.Add(articulo);
        await db.SaveChangesAsync();
        return articulo.Id;
    }
}
