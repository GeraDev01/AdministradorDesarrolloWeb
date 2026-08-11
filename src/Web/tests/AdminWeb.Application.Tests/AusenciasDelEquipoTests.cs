using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// «Quién más del equipo estará fuera esas fechas», que es la ayuda del asistente de ausencias que
/// evita el problema que de verdad pasa: dos personas del mismo equipo fuera la misma semana.
///
/// <para>Lo que se prueba aquí es lo que puede salir mal SIN QUE NADIE LO NOTE, porque el resultado
/// es plausible en los dos casos:</para>
///
/// <list type="bullet">
///   <item>Que se cuele lo que NO debe salir: solicitudes pendientes o rechazadas —anunciar como
///     ausencia algo que quizá se rechace empuja a mover unas fechas por nada—, gente dada de baja,
///     y las propias solicitudes de quien pregunta.</item>
///   <item>Que el SOLAPE se cuente mal en los extremos. Es el clásico error de un día y no se ve:
///     un renglón de más o de menos parece igual de razonable.</item>
///   <item>Que un rango al revés o absurdo tumbe una pantalla que solo estaba ayudando.</item>
/// </list>
/// </summary>
public class AusenciasDelEquipoTests
{
    private const int Yo = 1;
    private const int Companero = 2;
    private const int OtroEquipo = 3;
    private const int Inactivo = 4;

    private const int MiEquipo = 10;
    private const int SuEquipo = 20;

    private static readonly DateTime Lunes = new(2026, 8, 10);

