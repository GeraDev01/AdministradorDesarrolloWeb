using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AdminWeb.Domain.Security;

/// <summary>Por qué un código no se aceptó. Permite decir la verdad concreta en vez de «código incorrecto».</summary>
public enum MotivoDelCodigo
{
    Correcto = 0,

    /// <summary>No son seis dígitos. Ni se llegó a calcular nada.</summary>
    FormatoInvalido = 1,

    /// <summary>Seis dígitos, pero no son los de este secreto en este momento.</summary>
    NoCoincide = 2,

    /// <summary>
    /// Es un código legítimo, pero YA se usó. Se rechaza a propósito: ver la antirrepetición en
    /// <see cref="Totp.Verificar"/>.
    /// </summary>
    YaSeUso = 3
}

/// <summary>
/// El resultado de comprobar un código.
///
/// <para><b>La ventana viaja de vuelta y no es un detalle interno.</b> Quien acepta el código TIENE
/// que guardarla como «última ventana aceptada» de esa persona; si no lo hace, la antirrepetición
/// deja de existir sin que nada falle a la vista.</para>
/// </summary>
public readonly record struct VerificacionTotp(bool Valido, long Ventana, MotivoDelCodigo Motivo);

/// <summary>
/// El cálculo del código de seis dígitos que muestra la aplicación del teléfono (RFC 6238, TOTP).
///
/// <para><b>Escrito a mano y sin librerías.</b> El algoritmo entero son treinta líneas sobre
/// <c>HMACSHA1</c>, que viene en .NET: un HMAC del número de ventana con el secreto compartido, del
/// que se extraen 31 bits y se toman los últimos seis dígitos. Una dependencia externa para esto
/// añadiría superficie que mantener a cambio de nada.</para>
///
/// <para><b>Por qué SHA-1 y no algo más moderno.</b> Porque es lo que implementan las aplicaciones
/// de códigos del teléfono. El URI <c>otpauth://</c> admite declarar SHA-256, pero en la práctica
/// varias aplicaciones populares ignoran ese parámetro y calculan con SHA-1 de todos modos: el
/// resultado sería que el teléfono muestra un código y el servidor espera otro, sin que nadie
/// entienda por qué. Y aquí SHA-1 no es un riesgo: se usa dentro de un HMAC —construcción que sigue
/// siendo sólida aunque la resistencia a colisiones del hash esté rota— y sobre un valor que caduca
/// en treinta segundos.</para>
///
/// <para><b>Todo aquí es cálculo puro.</b> No hay base de datos, ni reloj propio, ni estado: el
/// instante entra por parámetro. Es lo que permite probar el desfase de reloj y la repetición sin
/// levantar nada.</para>
/// </summary>
public static class Totp
{
    /// <summary>Seis dígitos: lo que muestran todas las aplicaciones de códigos.</summary>
    public const int Digitos = 6;

    /// <summary>10 elevado a <see cref="Digitos"/>. Si se cambian los dígitos, hay que cambiar los dos.</summary>
    private const int Modulo = 1_000_000;

    /// <summary>Formato de relleno con ceros a la izquierda: un código «012345» son seis dígitos, no cinco.</summary>
    private const string FormatoDeDigitos = "D6";

    /// <summary>Treinta segundos por ventana: el valor por omisión del RFC y el que asumen los teléfonos.</summary>
    public const int SegundosPorVentana = 30;

    /// <summary>
    /// Cuántas ventanas de más se aceptan a cada lado de la actual.
    ///
    /// <para><b>Uno, y es obligatorio.</b> El reloj del teléfono se desvía —y el de un teléfono que
    /// lleva días sin sincronizar, bastante—. Sin tolerancia, la aplicación rechaza códigos que la
    /// persona está tecleando bien, y el fallo es de los que no se pueden diagnosticar desde el
    /// otro lado del teléfono: «lo estoy escribiendo tal cual sale».</para>
    ///
    /// <para><b>Y uno como máximo.</b> Cada ventana extra alarga la vida útil de un código robado:
    /// con ±1 un código vale entre 30 y 90 segundos; con ±3 pasaría de tres minutos. Quien mire por
    /// encima del hombro no debe tener margen para ir a otro equipo y teclearlo.</para>
    /// </summary>
    public const int VentanasDeTolerancia = 1;

