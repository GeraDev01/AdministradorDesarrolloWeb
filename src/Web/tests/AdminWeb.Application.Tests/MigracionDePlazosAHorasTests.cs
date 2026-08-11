using AdminWeb.Application.Services;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MIGRACIÓN DE LOS PLAZOS DEL POOL, DE DÍAS A HORAS. De todo lo que se probó en este cambio,
/// esto es lo único que se ejecuta contra la base de PRODUCCIÓN, y una sola vez: si sale mal no hay
/// segundo intento, porque el error se vería como plazos raros semanas después.
///
/// <para><b>Qué se está probando exactamente.</b> Al arrancar, la API pasa el esquema por
/// <see cref="DatabaseMigrator.EnsureUpToDate"/>. Ese método añade las columnas de horas al lado de
/// las de días —sin borrar ni renombrar nada, porque la aplicación de ESCRITORIO sigue leyendo las
/// viejas en producción hasta el corte— y después rellena las nuevas multiplicando por ocho, que es
/// la jornada. Aquí se comprueban las dos mitades y, sobre todo, que la segunda no se repita.</para>
///
/// <para><b>Por qué la idempotencia es la prueba que más importa.</b> La API arranca muchas veces:
/// cada despliegue, cada reinicio, cada instancia nueva. Si la conversión volviera a correr,
/// multiplicaría por ocho lo ya convertido y un plazo de 40 h pasaría a 320 sin que nadie tocara
/// nada. Y el modo de fallo silencioso es peor: la marca vive en <c>AppSettings</c> y no en una
/// condición sobre los datos justamente porque «está en 0» y «está en NULL» son valores LEGÍTIMOS
/// —«sin fecha límite» y «usa el de la matriz»— que el líder pone a propósito, así que deducir de
/// ellos que «falta convertir» le desharía la decisión en el arranque siguiente.</para>
///
/// <para><b>Cómo se simula una base vieja.</b> Se crea una base con el modelo de hoy y se le QUITAN
/// las columnas de horas, que es exactamente la forma que tiene una base que solo ha visto el
/// escritorio. Así el migrador tiene que hacer las dos cosas de verdad —crearlas y rellenarlas— y no
/// solo la segunda. Si se le dieran ya creadas, la mitad del trabajo quedaría sin probar.</para>
/// </summary>
public class MigracionDePlazosAHorasTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// La matriz tal como la dejó el escritorio, EN DÍAS. Está escrita aquí a mano, y no leída de
    /// <see cref="PoolSeed"/>, porque es el dato de partida real: si mañana alguien retoca la
    /// siembra, esta tabla tiene que seguir diciendo lo que había en las bases que se van a migrar.
    /// Es también la que permite comprobar el ×8 contra un número que no salió del código nuevo.
    /// </summary>
    private static readonly (PoolWorkType Tipo, PoolComplexity Complejidad, int Puntos, int Dias)[] MatrizEnDias =
    [
        (PoolWorkType.Bug,           PoolComplexity.Baja,    5,  3),
        (PoolWorkType.Bug,           PoolComplexity.Media,   8,  5),
        (PoolWorkType.Bug,           PoolComplexity.Alta,   12,  8),
        (PoolWorkType.Bug,           PoolComplexity.MuyAlta, 18, 13),

        (PoolWorkType.Tarea,         PoolComplexity.Baja,    3,  3),
        (PoolWorkType.Tarea,         PoolComplexity.Media,   6,  5),
        (PoolWorkType.Tarea,         PoolComplexity.Alta,   10,  8),
        (PoolWorkType.Tarea,         PoolComplexity.MuyAlta, 15, 13),

        (PoolWorkType.Requerimiento, PoolComplexity.Baja,    8,  5),
        (PoolWorkType.Requerimiento, PoolComplexity.Media,  12,  8),
        (PoolWorkType.Requerimiento, PoolComplexity.Alta,   18, 13),
        (PoolWorkType.Requerimiento, PoolComplexity.MuyAlta, 25, 20),
    ];

    /// <summary>La jornada con la que se convierte. El mismo ocho que usan el migrador y la siembra.</summary>
    private const int HorasPorJornada = 8;

    private const string ClaveDeLaMarca = "PoolHorasConvertidas";

    /// <summary>
    /// Una base con la forma EXACTA que tiene una que solo ha visto el escritorio: con las columnas
    /// de días y sin las de horas.
    ///
    /// <para>Se construye quitándole a la base de hoy las columnas nuevas en vez de escribiendo el
    /// esquema viejo a mano, para que no haya dos definiciones del esquema que puedan divergir.</para>
    /// </summary>
    private AppDbContext BaseComoLaDejoElEscritorio()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities""   DROP COLUMN ""HorasLimite""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities""   DROP COLUMN ""HorasEstimadas""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities""   DROP COLUMN ""HorasEstimadasEnUtc""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolPointsMatrix"" DROP COLUMN ""HorasLimite""");

        Assert.False(TieneColumna(db, "PoolActivities", "HorasLimite"),
            "La base de partida no debería traer ya las columnas de horas: si las trae, el migrador " +
            "no tendría que crearlas y esa mitad del trabajo se quedaría sin probar.");
        return db;
    }

    /// <summary>Otro contexto contra la misma base, para leer con EF lo que escribió el SQL crudo.</summary>
    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private static bool TieneColumna(AppDbContext db, string tabla, string columna)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{tabla}')";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { if (abrir) conn.Close(); }
    }

    /// <summary>La matriz vieja, escrita con SQL crudo porque la base todavía no tiene las horas.</summary>
    private static void SembrarMatrizEnDias(AppDbContext db)
    {
        foreach (var (tipo, complejidad, puntos, dias) in MatrizEnDias)
            db.Database.ExecuteSqlRaw(
                @"INSERT INTO ""PoolPointsMatrix"" (""WorkType"", ""Complexity"", ""Points"", ""DiasLimite"", ""UpdatedAt"")
                  VALUES ({0}, {1}, {2}, {3}, {4})",
                (int)tipo, (int)complejidad, puntos, dias, DateTime.UtcNow);
    }

    /// <summary>Una actividad como la escribía el escritorio: con su plazo en DÍAS (o sin plazo propio).</summary>
    private static void SembrarActividad(AppDbContext db, string titulo, int? dias,
        PoolActivityStatus estado = PoolActivityStatus.Disponible, DateTime? limiteYaCalculado = null)
    {
        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""PoolActivities""
                (""Title"", ""WorkType"", ""Complexity"", ""Points"", ""Priority"", ""Status"",
                 ""DiasLimite"", ""ClaimDeadlineAt"", ""CreatedAt"", ""ReturnedCount"", ""ReviewRound"")
              VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, 0, 0)",
            titulo, (int)PoolWorkType.Bug, (int)PoolComplexity.Media, 8, (int)PoolPriority.Media,
            (int)estado, dias, limiteYaCalculado, DateTime.UtcNow);
    }

    private static decimal HorasDeLaCelda(AppDbContext db, PoolWorkType tipo, PoolComplexity complejidad) =>
        db.PoolPointsMatrix.AsNoTracking()
          .Single(m => m.WorkType == tipo && m.Complexity == complejidad).HorasLimite;

    private static int MarcasDeConversion(AppDbContext db) =>
        db.AppSettings.AsNoTracking().Count(s => s.Key == ClaveDeLaMarca);

    // ── El relleno ───────────────────────────────────────────────────────────────

    /// <summary>
    /// La conversión, entera: crea las columnas que faltaban y rellena las horas multiplicando por
    /// ocho lo que había en días.
    /// </summary>
    [Fact]
    public void Convierte_los_plazos_de_dias_a_horas_multiplicando_por_ocho()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Con plazo propio de 5 días", dias: 5);
        SembrarActividad(db, "Sin fecha límite", dias: 0);
        SembrarActividad(db, "Sin plazo propio", dias: null);

        DatabaseMigrator.EnsureUpToDate(db);

        var leido = OtroContexto(db);

        // La matriz, celda por celda.
        foreach (var (tipo, complejidad, _, dias) in MatrizEnDias)
            Assert.Equal(dias * HorasPorJornada, HorasDeLaCelda(leido, tipo, complejidad));

        var actividades = leido.PoolActivities.AsNoTracking().ToList();

        Assert.Equal(5 * HorasPorJornada,
            actividades.Single(a => a.Title == "Con plazo propio de 5 días").HorasLimite);

        // El 0 se conserva como 0: significa «sin fecha límite» y tiene que seguir significándolo.
        // Convertirlo en NULL lo cambiaría por «usa el de la matriz», que es lo contrario.
        Assert.Equal(0m, actividades.Single(a => a.Title == "Sin fecha límite").HorasLimite);

        // Y el NULL se conserva como NULL: es «usa el de la matriz». Rellenarlo con 0 le pondría
        // «sin fecha límite» a una actividad que sí tenía plazo, el de su celda.
        Assert.Null(actividades.Single(a => a.Title == "Sin plazo propio").HorasLimite);
    }

    /// <summary>
    /// Las columnas de DÍAS no se tocan: la aplicación de escritorio las sigue leyendo en producción
    /// hasta el día del corte, y una migración que las vaciara la rompería esa misma tarde sin que
    /// nadie lo relacionara con este cambio.
    /// </summary>
    [Fact]
    public void No_borra_ni_vacia_las_columnas_de_dias_que_sigue_leyendo_el_escritorio()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Con plazo propio de 5 días", dias: 5);

        DatabaseMigrator.EnsureUpToDate(db);

        var leido = OtroContexto(db);
        Assert.True(TieneColumna(leido, "PoolActivities", "DiasLimite"));
        Assert.True(TieneColumna(leido, "PoolPointsMatrix", "DiasLimite"));

        Assert.Equal(5, leido.PoolActivities.AsNoTracking().Single().DiasLimite);
        foreach (var (tipo, complejidad, _, dias) in MatrizEnDias)
            Assert.Equal(dias, leido.PoolPointsMatrix.AsNoTracking()
                .Single(m => m.WorkType == tipo && m.Complexity == complejidad).DiasLimite);
    }

    /// <summary>
    /// Un plazo YA CALCULADO no se recalcula. Es un compromiso que quien tomó la actividad ya vio en
    /// su pantalla; moverlo sería cambiar el trato a medio camino, y el recálculo siempre iría hacia
    /// atrás —cinco días naturales pasan a cuarenta horas de reloj—, así que la mañana siguiente al
    /// despliegue habría actividades pintadas de vencidas señalando a gente que no hizo nada mal.
    /// </summary>
    [Fact]
    public void No_recalcula_el_plazo_de_las_actividades_que_ya_estaban_tomadas()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        var comprometido = new DateTime(2030, 3, 1, 15, 0, 0, DateTimeKind.Utc);
        SembrarActividad(db, "Tomada antes del despliegue", dias: 5,
                         estado: PoolActivityStatus.Tomada, limiteYaCalculado: comprometido);

        DatabaseMigrator.EnsureUpToDate(db);

        var tomada = OtroContexto(db).PoolActivities.AsNoTracking().Single();
        Assert.Equal(comprometido, tomada.ClaimDeadlineAt!.Value, TimeSpan.FromSeconds(1));
        // El plazo configurado sí se convierte; el instante ya prometido, no. Son cosas distintas.
        Assert.Equal(5 * HorasPorJornada, tomada.HorasLimite);
    }

    /// <summary>
    /// El tope existe para que UNA celda disparatada no aborte la conversión ENTERA: la matriz vieja
    /// no acotaba los días por arriba, y 2 000 días × 8 no cabe en el <c>decimal(6,2)</c> del otro
    /// dialecto. Sin el tope, esa celda dejaría la base a medio convertir en SQL Server.
    /// </summary>
    [Fact]
    public void Una_celda_disparatada_se_recorta_y_no_impide_convertir_el_resto()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        db.Database.ExecuteSqlRaw(
            @"UPDATE ""PoolPointsMatrix"" SET ""DiasLimite"" = 2000 WHERE ""WorkType"" = {0} AND ""Complexity"" = {1}",
            (int)PoolWorkType.Bug, (int)PoolComplexity.Baja);

        DatabaseMigrator.EnsureUpToDate(db);

        var leido = OtroContexto(db);
        Assert.Equal(9999m, HorasDeLaCelda(leido, PoolWorkType.Bug, PoolComplexity.Baja));
        // Y las demás celdas se convirtieron igual: una fila absurda no se lleva por delante al resto.
        Assert.Equal(5 * HorasPorJornada, HorasDeLaCelda(leido, PoolWorkType.Bug, PoolComplexity.Media));
    }

    // ── La idempotencia: la prueba que de verdad protege producción ──────────────

    /// <summary>
    /// La API arranca muchas veces —cada despliegue, cada reinicio, cada instancia— y el migrador
    /// corre en todas: aplicar la conversión tres veces tiene que dejar los mismos números que
    /// aplicarla una.
    ///
    /// <para><b>Y hay que decir POR QUÉ se cumple, porque no es por donde parece.</b> La conversión
    /// escribe <c>HorasLimite = DiasLimite × 8</c>, no <c>HorasLimite × 8</c>: recalcula desde una
    /// columna que ella misma nunca toca. Así que el número es estable por construcción y esta prueba
    /// seguiría verde aunque la marca de <c>AppSettings</c> no sirviera para nada. Lo que la marca
    /// protege de verdad es lo que se escribió ENCIMA después, y eso es lo que prueban las dos de
    /// abajo — que son las que se pondrían rojas si la marca se rompiera.</para>
    /// </summary>
    [Fact]
    public void Aplicarla_varias_veces_deja_los_mismos_numeros_que_aplicarla_una()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Con plazo propio de 5 días", dias: 5);

        DatabaseMigrator.EnsureUpToDate(db);
        DatabaseMigrator.EnsureUpToDate(db);   // el arranque siguiente
        DatabaseMigrator.EnsureUpToDate(db);   // y el de después

        var leido = OtroContexto(db);
        foreach (var (tipo, complejidad, _, dias) in MatrizEnDias)
            Assert.Equal(dias * HorasPorJornada, HorasDeLaCelda(leido, tipo, complejidad));
        Assert.Equal(5 * HorasPorJornada, leido.PoolActivities.AsNoTracking().Single().HorasLimite);

        // Y la marca se escribió UNA vez. Dos filas serían la señal de que la conversión corrió dos
        // veces y de que la próxima también podría.
        Assert.Equal(1, MarcasDeConversion(leido));
    }

    /// <summary>
    /// El caso que hace que la marca tenga que vivir en <c>AppSettings</c> y no deducirse de los
    /// datos: después de convertir, el líder pone a mano un 0 en una celda («sin fecha límite») y
    /// vacía el plazo de una actividad («usa el de la matriz»). Los dos son valores legítimos y los
    /// dos son exactamente lo que una condición ingenua —<c>WHERE HorasLimite = 0</c> o
    /// <c>IS NULL</c>— leería como «esto todavía no se ha convertido».
    ///
    /// <para>Si el arranque siguiente se los revirtiera, la decisión del líder se desharía sola y
    /// nadie sabría por qué: no hay ningún error, ninguna excepción y ninguna traza. Es el fallo
    /// silencioso que esta prueba existe para impedir.</para>
    /// </summary>
    [Fact]
    public void No_deshace_el_plazo_que_el_lider_puso_a_mano_despues_de_convertir()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Con plazo propio de 5 días", dias: 5);

        DatabaseMigrator.EnsureUpToDate(db);

        // El líder decide: esta celda ya no tiene fecha límite, y esta actividad usará la matriz.
        var editor = OtroContexto(db);
        editor.PoolPointsMatrix
              .Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Media)
              .HorasLimite = 0m;
        editor.PoolActivities.Single().HorasLimite = null;
        editor.SaveChanges();

        DatabaseMigrator.EnsureUpToDate(db);   // el arranque siguiente

        var leido = OtroContexto(db);
        Assert.Equal(0m, HorasDeLaCelda(leido, PoolWorkType.Bug, PoolComplexity.Media));
        Assert.Null(leido.PoolActivities.AsNoTracking().Single().HorasLimite);
        // Los días viejos siguen ahí, que es lo que haría reaparecer las 40 h si la condición fuera
        // sobre los datos en vez de sobre la marca.
        Assert.Equal(5, leido.PoolActivities.AsNoTracking().Single().DiasLimite);
    }

    /// <summary>
    /// LA CONTRAPRUEBA, y la razón de que la de arriba no esté pasando por casualidad: se repite el
    /// mismo guion BORRANDO la marca, y entonces el arranque siguiente sí le deshace al líder lo que
    /// acababa de decidir.
    ///
    /// <para>Está escrita a propósito, aunque describa el comportamiento indeseado, por dos motivos.
    /// Primero, demuestra que la marca es lo que sostiene la prueba anterior: sin esta, un cambio que
    /// dejara la marca sin efecto no rompería ninguna prueba y el fallo llegaría a producción.
    /// Segundo, es el único modo de fallo real que le queda a esta migración, y deja negro sobre
    /// blanco por qué la descripción de esa fila de <c>AppSettings</c> dice «NO BORRAR».</para>
    /// </summary>
    [Fact]
    public void Sin_la_marca_el_arranque_siguiente_desharia_lo_que_decidio_el_lider()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Con plazo propio de 5 días", dias: 5);

        DatabaseMigrator.EnsureUpToDate(db);

        var editor = OtroContexto(db);
        editor.PoolPointsMatrix
              .Single(m => m.WorkType == PoolWorkType.Bug && m.Complexity == PoolComplexity.Media)
              .HorasLimite = 0m;
        editor.PoolActivities.Single().HorasLimite = null;
        editor.SaveChanges();

        // Alguien limpia la tabla de configuración sin saber qué era esa fila.
        db.Database.ExecuteSqlRaw(@"DELETE FROM ""AppSettings"" WHERE ""Key"" = {0}", ClaveDeLaMarca);

        DatabaseMigrator.EnsureUpToDate(db);

        // Y las decisiones del líder desaparecen: la celda vuelve a tener plazo y la actividad
        // recupera el suyo, sin error, sin excepción y sin traza. Con la marca puesta esto no ocurre.
        var leido = OtroContexto(db);
        Assert.Equal(5 * HorasPorJornada, HorasDeLaCelda(leido, PoolWorkType.Bug, PoolComplexity.Media));
        Assert.Equal(5 * HorasPorJornada, leido.PoolActivities.AsNoTracking().Single().HorasLimite);
    }

    // ── Una base recién creada ───────────────────────────────────────────────────

    /// <summary>
    /// Una base NUEVA no tiene nada que convertir, pero sí tiene que quedar MARCADA. Si no lo
    /// quedara, el arranque siguiente correría la conversión sobre una matriz ya sembrada en horas
    /// y con <c>DiasLimite = 0</c> —la web ya no lo escribe—, así que pondría todas las celdas a
    /// <c>0 × 8 = 0</c>: el pool entero se quedaría sin plazos de golpe.
    /// </summary>
    [Fact]
    public async Task Una_base_nueva_queda_marcada_y_el_arranque_siguiente_no_borra_los_plazos()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        // El orden real del arranque: primero el migrador, después la siembra.
        DatabaseMigrator.EnsureUpToDate(db);
        await PoolSeed.SembrarAsync(db);

        Assert.Equal(1, MarcasDeConversion(db));

        DatabaseMigrator.EnsureUpToDate(db);   // el segundo arranque

        var leido = OtroContexto(db);
        foreach (var (tipo, complejidad, _, horas) in PoolSeed.Matriz)
            Assert.Equal(horas, HorasDeLaCelda(leido, tipo, complejidad));
    }

    /// <summary>
    /// El ocho del migrador y el de la siembra tienen que ser EL MISMO. Si divergieran, una base
    /// nueva y una migrada arrancarían con matrices distintas y nadie sabría cuál es la buena — el
    /// tipo de diferencia que solo se descubre comparando dos instalaciones meses después.
    /// </summary>
    [Fact]
    public void Una_base_migrada_arranca_con_la_misma_matriz_que_una_recien_creada()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);

        DatabaseMigrator.EnsureUpToDate(db);

        var leido = OtroContexto(db);
        foreach (var (tipo, complejidad, _, horas) in PoolSeed.Matriz)
            Assert.Equal(horas, HorasDeLaCelda(leido, tipo, complejidad));
    }

    // ── Las columnas nuevas ──────────────────────────────────────────────────────

    /// <summary>
    /// El ALTER es lo que de verdad se ejecuta contra una base con datos: el CREATE del migrador casi
    /// nunca corre, porque <c>EnsureCreated</c> no toca una base que ya existe. Si las columnas no
    /// aparecieran, la aplicación entera fallaría al primer SELECT.
    /// </summary>
    [Fact]
    public void Crea_las_columnas_de_horas_en_una_base_que_no_las_tenia()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Una cualquiera", dias: 3);

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.True(TieneColumna(db, "PoolActivities", "HorasLimite"));
        Assert.True(TieneColumna(db, "PoolActivities", "HorasEstimadas"));
        Assert.True(TieneColumna(db, "PoolActivities", "HorasEstimadasEnUtc"));
        Assert.True(TieneColumna(db, "PoolPointsMatrix", "HorasLimite"));

        // Y la aplicación puede volver a leer y escribir por EF, que es la comprobación que importa:
        // una columna creada con el tipo equivocado se vería justo aquí.
        var leido = OtroContexto(db);
        var actividad = leido.PoolActivities.Single();
        actividad.HorasEstimadas      = 2.5m;
        actividad.HorasEstimadasEnUtc = DateTime.UtcNow;
        leido.SaveChanges();

        Assert.Equal(2.5m, OtroContexto(db).PoolActivities.AsNoTracking().Single().HorasEstimadas);
    }

    /// <summary>
    /// El esfuerzo NO se rellena con nada. Solo se convierten los PLAZOS, que son los que existían en
    /// días; inventarle una estimación a lo que ya está en curso sería fabricar el número contra el
    /// que después se compara el cronómetro.
    /// </summary>
    [Fact]
    public void No_se_inventa_un_esfuerzo_para_lo_que_ya_estaba_en_curso()
    {
        var db = BaseComoLaDejoElEscritorio();
        SembrarMatrizEnDias(db);
        SembrarActividad(db, "Tomada antes del despliegue", dias: 5, estado: PoolActivityStatus.Tomada);

        DatabaseMigrator.EnsureUpToDate(db);

        var tomada = OtroContexto(db).PoolActivities.AsNoTracking().Single();
        Assert.Null(tomada.HorasEstimadas);
        Assert.Null(tomada.HorasEstimadasEnUtc);
    }
}
