using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El organigrama: lo que el diagrama de la pantalla y el PDF dibujan.
///
/// <b>Lo que se prueba aquí es que NADIE FALTE y que nadie salga en dos sitios.</b> En una lista,
/// perder una fila es perder una fila; en un organigrama, es una persona que oficialmente no está en
/// ninguna parte y un líder que aparece sin equipo. Ese es el fallo que este archivo persigue.
///
/// La función por persona se prueba junto al organigrama y no aparte porque es el dato que lo hace
/// distinto de la lista de siempre: sin ella el diagrama enseña puestos, con ella enseña
/// responsabilidades.
/// </summary>
public class OrganigramaDeEquiposTests
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

    /// <summary>
    /// Un equipo con líder, un backend con función escrita, alguien sin rol, un equipo vacío y una
    /// persona sin equipo. Es el mínimo que hace aparecer todos los casos raros del dibujo a la vez.
    /// </summary>
    private static (PersonasQueryService svc, AppDbContext db) Escenario()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa", Description = "Portales", ColorHex = "#2563EB" });
        db.Teams.Add(new Team { Id = 2, Name = "Beta" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.Lider, Seniority = "Senior" });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 1, TeamRole = TeamRole.Backend, TeamFunction = "Mantiene la pasarela de pagos" });
        db.Developers.Add(new Developer { Id = 3, FullName = "Caro", IsActive = true, TeamId = 1, TeamRole = TeamRole.SinRol });
        db.Developers.Add(new Developer { Id = 4, FullName = "Dani", IsActive = true });
        db.Developers.Add(new Developer { Id = 5, FullName = "Elsa", IsActive = false, TeamId = 1, TeamRole = TeamRole.QA });
        db.SaveChanges();

        // El cargo se apunta DESPUÉS de guardar: el equipo apunta a la ficha y la ficha al equipo, así
        // que insertarlos a la vez deja a EF sin saber cuál va primero.
        db.Teams.Find(1)!.LeadDeveloperId = 1;
        db.SaveChanges();

        return (Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin)), db);
    }

    // ── Nadie falta ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Todas las personas activas salen dibujadas, y cada una una sola vez. La cuenta de arriba del
    /// diagrama tiene que cuadrar con lo que hay debajo: un nodo que dice «12 personas» sobre once
    /// cajas convierte el dibujo en algo que nadie se cree.
    /// </summary>
    [Fact]
    public async Task Todos_apareceUnaSolaVez_yElTotalCuadra()
    {
        var (svc, _) = Escenario();

        var org = await svc.OrganigramaAsync();

        var dibujados = org.Equipos.SelectMany(e => e.Integrantes).Concat(org.SinEquipo).ToList();
        Assert.Equal(4, dibujados.Count);                       // los cuatro activos
        Assert.Equal(4, dibujados.Select(p => p.Id).Distinct().Count());
        Assert.Equal(dibujados.Count, org.TotalPersonas);
        Assert.DoesNotContain(dibujados, p => p.Nombre == "Elsa");   // la baja no se dibuja
    }

    /// <summary>Quien no tiene equipo aparece, y aparte. Sin esa caja el organigrama miente por omisión.</summary>
    [Fact]
    public async Task QuienNoTieneEquipo_saleEnSuPropiaLista()
    {
        var (svc, _) = Escenario();

        var org = await svc.OrganigramaAsync();

        Assert.Equal("Dani", Assert.Single(org.SinEquipo).Nombre);
    }

    /// <summary>
    /// El líder va PRIMERO y marcado, aunque su nombre no sea el primero por orden alfabético dentro
    /// del equipo. En el diagrama, el orden es la jerarquía.
    /// </summary>
    [Fact]
    public async Task ElLider_vaPrimeroYMarcado()
    {
        var (svc, _) = Escenario();

        var alfa = (await svc.OrganigramaAsync()).Equipos.Single(e => e.Nombre == "Alfa");

        Assert.Equal("Ana", alfa.Integrantes[0].Nombre);
        Assert.True(alfa.Integrantes[0].EsLider);
        Assert.Equal("Ana", alfa.Lider);
        Assert.Single(alfa.Integrantes, p => p.EsLider);
    }

    /// <summary>
    /// Si nadie tiene el rol de líder marcado pero el equipo apunta a alguien, ése es el líder y se
    /// dibuja con el rol de líder. Los dos datos pueden discrepar y el dibujo no puede enseñar a
    /// alguien como «sin rol» y llamarlo líder dos centímetros más arriba.
    /// </summary>
    [Fact]
    public async Task SinRolDeLiderMarcado_mandaElLiderDelEquipo()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.QA });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 1, TeamRole = TeamRole.SinRol });
        db.SaveChanges();
        db.Teams.Find(1)!.LeadDeveloperId = 2;
        db.SaveChanges();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var alfa = (await svc.OrganigramaAsync()).Equipos.Single();

        Assert.Equal("Beto", alfa.Integrantes[0].Nombre);
        Assert.True(alfa.Integrantes[0].EsLider);
        Assert.Equal(TeamRole.Lider, alfa.Integrantes[0].Rol);
        Assert.Equal(2, alfa.Integrantes.Count);   // Ana sigue estando
    }

    /// <summary>
    /// Un equipo con DOS líderes marcados —que hoy no se puede provocar desde la pantalla, pero que
    /// pudo quedar escrito antes— no puede hacer desaparecer al segundo. El bucle que agrupa por
    /// roles no pasa por «Líder», así que sin el colador del final esa persona se esfumaría del
    /// diagrama y del PDF sin que nada fallara.
    /// </summary>
    [Fact]
    public async Task ConDosLideresMarcados_ningunoDesaparece()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.Lider });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 1, TeamRole = TeamRole.Lider });
        db.SaveChanges();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var alfa = (await svc.OrganigramaAsync()).Equipos.Single();

        Assert.Equal(2, alfa.Integrantes.Count);
        Assert.Single(alfa.Integrantes, p => p.EsLider);   // solo uno manda
    }

    /// <summary>Un equipo sin nadie y sin descripción sigue siendo una caja: existe, y eso es el dato.</summary>
    [Fact]
    public async Task UnEquipoVacio_sigueSaliendo()
    {
        var (svc, _) = Escenario();

        var beta = (await svc.OrganigramaAsync()).Equipos.Single(e => e.Nombre == "Beta");

        Assert.Empty(beta.Integrantes);
        Assert.Null(beta.Descripcion);
        Assert.Null(beta.ColorHex);
        Assert.Null(beta.Lider);
    }

    /// <summary>
    /// La descripción y el color en blanco llegan como null y no como cadena vacía. Es lo que deja
    /// que quien dibuja pregunte una sola cosa —«¿hay?»— en vez de tener que acordarse de recortar
    /// espacios en cada uno de los tres sitios que lo pintan.
    /// </summary>
    [Fact]
    public async Task LosBlancos_lleganComoNulos()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa", Description = "   ", ColorHex = "  " });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamFunction = "   " });
        db.SaveChanges();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var alfa = (await svc.OrganigramaAsync()).Equipos.Single();

        Assert.Null(alfa.Descripcion);
        Assert.Null(alfa.ColorHex);
        Assert.Null(alfa.Integrantes[0].Funcion);
    }

    // ── La función de cada persona ───────────────────────────────────────────────

    [Fact]
    public async Task LaFuncion_seGuardaYLlegaAlDiagrama()
    {
        var (svc, db) = Escenario();

        var (ok, _) = await svc.GuardarFuncionAsync(new GuardarFuncionRequest(3, "  Escribe las pruebas  "));
        Assert.True(ok);

        var caro = (await svc.OrganigramaAsync()).Equipos
            .SelectMany(e => e.Integrantes).Single(p => p.Nombre == "Caro");

        Assert.Equal("Escribe las pruebas", caro.Funcion);        // recortada al guardar
        Assert.Equal("Escribe las pruebas", db.Developers.Find(3)!.TeamFunction);
    }

    /// <summary>Mandarla vacía la borra: es la única forma de quitar una frase que ya no es verdad.</summary>
    [Fact]
    public async Task LaFuncionVacia_borraLaQueHabia()
    {
        var (svc, db) = Escenario();

        var (ok, mensaje) = await svc.GuardarFuncionAsync(new GuardarFuncionRequest(2, "   "));

        Assert.True(ok);
        Assert.Null(db.Developers.Find(2)!.TeamFunction);
        Assert.Contains("sin función", mensaje);
    }

    /// <summary>
    /// Más larga que lo que cabe en una tarjeta se rechaza con su motivo, y no se guarda a medias.
    /// El tope lo dice el servicio: si la pantalla se olvidara de su MaxLength, el texto no entraría
    /// igualmente.
    /// </summary>
    [Fact]
    public async Task UnaFuncionDemasiadoLarga_seRechaza()
    {
        var (svc, db) = Escenario();

        var (ok, mensaje) = await svc.GuardarFuncionAsync(
            new GuardarFuncionRequest(2, new string('x', PersonasQueryService.LargoMaximoDeFuncion + 1)));

        Assert.False(ok);
        Assert.Contains(PersonasQueryService.LargoMaximoDeFuncion.ToString(), mensaje);
        Assert.Equal("Mantiene la pasarela de pagos", db.Developers.Find(2)!.TeamFunction);
    }

    /// <summary>
    /// A quien no tiene equipo no se le puede anotar función: describe lo que hace DENTRO de uno, y
    /// la primera rotación la borraría sin avisar a quien la escribió.
    /// </summary>
    [Fact]
    public async Task SinEquipo_noSeLePuedeAnotarFuncion()
    {
        var (svc, db) = Escenario();

        var (ok, mensaje) = await svc.GuardarFuncionAsync(new GuardarFuncionRequest(4, "Algo"));

        Assert.False(ok);
        Assert.Contains("no está en ningún equipo", mensaje);
        Assert.Null(db.Developers.Find(4)!.TeamFunction);
    }

    /// <summary>
    /// Cambiar de equipo borra la función, igual que borra el rol y por lo mismo: «mantiene la
    /// pasarela de pagos» era una responsabilidad del equipo que se deja. Arrastrarla publicaría en
    /// el organigrama del equipo nuevo una responsabilidad que nadie le ha dado.
    /// </summary>
    [Fact]
    public async Task AlCambiarDeEquipo_laFuncionSeBorra_PERO_EL_ROL_VIAJA()
    {
        // Las dos mitades van juntas a propósito, porque la pareja es la que explica la regla: lo
        // que la persona SABE HACER se lo lleva, y lo que se le había ENCARGADO en ese equipo no.
        var (svc, db) = Escenario();

        var (ok, _) = await svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([2], 2, "reorganización"));

        Assert.True(ok);
        var beto = db.Developers.Find(2)!;
        Assert.Equal(2, beto.TeamId);
        Assert.Null(beto.TeamFunction);
        Assert.Equal(TeamRole.Backend, beto.TeamRole);
    }

    [Fact]
    public async Task AlCambiarDeEquipo_EL_LIDER_NO_LLEGA_DE_LIDER_AL_EQUIPO_NUEVO()
    {
        // «Líder» es uno de los valores de TeamRole, así que si el rol viajara sin excepción, mover
        // a alguien lo metería de líder en el equipo de destino — donde ya hay uno, o donde nadie ha
        // decidido todavía que lo sea. El cargo lo da quien recibe, no el gesto de moverla.
        var (svc, db) = Escenario();
        var ana = db.Developers.Find(1)!;
        ana.TeamRole = TeamRole.Lider;
        db.SaveChanges();

        var (ok, _) = await svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([1], 2, "cambio de área"));

        Assert.True(ok);
        Assert.Equal(TeamRole.SinRol, db.Developers.Find(1)!.TeamRole);
    }

    /// <summary>Y si el equipo desaparece, tampoco queda función: no hay equipo dentro del cual tenerla.</summary>
    [Fact]
    public async Task AlBorrarElEquipo_susIntegrantesSeQuedanSinFuncion()
    {
        var (svc, db) = Escenario();

        var (ok, _) = await svc.EliminarEquipoAsync(1);

        Assert.True(ok);
        Assert.All(db.Developers.Where(d => d.Id != 4 && d.Id != 5).ToList(), d =>
        {
            Assert.Null(d.TeamId);
            Assert.Null(d.TeamFunction);
        });
    }

    // ── El papel y la pantalla dicen lo mismo ────────────────────────────────────

    /// <summary>
    /// Los datos del PDF salen del MISMO sitio que el diagrama, así que no pueden discrepar. Es la
    /// razón por la que <c>DatosDeEquiposAsync</c> no vuelve a consultar la base: dos consultas
    /// resolviendo por su cuenta quién manda en un equipo es cómo se llega a repartir un papel que
    /// contradice a la pantalla desde la que se imprimió.
    /// </summary>
    [Fact]
    public async Task ElPdf_dibujaExactamenteLoMismoQueLaPantalla()
    {
        var (svc, _) = Escenario();

        var org = await svc.OrganigramaAsync();
        var papel = await svc.DatosDeEquiposAsync();

        Assert.Equal(org.TotalPersonas, papel.TotalPersonas);
        Assert.Equal(org.Equipos.Select(e => e.Nombre), papel.Equipos.Select(e => e.Nombre));
        Assert.Equal(org.SinEquipo.Select(p => p.Nombre), papel.SinEquipo.Select(p => p.Nombre));

        var alfa = papel.Equipos.Single(e => e.Nombre == "Alfa");
        Assert.True(alfa.Integrantes[0].EsLider);
        Assert.Equal("Senior", alfa.Integrantes[0].Nivel);
        Assert.Contains(alfa.Integrantes, i => i.Funcion == "Mantiene la pasarela de pagos");
    }

    // ── La guarda ────────────────────────────────────────────────────────────────

    /// <summary>
    /// El organigrama lleva nombres, niveles y responsabilidades de toda la plantilla: es del líder.
    /// La guarda va pegada al dato y no solo a la ruta, porque el cliente corre en la máquina de cada
    /// persona y estas direcciones se pueden llamar a mano.
    /// </summary>
    [Fact]
    public async Task SinSerLider_niSeVeNiSeEscribe()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1 });
        db.SaveChanges();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.OrganigramaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarFuncionAsync(new GuardarFuncionRequest(1, "Algo")));
    }
}
