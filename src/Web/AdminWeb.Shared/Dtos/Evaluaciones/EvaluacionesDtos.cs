using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Evaluaciones;

/// <summary>
/// Una evaluación de líder: fortalezas, debilidades, calificación y comentarios de un periodo.
///
/// El texto viaja EN CRUDO, sin marcado, y así se pinta. Lo escribió una persona sobre otra persona
/// y acaba en su expediente: interpretarlo como HTML convertiría el campo «debilidades» en una vía
/// para inyectar marcado en la pantalla de quien lo lee.
/// </summary>
/// <param name="Calificacion">1 a 5, o null si el líder no quiso calificar. Es la escala de la ficha
/// (<c>DeveloperEvaluation.OverallRating</c>) y la que el escritorio pinta como estrellas.</param>
/// <param name="Evaluador">Nombre de quien evaluó, tal como estaba al momento de evaluar.</param>
public record EvaluacionDto(
    int Id,
    DateTime Fecha,
    string? Periodo,
    int? Calificacion,
    string? Fortalezas,
    string? Debilidades,
    string? Comentarios,
    string? Evaluador)
{
    /// <summary>
    /// «4 / 5» o «Sin calificar». La frase se sigue armando en el contrato —y no en cada pantalla—
    /// porque la enseñan las dos: la del líder y la del desarrollador, y tienen que decir lo mismo.
    ///
    /// <para>AQUÍ SE QUEDA SOLO EL NÚMERO. Antes devolvía «★★★★☆  (4/5)», y las estrellas se fueron a
    /// las dos pantallas como iconos de nuestra fuente. El motivo: ★ y ☆ no son emoji, pero tampoco
    /// son nuestros —los dibuja la fuente de TEXTO del equipo que abra la aplicación, y donde no
    /// estén salen como un cuadro—. Un contrato es una CADENA y no admite marcado, así que la forma
    /// solo puede ponerla quien tiene marcado, que es la pantalla. No vuelvas a meter aquí ningún
    /// glifo «porque en la rejilla se ve soso»: se vería soso otra vez en el equipo de al lado.</para>
    ///
    /// <para>El número NO se recorta a 5 aunque las estrellas sí: si algún día entrara un 7 por
    /// donde no debe, aquí se lee «7 / 5» y se ve el problema en vez de taparlo. El rango lo valida
    /// el servicio, que es donde la regla tiene que vivir.</para>
    /// </summary>
    public string Estrellas => Calificacion is not int r || r < 1
        ? "Sin calificar"
        : $"{r} / 5";

    /// <summary>Fortalezas y debilidades en una línea, para la columna de resumen de la rejilla.</summary>
    public string Resumen
    {
        get
        {
            var partes = new[] { Fortalezas, Debilidades }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Replace("\r", " ").Replace("\n", " "));
            var texto = string.Join("   ·   ", partes);
            return texto.Length == 0 ? "—" : texto.Length <= 130 ? texto : texto[..130] + "…";
        }
    }
}

/// <summary>
/// Los textos de esta pantalla que necesitan LOS DOS lados: el servidor para llenar los campos
/// «…Texto» de los DTO y el cliente para pintar el desplegable de tipos de hito sin pedirle al
/// servidor una lista que no cambia nunca. Mismo criterio que <c>EtiquetasDeCatalogo</c>.
///
/// Van SIN EMOJI, por lo que explica <c>EtiquetasDeCatalogo</c>: los dibuja el sistema operativo, no
/// obedecen al tema y donde falta la fuente salen como un cuadro. La paridad con el escritorio
/// (<c>DeveloperReportsControl.KindLabel</c>) caducó con el escritorio; no los repongas.
/// </summary>
public static class EtiquetasDeEvaluacion
{
    /// <summary>Cómo se lee un tipo de hito.</summary>
    public static string TipoDeHito(MilestoneKind k) => k switch
    {
        MilestoneKind.Logro          => "Logro",
        MilestoneKind.Proyecto       => "Proyecto",
        MilestoneKind.Certificacion  => "Certificación",
        MilestoneKind.Reconocimiento => "Reconocimiento",
        // El «•» que llevaba éste tampoco era emoji, pero era el sustituto pobre de los cuatro de
        // arriba: sin ellos quedaba como una viñeta suelta en medio de la columna.
        _                            => "Otro"
    };

    /// <summary>Los tipos en el orden en que se ofrecen, con su texto ya resuelto.</summary>
    public static IReadOnlyList<OpcionDeHito> TiposDeHito { get; } =
        [.. Enum.GetValues<MilestoneKind>().Select(k => new OpcionDeHito(k, TipoDeHito(k)))];
}

/// <summary>Una opción del desplegable de tipos de hito.</summary>
public record OpcionDeHito(MilestoneKind Valor, string Texto);

/// <summary>Un hito: una entrega importante, una certificación, un reconocimiento.</summary>
/// <param name="TipoTexto">«Logro», «Certificación»… resuelto en el servidor para que las dos
/// pantallas que lo enseñan no lo escriban distinto.</param>
public record HitoDto(
    int Id,
    DateTime Fecha,
    MilestoneKind Tipo,
    string TipoTexto,
    string Titulo,
    string? Descripcion);

/// <summary>
/// La ficha de evaluaciones e hitos de un desarrollador, tal como la ve el LÍDER: con los
/// identificadores, porque desde ahí se edita y se borra.
/// </summary>
public record EvaluacionesDeDesarrolladorDto(
    int DesarrolladorId,
    string Desarrollador,
    IReadOnlyList<EvaluacionDto> Evaluaciones,
    IReadOnlyList<HitoDto> Hitos);

/// <summary>
/// Lo que ve un DESARROLLADOR de sus propias evaluaciones e hitos: exactamente lo suyo y nada más.
///
/// Es otro contrato y no el mismo recortado a propósito, igual que <c>RankingPublicoFilaDto</c> en
/// desempeño: aquí no viaja ningún identificador de desarrollador, porque el servidor ya resolvió
/// de quién es la ficha a partir de la sesión y con un id en el JSON alguien podría intentar pedir
/// la de otro por su cuenta.
/// </summary>
/// <param name="TieneFicha">Falso si la cuenta no está ligada a una ficha. La pantalla se enseña
/// igual, explicando por qué está vacía: es mejor que un blanco sin motivo.</param>
public record MisEvaluacionesDto(
    bool TieneFicha,
    string Desarrollador,
    IReadOnlyList<EvaluacionDto> Evaluaciones,
    IReadOnlyList<HitoDto> Hitos);

/// <summary>
/// Alta o edición de una evaluación. <paramref name="Id"/> nulo o cero es un alta.
///
/// No trae evaluador ni fecha de captura: los pone el servidor con la identidad de la sesión. Que el
/// cliente pudiera escribir quién evaluó vaciaría de sentido el campo.
/// </summary>
public record GuardarEvaluacionRequest(
    int? Id,
    int DesarrolladorId,
    DateTime Fecha,
    string? Periodo,
    int? Calificacion,
    string? Fortalezas,
    string? Debilidades,
    string? Comentarios);

/// <summary>Alta o edición de un hito. <paramref name="Id"/> nulo o cero es un alta.</summary>
public record GuardarHitoRequest(
    int? Id,
    int DesarrolladorId,
    DateTime Fecha,
    MilestoneKind Tipo,
    string Titulo,
    string? Descripcion);
