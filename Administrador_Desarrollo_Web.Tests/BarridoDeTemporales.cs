using System.Runtime.CompilerServices;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Borra las bases SQLite que dejaron las EJECUCIONES ANTERIORES de esta suite.
///
/// <para><b>Hace falta porque nadie borra estos archivos.</b> Cada prueba se fabrica su base en el
/// temporal (<c>TestDb.NewConOpciones</c>, prefijo <c>advtest_</c>) y al terminar el contexto se
/// cierra, pero el archivo se queda. Son cientos por ejecución completa y ninguna limpia, así que
/// crecen sin techo: se midieron 51 149 archivos y 18,3 GB, y el disco de la máquina llegó a cero
/// bytes libres.</para>
///
/// <para>Y el modo en que se manifiesta es lo que obliga a arreglarlo aquí: cuando el disco se
/// llena, la suite NO falla donde está el problema. Revientan cientos de pruebas sin ninguna
/// relación entre sí con <c>SQLite Error 13: database or disk is full</c> lanzado desde el migrador,
/// que señala a cualquier sitio menos al verdadero; y quien lo vea pensará que el cambio que acaba
/// de hacer rompió medio sistema. Es el mismo barrido que ya lleva la suite de la web en
/// <c>src/Web/tests/AdminWeb.Application.Tests/TestSupport.cs</c>, copiado y no reinventado, para que
/// las dos se comporten igual.</para>
///
/// <para><b>Al EMPEZAR y no al terminar.</b> Borrar al terminar exigiría que las ~1 100 pruebas
/// cerraran su contexto —muchas no lo hacen, y SQLite mantiene el archivo tomado mientras viva la
/// conexión—, así que el borrado fallaría justo en los archivos que más importan. Barriendo al
/// principio, la tanda anterior ya murió y sus archivos están sueltos.</para>
///
/// <para><b>Solo lo de hace más de una hora.</b> Es lo que garantiza que jamás se toca un archivo de
/// la tanda en curso, ni aunque haya dos corriendo a la vez (la del escritorio y la de la web, o dos
/// ventanas del mismo). Si alguien baja ese corte, una ejecución puede borrarle la base a otra en
/// pleno uso y aparecerán fallos imposibles de reproducir.</para>
///
/// <para><b>El tope de borrados</b> acota lo que puede tardar el barrido: con un rezago de cientos
/// de miles de archivos, arrasar el temporal de una sentada costaría minutos al arrancar y parecería
/// un cuelgue. Con tope, el rezago se drena en unas cuantas ejecuciones y ninguna se nota.</para>
///
/// <para><b>Va en un inicializador de módulo</b> y no dentro de <c>TestDb</c> a propósito: así corre
/// una sola vez, antes que cualquier prueba —incluida la que se fabrica su propio archivo
/// <c>advtest_</c> sin pasar por <c>TestDb</c>—, y este arreglo no obliga a tocar
/// <c>TestSupport.cs</c>, que es de todos.</para>
/// </summary>
internal static class BarridoDeTemporales
{
    /// <summary>Todo lo que la suite deja en el temporal empieza con esto.</summary>
    private const string Prefijo = "advtest_";

    /// <summary>Techo por ejecución. Ver arriba: es un cortafuegos de tiempo, no de espacio.</summary>
    private const int TopeDeBorrados = 20_000;

    [ModuleInitializer]
    internal static void Barrer()
    {
        // Nada de esto puede tumbar la suite. Un fallo aquí saldría como una excepción del
        // inicializador de módulo en TODAS las pruebas a la vez, que es bastante peor que no barrer:
        // el barrido es higiene, no una regla que haya que hacer cumplir.
        try
        {
            var limite = DateTime.UtcNow.AddHours(-1);
            int borrados = 0;

            // El comodín cubre de paso los acompañantes que SQLite pueda dejar junto a la base
            // (-journal, -wal, -shm) y los .json de las pruebas de columnas, que llevan el mismo
            // prefijo.
            foreach (var archivo in Directory.EnumerateFiles(Path.GetTempPath(), Prefijo + "*"))
            {
                if (borrados >= TopeDeBorrados) break;
                try
                {
                    if (File.GetLastWriteTimeUtc(archivo) > limite) continue;
                    File.Delete(archivo);
                    borrados++;
                }
                catch { /* tomado por otro proceso o ya borrado: no es asunto de esta tanda */ }
            }
        }
        catch { /* sin temporal accesible se sigue igual */ }
    }
}
