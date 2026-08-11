using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Ausencias;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La tabla de días de vacaciones de la Ley Federal del Trabajo, escrita COMO TABLA.
///
/// <para><b>Por qué una tabla y no una fórmula.</b> Existe la fórmula —«10 + 2n hasta el quinto,
/// después 22 + 2·((n−6)/5)»— y de hecho está escrita en <c>LftVacaciones.DiasPorAnios</c>, que se
/// portó del escritorio sin tocar. Pero esto lo va a leer alguien con el artículo 76 delante para
/// comprobar renglón por renglón que dice lo mismo, y contra una fórmula ingeniosa no se puede
/// comparar nada: hay que ejecutarla mentalmente para cada año antes de saber si acierta. La tabla
/// se lee de un vistazo y se corrige de un vistazo el día que la ley cambie otra vez —cambió en
/// 2023, así que no es una hipótesis.</para>
///
/// <para><b>Y por qué hay dos sitios con la misma regla.</b> <c>LftVacaciones</c> es la SUGERENCIA
/// que rellena el campo «Días de vacaciones» de la ficha, y tiene que seguir dando exactamente el
/// mismo número que el escritorio hasta el corte —es una copia literal y no se toca—. Además hace
/// algo que este saldo NO hace: reparte proporcionalmente los 12 días del primer año (art. 77). Aquí
/// el primer año vale CERO hasta cumplirlo, por decisión de la empresa, así que las dos no podían
/// ser la misma función. Lo que sí hay es una prueba que compara las dos tablas año por año: si
/// alguien toca una, la suite lo dice.</para>
/// </summary>
public static class TablaDeVacacionesLft
{
    /// <summary>
    /// Los renglones de la ley, tal cual: años de servicio cumplidos → días del periodo anual.
    ///
    /// <code>
    ///   año  1        → 12 días
    ///   año  2        → 14
    ///   año  3        → 16
    ///   año  4        → 18
    ///   año  5        → 20
    ///   años  6 a 10  → 22
    ///   años 11 a 15  → 24
    ///   años 16 a 20  → 26
    ///   años 21 a 25  → 28
    ///   años 26 a 30  → 30
    ///   años 31 a 35  → 32
    ///   años 36 a 40  → 34
    ///   años 41 a 45  → 36
    ///   años 46 a 50  → 38
    /// </code>
    ///
    /// <para>El número NO es acumulativo: es lo que corresponde POR CADA periodo anual con esa
    /// antigüedad. Lo que se acumula de un periodo a otro es lo que no se gozó, y de eso se encarga
    /// <see cref="SaldoDeVacacionesService"/>.</para>
    /// </summary>
    private static readonly (int Desde, int Hasta, int Dias)[] Renglones =
    [
        ( 1,  1, 12),
        ( 2,  2, 14),
        ( 3,  3, 16),
        ( 4,  4, 18),
        ( 5,  5, 20),
        ( 6, 10, 22),
        (11, 15, 24),
        (16, 20, 26),
        (21, 25, 28),
        (26, 30, 30),
        (31, 35, 32),
        (36, 40, 34),
        (41, 45, 36),
        (46, 50, 38),
    ];

    /// <summary>
    /// Días que genera el periodo anual número <paramref name="anioDeServicio"/> (1 = el primero).
    ///
    /// <para>Cero o menos devuelve 0, y eso NO es un caso raro: es la regla del primer año. Mientras
    /// no se cumple el aniversario no hay días generados, sin parte proporcional.</para>
    /// </summary>
    public static int DiasDelPeriodo(int anioDeServicio)
    {
        if (anioDeServicio <= 0) return 0;

        foreach (var (desde, hasta, dias) in Renglones)
            if (anioDeServicio >= desde && anioDeServicio <= hasta)
                return dias;

        // Más allá del último renglón la ley sigue subiendo 2 días por cada quinquenio, y aquí se
        // continúa desde el último renglón en vez de dejar un tope. No es un caso real —serían más
        // de 50 años en la misma empresa— pero un tope silencioso pagaría de menos a quien llegara,
        // que es peor que estas dos líneas. Si algún día hace falta, se añaden renglones a la tabla
        // y esta rama deja de tocarse.
        var (_, ultimoHasta, ultimosDias) = Renglones[^1];
        return ultimosDias + 2 * ((anioDeServicio - ultimoHasta + 4) / 5);
    }

