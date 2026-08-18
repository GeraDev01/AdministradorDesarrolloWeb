using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL ALTA AUTOMÁTICA: que un work item de Azure DevOps acabe solo en el pool, sin clasificar, y que
/// al líder le quede únicamente decidir qué es, cuánto lleva y de quién es.
///
/// <para><b>Lo que estas pruebas cuidan es que el automatismo no haga daño.</b> Un alta que se repite
/// llena el pool de duplicados; una que nace tomable deja que alguien se lleve puntos que nadie
/// eligió; y una que empuja a DevOps antes de tiempo le baja la prioridad al ticket que acaba de
/// crearse. Las tres cosas fallan en SILENCIO, que es lo que las hace caras.</para>
///
/// <para>Nada de esto toca la red: el alta lee de <c>DevOpsTickets</c>, que es donde la
/// sincronización ya dejó los work items.</para>
/// </summary>
public class PoolDesdeDevOpsTests : IDisposable
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

    private const string CorreoDelLider = "lider@soltum.com.mx";

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);

    private PoolDesdeDevOpsService Alta(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolDesdeDevOpsService(ctx, cu, new SettingsService(ctx, cu, bitacora),
                                          bitacora, new NotificationService(ctx));
    }

    private PoolActivityService Pool(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
                                       new SettingsService(ctx, cu, bitacora));
    }

    /// <summary>Base con el pool sembrado y la integración encendida.</summary>
    private static async Task<AppDbContext> BaseListaAsync(string? correos = CorreoDelLider, int? dias = null)
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);

        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsEnabled, Value = "true" });
        if (correos != null)
            db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.PoolDevOpsCorreosDeAlta, Value = correos });
        if (dias is int d)
            db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.PoolDevOpsDiasDeAlta, Value = d.ToString() });

        await db.SaveChangesAsync();
        return db;
    }

    private static DevOpsTicket Ticket(
        int numero, string titulo = "Corregir el cálculo", string estado = "Active",
        string? correo = CorreoDelLider, int diasDeAntiguedad = 1, string? descripcion = null) => new()
        {
            ExternalId = numero,
            Title = titulo,
            WorkItemType = "Bug",
            State = estado,
            Priority = "2",
            AssignedTo = correo is null ? "" : "Quien Sea",
            AssignedToUniqueName = correo,
            Description = descripcion,
            Url = $"https://dev.azure.com/org/proj/_workitems/edit/{numero}",
            CreatedAtExternal = DateTime.UtcNow.AddDays(-diasDeAntiguedad)
        };

    private static async Task ConTicketsAsync(AppDbContext db, params DevOpsTicket[] tickets)
    {
        db.DevOpsTickets.AddRange(tickets);
        await db.SaveChangesAsync();
    }

    private static Task<List<PoolActivity>> DelPoolAsync(AppDbContext db) =>
        db.PoolActivities.AsNoTracking().OrderBy(a => a.Id).ToListAsync();

    // ── 1. Lo que entra, y cómo nace ─────────────────────────────────────────────

    /// <summary>
    /// <b>La prueba central.</b> El work item se convierte en actividad con lo que se puede copiar, y
    /// nace SIN CLASIFICAR y SIN PUNTOS: quien la mire sabe que todavía no vale nada.
    /// </summary>
    [Fact]
    public async Task Alta_creaLaActividadSinClasificarYSinPuntos()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321, "Corregir el cálculo de facturación",
                                         descripcion: "<div><p>Truena con archivos grandes</p></div>"));

        var resultado = await Alta(db, Admin()).LlevarAlPoolAsync();
        Assert.True(resultado.Ok, resultado.Mensaje);
        Assert.Equal(1, resultado.Creadas);

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(PoolActivityStatus.PorClasificar, actividad.Status);
        Assert.Equal(0, actividad.Points);
        Assert.Equal(4321, actividad.DevOpsWorkItemId);
        Assert.Equal("Corregir el cálculo de facturación", actividad.Title);
        Assert.Contains("_workitems/edit/4321", actividad.ExternalUrl);

        // La descripción llega en HTML y la escribe gente de fuera del equipo: se guarda en texto.
        Assert.Equal("Truena con archivos grandes", actividad.Description);
    }

    /// <summary>
    /// La prioridad se siembra del TICKET y no se deja en «Media».
    ///
    /// <para>No es cosmético: «Media» se escribe en DevOps como 3 y allá el valor por omisión es 2,
    /// así que publicar una bandeja entera sin tocar la prioridad le bajaría la prioridad a todos
    /// esos tickets de golpe.</para>
    /// </summary>
    [Fact]
    public async Task Alta_siembraLaPrioridadDelTicket()
    {
        using var db = await BaseListaAsync();
        var ticket = Ticket(100);
        ticket.Priority = "1";                      // muy alta en DevOps
        await ConTicketsAsync(db, ticket);

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(PoolPriority.Critica, actividad.Priority);
        Assert.Equal(1, PrioridadDelPoolEnDevOps.ADevOps(actividad.Priority));
    }

    /// <summary>
    /// <b>Sin clasificar no se puede TOMAR</b>, y se prueba contra el servicio y no contra la lista:
    /// esconderla de la pantalla no es impedirlo, porque a la ruta se la puede llamar con cualquier
    /// identificador.
    /// </summary>
    [Fact]
    public async Task SinClasificar_noSePuedeTomar()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));

        var dev = new Developer { FullName = "Quien la intenta", IsActive = true };
        db.Developers.Add(dev);
        await db.SaveChangesAsync();

        var quien = UsuarioDePrueba.Como(UserRole.Desarrollador, dev.Id, userId: 2);
        var (tomada, mensaje) = await Pool(db, quien).TomarAsync(actividad.Id, dev.Id);

        Assert.False(tomada, mensaje);
        Assert.Equal(PoolActivityStatus.PorClasificar,
                     (await DelPoolAsync(db)).Single().Status);
    }

    /// <summary>Tampoco sale en el pool de nadie: la lista de libres filtra por «Disponible».</summary>
    [Fact]
    public async Task SinClasificar_noSaleEnElPoolDelDesarrollador()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var dev = new Developer { FullName = "Cualquiera", IsActive = true };
        db.Developers.Add(dev);
        await db.SaveChangesAsync();

        var libres = await Pool(db, UsuarioDePrueba.Como(UserRole.Desarrollador, dev.Id, userId: 2))
            .DisponiblesAsync();

        Assert.Empty(libres);
    }

    // ── 2. Que no se repita ──────────────────────────────────────────────────────

    [Fact]
    public async Task SegundaPasada_noDuplica()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));

        await Alta(db, Admin()).LlevarAlPoolAsync();
        var segunda = await Alta(db, Admin()).LlevarAlPoolAsync();

        Assert.Equal(0, segunda.Creadas);
        Assert.Single(await DelPoolAsync(db));
    }

    /// <summary>
    /// Un work item cuya actividad se DESCARTÓ no vuelve a entrar solo.
    ///
    /// <para>Es la consecuencia asumida de deduplicar contra el pool en TODOS los estados, y hay que
    /// probarla porque es lo que separa «no se repite» de «se repite para siempre»: la regla de una
    /// sola actividad viva por work item deja de proteger en cuanto la anterior se retira, así que
    /// sin esto cada pasada volvería a crearla.</para>
    /// </summary>
    [Fact]
    public async Task WorkItemDescartado_noVuelveAEntrar()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));

        await Alta(db, Admin()).LlevarAlPoolAsync();
        var actividad = Assert.Single(await DelPoolAsync(db));

        var (retirada, mensaje) = await Pool(db, Admin()).RetirarAsync(actividad.Id);
        Assert.True(retirada, mensaje);

        var segunda = await Alta(db, Admin()).LlevarAlPoolAsync();

        Assert.Equal(0, segunda.Creadas);
        Assert.Single(await DelPoolAsync(db));
    }

    // ── 3. Qué se queda fuera ────────────────────────────────────────────────────

    [Fact]
    public async Task Cerrado_noEntra()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db,
            Ticket(1, "Ya terminado", estado: "Closed"),
            Ticket(2, "Hecho", estado: "Done"),
            Ticket(3, "Vivo", estado: "Active"));

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(3, actividad.DevOpsWorkItemId);
    }

    [Fact]
    public async Task FueraDeLaVentana_noEntra()
    {
        using var db = await BaseListaAsync(dias: 7);
        await ConTicketsAsync(db,
            Ticket(1, "De hace un mes", diasDeAntiguedad: 30),
            Ticket(2, "De ayer", diasDeAntiguedad: 1));

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(2, actividad.DevOpsWorkItemId);
    }

    /// <summary>De otra persona no entra; sin dueño sí, que es lo que hay que repartir.</summary>
    [Fact]
    public async Task Ajeno_noEntra_peroSinDuennoSi()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db,
            Ticket(1, "De otro", correo: "otro@soltum.com.mx"),
            Ticket(2, "Sin dueño", correo: null),
            Ticket(3, "Mío", correo: CorreoDelLider));

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var numeros = (await DelPoolAsync(db)).Select(a => a.DevOpsWorkItemId).ToList();
        Assert.Equal([2, 3], numeros);
    }

    /// <summary>
    /// Sin correos configurados solo entran los que no tienen dueño. Adivinar cuáles son «los míos»
    /// por cualquier otro camino llenaría el pool de trabajo ajeno.
    /// </summary>
    [Fact]
    public async Task SinCorreosConfigurados_soloEntraLoQueNoTieneDuenno()
    {
        using var db = await BaseListaAsync(correos: null);
        await ConTicketsAsync(db,
            Ticket(1, "De alguien", correo: "quien.sea@soltum.com.mx"),
            Ticket(2, "Sin dueño", correo: null));

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(2, actividad.DevOpsWorkItemId);
    }

    [Fact]
    public async Task ConLaIntegracionApagada_noHaceNada()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        await ConTicketsAsync(db, Ticket(4321));

        var resultado = await Alta(db, Admin()).LlevarAlPoolAsync();

        Assert.False(resultado.Ok);
        Assert.Empty(await DelPoolAsync(db));
        db.Dispose();
    }

    // ── 4. El tope por pasada ────────────────────────────────────────────────────

    /// <summary>
    /// El tope recorta, LO DICE, y deja pasar primero a los más antiguos.
    ///
    /// <para>El orden importa tanto como el tope: cortando por el más reciente, los de siempre
    /// ganarían cada pasada y los que quedaron fuera no entrarían jamás.</para>
    /// </summary>
    [Fact]
    public async Task Tope_recortaYDejaPasarPrimeroALosMasViejos()
    {
        using var db = await BaseListaAsync(dias: 90);

        // Uno más que el tope, y el más viejo es el de mayor antigüedad.
        var tickets = Enumerable.Range(1, PoolDesdeDevOpsService.MaxAltasPorPasada + 1)
            .Select(i => Ticket(i, $"Trabajo {i}", diasDeAntiguedad: i))
            .ToArray();
        await ConTicketsAsync(db, tickets);

        var resultado = await Alta(db, Admin()).LlevarAlPoolAsync();

        Assert.Equal(PoolDesdeDevOpsService.MaxAltasPorPasada, resultado.Creadas);
        Assert.Equal(1, resultado.Fuera);
        Assert.Contains("quedaron fuera", resultado.Mensaje);

        // El que se quedó fuera es el MÁS NUEVO (un día de antigüedad), no el más viejo.
        var dentro = (await DelPoolAsync(db)).Select(a => a.DevOpsWorkItemId).ToHashSet();
        Assert.DoesNotContain(1, dentro);
        Assert.Contains(PoolDesdeDevOpsService.MaxAltasPorPasada + 1, dentro);

        // Y en la siguiente pasada entra el que faltaba.
        var segunda = await Alta(db, Admin()).LlevarAlPoolAsync();
        Assert.Equal(1, segunda.Creadas);
        Assert.Equal(0, segunda.Fuera);
    }

    // ── 5. Títulos y textos que podrían reventar el guardado ─────────────────────

    /// <summary>
    /// Un título largo de DevOps se recorta. El alta NO pasa por la validación del borrador —es ella
    /// la que exige el tipo y las horas que aquí todavía no hay— así que este recorte es lo único que
    /// hay entre un título de 300 caracteres y un INSERT que revienta contra SQL Server.
    /// </summary>
    [Fact]
    public async Task TituloLargo_seRecorta()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(1, new string('x', 300)));

        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.Equal(200, actividad.Title.Length);
    }

    // ── 6. Clasificar es publicar ────────────────────────────────────────────────

    /// <summary>
    /// El líder le pone tipo, complejidad, horas y EQUIPO, y con eso la actividad pasa a Disponible
    /// con los puntos de la matriz. Es el único gesto: clasificar y publicar son lo mismo.
    /// </summary>
    [Fact]
    public async Task Clasificar_publicaConLosPuntosDeLaMatrizYConSuEquipo()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));

        var equipo = new Team { Name = "Desarrollo Web" };
        db.Teams.Add(equipo);
        await db.SaveChangesAsync();

        var clasificada = new PoolActivity
        {
            Title       = actividad.Title,
            Description = actividad.Description,
            WorkType    = PoolWorkType.Tarea,
            Complexity  = PoolComplexity.Alta,
            Priority    = PoolPriority.Alta,
            HorasEstimadas = 6m,
            EquipoId    = equipo.Id,
            ExternalUrl = actividad.ExternalUrl,
            DevOpsWorkItemId = actividad.DevOpsWorkItemId
        };

        var (ok, mensaje) = await Pool(db, Admin()).EditarAsync(actividad.Id, clasificada);
        Assert.True(ok, mensaje);

        var publicada = (await DelPoolAsync(db)).Single();
        Assert.Equal(PoolActivityStatus.Disponible, publicada.Status);
        Assert.Equal(PoolWorkType.Tarea, publicada.WorkType);
        Assert.Equal(PoolComplexity.Alta, publicada.Complexity);
        Assert.Equal(10, publicada.Points);                 // Tarea/Alta según la matriz sembrada
        Assert.Equal(equipo.Id, publicada.EquipoId);
        Assert.Equal(6m, publicada.HorasEstimadas);
    }

    /// <summary>
    /// <b>REGRESIÓN de un fallo previo.</b> Editar nunca escribía el equipo: el contrato lo traía, el
    /// endpoint lo metía en el borrador y el servicio lo descartaba en silencio. Se notaba poco porque
    /// toda actividad nacía con su equipo puesto; con el alta automática, que nace sin ninguno, es la
    /// única ruta que hay para segmentarla.
    /// </summary>
    [Fact]
    public async Task Editar_escribeElEquipo_tambienEnUnaYaPublicada()
    {
        using var db = await BaseListaAsync();
        var equipo = new Team { Name = "Soporte" };
        db.Teams.Add(equipo);
        await db.SaveChangesAsync();

        var borrador = new PoolActivity
        {
            Title = "Publicada a mano", WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media, HorasEstimadas = 4m
        };
        var (creada, mensaje, actividad) = await Pool(db, Admin()).CrearAsync(borrador);
        Assert.True(creada, mensaje);
        Assert.Null(actividad!.EquipoId);

        var cambio = new PoolActivity
        {
            Title = "Publicada a mano", WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media, HorasEstimadas = 4m, EquipoId = equipo.Id
        };
        var (editada, porQue) = await Pool(db, Admin()).EditarAsync(actividad.Id, cambio);
        Assert.True(editada, porQue);

        Assert.Equal(equipo.Id, (await DelPoolAsync(db)).Single().EquipoId);
    }

    /// <summary>Descartar lo que entró solo es la única salida que tiene: liberar exige que esté en
    /// curso, así que sin esto se quedaría en la bandeja del líder para siempre.</summary>
    [Fact]
    public async Task SinClasificar_sePuedeDescartar()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        var (ok, mensaje) = await Pool(db, Admin()).RetirarAsync(actividad.Id);

        Assert.True(ok, mensaje);
        Assert.Contains("no volverá a entrar", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PoolActivityStatus.Retirada, (await DelPoolAsync(db)).Single().Status);
    }

    // ── 7. Nada sale hacia DevOps antes de tiempo ────────────────────────────────

    /// <summary>
    /// <b>Una actividad sin clasificar no tiene NADA pendiente de enviar a DevOps.</b>
    ///
    /// <para>Es la puerta que impide dos daños a la vez: que el empuje intente resolver credenciales
    /// —lo que pasa por la tabla de secretos, que exige sesión, y desde un trabajo de fondo revienta
    /// dentro de esa guarda dejando «no hay sesión» escrito en todas—; y que se le mande la prioridad
    /// a un ticket recién creado, bajándosela.</para>
    /// </summary>
    [Fact]
    public async Task SinClasificar_noTienePendienteNadaParaDevOps()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));

        Assert.True(actividad.LigadaADevOps);
        Assert.False(actividad.YaPublicada);
        Assert.False(actividad.PrioridadPendienteDeEnviar);
        Assert.False(actividad.EsfuerzoPendienteDeEnviar);
        Assert.False(actividad.PendienteDeEnviarADevOps);
    }

    /// <summary>Y al clasificarla se abre: lo que hasta entonces se calló, ahora sí hay que mandarlo.</summary>
    [Fact]
    public async Task AlClasificar_seAbreLoQueHabiaQueMandar()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));

        await Pool(db, Admin()).EditarAsync(actividad.Id, new PoolActivity
        {
            Title = actividad.Title, WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media, HorasEstimadas = 4m,
            DevOpsWorkItemId = actividad.DevOpsWorkItemId
        });

        var publicada = (await DelPoolAsync(db)).Single();
        Assert.True(publicada.YaPublicada);
        Assert.True(publicada.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// Mientras está sin clasificar OCUPA su work item: si no contara como viva, cada pasada crearía
    /// otra actividad sobre el mismo ticket.
    /// </summary>
    [Fact]
    public async Task SinClasificar_ocupaSuWorkItem()
    {
        using var db = await BaseListaAsync();
        await ConTicketsAsync(db, Ticket(4321));
        await Alta(db, Admin()).LlevarAlPoolAsync();

        var actividad = Assert.Single(await DelPoolAsync(db));
        Assert.True(actividad.SigueEnJuego);

        var (ok, error) = await PoolDevOpsService.NadieMasLoTieneAsync(db, 4321, poolActivityId: 0, default);
        Assert.False(ok);
        Assert.Contains("4321", error);
    }
}
