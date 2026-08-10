using System.Text.Json;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Freshdesk;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Un Freshdesk de mentira: devuelve lo que la prueba le ponga y no sale a la red.
///
/// Es el motivo de que la integración esté detrás de <see cref="IApiDeFreshdesk"/>. Las reglas que
/// de verdad importan —qué se guarda, y sobre todo qué se BORRA— se pueden así probar enteras sin
/// depender de una cuenta real ni de que un ticket siga dentro de la ventana de 30 días.
/// </summary>
internal sealed class ApiDeFreshdeskFalsa : IApiDeFreshdesk
{
    public List<TicketDeFreshdeskCrudo> Tickets { get; set; } = [];
    public long? MiAgente { get; set; }

    /// <summary>Vacíos = lo que se recibe cuando /agents y /groups responden 403 (clave de agente).</summary>
    public Dictionary<long, string> Agentes { get; set; } = [];
    public Dictionary<long, string> Grupos { get; set; } = [];

    public Exception? Fallo { get; set; }

    public Task<PaginaDeTicketsDeFreshdesk> TicketsAsync(
        OpcionesDeFreshdesk opciones, int pagina, CancellationToken ct = default)
    {
        if (Fallo != null) throw Fallo;
        IReadOnlyList<TicketDeFreshdeskCrudo> lote = pagina == 1 ? Tickets : [];
        return Task.FromResult(new PaginaDeTicketsDeFreshdesk(lote, HayMas: false));
    }

    public Task<long?> MiAgenteIdAsync(OpcionesDeFreshdesk opciones, CancellationToken ct = default) =>
        Task.FromResult(MiAgente);

    public Task<IReadOnlyDictionary<long, string>> AgentesAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, string>>(Agentes);

    public Task<IReadOnlyDictionary<long, string>> GruposAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, string>>(Grupos);
}

/// <summary>
/// La sincronización de Freshdesk.
///
/// <b>La prueba que manda es la primera</b>: /tickets solo devuelve los de los últimos ~30 días, así
/// que un ticket que no viene en la respuesta NO es un ticket borrado. Si esto se rompiera, cada
/// sincronización se llevaría por delante todo el histórico anterior al mes en curso y nadie se
/// enteraría hasta necesitarlo.
/// </summary>
public class SincronizacionDeFreshdeskTests
{
    private const string LaClave = "clave-secretisima-de-freshdesk";
    private static readonly DateTime Base = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TicketDeFreshdeskCrudo Ticket(
        long numero, long? agente = null, long? grupo = null, int estado = 2, string? asunto = null) =>
        new(numero, asunto ?? $"Ticket {numero}", estado, 2, null, 1, agente, grupo,
            "Cliente Uno", "cliente@ejemplo.com", "", null, Base, Base);

    private static async Task<(TicketsDeFreshdeskService servicio, AppDbContext db, ApiDeFreshdeskFalsa api)> Armar(
        bool porAgente = false, string grupo = "", bool encendida = true, UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();
        var usuario = UsuarioDePrueba.Como(rol);
        var bitacora = new AuditService(db, usuario, new OrigenDePrueba());

        // La configuración se escribe SIEMPRE como administrador: guardarla es cosa suya, y el rol de
        // la prueba solo cambia quién sincroniza después.
        var configuracion = new SettingsService(db, UsuarioDePrueba.Como(UserRole.Admin), bitacora);
        await configuracion.GuardarAsync(SettingsService.Claves.FreshDeskEnabled, encendida ? "true" : "false");
        await configuracion.GuardarAsync(SettingsService.Claves.FreshDeskDomain, "miempresa");
        await configuracion.GuardarAsync(SettingsService.Claves.FreshDeskApiKey, LaClave);
        await configuracion.GuardarAsync(SettingsService.Claves.FreshDeskFilterMine, porAgente ? "true" : "false");
        await configuracion.GuardarAsync(SettingsService.Claves.FreshDeskGroup, grupo);

        var api = new ApiDeFreshdeskFalsa();
        var propia = new SettingsService(db, usuario, bitacora);
        return (new TicketsDeFreshdeskService(db, usuario, bitacora, propia, api), db, api);
    }

