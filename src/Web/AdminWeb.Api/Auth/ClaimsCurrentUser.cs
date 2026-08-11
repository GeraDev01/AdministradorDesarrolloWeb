using System.Security.Claims;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Auth;

/// <summary>Los nombres de los claims, en un solo sitio para que emisión y lectura no discrepen.</summary>
public static class ClaimsPersonalizados
{
    public const string DeveloperId = "developer_id";
    public const string SecurityStamp = "security_stamp";
    public const string MustChangePassword = "pwd_change";

    /// <summary>
    /// La cuenta entró pero todavía no tiene segundo factor: hay que llevarla a activarlo y no
    /// dejarla hacer nada más.
    ///
    /// <para>Va como claim y no se consulta la base en cada petición por la misma razón que
    /// <see cref="MustChangePassword"/>: son miles de peticiones al día y el dato cambia una vez en
    /// la vida de cada cuenta. La cookie se reemite en cuanto el alta se confirma, así que el claim
    /// no puede quedarse viejo por el lado que importaría — y si se quedara, el efecto sería pedir
    /// un alta ya hecha, no dejar entrar a quien no debe.</para>
    /// </summary>
    public const string SegundoFactorPendiente = "2fa_setup";
}

/// <summary>
/// La identidad de quien hace la petición, leída de los claims de su cookie.
///
/// Es la implementación que el escritorio dejó anotada como pendiente cuando escribió
/// <c>ICurrentUser</c> («una futura versión web la implemente por-request desde los claims»). Se
/// registra como scoped: una instancia por petición, sin estado compartido — el contraste exacto
/// con el singleton del escritorio, que era la identidad del único usuario de ese proceso.
/// </summary>
public class ClaimsCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Username => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? FullName => Principal?.FindFirstValue(ClaimTypes.GivenName) ?? Username;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var r) ? r : null;

    public int? DeveloperId =>
        int.TryParse(Principal?.FindFirstValue(ClaimsPersonalizados.DeveloperId), out var id) ? id : null;

    public bool IsLoggedIn => Principal?.Identity?.IsAuthenticated == true && UserId != null;

    // Comprobaciones POSITIVAS por rol, nunca por descarte: así un rol nuevo no hereda permisos
    // ajenos en silencio. Es la misma lección que el escritorio dejó escrita.
    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsDesarrollador => Role == UserRole.Desarrollador;
    public bool IsOperaciones => Role == UserRole.Operaciones;
}
