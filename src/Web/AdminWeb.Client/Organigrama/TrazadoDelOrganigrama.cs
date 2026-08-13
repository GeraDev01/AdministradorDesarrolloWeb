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
/// <see cref="TrazadoDelOrganigrama.Trazar"/>.</param>
public readonly record struct CajaPorTrazar(int Id, int Nivel);

/// <summary>
/// Qué se dibuja en UNA columna de sangría, a la izquierda de una caja. Son las cuatro piezas con las
/// que se arma cualquier árbol con sangrías:
/// <list type="bullet">
///   <item><see cref="Ninguna"/>: hueco. La rama de ese antepasado ya se cerró más arriba.</item>
///   <item><see cref="Linea"/>: la vertical que pasa de largo, porque a ese antepasado todavía le
///   quedan hermanos más abajo.</item>
///   <item><see cref="Codo"/>: la que entra en esta caja y sigue bajando: tiene hermanos debajo.</item>
///   <item><see cref="CodoFinal"/>: la que entra en esta caja y se acaba: es la última de su rama.</item>
/// </list>
/// </summary>
public enum GuiaDelArbol { Ninguna, Linea, Codo, CodoFinal }

/// <summary>
/// Una caja ya colocada: qué sangría le toca, qué líneas la unen a su rama y qué está tapando.
/// </summary>
/// <param name="Guias">Una por columna de sangría, de fuera hacia dentro. Tantas como el nivel, así
/// que las cajas de arriba del todo no llevan ninguna.</param>
/// <param name="TieneRama">Si le cuelga algo. Es lo que decide que la caja pueda plegarse.</param>
/// <param name="Escondidos">Cuántas cajas se está tragando por estar plegada, con sus nietos dentro.
/// Cero si no lo está. Se dice en la caja con todas sus letras: una rama que desaparece sin avisar se
/// lee como equipos que ya no existen.</param>
public sealed record CajaTrazada(
    int Id, int Nivel, IReadOnlyList<GuiaDelArbol> Guias, bool TieneRama, bool Plegada, int Escondidos);

