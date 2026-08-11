using System.Security.Cryptography;
using System.Text;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;

namespace AdminWeb.Api.Auth;

/// <summary>
/// La cookie que dice «a este navegador ya no le pidas el código durante treinta días».
///
/// <para><b>Es una cookie PROPIA y no la de sesión, y esa separación es el diseño entero.</b> La de
/// sesión dura ocho horas y se va al cerrar sesión; esta dura treinta días y sobrevive a cerrar
/// sesión, que es justo su trabajo. Mezclarlas obligaría a que una de las dos mintiera sobre su
/// caducidad.</para>
///
/// <para><b>Y no es una credencial.</b> Sola no abre nada: quien la tenga sigue necesitando el
/// usuario y la contraseña. Lo único que hace es ahorrar el segundo tramo. Por eso puede vivir
/// treinta días sin que eso sea una barbaridad — y aun así se guarda con todas las protecciones de
/// la de sesión, porque quien la robe SÍ se lleva el segundo factor de esa cuenta si además consigue
/// la contraseña.</para>
///
/// <para><b>Lo que vale es la fila de la base, no esto.</b> Aquí solo viaja un testigo aleatorio de
/// 256 bits; lo que decide es la fila que le corresponde, con su usuario y su caducidad puestos por
/// el servidor. Consecuencia práctica: borrar filas —cambiar la contraseña, reiniciar el segundo
/// factor, olvidar los equipos— desarma la cookie aunque siga en el navegador, y alargar a mano la
/// caducidad de la cookie no alarga nada.</para>
/// </summary>
public static class CookieDeEquipoRecordado
{
    /// <summary>
    /// El nombre. Con el mismo prefijo que la de sesión para que, mirando las cookies del sitio, se
    /// vea de un golpe qué le pertenece a esta aplicación.
    /// </summary>
    public const string Nombre = "adminweb.equipo";

    /// <summary>Separa la huella del sello del testigo dentro del valor de la cookie.</summary>
    private const char Separador = '.';

    /// <summary>
    /// El testigo que trae este navegador, <b>si además sigue valiendo para el sello actual de la
    /// cuenta</b>. Devuelve <c>null</c> en cualquier otro caso.
    ///
    /// <para><b>Aquí está la segunda mitad de la invalidación, y es la que no se puede olvidar.</b>
    /// La primera —borrar las filas— la hace <c>AuthService</c> al rotar el sello por un cambio de
    /// contraseña. Pero el sello se rota desde más sitios: al desactivar una cuenta, al cambiarle el
    /// rol, y desde donde se rote mañana. Confiar en que cada uno de esos sitios se acuerde de borrar
    /// también los equipos recordados es confiar en que nadie escriba nunca una rotación nueva sin
    /// leer esto — y el día que ocurra no fallaría nada: simplemente quedaría un navegador que se
    /// salta el segundo factor de una cuenta que acababa de cambiar.</para>
    ///
    /// <para>Por eso la cookie <b>lleva dentro con qué sello se emitió</b>. Rotar el sello la
    /// invalida sola, venga la rotación de donde venga, sin que nadie tenga que acordarse de nada. Lo
    /// que se guarda es una HUELLA del sello y no el sello: así la cookie no revela nada del lado del
    /// servidor, y comparar dos huellas cuesta lo mismo que comparar dos sellos.</para>
    /// </summary>
    public static string? TestigoVigente(HttpContext ctx, User usuario)
    {
        var valor = ctx.Request.Cookies[Nombre];
        if (string.IsNullOrWhiteSpace(valor)) return null;

        int corte = valor.IndexOf(Separador);
        if (corte <= 0 || corte == valor.Length - 1) return null;

        var huella = valor[..corte];
        var testigo = valor[(corte + 1)..];

        // Tiempo constante: comparar huellas con == corta en el primer carácter distinto y ese tiempo
        // es medible. Aquí no habilita ningún ataque práctico —la huella no es un secreto que sirva
        // para nada por sí sola—, pero la forma correcta no cuesta nada.
        return CryptographicOperations.FixedTimeEquals(
                   Encoding.ASCII.GetBytes(huella),
                   Encoding.ASCII.GetBytes(HuellaDelSello(usuario.SecurityStamp)))
            ? testigo
            : null;
    }