    private static FreshDeskTicket Guardado(long numero, long? agente = null, int estado = 2) => new()
    {
        ExternalId = numero, Subject = $"Ticket {numero}", Status = estado, Priority = 2,
        ResponderId = agente, SyncedAt = Base
    };

    // ── La regla de oro: nunca borrar por ausencia ───────────────────────────────

    [Fact]
    public async Task NoBorraLosTicketsQueNoVinieronEnLaRespuesta()
    {
        // El 100 es de hace tres meses: /tickets no lo devuelve porque solo trae ~30 días. Eso NO
        // significa que se haya borrado en Freshdesk.
        var (servicio, db, api) = await Armar(porAgente: true);
        db.FreshDeskTickets.AddRange(Guardado(100, agente: 7), Guardado(200, agente: 7));
        await db.SaveChangesAsync();

        api.MiAgente = 7;
        api.Tickets = [Ticket(200, agente: 7)];

        var (ok, mensaje, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok, mensaje);
        Assert.Equal(0, resultado!.Quitados);
        Assert.NotNull(await db.FreshDeskTickets.FirstOrDefaultAsync(t => t.ExternalId == 100));
    }

    [Fact]
    public async Task QuitaSoloLoQueVioYConfirmoQueYaNoCumpleElFiltro()
    {
        var (servicio, db, api) = await Armar(porAgente: true);
        db.FreshDeskTickets.AddRange(Guardado(100, agente: 7), Guardado(300, agente: 7));
        await db.SaveChangesAsync();

        api.MiAgente = 7;
        // El 300 vino y ahora lo tiene otra persona; el 100 ni siquiera vino.
        api.Tickets = [Ticket(300, agente: 99)];

        var (ok, _, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok);
        Assert.Equal(1, resultado!.Quitados);
        Assert.Null(await db.FreshDeskTickets.FirstOrDefaultAsync(t => t.ExternalId == 300));
        Assert.NotNull(await db.FreshDeskTickets.FirstOrDefaultAsync(t => t.ExternalId == 100));
    }

    [Fact]
    public async Task NoQuitaUnTicketQueAlguienVinculoAMano()
    {
        var (servicio, db, api) = await Armar(porAgente: true);
        var ticket = Guardado(300, agente: 7);
        var workItem = new DevOpsTicket { ExternalId = 4321, Title = "Redondeo", WorkItemType = "Bug", State = "Active" };
        db.FreshDeskTickets.Add(ticket);
        db.DevOpsTickets.Add(workItem);
        await db.SaveChangesAsync();
        db.TicketLinks.Add(new TicketLink { DevOpsTicketId = workItem.Id, FreshDeskTicketId = ticket.Id });
        await db.SaveChangesAsync();

        api.MiAgente = 7;
        api.Tickets = [Ticket(300, agente: 99)];   // ya no es mío, pero está vinculado

        var (ok, _, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok);
        Assert.Equal(0, resultado!.Quitados);
        Assert.NotNull(await db.FreshDeskTickets.FirstOrDefaultAsync(t => t.ExternalId == 300));
    }

    [Fact]
    public async Task SiNoSePudoIdentificarMiAgente_NoQuitaNada_YLoAvisa()
    {
        // Con el filtro a medio resolver, «ya no cumple» sería una conclusión sacada de datos
        // incompletos: se quitarían tickets que sí eran míos.
        var (servicio, db, api) = await Armar(porAgente: true);
        db.FreshDeskTickets.Add(Guardado(300, agente: 7));
        await db.SaveChangesAsync();

        api.MiAgente = null;                        // /agents/me no respondió
        api.Tickets = [Ticket(300, agente: 7)];

        var (ok, _, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok);
        Assert.Equal(0, resultado!.Quitados);
        Assert.NotNull(await db.FreshDeskTickets.FirstOrDefaultAsync(t => t.ExternalId == 300));
        Assert.Contains("agente", resultado.Aviso);
    }

    // ── /agents y /groups en 403: «no disponible», no «clave mal» ────────────────

