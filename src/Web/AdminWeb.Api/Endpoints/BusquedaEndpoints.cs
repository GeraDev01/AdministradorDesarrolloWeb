using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos.Busqueda;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La búsqueda global: requerimientos, tickets de DevOps, desarrolladores, sugerencias y plantillas
/// en una sola caja.
///
/// <para>El servicio estaba portado y registrado desde hace tiempo, pero <b>sin ninguna ruta que lo
/// llamara</b>: era código muerto que además mentía, porque su comentario seguía anunciando el
/// Ctrl+K del escritorio. Esto es lo que faltaba para que exista de verdad.</para>
///
/// <para>SoloAdmin, igual que en el escritorio: la búsqueda cruza requerimientos, tickets y fichas
/// de personas, y devolver eso a cualquiera saltándose el filtro por rol de cada pantalla sería una
/// puerta lateral a datos que su dueño no ve.</para>
/// </summary>
public static class BusquedaEndpoints
{
    public static void MapBusquedaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/busqueda", async (
            string? q, SearchService busqueda, CancellationToken ct) =>
        {
            var hits = await busqueda.BuscarAsync(q, ct: ct);
            return Results.Ok(hits.Select(ARuta).ToList());
        })
        .WithTags("Búsqueda")
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Busca en requerimientos, tickets, personas, sugerencias y plantillas");
    }

    /// <summary>
    /// Traduce el resultado a algo que el navegador pueda usar: el tipo en palabras y la URL de la
    /// web.
    ///
    /// <para>La clave que devuelve el servicio es la del ESCRITORIO —allí era la llave de un
    /// diccionario de controles— y no coincide con las rutas de aquí. La traducción vive en este
    /// borde y no en el cliente a propósito: si el cliente tuviera el mapa, añadir una pantalla
    /// obligaría a acordarse de tocarlo, y ese es exactamente el sitio donde no se acordaría
    /// nadie.</para>
    ///
    /// <para>Una clave desconocida NO se inventa: se manda al inicio. Un enlace roto es peor que
    /// uno que lleva a un sitio útil, porque el roto parece un fallo de la aplicación.</para>
    /// </summary>
    private static ResultadoDeBusquedaDto ARuta(SearchHit h) => new(
        Tipo: h.Kind switch
        {
            SearchKind.Requerimiento => "Requerimiento",
            SearchKind.Ticket        => "Ticket de DevOps",
            SearchKind.Desarrollador => "Desarrollador",
            SearchKind.Sugerencia    => "Sugerencia",
            SearchKind.Plantilla     => "Plantilla",
            SearchKind.Articulo      => "Artículo",
            _                        => h.Kind.ToString()
        },
        h.Texto,
        h.Detalle,
        Ruta: h.NavKey switch
        {
            "requirements"   => "/requerimientos",
            "devops-tickets" => "/devops",
            "developers"     => "/desarrolladores",
            "suggestions"    => "/sugerencias",
            "templates"      => "/plantillas",
            // Los artículos de conocimiento son los únicos que llevan su identificador dentro de la
            // clave, y por eso su ruta se compone en vez de estar en la tabla: buscar un tema y
            // aterrizar en la lista completa de la documentación no sería encontrarlo. La clave la
            // fabrica el servicio con el Id de la fila, así que aquí no hay texto de nadie que
            // pudiera colarse en la dirección.
            _ when h.NavKey.StartsWith("conocimiento/", StringComparison.Ordinal) => "/" + h.NavKey,
            _                => "/"
        });
}
