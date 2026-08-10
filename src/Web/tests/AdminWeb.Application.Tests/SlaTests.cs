using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los compromisos de atención y sus avisos.
///
/// <para><b>Lo que más se prueba aquí es el rastro de alertas, y no por gusto.</b> En el escritorio,
/// «de qué ya avisé» vivía en un HashSet dentro del proceso; en el servidor eso se pierde en cada
/// despliegue, y sin persistirlo la primera vuelta del trabajo de fondo tras publicar una versión
/// volvería a avisar de todo lo ya avisado, a todo el equipo. Que un reinicio NO vuelva a escalar lo
/// ya escalado es justo lo que se está arreglando, así que se comprueba simulando el reinicio: se
/// tira el servicio y se levanta otro con contextos nuevos contra la misma base.</para>
/// </summary>
public class SlaTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

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

    // ── Andamiaje ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Cada servicio con su PROPIO contexto contra la misma base: en la web hay uno por petición, y
    /// una consulta tiene que ver lo que la escritura anterior dejó EN LA BASE y no lo que quedó
    /// rastreado en memoria.
    /// </summary>
    private SlaService Sla(AppDbContext db, ICurrentUser cu, ClienteDevOpsDePrueba? devops = null)
    {
        var ctx = OtroContexto(db);
        var auditoria = new AuditService(ctx, cu, new OrigenDePrueba());
        var configuracion = new SettingsService(ctx, cu, auditoria);

        // El cliente de DevOps es siempre de mentira: registrar avance publica un comentario, y
        // ninguna prueba sale a la red.
        var integracion = new DevOpsService(
            ctx, cu, configuracion, new UserSecretsService(ctx, cu, new ProtectorSimulado(), auditoria),
            auditoria, new NotificationService(OtroContexto(db)), devops ?? new ClienteDevOpsDePrueba());

        return new SlaService(ctx, cu, auditoria, configuracion, integracion);
    }

    /// <summary>
    /// Los avisos de SLA, con un buzón de mentira: el escalamiento también sale por correo y estas
    /// pruebas no pueden tocar la red. Sin <paramref name="buzon"/> se pone uno que nadie mira.
    /// </summary>
    private SlaNotificationService Avisos(AppDbContext db, ICurrentUser cu, CorreoDeMentira? buzon = null)
    {
        var ctx = OtroContexto(db);
        var auditoria = new AuditService(ctx, cu, new OrigenDePrueba());
        return new SlaNotificationService(
            ctx, Sla(db, cu), new SlaAlertTracker(OtroContexto(db)),
            new NotificationService(OtroContexto(db)), new SettingsService(ctx, cu, auditoria),
            buzon ?? new CorreoDeMentira());
    }

    /// <summary>Deja el correo de la aplicación configurado, que es lo que el envío exige antes de nada.</summary>
    private async Task ConfigurarCorreoAsync(AppDbContext db, ICurrentUser cu, string? escalamiento = null)
    {
        var ctx = OtroContexto(db);
        var ajustes = new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()));

        await ajustes.GuardarAsync(SettingsService.Claves.EmailEnabled, "true");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailAddress, "aplicacion@empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailPassword, "contrasena-de-aplicacion");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");

        if (escalamiento != null)
            await ajustes.GuardarAsync(SettingsService.Claves.SlaEscalationEmail, escalamiento);
    }

    private static UsuarioDePrueba Admin() => UsuarioDePrueba.Como(UserRole.Admin, userId: 9);
    private static UsuarioDePrueba Dev(int developerId) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId: 1);

    private static int NuevoDesarrollador(AppDbContext db, string nombre = "Ana Ruiz", string? correo = null)
    {
        var d = new Developer { FullName = nombre, IsActive = true, Email = correo };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static int NuevaCuenta(AppDbContext db, int developerId, UserRole rol = UserRole.Desarrollador)
    {
        var u = new User
        {
            Username = $"u{developerId}_{Guid.NewGuid():N}"[..12],
            PasswordHash = "x", FullName = "Cuenta", Role = rol, DeveloperId = developerId, IsActive = true
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    }

    private static int NuevoRequerimiento(AppDbContext db, string titulo = "Corregir facturación")
    {
        var r = new Requirement { Title = titulo, Status = RequirementStatus.EnDesarrollo };
        db.Requirements.Add(r);
        db.SaveChanges();
        return r.Id;
    }

    /// <summary>Un compromiso ya guardado, para no depender del alta en cada prueba.</summary>
    private static SlaCommitment Compromiso(
        AppDbContext db, int developerId, DateTime venceUtc,
        DateTime? proximoRecordatorioUtc = null, int? ticket = null, SlaStatus estado = SlaStatus.Activo)
    {
        var sla = new SlaCommitment
        {
            RequirementId = NuevoRequerimiento(db),
            DeveloperId = developerId,
            DueAtUtc = venceUtc,
            ReminderEveryHours = 24,
            NextReminderAtUtc = proximoRecordatorioUtc,
            DevOpsTicketExternalId = ticket,
            Status = estado,
            CreatedAt = DateTime.UtcNow
        };
        db.SlaCommitments.Add(sla);
        db.SaveChanges();
        return sla;
    }

    // ── El rastro de alertas sobrevive al reinicio ──────────────────────────────

    [Fact]
    public async Task Un_recordatorio_se_avisa_una_sola_vez_aunque_el_servidor_se_reinicie()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        NuevaCuenta(db, dev);

        var ahora = DateTime.UtcNow;
        Compromiso(db, dev, ahora.AddDays(2), proximoRecordatorioUtc: ahora.AddMinutes(-5));

        // Primera vuelta del trabajo de fondo.
        var primera = await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: ahora);
        Assert.Equal(1, primera.Avisados);

        // «Reinicio»: servicios nuevos, contextos nuevos, misma base. Con el rastro en memoria esto
        // volvería a avisar; con el rastro en la base, no.
        var segunda = await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: ahora.AddMinutes(15));
        Assert.Equal(0, segunda.Avisados);
        Assert.Equal(1, segunda.Pendientes);   // sigue pendiente: lo que no se repite es el aviso

        // Y no se creó un segundo aviso en la bandeja.
        Assert.Equal(1, await OtroContexto(db).Notifications.CountAsync());
    }

    [Fact]
    public async Task Al_pasarse_de_la_fecha_se_vuelve_a_avisar_una_vez_mas()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        NuevaCuenta(db, dev);

        var ahora = DateTime.UtcNow;
        Compromiso(db, dev, ahora.AddHours(1), proximoRecordatorioUtc: ahora.AddMinutes(-5));

        Assert.Equal(1, (await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: ahora)).Avisados);

        // Ya fuera de plazo: es información nueva y más grave, así que vuelve a avisarse.
        var despues = ahora.AddHours(2);
        Assert.Equal(1, (await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: despues)).Avisados);

        // Pero solo una vez: seguir vencido no es novedad.
        Assert.Equal(0, (await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: despues.AddHours(1))).Avisados);
    }

    [Fact]
    public async Task Posponer_reprograma_el_recordatorio_y_el_siguiente_vuelve_a_avisar()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        NuevaCuenta(db, dev);

        var ahora = DateTime.UtcNow;
        var sla = Compromiso(db, dev, ahora.AddDays(5), proximoRecordatorioUtc: ahora.AddMinutes(-5));

        Assert.Equal(1, (await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: ahora)).Avisados);

        var (ok, _) = await Sla(db, Dev(dev)).PosponerAsync(sla.Id, 4);
        Assert.True(ok);

        // Pasadas las cuatro horas el recordatorio vuelve a tocar, y como pertenece a un ciclo nuevo
        // se avisa otra vez: es la regla del escritorio («se olvida al salir de pendientes»).
        var luego = DateTime.UtcNow.AddHours(5);
        Assert.Equal(1, (await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: luego)).Avisados);
    }

    [Fact]
    public async Task El_rastro_queda_escrito_en_el_propio_compromiso()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        NuevaCuenta(db, dev);

        var ahora = DateTime.UtcNow;
        var sla = Compromiso(db, dev, ahora.AddDays(1), proximoRecordatorioUtc: ahora.AddMinutes(-1));

        await Avisos(db, Admin()).AvisarPendientesAsync(nowUtc: ahora);

        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.NotNull(guardado.ReminderNotifiedAtUtc);
        Assert.Null(guardado.OverdueNotifiedAtUtc);   // todavía está en plazo
    }

    // ── Escalamiento de incumplimientos ────────────────────────────────────────

    [Fact]
    public async Task Un_reinicio_no_vuelve_a_escalar_lo_ya_escalado()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int lider = NuevoDesarrollador(db, "Líder", "lider@empresa.com");
        NuevaCuenta(db, lider, UserRole.Admin);

        Compromiso(db, dev, DateTime.UtcNow.AddHours(-3));

        var primera = await Avisos(db, Admin()).EscalarIncumplimientosAsync();
        Assert.Equal(1, primera.Vencidos);
        Assert.Equal(1, primera.Escalados);

        // Servicios nuevos = proceso nuevo. El compromiso ya está marcado como notificado en la base.
        var segunda = await Avisos(db, Admin()).EscalarIncumplimientosAsync();
        Assert.Equal(0, segunda.Vencidos);
        Assert.Equal(1, await OtroContexto(db).Notifications.CountAsync());
    }

    [Fact]
    public async Task Revisar_vencimientos_marca_el_estado_y_solo_devuelve_lo_de_esta_vuelta()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var sla = Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        var vencidos = await Sla(db, Admin()).RevisarVencimientosAsync();
        Assert.Single(vencidos);
        Assert.All(vencidos, v => Assert.Null(v.BreachNotifiedAtUtc));

        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.Equal(SlaStatus.Vencido, guardado.Status);
        Assert.Null(guardado.NextReminderAtUtc);   // deja de recordar

        // La segunda vuelta ya no lo devuelve: dejó de ser Activo y no vuelve a entrar por aquí. Es
        // la misma acotación del escritorio, y la que evita que el trabajo de fondo escale de golpe
        // el histórico de vencidos que nunca se marcó como notificado.
        Assert.Empty(await Sla(db, Admin()).RevisarVencimientosAsync());
    }

    [Fact]
    public async Task Confirmar_el_aviso_queda_escrito_en_el_compromiso()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var sla = Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        await Sla(db, Admin()).RevisarVencimientosAsync();
        await Sla(db, Admin()).MarcarIncumplimientoNotificadoAsync([sla.Id]);

        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.NotNull(guardado.BreachNotifiedAtUtc);
    }

    [Fact]
    public async Task Sin_destinatario_resuelto_no_se_da_por_escalado()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var sla = Compromiso(db, dev, DateTime.UtcNow.AddHours(-2));

        // No hay ninguna cuenta de líder ni destinatario configurado: no hay a quién avisar.
        var resultado = await Avisos(db, Admin()).EscalarIncumplimientosAsync();
        Assert.True(resultado.SinDestinatario);
        Assert.Equal(0, resultado.Escalados);
        Assert.Empty(await OtroContexto(db).Notifications.ToListAsync());

        // El vencimiento SÍ queda marcado —el líder lo ve en su pantalla—, pero no se da por avisado:
        // esa es la constancia de que el aviso no llegó a darse.
        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.Equal(SlaStatus.Vencido, guardado.Status);
        Assert.Null(guardado.BreachNotifiedAtUtc);
    }

    [Fact]
    public async Task Sin_buzon_configurado_el_escalamiento_cae_en_los_lideres()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int lider = NuevoDesarrollador(db, "Líder");
        int cuentaLider = NuevaCuenta(db, lider, UserRole.Admin);

        Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        // Nadie configuró «SlaEscalationEmail»: un incumplimiento no puede quedarse sin llegar a
        // nadie, así que cae en los líderes activos.
        Assert.Equal(1, (await Avisos(db, Admin()).EscalarIncumplimientosAsync()).Escalados);

        var destinatarios = await OtroContexto(db).Notifications.AsNoTracking()
            .Select(n => n.ForUserId).ToListAsync();
        Assert.Equal([cuentaLider], destinatarios);
    }

    [Fact]
    public async Task El_escalamiento_va_a_los_buzones_configurados_y_admite_varios()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int jefe = NuevoDesarrollador(db, "Jefe", "jefe@empresa.com");
        int suplente = NuevoDesarrollador(db, "Suplente", "suplente@empresa.com");
        int ajeno = NuevoDesarrollador(db, "Ajeno", "ajeno@empresa.com");
        int cuentaJefe = NuevaCuenta(db, jefe);
        int cuentaSuplente = NuevaCuenta(db, suplente);
        NuevaCuenta(db, ajeno);

        // Separados por «;», con espacios y otra caja: tal como los escribe una persona.
        var ctx = OtroContexto(db);
        var auditoria = new AuditService(ctx, Admin(), new OrigenDePrueba());
        await new SettingsService(ctx, Admin(), auditoria)
            .GuardarAsync(SettingsService.Claves.SlaEscalationEmail, " JEFE@empresa.com ; suplente@empresa.com ");

        Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));
        var resultado = await Avisos(db, Admin()).EscalarIncumplimientosAsync();
        Assert.Equal(1, resultado.Vencidos);

        var destinatarios = await OtroContexto(db).Notifications.AsNoTracking()
            .Select(n => n.ForUserId).ToListAsync();
        Assert.Equal(2, destinatarios.Count);
        Assert.Contains(cuentaJefe, destinatarios);
        Assert.Contains(cuentaSuplente, destinatarios);
    }

    [Fact]
    public void Los_buzones_se_separan_por_punto_y_coma_o_coma_sin_repetidos()
    {
        var buzones = SlaNotificationService.SepararBuzones("a@x.com; b@x.com , A@X.COM ; sin-arroba");

        Assert.Equal(2, buzones.Count);
        Assert.Contains("a@x.com", buzones);
        Assert.Contains("b@x.com", buzones);
    }

    // ── El escalamiento también sale por correo ────────────────────────────────
    //
    // Sin red: el envío pasa por IClienteDeCorreo y aquí se le pone el mismo buzón de mentira que
    // usan las pruebas del resumen. Lo que se comprueba es la regla que hace que este canal se pueda
    // añadir sin riesgo: el aviso dentro de la aplicación llega pase lo que pase con el SMTP.

    [Fact]
    public async Task El_escalamiento_sale_ademas_por_correo_a_los_buzones_configurados()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int jefe = NuevoDesarrollador(db, "Jefe", "jefe@empresa.com");
        NuevaCuenta(db, jefe);

        await ConfigurarCorreoAsync(db, Admin(), "jefe@empresa.com; suplente@empresa.com");
        Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        var buzon = new CorreoDeMentira();
        var resultado = await Avisos(db, Admin(), buzon).EscalarIncumplimientosAsync();

        Assert.True(resultado.SalioPorCorreo);
        Assert.Null(resultado.FalloDelCorreo);

        var correo = Assert.Single(buzon.Enviados);
        Assert.Equal(new[] { "jefe@empresa.com", "suplente@empresa.com" }, correo.Destinatarios);
        Assert.Equal("SLA vencido: 1 compromiso(s)", correo.Asunto);
        Assert.Contains("Se vencieron 1 compromiso(s) de atención", correo.Cuerpo);
        Assert.Contains("Ana Ruiz", correo.Cuerpo);
        Assert.Null(correo.Adjunto);

        // Y el aviso dentro de la aplicación sigue estando: el correo se AÑADE, no sustituye.
        Assert.Equal(1, await OtroContexto(db).Notifications.CountAsync());
    }

    /// <summary>
    /// La regla que permite añadir el correo sin arriesgar nada: si el SMTP falla, el aviso in-app ya
    /// se entregó y el escalamiento se da por hecho igual. Al revés no puede pasar.
    /// </summary>
    [Fact]
    public async Task Si_el_correo_falla_el_aviso_dentro_de_la_aplicacion_llega_igual()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int jefe = NuevoDesarrollador(db, "Jefe", "jefe@empresa.com");
        NuevaCuenta(db, jefe);

        await ConfigurarCorreoAsync(db, Admin(), "jefe@empresa.com");
        var sla = Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        var resultado = await Avisos(db, Admin(), new CorreoDeMentira { FallaElEnvio = true })
            .EscalarIncumplimientosAsync();

        Assert.Equal(1, resultado.Escalados);
        Assert.False(resultado.SinDestinatario);
        Assert.False(resultado.SalioPorCorreo);
        Assert.Contains("rechazó el envío", resultado.FalloDelCorreo);

        Assert.Equal(1, await OtroContexto(db).Notifications.CountAsync());

        // Y queda marcado como notificado: el aviso SÍ se dio, por el canal que siempre funciona.
        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.NotNull(guardado.BreachNotifiedAtUtc);
    }

    [Fact]
    public async Task Sin_correo_configurado_el_escalamiento_sale_solo_dentro_de_la_aplicacion()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int lider = NuevoDesarrollador(db, "Líder");
        NuevaCuenta(db, lider, UserRole.Admin);

        Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        var buzon = new CorreoDeMentira();
        var resultado = await Avisos(db, Admin(), buzon).EscalarIncumplimientosAsync();

        Assert.Equal(1, resultado.Escalados);
        Assert.False(resultado.SalioPorCorreo);
        Assert.Empty(buzon.Enviados);

        // El motivo se devuelve para que el trabajo de fondo lo pueda registrar: un buzón sin
        // configurar no puede pasar meses en silencio solo porque el aviso in-app sí llega.
        Assert.Contains("no está habilitado", resultado.FalloDelCorreo);
    }

    /// <summary>
    /// La caída del escritorio: sin buzón de escalamiento capturado, el correo sale a la propia
    /// cuenta de la aplicación para que el incumplimiento no se quede dentro de la máquina.
    /// </summary>
    [Fact]
    public async Task Sin_buzon_de_escalamiento_el_correo_cae_en_la_cuenta_de_la_aplicacion()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int lider = NuevoDesarrollador(db, "Líder");
        NuevaCuenta(db, lider, UserRole.Admin);

        await ConfigurarCorreoAsync(db, Admin());
        Compromiso(db, dev, DateTime.UtcNow.AddHours(-1));

        var buzon = new CorreoDeMentira();
        await Avisos(db, Admin(), buzon).EscalarIncumplimientosAsync();

        var correo = Assert.Single(buzon.Enviados);
        Assert.Equal(new[] { "aplicacion@empresa.com" }, correo.Destinatarios);
    }

    [Fact]
    public async Task Sin_nada_vencido_no_sale_ningun_correo()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        await ConfigurarCorreoAsync(db, Admin(), "jefe@empresa.com");
        Compromiso(db, dev, DateTime.UtcNow.AddDays(3));   // todavía en plazo

        var buzon = new CorreoDeMentira();
        var resultado = await Avisos(db, Admin(), buzon).EscalarIncumplimientosAsync();

        Assert.Equal(0, resultado.Vencidos);
        Assert.Empty(buzon.Enviados);
    }

    /// <summary>
    /// El texto del correo es el del escritorio, palabra por palabra: quien ya recibía estos avisos
    /// no tiene que volver a aprender a leerlos.
    /// </summary>
    [Fact]
    public void El_cuerpo_del_escalamiento_lleva_una_linea_por_compromiso()
    {
        var vencidos = new List<SlaCommitment>
        {
            new()
            {
                Id = 1,
                DueAtUtc = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc),
                Developer = new Developer { FullName = "Ana Ruiz" },
                Requirement = new Requirement { Id = 3, Title = "Corregir facturación" },
                LastCommentAtUtc = new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Id = 2,
                DueAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
                Developer = new Developer { FullName = "Beto Lara" },
                Requirement = new Requirement { Id = 4, Title = "Pantalla nueva" }
            }
        };

        var cuerpo = SlaNotificationService.ComponerEscalamiento(vencidos);

        Assert.StartsWith("Se vencieron 2 compromiso(s) de atención:", cuerpo);
        Assert.Contains("Ana Ruiz — Requerimiento #3 — Corregir facturación", cuerpo);
        Assert.Contains("último comentario:", cuerpo);
        Assert.Contains("Beto Lara — Requerimiento #4 — Pantalla nueva", cuerpo);
        Assert.Contains("sin comentarios en el ticket", cuerpo);
        Assert.EndsWith("— Administrador de Desarrollo", cuerpo);

        Assert.Equal("SLA vencido: 2 compromiso(s)", SlaNotificationService.AsuntoDelEscalamiento(2));
    }

    // ── Alta y reglas del compromiso ───────────────────────────────────────────

    [Fact]
    public async Task Asignar_exige_ser_lider()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int req = NuevoRequerimiento(db);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Sla(db, Dev(dev)).AsignarAsync(
                SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddDays(1), 24, null, null, null));
    }

    [Fact]
    public async Task No_se_admite_una_fecha_pasada_ni_dos_SLA_activos_sobre_lo_mismo()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int req = NuevoRequerimiento(db);
        var servicio = Sla(db, Admin());

        var pasada = await servicio.AsignarAsync(
            SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddHours(-1), 24, null, null, null);
        Assert.False(pasada.ok);
        Assert.Contains("ya pasó", pasada.mensaje);

        var primera = await servicio.AsignarAsync(
            SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddDays(1), 24, null, null, null);
        Assert.True(primera.ok);

        var repetida = await Sla(db, Admin()).AsignarAsync(
            SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddDays(2), 24, null, null, null);
        Assert.False(repetida.ok);
        Assert.Contains("ya tiene un SLA activo", repetida.mensaje);
    }

    [Fact]
    public async Task El_objetivo_es_un_requerimiento_o_una_actividad_pero_no_ambos()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);

        var ninguno = await Sla(db, Admin()).AsignarAsync(
            new SlaObjetivo(null, null), dev, DateTime.UtcNow.AddDays(1), 24, null, null, null);

        Assert.False(ninguno.ok);
        Assert.Contains("no a ambos", ninguno.mensaje);
    }

    [Fact]
    public async Task La_URL_del_ticket_solo_se_acepta_si_es_http_o_https()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        int req = NuevoRequerimiento(db);

        // El enlace acaba en el navegador de otra persona: un «javascript:» se ejecutaría en su sesión.
        var maliciosa = await Sla(db, Admin()).AsignarAsync(
            SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddDays(1), 24, 10,
            "javascript:alert(document.cookie)", null);

        Assert.False(maliciosa.ok);
        Assert.Contains("http", maliciosa.mensaje);

        var buena = await Sla(db, Admin()).AsignarAsync(
            SlaObjetivo.Requerimiento(req), dev, DateTime.UtcNow.AddDays(1), 24, 10,
            "https://dev.azure.com/x/_workitems/edit/10", null);
        Assert.True(buena.ok);
    }

    [Fact]
    public void El_primer_recordatorio_nunca_cae_despues_del_vencimiento()
    {
        var ahora = new DateTime(2026, 8, 6, 9, 0, 0, DateTimeKind.Utc);
        var vence = ahora.AddHours(3);

        // Intervalo más largo que el plazo: se recuerda AL VENCER, no nunca.
        Assert.Equal(vence, SlaService.PrimerRecordatorio(ahora, vence, 24));
        Assert.Equal(ahora.AddHours(1), SlaService.PrimerRecordatorio(ahora, vence, 1));

        // «Solo al vencer» (0 horas) también programa un recordatorio: el del propio vencimiento.
        Assert.Equal(vence, SlaService.PrimerRecordatorio(ahora, vence, 0));
    }

    [Fact]
    public async Task Posponer_tiene_topes_y_nunca_pasa_del_vencimiento()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var sla = Compromiso(db, dev, DateTime.UtcNow.AddHours(2),
            proximoRecordatorioUtc: DateTime.UtcNow.AddMinutes(-1));

        var demasiado = await Sla(db, Dev(dev)).PosponerAsync(sla.Id, 100);
        Assert.False(demasiado.ok);
        Assert.Contains("entre 1 y 72", demasiado.mensaje);

        // Se piden 48 horas sobre un plazo de 2: el recordatorio se queda EN el vencimiento.
        var (ok, _) = await Sla(db, Dev(dev)).PosponerAsync(sla.Id, 48);
        Assert.True(ok);

        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.Equal(guardado.DueAtUtc, guardado.NextReminderAtUtc);
    }

    [Fact]
    public async Task Nadie_pospone_el_compromiso_de_otra_persona()
    {
        var db = TestDb.New();
        int dueno = NuevoDesarrollador(db);
        int otro = NuevoDesarrollador(db, "Otro");
        var sla = Compromiso(db, dueno, DateTime.UtcNow.AddDays(1));

        await Assert.ThrowsAsync<AuthorizationException>(() => Sla(db, Dev(otro)).PosponerAsync(sla.Id, 4));
    }

    // ── Cierre automático por el ticket de DevOps ──────────────────────────────

    [Fact]
    public async Task Un_ticket_cerrado_a_tiempo_cierra_el_SLA_como_cumplido_y_no_se_escala()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var ahora = DateTime.UtcNow;

        // Venció hace una hora, pero el ticket se cerró ANTES del plazo: no es un incumplimiento.
        var sla = Compromiso(db, dev, ahora.AddHours(-1), ticket: 555);
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 555, Title = "T", State = "Done",
            UpdatedAtExternal = ahora.AddHours(-3), SyncedAt = ahora
        });
        db.SaveChanges();

        var vencidos = await Sla(db, Admin()).RevisarVencimientosAsync(ahora);
        Assert.Empty(vencidos);

        var guardado = await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id);
        Assert.Equal(SlaStatus.Cumplido, guardado.Status);
        Assert.Null(guardado.NextReminderAtUtc);
        Assert.Contains("automáticamente", guardado.Notes);
    }

    [Fact]
    public async Task Un_ticket_descartado_deja_el_SLA_cancelado_y_uno_cerrado_tarde_vencido()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var ahora = DateTime.UtcNow;

        var descartado = Compromiso(db, dev, ahora.AddHours(5), ticket: 100);
        var tardio = Compromiso(db, dev, ahora.AddHours(-5), ticket: 200);
        db.DevOpsTickets.AddRange(
            new DevOpsTicket { ExternalId = 100, Title = "A", State = "Removed", UpdatedAtExternal = ahora.AddHours(-1), SyncedAt = ahora },
            new DevOpsTicket { ExternalId = 200, Title = "B", State = "Closed", UpdatedAtExternal = ahora.AddHours(-1), SyncedAt = ahora });
        db.SaveChanges();

        await Sla(db, Admin()).ReconciliarConDevOpsAsync(nowUtc: ahora);

        var ctx = OtroContexto(db);
        Assert.Equal(SlaStatus.Cancelado, (await ctx.SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == descartado.Id)).Status);
        Assert.Equal(SlaStatus.Vencido, (await ctx.SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == tardio.Id)).Status);
    }

    [Fact]
    public async Task Un_ticket_todavia_abierto_no_toca_el_SLA()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var ahora = DateTime.UtcNow;

        var sla = Compromiso(db, dev, ahora.AddDays(1), ticket: 300);
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 300, Title = "C", State = "Active", UpdatedAtExternal = ahora, SyncedAt = ahora
        });
        db.SaveChanges();

        Assert.Equal(0, await Sla(db, Admin()).ReconciliarConDevOpsAsync(nowUtc: ahora));
        Assert.Equal(SlaStatus.Activo,
            (await OtroContexto(db).SlaCommitments.AsNoTracking().FirstAsync(s => s.Id == sla.Id)).Status);
    }

    // ── Pantallas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mis_SLA_solo_trae_los_propios_y_avisa_si_la_cuenta_no_tiene_ficha()
    {
        var db = TestDb.New();
        int mio = NuevoDesarrollador(db);
        int ajeno = NuevoDesarrollador(db, "Ajeno");
        Compromiso(db, mio, DateTime.UtcNow.AddDays(1));
        Compromiso(db, ajeno, DateTime.UtcNow.AddDays(1));

        var mios = await Sla(db, Dev(mio)).MisCompromisosAsync();
        Assert.True(mios.TieneFicha);
        Assert.Single(mios.Compromisos);
        Assert.All(mios.Compromisos, c => Assert.Equal(mio, c.DesarrolladorId));

        var sinFicha = await Sla(db, UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 3))
            .MisCompromisosAsync();
        Assert.False(sinFicha.TieneFicha);
        Assert.Empty(sinFicha.Compromisos);
    }

    [Fact]
    public async Task La_pantalla_del_lider_cuenta_las_tarjetas_sobre_todo_y_no_sobre_el_filtro()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        var ahora = DateTime.UtcNow;

        Compromiso(db, dev, ahora.AddHours(5));                              // activo, vence en 24 h
        Compromiso(db, dev, ahora.AddDays(10));                              // activo, lejos
        Compromiso(db, dev, ahora.AddHours(-2));                             // activo pero pasado de fecha
        Compromiso(db, dev, ahora.AddDays(3), estado: SlaStatus.Cumplido);   // cerrado

        var pantalla = await Sla(db, Admin()).PantallaDelLiderAsync(SlaStatus.Cumplido);

        Assert.Single(pantalla.Compromisos);            // el filtro solo trae el cumplido…
        Assert.Equal(3, pantalla.Resumen.Activos);      // …pero las tarjetas siguen contando todo
        Assert.Equal(1, pantalla.Resumen.VencenEn24h);
        Assert.Equal(1, pantalla.Resumen.FueraDePlazo);
    }

    [Fact]
    public async Task La_pantalla_del_lider_es_solo_del_lider()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);

        await Assert.ThrowsAsync<AuthorizationException>(() => Sla(db, Dev(dev)).PantallaDelLiderAsync(null));
        await Assert.ThrowsAsync<AuthorizationException>(() => Sla(db, Dev(dev)).OpcionesAsync());
    }

    // ── Políticas automáticas por prioridad ────────────────────────────────────

    [Fact]
    public void Las_politicas_siempre_son_cuatro_y_por_omision_estan_apagadas()
    {
        var porDefecto = SlaPolicyStore.Parse(null);

        Assert.Equal(4, porDefecto.Count);
        Assert.All(porDefecto, p => Assert.False(p.Enabled));

        // Un JSON corrupto no puede dejar la pantalla en blanco: cae a los valores por defecto.
        Assert.Equal(4, SlaPolicyStore.Parse("{no es json}").Count);

        // Una lista incompleta se completa, y los valores fuera de rango se acotan.
        var completadas = SlaPolicyStore.Parse(
            SlaPolicyStore.Serialize([new SlaPolicy(1, true, 0, 5000)]));
        Assert.Equal(4, completadas.Count);
        var p1 = completadas.First(p => p.Priority == 1);
        Assert.True(p1.Hours >= 1);
        Assert.Equal(720, p1.ReminderEveryHours);
    }

    [Fact]
    public void Resolver_devuelve_la_politica_solo_si_esta_activa()
    {
        var politicas = new List<SlaPolicy> { new(1, true, 4, 2), new(2, false, 8, 4) };

        Assert.NotNull(SlaPolicyStore.Resolver(politicas, "1"));
        Assert.Null(SlaPolicyStore.Resolver(politicas, "2"));    // desactivada
        Assert.Null(SlaPolicyStore.Resolver(politicas, "9"));    // sin política
        Assert.Null(SlaPolicyStore.Resolver(politicas, null));   // sin prioridad en el ticket
    }

    [Fact]
    public async Task Las_politicas_se_guardan_y_se_releen_completas()
    {
        var db = TestDb.New();

        var (ok, _) = await Sla(db, Admin()).GuardarPoliticasAsync(
            [new(1, "Muy alta", true, 6, 2), new(3, "Media", false, 24, 24)]);
        Assert.True(ok);

        var releidas = await Sla(db, Admin()).PoliticasAsync();
        Assert.Equal(4, releidas.Count);

        var muyAlta = releidas.First(p => p.Prioridad == 1);
        Assert.True(muyAlta.Activa);
        Assert.Equal(6, muyAlta.Horas);
        Assert.Equal(2, muyAlta.RecordatorioCadaHoras);
    }

    [Fact]
    public async Task Guardar_politicas_exige_ser_lider()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Sla(db, Dev(dev)).GuardarPoliticasAsync([new(1, "Muy alta", true, 4, 2)]));
    }

    // ── Avisos de la fecha comprometida ────────────────────────────────────────

    [Fact]
    public void Solo_se_avisa_al_cruzar_un_umbral_y_no_todos_los_dias()
    {
        var hoy = new DateTime(2026, 8, 6);
        var req = new Requirement { Id = 1, Title = "X", Status = RequirementStatus.EnDesarrollo };

        // A cinco días no toca nada.
        req.CommittedDeliveryDate = hoy.AddDays(5);
        Assert.Empty(CommitmentAlertService.Calcular([(req, 1)], hoy));

        // A tres, el umbral de 3. A dos, el MISMO umbral: la clave se repite y el dedupe lo calla,
        // que es justo lo que se quiere para no avisar los días intermedios.
        req.CommittedDeliveryDate = hoy.AddDays(3);
        var aTres = CommitmentAlertService.Calcular([(req, 1)], hoy).Single();

        req.CommittedDeliveryDate = hoy.AddDays(3);
        var aDos = CommitmentAlertService.Calcular([(req, 1)], hoy.AddDays(1)).Single();
        Assert.Equal(aTres.DedupeKey, aDos.DedupeKey);

        // Ya vencido: umbral distinto, clave distinta, aviso nuevo.
        var vencido = CommitmentAlertService.Calcular([(req, 1)], hoy.AddDays(7)).Single();
        Assert.NotEqual(aTres.DedupeKey, vencido.DedupeKey);
        Assert.Contains("venció", vencido.Etiqueta);
    }

    [Fact]
    public void Lo_entregado_o_cancelado_ya_no_se_apura()
    {
        var hoy = new DateTime(2026, 8, 6);
        var entregado = new Requirement
        {
            Id = 1, Title = "X", Status = RequirementStatus.Entregado, CommittedDeliveryDate = hoy.AddDays(-4)
        };
        var cancelado = new Requirement
        {
            Id = 2, Title = "Y", Status = RequirementStatus.Cancelado, CommittedDeliveryDate = hoy.AddDays(-4)
        };

        Assert.Empty(CommitmentAlertService.Calcular([(entregado, 1), (cancelado, 1)], hoy));
    }

    [Fact]
    public async Task Mover_la_fecha_comprometida_vuelve_a_avisar_pero_repetir_la_revision_no()
    {
        var db = TestDb.New();
        int dev = NuevoDesarrollador(db);
        NuevaCuenta(db, dev);

        var hoy = DateTime.Today;
        var req = new Requirement
        {
            Title = "Reporte mensual", Status = RequirementStatus.EnDesarrollo, CommittedDeliveryDate = hoy.AddDays(1)
        };
        db.Requirements.Add(req);
        db.SaveChanges();
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev });
        db.SaveChanges();

        Assert.Equal(1, await new CommitmentAlertService(OtroContexto(db)).RevisarYAvisarAsync(hoy));

        // Otra vuelta del trabajo de fondo, sin cambios: nada nuevo.
        Assert.Equal(0, await new CommitmentAlertService(OtroContexto(db)).RevisarYAvisarAsync(hoy));

        // El líder mueve la fecha: es información nueva y vuelve a avisarse.
        var ctx = OtroContexto(db);
        var guardado = await ctx.Requirements.FirstAsync(r => r.Id == req.Id);
        guardado.CommittedDeliveryDate = hoy.AddDays(-1);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await new CommitmentAlertService(OtroContexto(db)).RevisarYAvisarAsync(hoy));
    }
}
