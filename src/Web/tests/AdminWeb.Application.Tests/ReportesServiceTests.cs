using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El centro de reportes, portado de <c>ReportsControl</c>.
///
/// No se comprueba cada uno de los quince generadores renglón por renglón —serían pruebas que
/// repiten la consulta que están probando—, sino lo que de verdad puede romperse al mudarlos: que
/// TODOS corran sin reventar contra una base con datos, que el filtro por persona y por período
/// signifiquen lo mismo que en el escritorio, que las cuentas de las columnas donde había una
/// fórmula (el prorrateo del estimado) sigan dando lo mismo, y que el resumen —cifras y barras—
/// salga de la columna que cada reporte declara.
/// </summary>
public class ReportesServiceTests
{
    /// <summary>
    /// El servicio con un buzón de mentira: mandar el reporte por correo es parte de lo que hace, y
    /// estas pruebas no tocan la red.
    /// </summary>
    private static ReportesService Svc(
        AppDbContext db, ICurrentUser? cu = null, CorreoDeMentira? buzon = null)
    {
        var actual = cu ?? UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        var bitacora = new AuditService(db, actual, new OrigenDePrueba());

        return new ReportesService(db, actual, new SettingsService(db, actual, bitacora),
            buzon ?? new CorreoDeMentira(), bitacora);
    }

