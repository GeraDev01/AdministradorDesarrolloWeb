using AdminWeb.Client.Servicios;
using AdminWeb.Shared.Dtos.Personas;

namespace AdminWeb.Client.Organigrama;

/// <summary>
/// Una caja antes de colocarla: quién es y a qué altura cuelga.
/// </summary>
/// <param name="Id">El del equipo. Las dos cajas que no son equipos —la de arriba y la de «sin
/// equipo»— entran con el identificador que les dé la pantalla; para el trazado son una caja más.</param>
/// <param name="Nivel">A qué altura cuelga. Lo calcula el servidor —es una propiedad del ÁRBOL, no
/// del equipo— y aquí se toma como viene, con la única salvedad escrita en
/// <see cref="TrazadoDelOrganigrama.Armar"/>.</param>
public readonly record struct CajaPorTrazar(int Id, int Nivel);

/// <summary>
/// Una caja ya colocada, CON SU RAMA DENTRO. El dibujo es un organigrama clásico —cada caja centrada
/// sobre sus hijos y unida a ellos con líneas—, así que la estructura que lo describe es un árbol y
/// no una lista: quien pinta recorre esto y la colocación sale sola del anidamiento.
/// </summary>
/// <param name="TieneRama">Si le cuelga algo. Es lo que decide que la caja pueda plegarse, y se
/// responde aunque esté plegada: si dependiera de que <see cref="Hijos"/> venga lleno, plegar una
/// caja la dejaría sin botón para volver a desplegarla.</param>
/// <param name="Plegada">Se responde tenga rama o no: una caja sin subequipos pero con gente dentro se
/// pliega igual, y esconde a su gente.</param>
/// <param name="Escondidos">Cuántas CAJAS se está tragando por estar plegada, con sus nietos dentro.
/// Cero si no lo está y cero también si lo que esconde es solo su gente. Se dice en la caja con todas
/// sus letras: una rama que desaparece sin avisar se lee como equipos que ya no existen.</param>
/// <param name="Hijos">Vacío si es una hoja O si está plegada. Los dos casos se distinguen por
/// <see cref="TieneRama"/>.</param>
public sealed record NodoDelOrganigrama(
    int Id, int Nivel, bool TieneRama, bool Plegada, int Escondidos,
    IReadOnlyList<NodoDelOrganigrama> Hijos);

