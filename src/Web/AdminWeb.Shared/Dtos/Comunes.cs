namespace AdminWeb.Shared.Dtos;

/// <summary>Una preferencia de interfaz. <c>Json</c> nulo o vacío significa «sin configurar».</summary>
public record PreferenciaDto(string Clave, string? Json);

/// <summary>
/// Una página de resultados. Se usa en todo lo que puede crecer sin límite (la bitácora, el foro,
/// los tickets): traer la tabla entera funciona el primer mes y deja de funcionar el segundo.
/// </summary>
public record PaginaDto<T>(IReadOnlyList<T> Filas, int Total, int Pagina, int TamanoPagina)
{
    public int TotalPaginas => TamanoPagina <= 0 ? 1 : (int)Math.Ceiling(Total / (double)TamanoPagina);
}

/// <summary>Una opción de un desplegable (un desarrollador, un equipo, un criterio).</summary>
public record OpcionDto(int Id, string Texto);

/// <summary>
/// El testigo antiforgery que acompaña a las subidas de archivos.
///
/// Solo lo necesitan esas: una petición con cuerpo JSON obliga al navegador a preguntar antes
/// (preflight) y sin política CORS no pasa, pero un formulario multipart de otro sitio sí llegaría
/// con la cookie de sesión puesta.
/// </summary>
public record TestigoDto(string Valor);

/// <summary>
/// Respuesta de las operaciones que solo dicen si salieron bien.
///
/// El <c>Mensaje</c> viene del servicio y se enseña TAL CUAL, incluso cuando la operación se
/// rechaza. Los servicios portados del escritorio explican el motivo en concreto —«ya marcaste tu
/// entrada hoy a las 09:12», «esa actividad la tomó alguien más»— y ese texto es la mitad del
/// valor: cambiarlo aquí por un «no se pudo» genérico sería tirar lo que el escritorio hacía bien.
/// </summary>
public record ResultadoDto(bool Ok, string Mensaje);
