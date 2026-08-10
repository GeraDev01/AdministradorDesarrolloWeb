using System.IO.Compression;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

// ─────────────────────────────────────────────────────────────────────────────────
//  Los despliegues.
//
//  NINGUNA DE ESTAS PRUEBAS TOCA LA RED, y esa es la razón de que el FTP esté detrás de
//  IPublicacionDeDespliegue: en el escritorio la conversación con FluentFTP vivía dentro del propio
//  servicio, así que comprobar «qué pasa si un servidor falla a mitad» o «qué queda escrito al
//  cancelar» habría exigido un servidor FTP de verdad — es decir, no se comprobaba.
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>El checklist previo: lo que exige y lo que deja escrito.</summary>
public class DeploymentChecklistTests
{
    [Fact]
    public void Sin_marcar_nada_faltan_todos_los_puntos()
    {
        var faltantes = DeploymentChecklist.Faltantes([]);
        Assert.Equal(DeploymentChecklist.Puntos.Length, faltantes.Count);
    }

    [Fact]
    public void Con_todos_marcados_no_falta_ninguno()
    {
        var todas = DeploymentChecklist.Puntos.Select(p => p.Clave).ToList();
        Assert.Empty(DeploymentChecklist.Faltantes(todas));
    }

    [Fact]
    public void Marcar_de_mas_no_sustituye_a_marcar_lo_que_toca()
    {
        // Mandar claves inventadas no puede colar como checklist completo: se comprueba punto por
        // punto y no por conteo.
        var faltantes = DeploymentChecklist.Faltantes(["inventada", "otra", "tercera", "cuarta"]);
        Assert.Equal(DeploymentChecklist.Puntos.Length, faltantes.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData(" . ")]
    public void La_nota_vacia_o_de_relleno_no_vale(string? nota) =>
        Assert.False(DeploymentChecklist.NotaSuficiente(nota));

    [Fact]
    public void Una_referencia_corta_si_vale_como_justificacion() =>
        // El mínimo es bajo a propósito: «CAB-233» es una justificación legítima y completa.
        Assert.True(DeploymentChecklist.NotaSuficiente("CAB-233"));

    [Fact]
    public void La_evidencia_deja_escrito_quien_que_y_con_que_respaldo()
    {
        var evidencia = DeploymentChecklist.Evidencia(
            "Ana Ruiz", new DateTime(2026, 8, 5, 19, 30, 0), "Portal v2.4.1",
            "2 servidor(es): prod-1, prod-2", "todos",
            ["version", "ventana"], "CAB-233");

        Assert.Contains("Ana Ruiz", evidencia);
        Assert.Contains("Portal v2.4.1", evidencia);
        Assert.Contains("prod-1, prod-2", evidencia);
        Assert.Contains("Respaldo previo: todos", evidencia);
        Assert.Contains("Nota: CAB-233", evidencia);

        // Lo marcado y lo NO marcado, ambos: una evidencia que solo enseñara lo confirmado no
        // permitiría ver que dos puntos se dejaron en blanco.
        Assert.Contains("[x] Verifiqué que la versión es la correcta", evidencia);
        Assert.Contains("[ ] Sé cómo revertir si algo sale mal", evidencia);
    }

    [Fact]
    public void Un_despliegue_sin_nota_se_puede_redactar_igual()
    {
        // Los despliegues anteriores a que la nota fuera obligatoria tienen que poder documentarse
        // sin inventarles una.
        var evidencia = DeploymentChecklist.Evidencia(
            "sistema", DateTime.Now, "v1", "prod", "ninguno", [], null);

        Assert.DoesNotContain("Nota:", evidencia);
    }
}

/// <summary>La política de reintentos del FTP, sin FTP.</summary>
public class FtpRetryPolicyTests
{
    [Theory]
    [InlineData("ftps://mi.servidor.com/", "mi.servidor.com", true)]
    [InlineData("ftp://mi.servidor.com", "mi.servidor.com", false)]
    [InlineData("  mi.servidor.com  ", "mi.servidor.com", false)]
    [InlineData("FTPS://MAYUSCULAS.COM", "MAYUSCULAS.COM", true)]
    public void El_host_se_limpia_y_dice_si_pide_tls(string crudo, string esperado, bool tls)
    {
        var (host, tlsExplicito) = FtpRetryPolicy.PartirHost(crudo);
        Assert.Equal(esperado, host);
        Assert.Equal(tls, tlsExplicito);
    }

