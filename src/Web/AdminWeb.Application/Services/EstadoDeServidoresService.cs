using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Programados;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma la foto de «qué versión tiene cada servidor».
///
/// <para>El historial contesta «¿qué se desplegó?», ordenado por despliegue. Esto contesta la otra
/// mitad, que es la que se pregunta en caliente: «¿qué hay AHORA en cada servidor y a cuál le
/// falta?». Reconstruirlo desde el historial obliga a leerlo entero hacia atrás y cruzar perfiles, y
/// con selección directa de servidores ni siquiera basta con eso.</para>
///
/// <para><b>Todo se lee con <c>AsNoTracking</c>.</b> En el escritorio era obligatorio porque el
/// contexto era Singleton y compartido: sin eso, la resolución de identidad devolvía las instancias
/// rastreadas de antes del despliegue y esta pantalla mostraba, con toda seriedad, la versión
/// anterior. Aquí el contexto es uno por petición y ese riesgo desapareció, pero se conserva porque
/// sigue siendo lo correcto para una consulta que no escribe: ni rastreo ni copias que nadie usa.</para>
/// </summary>
public class EstadoDeServidoresService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>La foto completa, con su resumen ya redactado.</summary>
    /// <param name="incluirDadosDeBaja">
    /// Los servidores retirados. Fuera por omisión: lo que se pregunta a diario es por los que están
    /// en servicio, y mezclarlos infla la cuenta de «atrasados» con máquinas que ya no existen.
    /// </param>
    public async Task<EstadoDeServidoresDto> ObtenerAsync(
        bool incluirDadosDeBaja = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(currentUser);

        var servidores = await db.DeploymentTargets.AsNoTracking()
            .Include(t => t.LastRelease).ThenInclude(r => r!.AppSystem)
            .Where(t => incluirDadosDeBaja || t.IsActive)
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);

        if (servidores.Count == 0)
            return new EstadoDeServidoresDto([], 0, 0, 0, "Todavía no hay servidores dados de alta.");

        // Los nombres en UNA consulta: con veintitantos servidores, resolver el usuario fila por fila
        // son veintitantos viajes a una base remota.
        var usuarioIds = servidores
            .Where(t => t.LastDeployedById != null)
            .Select(t => t.LastDeployedById!.Value)
            .Distinct()
            .ToList();

        var quienes = (await db.Users.AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Username })
                .ToListAsync(ct))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);

        var ultimas = await UltimaVersionPorSistemaAsync(
            [.. servidores.Where(t => t.LastRelease != null).Select(t => t.LastRelease!.AppSystemId).Distinct()], ct);

        var ahora = DateTime.UtcNow;
        var filas = servidores.Select(t =>
        {
            var version = t.LastRelease?.Version;
            var ultima = t.LastRelease != null && ultimas.TryGetValue(t.LastRelease.AppSystemId, out var v) ? v : null;

            bool nunca = t.LastDeployedAt == null;
            bool atrasado = version != null && ultima != null &&
                            !string.Equals(version, ultima, StringComparison.OrdinalIgnoreCase);

            return new EstadoDeServidorDto(
                ServidorId: t.Id,
                Servidor: t.Nombre,
                Host: t.Host,
                Url: string.IsNullOrWhiteSpace(t.URL) ? null : t.URL,
                Activo: t.IsActive,
                Sistema: t.LastRelease?.AppSystem.Name,
                Version: version,
                UltimaVersionDelSistema: ultima,
                DesplegadoUtc: t.LastDeployedAt,
                Quien: t.LastDeployedById is int id && quienes.TryGetValue(id, out var n) ? n : null,
                DespliegueId: t.LastDeploymentJobId,
                NuncaDesplegado: nunca,
                Atrasado: atrasado,
                Antiguedad: Antiguedad(t.LastDeployedAt, ahora),
                EstadoTexto: Etiqueta(nunca, atrasado, ultima));
        }).ToList();

        int atrasados = filas.Count(f => f.Atrasado);
        int sinDesplegar = filas.Count(f => f.NuncaDesplegado);
        int alDia = filas.Count - atrasados - sinDesplegar;

        // Sin los símbolos que llevaba delante de cada cifra (✅ ⚠ ○), por lo mismo que la etiqueta de
        // cada fila: los dibuja el sistema operativo y donde falta la fuente salen como cuadros
        // vacíos. Aquí molestaban el doble, porque eran tres cuadros seguidos en una sola frase.
        var resumen = $"{filas.Count} servidor(es)  ·  {alDia} al día  ·  {atrasados} atrasado(s)  " +
                      $"·  {sinDesplegar} sin desplegar nunca";

        return new EstadoDeServidoresDto(filas, alDia, atrasados, sinDesplegar, resumen);
    }

    /// <summary>
    /// La misma foto en una hoja de cálculo.
    /// </summary>
    /// <param name="incluirDadosDeBaja">El mismo interruptor de la pantalla.</param>
    /// <param name="soloAtrasados">
    /// El otro filtro de la pantalla, que allí se aplica en el navegador sobre lo ya traído. Se
    /// repite aquí porque lo que se baja es <b>lo que el filtro está enseñando</b>, que es la decisión
    /// que ya se tomó en minutas: con «solo atrasados» marcado, quien pulsa exportar espera la lista
    /// corta que tiene delante y no las veintitantas máquinas del inventario.
    /// </param>
    public async Task<byte[]> ExcelAsync(
        bool incluirDadosDeBaja = false, bool soloAtrasados = false, CancellationToken ct = default)
    {
        // La guarda y el orden los pone ObtenerAsync; aquí solo se recorta y se escribe.
        var foto = await ObtenerAsync(incluirDadosDeBaja, ct);

        var filas = foto.Servidores
            .Where(s => !soloAtrasados || s.Atrasado)
            .Select(s => new object?[]
            {
                s.Servidor,
                s.Host,
                s.Sistema,
                s.Version,
                s.UltimaVersionDelSistema,
                s.EstadoTexto,
                s.DesplegadoUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                s.Antiguedad,
                // Nulo y no «—»: en la hoja, una celda vacía se lee como «no se registró» y además se
                // puede filtrar, mientras que una raya es un texto más con el que nadie puede contar.
                s.Quien,
                s.DespliegueId,
                s.Activo,
                s.Url
            })
            .ToList();

        return HojaDeCalculo.Escribir(
            ["Servidor", "Host", "Sistema", "Versión desplegada", "Última publicada", "Estado",
             "Últ. actualización", "Hace", "Quién lo desplegó", "Despliegue #", "Activo", "URL"],
            filas, "Servidores");
    }

    /// <summary>
    /// Versión más reciente registrada de cada sistema, por fecha de alta.
    ///
    /// <para>Por <c>CreatedAt</c> y no comparando el texto de la versión: «1.10» es posterior a «1.9»
    /// pero menor alfabéticamente, y no todos los sistemas numeran igual. La fecha de alta sí es un
    /// orden confiable con cualquier convención.</para>
    /// </summary>
    private async Task<Dictionary<int, string>> UltimaVersionPorSistemaAsync(
        List<int> sistemaIds, CancellationToken ct)
    {
        if (sistemaIds.Count == 0) return [];

        var ultimas = await db.AppReleases.AsNoTracking()
            .Where(r => sistemaIds.Contains(r.AppSystemId))
            .GroupBy(r => r.AppSystemId)
            .Select(g => new
            {
                AppSystemId = g.Key,
                Version = g.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                           .Select(r => r.Version).First()
            })
            .ToListAsync(ct);

        return ultimas.ToDictionary(x => x.AppSystemId, x => x.Version);
    }

    /// <summary>
    /// Cuánto lleva ese servidor con lo que tiene. Se redacta en el servidor para que diga lo mismo
    /// en la pantalla, en un reporte y en cualquier sitio donde acabe.
    /// </summary>
    private static string Antiguedad(DateTime? desplegadoUtc, DateTime ahoraUtc)
    {
        if (desplegadoUtc is not DateTime cuando) return "—";

        var d = ahoraUtc - cuando;
        if (d.TotalMinutes < 1) return "hace un momento";
        if (d.TotalMinutes < 60) return $"hace {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"hace {(int)d.TotalHours} h";
        if (d.TotalDays < 30) return $"hace {(int)d.TotalDays} d";
        return $"hace {(int)(d.TotalDays / 30)} meses";
    }

    /// <summary>
    /// La etiqueta de estado, con la PALABRA SOLA. «Desplegado» a secas cuando no se sabe cuál es la
    /// última publicada: decir «al día» sin poder compararlo sería afirmar algo que no se comprobó.
    ///
    /// <para>Los cuatro estados llevaban delante un símbolo (○ ⚠ ✅, y «Desplegado» ninguno, que ya
    /// era una incoherencia). Se fueron: los dibuja EL SISTEMA OPERATIVO y no nosotros, así que se ven
    /// distintos en cada equipo, NO heredan el color del texto y donde no hay fuente de emoji
    /// instalada salen como un CUADRO VACÍO — comprobado en una captura. En este rack pesaba de más,
    /// porque la etiqueta también viaja a una celda de la exportación a Excel, que se abre en un
    /// equipo del que no sabemos nada.</para>
    ///
    /// <para>La señal no se pierde. El DTO lleva los booleanos <c>NuncaDesplegado</c> y
    /// <c>Atrasado</c>, y de ellos —no de comparar esta palabra— sale ya el color de la columna en
    /// <c>EstadoDeServidores.razor</c>. Nadie coteja esta cadena por igualdad; si alguien empieza a
    /// hacerlo, que use esos booleanos.</para>
    /// </summary>
    private static string Etiqueta(bool nunca, bool atrasado, string? ultimaDelSistema)
    {
        if (nunca) return "Sin desplegar";
        if (atrasado) return "Atrasado";
        return ultimaDelSistema == null ? "Desplegado" : "Al día";
    }
}
