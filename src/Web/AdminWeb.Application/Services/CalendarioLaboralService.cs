using AdminWeb.Domain.Calculo;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Qué días se trabajan y cuáles no.
///
/// <para><b>De aquí depende cuántos días consume una solicitud de vacaciones</b>, y por eso está en un
/// solo sitio. El artículo 76 de la LFT concede días LABORABLES; contar naturales —que es lo que se
/// hacía— descuenta sábados, domingos y festivos, o sea que a unas vacaciones de lunes a viernes de la
/// semana siguiente le cobraba doce días en vez de diez. Es cobrar de más, todos los años, en
/// silencio.</para>
///
/// <para><b>Los festivos salen de la TABLA y no de la regla</b>, aunque la tabla se siembre desde ella.
/// La casa puede añadir días que la ley no conoce —un puente, el aniversario, el día que cierra la
/// oficina— y calcularlos al vuelo dejaría esos fuera. Ver <see cref="DiaFestivo"/>.</para>
/// </summary>
public class CalendarioLaboralService(AppDbContext db)
{
    /// <summary>
    /// Hasta cuántos años por delante se siembra al arrancar. Cinco es bastante para que nadie tenga
    /// que acordarse de resembrar durante un lustro, y poco para que la tabla siga siendo legible.
    /// </summary>
    public const int AniosQueSeSiembran = 5;

    /// <summary>
    /// Cuántos días LABORABLES cubre un rango, contando el primero y el último.
    ///
    /// <para>Fuera sábados, domingos y los días de la tabla. Un rango entero de festivos y fin de
    /// semana da CERO, y eso es correcto: no consumió vacaciones. Quien valide la solicitud es quien
    /// decide si un rango de cero días tiene sentido; aquí solo se cuenta.</para>
    /// </summary>
    public async Task<int> DiasLaborablesAsync(DateTime inicio, DateTime fin, CancellationToken ct = default)
    {
        if (fin.Date < inicio.Date) return 0;

        var festivos = await FestivosEntreAsync(inicio, fin, ct);
        return ContarLaborables(inicio, fin, festivos);
    }

    /// <summary>
    /// La misma cuenta, pero con los festivos YA LEÍDOS.
    ///
    /// <para>Existe porque el saldo de vacaciones cuenta muchos rangos seguidos —uno por solicitud— y
    /// preguntarle a la base por cada uno sería una consulta por fila. Quien la use lee una vez el
    /// rango entero y cuenta en memoria.</para>
    /// </summary>
    public static int ContarLaborables(DateTime inicio, DateTime fin, IReadOnlySet<DateTime> festivos)
    {
        if (fin.Date < inicio.Date) return 0;

        int dias = 0;
        for (var d = inicio.Date; d <= fin.Date; d = d.AddDays(1))
            if (EsLaborable(d, festivos)) dias++;

        return dias;
    }

    /// <summary>Un día cualquiera: ¿se trabaja?</summary>
    public static bool EsLaborable(DateTime dia, IReadOnlySet<DateTime> festivos) =>
        dia.DayOfWeek != DayOfWeek.Saturday
        && dia.DayOfWeek != DayOfWeek.Sunday
        && !festivos.Contains(dia.Date);

    /// <summary>Los festivos que caen dentro de un rango, como fechas sin hora.</summary>
    public async Task<HashSet<DateTime>> FestivosEntreAsync(
        DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var d = desde.Date;
        var h = hasta.Date;

        return [.. await db.DiasFestivos.AsNoTracking()
            .Where(f => f.Fecha >= d && f.Fecha <= h)
            .Select(f => f.Fecha)
            .ToListAsync(ct)];
    }

    /// <summary>Todos los festivos que hay en la tabla. Para cuentas que abarcan varios años.</summary>
    public async Task<HashSet<DateTime>> TodosLosFestivosAsync(CancellationToken ct = default) =>
        [.. await db.DiasFestivos.AsNoTracking().Select(f => f.Fecha).ToListAsync(ct)];

    /// <summary>
    /// Siembra los días del artículo 74 que falten, desde el año en curso y por los próximos años.
    ///
    /// <para><b>Solo AÑADE lo que falta.</b> No borra, no reescribe y no toca los días que puso la
    /// casa: se corre en cada arranque y tiene que ser inocuo. Lo que decide si un día ya está es su
    /// FECHA, no su motivo, porque un festivo movido de sitio a mano seguiría siendo el mismo día que
    /// no se trabaja.</para>
    ///
    /// <para>Devuelve cuántos añadió, para que el arranque lo pueda decir.</para>
    /// </summary>
    public async Task<int> SembrarLosDeLeyAsync(int desdeElAnio, CancellationToken ct = default)
    {
        int hasta = desdeElAnio + AniosQueSeSiembran - 1;

        var yaEstan = await db.DiasFestivos.AsNoTracking()
            .Where(f => f.Fecha.Year >= desdeElAnio && f.Fecha.Year <= hasta)
            .Select(f => f.Fecha)
            .ToListAsync(ct);
        var conocidas = yaEstan.Select(f => f.Date).ToHashSet();

        var nuevos = FestivosDeLey.DeLosAnios(desdeElAnio, hasta)
            .Select(d => new { Fecha = d.Fecha.ToDateTime(TimeOnly.MinValue), d.Motivo })
            .Where(d => !conocidas.Contains(d.Fecha))
            .Select(d => new DiaFestivo { Fecha = d.Fecha, Motivo = d.Motivo, EsDeLey = true })
            .ToList();

        if (nuevos.Count == 0) return 0;

        db.DiasFestivos.AddRange(nuevos);
        await db.SaveChangesAsync(ct);
        return nuevos.Count;
    }
}