    [Fact]
    public void El_backoff_crece_y_tiene_tope()
    {
        Assert.True(FtpRetryPolicy.BackoffMs(0) < FtpRetryPolicy.BackoffMs(1));
        Assert.True(FtpRetryPolicy.BackoffMs(1) < FtpRetryPolicy.BackoffMs(2));
        // El tope existe para no dejar un despliegue esperando horas por un servidor caído.
        Assert.Equal(30_000, FtpRetryPolicy.BackoffMs(20));
    }

    [Fact]
    public async Task Reintenta_hasta_que_sale_bien()
    {
        int intentos = 0;
        var resultado = await FtpRetryPolicy.ConReintentosAsync(async _ =>
        {
            intentos++;
            if (intentos < 2) throw new IOException("la conexión se cayó");
            return await Task.FromResult("publicado");
        }, maxIntentos: 3, bitacora: null, queEs: "prueba", ct: CancellationToken.None);

        Assert.Equal("publicado", resultado);
        Assert.Equal(2, intentos);
    }

    [Fact]
    public async Task Al_agotar_los_intentos_falla_diciendo_el_ultimo_error()
    {
        var error = await Assert.ThrowsAsync<IOException>(() =>
            FtpRetryPolicy.ConReintentosAsync<string>(
                _ => throw new IOException("530 User cannot log in"),
                maxIntentos: 1, bitacora: null, queEs: "servidor prod-1", ct: CancellationToken.None));

        // El motivo concreto tiene que sobrevivir hasta arriba: es lo que se enseña y lo que
        // distingue «credenciales mal» de «la red se cayó».
        Assert.Contains("530 User cannot log in", error.Message);
        Assert.Contains("servidor prod-1", error.Message);
    }

    [Fact]
    public async Task La_cancelacion_no_espera_al_backoff()
    {
        using var cancelacion = new CancellationTokenSource();
        await cancelacion.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FtpRetryPolicy.ConReintentosAsync<string>(
                _ => throw new IOException("no debería llegar aquí"),
                maxIntentos: 6, bitacora: null, queEs: "prueba", ct: cancelacion.Token));
    }
}

/// <summary>
/// El estado de un despliegue en curso: lo que permite cerrar la pestaña y volver a encontrárselo.
/// </summary>
public class RegistroDeDesplieguesTests
{
    private static OrdenDeDespliegue Orden(int jobId = 1, params int[] servidores) => new(
        jobId, VersionId: 10, "Portal", "2.4.1", "2 servidor(es)",
        servidores.Length == 0 ? [7] : servidores,
        new HashSet<int>(servidores.Length == 0 ? [7] : servidores),
        new IdentidadDelDespliegue(3, "ana", "Ana Ruiz", UserRole.Operaciones, null));

    [Fact]
    public void Recien_lanzado_la_foto_esta_en_cero_y_sin_final()
    {
        var registro = new RegistroDeDespliegues();
        registro.Registrar(Orden(), DateTime.UtcNow);

        var foto = registro.Buscar(1)!.Foto(userId: 3);
        Assert.Equal(0, foto.Avance.Porcentaje);
        Assert.Empty(foto.Bitacora);
        Assert.Null(foto.Fin);
        Assert.True(foto.Mio);
        Assert.Equal("Ana Ruiz", foto.QuienLoLanzo);
    }

    [Fact]
    public void La_foto_de_otra_persona_no_se_marca_como_propia()
    {
        // Importa porque la pantalla adopta sola el despliegue PROPIO y solo ofrece mirar los ajenos:
        // robarle la pantalla a quien acaba de entrar sería peor que no enseñárselo.
        var registro = new RegistroDeDespliegues();
        registro.Registrar(Orden(), DateTime.UtcNow);

        Assert.False(registro.Buscar(1)!.Foto(userId: 99).Mio);
    }

    [Fact]
    public void La_foto_acumula_lo_que_se_va_contando()
    {
        var registro = new RegistroDeDespliegues();
        var trabajo = registro.Registrar(Orden(), DateTime.UtcNow);

        trabajo.Anotar(new RenglonDeBitacoraDto(DateTime.UtcNow, "prod-1", "conectado", DeployLogLevel.Info));
        trabajo.Avanzar(new AvanceDeDespliegueDto(1, 45, "prod-1", "subiendo"));

        var foto = trabajo.Foto(3);
        Assert.Single(foto.Bitacora);
        Assert.Equal(45, foto.Avance.Porcentaje);
        Assert.Equal("prod-1", foto.Avance.Servidor);
    }