    /// <summary>Deja el correo de la aplicación configurado, que es lo que el envío exige antes de nada.</summary>
    private static async Task ConfigurarCorreoAsync(AppDbContext db)
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        var ajustes = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await ajustes.GuardarAsync(SettingsService.Claves.EmailEnabled, "true");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailAddress, "aplicacion@empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailPassword, "contrasena-de-aplicacion");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");
    }

    private static readonly DateTime Desde = new(2026, 1, 1);
    private static readonly DateTime Hasta = new(2026, 12, 31);

    /// <summary>Una base con algo de cada cosa, para que ningún generador se quede sin datos que tocar.</summary>
    private static (AppDbContext db, int ana, int beto) BaseConDatos()
    {
        var db = TestDb.New();

        var ana = new Developer { FullName = "Ana", IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        var equipo = new Team { Name = "Plataforma" };
        var criterio = new ScoringCriterion { Name = "Entrega" };
        db.Developers.AddRange(ana, beto);
        db.Teams.Add(equipo);
        db.ScoringCriteria.Add(criterio);
        db.SaveChanges();

        ana.TeamId = equipo.Id;
        db.SaveChanges();

        var abierto = new Requirement
        {
            Title = "Pantalla nueva",
            Status = RequirementStatus.EnDesarrollo,
            EstimateHours = 10m,
            ProgressPercent = 40,
            CreatedAt = new DateTime(2026, 3, 1),
            CommittedDeliveryDate = new DateTime(2026, 3, 20)
        };
        var entregado = new Requirement
        {
            Title = "Reporte viejo",
            Status = RequirementStatus.Entregado,
            EstimateHours = 4m,
            ProgressPercent = 100,
            CreatedAt = new DateTime(2026, 2, 1),
            CommittedDeliveryDate = new DateTime(2026, 2, 10),
            ActualDeliveryDate = new DateTime(2026, 2, 14),
            Source = RequirementSource.AzureDevOps
        };
        db.Requirements.AddRange(abierto, entregado);
        db.SaveChanges();

        db.Assignments.AddRange(
            new Assignment { RequirementId = abierto.Id, DeveloperId = ana.Id },
            new Assignment { RequirementId = abierto.Id, DeveloperId = beto.Id },
            new Assignment { RequirementId = entregado.Id, DeveloperId = ana.Id });

        db.WorkSessions.Add(new WorkSession
        {
            RequirementId = abierto.Id, DeveloperId = ana.Id,
            StartedAt = new DateTime(2026, 3, 2), Status = WorkSessionStatus.Detenida,
            AccumulatedSeconds = 7200   // 2 h
        });

        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = ana.Id, CriterionId = criterio.Id, Points = 8,
            Year = 2026, Month = 3, Date = new DateTime(2026, 3, 5),
            ApprovalStatus = PointApprovalStatus.Aprobado
        });
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = beto.Id, CriterionId = criterio.Id, Points = 5,
            Year = 2026, Month = 3, Date = new DateTime(2026, 3, 6),
            ApprovalStatus = PointApprovalStatus.Pendiente   // no cuenta: nadie la ha aprobado
        });

        db.TeamPointEntries.Add(new TeamPointEntry
        {
            TeamId = equipo.Id, CriterionId = criterio.Id, Points = 3,
            Year = 2026, Month = 3, Date = new DateTime(2026, 3, 7)
        });

        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = beto.Id,
            StartDate = new DateTime(2026, 4, 1), EndDate = new DateTime(2026, 4, 5),
            Status = VacationStatus.Aprobada
        });

        db.TeamRotations.Add(new TeamRotation
        {
            DeveloperId = ana.Id, DeveloperName = "Ana",
            FromTeamName = "Sin equipo", ToTeamName = "Plataforma",
            RotatedAt = new DateTime(2026, 2, 20)
        });

        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 4321, Title = "Bug en producción", WorkItemType = "Bug",
            State = "Active", AssignedTo = "Ana", AreaPath = "Proyecto\\Web"
        });

        db.SaveChanges();
        return (db, ana.Id, beto.Id);
    }

    [Fact]
    public async Task Catalogo_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.CatalogoAsync());
    }

    [Fact]
    public async Task Catalogo_TraeLosQuinceReportesYAQuienSePuedeFiltrar()
    {
        var (db, _, _) = BaseConDatos();

        var catalogo = await Svc(db).CatalogoAsync();

        Assert.Equal(15, catalogo.Reportes.Count);
        Assert.Equal(catalogo.Reportes.Count, catalogo.Reportes.Select(r => r.Clave).Distinct().Count());
        Assert.All(catalogo.Reportes, r => Assert.False(string.IsNullOrWhiteSpace(r.Descripcion)));
        Assert.Equal(["Ana", "Beto"], catalogo.Desarrolladores.Select(d => d.Texto).ToArray());
    }

    [Fact]
    public async Task Generar_TodosLosReportesCorrenContraDatosReales()
    {
        var (db, _, _) = BaseConDatos();
        var svc = Svc(db);

        foreach (var definicion in (await svc.CatalogoAsync()).Reportes)
        {
            var reporte = await svc.GenerarAsync(definicion.Clave, Desde, Hasta, []);

            Assert.NotNull(reporte);
            Assert.NotEmpty(reporte!.Columnas);
            // Ninguna fila puede traer más celdas que columnas: la rejilla las pinta por índice.
            Assert.All(reporte.Filas, f => Assert.True(f.Count <= reporte.Columnas.Count, definicion.Clave));
            // «Filas» está siempre, con o sin columna numérica.
            Assert.Contains(reporte.Indicadores, i => i.Titulo == "Filas");
        }
    }

    [Fact]
    public async Task Generar_ClaveDesconocida_DevuelveNulo()
    {
        var (db, _, _) = BaseConDatos();

        Assert.Null(await Svc(db).GenerarAsync("no-existe", Desde, Hasta, []));
    }

    [Fact]
    public async Task Generar_FiltroPorPersona_DejaFueraALosDemas()
    {
        var (db, ana, _) = BaseConDatos();

        var todos = await Svc(db).GenerarAsync("carga-por-desarrollador", Desde, Hasta, []);
        var soloAna = await Svc(db).GenerarAsync("carga-por-desarrollador", Desde, Hasta, [ana]);

        Assert.Equal(2, todos!.Filas.Count);
        Assert.Single(soloAna!.Filas);
        Assert.Equal("Ana", soloAna.Filas[0][0]);
        Assert.Equal("Plataforma", soloAna.Filas[0][1]);
    }

    [Fact]
    public async Task Generar_Desempeno_SoloCuentaLoAprobado()
    {
        var (db, _, _) = BaseConDatos();

        var reporte = await Svc(db).GenerarAsync("desempeno", Desde, Hasta, []);

        // Beto tiene puntos pendientes de revisión: contarlos adelantaría una decisión que el líder
        // no ha tomado.
        var fila = Assert.Single(reporte!.Filas);
        Assert.Equal("Ana", fila[0]);
        Assert.Equal("8", fila[1]);
    }

    [Fact]
    public async Task Generar_Desempeno_ElPeriodoAcota()
    {
        var (db, _, _) = BaseConDatos();

        var fuera = await Svc(db).GenerarAsync(
            "desempeno", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), []);

        Assert.Empty(fuera!.Filas);
    }

    [Fact]
    public async Task Generar_EstimadoVsRealPorDesarrollador_ProrrateaLaEstimacionEntreLosAsignados()
    {
        var (db, _, _) = BaseConDatos();

        var reporte = await Svc(db).GenerarAsync("estimado-vs-real-por-desarrollador", Desde, Hasta, []);

        // El requerimiento abierto estima 10 h y lo tienen dos personas: 5 h a cada una. Ana suma
        // además las 4 h del entregado, que es solo suyo → 9 h. Beto se queda en 5.
        var ana = reporte!.Filas.Single(f => f[0] == "Ana");
        var beto = reporte.Filas.Single(f => f[0] == "Beto");

        Assert.Equal("9", ana[1]);
        Assert.Equal("5", beto[1]);

        // Y las horas reales son las de la sesión del período: 7200 s = 2 h, todas de Ana.
        Assert.Equal("2", ana[2]);
        Assert.Equal("0", beto[2]);
    }

    [Fact]
    public async Task Generar_Entregas_ClasificaLaTardia()
    {
        var (db, _, _) = BaseConDatos();

        var reporte = await Svc(db).GenerarAsync("entregas", Desde, Hasta, []);

        var fila = Assert.Single(reporte!.Filas);
        Assert.Equal("4", fila[5]);          // cuatro días después del compromiso
        Assert.Equal("Tardía", fila[6]);
    }

    [Fact]
    public async Task Generar_ElResumenSaleDeLaColumnaQueDeclaraElReporte()
    {
        var (db, _, _) = BaseConDatos();

        // «por-origen» agrupa por la columna 0 (el origen) y suma la 1 (el total).
        var reporte = await Svc(db).GenerarAsync("por-origen", Desde, Hasta, []);

        Assert.Equal("Requerimientos por origen", reporte!.TituloDeGrafica);
        Assert.Contains(reporte.Barras, b => b.Etiqueta == "Manual" && b.Valor == 1);
        Assert.Contains(reporte.Barras, b => b.Etiqueta == "DevOps" && b.Valor == 1);

        // Con columna numérica declarada, las cifras grandes son total / promedio / máximo.
        Assert.Contains(reporte.Indicadores, i => i.Titulo == "Total" && i.Valor == "2");
        Assert.Contains(reporte.Indicadores, i => i.Titulo == "Máximo");
    }

    [Fact]
    public async Task Generar_SinColumnaNumerica_ElResumenCuentaCategorias()
    {
        var (db, _, _) = BaseConDatos();

        // «pendientes» declara -1 en la columna de valor: se cuentan filas, no se suman.
        var reporte = await Svc(db).GenerarAsync("pendientes", Desde, Hasta, []);

        Assert.Contains(reporte!.Indicadores, i => i.Titulo == "Categorías");
        Assert.DoesNotContain(reporte.Indicadores, i => i.Titulo == "Promedio");
    }

    [Fact]
    public async Task Excel_DevuelveUnLibroConLaMismaTabla()
    {
        var (db, _, _) = BaseConDatos();

        var archivo = await Svc(db).ExcelAsync("pendientes", Desde, Hasta, []);

        Assert.NotNull(archivo);
        Assert.Equal("Requerimientos pendientes", archivo!.Value.nombre);
        // Un .xlsx es un ZIP: sus dos primeros bytes son «PK». Comprobarlo es barato y descarta el
        // fallo real que puede ocurrir aquí — devolver un flujo vacío o a medio escribir.
        Assert.True(archivo.Value.contenido.Length > 0);
        Assert.Equal((byte)'P', archivo.Value.contenido[0]);
        Assert.Equal((byte)'K', archivo.Value.contenido[1]);
    }

    [Fact]
    public async Task Excel_LoExigeElLider()
    {
        var (db, _, _) = BaseConDatos();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 9));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ExcelAsync("pendientes", Desde, Hasta, []));
    }

    // ── El envío por correo ──────────────────────────────────────────────────────────────────
    //
    // Sin red: el envío pasa por IClienteDeCorreo y aquí se le pone un buzón de mentira. Lo que se
    // comprueba es lo que el escritorio no podía probar: que el adjunto es la hoja de cálculo de
    // verdad y que ninguna de las tres formas de no poder mandarlo revienta la operación.

    [Fact]
    public async Task EnviarPorCorreo_MandaLaHojaDeCalculoComoAdjunto()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);
        var buzon = new CorreoDeMentira();

        var (ok, mensaje) = await Svc(db, buzon: buzon).EnviarPorCorreoAsync(
            "pendientes", Desde, Hasta, [], "jefe@empresa.com; suplente@empresa.com", "Ahí va lo de la junta.");

        Assert.True(ok);
        Assert.Contains("2 destinatario(s)", mensaje);

        var correo = Assert.Single(buzon.Enviados);
        Assert.Equal(new[] { "jefe@empresa.com", "suplente@empresa.com" }, correo.Destinatarios);
        Assert.Equal("Reporte: Requerimientos pendientes", correo.Asunto);
        Assert.Contains("Ahí va lo de la junta.", correo.Cuerpo);

        var adjunto = correo.Adjunto;
        Assert.NotNull(adjunto);
        Assert.EndsWith(".xlsx", adjunto!.Nombre);
        Assert.Equal(AdjuntoDeCorreo.HojaDeCalculo, adjunto.TipoDeMedio);

        // Un .xlsx es un ZIP: sus dos primeros bytes son «PK». Es lo que descarta el fallo real que
        // puede ocurrir aquí — adjuntar un flujo vacío o a medio escribir.
        Assert.Equal((byte)'P', adjunto.Contenido[0]);
        Assert.Equal((byte)'K', adjunto.Contenido[1]);
    }

    /// <summary>
    /// Varios reportes llevan «:» o «.» en el nombre y de ahí sale el nombre del archivo que llega al
    /// buzón. Se sanea por lo mismo que en la descarga.
    /// </summary>
    [Fact]
    public async Task EnviarPorCorreo_ElNombreDelAdjuntoNoLlevaCaracteresDeRuta()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);
        var buzon = new CorreoDeMentira();

        await Svc(db, buzon: buzon).EnviarPorCorreoAsync(
            "entregas", Desde, Hasta, [], "jefe@empresa.com", null);

        var nombre = Assert.Single(buzon.Enviados).Adjunto!.Nombre;
        Assert.DoesNotContain(":", nombre);
        Assert.DoesNotContain("/", nombre);
        Assert.DoesNotContain("\\", nombre);
    }

    [Fact]
    public async Task EnviarPorCorreo_SinCorreoConfigurado_LoExplicaYNoManda()
    {
        var (db, _, _) = BaseConDatos();
        var buzon = new CorreoDeMentira();

        var (ok, mensaje) = await Svc(db, buzon: buzon).EnviarPorCorreoAsync(
            "pendientes", Desde, Hasta, [], "jefe@empresa.com", null);

        Assert.False(ok);
        Assert.Contains("no está habilitado", mensaje);
        Assert.Empty(buzon.Enviados);
    }

    [Fact]
    public async Task EnviarPorCorreo_SinDestinatariosValidos_LoExplica()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);
        var buzon = new CorreoDeMentira();

        var (ok, mensaje) = await Svc(db, buzon: buzon).EnviarPorCorreoAsync(
            "pendientes", Desde, Hasta, [], "pendiente-de-preguntar", null);

        Assert.False(ok);
        Assert.Contains("destinatario válido", mensaje);
        Assert.Empty(buzon.Enviados);
    }

    /// <summary>
    /// Un servidor SMTP que rechaza el envío no puede convertirse en un 500: el reporte se generó y
    /// lo que hay que enseñar es por qué no salió.
    /// </summary>
    [Fact]
    public async Task EnviarPorCorreo_SiElServidorLoRechaza_LoDiceEnVezDeReventar()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);

        var (ok, mensaje) = await Svc(db, buzon: new CorreoDeMentira { FallaElEnvio = true })
            .EnviarPorCorreoAsync("pendientes", Desde, Hasta, [], "jefe@empresa.com", null);

        Assert.False(ok);
        Assert.Contains("rechazó el envío", mensaje);
    }

    [Fact]
    public async Task EnviarPorCorreo_ConUnaClaveQueNoExiste_LoDice()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);

        var (ok, mensaje) = await Svc(db).EnviarPorCorreoAsync(
            "no-existe", Desde, Hasta, [], "jefe@empresa.com", null);

        Assert.False(ok);
        Assert.Contains("no existe", mensaje);
    }

    [Fact]
    public async Task EnviarPorCorreo_LoExigeElLider()
    {
        var (db, _, _) = BaseConDatos();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 9));

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            svc.EnviarPorCorreoAsync("pendientes", Desde, Hasta, [], "jefe@empresa.com", null));
    }

    /// <summary>
    /// La bitácora deja constancia de que salió, pero no de a quién ni con qué cifras: la leen más
    /// personas que las que pidieron el envío, y las filas del reporte son justamente lo delicado.
    /// </summary>
    [Fact]
    public async Task EnviarPorCorreo_QuedaEnLaBitacoraSinLosDatosDelReporte()
    {
        var (db, _, _) = BaseConDatos();
        await ConfigurarCorreoAsync(db);

        await Svc(db, buzon: new CorreoDeMentira()).EnviarPorCorreoAsync(
            "pendientes", Desde, Hasta, [], "jefe@empresa.com", null);

        Assert.Contains(db.AuditLogs, a => a.EntityType == "Reporte"
                                        && (a.Details ?? "").Contains("enviado por correo"));
        Assert.DoesNotContain(db.AuditLogs, a => (a.Details ?? "").Contains("jefe@empresa.com"));
        Assert.DoesNotContain(db.AuditLogs, a => (a.Details ?? "").Contains("Pantalla nueva"));
    }

    [Fact]
    public void ComponerCorreo_DiceQueReporteEsDeQuePeriodoYConQueFiltro()
    {
        var parametros = new ParametrosDeReporte(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), [7, 9]);

        var texto = ReportesService.ComponerCorreo(
            "Requerimientos pendientes", parametros, 12, "  Para la junta del lunes.  ");

        Assert.StartsWith("Para la junta del lunes.", texto);
        Assert.Contains("Reporte: Requerimientos pendientes", texto);
        Assert.Contains("Período: 01/01/2026 al 31/01/2026", texto);
        Assert.Contains("Personas: 2 seleccionada(s)", texto);
        Assert.Contains("Filas: 12", texto);
        Assert.Contains("hoja de cálculo (.xlsx)", texto);
    }

    [Fact]
    public void ComponerCorreo_SinNotaNiFiltro_LoDiceIgual()
    {
        var parametros = new ParametrosDeReporte(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), []);

        var texto = ReportesService.ComponerCorreo("Entregas", parametros, 0, null);

        Assert.StartsWith("Reporte: Entregas", texto);
        Assert.Contains("Personas: todas", texto);
    }
}
