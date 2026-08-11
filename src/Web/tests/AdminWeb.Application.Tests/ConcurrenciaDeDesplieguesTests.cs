using System.Diagnostics;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AdminWeb.Application.Tests;

// ─────────────────────────────────────────────────────────────────────────────────
//  Dos despliegues a la vez, y qué pasa cuando el servidor que corría uno se muere.
//
//  Todo esto se prueba sobre SQLite y NINGUNA de estas pruebas toca la red ni una base de verdad.
//  Conviene saber qué significa eso exactamente para no leer de más:
//
//   · En producción, quien serializa el «comprobar y registrar» es sp_getapplock, que es lo único
//     que ven todas las instancias. Sobre SQLite ese camino ni se pisa y queda el cerrojo de
//     proceso, que ahí es suficiente porque SQLite es un archivo local de una aplicación que corre
//     en un único proceso. Lo que estas pruebas comprueban es el COMPORTAMIENTO —que de dos
//     lanzamientos simultáneos solo pasa uno, y que el candado se suelta pase lo que pase—, que es
//     el mismo con los dos mecanismos.
//
//   · La visibilidad ENTRE INSTANCIAS sí se prueba de verdad, y es lo más importante de aquí: se
//     monta un segundo contexto con su propio ejecutor —o sea, otra instancia de la API, con su
//     memoria vacía— y se comprueba que ve el despliegue de la primera. Eso solo puede salir bien
//     si la ocupación se lee de la base, que es el agujero que se estaba tapando.
// ─────────────────────────────────────────────────────────────────────────────────

public class ConcurrenciaDeDesplieguesTests
{
    // ── Andamiaje ────────────────────────────────────────────────────────────────
    //
    // Base propia y no TestDb.New(), porque aquí hacen falta DOS contextos sobre el MISMO archivo:
    // es la única forma de tener dos instancias de la API mirando la misma base. El nombre lleva el
    // prefijo de siempre para que el barrido de TestDb también se lleve estos archivos.

    private static string NuevaBase() =>
        Path.Combine(Path.GetTempPath(), "adminweb_" + Guid.NewGuid().ToString("N") + ".db");

    /// <summary>
    /// Un contexto más contra la misma base. El plazo generoso es para SQLite: dos conexiones que
    /// escriben en el mismo archivo se esperan en vez de fallar con «database is locked», que es un
    /// fallo del andamiaje y no del código que se está probando.
    /// </summary>
    private static AppDbContext Abrir(string archivo)
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={archivo};Default Timeout=30")
            .Options;

