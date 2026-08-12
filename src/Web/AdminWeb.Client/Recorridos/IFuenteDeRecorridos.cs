namespace AdminWeb.Client.Recorridos;

/// <summary>
/// Un archivo de guiones. Quien escribe los recorridos de un área implementa esto UNA vez y no toca
/// nada más: el registro los encuentra solo.
///
/// <para>No hay ninguna lista central donde apuntarse, y es a propósito. Un recorrido por pantalla,
/// escritos por varias personas contra un único archivo de registro, es un conflicto de fusión por
/// cada uno, y —peor— un recorrido perfecto que no aparece porque su línea se perdió en una
/// resolución de conflicto. Aquí, crear el archivo es todo el trámite.</para>
///
/// <para>La clase tiene que ser pública, no abstracta y tener constructor sin parámetros — lo que
/// una clase normal es sin hacer nada.</para>
///
/// <example>
/// Un archivo de guiones completo, que es también el contrato entero:
/// <code>
/// namespace AdminWeb.Client.Recorridos.Guiones;
///
/// public sealed class RecorridosDelPool : IFuenteDeRecorridos
/// {
///     public IEnumerable&lt;Recorrido&gt; Recorridos() =>
///     [
///         new Recorrido("/pool", "El pool de tickets",
///
///             // Paso 1, SIEMPRE: qué es esta pantalla. Sale centrado y no señala nada.
///             Paso.Portada("El pool",
///                 "Aquí caen los tickets que todavía no tienen quien los lleve. Desde esta " +
///                 "pantalla se reparten: se mira lo que hay, se elige a alguien y se le asigna."),
///
///             new Paso("pool-filtros", "Acotar lo que se ve",
///                 "Los filtros se aplican a la vez y se recuerdan al volver. Lo habitual es " +
///                 "dejar puesto el de tu equipo y cambiar solo el de prioridad."),
///
///             new Paso("pool-rejilla", "Los tickets sin dueño",
///                 "Cada fila es un ticket. La columna de días lleva el color del vencimiento: " +
///                 "en ámbar los que vencen esta semana y en rojo los que ya vencieron."),
///
///             new Paso("pool-asignar", "Asignar",
///                 "Con una fila seleccionada, abre el cuadro para elegir a quién se le da. " +
///                 "Al confirmar le llega el aviso a esa persona y el ticket sale del pool.")
///             {
///                 Lado = LadoDelPaso.Izquierda   // solo si el sitio automático tapa la rejilla
///             })
///     ];
/// }
/// </code>
/// Y en <c>Paginas/Pool/Pool.razor</c>, las marcas correspondientes:
/// <code>
/// &lt;RadzenStack data-recorrido="pool-filtros" Orientation="Orientation.Horizontal"&gt;
/// &lt;RadzenDataGrid data-recorrido="pool-rejilla" Data="@_tickets" ... /&gt;
/// &lt;RadzenButton data-recorrido="pool-asignar" Text="Asignar" Click="@Asignar" /&gt;
/// </code>
/// </example>
/// </summary>
public interface IFuenteDeRecorridos
{
    /// <summary>Los recorridos de este área. Uno por pantalla.</summary>
    IEnumerable<Recorrido> Recorridos();
}
