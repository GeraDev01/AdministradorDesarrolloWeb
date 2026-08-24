using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL CRITERIO EXTRA QUE OBLIGA A CITAR UN ARTÍCULO: «Aplicaste una práctica documentada en la base
/// de conocimiento».
///
/// <para><b>Qué cierra.</b> La base de conocimiento pagaba por ESCRIBIR y nada más. Un artículo que
/// nadie aplica no vale nada, y hasta ahora no había forma de saber cuáles se aplican: la única señal
/// de utilidad era la opinión de su autor. Este criterio paga por APLICAR, y de paso —con un
/// <c>GROUP BY</c> sobre las filas cumplidas— contesta por primera vez qué prácticas se usan de
/// verdad y cuáles llevan un año publicadas sin que nadie las abra.</para>
///
/// <para><b>Por qué hace falta que se justifique.</b> Los otros extras se verifican mirando la
/// entrega: «agregaste pruebas» está en el pull request. Éste no se puede adivinar —el líder no sabe
/// QUÉ práctica se aplicó ni DÓNDE—, así que sin decirlo, evaluarlo sería un acto de fe, y un
/// criterio que se da por bueno sin mirar es un aumento de puntos disfrazado. Por eso se pide ANTES
/// de entregar y el servidor no deja entregar sin ello.</para>
///
/// <para><b>La pregunta incómoda, contestada.</b> Sí se admite un artículo PROPIO, y no es doble
/// pago: escribirlo se cobró una vez y para siempre, aplicarlo se cobra cada vez, que es lo que se
/// quiere premiar. Lo que sí hace la aplicación es DECIRLO en el panel del líder, y eso es lo que
/// separa una política que funciona de una que nadie aplica.</para>
/// </summary>
public class PracticaDocumentadaTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
                                       new SettingsService(ctx, cu, bitacora));
    }

    private PoolQueryService Consultas(AppDbContext db, ICurrentUser cu) =>
        new(OtroContexto(db), cu, Svc(db, cu));

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static async Task<AppDbContext> BaseConPoolAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        await ScoringCriteriaSeed.SembrarAsync(db);
        return db;
    }

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    /// <summary>Un artículo de la base de conocimiento, con el estado y el autor que se pidan.</summary>
    private static int NuevoArticulo(AppDbContext db, string titulo,
        KnowledgeStatus estado = KnowledgeStatus.Publicado, int? autorDevId = null)
    {
        var a = new KnowledgeArticle
        {
            Title = titulo, Body = "…", Status = estado,
            AuthorUserId = 1, AuthorDeveloperId = autorDevId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.KnowledgeArticles.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    /// <summary>
    /// Una actividad publicada CON el criterio de la práctica documentada como extra, ya tomada por
    /// quien se diga. Es el montaje entero: publicar, tomar y devolver los identificadores.
    /// </summary>
    private async Task<(int actividadId, int extraId)> ConElExtraTomadaAsync(AppDbContext db, int devId)
    {
        int criterioId = db.ScoringCriteria.AsNoTracking()
            .Single(c => c.Name == PoolSeed.CriterioDePracticaDocumentada).Id;

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(new PoolActivity
        {
            Title = "Migrar el importador", WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media, HorasEstimadas = 6m
        }, [criterioId]);
        Assert.True(ok, mensaje);

        var (tomada, porQue) = await Svc(db, Dev(devId)).TomarAsync(actividad!.Id, devId);
        Assert.True(tomada, porQue);

        int extraId = db.PoolActivityExtraCriteria.AsNoTracking()
            .Single(c => c.PoolActivityId == actividad.Id).Id;

        return (actividad.Id, extraId);
    }

    /// <summary>Marca todo el checklist, con evidencia donde hace falta, para poder entregar.</summary>
    private async Task CompletarChecklistAsync(AppDbContext db, int actividadId, int devId)
    {
        var svc = Svc(db, Dev(devId));
        foreach (var item in await svc.ChecklistDeAsync(actividadId))
        {
            var (ok, mensaje) = await svc.MarcarItemAsync(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(ok, mensaje);
        }
    }

    // ── 1. El criterio existe y se ofrece ────────────────────────────────────────

    /// <summary>
    /// Está en el catálogo, con puntos positivos, y se OFRECE como extra del pool.
    ///
    /// <para>Las dos mitades importan y son dos listas distintas: el catálogo lo tiene todo —incluidos
    /// los que no tienen sentido pedir de más— y <c>CriteriosExtraOfrecidos</c> es la lista corta de lo
    /// que el pool sí ofrece. Un criterio sembrado pero no ofrecido existiría sin ninguna puerta.</para>
    /// </summary>
    [Fact]
    public async Task ElCriterio_EstaSembradoYSeOfreceComoExtra()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;

        var enCatalogo = db.ScoringCriteria.AsNoTracking()
            .Single(c => c.Name == PoolSeed.CriterioDePracticaDocumentada);
        Assert.True(enCatalogo.IsActive);
        Assert.True(enCatalogo.DefaultPoints > 0, "un extra que no suma no es un extra");
        Assert.Equal(CriterionScope.Individual, enCatalogo.Scope);

        var ofrecidos = await Consultas(db, Admin()).CriteriosExtraDisponiblesAsync();
        Assert.Contains(ofrecidos, c => c.Nombre == PoolSeed.CriterioDePracticaDocumentada);
    }

    /// <summary>
    /// Y la aplicación sabe reconocerlo. <c>ExigeArticulo</c> compara por el NOMBRE congelado y no por
    /// el identificador del catálogo: aquél no es estable entre instalaciones —cada base sembró el
    /// suyo— y el nombre es lo que la fila guarda y lo que sobrevive a que el catálogo se depure.
    /// </summary>
    [Fact]
    public void ExigeArticulo_ReconoceAlSuyoYSoloAlSuyo()
    {
        Assert.True(PoolActivityService.ExigeArticulo(PoolSeed.CriterioDePracticaDocumentada));
        Assert.False(PoolActivityService.ExigeArticulo("Agregaste pruebas automatizadas"));
        Assert.False(PoolActivityService.ExigeArticulo("Dejaste la documentación al día"));
    }

    // ── 2. Justificar ────────────────────────────────────────────────────────────

    /// <summary>
    /// El caso corriente: se elige el artículo, se explica, y el título queda CONGELADO en la fila.
    ///
    /// <para>La copia del título es lo que hace que un extra cobrado hace seis meses siga diciendo qué
    /// se aplicó aunque el autor le cambie el nombre al artículo o lo retire. Sin ella diría
    /// «(artículo 47)» y nadie sabría de qué se pagó.</para>
    /// </summary>
    [Fact]
    public async Task Justificar_CongelaElTituloDelArticulo()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "Cómo migrar un importador sin parar producción");
        var (_, extraId) = await ConElExtraTomadaAsync(db, dev);

        var (ok, mensaje) = await Svc(db, Dev(dev)).JustificarCriterioExtraAsync(
            extraId, dev, "Seguí los cinco pasos del artículo para el corte en caliente.", articulo);
        Assert.True(ok, mensaje);

        var fila = db.PoolActivityExtraCriteria.AsNoTracking().Single(c => c.Id == extraId);
        Assert.Equal(articulo, fila.KnowledgeArticleId);
        Assert.Equal("Cómo migrar un importador sin parar producción", fila.KnowledgeArticleTitle);
        Assert.Contains("cinco pasos", fila.Justificacion);

        // Y el título congelado NO cambia aunque el artículo se renombre después.
        var art = db.KnowledgeArticles.Single(a => a.Id == articulo);
        art.Title = "Otro nombre completamente distinto";
        db.SaveChanges();

        Assert.Equal("Cómo migrar un importador sin parar producción",
            db.PoolActivityExtraCriteria.AsNoTracking().Single(c => c.Id == extraId).KnowledgeArticleTitle);
    }

    /// <summary>
    /// Solo se cita lo PUBLICADO. Un borrador —aunque sea propio— no es una práctica documentada, es
    /// un texto que alguien está escribiendo; citarlo permitiría escribir el artículo el mismo día,
    /// dejarlo sin revisar y cobrar por «aplicarlo».
    /// </summary>
    [Theory]
    [InlineData(KnowledgeStatus.Borrador)]
    [InlineData(KnowledgeStatus.PorRevisar)]
    [InlineData(KnowledgeStatus.Rechazado)]
    public async Task Justificar_RechazaLoQueNoEstaPublicado(KnowledgeStatus estado)
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "A medio escribir", estado, autorDevId: dev);
        var (_, extraId) = await ConElExtraTomadaAsync(db, dev);

        var (ok, mensaje) = await Svc(db, Dev(dev))
            .JustificarCriterioExtraAsync(extraId, dev, "Lo apliqué.", articulo);

        Assert.False(ok);
        Assert.Contains("PUBLICADO", mensaje);
        Assert.Null(db.PoolActivityExtraCriteria.AsNoTracking().Single(c => c.Id == extraId).KnowledgeArticleId);
    }

    /// <summary>
    /// EL ARTÍCULO PROPIO SÍ VALE. No es doble pago: escribirlo se cobró una vez y para siempre;
    /// aplicarlo se cobra cada vez, que es exactamente lo que se quiere premiar. Lo que la aplicación
    /// hace es DECÍRSELO al líder, que es la prueba de abajo.
    /// </summary>
    [Fact]
    public async Task Justificar_AdmiteUnArticuloPropio()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "Lo escribí yo", autorDevId: dev);
        var (_, extraId) = await ConElExtraTomadaAsync(db, dev);

        var (ok, mensaje) = await Svc(db, Dev(dev))
            .JustificarCriterioExtraAsync(extraId, dev, "Apliqué lo que documenté en marzo.", articulo);

        Assert.True(ok, mensaje);
    }

    /// <summary>
    /// Y el líder LO VE. Es una derivación de una línea —el autor del artículo contra el dueño de la
    /// actividad— y la diferencia entre una política que funciona y una que nadie aplica: sin
    /// decírselo, la regla «se puede, pero míralo» sería una frase de un documento.
    /// </summary>
    [Fact]
    public async Task ElPanelDelLider_DiceCuandoElArticuloEsDeQuienLoAplica()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");

        int suyo = NuevoArticulo(db, "Lo escribió Ana", autorDevId: ana);
        int ajeno = NuevoArticulo(db, "Lo escribió Beto", autorDevId: beto);

        var (actividadId, extraId) = await ConElExtraTomadaAsync(db, ana);

        await Svc(db, Dev(ana)).JustificarCriterioExtraAsync(extraId, ana, "Apliqué lo mío.", suyo);
        var propio = Assert.Single(await Consultas(db, Admin()).CriteriosExtraDeAsync(actividadId));
        Assert.True(propio.ArticuloEsDeQuienLoAplica);
        Assert.True(propio.ExigeArticulo);

        await Svc(db, Dev(ana)).JustificarCriterioExtraAsync(extraId, ana, "Ahora el de Beto.", ajeno);
        var deOtro = Assert.Single(await Consultas(db, Admin()).CriteriosExtraDeAsync(actividadId));
        Assert.False(deOtro.ArticuloEsDeQuienLoAplica);
    }

    /// <summary>
    /// Justificar es de QUIEN HACE EL TRABAJO, y solo del suyo. La justificación y el veredicto son
    /// dos rutas y dos contratos precisamente para que ninguno de los dos extremos pueda escribir lo
    /// del otro: con una sola operación «actualizar el criterio», quien lo trabaja acabaría pudiendo
    /// darse por cumplido su propio extra.
    /// </summary>
    [Fact]
    public async Task Justificar_NoSePuedeSobreLoAjeno()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        int articulo = NuevoArticulo(db, "Un artículo");
        var (_, extraId) = await ConElExtraTomadaAsync(db, ana);

        // A nombre de otro: excepción, porque la ruta no lleva identificador y llegar aquí con uno
        // ajeno no es un error de quien usa la pantalla.
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Dev(beto, userId: 2)).JustificarCriterioExtraAsync(extraId, ana, "Mío", articulo));

        // Y a nombre propio sobre una actividad ajena: rechazo de negocio, porque eso sí lo puede
        // intentar alguien con una pantalla vieja abierta.
        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2))
            .JustificarCriterioExtraAsync(extraId, beto, "Mío", articulo);
        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
    }

    // ── 3. La guarda de entregar ─────────────────────────────────────────────────

    /// <summary>
    /// SIN JUSTIFICAR NO SE ENTREGA, y el checklist completo no basta.
    ///
    /// <para>Misma forma que la guarda de la evidencia faltante, y por el mismo motivo: pedirlo al
    /// verificar obligaría al líder a devolver la entrega solo para reclamar una frase, y esa vuelta
    /// cuesta más que la frase.</para>
    /// </summary>
    [Fact]
    public async Task Entregar_SeNiegaSiElExtraQueLoPideNoEstaJustificado()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        var (actividadId, _) = await ConElExtraTomadaAsync(db, dev);
        await CompletarChecklistAsync(db, actividadId, dev);

        var (ok, mensaje) = await Svc(db, Dev(dev)).EntregarAsync(actividadId, dev);

        Assert.False(ok);
        Assert.Contains("artículo", mensaje);
        Assert.Equal(PoolActivityStatus.Tomada,
            db.PoolActivities.AsNoTracking().Single(a => a.Id == actividadId).Status);
    }

    /// <summary>
    /// Y la justificación SIN artículo tampoco basta: se piden los dos. El texto explica qué se hizo;
    /// el artículo es lo que hace la afirmación comprobable —y lo que, sumado sobre todas las
    /// entregas, contesta qué prácticas se usan de verdad—.
    /// </summary>
    [Fact]
    public async Task Entregar_SeNiegaConJustificacionPeroSinArticulo()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        var (actividadId, extraId) = await ConElExtraTomadaAsync(db, dev);
        await CompletarChecklistAsync(db, actividadId, dev);

        await Svc(db, Dev(dev)).JustificarCriterioExtraAsync(extraId, dev, "Apliqué una práctica.", null);

        var (ok, _) = await Svc(db, Dev(dev)).EntregarAsync(actividadId, dev);
        Assert.False(ok);
    }

    /// <summary>Con las dos cosas, se entrega.</summary>
    [Fact]
    public async Task Entregar_PasaCuandoEstaJustificadoYCitado()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "La práctica");
        var (actividadId, extraId) = await ConElExtraTomadaAsync(db, dev);
        await CompletarChecklistAsync(db, actividadId, dev);

        await Svc(db, Dev(dev)).JustificarCriterioExtraAsync(
            extraId, dev, "Lo apliqué en el paso del corte.", articulo);

        var (ok, mensaje) = await Svc(db, Dev(dev)).EntregarAsync(actividadId, dev);

        Assert.True(ok, mensaje);
        Assert.Equal(PoolActivityStatus.EnRevision,
            db.PoolActivities.AsNoTracking().Single(a => a.Id == actividadId).Status);
    }

    /// <summary>
    /// LOS DEMÁS EXTRAS NO SE TOCAN. Obligar a justificar todos convertiría en trámite unos campos
    /// que existen para que UNO concreto se pueda verificar — y la mayoría se verifican mirando la
    /// entrega, que es lo que los hace baratos.
    /// </summary>
    [Fact]
    public async Task Entregar_NoPideJustificacionALosDemasExtras()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        int otro = db.ScoringCriteria.AsNoTracking()
            .Single(c => c.Name == "Agregaste pruebas automatizadas").Id;

        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(new PoolActivity
        {
            Title = "Con otro extra", WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media, HorasEstimadas = 6m
        }, [otro]);

        await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev);
        await CompletarChecklistAsync(db, actividad.Id, dev);

        var (ok, mensaje) = await Svc(db, Dev(dev)).EntregarAsync(actividad.Id, dev);

        Assert.True(ok, mensaje);
    }

    /// <summary>
    /// Después de entregar ya no se reescribe: el líder la está mirando y cambiarle la justificación
    /// bajo los pies sería contestarle a una pregunta que ya no hizo. Devuelta SÍ, que es justo
    /// cuando hay que corregirla.
    /// </summary>
    [Fact]
    public async Task Justificar_SeCierraAlEntregarYSeReabreAlDevolver()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "La práctica");
        var (actividadId, extraId) = await ConElExtraTomadaAsync(db, dev);
        await CompletarChecklistAsync(db, actividadId, dev);
        await Svc(db, Dev(dev)).JustificarCriterioExtraAsync(extraId, dev, "Primera versión.", articulo);
        Assert.True((await Svc(db, Dev(dev)).EntregarAsync(actividadId, dev)).ok);

        var (cerrado, queja) = await Svc(db, Dev(dev))
            .JustificarCriterioExtraAsync(extraId, dev, "Se me ocurrió otra cosa.", articulo);
        Assert.False(cerrado);
        Assert.Contains("entregaste", queja);

        // El líder la devuelve porque la justificación no explicaba nada: ahora sí se corrige.
        Assert.True((await Svc(db, Admin()).RechazarAsync(actividadId, "Explica mejor qué aplicaste.")).ok);

        var (reabierto, mensaje) = await Svc(db, Dev(dev))
            .JustificarCriterioExtraAsync(extraId, dev, "Apliqué los pasos 3 y 4 en el corte.", articulo);
        Assert.True(reabierto, mensaje);
        Assert.Contains("pasos 3 y 4",
            db.PoolActivityExtraCriteria.AsNoTracking().Single(c => c.Id == extraId).Justificacion);
    }

    /// <summary>
    /// Y las DOS VOCES no se pisan: la justificación es de quien hizo el trabajo y el comentario es
    /// del líder. Es la razón de que sean dos columnas y no una — a los seis meses, con una sola,
    /// nadie sabría quién escribió qué.
    /// </summary>
    [Fact]
    public async Task LaJustificacionYElComentario_SonDosVocesQueNoSePisan()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        int articulo = NuevoArticulo(db, "La práctica");
        var (actividadId, extraId) = await ConElExtraTomadaAsync(db, dev);
        await CompletarChecklistAsync(db, actividadId, dev);
        await Svc(db, Dev(dev)).JustificarCriterioExtraAsync(extraId, dev, "Lo que hice yo.", articulo);
        await Svc(db, Dev(dev)).EntregarAsync(actividadId, dev);

        await Svc(db, Admin()).EvaluarCriterioExtraAsync(extraId, true, "Lo que digo yo.");

        var fila = db.PoolActivityExtraCriteria.AsNoTracking().Single(c => c.Id == extraId);
        Assert.Equal("Lo que hice yo.", fila.Justificacion);
        Assert.Equal("Lo que digo yo.", fila.Comment);
    }
}
