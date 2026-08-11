using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Ficha de un integrante del equipo. Misma tabla que el escritorio.
/// </summary>
public class Developer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }

    /// <summary>Teléfono de contacto. Libre a propósito: admite extensión, lada o formato local.</summary>
    public string? Phone { get; set; }

    public string? Seniority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Address { get; set; }

    /// <summary>Número de serie del equipo (computadora) asignado al desarrollador.</summary>
    public string? EquipmentSerial { get; set; }

    /// <summary>
    /// Los días de vacaciones que RH dejó escritos a mano en la ficha. <b>No es el saldo.</b>
    ///
    /// <para>Es un número suelto que alguien teclea (el botón «Calcular (LFT)» lo sugiere) y que
    /// nadie mantiene después: no baja al aprobarse unas vacaciones ni sube al cumplir años. Se
    /// conserva tal cual porque el escritorio lo escribe, sale impreso en el documento de
    /// vacaciones y hay histórico capturado ahí. El saldo de verdad lo calcula
    /// <c>SaldoDeVacacionesService</c> desde <see cref="HireDate"/>, y lo único que un humano
    /// escribe de ese cálculo es el ajuste de abajo.</para>
    /// </summary>
    public int VacationDaysLeft { get; set; } = 15;

    // ── El ajuste manual del saldo de vacaciones ─────────────────────────────────
    //
    // Es el ÚNICO dato del saldo que se guarda, y es a propósito: todo lo demás —los días que da la
    // ley por antigüedad, lo gozado, lo que caducó— se DERIVA de la fecha de ingreso y de las
    // solicitudes cada vez que alguien pregunta. Un saldo guardado como número se desincroniza el
    // primer día que alguien cancele unas vacaciones por otro camino, y a partir de ahí miente sin
    // que nada avise.
    //
    // Existe porque el cálculo, sin histórico, sale muy alto: al arrancar la web no hay ni una
    // solicitud registrada, así que a alguien con cinco años de antigüedad el sistema le cuenta
    // todos los días que la ley le fue dando y ninguno gozado. El líder corrige ese número diciendo
    // por qué, y esa frase es lo que vuelve verdad el saldo.
    //
    // Se guarda el ajuste VIGENTE, no la historia: la historia es la bitácora, que además registra
    // quién lo hizo y desde dónde. Al capturar uno nuevo se reemplaza el anterior.

    /// <summary>Días que el líder suma (positivo) o resta (negativo) al saldo calculado.</summary>
    public int VacationAdjustmentDays { get; set; }

    /// <summary>Por qué. Sin nota no se guarda ajuste: un número corregido sin motivo no se puede
    /// defender delante de quien reclama sus días.</summary>
    public string? VacationAdjustmentNote { get; set; }

    /// <summary>Quién lo capturó, por nombre de cuenta. Texto y no clave foránea, igual que
    /// <c>AuditLog.UserName</c>: dar de baja al usuario no debe borrar de quién fue la decisión.</summary>
    public string? VacationAdjustmentBy { get; set; }

    /// <summary>Cuándo, en UTC. Sirve para saber si el ajuste sigue hablando del saldo de hoy o si
    /// se quedó viejo tras un año más de antigüedad.</summary>
    public DateTime? VacationAdjustmentAtUtc { get; set; }

    public int? TeamId { get; set; }
    public TeamRole TeamRole { get; set; } = TeamRole.SinRol;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Equipo al que pertenece, o null si está sin equipo. Es la otra punta de <see cref="Team.Members"/>.</summary>
    public Team? Team { get; set; }

    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