    [Fact]
    public void La_bitacora_en_memoria_no_crece_sin_limite()
    {
        var registro = new RegistroDeDespliegues();
        var trabajo = registro.Registrar(Orden(), DateTime.UtcNow);

        for (int i = 0; i < TrabajoVivo.MaxRenglones + 50; i++)
            trabajo.Anotar(new RenglonDeBitacoraDto(DateTime.UtcNow, null, $"archivo {i}", DeployLogLevel.Info));

        var foto = trabajo.Foto(3);
        Assert.Equal(TrabajoVivo.MaxRenglones, foto.Bitacora.Count);
        // Se conservan los ÚLTIMOS: es lo que quiere ver quien acaba de abrir la pantalla.
        Assert.Contains("archivo " + (TrabajoVivo.MaxRenglones + 49), foto.Bitacora[^1].Mensaje);
    }

    [Fact]
    public void Un_servidor_ocupado_se_libera_al_terminar()
    {
        var registro = new RegistroDeDespliegues();
        var trabajo = registro.Registrar(Orden(1, 7, 8), DateTime.UtcNow);

        Assert.True(registro.AlgunoUsa(7));
        Assert.True(registro.AlgunoUsa(8));
        Assert.False(registro.AlgunoUsa(9));

        trabajo.Terminar(new FinDeDespliegueDto(1, JobStatus.Completado, "Completado", 2, 0, 0, "listo"),
            DateTime.UtcNow);

        Assert.False(registro.AlgunoUsa(7));
    }

    [Fact]
    public void Lo_terminado_se_conserva_un_rato_y_luego_se_olvida()
    {
        var registro = new RegistroDeDespliegues();
        var trabajo = registro.Registrar(Orden(), DateTime.UtcNow);
        var termino = DateTime.UtcNow;
        trabajo.Terminar(new FinDeDespliegueDto(1, JobStatus.Completado, "Completado", 1, 0, 0, "listo"), termino);

        // Quien cerró la pestaña justo al acabar tiene que poder volver y ver cómo salió.
        registro.Limpiar(termino.Add(RegistroDeDespliegues.Retencion / 2));
        Assert.NotNull(registro.Buscar(1));

        registro.Limpiar(termino.Add(RegistroDeDespliegues.Retencion).AddMinutes(1));
        Assert.Null(registro.Buscar(1));
    }
}

/// <summary>Quién puede desplegar qué perfil.</summary>
public class PermisoDePerfilTests
{
    [Fact]
    public void Operaciones_no_puede_desplegar_un_perfil_que_no_esta_habilitado() =>
        Assert.False(DeploymentService.PuedeDesplegarPerfil(
            UsuarioDePrueba.Como(UserRole.Operaciones),
            new DeploymentProfile { Name = "Producción", AllowedForOperaciones = false }));

    [Fact]
    public void Operaciones_si_puede_desplegar_el_que_si_lo_esta() =>
        Assert.True(DeploymentService.PuedeDesplegarPerfil(
            UsuarioDePrueba.Como(UserRole.Operaciones),
            new DeploymentProfile { Name = "QA", AllowedForOperaciones = true }));

    [Fact]
    public void El_lider_puede_desplegar_cualquiera() =>
        Assert.True(DeploymentService.PuedeDesplegarPerfil(
            UsuarioDePrueba.Como(UserRole.Admin),
            new DeploymentProfile { Name = "Producción", AllowedForOperaciones = false }));

    [Fact]
    public void Los_destinos_congelados_de_un_despliegue_estan_exentos() =>
        // No son perfiles guardados: son la foto de una selección directa que ya pasó por su propia
        // guarda de autorización.
        Assert.True(DeploymentService.PuedeDesplegarPerfil(
            UsuarioDePrueba.Como(UserRole.Operaciones),
            new DeploymentProfile { Name = "⚡ prod-1", IsAdHoc = true, AllowedForOperaciones = false }));
}

