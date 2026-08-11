using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Ausencias;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Quién más del equipo estará fuera en unas fechas dadas.
///
/// <para><b>Por qué existe este servicio y no una línea más en <see cref="AusenciasService"/>.</b>
/// Aquél arma «Mis vacaciones» y «Mis permisos» a partir de datos PROPIOS: todas sus consultas
/// filtran por el desarrollador de la sesión. Éste lee, por definición, solicitudes AJENAS, y esa es
/// una diferencia que conviene que se vea desde el nombre del archivo. Mezclarlos dejaría en la misma
/// clase consultas con dos reglas distintas de a-quién-pertenece-el-dato, que es como se acaba
/// devolviendo por descuido el comentario de otra persona en una respuesta que nadie revisa porque
/// «es la de mis vacaciones».</para>
///
/// <para><b>El cruce no se reinventa</b>: los días que se solapan salen de
/// <see cref="CapacityStats.DiasVacacionEnRango"/>, que es la copia literal del escritorio que ya usa
/// la planeación de capacidad y que tiene sus propias pruebas. Dos formas de contar un solape acaban
/// discrepando en los extremos —el día que empieza y el que termina— y ninguna prueba lo delata,
/// porque cada una prueba la suya.</para>
///
/// <para><b>Solo VACACIONES APROBADAS.</b> Lo pendiente no se cuenta porque todavía puede rechazarse
/// y anunciar como ausencia lo que quizá no lo sea empujaría a mover unas fechas por nada. Los
/// permisos quedan fuera por privacidad, y el porqué está en <see cref="CompaneroFueraDto"/>.</para>
/// </summary>
public class AusenciasDelEquipoService(AppDbContext db, ICurrentUser usuarioActual)
{
    /// <summary>
    /// Lo más largo que se acepta consultar de una vez.
    ///
    /// Un año cubre de sobra cualquier petición real —la más larga que permite la tabla de la ley son
    /// unas semanas— y pone un techo a lo que una llamada puede hacer recorrer. No es validación de
    /// negocio sino freno: el rango llega en la cadena de consulta y a esta ruta se la puede llamar
    /// sin pasar por la pantalla, con dos fechas separadas por un siglo.
    /// </summary>
    public const int MaxDiasDeConsulta = 366;

    /// <summary>
    /// Las vacaciones aprobadas de los DEMÁS que pisan el rango dado.
    ///
    /// <para>El rango se NORMALIZA en vez de rechazarse: si llegan al revés se voltean, si traen hora
    /// se recorta, y si es más largo que <see cref="MaxDiasDeConsulta"/> se acorta. Es una ayuda de
    /// pantalla, no un alta: contestar un 400 a quien está eligiendo fechas dejaría el paso del
    /// asistente en blanco sin decirle qué hacer, y lo que quiere saber cabe igual en el rango
    /// recortado.</para>
    ///
    /// <para>Quien pregunta NO aparece en su propia lista. Sus solicitudes ya las tiene delante en la
    /// misma pantalla, y verse a uno mismo entre «quién más estará fuera» haría dudar de si el
    /// asistente entendió de quién son las fechas.</para>
    /// </summary>
    public async Task<AusenciasDelEquipoDto> QuienEstaraFueraAsync(
        DateTime inicio, DateTime fin, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        var desde = inicio.Date;
        var hasta = fin.Date;
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        if ((hasta - desde).Days + 1 > MaxDiasDeConsulta) hasta = desde.AddDays(MaxDiasDeConsulta - 1);

        // La cuenta del administrador puede no tener ficha. No es un error: simplemente no hay «yo»
        // a quien excluir ni equipo propio con el que comparar, y la lista sale igual de útil.
        int? yo = usuarioActual.DeveloperId;

        int? miEquipo = yo is int devId
            ? await db.Developers.AsNoTracking()
                .Where(d => d.Id == devId).Select(d => d.TeamId).FirstOrDefaultAsync(ct)
            : null;

        // El solape se pide EN LA BASE y no trayendo todas las vacaciones para filtrarlas aquí: con
        // el histórico de varios años, lo segundo arrastraría por la red cada solicitud que jamás se
        // aprobó para descartarlas en memoria. La proyección deja fuera los BLOB del respaldo y el
        // comentario, que aquí no pintan nada.
        //
        // El extremo derecho se compara contra el DÍA SIGUIENTE y con «menor que», en vez de usar
        // .Date sobre la columna: si alguna fila trae hora —las escribe gente por varios caminos y
        // basta uno que guarde DateTime.Now—, «<= hasta» dejaría fuera una vacación que empieza el
        // último día del rango. Además esta forma se traduce a una comparación simple y el motor
        // puede usar el índice, que es lo que .Date impide.
        var finExclusivo = hasta.AddDays(1);

        var consulta = db.VacationRequests.AsNoTracking()
            .Where(v => v.Status == VacationStatus.Aprobada
                     && v.Developer.IsActive
                     && v.StartDate < finExclusivo
                     && v.EndDate >= desde);

        if (yo is int mio) consulta = consulta.Where(v => v.DeveloperId != mio);

        var filas = await consulta
            .OrderBy(v => v.StartDate).ThenBy(v => v.Developer.FullName)
            .Select(v => new
            {
                v.Developer.FullName,
                v.StartDate,
                v.EndDate,
                v.Developer.TeamId
            })
            .ToListAsync(ct);

        var fuera = filas
            .Select(v => new CompaneroFueraDto(
                v.FullName,
                v.StartDate.Date,
                v.EndDate.Date,
                CapacityStats.DiasVacacionEnRango(v.StartDate, v.EndDate, desde, hasta),
                // Sin equipo asignado no hay «mismo equipo» que marcar: dos nulos coincidirían y
                // saldría todo el mundo señalado como compañero directo, que es peor que no señalar
                // a nadie porque la marca dejaría de significar nada.
                MismoEquipo: miEquipo != null && v.TeamId == miEquipo))
            .ToList();

        return new AusenciasDelEquipoDto(desde, hasta, fuera, Resumen(fuera, desde, hasta));
    }

    /// <summary>
    /// La frase del cruce. La escribe el servidor y no la pantalla porque decidir entre «nadie»,
    /// «una persona» y «tres personas» —y contar personas y no solicitudes, que no es lo mismo cuando
    /// alguien tiene dos periodos dentro del rango— es lógica, y repetirla en cada pantalla que
    /// pregunte lo mismo es cómo empiezan las dos versiones que discrepan.
    /// </summary>
    private static string Resumen(IReadOnlyList<CompaneroFueraDto> fuera, DateTime desde, DateTime hasta)
    {
        string rango = $"del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";

        if (fuera.Count == 0)
            return $"Nadie más del equipo tiene vacaciones aprobadas {rango}.";

        int personas = fuera.Select(f => f.Nombre).Distinct().Count();
        int delMismoEquipo = fuera.Where(f => f.MismoEquipo).Select(f => f.Nombre).Distinct().Count();

        string frase = personas == 1
            ? $"1 persona del equipo ya tiene vacaciones aprobadas {rango}."
            : $"{personas} personas del equipo ya tienen vacaciones aprobadas {rango}.";

        // El dato del propio equipo va al final y solo cuando lo hay: es el que puede cambiar la
        // decisión, y decir «0 de tu equipo» en cada consulta lo convertiría en ruido que se deja de
        // leer justo antes de la vez que sí importaba.
        if (delMismoEquipo > 0)
            frase += delMismoEquipo == 1
                ? " 1 de ellas es de tu mismo equipo."
                : $" {delMismoEquipo} de ellas son de tu mismo equipo.";

        return frase;
    }
}
