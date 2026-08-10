using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Catalogos;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El alta, la edición y la baja de los catálogos: desarrolladores, contactos, programas y recursos
/// de Azure.
///
/// Las dos pruebas que más importan del archivo son las de la CLAVE de licencia: que editar
/// cualquier otro campo no se la lleve por delante, y que consultarla deje rastro en la bitácora.
/// Lo demás son reglas de captura; esas dos son las que protegen un secreto.
/// </summary>
public class CatalogosServiceTests
{
    private static CatalogosService Nuevo(AppDbContext db, ICurrentUser usuario)
    {
        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, usuario, origen);
        var personas = new PersonasQueryService(
            db, usuario, bitacora,
            new AuthService(db, usuario, bitacora),
            new PresenceService(db, usuario, origen),
            new AttendanceService(db, usuario, bitacora, origen),
            new DeveloperProfileService(db, usuario, bitacora),
            new AnnouncementService(db, usuario, bitacora));

        return new CatalogosService(db, usuario, bitacora, personas);
    }

    private static (CatalogosService svc, AppDbContext db) Nuevo(UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();
        return (Nuevo(db, UsuarioDePrueba.Como(rol)), db);
    }

    private static GuardarDesarrolladorRequest Dev(
        int? id = null, string nombre = "Ana", DateTime? ingreso = null, int dias = 15,
        bool activo = true) =>
        new(id, nombre, "ana@x.com", null, "Senior", ingreso, null, null, dias, activo, null);

    // ── Desarrolladores ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Alta_y_edicion_de_un_desarrollador()
    {
        var (svc, db) = Nuevo();

        var (ok, _, id) = await svc.GuardarDesarrolladorAsync(Dev());
        Assert.True(ok);

        var (ok2, _, _) = await svc.GuardarDesarrolladorAsync(Dev(id, nombre: "Ana María"));
        Assert.True(ok2);

        var guardado = await db.Developers.AsNoTracking().SingleAsync();
        Assert.Equal("Ana María", guardado.FullName);
        // Una sola ficha: editar no dio de alta una segunda.
        Assert.Equal(1, await db.Developers.CountAsync());
    }

    [Fact]
    public async Task El_nombre_es_obligatorio_y_los_espacios_no_cuentan()
    {
        var (svc, db) = Nuevo();

        var (ok, mensaje, _) = await svc.GuardarDesarrolladorAsync(Dev(nombre: "   "));

        Assert.False(ok);
        Assert.Contains("obligatorio", mensaje);
        Assert.Equal(0, await db.Developers.CountAsync());
    }

    /// <summary>
    /// El escritorio dejaba guardar una fecha de ingreso futura y solo se quejaba al calcular la
    /// LFT. Una antigüedad negativa no significa nada y envenena el cálculo de esa persona.
    /// </summary>
    [Fact]
    public async Task La_fecha_de_ingreso_futura_se_rechaza()
    {
        var (svc, _) = Nuevo();

        var (ok, mensaje, _) = await svc.GuardarDesarrolladorAsync(
            Dev(ingreso: DateTime.Today.AddDays(1)));

        Assert.False(ok);
        Assert.Contains("futura", mensaje);
    }

    [Fact]
    public async Task Los_campos_en_blanco_se_guardan_como_nulos_y_no_como_cadena_vacia()
    {
        var (svc, db) = Nuevo();

        await svc.GuardarDesarrolladorAsync(
            new GuardarDesarrolladorRequest(null, "Ana", "  ", "", null, null, "   ", "", 15, true, ""));

        var d = await db.Developers.AsNoTracking().SingleAsync();
        Assert.Null(d.Email);
        Assert.Null(d.Phone);
        Assert.Null(d.Address);
        Assert.Null(d.EquipmentSerial);
        Assert.Null(d.Notes);
    }

    [Fact]
    public async Task La_baja_DESACTIVA_y_no_borra()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev());

        var (ok, _) = await svc.DesactivarDesarrolladorAsync(id);

        Assert.True(ok);
        var d = await db.Developers.AsNoTracking().SingleAsync();
        Assert.False(d.IsActive);
        Assert.Equal(1, await db.Developers.CountAsync());
    }

    [Fact]
    public async Task Desactivar_a_quien_ya_estaba_inactivo_lo_dice_en_vez_de_callarse()
    {
        var (svc, _) = Nuevo();
        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev(activo: false));

        var (ok, mensaje) = await svc.DesactivarDesarrolladorAsync(id);

        Assert.False(ok);
        Assert.Contains("ya estaba inactivo", mensaje);
    }

    [Fact]
    public async Task Solo_el_lider_toca_los_catalogos()
    {
        var (svc, _) = Nuevo(UserRole.Desarrollador);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarDesarrolladorAsync(Dev()));
    }

    // ── Cuenta de acceso ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_acceso_deriva_el_usuario_del_correo_y_devuelve_la_temporal_una_vez()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev());

        var (ok, _, credenciales) = await svc.CrearAccesoAsync(id);

        Assert.True(ok);
        Assert.NotNull(credenciales);
        Assert.Equal("ana", credenciales!.Usuario);   // la parte del correo antes de la arroba
        Assert.NotEmpty(credenciales.ContrasenaTemporal);

        var cuenta = await db.Users.AsNoTracking().SingleAsync(u => u.DeveloperId == id);
        Assert.Equal(UserRole.Desarrollador, cuenta.Role);
        Assert.True(cuenta.MustChangePassword);
        // Lo que se guarda es el HASH: la temporal viajó en la respuesta y en ningún otro sitio.
        Assert.DoesNotContain(credenciales.ContrasenaTemporal, cuenta.PasswordHash);
    }

    [Fact]
    public async Task Un_usuario_ya_tomado_hace_que_el_siguiente_lleve_numero()
    {
        var (svc, db) = Nuevo();
        db.Users.Add(new User { Username = "ana", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        await db.SaveChangesAsync();

        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev());
        var (ok, _, credenciales) = await svc.CrearAccesoAsync(id);

        Assert.True(ok);
        Assert.Equal("ana2", credenciales!.Usuario);
    }

    /// <summary>
    /// Quien ya tiene cuenta no la recrea ni se le restablece la contraseña por la puerta de atrás:
    /// eso es una decisión con consecuencia propia y se toma en «Usuarios».
    /// </summary>
    [Fact]
    public async Task Crear_acceso_a_quien_ya_tiene_cuenta_lo_dice_y_no_la_toca()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev());
        await svc.CrearAccesoAsync(id);

        var hashAntes = await db.Users.AsNoTracking()
            .Where(u => u.DeveloperId == id).Select(u => u.PasswordHash).SingleAsync();

        var (ok, mensaje, credenciales) = await svc.CrearAccesoAsync(id);

        Assert.False(ok);
        Assert.Null(credenciales);
        Assert.Contains("ya tiene la cuenta", mensaje);

        var hashDespues = await db.Users.AsNoTracking()
            .Where(u => u.DeveloperId == id).Select(u => u.PasswordHash).SingleAsync();
        Assert.Equal(hashAntes, hashDespues);
        Assert.Equal(1, await db.Users.CountAsync(u => u.DeveloperId == id));
    }

    [Fact]
    public async Task A_alguien_inactivo_no_se_le_estrena_acceso()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarDesarrolladorAsync(Dev(activo: false));

        var (ok, mensaje, _) = await svc.CrearAccesoAsync(id);

        Assert.False(ok);
        Assert.Contains("inactivo", mensaje);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    // ── La sugerencia de la Ley Federal del Trabajo ──────────────────────────────

    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 14)]
    [InlineData(5, 20)]
    [InlineData(6, 22)]
    [InlineData(11, 24)]
    public void La_sugerencia_LFT_sigue_la_tabla_del_articulo_76(int anios, int esperados)
    {
        var (svc, _) = Nuevo();

        var sugerencia = svc.SugerenciaDeVacaciones(DateTime.Today.AddYears(-anios));

        Assert.Equal(esperados, sugerencia.Dias);
        Assert.Contains($"{anios} año(s)", sugerencia.Nota);
    }

    [Fact]
    public void Sin_cumplir_el_ano_la_sugerencia_es_proporcional_y_lo_dice()
    {
        var (svc, _) = Nuevo();

        var sugerencia = svc.SugerenciaDeVacaciones(DateTime.Today.AddDays(-182));

        Assert.InRange(sugerencia.Dias, 5, 7);   // la mitad de los 12 del primer año
        Assert.Contains("menos de un año", sugerencia.Nota);
    }

    // ── Contactos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alta_edicion_y_borrado_de_un_contacto()
    {
        var (svc, db) = Nuevo();

        var (ok, _, id) = await svc.GuardarContactoAsync(
            new GuardarContactoRequest(null, "Luis", "Compras", "ACME", null, null, null, null));
        Assert.True(ok);

        await svc.GuardarContactoAsync(
            new GuardarContactoRequest(id, "Luis Pérez", "Compras", "ACME", null, null, null, null));
        Assert.Equal("Luis Pérez", (await db.Contacts.AsNoTracking().SingleAsync()).Name);

        // El contacto SÍ se borra de verdad: no está referenciado por nada.
        var (borrado, _) = await svc.BorrarContactoAsync(id);
        Assert.True(borrado);
        Assert.Equal(0, await db.Contacts.CountAsync());
    }

    // ── Programas y su clave de licencia ─────────────────────────────────────────

    private static GuardarProgramaRequest Prog(int? id = null, string nombre = "Visual Studio",
        string? clave = null) =>
        new(id, nombre, SoftwareCategory.IDE, SoftwareStatus.EnUso,
            SoftwareLicenseType.Suscripcion, "2022", "Microsoft", clave, null, null, null, null);

    [Fact]
    public async Task Al_editar_sin_mandar_clave_la_que_ya_habia_se_CONSERVA()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarProgramaAsync(Prog(clave: "ABC-123"));

        // Se edita el nombre desde un formulario que no enseña la clave: manda nulo.
        await svc.GuardarProgramaAsync(Prog(id, nombre: "Visual Studio 2022", clave: null));

        var guardado = await db.SoftwareItems.AsNoTracking().SingleAsync();
        Assert.Equal("Visual Studio 2022", guardado.Name);
        Assert.Equal("ABC-123", guardado.LicenseKey);
    }

    [Fact]
    public async Task Mandar_la_clave_en_blanco_SI_la_quita_porque_es_una_decision_explicita()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarProgramaAsync(Prog(clave: "ABC-123"));

        await svc.GuardarProgramaAsync(Prog(id, clave: "   "));

        Assert.Null((await db.SoftwareItems.AsNoTracking().SingleAsync()).LicenseKey);
    }

    [Fact]
    public async Task Consultar_la_clave_la_devuelve_y_deja_rastro_en_la_bitacora()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarProgramaAsync(Prog(clave: "ABC-123"));

        var (ok, _, clave) = await svc.VerClaveDeLicenciaAsync(id);

        Assert.True(ok);
        Assert.Equal("ABC-123", clave);

        var registro = await db.AuditLogs.AsNoTracking()
            .SingleAsync(l => l.Action == AuditAction.Read);
        Assert.Equal("Software", registro.EntityType);
        Assert.Contains("clave de licencia", registro.Details);
    }

    /// <summary>
    /// La bitácora la lee más gente que la que puede ver el inventario. Un secreto copiado ahí ya no
    /// se retira, así que el detalle dice QUE se cambió, nunca a qué.
    /// </summary>
    [Fact]
    public async Task La_clave_NUNCA_aparece_en_el_detalle_de_la_bitacora()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarProgramaAsync(Prog(clave: "SECRETO-XYZ"));
        await svc.GuardarProgramaAsync(Prog(id, clave: "OTRO-SECRETO"));
        await svc.VerClaveDeLicenciaAsync(id);

        var detalles = await db.AuditLogs.AsNoTracking().Select(l => l.Details ?? "").ToListAsync();

        Assert.NotEmpty(detalles);
        Assert.All(detalles, d =>
        {
            Assert.DoesNotContain("SECRETO-XYZ", d);
            Assert.DoesNotContain("OTRO-SECRETO", d);
        });
    }

    [Fact]
    public async Task Pedir_la_clave_de_un_programa_que_no_tiene_lo_dice_sin_anotar_nada()
    {
        var (svc, db) = Nuevo();
        var (_, _, id) = await svc.GuardarProgramaAsync(Prog(clave: null));

        var (ok, mensaje, clave) = await svc.VerClaveDeLicenciaAsync(id);

        Assert.False(ok);
        Assert.Null(clave);
        Assert.Contains("no tiene clave", mensaje);
        Assert.Equal(0, await db.AuditLogs.CountAsync(l => l.Action == AuditAction.Read));
    }

    // ── Recursos de Azure ────────────────────────────────────────────────────────

    [Fact]
    public async Task Alta_y_edicion_de_un_recurso_de_Azure()
    {
        var (svc, db) = Nuevo();

        var (ok, _, id) = await svc.GuardarRecursoAzureAsync(new GuardarRecursoAzureRequest(
            null, "app-prod", AzureResourceType.AppService, AzureResourceStatus.EnUso,
            AzureEnvironment.Produccion, "rg-prod", "Pago x uso", "eastus", 120m, null, null));
        Assert.True(ok);

        await svc.GuardarRecursoAzureAsync(new GuardarRecursoAzureRequest(
            id, "app-prod", AzureResourceType.AppService, AzureResourceStatus.EnUso,
            AzureEnvironment.Produccion, "rg-prod", "Pago x uso", "eastus", 150m, null, "Se escaló"));

        var r = await db.AzureResources.AsNoTracking().SingleAsync();
        Assert.Equal(150m, r.MonthlyCostEstimate);
        Assert.Equal("Se escaló", r.Notes);
        Assert.NotNull(r.UpdatedAt);
    }

    [Fact]
    public async Task Un_costo_negativo_se_rechaza()
    {
        var (svc, _) = Nuevo();

        var (ok, mensaje, _) = await svc.GuardarRecursoAzureAsync(new GuardarRecursoAzureRequest(
            null, "app", AzureResourceType.AppService, AzureResourceStatus.EnUso,
            AzureEnvironment.Produccion, null, null, null, -1m, null, null));

        Assert.False(ok);
        Assert.Contains("negativo", mensaje);
    }
}
