using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL POOL SEGMENTADO POR SUBEQUIPO.
///
/// <para>Una actividad se puede publicar para un subequipo concreto, y entonces solo la ven —y solo
/// la toman— quienes están en él o en algo que cuelgue de él. Sin segmentar, que es lo que hay en
/// todas las filas que ya existían, la ve todo el mundo igual que siempre.</para>
///
/// <para><b>Lo que este archivo persigue de verdad es el ATAJO.</b> Un filtro que solo viva en la
/// consulta de la lista esconde la actividad de la pantalla y no impide tomarla: a
/// <c>POST /api/pool/{id}/tomar</c> se la puede llamar a mano con cualquier identificador. Por eso la
/// mitad de las pruebas de aquí no miran lo que se ve, miran lo que se puede TOMAR.</para>
///
/// <para><b>Y lo que estas pruebas no alcanzan</b>, para no confundirlo con lo que sí: la carrera
/// entre leer la actividad y escribirla. El servicio se defiende de ella metiendo la visibilidad en
/// el propio UPDATE condicional, pero aquí no se reproduce; lo que se comprueba es que quien no debe
/// no se la lleva.</para>
/// </summary>
public class PoolSegmentadoTests
{
    private static (PoolActivityService pool, AppDbContext db) Servicio(AppDbContext db, ICurrentUser quien)
    {
        var bitacora = new AuditService(db, quien, new OrigenDePrueba());
        return (new PoolActivityService(
            db, quien, bitacora, new NotificationService(db),
            new SettingsService(db, quien, bitacora)), db);
    }

