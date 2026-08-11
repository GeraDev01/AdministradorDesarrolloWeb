using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using ClosedXML.Excel;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las etiquetas que escribe el SERVIDOR van con la palabra sola, sin dibujos delante.
///
/// <para><b>Por qué hay una prueba para algo que parece cosmético.</b> Los emoji no los dibujábamos
/// nosotros: los dibuja el sistema operativo de quien mira. Se ven distintos en cada equipo, NO
/// heredan el color del texto —en el tema oscuro se quedaban con el suyo mientras la palabra de al
/// lado cambiaba— y donde no hay fuente de emoji instalada salen como un CUADRO VACÍO. Lo último está
/// comprobado en una captura, no es una precaución teórica. Y volver a colarlos es facilísimo: son un
/// carácter más dentro de una cadena, nadie los ve en una revisión de código y el compilador no tiene
/// nada que decir. Por eso la comprobación no es «¿dice Abierta?» sino «¿queda algún carácter que
/// tenga que dibujar el sistema operativo?», que es lo que de verdad se quiere impedir.</para>
///
/// <para><b>Lo que se persigue</b> son dos familias de caracteres: los pares suplentes —todo emoji
/// fuera del plano básico, 🟢 🔴 🏖— y los símbolos sueltos del plano básico —⚠ ✅ ⏳ ○ ▶—, que
/// tienen el mismo defecto aunque quepan en un solo <c>char</c>. Las letras acentuadas, la barra de
/// «IDE / Editor» y el punto medio del resumen de servidores NO son símbolos y pasan sin problema.
/// </para>
///
/// <para>La lista de abajo es la del lote que limpió el usuario. Si algún día se limpia otra tabla
/// —el tipo de permiso, los indicadores de métricas—, se añade aquí y deja de poder volver sola.</para>
/// </summary>
public class EtiquetasDelServidorSinEmojiTests
{
    // ── Cómo se comprueba ────────────────────────────────────────────────────────

    /// <summary>
    /// Falla si la cadena lleva algo que tenga que pintar el sistema operativo.
    ///
    /// El mensaje incluye el punto de código porque un emoji copiado a la consola de un fallo de
    /// prueba se ve, precisamente, como un cuadro vacío: sin el «U+XXXX» no habría forma de saber
    /// cuál de los dos caracteres del par es el que sobra.
    /// </summary>
    private static void SinDibujos(string texto, string donde)
    {
        foreach (var c in texto)
            Assert.False(char.IsSurrogate(c) || char.IsSymbol(c),
                $"{donde}: «{texto}» todavía lleva «{c}» (U+{(int)c:X4}). Los dibuja el sistema " +
                "operativo, no heredan el color del texto y sin fuente de emoji salen como un cuadro.");
    }

    // ── Las tablas que son funciones puras ───────────────────────────────────────

    [Theory]
    [InlineData(Disponibilidad.DeVacaciones, "De vacaciones")]
    [InlineData(Disponibilidad.Libre, "Libre")]
    [InlineData(Disponibilidad.Ocupado, "Ocupado")]
    [InlineData(Disponibilidad.Sobrecargado, "Sobrecargado")]
    public void Capacidad_diceLaPalabraSola(Disponibilidad estado, string esperado)
    {
        Assert.Equal(esperado, CapacityStats.EtiquetaEstado(estado));
        SinDibujos(CapacityStats.EtiquetaEstado(estado), "CapacityStats.EtiquetaEstado");
    }

    [Theory]
    [InlineData(DevActivityStatus.Abierta, "Abierta")]
    [InlineData(DevActivityStatus.Cerrada, "Cerrada")]
    public void ActividadLibre_diceLaPalabraSola(DevActivityStatus estado, string esperado)
    {
        Assert.Equal(esperado, DevActivityService.Etiqueta(estado));
        SinDibujos(DevActivityService.Etiqueta(estado), "DevActivityService.Etiqueta");
    }

    [Theory]
    [InlineData(LeaveStatus.Pendiente, "Pendiente")]
    [InlineData(LeaveStatus.Aprobada, "Aprobada")]
    [InlineData(LeaveStatus.Rechazada, "Rechazada")]
    [InlineData(LeaveStatus.Cancelada, "Cancelada")]
    public void Permiso_diceLaPalabraSola(LeaveStatus estado, string esperado)
    {
        Assert.Equal(esperado, LeaveRequestService.Etiqueta(estado));
        SinDibujos(LeaveRequestService.Etiqueta(estado), "LeaveRequestService.Etiqueta");
    }

