using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Components;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Reacciona a las respuestas de la API que no son datos, sino situaciones: la sesión caducó, la
/// contraseña temporal sigue sin cambiarse, no hay permiso.
///
/// Vive en un solo sitio a propósito. Si cada pantalla tuviera que acordarse de mirar el código de
/// estado, la que se olvidara dejaría al usuario ante una tabla vacía sin explicación — que es el
/// peor final posible para «tu sesión caducó».
/// </summary>
public class ManejadorDeRespuestas(NavigationManager navegacion, EstadoSesion sesion) : DelegatingHandler
{
    /// <summary>Lo que devuelve la API cuando la cuenta arrastra una contraseña temporal.</summary>
    private const string CodigoDebeCambiarContrasena = "MUST_CHANGE_PASSWORD";

    /// <summary>
    /// Y lo que devuelve cuando falta activar el segundo factor. Los dos son 403 y llevan a pantallas
    /// distintas: por eso el servidor manda un código como DATO y aquí se mira ese código y no el
    /// texto del mensaje, que se reescribe cualquier día sin que nadie lo relacione con esto.
    /// </summary>
    private const string CodigoDebeActivarSegundoFactor = "MUST_ENROLL_2FA";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion, CancellationToken ct)
    {
        var respuesta = await base.SendAsync(peticion, ct);

        // La consulta de sesión se excluye: es la que se usa justamente para averiguar si hay
        // sesión, y un 401 ahí es una respuesta legítima, no una expulsión.
        //
        // EstadoSesion ya no pasa por aquí —tiene su propio cliente sin manejador, porque si no la
        // construcción del contenedor entra en recursión infinita; ver Program.cs—, así que esta
        // comprobación cubre a cualquier otro que llame a esa ruta por su cuenta.
        bool esConsultaDeSesion = peticion.RequestUri?.AbsolutePath.EndsWith("/api/auth/me") == true;

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized && !esConsultaDeSesion)
        {
            sesion.Olvidar();
            navegacion.NavigateTo("acceso", replace: true);
            return respuesta;
        }

        if (respuesta.StatusCode == HttpStatusCode.Forbidden)
        {
            // El cuerpo se lee UNA vez y se decide con lo leído. Preguntar dos veces —una por cada
            // código— vaciaría el flujo de la respuesta en la primera y la segunda nunca encontraría
            // nada: el fallo aparecería como «a veces no me lleva a la pantalla».
            switch (await CodigoDeLaSituacion(respuesta))
            {
                case CodigoDebeCambiarContrasena:
                    navegacion.NavigateTo("cambiar-contrasena", replace: true);
                    break;

                case CodigoDebeActivarSegundoFactor:
                    navegacion.NavigateTo("segundo-factor", replace: true);
                    break;
            }
        }

        return respuesta;
    }

    /// <summary>
    /// Distingue «no tienes permiso» de las dos situaciones que se arreglan solas yendo a una
    /// pantalla: la contraseña temporal sin cambiar y el segundo factor sin activar. Los tres casos
    /// son 403 y llevan a sitios distintos, así que lo que los separa es el código que la API manda
    /// como dato dentro del cuerpo.
    /// </summary>
    private static async Task<string?> CodigoDeLaSituacion(HttpResponseMessage respuesta)
    {
        try
        {
            var problema = await respuesta.Content.ReadFromJsonAsync<ProblemaConCodigo>();
            return problema?.Code;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ProblemaConCodigo(string? Code);
}

/// <summary>
/// Envuelve las llamadas a la API para que las pantallas no repitan el mismo try/catch.
///
/// Devuelve <c>null</c> cuando algo salió mal y el mensaje ya se le mostró al usuario, así que quien
/// llama solo tiene que preguntarse «¿hay datos?» en vez de interpretar códigos de estado.
/// </summary>
public class ClienteApi(HttpClient http, AvisosDeInterfaz avisos)
{
    public async Task<T?> LeerAsync<T>(string ruta, CancellationToken ct = default)
    {
        try
        {
            var respuesta = await http.GetAsync(ruta, ct);
            if (respuesta.IsSuccessStatusCode) return await respuesta.Content.ReadFromJsonAsync<T>(ct);

            // El 401 ya lo atendió el manejador (redirige al acceso); avisar además sería ruido.
            if (respuesta.StatusCode != HttpStatusCode.Unauthorized)
                avisos.Error(await DescribirAsync(respuesta));

            return default;
        }
        catch (Exception ex)
        {
            avisos.Error($"No se pudo consultar: {ex.Message}");
            return default;
        }
    }

    public async Task<(bool ok, TRespuesta? datos)> EnviarAsync<TCuerpo, TRespuesta>(
        string ruta, TCuerpo cuerpo, CancellationToken ct = default)
    {
        try
        {
            var respuesta = await http.PostAsJsonAsync(ruta, cuerpo, ct);
            if (respuesta.IsSuccessStatusCode)
                return (true, await respuesta.Content.ReadFromJsonAsync<TRespuesta>(ct));

            if (respuesta.StatusCode != HttpStatusCode.Unauthorized)
                avisos.Error(await DescribirAsync(respuesta));

            return (false, default);
        }
        catch (Exception ex)
        {
            avisos.Error($"No se pudo completar la operación: {ex.Message}");
            return (false, default);
        }
    }

    /// <summary>
    /// Envía un formulario con archivos.
    ///
    /// Multipart y no JSON con los bytes en base64: base64 infla cada archivo un tercio y obliga al
    /// servidor a materializarlo entero en memoria antes de poder mirarlo. Con multipart los bytes
    /// viajan tal cual y la API los lee en flujo.
    ///
    /// El <c>StreamContent</c> se queda sin liberar a propósito hasta que la petición termina: es el
    /// propio <c>MultipartFormDataContent</c> quien lo cierra, y adelantarse cancelaría el envío a
    /// mitad.
    /// </summary>
    public async Task<(bool ok, TRespuesta? datos)> SubirAsync<TRespuesta>(
        string ruta, MultipartFormDataContent formulario, CancellationToken ct = default)
    {
        try
        {
            using (formulario)
            {
                using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = formulario };

                // El testigo lo pone este método y no cada pantalla: una que se olvidara recibiría un
                // 400 sin explicación, y quien lo depurara acabaría quitando la protección para que
                // «funcione». Aquí no hay nada que olvidar.
                if (await TestigoAsync(ct) is { } testigo)
                    peticion.Headers.Add("X-XSRF-TOKEN", testigo);

                var respuesta = await http.SendAsync(peticion, ct);
                if (respuesta.IsSuccessStatusCode)
                    return (true, await respuesta.Content.ReadFromJsonAsync<TRespuesta>(ct));

                if (respuesta.StatusCode != HttpStatusCode.Unauthorized)
                    avisos.Error(await DescribirAsync(respuesta));

                return (false, default);
            }
        }
        catch (Exception ex)
        {
            avisos.Error($"No se pudo subir: {ex.Message}");
            return (false, default);
        }
    }

    /// <summary>Testigo antiforgery, pedido una vez y guardado mientras dure la sesión.</summary>
    private string? _testigo;

    private async Task<string?> TestigoAsync(CancellationToken ct)
    {
        if (_testigo != null) return _testigo;

        try
        {
            var r = await http.GetFromJsonAsync<TestigoDto>("api/auth/antiforgery", ct);
            return _testigo = r?.Valor;
        }
        catch
        {
            // Sin testigo la subida fallará con un 400 que el usuario verá explicado. Reventar aquí
            // solo cambiaría dónde aparece el error, no que exista.
            return null;
        }
    }

    /// <summary>
    /// Traduce la respuesta a algo que una persona pueda leer.
    ///
    /// Un rechazo llega de dos formas y las dos importan. El filtro global de la API convierte las
    /// excepciones en <c>ProblemDetails</c> (<c>Detail</c>/<c>Title</c>); los endpoints que rechazan
    /// por REGLA DE NEGOCIO devuelven un <c>ResultadoDto</c> con su <c>Mensaje</c>, y ese texto lo
    /// escribió el servicio explicando el motivo en concreto —«ya marcaste tu entrada hoy a las
    /// 09:12»—. Leer solo una de las dos formas cambia media aplicación por un «no se pudo»
    /// genérico, que es exactamente lo que el escritorio hacía bien y no hay que perder.
    ///
    /// El 409 se explica aparte porque es el único que pide una acción distinta a reintentar:
    /// recargar antes de volver a guardar.
    /// </summary>
    private static async Task<string> DescribirAsync(HttpResponseMessage respuesta)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<CuerpoDeFallo>();
            if (!string.IsNullOrWhiteSpace(cuerpo?.Mensaje)) return cuerpo!.Mensaje!;
            if (!string.IsNullOrWhiteSpace(cuerpo?.Detail)) return cuerpo!.Detail!;
            if (!string.IsNullOrWhiteSpace(cuerpo?.Title)) return cuerpo!.Title!;
        }
        catch { /* la respuesta pudo no traer cuerpo, o no ser JSON */ }

        return respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tienes permiso para hacer eso.",
            HttpStatusCode.NotFound => "Eso ya no existe. Actualiza la lista.",
            HttpStatusCode.Conflict => "Alguien más lo modificó mientras tanto. Recarga antes de guardar.",
            _ => "No se pudo completar la operación."
        };
    }

    /// <summary>Las dos formas en que un fallo trae su explicación, en un solo tipo.</summary>
    private sealed record CuerpoDeFallo(string? Title, string? Detail, string? Mensaje);
}
