using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El aplanado del hilo y sus casos feos.
///
/// Un hilo mal aplanado convierte una conversación en una lista de mensajes sueltos, y en un foro
/// que sirve para auditar eso es perder el sentido de lo que se dijo. Peor: un dato inconsistente
/// —un comentario huérfano, un ciclo— podría hacer que un mensaje **desapareciera de la vista** sin
/// que nada lo delate, o colgar la aplicación. De eso van casi todas estas pruebas.
/// </summary>
public class ForumFilterTests
{
    private static readonly DateTime Base = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    private static ForumPost P(int id, int? parentId, string cuerpo, int minutos, int autor = 1) => new()
    {
        Id = id, ParentId = parentId, RootId = 1, Body = cuerpo,
        AuthorUserId = autor, AuthorName = autor == 1 ? "Ana" : "Beto",
        CreatedAtUtc = Base.AddMinutes(minutos)
    };

    // ── Aplanado ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Aplanar_PoneCadaRespuestaDebajoDeLoQueContesta()
    {
        var entradas = new List<ForumPost>
        {
            P(1, null, "raíz", 0),
            P(2, 1, "A", 1),
            P(3, 1, "B", 2),
            P(4, 2, "A.1", 3),
            P(5, 4, "A.1.1", 4),
            P(6, 3, "B.1", 5),
        };

        var plano = ForumFilter.Aplanar(entradas);

        Assert.Equal(["raíz", "A", "A.1", "A.1.1", "B", "B.1"], plano.Select(x => x.Post.Body).ToArray());
        Assert.Equal([0, 1, 2, 3, 1, 2], plano.Select(x => x.Nivel).ToArray());
    }

    [Fact]
    public void Aplanar_LosHermanosVanPorFecha()
    {
        // Llegan desordenadas de la base a propósito.
        var entradas = new List<ForumPost>
        {
            P(3, 1, "tercero", 30),
            P(1, null, "raíz", 0),
            P(2, 1, "segundo", 10),
        };

        var plano = ForumFilter.Aplanar(entradas);

        Assert.Equal(["raíz", "segundo", "tercero"], plano.Select(x => x.Post.Body).ToArray());
    }

    [Fact]
    public void Aplanar_ElNivelSeRecalcula_NoSeConfiaEnElGuardado()
    {
        // Si una entrada intermedia desapareciera, sus respuestas quedarían sangradas contra un
        // padre inexistente. El nivel sale del árbol que hay, no del número guardado.
        var raiz = P(1, null, "raíz", 0);
        var hijo = P(2, 1, "hijo", 1);
        hijo.Depth = 4;   // valor guardado incoherente

        var plano = ForumFilter.Aplanar([raiz, hijo]);

        Assert.Equal(1, plano[1].Nivel);
    }

    [Fact]
    public void Aplanar_UnComentarioHuerfano_NoSePierde()
    {
        // Su padre no vino en el lote. Antes que esconderlo, se muestra al nivel de arriba: un
        // mensaje que desaparece de la vista sin avisar es lo peor que puede pasar en un foro.
        var entradas = new List<ForumPost>
        {
            P(1, null, "raíz", 0),
            P(9, 777, "huérfano", 5),   // el 777 no existe
        };

        var plano = ForumFilter.Aplanar(entradas);

        Assert.Equal(2, plano.Count);
        Assert.Contains(plano, x => x.Post.Body == "huérfano");
    }

    [Fact]
    public void Aplanar_UnCicloNoCuelgaLaAplicacion()
    {
        // Dato corrupto: dos entradas que se apuntan entre sí. Sin corte, el recorrido no termina.
        var a = P(1, 2, "a", 0);
        var b = P(2, 1, "b", 1);

        var plano = ForumFilter.Aplanar([a, b]);

        Assert.Equal(2, plano.Count);
        Assert.Equal(2, plano.Select(x => x.Post.Id).Distinct().Count());
    }

