namespace Administrador_Desarrollo_Web.Services;

/// <summary>Un bug colgado como HIJO de un ticket. Cada uno es una regresión que provocó.</summary>
public record DevOpsBugHijo(int Id, string Titulo, string Estado, string Url)
{
    /// <summary>Los estados cerrados de DevOps. Una regresión ya arreglada sigue habiendo existido.</summary>
    public bool Cerrado => AzureDevOpsService.EsCerrado(Estado);
}

/// <summary>Un cambio de dueño del ticket, tal como lo cuenta el historial de revisiones de DevOps.</summary>
public record DevOpsCambioAsignacion(DateTime Fecha, string? De, string? A, string? AEmail);

/// <summary>
/// Lo que se puede averiguar de un ticket más allá de sus campos: qué regresiones colgó y por
/// cuántas manos pasó.
/// </summary>
public record DevOpsFichaTicket(
    int ExternalId,
    IReadOnlyList<DevOpsBugHijo> BugsHijos,
    IReadOnlyList<DevOpsCambioAsignacion> Asignaciones)
{
    public int Regresiones => BugsHijos.Count;
    public int RegresionesAbiertas => BugsHijos.Count(b => !b.Cerrado);

    /// <summary>
    /// Cuántas veces le devolvieron el ticket a esta persona DESPUÉS de que lo tuviera alguien más.
    ///
    /// No es «cuántas veces se lo asignaron»: la primera vez no es una devolución, y que se lo
    /// reasignen a uno mismo dos veces seguidas tampoco. Lo que cuenta —y lo que duele— es que
    /// vuelva después de haber pasado por otras manos, porque suele significar que se dio por
    /// terminado algo que no lo estaba.
    /// </summary>
    public int DevolucionesA(string? nombre, string? email) =>
        ContarDevoluciones(Asignaciones, nombre, email);

    /// <summary>Pura, para poder probarla sin tocar la red.</summary>
    public static int ContarDevoluciones(
        IEnumerable<DevOpsCambioAsignacion> historial, string? nombre, string? email)
    {
        if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(email)) return 0;

        int devoluciones = 0;
        bool loTuvo = false;          // la persona ya fue dueña en algún momento
        bool otroDespues = false;     // …y desde entonces lo tuvo alguien más

        foreach (var c in historial.OrderBy(c => c.Fecha))
        {
            if (EsLaPersona(c, nombre, email))
            {
                if (loTuvo && otroDespues) devoluciones++;
                loTuvo = true;
                otroDespues = false;
            }
            // Que quede SIN asignar no cuenta como «lo tuvo alguien más»: nadie lo trabajó.
            else if (!string.IsNullOrWhiteSpace(c.A) && loTuvo)
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
    private static bool EsLaPersona(DevOpsCambioAsignacion c, string? nombre, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(c.AEmail))
            return string.Equals(c.AEmail, email, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(nombre)
            && string.Equals(c.A?.Trim(), nombre.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