    /// <summary>
    /// Los seis, recorriendo el enum y no una lista escrita a mano: así, un estado nuevo entra en la
    /// comprobación el día que se declara y no el día que alguien se acuerde de esta prueba.
    /// </summary>
    [Fact]
    public void Programados_todosLosEstadosDicenLaPalabraSola()
    {
        foreach (var estado in Enum.GetValues<ScheduledDeploymentStatus>())
            SinDibujos(ProgramadosService.EtiquetaDeEstado(estado), "ProgramadosService.EtiquetaDeEstado");

        Assert.Equal("En ejecución", ProgramadosService.EtiquetaDeEstado(ScheduledDeploymentStatus.EnEjecucion));
        Assert.Equal("Perdido", ProgramadosService.EtiquetaDeEstado(ScheduledDeploymentStatus.Perdido));
    }

    /// <summary>
    /// El catálogo de consulta entero, por el mismo motivo: son diez tablas y la tentación de
    /// «devolverle su colorcito» a una sola es permanente.
    /// </summary>
    [Fact]
    public void CatalogosDeConsulta_ningunaEtiquetaLlevaDibujo()
    {
        foreach (var v in Enum.GetValues<SoftwareCategory>())
            SinDibujos(EtiquetasDeCatalogo.Categoria(v), "Categoria");
        foreach (var v in Enum.GetValues<SoftwareLicenseType>())
            SinDibujos(EtiquetasDeCatalogo.Licencia(v), "Licencia");
        foreach (var v in Enum.GetValues<SoftwareStatus>())
            SinDibujos(EtiquetasDeCatalogo.EstadoDePrograma(v), "EstadoDePrograma");
        foreach (var v in Enum.GetValues<AzureResourceType>())
            SinDibujos(EtiquetasDeCatalogo.TipoDeRecurso(v), "TipoDeRecurso");
        foreach (var v in Enum.GetValues<AzureResourceStatus>())
            SinDibujos(EtiquetasDeCatalogo.EstadoDeRecurso(v), "EstadoDeRecurso");
        foreach (var v in Enum.GetValues<AzureEnvironment>())
            SinDibujos(EtiquetasDeCatalogo.Ambiente(v), "Ambiente");
        foreach (var v in Enum.GetValues<TeamRole>())
            SinDibujos(EtiquetasDeCatalogo.RolDeEquipo(v), "RolDeEquipo");
        foreach (var v in Enum.GetValues<PoolPriority>())
            SinDibujos(EtiquetasDeCatalogo.PrioridadDelPool(v), "PrioridadDelPool");
        foreach (var v in Enum.GetValues<AuditAction>())
            SinDibujos(EtiquetasDeCatalogo.Accion(v), "Accion");
        foreach (var v in Enum.GetValues<AuditOutcome>())
            SinDibujos(EtiquetasDeCatalogo.Resultado(v), "Resultado");
    }

    /// <summary>
    /// La errata que venía del escritorio. Se corrige con una sola «i» porque el escritorio se apagó y
    /// la paridad que la sostenía dejó de existir; la prueba está para que nadie la «restaure»
    /// creyendo que arregla una diferencia.
    /// </summary>
    [Fact]
    public void Programas_laUtileriaDeRedVaConUnaSolaI()
    {
        Assert.Equal("Utilería de red", EtiquetasDeCatalogo.Categoria(SoftwareCategory.UtileriaRed));
    }

    // ── Las que hay que sacarle a un servicio ────────────────────────────────────

