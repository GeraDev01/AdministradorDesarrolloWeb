using AdminWeb.Application.Services;

namespace AdminWeb.Api.Auth;

/// <summary>
/// De dónde salió la petición, para la bitácora: IP del cliente y navegador.
///
/// Sustituye al «MÁQUINA\usuario» del escritorio, que aquí sería siempre el nombre del servidor y
/// no distinguiría a nadie. Durante el corte tiene un uso extra: es lo que permite comprobar en la
/// bitácora que ya nadie escribe desde el .exe.
/// </summary>
public class HttpRequestOrigin(IHttpContextAccessor accessor) : IRequestOrigin
{
    public string Describir()
    {
        var ctx = accessor.HttpContext;
        if (ctx == null) return "(servidor)";

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
        var agente = ctx.Request.Headers.UserAgent.ToString();
        if (agente.Length > 120) agente = agente[..120];

        return string.IsNullOrWhiteSpace(agente) ? ip : $"{ip} · {agente}";
    }
}

/// <summary>Origen para los trabajos de fondo, que no tienen petición detrás.</summary>
public class OrigenDeJob(string nombreDelJob) : IRequestOrigin
{
    public string Describir() => $"(job: {nombreDelJob})";
}