    [Fact]
    public async Task ConLosCatalogosEn403_SincronizaIgual_ConLosNombresEnBlanco()
    {
        // Una clave de AGENTE no puede listar /agents ni /groups. Eso deja los nombres vacíos, no
        // rompe la sincronización: los ids —que es lo que el filtro necesita— sí se guardan.
        var (servicio, db, api) = await Armar(porAgente: true);
        api.MiAgente = 7;
        api.Agentes = [];   // 403
        api.Grupos = [];    // 403
        api.Tickets = [Ticket(500, agente: 7)];

        var (ok, mensaje, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok, mensaje);
        Assert.Equal(1, resultado!.Nuevos);
        var guardado = await db.FreshDeskTickets.SingleAsync();
        Assert.Equal(7, guardado.ResponderId);
        Assert.Equal("", guardado.AgentName);
        Assert.Null(resultado.Aviso);   // el filtro por agente sí se pudo aplicar
    }

    [Fact]
    public async Task ConLosGruposEn403_ElGrupoEscritoComoIdNumericoSigueFuncionando()
    {
        var (servicio, db, api) = await Armar(grupo: "12");
        api.Grupos = [];   // 403: no se puede resolver ningún nombre
        api.Tickets = [Ticket(600, grupo: 12), Ticket(601, grupo: 99)];

        var (ok, _, resultado) = await servicio.SincronizarAsync();

        Assert.True(ok);
        Assert.Null(resultado!.Aviso);
        Assert.Equal(600, (await db.FreshDeskTickets.SingleAsync()).ExternalId);
    }

    // ── La clave de API no sale del servidor ─────────────────────────────────────

    [Fact]
    public async Task LaPantallaNoLlevaLaClaveDeApi()
    {
        var (servicio, _, _) = await Armar();

        var pantalla = await servicio.PantallaAsync();

        Assert.DoesNotContain(LaClave, JsonSerializer.Serialize(pantalla));
        Assert.True(pantalla.Habilitada);
        Assert.Null(pantalla.MotivoDeNoDisponible);
    }

    // ── La pantalla ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaBusquedaSeResuelveEnLaBase_PorTextoYPorNumeroDeTicket()
    {
        // La búsqueda viaja a la base (la tabla no puede mandarse entera al navegador), así que hay
        // que comprobar que se traduce de verdad y no revienta al ejecutarse.
        var (servicio, _, api) = await Armar();
        api.Tickets = [Ticket(700, asunto: "No puedo timbrar"), Ticket(701, asunto: "Otra cosa")];
        await servicio.SincronizarAsync();

        Assert.Single((await servicio.PantallaAsync(buscar: "timbrar")).Tickets);
        Assert.Single((await servicio.PantallaAsync(buscar: "701")).Tickets);
        Assert.Empty((await servicio.PantallaAsync(buscar: "no existe nada así")).Tickets);

        var pantalla = await servicio.PantallaAsync(estado: 2);
        Assert.Equal(2, pantalla.Tickets.Count);
        Assert.Equal(2, pantalla.Resumen.Abiertos);
        Assert.Equal(2, pantalla.Coincidencias);
        Assert.False(pantalla.Truncada);
        Assert.Empty((await servicio.PantallaAsync(prioridad: 4)).Tickets);
    }

    [Fact]
    public async Task CuandoFreshdeskFalla_ElMensajeExplicaYNoLlevaLaClave()
    {
        var (servicio, _, api) = await Armar();
        api.Fallo = new ErrorDeFreshdeskException(
            "Freshdesk no aceptó la clave de API. Vuelve a capturarla en Configuración.");

        var (ok, mensaje, resultado) = await servicio.SincronizarAsync();

        Assert.False(ok);
        Assert.Null(resultado);
        Assert.Contains("Configuración", mensaje);
        Assert.DoesNotContain(LaClave, mensaje);
    }

    [Fact]
    public async Task SinConfigurar_DiceQueFaltaYNoRevienta()
    {
        var (servicio, _, _) = await Armar(encendida: false);

        var (ok, mensaje, _) = await servicio.SincronizarAsync();

        Assert.False(ok);
        Assert.Contains("apagada", mensaje);
    }

