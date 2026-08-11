namespace AdminWeb.Client.Paginas.Pool;

/// <summary>
/// Cómo se escriben y cómo se acotan las HORAS en las dos pantallas del pool.
///
/// <para>Vive aquí, en la carpeta de las dos pantallas, por lo mismo que
/// <c>Paginas/Foro/TiempoDelForo.cs</c>: es formato de interfaz, no una regla, y hacen falta las
/// mismas frases en «Mi pool» y en la pantalla del líder. Escribirlas dos veces garantizaría que un
/// día una diga «sin límite» y la otra «—» para el mismo cero.</para>
///
/// <para><b>Todo el pool está en HORAS y ya no hay días en ninguna parte.</b> El motivo no es
/// cosmético: los números del pool existen para poder contrastarse con lo que miden los CRONÓMETROS,
/// que registran horas. Una estimación en días no se compara con un cronómetro sin inventarse cuánto
/// dura un día, y ese invento es justo lo que hacía incomparables los números. Si alguien vuelve a
/// meter «días» en un rótulo de estas pantallas, rompe esa comparación sin tocar una sola línea de
/// cálculo.</para>
/// </summary>
public static class HorasDelPool
{
    // ── Los topes ────────────────────────────────────────────────────────────
    //
    // Son una COPIA de los del servidor (PoolActivityService.MaxHorasDePlazo, MinHorasEstimadas y
    // MaxHorasEstimadas) y están aquí porque el cliente no puede referenciar la capa de aplicación:
    // es una frontera dura de este proyecto. Sirven SOLO para acotar el cuadro de captura, que es
    // comodidad; quien decide sigue siendo el servidor, y su mensaje es el que se enseña tal cual
    // cuando rechaza algo.
    //
    // Si algún día cambian allá y no aquí, no se cuela nada malo —el servidor rechaza—, pero el
    // cuadro dejará escribir un número que después se rebota. La forma de que no pase es subirlos a
    // Shared, que hoy no los tiene; queda dicho aquí para quien lo haga.

    /// <summary>Tope del PLAZO: 2 920 h = los 365 días de antes por una jornada de ocho.</summary>
    public const decimal MaxPlazo = 2920m;

    /// <summary>
    /// Piso del ESFUERZO: el cuarto de hora, la granularidad con la que ya se estima en la pantalla
    /// de tickets. Con un piso de cero, «0» pasaría por estimación y dejaría la comparación contra el
    /// cronómetro sin nada al otro lado.
    /// </summary>
    public const decimal MinEsfuerzo = 0.25m;

    /// <summary>Tope del ESFUERZO: mil horas son medio año de una persona; más que eso es un
    /// proyecto que hay que partir, no una actividad.</summary>
    public const decimal MaxEsfuerzo = 1000m;

    /// <summary>El salto de las flechitas del cuadro de captura, en cuartos de hora.</summary>
    public const string Salto = "0.25";

    // ── Los textos ───────────────────────────────────────────────────────────

    /// <summary>
    /// Un número de horas cualquiera: «4 h», «0.25 h», «64 h». Sin ceros de relleno, porque una
    /// columna de «4.00 h» se lee peor y aquí los decimales son la excepción.
    /// </summary>
    public static string Escribir(decimal horas) => $"{horas:0.##} h";

    /// <summary>
    /// El ESFUERZO estimado. Nulo no es cero: es «todavía nadie lo ha estimado», que es el estado
    /// normal de un bug que sigue en el pool —lo escribe quien lo tome, al tomarlo—.
    /// </summary>
    public static string Esfuerzo(decimal? horas) => horas is decimal h ? Escribir(h) : "—";

    /// <summary>
    /// El PLAZO. El cero está cargado de significado y no se puede pintar como «0 h»: quiere decir
    /// <b>sin fecha límite</b>, y enseñarlo como un cero haría leer «hay que entregarlo ya» donde
    /// dice justo lo contrario. Nulo se pinta igual que el cero porque en el plazo efectivo —el que
    /// ya viene resuelto contra la matriz— los dos significan lo mismo.
    /// </summary>
    public static string Plazo(decimal? horas) =>
        horas is not decimal h || h <= 0 ? "sin límite" : Escribir(h);

    /// <summary>
    /// En qué momento del CALENDARIO caería un plazo de tantas horas si se tomara ahora mismo.
    ///
    /// <para>Es la frase más importante de las dos pantallas y por eso se calcula y se enseña, en vez
    /// de dejar el número de horas a solas. Los plazos son horas de RELOJ: se suman sobre el instante
    /// de tomarla e incluyen noches y fines de semana, que es lo coherente con medir contra un
    /// cronómetro. Así, 64 h no vencen dentro de ocho jornadas sino dentro de dos días y dieciséis
    /// horas, y quien lo lea tiene que poder verlo antes de comprometerse y no descubrirlo al día
    /// siguiente.</para>
    ///
    /// <para>Es aproximado a propósito y el texto que lo acompaña lo dice: aquí se cuenta desde el
    /// reloj del navegador, y el plazo de verdad lo fija el servidor cuando gana el reclamo.</para>
    /// </summary>
    public static string CaeriaEl(decimal horas) =>
        DateTime.Now.AddHours((double)horas).ToString("dd/MM/yyyy HH:mm");

    /// <summary>
    /// Un instante ya fijado —el plazo de una actividad tomada— en hora local y <b>con la hora a la
    /// vista</b>.
    ///
    /// <para>Con la hora y no solo el día, que es como se pintaba cuando esto iba en días: con plazos
    /// de cuatro u ocho horas, una fecha a secas es un plazo falso —«hoy» no dice si vence a las once
    /// de la mañana o a las siete de la tarde— y quien la lea creerá que le queda el día entero. Es
    /// el mismo criterio con el que el servicio redacta el mensaje al tomar la actividad.</para>
    /// </summary>
    public static string Limite(DateTime? utc) =>
        utc is DateTime f ? f.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "—";
}