    /// <summary>
    /// «Desarrollo Web» con dos subequipos, y una persona en cada sitio.
    ///
    /// <para>Se necesitan los tres niveles para poder distinguir el subárbol de la igualdad estricta:
    /// quien está en «Soporte» tiene que ver lo publicado a «Desarrollo Web» —se publica al nivel al
    /// que se quiere que se vea— y NO lo publicado a «Satélites», que es su hermano.</para>
    /// </summary>
    private static AppDbContext Casa()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Desarrollo Web" });
        db.Teams.Add(new Team { Id = 2, Name = "Soporte", EquipoPadreId = 1 });
        db.Teams.Add(new Team { Id = 3, Name = "Satélites", EquipoPadreId = 1 });
        db.Developers.Add(new Developer { Id = 10, FullName = "Ana",  IsActive = true, TeamId = 1 });
        db.Developers.Add(new Developer { Id = 20, FullName = "Beto", IsActive = true, TeamId = 2 });
        db.Developers.Add(new Developer { Id = 30, FullName = "Caro", IsActive = true, TeamId = 3 });
        db.Developers.Add(new Developer { Id = 40, FullName = "Dani", IsActive = true });   // sin equipo
        db.SaveChanges();
        return db;
    }

    /// <summary>Una actividad libre, opcionalmente publicada a un subequipo.</summary>
    private static int Publicar(AppDbContext db, string titulo, int? equipoId)
    {
        var a = new PoolActivity
        {
            Title = titulo,
            WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media,
            Priority = PoolPriority.Media,
            Status = PoolActivityStatus.Disponible,
            EquipoId = equipoId,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.PoolActivities.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    private static ICurrentUser Dev(int developerId) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: developerId);

    private static async Task<List<string>> VeAsync(AppDbContext db, ICurrentUser quien)
    {
        var (pool, _) = Servicio(db, quien);
        return [.. (await pool.DisponiblesAsync()).Select(a => a.Title)];
    }

    // ── Lo que se ve ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Lo no segmentado lo sigue viendo todo el mundo.</b> Es la fila que hay en producción hoy —la
    /// columna nace en nulo— y la prueba que impide que el pool entero desaparezca de la pantalla de
    /// todos a la mañana siguiente del despliegue.
    /// </summary>
    [Fact]
    public async Task SIN_SEGMENTAR_lo_ve_todo_el_mundo()
    {
        var db = Casa();
        Publicar(db, "Para toda la casa", null);

        Assert.Contains("Para toda la casa", await VeAsync(db, Dev(10)));
        Assert.Contains("Para toda la casa", await VeAsync(db, Dev(20)));
        Assert.Contains("Para toda la casa", await VeAsync(db, Dev(40)));   // sin equipo
        Assert.Contains("Para toda la casa", await VeAsync(db, UsuarioDePrueba.Como(UserRole.Admin)));
    }

    /// <summary>Publicada a un subequipo, solo la ve ese subequipo.</summary>
    [Fact]
    public async Task SEGMENTADA_solo_la_ve_su_subequipo()
    {
        var db = Casa();
        Publicar(db, "Solo Soporte", 2);

        Assert.Contains("Solo Soporte", await VeAsync(db, Dev(20)));      // está en Soporte
        Assert.DoesNotContain("Solo Soporte", await VeAsync(db, Dev(30))); // Satélites, su hermano
        Assert.DoesNotContain("Solo Soporte", await VeAsync(db, Dev(40))); // sin equipo
    }

    /// <summary>
    /// <b>Publicar a un equipo alcanza a TODA su rama.</b> Se lee como «publico al nivel al que quiero
    /// que se vea»: a «Desarrollo Web» lo ve el área entera. Es lo que distingue esto de comparar
    /// identificadores a secas, que dejaría fuera a los subequipos de aquel a quien se publica.
    /// </summary>
    [Fact]
    public async Task PUBLICADA_AL_PADRE_la_ve_toda_su_rama()
    {
        var db = Casa();
        Publicar(db, "Para el área", 1);

        Assert.Contains("Para el área", await VeAsync(db, Dev(10)));   // el propio Desarrollo Web
        Assert.Contains("Para el área", await VeAsync(db, Dev(20)));   // Soporte, que cuelga de él
        Assert.Contains("Para el área", await VeAsync(db, Dev(30)));   // Satélites, que cuelga de él
        Assert.DoesNotContain("Para el área", await VeAsync(db, Dev(40)));
    }

    /// <summary>
    /// Al revés NO: quien está arriba no ve por herencia lo publicado a un subequipo suyo —eso sería
    /// no segmentar nada—, y por eso administración sí lo ve, pero por ser administración.
    /// </summary>
    [Fact]
    public async Task PUBLICADA_AL_HIJO_no_la_ve_el_padre_por_serlo()
    {
        var db = Casa();
        Publicar(db, "Solo Soporte", 2);

        Assert.DoesNotContain("Solo Soporte", await VeAsync(db, Dev(10)));
        Assert.Contains("Solo Soporte", await VeAsync(db, UsuarioDePrueba.Como(UserRole.Admin)));
    }

    /// <summary>
    /// Administración lo ve todo: es quien publica, quien reparte y quien rescata lo atascado.
    /// Segmentarle la lista dejaría actividades sin nadie que pudiera sacarlas de donde estén.
    /// </summary>
    [Fact]
    public async Task ADMINISTRACION_lo_ve_todo()
    {
        var db = Casa();
        Publicar(db, "Para toda la casa", null);
        Publicar(db, "Solo Soporte", 2);
        Publicar(db, "Solo Satélites", 3);

        var visto = await VeAsync(db, UsuarioDePrueba.Como(UserRole.Admin));
        Assert.Equal(3, visto.Count);
    }

    // ── Lo que se puede TOMAR, que es lo que importa ─────────────────────────────

    /// <summary>
    /// <b>Esconder la actividad de la lista no basta: hay que impedir TOMARLA.</b> A la dirección de
    /// «tomar» se la puede llamar a mano con cualquier identificador, así que un filtro que solo
    /// viviera en la consulta de la lista no escondería nada, solo lo dibujaría de menos.
    ///
    /// <para><b>Lo que esta prueba NO distingue</b>, dicho aquí para que nadie se lo crea de más: el
    /// servicio tiene DOS guardas —la comprobación temprana, que existe para dar un mensaje que se
    /// entienda, y el filtro dentro del UPDATE que gana el reclamo— y cualquiera de las dos por
    /// separado hace pasar esta prueba. Se comprobó quitando cada una: sigue verde. Solo se pone roja
    /// quitando las dos.</para>
    ///
    /// <para>Las dos se quedan igualmente, y no por si acaso: la temprana lee y decide, y entre esa
    /// lectura y la escritura cabe que alguien cambie el equipo de la actividad. Esa ventana la cierra
    /// el filtro del UPDATE, y esa carrera no la reproduce ninguna prueba de aquí.</para>
    /// </summary>
    [Fact]
    public async Task NO_SE_PUEDE_TOMAR_lo_publicado_a_otro_equipo()
    {
        var db = Casa();
        int id = Publicar(db, "Solo Soporte", 2);

        var (pool, _) = Servicio(db, Dev(30));            // Caro está en Satélites
        var (ok, mensaje) = await pool.TomarAsync(id, 30);

        Assert.False(ok);
        Assert.Contains("otro equipo", mensaje);

        db.ChangeTracker.Clear();
        var quedo = db.PoolActivities.Find(id)!;
        Assert.Equal(PoolActivityStatus.Disponible, quedo.Status);
        Assert.Null(quedo.ClaimedByDeveloperId);
    }

    /// <summary>Quien SÍ está en el subequipo la toma con normalidad.</summary>
    [Fact]
    public async Task SI_ESTA_EN_EL_SUBEQUIPO_la_toma()
    {
        var db = Casa();
        int id = Publicar(db, "Solo Soporte", 2);

        var (pool, _) = Servicio(db, Dev(20));
        var (ok, _) = await pool.TomarAsync(id, 20);

        Assert.True(ok);
        db.ChangeTracker.Clear();
        Assert.Equal(PoolActivityStatus.Tomada, db.PoolActivities.Find(id)!.Status);
    }

    /// <summary>Y quien está en el equipo padre también, porque lo publicado a su rama es suyo.</summary>
    [Fact]
    public async Task DESDE_EL_PADRE_se_toma_lo_publicado_a_su_rama()
    {
        var db = Casa();
        int id = Publicar(db, "Para el área", 1);

        var (pool, _) = Servicio(db, Dev(20));            // Beto, en Soporte
        var (ok, _) = await pool.TomarAsync(id, 20);

        Assert.True(ok);
    }

    /// <summary>
    /// Quien no tiene equipo se queda solo con lo no segmentado. Es el estado en que queda alguien
    /// cuando se borra su equipo; lo contrario convertiría quedarse sin equipo en un privilegio.
    /// </summary>
    [Fact]
    public async Task SIN_EQUIPO_solo_lo_no_segmentado()
    {
        var db = Casa();
        int libre = Publicar(db, "Para toda la casa", null);
        int suyo = Publicar(db, "Solo Soporte", 2);

        Assert.Equal(["Para toda la casa"], await VeAsync(db, Dev(40)));

        var (pool, _) = Servicio(db, Dev(40));
        Assert.False((await pool.TomarAsync(suyo, 40)).ok);
        Assert.True((await pool.TomarAsync(libre, 40)).ok);
    }
}