    /// <summary>
    /// 160 bits de secreto: 20 bytes, que es el tamaño del bloque interno de SHA-1 y lo que
    /// recomienda el RFC 4226. Más corto reduce la seguridad sin ahorrar nada perceptible; más
    /// largo no aporta, porque HMAC-SHA1 lo reduciría igualmente a 160 bits.
    /// </summary>
    public const int BytesDelSecreto = 20;

    /// <summary>Un secreto nuevo, del generador criptográfico del sistema.</summary>
    public static byte[] GenerarSecreto() => RandomNumberGenerator.GetBytes(BytesDelSecreto);

    /// <summary>El secreto nuevo ya en el formato que entiende el teléfono.</summary>
    public static string GenerarSecretoEnBase32() => Base32.Codificar(GenerarSecreto());

    /// <summary>
    /// En qué ventana de treinta segundos cae un instante. Es el contador que entra en el HMAC, y
    /// se cuenta desde el 1 de enero de 1970 porque así lo fija el RFC — el teléfono cuenta igual.
    /// </summary>
    public static long VentanaDe(DateTimeOffset instante) =>
        instante.ToUnixTimeSeconds() / SegundosPorVentana;

    /// <summary>
    /// Segundos que le quedan de vida al código que se muestra ahora. Sirve para que una pantalla
    /// pueda avisar «este código caduca en 4 s, espera al siguiente» en lugar de dejar que la
    /// persona lo teclee justo cuando cambia.
    /// </summary>
    public static int SegundosQueLeQuedanAlCodigo(DateTimeOffset instante) =>
        SegundosPorVentana - (int)(instante.ToUnixTimeSeconds() % SegundosPorVentana);

    /// <summary>
    /// El código de seis dígitos de un secreto para una ventana concreta. Es el mismo cálculo que
    /// hace el teléfono; si los dos parten del mismo secreto y del mismo número de ventana, sale el
    /// mismo número.
    /// </summary>
    public static string Calcular(byte[] secreto, long ventana)
    {
        ArgumentNullException.ThrowIfNull(secreto);

        // El contador va en 8 bytes y con el byte más significativo primero. El orden es parte del
        // formato: invertirlo produce códigos que no coinciden con los de ningún teléfono.
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, ventana);

        Span<byte> firma = stackalloc byte[20];   // HMAC-SHA1 siempre produce 20 bytes.
        HMACSHA1.HashData(secreto, contador, firma);

        // «Truncamiento dinámico» del RFC 4226: los cuatro bits bajos del último byte dicen desde
        // qué posición leer los cuatro bytes que forman el número. Se hace así —y no tomando
        // siempre los primeros— para que el código no dependa de una parte fija de la firma.
        int desplazamiento = firma[^1] & 0x0F;

        // El bit más alto se apaga (0x7F) para que el número no salga negativo al interpretarse
        // con signo. Es una rareza heredada de Java que está EN el estándar: quitarla desalinearía
        // el resultado con el de los teléfonos.
        int numero = ((firma[desplazamiento] & 0x7F) << 24)
                   | (firma[desplazamiento + 1] << 16)
                   | (firma[desplazamiento + 2] << 8)
                   | firma[desplazamiento + 3];

