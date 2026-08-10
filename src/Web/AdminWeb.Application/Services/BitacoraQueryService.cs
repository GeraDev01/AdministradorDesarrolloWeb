using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Bitacora;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La consulta de la bitácora. La ESCRITURA vive en <see cref="AuditService"/>; aquí solo se lee.
///
/// Es la tabla que más crece de todo el sistema: cada inicio de sesión, cada alta, cada despliegue
/// deja una fila y nada las borra. Por eso se pagina EN EL SERVIDOR y no hay ningún método que
/// devuelva «todo». El escritorio la traía entera y filtraba en memoria (<c>AsEnumerable()</c>
/// antes de los Where); eso funcionaba con un proceso y unos miles de filas, y en la web sería un
/// <c>SELECT *</c> por cada persona que abra la pantalla.
///
/// Quién puede leerla: solo el líder. Una bitácora que puede consultar cualquiera deja de servir
/// para vigilar a nadie.
/// </summary>
public class BitacoraQueryService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Tope duro del tamaño de página. Sin él, <c>?tamano=100000</c> convierte la consulta
    /// paginada en la consulta completa que precisamente se quería evitar.</summary>
    public const int MaxTamanoPagina = 200;

    public const int TamanoPaginaPorOmision = 50;

    /// <summary>
    /// Una página de la bitácora, de lo más reciente a lo más antiguo.
    /// </summary>
    /// <param name="desde">Instante inicial INCLUSIVO, en UTC.</param>
    /// <param name="hasta">Instante final EXCLUSIVO, en UTC. Lo calcula quien llama a partir del
    /// día que eligió la persona: la conversión de «hasta el 5 de agosto» a un instante depende del
    /// huso de quien mira, y el servidor no lo sabe.</param>
    /// <param name="texto">Busca en los detalles y en el tipo de entidad, como el escritorio.</param>
    public async Task<PaginaDto<EntradaBitacoraDto>> BuscarAsync(
        DateTime? desde = null,
        DateTime? hasta = null,
        string? usuario = null,
        AuditAction? accion = null,
        string? texto = null,
        int pagina = 1,
        int tamanoPagina = TamanoPaginaPorOmision,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, MaxTamanoPagina);

        var q = db.AuditLogs.AsNoTracking().AsQueryable();

        if (desde is { } d) q = q.Where(l => l.Timestamp >= d);
        if (hasta is { } h) q = q.Where(l => l.Timestamp < h);

        usuario = (usuario ?? "").Trim();
        if (usuario.Length > 0) q = q.Where(l => l.UserName == usuario);

        if (accion is { } a) q = q.Where(l => l.Action == a);

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            q = q.Where(l => (l.Details != null && l.Details.Contains(texto))
                          || (l.EntityType != null && l.EntityType.Contains(texto)));

        // El total se cuenta con los MISMOS filtros: es lo que le dice a la pantalla cuántas
        // páginas hay, y contar sobre otra cosa daría un paginador que promete filas inexistentes.
        var total = await q.CountAsync(ct);

        var filas = await q
            // Desempate por Id: dos entradas de la misma operación comparten el milisegundo, y sin
            // un orden total la fila que cae en el corte de una página puede repetirse en la
            // siguiente (o no salir en ninguna).
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(l => new
            {
                l.Id, l.Timestamp, l.UserName, l.Action, l.EntityType, l.EntityId,
                l.Details, l.Outcome, l.Origin, l.CorrelationId
                // OldValues/NewValues quedan fuera: son JSON completos de la entidad y engordarían
                // la página sin que la rejilla los muestre. Su sitio es la ficha de una entrada.
            })
            .ToListAsync(ct);

        var dtos = filas.Select(l => new EntradaBitacoraDto(
            l.Id,
            DateTime.SpecifyKind(l.Timestamp, DateTimeKind.Utc),
            l.UserName,
            l.Action, EtiquetasDeCatalogo.Accion(l.Action),
            l.EntityType, l.EntityId, l.Details,
            l.Outcome, EtiquetasDeCatalogo.Resultado(l.Outcome),
            l.Origin, l.CorrelationId)).ToList();

        return new PaginaDto<EntradaBitacoraDto>(dtos, total, pagina, tamanoPagina);
    }

    /// <summary>
    /// Los usuarios que aparecen en la bitácora, para el desplegable del filtro.
    ///
    /// Se lee de la propia bitácora y no de la tabla de usuarios a propósito: hay entradas de
    /// cuentas ya borradas y de «sistema», y si el filtro solo ofreciera cuentas vigentes esas
    /// entradas quedarían fuera del alcance de quien investiga.
    /// </summary>
    public async Task<OpcionesDeBitacoraDto> OpcionesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var usuarios = await db.AuditLogs.AsNoTracking()
            .Select(l => l.UserName)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync(ct);

        return new OpcionesDeBitacoraDto(usuarios);
    }
}
