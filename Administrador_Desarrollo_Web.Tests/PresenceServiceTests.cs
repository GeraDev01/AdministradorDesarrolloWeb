using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Presencia y registro de jornadas.
///
/// Lo delicado no es el indicador sino las HORAS: una jornada que se cierra mal le regala a alguien
/// tiempo que no trabajó, o se lo quita. Por eso casi todas estas pruebas miran la hora de cierre,
/// no el color del puntito.
/// </summary>
public class PresenceServiceTests
{
    private static PresenceService Svc(AppDbContext db, CurrentUserContext cu) => new(db, cu);

    private static CurrentUserContext Usuario(AppDbContext db, int userId, string nombre, UserRole rol = UserRole.Desarrollador)
    {
        db.Users.Add(new User
        {
            Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre,
            Role = rol, IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();

        var cu = new CurrentUserContext();
        cu.SetUser(db.Users.AsNoTracking().Single(u => u.Id == userId));
        return cu;
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
    public void Entrar_AbreLaJornada()
    {
        var db = TestDb.New();
        Svc(db, Usuario(db, 1, "Ana")).Entrar();

        var j = db.WorkPresences.Single();
        Assert.Equal(1, j.UserId);
        Assert.Equal("Ana", j.DisplayName);
        Assert.True(j.Abierta);
        Assert.Equal(PresenceState.Disponible, j.State);
    }

    [Fact]
    public void Salir_CierraLaJornadaConLaHoraDeAhora()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        svc.Salir();

        var j = db.WorkPresences.Single();
        Assert.False(j.Abierta);
        Assert.Equal(PresenceEnd.CierreNormal, j.EndReason);
        Assert.True(j.EndedAtUtc >= j.StartedAtUtc);
    }

    [Fact]
    public void EntrarDosVeces_NoDejaDosJornadasAbiertas()
    {
        // Si su equipo anterior se colgó, al volver a entrar habría dos abiertas y el tablero
        // contaría doble.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        svc.Entrar();

        Assert.Equal(2, db.WorkPresences.Count());
        Assert.Single(db.WorkPresences.Where(p => p.EndedAtUtc == null));
    }

    // ── El latido: el corazón de todo esto ───────────────────────────────────────

    [Fact]
    public void SinLatido_LaJornadaSeCierraSola()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        Assert.Equal(1, svc.CerrarCaidas());

        var j = db.WorkPresences.Single();
        Assert.False(j.Abierta);
        Assert.Equal(PresenceEnd.SinLatido, j.EndReason);
    }

    [Fact]
    public void UnaJornadaCaida_SeCierraConLaHoraDelUltimoLatido_NoConLaDeAhora()
    {
        // Lo importante de verdad: cerrarla con la hora actual le regalaría a alguien todas las
        // horas que su equipo pasó apagado.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();

        var ultimoLatido = DateTime.UtcNow - TimeSpan.FromHours(9);
        var p = db.WorkPresences.Single();
        p.StartedAtUtc = DateTime.UtcNow - TimeSpan.FromHours(10);
        p.LastSeenUtc = ultimoLatido;
        db.SaveChanges();

        svc.CerrarCaidas();

        var cerrada = db.WorkPresences.AsNoTracking().Single();
        Assert.Equal(ultimoLatido, cerrada.EndedAtUtc);
        // Una hora de jornada real, no diez.
        Assert.InRange(cerrada.Duracion.TotalHours, 0.9, 1.1);
    }

    [Fact]
    public void UnaPausaCorta_NoDaPorDesconectadoANadie()
    {
        // Bloquear la pantalla o que el equipo suspenda un momento no puede marcar a nadie ausente.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido - TimeSpan.FromMinutes(1));

        Assert.Equal(0, svc.CerrarCaidas());
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
    public void Latir_RefrescaLaMarcaDeVida()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        SinLatirDesdeHace(db, 1, TimeSpan.FromMinutes(3));

        svc.Latir();

        var j = db.WorkPresences.AsNoTracking().Single();
        Assert.True(j.Abierta);
        Assert.True(DateTime.UtcNow - j.LastSeenUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Latir_TrasUnaCaida_AbreOtraJornada()
    {
        // Se cayó la red un rato largo y volvió: la persona sigue trabajando, así que su jornada
        // continúa aunque sea en un registro nuevo. OJO: sin barrer a mano antes — el barrido lo
        // hace el propio Latir(), que es lo que pasa en el equipo que se suspendió.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        svc.Latir();

        Assert.Equal(2, db.WorkPresences.Count());
        Assert.Single(db.WorkPresences.Where(p => p.EndedAtUtc == null));
    }

    [Fact]
    public void Latir_TrasUnaSuspensionLarga_NoRegalaLasHorasDormidas()
    {
        // El defecto que motivó poner el barrido ANTES del refresco: la PC durmió toda la noche
        // con la aplicación en la bandeja y nadie más tenía la app abierta para barrer. El primer
        // latido al despertar NO debe revivir la jornada de anoche.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();

        // Una hora real de trabajo, luego 9 horas de máquina dormida.
        var vieja = db.WorkPresences.Single();
        vieja.StartedAtUtc = DateTime.UtcNow.AddHours(-10);
        vieja.LastSeenUtc  = DateTime.UtcNow.AddHours(-9);
        db.SaveChanges();

        svc.Latir();

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
    public void Latir_TrasUnaPausaCorta_NoParteLaJornada()
    {
        // Un parpadeo (menos que la tolerancia) no debe fragmentar el registro en dos filas.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido - TimeSpan.FromMinutes(1));

        svc.Latir();

        var j = db.WorkPresences.AsNoTracking().Single();
        Assert.True(j.Abierta);
        Assert.True(DateTime.UtcNow - j.LastSeenUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void JornadasDelDia_CierraLasCaidasAntesDeListar()
    {
        // Lunes por la mañana: nadie tuvo la aplicación abierta desde el viernes, así que ningún
        // latido barrió. El registro NO debe listar la jornada caída como «en curso».
        var db = TestDb.New();
        var svcAna = Svc(db, Usuario(db, 1, "Ana"));
        svcAna.Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var admin = Usuario(db, 2, "Jefa", UserRole.Admin);
        var jornadas = Svc(db, admin).JornadasDelDia(DateTime.Today);

        var j = Assert.Single(jornadas, x => x.UserId == 1);
        Assert.NotNull(j.EndedAtUtc);
        Assert.Equal(PresenceEnd.SinLatido, j.EndReason);
        Assert.Equal(j.LastSeenUtc, j.EndedAtUtc);
    }

    // ── Estados ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CambiarEstado_SeGuardaConSuNota()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();

        var (ok, _) = svc.CambiarEstado(PresenceState.Comiendo, "vuelvo 15:30");

        Assert.True(ok);
        var j = db.WorkPresences.Single();
        Assert.Equal(PresenceState.Comiendo, j.State);
        Assert.Equal("vuelvo 15:30", j.StateNote);
    }

    [Fact]
    public void ElEstadoSeSobrescribe_NoSeHistoriza()
    {
        // Decisión deliberada: un registro minutado de las pausas de alguien es vigilancia, no
        // asistencia. Lo que queda guardado es la jornada, no cuánto estuvo en el baño.
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();

        svc.CambiarEstado(PresenceState.Descanso);
        svc.CambiarEstado(PresenceState.Comiendo);
        svc.CambiarEstado(PresenceState.Disponible);

        Assert.Single(db.WorkPresences);
        Assert.Equal(PresenceState.Disponible, db.WorkPresences.Single().State);
    }

    [Fact]
    public void CambiarEstado_SinJornadaAbierta_LaAbre()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        var (ok, _) = svc.CambiarEstado(PresenceState.EnReunion);

        Assert.True(ok);
        Assert.Equal(PresenceState.EnReunion, db.WorkPresences.Single().State);
    }

    [Fact]
    public void MiEstado_DevuelveLoQueSeMarco()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.Entrar();
        svc.CambiarEstado(PresenceState.Ocupado, "en una entrega");

        var (estado, nota) = svc.MiEstado();

        Assert.Equal(PresenceState.Ocupado, estado);
        Assert.Equal("en una entrega", nota);
    }

    // ── Tablero ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Tablero_EsSoloDelAdministrador()
    {
        var db = TestDb.New();
        var dev = Svc(db, Usuario(db, 1, "Ana"));
        Assert.Throws<AuthorizationException>(() => dev.Tablero());
        Assert.Throws<AuthorizationException>(() => dev.JornadasDelDia(DateTime.Today));
    }

    [Fact]
    public void Tablero_IncluyeAQuienNuncaHaEntrado()
    {
        // Que alguien falte es justamente el dato; esconderlo haría inútil el tablero.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);

        Svc(db, ana).Entrar();

        var filas = Svc(db, admin).Tablero();

        Assert.Equal(3, filas.Count);
        Assert.True(filas.Single(f => f.Nombre == "Ana").Conectado);
        Assert.False(filas.Single(f => f.Nombre == "Beto").Conectado);
    }

    [Fact]
    public void Tablero_MuestraLosConectadosPrimero()
    {
        var db = TestDb.New();
        Usuario(db, 1, "Zulema");
        var beto = Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        Svc(db, beto).Entrar();

        var filas = Svc(db, admin).Tablero();

        Assert.Equal("Beto", filas[0].Nombre);
        Assert.True(filas[0].Conectado);
    }

    [Fact]
    public void Tablero_QuienDejoDeLatir_SaleDesconectado()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        Svc(db, ana).Entrar();
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var fila = Svc(db, admin).Tablero().Single(f => f.Nombre == "Ana");

        Assert.False(fila.Conectado);
        Assert.Equal(PresenceState.Ausente, fila.Estado);
    }

    [Fact]
    public void Tablero_NoDelataLaNotaDeQuienNoEstaConectado()
    {
        // «Comiendo — vuelvo 15:30» de hace tres días no informa de nada; solo confunde.
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        var svc = Svc(db, ana);
        svc.Entrar();
        svc.CambiarEstado(PresenceState.Comiendo, "vuelvo 15:30");
        SinLatirDesdeHace(db, 1, PresenceService.ToleranciaSinLatido + TimeSpan.FromMinutes(5));

        var fila = Svc(db, admin).Tablero().Single(f => f.Nombre == "Ana");

        Assert.Null(fila.Nota);
    }

    // ── Registro de jornadas ─────────────────────────────────────────────────────

    [Fact]
    public void JornadasDelDia_TraeLasDeEseDia()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        var svc = Svc(db, ana);
        svc.Entrar();
        svc.Salir();

        Assert.Single(Svc(db, admin).JornadasDelDia(DateTime.Today));
        Assert.Empty(Svc(db, admin).JornadasDelDia(DateTime.Today.AddDays(-3)));
    }

    [Fact]
    public void JornadasDelDia_SePuedeAcotarAUnaPersona()
    {
        var db = TestDb.New();
        var ana  = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        var admin = Usuario(db, 99, "Jefa", UserRole.Admin);
        Svc(db, ana).Entrar();
        Svc(db, beto).Entrar();

        Assert.Equal(2, Svc(db, admin).JornadasDelDia(DateTime.Today).Count);
        Assert.Single(Svc(db, admin).JornadasDelDia(DateTime.Today, userId: 1));
    }

    [Fact]
    public void Duracion_SeLeeEnHorasYMinutos()
    {
        Assert.Equal("menos de 1 min", PresenceService.Duracion(TimeSpan.FromSeconds(30)));
        Assert.Equal("45 min", PresenceService.Duracion(TimeSpan.FromMinutes(45)));
        Assert.Equal("8 h 15 min", PresenceService.Duracion(TimeSpan.FromMinutes(495)));
    }
}
