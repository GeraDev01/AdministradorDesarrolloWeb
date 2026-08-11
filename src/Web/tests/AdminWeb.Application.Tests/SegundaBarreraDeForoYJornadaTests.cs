using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La SEGUNDA barrera del foro y de la jornada: la que vive dentro del servicio.
///
/// <para><b>Por qué existe este archivo si ya hay pruebas de que Operaciones recibe 403.</b> Las hay,
/// y son de endpoint: levantan la API y comprueban que las cinco rutas del foro y las de la jornada
/// contestan 403. Eso prueba la PRIMERA barrera —la política del grupo— y no dice nada de la
/// segunda. Aquí no hay API: se llama al servicio a pelo, que es exactamente lo que haría un
/// endpoint nuevo escrito dentro de un año por alguien que no sabe que estos métodos eran del
/// equipo. Si la guarda se cayera del servicio, las pruebas de endpoint seguirían en verde y este
/// archivo sería lo único que se enteraría.</para>
///
/// <para>La otra mitad importa igual: <b>quien hoy pasa tiene que seguir pasando</b>. Una guarda que
/// cierra de más no se nota en una prueba de rechazo —todas siguen en verde— y deja al líder o al
/// desarrollador fuera de su propia pantalla. Por eso cada bloque de negativas tiene su bloque
/// gemelo de afirmativas.</para>
/// </summary>
public class SegundaBarreraDeForoYJornadaTests
{
    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private static ICurrentUser Quien(UserRole rol, int userId, int? devId = null) =>
        new UsuarioDePrueba
        {
            UserId = userId,
            Username = rol.ToString().ToLowerInvariant(),
            FullName = rol.ToString(),
            Role = rol,
            DeveloperId = devId
        };

    /// <summary>El operativo: la cuenta que estas guardas existen para dejar fuera.</summary>
    private static ICurrentUser Operativo() => Quien(UserRole.Operaciones, 3);

    /// <summary>El líder. Sin ficha de desarrollador a propósito: es el caso real.</summary>
    private static ICurrentUser Lider() => Quien(UserRole.Admin, 9);

    private static ICurrentUser Desarrolladora(int userId = 1, int devId = 7) =>
        Quien(UserRole.Desarrollador, userId, devId);

