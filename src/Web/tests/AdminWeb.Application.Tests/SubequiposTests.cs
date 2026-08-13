using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LOS SUBEQUIPOS vistos desde el servidor: quién puede colgar de quién, qué pasa al borrar un equipo
/// intermedio, y qué cambia —y qué NO cambia— para un equipo sin padre.
///
/// <para><b>La barrera de los círculos se prueba AQUÍ y no en la pantalla</b> por la misma razón por
/// la que está escrita aquí: a esta dirección se la puede llamar a mano, y el desplegable que no
/// ofrece los descendientes es una comodidad, no una regla. Un círculo no deja un organigrama raro:
/// deja una pantalla que no carga.</para>
///
/// <para>Y la mitad de este archivo comprueba lo contrario de una novedad: que un equipo SIN padre
/// —que es como están todos hoy— se siga comportando exactamente igual que antes. Es lo que hay que
/// poder afirmar antes de soltar esto contra una base con años de equipos dentro.</para>
/// </summary>
public class SubequiposTests
{
    private static PersonasQueryService Nuevo(AppDbContext db, ICurrentUser? usuario = null)
    {
        usuario ??= UsuarioDePrueba.Como(UserRole.Admin);
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
    /// Una cadena de equipos, cada uno colgando del anterior: 1 → 2 → 3 → … Es la forma con la que se
    /// prueban los círculos de tres y de cuatro, que son los que se cuelan; «A es padre de A» se ve a
    /// la primera y no es el que llega a producción.
    /// </summary>
    private static AppDbContext Cadena(int cuantos)
    {
        var db = TestDb.New();
        for (int i = 1; i <= cuantos; i++)
            db.Teams.Add(new Team { Id = i, Name = $"Equipo {i}", EquipoPadreId = i == 1 ? null : i - 1 });
        db.SaveChanges();
        return db;
    }

    private static GuardarEquipoRequest ColgarDe(AppDbContext db, int equipoId, int? padreId) =>
        new(equipoId, db.Teams.AsNoTracking().Single(t => t.Id == equipoId).Name, null, null, padreId);

    // ── Los círculos ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnEquipo_noPuedeColgarDeSiMismo()
    {
        using var db = Cadena(1);

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 1, 1));

