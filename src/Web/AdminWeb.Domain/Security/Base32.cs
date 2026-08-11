using System.Text;

namespace AdminWeb.Domain.Security;

/// <summary>
/// Base32 del RFC 4648, escrito a mano porque .NET no lo trae (solo trae Base64).
///
/// <para><b>Por qué hace falta y no vale Base64.</b> El secreto del segundo factor tiene que poder
/// TECLEARSE: quien no puede escanear el código QR —cámara rota, teléfono de empresa sin permiso
/// para la cámara, o simplemente que la pantalla esté en otro equipo— lo escribe a mano en su
/// aplicación de códigos. Base64 distingue mayúsculas de minúsculas y usa <c>+</c> y <c>/</c>, así
/// que dictarlo o teclearlo es una fuente garantizada de errores. Base32 usa un alfabeto de 32
/// símbolos —A a Z y 2 a 7— que no distingue mayúsculas al leer y no tiene signos raros. Además es
/// el formato que TODAS las aplicaciones de códigos esperan en el parámetro <c>secret</c> del
/// URI <c>otpauth://</c>: si aquí se escribiera otra cosa, ninguna sabría leerlo.</para>
///
/// <para><b>Si se toca esto, se rompe el segundo factor de todo el mundo a la vez.</b> El secreto ya
/// escaneado por los teléfonos se guardó como estos caracteres; cambiar el alfabeto o el orden de
/// los bits haría que el código que muestra el teléfono dejara de coincidir con el que calcula el
/// servidor, y nadie podría entrar hasta que el líder les reiniciara el segundo factor uno por uno.
/// </para>
/// </summary>
public static class Base32
{
    /// <summary>
    /// El alfabeto del RFC 4648, en su orden EXACTO. El orden es parte del formato, no una
    /// preferencia: la posición de cada carácter es el valor de los cinco bits que representa.
    /// </summary>
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Los bytes del secreto como texto tecleable.
    ///
    /// <para><b>No se emite el relleno con <c>=</c>.</b> El RFC lo pide para que la longitud sea
    /// múltiplo de 8 caracteres, pero el secreto de este sistema son 20 bytes = 160 bits = 32
    /// caracteres exactos, así que nunca sobra nada que rellenar. Y omitirlo tiene una ventaja
    /// concreta: el <c>=</c> dentro de un URI hay que escaparlo como <c>%3D</c>, y hay aplicaciones
    /// de códigos que se atragantan con eso en el parámetro <c>secret</c>.</para>
    /// </summary>
    public static string Codificar(byte[]? datos)
    {
        if (datos is null || datos.Length == 0) return "";

        // Cada 5 bits de entrada producen un carácter; se redondea hacia arriba.
        var salida = new StringBuilder((datos.Length * 8 + 4) / 5);

        // Se van metiendo bytes por la derecha de un acumulador y sacando grupos de 5 bits por la
        // izquierda. Es la forma directa de reagrupar 8 en 5 sin hacer cuentas por bloques de 40
        // bits, que es donde suelen aparecer los errores de un byte.
        int acumulador = 0, bitsPendientes = 0;
        foreach (var b in datos)
        {
            acumulador = (acumulador << 8) | b;
            bitsPendientes += 8;

            while (bitsPendientes >= 5)
            {
                bitsPendientes -= 5;
                salida.Append(Alfabeto[(acumulador >> bitsPendientes) & 31]);
            }
        }

        // Lo que queda suelto se completa con ceros a la derecha, como manda el RFC.
        if (bitsPendientes > 0)
            salida.Append(Alfabeto[(acumulador << (5 - bitsPendientes)) & 31]);

        return salida.ToString();
    }

    /// <summary>
    /// El camino de vuelta. Devuelve <c>null</c> si el texto no es Base32 válido, para que quien
    /// llama pueda decir «ese secreto no sirve» en vez de operar con bytes inventados.
    ///
    /// <para>Es TOLERANTE con lo que teclea una persona: acepta minúsculas, espacios, guiones y el
    /// relleno <c>=</c>. Esa tolerancia no debilita nada —el secreto sigue siendo el mismo— y evita
    /// el rechazo más frustrante que existe, el de un valor que está bien escrito pero con un
    /// espacio de más al copiarlo.</para>
    /// </summary>
    public static byte[]? Decodificar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var bytes = new List<byte>(texto.Length * 5 / 8 + 1);
        int acumulador = 0, bitsPendientes = 0;

        foreach (var c in texto)
        {
            // Separadores de cortesía y relleno: se ignoran, no invalidan.
            if (c is ' ' or '-' or '=' or '\t' or '\r' or '\n') continue;

            int valor = Alfabeto.IndexOf(char.ToUpperInvariant(c));
            if (valor < 0) return null;   // un carácter que no es del alfabeto sí invalida.

            acumulador = (acumulador << 5) | valor;
            bitsPendientes += 5;

            if (bitsPendientes >= 8)
            {
                bitsPendientes -= 8;
                bytes.Add((byte)((acumulador >> bitsPendientes) & 0xFF));
            }
        }

        // Los bits sobrantes (menos de 8) son el relleno de la codificación y se descartan: no son
        // datos. Un texto que no produjo ni un byte no es un secreto.
        return bytes.Count == 0 ? null : [.. bytes];
    }
}
