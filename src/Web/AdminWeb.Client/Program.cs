using AdminWeb.Client;
using AdminWeb.Client.Servicios;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var origen = new Uri(builder.HostEnvironment.BaseAddress);

// ── EL CLIENTE DE LA SESIÓN VA APARTE, Y NO ES UN CAPRICHO ──────────────────────
//
// «¿Hay sesión?» se pregunta con un cliente SIN el manejador de respuestas. Dos motivos, y el
// segundo es el que obliga:
//
//   1. Un 401 en /api/auth/me es la RESPUESTA («no hay sesión»), no una expulsión. Pasarlo por el
//      manejador que reacciona a los 401 mandando a la pantalla de acceso sería pedirle que
//      interprete como problema justo lo que se fue a averiguar.
//
//   2. Sin esto hay una DEPENDENCIA CIRCULAR: HttpClient → ManejadorDeRespuestas → EstadoSesion →
//      HttpClient. El contenedor no la detecta porque el HttpClient se construye con una fábrica, y
//      la recursión infinita al resolverlo CONGELA el hilo único de WebAssembly antes de que se
//      ejecute ningún componente: pantalla en «Cargando…» para siempre, sin una sola línea en la
//      consola ni una petición a la API. Costó encontrarlo justamente porque no falla, se cuelga.
builder.Services.AddScoped(sp => new EstadoSesion(new HttpClient { BaseAddress = origen }));

// El HttpClient de las pantallas apunta a la propia API que sirve esta aplicación (mismo origen),
// que es lo que permite que la cookie de sesión viaje sola en cada petición sin que nadie la toque.
//
// Va con el manejador que atiende las respuestas que no son datos sino situaciones (sesión caducada,
// contraseña temporal sin cambiar). Resolverlo aquí evita que cada pantalla tenga que acordarse.
builder.Services.AddScoped<ManejadorDeRespuestas>();
builder.Services.AddScoped(sp =>
{
    var manejador = sp.GetRequiredService<ManejadorDeRespuestas>();
    manejador.InnerHandler = new HttpClientHandler();
    return new HttpClient(manejador) { BaseAddress = origen };
});
builder.Services.AddScoped<AuthenticationStateProvider, ProveedorEstadoAutenticacion>();
builder.Services.AddScoped<AvisosDeInterfaz>();
builder.Services.AddScoped<ClienteApi>();
builder.Services.AddScoped<Descargas>();
builder.Services.AddScoped<PreferenciasDeUsuario>();
// Una sola para toda la aplicación: es la conexión que sostiene la presencia y el latido del
// cronómetro. Se abre al entrar (ver MainLayout) y sigue viva al cambiar de pantalla — el
// cronómetro no se para por navegar, igual que en el escritorio no se paraba al cambiar de pestaña.
builder.Services.AddScoped<ConexionEnVivo>();
// Las MISMAS políticas por nombre que declara la API. No es duplicar la seguridad: aquí solo sirven
// para que una página pueda decir [Authorize(Policy="SoloAdmin")] y no se pinte, y si el nombre no
// existe el componente revienta al renderizar en vez de limitarse a no autorizar. La barrera de
// verdad es la de los endpoints, que se aplica aunque nadie pase por este cliente.
builder.Services.AddAuthorizationCore(opciones =>
{
    opciones.AddPolicy("SoloAdmin", p => p.RequireRole(nameof(UserRole.Admin)));
    opciones.AddPolicy("AdminUOperaciones",
        p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Operaciones)));
    opciones.AddPolicy("AdminUDesarrollador",
        p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Desarrollador)));
});

// Radzen: diálogos (los ~80 modales del escritorio), avisos (los 571 MessageBox) y las rejillas.
builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();
