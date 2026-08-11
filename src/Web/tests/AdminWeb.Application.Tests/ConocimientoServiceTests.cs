using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La base de conocimiento: escribir, revisar, publicar y puntuar.
///
/// Lo que estas pruebas cuidan son las tres promesas de las que depende que la funcionalidad siga
/// viva: que un borrador ajeno NO se lea (ni el líder), que la cola se vea y se avise de ella, y que
/// un artículo otorgue puntos exactamente UNA vez por muchas veces que se apruebe.
/// </summary>
public class ConocimientoServiceTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    /// <summary>
    /// Cada llamada con su PROPIO contexto contra la misma base, como en producción (scoped, uno por
    /// petición). Compartir contexto entre llamadas probaría un escenario que en la web no existe y
    /// escondería justamente lo que aquí importa: que la segunda llamada lee la BASE y no lo que
    /// dejó rastreado la primera.
    /// </summary>
    private ConocimientoService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = Otro(db);
        return new ConocimientoService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx));
    }

    private AppDbContext Otro(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private const int UidAna = 10, UidBeto = 11, UidLider = 1, UidOps = 20;

    private static ICurrentUser Ana(int? developerId = null) => new UsuarioDePrueba
    { UserId = UidAna, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, DeveloperId = developerId };

    private static ICurrentUser Beto => new UsuarioDePrueba
    { UserId = UidBeto, Username = "beto", FullName = "Beto", Role = UserRole.Desarrollador, DeveloperId = null };

    private static ICurrentUser Lider => new UsuarioDePrueba
    { UserId = UidLider, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin };

    private static ICurrentUser Operaciones => new UsuarioDePrueba
    { UserId = UidOps, Username = "ops", FullName = "Ops", Role = UserRole.Operaciones };

    /// <summary>Un cuerpo que pasa el mínimo para mandar a revisar.</summary>
    private const string Cuerpo =
        "# Cómo se despliega\n\nSe corre el paquete y se revisa el log antes de dar por bueno el cambio.";

    private static AppDbContext BaseConLider()
    {
        var db = TestDb.New();
        db.Users.Add(new User { Id = UidLider, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();
        return db;
    }

    /// <summary>Base con líder, la ficha de Ana y un criterio del catálogo con el que puntuar.</summary>
    private static (AppDbContext db, int developerId, int criterioId) BaseCompleta()
    {
        var db = BaseConLider();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        var criterio = new ScoringCriterion
        { Name = "Dejaste la documentación al día", DefaultPoints = 5, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(criterio);
        db.SaveChanges();
        return (db, dev.Id, criterio.Id);
    }

    private async Task<int> CrearYEnviarAsync(AppDbContext db, ICurrentUser autor)
    {
        var (_, _, articulo) = await Svc(db, autor).CrearAsync("Despliegue del portal", Cuerpo, "despliegue, Portal");
        await Svc(db, autor).EnviarARevisionAsync(articulo!.Id);
        return articulo.Id;
    }

    // ── Los estados ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_NaceEnBorradorYConLasEtiquetasNormalizadas()
    {
        var db = BaseConLider();
        var (ok, _, a) = await Svc(db, Ana()).CrearAsync("Glosario: SLA", Cuerpo, "Glosario, SLA, glosario");

        Assert.True(ok);
        var g = db.KnowledgeArticles.AsNoTracking().Single();
        Assert.Equal(KnowledgeStatus.Borrador, g.Status);
        Assert.Equal(UidAna, g.AuthorUserId);
        Assert.Equal("Ana", g.AuthorName);
        // En minúsculas y sin repetidas: las etiquetas son el índice de la base, y «SLA» y «sla»
        // escritas por dos personas partirían en dos el mismo tema.
        Assert.Equal("glosario, sla", g.Tags);
        Assert.Equal(a!.Id, g.Id);
    }

    [Fact]
    public async Task Borrador_NoLoVeNadieMas_NiSiquieraElLider()
    {
        var db = BaseConLider();
        var (_, _, a) = await Svc(db, Ana()).CrearAsync("A medias", Cuerpo, null);

        Assert.NotNull(await Svc(db, Ana()).LeerAsync(a!.Id));
        Assert.Null(await Svc(db, Beto).LeerAsync(a.Id));
        // El líder tampoco: si pudiera leer lo que está a medias, «borrador» dejaría de significar
        // nada y la gente escribiría en otro sitio hasta tenerlo presentable.
        Assert.Null(await Svc(db, Lider).LeerAsync(a.Id));

        Assert.Empty((await Svc(db, Lider).BuscarAsync()).Filas);
        Assert.Single((await Svc(db, Ana()).BuscarAsync()).Filas);
    }

    [Fact]
    public async Task PorRevisar_LoVenSuAutorYElLider_YNadieMas()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());

        Assert.NotNull(await Svc(db, Ana()).LeerAsync(id));
        Assert.NotNull(await Svc(db, Lider).LeerAsync(id));
        Assert.Null(await Svc(db, Beto).LeerAsync(id));
        Assert.Null(await Svc(db, Operaciones).LeerAsync(id));
    }

    [Fact]
    public async Task Publicado_LoVeCualquieraConSesion_IncluidaOperaciones()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        // Operaciones entra a leer lo publicado: esto es documentación de trabajo —cómo se despliega
        // un sistema—, no la conversación del equipo de la que sí queda fuera en el foro.
        var leido = await Svc(db, Operaciones).LeerAsync(id);
        Assert.NotNull(leido);
        Assert.Equal(KnowledgeStatus.Publicado, leido!.Estado);
        // Pero no puede editarlo ni ve el ida y vuelta con el líder: eso es de su autor y del líder.
        Assert.False(leido.PuedoEditar);
        Assert.Null(leido.Fuente);
        Assert.Null(leido.Historial);
    }

    [Fact]
    public async Task Operaciones_NoEscribe_AunqueLeaLoPublicado()
    {
        var db = BaseConLider();
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, Operaciones).CrearAsync("Un artículo", Cuerpo, null));
    }

    [Fact]
    public async Task Desarrollador_NoAprueba()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Beto).AprobarAsync(id));
    }

    // ── El rechazo, que es un estado propio ──────────────────────────────────────

    [Fact]
    public async Task Rechazar_DejaEstadoPropio_Y_ExigeMotivo()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());

        var (sinMotivo, _) = await Svc(db, Lider).RechazarAsync(id, "  ");
        Assert.False(sinMotivo);   // devolver sin decir por qué no enseña nada

        var (ok, _) = await Svc(db, Lider).RechazarAsync(id, "Falta el paso de respaldo previo.");
        Assert.True(ok);

        var g = db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id);
        // Estado propio y no «vuelve a borrador»: «nunca se mandó» y «se mandó y se devolvió» son
        // dos hechos distintos, y lo devuelto tiene que poder contarse.
        Assert.Equal(KnowledgeStatus.Rechazado, g.Status);
        Assert.Equal("Falta el paso de respaldo previo.", g.ReviewComment);
        Assert.Equal("Jefe", g.ReviewerName);
    }

    [Fact]
    public async Task Devuelto_SeCorrige_SeReenviaYSubeDeVuelta_ConservandoElHistorial()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).RechazarAsync(id, "Falta el paso de respaldo previo.");

        var (ok, _) = await Svc(db, Ana()).EditarAsync(id, "Despliegue del portal", Cuerpo + "\n\nAntes se respalda.", "despliegue");
        Assert.True(ok);
        await Svc(db, Ana()).EnviarARevisionAsync(id);

        var g = db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(KnowledgeStatus.PorRevisar, g.Status);
        Assert.Equal(2, g.ReviewRound);
        // El motivo de la primera devolución no se pierde al volver a enviar: sin el historial, la
        // conversación se quedaría sin la mitad que la explica.
        Assert.Contains("Falta el paso de respaldo previo", g.ReviewHistory);
        Assert.Contains("vuelta 2", g.ReviewHistory);
    }

    [Fact]
    public async Task Rechazar_SirveParaRetirarAlgoYaPublicado()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        var (ok, _) = await Svc(db, Lider).RechazarAsync(id, "El procedimiento cambió: ya no aplica.");
        Assert.True(ok);

        Assert.Equal(KnowledgeStatus.Rechazado, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).Status);
        Assert.Null(await Svc(db, Beto).LeerAsync(id));   // deja de verse
    }

    // ── Editar ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editar_LoPublicado_LoDevuelveALaCola_SiLoHaceSuAutor()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        await Svc(db, Ana()).EditarAsync(id, "Despliegue del portal", Cuerpo + "\n\nUn paso más.", "despliegue");

        var g = db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id);
        // Si una edición posterior se colara sin revisar, la revisión no garantizaría nada: bastaría
        // publicar algo inocuo y cambiarlo después.
        Assert.Equal(KnowledgeStatus.PorRevisar, g.Status);
        Assert.Equal(2, g.ReviewRound);
        Assert.NotNull(g.PublishedAtUtc);   // la primera publicación no se borra
    }

    [Fact]
    public async Task Editar_LoPublicado_NoLoSacaSiLoHaceElLider()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        await Svc(db, Lider).EditarAsync(id, "Despliegue del portal", Cuerpo + "\n\nErrata corregida.", "despliegue");

        // El líder es el revisor: su edición ya está revisada por definición. Es lo que permite
        // arreglar una errata sin dejar al equipo sin el artículo.
        Assert.Equal(KnowledgeStatus.Publicado, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).Status);
    }

    [Fact]
    public async Task Editar_LoAjeno_NoSePuede()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        var (ok, mensaje) = await Svc(db, Beto).EditarAsync(id, "Otro título", Cuerpo, null);
        Assert.False(ok);
        Assert.Contains("tú escribiste", mensaje);
    }

    // ── Los puntos: la parte que no puede pagar dos veces ────────────────────────

    [Fact]
    public async Task Aprobar_ConCriterio_PublicaYOtorgaEnLaMismaOperacion()
    {
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        var (ok, mensaje) = await Svc(db, Lider).AprobarAsync(id, criterioId, 8, "Quedó muy claro.");
        Assert.True(ok);
        Assert.Contains("8 puntos", mensaje);

        var g = db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(KnowledgeStatus.Publicado, g.Status);
        Assert.Equal(8, g.PointsAwarded);
        Assert.NotNull(g.PointEntryId);

        var entrada = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(devId, entrada.DeveloperId);
        Assert.Equal(criterioId, entrada.CriterionId);
        Assert.Equal(8, entrada.Points);
        // Nace aprobada: la revisión es ÉSTA, mandarla además a la cola de autocalificación sería
        // revisar dos veces lo mismo.
        Assert.Equal(PointApprovalStatus.Aprobado, entrada.ApprovalStatus);
        Assert.Equal(g.PointEntryId, entrada.Id);
        Assert.Contains($"Conocimiento #{id}", entrada.Comment);
    }

    [Fact]
    public async Task Aprobar_SinCriterio_PublicaYNoCreaNingunaEntrada()
    {
        var (db, devId, _) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        var (ok, _) = await Svc(db, Lider).AprobarAsync(id);
        Assert.True(ok);

        // No todos los artículos puntúan: aprobar sin puntos es normal, no un descuido.
        Assert.Empty(db.PointEntries.AsNoTracking());
        Assert.Equal(0, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).PointsAwarded);
    }

    [Fact]
    public async Task Aprobar_DosVecesTrasUnaEdicion_NoVuelveAPagar()
    {
        // ESTE es el escenario que obliga a que la guarda viva en la fila y no en una comprobación:
        // un artículo publicado se edita, vuelve a la cola y se vuelve a aprobar. Puede pasar muchas
        // veces; pagar tiene que pasar una.
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        await Svc(db, Lider).AprobarAsync(id, criterioId, 8);
        await Svc(db, Ana(devId)).EditarAsync(id, "Despliegue del portal", Cuerpo + "\n\nUn paso más.", "despliegue");

        var (ok, mensaje) = await Svc(db, Lider).AprobarAsync(id, criterioId, 8);

        Assert.True(ok);                                   // la reaprobación es legítima
        Assert.Contains("ya recibió 8", mensaje);          // y se dice por qué no se pagó otra vez
        Assert.Single(db.PointEntries.AsNoTracking());     // UNA sola entrada en toda su vida
        Assert.Equal(8, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).PointsAwarded);
        Assert.Equal(KnowledgeStatus.Publicado, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).Status);
    }

    [Fact]
    public async Task Aprobar_LoQueYaEstaPublicado_SeRechaza()
    {
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));
        await Svc(db, Lider).AprobarAsync(id, criterioId, 5);

        var (ok, _) = await Svc(db, Lider).AprobarAsync(id, criterioId, 5);
        Assert.False(ok);
        Assert.Single(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Aprobar_ConPuntosPeroSinCriterio_SeRechaza()
    {
        var (db, devId, _) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        var (ok, mensaje) = await Svc(db, Lider).AprobarAsync(id, null, 8);
        Assert.False(ok);
        Assert.Contains("criterio", mensaje);
        Assert.Equal(KnowledgeStatus.PorRevisar, db.KnowledgeArticles.AsNoTracking().Single(a => a.Id == id).Status);
    }

    [Fact]
    public async Task Aprobar_SinCantidad_TomaLaDelCatalogo()
    {
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        await Svc(db, Lider).AprobarAsync(id, criterioId);
        Assert.Equal(5, db.PointEntries.AsNoTracking().Single().Points);   // el DefaultPoints del criterio
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ConocimientoService.MaxPuntos + 1)]
    public async Task Aprobar_ConCantidadImposible_SeRechaza(int puntos)
    {
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));

        var (ok, _) = await Svc(db, Lider).AprobarAsync(id, criterioId, puntos);
        Assert.False(ok);
        Assert.Empty(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Aprobar_ConCriterioDeEquipo_SeRechaza()
    {
        var (db, devId, _) = BaseCompleta();
        var equipo = new ScoringCriterion { Name = "Documentación del equipo al día", DefaultPoints = 5, Scope = CriterionScope.Equipo };
        db.ScoringCriteria.Add(equipo);
        db.SaveChanges();

        var id = await CrearYEnviarAsync(db, Ana(devId));
        var (ok, mensaje) = await Svc(db, Lider).AprobarAsync(id, equipo.Id, 5);

        Assert.False(ok);
        Assert.Contains("de equipo", mensaje);
    }

    [Fact]
    public async Task Aprobar_ConPuntos_AAlguienSinFicha_SeExplicaYNoSePublica()
    {
        var (db, _, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Beto);   // Beto no tiene ficha de desarrollador

        var (ok, mensaje) = await Svc(db, Lider).AprobarAsync(id, criterioId, 5);
        Assert.False(ok);
        Assert.Contains("sin puntos", mensaje);   // y se le dice al líder qué puede hacer

        // Sin ficha se puede publicar igual: lo que no hay es a quién abonarle.
        Assert.True((await Svc(db, Lider).AprobarAsync(id)).ok);
    }

    [Fact]
    public async Task Borrar_NoSePuede_SiElArticuloYaOtorgoPuntos()
    {
        var (db, devId, criterioId) = BaseCompleta();
        var id = await CrearYEnviarAsync(db, Ana(devId));
        await Svc(db, Lider).AprobarAsync(id, criterioId, 5);
        await Svc(db, Lider).RechazarAsync(id, "Ya no aplica el procedimiento.");

        var (ok, mensaje) = await Svc(db, Ana(devId)).EliminarAsync(id);
        Assert.False(ok);
        Assert.Contains("abono", mensaje);
        Assert.Single(db.KnowledgeArticles.AsNoTracking());
    }

    // ── La cola, que es lo que mata a la funcionalidad si nadie la mira ──────────

    [Fact]
    public async Task Enviar_AvisaALosLideres_ConUnAvisoPorVuelta()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());

        var aviso = Assert.Single(db.Notifications.AsNoTracking().Where(n => n.ForUserId == UidLider));
        Assert.Contains("por revisar", aviso.Title);
        Assert.Contains("Ana", aviso.Message);

        // Reintentar el mismo envío no vuelve a avisar…
        await Svc(db, Ana()).EnviarARevisionAsync(id);
        Assert.Single(db.Notifications.AsNoTracking().Where(n => n.ForUserId == UidLider));

        // …pero una vuelta nueva sí, porque es otra revisión distinta.
        await Svc(db, Lider).RechazarAsync(id, "Le falta el paso de respaldo.");
        await Svc(db, Ana()).EnviarARevisionAsync(id);
        Assert.Equal(2, db.Notifications.AsNoTracking().Count(n => n.ForUserId == UidLider));
    }

    [Fact]
    public async Task Aprobar_YRechazar_AvisanASuAutor()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).RechazarAsync(id, "Le falta el paso de respaldo.");

        var aviso = Assert.Single(db.Notifications.AsNoTracking().Where(n => n.ForUserId == UidAna));
        Assert.Contains("devolvieron", aviso.Title);
        Assert.Contains("respaldo", aviso.Message);
    }

    [Fact]
    public async Task Pendientes_CuentaLosDosLados()
    {
        var db = BaseConLider();
        var enCola = await CrearYEnviarAsync(db, Ana());
        var devuelto = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).RechazarAsync(devuelto, "Le falta el ejemplo.");
        await Svc(db, Ana()).CrearAsync("A medias", Cuerpo, null);

        var deAna = await Svc(db, Ana()).PendientesAsync();
        Assert.Equal(0, deAna.PorRevisar);      // Ana no revisa
        Assert.Equal(1, deAna.MisBorradores);
        Assert.Equal(1, deAna.MisDevueltos);

        var delLider = await Svc(db, Lider).PendientesAsync();
        Assert.Equal(1, delLider.PorRevisar);
        Assert.Equal(0, delLider.MisBorradores);

        Assert.Single(await Svc(db, Lider).ColaDeRevisionAsync());
        Assert.Equal(enCola, (await Svc(db, Lider).ColaDeRevisionAsync())[0].Id);
    }

    [Fact]
    public async Task Cola_PoneArribaLoQueLlevaMasTiempoEsperando()
    {
        // El orden es la mitad del asunto: lo que lleva nueve días parado es exactamente lo que hace
        // que su autor no vuelva a escribir, y por fecha descendente eso se queda al final.
        var db = BaseConLider();
        var viejo = await CrearYEnviarAsync(db, Ana());
        var nuevo = await CrearYEnviarAsync(db, Beto);

        var antiguo = db.KnowledgeArticles.Single(a => a.Id == viejo);
        antiguo.SubmittedAtUtc = DateTime.UtcNow.AddDays(-9);
        db.SaveChanges();

        var cola = await Svc(db, Lider).ColaDeRevisionAsync();
        Assert.Equal(viejo, cola[0].Id);
        Assert.Equal(nuevo, cola[1].Id);
        Assert.Equal(9, cola[0].DiasEsperando);

        Assert.Equal(9, (await Svc(db, Lider).PendientesAsync()).DiasDelMasAntiguo);
    }

    // ── Buscar ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Buscar_PorEtiqueta_CasaLaEtiquetaENTERA()
    {
        var db = BaseConLider();
        var uno = await Svc(db, Ana()).CrearAsync("Índices", Cuerpo, "sql");
        var dos = await Svc(db, Ana()).CrearAsync("Mantenimiento", Cuerpo, "sql server");
        await Svc(db, Ana()).EnviarARevisionAsync(uno.articulo!.Id);
        await Svc(db, Ana()).EnviarARevisionAsync(dos.articulo!.Id);
        await Svc(db, Lider).AprobarAsync(uno.articulo.Id);
        await Svc(db, Lider).AprobarAsync(dos.articulo.Id);

        // Sin comparar con los separadores puestos, «sql» arrastraría también «sql server».
        var soloSql = await Svc(db, Beto).BuscarAsync(new ConocimientoFiltro(Etiqueta: "sql"));
        Assert.Equal(1, soloSql.Total);
        Assert.Equal("Índices", soloSql.Filas[0].Titulo);

        var etiquetas = await Svc(db, Beto).EtiquetasAsync();
        Assert.Equal(2, etiquetas.Count);
    }

    [Fact]
    public async Task Buscar_PorTexto_NoDevuelveBorradoresAjenos()
    {
        var db = BaseConLider();
        await Svc(db, Ana()).CrearAsync("Secreto a medias", "Esto habla de kubernetes y no está listo.", "infra");

        // El filtro de visibilidad va DENTRO de la consulta: si se colara y se descartara después,
        // el total mentiría — y sobre todo, el dato ya habría salido de la base.
        var deBeto = await Svc(db, Beto).BuscarAsync(new ConocimientoFiltro(Texto: "kubernetes"));
        Assert.Equal(0, deBeto.Total);
        Assert.Empty(deBeto.Filas);

        var deAna = await Svc(db, Ana()).BuscarAsync(new ConocimientoFiltro(Texto: "kubernetes"));
        Assert.Equal(1, deAna.Total);
    }

    [Fact]
    public async Task Leer_DevuelveLaFuenteSoloAQuienPuedeEditar()
    {
        var db = BaseConLider();
        var id = await CrearYEnviarAsync(db, Ana());
        await Svc(db, Lider).AprobarAsync(id);

        var deAna = await Svc(db, Ana()).LeerAsync(id);
        Assert.True(deAna!.PuedoEditar);
        Assert.Equal(Cuerpo, deAna.Fuente);
        Assert.NotEmpty(deAna.Cuerpo);   // y también el cuerpo ya analizado en bloques

        var deBeto = await Svc(db, Beto).LeerAsync(id);
        Assert.False(deBeto!.PuedoEditar);
        Assert.Null(deBeto.Fuente);
        Assert.NotEmpty(deBeto.Cuerpo);
    }

    [Fact]
    public async Task Enviar_LoQueEsDemasiadoCorto_SeRechaza()
    {
        var db = BaseConLider();
        // Un borrador puede estar como sea; hacer que el líder abra tres renglones sueltos es la
        // forma más rápida de que deje de abrir la cola.
        var (_, _, a) = await Svc(db, Ana()).CrearAsync("Un título válido", "corto", null);
        var (ok, _) = await Svc(db, Ana()).EnviarARevisionAsync(a!.Id);

        Assert.False(ok);
        Assert.Equal(KnowledgeStatus.Borrador, db.KnowledgeArticles.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Enviar_LoAjeno_NoSePuede()
    {
        var db = BaseConLider();
        var (_, _, a) = await Svc(db, Ana()).CrearAsync("Un título válido", Cuerpo, null);

        // Beto ni siquiera lo ve, así que la negativa no le confirma que exista.
        var (ok, _) = await Svc(db, Beto).EnviarARevisionAsync(a!.Id);
        Assert.False(ok);
    }
}