        var db = new AppDbContext(opciones);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Un fabricante de ámbitos que no entrega nada.
    ///
    /// El despliegue de verdad NO debe correr en estas pruebas: lo que se comprueba es quién llega a
    /// registrarse, no qué se sube. Con esto, el trabajo que arranca al ganar la carrera muere en el
    /// acto sin tocar la base ni la red —el ejecutor lo tiene previsto y lo anota—, así que las
    /// afirmaciones de cada prueba miran una base que nadie está cambiando por debajo.
    /// </summary>
    private sealed class AmbitosVacios : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type servicio) => null;
        public void Dispose() { }
    }

    private sealed class AvisosMudos : IAvisosDeDespliegue
    {
        public Task AvanceAsync(int jobId, AvanceDeDespliegueDto avance) => Task.CompletedTask;
        public Task RegistroAsync(int jobId, RenglonDeBitacoraDto renglon) => Task.CompletedTask;
        public Task FinAsync(int jobId, FinDeDespliegueDto fin) => Task.CompletedTask;
    }

    /// <summary>Un ejecutor nuevo es, a efectos de esto, una instancia nueva de la API: memoria vacía.</summary>
    private static EjecutorDeDespliegues Ejecutor() =>
        new(new AmbitosVacios(), new AvisosMudos(), NullLogger<EjecutorDeDespliegues>.Instance);

    private static DeploymentService Servicio(AppDbContext db, EjecutorDeDespliegues ejecutor)
    {
        var quien = UsuarioDePrueba.Como(UserRole.Admin);
        return new DeploymentService(db, quien, new AuditService(db, quien, new OrigenDePrueba()), ejecutor);
    }

    /// <summary>Un sistema, una versión y los servidores que se le pidan.</summary>
    private static (int versionId, List<int> servidorIds) Sembrar(AppDbContext db, params string[] nombres)
    {
        var sistema = new AppSystem { Name = "Portal", IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();

        var version = new AppRelease { AppSystemId = sistema.Id, Version = "2.4.1", ZipLocalPath = "no-se-usa.zip" };
        db.AppReleases.Add(version);

        var servidores = nombres.Select(n => new DeploymentTarget
        {
            Nombre = n,
            Host = $"ftps://{n}.example",
            Puerto = 21,
            Usuario = "publicador",
            Contrasena = ProtectorPortable.Cifrar("s3cr3t0-que-no-debe-salir"),
            RutaRemota = "/site/wwwroot",
            IsActive = true
        }).ToList();
        db.DeploymentTargets.AddRange(servidores);
        db.SaveChanges();

        return (version.Id, [.. servidores.Select(s => s.Id)]);
    }

    /// <summary>Una petición completa y correcta: checklist marcado entero y nota escrita.</summary>
    private static LanzarDespliegueRequest Peticion(int versionId, params int[] servidorIds) =>
        new(versionId, servidorIds, null, null, [.. DeploymentChecklist.Puntos.Select(p => p.Clave)], "CAB-233");

    /// <summary>
    /// Un despliegue ya en marcha, tal como lo dejaría OTRA instancia: su trabajo «En curso», su
    /// perfil congelado apuntando a los servidores y su señal de vida con la antigüedad que se pida.
    /// </summary>
    private static int DespliegueEnCurso(
        AppDbContext db, int versionId, DateTime? senalUtc, params int[] servidorIds)
    {
        var perfil = new DeploymentProfile { Name = "⚡ de otra instancia", IsAdHoc = true };
        db.DeploymentProfiles.Add(perfil);
        db.SaveChanges();

        int orden = 0;
        foreach (var id in servidorIds)
            db.DeploymentProfileTargets.Add(new DeploymentProfileTarget
            {
                ProfileId = perfil.Id, TargetId = id, Order = orden++
            });

        var job = new DeploymentJob
        {
            AppReleaseId = versionId,
            DeploymentProfileId = perfil.Id,
            Status = JobStatus.EnCurso,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            TargetsTotal = servidorIds.Length
        };
        db.DeploymentJobs.Add(job);
        db.SaveChanges();

        if (senalUtc is DateTime cuando)
            SenalDeVidaDelDespliegue.RefrescarAsync(db, job.Id, cuando).GetAwaiter().GetResult();

        return job.Id;
    }

    private static DeploymentLogEntry? Senal(AppDbContext db, int jobId) =>
        db.DeploymentLogEntries.AsNoTracking()
            .FirstOrDefault(l => l.JobId == jobId && l.Message == SenalDeVidaDelDespliegue.Marca);

    // ── Agujero 1: la ventana entre comprobar y registrar ────────────────────────

    [Fact]
    public async Task Dos_lanzamientos_a_la_vez_sobre_el_mismo_servidor_y_solo_uno_pasa()
    {
        var archivo = NuevaBase();
        using var db1 = Abrir(archivo);
        using var db2 = Abrir(archivo);

        var (versionId, servidores) = Sembrar(db1, "prod-1");
        var peticion = Peticion(versionId, servidores[0]);

        // Dos instancias distintas —dos contextos, dos ejecutores— pulsando el botón a la vez. Antes
        // pasaban las dos: entre comprobar que el servidor estaba libre y dejar el trabajo apuntado
        // había varios await, y cada una comprobaba mientras la otra todavía no se había registrado.
        var resultados = await Task.WhenAll(
            Servicio(db1, Ejecutor()).LanzarAsync(peticion),
            Servicio(db2, Ejecutor()).LanzarAsync(peticion));

        Assert.Single(resultados, r => r.ok);

        var rechazado = Assert.Single(resultados, r => !r.ok);
        Assert.Contains("está recibiendo otro despliegue", rechazado.mensaje);

        // Y el que no pasó no dejó rastro: ni un trabajo de más en el historial.
        using var comprobacion = Abrir(archivo);
        Assert.Equal(1, await comprobacion.DeploymentJobs.CountAsync());
    }

    [Fact]
    public async Task El_candado_se_suelta_aunque_lo_de_dentro_reviente()
    {
        using var db = TestDb.New();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var candado = await CandadoDelRegistroDeDespliegues.TomarAsync(db);
            Assert.NotNull(candado);
            throw new InvalidOperationException("algo revienta a media faena");
        });

        // Un candado filtrado deja sin desplegar a TODO el mundo hasta que alguien reinicie, que es
        // peor que el problema que resuelve. Si se hubiera quedado tomado, esto tardaría los quince
        // segundos del plazo y volvería con null en vez de con un candado.
        var reloj = Stopwatch.StartNew();
        await using var siguiente = await CandadoDelRegistroDeDespliegues.TomarAsync(db);
        reloj.Stop();

        Assert.NotNull(siguiente);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(5),
            $"El candado no se soltó: el siguiente tardó {reloj.Elapsed.TotalSeconds:0.0}s en conseguirlo.");
    }

    // ── Agujero 2: la guarda era de un solo proceso ──────────────────────────────

    [Fact]
    public async Task Un_despliegue_de_OTRA_instancia_bloquea_su_servidor_y_uno_huerfano_no()
    {
        var archivo = NuevaBase();
        using var db = Abrir(archivo);
        using var otraInstancia = Abrir(archivo);

        var (versionId, servidores) = Sembrar(db, "prod-1");

        // Lo que dejaría otra instancia que está desplegando ahora mismo. Esta no sabe nada de él:
        // su ejecutor acaba de nacer y su registro en memoria está vacío.
        int ajeno = DespliegueEnCurso(otraInstancia, versionId, DateTime.UtcNow, servidores[0]);

        var servicio = Servicio(db, Ejecutor());
        var (ok, mensaje, _) = await servicio.LanzarAsync(Peticion(versionId, servidores[0]));

        Assert.False(ok);
        Assert.Contains("prod-1", mensaje);
        Assert.Contains("está recibiendo otro despliegue", mensaje);

        // Y ahora el mismo trabajo, pero sin nadie detrás: la instancia que lo corría se murió y su
        // señal caducó. El servidor tiene que quedar LIBRE sin que haga falta reiniciar nada — si un
        // trabajo huérfano bloqueara su destino para siempre, la protección sería peor que el daño.
        var senal = otraInstancia.DeploymentLogEntries
            .First(l => l.JobId == ajeno && l.Message == SenalDeVidaDelDespliegue.Marca);
        senal.Timestamp = DateTime.UtcNow - SenalDeVidaDelDespliegue.Tolerancia - TimeSpan.FromMinutes(1);
        await otraInstancia.SaveChangesAsync();

        // De paso, esto solo puede pasar si el rechazo anterior soltó el candado al volverse por
        // donde vino: se sale del bloque protegido con un return, no por su final.
        var (segundo, _, jobId) = await servicio.LanzarAsync(Peticion(versionId, servidores[0]));

        Assert.True(segundo);
        Assert.NotNull(jobId);
    }

    // ── Agujero 3: el reinicio a media faena ─────────────────────────────────────

    [Fact]
    public async Task La_reconciliacion_cierra_al_huerfano_y_no_toca_al_que_otra_instancia_corre()
    {
        var archivo = NuevaBase();
        using var db = Abrir(archivo);

        var (versionId, servidores) = Sembrar(db, "prod-1", "prod-2");

        var muerto = DateTime.UtcNow - SenalDeVidaDelDespliegue.Tolerancia - TimeSpan.FromMinutes(3);
        int huerfano = DespliegueEnCurso(db, versionId, muerto, servidores[0]);
        int vivo = DespliegueEnCurso(db, versionId, DateTime.UtcNow, servidores[1]);

        // Sin señal ninguna: o murió antes de dar la primera, o lo lanzó una versión de la
        // aplicación anterior a que esto existiera. En los dos casos no lo está corriendo nadie.
        int sinSenal = DespliegueEnCurso(db, versionId, null, servidores[0]);

        int cerrados = await ReconciliacionDeDespliegues.CerrarInterrumpidosAsync(db, DateTime.UtcNow);

        Assert.Equal(2, cerrados);

        using var comprobacion = Abrir(archivo);

        // El huérfano: cerrado, fechado cuando se le perdió la pista —y no cuando lo notamos, que
        // le regalaría al despliegue las horas que la aplicación estuvo caída— y con la verdad
        // escrita en su expediente.
        var cerrado = await comprobacion.DeploymentJobs.FirstAsync(j => j.Id == huerfano);
        Assert.Equal(JobStatus.Fallido, cerrado.Status);
        Assert.Equal(muerto, cerrado.CompletedAt!.Value, TimeSpan.FromSeconds(1));

        var explicacion = await comprobacion.DeploymentLogEntries
            .Where(l => l.JobId == huerfano && l.Level == DeployLogLevel.Error)
            .Select(l => l.Message).SingleAsync();
        Assert.Contains("INTERRUMPIDO", explicacion);
        Assert.Contains("no se sabe", explicacion, StringComparison.OrdinalIgnoreCase);

        // Y su señal deja de serlo: se reescribió, así que una segunda pasada ya no lo confunde con
        // un despliegue vivo ni vuelve a anotarle nada.
        Assert.Null(Senal(comprobacion, huerfano));

        Assert.Equal(JobStatus.Fallido, (await comprobacion.DeploymentJobs.FirstAsync(j => j.Id == sinSenal)).Status);

        // EL QUE IMPORTA: al que otra instancia está corriendo ahora mismo no se le toca nada. Un
        // arranque que cerrara todo lo que encuentra «En curso» le arruinaría el historial a quien
        // está desplegando y, peor, liberaría su servidor para que un tercero desplegara encima.
        var intacto = await comprobacion.DeploymentJobs.FirstAsync(j => j.Id == vivo);
        Assert.Equal(JobStatus.EnCurso, intacto.Status);
        Assert.Null(intacto.CompletedAt);
        Assert.NotNull(Senal(comprobacion, vivo));

        // Y volver a pasar no encuentra nada: es idempotente, que es lo que permite llamarla en cada
        // arranque y otra vez unos minutos después sin ensuciar el historial.
        Assert.Equal(0, await ReconciliacionDeDespliegues.CerrarInterrumpidosAsync(comprobacion, DateTime.UtcNow));
    }

    [Fact]
    public async Task Al_cerrar_un_interrumpido_se_conserva_lo_que_la_bitacora_alcanzo_a_confirmar()
    {
        var archivo = NuevaBase();
        using var db = Abrir(archivo);

        var (versionId, servidores) = Sembrar(db, "prod-1", "prod-2", "prod-3");

        var muerto = DateTime.UtcNow - SenalDeVidaDelDespliegue.Tolerancia - TimeSpan.FromMinutes(2);
        int jobId = DespliegueEnCurso(db, versionId, muerto, [.. servidores]);

        // El primero se publicó y quedó anotado; el segundo falló; del tercero no se llegó a saber
        // nada porque el servidor de la aplicación se murió antes de llegar a él.
        db.DeploymentLogEntries.Add(new DeploymentLogEntry
        {
            JobId = jobId, TargetId = servidores[0], TargetName = "prod-1",
            Level = DeployLogLevel.Exito, Message = "Despliegue completado en 3.2s.",
            Timestamp = muerto.AddMinutes(-2)
        });
        db.DeploymentLogEntries.Add(new DeploymentLogEntry
        {
            JobId = jobId, TargetId = servidores[1], TargetName = "prod-2",
            Level = DeployLogLevel.Error, Message = "Error tras 1.0s: conexión rechazada",
            Timestamp = muerto.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        await ReconciliacionDeDespliegues.CerrarInterrumpidosAsync(db, DateTime.UtcNow);

        using var comprobacion = Abrir(archivo);
        var job = await comprobacion.DeploymentJobs.FirstAsync(j => j.Id == jobId);

        // «No se sabe qué pasó» y «no se sabe nada» no son lo mismo, y el historial tiene que poder
        // decir hasta dónde se llegó: lo que la bitácora confirmó servidor por servidor es cierto
        // aunque el despliegue se cortara después.
        Assert.Equal(1, job.TargetsOk);
        Assert.Equal(1, job.TargetsFailed);
        Assert.Equal(3, job.TargetsTotal);

        var explicacion = await comprobacion.DeploymentLogEntries
            .Where(l => l.JobId == jobId && l.Message.Contains("INTERRUMPIDO"))
            .Select(l => l.Message).SingleAsync();
        Assert.Contains("1 de 3 servidor(es)", explicacion);
    }

    // ── La señal de vida en sí ───────────────────────────────────────────────────

    [Fact]
    public async Task La_señal_es_una_sola_fila_que_se_renueva_y_se_retira_al_acabar()
    {
        var archivo = NuevaBase();
        using var db = Abrir(archivo);

        var (versionId, servidores) = Sembrar(db, "prod-1");
        int jobId = DespliegueEnCurso(db, versionId, DateTime.UtcNow.AddMinutes(-1), servidores[0]);

        var despues = DateTime.UtcNow;
        await SenalDeVidaDelDespliegue.RefrescarAsync(db, jobId, despues);

        // Una sola fila, no un rastro de latidos: un despliegue de media hora dejaría sesenta
        // renglones idénticos en el expediente y lo volvería ilegible.
        using var comprobacion = Abrir(archivo);
        var filas = await comprobacion.DeploymentLogEntries
            .Where(l => l.JobId == jobId && l.Message == SenalDeVidaDelDespliegue.Marca).ToListAsync();

        Assert.Single(filas);
        Assert.Equal(despues, filas[0].Timestamp, TimeSpan.FromSeconds(1));

        await SenalDeVidaDelDespliegue.RetirarAsync(db, jobId);
        Assert.Null(Senal(comprobacion, jobId));
    }
}
