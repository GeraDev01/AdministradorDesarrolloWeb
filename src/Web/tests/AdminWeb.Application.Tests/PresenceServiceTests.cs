using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Presencia y registro de jornadas.
///
/// Lo delicado no es el indicador sino las HORAS: una jornada que se cierra mal le regala a alguien
/// tiempo que no trabajó, o se lo quita. Por eso casi todas estas pruebas miran la hora de cierre,
/// no el color del puntito.
/// </summary>
public class PresenceServiceTests
{
    private static PresenceService Svc(AppDbContext db, ICurrentUser cu) => new(db, cu, new OrigenDePrueba());

    /// <summary>
    /// El mismo servicio, pero sabiendo quién tiene un socket abierto ahora mismo. Hace falta solo
    /// para las cuentas que no registran jornada: para el resto, «conectado» se sigue deduciendo de
    /// la fila abierta y este doble no pinta nada.
    /// </summary>
    private static PresenceService Svc(AppDbContext db, ICurrentUser cu, IConexionesEnVivo enVivo) =>
        new(db, cu, new OrigenDePrueba(), enVivo);

    /// <summary>Los identificadores que se dan por conectados. Lo que no esté, no está.</summary>
    private sealed class ConexionesFalsas(params int[] conectados) : IConexionesEnVivo
    {
        public bool EstaConectado(int userId) => conectados.Contains(userId);
    }

    /// <summary>
    /// Da de alta la cuenta y devuelve su identidad de sesión. En el escritorio el contexto se
    /// rellenaba desde la fila de Users; aquí la identidad viene de los claims, así que la prueba
    /// la construye igual que lo haría la petición.
    /// </summary>
    private static ICurrentUser Usuario(AppDbContext db, int userId, string nombre, UserRole rol = UserRole.Desarrollador)
    {
        db.Users.Add(new User
        {
            Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre,
            Role = rol, IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();

        return new UsuarioDePrueba
        {
            UserId = userId, Username = nombre.ToLowerInvariant(), FullName = nombre, Role = rol
        };
    }

    /// <summary>Envejece la jornada abierta: simula que la aplicación lleva rato sin dar señales.</summary>
    private static void SinLatirDesdeHace(AppDbContext db, int userId, TimeSpan cuanto)
    {
        var p = db.WorkPresences.Single(x => x.UserId == userId && x.EndedAtUtc == null);
        p.LastSeenUtc = DateTime.UtcNow - cuanto;
        db.SaveChanges();
    }

    // ── Jornada ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Entrar_AbreLaJornada()
    {
        var db = TestDb.New();
        await Svc(db, Usuario(db, 1, "Ana")).EntrarAsync();

        var j = db.WorkPresences.Single();
        Assert.Equal(1, j.UserId);
        Assert.Equal("Ana", j.DisplayName);
        Assert.True(j.Abierta);
        Assert.Equal(PresenceState.Disponible, j.State);
    }

    [Fact]
    public async Task Salir_CierraLaJornadaConLaHoraDeAhora()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        await svc.SalirAsync();

        var j = db.WorkPresences.Single();
        Assert.False(j.Abierta);
        Assert.Equal(PresenceEnd.CierreNormal, j.EndReason);
        Assert.True(j.EndedAtUtc >= j.StartedAtUtc);
    }

    /// <summary>
    /// Entrar dos veces es UNA sola jornada, no dos.
    ///
    /// <para><b>Esta prueba cambió de expectativa a propósito.</b> Antes afirmaba que quedaban DOS
    /// filas —una cerrada y otra abierta— porque cada conexión abría una nueva. En el escritorio eso
    /// daba igual: había una instancia por sesión de Windows y «entrar dos veces» era raro. En un
    /// navegador es lo NORMAL —cada F5 y cada pestaña— y el registro del día acababa con ocho o diez
    /// jornadas de minutos donde hubo una de ocho horas.</para>
    ///
    /// <para>Lo que la prueba protegía —que no haya dos ABIERTAS a la vez, que harían contar doble
    /// al tablero— se sigue protegiendo, y de hecho mejor: ahora solo puede haber una en total.</para>
    /// </summary>
    [Fact]
    public async Task EntrarDosVeces_ReutilizaLaMismaJornada()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        var primera = await svc.EntrarAsync();
        var segunda = await svc.EntrarAsync();

        Assert.Equal(primera!.Id, segunda!.Id);
        Assert.Single(db.WorkPresences);
        Assert.Single(db.WorkPresences.Where(p => p.EndedAtUtc == null));
    }

