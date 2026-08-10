using System.Text;

namespace AdminWeb.Application.Services;

/// <summary>Un punto del checklist previo al despliegue.</summary>
/// <param name="Clave">Identificador estable: es lo que queda escrito en la evidencia.</param>
/// <param name="Texto">Lo que se le pregunta a la persona.</param>
/// <param name="Ayuda">Por qué importa. Un checklist que no explica se marca sin leer.</param>
public sealed record PuntoChecklist(string Clave, string Texto, string Ayuda);

/// <summary>
/// El checklist que hay que confirmar ANTES de desplegar a producción, y la evidencia que queda
/// escrita en el despliegue.
///
/// Existe porque Operaciones es el rol con menos pantallas y el de mayor riesgo por clic: una
/// confirmación de «¿seguro?» se contesta que sí por reflejo, mientras que marcar cuatro puntos
/// obliga a mirarlos. Y porque cuando algo sale mal, la pregunta que sigue —«¿se tomó respaldo?»,
/// «¿estaba autorizada la ventana?»— no tenía respuesta escrita en ninguna parte.
///
/// La evidencia se guarda en <c>DeploymentJob.Notes</c>, junto al despliegue y no en un archivo
/// aparte: separada del hecho que documenta, se pierde.
///
/// <para><b>Portado sin relajarlo, y con una barrera más.</b> En el escritorio quien exigía el
/// checklist era el diálogo: si otra ruta llamaba al servicio, desplegaba sin él. Aquí la exigencia
/// vive además en el servidor (ver <c>DeploymentService</c>), porque el diálogo corre en la máquina
/// del usuario y la API se puede llamar sin pasar por él.</para>
/// </summary>
public static class DeploymentChecklist
{
    /// <summary>
    /// Los puntos, fijos y en código a propósito: si fueran configurables, el primer despliegue
    /// apurado los dejaría vacíos y el checklist no protegería de nada.
    /// </summary>
    public static readonly PuntoChecklist[] Puntos =
    [
        new("version",   "Verifiqué que la versión es la correcta",
                         "El error más caro es desplegar la versión de ayer, y es el más fácil de cometer."),
        new("ventana",   "El despliegue está autorizado para esta ventana de tiempo",
                         "Fuera de la ventana acordada, una caída de dos minutos le pega a quien está trabajando."),
        new("aviso",     "Avisé a quien corresponde que voy a desplegar",
                         "Si el sistema se cae, alguien tiene que saber que fue el despliegue y no una falla."),
        new("reversion", "Sé cómo revertir si algo sale mal",
                         "El momento de averiguarlo no es cuando ya está roto."),
    ];

    /// <summary>Qué puntos faltan por marcar. Vacío = se puede desplegar.</summary>
    public static List<PuntoChecklist> Faltantes(IReadOnlyCollection<string> marcados) =>
        Puntos.Where(p => !marcados.Contains(p.Clave)).ToList();

    /// <summary>
    /// Mínimo de caracteres de la nota, que es obligatoria en todos los despliegues.
    ///
    /// Las casillas responden «¿se revisó?» y siempre salen marcadas —no se puede desplegar de otro
    /// modo—, así que por sí solas no distinguen un despliegue de otro. La nota es lo único que
    /// responde «¿por qué este despliegue, ahora?», y esa es justo la pregunta que nadie puede
    /// reconstruir un mes después.
    ///
    /// El mínimo es bajo a propósito: «CAB-233» es una justificación legítima y completa. Lo que se
    /// busca impedir es el punto o el espacio que se teclea para que el botón se encienda; medir la
    /// calidad de la nota no es trabajo de una validación.
    /// </summary>
    public const int MinimoNota = 3;

    /// <summary>Si la nota alcanza para valer como justificación escrita.</summary>
    public static bool NotaSuficiente(string? nota) =>
        (nota ?? string.Empty).Trim().Length >= MinimoNota;

    /// <summary>
    /// El texto que queda guardado con el despliegue. Es la evidencia: quién confirmó qué, cuándo,
    /// y los hechos del despliegue que NO dependen de que alguien los marque (versión, destino y
    /// respaldo salen del sistema, no de la buena fe de quien despliega).
    ///
    /// Acepta <paramref name="nota"/> vacía aunque la nota sea obligatoria: quien la exige es quien
    /// recibe la orden de desplegar. Este método solo redacta lo que pasó, y tiene que poder
    /// redactar también un despliegue sin nota —los anteriores a esta regla— sin inventar una.
    /// </summary>
    public static string Evidencia(
        string usuario, DateTime cuandoLocal, string version, string destino, string respaldo,
        IReadOnlyCollection<string> marcados, string? nota)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CHECKLIST PREVIO AL DESPLIEGUE");
        sb.AppendLine($"Confirmado por : {usuario}");
        sb.AppendLine($"Fecha y hora   : {cuandoLocal:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Versión        : {version}");
        sb.AppendLine($"Destino        : {destino}");
        sb.AppendLine($"Respaldo previo: {respaldo}");
        sb.AppendLine();
        foreach (var p in Puntos)
            sb.AppendLine($"  [{(marcados.Contains(p.Clave) ? "x" : " ")}] {p.Texto}");

        if (!string.IsNullOrWhiteSpace(nota))
        {
            sb.AppendLine();
            sb.AppendLine($"Nota: {nota.Trim()}");
        }
        return sb.ToString().TrimEnd();
    }
}
