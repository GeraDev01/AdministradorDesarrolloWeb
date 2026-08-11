using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La única consulta de las pantallas de ausencias que mira datos de OTRAS personas: quién más del
/// equipo estará fuera en unas fechas.
///
/// <para><b>Está en su propio archivo y no dentro de <see cref="AusenciasEndpoints"/></b> por lo
/// mismo que su servicio está en su propia clase: allí la regla de todo el grupo es «ninguna ruta
/// toca datos de otro», escrita en su cabecera y sostenida por que ninguna lleva identificador de
/// persona. Ésta sí lee lo ajeno —por eso lo dice el nombre del archivo— y la regla de allí sigue
/// siendo cierta sin excepciones que haya que recordar al leerla.</para>
///
/// <para>La política es la misma del autoservicio: quien puede pedir vacaciones puede saber si su
/// equipo ya estará fuera esa semana. Lo que se devuelve son nombres y fechas de vacaciones ya
/// APROBADAS y nada más — ver <c>CompaneroFueraDto</c>, donde está por escrito qué se deja fuera y
/// por qué.</para>
/// </summary>
public static class AusenciasDelEquipoEndpoints
{
    public static void MapAusenciasDelEquipoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ausencias/equipo")
            .WithTags("Ausencias")
            .RequireAuthorization("AdminUDesarrollador");

        // Las fechas viajan en la CADENA DE CONSULTA y no en un cuerpo, porque esto es una lectura y
        // se repite cada vez que alguien cambia una fecha en el asistente: así el navegador puede
        // reusar la respuesta y la ruta se puede abrir a mano para comprobarla.
        //
        // Un rango absurdo no se rechaza con un 400: el servicio lo normaliza —lo voltea si viene al
        // revés y lo acorta si es enorme— y contesta sobre lo que sí se puede contestar. Es una ayuda
        // de pantalla; un error aquí dejaría el paso del asistente en blanco sin decir qué hacer.
        grupo.MapGet("/fuera", async (
            DateTime inicio, DateTime fin, AusenciasDelEquipoService equipo, CancellationToken ct) =>
                Results.Ok(await equipo.QuienEstaraFueraAsync(inicio, fin, ct)))
        .WithSummary("Compañeros con vacaciones aprobadas que se solapan con un rango de fechas");
    }
}