    private static ForumService Foro(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static ForoQueryService LecturaDelForo(AppDbContext db, ICurrentUser cu) => new(db, cu);

    private static AttendanceService Asistencia(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());

    private static JornadaQueryService Jornada(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, Asistencia(db, cu), new PresenceService(db, cu, new OrigenDePrueba()));

    private static PersonasQueryService Personas(AppDbContext db, ICurrentUser cu)
    {
        var bitacora = new AuditService(db, cu, new OrigenDePrueba());
        return new PersonasQueryService(
            db, cu, bitacora,
            new AuthService(db, cu, bitacora),
            new PresenceService(db, cu, new OrigenDePrueba()),
            Asistencia(db, cu),
            new DeveloperProfileService(db, cu, bitacora),
            new AnnouncementService(db, cu, bitacora));
    }

    /// <summary>Una publicación con un comentario, para tener sobre qué llamar a los métodos.</summary>
    private static async Task<(int raiz, int comentario)> UnHiloAsync(AppDbContext db)
    {
        var escritora = Foro(db, Desarrolladora());
        var (_, _, post) = await escritora.PublicarAsync(
            "Un tema", "Cuerpo de prueba suficientemente largo.", ForumTopic.Idea);
        var (_, _, com) = await escritora.ComentarAsync(post!.Id, "Un comentario.");
        return (post.Id, com!.Id);
    }

    // ── El foro le dice que no a Operaciones ─────────────────────────────────────

    /// <summary>
    /// Las once puertas de <see cref="ForumService"/>. Van todas en una prueba y no en once porque lo
    /// que se afirma es una sola cosa —«por aquí no se entra»— y partirla en once haría que añadir un
    /// método nuevo pareciera cubierto por tener diez en verde.
    /// </summary>
    [Fact]
    public async Task Operaciones_NoEntraAlForoNiParaLeerNiParaEscribir()
    {
        using var db = TestDb.New();
        var (raiz, _) = await UnHiloAsync(db);

        var ops = Foro(db, Operativo());

        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.HiloAsync(raiz));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ObtenerAsync(raiz));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ops.PublicarAsync("Título", "Cuerpo de prueba suficiente.", ForumTopic.Idea));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ComentarAsync(raiz, "Hola"));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.EditarAsync(raiz, "Otro", "Otro cuerpo."));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.RetirarAsync(raiz));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MeGustaAsync(raiz));

        // Las tres de las imágenes cuentan aparte: son el camino por el que salen los BYTES de las
        // capturas, y el endpoint que las sirve vive en otro grupo (/api/adjuntos). Si la guarda
        // faltara justo aquí, el muro contestaría 403 y las capturas se seguirían bajando por número.
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ImagenesDeAsync([raiz]));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ConteoImagenesAsync([raiz]));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.BytesDeImagenAsync(1));
    }

    [Fact]
    public async Task Operaciones_TampocoLeeElForoPorLaConsultaPaginada()
    {
        using var db = TestDb.New();
        var (raiz, _) = await UnHiloAsync(db);

        var ops = LecturaDelForo(db, Operativo());

        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.HiloAsync(raiz));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.OpcionesDelMuroAsync());
    }

    /// <summary>
    /// Y no se le niega por «no ser líder», que dejaría entrar a cualquier rol que se invente mañana:
    /// se le niega porque la comprobación es POSITIVA. El mensaje lo dice, y por eso se afirma —es la
    /// diferencia entre una guarda escrita bien y una escrita por descarte que hoy parece igual.
    /// </summary>
    [Fact]
    public async Task LaNegativaDelForo_DiceDeQueModuloSeTrata()
    {
        using var db = TestDb.New();

        var error = await Assert.ThrowsAsync<AuthorizationException>(
            () => Foro(db, Operativo()).MuroAsync());

        Assert.Contains("del foro del equipo", error.Message);
        Assert.Contains("Desarrollador", error.Message);
    }

    // ── …y el foro le sigue diciendo que sí al equipo ────────────────────────────

    [Fact]
    public async Task ElDesarrollador_SiguePublicandoLeyendoYComentando()
    {
        using var db = TestDb.New();
        var ana = Foro(db, Desarrolladora());

        var (ok, _, post) = await ana.PublicarAsync(
            "Migrar el reporte", "Bajó de 40 s a 1.2 s con una vista indexada.", ForumTopic.Aprendizaje);
        Assert.True(ok);

        Assert.True((await ana.ComentarAsync(post!.Id, "Buen dato.")).ok);
        Assert.Single(await ana.MuroAsync());
        Assert.Equal(2, (await ana.HiloAsync(post.Id)).Count);
        Assert.True((await ana.MeGustaAsync(post.Id)).meGusta);
        Assert.Empty(await ana.ImagenesDeAsync([post.Id]));

        var lectura = LecturaDelForo(db, Desarrolladora());
        Assert.Equal(1, (await lectura.MuroAsync()).Total);
        Assert.NotNull(await lectura.HiloAsync(post.Id));
        Assert.NotEmpty((await lectura.OpcionesDelMuroAsync()).Temas);
    }

    [Fact]
    public async Task ElLider_SigueEntrandoAlForoAunqueNoTengaFichaDeDesarrollador()
    {
        using var db = TestDb.New();
        await UnHiloAsync(db);

        var jefa = Foro(db, Lider());

        Assert.Single(await jefa.MuroAsync());
        Assert.True((await jefa.PublicarAsync("Aviso", "Cuerpo de prueba suficiente.", ForumTopic.Anuncio)).ok);
        Assert.Equal(2, (await LecturaDelForo(db, Lider()).MuroAsync()).Total);
    }

    /// <summary>
    /// La guarda nueva NO puede haber ablandado lo que ya era del líder. Es el error fácil al tocar
    /// una tanda de guardas: se pasa el rodillo por todas y una que era más estricta se queda más
    /// floja, que es peor que no haber tocado nada.
    /// </summary>
    [Fact]
    public async Task LoQueEraSoloDelLider_SigueSiendoSoloDelLider()
    {
        using var db = TestDb.New();
        var (raiz, _) = await UnHiloAsync(db);

        var ana = Foro(db, Desarrolladora());
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.AuditoriaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.FijarAsync(raiz, true));
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.CerrarAsync(raiz, true));
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.EliminarPublicacionAsync(raiz));

        var lectura = LecturaDelForo(db, Desarrolladora());
        await Assert.ThrowsAsync<AuthorizationException>(() => lectura.AuditoriaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => lectura.OpcionesDeAuditoriaAsync());

        // Y el líder las sigue teniendo: sin esto, la prueba pasaría con una guarda que no dejara
        // entrar a nadie.
        Assert.Equal(2, (await LecturaDelForo(db, Lider()).AuditoriaAsync()).Total);
    }

    // ── La jornada le dice que no a Operaciones ──────────────────────────────────

    [Fact]
    public async Task Operaciones_NoMarcaJornadaNiLeeLaSuya()
    {
        using var db = TestDb.New();
        var ops = Asistencia(db, Operativo());

        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MiRegistroAbiertoAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MarcarEntradaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MarcarSalidaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MisRegistrosAsync(DateTime.Today, DateTime.Today));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.SolicitarCorreccionAsync(1, "me equivoqué"));
    }

    [Fact]
    public async Task Operaciones_NoAbreLaPantallaDeMiJornada()
    {
        using var db = TestDb.New();
        var ops = Jornada(db, Operativo());

        await Assert.ThrowsAsync<AuthorizationException>(() => ops.MiJornadaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.EstadoDeMarcajeAsync());

        // El cronómetro no tenía guarda ninguna: se apoyaba en que sin ficha de desarrollador no hay
        // nada que devolver. Eso no es un permiso, es una casualidad — y deja de valer en cuanto una
        // cuenta de Operaciones tenga ficha ligada, que es justo lo que se prueba aquí.
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Jornada(db, Quien(UserRole.Operaciones, 3, devId: 44)).CronometroAsync());
    }

    [Fact]
    public async Task LaNegativaDeLaJornada_DiceDeQueModuloSeTrata()
    {
        using var db = TestDb.New();

        var error = await Assert.ThrowsAsync<AuthorizationException>(
            () => Asistencia(db, Operativo()).MarcarEntradaAsync());

        Assert.Contains("de la jornada propia", error.Message);
    }

    // ── …y la jornada le sigue diciendo que sí al equipo ─────────────────────────

    [Fact]
    public async Task ElDesarrollador_SigueMarcandoYViendoSuJornada()
    {
        using var db = TestDb.New();
        var ana = Desarrolladora();

        Assert.True((await Asistencia(db, ana).MarcarEntradaAsync("empiezo")).ok);
        Assert.NotNull(await Asistencia(db, ana).MiRegistroAbiertoAsync());

        var mia = await Jornada(db, ana).MiJornadaAsync();
        Assert.NotNull(mia.EntradaUtc);
        Assert.False(mia.PuedeMarcarEntrada);

        Assert.True((await Jornada(db, ana).EstadoDeMarcajeAsync()).DentroDeJornada);
        Assert.Null(await Jornada(db, ana).CronometroAsync());   // no hay ninguno corriendo

        Assert.True((await Asistencia(db, ana).MarcarSalidaAsync()).ok);
        var registro = db.AttendanceRecords.Single();
        Assert.True((await Asistencia(db, ana).SolicitarCorreccionAsync(registro.Id, "salí antes")).ok);
    }

    /// <summary>
    /// El líder marca jornada igual que cualquiera y NO tiene ficha de desarrollador. Es el caso que
    /// una guarda mal escrita —«tiene que tener ficha»— rompería sin que nadie se diera cuenta hasta
    /// que el líder intentara marcar su entrada.
    /// </summary>
    [Fact]
    public async Task ElLider_SinFichaDeDesarrollador_SigueMarcandoSuJornada()
    {
        using var db = TestDb.New();
        var jefa = Lider();

        Assert.True((await Asistencia(db, jefa).MarcarEntradaAsync()).ok);

        var mia = await Jornada(db, jefa).MiJornadaAsync();
        Assert.NotNull(mia.EntradaUtc);
        Assert.Null(mia.Cronometro);        // sin ficha no hay cronómetro, y eso no es un error
    }

    /// <summary>
    /// El tablero del líder NO se toca. Los días viejos de una cuenta de Operaciones existen, no se
    /// borran, y el líder tiene que poder seguir mirándolos y corrigiéndolos aunque esa cuenta ya no
    /// pueda marcar.
    /// </summary>
    [Fact]
    public async Task ElTableroDelLider_SigueSiendoDelLiderYVeLosDiasViejosDeUnOperativo()
    {
        using var db = TestDb.New();
        db.Users.Add(new User
        {
            Id = 3, Username = "ops", FullName = "Operativo", Role = UserRole.Operaciones,
            IsActive = true, PasswordHash = "x"
        });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = 3, DisplayName = "Operativo",
            CheckInUtc = DateTime.Today.AddHours(9).ToUniversalTime(),
            CheckOutUtc = DateTime.Today.AddHours(18).ToUniversalTime(),
            CloseKind = AttendanceCloseKind.Manual
        });
        db.SaveChanges();

        var jefa = Asistencia(db, Lider());
        var filas = await jefa.AsistenciaDelDiaAsync(DateTime.Today);
        Assert.Equal("Operativo", Assert.Single(filas).Nombre);
        Assert.Equal(0, await jefa.CorreccionesPendientesAsync());

        // Y sigue sin ser del desarrollador.
        var ana = Asistencia(db, Desarrolladora());
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.AsistenciaDelDiaAsync(DateTime.Today));
        await Assert.ThrowsAsync<AuthorizationException>(() => ana.CorreccionesPendientesAsync());
        await Assert.ThrowsAsync<AuthorizationException>(
            () => ana.CorregirRegistroAsync(1, DateTime.Today.AddHours(9), null, "porque sí"));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => ana.CrearRegistroManualAsync(3, DateTime.Today.AddHours(9), null, "porque sí"));
    }

    // ── Sin sesión sigue sin haber nada ──────────────────────────────────────────

    /// <summary>
    /// La guarda nueva empieza por comprobar la sesión, así que el anónimo tiene que seguir chocando
    /// igual. Se comprueba porque el cambio pudo haberse escrito de forma que un anónimo —sin rol—
    /// cayera en una rama distinta.
    /// </summary>
    [Fact]
    public async Task SinSesion_NiForoNiJornada()
    {
        using var db = TestDb.New();
        var nadie = UsuarioDePrueba.Anonimo();

        await Assert.ThrowsAsync<AuthorizationException>(() => Foro(db, nadie).MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => LecturaDelForo(db, nadie).MuroAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Asistencia(db, nadie).MarcarEntradaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Jornada(db, nadie).MiJornadaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => Jornada(db, nadie).CronometroAsync());
    }

    // ── Las etiquetas se quedan con la palabra ───────────────────────────────────

    /// <summary>
    /// Falla si la cadena lleva algo que tenga que dibujar el sistema operativo. Se persiguen las dos
    /// familias: los pares suplentes (todo emoji fuera del plano básico, 🗑 🙋) y los símbolos sueltos
    /// del plano básico (⚠ ✏), que tienen el mismo defecto aunque quepan en un solo <c>char</c>.
    ///
    /// <para>El punto medio «·» y la raya «—» NO son símbolos para Unicode —son puntuación—, así que
    /// pasan: separan, no decoran, y se ven igual en cualquier equipo.</para>
    /// </summary>
    private static void SinDibujos(string texto, string donde)
    {
        foreach (var c in texto)
            Assert.False(char.IsSurrogate(c) || char.IsSymbol(c),
                $"{donde}: «{texto}» todavía lleva «{c}» (U+{(int)c:X4}). Lo dibuja el sistema " +
                "operativo, no hereda el color del texto y sin fuente de emoji sale como un cuadro.");
    }

    [Fact]
    public void ElCierreDeUnaJornada_DiceLaPalabraSola()
    {
        Assert.Equal("Marcada", AttendanceService.EtiquetaCierre(AttendanceCloseKind.Manual));
        Assert.Equal("Olvido (estimada)", AttendanceService.EtiquetaCierre(AttendanceCloseKind.Olvido));
        Assert.Equal("Corregida por el líder", AttendanceService.EtiquetaCierre(AttendanceCloseKind.Admin));

        foreach (var cierre in Enum.GetValues<AttendanceCloseKind>())
            SinDibujos(AttendanceService.EtiquetaCierre(cierre), "EtiquetaCierre");
        SinDibujos(AttendanceService.EtiquetaCierre(null), "EtiquetaCierre(null)");
    }

    /// <summary>
    /// La columna «Estado» de la rejilla de auditoría del foro. Se comprueba sobre el DTO de verdad y
    /// no sobre una constante: lo que llega a la pantalla es esto.
    /// </summary>
    [Fact]
    public async Task LaAuditoriaDelForo_DiceRetiradaYEditadaSinDibujos()
    {
        using var db = TestDb.New();
        var ana = Desarrolladora();
        var (raiz, comentario) = await UnHiloAsync(db);

        await Foro(db, ana).RetirarAsync(comentario);
        await Foro(db, ana).EditarAsync(raiz, "Un tema", "Cuerpo corregido y suficientemente largo.");

        var filas = (await LecturaDelForo(db, Lider()).AuditoriaAsync()).Filas;

        Assert.Equal("Retirada", filas.Single(f => f.Id == comentario).Estado);
        Assert.Equal("Editada", filas.Single(f => f.Id == raiz).Estado);
        foreach (var f in filas) SinDibujos(f.Estado, "estado de la auditoría del foro");

        // Lo que la pantalla necesita para colorear no era el dibujo: son estos booleanos, que siguen
        // viajando. Por eso quitar la marca no pierde información.
        Assert.True(filas.Single(f => f.Id == comentario).Retirada);
        Assert.True(filas.Single(f => f.Id == raiz).Editada);
    }

    /// <summary>
    /// La columna «Estado» del tablero de asistencia, que es la que concatena dos etiquetas de dos
    /// servicios distintos: si una de las dos se hubiera quedado con su marca, aquí saldría a medias.
    /// </summary>
    [Fact]
    public async Task ElEstadoDeAsistencia_DiceLaPalabraSolaAunqueSeConcatene()
    {
        using var db = TestDb.New();

        db.Users.AddRange(
            new User { Id = 1, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, IsActive = true, PasswordHash = "x" },
            new User { Id = 2, Username = "beto", FullName = "Beto", Role = UserRole.Desarrollador, IsActive = true, PasswordHash = "x" });

        // Ana olvidó marcar su salida y además pide corrección: es la fila que junta las dos mitades.
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = 1, DisplayName = "Ana",
            CheckInUtc = DateTime.Today.AddHours(9).ToUniversalTime(),
            CheckOutUtc = DateTime.Today.AddHours(17).ToUniversalTime(),
            CloseKind = AttendanceCloseKind.Olvido,
            CorrectionRequestNote = "salí a las 18:00",
            CorrectionRequestedAtUtc = DateTime.UtcNow
        });

        // Beto tuvo la aplicación abierta y no marcó nada.
        db.WorkPresences.Add(new WorkPresence
        {
            UserId = 2, DisplayName = "Beto",
            StartedAtUtc = DateTime.Today.AddHours(9).ToUniversalTime(),
            EndedAtUtc = DateTime.Today.AddHours(18).ToUniversalTime(),
            LastSeenUtc = DateTime.Today.AddHours(18).ToUniversalTime(),
            EndReason = PresenceEnd.CierreNormal
        });
        db.SaveChanges();

        var resumen = await Personas(db, Lider()).AsistenciaDelDiaAsync(DateTime.Today);

        Assert.Equal("Pide corrección · Olvido (estimada)",
            resumen.Filas.Single(f => f.Nombre == "Ana").EstadoTexto);
        Assert.Equal("Sin marcar", resumen.Filas.Single(f => f.Nombre == "Beto").EstadoTexto);
        foreach (var f in resumen.Filas) SinDibujos(f.EstadoTexto, $"estado de «{f.Nombre}»");
    }

    /// <summary>
    /// La fila de «Mi jornada» que sale solo de la telemetría. Cae en la MISMA columna que las
    /// etiquetas de cierre, así que se comprueba con ellas: media columna con marca y media sin ella
    /// se leería como si significaran cosas distintas.
    /// </summary>
    [Fact]
    public async Task ElDiaSinMarcarDeMiJornada_DiceLaPalabraSola()
    {
        using var db = TestDb.New();
        var ana = Desarrolladora();

        db.WorkPresences.Add(new WorkPresence
        {
            UserId = 1, DeveloperId = 7, DisplayName = "Ana",
            StartedAtUtc = DateTime.Today.AddDays(-1).AddHours(9).ToUniversalTime(),
            EndedAtUtc = DateTime.Today.AddDays(-1).AddHours(18).ToUniversalTime(),
            LastSeenUtc = DateTime.Today.AddDays(-1).AddHours(18).ToUniversalTime(),
            EndReason = PresenceEnd.CierreNormal
        });
        db.SaveChanges();

        var dia = Assert.Single((await Jornada(db, ana).MiJornadaAsync()).Historial);

        Assert.Null(dia.Id);                                        // no hay registro que corregir
        Assert.Equal("Sin marcar (solo telemetría)", dia.Cierre);
        SinDibujos(dia.Cierre, "cierre de un día solo con telemetría");
    }

    /// <summary>
    /// Ninguna de estas etiquetas se GUARDA: se arman al responder y mueren en la pantalla. Importa
    /// porque si alguna se escribiera en la bitácora, cambiarla partiría el histórico en dos —los
    /// asientos viejos con dibujo y los nuevos sin él— y una búsqueda dejaría de encontrar la mitad.
    /// Aquí se ejercita el camino completo de una jornada y se comprueba que la bitácora no las tiene.
    /// </summary>
    [Fact]
    public async Task LasEtiquetasNoAcabanEnLaBitacora()
    {
        using var db = TestDb.New();
        var ana = Desarrolladora();

        await Asistencia(db, ana).MarcarEntradaAsync("empiezo");
        await Asistencia(db, ana).MarcarSalidaAsync("me voy");
        await Asistencia(db, ana).SolicitarCorreccionAsync(db.AttendanceRecords.Single().Id, "salí antes");

        var (raiz, comentario) = await UnHiloAsync(db);
        await Foro(db, ana).RetirarAsync(comentario);
        await Foro(db, ana).EditarAsync(raiz, "Un tema", "Cuerpo corregido y suficientemente largo.");

        var asientos = db.AuditLogs.Select(a => a.Details ?? "").ToList();

        Assert.NotEmpty(asientos);
        foreach (var detalle in asientos) SinDibujos(detalle, "asiento de la bitácora");
    }
}