/// <summary>
/// El dibujo del organigrama en pantalla: dónde cae cada caja y qué soltadas puede aceptar.
///
/// <para><b>Por qué está aquí y no dentro de la pantalla.</b> Son cuentas puras —entra una lista de
/// equipos, sale una lista de cajas— y son justo las que no se comprueban abriendo el navegador: que
/// un árbol de cuatro niveles salga con sus líneas donde toca, y que soltar un equipo dentro de su
/// propio subequipo se rechace ANTES de escribir nada. Dentro del <c>@code</c> de la pantalla serían
/// métodos privados que ninguna prueba alcanza.</para>
///
/// <para><b>Y por qué el dibujo es un árbol con sangrías y no el reparto en filas de antes.</b> Hasta
/// que hubo subequipos, las cajas se repartían en filas de dos, tres o cuatro columnas por el ancho
/// del lienzo, con un espinazo a la izquierda: era una cuadrícula, y las filas no significaban nada
/// —de quién colgaba cada equipo lo decía un renglón escrito DENTRO de la caja—. Con jerarquía de
/// verdad eso ya no vale: la posición tiene que decir de quién cuelga cada uno, o el dibujo contradice
/// al dato. De las dos formas clásicas de dibujar un árbol se eligió la de sangrías y no la de cajas
/// colgando en horizontal, por lo que le pasa a cada una cuando el árbol se deforma:</para>
/// <list type="bullet">
///   <item><b>Ancho y plano</b> —lo de hoy: veinte equipos sin padre— en horizontal ocupa veinte
///   anchos de caja y obliga a arrastrar la barra de abajo para leer el diagrama entero. Con sangrías
///   crece hacia ABAJO, que es la dirección en la que una página ya sabe desplazarse.</item>
///   <item><b>Hondo y estrecho</b> —una cadena de seis— en horizontal es un dibujo casi vacío con seis
///   renglones aprovechados; con sangrías son seis renglones seguidos, cada nivel cuesta una sangría
///   fija y lo que sobre por la derecha lo recoge el desplazamiento del contenedor.</item>
/// </list>
///
/// <para>Y hay una tercera razón, que es la que manda ahora que el diagrama se edita: en un árbol con
/// sangrías las cajas son HERMANAS en el marcado, nunca una dentro de otra. Anidadas, soltar algo en
/// una caja lo soltaría también en todas las que la contienen —los eventos suben—, y habría que ir
/// cortando la propagación caja por caja para que una persona no acabara además en el equipo de más
/// arriba.</para>
/// </summary>
public static class TrazadoDelOrganigrama
{
    /// <summary>
    /// Coloca las cajas: sangría, líneas y ramas plegadas.
    ///
    /// <para>Las cajas entran <b>en orden de dibujo</b> —cada padre delante de su rama—, que es como
    /// las manda el servidor, y de ese orden sale todo lo demás: quién tiene rama, quién tiene
    /// hermanos debajo y qué esconde cada pliegue. Los niveles se toman como vienen con una salvedad:
    /// <b>ninguno puede saltar más de uno respecto al anterior</b>. Con datos sanos eso no recorta
    /// nada; con un círculo escrito a mano contra la base —lo único que puede colar un nivel
    /// imposible— evita que el dibujo se salga de sus propias columnas. Un organigrama torcido se ve
    /// y se arregla; una pantalla que revienta al pintarse, no.</para>
    /// </summary>
    /// <param name="cajas">Las cajas en orden de dibujo, incluidas las que no son equipos.</param>
    /// <param name="plegados">Los identificadores plegados. Plegar una caja esconde su rama ENTERA,
    /// no solo lo que le cuelga directamente.</param>
    public static IReadOnlyList<CajaTrazada> Trazar(
        IReadOnlyList<CajaPorTrazar> cajas, IReadOnlySet<int> plegados)
    {
        int total = cajas.Count;
        if (total == 0) return [];

        // 1. Los niveles, saneados. De aquí en adelante el nivel de cada caja es el de esta lista y
        //    no el que trajo el DTO: lo que sigue da por hecho que la sucesión se puede recorrer.
        var niveles = new int[total];
        int anterior = -1;
        for (int i = 0; i < total; i++)
        {
            niveles[i] = Math.Clamp(cajas[i].Nivel, 0, anterior + 1);
            anterior = niveles[i];
        }

        // 2. Quién tiene rama: en orden de dibujo, tener algo colgando es que el de detrás cuelgue de
        //    ti, porque su rama va justo detrás y empieza un nivel más abajo.
        var tieneRama = new bool[total];
        for (int i = 0; i < total - 1; i++)
            tieneRama[i] = niveles[i + 1] == niveles[i] + 1;

        // 3. Lo que se tragan las cajas plegadas. Basta con recordar UNA: mientras no se vuelva a su
        //    altura, todo lo que pase está debajo de ella, incluidas otras plegadas.
        var visibles = new List<int>(total);
        var escondidos = new int[total];
        int tapando = -1;

        for (int i = 0; i < total; i++)
        {
            if (tapando >= 0 && niveles[i] > niveles[tapando]) { escondidos[tapando]++; continue; }

            tapando = -1;
            visibles.Add(i);
            if (tieneRama[i] && plegados.Contains(cajas[i].Id)) tapando = i;
        }

        // 4. Quién tiene hermanos DEBAJO, contando solo lo que se ve: de eso depende que la vertical
        //    de su columna siga bajando o se corte en el codo. Se mira hacia adelante hasta salir de
        //    la rama — el primero que cuelgue más arriba cierra la pregunta.
        var conHermanoDebajo = new bool[visibles.Count];
        for (int a = 0; a < visibles.Count; a++)
        {
            int nivel = niveles[visibles[a]];
            for (int b = a + 1; b < visibles.Count; b++)
            {
                int otro = niveles[visibles[b]];
                if (otro < nivel) break;
                if (otro == nivel) { conHermanoDebajo[a] = true; break; }
            }
        }

        // 5. Las guías. La columna de más adentro es el codo de la propia caja; las de fuera son las
        //    verticales de sus antepasados, y cada una se dibuja solo si a aquel le quedan hermanos
        //    debajo — si no, su rama ya terminó y por ahí no pasa ninguna línea.
        var porNivel = new List<int>();   // por altura, la posición del antepasado en lo visible
        var trazadas = new List<CajaTrazada>(visibles.Count);

        for (int a = 0; a < visibles.Count; a++)
        {
            int i = visibles[a];
            int nivel = niveles[i];

            if (porNivel.Count > nivel) porNivel.RemoveRange(nivel, porNivel.Count - nivel);
            porNivel.Add(a);   // queda en porNivel[nivel]: el paso 1 garantiza que no hay huecos

            var guias = new GuiaDelArbol[nivel];
            for (int j = 0; j < nivel; j++)
                guias[j] = j == nivel - 1
                    ? conHermanoDebajo[a] ? GuiaDelArbol.Codo : GuiaDelArbol.CodoFinal
                    : conHermanoDebajo[porNivel[j + 1]] ? GuiaDelArbol.Linea : GuiaDelArbol.Ninguna;

            trazadas.Add(new CajaTrazada(
                cajas[i].Id, nivel, guias, tieneRama[i],
                plegados.Contains(cajas[i].Id), escondidos[i]));
        }

        return trazadas;
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
