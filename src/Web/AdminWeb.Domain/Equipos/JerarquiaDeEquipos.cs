using AdminWeb.Domain.Entities;

namespace AdminWeb.Domain.Equipos;

/// <summary>
/// El árbol de equipos, resuelto UNA vez y en memoria: quién cuelga de quién, qué hay debajo de un
/// equipo, en qué orden se dibuja y qué padre convertiría la jerarquía en un círculo.
///
/// <para><b>Por qué en memoria y no con una consulta recursiva.</b> «Este equipo y todo lo que cuelga
/// de él» se pide en varios sitios —el organigrama, el PDF, el ranking, el cruce de ausencias— y en
/// SQL Server eso se escribe con un CTE recursivo, que es fácil de escribir mal: sin tope de
/// profundidad, un padre mal grabado lo convierte en un bucle que el motor corta a las cien vueltas
/// con un error, y con datos buenos sigue costando una consulta por cada sitio que pregunte. La
/// tabla de equipos son unas decenas de filas —una por equipo de la empresa—, así que traer
/// <c>(Id, EquipoPadreId)</c> de golpe y recorrerlo aquí es una sola lectura y ningún SQL que
/// mantener en dos dialectos.</para>
///
/// <para><b>Aguanta datos rotos.</b> Un padre que apunta a un equipo que ya no está se trata como
/// raíz, y un ciclo escrito desde fuera de la aplicación —a mano contra la base, que es la única
/// forma de que entre uno— no cuelga ningún recorrido: todos llevan su conjunto de visitados. Un
/// organigrama que se queda dando vueltas no es un error que alguien pueda diagnosticar; es una
/// pantalla que no carga.</para>
///
/// <para><b>Esto NO decide permisos.</b> El árbol dice cómo se agrupa y se dibuja la organización;
/// quién puede hacer qué lo sigue diciendo el rol de la cuenta. Ver <see cref="Team.EquipoPadreId"/>.</para>
/// </summary>
public sealed class JerarquiaDeEquipos
{
    /// <summary>De quién cuelga cada equipo. La clave es también el censo: lo que no está aquí, no existe.</summary>
    private readonly Dictionary<int, int?> _padreDe;

    /// <summary>Los hijos DIRECTOS de cada equipo, en el orden en que llegaron.</summary>
    private readonly Dictionary<int, List<int>> _hijosDe;

    /// <summary>Los equipos sin padre, en el orden en que llegaron.</summary>
    private readonly List<int> _raices;

    private JerarquiaDeEquipos(Dictionary<int, int?> padreDe, Dictionary<int, List<int>> hijosDe, List<int> raices)
    {
        _padreDe = padreDe;
        _hijosDe = hijosDe;
        _raices  = raices;
    }

    /// <summary>
    /// Arma la jerarquía a partir de los pares (equipo, su padre).
    ///
    /// <para>El ORDEN de entrada se conserva entre hermanos, y eso importa: quien llama trae los
    /// equipos ordenados por nombre y espera que el organigrama salga alfabético dentro de cada
    /// rama. Reordenar aquí obligaría a este tipo a conocer los nombres, que no le incumben.</para>
    /// </summary>
    public static JerarquiaDeEquipos De(IEnumerable<(int Id, int? PadreId)> equipos)
    {
        var padreDe = new Dictionary<int, int?>();
        var orden = new List<int>();

        foreach (var (id, padreId) in equipos)
        {
            // Un identificador repetido se queda con el primero. No debería llegar nunca —es la
            // clave de la tabla—, pero la alternativa es que el diccionario lance en mitad de un
            // organigrama por un dato que no depende de quien lo está mirando.
            if (padreDe.ContainsKey(id)) continue;
            padreDe[id] = padreId;
            orden.Add(id);
        }

        var hijosDe = new Dictionary<int, List<int>>();
        var raices = new List<int>();

        foreach (var id in orden)
        {
            var padre = padreDe[id];

            // Sin padre, con un padre que ya no existe, o apuntándose a sí mismo: raíz. Los dos
            // últimos casos no los puede crear la aplicación, pero un equipo que desapareciera de
            // aquí desaparecería del organigrama, y en un organigrama faltar no es una fila menos:
            // es un equipo que oficialmente no está en ninguna parte.
            if (padre is not int padreId || padreId == id || !padreDe.ContainsKey(padreId))
            {
                raices.Add(id);
                continue;
            }

            if (!hijosDe.TryGetValue(padreId, out var hijos))
                hijosDe[padreId] = hijos = [];
            hijos.Add(id);
        }

        return new JerarquiaDeEquipos(padreDe, hijosDe, raices);
    }

