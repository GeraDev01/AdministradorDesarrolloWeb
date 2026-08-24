using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL HISTÓRICO NO SE TOCÓ, Y ÉSTA ES LA PRUEBA QUE LO FIJA.
///
/// <para><b>Qué se prometió.</b> Reducir cuatro caminos de puntos a uno se hizo <b>sin borrar,
/// actualizar ni reasignar una sola fila de <c>PointEntry</c></b>. Solo columnas nulables nuevas.
/// Todo lo que se registró antes del corte —una autocalificación aprobada en junio, una actividad
/// libre que el líder calificó en julio, un artículo publicado, una actividad del pool aceptada—
/// sigue contando exactamente igual, y el ranking de un mes cerrado da el mismo número que daba.</para>
///
/// <para><b>Por qué hace falta escribirlo como prueba y no como promesa.</b> Lo que amenaza a este
/// histórico no es un despliegue: es alguien, dentro de seis meses, «limpiando» un catálogo de
/// criterios que ya no se ofrece —«total, la autocalificación se retiró»— o borrando las entradas de
/// un camino apagado por pulcritud. Las dos cosas se ven razonables y las dos dejan el desempeño de
/// medio año diciendo otra cosa, en silencio y sin vuelta atrás. Esta prueba las convierte en un
/// rojo.</para>
///
/// <para><b>Lo que NO se prueba aquí, y por qué.</b> El plan pedía comparar el ranking «con la puerta
/// apagada y encendida». Esa mitad no existe: se decidió apagar en firme, sin interruptor, así que no
/// hay dos estados que comparar. Lo que sí se puede fijar —y es lo que esa prueba tenía que fijar de
/// verdad— es que <b>las cuatro procedencias siguen sumando</b> aunque dos de ellas ya no puedan
/// producir filas nuevas.</para>
/// </summary>
public class HistoricoIntactoTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext Nueva()
    {
        var db = TestDb.New();
        _contextos.Add(db);
        return db;
    }

    private static UsuarioDePrueba Admin() => UsuarioDePrueba.Como(UserRole.Admin, userId: 9);

    /// <summary>El mes cerrado del que se habla. Cualquiera pasado sirve: lo que importa es que no es hoy.</summary>
    private const int Anio = 2026;
    private const int Mes = 6;

    /// <summary>
    /// Las CUATRO procedencias, sembradas como quedaron en la base. Se escriben a mano y no llamando
    /// a cada servicio, y es deliberado: dos de esos servicios ya no pagan, así que llamarlos daría
    /// cero filas y la prueba se quedaría verde comprobando nada. Lo que hay que reproducir es la
    /// FORMA de lo que quedó escrito, que es lo único que el ranking mira.
    /// </summary>
    private static void SembrarLasCuatroProcedenciasAsync(AppDbContext db, int devId, int criterioId)
    {
        void Entrada(int puntos, string comentario, int? porQuien, int? deQuien) =>
            db.PointEntries.Add(new PointEntry
            {
                DeveloperId = devId, CriterionId = criterioId, Points = puntos,
                Year = Anio, Month = Mes, Date = new DateTime(Anio, Mes, 15, 12, 0, 0, DateTimeKind.Utc),
                ApprovalStatus = PointApprovalStatus.Aprobado,
                Comment = comentario,
                SubmittedByDeveloperId = deQuien,
                AssignedByUserId = porQuien,
                ReviewedByUserId = porQuien ?? 9,
                ReviewedAt = new DateTime(Anio, Mes, 16, 12, 0, 0, DateTimeKind.Utc)
            });

        // 1. La AUTOCALIFICACIÓN: la mandó el desarrollador y el líder la aprobó.
        Entrada(5, "Autocalificación: documenté el módulo", porQuien: null, deQuien: devId);

        // 2. La ACTIVIDAD LIBRE que el líder calificó, con el tiempo medido delante.
        Entrada(7, "Actividad: Investigar la caída — lo sacó en dos horas", porQuien: 9, deQuien: null);

        // 3. El POOL aceptado, con los puntos que la matriz congeló.
        Entrada(12, "Pool: Corregir el importador", porQuien: 9, deQuien: null);

        // 4. El ARTÍCULO de conocimiento publicado.
        Entrada(8, "Artículo publicado: cómo migrar sin parar producción", porQuien: 9, deQuien: null);

        db.SaveChanges();
    }

    private static (int devId, int criterioId) Entorno(AppDbContext db)
    {
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);

        var crit = new ScoringCriterion
        {
            Name = "Iniciativa", DefaultPoints = 5, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(crit);
        db.SaveChanges();

        return (dev.Id, crit.Id);
    }

    private PerformanceScoringService Puntos(AppDbContext db) => new(db, Admin());

    // ── El ranking de un mes cerrado ─────────────────────────────────────────────

    /// <summary>
    /// Las cuatro procedencias suman, y suman lo suyo. 5 + 7 + 12 + 8 = 32.
    ///
    /// <para>El número está escrito a mano a propósito, en vez de derivarlo de la siembra: una prueba
    /// que suma lo mismo que sembró pasa aunque el ranking devuelva la lista vacía. Aquí, si alguien
    /// hace que un camino apagado deje de contar, el 32 se convierte en 27 o en 20 y la prueba dice
    /// exactamente cuánto se perdió.</para>
    /// </summary>
    [Fact]
    public async Task ElRankingDeUnMesCerrado_SumaLasCuatroProcedencias()
    {
        var db = Nueva();
        var (devId, criterioId) = Entorno(db);
        SembrarLasCuatroProcedenciasAsync(db, devId, criterioId);

        var ranking = await Puntos(db).IndividualRankingAsync(Anio, Mes);

        var ana = Assert.Single(ranking);
        Assert.Equal(32, ana.Total);
        Assert.Equal(4, ana.Count);
    }

    /// <summary>
    /// Y la de la AUTOCALIFICACIÓN sigue reconociéndose como suya. <c>SubmittedByDeveloperId</c> es lo
    /// que distingue «lo mandé yo» de «me lo asignó el líder», y de ahí cuelga que se pueda corregir y
    /// replicar: si al apagar el camino se hubiera limpiado ese campo, las entradas pendientes de la
    /// cola dejarían de ser editables por su dueño y nadie sabría por qué.
    /// </summary>
    [Fact]
    public async Task LaEntradaDeLaAutocalificacion_SigueSiendoDeQuienLaMando()
    {
        var db = Nueva();
        var (devId, criterioId) = Entorno(db);
        SembrarLasCuatroProcedenciasAsync(db, devId, criterioId);

        var suyas = await db.PointEntries.AsNoTracking()
            .Where(p => p.SubmittedByDeveloperId == devId)
            .ToListAsync();

        var mia = Assert.Single(suyas);
        Assert.Contains("Autocalificación", mia.Comment);
        Assert.Null(mia.AssignedByUserId);
    }

    /// <summary>
    /// EL CATÁLOGO NO SE DEPURÓ, y es lo que sostiene que el histórico se pueda LEER además de sumar.
    ///
    /// <para>Apagar la oferta de criterios fue una decisión de consulta: las dos pantallas devuelven
    /// listas vacías y ninguna fila cambió de <c>IsActive</c>. Si en vez de eso se hubiera desactivado
    /// el catálogo, el total seguiría saliendo —el ranking no mira <c>IsActive</c>— pero cada entrada
    /// aprobada apuntaría a un criterio retirado, y el detalle del mes diría de qué se pagó con una
    /// etiqueta tachada. Que el total sobreviva no basta: tiene que sobrevivir la explicación.</para>
    /// </summary>
    [Fact]
    public async Task ElCriterioDeUnaEntradaVieja_SigueActivoYLegible()
    {
        var db = Nueva();
        var (devId, criterioId) = Entorno(db);
        SembrarLasCuatroProcedenciasAsync(db, devId, criterioId);

        var ranking = await Puntos(db).IndividualRankingAsync(Anio, Mes);
        var ana = Assert.Single(ranking);

        Assert.All(ana.Entries, e =>
        {
            Assert.NotNull(e.Criterion);
            Assert.True(e.Criterion!.IsActive,
                "apagar la oferta de criterios no puede haber desactivado ninguna fila del catálogo: " +
                "el histórico dejaría de poder explicarse");
        });
    }

    /// <summary>
    /// Y lo RECHAZADO sigue sin contar, que es la otra mitad de «nada se tocó»: apagar un camino no
    /// puede haber colado en el ranking lo que su dueño nunca consiguió aprobar.
    /// </summary>
    [Fact]
    public async Task LoPendienteYLoRechazado_SiguenSinContar()
    {
        var db = Nueva();
        var (devId, criterioId) = Entorno(db);
        SembrarLasCuatroProcedenciasAsync(db, devId, criterioId);

        foreach (var estado in new[] { PointApprovalStatus.Pendiente, PointApprovalStatus.Rechazado })
            db.PointEntries.Add(new PointEntry
            {
                DeveloperId = devId, CriterionId = criterioId, Points = 100,
                Year = Anio, Month = Mes, Date = new DateTime(Anio, Mes, 20, 12, 0, 0, DateTimeKind.Utc),
                ApprovalStatus = estado, SubmittedByDeveloperId = devId
            });
        db.SaveChanges();

        var ana = Assert.Single(await Puntos(db).IndividualRankingAsync(Anio, Mes));
        Assert.Equal(32, ana.Total);
    }

    /// <summary>
    /// Y el mes cerrado NO se mueve porque hoy pase algo. Es lo que hace que un ranking pasado sea una
    /// foto y no una consulta viva: si una entrada nueva de este mes cambiara el total de junio, todo
    /// lo anterior sería renegociable.
    /// </summary>
    [Fact]
    public async Task UnaEntradaDeHoy_NoCambiaElTotalDeUnMesPasado()
    {
        var db = Nueva();
        var (devId, criterioId) = Entorno(db);
        SembrarLasCuatroProcedenciasAsync(db, devId, criterioId);

        var ahora = DateTime.Now;
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = devId, CriterionId = criterioId, Points = 50,
            Year = ahora.Year, Month = ahora.Month, Date = DateTime.UtcNow,
            ApprovalStatus = PointApprovalStatus.Aprobado
        });
        db.SaveChanges();

        var ana = Assert.Single(await Puntos(db).IndividualRankingAsync(Anio, Mes));
        Assert.Equal(32, ana.Total);
    }
}