    [Fact]
    public void Aplanar_NoPierdeNiRepiteNada()
    {
        var entradas = Enumerable.Range(1, 20)
            .Select(i => P(i, i == 1 ? null : i / 2, $"m{i}", i))
            .ToList();

        var plano = ForumFilter.Aplanar(entradas);

        Assert.Equal(20, plano.Count);
        Assert.Equal(20, plano.Select(x => x.Post.Id).Distinct().Count());
    }

    [Fact]
    public void Aplanar_ListaVacia_DevuelveVacio()
    {
        Assert.Empty(ForumFilter.Aplanar([]));
    }

    // ── Filtros ──────────────────────────────────────────────────────────────────

    private static List<ForumPost> Lote()
    {
        var a = P(1, null, "cómo bajamos el reporte", 0);
        a.Title = "Índices en SQL"; a.Tags = "sql, rendimiento"; a.Topic = ForumTopic.Aprendizaje;
        var b = P(2, null, "nada que ver", 10, autor: 2);
        b.Title = "Otra cosa"; b.Topic = ForumTopic.Anuncio;
        var c = P(3, 1, "buen apunte", 20, autor: 2);
        return [a, b, c];
    }

    [Fact]
    public void Filtro_TextoEnBlanco_NoDejaLaListaEnCero()
    {
        foreach (var q in new[] { "", "   ", null })
            Assert.Equal(3, ForumFilter.Aplicar(Lote(), new ForumFiltro(Texto: q), 1, Base).Count);
    }

    [Fact]
    public void Filtro_PorTemaAutorYSoloMios()
    {
        var todas = Lote();
        Assert.Single(ForumFilter.Aplicar(todas, new ForumFiltro(Tema: ForumTopic.Anuncio), 1, Base));
        Assert.Equal(2, ForumFilter.Aplicar(todas, new ForumFiltro(Autor: "Beto"), 1, Base).Count);
        Assert.Single(ForumFilter.Aplicar(todas, new ForumFiltro(SoloMios: true), 1, Base));
    }

    [Fact]
    public void Filtro_VentanaDeDias()
    {
        var vieja = P(9, null, "de hace mucho", 0);
        vieja.CreatedAtUtc = Base.AddDays(-100);

        var r = ForumFilter.Aplicar([vieja, P(1, null, "reciente", 0)], new ForumFiltro(UltimosDias: 30), 1, Base);

        Assert.Single(r);
        Assert.Equal("reciente", r[0].Body);
    }

    [Fact]
    public void Filtro_ElAutorEmpataPorNombreCompleto()
    {
        var ana   = P(1, null, "x", 0);
        var anabel = P(2, null, "y", 1);
        anabel.AuthorName = "Anabel";

        var r = ForumFilter.Aplicar([ana, anabel], new ForumFiltro(Autor: "Ana"), 1, Base);

        Assert.Single(r);
        Assert.Equal("Ana", r[0].AuthorName);
    }

    // ── Cómo se lee la fecha ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "ahora mismo")]
    [InlineData(-5, "hace 5 min")]
    [InlineData(-120, "hace 2 h")]
    public void HaceCuanto_LoCercanoSeLeeEnRelativo(int minutos, string esperado)
    {
        Assert.Equal(esperado, ForumFilter.HaceCuanto(Base.AddMinutes(minutos), Base));
    }

    [Fact]
    public void HaceCuanto_LoViejoSeLeeConFecha()
    {
        Assert.Equal("ayer", ForumFilter.HaceCuanto(Base.AddDays(-1), Base));
        Assert.Equal("hace 3 días", ForumFilter.HaceCuanto(Base.AddDays(-3), Base));
        Assert.Contains("/", ForumFilter.HaceCuanto(Base.AddDays(-40), Base));
    }

    [Fact]
    public void HaceCuanto_UnRelojAdelantado_NoDiceDisparates()
    {
        // Si un equipo tiene la hora adelantada, «hace -3 min» quedaría feísimo en pantalla.
        var texto = ForumFilter.HaceCuanto(Base.AddMinutes(30), Base);
        Assert.DoesNotContain("-", texto);
    }

    [Fact]
    public void Autores_SinRepetirYOrdenados()
    {
        Assert.Equal(["Ana", "Beto"], ForumFilter.Autores(Lote()));
    }
}