    /// <summary>
    /// El aniversario número <paramref name="anios"/> de la fecha de ingreso.
    ///
    /// <para>Quien entró un 29 de febrero cumple años el 28 en los años que no son bisiestos. Sin
    /// esto, <c>AddYears</c> hace lo mismo pero el día del corte quedaría a merced de un detalle del
    /// marco en vez de ser una decisión escrita, y esta fecha decide cuándo se generan los días de
    /// alguien.</para>
    /// </summary>
    public static DateTime Aniversario(DateTime ingreso, int anios)
    {
        int anio = ingreso.Year + anios;
        int dia = Math.Min(ingreso.Day, DateTime.DaysInMonth(anio, ingreso.Month));
        return new DateTime(anio, ingreso.Month, dia);
    }

    /// <summary>Aniversarios de ingreso ya cumplidos a la fecha de corte.</summary>
    public static int AniosCumplidos(DateTime ingreso, DateTime corte)
    {
        var a = ingreso.Date;
        var b = corte.Date;
        if (b <= a) return 0;

        int anios = b.Year - a.Year;
        // Si el aniversario de este año todavía no llega, ese año aún no está cumplido.
        if (b < Aniversario(a, anios)) anios--;
        return Math.Max(0, anios);
    }
}

/// <summary>
/// El saldo de vacaciones de una persona: cuántos días le ha dado la ley, cuántos gozó, cuántos
/// perdió por no tomarlos a tiempo y cuántos le quedan hoy.
///
/// <para><b>EL SALDO NO SE GUARDA.</b> Es la decisión que sostiene todo lo demás. Se recalcula en
/// cada consulta desde la fecha de ingreso y las solicitudes, y lo único almacenado es el ajuste
/// manual, que es el único dato que un humano escribe y que no se puede deducir de nada. Un saldo
/// guardado como número habría que mantenerlo sincronizado a mano en cada alta, cada aprobación,
/// cada cancelación y cada cambio de fecha de ingreso; se desincroniza el primer día que alguien
/// toque algo por un camino que nadie recordó actualizar, y a partir de ahí miente sin avisar.
/// Derivarlo cuesta una consulta y no puede quedar desfasado nunca.</para>
///
/// <para><b>Las cinco reglas que aplica</b>, todas decididas por la empresa:</para>
/// <list type="number">
///   <item>Los días salen de <see cref="TablaDeVacacionesLft"/>, contados desde la fecha de ingreso.</item>
///   <item>El periodo es el ANIVERSARIO de cada quien, no el año calendario: cada persona tiene su
///         propia fecha de corte.</item>
///   <item>Lo no gozado se ACUMULA, pero CADUCA: 18 meses desde el cierre de su periodo, y el plazo
///         es configurable porque es política de la empresa (ver <see cref="ClaveCaducidadMeses"/>).</item>
///   <item>El PRIMER AÑO ES CERO hasta cumplirlo, sin parte proporcional; y el mensaje dice en qué
///         fecha cambia, porque un cero sin fecha parece un error del programa.</item>
///   <item>Al calculado se le resta lo aprobado en el sistema y se le suma el AJUSTE MANUAL del
///         líder, con su nota y su autor, que queda en la bitácora.</item>
/// </list>
/// </summary>
public class SaldoDeVacacionesService(
    AppDbContext db,
    ICurrentUser usuarioActual,
    AuditService auditoria,
    SettingsService ajustes)
{
    /// <summary>
    /// Meses que se arrastran los días no gozados antes de caducar.
    ///
    /// <para>La clave se declara aquí y no solo en <c>SettingsService.Claves</c> por lo mismo que
    /// <c>PoolActivityService.ClaveMaxTomadas</c>: quien lee este servicio tiene que ver de qué
    /// depende el cálculo sin abrir otro archivo. El texto es el contrato y las dos copias tienen
    /// que decir lo mismo.</para>
    /// </summary>
    public const string ClaveCaducidadMeses = "vacaciones.caducidad-meses";

    /// <summary>
    /// Los 18 meses por omisión: seis meses para conceder las vacaciones ya generadas más el año de
    /// prescripción que corre después.
    ///
    /// <para><b>Es un valor por omisión razonable, no una afirmación de que sea obligatorio</b>, y
    /// menos una asesoría legal. Por eso vive en la configuración y no como constante del programa:
    /// si el área correspondiente decide otro plazo, se cambia ahí y el saldo se recalcula solo
    /// —no hay ningún número guardado que corregir después—.</para>
    /// </summary>
    public const int CaducidadMesesPorOmision = 18;

    /// <summary>
    /// Tope de la ventana configurable, en meses. Cien años es «que no caduque nunca» sin necesidad
    /// de un valor especial que hubiera que recordar; por encima se toma como un dedazo y se cae al
    /// valor por omisión, porque un número absurdo aquí no da error: da un saldo enorme y creíble.
    /// </summary>
    public const int CaducidadMesesMaxima = 1200;

    /// <summary>
    /// Tope del ajuste manual, en días y en los dos sentidos. No es una regla de negocio: es la red
    /// contra el dedazo. Un año entero de días es más de lo que cualquier corrección legítima
    /// necesita, y sin tope un 3650 tecleado por accidente pasaría por saldo bueno.
    /// </summary>
    public const int MaxAjuste = 365;

    // ── Consulta ─────────────────────────────────────────────────────────────────

    /// <summary>El saldo de quien tiene la sesión.</summary>
    public Task<SaldoAcumuladoDto> MioAsync(DateTime? hoy = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        // Sin ficha no hay antigüedad que contar. Se responde vacío en lugar de fallar porque a las
        // pantallas de ausencias también llega el administrador, cuya cuenta no siempre tiene ficha.
        return usuarioActual.DeveloperId is int devId
            ? CalcularAsync(devId, hoy, ct)
            : Task.FromResult(SinFicha());
    }

    /// <summary>
    /// El saldo de una persona. El líder puede ver el de cualquiera; el desarrollador, solo el suyo.
    /// </summary>
    /// <param name="hoy">La fecha de corte. Se pasa y no se lee del reloj por dentro, igual que en
    /// <c>LftVacaciones</c>: en el servidor el reloj es UTC y quien pregunta está en otro huso, así
    /// que qué día es «hoy» lo decide el llamador. Nulo toma la fecha local del servidor, que es lo
    /// que ya hacen las demás pantallas de ausencias.</param>
    public async Task<SaldoAcumuladoDto> CalcularAsync(
        int developerId, DateTime? hoy = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);
        AuthorizationGuard.RequireOwnershipOrAdmin(usuarioActual, developerId);

        var corte = (hoy ?? DateTime.Today).Date;

        var ficha = await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId)
            // Proyección explícita y CORTA a propósito: de la ficha aquí solo hacen falta la fecha
            // de ingreso y el ajuste. Traer la entidad entera arrastraría campos que no pintan nada
            // en un saldo y que no tienen por qué salir de su pantalla.
            .Select(d => new
            {
                d.HireDate,
                d.VacationAdjustmentDays,
                d.VacationAdjustmentNote,
                d.VacationAdjustmentBy,
                d.VacationAdjustmentAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (ficha is null) return SinFicha();

        int ventana = await VentanaDeCaducidadAsync(ct);

        // Solo lo que hace falta para repartir: fecha de inicio, fin y estado. Ni comentarios ni
        // respaldos —serían hasta 15 MB por fila cruzando la red para no pintarse.
        var solicitudes = await db.VacationRequests.AsNoTracking()
            .Where(v => v.DeveloperId == developerId
                     && (v.Status == VacationStatus.Aprobada || v.Status == VacationStatus.Pendiente))
            .Select(v => new { v.StartDate, v.EndDate, v.Status })
            .ToListAsync(ct);

        // QUÉ CUENTA COMO CONSUMIDO, que es donde un saldo se vuelve mentira:
        //
        //  · APROBADAS: descuentan. Son la decisión del líder, tomadas o por tomar.
        //
        //  · PENDIENTES: NO son días gozados, pero SÍ están apartados, y por eso restan del
        //    disponible aunque viajen en su propio renglón. Si no restaran, alguien podría pedir tres
        //    veces los mismos días y las tres solicitudes parecerían caber; el líder aprobaría la
        //    primera creyendo que quedan días y las otras dos ya estarían de más. Se enseñan aparte
        //    para que se entienda por qué el disponible bajó sin que nadie haya aprobado nada.
        //
        //  · RECHAZADAS y CANCELADAS: no cuentan, ni un día. Y aquí es donde se paga sola la
        //    decisión de no guardar el saldo: al cancelar unas vacaciones ya aprobadas, los días
        //    vuelven SOLOS al saldo porque la siguiente consulta ya no encuentra esa fila entre las
        //    aprobadas. No hay ninguna devolución que programar ni que se pueda olvidar. Con un saldo
        //    almacenado habría que acordarse de sumar los días de vuelta en CancelarAsync, en
        //    EliminarAsync y en cualquier camino futuro que cambie un estado.
        //
        //  · Los PERMISOS (LeaveRequests) no tocan este saldo: una incapacidad o una cita médica no
        //    son vacaciones, y descontarlas de aquí le cobraría a la persona días que la ley no
        //    permite cobrarle.
        var gozados = solicitudes
            .Where(v => v.Status == VacationStatus.Aprobada)
            .Select(v => (Fecha: v.StartDate.Date, Dias: Dias(v.StartDate, v.EndDate)))
            .OrderBy(g => g.Fecha)
            .ToList();

        int comprometidos = solicitudes
            .Where(v => v.Status == VacationStatus.Pendiente)
            .Sum(v => Dias(v.StartDate, v.EndDate));

        if (ficha.HireDate is not DateTime ingreso)
            // Sin fecha de ingreso no hay antigüedad, y por tanto no hay nada generado. Se responde
            // con el mensaje que lo dice en lugar de un cero mudo: es el único caso en el que el cero
            // significa «falta capturar un dato» y no «todavía no te toca nada».
            return new SaldoAcumuladoDto(
                TieneFicha: true, FechaDeIngreso: null, AniosCumplidos: 0, ProximoAniversario: null,
                DiasDelProximoPeriodo: 0, VentanaDeCaducidadMeses: ventana,
                DiasGenerados: 0, DiasTomados: gozados.Sum(g => g.Dias), DiasCaducados: 0,
                DiasVigentes: -gozados.Sum(g => g.Dias), DiasComprometidos: comprometidos,
                AjusteManual: ficha.VacationAdjustmentDays,
                NotaDelAjuste: ficha.VacationAdjustmentNote,
                AutorDelAjuste: ficha.VacationAdjustmentBy,
                FechaDelAjusteUtc: ficha.VacationAdjustmentAtUtc,
                Mensaje: "Esta ficha no tiene fecha de ingreso, así que no hay antigüedad que "
                       + "calcular. Captúrala en la ficha del desarrollador y el saldo aparece solo.",
                Periodos: []);

        int anios = TablaDeVacacionesLft.AniosCumplidos(ingreso, corte);
        var periodos = Repartir(ingreso, anios, ventana, gozados, corte);

        int generados = periodos.Sum(p => p.Dias);
        int tomados = gozados.Sum(g => g.Dias);
        int caducados = periodos.Where(p => p.Caducado).Sum(p => p.Restantes);

        // La identidad que cualquiera puede comprobar sumando los renglones de la respuesta. Sale de
        // que todo lo generado o se gozó, o caducó, o sigue vivo — y lo que se gozó de más (más días
        // de los que había) entra restando, que es justo lo que deja el saldo en rojo.
        int vigentes = generados - tomados - caducados;

        var proximo = TablaDeVacacionesLft.Aniversario(ingreso, anios + 1);

        return new SaldoAcumuladoDto(
            TieneFicha: true,
            FechaDeIngreso: ingreso,
            AniosCumplidos: anios,
            ProximoAniversario: proximo,
            DiasDelProximoPeriodo: TablaDeVacacionesLft.DiasDelPeriodo(anios + 1),
            VentanaDeCaducidadMeses: ventana,
            DiasGenerados: generados,
            DiasTomados: tomados,
            DiasCaducados: caducados,
            DiasVigentes: vigentes,
            DiasComprometidos: comprometidos,
            AjusteManual: ficha.VacationAdjustmentDays,
            NotaDelAjuste: ficha.VacationAdjustmentNote,
            AutorDelAjuste: ficha.VacationAdjustmentBy,
            FechaDelAjusteUtc: ficha.VacationAdjustmentAtUtc,
            Mensaje: Mensaje(anios, proximo, periodos, corte),
            Periodos: periodos);
    }

    /// <summary>
    /// Reparte lo gozado entre los periodos ya cerrados y dice qué queda vivo y qué caducó.
    ///
    /// <para><b>El reparto es por orden de antigüedad (lo más viejo primero)</b>, y no es un detalle
    /// de implementación: es la única forma de no perder días teniendo saldo. Cargando lo que se
    /// toma contra el periodo más reciente, los días viejos se quedarían quietos hasta caducar
    /// mientras la persona gasta los nuevos — y acabaría perdiendo días con el saldo lleno.</para>
    ///
    /// <para>Cada solicitud se carga entera contra la fecha en que EMPIEZA. Unas vacaciones que
    /// cruzan la fecha de caducidad de un periodo se cargan, por tanto, contra el saldo que había el
    /// día que arrancaron. Es deliberado: las vacaciones se conceden como un bloque, y partirlas por
    /// una frontera invisible daría un descuento que nadie sabría reconstruir.</para>
    ///
    /// <para>Lo que no cabe en ningún periodo —se gozaron días que no se habían generado, o que ya
    /// habían caducado— no se pierde ni se calla: sigue contando en el total de días tomados, así
    /// que la identidad <c>vigentes = generados − tomados − caducados</c> lo deja en el saldo como
    /// números rojos. Ahí es donde el líder tiene que ajustar.</para>
    /// </summary>
    private static List<PeriodoDeVacacionesDto> Repartir(
        DateTime ingreso, int aniosCumplidos, int ventanaMeses,
        List<(DateTime Fecha, int Dias)> gozados, DateTime corte)
    {
        // Solo los periodos CERRADOS: los días se generan al cumplir el año de servicio, no durante.
        // El primer año, por tanto, no aparece hasta el primer aniversario, y por eso vale cero.
        var cierres = new DateTime[aniosCumplidos];
        var caduca = new DateTime[aniosCumplidos];
        var dias = new int[aniosCumplidos];
        var usados = new int[aniosCumplidos];

        for (int i = 0; i < aniosCumplidos; i++)
        {
            cierres[i] = TablaDeVacacionesLft.Aniversario(ingreso, i + 1);
            caduca[i] = cierres[i].AddMonths(ventanaMeses);
            dias[i] = TablaDeVacacionesLft.DiasDelPeriodo(i + 1);
        }

        foreach (var (fecha, cuantos) in gozados)
        {
            int porRepartir = cuantos;
            for (int i = 0; i < aniosCumplidos && porRepartir > 0; i++)
            {
                // Los periodos van en orden de cierre, así que en cuanto uno cierra después de la
                // fecha, los siguientes también: no había nada más generado ese día.
                if (cierres[i] > fecha) break;

                // Ya había caducado cuando se tomaron estos días: no se puede gastar de ahí.
                if (caduca[i] <= fecha) continue;

                int hueco = dias[i] - usados[i];
                if (hueco <= 0) continue;

                int usar = Math.Min(hueco, porRepartir);
                usados[i] += usar;
                porRepartir -= usar;
            }
            // Lo que sobra se gozó sin tenerlo. No se apunta contra ningún periodo a propósito: no
            // pertenece a ninguno. Aparece en el saldo por la vía del total de días tomados.
        }

        var filas = new List<PeriodoDeVacacionesDto>(aniosCumplidos);
        for (int i = 0; i < aniosCumplidos; i++)
            filas.Add(new PeriodoDeVacacionesDto(
                Anio: i + 1,
                Inicio: TablaDeVacacionesLft.Aniversario(ingreso, i),
                Cierre: cierres[i],
                Caduca: caduca[i],
                Dias: dias[i],
                Usados: usados[i],
                Restantes: dias[i] - usados[i],
                Caducado: caduca[i] <= corte));

        return filas;
    }

    /// <summary>
    /// La frase que acompaña al número. Existe por la regla del primer año: un cero sin fecha parece
    /// un error del programa, así que el mensaje tiene que decir CUÁNDO deja de ser cero y cuántos
    /// días serán —«el 05/01/2027 te corresponden 12 días»—.
    /// </summary>
    private static string Mensaje(
        int anios, DateTime proximoAniversario, List<PeriodoDeVacacionesDto> periodos, DateTime corte)
    {
        int diasDelProximo = TablaDeVacacionesLft.DiasDelPeriodo(anios + 1);

        string frase = anios == 0
            ? $"Todavía no cumples tu primer año, así que aún no tienes días generados: "
              + $"el {proximoAniversario:dd/MM/yyyy} te corresponden {diasDelProximo} días."
            : $"Llevas {anios} año(s) cumplidos. El {proximoAniversario:dd/MM/yyyy} se te suman "
              + $"{diasDelProximo} días.";

        // El aviso de lo que está por caducar va en el mismo mensaje y no en un campo aparte porque
        // es la única parte del saldo que hay que atender ANTES de una fecha: enseñarlo solo en el
        // desglose lo dejaría a la vista de quien despliegue el detalle, que es justo quien ya lo
        // sabe. Tres meses de aviso dan tiempo a pedir los días y a que alguien los apruebe.
        var proximaEnCaducar = periodos
            .Where(p => !p.Caducado && p.Restantes > 0 && p.Caduca <= corte.AddMonths(3))
            .OrderBy(p => p.Caduca)
            .FirstOrDefault();

        if (proximaEnCaducar is not null)
            frase += $" Ojo: {proximaEnCaducar.Restantes} día(s) caducan el "
                   + $"{proximaEnCaducar.Caduca:dd/MM/yyyy}.";

        return frase;
    }

    // ── El ajuste manual ─────────────────────────────────────────────────────────

    /// <summary>
    /// El líder corrige el saldo calculado de una persona, diciendo por qué.
    ///
    /// <para><b>Para qué existe.</b> Al arrancar la web no hay ni una solicitud de vacaciones
    /// registrada: el histórico se quedó en papel. Así que el cálculo le cuenta a cada quien todos
    /// los días que la ley le fue dando y ninguno gozado, y a alguien con cinco años de antigüedad
    /// le sale un saldo altísimo que no es verdad. Este ajuste es lo que lo vuelve verdad, y la nota
    /// es lo que permite defenderlo dentro de un año delante de quien reclame sus días.</para>
    ///
    /// <para>REEMPLAZA al ajuste anterior, no se suma: es «la corrección vigente de esta persona»,
    /// una sola. La historia de las correcciones es la bitácora, que además guarda el valor que
    /// había antes.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> RegistrarAjusteAsync(
        int developerId, int dias, string? nota, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuarioActual);

        // Solo el líder. La guarda está aquí además de en la política del endpoint porque a la API se
        // puede llamar sin pasar por el navegador, y esto mueve el saldo de otra persona.
        AuthorizationGuard.RequireAdmin(usuarioActual);

        if (dias < -MaxAjuste || dias > MaxAjuste)
            return (false, $"El ajuste tiene que estar entre −{MaxAjuste} y {MaxAjuste} días.");

        nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        if (nota is null)
            return (false, "El ajuste necesita una nota que diga por qué. Un saldo corregido sin "
                         + "motivo escrito no se puede explicar después.");

        // Se recorta en vez de rechazarse: la columna admite 500 y perder el final de una nota larga
        // es mejor que perder el ajuste entero por un carácter de más.
        if (nota.Length > 500) nota = nota[..500];

        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev is null) return (false, "Esa ficha ya no existe. Actualiza la lista.");

        // Lo que había antes, SOLO del ajuste. La bitácora no lleva la entidad entera a propósito:
        // ahí acabarían campos de la ficha que no tienen nada que ver con las vacaciones, y la
        // bitácora la lee más gente que la que puede abrir una ficha.
        var antes = new
        {
            Dias = dev.VacationAdjustmentDays,
            Nota = dev.VacationAdjustmentNote,
            Autor = dev.VacationAdjustmentBy,
            Cuando = dev.VacationAdjustmentAtUtc
        };

        dev.VacationAdjustmentDays = dias;
        dev.VacationAdjustmentNote = nota;
        dev.VacationAdjustmentBy = usuarioActual.Username ?? "sistema";
        dev.VacationAdjustmentAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var despues = new
        {
            Dias = dev.VacationAdjustmentDays,
            Nota = dev.VacationAdjustmentNote,
            Autor = dev.VacationAdjustmentBy,
            Cuando = dev.VacationAdjustmentAtUtc
        };

        // Bitácora DETALLADA y no la corta: de un ajuste de saldo hay que poder reconstruir qué
        // número había antes, no solo que alguien lo tocó. El nombre de la persona entra en el
        // detalle para que la línea se entienda sin ir a buscar la ficha por su identificador.
        await auditoria.RecordDetailedAsync(
            AuditAction.Update, "Developer", developerId.ToString(),
            $"Ajuste del saldo de vacaciones de «{dev.FullName}»: {dias:+#;-#;0} día(s). Motivo: {nota}",
            AuditOutcome.Exito, antes, despues, null, ct);

        return (true, dias == 0
            ? $"El ajuste de «{dev.FullName}» quedó en cero: su saldo vuelve a ser el calculado."
            : $"Ajuste guardado: {dias:+#;-#;0} día(s) sobre el saldo calculado de «{dev.FullName}».");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La ventana de caducidad configurada, acotada. Un valor fuera de rango —o basura— cae al valor
    /// por omisión en vez de propagarse: aquí un número absurdo no da error, da un saldo enorme que
    /// parece bueno, y ése es el peor fallo posible en un cálculo que alguien va a firmar.
    /// </summary>
    public async Task<int> VentanaDeCaducidadAsync(CancellationToken ct = default)
    {
        int meses = await ajustes.ObtenerEnteroAsync(ClaveCaducidadMeses, CaducidadMesesPorOmision, ct);
        return meses >= 1 && meses <= CaducidadMesesMaxima ? meses : CaducidadMesesPorOmision;
    }

    /// <summary>Días que cubre un rango, contando el primero y el último.</summary>
    private static int Dias(DateTime inicio, DateTime fin) => (fin.Date - inicio.Date).Days + 1;

    /// <summary>La respuesta para una cuenta sin ficha de desarrollador: no hay saldo del que hablar.</summary>
    private static SaldoAcumuladoDto SinFicha() => new(
        TieneFicha: false, FechaDeIngreso: null, AniosCumplidos: 0, ProximoAniversario: null,
        DiasDelProximoPeriodo: 0, VentanaDeCaducidadMeses: CaducidadMesesPorOmision,
        DiasGenerados: 0, DiasTomados: 0, DiasCaducados: 0, DiasVigentes: 0, DiasComprometidos: 0,
        AjusteManual: 0, NotaDelAjuste: null, AutorDelAjuste: null, FechaDelAjusteUtc: null,
        Mensaje: "Esta cuenta no tiene ficha de desarrollador, así que no tiene saldo de vacaciones.",
        Periodos: []);
}
