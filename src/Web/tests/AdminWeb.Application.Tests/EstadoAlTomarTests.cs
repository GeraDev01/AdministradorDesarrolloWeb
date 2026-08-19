using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// ADÓNDE SE MUEVE EL WORK ITEM AL TOMAR SU ACTIVIDAD: a qué estado —que resultó depender del TIPO—
/// y a qué columna del tablero.
///
/// <para><b>De dónde sale esto.</b> La primera versión tenía un solo estado para todos los tipos y
/// falló en producción: el proyecto real usa «Approved» para tareas y requerimientos y «New» para
/// bugs, y no existe ningún nombre que valga para los dos. No era un nombre mal elegido — era la
/// forma del ajuste, que no puede ser única porque en DevOps los estados válidos son una propiedad
/// del TIPO de work item.</para>
///
/// <para>El resto de la clase prueba lo que hace falta para que la configuración se pueda escribir
/// sin manual: que el tipo gane sobre el valor general, que el general exista para no tener que
/// enumerarlos todos, que un tipo en blanco signifique «no lo muevas», y que sin nada configurado se
/// siga deduciendo — porque nadie configura lo que no sabe que existe.</para>
/// </summary>
public class EstadoAlTomarTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── El intérprete del ajuste, que es lo que se teclea ────────────────────────

    [Fact]
    public void ElAjusteDelProyectoReal_seEntiendeTalComoSeEscribe()
    {
        var (porTipo, general) = PoolDevOpsService.ParesDeEstadoPorTipo(
            "Bug=New; Task=Approved; Product Backlog Item=Approved");

        Assert.Null(general);
        Assert.Equal("New", porTipo["Bug"]);
        Assert.Equal("Approved", porTipo["Task"]);
        // Los espacios de DENTRO del nombre se conservan: «Product Backlog Item» lleva dos.
        Assert.Equal("Approved", porTipo["Product Backlog Item"]);
    }

    [Fact]
    public void ElTipo_seCompararSinDistinguirMayusculas()
    {
        var (porTipo, _) = PoolDevOpsService.ParesDeEstadoPorTipo("bug=New");
        Assert.True(porTipo.ContainsKey("BUG"));
    }

    [Theory]
    [InlineData("Bug=New, Task=Approved")]
    [InlineData("Bug=New; Task=Approved")]
    [InlineData("  Bug = New ;  Task = Approved  ")]
    public void SeAdmitenLasDosFormasDeSepararYLosEspaciosSobrantes(string ajuste)
    {
        var (porTipo, _) = PoolDevOpsService.ParesDeEstadoPorTipo(ajuste);
        Assert.Equal("New", porTipo["Bug"]);
        Assert.Equal("Approved", porTipo["Task"]);
    }

    /// <summary>Un valor suelto es el que vale para los tipos que nadie nombró.</summary>
    [Fact]
    public void UnValorSuelto_esElGeneral()
    {
        var (porTipo, general) = PoolDevOpsService.ParesDeEstadoPorTipo("Active; Bug=New");

        Assert.Equal("Active", general);
        Assert.Equal("New", porTipo["Bug"]);
    }

    [Fact]
    public void SinAjuste_noHayNiPares_niGeneral()
    {
        var (porTipo, general) = PoolDevOpsService.ParesDeEstadoPorTipo("   ");
        Assert.Empty(porTipo);
        Assert.Null(general);
    }

    // ── La resolución, contra la base ────────────────────────────────────────────

    private PoolDevOpsService Puente(AppDbContext db)
    {
        var cu = UsuarioDePrueba.Como(UserRole.Admin, userId: 9);
        var bitacora = new AuditService(db, cu, new OrigenDePrueba());
        return new PoolDevOpsService(db, cu, new SettingsService(db, cu, bitacora),
            new UserSecretsService(db, cu, new ProtectorDePrueba(), bitacora), bitacora,
            new ClienteQueNoSeUsa());
    }

    private AppDbContext BaseCon(params (string clave, string valor)[] ajustes)
    {
        var db = TestDb.New();
        _contextos.Add(db);
        foreach (var (clave, valor) in ajustes)
            db.AppSettings.Add(new AppSetting { Key = clave, Value = valor });
        db.SaveChanges();
        return db;
    }

    /// <summary>El caso que motivó todo esto: dos tipos, dos estados, y ninguno vale para el otro.</summary>
    [Theory]
    [InlineData("Bug", "New")]
    [InlineData("Task", "Approved")]
    [InlineData("Product Backlog Item", "Approved")]
    public async Task ElTipoDecideElEstado(string tipo, string esperado)
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoAlTomar,
                                "Bug=New; Task=Approved; Product Backlog Item=Approved"));

        Assert.Equal(esperado, await Puente(db).EstadoAlTomarAsync(tipo));
    }

    /// <summary>Un tipo que nadie nombró cae en el valor suelto, que es lo que evita tener que
    /// enumerar los siete tipos de una plantilla para cubrir uno raro.</summary>
    [Fact]
    public async Task UnTipoNoNombrado_caeEnElGeneral()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoAlTomar, "Active; Bug=New"));

        Assert.Equal("New", await Puente(db).EstadoAlTomarAsync("Bug"));
        Assert.Equal("Active", await Puente(db).EstadoAlTomarAsync("Feature"));
    }

    /// <summary>
    /// Un tipo con el estado EN BLANCO significa «no lo muevas», y hace falta: cuando el estado
    /// inicial ya es el bueno no hay nada que cambiar, y sin esta salida la única alternativa sería
    /// escribirle el estado que ya tiene.
    /// </summary>
    [Fact]
    public async Task UnTipoEnBlanco_significaNoMoverlo()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoAlTomar, "Approved; Bug="));

        Assert.Null(await Puente(db).EstadoAlTomarAsync("Bug"));
        Assert.Equal("Approved", await Puente(db).EstadoAlTomarAsync("Task"));
    }

    /// <summary>Sin ticket sincronizado no se sabe el tipo; entonces solo puede aplicarse el general.</summary>
    [Fact]
    public async Task SinTipoConocido_seUsaElGeneral()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoAlTomar, "Active; Bug=New"));

        Assert.Equal("Active", await Puente(db).EstadoAlTomarAsync(null));
        Assert.Equal("Active", await Puente(db).EstadoAlTomarAsync(""));
    }

    /// <summary>
    /// La clave ANTERIOR se sigue leyendo. Quien la hubiera capturado antes de que esto fuera por
    /// tipo no tiene que borrarla para que la nueva funcione, ni se queda sin nada si no hace nada.
    /// </summary>
    [Fact]
    public async Task LaClaveVieja_sigueValiendoComoRespaldo()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoEnProgreso, "Doing"));

        Assert.Equal("Doing", await Puente(db).EstadoAlTomarAsync("Bug"));
    }

    /// <summary>Y la nueva gana sobre la vieja, para que migrar sea escribir la nueva y ya.</summary>
    [Fact]
    public async Task LaClaveNueva_ganaSobreLaVieja()
    {
        using var db = BaseCon(
            (SettingsService.Claves.PoolDevOpsEstadoEnProgreso, "Doing"),
            (SettingsService.Claves.PoolDevOpsEstadoAlTomar, "Bug=New"));

        Assert.Equal("New", await Puente(db).EstadoAlTomarAsync("Bug"));
    }

    /// <summary>Sin nada configurado se deduce de lo que usan los tickets, que es lo que hace que
    /// esto sirva de algo el primer día.</summary>
    [Fact]
    public async Task SinNadaConfigurado_seDeduceDeLosTickets()
    {
        using var db = BaseCon();
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 1, Title = "a", State = "Doing" });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 2, Title = "b", State = "Doing" });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 3, Title = "c", State = "New" });
        await db.SaveChangesAsync();

        Assert.Equal("Doing", await Puente(db).EstadoAlTomarAsync("Bug"));
    }

    [Fact]
    public async Task SinNadaDeNada_caeEnElValorPorOmision()
    {
        using var db = BaseCon();
        Assert.Equal(PoolDevOpsService.EstadoEnProgresoPorOmision,
                     await Puente(db).EstadoAlTomarAsync("Bug"));
    }

    // ── La columna del tablero, que no es el estado ─────────────────────────────

    /// <summary>
    /// Sin ajuste NO se toca la columna, que es lo normal y tiene que seguir siéndolo.
    ///
    /// <para>En casi todos los tableros cada columna está mapeada a un estado, así que cambiar el
    /// estado ya mueve la tarjeta. Escribir además la columna «por si acaso» costaría dos peticiones
    /// más por cada toma y podría fallar en los work items que no están en ningún tablero.</para>
    /// </summary>
    [Fact]
    public async Task SinAjuste_noSeTocaLaColumna()
    {
        using var db = BaseCon();
        Assert.Null(await Puente(db).ColumnaAlTomarAsync("Bug"));
    }

    /// <summary>Se escribe con los mismos pares por tipo que el estado: es la misma pregunta sobre
    /// otro campo, y aprender dos formatos para lo mismo es lo que hace que no se configure.</summary>
    [Theory]
    [InlineData("Bug", "Corrección")]
    [InlineData("Task", "En curso")]
    public async Task ElTipoDecideLaColumna(string tipo, string esperada)
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsColumnaAlTomar,
                                "Bug=Corrección; Task=En curso"));

        var columna = await Puente(db).ColumnaAlTomarAsync(tipo);

        Assert.Equal(esperada, columna!.Value.nombre);
        Assert.False(columna.Value.mitadHecha);
    }

    /// <summary>Y el valor suelto vale para los tipos que nadie nombró, igual que en el estado.</summary>
    [Fact]
    public async Task UnValorSuelto_valeParaLosTiposNoNombrados()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsColumnaAlTomar, "En curso; Bug=Corrección"));

        Assert.Equal("Corrección", (await Puente(db).ColumnaAlTomarAsync("Bug"))!.Value.nombre);
        Assert.Equal("En curso", (await Puente(db).ColumnaAlTomarAsync("Feature"))!.Value.nombre);
    }

    /// <summary>Un tipo en blanco significa «esa columna no se toca», y hace falta para poder mover
    /// unos tipos sí y otros no sin dejar de tener un valor general.</summary>
    [Fact]
    public async Task UnTipoEnBlanco_dejaLaColumnaQuieta()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsColumnaAlTomar, "En curso; Bug="));

        Assert.Null(await Puente(db).ColumnaAlTomarAsync("Bug"));
        Assert.Equal("En curso", (await Puente(db).ColumnaAlTomarAsync("Task"))!.Value.nombre);
    }

    /// <summary>
    /// La columna NO hereda del ajuste del estado. Son campos distintos con nombres distintos
    /// —«Approved» es un estado y «Análisis» es una columna— y usar uno como el otro pediría a DevOps
    /// una columna que no existe en el tablero.
    /// </summary>
    [Fact]
    public async Task LaColumna_noSacaNadaDelAjusteDelEstado()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsEstadoAlTomar, "Bug=New; Task=Approved"));

        Assert.Null(await Puente(db).ColumnaAlTomarAsync("Bug"));
        Assert.Null(await Puente(db).ColumnaAlTomarAsync("Task"));
    }

    // ── La mitad derecha de una columna partida ────────────────────────────────

    /// <summary>Una columna sin sufijo cae en la mitad izquierda, que es «haciéndose».</summary>
    [Fact]
    public void SinSufijo_esLaMitadIzquierda()
    {
        var (nombre, hecha) = PoolDevOpsService.SepararLaMitad("  En curso  ");

        Assert.Equal("En curso", nombre);
        Assert.False(hecha);
    }

    /// <summary>El sufijo se admite en las dos lenguas: el tablero de DevOps lo llama «Done» y aquí
    /// todo lo demás está en español, así que obligar a acertar con una sola sería un ajuste que no
    /// hace nada y no dice por qué.</summary>
    [Theory]
    [InlineData("En curso|hecho")]
    [InlineData("En curso|done")]
    [InlineData("En curso | HECHO")]
    public void ConSufijo_esLaMitadDerecha(string valor)
    {
        var (nombre, hecha) = PoolDevOpsService.SepararLaMitad(valor);

        Assert.Equal("En curso", nombre);
        Assert.True(hecha);
    }

    /// <summary>
    /// Una barra que NO es el sufijo forma parte del nombre y se respeta entera.
    ///
    /// <para>Los nombres de columna los escribe quien configura el tablero y pueden llevar barra
    /// —«Análisis/Diseño»—. Tragarse lo que hay tras la última barra mandaría a DevOps media columna
    /// y el fallo diría que esa columna no existe, sin decir que se la comió esta función.</para>
    /// </summary>
    [Theory]
    [InlineData("Análisis/Diseño")]
    [InlineData("Análisis|Diseño")]
    public void UnaBarraQueNoEsElSufijo_seQuedaEnElNombre(string valor)
    {
        var (nombre, hecha) = PoolDevOpsService.SepararLaMitad(valor);

        Assert.Equal(valor, nombre);
        Assert.False(hecha);
    }

    /// <summary>Y de punta a punta: el ajuste con sufijo llega separado a quien tiene que mandarlo.</summary>
    [Fact]
    public async Task ElSufijo_sobreviveAlAjuste()
    {
        using var db = BaseCon((SettingsService.Claves.PoolDevOpsColumnaAlTomar, "Task=En curso|hecho"));

        var columna = await Puente(db).ColumnaAlTomarAsync("Task");

        Assert.Equal("En curso", columna!.Value.nombre);
        Assert.True(columna.Value.mitadHecha);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    private sealed class ProtectorDePrueba : IProtectorDeSecretos
    {
        public string Proteger(string valorEnClaro) => valorEnClaro;
        public string? Desproteger(string cifrado) => cifrado;
    }

    /// <summary>Resolver el estado no sale a la red: si alguna de estas pruebas acabara llamando a
    /// DevOps, esto lo dice en voz alta en vez de pasar en verde sin haber probado nada.</summary>
    private sealed class ClienteQueNoSeUsa : IClienteAzureDevOps
    {
        private static T No<T>() => throw new InvalidOperationException(
            "Resolver el estado al tomar no habla con Azure DevOps.");

        public Task<IReadOnlyList<int>> ConsultarIdsAsync(CredencialesDevOps c, string? w, CancellationToken ct = default) => No<Task<IReadOnlyList<int>>>();
        public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(CredencialesDevOps c, IReadOnlyCollection<int> i, CancellationToken ct = default) => No<Task<IReadOnlyList<WorkItemDevOps>>>();
        public Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<ComentarioDevOps>>>();
        public Task PublicarComentarioAsync(CredencialesDevOps c, int n, string t, CancellationToken ct = default) => No<Task>();
        public Task<string> SubirAdjuntoAsync(CredencialesDevOps c, byte[] b, string n, CancellationToken ct = default) => No<Task<string>>();
        public Task<(string nombre, string correo)> ReasignarAsync(CredencialesDevOps c, int n, string? correo, CancellationToken ct = default) => No<Task<(string, string)>>();
        public Task<string> CambiarEstadoAsync(CredencialesDevOps c, int n, string e, CancellationToken ct = default) => No<Task<string>>();
        public Task<ColumnaDeTablero> CambiarColumnaAsync(CredencialesDevOps c, int n, string col, bool m, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task<ColumnaDeTablero> LeerColumnaAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task CambiarPrioridadAsync(CredencialesDevOps c, int n, int p, CancellationToken ct = default) => No<Task>();
        public Task<(bool escrito, string aviso)> EscribirEstimacionAsync(CredencialesDevOps c, int n, double h, CancellationToken ct = default) => No<Task<(bool, string)>>();
        public Task<bool> SumarTrabajoCompletadoAsync(CredencialesDevOps c, int n, double h, bool r, CancellationToken ct = default) => No<Task<bool>>();
        public Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<BugHijoDevOps>>>();
        public Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<CambioDeAsignacionDevOps>>>();
        public Task<(bool ok, string mensaje)> ProbarCredencialesAsync(CredencialesDevOps c, CancellationToken ct = default) => No<Task<(bool, string)>>();
    }
}