        return (numero % Modulo).ToString(FormatoDeDigitos);
    }

    /// <summary>
    /// Comprueba un código tecleado contra un secreto, con tolerancia de reloj y ANTIRREPETICIÓN.
    ///
    /// <para><b>La antirrepetición no es opcional y por eso el parámetro no tiene valor por
    /// omisión.</b> Un código vive hasta noventa segundos por la tolerancia de reloj; sin esto,
    /// quien lo vea —por encima del hombro, en una captura de pantalla compartida, en el historial
    /// de un chat donde alguien lo pegó por error— tiene ese minuto y medio para entrar con él.
    /// Guardando la última ventana aceptada, el segundo uso del mismo código se rechaza aunque el
    /// código siga siendo matemáticamente correcto. Quien llama DEBE persistir
    /// <see cref="VerificacionTotp.Ventana"/> cuando el resultado sea válido; si no lo hace, esto
    /// no protege nada y nada falla a la vista.</para>
    ///
    /// <para><b>Consecuencia aceptada:</b> quien se equivoca al teclear y vuelve a intentar dentro
    /// de los mismos treinta segundos tiene que esperar al código siguiente. Es un inconveniente de
    /// segundos frente a un acceso ajeno.</para>
    /// </summary>
    /// <param name="ultimaVentanaAceptada">
    /// La última ventana que esta cuenta ya usó, o <c>null</c> si nunca ha usado ninguna (primer
    /// código de su vida). Cualquier ventana igual o anterior a esta se rechaza.
    /// </param>
    public static VerificacionTotp Verificar(
        byte[]? secreto, string? codigoTecleado, long? ultimaVentanaAceptada, DateTimeOffset ahora)
    {
        var codigo = NormalizarCodigo(codigoTecleado);
        if (secreto is null || secreto.Length == 0 || codigo is null)
            return new VerificacionTotp(false, 0, MotivoDelCodigo.FormatoInvalido);

        long actual = VentanaDe(ahora);

        // De la más vieja a la más nueva. El orden no cambia el resultado —como mucho una ventana
        // coincide— pero deja el recorrido en el sentido en que se lee la tolerancia.
        for (long ventana = actual - VentanasDeTolerancia; ventana <= actual + VentanasDeTolerancia; ventana++)
        {
            if (!SonIguales(Calcular(secreto, ventana), codigo)) continue;

            if (ultimaVentanaAceptada is long ultima && ventana <= ultima)
                return new VerificacionTotp(false, ventana, MotivoDelCodigo.YaSeUso);

            return new VerificacionTotp(true, ventana, MotivoDelCodigo.Correcto);
        }

        return new VerificacionTotp(false, 0, MotivoDelCodigo.NoCoincide);
    }

    /// <summary>
    /// Deja el código en seis dígitos limpios, o <c>null</c> si no lo es.
    ///
    /// Se admiten espacios y guiones porque las aplicaciones del teléfono muestran el código
    /// separado en dos grupos de tres y quien lo copia se lleva el espacio dentro. Rechazar por eso
    /// sería rechazar un código correcto.
    /// </summary>
    private static string? NormalizarCodigo(string? tecleado)
    {
        if (string.IsNullOrWhiteSpace(tecleado)) return null;

        var limpio = new StringBuilder(Digitos);
        foreach (var c in tecleado)
        {
            if (c is ' ' or '-' or '\t') continue;
            if (!char.IsAsciiDigit(c)) return null;
            if (limpio.Length == Digitos) return null;   // más dígitos de la cuenta: no es un código.
            limpio.Append(c);
        }

        return limpio.Length == Digitos ? limpio.ToString() : null;
    }

    /// <summary>
    /// Comparación en tiempo constante.
    ///
    /// Comparar con <c>==</c> corta en el primer carácter distinto, y ese tiempo es medible: con
    /// suficientes intentos permite averiguar el código dígito a dígito en vez de tener que
    /// acertarlo entero. Sobre seis dígitos el ataque es rebuscado, pero la alternativa correcta no
    /// cuesta nada.
    /// </summary>
    private static bool SonIguales(string esperado, string tecleado) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(esperado), Encoding.ASCII.GetBytes(tecleado));
}