    /// <summary>
    /// <b>La que más urgía.</b> La etiqueta de una actividad libre no se queda en nuestra pantalla:
    /// acaba en la columna «Estado» de un .xlsx que se manda por correo y se abre en un equipo que no
    /// controlamos. Ahí no hay tema que ajustar ni captura que nos avise de que salió un cuadro, así
    /// que la comprobación se hace sobre la CELDA de verdad y no sobre lo que devuelve la función.
    /// </summary>
    [Fact]
    public async Task ExcelDeActividades_laCeldaDeEstadoLlevaLaPalabraSola()
    {
        using var db = TestDb.New();
        var lider = UsuarioDePrueba.Como(UserRole.Admin);

        db.Developers.Add(new Developer { Id = 71, FullName = "Ana", IsActive = true });
        db.SaveChanges();

        db.DevActivities.AddRange(
            new DevActivity
            {
                DeveloperId = 71, Title = "Revisión de logs",
                Status = DevActivityStatus.Abierta, CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new DevActivity
            {
                DeveloperId = 71, Title = "Alta de usuario",
                Status = DevActivityStatus.Cerrada,
                CreatedAt = DateTime.UtcNow.AddDays(-3), ClosedAt = DateTime.UtcNow.AddDays(-1)
            });
        db.SaveChanges();

        var auditoria = new AuditService(db, lider, new OrigenDePrueba());
        var servicio = new DevActivityService(db, lider, auditoria, new WorkSessionService(db, lider, auditoria));

        var libro = await servicio.ExcelDelEquipoAsync();

        var estados = CeldasDeLaColumna(libro, columna: 3);
        Assert.Equal(new[] { "Abierta", "Cerrada" }, estados.Order().ToArray());
        foreach (var celda in estados) SinDibujos(celda, "celda «Estado» del .xlsx de actividades");
    }

    /// <summary>
    /// El rack de servidores: la etiqueta de cada fila y el resumen de arriba, que llevaba TRES
    /// símbolos seguidos en una sola frase y era donde peor se veía el defecto.
    /// </summary>
    [Fact]
    public async Task EstadoDeServidores_niLaFilaNiElResumenLlevanDibujos()
    {
        using var db = TestDb.New();

        var sistema = new AppSystem { Name = "Portal", IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();

        var vieja = new AppRelease { AppSystemId = sistema.Id, Version = "1.0.0", CreatedAt = DateTime.UtcNow.AddDays(-10) };
        var nueva = new AppRelease { AppSystemId = sistema.Id, Version = "1.1.0", CreatedAt = DateTime.UtcNow.AddDays(-1) };
        db.AppReleases.AddRange(vieja, nueva);
        db.SaveChanges();

        // Uno al día, uno atrasado y uno que nunca se desplegó: los tres estados con etiqueta, y las
        // tres cifras del resumen distintas de cero.
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
            },
            new DeploymentTarget
            {
                Nombre = "web03", Host = "ftps://web03", Usuario = "publicador", RutaRemota = "/site/wwwroot",
                IsActive = true
            });
        db.SaveChanges();

        var foto = await new EstadoDeServidoresService(db, UsuarioDePrueba.Como(UserRole.Operaciones))
            .ObtenerAsync();

        foreach (var fila in foto.Servidores) SinDibujos(fila.EstadoTexto, $"estado de «{fila.Servidor}»");
        SinDibujos(foto.Resumen, "resumen del rack");

        Assert.Contains("1 al día", foto.Resumen);
        Assert.Contains("1 atrasado(s)", foto.Resumen);
        Assert.Contains("1 sin desplegar nunca", foto.Resumen);
    }

    /// <summary>
    /// Las notas: la columna de la rejilla y el desplegable del formulario. Se comprueban los dos
    /// porque son dos caminos distintos hasta la misma tabla, y basta con que uno se quede atrás para
    /// que el filtro enseñe una palabra que la columna de al lado no dice.
    /// </summary>
    [Fact]
    public async Task Notas_laPrioridadDiceLaPalabraSolaEnLaRejillaYEnElDesplegable()
    {
        using var db = TestDb.New();
        var lider = UsuarioDePrueba.Como(UserRole.Admin);

        db.Notes.AddRange(
            new Note { Title = "Urge", Priority = NotePriority.Alta, CreatedAt = DateTime.UtcNow },
            new Note { Title = "Cuando se pueda", Priority = NotePriority.Media, CreatedAt = DateTime.UtcNow.AddMinutes(-1) },
            new Note { Title = "Algún día", Priority = NotePriority.Baja, CreatedAt = DateTime.UtcNow.AddMinutes(-2) });
        db.SaveChanges();

        var datos = await new NotasService(db, lider, new AuditService(db, lider, new OrigenDePrueba()))
            .ListarAsync();

        Assert.Equal(new[] { "Alta", "Media", "Baja" }, datos.Notas.Select(n => n.PrioridadTexto).ToArray());
        Assert.Equal(new[] { "Alta", "Media", "Baja" }, datos.Prioridades.Select(p => p.Texto).ToArray());
    }

