using System.Collections.Concurrent;
using AdminWeb.Application.Services;

namespace AdminWeb.Api.TiempoReal;

/// <summary>
/// Cuántas conexiones vivas tiene cada usuario ahora mismo.
///
/// <para><b>Para qué.</b> En un navegador, tener tres pestañas abiertas es lo normal, y cada una es
/// una conexión al hub. Sin llevar la cuenta, cerrar UNA cerraba la jornada entera: quien cerraba la
/// pestaña del foro dejando abierta la del sprint aparecía como desconectado y con el día terminado.
/// Aquí la jornada se cierra cuando se va la ÚLTIMA.</para>
///
/// <para><b>Por qué en memoria y no en la base.</b> Porque es estado del PROCESO, no del negocio:
/// describe qué sockets tiene abiertos esta instancia, y eso deja de ser cierto en cuanto el proceso
/// muere. Guardarlo en la base obligaría a limpiarlo al arrancar y a distinguir «conexiones de esta
/// instancia» de las de otra, para un dato que caduca en segundos.</para>
///
/// <para><b>Qué pasa si el proceso se reinicia con conexiones vivas.</b> El contador se pierde y las
/// jornadas se quedan abiertas. No es un agujero: el barrido por latido —que existe justamente para
/// los cierres sucios— las sella con la hora de su última señal. Esta clase evita el cierre
/// prematuro; el barrido cubre el tardío. Con varias instancias detrás de un balanceador pasa lo
/// mismo por conexiones repartidas, y la respuesta es la misma.</para>
/// </summary>
public sealed class RegistroDeConexiones
{
    private readonly ConcurrentDictionary<int, int> _porUsuario = new();

    /// <summary>Apunta una conexión nueva. Devuelve true si es la PRIMERA de ese usuario.</summary>
    public bool Entra(int userId)
    {
        // AddOrUpdate es atómico: dos pestañas abriéndose a la vez no pueden dejar el contador en 1.
        var total = _porUsuario.AddOrUpdate(userId, 1, (_, n) => n + 1);
        return total == 1;
    }

    /// <summary>Da una conexión por cerrada. Devuelve true si era la ÚLTIMA de ese usuario.</summary>
    public bool Sale(int userId)
    {
        while (true)
        {
            if (!_porUsuario.TryGetValue(userId, out var actual)) return true;

            if (actual <= 1)
            {
                // Se quita la entrada en vez de dejarla en 0: así el diccionario no crece con una
                // fila por cada persona que haya entrado alguna vez desde que arrancó el proceso.
                if (_porUsuario.TryRemove(new KeyValuePair<int, int>(userId, actual))) return true;
                continue;   // otra conexión cambió el número entre la lectura y el borrado
            }

            if (_porUsuario.TryUpdate(userId, actual - 1, actual)) return false;
        }
    }

    /// <summary>
    /// Cuántas conexiones tiene.
    ///
    /// <para>Dejó de ser «solo para diagnóstico» el día que Operaciones dejó de abrir jornada: para
    /// esas cuentas no hay fila que mirar, así que este contador es el ÚNICO sitio donde consta que
    /// están dentro. Quien lo usa para eso es <see cref="ConexionesEnVivoDelHub"/>. Si algún día se
    /// cambia lo que cuenta, el tablero de presencia cambia con él.</para>
    /// </summary>
    public int Cuantas(int userId) => _porUsuario.TryGetValue(userId, out var n) ? n : 0;
}

/// <summary>
/// La respuesta de la capa web a lo que <see cref="IConexionesEnVivo"/> pregunta: «¿está esta
/// persona dentro ahora mismo?».
///
/// <para><b>Por qué existe esta clase de tres líneas.</b> <c>PresenceService</c> vive en la capa de
/// aplicación y no conoce —ni debe conocer— los sockets del hub; declara QUÉ necesita saber y aquí
/// se dice CÓMO se sabe. Es el mismo arreglo de <c>IRequestOrigin</c> y <c>HttpRequestOrigin</c>.</para>
///
/// <para><b>Y por qué NO se puede quitar dejando la interfaz suelta.</b> El parámetro de
/// <c>PresenceService</c> es opcional para no obligar a las pruebas a inventarse un doble, así que
/// sin este registro el servicio se construye igual, sin error y sin aviso — y todas las cuentas de
/// Operaciones salen «Desconectado» para siempre, incluso mientras están desplegando. Es justo el
/// defecto que el contrato existe para evitar, y no lo delata nada: hay que verlo en el tablero.</para>
///
/// <para>SINGLETON, como el registro del que lee: no guarda nada propio y el estado que consulta es
/// del proceso. La salvedad de siempre —una sola instancia detrás del balanceador— está escrita en
/// <see cref="RegistroDeConexiones"/> y en <see cref="IConexionesEnVivo"/>.</para>
/// </summary>
public sealed class ConexionesEnVivoDelHub(RegistroDeConexiones conexiones) : IConexionesEnVivo
{
    public bool EstaConectado(int userId) => conexiones.Cuantas(userId) > 0;
}
