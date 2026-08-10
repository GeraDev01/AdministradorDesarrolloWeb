using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using ClosedXML.Excel;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las tres exportaciones a Excel que le faltaban a la web: vacaciones del líder, actividades del
/// equipo y estado de los servidores.
///
/// <para><b>Lo que se comprueba es la decisión, no el formato.</b> El acuerdo —tomado en minutas y
/// repetido en las tres— es que se baja <b>lo que el filtro está enseñando</b>, no la tabla entera.
/// Es exactamente lo que se puede romper sin que nadie lo note: la hoja se abre, tiene columnas y
/// filas, y solo quien conozca los datos descubrirá que trae de más. Por eso estas pruebas ABREN el
/// libro y cuentan renglones en vez de conformarse con que empiece por «PK».</para>
///
/// <para>La otra mitad que se cuida es la guarda: una exportación es una copia completa de los datos
/// de una pantalla, así que tiene que exigir lo mismo que la pantalla.</para>
/// </summary>
public class ExportacionesAExcelTests
{
    // ── Cómo se lee la hoja producida ───────────────────────────────────────────

    /// <summary>Los renglones de datos (sin el de encabezados), cada uno como textos de celda.</summary>
    private static List<string[]> Filas(byte[] libro)
    {
        using var memoria = new MemoryStream(libro);
        using var archivo = new XLWorkbook(memoria);
        var hoja = archivo.Worksheet(1);

        int ultimaColumna = hoja.LastColumnUsed()!.ColumnNumber();

        return hoja.RowsUsed()
            .Skip(1)   // encabezados
            .Select(r => Enumerable.Range(1, ultimaColumna)
                .Select(c => r.Cell(c).GetString())
                .ToArray())
            .ToList();
    }

    private static string[] Encabezados(byte[] libro)
    {
        using var memoria = new MemoryStream(libro);
        using var archivo = new XLWorkbook(memoria);
        var hoja = archivo.Worksheet(1);

        return hoja.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
    }

    // ═══ Vacaciones del líder ════════════════════════════════════════════════════

    private static DocumentoDeVacacionesService Vacaciones(AppDbContext db, ICurrentUser quien)
    {
        var auditoria = new AuditService(db, quien, new OrigenDePrueba());
        var ajustes = new SettingsService(db, quien, auditoria);

        return new DocumentoDeVacacionesService(
            db, quien, ajustes, new SignatureService(db, quien, auditoria),
            new GeneradorDeDocumentosQuestPdf(), new PlantillaDeVacacionesOpenXml(), auditoria);
    }

