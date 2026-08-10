using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// El registro OFICIAL de asistencia: una entrada y una salida marcadas a mano, por acción
/// explícita de la persona. Es lo que cuenta como asistencia.
///
/// Convive con <see cref="WorkPresence"/>, que es otra cosa: la jornada que la aplicación abre y
/// cierra sola por el latido. Esa sirve para saber quién está conectado ahora y como CONTRASTE
/// —cuánto se parece lo marcado a lo que la máquina vio—, pero no como asistencia: la aplicación
/// puede quedarse abierta sola en un equipo encendido, y alguien puede trabajar sin abrirla.
/// Separarlas es lo que permite que ninguna de las dos mienta por la otra.
///
/// Un solo registro por día y por persona. La reentrada después de comer NO se marca: los tramos
/// de un día son asunto del cronómetro y de la telemetría, y un minutado de las pausas de alguien
/// es vigilancia, no asistencia — la misma decisión que ya está tomada en <see cref="WorkPresence"/>.
/// </summary>
public class AttendanceRecord
{
    public int Id { get; set; }

    /// <summary>
    /// Por UserId y NO por DeveloperId, igual que <see cref="WorkPresence"/>: la ficha es una copia
    /// opcional que se toma al marcar (hay cuentas sin ficha), y las marcas anteriores a ligarla la
    /// tienen en null — se perderían del total. UserId es la identidad de la sesión y además es lo
    /// que permite cruzar 1:1 contra la telemetría.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>Ficha del desarrollador si la cuenta la tiene ligada. Informativa.</summary>
    public int? DeveloperId { get; set; }

    /// <summary>Nombre con el que mostrarlo aunque después se borre el usuario o la ficha.</summary>
    public string DisplayName { get; set; } = "";

    public DateTime CheckInUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null mientras la persona no ha marcado su salida.</summary>
    public DateTime? CheckOutUtc { get; set; }

    /// <summary>
    /// Equipo desde el que se marcó. Uno por marca y no uno por registro a propósito: entrar en el
    /// equipo de la oficina y salir desde la laptop es normal, y guardar solo uno escondería el dato.
    /// </summary>
    public string? CheckInOrigin { get; set; }
    public string? CheckOutOrigin { get; set; }

    /// <summary>Notas cortas y opcionales de cada marca («llegué tarde por la junta con soporte»).</summary>
    public string? CheckInNote { get; set; }
    public string? CheckOutNote { get; set; }

    /// <summary>Null mientras el registro sigue abierto.</summary>
    public AttendanceCloseKind? CloseKind { get; set; }

    // ── Solicitud de corrección (la pide el dueño; NO edita) ─────────────
    /// <summary>
    /// Lo que la persona pide corregir. El desarrollador nunca cambia sus propias horas —si pudiera,
    /// el registro dejaría de probar nada—, pero tampoco puede quedarse sin forma de avisar de un
    /// error dentro de la aplicación; sin esto la discusión se va a un chat donde no queda rastro.
    /// </summary>
    public string? CorrectionRequestNote { get; set; }
    public DateTime? CorrectionRequestedAtUtc { get; set; }

    // ── Corrección del administrador ─────────────────────────────────────
    /// <summary>Quién corrigió, cuándo y por qué. Los valores anteriores quedan en la bitácora
    /// (OldValues/NewValues), que es donde vive el histórico; aquí solo la última decisión.</summary>
    public int? CorrectedByUserId { get; set; }
    public string? CorrectedByName { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }
    public string? CorrectionReason { get; set; }

    public bool Abierto => CheckOutUtc == null;

    /// <summary>Tiempo entre las dos marcas; null mientras no haya salida.</summary>
    public TimeSpan? Duracion => CheckOutUtc is DateTime fin ? fin - CheckInUtc : null;
}