/// <summary>El despliegue de punta a punta, con el FTP de mentira.</summary>
public class MotorDeDespliegueTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "despliegue_" + Guid.NewGuid().ToString("N"));
    private readonly string _zip;

    public MotorDeDespliegueTests()
    {
        Directory.CreateDirectory(_carpeta);

        var origen = Path.Combine(_carpeta, "publicado");
        Directory.CreateDirectory(origen);
        File.WriteAllText(Path.Combine(origen, "index.html"), "<h1>hola</h1>");
        File.WriteAllText(Path.Combine(origen, "app.dll"), "binario");

        _zip = Path.Combine(_carpeta, "portal_2.4.1.zip");
        ZipFile.CreateFromDirectory(origen, _zip);
    }

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* temporal */ }
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────

    private sealed class PublicacionDeMentira : IPublicacionDeDespliegue
    {
        public List<string> Publicados { get; } = [];
        public List<string> Respaldados { get; } = [];
        public HashSet<string> FallanAlPublicar { get; } = [];
        public Func<Task>? AntesDePublicar { get; set; }

        public async Task<int> PublicarAsync(DestinoDeDespliegue destino, IReadOnlyList<ArchivoDelPaquete> archivos,
            IProgress<string> bitacora, Action<int, string> avance, CancellationToken ct)
        {
            if (AntesDePublicar != null) await AntesDePublicar();
            ct.ThrowIfCancellationRequested();

            if (FallanAlPublicar.Contains(destino.Nombre))
                throw new IOException($"El servidor {destino.Nombre} rechazó la conexión");

            for (int i = 0; i < archivos.Count; i++)
            {
                bitacora.Report($"  subiendo {archivos[i].RutaRelativa}");
                avance(i, archivos[i].RutaRelativa);
            }
            Publicados.Add(destino.Nombre);
            return archivos.Count;
        }

        public Task<int> DescargarCarpetaAsync(DestinoDeDespliegue destino, string carpetaLocal,
            IProgress<string> bitacora, CancellationToken ct)
        {
            Respaldados.Add(destino.Nombre);
            // Un archivo, para que el respaldo llegue a comprimirse de verdad.
            Directory.CreateDirectory(carpetaLocal);
            File.WriteAllText(Path.Combine(carpetaLocal, "anterior.txt"), "lo que había");
            return Task.FromResult(1);
        }
    }

    private sealed class AvisosDeMentira : IAvisosDeDespliegue
    {
        public List<RenglonDeBitacoraDto> Renglones { get; } = [];
        public FinDeDespliegueDto? Fin { get; private set; }

        public Task AvanceAsync(int jobId, AvanceDeDespliegueDto avance) => Task.CompletedTask;

        public Task RegistroAsync(int jobId, RenglonDeBitacoraDto renglon)
        {
            lock (Renglones) Renglones.Add(renglon);
            return Task.CompletedTask;
        }

        public Task FinAsync(int jobId, FinDeDespliegueDto fin) { Fin = fin; return Task.CompletedTask; }
    }

    private (AppDbContext db, int jobId, List<int> servidorIds) Sembrar(
        AppDbContext db, string zipPath, params string[] nombres)
    {
        var sistema = new AppSystem { Name = "Portal", IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();

        var version = new AppRelease
        {
            AppSystemId = sistema.Id, Version = "2.4.1", ZipLocalPath = zipPath, ZipSizeBytes = 100
        };
        db.AppReleases.Add(version);

        var perfil = new DeploymentProfile { Name = "⚡ prueba", IsAdHoc = true };
        db.DeploymentProfiles.Add(perfil);
        db.SaveChanges();

        var servidores = nombres.Select(n => new DeploymentTarget
        {
            Nombre = n, Host = $"ftps://{n}.example", Puerto = 21, Usuario = "publicador",
            Contrasena = ProtectorPortable.Cifrar("s3cr3t0-que-no-debe-salir"),
            RutaRemota = "/site/wwwroot", IsActive = true
        }).ToList();
        db.DeploymentTargets.AddRange(servidores);
        db.SaveChanges();

        var job = new DeploymentJob
        {
            AppReleaseId = version.Id, DeploymentProfileId = perfil.Id, Status = JobStatus.EnCurso,
            StartedAt = DateTime.UtcNow, TargetsTotal = servidores.Count
        };
        db.DeploymentJobs.Add(job);
        db.SaveChanges();

        _versionId = version.Id;
        return (db, job.Id, [.. servidores.Select(s => s.Id)]);
    }

    private int _versionId;

    private (TrabajoVivo trabajo, RegistroDeDespliegues registro) Trabajo(
        int jobId, IReadOnlyList<int> servidorIds, IReadOnlySet<int>? respaldar = null)
    {
        var registro = new RegistroDeDespliegues();
        var orden = new OrdenDeDespliegue(
            jobId, _versionId, "Portal", "2.4.1", $"{servidorIds.Count} servidor(es)",
            servidorIds, respaldar ?? new HashSet<int>(),
            new IdentidadDelDespliegue(1, "ana", "Ana Ruiz", UserRole.Operaciones, null));
        return (registro.Registrar(orden, DateTime.UtcNow), registro);
    }

    private static AuditService Auditoria(AppDbContext db) =>
        new(db, UsuarioDePrueba.Como(UserRole.Operaciones), new OrigenDePrueba());

    /// <summary>
    /// El almacén y el respaldo previo tal como los ve el motor en estas pruebas: <b>sin configurar</b>.
    ///
    /// Es lo correcto y no un atajo. Estas pruebas ejercitan el MOTOR —qué se publica, en qué orden,
    /// qué queda escrito y cómo termina cuando algo falla—; el respaldo a Blob y la descarga del
    /// paquete tienen las suyas. Sin cadena de conexión configurada, el respaldo se salta con su
    /// aviso y el paquete se lee de la carpeta local, que es justo lo que estas pruebas preparan.
    /// </summary>
    private static AlmacenamientoService Almacen(AppDbContext db) =>
        new(Configuracion(db), new BlobsSinConfigurar(), UsuarioDePrueba.Como(UserRole.Admin), Auditoria(db));

    private static RespaldoPrevioService Respaldo(AppDbContext db) =>
        new(Configuracion(db), Almacen(db), new DescargaSinRed());

    private static SettingsService Configuracion(AppDbContext db) =>
        new(db, UsuarioDePrueba.Como(UserRole.Admin), Auditoria(db));

    /// <summary>Deja el almacén «configurado» para las pruebas que sí ejercitan el respaldo.</summary>
    private static Task ConfigurarAlmacenAsync(AppDbContext db) =>
        Configuracion(db).GuardarAsync(
            SettingsService.Claves.AzureBlobConnectionString, "UseDevelopmentStorage=true");

    /// <summary>Anota a qué servidores se les pidió bajar la carpeta, que es lo que se comprueba.</summary>
    private sealed class DescargaQueAnota : IDescargaDeCarpetaRemota
    {
        public readonly List<string> Respaldados = [];

        public Task<int> DescargarAsync(CredencialesDeCarpetaRemota c, string carpetaLocal,
            IProgress<string> avance, CancellationToken ct = default)
        {
            Respaldados.Add(c.Host);
            // Cero archivos: la carpeta remota está vacía, así que no hay ZIP que subir y el
            // respaldo termina bien sin tocar el almacén. Basta para comprobar a quién se respalda.
            return Task.FromResult(0);
        }
    }

    private static RespaldoPrevioService RespaldoConAlmacen(AppDbContext db, IDescargaDeCarpetaRemota descarga) =>
        new(Configuracion(db), Almacen(db), descarga);

    // ── Pruebas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_despliegue_que_sale_bien_queda_completado_y_deja_rastro()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1", "prod-2");
        var (trabajo, _) = Trabajo(jobId, servidores);
        var publicacion = new PublicacionDeMentira();
        var avisos = new AvisosDeMentira();

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), avisos,
            new OpcionesDeEjecucion(RespaldoHabilitado: true, CarpetaDeDespliegue: _carpeta),
            Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Completado, job.Status);
        Assert.Equal(2, job.TargetsOk);
        Assert.Equal(0, job.TargetsFailed);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal(["prod-1", "prod-2"], publicacion.Publicados);

        // La bitácora se persiste paso a paso, no al final: es lo que hace que una caída no se lleve
        // el registro entero.
        Assert.NotEmpty(await db.DeploymentLogEntries.Where(l => l.JobId == jobId).ToListAsync());

        // Lo que responde «¿qué tiene este servidor y quién se lo puso?» sin releer la bitácora.
        var actualizados = await db.DeploymentTargets.Where(t => servidores.Contains(t.Id)).ToListAsync();
        Assert.All(actualizados, t => Assert.NotNull(t.LastDeployedAt));
        Assert.All(actualizados, t => Assert.Equal(jobId, t.LastDeploymentJobId));
    }

    [Fact]
    public async Task Un_servidor_caido_deja_el_despliegue_en_PARCIAL_y_no_en_completado()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1", "prod-2");
        var (trabajo, _) = Trabajo(jobId, servidores);
        var publicacion = new PublicacionDeMentira();
        publicacion.FallanAlPublicar.Add("prod-2");

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        // Antes esto se reportaba como «Completado», que es la peor respuesta posible: dice que todo
        // está publicado cuando la mitad no lo está.
        Assert.Equal(JobStatus.Parcial, job.Status);
        Assert.Equal(1, job.TargetsOk);
        Assert.Equal(1, job.TargetsFailed);

        // Que uno falle no impide que el anterior quede registrado como publicado.
        Assert.Equal(["prod-1"], publicacion.Publicados);
    }

    [Fact]
    public async Task Si_fallan_todos_el_despliegue_es_FALLIDO()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1");
        var (trabajo, _) = Trabajo(jobId, servidores);
        var publicacion = new PublicacionDeMentira();
        publicacion.FallanAlPublicar.Add("prod-1");

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Fallido, job.Status);
    }

    [Fact]
    public async Task Cancelar_cierra_el_trabajo_en_vez_de_dejarlo_colgado_en_curso()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1", "prod-2");
        var (trabajo, _) = Trabajo(jobId, servidores);

        using var cancelacion = new CancellationTokenSource();
        var publicacion = new PublicacionDeMentira
        {
            // Cancela DURANTE el primer servidor: es el caso que en el escritorio dejaba el trabajo
            // eternamente «En curso» y perdía toda la bitácora.
            AntesDePublicar = () => { cancelacion.Cancel(); return Task.CompletedTask; }
        };

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db), trabajo, cancelacion.Token);

        Assert.Equal(JobStatus.Cancelado, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.NotEmpty(await db.DeploymentLogEntries.Where(l => l.JobId == jobId).ToListAsync());
        Assert.True(trabajo.Terminado);
    }

    [Fact]
    public async Task El_respaldo_previo_se_hace_solo_donde_se_pidio()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1", "prod-2");
        var (trabajo, _) = Trabajo(jobId, servidores, new HashSet<int> { servidores[0] });

        var descarga = new DescargaQueAnota();
        await ConfigurarAlmacenAsync(db);

        await new MotorDeDespliegue().EjecutarAsync(
            db, new PublicacionDeMentira(), Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), RespaldoConAlmacen(db, descarga), Almacen(db),
            trabajo, CancellationToken.None);

        // El respaldo ya no baja por el mismo canal que publica ni deja un ZIP en la carpeta del
        // servidor: sube a Azure Blob, como en el escritorio. Lo que sigue importando —y es lo que
        // esta prueba fija— es a QUIÉN se respalda: solo a los servidores marcados.
        //
        // Se compara contra el HOST y no contra el nombre porque es lo que recibe quien baja la
        // carpeta: el nombre es una etiqueta de la pantalla, el host es a dónde se conecta.
        var soloUno = Assert.Single(descarga.Respaldados);
        Assert.Contains("prod-1", soloUno);
    }

    [Fact]
    public async Task Con_el_respaldo_pedido_y_el_almacen_sin_configurar_NO_se_despliega()
    {
        // Es la regla del escritorio y la que da sentido al respaldo: sin red de seguridad no se
        // toca el servidor. Antes esto se escapaba porque el ZIP se guardaba en la carpeta local y
        // «siempre funcionaba»; ahora, si no hay dónde dejarlo, el despliegue de ese servidor falla
        // con un motivo que dice qué configurar.
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1");
        var (trabajo, _) = Trabajo(jobId, servidores, new HashSet<int>(servidores));

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, new PublicacionDeMentira(), Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db),
            trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Fallido, job.Status);
        Assert.Contains(db.DeploymentLogEntries.AsEnumerable(),
            l => l.Message.Contains("Blob", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Con_el_respaldo_apagado_en_configuracion_no_se_respalda_nada()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1");
        var (trabajo, _) = Trabajo(jobId, servidores, new HashSet<int>(servidores));
        var publicacion = new PublicacionDeMentira();

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(RespaldoHabilitado: false, CarpetaDeDespliegue: _carpeta),
            Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        Assert.Empty(publicacion.Respaldados);
        Assert.Equal(JobStatus.Completado, job.Status);
        // Y queda escrito que se desplegó sin red de seguridad, que es la mitad del valor de la regla.
        var bitacora = await db.DeploymentLogEntries.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Contains(bitacora, l => l.Message.Contains("desactivado en la configuración"));
    }

    [Fact]
    public async Task Si_no_se_puede_respaldar_ese_servidor_no_recibe_el_despliegue()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1");
        var (trabajo, _) = Trabajo(jobId, servidores, new HashSet<int>(servidores));
        var publicacion = new PublicacionDeMentira();

        // Sin carpeta configurada el respaldo no se puede guardar. La regla del escritorio manda:
        // desplegar creyendo que hay red de seguridad es peor que saber que no la hay.
        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(RespaldoHabilitado: true, CarpetaDeDespliegue: null),
            Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Fallido, job.Status);
        Assert.Empty(publicacion.Publicados);
    }

    [Fact]
    public async Task Una_contrasena_ilegible_falla_ese_servidor_sin_ensenar_el_secreto()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, _zip, "prod-1");

        // Como los valores heredados del cifrado DPAPI de Windows, que el servidor no puede leer.
        var servidor = await db.DeploymentTargets.FirstAsync(t => t.Id == servidores[0]);
        servidor.Contrasena = "dGV4dG8gaGVyZWRhZG8gZGUgV2luZG93cw==";
        await db.SaveChangesAsync();

        var (trabajo, _) = Trabajo(jobId, servidores);
        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, new PublicacionDeMentira(), Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db),
            trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Fallido, job.Status);

        var bitacora = await db.DeploymentLogEntries.Where(l => l.JobId == jobId).ToListAsync();
        // Se dice qué hacer, no un «530» incomprensible…
        Assert.Contains(bitacora, l => l.Message.Contains("volver a capturarla"));
        // …y en ningún renglón aparece el valor guardado.
        Assert.DoesNotContain(bitacora, l => l.Message.Contains(servidor.Contrasena));
    }

    [Fact]
    public async Task Si_el_paquete_no_esta_se_dice_y_el_trabajo_no_queda_en_curso()
    {
        using var db = TestDb.New();
        var (_, jobId, servidores) = Sembrar(db, Path.Combine(_carpeta, "no-existe.zip"), "prod-1");
        var (trabajo, _) = Trabajo(jobId, servidores);
        var publicacion = new PublicacionDeMentira();

        var job = await new MotorDeDespliegue().EjecutarAsync(
            db, publicacion, Auditoria(db), new AvisosDeMentira(),
            new OpcionesDeEjecucion(true, _carpeta), Respaldo(db), Almacen(db), trabajo, CancellationToken.None);

        Assert.Equal(JobStatus.Fallido, job.Status);
        Assert.Empty(publicacion.Publicados);

        var bitacora = await db.DeploymentLogEntries.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Contains(bitacora, l => l.Level == DeployLogLevel.Error && l.Message.Contains("paquete"));
    }

    [Fact]
    public async Task El_paquete_se_lee_entero_y_sin_las_entradas_de_carpeta()
    {
        var archivos = await MotorDeDespliegue.LeerPaqueteAsync(_zip, CancellationToken.None);

        Assert.Equal(2, archivos.Count);
        Assert.Contains(archivos, a => a.RutaRelativa == "index.html");
        Assert.All(archivos, a => Assert.NotEmpty(a.Contenido));
        // Las rutas viajan con «/» aunque el ZIP se haya creado en Windows: el destino es un FTP.
        Assert.All(archivos, a => Assert.DoesNotContain('\\', a.RutaRelativa));
    }

    [Fact]
    public async Task Un_paquete_vacio_se_rechaza_con_su_motivo()
    {
        var vacio = Path.Combine(_carpeta, "vacio.zip");
        var sinNada = Path.Combine(_carpeta, "sin-nada");
        Directory.CreateDirectory(sinNada);
        ZipFile.CreateFromDirectory(sinNada, vacio);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MotorDeDespliegue.LeerPaqueteAsync(vacio, CancellationToken.None));

        Assert.Contains("no contiene archivos", error.Message);
    }
}
