using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// QUÉ EQUIPO SE ENCARGA DE CADA SISTEMA.
///
/// <para><b>El dato existía y no se podía escribir.</b> La columna estaba en la tabla y el organigrama
/// la leía —de ahí el «2 sistema(s)» de cada caja—, pero ni el alta ni la edición la tocaban: lo que
/// hay en producción lo escribió el ejecutable de escritorio, que ya no se usa. O sea que estaba a la
/// vista, envejeciendo, y nadie podía corregirlo desde la aplicación oficial.</para>
///
/// <para>De paso se arregla algo peor que faltar: editar un sistema le BORRABA la descripción. La
/// pantalla encadenaba dos cuadros de texto y el de la descripción salía en blanco, así que cambiarle
/// el nombre a un sistema la perdía — y la bitácora lo registraba como una edición normal. Eso vivía
/// en el cliente y aquí se comprueba el lado que sí se puede probar: que el servicio guarda lo que le
/// mandan y no inventa nulos.</para>
/// </summary>
public class SistemaPorEquipoTests
{
    /// <summary>
    /// Solo el servicio de CATÁLOGO, que es el que escribe. La consulta que arma el DTO necesita medio
    /// subsistema de despliegues montado —el ejecutor, los ajustes— y aquí lo que hay que comprobar es
    /// lo que se GUARDA: se relee de la base, que es donde se ve si se guardó.
    /// </summary>
    private static (DespliegueCatalogoService catalogo, AppDbContext db) Escenario(UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Plataforma" });
        db.Teams.Add(new Team { Id = 2, Name = "Soporte", EquipoPadreId = 1 });
        db.SaveChanges();

        var quien = UsuarioDePrueba.Como(rol);
        var bitacora = new AuditService(db, quien, new OrigenDePrueba());
        return (new DespliegueCatalogoService(db, quien, bitacora, new SettingsService(db, quien, bitacora)), db);
    }

    /// <summary>El sistema tal como quedó escrito, releído sin rastreo.</summary>
    private static AppSystem Releer(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        return db.AppSystems.Single();
    }

    // ── El grifo que faltaba ─────────────────────────────────────────────────────

    [Fact]
    public async Task ALDARLO_DE_ALTA_se_le_puede_poner_equipo()
    {
        var (catalogo, db) = Escenario();

        var (ok, _) = await catalogo.CrearSistemaAsync(
            new GuardarSistemaRequest("Expediente", "El expediente electrónico.", null, EquipoId: 2));
        Assert.True(ok);

        Assert.Equal(2, Releer(db).TeamId);
    }

    [Fact]
    public async Task ALEDITARLO_se_le_puede_cambiar_el_equipo()
    {
        var (catalogo, db) = Escenario();
        await catalogo.CrearSistemaAsync(new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: 1));
        int id = Releer(db).Id;

        await catalogo.EditarSistemaAsync(id,
            new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: 2));

        Assert.Equal(2, Releer(db).TeamId);
    }

    /// <summary>Dejarlo sin asignar también tiene que poder hacerse, o el dato sería una trampa de un solo sentido.</summary>
    [Fact]
    public async Task SE_LE_PUEDE_QUITAR_el_equipo()
    {
        var (catalogo, db) = Escenario();
        await catalogo.CrearSistemaAsync(new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: 1));
        int id = Releer(db).Id;

        await catalogo.EditarSistemaAsync(id,
            new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: null));

        Assert.Null(Releer(db).TeamId);
    }

    /// <summary>
    /// <b>Asignar es QUITÁRSELO a quien lo tuviera.</b> Un sistema lo lleva un equipo: no hay ni un
    /// dato ni una petición que pida lo contrario, y una tabla intermedia cambiaría a la vez el
    /// organigrama, el catálogo, la limpieza de datos y el PDF. Lo que hay que garantizar es que no
    /// quede contado en los dos sitios; que la pantalla lo AVISE antes es cosa suya y no de aquí.
    /// </summary>
    [Fact]
    public async Task ASIGNARLO_A_OTRO_se_lo_quita_al_primero()
    {
        var (catalogo, db) = Escenario();
        await catalogo.CrearSistemaAsync(new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: 1));
        int id = Releer(db).Id;

        await catalogo.EditarSistemaAsync(id,
            new GuardarSistemaRequest("Expediente", "Uno.", null, EquipoId: 2));

        db.ChangeTracker.Clear();
        Assert.Empty(db.AppSystems.Where(x => x.TeamId == 1).ToList());
        Assert.Single(db.AppSystems.Where(x => x.TeamId == 2).ToList());
    }

    // ── Lo que se borraba ────────────────────────────────────────────────────────

    /// <summary>
    /// El servicio guarda lo que le mandan: si le llega la descripción, la conserva. La pantalla es la
    /// que la perdía por preguntar con la caja en blanco, y eso se arregló allí; esto deja escrito que
    /// el servicio nunca fue el problema, para que nadie lo busque aquí.
    /// </summary>
    [Fact]
    public async Task LA_DESCRIPCION_SE_CONSERVA_si_se_manda()
    {
        var (catalogo, db) = Escenario();
        await catalogo.CrearSistemaAsync(
            new GuardarSistemaRequest("Expediente", "El expediente electrónico.", null));
        int id = Releer(db).Id;

        await catalogo.EditarSistemaAsync(id,
            new GuardarSistemaRequest("Expediente Web", "El expediente electrónico.", null));

        var sistema = Releer(db);
        Assert.Equal("Expediente Web", sistema.Name);
        Assert.Equal("El expediente electrónico.", sistema.Description);
    }

    // ── La guarda ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Operaciones despliega, no administra el catálogo. Decir qué equipo se encarga de un sistema es
    /// una decisión de organización, y la guarda ya estaba puesta: se comprueba para que el campo
    /// nuevo no se cuele por una puerta que nadie volvió a mirar.
    /// </summary>
    [Fact]
    public async Task OPERACIONES_no_reparte_sistemas()
    {
        var (catalogo, _) = Escenario(UserRole.Operaciones);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            catalogo.CrearSistemaAsync(new GuardarSistemaRequest("Expediente", null, null, EquipoId: 1)));
    }
}