    /// <summary>Las vacaciones propias, que tienen su propia copia de los cuatro estados.</summary>
    [Fact]
    public async Task MisVacaciones_elEstadoDiceLaPalabraSola()
    {
        using var db = TestDb.New();

        var ana = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(ana);
        db.SaveChanges();

        foreach (var estado in Enum.GetValues<VacationStatus>())
            db.VacationRequests.Add(new VacationRequest
            {
                DeveloperId = ana.Id,
                StartDate = new DateTime(2026, 9, 1).AddDays((int)estado),
                EndDate = new DateTime(2026, 9, 2).AddDays((int)estado),
                Status = estado
            });
        db.SaveChanges();

        var mias = await Ausencias(db, UsuarioDePrueba.Como(UserRole.Desarrollador, ana.Id, userId: 9))
            .MisVacacionesAsync();

        Assert.Equal(Enum.GetValues<VacationStatus>().Length, mias.Solicitudes.Count);
        foreach (var s in mias.Solicitudes) SinDibujos(s.EtiquetaEstado, "estado de «Mis vacaciones»");
    }

    // ── La que se GUARDA ─────────────────────────────────────────────────────────

    /// <summary>
    /// La etiqueta de estado de un permiso no es solo un adorno de rejilla: se interpola en la
    /// descripción que se escribe en la BITÁCORA al resolverlo. Se comprueba aquí porque es lo que
    /// justifica la decisión de cambiarla igualmente: cae el SÍMBOLO y no la palabra, así que un
    /// asiento viejo («Permiso ✅ aprobada: …») y uno nuevo («Permiso aprobada: …») se leen igual y
    /// una búsqueda por «aprobada» sigue encontrando los dos.
    ///
    /// <para>El emoji de <c>EtiquetaTipo</c> sigue ahí a propósito —el usuario acotó esta tanda a los
    /// estados—, y por eso lo que se afirma es el PRINCIPIO del asiento y no la frase entera. Cuando
    /// se limpien los tipos, esta prueba se amplía; mientras tanto, no miente.</para>
    /// </summary>
    [Fact]
    public async Task Bitacora_alAprobarUnPermisoElAsientoYaNoLlevaElSimbolo()
    {
        using var db = TestDb.New();

        var ana = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(ana);
        db.SaveChanges();

        var lider = UsuarioDePrueba.Como(UserRole.Admin);
        var permisos = new LeaveRequestService(db, lider, new AuditService(db, lider, new OrigenDePrueba()));

        db.LeaveRequests.Add(new LeaveRequest
        {
            DeveloperId = ana.Id,
            Type = LeaveType.CitaMedica,
            Date = new DateTime(2026, 9, 10),
            DaysCount = 1,
            Reason = "Consulta",
            Status = LeaveStatus.Pendiente,
            RequestedByDeveloperId = ana.Id,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        int id = db.LeaveRequests.Single().Id;
        var (ok, _) = await permisos.AprobarAsync(id, "Adelante");
        Assert.True(ok);

        var asiento = db.AuditLogs
            .Where(a => a.EntityType == "LeaveRequest")
            .OrderByDescending(a => a.Id)
            .First();

        Assert.StartsWith("Permiso aprobada:", asiento.Details);
        Assert.DoesNotContain("✅", asiento.Details);
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────

    private static AusenciasService Ausencias(AppDbContext db, ICurrentUser quien)
    {
        var auditoria = new AuditService(db, quien, new OrigenDePrueba());
        return new AusenciasService(db, quien, new LeaveRequestService(db, quien, auditoria), auditoria);
    }

    /// <summary>El texto de una columna del libro, sin el renglón de encabezados.</summary>
    private static List<string> CeldasDeLaColumna(byte[] libro, int columna)
    {
        using var memoria = new MemoryStream(libro);
        using var archivo = new XLWorkbook(memoria);

        return archivo.Worksheet(1).RowsUsed().Skip(1)
            .Select(r => r.Cell(columna).GetString())
            .ToList();
    }
}