    private static (AppDbContext db, AusenciasDelEquipoService svc) Nuevo(int? devId = Yo)
    {
        var db = TestDb.New();

        db.Teams.Add(new Team { Id = MiEquipo, Name = "Plataforma" });
        db.Teams.Add(new Team { Id = SuEquipo, Name = "Soporte" });

        db.Developers.Add(new Developer { Id = Yo, FullName = "Ana", IsActive = true, TeamId = MiEquipo });
        db.Developers.Add(new Developer { Id = Companero, FullName = "Beto", IsActive = true, TeamId = MiEquipo });
        db.Developers.Add(new Developer { Id = OtroEquipo, FullName = "Carla", IsActive = true, TeamId = SuEquipo });
        db.Developers.Add(new Developer { Id = Inactivo, FullName = "Darío", IsActive = false, TeamId = MiEquipo });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, devId);
        return (db, new AusenciasDelEquipoService(db, usuario));
    }

    private static void Vacacion(AppDbContext db, int devId, DateTime inicio, DateTime fin,
        VacationStatus estado = VacationStatus.Aprobada)
    {
        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = devId,
            StartDate = inicio,
            EndDate = fin,
            Status = estado,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    // ── Qué entra y qué no ───────────────────────────────────────────────────

    [Fact]
    public async Task SoloSalenLasVacacionesAPROBADAS()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes, Lunes.AddDays(4));
        Vacacion(db, OtroEquipo, Lunes, Lunes.AddDays(4), VacationStatus.Pendiente);

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // La pendiente todavía puede rechazarse: anunciarla como ausencia haría mover unas fechas
        // por algo que quizá nunca pase.
        Assert.Equal(["Beto"], r.Fuera.Select(f => f.Nombre));
    }

    [Theory]
    [InlineData(VacationStatus.Pendiente)]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task LoQueNoEstaAprobadoNoCuenta(VacationStatus estado)
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes, Lunes.AddDays(4), estado);

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        Assert.Empty(r.Fuera);
        Assert.Contains("Nadie más", r.Resumen);
    }

    [Fact]
    public async Task NoMeVeoAMiMismoEnLaLista()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Yo, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // Mis solicitudes ya las tengo delante en la misma pantalla; verme aquí haría dudar de si el
        // asistente entendió de quién son las fechas.
        Assert.Empty(r.Fuera);
    }

    [Fact]
    public async Task QuienYaNoTrabajaAquiNoAparece()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Inactivo, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        Assert.Empty(r.Fuera);
    }

    [Fact]
    public async Task LosPERMISOSNoSalen()
    {
        var (db, svc) = Nuevo();
        db.LeaveRequests.Add(new LeaveRequest
        {
            DeveloperId = Companero,
            Type = LeaveType.Incapacidad,
            Date = Lunes,
            DaysCount = 5,
            Status = LeaveStatus.Aprobada,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // Un permiso trae su TIPO, y entre los tipos hay «Incapacidad» y «Cita médica»: enseñárselo
        // al resto del equipo sería repartir información de salud ajena por una pantalla de
        // planeación. Si algún día alguien los añade «para que se vea todo», esta prueba lo dice.
        Assert.Empty(r.Fuera);
    }

    // ── El solape, día a día ─────────────────────────────────────────────────

    [Fact]
    public async Task LaVacacionQueTERMINAElPrimerDiaDelRangoSICUENTA()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddDays(-3), Lunes);

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // Un solo día de solape, pero es el día en que se vuelve a coincidir. Descartarla sería el
        // error de un día clásico, y ninguna pantalla lo delataría.
        var fila = Assert.Single(r.Fuera);
        Assert.Equal(1, fila.DiasQueSeSolapan);
    }

    [Fact]
    public async Task LaVacacionQueEMPIEZAElUltimoDiaDelRangoSICUENTA()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddDays(4), Lunes.AddDays(9));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        var fila = Assert.Single(r.Fuera);
        Assert.Equal(1, fila.DiasQueSeSolapan);
    }

    [Fact]
    public async Task LaQueTERMINAElDiaANTERIORNoCuenta()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddDays(-5), Lunes.AddDays(-1));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        Assert.Empty(r.Fuera);
    }

    [Fact]
    public async Task LaQueEMPIEZAElDiaSIGUIENTENoCuenta()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddDays(5), Lunes.AddDays(9));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        Assert.Empty(r.Fuera);
    }

    [Fact]
    public async Task LosDiasQueSeSolapanSonLosQuePISANMiRangoYNoLosSuyos()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddDays(-10), Lunes.AddDays(1));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // Sus vacaciones duran doce días; de los míos pisa dos. Lo que ayuda a decidir si mover unas
        // fechas es lo segundo, no lo primero.
        var fila = Assert.Single(r.Fuera);
        Assert.Equal(2, fila.DiasQueSeSolapan);
        Assert.Equal(Lunes.AddDays(-10), fila.Inicio);
        Assert.Equal(Lunes.AddDays(1), fila.Fin);
    }

    // ── El equipo propio ─────────────────────────────────────────────────────

    [Fact]
    public async Task SeMarcaQuienEsDEMIEQUIPOYSaleTambienElRestoDeLaCasa()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes, Lunes.AddDays(4));
        Vacacion(db, OtroEquipo, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // En una casa de once personas, coincidir con alguien de otro equipo también deja un hueco:
        // por eso el de fuera se enseña igual, marcado como lo que es.
        Assert.Equal(2, r.Fuera.Count);
        Assert.True(r.Fuera.Single(f => f.Nombre == "Beto").MismoEquipo);
        Assert.False(r.Fuera.Single(f => f.Nombre == "Carla").MismoEquipo);
        Assert.Contains("1 de ellas es de tu mismo equipo", r.Resumen);
    }

    [Fact]
    public async Task SinEquipoAsignadoNadieSaleMarcadoComoCompaneroDirecto()
    {
        var (db, svc) = Nuevo();
        var yo = db.Developers.Single(x => x.Id == Yo);
        var el = db.Developers.Single(x => x.Id == Companero);
        yo.TeamId = null;
        el.TeamId = null;
        db.SaveChanges();

        Vacacion(db, Companero, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // Dos nulos coincidirían y saldría TODO el mundo señalado como compañero directo, con lo que
        // la marca dejaría de significar nada — que es peor que no marcar a nadie.
        Assert.False(Assert.Single(r.Fuera).MismoEquipo);
        Assert.DoesNotContain("mismo equipo", r.Resumen);
    }

    // ── El resumen cuenta PERSONAS, no renglones ─────────────────────────────

    [Fact]
    public async Task DosPeriodosDeLaMISMAPersonaSonDosRenglonesYUNAPersona()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes, Lunes.AddDays(1));
        Vacacion(db, Companero, Lunes.AddDays(3), Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        Assert.Equal(2, r.Fuera.Count);
        Assert.StartsWith("1 persona", r.Resumen);
    }

    // ── El rango, que llega de fuera ─────────────────────────────────────────

    [Fact]
    public async Task UnRangoALREVESSeVolteaEnVezDeFallar()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes.AddDays(4), Lunes);

        // Es una ayuda de pantalla, no un alta: un 400 dejaría el paso del asistente en blanco sin
        // decirle a nadie qué hacer, y la respuesta cabe igual en el rango bien puesto.
        Assert.Equal(Lunes, r.Inicio);
        Assert.Equal(Lunes.AddDays(4), r.Fin);
        Assert.Single(r.Fuera);
    }

    [Fact]
    public async Task UnRangoENORMESeACORTAYSeDiceHastaDonde()
    {
        var (_, svc) = Nuevo();

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddYears(30));

        // A esta ruta se la puede llamar sin pasar por la pantalla, con dos fechas separadas por un
        // siglo. El rango recortado VUELVE EN LA RESPUESTA para que quien pregunte sepa sobre qué se
        // le contestó, en vez de creer que se miró lo que pidió.
        Assert.Equal(Lunes, r.Inicio);
        Assert.Equal(Lunes.AddDays(AusenciasDelEquipoService.MaxDiasDeConsulta - 1), r.Fin);
    }

    [Fact]
    public async Task LaHoraDeLaFechaNoCambiaElResultado()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, Companero, Lunes.AddHours(18), Lunes.AddDays(4).AddHours(9));

        var r = await svc.QuienEstaraFueraAsync(Lunes.AddHours(23), Lunes.AddDays(4));

        // Las fechas las escribe gente por varios caminos y basta uno que guarde DateTime.Now para
        // que una fila traiga hora. Comparando «<= fin» a secas, una vacación que empieza el último
        // día del rango se caería de la lista sin que nada avisara.
        var fila = Assert.Single(r.Fuera);
        Assert.Equal(5, fila.DiasQueSeSolapan);
        Assert.Equal(Lunes, fila.Inicio);
    }

    // ── Sin ficha ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaCuentaSINFichaVeLaListaCompleta()
    {
        var (db, svc) = Nuevo(devId: null);
        Vacacion(db, Companero, Lunes, Lunes.AddDays(4));

        var r = await svc.QuienEstaraFueraAsync(Lunes, Lunes.AddDays(4));

        // El administrador puede no tener ficha. No es un error: simplemente no hay «yo» a quien
        // excluir ni equipo propio con el que comparar, y la lista sale igual de útil.
        Assert.Single(r.Fuera);
        Assert.False(r.Fuera[0].MismoEquipo);
    }
}
