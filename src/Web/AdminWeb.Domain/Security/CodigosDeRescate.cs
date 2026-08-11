using System.Security.Cryptography;
using System.Text;

namespace AdminWeb.Domain.Security;

/// <summary>
/// Los códigos de rescate: la salida cuando el teléfono se pierde, se rompe o se formatea.
///
/// <para><b>Por qué existen.</b> Un segundo factor obligatorio sin plan B convierte un teléfono
/// mojado en una cuenta perdida. Son ocho, se enseñan UNA vez al activar el segundo factor y cada
/// uno sirve para entrar exactamente una vez. La otra salida —que el líder reinicie el segundo
/// factor desde la pantalla de Usuarios— existe también, pero depende de que el líder esté
/// disponible; estos no dependen de nadie.</para>
///
/// <para><b>Se guardan HASHEADOS, nunca en claro.</b> Un código de rescate salta el segundo factor
/// entero: guardarlos legibles sería dejar en la base una llave maestra por persona.</para>
/// </summary>
public static class CodigosDeRescate
{
    /// <summary>Ocho. Suficientes para años de emergencias sin que la hoja impresa sea un inventario.</summary>
    public const int Cuantos = 8;

    /// <summary>
    /// Caracteres por código, sin contar los guiones de cortesía. Dieciséis del alfabeto de abajo
    /// son 16 × 5 = <b>80 bits</b> de aleatoriedad, y ese número es la razón por la que el hash
    /// puede ser rápido — ver <see cref="Hashear"/>.
    /// </summary>
    public const int Caracteres = 16;

    /// <summary>Se muestran en grupos de cuatro, como una clave de producto: se leen y se dictan mejor.</summary>
    public const int CaracteresPorGrupo = 4;

    /// <summary>
    /// Treinta y dos símbolos, <b>sin I, O, 0 ni 1</b>.
    ///
    /// <para>Que sean 32 exactos no es estético: 32 es potencia de dos, así que cinco bits de azar
    /// se convierten en un carácter sin sesgo y sin descartes. Y quitar los cuatro confundibles
    /// importa porque estos códigos se imprimen o se guardan en un papel y se teclean meses después,
    /// cuando ya no hay forma de comprobar si aquello era un uno o una ele.</para>
    /// </summary>
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Ocho códigos nuevos, ya con guiones, listos para enseñarse. Es la ÚNICA vez que existen en
    /// claro: quien los recibe los muestra y los olvida.
    /// </summary>
    public static IReadOnlyList<string> Generar()
    {
        var codigos = new List<string>(Cuantos);
        for (int i = 0; i < Cuantos; i++) codigos.Add(GenerarUno());
        return codigos;
    }

    private static string GenerarUno()
    {
        // Un byte por carácter y se usan sus cinco bits bajos. Como el alfabeto tiene exactamente
        // 32 símbolos, «& 31» reparte por igual: con un alfabeto que no fuera potencia de dos, el
        // resto de la división favorecería a los primeros símbolos y el código tendría menos azar
        // del que aparenta.
        var bytes = RandomNumberGenerator.GetBytes(Caracteres);

        var sb = new StringBuilder(Caracteres + Caracteres / CaracteresPorGrupo);
        for (int i = 0; i < Caracteres; i++)
        {
            if (i > 0 && i % CaracteresPorGrupo == 0) sb.Append('-');
            sb.Append(Alfabeto[bytes[i] & 31]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Deja lo tecleado en su forma canónica —mayúsculas, sin guiones ni espacios— o devuelve
    /// <c>null</c> si no puede ser uno de estos códigos.
    ///
    /// <para>La tolerancia es deliberada: la persona copia el código de un papel o de un archivo y
    /// se trae guiones, espacios o minúsculas. Rechazarlo por eso sería rechazar un código bueno.
    /// Lo que NO se hace es adivinar confundibles —traducir una O por un cero, por ejemplo—: el
    /// alfabeto ya no los contiene, así que una O tecleada es un error de verdad y decirlo es más
    /// honesto que aceptar un código que no era ese.</para>
    /// </summary>
    public static string? Normalizar(string? tecleado)
    {
        if (string.IsNullOrWhiteSpace(tecleado)) return null;

        var sb = new StringBuilder(Caracteres);
        foreach (var c in tecleado)
        {
            if (c is ' ' or '-' or '_' or '\t' or '\r' or '\n') continue;

            char mayuscula = char.ToUpperInvariant(c);
            if (!Alfabeto.Contains(mayuscula)) return null;
            if (sb.Length == Caracteres) return null;   // más largo de la cuenta: no es uno de estos.
            sb.Append(mayuscula);
        }

        return sb.Length == Caracteres ? sb.ToString() : null;
    }

    /// <summary>
    /// El hash que se guarda en la base. Hexadecimal en minúsculas, 64 caracteres siempre.
    ///
    /// <para><b>SHA-256 y NO BCrypt, por dos razones concretas.</b></para>
    ///
    /// <para>La primera es que aquí no hay nada que un hash lento proteja. BCrypt existe para
    /// encarecer la fuerza bruta contra CONTRASEÑAS, que las elige una persona y por tanto viven en
    /// un espacio ridículamente pequeño comparado con su longitud. Estos códigos no los elige nadie:
    /// son 80 bits del generador criptográfico del sistema, o sea del orden de 10²⁴ combinaciones.
    /// Probarlas todas a mil millones de intentos por segundo llevaría decenas de miles de años, y
    /// eso ya suponiendo que el atacante se haya llevado la base entera. Encarecer cada intento no
    /// mueve ese número a ningún sitio útil.</para>
    ///
    /// <para>La segunda es que aquí SÍ tendría un coste. Un código tecleado hay que compararlo
    /// contra los ocho de la persona, porque no se sabe cuál es. Con BCrypt a factor 12 —el que usa
    /// este sistema para las contraseñas, y son ~250 ms por comprobación— cada intento tardaría
    /// alrededor de dos segundos, y eso mientras alguien espera en la pantalla de acceso con el
    /// teléfono roto en la mano. Con SHA-256 se hashea UNA vez lo tecleado y se busca por índice:
    /// una consulta.</para>
    ///
    /// <para><b>Sin sal, también a propósito.</b> La sal sirve contra tablas precalculadas y contra
    /// atacar a muchos usuarios de una vez; las dos cosas presuponen valores que se repiten entre
    /// personas, y dos de estos códigos no coinciden jamás. Lo que de verdad protege aquí es la
    /// aleatoriedad del código, y esa ya está.</para>
    /// </summary>
    public static string Hashear(string codigoNormalizado) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigoNormalizado))).ToLowerInvariant();

    /// <summary>
    /// El hash de lo que alguien tecleó, o <c>null</c> si lo tecleado ni siquiera tiene forma de
    /// código. Es el único camino que debería usarse para buscar en la base: obliga a normalizar
    /// antes de hashear, que es donde se cuela el error de comparar «abcd-efgh» contra «ABCDEFGH».
    /// </summary>
    public static string? HashearLoTecleado(string? tecleado) =>
        Normalizar(tecleado) is string limpio ? Hashear(limpio) : null;
}