    /// <summary>La misma jerarquía, leyendo las entidades. Atajo para quien ya tiene los equipos cargados.</summary>
    public static JerarquiaDeEquipos De(IEnumerable<Team> equipos) =>
        De(equipos.Select(t => (t.Id, t.EquipoPadreId)));

    /// <summary>¿Este equipo tiene subequipos colgando directamente?</summary>
    public bool TieneSubequipos(int equipoId) => _hijosDe.ContainsKey(equipoId);

    /// <summary>
    /// De qué equipo cuelga, TAL COMO LO VE EL ÁRBOL: nulo si es raíz, y nulo también si su columna
    /// apuntaba a un equipo que ya no está o a sí mismo.
    ///
    /// <para>Es lo que hay que publicarle a quien dibuje: si se le pasara el valor crudo de la fila,
    /// buscaría una caja que no está en la lista y el equipo se quedaría sin dibujar.</para>
    /// </summary>
    public int? PadreDe(int equipoId) =>
        PadreCrudo(equipoId) is int padreId && padreId != equipoId && _padreDe.ContainsKey(padreId)
            ? padreId
            : null;

    /// <summary>Lo que dice la columna, sin interpretar. Solo para las comprobaciones de ciclos.</summary>
    private int? PadreCrudo(int equipoId) => _padreDe.TryGetValue(equipoId, out var padre) ? padre : null;

    /// <summary>Los subequipos DIRECTOS, en orden. Lo que se publica es el subárbol; esto es su paso.</summary>
    private IReadOnlyList<int> Hijos(int equipoId) =>
        _hijosDe.TryGetValue(equipoId, out var hijos) ? hijos : [];

    /// <summary>
    /// El equipo Y TODO lo que cuelga de él, de arriba abajo y con los hermanos en su orden.
    ///
    /// <para>El primero de la lista es siempre el equipo por el que se preguntó, así que
    /// <c>Subarbol(x).Count == 1</c> es «no tiene nada debajo». Un equipo desconocido devuelve la
    /// lista vacía, no una con él dentro: inventarlo haría que un identificador viejo pareciera un
    /// equipo de una sola caja.</para>
    /// </summary>
    public IReadOnlyList<int> Subarbol(int equipoId)
    {
        if (!_padreDe.ContainsKey(equipoId)) return [];

        var rama = new List<int>();
        var visitados = new HashSet<int>();

        // Con pila explícita y no con recursión: la profundidad la decide quien captura equipos, no
        // este código, y una cadena larga —o un ciclo grabado a mano— no debe tumbar el proceso
        // entero con un desbordamiento de pila. Los hijos se apilan al revés para que salgan en su
        // orden.
        var pendientes = new Stack<int>();
        pendientes.Push(equipoId);

        while (pendientes.Count > 0)
        {
            var actual = pendientes.Pop();
            if (!visitados.Add(actual)) continue;
            rama.Add(actual);

            var hijos = Hijos(actual);
            for (int i = hijos.Count - 1; i >= 0; i--) pendientes.Push(hijos[i]);
        }

        return rama;
    }

    /// <summary>
    /// La cadena de mando hacia arriba: el padre, el abuelo… SIN el propio equipo, y el más cercano
    /// primero. Vacía si es raíz o si no existe.
    /// </summary>
    public IReadOnlyList<int> Ancestros(int equipoId)
    {
        var cadena = new List<int>();
        var visitados = new HashSet<int> { equipoId };

        var actual = equipoId;
        while (PadreDe(actual) is int padreId && visitados.Add(padreId))
        {
            cadena.Add(padreId);
            actual = padreId;
        }

        return cadena;
    }

    /// <summary>A qué altura cuelga: 0 los equipos raíz, 1 sus subequipos, y así. Un desconocido es 0.</summary>
    public int Nivel(int equipoId) => Ancestros(equipoId).Count;

    /// <summary>
    /// El equipo del que cuelga todo lo demás en esa línea: el ancestro más alto, o el propio equipo
    /// si ya es raíz. Uno desconocido es su propia raíz.
    /// </summary>
    public int Raiz(int equipoId)
    {
        var arriba = Ancestros(equipoId);
        return arriba.Count == 0 ? equipoId : arriba[^1];
    }