        Assert.False(ok);
        Assert.Contains("sí mismo", mensaje);
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 1).EquipoPadreId);
    }

    [Fact]
    public async Task UnEquipo_noPuedeColgarDeSuPropioSubequipo()
    {
        using var db = Cadena(2);

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 1, 2));

        Assert.False(ok);
        Assert.Contains("círculo", mensaje);
    }

    [Fact]
    public async Task EnUnaCadenaDeTres_elDeArribaNoPuedeColgarDelDeAbajo()
    {
        // 1 → 2 → 3. Colgar el 1 del 3 cierra el anillo pasando por un equipo intermedio, que es el
        // caso que una comprobación de «no seas tu propio padre» dejaría pasar.
        using var db = Cadena(3);

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 1, 3));

        Assert.False(ok);
        // El motivo EXACTO, y no solo que se rechazara: la comprobación de después de guardar también
        // acaba diciendo «círculo», así que un rechazo sin más no distingue entre la barrera de antes
        // —la que no llega a escribir nada— y el deshacer de la carrera. Si la de antes se quedara
        // mirando solo al padre directo, esta prueba tiene que ponerse roja aquí.
        Assert.Contains("ya está debajo", mensaje);
        Assert.DoesNotContain("al mismo tiempo", mensaje);
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 1).EquipoPadreId);
    }

    [Fact]
    public async Task EnUnaCadenaDeCuatro_tampoco_niDesdeLaRaizNiDesdeElMedio()
    {
        // 1 → 2 → 3 → 4. Con dos eslabones de por medio ya no queda ninguna comprobación «a ojo»
        // que valga: o se recorre el subárbol entero o se cuela.
        using var db = Cadena(4);
        var svc = Nuevo(db);

        var (raiz, porLaRaiz) = await svc.GuardarEquipoAsync(ColgarDe(db, 1, 4));
        var (medio, porElMedio) = await svc.GuardarEquipoAsync(ColgarDe(db, 2, 4));

        Assert.False(raiz);
        Assert.False(medio);
        // Rechazados por la barrera de ANTES de escribir, no por el deshacer de la carrera: ver
        // EnUnaCadenaDeTres_elDeArribaNoPuedeColgarDelDeAbajo.
        Assert.Contains("ya está debajo", porLaRaiz);
        Assert.Contains("ya está debajo", porElMedio);
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 1).EquipoPadreId);
        Assert.Equal(1, db.Teams.AsNoTracking().Single(t => t.Id == 2).EquipoPadreId);
    }

    [Fact]
    public async Task ColgarDeOtraRama_siSePuede_yNoRompeNada()
    {
        // Lo que NO es un círculo tiene que seguir dejándose: mover una rama entera bajo otro equipo
        // es exactamente lo que se viene a hacer a esta pantalla.
        using var db = Cadena(4);

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 3, 1));

        Assert.True(ok);
        Assert.Contains("Equipo 1", mensaje);
        Assert.Equal(1, db.Teams.AsNoTracking().Single(t => t.Id == 3).EquipoPadreId);
        // El 4 se fue con él: colgaba del 3 y sigue colgando del 3.
        Assert.Equal(3, db.Teams.AsNoTracking().Single(t => t.Id == 4).EquipoPadreId);
    }

    [Fact]
    public async Task UnPadreQueYaNoExiste_seRechazaConSuMotivo()
    {
        using var db = Cadena(1);

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 1, 99));

        Assert.False(ok);
        Assert.Contains("padre ya no existe", mensaje);
    }

    [Fact]
    public async Task UnEquipoNuevo_puedeNacerYaColgandoDeOtro()
    {
        using var db = Cadena(1);

        var (ok, _) = await Nuevo(db).GuardarEquipoAsync(
            new GuardarEquipoRequest(0, "Front", null, null, 1));

        Assert.True(ok);
        Assert.Equal(1, db.Teams.AsNoTracking().Single(t => t.Name == "Front").EquipoPadreId);
    }

    // ── Dos guardados a la vez ───────────────────────────────────────────────────

    /// <summary>
    /// EL CASO DE LA CARRERA, que es el que la comprobación de antes de guardar no puede atrapar.
    ///
    /// <para>Dos personas guardan a la vez: una cuelga el 2 del 1 y la otra el 1 del 2. Cada cambio,
    /// mirado contra el árbol que había, es perfectamente válido; juntos cierran el círculo. Aquí se
    /// reproduce con un interceptor que escribe el cambio del OTRO justo después de que este guarde el
    /// suyo —que es exactamente la ventana donde ocurre— y se comprueba que quien confirma el último
    /// lo detecta y deshace lo suyo.</para>
    /// </summary>
    [Fact]
    public async Task DosGuardadosALaVez_noDejanUnCirculoEscrito()
    {
        var elOtro = new ElOtroGuardaEnMedio();
        var ruta = Path.Combine(Path.GetTempPath(), "adminweb_" + Guid.NewGuid().ToString("N") + ".db");
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={ruta}")
            .AddInterceptors(elOtro)
            .Options;

        using var db = new AppDbContext(opciones);
        db.Database.EnsureCreated();
        db.Teams.Add(new Team { Id = 1, Name = "Web" });
        db.Teams.Add(new Team { Id = 2, Name = "Front" });
        db.SaveChanges();

        // El otro no empieza a guardar hasta que el escenario está montado: armado antes, se
        // colaría en el guardado del propio montaje y la carrera sería otra.
        elOtro.Armar();

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 2, 1));

        Assert.False(ok);
        Assert.Contains("al mismo tiempo", mensaje);

        // Lo escrito por este quedó deshecho, así que no hay ningún equipo colgando de sí mismo.
        db.ChangeTracker.Clear();
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 2).EquipoPadreId);
    }

    /// <summary>
    /// Simula al otro guardado: en cuanto ESTE contexto termina de escribir, mete el cambio contrario
    /// —«el 1 cuelga del 2»— como lo habría dejado otra instancia de la API. Una sola vez, o el
    /// deshacer volvería a dispararlo y la carrera no acabaría nunca.
    /// </summary>
    private sealed class ElOtroGuardaEnMedio : SaveChangesInterceptor
    {
        private bool _armado;

        /// <summary>A partir de aquí, el siguiente guardado tendrá compañía.</summary>
        public void Armar() => _armado = true;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            if (_armado && eventData.Context is not null)
            {
                _armado = false;
                eventData.Context.Database.ExecuteSqlRaw(
                    @"UPDATE ""Teams"" SET ""EquipoPadreId"" = 2 WHERE ""Id"" = 1");
            }

            return base.SavedChangesAsync(eventData, result, ct);
        }
    }

    // ── Borrar un equipo con rama ────────────────────────────────────────────────

    [Fact]
    public async Task AlBorrarUnEquipoDelMedio_susSubequiposSubenAlAbuelo()
    {
        // 1 → 2 → 3. Se borra el 2: el 3 pasa a colgar del 1, no se va con él ni se queda apuntando
        // a un equipo que ya no existe.
        using var db = Cadena(3);

        var (ok, mensaje) = await Nuevo(db).EliminarEquipoAsync(2);

        Assert.True(ok);
        Assert.Contains("subieron un nivel", mensaje);
        Assert.Equal(1, db.Teams.AsNoTracking().Single(t => t.Id == 3).EquipoPadreId);
        Assert.Equal(2, db.Teams.AsNoTracking().Count());
    }

    [Fact]
    public async Task AlBorrarUnEquipoDeUnCirculoEscritoAMano_nadieQuedaColgandoDeSiMismo()
    {
        // El único sitio donde un círculo de la base se PROPAGA en vez de aguantarse. Con «1 cuelga
        // de 2» y «2 cuelga de 1» escritos a mano, borrar el 1 le da al 2 como abuelo… al propio 2:
        // el equipo acabaría colgando de sí mismo, y como un equipo así se dibuja como raíz, nadie se
        // enteraría de que la fila quedó corrupta. Los ciclos se aguantan al leer; al escribir, no se
        // reparten.
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Uno" });
        db.Teams.Add(new Team { Id = 2, Name = "Dos" });
        db.SaveChanges();
        db.Database.ExecuteSqlRaw(@"PRAGMA foreign_keys = OFF");
        db.Database.ExecuteSqlRaw(@"UPDATE ""Teams"" SET ""EquipoPadreId"" = 2 WHERE ""Id"" = 1");
        db.Database.ExecuteSqlRaw(@"UPDATE ""Teams"" SET ""EquipoPadreId"" = 1 WHERE ""Id"" = 2");
        db.ChangeTracker.Clear();

        var (ok, _) = await Nuevo(db).EliminarEquipoAsync(1);

        Assert.True(ok);
        db.ChangeTracker.Clear();
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 2).EquipoPadreId);
    }

    [Fact]
    public async Task AlBorrarUnEquipoRaiz_susSubequiposQuedanComoRaiz()
    {
        using var db = Cadena(3);

        var (ok, mensaje) = await Nuevo(db).EliminarEquipoAsync(1);

        Assert.True(ok);
        Assert.Contains("equipos raíz", mensaje);
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 2).EquipoPadreId);
        Assert.Equal(2, db.Teams.AsNoTracking().Single(t => t.Id == 3).EquipoPadreId);
    }

    // ── El organigrama ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ElOrganigrama_poneCadaPadreDelanteDeSuRama_conSuNivel()
    {
        using var db = TestDb.New();
        // Los nombres eligen a propósito un orden alfabético que NO coincide con el del árbol: si el
        // servidor devolviera la lista tal cual, «Zeta» saldría al final en vez de debajo de «Alfa».
        db.Teams.Add(new Team { Id = 1, Name = "Alfa" });
        db.Teams.Add(new Team { Id = 2, Name = "Zeta", EquipoPadreId = 1 });
        db.Teams.Add(new Team { Id = 3, Name = "Beta" });
        db.SaveChanges();

        var equipos = (await Nuevo(db).OrganigramaAsync()).Equipos;

        string[] enOrdenDeDibujo = ["Alfa", "Zeta", "Beta"];
        int[] susNiveles = [0, 1, 0];
        Assert.Equal(enOrdenDeDibujo, equipos.Select(e => e.Nombre));
        Assert.Equal(susNiveles, equipos.Select(e => e.Nivel));
        Assert.Equal(1, equipos.Single(e => e.Nombre == "Zeta").EquipoPadreId);
        Assert.Null(equipos.Single(e => e.Nombre == "Beta").EquipoPadreId);
    }

    [Fact]
    public async Task ElPdf_dibujaLaMismaRama_yConElNombreDelPadre()
    {
        // El papel no conoce identificadores: de quién cuelga cada equipo le llega por su nombre, y
        // en el mismo orden que a la pantalla. Dos ordenaciones distintas es como el PDF acaba
        // contradiciendo a la pantalla desde la que se imprimió.
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Web" });
        db.Teams.Add(new Team { Id = 2, Name = "Front", EquipoPadreId = 1 });
        db.SaveChanges();
        var svc = Nuevo(db);

        var papel = await svc.DatosDeEquiposAsync();
        var pantalla = await svc.OrganigramaAsync();

        Assert.Equal(pantalla.Equipos.Select(e => e.Nombre), papel.Equipos.Select(e => e.Nombre));
        Assert.Null(papel.Equipos[0].EquipoPadre);
        Assert.Equal("Web", papel.Equipos[1].EquipoPadre);
    }

    [Fact]
    public async Task UnPadreQueYaNoEsta_seDibujaComoRaizYNoDesaparece()
    {
        // Solo puede llegar escrito contra la base a mano, pero cuando llegue: el equipo sale, y sale
        // como raíz. Publicar un padre que no está en la lista dejaría a quien dibuja buscando una
        // caja que no existe.
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Huérfano" });
        db.SaveChanges();
        db.Database.ExecuteSqlRaw(@"PRAGMA foreign_keys = OFF");
        db.Database.ExecuteSqlRaw(@"UPDATE ""Teams"" SET ""EquipoPadreId"" = 77 WHERE ""Id"" = 1");
        db.ChangeTracker.Clear();

        var equipo = Assert.Single((await Nuevo(db).OrganigramaAsync()).Equipos);

        Assert.Equal("Huérfano", equipo.Nombre);
        Assert.Null(equipo.EquipoPadreId);
        Assert.Equal(0, equipo.Nivel);
    }

    // ── Un equipo sin padre se comporta EXACTAMENTE como antes ───────────────────

    [Fact]
    public async Task SinJerarquia_elOrganigramaSaleAlfabetico_yTodosSonRaiz()
    {
        using var db = TestDb.New();
        db.Teams.Add(new Team { Id = 1, Name = "Zeta" });
        db.Teams.Add(new Team { Id = 2, Name = "Alfa" });
        db.Teams.Add(new Team { Id = 3, Name = "Media" });
        db.SaveChanges();

        var equipos = (await Nuevo(db).OrganigramaAsync()).Equipos;

        string[] alfabetico = ["Alfa", "Media", "Zeta"];
        Assert.Equal(alfabetico, equipos.Select(e => e.Nombre));
        Assert.All(equipos, e =>
        {
            Assert.Null(e.EquipoPadreId);
            Assert.Equal(0, e.Nivel);
        });
    }

    [Fact]
    public async Task GuardarSinPadre_dejaElEquipoComoRaiz_yLoDiceIgualQueSiempre()
    {
        using var db = TestDb.New();

        var (ok, mensaje) = await Nuevo(db).GuardarEquipoAsync(
            new GuardarEquipoRequest(0, "Plataforma", "Los portales", "#2563EB", null));

        Assert.True(ok);
        Assert.Equal("Equipo «Plataforma» creado.", mensaje);   // sin coletilla de jerarquía
        Assert.Null(db.Teams.AsNoTracking().Single().EquipoPadreId);
    }

    [Fact]
    public async Task QuitarleElPadreAUnSubequipo_loDevuelveALaRaiz()
    {
        using var db = Cadena(2);

        var (ok, _) = await Nuevo(db).GuardarEquipoAsync(ColgarDe(db, 2, null));

        Assert.True(ok);
        Assert.Null(db.Teams.AsNoTracking().Single(t => t.Id == 2).EquipoPadreId);
    }
}
