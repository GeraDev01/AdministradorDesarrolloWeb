using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// QUÉ HACE, EN CADA EQUIPO, QUIEN TIENE CADA ROL.
///
/// <para>Sustituye a teclear la misma frase una vez por persona: en el organigrama de la casa, los
/// tres «Fullstack» de un subequipo decían lo mismo palabra por palabra y los tres del subequipo de
/// al lado decían otra cosa, también repetida tres veces.</para>
///
/// <para><b>Lo que este archivo persigue es que la descripción NO SE COPIE a las fichas.</b> Se
/// resuelve al leer, y esa decisión es lo único que impide que el organigrama se quede mudo justo
/// después de una reorganización: la función propia se borra al mover a alguien de equipo y el rol
/// no, así que nadie vuelve a pasar por la asignación de rol.</para>
/// </summary>
public class DescripcionesDeRolTests
{
    private static PersonasQueryService Nuevo(AppDbContext db, ICurrentUser usuario)
    {
        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, usuario, origen);
        return new PersonasQueryService(
            db, usuario, bitacora,
            new AuthService(db, usuario, bitacora),
            new PresenceService(db, usuario, origen),
            new AttendanceService(db, usuario, bitacora, origen),
            new DeveloperProfileService(db, usuario, bitacora),
            new AnnouncementService(db, usuario, bitacora));
    }

    /// <summary>Un equipo raíz con su subequipo, y en cada uno un «Fullstack».</summary>
    private static (PersonasQueryService svc, AppDbContext db) Escenario()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Desarrollo Web" });
        db.Teams.Add(new Team { Id = 2, Name = "Soporte", EquipoPadreId = 1 });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.Fullstack });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 2, TeamRole = TeamRole.Fullstack });
        db.SaveChanges();

        return (Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin)), db);
    }

    private static PersonaDelOrganigramaDto EnElOrganigrama(OrganigramaDto org, string nombre) =>
        org.Equipos.SelectMany(e => e.Integrantes).Single(p => p.Nombre == nombre);

    // ── Lo que se guarda y lo que se lee ─────────────────────────────────────────

    [Fact]
    public async Task SEGUARDA_Y_SALE_bajo_la_gente_de_ese_equipo()
    {
        var (svc, _) = Escenario();

        var (ok, _) = await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas en tiempo y forma."));
        Assert.True(ok);

        var ana = EnElOrganigrama(await svc.OrganigramaAsync(), "Ana");
        Assert.Equal("Desarrolla características nuevas en tiempo y forma.", ana.FuncionDelRol);
        Assert.Null(ana.Funcion);   // la suya sigue vacía: no se copió nada
    }

    /// <summary>
    /// <b>La misma frase NO vale para los dos equipos.</b> Es lo que se pidió y lo que distingue esto
    /// de un catálogo único: un «Fullstack» de soporte no hace lo mismo que uno de desarrollo.
    /// </summary>
    [Fact]
    public async Task CADA_EQUIPO_DESCRIBE_EL_SUYO()
    {
        var (svc, _) = Escenario();

        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            2, TeamRole.Fullstack, "Resuelve los bugs que reporta operación."));

        var org = await svc.OrganigramaAsync();
        Assert.Equal("Desarrolla características nuevas.", EnElOrganigrama(org, "Ana").FuncionDelRol);
        Assert.Equal("Resuelve los bugs que reporta operación.", EnElOrganigrama(org, "Beto").FuncionDelRol);
    }

    /// <summary>
    /// <b>Lo escrito a mano manda.</b> La descripción del puesto es el valor por omisión, no una
    /// orden: quien tenga una función propia sigue con la suya, y se enseñan las dos por separado para
    /// que el editor sepa cuál está tocando.
    /// </summary>
    [Fact]
    public async Task LA_FUNCION_PROPIA_MANDA_sobre_la_del_puesto()
    {
        var (svc, db) = Escenario();
        db.Developers.Find(1)!.TeamFunction = "Lleva además la pasarela de pagos";
        db.SaveChanges();

        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));

        var ana = EnElOrganigrama(await svc.OrganigramaAsync(), "Ana");
        Assert.Equal("Lleva además la pasarela de pagos", ana.Funcion);
        Assert.Equal("Desarrolla características nuevas.", ana.FuncionDelRol);
    }

    /// <summary>
    /// <b>Y en el PAPEL, la propia también manda.</b> El contrato de documentos lleva un solo renglón
    /// de función, así que la regla se aplica al traducir. Se comprueba aparte porque es el sitio donde
    /// una regla resuelta dos veces empieza a decir cosas distintas.
    /// </summary>
    [Fact]
    public async Task EN_EL_PAPEL_sale_la_del_puesto_solo_si_no_hay_propia()
    {
        var (svc, db) = Escenario();
        db.Developers.Find(1)!.TeamFunction = "Lleva además la pasarela de pagos";
        db.SaveChanges();

        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            2, TeamRole.Fullstack, "Resuelve los bugs que reporta operación."));

        var papel = await svc.DatosDeEquiposAsync();
        var impresos = papel.Equipos.SelectMany(e => e.Integrantes).ToList();

        Assert.Equal("Lleva además la pasarela de pagos",
            impresos.Single(i => i.Nombre == "Ana").Funcion);
        Assert.Equal("Resuelve los bugs que reporta operación.",
            impresos.Single(i => i.Nombre == "Beto").Funcion);
    }

    /// <summary>
    /// <b>Nada se copia a ninguna ficha.</b> Es la comprobación que sostiene toda la decisión: si un
    /// día alguien «simplifica» copiando la frase al asignar el rol, esto se pone rojo.
    /// </summary>
    [Fact]
    public async Task NO_SE_COPIA_A_LA_FICHA_DE_NADIE()
    {
        var (svc, db) = Escenario();

        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));
        db.ChangeTracker.Clear();

        Assert.All(db.Developers.ToList(), d => Assert.Null(d.TeamFunction));
    }

    /// <summary>
    /// Y lo que de verdad protege esa decisión: <b>al mover a alguien de equipo hereda la descripción
    /// de SU NUEVO equipo</b>, sin que nadie vuelva a tocar nada. Copiando la frase a la ficha, aquí
    /// se quedaría la del equipo viejo — o nada, porque mover borra la función propia.
    /// </summary>
    [Fact]
    public async Task ALCAMBIAR_DE_EQUIPO_hereda_la_descripcion_del_nuevo()
    {
        var (svc, db) = Escenario();
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            2, TeamRole.Fullstack, "Resuelve los bugs que reporta operación."));

        // Ana se va de «Desarrollo Web» a «Soporte».
        db.Developers.Find(1)!.TeamId = 2;
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var ana = EnElOrganigrama(await svc.OrganigramaAsync(), "Ana");
        Assert.Equal("Resuelve los bugs que reporta operación.", ana.FuncionDelRol);
    }

    // ── El catálogo de la pantalla ───────────────────────────────────────────────

    /// <summary>
    /// Salen TODOS los roles, escritos o no: una lista que solo trae lo redactado no deja ver qué
    /// falta por redactar, que es para lo que se abre esa pantalla. Y «Líder» tiene que estar, que es
    /// el puesto que más se lee y el que <c>OrdenDeRoles</c> no incluye.
    /// </summary>
    [Fact]
    public async Task SALEN_TODOS_LOS_ROLES_aunque_no_esten_escritos()
    {
        var (svc, _) = Escenario();
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));

        var catalogo = await svc.DescripcionesDeRolAsync(1);

        Assert.Contains(catalogo, d => d.Rol == TeamRole.Lider);
        Assert.True(catalogo.Count > 5, "Tienen que salir todos los roles, no solo los descritos.");
        Assert.Equal("Desarrolla características nuevas.",
            catalogo.Single(d => d.Rol == TeamRole.Fullstack).Descripcion);
        Assert.Null(catalogo.Single(d => d.Rol == TeamRole.QA).Descripcion);
    }

    /// <summary>El rótulo del líder ya viene resuelto, y en un subequipo dice «Líder de subequipo».</summary>
    [Fact]
    public async Task EN_UN_SUBEQUIPO_el_rotulo_del_lider_ya_viene_resuelto()
    {
        var (svc, _) = Escenario();

        Assert.Equal("Líder",
            (await svc.DescripcionesDeRolAsync(1)).Single(d => d.Rol == TeamRole.Lider).RolTexto);
        Assert.Equal("Líder de subequipo",
            (await svc.DescripcionesDeRolAsync(2)).Single(d => d.Rol == TeamRole.Lider).RolTexto);
    }

    // ── Borrar ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vacío BORRA la fila, y no guarda una descripción en blanco. Dos formas de decir «no hay» acaban
    /// discrepando: una fila vacía se colaría como función heredada y dejaría a media plantilla con un
    /// renglón en blanco bajo el nombre.
    /// </summary>
    [Fact]
    public async Task VACIO_BORRA_y_no_deja_una_fila_en_blanco()
    {
        var (svc, db) = Escenario();
        await svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(
            1, TeamRole.Fullstack, "Desarrolla características nuevas."));

        var (ok, _) = await svc.GuardarDescripcionDeRolAsync(
            new GuardarDescripcionDeRolRequest(1, TeamRole.Fullstack, "   "));

        Assert.True(ok);
        db.ChangeTracker.Clear();
        Assert.Empty(db.DescripcionesDeRolDeEquipo.ToList());
        Assert.Null(EnElOrganigrama(await svc.OrganigramaAsync(), "Ana").FuncionDelRol);
    }

    // ── La guarda ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Describir un puesto es decir qué hace la gente de un equipo: es del líder. La guarda va pegada
    /// al dato y no solo a la ruta, porque el cliente corre en la máquina de cada persona.
    /// </summary>
    [Fact]
    public async Task SIN_SER_LIDER_ni_se_ve_ni_se_escribe()
    {
        var (_, db) = Escenario();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.DescripcionesDeRolAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            svc.GuardarDescripcionDeRolAsync(new GuardarDescripcionDeRolRequest(1, TeamRole.QA, "Algo")));
    }
}