    /// <summary>
    /// ¿Cuelgan los dos del MISMO EQUIPO RAÍZ? Es la regla de «es de mi equipo» ahora que hay
    /// subequipos, e incluye a los HERMANOS: dos subequipos distintos de «Desarrollo Web» son el
    /// mismo equipo grande.
    ///
    /// <para>Antes esto era «la misma rama» —uno cuelga del otro— y dejaba a los hermanos fuera con
    /// el argumento de que marcarlos convertiría la señal en «casi todo el mundo». Es una decisión
    /// de negocio y el dueño la tomó al revés, con un motivo que gana al argumento: el paraguas
    /// existe justamente para que el área se reconozca entre sí. Con la rama, dos personas de
    /// subequipos hermanos no se veían como compañeras aunque tuvieran el mismo jefe de área, que
    /// es lo contrario de para lo que se creó el equipo padre.</para>
    ///
    /// <para>Entre equipos sin padre —lo de antes de los subequipos— esto sigue siendo exactamente la
    /// igualdad de siempre: cada uno es su propia raíz.</para>
    /// </summary>
    public bool MismaRaiz(int unEquipo, int otroEquipo) =>
        Raiz(unEquipo) == Raiz(otroEquipo);

    /// <summary>
    /// Todos los equipos en el orden en que se dibujan: cada padre antes que su rama, y los
    /// hermanos en el orden en que llegaron.
    ///
    /// <para>Los dos dibujantes —la pantalla y el PDF— reciben la lista ya en este orden para que no
    /// tengan que ordenarla cada uno por su cuenta; ahí es donde el papel empezaría a contradecir a
    /// la pantalla.</para>
    ///
    /// <para>Al final se añade lo que no colgaba de ninguna raíz, que solo puede ser un ciclo escrito
    /// contra la base a mano. Sale descolocado, pero SALE: perder un equipo del organigrama es peor
    /// que dibujarlo donde no toca, y así además se ve que algo está mal.</para>
    /// </summary>
    public IReadOnlyList<int> EnOrdenDeDibujo()
    {
        var orden = new List<int>(_padreDe.Count);
        var visitados = new HashSet<int>();

        foreach (var raiz in _raices)
            foreach (var id in Subarbol(raiz))
                if (visitados.Add(id))
                    orden.Add(id);

        foreach (var id in _padreDe.Keys)
            if (visitados.Add(id))
                orden.Add(id);

        return orden;
    }

    /// <summary>
    /// ¿Colgar <paramref name="equipoId"/> de <paramref name="padrePropuesto"/> dejaría un círculo?
    ///
    /// <para>Lo es si el padre propuesto es el propio equipo o si está DEBAJO de él, por muchos
    /// eslabones que haya en medio: «A es padre de A» es el caso que se ve a la primera, y
    /// A→B→C→D→A es el que se cuela. Un ciclo no es un dibujo raro: es un organigrama que no se
    /// puede recorrer y una rama que no se puede sumar.</para>
    /// </summary>
    public bool SeriaCiclo(int equipoId, int? padrePropuesto)
    {
        if (padrePropuesto is not int padreId) return false;   // sin padre, no hay círculo posible
        return padreId == equipoId || Subarbol(equipoId).Contains(padreId);
    }

    /// <summary>
    /// ¿Este equipo acabó colgando de sí mismo, directa o indirectamente?
    ///
    /// <para>Es la comprobación de DESPUÉS de guardar: <see cref="SeriaCiclo"/> mira el árbol de antes
    /// de escribir, y dos personas guardando a la vez pueden pasar las dos por esa puerta con cambios
    /// que por separado son válidos y juntos no. Esto se pregunta con lo que quedó escrito de verdad.</para>
    ///
    /// <para>Sube por el valor CRUDO de la columna y no por <see cref="PadreDe"/>: aquel disimula el
    /// «A cuelga de A» para que el dibujo no se rompa, y eso es exactamente lo que aquí hay que
    /// descubrir.</para>
    /// </summary>
    public bool EsSuPropioAncestro(int equipoId)
    {
        var visitados = new HashSet<int>();
        var actual = equipoId;

        while (PadreCrudo(actual) is int padreId && _padreDe.ContainsKey(padreId))
        {
            if (padreId == equipoId) return true;
            if (!visitados.Add(padreId)) return false;   // un círculo que no pasa por él: no es suyo
            actual = padreId;
        }

        return false;
    }
}
