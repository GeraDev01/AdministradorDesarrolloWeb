namespace AdminWeb.Client.Paginas.Foro;

/// <summary>
/// «hace 5 min», «ayer», «12/03/2026» — el sello de tiempo que lleva cada tarjeta y cada entrada del
/// hilo.
///
/// Es la misma escala que <c>ForumFilter.HaceCuanto</c> del servidor, y está repetida aquí a
/// propósito: el cálculo tiene que hacerse en el NAVEGADOR. Los DTO del foro viajan en UTC, y quien
/// convierte a hora local debe ser la máquina de quien lee — si lo hiciera el servidor, una fecha
/// absoluta saldría en la zona horaria del servidor (en Azure, UTC) y podría verse un día corrido.
///
/// La otra opción era mover <c>ForumFilter</c> a Shared, y no compensa: arrastraría la lógica de
/// filtrado y aplanado del foro al navegador solo para formatear una fecha.
/// </summary>
public static class TiempoDelForo
{
    public static string HaceCuanto(DateTime utc)
    {
        var t = DateTime.UtcNow - utc;

        // Un instante «en el futuro» solo puede venir de un reloj desajustado entre máquinas: se
        // muestra la fecha en lugar de un «hace -3 min» que nadie sabría interpretar.
        if (t < TimeSpan.Zero) return Fecha(utc, conHora: true);
        if (t < TimeSpan.FromMinutes(1)) return "ahora mismo";
        if (t < TimeSpan.FromHours(1)) return $"hace {(int)t.TotalMinutes} min";
        if (t < TimeSpan.FromDays(1)) return $"hace {(int)t.TotalHours} h";
        if (t < TimeSpan.FromDays(2)) return "ayer";
        if (t < TimeSpan.FromDays(7)) return $"hace {(int)t.TotalDays} días";
        return Fecha(utc, conHora: false);
    }

    /// <summary>La fecha exacta, para el tooltip: «hace 3 días» está bien para leer, no para citar.</summary>
    public static string Fecha(DateTime utc, bool conHora = true) =>
        utc.ToLocalTime().ToString(conHora ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy");
}
