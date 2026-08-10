using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Avisos;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lectura de la bandeja de avisos.
///
/// El alta, el conteo y el marcado siguen en <see cref="NotificationService"/>, que ya está portado
/// y es de donde salen los avisos; aquí solo vive lo que aquel no ofrece: la página de resultados y
/// la comprobación de a quién pertenece un aviso.
///
/// Va PAGINADO y el escritorio no lo estaba (leía los 100 últimos de un tirón). La bandeja crece con
/// cada asignación de DevOps, cada requerimiento y cada comunicado, y es de las tablas que no paran
/// de crecer nunca: traerla entera funciona el primer mes y deja de funcionar el segundo.
///
/// TODAS las consultas de aquí filtran por el usuario de la sesión y NUNCA por un identificador que
/// venga de fuera. Un aviso es correspondencia personal —quién te asignó qué, qué ticket te tocó— y
/// un id por parámetro es lo único que hace falta para leer la de otro.
/// </summary>
public class AvisosQueryService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Tope duro de filas por página. Pedir 10 000 no debe poder tumbar la respuesta.</summary>
    public const int MaxTamanoPagina = 100;

    public async Task<PaginaDto<AvisoDto>> PaginaAsync(int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        int usuario = currentUser.UserId ?? 0;

        // Se normaliza en vez de rechazar: una página fuera de rango es casi siempre un enlace viejo
        // o un clic de más, y devolver un error por eso solo consigue una pantalla en blanco.
        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, MaxTamanoPagina);

        var mios = db.Notifications.AsNoTracking().Where(n => n.ForUserId == usuario);

        int total = await mios.CountAsync(ct);

        var filas = await mios
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)   // desempate estable: sin él, dos avisos del mismo instante podrían saltar de página
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(n => new { n.Id, n.Kind, n.Title, n.Message, n.Url, n.CreatedAt, n.ReadAt })
            .ToListAsync(ct);

        var avisos = filas
            .Select(n => new AvisoDto(
                n.Id, n.Kind, EtiquetaDeTipo(n.Kind), n.Title, n.Message,
                Url: EnlaceSeguro(n.Url),
                CreadoUtc: DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                Leido: n.ReadAt != null))
            .ToList();

        return new PaginaDto<AvisoDto>(avisos, total, pagina, tamanoPagina);
    }

    /// <summary>
    /// ¿Este aviso es de quien lo está pidiendo? Lo usa el endpoint de «marcar leído» antes de tocar
    /// nada, porque <see cref="NotificationService.MarkReadAsync"/> recibe solo el id del aviso y no
    /// comprueba de quién es: en el escritorio el id salía siempre de la rejilla del propio usuario,
    /// pero aquí llega por la ruta y cualquiera puede escribir otro número.
    /// </summary>
    public Task<bool> EsMioAsync(int avisoId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        int usuario = currentUser.UserId ?? 0;
        return db.Notifications.AnyAsync(n => n.Id == avisoId && n.ForUserId == usuario, ct);
    }

    /// <summary>
    /// Devuelve el enlace solo si es http o https; cualquier otra cosa, null.
    ///
    /// El escritorio ya lo hacía porque este campo lo abría el SHELL y admitir cualquier esquema
    /// habría convertido un texto guardado en la base en una forma de ejecutar algo con un clic. En
    /// el navegador la regla vale doble: un <c>javascript:</c> aquí correría dentro de la sesión de
    /// quien abre el aviso. Se filtra al SALIR de la base y no al pintar, porque lo que no sale no se
    /// puede pintar por descuido.
    /// </summary>
    private static string? EnlaceSeguro(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? url.Trim()
            : null;

    /// <summary>Los mismos rótulos con ícono del escritorio, resueltos en el servidor.</summary>
    private static string EtiquetaDeTipo(NotificationKind k) => k switch
    {
        NotificationKind.DevOpsAssigned       => "🔷 Ticket DevOps",
        NotificationKind.RequirementAssigned  => "📋 Requerimiento",
        NotificationKind.FreshDeskAssigned    => "🎫 Ticket Freshdesk",
        NotificationKind.Comunicado           => "📢 Comunicado",
        NotificationKind.CompromisoPorVencer  => "⏱ Compromiso por vencer",
        _                                     => "🔔 Aviso"
    };
}