    // ── Autorización ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SincronizarEsDelLider()
    {
        var (servicio, _, _) = await Armar(rol: UserRole.Desarrollador);

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.SincronizarAsync());
    }

    // ── Avisos de «te asignaron este ticket» ─────────────────────────────────────

    [Fact]
    public async Task LaPrimeraSincronizacionNoAvisaDelBacklog_LaSiguienteSiDeLoNuevo()
    {
        var (servicio, db, api) = await Armar(porAgente: true);
        api.MiAgente = 7;
        api.Tickets = [Ticket(100, agente: 7), Ticket(101, agente: 7)];

        await servicio.SincronizarAsync();
        Assert.Empty(await db.Notifications.ToListAsync());   // línea base: nada que avisar

        api.Tickets = [Ticket(100, agente: 7), Ticket(101, agente: 7), Ticket(102, agente: 7, asunto: "Recién asignado")];
        await servicio.SincronizarAsync();

        var aviso = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(NotificationKind.FreshDeskAssigned, aviso.Kind);
        Assert.Contains("#102", aviso.Title);
    }

    // ── Cálculo puro ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7L, null, 7L, null, true)]     // asignado a mí
    [InlineData(99L, null, 7L, null, false)]   // asignado a otro
    [InlineData(99L, 12L, 7L, 12L, true)]      // no es mío, pero es del grupo del filtro
    [InlineData(null, null, 7L, 12L, false)]   // sin agente ni grupo
    public void EsMio_CombinaAgenteYGrupo(
        long? agente, long? grupo, long? miAgente, long? grupoDelFiltro, bool esperado) =>
        Assert.Equal(esperado, TicketsDeFreshdeskService.EsMio(agente, grupo, miAgente, grupoDelFiltro));

    [Fact]
    public void ResolverGrupoId_ElNombreTienePrioridadSobreElNumero()
    {
        // Un grupo llamado «2024» tiene que resolverse por su nombre, no interpretarse como el id 2024.
        var grupos = new Dictionary<long, string> { [55] = "2024", [12] = "Soporte" };

        Assert.Equal(55, TicketsDeFreshdeskService.ResolverGrupoId("2024", grupos));
        Assert.Equal(12, TicketsDeFreshdeskService.ResolverGrupoId("soporte", grupos));
    }

    [Fact]
    public void ResolverGrupoId_ConElCatalogoDisponible_UnIdInexistenteNoSeAcepta()
    {
        // Filtrar por un id fantasma no traería nada y, con el filtro «completo», habría dado permiso
        // para quitar tickets que sí eran del grupo bueno.
        var grupos = new Dictionary<long, string> { [12] = "Soporte" };

        Assert.Null(TicketsDeFreshdeskService.ResolverGrupoId("999", grupos));
        Assert.Equal(12, TicketsDeFreshdeskService.ResolverGrupoId("12", grupos));
    }

    [Fact]
    public void ResolverGrupoId_SinCatalogo_SeConfiaEnElIdQueCapturoElLider()
    {
        // Mapa vacío = /groups respondió 403. Escribir el id numérico es la única salida que le queda
        // a quien tiene una clave de agente, así que no se le puede desconfiar de ella.
        Assert.Equal(999, TicketsDeFreshdeskService.ResolverGrupoId("999", new Dictionary<long, string>()));
        Assert.Null(TicketsDeFreshdeskService.ResolverGrupoId("Soporte", new Dictionary<long, string>()));
        Assert.Null(TicketsDeFreshdeskService.ResolverGrupoId("   ", new Dictionary<long, string>()));
    }

    [Fact]
    public void Aviso_SoloHablaCuandoElFiltroNoSePudoAplicarDelTodo()
    {
        var sinFiltro = new FiltroDeSincronizacionDto(false, "");
        var porAgente = new FiltroDeSincronizacionDto(true, "");
        var porGrupo = new FiltroDeSincronizacionDto(false, "Soporte");

        Assert.Null(TicketsDeFreshdeskService.Aviso(sinFiltro, null, false, null));
        Assert.Null(TicketsDeFreshdeskService.Aviso(porAgente, 7, false, null));
        Assert.Contains("agente", TicketsDeFreshdeskService.Aviso(porAgente, null, false, null));
        Assert.Contains("Soporte", TicketsDeFreshdeskService.Aviso(porGrupo, null, true, null));
    }
}
