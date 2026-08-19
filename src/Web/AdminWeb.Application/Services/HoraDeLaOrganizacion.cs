using System.Globalization;

namespace AdminWeb.Application.Services;

/// <summary>
/// La hora de la organización: un instante UTC dicho en la zona horaria que el líder configuró.
///
/// <para><b>Existe porque el servidor no vive donde vive la gente.</b> El App Service corre en UTC y
/// nadie le puso <c>WEBSITE_TIME_ZONE</c>, así que allá <c>ToLocalTime()</c> devuelve el propio UTC y
/// un tramo que empezó a las 15:30 se publica como las 21:30. Esto es SOLO para lo que se compone en
/// el servidor y viaja ya escrito —un comentario en Azure DevOps, un correo, un PDF—. En los
/// <c>.razor</c> no hace falta y sería un error usarlo: Blazor WebAssembly corre en el navegador de
/// la persona, y ahí «local» sí es su hora.</para>
///
/// <para><b>No lanza nunca, y ése es el requisito principal.</b> El identificador lo teclea alguien
/// en la pantalla de Configuración: puede estar mal escrito, o bien escrito y no existir en esta
/// máquina. Un comentario que no se publica porque sobró un espacio sería mucho peor que una hora
/// dicha en la zona por omisión, así que lo que no se resuelve cae en <see cref="ZonaPorOmision"/> y,
/// si ni eso existe, en UTC.</para>
/// </summary>
public static class HoraDeLaOrganizacion
{
    /// <summary>
    /// Ciudad de México, escrita en IANA. En IANA y no en el identificador de Windows porque ese
    /// catálogo existe en los DOS sistemas —producción es Linux— y porque no se rompe si alguien
    /// cambia el idioma del servidor.
    /// </summary>
    public const string ZonaPorOmision = "America/Mexico_City";

    /// <summary>Cómo se escribe aquí una fecha con hora. El mismo formato que usa el resto del
    /// sistema, para que un comentario no se lea distinto de la bitácora.</summary>
    public const string Formato = "dd/MM/yyyy HH:mm";

    /// <summary>
    /// La zona que toca, sin lanzar jamás.
    ///
    /// <para>Desde .NET 6 la búsqueda TRADUCE sola entre los dos catálogos: en Windows un
    /// identificador IANA se convierte al de Windows antes de buscarlo, y en Linux al revés. Por eso
    /// no hacen falta dos ramas ni detectar el sistema; lo único que hace falta es NO usar
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>, que lanza, y usar la variante
    /// <c>Try…</c>.</para>
    ///
    /// <para>La comprobación de vacío va ANTES de la llamada porque con <c>null</c> la variante
    /// <c>Try…</c> tampoco devuelve <c>false</c>: lanza. Solo se traga el identificador desconocido.</para>
    /// </summary>
    public static TimeZoneInfo Zona(string? identificador) =>
        Buscar(identificador) ?? Buscar(ZonaPorOmision) ?? TimeZoneInfo.Utc;

    private static TimeZoneInfo? Buscar(string? identificador) =>
        !string.IsNullOrWhiteSpace(identificador)
        && TimeZoneInfo.TryFindSystemTimeZoneById(identificador.Trim(), out var zona)
            ? zona
            : null;

    /// <summary>
    /// El instante UTC, movido a la zona.
    ///
    /// <para>El <c>SpecifyKind</c> no es adorno: <see cref="TimeZoneInfo.ConvertTimeFromUtc"/> lanza
    /// si el <c>DateTime</c> viene con <c>Kind == Local</c>. Lo que devuelve EF de la base viene como
    /// <c>Unspecified</c> y pasa bien, pero basta con que alguien meta un <c>DateTime.Now</c> por el
    /// camino para tumbar la publicación del comentario.</para>
    /// </summary>
    public static DateTime EnZona(DateTime utc, TimeZoneInfo zona) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona);

    /// <summary>
    /// El instante, escrito. <b>Con cultura invariante a propósito</b>: en «dd/MM/yyyy» la barra no
    /// es literal, la sustituye el separador de la cultura del proceso —en sueco saldría
    /// «18-08-2026» y en árabe otro calendario entero—. Y esa cultura no la fija nadie: sale del
    /// sistema operativo, así que sin esto el mismo código escribe una cosa en la máquina de
    /// desarrollo y otra en el App Service.
    /// </summary>
    public static string Texto(DateTime utc, TimeZoneInfo zona, string? formato = null) =>
        EnZona(utc, zona).ToString(formato ?? Formato, CultureInfo.InvariantCulture);

    /// <summary>
    /// Igual, con el huso dicho en claro: «18/08/2026 15:30 (UTC-06:00)».
    ///
    /// <para>Es la que debe usar un comentario de Azure DevOps. Allá lo firma el token de la
    /// instalación —una cuenta compartida— y lo puede leer alguien que no esté en esta zona; una hora
    /// suelta le diría a esa persona algo distinto de lo que pasó. El desfase se saca del INSTANTE y
    /// no de la zona, para que un día con cambio de horario no mienta.</para>
    /// </summary>
    public static string TextoConHuso(DateTime utc, TimeZoneInfo zona, string? formato = null)
    {
        var desfase = zona.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        var signo = desfase < TimeSpan.Zero ? "-" : "+";

        return Texto(utc, zona, formato)
             + $" (UTC{signo}{desfase.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)})";
    }
}
