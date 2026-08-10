using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Foro;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// Las subidas de archivos, que son la única familia de peticiones con protección antifalsificación.
///
/// <b>Estas pruebas existen porque esa protección se rompe en silencio.</b> Si el testigo dejara de
/// emitirse o de adjuntarse, nada fallaría al compilar y ninguna prueba de servicio se enteraría:
/// simplemente publicar en el foro empezaría a responder 400 y adjuntar evidencias también. Y si se
/// desactivara «para que funcione», tampoco fallaría nada — y ahí la que se pierde es la protección.
/// Los dos sentidos se comprueban aquí.
/// </summary>
public class SubidasEndpointsTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    /// <summary>Un PNG mínimo de verdad: la cabecera manda, porque el servidor decide por los bytes.</summary>
    private static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,   // IHDR
        0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x18,   // 32 × 24
        0x08, 0x06, 0x00, 0x00, 0x00
    ];

    private static MultipartFormDataContent Publicacion(byte[]? miniatura = null)
    {
        var formulario = new MultipartFormDataContent
        {
            { new StringContent("Prueba de subida"), "titulo" },
            { new StringContent("Cuerpo de la prueba."), "cuerpo" },
            { new StringContent("0"), "tema" },
            { new StringContent(""), "etiquetas" }
        };
        formulario.Add(new ByteArrayContent(Png()), "imagenes", "captura.png");
        formulario.Add(new ByteArrayContent(miniatura ?? []), "miniaturas", "captura.png");
        return formulario;
    }

    private static async Task<string> TestigoAsync(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<TestigoDto>("/api/auth/antiforgery"))!.Valor;

    [Fact]
    public async Task ConSuTestigo_LaSubidaPasa()
    {
        var cliente = await api.ClienteAdminAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/foro/publicaciones")
        {
            Content = Publicacion()
        };
        peticion.Headers.Add("X-XSRF-TOKEN", await TestigoAsync(cliente));

        var r = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var publicada = await r.Content.ReadFromJsonAsync<ForoPublicadaDto>();
        Assert.True(publicada!.Id > 0);
    }

    [Fact]
    public async Task SinTestigo_LaSubidaSeRechaza()
    {
        // El caso que justifica todo esto: un formulario alojado en otro sitio llegaría con la cookie
        // de sesión puesta, pero sin este testigo. Si esta prueba se pusiera verde con un 200, la
        // protección se habría perdido.
        var cliente = await api.ClienteAdminAsync();

        var r = await cliente.PostAsync("/api/foro/publicaciones", Publicacion());

        // 400 y no 500: al usuario hay que poder decirle qué pasó, y a quien vigila el servidor un
        // 500 le parecería un fallo de la aplicación cuando es esta protección haciendo su trabajo.
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)(int)r.StatusCode);
        Assert.Contains("Recarga", cuerpo);
    }

    [Fact]
    public async Task UnaMiniaturaQueNoEsImagen_NoSeGuarda_YLaPublicacionSigueAdelante()
    {
        // La miniatura la genera el navegador, así que puede llegar manipulada. Que no cuele es lo
        // que importa; que por eso se caiga la publicación entera, no — el servicio guarda entonces
        // el original en su lugar, como hacía antes.
        var cliente = await api.ClienteAdminAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/foro/publicaciones")
        {
            Content = Publicacion("<html><script>alert(1)</script></html>"u8.ToArray())
        };
        peticion.Headers.Add("X-XSRF-TOKEN", await TestigoAsync(cliente));

        var r = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task UnEjecutableDisfrazadoDeImagen_NoEntraEnElForo()
    {
        var cliente = await api.ClienteAdminAsync();

        var formulario = new MultipartFormDataContent
        {
            { new StringContent("Intento"), "titulo" },
            { new StringContent("Cuerpo."), "cuerpo" },
            { new StringContent("0"), "tema" },
            { new StringContent(""), "etiquetas" }
        };
        // Se llama «captura.png» y no lo es. Quien decide es el contenido, no el nombre.
        formulario.Add(new ByteArrayContent("MZ\0"u8.ToArray()), "imagenes", "captura.png");
        formulario.Add(new ByteArrayContent([]), "miniaturas", "captura.png");

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/foro/publicaciones")
        {
            Content = formulario
        };
        peticion.Headers.Add("X-XSRF-TOKEN", await TestigoAsync(cliente));

        var r = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("no es una imagen", await r.Content.ReadAsStringAsync());
    }
}
