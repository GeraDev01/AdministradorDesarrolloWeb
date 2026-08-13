using System.Reflection;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las pantallas de personas: la ficha confidencial, la organización de equipos y las cuentas.
///
/// La primera prueba del archivo es la que más importa: <b>el salario no llega a la bitácora</b>.
/// </summary>
public class PersonasQueryServiceTests
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

    private static (PersonasQueryService svc, AppDbContext db) ConUnDesarrollador(
        UserRole rol = UserRole.Admin)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true });
        db.SaveChanges();
        var usuario = UsuarioDePrueba.Como(rol);
        return (Nuevo(db, usuario), db);
    }

    // ── Lo confidencial ──────────────────────────────────────────────────────────

    /// <summary>
    /// Guardar la ficha desde la pantalla NO puede dejar el importe en la bitácora — ni en el
    /// mensaje, ni en los valores anteriores, ni en los nuevos. Queda constancia de que la ficha
    /// cambió, que es lo que hace falta para auditar; cuánto gana alguien, no.
    /// </summary>
    [Fact]
    public async Task ElSalarioNoLlegaALaBitacora()
    {
        var (svc, db) = ConUnDesarrollador();

        var (ok, _) = await svc.GuardarFichaAsync(
            new GuardarFichaRequest(1, "fuerte", null, null, 123456m, "MXN", null, null));
        Assert.True(ok);

        // Se guardó de verdad: sin esto la prueba pasaría aunque no se hubiera escrito nada.
        Assert.Equal(123456m, (await svc.FichaAsync(1))!.Salario);

        foreach (var entrada in db.AuditLogs)
        {
            var todo = $"{entrada.Details} {entrada.OldValues} {entrada.NewValues}";
            Assert.DoesNotContain("123456", todo);
            Assert.DoesNotContain("MXN", todo);
        }

        // Pero sí queda constancia de que la ficha se tocó.
        Assert.Contains(db.AuditLogs, a => a.EntityType == "DeveloperProfile");
    }

    /// <summary>
    /// El salario viaja en UN solo contrato de ida y en UNO de vuelta, y en ningún otro DTO del
    /// vertical. Es una prueba estructural a propósito: la forma realista de filtrarlo es que alguien
    /// añada «para que se vea de un vistazo» el importe a la lista, y eso se detecta aquí antes de
    /// que salga por una respuesta que se pide para otra cosa.
    /// </summary>
    [Fact]
    public void SoloLaFichaLlevaSalario()
    {
        var permitidos = new[] { nameof(FichaDeDesarrolloDto), nameof(GuardarFichaRequest) };

        var infractores = typeof(PersonaConFichaDto).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(PersonaConFichaDto).Namespace)
            .Where(t => !permitidos.Contains(t.Name))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.Name.Contains("salar", StringComparison.OrdinalIgnoreCase)
                       || p.Name.Contains("sueldo", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(infractores);
    }

    /// <summary>Ni la lista ni la ficha se le enseñan a quien no es líder.</summary>
    [Fact]
    public async Task LasFichas_soloLasVeElLider()
    {
        var (svc, _) = ConUnDesarrollador(UserRole.Desarrollador);

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.PersonasConFichaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.FichaAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarFichaAsync(new GuardarFichaRequest(1, null, null, null, 1m, null, null, null)));
    }

    /// <summary>
    /// Un salario en cero es «sin registrar», igual que el 0 del control del escritorio. Guardarlo
    /// como importe haría creer que a alguien se le paga cero.
    /// </summary>
    [Fact]
    public async Task SalarioEnCero_seGuardaComoSinRegistrar()
    {
        var (svc, _) = ConUnDesarrollador();

        await svc.GuardarFichaAsync(new GuardarFichaRequest(1, null, null, null, 0m, null, null, null));

        Assert.Null((await svc.FichaAsync(1))!.Salario);
    }

    /// <summary>Sin ficha todavía se devuelve el esqueleto vacío: la pantalla tiene que dejar capturarla.</summary>
    [Fact]
    public async Task SinFicha_devuelveElEsqueletoYNoNulo()
    {
        var (svc, _) = ConUnDesarrollador();

        var ficha = await svc.FichaAsync(1);

        Assert.NotNull(ficha);
        Assert.Equal("Ana", ficha!.Nombre);
        Assert.Null(ficha.Salario);
        Assert.Null(ficha.ActualizadaUtc);
    }

    /// <summary>La lista no trae ni el campo, así que no hay nada que esconder en el navegador.</summary>
    [Fact]
    public async Task LaListaDePerfiles_diceQuienTieneFicha()
    {
        var (svc, db) = ConUnDesarrollador();
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true });
        db.Developers.Add(new Developer { Id = 3, FullName = "Baja", IsActive = false });
        db.SaveChanges();

        await svc.GuardarFichaAsync(new GuardarFichaRequest(1, "x", null, null, 10m, null, null, null));

        var lista = await svc.PersonasConFichaAsync();

        Assert.Equal(2, lista.Count);                              // el inactivo no aparece
        Assert.True(lista.Single(p => p.DeveloperId == 1).TieneFicha);
        Assert.False(lista.Single(p => p.DeveloperId == 2).TieneFicha);
    }

    // ── Presencia ────────────────────────────────────────────────────────────────

    /// <summary>
    /// El tablero incluye a TODAS las cuentas activas, no solo a las conectadas: si alguien falta,
    /// esa ausencia es el dato. Y quien no está conectado sale como «Ausente», que lo pone el
    /// servidor y nadie elige a mano.
    /// </summary>
    [Fact]
    public async Task Tablero_incluyeAQuienNoEstaConectado()
    {
        var db = TestDb.New();
        db.Users.Add(new User { Id = 1, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        db.Users.Add(new User { Id = 2, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, IsActive = true, PasswordHash = "x" });
        db.WorkPresences.Add(new WorkPresence
        {
            UserId = 2, DisplayName = "Ana", StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            LastSeenUtc = DateTime.UtcNow, State = PresenceState.Comiendo, StateNote = "vuelvo 15:30"
        });
        db.SaveChanges();

        var tablero = await Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin)).TableroAsync();

        Assert.Equal(2, tablero.Total);
        Assert.Equal(1, tablero.Conectados);

        var ana = tablero.Personas.Single(p => p.UserId == 2);
        Assert.True(ana.Conectado);
        Assert.Equal(PresenceState.Comiendo, ana.Estado);
        Assert.Equal("vuelvo 15:30", ana.Nota);

        var jefe = tablero.Personas.Single(p => p.UserId == 1);
        Assert.False(jefe.Conectado);
        Assert.Equal("Desconectado", jefe.EstadoTexto);
        // Nunca ha entrado: el centinela DateTime.MinValue no se le enseña a la pantalla.
        Assert.Null(jefe.UltimoLatidoUtc);
    }

    // ── Equipos ──────────────────────────────────────────────────────────────────

    private static AppDbContext ConDosEquipos()
    {
        var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Alfa" });
        db.Teams.Add(new Team { Id = 2, Name = "Beta" });
        db.Developers.Add(new Developer { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.Lider });
        db.Developers.Add(new Developer { Id = 2, FullName = "Beto", IsActive = true, TeamId = 1, TeamRole = TeamRole.Backend });
        db.Developers.Add(new Developer { Id = 3, FullName = "Caro", IsActive = true });
        db.SaveChanges();

        var alfa = db.Teams.Find(1)!;
        alfa.LeadDeveloperId = 1;
        db.SaveChanges();
        return db;
    }

    /// <summary>
    /// Mover a alguien conserva las tres reglas de <c>MoveDeveloper</c>: suelta el liderazgo del
    /// equipo que deja, pierde el rol, y queda registrada la rotación con los nombres copiados.
    /// </summary>
    [Fact]
    public async Task Mover_sueltaElLiderazgo_quitaElRol_yRegistraLaRotacion()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, _) = await svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([1], 2, "reorganización"));
        Assert.True(ok);

        var ana = db.Developers.Find(1)!;
        Assert.Equal(2, ana.TeamId);
        Assert.Equal(TeamRole.SinRol, ana.TeamRole);
        Assert.Null(db.Teams.Find(1)!.LeadDeveloperId);

        var rotacion = Assert.Single(db.TeamRotations);
        Assert.Equal("Ana", rotacion.DeveloperName);
        Assert.Equal("Alfa", rotacion.FromTeamName);
        Assert.Equal("Beta", rotacion.ToTeamName);
        Assert.Equal("reorganización", rotacion.Note);
    }

    /// <summary>Quitar del equipo es mover a «sin equipo», y también deja rastro.</summary>
    [Fact]
    public async Task QuitarDelEquipo_dejaSinEquipoYRegistraLaRotacion()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        Assert.True((await svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([2], null, null))).ok);

        Assert.Null(db.Developers.Find(2)!.TeamId);
        Assert.Equal("Sin equipo", Assert.Single(db.TeamRotations).ToTeamName);
    }

    /// <summary>Mover a quien ya está donde se le manda no inventa una rotación falsa.</summary>
    [Fact]
    public async Task Mover_aDondeYaEsta_noRegistraNada()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, _) = await svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([2], 1, null));

        Assert.False(ok);
        Assert.Empty(db.TeamRotations);
    }

    /// <summary>
    /// «Líder» es excluyente y se escribe en los DOS sitios: el rol de la ficha y el
    /// <c>LeadDeveloperId</c> del equipo. Escribir solo uno es lo que los hace discrepar.
    /// </summary>
    [Fact]
    public async Task NombrarLider_desbancaAlAnterior_yActualizaElEquipo()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        Assert.True((await svc.AsignarRolAsync(new AsignarRolRequest(2, TeamRole.Lider))).ok);

        Assert.Equal(TeamRole.Lider, db.Developers.Find(2)!.TeamRole);
        Assert.Equal(TeamRole.SinRol, db.Developers.Find(1)!.TeamRole);
        Assert.Equal(2, db.Teams.Find(1)!.LeadDeveloperId);
    }

    /// <summary>Quien no tiene equipo no puede tener rol dentro de uno: se dice, no se ignora.</summary>
    [Fact]
    public async Task AsignarRol_aQuienNoTieneEquipo_seRechaza()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, mensaje) = await svc.AsignarRolAsync(new AsignarRolRequest(3, TeamRole.QA));

        Assert.False(ok);
        Assert.Contains("no está en ningún equipo", mensaje);
    }

    /// <summary>Borrar el equipo NO borra a su gente: quedan sin equipo, que es lo que avisa el diálogo.</summary>
    [Fact]
    public async Task EliminarEquipo_dejaSinEquipoASuGente()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        Assert.True((await svc.EliminarEquipoAsync(1)).ok);

        Assert.Null(db.Teams.Find(1));
        Assert.All(db.Developers.Where(d => d.Id != 3), d =>
        {
            Assert.Null(d.TeamId);
            Assert.Equal(TeamRole.SinRol, d.TeamRole);
        });
    }

    /// <summary>Dos equipos con el mismo nombre son indistinguibles en el organigrama y en el PDF.</summary>
    [Fact]
    public async Task Equipo_conNombreRepetido_seRechaza()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, mensaje) = await svc.GuardarEquipoAsync(new GuardarEquipoRequest(0, "Alfa", null, null, null));

        Assert.False(ok);
        Assert.Contains("Alfa", mensaje);
    }

    /// <summary>Los datos del PDF llevan a cada quien con su rol, y aparte a quien no tiene equipo.</summary>
    [Fact]
    public async Task DatosDelPdf_traenIntegrantesYSinEquipo()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var datos = await svc.DatosDeEquiposAsync();

        var alfa = datos.Equipos.Single(e => e.Nombre == "Alfa");
        Assert.Equal("Ana", alfa.Lider);
        Assert.Contains(alfa.Integrantes, i => i.Nombre == "Beto" && i.Rol == "Backend Dev");
        Assert.Contains(datos.SinEquipo, i => i.Nombre == "Caro");
    }

    // ── Usuarios ─────────────────────────────────────────────────────────────────

    private static AppDbContext ConUnAdmin()
    {
        var db = TestDb.New();
        db.Users.Add(new User
        {
            Id = 1, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin,
            IsActive = true, PasswordHash = "x"
        });
        db.Developers.Add(new Developer { Id = 7, FullName = "Ana", IsActive = true });
        db.SaveChanges();
        return db;
    }

    /// <summary>
    /// El alta genera la contraseña, no la recibe: se devuelve una vez y la cuenta queda obligada a
    /// cambiarla al entrar.
    /// </summary>
    [Fact]
    public async Task CrearUsuario_devuelveUnaTemporalYExigeCambiarla()
    {
        var db = ConUnAdmin();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, _, temporal) = await svc.CrearUsuarioAsync(
            new CrearUsuarioRequest("ana", "Ana", UserRole.Desarrollador, 7));

        Assert.True(ok);
        Assert.False(string.IsNullOrWhiteSpace(temporal));

        var creada = db.Users.Single(u => u.Username == "ana");
        Assert.True(creada.MustChangePassword);
        Assert.True(PasswordHasher.Verify(temporal!, creada.PasswordHash));
    }

    /// <summary>Un usuario repetido no se acepta: el índice es único y el mensaje lo explica antes.</summary>
    [Fact]
    public async Task CrearUsuario_repetido_seRechaza()
    {
        var db = ConUnAdmin();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, mensaje, _) = await svc.CrearUsuarioAsync(
            new CrearUsuarioRequest("jefe", "Otro", UserRole.Operaciones, null));

        Assert.False(ok);
        Assert.Contains("jefe", mensaje);
    }

    /// <summary>
    /// Dos cuentas sobre la misma ficha harían que los comunicados llegaran a una de las dos de forma
    /// arbitraria y la otra persona no se enterara nunca.
    /// </summary>
    [Fact]
    public async Task CrearUsuario_conFichaYaLigada_seRechaza()
    {
        var db = ConUnAdmin();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        Assert.True((await svc.CrearUsuarioAsync(
            new CrearUsuarioRequest("ana", "Ana", UserRole.Desarrollador, 7))).ok);

        var (ok, mensaje, _) = await svc.CrearUsuarioAsync(
            new CrearUsuarioRequest("ana2", "Ana bis", UserRole.Desarrollador, 7));

        Assert.False(ok);
        Assert.Contains("ya está ligada", mensaje);
    }

    /// <summary>
    /// Quedarse sin ninguna cuenta de líder activa deja la aplicación sin quien la administre, y el
    /// único arreglo sería tocar la base a mano.
    /// </summary>
    [Fact]
    public async Task NoSePuedeQuitarAlUltimoLider()
    {
        var db = ConUnAdmin();
        // La sesión es de OTRA cuenta: así lo que rechaza la operación es la regla del último líder
        // y no la de «no puedes desactivarte a ti mismo», que se prueba aparte.
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

        var (ok, mensaje) = await svc.ActualizarUsuarioAsync(
            new ActualizarUsuarioRequest(1, "jefe", "Jefe", UserRole.Operaciones, null, true));
        Assert.False(ok);
        Assert.Contains("única cuenta de líder", mensaje);

        Assert.False((await svc.AlternarActivoAsync(1)).ok);
        Assert.Equal(UserRole.Admin, db.Users.Find(1)!.Role);
    }

    /// <summary>
    /// Cambiar el rol renueva el sello de sesión. Es propio de la web: la cookie lleva el rol dentro
    /// y dura ocho horas, así que sin esto a quien pierde el rol de líder le seguirían abriendo las
    /// pantallas de líder toda la tarde.
    /// </summary>
    [Fact]
    public async Task CambiarElRol_cierraLaSesionAbierta()
    {
        var db = ConUnAdmin();
        db.Users.Add(new User
        {
            Id = 2, Username = "ana", FullName = "Ana", Role = UserRole.Admin,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();
        var selloAntes = db.Users.Find(2)!.SecurityStamp;

        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));
        Assert.True((await svc.ActualizarUsuarioAsync(
            new ActualizarUsuarioRequest(2, "ana", "Ana", UserRole.Desarrollador, null, true))).ok);

        Assert.NotEqual(selloAntes, db.Users.Find(2)!.SecurityStamp);
    }

    /// <summary>Desactivar también echa de la sesión abierta; reactivar no tiene por qué tocar nada.</summary>
    [Fact]
    public async Task Desactivar_cierraLaSesionAbierta()
    {
        var db = ConUnAdmin();
        db.Users.Add(new User
        {
            Id = 2, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();
        var selloAntes = db.Users.Find(2)!.SecurityStamp;

        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));
        Assert.True((await svc.AlternarActivoAsync(2)).ok);

        Assert.False(db.Users.Find(2)!.IsActive);
        Assert.NotEqual(selloAntes, db.Users.Find(2)!.SecurityStamp);
    }

    /// <summary>La cuenta de líder no se borra: hay que bajarle el rol antes, que obliga a pensarlo.</summary>
    [Fact]
    public async Task EliminarUsuario_lider_seRechaza()
    {
        var db = ConUnAdmin();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin));

        var (ok, mensaje) = await svc.EliminarUsuarioAsync(1);

        Assert.False(ok);
        Assert.Contains("no se puede eliminar", mensaje);
        Assert.NotNull(db.Users.Find(1));
    }

    /// <summary>Y la propia tampoco: sería quedarse fuera con la puerta cerrada por dentro.</summary>
    [Fact]
    public async Task EliminarUsuario_laPropia_seRechaza()
    {
        var db = ConUnAdmin();
        db.Users.Add(new User
        {
            Id = 2, Username = "ops", FullName = "Ops", Role = UserRole.Operaciones,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();

        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 2));

        var (ok, mensaje) = await svc.EliminarUsuarioAsync(2);

        Assert.False(ok);
        Assert.Contains("tu propia cuenta", mensaje);
    }

    /// <summary>
    /// Lo que NO viaja al navegador es tan importante como lo que viaja: ni el hash de la contraseña
    /// ni el sello de sesión tienen nada que hacer en una lista de cuentas.
    /// </summary>
    [Fact]
    public void ElDtoDeUsuario_noLlevaHashNiSello()
    {
        var prohibidos = new[] { "hash", "password", "contrasena", "stamp", "sello", "secret" };

        var filtrados = typeof(UsuarioDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => prohibidos.Any(x => p.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
            // «DebeCambiarContrasena» es un booleano, no un secreto: dice que la cuenta arrastra una
            // temporal sin cambiar, que es justo lo que el líder tiene que ver.
            .Where(p => p.PropertyType != typeof(bool))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(filtrados);
    }

    /// <summary>La pantalla de cuentas es del líder y el servicio lo comprueba por su cuenta.</summary>
    [Fact]
    public async Task LasCuentas_soloLasVeElLider()
    {
        var db = ConUnAdmin();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Operaciones));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.UsuariosAsync());
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.CrearUsuarioAsync(new CrearUsuarioRequest("x", "X", UserRole.Desarrollador, null)));
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.EliminarUsuarioAsync(1));
    }

    /// <summary>Los equipos también: la edición del organigrama no es de nadie más.</summary>
    [Fact]
    public async Task LosEquipos_soloLosEditaElLider()
    {
        var db = ConDosEquipos();
        var svc = Nuevo(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarEquipoAsync(new GuardarEquipoRequest(0, "Gamma", null, null, null)));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.MoverIntegrantesAsync(new MoverIntegrantesRequest([1], 2, null)));
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.DatosDeEquiposAsync());
    }
}