/// <summary>
/// El dibujo del organigrama en pantalla: qué cuelga de qué y qué soltadas puede aceptar cada caja.
///
/// <para><b>Por qué está aquí y no dentro de la pantalla.</b> Son cuentas puras —entra una lista de
/// equipos, sale un árbol— y son justo las que no se comprueban abriendo el navegador: que un árbol de
/// cuatro niveles se arme bien, que plegar una caja se trague su rama entera, y que soltar un equipo
/// dentro de su propio subequipo se rechace ANTES de escribir nada. Dentro del <c>@code</c> de la
/// pantalla serían métodos privados que ninguna prueba alcanza.</para>
///
/// <para><b>El dibujo es un ORGANIGRAMA CLÁSICO</b>: la caja arriba, sus hijos debajo en una fila, y
/// líneas uniéndolos. Antes fue una cuadrícula por el ancho del lienzo —cuando no había jerarquía y
/// las filas no significaban nada— y después un árbol con sangrías, tipo explorador de archivos. Lo
/// eligió el dueño, y lo que se gana es que la forma del dibujo ES la forma de la organización: se
/// entiende de un vistazo quién cuelga de quién sin leer un solo renglón.</para>
///
/// <para><b>Lo que cuesta, dicho sin adornos.</b> En horizontal, veinte equipos raíz ocupan veinte
/// anchos de caja y hay que desplazar el lienzo de lado para leerlos; con sangrías crecían hacia
/// abajo, que es la dirección en la que una página ya sabe moverse. Es el precio de que la posición
/// signifique algo, y por eso la pantalla conserva el selector de tamaño y el plegado: son las dos
/// herramientas con las que un árbol ancho se vuelve manejable.</para>
///
/// <para><b>Que esto sea un árbol no obliga a anidar las cajas.</b> En el marcado la rama va JUNTO a
/// la caja de la que cuelga y no dentro de ella, así que dos cajas no se contienen nunca. Importa
/// porque los eventos suben: con las cajas anidadas, soltar a alguien en un subequipo lo soltaría
/// además en su padre y en su abuelo, y habría que ir cortando la propagación caja por caja. El
/// detalle vive en quien pinta, no aquí, pero se apunta porque condiciona cómo se recorre esto.</para>
/// </summary>
public static class TrazadoDelOrganigrama
{
    /// <summary>
    /// Arma el árbol que se va a dibujar.
    ///
    /// <para>Las cajas entran <b>en orden de dibujo</b> —cada padre delante de su rama—, que es como
    /// las manda el servidor, y de ese orden sale el anidamiento. Los niveles se toman como vienen con
    /// una salvedad: <b>ninguno puede saltar más de uno respecto al anterior</b>. Con datos sanos eso
    /// no recorta nada; con un círculo escrito a mano contra la base —lo único que puede colar un
    /// nivel imposible— evita que una caja quede colgando de un padre que no existe en el recorrido.
    /// Un organigrama torcido se ve y se arregla; una pantalla que revienta al pintarse, no.</para>
    /// </summary>
    /// <param name="cajas">Las cajas en orden de dibujo, incluidas las que no son equipos.</param>
    /// <param name="plegados">Los identificadores plegados. Plegar una caja esconde su rama ENTERA,
    /// no solo lo que le cuelga directamente.</param>
    public static IReadOnlyList<NodoDelOrganigrama> Armar(
        IReadOnlyList<CajaPorTrazar> cajas, IReadOnlySet<int> plegados)
    {
        int total = cajas.Count;
        if (total == 0) return [];

        // Los niveles, saneados. De aquí en adelante manda esta lista y no la del DTO: lo que sigue da
        // por hecho que la sucesión se puede recorrer sin huecos.
        var niveles = new int[total];
        int anterior = -1;
        for (int i = 0; i < total; i++)
        {
            niveles[i] = Math.Clamp(cajas[i].Nivel, 0, anterior + 1);
            anterior = niveles[i];
        }

        // Tener rama es que el de detrás cuelgue de ti: su rama va justo detrás y empieza un nivel más
        // abajo. Se calcula antes de plegar nada, porque una caja plegada SIGUE teniendo rama —es lo
        // único que la deja volver a desplegarse—.
        var tieneRama = new bool[total];
        for (int i = 0; i < total - 1; i++)
            tieneRama[i] = niveles[i + 1] == niveles[i] + 1;

        // Cuántas cajas se traga cada plegada: todo lo que venga detrás por debajo de su altura,
        // nietos incluidos. Se cuenta aunque haya otra plegada dentro, porque lo que se esconde de
        // cara a quien mira es la rama entera.
        var escondidos = new int[total];
        for (int i = 0; i < total; i++)
        {
            if (!tieneRama[i] || !plegados.Contains(cajas[i].Id)) continue;
            for (int j = i + 1; j < total && niveles[j] > niveles[i]; j++) escondidos[i]++;
        }

        // El anidamiento, en una sola pasada. `abiertos[n]` es la lista de hijos de la caja que hoy
        // ocupa la altura n; al llegar una caja de altura n se cierra todo lo que hubiera de n para
        // abajo y se cuelga de la de n-1.
        var raices = new List<NodoDelOrganigrama>();
        var abiertos = new List<List<NodoDelOrganigrama>>();

        // Se recorre AL REVÉS porque un nodo necesita a sus hijos ya armados para nacer: los records
        // son inmutables y las ramas van dentro. Hacia atrás, cuando se llega a una caja, todo lo que
        // cuelga de ella ya pasó.
        var porNivel = new Dictionary<int, List<NodoDelOrganigrama>>();

        for (int i = total - 1; i >= 0; i--)
        {
            int nivel = niveles[i];

            // Plegada es plegada, tenga rama o no: una caja sin subequipos pero con gente dentro se
            // pliega para esconder a su gente, y es de las que más se pliegan. Atarlo a que tuviera
            // rama dejaba a esas cajas con el interruptor pulsado y sin efecto ninguno.
            bool plegada = plegados.Contains(cajas[i].Id);

            // Los hijos que se hayan ido acumulando para la altura de debajo son los suyos: nadie más
            // puede reclamarlos, porque hacia atrás el primero que aparece a esa altura es su padre.
            var hijos = !plegada && porNivel.TryGetValue(nivel + 1, out var pendientes)
                ? Enumerable.Reverse(pendientes).ToList()
                : [];
            porNivel.Remove(nivel + 1);

            var nodo = new NodoDelOrganigrama(
                cajas[i].Id, nivel, tieneRama[i], plegada, escondidos[i], hijos);

            if (nivel == 0) raices.Add(nodo);
            else
            {
                if (!porNivel.TryGetValue(nivel, out var hermanos))
                    porNivel[nivel] = hermanos = [];
                hermanos.Add(nodo);
            }
        }

        raices.Reverse();
        return raices;
    }