    /// <summary>¿Este navegador trae alguna cookie de equipo, valga o no? Sirve para saber si hay que retirarla.</summary>
    public static bool TraeAlguna(HttpContext ctx) => ctx.Request.Cookies.ContainsKey(Nombre);

    /// <summary>
    /// Guarda el testigo en el navegador, atado al sello con el que se emitió.
    ///
    /// <para>Las tres protecciones no son adorno:</para>
    /// <list type="bullet">
    ///   <item><b>HttpOnly</b>: ningún JavaScript la puede leer, así que un XSS no se lleva el
    ///   «equipo de confianza» de nadie. Y no le hace falta a nadie leerla: quien la interpreta es el
    ///   servidor.</item>
    ///   <item><b>SameSite=Strict</b>: no se manda cuando la petición la origina otro sitio. Un
    ///   formulario ajeno apuntando aquí no puede aprovecharse de que el navegador la lleve puesta.</item>
    ///   <item><b>Secure</b>: no viaja por HTTP en claro. Se afloja en desarrollo por el mismo motivo
    ///   documentado en la cookie de sesión: ahí la aplicación se levanta en HTTP y una cookie Secure
    ///   simplemente no se devuelve, con el síntoma de que «recordar el equipo» parece funcionar y
    ///   nunca recuerda nada.</item>
    /// </list>
    ///
    /// <para><b>La caducidad se escribe también aquí, y no manda.</b> La que decide está en la fila
    /// de la base; esta solo evita que el navegador guarde para siempre algo que dejó de valer.</para>
    /// </summary>
    public static void Poner(HttpContext ctx, User usuario, string testigo, bool esDesarrollo)
    {
        var opciones = Opciones(esDesarrollo, ctx);
        opciones.Expires = DateTimeOffset.UtcNow.AddDays(SegundoFactorService.DiasQueSeRecuerdaElEquipo);

        ctx.Response.Cookies.Append(
            Nombre, HuellaDelSello(usuario.SecurityStamp) + Separador + testigo, opciones);
    }

    /// <summary>
    /// Retira la cookie de este navegador.
    ///
    /// <para>Se llama cuando el testigo ya no vale —la fila se borró o venció—. No es cosmética:
    /// dejarla puesta hace que el navegador la mande otros treinta días en cada acceso para que el
    /// servidor la rechace otras tantas veces, y quien mirara las cookies creería que su equipo sigue
    /// recordado cuando no lo está.</para>
    ///
    /// <para><b>Las opciones tienen que ser LAS MISMAS que al ponerla.</b> El navegador identifica
    /// una cookie por nombre, dominio y ruta; con una ruta distinta, esto crearía una cookie vacía
    /// nueva y dejaría la buena intacta, que es el error clásico de este par de métodos.</para>
    /// </summary>
    public static void Borrar(HttpContext ctx, bool esDesarrollo) =>
        ctx.Response.Cookies.Delete(Nombre, Opciones(esDesarrollo, ctx));

    /// <summary>
    /// La huella del sello que viaja en la cookie: los dieciséis primeros caracteres del SHA-256 en
    /// hexadecimal, o sea 64 bits.
    ///
    /// <para>Sobran para lo que se usa, que es <b>detectar que el sello cambió</b> y no autenticar
    /// nada. Se recorta porque una cookie corta es una cookie que viaja en cada petición sin pesar, y
    /// dos sellos distintos —dos GUID— no comparten estos 64 bits ni por casualidad.</para>
    /// </summary>
    private static string HuellaDelSello(string sello) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sello ?? ""))).ToLowerInvariant()[..16];

    private static CookieOptions Opciones(bool esDesarrollo, HttpContext ctx) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = !esDesarrollo || ctx.Request.IsHttps,
        Path = "/",

        // Esencial: la aplicación no funciona sin ella para quien pidió que se le recordara el
        // equipo, así que no depende de un consentimiento de cookies no esenciales.
        IsEssential = true
    };
}
