using AdminWeb.Shared.Dtos.Personas;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// El árbol de equipos visto desde el NAVEGADOR: qué equipos se le pueden ofrecer a alguien como
/// «de quién cuelga este» sin ofrecerle un círculo.
///
/// <para><b>Esto no es una regla, es una comodidad</b>, y la diferencia importa. La regla —que un
/// equipo no puede acabar colgando de sí mismo por larga que sea la cadena— vive en el servidor,
/// dentro de <c>PersonasQueryService.GuardarEquipoAsync</c>, porque a esa dirección se la puede
/// llamar sin pasar por esta pantalla. Lo que se hace aquí es no enseñar una puerta que está
/// cerrada.</para>
///
/// <para><b>Por qué no reutiliza el recorrido del servidor.</b> El del servidor está en
/// <c>AdminWeb.Domain</c> y este proyecto solo referencia a <c>AdminWeb.Shared</c>: es una frontera
/// puesta a propósito, porque una referencia a Domain haría que el navegador descargara el modelo de
/// datos entero. Así que aquí se recorre lo único que el navegador tiene, que es la lista de equipos
/// que llegó en la respuesta.</para>
///
/// <para>Vive en un archivo aparte y no dentro del <c>@code</c> de la pantalla por una razón
/// concreta: así se puede probar. Un filtro escrito dentro del marcado solo se comprueba abriendo la
/// pantalla y mirándola, y esta es justo la clase de regla —«ni él ni sus nietos»— que se rompe sin
/// que se note.</para>
/// </summary>
public static class ArbolDeEquipos
{
    /// <summary>
    /// Una opción del desplegable de «cuelga de»: el equipo y cómo se escribe en la lista.
    /// </summary>
    /// <param name="Etiqueta">El nombre con una sangría por cada nivel. Sin ella, la lista sería
    /// plana y no habría forma de ver que el equipo que estás a punto de elegir ya cuelga de otro —o
    /// sea, que estás metiendo un tercer nivel sin querer.</param>
    public sealed record OpcionDePadre(int Id, string Etiqueta);

    /// <summary>La sangría de cada nivel. Un punto medio y un espacio: se ve, y no lo colapsa el HTML.</summary>
    private const string Sangria = "· ";

    /// <summary>
    /// Los equipos que se le pueden ofrecer a <paramref name="equipoEnEdicion"/> como padre: todos
    /// menos ÉL MISMO y menos los que cuelgan de él, por muchos eslabones que haya en medio.
    ///
    /// <para>Con un equipo NUEVO (<paramref name="equipoEnEdicion"/> nulo) no hay nada que descartar:
    /// todavía no existe, así que nada puede colgar de él.</para>
    ///
    /// <para>Los equipos llegan del servidor ya en orden de dibujo —cada padre delante de su rama—,
    /// y ese orden se conserva: quitar de en medio una rama entera no descoloca a los demás, porque
    /// lo que se quita es un bloque contiguo. Y la lista que queda sigue siendo un árbol completo:
    /// si un equipo no es descendiente del que se edita, su padre tampoco lo es, así que ninguna
    /// sangría se queda sin la caja de la que cuelga.</para>
    /// </summary>
    public static IReadOnlyList<OpcionDePadre> PosiblesPadres(
        IReadOnlyList<EquipoDelOrganigramaDto> equipos, int? equipoEnEdicion) =>
        [.. equipos
            .Where(e => equipoEnEdicion is not int id || (e.Id != id && !CuelgaDe(equipos, e.Id, id)))
            .Select(e => new OpcionDePadre(
                e.Id,
                string.Concat(Enumerable.Repeat(Sangria, Math.Max(e.Nivel, 0))) + e.Nombre))];

    /// <summary>
    /// ¿<paramref name="equipoId"/> cuelga de <paramref name="ancestro"/>, directa o indirectamente?
    ///
    /// <para>Sube por los padres con un conjunto de visitados. Un ciclo no lo puede crear esta
    /// aplicación —el servidor los rechaza—, pero puede llegar escrito a mano contra la base, y
    /// entonces lo que se lleva el golpe es la pantalla: sin el conjunto, este bucle no terminaría
    /// nunca y el navegador se quedaría colgado sin ningún error que leer.</para>
    /// </summary>
    public static bool CuelgaDe(IReadOnlyList<EquipoDelOrganigramaDto> equipos, int equipoId, int ancestro)
    {
        var visitados = new HashSet<int> { equipoId };
        var actual = equipos.FirstOrDefault(e => e.Id == equipoId);

        while (actual?.EquipoPadreId is int padreId && visitados.Add(padreId))
        {
            if (padreId == ancestro) return true;
            actual = equipos.FirstOrDefault(e => e.Id == padreId);
        }

        return false;
    }
}
