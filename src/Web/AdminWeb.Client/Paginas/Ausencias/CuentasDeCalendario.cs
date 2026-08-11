using System.Globalization;

namespace AdminWeb.Client.Paginas.Ausencias;

/// <summary>
/// Las cuentas de calendario que los dos asistentes de ausencias enseñan mientras se eligen fechas:
/// cuántos días hábiles cubre un rango y cuándo se vuelve al trabajo.
///
/// <para><b>Por qué está aquí y no en la capa de aplicación.</b> El cliente Blazor solo referencia a
/// Shared —es una frontera dura del proyecto, para que el navegador no acabe descargando el modelo
/// de datos— así que no puede llamar a
/// <c>DocumentoDeVacacionesService.SiguienteDiaHabil</c>, que es la copia que imprime la fecha de
/// regreso en el papel. Se repite la regla, y por eso conviene que esté en UN solo sitio del cliente
/// y no copiada en las dos pantallas.</para>
///
/// <para><b>SÁBADO Y DOMINGO Y NADA MÁS.</b> Es la misma regla del escritorio y la del servidor. El
/// sistema NO tiene calendario de días festivos y aquí no se inventa uno: adivinar cuáles son —que
/// cambian por año, por estado y por empresa— sería peor que la regla simple que todo el mundo ya
/// conoce, porque un número equivocado que parece exacto no se revisa. Lo que sí se hace es decirlo
/// en pantalla, con <c>AvisoDeFestivos</c>, en vez de dejar que el número parezca lo que no es.</para>
///
/// <para><b>Si algún día se añaden festivos, hay que añadirlos EN LOS DOS SITIOS el mismo día.</b>
/// Si solo se toca uno, el documento impreso dirá una fecha de regreso y esta pantalla otra — y el
/// que se lleva la razón delante de RH es el papel.</para>
/// </summary>
internal static class CuentasDeCalendario
{
    /// <summary>La cultura con la que se escriben los días de la semana. La aplicación es en español.</summary>
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Un día laborable: cualquiera que no sea sábado ni domingo.</summary>
    public static bool EsHabil(DateTime d) =>
        d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// Días de lunes a viernes dentro del rango, contando el primero y el último.
    ///
    /// Se recorre día a día en vez de con una fórmula sobre semanas completas: son rangos de días o
    /// semanas —el tope de un permiso son 365 días— y una fórmula con residuos es justo donde se
    /// cuela el error de un día que esta cuenta existe para evitar.
    /// </summary>
    public static int DiasHabiles(DateTime inicio, DateTime fin)
    {
        if (fin.Date < inicio.Date) return 0;

        int cuenta = 0;
        for (var d = inicio.Date; d <= fin.Date; d = d.AddDays(1))
            if (EsHabil(d)) cuenta++;

        return cuenta;
    }

    /// <summary>
    /// El primer día laborable después del último día de ausencia, ya escrito para leerse.
    ///
    /// <para>El DÍA DE LA SEMANA va delante de la fecha, y no es adorno: es lo que deja comprobar de
    /// un vistazo que a nadie se le está citando un sábado. El documento impreso lo escribe igual.</para>
    /// </summary>
    public static string Regreso(DateTime ultimoDia)
    {
        var d = ultimoDia.Date.AddDays(1);
        while (!EsHabil(d)) d = d.AddDays(1);

        var nombre = d.ToString("dddd", Espanol);
        return $"{char.ToUpper(nombre[0], Espanol)}{nombre[1..]} {d:dd/MM/yyyy}";
    }
}
