using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El pool en HORAS: quién pone cada número, cuándo se le pide y qué pasa cuando deja de ser suyo.
///
/// <para><b>El trato, en una tabla.</b> Son dos números distintos y conviene no confundirlos nunca:
/// el PLAZO es <i>cuándo</i> hay que entregarlo y el ESFUERZO es <i>cuánto trabajo</i> cuesta.</para>
/// <list type="table">
///   <item><term>Bug</term><description>PLAZO: lo pone el líder al publicar, obligatorio.
///         ESFUERZO: lo pone quien lo toma, en el momento de tomarlo, obligatorio.</description></item>
///   <item><term>Tarea y requerimiento</term><description>PLAZO: sale de la matriz.
///         ESFUERZO: lo pone el líder al publicar, obligatorio.</description></item>
/// </list>
///
/// <para><b>Por qué todo en horas y no en días.</b> Estos números existen para contrastarse con lo
/// que miden los CRONÓMETROS, que registran horas. Una estimación en días no se compara con un
/// cronómetro sin inventarse cuánto dura un día, y ese invento es justo lo que hacía incomparables
/// los números. Por eso las pruebas de aquí miran <c>TotalHours</c> y no <c>TotalDays</c>: si alguien
/// volviera a sumar días, la mitad de este archivo se pondría roja.</para>
///
/// <para><b>Por qué la estimación de un bug se pide AL TOMARLO y en ningún otro momento.</b> Escrita
/// a mitad del trabajo ya no es una estimación: quien la escribe sabe lo que le costó, y el número
/// deja de servir para comparar. De ahí que este archivo insista tanto en que un intento inválido no
/// deje NADA escrito — si dejara la actividad reclamada y sin número, la siguiente pantalla se lo
/// pediría demasiado tarde.</para>
/// </summary>
public class PoolHorasTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    /// <summary>
    /// Un contexto por llamada contra la misma base, como en el resto de las pruebas del pool: en la
    /// web hay uno por petición, así que compartirlo probaría un escenario que no existe.
    /// </summary>
    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
                                       new SettingsService(ctx, cu, bitacora));
    }

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<AppDbContext> BaseConPoolAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        return db;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static int NuevoDesarrollador(AppDbContext db, string nombre = "Ana")
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    /// <summary>Un bug con el plazo en horas que le pone el líder, que es lo que exige el modelo.</summary>
    private static PoolActivity Bug(decimal plazoEnHoras, string titulo = "Corregir el cálculo de facturación",
        PoolComplexity complejidad = PoolComplexity.Alta) =>
        new()
        {
            Title = titulo, WorkType = PoolWorkType.Bug, Complexity = complejidad,
            HorasLimite = plazoEnHoras
        };

    /// <summary>
    /// Una tarea con el esfuerzo que estima el líder y SIN plazo propio: el plazo de una tarea sale
    /// de la matriz, y ponérselo a mano es lo que el modelo reserva para los bugs.
    /// </summary>
    private static PoolActivity Tarea(decimal esfuerzoEnHoras, string titulo = "Migrar el reporte mensual",
        PoolComplexity complejidad = PoolComplexity.Media) =>
        new()
        {
            Title = titulo, WorkType = PoolWorkType.Tarea, Complexity = complejidad,
            HorasEstimadas = esfuerzoEnHoras
        };

    /// <summary>Marca todo el checklist, con evidencia donde se exige, para poder entregar.</summary>
    private async Task CompletarChecklistAsync(AppDbContext db, ICurrentUser cu, int actividadId, int devId)
    {
        var svc = Svc(db, cu);
        foreach (var item in await svc.ChecklistDeAsync(actividadId))
        {
            var (ok, mensaje) = await svc.MarcarItemAsync(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(ok, mensaje);
        }
    }

    private static PoolActivity Recargar(AppDbContext db, int id) =>
        db.PoolActivities.AsNoTracking().Single(a => a.Id == id);

    /// <summary>Las horas de plazo que la matriz de partida da a esa celda.</summary>
    private static decimal HorasDeLaMatriz(PoolWorkType tipo, PoolComplexity complejidad) =>
        PoolSeed.Matriz.Single(m => m.Tipo == tipo && m.Complejidad == complejidad).Horas;

    // ── Publicar: el líder pone el plazo del bug ─────────────────────────────────

    [Fact]
    public async Task Un_bug_guarda_el_plazo_en_horas_que_puso_el_lider()
    {
        var db = await BaseConPoolAsync();

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(Bug(plazoEnHoras: 12.5m));
        Assert.True(ok, mensaje);

        var guardada = Recargar(db, actividad!.Id);
        Assert.Equal(12.5m, guardada.HorasLimite);
        // El plazo de la celda Bug/Alta es otro (64 h) y no manda: en un bug la matriz no pone el plazo.
        Assert.NotEqual(HorasDeLaMatriz(PoolWorkType.Bug, PoolComplexity.Alta), guardada.HorasLimite);
        // El esfuerzo se queda vacío: lo escribirá quien lo tome.
        Assert.Null(guardada.HorasEstimadas);
        Assert.Null(guardada.HorasEstimadasEnUtc);
    }

    /// <summary>
    /// «Cuando sea Bug, el plazo se lo pongo yo». Si al venir vacío se dejara caer a la matriz, esa
    /// frase dejaría de ser cierta sin que nadie lo notara: el bug saldría publicado, con plazo, y
    /// con uno que el líder no eligió.
    /// </summary>
    [Fact]
    public async Task Un_bug_sin_plazo_no_se_publica()
    {
        var db = await BaseConPoolAsync();
        var sinPlazo = Bug(0m);
        sinPlazo.HorasLimite = null;

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(sinPlazo);

        Assert.False(ok);
        Assert.Contains("plazo", mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking());
    }

    /// <summary>
    /// El esfuerzo de un bug se RECHAZA al publicar en vez de ignorarse. Si el líder pudiera
    /// precargarlo, a quien lo toma no se le preguntaría nunca —el hueco ya estaría lleno— y el
    /// número dejaría de ser suyo, que es lo único que lo hace comparable.
    /// </summary>
    [Fact]
    public async Task Un_bug_no_admite_que_el_lider_le_ponga_el_esfuerzo()
    {
        var db = await BaseConPoolAsync();
        var conEsfuerzo = Bug(16m);
        conEsfuerzo.HorasEstimadas = 3m;

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(conEsfuerzo);

        Assert.False(ok);
        Assert.Contains("quien lo toma", mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking());
    }

    // ── Publicar: el líder pone el esfuerzo de la tarea ──────────────────────────

    [Fact]
    public async Task Una_tarea_guarda_el_esfuerzo_del_lider_y_no_lleva_plazo_a_mano()
    {
        var db = await BaseConPoolAsync();
        var antes = DateTime.UtcNow;

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(Tarea(esfuerzoEnHoras: 6m));
        Assert.True(ok, mensaje);

        var guardada = Recargar(db, actividad!.Id);
        Assert.Equal(6m, guardada.HorasEstimadas);
        // Sin plazo propio: nulo significa «el de la matriz», y es lo que se resolverá al tomarla.
        Assert.Null(guardada.HorasLimite);
        // El sello acompaña SIEMPRE al número. Sin él, «la estimación de un bug se escribe al
        // tomarlo» sería una afirmación que nadie podría comprobar después.
        Assert.NotNull(guardada.HorasEstimadasEnUtc);
        Assert.InRange(guardada.HorasEstimadasEnUtc!.Value, antes.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    /// <summary>
    /// El esfuerzo de una tarea es obligatorio. Si fuera opcional, el número del que depende toda la
    /// comparación contra el cronómetro sería el primero en saltarse el día que alguien tenga prisa.
    /// </summary>
    [Fact]
    public async Task Una_tarea_sin_esfuerzo_no_se_publica()
    {
        var db = await BaseConPoolAsync();
        var sinEsfuerzo = Tarea(0m);
        sinEsfuerzo.HorasEstimadas = null;

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(sinEsfuerzo);

        Assert.False(ok);
        Assert.Contains("esfuerzo estimado", mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking());
    }

    /// <summary>
    /// Por abajo el cuarto de hora, que es la granularidad con la que ya se estima en la pantalla de
    /// tickets: con un piso de cero, «0» pasaría por estimación y dejaría la comparación contra el
    /// cronómetro sin nada al otro lado. Por arriba, mil horas: medio año de una persona no es una
    /// actividad, es un proyecto que hay que partir.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(1001)]
    public async Task Un_esfuerzo_fuera_de_rango_no_se_publica(double horas)
    {
        var db = await BaseConPoolAsync();

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(Tarea((decimal)horas));

        Assert.False(ok);
        // Sin comprobar el «0.25» del texto: el servicio lo interpola con la cultura del proceso y
        // en media Europa saldría «0,25». Lo que importa es que rechace y diga de qué número habla.
        Assert.Contains("esfuerzo estimado", mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking());
    }

    /// <summary>
    /// Lo que se relee es exactamente lo que se guardó, con dos decimales y el medio hacia arriba.
    /// El redondeo se hace en el servicio y no en la columna: si lo hiciera la base, el mismo 1.005
    /// podría quedar en 1.00 en un motor y en 1.01 en otro, y nadie sabría de dónde salió.
    /// </summary>
    [Fact]
    public async Task El_esfuerzo_se_guarda_con_dos_decimales()
    {
        var db = await BaseConPoolAsync();

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(Tarea(1.005m));
        Assert.True(ok, mensaje);

        Assert.Equal(1.01m, Recargar(db, actividad!.Id).HorasEstimadas);
    }

    // ── Tomar un bug: la estimación es obligatoria ───────────────────────────────

    /// <summary>
    /// LA REGLA CENTRAL DEL MODELO: un bug no se toma sin decir en cuántas horas se cree resolverlo,
    /// y el intento fallido no deja NADA escrito.
    ///
    /// <para>Lo segundo importa tanto como lo primero. Si el rechazo dejara la actividad reclamada
    /// —aunque fuera sin número—, la estimación se acabaría pidiendo después, y una estimación
    /// escrita después de empezar ya no es una estimación: quien la escribe sabe lo que le costó.
    /// Por eso se comprueba la actividad entera, su checklist y el cronómetro.</para>
    /// </summary>
    [Fact]
    public async Task No_se_puede_tomar_un_bug_sin_estimacion_y_el_intento_no_deja_nada_escrito()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m));

        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, horasEstimadas: null);

        Assert.False(ok);
        Assert.Contains("horas", mensaje);

        // La actividad sigue exactamente como estaba: libre y sin dueño.
        var sinTocar = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, sinTocar.Status);
        Assert.Null(sinTocar.ClaimedByDeveloperId);
        Assert.Null(sinTocar.ClaimedAt);
        Assert.Null(sinTocar.ClaimDeadlineAt);
        Assert.Null(sinTocar.HorasEstimadas);
        Assert.Null(sinTocar.HorasEstimadasEnUtc);
        Assert.Null(sinTocar.LinkedDevActivityId);

        // Ni checklist copiado, ni actividad libre para el cronómetro, ni línea en la bitácora.
        Assert.Empty(db.PoolActivityChecklistItems.AsNoTracking());
        Assert.Empty(db.DevActivities.AsNoTracking());
        Assert.DoesNotContain(db.AuditLogs.AsNoTracking().ToList(),
                              l => (l.Details ?? "").Contains("Tomada"));

        // Y sigue libre para cualquiera: quien no pudo tomarla no la dejó bloqueada.
        Assert.Contains(await Svc(db, Dev(dev)).DisponiblesAsync(), a => a.Id == actividad.Id);
    }

    /// <summary>
    /// LA CARRERA: dos personas mandan su estimación para el mismo bug y solo una lo consigue. La que
    /// pierde no deja su número escrito.
    ///
    /// <para>Es la razón de que la estimación viaje DENTRO del mismo UPDATE condicional que gana el
    /// reclamo y no en un guardado posterior. Si fuera un segundo paso, la actividad podría acabar a
    /// nombre de una persona con el número de la otra: la comparación contra el cronómetro se haría
    /// entonces contra una estimación que su dueño nunca escribió, y no fallaría nada — saldría un
    /// número plausible y equivocado.</para>
    ///
    /// <para>Las dos estimaciones son DISTINTAS a propósito. Con el mismo número la prueba pasaría
    /// aunque ganara el perdedor, que es exactamente el fallo que busca.</para>
    /// </summary>
    [Fact]
    public async Task Quien_pierde_la_carrera_por_un_bug_no_deja_su_estimacion_escrita()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m));

        var primera = await Svc(db, Dev(ana)).TomarAsync(actividad!.Id, ana, horasEstimadas: 3m);
        var segunda = await Svc(db, Dev(beto, userId: 2)).TomarAsync(actividad.Id, beto, horasEstimadas: 40m);

        Assert.True(primera.ok, primera.mensaje);
        Assert.False(segunda.ok);

        var tomada = Recargar(db, actividad.Id);
        Assert.Equal(ana, tomada.ClaimedByDeveloperId);
        // Las 3 h de Ana, que es quien la tiene; NUNCA las 40 de Beto, que no la tomó.
        Assert.Equal(3m, tomada.HorasEstimadas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(1001)]
    public async Task Una_estimacion_fuera_de_rango_al_tomar_se_rechaza_sin_reclamar_la_actividad(double horas)
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m));

        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, (decimal)horas);

        Assert.False(ok);
        Assert.Contains("estimación", mensaje);

        var sinTocar = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, sinTocar.Status);
        Assert.Null(sinTocar.ClaimedByDeveloperId);
        Assert.Null(sinTocar.HorasEstimadas);
        Assert.Empty(db.DevActivities.AsNoTracking());
    }

    /// <summary>
    /// El sello dice CUÁNDO se capturó la estimación, y aquí tiene que coincidir con el momento de
    /// tomarla. Es lo que permite auditar después que el número se escribió antes de empezar y no a
    /// mitad del trabajo: sin el sello, esa promesa no se puede comprobar.
    /// </summary>
    [Fact]
    public async Task Al_tomar_un_bug_la_estimacion_queda_sellada_en_el_momento_de_tomarlo()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m));

        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 3.5m);
        Assert.True(ok, mensaje);

        var tomada = Recargar(db, actividad.Id);
        Assert.Equal(3.5m, tomada.HorasEstimadas);
        Assert.NotNull(tomada.HorasEstimadasEnUtc);
        Assert.NotNull(tomada.ClaimedAt);
        Assert.Equal(tomada.ClaimedAt!.Value, tomada.HorasEstimadasEnUtc!.Value, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// En una tarea el esfuerzo ya lo fijó el líder, así que mandar otro al tomarla se rechaza en vez
    /// de ignorarse: aceptarlo en silencio dejaría que quien la toma se rebaje el número contra el
    /// que después se va a comparar su cronómetro.
    /// </summary>
    [Fact]
    public async Task Quien_toma_una_tarea_no_puede_cambiar_el_esfuerzo_que_puso_el_lider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Tarea(6m));

        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 40m);

        Assert.False(ok);
        Assert.Contains("lo fija el líder", mensaje);

        var sinTocar = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, sinTocar.Status);
        Assert.Equal(6m, sinTocar.HorasEstimadas);
    }

    /// <summary>
    /// Tomar una tarea NO puede borrar el esfuerzo del líder. Es el fallo más fácil de escribir aquí:
    /// al tomarla la estimación propia es nula, y escribirla tal cual dejaría a quien la tomó sin
    /// nada contra qué comparar su cronómetro, en silencio.
    /// </summary>
    [Fact]
    public async Task Tomar_una_tarea_conserva_el_esfuerzo_que_estimo_el_lider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Tarea(6m));
        var selloAlPublicar = Recargar(db, actividad!.Id).HorasEstimadasEnUtc;

        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, horasEstimadas: null);
        Assert.True(ok, mensaje);

        var tomada = Recargar(db, actividad.Id);
        Assert.Equal(6m, tomada.HorasEstimadas);
        // Y el sello sigue siendo el de cuando el líder lo escribió, no el de ahora: quien la tomó no
        // estimó nada, así que no hay nada nuevo que fechar.
        Assert.Equal(selloAlPublicar, tomada.HorasEstimadasEnUtc);
    }

    // ── El plazo se calcula sumando HORAS ────────────────────────────────────────

    /// <summary>
    /// La conversión entera se juega aquí: 10 significa diez HORAS, no diez días. Si alguien volviera
    /// a sumar días, el margen de abajo lo delata — el plazo saldría 240 h en vez de 10.
    /// </summary>
    [Fact]
    public async Task El_plazo_de_un_bug_se_calcula_sumando_HORAS_y_no_dias()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(plazoEnHoras: 10m));

        var antesDeTomar = DateTime.UtcNow;
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 4m)).ok);

        var limite = Recargar(db, actividad.Id).ClaimDeadlineAt;
        Assert.NotNull(limite);
        Assert.InRange((limite!.Value - antesDeTomar).TotalHours, 9.9, 10.1);
    }

    /// <summary>
    /// Una tarea no lleva plazo propio, así que el suyo sale de su celda de la matriz — ya en horas.
    /// </summary>
    [Fact]
    public async Task Sin_plazo_propio_el_plazo_sale_de_la_matriz_en_horas()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin())
            .CrearAsync(Tarea(6m, complejidad: PoolComplexity.Media));

        var antesDeTomar = DateTime.UtcNow;
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev)).ok);

        var esperadas = (double)HorasDeLaMatriz(PoolWorkType.Tarea, PoolComplexity.Media);
        var limite = Recargar(db, actividad.Id).ClaimDeadlineAt;
        Assert.NotNull(limite);
        Assert.InRange((limite!.Value - antesDeTomar).TotalHours, esperadas - 0.1, esperadas + 0.1);
    }

    /// <summary>Cero en la celda significa «sin fecha límite», y sigue significándolo en horas.</summary>
    [Fact]
    public async Task Una_celda_de_la_matriz_con_cero_horas_deja_la_actividad_sin_fecha_limite()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db);

        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz.Single(m => m.WorkType == PoolWorkType.Tarea && m.Complexity == PoolComplexity.Media)
              .HorasLimite = 0m;
        Assert.True((await Svc(db, admin).GuardarMatrizAsync(matriz)).ok);

        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Tarea(6m, complejidad: PoolComplexity.Media));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev)).ok);

        Assert.Null(Recargar(db, actividad.Id).ClaimDeadlineAt);
    }

    // ── La convivencia con el escritorio ─────────────────────────────────────────

    /// <summary>
    /// UN BUG PUEDE LLEGAR SIN PLAZO PROPIO, y entonces se le aplica el de la matriz.
    ///
    /// <para>Parece contradecir la regla de que un bug lleva siempre el plazo del líder, y no la
    /// contradice: esa regla vale al PUBLICAR desde la web. Pero hasta el día del corte el
    /// ESCRITORIO sigue creando actividades en esta misma base y no escribe horas, y las que ya
    /// existían tampoco las tienen si su plazo en días venía vacío. Todas ésas llegan aquí con el
    /// plazo en nulo.</para>
    ///
    /// <para>Por eso <c>TomarAsync</c> no puede exigir que un bug traiga plazo propio: si lo
    /// exigiera, ninguna de esas actividades se podría tomar y el pool se quedaría bloqueado a mitad
    /// de la convivencia. Esta prueba está para que nadie «apriete» esa validación por simetría con
    /// la de publicar.</para>
    /// </summary>
    [Fact]
    public async Task Un_bug_que_llega_sin_plazo_propio_toma_el_de_la_matriz_y_se_puede_tomar()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m, complejidad: PoolComplexity.Media));

        // Como la dejaría el escritorio: con su plazo en días y sin horas.
        var fila = db.PoolActivities.Single(a => a.Id == actividad!.Id);
        fila.HorasLimite = null;
        fila.DiasLimite  = 5;
        db.SaveChanges();

        var antesDeTomar = DateTime.UtcNow;
        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 3m);
        Assert.True(ok, mensaje);

        var esperadas = (double)HorasDeLaMatriz(PoolWorkType.Bug, PoolComplexity.Media);
        var limite = Recargar(db, actividad.Id).ClaimDeadlineAt;
        Assert.NotNull(limite);
        Assert.InRange((limite!.Value - antesDeTomar).TotalHours, esperadas - 0.1, esperadas + 0.1);
    }

    /// <summary>
    /// Cambiar la matriz desde la web NO toca la columna de días, que es la que sigue leyendo el
    /// escritorio. La consecuencia es real y está asumida: mientras los dos sistemas convivan, cada
    /// uno usa su propia columna, así que un plazo cambiado aquí no se ve allá.
    ///
    /// <para>Se prueba para que quede constancia de que es una decisión y no un olvido: escribir
    /// también los días sería mantener dos verdades sobre lo mismo —una en horas y otra en jornadas,
    /// que no se pueden convertir sin perder información— y se contradirían al primer valor que no
    /// fuera múltiplo de ocho.</para>
    /// </summary>
    [Fact]
    public async Task Cambiar_la_matriz_desde_la_web_no_toca_los_dias_que_lee_el_escritorio()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        // Una matriz como la de una base migrada: con sus días viejos todavía puestos.
        foreach (var fila in db.PoolPointsMatrix) fila.DiasLimite = 5;
        db.SaveChanges();

        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz.Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Media)
              .HorasLimite = 12m;
        Assert.True((await Svc(db, admin).GuardarMatrizAsync(matriz)).ok);

        var celda = (await Svc(db, admin).ObtenerMatrizAsync())
            .Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Media);
        Assert.Equal(12m, celda.HorasLimite);
        Assert.Equal(5, celda.DiasLimite);   // intacta: el escritorio seguirá viendo cinco días
    }

    // ── Cambiar el tipo de trabajo ───────────────────────────────────────────────

    /// <summary>
    /// Al cruzar la frontera bug / no-bug, el esfuerzo cambia de dueño: en una tarea lo escribe el
    /// líder y en un bug quien la toma. Conservarlo dejaría un número atribuido a quien no lo
    /// escribió, y —peor— daría por satisfecha la obligación de estimar de quien tome el bug después.
    ///
    /// <para>Ésta es la invariante que permite DERIVAR el autor del esfuerzo del tipo de la actividad
    /// en vez de guardarlo en una columna aparte. Si se rompe, todo lo que lea ese número estará
    /// atribuyéndoselo a la persona equivocada.</para>
    /// </summary>
    [Fact]
    public async Task Cambiar_una_tarea_a_bug_borra_el_esfuerzo_que_ya_no_es_de_quien_lo_escribio()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db);

        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Tarea(6m));
        Assert.Equal(6m, Recargar(db, actividad!.Id).HorasEstimadas);

        var (ok, mensaje) = await Svc(db, admin).EditarAsync(actividad.Id, Bug(16m, "Ahora resulta que es un bug"));
        Assert.True(ok, mensaje);

        var ahoraBug = Recargar(db, actividad.Id);
        Assert.Equal(PoolWorkType.Bug, ahoraBug.WorkType);
        Assert.Null(ahoraBug.HorasEstimadas);
        Assert.Null(ahoraBug.HorasEstimadasEnUtc);
        Assert.Equal(16m, ahoraBug.HorasLimite);

        // Y la consecuencia que importa: al siguiente sí se le pide el número, porque el hueco quedó
        // vacío. Si se hubiera conservado el del líder, no se le habría preguntado nunca.
        var (tomada, error) = await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, horasEstimadas: null);
        Assert.False(tomada);
        Assert.Contains("horas", error);
    }

    /// <summary>
    /// El camino de vuelta: lo que quede guardado tras convertir un bug en tarea es el número DEL
    /// LÍDER y con el sello de ahora, nunca el que arrastrara la actividad de antes.
    /// </summary>
    [Fact]
    public async Task Cambiar_un_bug_a_tarea_deja_el_esfuerzo_del_lider_y_no_el_que_hubiera_antes()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Bug(16m));

        // Un bug con estimación de quien lo tuvo tomado. Se escribe directo porque el servicio la
        // borra al soltar el reclamo (se prueba abajo): es la fila que quedaría si esa limpieza
        // fallara, y es justo el número que el cambio de tipo no debe dejar pasar como del líder.
        var fila = db.PoolActivities.Single(a => a.Id == actividad!.Id);
        fila.HorasEstimadas      = 99m;
        fila.HorasEstimadasEnUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.SaveChanges();

        var (ok, mensaje) = await Svc(db, admin).EditarAsync(actividad!.Id, Tarea(6m, "Al final era una tarea"));
        Assert.True(ok, mensaje);

        var ahoraTarea = Recargar(db, actividad.Id);
        Assert.Equal(PoolWorkType.Tarea, ahoraTarea.WorkType);
        Assert.Equal(6m, ahoraTarea.HorasEstimadas);
        Assert.NotEqual(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), ahoraTarea.HorasEstimadasEnUtc);
    }

    /// <summary>
    /// Editar SIN cruzar la frontera no toca al de nadie: en tarea y requerimiento el autor es el
    /// mismo líder, así que borrar ahí solo le haría recapturar un número que sigue siendo suyo.
    /// </summary>
    [Fact]
    public async Task Cambiar_de_tarea_a_requerimiento_conserva_el_esfuerzo_del_lider()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Tarea(6m));

        var aRequerimiento = Tarea(6m, "Ahora es un requerimiento");
        aRequerimiento.WorkType = PoolWorkType.Requerimiento;
        Assert.True((await Svc(db, admin).EditarAsync(actividad!.Id, aRequerimiento)).ok);

        var releida = Recargar(db, actividad.Id);
        Assert.Equal(PoolWorkType.Requerimiento, releida.WorkType);
        Assert.Equal(6m, releida.HorasEstimadas);
    }

    // ── Soltar el reclamo ────────────────────────────────────────────────────────

    /// <summary>
    /// La segunda invariante que sostiene la derivación del autor: la estimación de un bug se va con
    /// quien la escribió. Si se quedara, el siguiente heredaría un número ajeno y la obligación de
    /// estimar quedaría satisfecha por algo que esa persona no dijo.
    /// </summary>
    [Fact]
    public async Task Devolver_un_bug_se_lleva_la_estimacion_de_quien_lo_tenia()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Bug(16m));

        Assert.True((await Svc(db, Dev(ana)).TomarAsync(actividad!.Id, ana, 3m)).ok);
        Assert.True((await Svc(db, Dev(ana)).DevolverAsync(actividad.Id, ana, "me asignaron otra cosa")).ok);

        var devuelta = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, devuelta.Status);
        Assert.Null(devuelta.HorasEstimadas);
        Assert.Null(devuelta.HorasEstimadasEnUtc);

        // Y a Beto se le vuelve a pedir: no hereda el número de Ana.
        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2)).TomarAsync(actividad.Id, beto, horasEstimadas: null);
        Assert.False(ok);
        Assert.Contains("horas", mensaje);
    }

    [Fact]
    public async Task Liberar_un_bug_tambien_se_lleva_la_estimacion()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Bug(16m));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 3m)).ok);

        Assert.True((await Svc(db, admin).LiberarAsync(actividad.Id, "se fue de vacaciones")).ok);

        var liberada = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, liberada.Status);
        Assert.Null(liberada.HorasEstimadas);
        Assert.Null(liberada.HorasEstimadasEnUtc);
    }

    /// <summary>
    /// La cara contraria: en una tarea el esfuerzo es del LÍDER y sigue siendo válido con la
    /// actividad de vuelta en el pool. Borrarlo ahí le haría recapturar un número que nunca dejó de
    /// ser suyo, y dejaría la tarea publicada sin la estimación que el modelo le exige.
    /// </summary>
    [Fact]
    public async Task Devolver_una_tarea_conserva_el_esfuerzo_del_lider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Tarea(6m));

        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev)).ok);
        Assert.True((await Svc(db, Dev(dev)).DevolverAsync(actividad.Id, dev, "no alcanzo")).ok);

        var devuelta = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, devuelta.Status);
        Assert.Equal(6m, devuelta.HorasEstimadas);
        Assert.NotNull(devuelta.HorasEstimadasEnUtc);
    }

    /// <summary>
    /// Devolverla PARA CORREGIR no es soltar el reclamo: la actividad sigue siendo de quien la tomó,
    /// así que su estimación sigue siendo suya y tiene que sobrevivir a la vuelta. Borrarla aquí
    /// dejaría a esa persona corrigiendo sin nada contra qué contrastar su cronómetro, y volver a
    /// pedírsela a mitad del trabajo ya no serviría: a estas alturas sabe lo que le costó.
    /// </summary>
    [Fact]
    public async Task Rechazar_una_entrega_no_borra_la_estimacion_de_quien_sigue_teniendola()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Bug(16m));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 3m)).ok);
        await CompletarChecklistAsync(db, Dev(dev), actividad.Id, dev);
        Assert.True((await Svc(db, Dev(dev)).EntregarAsync(actividad.Id, dev)).ok);

        Assert.True((await Svc(db, admin).RechazarAsync(actividad.Id, "falta la prueba en QA")).ok);

        var devuelta = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Devuelta, devuelta.Status);
        Assert.Equal(dev, devuelta.ClaimedByDeveloperId);   // sigue siendo suya
        Assert.Equal(3m, devuelta.HorasEstimadas);
    }

    /// <summary>
    /// Y al aceptarla el número se queda. Es el momento en que por fin sirve para algo: es cuando el
    /// líder puede poner las horas que se dijeron al lado de las que marcó el cronómetro. Perderlo
    /// justo aquí dejaría todo el cambio sin la comparación para la que existe.
    /// </summary>
    [Fact]
    public async Task Aceptar_conserva_la_estimacion_que_hay_que_contrastar_con_el_cronometro()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db);
        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Bug(16m));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, 3m)).ok);
        await CompletarChecklistAsync(db, Dev(dev), actividad.Id, dev);
        Assert.True((await Svc(db, Dev(dev)).EntregarAsync(actividad.Id, dev)).ok);

        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        var aceptada = Recargar(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Aceptada, aceptada.Status);
        Assert.Equal(3m, aceptada.HorasEstimadas);
        Assert.NotNull(aceptada.HorasEstimadasEnUtc);
    }

    // ── La matriz, ya en horas ───────────────────────────────────────────────────

    [Fact]
    public async Task La_matriz_guarda_el_plazo_en_horas_con_dos_decimales()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz.Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Baja)
              .HorasLimite = 7.505m;
        Assert.True((await Svc(db, admin).GuardarMatrizAsync(matriz)).ok);

        var guardada = (await Svc(db, admin).ObtenerMatrizAsync())
            .Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Baja);
        Assert.Equal(7.51m, guardada.HorasLimite);
        // Y la columna vieja de días no se mueve: es la que sigue leyendo el escritorio.
        Assert.Equal(0, guardada.DiasLimite);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2921)]
    public async Task La_matriz_rechaza_un_plazo_fuera_de_rango(int horas)
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        var celda = matriz[0];
        var comoEstaba = celda.HorasLimite;
        celda.HorasLimite = horas;

        var (ok, mensaje) = await Svc(db, admin).GuardarMatrizAsync(matriz);

        Assert.False(ok);
        Assert.Contains("horas", mensaje);
        // Y no guardó a medias: la celda que ya estaba bien tampoco se movió.
        Assert.Equal(comoEstaba, (await Svc(db, admin).ObtenerMatrizAsync())
            .Single(m => m.WorkType == celda.WorkType && m.Complexity == celda.Complexity).HorasLimite);
    }

    /// <summary>
    /// El plazo crece con la complejidad, igual que los puntos. Si «muy alta» diera casi las mismas
    /// horas que «baja», la matriz estaría prometiendo lo mismo para trabajos que no se parecen.
    /// </summary>
    [Fact]
    public void Las_horas_de_la_matriz_de_partida_crecen_con_la_complejidad()
    {
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
        {
            var horas = PoolSeed.Matriz.Where(m => m.Tipo == tipo)
                .OrderBy(m => m.Complejidad).Select(m => m.Horas).ToList();
            for (int i = 1; i < horas.Count; i++)
                Assert.True(horas[i] > horas[i - 1], $"{tipo}: las horas no crecen con la complejidad.");
        }
    }
}