    private static (AppDbContext db, Developer ana, Developer beto) EquipoConVacaciones()
    {
        var db = TestDb.New();
        var ana = new Developer { FullName = "Ana", IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(ana, beto);
        db.SaveChanges();

        db.VacationRequests.AddRange(
            new VacationRequest
            {
                DeveloperId = ana.Id,
                StartDate = new DateTime(2026, 9, 1),
                EndDate = new DateTime(2026, 9, 5),
                Status = VacationStatus.Pendiente,
                Comment = "Boda de mi hermana"
            },
            new VacationRequest
            {
                DeveloperId = beto.Id,
                StartDate = new DateTime(2026, 10, 1),
                EndDate = new DateTime(2026, 10, 3),
                Status = VacationStatus.Aprobada,
                ReviewComment = "Adelante"
            });
        db.SaveChanges();

        return (db, ana, beto);
    }

    [Fact]
    public async Task Vacaciones_SinFiltro_BajaTodasLasSolicitudes()
    {
        var (db, _, _) = EquipoConVacaciones();

        var libro = await Vacaciones(db, UsuarioDePrueba.Como(UserRole.Admin)).ExcelAsync();

        Assert.Equal(2, Filas(libro).Count);
        Assert.Equal("Desarrollador", Encabezados(libro)[0]);
    }

    [Fact]
    public async Task Vacaciones_ConFiltroDePersona_BajaSoloLoQueSeVe()
    {
        var (db, ana, _) = EquipoConVacaciones();

        var libro = await Vacaciones(db, UsuarioDePrueba.Como(UserRole.Admin)).ExcelAsync(developerId: ana.Id);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("Ana", fila[0]);
        Assert.Equal("5", fila[3]);                 // del 1 al 5 de septiembre, contando el primero
        Assert.Equal("⏳ Pendiente", fila[4]);
    }

    [Fact]
    public async Task Vacaciones_ConFiltroDeEstado_BajaSoloLoQueSeVe()
    {
        var (db, _, _) = EquipoConVacaciones();

        var libro = await Vacaciones(db, UsuarioDePrueba.Como(UserRole.Admin))
            .ExcelAsync(estado: VacationStatus.Aprobada);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("Beto", fila[0]);
    }

    /// <summary>
    /// Los bytes del respaldo y del PDF firmado NO salen en la hoja: son hasta 15 MB por fila que
    /// nadie mira. Lo que se dice es que existen.
    /// </summary>
    [Fact]
    public async Task Vacaciones_DiceSiHayRespaldoPeroNoLoLleva()
    {
        var (db, ana, _) = EquipoConVacaciones();
        var suya = db.VacationRequests.Single(v => v.DeveloperId == ana.Id);
        suya.AttachmentBytes = [9, 9, 9, 9];
        suya.AttachmentFileName = "constancia.pdf";
        db.SaveChanges();

        var libro = await Vacaciones(db, UsuarioDePrueba.Como(UserRole.Admin)).ExcelAsync(developerId: ana.Id);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("Sí", fila[7]);
        Assert.DoesNotContain("constancia.pdf", string.Join("|", fila));
    }

    [Fact]
    public async Task Vacaciones_LaExportacionEsDelLider()
    {
        var (db, _, _) = EquipoConVacaciones();
        var servicio = Vacaciones(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 5));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.ExcelAsync());
    }

    // ═══ Actividades libres del equipo ═══════════════════════════════════════════

    private const int AnaId = 21;
    private const int BetoId = 22;

    private static (AppDbContext db, DevActivityService servicio) EquipoConActividades(ICurrentUser quien)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true });
        db.Developers.Add(new Developer { Id = BetoId, FullName = "Beto", IsActive = true });
        db.SaveChanges();

        var auditoria = new AuditService(db, quien, new OrigenDePrueba());
        var cronometro = new WorkSessionService(db, quien, auditoria);
        return (db, new DevActivityService(db, quien, auditoria, cronometro));
    }

    /// <summary>Una actividad con tiempo ya consolidado: sin sesión activa la cuenta es determinista.</summary>
    private static DevActivity SembrarActividad(AppDbContext db, int devId, string titulo,
        DevActivityStatus estado = DevActivityStatus.Abierta, int segundos = 0)
    {
        var actividad = new DevActivity
        {
            DeveloperId = devId,
            Title = titulo,
            Status = estado,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ClosedAt = estado == DevActivityStatus.Cerrada ? DateTime.UtcNow : null
        };
        db.DevActivities.Add(actividad);
        db.SaveChanges();

        if (segundos > 0)
        {
            db.WorkSessions.Add(new WorkSession
            {
                ActivityId = actividad.Id,
                DeveloperId = devId,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                EndedAt = DateTime.UtcNow.AddHours(-1),
                AccumulatedSeconds = segundos,
                Status = WorkSessionStatus.Detenida
            });
            db.SaveChanges();
        }

        return actividad;
    }

    [Fact]
    public async Task Actividades_ConFiltroDePersona_BajaSoloLoQueSeVe()
    {
        var (db, servicio) = EquipoConActividades(UsuarioDePrueba.Como(UserRole.Admin));
        SembrarActividad(db, AnaId, "Soporte a usuario", segundos: 3900);
        SembrarActividad(db, BetoId, "Investigación");

        var libro = await servicio.ExcelDelEquipoAsync(developerId: AnaId);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("Ana", fila[0]);
        Assert.Equal("Soporte a usuario", fila[1]);
        Assert.Equal("🟢 Abierta", fila[2]);
    }

    [Fact]
    public async Task Actividades_ConFiltroDeEstado_BajaSoloLoQueSeVe()
    {
        var (db, servicio) = EquipoConActividades(UsuarioDePrueba.Como(UserRole.Admin));
        SembrarActividad(db, AnaId, "Abierta");
        SembrarActividad(db, BetoId, "Cerrada", DevActivityStatus.Cerrada);

        var libro = await servicio.ExcelDelEquipoAsync(estado: DevActivityStatus.Cerrada);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("⚪ Cerrada", fila[2]);
    }

    /// <summary>
    /// El tiempo va en las dos formas y las dos hacen falta: el texto para leerlo y los segundos para
    /// poder sumarlos y ordenarlos en la hoja —ordenar «1h 05m 00s» como texto pone «59m» encima de
    /// «2h»—.
    /// </summary>
    [Fact]
    public async Task Actividades_LlevanElTiempoLegibleYLosSegundos()
    {
        var (db, servicio) = EquipoConActividades(UsuarioDePrueba.Como(UserRole.Admin));
        SembrarActividad(db, AnaId, "Soporte a usuario", segundos: 3900);

        var libro = await servicio.ExcelDelEquipoAsync();

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("1h 05m 00s", fila[5]);
        Assert.Equal("3900", fila[6]);
    }

    [Fact]
    public async Task Actividades_LaExportacionEsDelLider()
    {
        var (_, servicio) = EquipoConActividades(
            UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: AnaId, userId: 5));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.ExcelDelEquipoAsync());
    }

    // ═══ Estado de los servidores ════════════════════════════════════════════════

    /// <summary>
    /// Dos servidores del mismo sistema: uno con la última versión y otro con la anterior. Es el
    /// escenario donde «solo atrasados» cambia lo que se ve, que es lo que la exportación tiene que
    /// respetar.
    /// </summary>
    private static AppDbContext DosServidoresUnoAtrasado()
    {
        var db = TestDb.New();

        var sistema = new AppSystem { Name = "Portal", IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();

        var vieja = new AppRelease { AppSystemId = sistema.Id, Version = "1.0.0", CreatedAt = DateTime.UtcNow.AddDays(-10) };
        var nueva = new AppRelease { AppSystemId = sistema.Id, Version = "1.1.0", CreatedAt = DateTime.UtcNow.AddDays(-1) };
        db.AppReleases.AddRange(vieja, nueva);
        db.SaveChanges();

        db.DeploymentTargets.AddRange(
            new DeploymentTarget
            {
                Nombre = "web01", Host = "ftps://web01", Usuario = "publicador", RutaRemota = "/site/wwwroot",
                IsActive = true, LastReleaseId = nueva.Id, LastDeployedAt = DateTime.UtcNow.AddHours(-2)
            },
            new DeploymentTarget
            {
                Nombre = "web02", Host = "ftps://web02", Usuario = "publicador", RutaRemota = "/site/wwwroot",
                IsActive = true, LastReleaseId = vieja.Id, LastDeployedAt = DateTime.UtcNow.AddDays(-9)
            });
        db.SaveChanges();

        return db;
    }

    [Fact]
    public async Task Servidores_SinFiltro_BajanTodos()
    {
        using var db = DosServidoresUnoAtrasado();
        var servicio = new EstadoDeServidoresService(db, UsuarioDePrueba.Como(UserRole.Operaciones));

        var libro = await servicio.ExcelAsync();

        Assert.Equal(2, Filas(libro).Count);
    }

    /// <summary>
    /// «Solo atrasados» filtra en el navegador, así que era el filtro fácil de olvidar al exportar. Y
    /// es justo el que importa: quien lo marca y exporta viene a mandar la lista corta de máquinas
    /// por actualizar, no el inventario entero.
    /// </summary>
    [Fact]
    public async Task Servidores_SoloAtrasados_BajaSoloLoQueSeVe()
    {
        using var db = DosServidoresUnoAtrasado();
        var servicio = new EstadoDeServidoresService(db, UsuarioDePrueba.Como(UserRole.Operaciones));

        var libro = await servicio.ExcelAsync(soloAtrasados: true);

        var fila = Assert.Single(Filas(libro));
        Assert.Equal("web02", fila[0]);
        Assert.Equal("1.0.0", fila[3]);
        Assert.Equal("1.1.0", fila[4]);
        Assert.Equal("⚠ Atrasado", fila[5]);
    }

    [Fact]
    public async Task Servidores_LosDadosDeBajaSoloSalenSiSePidieron()
    {
        using var db = DosServidoresUnoAtrasado();
        var retirado = db.DeploymentTargets.Single(t => t.Nombre == "web02");
        retirado.IsActive = false;
        db.SaveChanges();

        var servicio = new EstadoDeServidoresService(db, UsuarioDePrueba.Como(UserRole.Operaciones));

        Assert.Single(Filas(await servicio.ExcelAsync()));
        Assert.Equal(2, Filas(await servicio.ExcelAsync(incluirDadosDeBaja: true)).Count);
    }

    /// <summary>
    /// La exportación exige lo mismo que la pantalla: despliegues. Un rol sin ese alcance no puede
    /// bajarse el inventario de servidores por una ruta lateral.
    /// </summary>
    [Fact]
    public async Task Servidores_LaExportacionExigeLoMismoQueLaPantalla()
    {
        using var db = DosServidoresUnoAtrasado();
        var servicio = new EstadoDeServidoresService(
            db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 5));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.ExcelAsync());
    }
}