    /// <summary>Recorre el árbol en el orden en que se dibuja. Para pruebas y para contar.</summary>
    public static IEnumerable<NodoDelOrganigrama> Recorrer(IEnumerable<NodoDelOrganigrama> nodos)
    {
        foreach (var n in nodos)
        {
            yield return n;
            foreach (var h in Recorrer(n.Hijos)) yield return h;
        }
    }

    /// <summary>
    /// ¿Se puede colgar este equipo de aquel? Es lo que decide si la caja acepta la soltada.
    ///
    /// <para>Dice que no en tres casos y los tres por una razón distinta: de sí mismo y de uno de sus
    /// subequipos porque cerrarían el árbol en círculo —que no es un dibujo raro, es un organigrama
    /// que no se puede recorrer—; y de donde YA cuelga porque no hay nada que guardar, y una escritura
    /// que no cambia nada deja su línea en la bitácora igual que las demás.</para>
    ///
    /// <para><b>Esto no es la barrera.</b> La barrera está en el servidor, que rechaza el círculo
    /// mirando lo que hay escrito de verdad y otra vez después de guardar. Esto es para que la caja se
    /// vea imposible ANTES de soltar, en vez de aceptar el gesto y contestar con un error.</para>
    ///
    /// <para>El recorrido hacia arriba no se repite aquí: es el mismo de
    /// <see cref="ArbolDeEquipos.CuelgaDe"/>, el que decide qué ofrece el desplegable «cuelga de» del
    /// editor. Dos formas de contestar «¿cuelga este de aquel?» acabarían contestando distinto, y
    /// entonces el diagrama aceptaría una soltada que el desplegable de al lado no ofrece.</para>
    /// </summary>
    public static bool SePuedeColgar(
        IReadOnlyList<EquipoDelOrganigramaDto> equipos, int equipoId, int? padrePropuesto)
    {
        if (equipos.FirstOrDefault(e => e.Id == equipoId) is not { } suyo) return false;   // pantalla vieja

        if (padrePropuesto is not int padreId) return suyo.EquipoPadreId is not null;   // dejarlo como raíz
        return padreId != equipoId
            && padreId != suyo.EquipoPadreId
            && !ArbolDeEquipos.CuelgaDe(equipos, padreId, equipoId);
    }

    /// <summary>
    /// ¿Tiene sentido soltar a esta persona ahí? Solo si cambia de sitio: mover a alguien al equipo en
    /// el que ya está no es un error, es un gesto sin consecuencia, y el servicio lo contesta con un
    /// «nadie cambió de equipo» que se lee como un fallo. Nulo es «sin equipo», a los dos lados.
    /// </summary>
    public static bool SePuedeMover(int? equipoActual, int? destino) => equipoActual != destino;
}
