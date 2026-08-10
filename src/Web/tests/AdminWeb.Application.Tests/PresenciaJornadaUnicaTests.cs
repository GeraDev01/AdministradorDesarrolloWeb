using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Que N pestañas sean UNA jornada, y que el estado sobreviva a recargar.
///
/// <para>El escritorio garantizaba una instancia por sesión de Windows, así que la jornada era una
/// sola de la mañana a la noche. En un navegador cada F5 y cada pestaña es una conexión más, y
/// abriendo una fila por conexión el registro del día acababa con ocho o diez jornadas de minutos
/// donde hubo una de ocho horas. De paso el estado volvía a «Disponible» en cada recarga y con él se
/// perdía de vista la nota de «vuelvo 15:30».</para>
///
/// <para>Estas pruebas fijan las tres reglas: reutilizar la abierta, reanudar la recién cerrada, y
/// empezar una nueva solo cuando de verdad tocó.</para>
/// </summary>
public class PresenciaJornadaUnicaTests
{
    private static PresenceService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new OrigenDePrueba());

    private static (AppDbContext db, ICurrentUser cu) Nuevo(int userId = 7)
    {
        var db = TestDb.New();
        db.Users.Add(new User
        {
            Id = userId, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();
        return (db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: userId));
    }

    private static List<WorkPresence> Jornadas(AppDbContext db) =>
        [.. db.WorkPresences.AsNoTracking().OrderBy(p => p.StartedAtUtc)];

    [Fact]
    public async Task Tres_pestanas_son_UNA_sola_jornada()
    {
        var (db, cu) = Nuevo();
        var svc = Svc(db, cu);

        var primera = await svc.EntrarAsync("pestaña 1");
        var segunda = await svc.EntrarAsync("pestaña 2");
        var tercera = await svc.EntrarAsync("pestaña 3");

        Assert.Equal(primera!.Id, segunda!.Id);
        Assert.Equal(primera.Id, tercera!.Id);
        Assert.Single(Jornadas(db));
    }

    /// <summary>
    /// Abrir otra pestaña NO puede pisar el estado: quien puso «En reunión» en una y abre otra para
    /// mirar el sprint seguiría en reunión, y antes volvía a «Disponible».
    /// </summary>
    [Fact]
    public async Task Abrir_otra_pestana_conserva_el_estado_y_la_nota()
    {
        var (db, cu) = Nuevo();

        await Svc(db, cu).EntrarAsync();
        await Svc(db, cu).CambiarEstadoAsync(PresenceState.EnReunion, "vuelvo 15:30");

        await Svc(db, cu).EntrarAsync("otra pestaña");

        var (estado, nota) = await Svc(db, cu).MiEstadoAsync();
        Assert.Equal(PresenceState.EnReunion, estado);
        Assert.Equal("vuelvo 15:30", nota);
        Assert.Single(Jornadas(db));
    }

    /// <summary>Un F5: la jornada se cierra y se vuelve a abrir en segundos. Es la MISMA.</summary>
    [Fact]
    public async Task Recargar_reanuda_la_misma_jornada_en_vez_de_abrir_otra()
    {
        var (db, cu) = Nuevo();

        var original = await Svc(db, cu).EntrarAsync();
        await Svc(db, cu).CambiarEstadoAsync(PresenceState.Ocupado, "compilando");
        await Svc(db, cu).SalirAsync();                    // se cierra la pestaña

        var trasRecargar = await Svc(db, cu).EntrarAsync();  // vuelve enseguida

        Assert.Equal(original!.Id, trasRecargar!.Id);
        Assert.Single(Jornadas(db));
        Assert.Null(Jornadas(db)[0].EndedAtUtc);           // vuelve a estar abierta

        var (estado, nota) = await Svc(db, cu).MiEstadoAsync();
        Assert.Equal(PresenceState.Ocupado, estado);
        Assert.Equal("compilando", nota);
    }

    /// <summary>
    /// Volver HORAS después sí es una jornada nueva: quien se fue a comer y regresó tiene dos
    /// tramos, y juntarlos en uno se comería el hueco y mentiría sobre el día.
    /// </summary>
    [Fact]
    public async Task Volver_mucho_despues_SI_abre_una_jornada_nueva()
    {
        var (db, cu) = Nuevo();

        var manana = await Svc(db, cu).EntrarAsync();
        await Svc(db, cu).SalirAsync();

        // Se envejece el cierre a mano: es la única forma de cruzar la ventana sin esperar.
        var cerrada = db.WorkPresences.Single(p => p.Id == manana!.Id);
        cerrada.EndedAtUtc = DateTime.UtcNow.AddHours(-3);
        cerrada.StartedAtUtc = DateTime.UtcNow.AddHours(-5);
        cerrada.LastSeenUtc = DateTime.UtcNow.AddHours(-3);
        db.SaveChanges();

        var tarde = await Svc(db, cu).EntrarAsync();

        Assert.NotEqual(manana!.Id, tarde!.Id);
        Assert.Equal(2, Jornadas(db).Count);
        // Y la de la mañana conserva su cierre: reanudar no puede reabrir un tramo ya terminado.
        Assert.NotNull(Jornadas(db)[0].EndedAtUtc);
    }

    /// <summary>
    /// Una jornada colgada de un cierre sucio VIEJO se sella con su último latido, no con la hora
    /// de ahora: contar como trabajadas las horas en que la aplicación estuvo cerrada sería regalar
    /// tiempo que nadie trabajó.
    /// </summary>
    [Fact]
    public async Task Una_jornada_colgada_se_sella_con_su_ultimo_latido()
    {
        var (db, cu) = Nuevo();

        var vieja = await Svc(db, cu).EntrarAsync();
        var ultimoLatido = DateTime.UtcNow.AddHours(-6);
        var colgada = db.WorkPresences.Single(p => p.Id == vieja!.Id);
        colgada.StartedAtUtc = DateTime.UtcNow.AddHours(-8);
        colgada.LastSeenUtc = ultimoLatido;
        db.SaveChanges();

        await Svc(db, cu).EntrarAsync();   // alguien vuelve al día siguiente

        var sellada = db.WorkPresences.AsNoTracking().Single(p => p.Id == vieja!.Id);
        Assert.Equal(PresenceEnd.SinLatido, sellada.EndReason);
        Assert.Equal(ultimoLatido, sellada.EndedAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Salir_cierra_la_jornada_con_la_hora_de_ahora()
    {
        var (db, cu) = Nuevo();

        await Svc(db, cu).EntrarAsync();
        await Svc(db, cu).SalirAsync();

        var cerrada = Jornadas(db)[0];
        Assert.Equal(PresenceEnd.CierreNormal, cerrada.EndReason);
        Assert.NotNull(cerrada.EndedAtUtc);
        Assert.Equal(DateTime.UtcNow, cerrada.EndedAtUtc!.Value, TimeSpan.FromSeconds(5));
    }

    /// <summary>La jornada de una persona no puede tocar la de otra.</summary>
    [Fact]
    public async Task Reanudar_no_se_lleva_por_delante_la_jornada_de_otro()
    {
        var (db, ana) = Nuevo(userId: 7);
        db.Users.Add(new User
        {
            Id = 8, Username = "beto", FullName = "Beto", Role = UserRole.Desarrollador,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();
        var beto = UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 8);

        var deBeto = await Svc(db, beto).EntrarAsync();
        await Svc(db, ana).EntrarAsync();
        await Svc(db, ana).SalirAsync();
        await Svc(db, ana).EntrarAsync();

        var laDeBeto = db.WorkPresences.AsNoTracking().Single(p => p.Id == deBeto!.Id);
        Assert.Null(laDeBeto.EndedAtUtc);          // sigue abierta
        Assert.Equal(2, Jornadas(db).Count);       // una de cada uno, y no más
    }
}
