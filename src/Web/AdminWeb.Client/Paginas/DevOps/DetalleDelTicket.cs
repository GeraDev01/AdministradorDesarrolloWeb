using AdminWeb.Client.Componentes;
using AdminWeb.Shared.Dtos.DevOps;

namespace AdminWeb.Client.Paginas.DevOps;

/// <summary>
/// LO QUE ENSEÑA EL CUADRO DE DETALLE DE UN TICKET DE AZURE DEVOPS.
///
/// <para>Vive aquí, en la carpeta de las pantallas de DevOps, por lo mismo que
/// <see cref="EstadosDeDevOps"/>: es la MISMA rejilla del MISMO dato vista por el desarrollador
/// («Mis tickets») y por el líder («Azure DevOps»), y ese mapa de estados está aquí precisamente
/// porque antes eran dos copias y se corregía una y se olvidaba la otra. Un cuadro de detalle escrito
/// dos veces acabaría igual: una pantalla enseñando el área y la otra no, para el mismo ticket.</para>
///
/// <para>Los valores se escriben con los MISMOS formatos que las celdas —<c>0.#</c> para los puntos,
/// <c>0.##</c> para las horas, <c>dd/MM/yyyy</c> para las fechas en hora local—. Formatearlos de otra
/// manera aquí daría dos verdades del mismo dato, y la que se corregiría el día que cambie una sería
/// solo una de las dos.</para>
/// </summary>
public static class DetalleDelTicket
{
    /// <summary>
    /// Un ticket, campo por campo.
    ///
    /// <para>Van también los campos que la rejilla puede tener ESCONDIDOS —área, iteración,
    /// etiquetas—: unos nacen con <c>Visible="false"</c>, otros los quita el selector de columnas y
    /// esa configuración se guarda por persona. El cuadro existe justamente para no depender de eso,
    /// así que los enseña todos siempre.</para>
    ///
    /// <para>Va además la fecha de SINCRONIZACIÓN, que no está en ninguna columna: estas pantallas no
    /// leen Azure DevOps en vivo sino la copia que se trajo la última vez, y quien se para a mirar un
    /// ticket con calma tiene que poder saber de cuándo es lo que está leyendo.</para>
    ///
    /// <para>Lo que las rejillas dicen CON COLOR se escribe aquí con palabras: «sin prioridad» y «sin
    /// estimar» se pintan allá en ámbar, y el color no viaja a un cuadro de texto. Los dos avisos solo
    /// aparecen si el ticket sigue ABIERTO, que es la misma condición que aplican las celdas: en uno
    /// cerrado ya no falta nada.</para>
    /// </summary>
    public static DetalleDeFila De(TicketDevOpsDto t) =>
        new DetalleDeFila("Ticket de Azure DevOps", t.Titulo)
            .Campo("Número", $"#{t.Numero}")
            .Campo("Tipo", t.Tipo)
            .Campo("Estado", t.Estado)
            // La coletilla se AÑADE al número en vez de sustituirlo: «sin prioridad definida» no
            // quiere decir que no tenga —DevOps le pone 2 por omisión a todo lo que se crea—, sino
            // que nadie la ha confirmado desde aquí. Escribir «sin definir» a secas borraría el
            // número que el ticket sí trae.
            .Campo("Prioridad", t.Prioridad
                                + (t.SinPrioridadDefinida && !t.Cerrado
                                       ? " — nadie le ha definido prioridad" : ""))
            .Campo("Asignado a", t.AsignadoA)
            .Campo("Iteración", t.Iteracion)
            .Campo("Área", t.Area)
            .Campo("Puntos", t.Puntos?.ToString("0.#"))
            .Campo("Esfuerzo estimado (h)", t.HorasEstimadas is double horas
                                                ? horas.ToString("0.##")
                                                : t.SinEstimar && !t.Cerrado ? "sin estimar" : null)
            .Campo("Comentarios", $"{t.Comentarios}")
            .Campo("Vigilado", t.Vigilado ? "Sí" : "No")
            .Campo("Actualizado", t.ActualizadoUtc?.ToLocalTime().ToString("dd/MM/yyyy"))
            .Campo("Traído de DevOps", t.SincronizadoUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
            .Campo("Enlace", t.Url)
            // Las etiquetas van como texto largo: son una lista separada por punto y coma que en una
            // columna de 140 px se recorta casi siempre, y es de lo que más se busca en un ticket.
            .Texto("Etiquetas", t.Etiquetas);
}
