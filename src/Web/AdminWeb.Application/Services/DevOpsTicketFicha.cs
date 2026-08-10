using AdminWeb.Infrastructure.Integraciones;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que se puede averiguar de un ticket más allá de sus campos: qué regresiones colgó y por
/// cuántas manos pasó.
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte.
/// Los tipos de los bugs hijos y del historial vienen del cliente de la integración, que es quien
/// los lee de la API; aquí vive solo el CÁLCULO, que es lo que hay que poder probar sin red.</para>
/// </summary>
public record DevOpsTicketFicha(
    int Numero,
    IReadOnlyList<BugHijoDevOps> BugsHijos,
    IReadOnlyList<CambioDeAsignacionDevOps> Asignaciones)
{
    public int Regresiones => BugsHijos.Count;

    public int RegresionesAbiertas => BugsHijos.Count(b => !DevOpsService.EsCerrado(b.Estado));

    /// <summary>
    /// Cuántas personas distintas han tenido el ticket. Se cuenta por nombre porque es lo único que
    /// el historial trae siempre; el correo falta en las revisiones antiguas.
    /// </summary>
    public int Manos => Asignaciones
        .Select(c => c.A)
        .Where(a => !string.IsNullOrWhiteSpace(a))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>Cuántas veces le devolvieron el ticket a esta persona. Ver <see cref="ContarDevoluciones"/>.</summary>
    public int DevolucionesA(string? nombre, string? correo) =>
        ContarDevoluciones(Asignaciones, nombre, correo);

    /// <summary>
    /// Cuántas veces le devolvieron el ticket a alguien DESPUÉS de que lo tuviera otra persona.
    ///
    /// No es «cuántas veces se lo asignaron»: la primera vez no es una devolución, y que se lo
    /// reasignen a uno mismo dos veces seguidas tampoco. Lo que cuenta —y lo que duele— es que vuelva
    /// después de haber pasado por otras manos, porque suele significar que se dio por terminado algo
    /// que no lo estaba.
    ///
    /// Estático y puro para poder probarlo sin tocar la red.
    /// </summary>
    public static int ContarDevoluciones(
        IEnumerable<CambioDeAsignacionDevOps> historial, string? nombre, string? correo)
    {
        if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(correo)) return 0;

        int devoluciones = 0;
        bool loTuvo = false;        // la persona ya fue dueña en algún momento
        bool otroDespues = false;   // …y desde entonces lo tuvo alguien más

        foreach (var cambio in historial.OrderBy(c => c.Fecha))
        {
            if (EsLaPersona(cambio, nombre, correo))
            {
                if (loTuvo && otroDespues) devoluciones++;
                loTuvo = true;
                otroDespues = false;
            }
            // Que quede SIN asignar no cuenta como «lo tuvo alguien más»: nadie lo trabajó.
            else if (!string.IsNullOrWhiteSpace(cambio.A) && loTuvo)
            {
                otroDespues = true;
            }
        }

        return devoluciones;
    }

    /// <summary>
    /// El correo manda cuando lo hay: el nombre para mostrar de DevOps trae o quita acentos y
    /// segundos nombres según cómo se capturó, y empatar por él solo produce falsos negativos.
    /// </summary>
    private static bool EsLaPersona(CambioDeAsignacionDevOps cambio, string? nombre, string? correo)
    {
        if (!string.IsNullOrWhiteSpace(correo) && !string.IsNullOrWhiteSpace(cambio.CorreoDeA))
            return string.Equals(cambio.CorreoDeA, correo, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(nombre)
            && string.Equals(cambio.A?.Trim(), nombre.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
