using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Dtos.Jornada;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// Lo que solo se ve con la API levantada: la tubería.
///
/// Las ~570 pruebas de <c>AdminWeb.Application</c> comprueban las reglas dentro de los servicios y
/// no pueden ver nada de esto. Un servicio puede estar impecable y la API responder mal por cosas
/// que compilan sin quejarse: una política mal escrita, un servicio sin registrar en el contenedor,
/// el middleware en el orden equivocado, una cookie que no viaja, un rechazo de negocio que sale
/// como error 500 en vez de como 400 con su explicación.
///
/// La prueba de humo (<c>humo.ps1</c>) cubre este mismo terreno pero se lanza a mano; esto corre en
/// cada compilación.
/// </summary>
public class AutorizacionEndpointsTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    // ── La puerta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaSalud_ResponderSinSesion()
    {
        // Es la que mira el balanceador antes de mandar tráfico: si exigiera sesión, un despliegue
        // correcto se marcaría como caído y no entraría nunca en servicio.
        var r = await api.NuevoCliente().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SinSesion_UnaRutaProtegidaResponde401YNoUnaRedireccion()
    {
        // Por omisión, la autenticación por cookie contesta con una redirección 302 a una página de
        // acceso que aquí no existe. El cliente necesita el CÓDIGO: con un 302 vería un HTML donde
        // esperaba datos y el fallo aparecería como un error de deserialización sin sentido.
        var r = await api.NuevoCliente().GetAsync("/api/jornada/mia");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ConCredencialesMalas_Responde401YNoDiceCual()
    {
        var r = await api.NuevoCliente().PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin", "esta-no-es"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);

        // Distinguir «ese usuario no existe» de «la contraseña no es» le sirve a quien prueba
        // usuarios uno por uno, y a nadie más.
        var cuerpo = await r.Content.ReadFromJsonAsync<ResultadoDto>();
        Assert.DoesNotContain("no existe", cuerpo!.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConLaContrasenaTemporal_TodoLoDemasQuedaCortadoConSuCodigo()
    {
        // Instancia PROPIA, y no la compartida: la contraseña temporal deja de valer en cuanto
        // alguien la cambia, y esta es la única prueba que necesita usarla sin cambiarla. Colgarla
        // de la compartida la haría depender del orden en que xUnit decida correr las demás.
        var propia = new ApiDePrueba();
        try
        {
            await ((IAsyncLifetime)propia).InitializeAsync();

            var cliente = propia.NuevoCliente();
            await cliente.PostAsJsonAsync("/api/auth/login",
                new LoginRequest("admin", propia.ContrasenaTemporal));

            var r = await cliente.GetAsync("/api/dashboard");

            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            // El código viaja como DATO y no dentro del texto del mensaje: el cliente lo usa para
            // llevar a la pantalla de cambio, y un mensaje se reescribe cualquier día.
            Assert.Contains("MUST_CHANGE_PASSWORD", await r.Content.ReadAsStringAsync());
        }
        finally
        {
            await ((IAsyncLifetime)propia).DisposeAsync();
        }
    }

    // ── Con sesión ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrasCambiarLaContrasena_LaSesionSirve()
    {
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yo = await r.Content.ReadFromJsonAsync<UsuarioSesionDto>();
        Assert.Equal("admin", yo!.Usuario);
        Assert.False(yo.DebeCambiarContrasena);
    }

    [Fact]
    public async Task UnRechazoDeNegocio_SaleComo400ConSuMotivo_NoComo500()
    {
        // Es la diferencia entre «ya marcaste tu entrada hoy a las 09:12» y una pantalla en blanco.
        // Los servicios portados del escritorio explican el motivo en concreto, y ese texto es la
        // mitad de su valor: si se perdiera por el camino, la web sería peor que el escritorio.
        var cliente = await api.ClienteAdminAsync();

        var primera = await cliente.PostAsJsonAsync("/api/jornada/entrada", new MarcajeRequest(null));
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        var segunda = await cliente.PostAsJsonAsync("/api/jornada/entrada", new MarcajeRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
        var resultado = await segunda.Content.ReadFromJsonAsync<ResultadoDto>();
        Assert.False(resultado!.Ok);
        Assert.Contains("Ya marcaste", resultado.Mensaje);
    }

    [Fact]
    public async Task UnAdjuntoQueNoExiste_Responde404YNoUnArchivoVacio()
    {
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.GetAsync("/api/adjuntos/foro/999999");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task AlCerrarSesion_LaCookieDejaDeValerDeVerdad()
    {
        var cliente = await api.ClienteAdminAsync();

        await cliente.PostAsync("/api/auth/logout", null);
        var r = await cliente.GetAsync("/api/jornada/mia");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ── Las políticas de rol ─────────────────────────────────────────────────────

    [Fact]
    public async Task UnaCuentaSinRolDeAdmin_NoEntraEnLoQueEsSoloDelAdmin()
    {
        // Es LA prueba que justifica este proyecto. En el cliente, estas pantallas ni siquiera
        // aparecen en el menú — pero eso es comodidad: el cliente corre en la máquina de cada
        // persona y a la API se la puede llamar sin pasar por él. Esto comprueba la barrera que
        // cuenta, la del servidor.
        var cliente = await api.ClienteComoAsync(UserRole.Desarrollador, "dev.prueba");

        foreach (var ruta in new[] { "/api/bitacora?pagina=1&tamanoPagina=5", "/api/foro/auditoria" })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Fact]
    public async Task UnaCuentaDeOperaciones_TampocoEntraEnLoDelEquipo()
    {
        // Operaciones existe para los despliegues. El dashboard enseña carga del equipo, ranking de
        // desempeño y recordatorios internos, y nada de eso le corresponde.
        var cliente = await api.ClienteComoAsync(UserRole.Operaciones, "ops.prueba");

        var r = await cliente.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task ElAutoservicio_NoEsParaOperaciones()
    {
        // Pool, autocalificación y ausencias son del desarrollador. Operaciones no tiene ficha ni
        // participa del desempeño, así que estas rutas no le corresponden — y el menú, que es lo
        // único que lo esconde en el cliente, no cuenta como barrera.
        var cliente = await api.ClienteComoAsync(UserRole.Operaciones, "ops.prueba");

        foreach (var ruta in new[]
                 {
                     "/api/pool/mio",
                     "/api/autocalificacion/mias",
                     "/api/ausencias/vacaciones/mias",
                     "/api/ausencias/permisos/mios"
                 })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Fact]
    public async Task LaAdministracionDelPool_NoEsParaElDesarrollador()
    {
        // Publicar, valuar y aceptar es del líder. Que el desarrollador entre en «su» pool no le da
        // acceso al del equipo, y menos a la matriz de puntos: podría revaluar su propio trabajo.
        var cliente = await api.ClienteComoAsync(UserRole.Desarrollador, "dev.prueba");

        foreach (var ruta in new[] { "/api/pool/lider", "/api/pool/configuracion" })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Fact]
    public async Task LaJornada_EsDeLaCuenta_NoDeLaFichaDeDesarrollador()
    {
        // Lo que esta prueba protege sigue siendo cierto y por eso conserva su nombre: la jornada
        // cuelga de la CUENTA y no de la ficha de desarrollador. El administrador inicial no tiene
        // ficha y tiene que poder entrar igual; si esta ruta se atara al DeveloperId, dejaría fuera a
        // quien revisa la asistencia de los demás.
        //
        // Lo que cambió —y es la mitad que antes decía esta prueba— es QUIÉN registra jornada:
        // Operaciones ya no. Ese caso está en Operaciones_NoEntraEnLaJornada.
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.GetAsync("/api/jornada/estado");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Operaciones_NoEntraEnLaJornada()
    {
        // Operaciones no registra jornada: su alcance son los despliegues y su estado lo pone el
        // servidor. En el cliente ni siquiera hay entrada de menú, pero eso es comodidad — el cliente
        // corre en la máquina de cada persona y a la API se la puede llamar a mano.
        //
        // Van TODAS en un solo recorrido y no una de muestra, porque el valor está justamente en que
        // no quede ninguna suelta: basta una para poder marcar entrada o leerse el registro entero.
        var cliente = await api.ClienteComoAsync(UserRole.Operaciones, "ops.prueba");

        foreach (var ruta in new[] { "/api/jornada/mia", "/api/jornada/estado", "/api/jornada/presencia" })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        foreach (var ruta in new[] { "/api/jornada/entrada", "/api/jornada/salida" })
        {
            var r = await cliente.PostAsJsonAsync(ruta, new MarcajeRequest(null));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Fact]
    public async Task Operaciones_NoEntraEnElForo()
    {
        // El foro es la conversación del equipo de desarrollo. La última ruta es la que de verdad hay
        // que escribir: los BYTES de las capturas no salen por /api/foro sino por /api/adjuntos, y sin
        // cerrarla también cerrar el muro no habría cerrado nada — se seguirían bajando por número.
        var cliente = await api.ClienteComoAsync(UserRole.Operaciones, "ops.prueba");

        foreach (var ruta in new[]
                 {
                     "/api/foro/muro?pagina=1&tamano=5",
                     "/api/foro/opciones",
                     "/api/foro/hilos/1",
                     "/api/foro/imagenes?entradas=1",
                     "/api/adjuntos/foro/1"
                 })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    [Fact]
    public async Task ElDesarrollador_SigueEntrandoEnElForoYEnLaJornada()
    {
        // Sin esta, las dos de arriba pasarían igual de bien si alguien hubiera cerrado el foro y la
        // jornada para TODO el mundo. Lo que se recortó es un rol, no el módulo.
        var cliente = await api.ClienteComoAsync(UserRole.Desarrollador, "dev.prueba");

        foreach (var ruta in new[] { "/api/foro/muro?pagina=1&tamano=5", "/api/jornada/estado" })
        {
            var r = await cliente.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }

    [Fact]
    public async Task ElLider_SigueViendoLaAuditoriaDelForo()
    {
        // El error fácil de este cambio: la auditoría lleva «SoloAdmin» propio y el grupo lleva ahora
        // «AdminUDesarrollador». ASP.NET Core las compone con Y —hay que cumplir las dos—, así que si
        // alguna de las dos se escribiera mal, el propio administrador se quedaría fuera de su
        // herramienta de supervisión sin que nada más lo delatara.
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.GetAsync("/api/foro/auditoria");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task ElTestigoAntiforgery_SeEmite()
    {
        // Sin él, todas las subidas de archivos fallarían con un 400 sin explicación.
        var cliente = await api.ClienteAdminAsync();

        var testigo = await cliente.GetFromJsonAsync<TestigoDto>("/api/auth/antiforgery");

        Assert.False(string.IsNullOrWhiteSpace(testigo!.Valor));
    }
}