    // ── El latido: el corazón de todo esto ───────────────────────────────────────

    [Fact]
    public async Task SinLatido_LaJornadaSeCierraSola()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        Assert.Equal(1, await svc.CerrarCaidasAsync());

        var j = db.WorkPresences.Single();
        Assert.False(j.Abierta);
        Assert.Equal(PresenceEnd.SinLatido, j.EndReason);
    }

    [Fact]
    public async Task UnaJornadaCaida_SeCierraConLaHoraDelUltimoLatido_NoConLaDeAhora()
    {
        // Lo importante de verdad: cerrarla con la hora actual le regalaría a alguien todas las
        // horas que su equipo pasó apagado.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();

        var ultimoLatido = DateTime.UtcNow - TimeSpan.FromHours(9);
        var p = db.WorkPresences.Single();
        p.StartedAtUtc = DateTime.UtcNow - TimeSpan.FromHours(10);
        p.LastSeenUtc = ultimoLatido;
        db.SaveChanges();

        await svc.CerrarCaidasAsync();

        var cerrada = db.WorkPresences.AsNoTracking().Single();
        Assert.Equal(ultimoLatido, cerrada.EndedAtUtc);
        // Una hora de jornada real, no diez.
        Assert.InRange(cerrada.Duracion.TotalHours, 0.9, 1.1);
    }

    [Fact]
    public async Task UnaPausaCorta_NoDaPorDesconectadoANadie()
    {
        // Bloquear la pantalla o que el equipo suspenda un momento no puede marcar a nadie ausente.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido - TimeSpan.FromMinutes(1));

        Assert.Equal(0, await svc.CerrarCaidasAsync());
        Assert.True(db.WorkPresences.Single().Abierta);
    }

    [Fact]
    public void LaToleranciaEsVariasVecesElIntervalo()
    {
        // Si la tolerancia fuera igual o menor que el intervalo, un solo latido perdido bastaría
        // para marcar a alguien como desconectado.
        Assert.True(PresenceService.ToleranciaSinLatido >= PresenceService.IntervaloLatido * 3);
    }

    [Fact]
    public async Task Latir_RefrescaLaMarcaDeVida()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, TimeSpan.FromMinutes(3));

        await svc.LatirAsync();

        var j = db.WorkPresences.AsNoTracking().Single();
        Assert.True(j.Abierta);
        Assert.True(DateTime.UtcNow - j.LastSeenUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Latir_TrasUnaCaida_AbreOtraJornada()
    {
        // Se cayó la red un rato largo y volvió: la persona sigue trabajando, así que su jornada
        // continúa aunque sea en un registro nuevo. OJO: sin barrer a mano antes — el barrido lo
        // hace el propio Latir(), que es lo que pasa en el equipo que se suspendió.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        await svc.LatirAsync();

        Assert.Equal(2, db.WorkPresences.Count());
        Assert.Single(db.WorkPresences.Where(p => p.EndedAtUtc == null));
    }

    [Fact]
    public async Task Latir_TrasUnaSuspensionLarga_NoRegalaLasHorasDormidas()
    {
        // El defecto que motivó poner el barrido ANTES del refresco: la PC durmió toda la noche
        // con la aplicación en la bandeja y nadie más tenía la app abierta para barrer. El primer
        // latido al despertar NO debe revivir la jornada de anoche.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();

        // Una hora real de trabajo, luego 9 horas de máquina dormida.
        var vieja = db.WorkPresences.Single();
        vieja.StartedAtUtc = DateTime.UtcNow.AddHours(-10);
        vieja.LastSeenUtc  = DateTime.UtcNow.AddHours(-9);
        db.SaveChanges();

        await svc.LatirAsync();

        var filas = db.WorkPresences.AsNoTracking().OrderBy(p => p.StartedAtUtc).ToList();
        Assert.Equal(2, filas.Count);

        // La vieja quedó sellada con su último latido real y marcada como caída.
        Assert.Equal(PresenceEnd.SinLatido, filas[0].EndReason);
        Assert.Equal(filas[0].LastSeenUtc, filas[0].EndedAtUtc);

        // Y el total del día es ~1 hora, no 10: el hueco no se contabiliza.
        var totalHoras = filas.Sum(p => p.Duracion.TotalHours);
        Assert.InRange(totalHoras, 0.9, 1.1);
    }

    [Fact]
    public async Task Latir_TrasUnaPausaCorta_NoParteLaJornada()
    {
        // Un parpadeo (menos que la tolerancia) no debe fragmentar el registro en dos filas.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido - TimeSpan.FromMinutes(1));

        await svc.LatirAsync();

        var j = db.WorkPresences.AsNoTracking().Single();
        Assert.True(j.Abierta);
        Assert.True(DateTime.UtcNow - j.LastSeenUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task JornadasDelDia_CierraLasCaidasAntesDeListar()
    {
        // Lunes por la mañana: nadie tuvo la aplicación abierta desde el viernes, así que ningún
        // latido barrió. El registro NO debe listar la jornada caída como «en curso».
        var db = TestDb.New();
        var svcAna = Svc(db, Usuario(db, 1, "Ana"));
        await svcAna.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var admin = Usuario(db, 2, "Jefa", UserRole.Admin);
        var jornadas = await Svc(db, admin).JornadasDelDiaAsync(DateTime.Today);

        var j = Assert.Single(jornadas, x => x.UserId == 1);
        Assert.NotNull(j.EndedAtUtc);
        Assert.Equal(PresenceEnd.SinLatido, j.EndReason);
        Assert.Equal(j.LastSeenUtc, j.EndedAtUtc);
    }

    // ── Mi jornada (cada quien la suya) ──────────────────────────────────────────

    [Fact]
    public async Task MisJornadas_SoloDevuelveLasPropias()
    {
        // Lo esencial: nadie ve la asistencia de otro. El método ni siquiera acepta un id, así
        // que no hay forma de pedir las ajenas — esta prueba fija esa garantía.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        await Svc(db, ana).EntrarAsync();
        await Svc(db, beto).EntrarAsync();

        var mias = await Svc(db, ana).MisJornadasAsync(DateTime.Today, DateTime.Today);

        Assert.Single(mias);
        Assert.Equal(2, db.WorkPresences.Count());   // la de Beto existe, pero no es de Ana
    }

    [Fact]
    public async Task MisJornadas_IncluyeElUltimoDiaDelRango()
    {
        // El error de una línea: sin AddDays(1) al convertir el «hasta», el último día del rango
        // se pierde entero y «esta semana» no incluiría hoy.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        await Svc(db, ana).EntrarAsync();

        Assert.Single(await Svc(db, ana).MisJornadasAsync(DateTime.Today.AddDays(-6), DateTime.Today));
    }

    [Fact]
    public async Task MisJornadas_FueraDelRango_NoAparecen()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        await Svc(db, ana).EntrarAsync();

        var ayer = DateTime.Today.AddDays(-1);
        Assert.Empty(await Svc(db, ana).MisJornadasAsync(ayer.AddDays(-5), ayer));
    }

    [Fact]
    public async Task MisJornadas_ElTotalCuadraConLaDuracionDeCadaUna()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var svc = Svc(db, ana);
        await svc.EntrarAsync();

        // Dos horas trabajadas, ya cerradas.
        var j = db.WorkPresences.Single();
        j.StartedAtUtc = DateTime.UtcNow.AddHours(-2);
        j.LastSeenUtc  = DateTime.UtcNow;
        db.SaveChanges();
        await svc.SalirAsync();

        // El rango cubre ayer: corriendo la prueba de madrugada, «hace 2 horas» es el día anterior.
        var total = TimeSpan.FromTicks(
            (await svc.MisJornadasAsync(DateTime.Today.AddDays(-1), DateTime.Today)).Sum(x => x.Duracion.Ticks));
        Assert.InRange(total.TotalHours, 1.9, 2.1);
    }

    [Fact]
    public async Task MisJornadas_MarcaLaCaidaComoSinLatido()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var svc = Svc(db, ana);
        await svc.EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));
        await svc.CerrarCaidasAsync();

        var j = Assert.Single(await svc.MisJornadasAsync(DateTime.Today, DateTime.Today));
        Assert.Equal(PresenceEnd.SinLatido, j.Cierre);
    }

    [Fact]
    public async Task MisJornadas_NoBarreJornadasAjenas()
    {
        // Consultar el registro propio no puede convertirse en escritura sobre filas de terceros:
        // CerrarCaidas cierra las de TODOS y un fallo de red volvería la lectura un error.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        await Svc(db, beto).EntrarAsync();
        SinLatirDesdeHace(db, 2, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        await Svc(db, ana).MisJornadasAsync(DateTime.Today, DateTime.Today);

        Assert.Null(db.WorkPresences.AsNoTracking().Single(p => p.UserId == 2).EndedAtUtc);
    }

    [Fact]
    public async Task MisJornadas_SinSesion_NoDevuelveNada()
    {
        var db = TestDb.New();
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Anonimo()).MisJornadasAsync(DateTime.Today, DateTime.Today));
    }

    // ── Estados ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarEstado_SeGuardaConSuNota()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();

        var (ok, _) = await svc.CambiarEstadoAsync(PresenceState.Comiendo, "vuelvo 15:30");

        Assert.True(ok);
        var j = db.WorkPresences.Single();
        Assert.Equal(PresenceState.Comiendo, j.State);
        Assert.Equal("vuelvo 15:30", j.StateNote);
    }

    [Fact]
    public async Task ElEstadoSeSobrescribe_NoSeHistoriza()
    {
        // Decisión deliberada: un registro minutado de las pausas de alguien es vigilancia, no
        // asistencia. Lo que queda guardado es la jornada, no cuánto estuvo en el baño.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();

        await svc.CambiarEstadoAsync(PresenceState.Descanso);
        await svc.CambiarEstadoAsync(PresenceState.Comiendo);
        await svc.CambiarEstadoAsync(PresenceState.Disponible);

        Assert.Single(db.WorkPresences);
        Assert.Equal(PresenceState.Disponible, db.WorkPresences.Single().State);
    }

    [Fact]
    public async Task CambiarEstado_SinJornadaAbierta_LaAbre()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        var (ok, _) = await svc.CambiarEstadoAsync(PresenceState.EnReunion);

        Assert.True(ok);
        Assert.Equal(PresenceState.EnReunion, db.WorkPresences.Single().State);
    }

    [Fact]
    public async Task MiEstado_DevuelveLoQueSeMarco()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        await svc.EntrarAsync();
        await svc.CambiarEstadoAsync(PresenceState.Ocupado, "en una entrega");

        var (estado, nota) = await svc.MiEstadoAsync();

        Assert.Equal(PresenceState.Ocupado, estado);
        Assert.Equal("en una entrega", nota);
    }

    // ── Operaciones: sin jornada, sin estado, y aun así visible ──────────────────
    //
    // El área de operaciones despliega; no registra asistencia. Lo que estas pruebas fijan es que las
    // dos mitades de esa decisión se cumplan a la vez, porque cada una sin la otra da el resultado
    // contrario al que se pidió: si se deja de abrir la jornada y nadie más lo sabe, el operativo sale
    // «Desconectado» aunque esté desplegando; y si se le regala un «Disponible» fijo, el tablero
    // miente sobre quien se fue a su casa.

    [Fact]
    public async Task Operaciones_AlConectarse_NoSeLeAbreJornada()
    {
        var db = TestDb.New();
        var ops = Usuario(db, 3, "Ops", UserRole.Operaciones);

        // Devuelve null y no lanza: conectarse es lo normal, no un fallo que anotar en el registro.
        Assert.Null(await Svc(db, ops).EntrarAsync());
        Assert.Empty(db.WorkPresences.AsNoTracking().Where(p => p.UserId == 3));
    }

    [Fact]
    public async Task Operaciones_ElLatido_NoLeAbreJornada()
    {
        // El camino que se escapa si alguien pone la guarda en el hub en lugar de en EntrarAsync: la
        // conexión no abriría fila, pero el primer latido —a los dos minutos— sí, y el defecto no se
        // vería hasta mirar el registro de jornadas de un mes después.
        var db = TestDb.New();
        var ops = Usuario(db, 3, "Ops", UserRole.Operaciones);

        await Svc(db, ops).LatirAsync();

        Assert.Empty(db.WorkPresences.AsNoTracking().Where(p => p.UserId == 3));
    }

    [Fact]
    public async Task Operaciones_ConUnaJornadaYaAbierta_NoSeLeBorraYSeCierraNormal()
    {
        // El caso real del día que esto se despliegue: un operativo con su jornada de la mañana ya
        // abierta. Dejar de registrar de aquí en adelante NO puede borrar lo de atrás, y esa fila
        // tiene que poder cerrarse limpiamente — si SalirAsync llevara la misma guarda que EntrarAsync,
        // se quedaría abierta para siempre y el registro del líder nunca volvería a cuadrar.
        var db = TestDb.New();
        var ops = Usuario(db, 3, "Ops", UserRole.Operaciones);

        db.WorkPresences.Add(new WorkPresence
        {
            UserId       = 3,
            DisplayName  = "Ops",
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            LastSeenUtc  = DateTime.UtcNow,
            State        = PresenceState.Disponible,
            Origin       = "prueba"
        });
        db.SaveChanges();

        await Svc(db, ops).SalirAsync();

        var suya = db.WorkPresences.AsNoTracking().Single(p => p.UserId == 3);
        Assert.NotNull(suya.EndedAtUtc);
        Assert.Equal(PresenceEnd.CierreNormal, suya.EndReason);
    }

    [Fact]
    public async Task Operaciones_NoPuedeCambiarSuEstado()
    {
        var db = TestDb.New();
        var ops = Usuario(db, 3, "Ops", UserRole.Operaciones);

        var (ok, mensaje) = await Svc(db, ops).CambiarEstadoAsync(PresenceState.Ocupado);

        Assert.False(ok);
        Assert.Contains("Disponible", mensaje);
        // Y de paso: el rechazo no puede colarse por la puerta de atrás abriendo la jornada que
        // EntrarAsync se negó a abrir. Cambiar el estado la abre cuando no hay ninguna.
        Assert.Empty(db.WorkPresences.AsNoTracking().Where(p => p.UserId == 3));
    }

    [Fact]
    public async Task Operaciones_NoLeeSuRegistroDeJornadas()
    {
        // La telemetría propia es lo que alimenta «Mi jornada». Si el resto del módulo contesta 403 y
        // esta no, queda un hueco por el que se sigue leyendo el registro.
        var db = TestDb.New();
        var ops = Usuario(db, 3, "Ops", UserRole.Operaciones);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, ops).MisJornadasAsync(DateTime.Today, DateTime.Today));
    }

    [Fact]
    public async Task ElTablero_MuestraDisponibleAlOperativoConectado()
    {
        // La otra mitad de la petición: «siempre debe estar disponible el estatus». Sin fila que
        // mirar, quien sabe si está es el socket abierto.
        var db = TestDb.New();
        Usuario(db, 3, "Ops", UserRole.Operaciones);
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);

        var fila = (await Svc(db, admin, new ConexionesFalsas(3)).TableroAsync()).Single(f => f.Nombre == "Ops");

        Assert.True(fila.Conectado);
        Assert.Equal(PresenceState.Disponible, fila.Estado);
        Assert.False(fila.RegistraJornada);
        // Sin jornada no hay «desde cuándo»: poner la hora de ahora fingiría que acaba de llegar cada
        // vez que alguien abre el tablero.
        Assert.Null(fila.DesdeUtc);
    }

    [Fact]
    public async Task ElTablero_MuestraDesconectadoAlOperativoSinConexion()
    {
        // Lo que NO se puede hacer: un «Disponible» fijo. Un operativo que se fue a su casa tiene que
        // verse como lo que es, o el tablero deja de servir para lo único que sirve.
        var db = TestDb.New();
        Usuario(db, 3, "Ops", UserRole.Operaciones);
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);

        var fila = (await Svc(db, admin, new ConexionesFalsas()).TableroAsync()).Single(f => f.Nombre == "Ops");

        Assert.False(fila.Conectado);
        Assert.Equal(PresenceState.Ausente, fila.Estado);

        // Y si el contrato de sockets no está enchufado, el resultado es el mismo: se pierde
        // información, no se inventa. Nunca «Disponible» por no saber.
        var sinContrato = (await Svc(db, admin).TableroAsync()).Single(f => f.Nombre == "Ops");
        Assert.False(sinContrato.Conectado);
        Assert.Equal(PresenceState.Ausente, sinContrato.Estado);
    }

    [Fact]
    public async Task ElTablero_AQuienSiRegistraJornada_LoSigueMirandoEnSuFila()
    {
        // La guarda de que el atajo anterior no se llevó por delante el camino normal: un
        // desarrollador conectado sigue saliendo por su jornada abierta, con su estado y su «desde»,
        // aunque no tenga ningún socket apuntado.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        await Svc(db, ana).EntrarAsync();
        await Svc(db, ana).CambiarEstadoAsync(PresenceState.EnReunion, "vuelvo 15:30");

        var fila = (await Svc(db, admin, new ConexionesFalsas()).TableroAsync()).Single(f => f.Nombre == "Ana");

        Assert.True(fila.Conectado);
        Assert.Equal(PresenceState.EnReunion, fila.Estado);
        Assert.Equal("vuelvo 15:30", fila.Nota);
        Assert.NotNull(fila.DesdeUtc);
        Assert.True(fila.RegistraJornada);
    }

    // ── Tablero ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tablero_EsSoloDelAdministrador()
    {
        var db = TestDb.New();
        var dev = Svc(db, Usuario(db, 1, "Ana"));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.TableroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.JornadasDelDiaAsync(DateTime.Today));
    }

    [Fact]
    public async Task Tablero_IncluyeAQuienNuncaHaEntrado()
    {
        // Que alguien falte es justamente el dato; esconderlo haría inútil el tablero.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);

        await Svc(db, ana).EntrarAsync();

        var filas = await Svc(db, admin).TableroAsync();

        Assert.Equal(3, filas.Count);
        Assert.True(filas.Single(f => f.Nombre == "Ana").Conectado);
        Assert.False(filas.Single(f => f.Nombre == "Beto").Conectado);
    }

    [Fact]
    public async Task Tablero_MuestraLosConectadosPrimero()
    {
        var db = TestDb.New();
        Usuario(db, 1, "Zulema");
        var beto = Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        await Svc(db, beto).EntrarAsync();

        var filas = await Svc(db, admin).TableroAsync();

        Assert.Equal("Beto", filas[0].Nombre);
        Assert.True(filas[0].Conectado);
    }

    [Fact]
    public async Task Tablero_QuienDejoDeLatir_SaleDesconectado()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        await Svc(db, ana).EntrarAsync();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var fila = (await Svc(db, admin).TableroAsync()).Single(f => f.Nombre == "Ana");

        Assert.False(fila.Conectado);
        Assert.Equal(PresenceState.Ausente, fila.Estado);
    }

    [Fact]
    public async Task Tablero_NoDelataLaNotaDeQuienNoEstaConectado()
    {
        // «Comiendo — vuelvo 15:30» de hace tres días no informa de nada; solo confunde.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        var svc = Svc(db, ana);
        await svc.EntrarAsync();
        await svc.CambiarEstadoAsync(PresenceState.Comiendo, "vuelvo 15:30");
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var fila = (await Svc(db, admin).TableroAsync()).Single(f => f.Nombre == "Ana");

        Assert.Null(fila.Nota);
    }

    // ── Registro de jornadas ─────────────────────────────────────────────────────

    [Fact]
    public async Task JornadasDelDia_TraeLasDeEseDia()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        var svc = Svc(db, ana);
        await svc.EntrarAsync();
        await svc.SalirAsync();

        Assert.Single(await Svc(db, admin).JornadasDelDiaAsync(DateTime.Today));
        Assert.Empty(await Svc(db, admin).JornadasDelDiaAsync(DateTime.Today.AddDays(-3)));
    }

    [Fact]
    public async Task JornadasDelDia_SePuedeAcotarAUnaPersona()
    {
        var db = TestDb.New();
        var ana  = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        await Svc(db, ana).EntrarAsync();
        await Svc(db, beto).EntrarAsync();

        Assert.Equal(2, (await Svc(db, admin).JornadasDelDiaAsync(DateTime.Today)).Count);
        Assert.Single(await Svc(db, admin).JornadasDelDiaAsync(DateTime.Today, userId: 1));
    }

    [Fact]
    public void Duracion_SeLeeEnHorasYMinutos()
    {
        Assert.Equal("menos de 1 min", PresenceService.Duracion(TimeSpan.FromSeconds(30)));
        Assert.Equal("45 min", PresenceService.Duracion(TimeSpan.FromMinutes(45)));
        Assert.Equal("8 h 15 min", PresenceService.Duracion(TimeSpan.FromMinutes(495)));
    }
}
