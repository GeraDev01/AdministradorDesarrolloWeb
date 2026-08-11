using System.Net;
using System.Net.Http.Json;
using AdminWeb.Api.Endpoints;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// El reinicio del segundo factor desde la pantalla de usuarios, visto por la tubería.
///
/// <para><b>Esto es lo que las pruebas de servicios no pueden ver.</b> Allí se comprueba que el
/// servicio exige ser líder; aquí, que la ruta está de verdad dentro del grupo que lo exige y que
/// responde con el código que toca. Un servicio impecable colgado por descuido de un grupo sin
/// política sería una dirección que devuelve el acceso a cualquier cuenta a quien la escriba en la
/// barra del navegador — y compilaría igual de bien.</para>
///
/// <para><b>El botón no cuenta como barrera</b>, y por eso estas pruebas no pasan por ninguna
/// pantalla: el cliente Blazor corre en la máquina de cada persona y esta dirección se puede llamar a
/// mano. Lo único que separa a un desarrollador de reiniciarle el segundo factor al líder es lo que
/// se comprueba aquí abajo.</para>
///
/// <para>Los clientes de <see cref="ApiDePrueba"/> ya vienen con su primer día hecho —contraseña
/// cambiada y segundo factor dado de alta—, así que aquí no se repite ese trámite: sería una segunda
/// copia del alta, y dos copias acaban un día haciendo cosas distintas.</para>
/// </summary>
public class ReinicioDelSegundoFactorTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    /// <summary>
    /// Una cuenta cualquiera. En las pruebas de rechazo da igual sobre quién se intente: lo que se
    /// mide es que la petición no llega a ejecutarse.
    /// </summary>
    private const string RutaDeUnaCuenta = "/api/personas/usuarios/1/segundo-factor/reiniciar";

    // ── Quién NO puede ───────────────────────────────────────────────────────────

    /// <summary>
    /// Un desarrollador no reinicia el segundo factor de nadie, y hay que comprobar QUE SE LE NIEGA
    /// POR EL ROL.
    ///
    /// <para>Ese matiz es lo que hace esta prueba menos obvia de lo que parece. Con el segundo factor
    /// obligatorio, una cuenta a la que le faltara el alta chocaría antes con su middleware y recibiría
    /// un 403 igualmente: la prueba pasaría en verde aunque la ruta no tuviera ninguna política de rol.
    /// De ahí la segunda comprobación, que distingue los dos rechazos por el código que traen.</para>
    /// </summary>
    [Fact]
    public async Task UnDesarrollador_NoPuedeReiniciarleElSegundoFactorANadie()
    {
        var cliente = await api.ClienteComoAsync(UserRole.Desarrollador, "dev.reinicio");

        var r = await cliente.PostAsync(RutaDeUnaCuenta, null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        // Y que el rechazo NO sea el del alta pendiente: si lo fuera, esto no estaría probando la
        // política de la ruta sino el middleware, y una ruta desprotegida pasaría desapercibida.
        Assert.DoesNotContain(AuthEndpoints.CodigoDebeActivarSegundoFactor, await r.Content.ReadAsStringAsync());
    }

    /// <summary>Operaciones despliega; las cuentas no son suyas. Va aparte porque es otra política.</summary>
    [Fact]
    public async Task Operaciones_Tampoco()
    {
        var cliente = await api.ClienteComoAsync(UserRole.Operaciones, "ops.reinicio");

        var r = await cliente.PostAsync(RutaDeUnaCuenta, null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain(AuthEndpoints.CodigoDebeActivarSegundoFactor, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SinSesion_NiSiquieraSeLlegaAPreguntar()
    {
        var r = await api.NuevoCliente().PostAsync(RutaDeUnaCuenta, null);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ── El líder ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El recorrido entero tal como lo hace el líder: abre la lista de cuentas, ve el estado del
    /// segundo factor de una de ellas y la reinicia.
    ///
    /// <para>Se comprueba de paso que <b>el estado viaja en el JSON de la lista</b>. Es una respuesta
    /// que las pruebas de servicios ven como objetos en memoria; que además llegue serializada y con
    /// sus nombres es cosa de esta tubería, y sin ella la rejilla enseñaría a todo el mundo como «sin
    /// activar» pasara lo que pasara.</para>
    ///
    /// <para>Y se reinicia una cuenta RECIÉN CREADA, no la del propio administrador: quitarle a la
    /// sesión compartida de estas pruebas su segundo factor dejaría el resto de los casos dependiendo
    /// del orden en que xUnit decidiera correrlos.</para>
    /// </summary>
    [Fact]
    public async Task ElLider_VeElEstadoEnLaListaYPuedeReiniciarlo()
    {
        var cliente = await api.ClienteAdminAsync();
        const string usuario = "victima.reinicio";

        var alta = await cliente.PostAsJsonAsync("/api/personas/usuarios",
            new CrearUsuarioRequest(usuario, "Quien perdió el teléfono", UserRole.Desarrollador, null));
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);

        var pantalla = await cliente.GetFromJsonAsync<PantallaDeUsuariosDto>("/api/personas/usuarios");
        var cuenta = pantalla!.Usuarios.Single(u => u.Usuario == usuario);

        // Recién creada: no ha entrado nunca, así que todavía no ha dado de alta su segundo factor.
        Assert.False(cuenta.SegundoFactorActivo);
        Assert.Null(cuenta.SegundoFactorDesdeUtc);
        Assert.Equal(0, cuenta.CodigosDeRescateRestantes);

        // Y la pantalla recibe los números con los que escribe «le quedan 2 de 8», que no se escriben
        // a mano en la interfaz.
        Assert.True(pantalla.CodigosDeRescateEmitidos > 0);
        Assert.True(pantalla.CodigosDeRescateParaAvisar > 0);

        var r = await cliente.PostAsync($"/api/personas/usuarios/{cuenta.Id}/segundo-factor/reiniciar", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var resultado = await r.Content.ReadFromJsonAsync<ResultadoDto>();
        Assert.True(resultado!.Ok);
        // El mensaje del servidor se enseña tal cual: tiene que nombrar la cuenta sobre la que actuó.
        Assert.Contains(usuario, resultado.Mensaje);
    }

    /// <summary>
    /// Una cuenta que no existe sale como 400 con su explicación, no como 500.
    ///
    /// <para>La pantalla enseña el mensaje del servidor tal cual; un 500 se convertiría en un «no se
    /// pudo completar la operación» genérico justo cuando alguien está esperando al teléfono a que le
    /// devuelvan el acceso.</para>
    /// </summary>
    [Fact]
    public async Task UnaCuentaQueNoExiste_Responde400ConSuMotivo()
    {
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.PostAsync("/api/personas/usuarios/999999/segundo-factor/reiniciar", null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var resultado = await r.Content.ReadFromJsonAsync<ResultadoDto>();
        Assert.False(resultado!.Ok);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensaje));
    }

}
