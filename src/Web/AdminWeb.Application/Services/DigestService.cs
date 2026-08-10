using System.Globalization;
using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Correo;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Las cifras que lleva el resumen.</summary>
public record DatosDelResumen(
    int SlaVencidos,
    int SlaPorVencer24,
    int SlaActivos,
    int SugerenciasNuevas,
    int DesplieguesUlt24,
    int TicketsSyncUlt24,
    int ReqPorEntregar7d);

/// <summary>
/// Cómo está configurado el resumen.
/// </summary>
/// <param name="UltimaCorridaUtc">Null cuando nunca se ha enviado; entonces toca en la primera vuelta.</param>
public record ConfiguracionDelResumen(
    bool Activo,
    int CadaDias,
    IReadOnlyList<string> Destinatarios,
    DateTime? UltimaCorridaUtc);

/// <summary>
/// El resumen que le llega por correo al líder: SLA vencidos y por vencer, sugerencias sin atender,
/// despliegues y tickets del día, y las entregas de la semana que viene.
///
/// <para>Portado del <c>DigestService</c> del escritorio. Las cifras, los textos y las reglas son
/// las mismas; lo que cambia es de dónde sale el disparo y cómo se recuerda que ya se envió.</para>
///
/// <para>── <b>LO QUE CAMBIA RESPECTO DEL ESCRITORIO</b> ────────────────────────────────────</para>
///
/// <para>· <b>El contexto de datos ya no se fabrica a mano.</b> Allí había que abrir un
/// <c>AppDbContext</c> nuevo para recopilar («contexto FRESCO») porque el de la aplicación era un
/// Singleton compartido con la interfaz y traía entidades rastreadas de hace horas. Aquí el contexto
/// es scoped y el trabajo de fondo abre su propio ámbito en cada vuelta: el que se inyecta ya es
/// nuevo, y crear otro solo escondería que se entendió mal por qué existía aquel.</para>
///
/// <para>· <b>El freno de una hora desaparece</b>, y su trabajo lo hace ahora el temporizador del
/// trabajo de fondo. En el escritorio el servicio era un Singleton y guardaba en memoria cuándo lo
/// había intentado, para que un envío que fallara no se reintentara en cada latido —el latido era de
/// segundos—. Un servicio scoped no puede recordar nada entre vueltas, y tampoco hace falta: el
/// ritmo lo pone ahora el temporizador del trabajo de fondo, que corre cada media hora.</para>
///
/// <para>· <b>La marca de «ya se envió» se escribe directamente en la tabla de configuración</b> y
/// no por <c>SettingsService</c>. Ese servicio exige ser administrador y anota un cambio de
/// configuración en la bitácora, y las dos cosas están bien para la pantalla de Configuración: son
/// filas que deciden a qué servidor se despliega. Pero esto no es una configuración que alguien
/// ajuste, es el apunte que el propio trabajo deja de sí mismo, y el trabajo corre sin nadie detrás.
/// La clave es la misma que usa el escritorio, porque hasta el corte los dos leen esta fila.</para>
///
/// <para>Se conservan tal cual las dos reglas que evitan el ruido: <b>no se manda un correo vacío</b>
/// —si no hay nada que reportar se da el período por cumplido y ya— y <b>el período solo se marca
/// cuando el envío salió bien</b>, para que un servidor de correo caído no se coma el resumen del
/// día.</para>
/// </summary>
public class DigestService(
    AppDbContext db,
    SettingsService ajustes,
    IClienteDeCorreo correo,
    AuditService bitacora,
    ICurrentUser usuarioActual)
{
    // Las claves son EXACTAMENTE las del escritorio: hasta el corte, las dos aplicaciones leen y
    // escriben estas mismas filas. Renombrarlas aquí haría que el resumen se enviara dos veces.
    public const string ClaveActivo = "DigestEnabled";
    public const string ClaveFrecuencia = "DigestFrequencyDays";
    public const string ClaveDestinatarios = "DigestRecipients";
    public const string ClaveUltimaCorrida = "DigestLastRunUtc";

    /// <summary>
    /// El resumen lo lee gente que trabaja en español y lo escribe un servidor que probablemente
    /// corra en inglés. Sin fijar la cultura, el día de la semana saldría «Wednesday».
    /// </summary>
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

    // ── Configuración ────────────────────────────────────────────────────────────

    /// <summary>Cómo está configurado el resumen ahora mismo.</summary>
    public async Task<ConfiguracionDelResumen> ConfiguracionAsync(CancellationToken ct = default)
    {
        var destinos = Destinatarios(
            await ajustes.ObtenerAsync(ClaveDestinatarios, ct),
            await ajustes.ObtenerAsync(SettingsService.Claves.SlaEscalationEmail, ct),
            await ajustes.ObtenerAsync(SettingsService.Claves.EmailAddress, ct));

        return new ConfiguracionDelResumen(
            Activo: await ajustes.ObtenerBooleanoAsync(ClaveActivo, ct),
            CadaDias: Math.Max(1, await ajustes.ObtenerEnteroAsync(ClaveFrecuencia, 1, ct)),
            Destinatarios: destinos,
            UltimaCorridaUtc: LeerFecha(await ajustes.ObtenerAsync(ClaveUltimaCorrida, ct)));
    }

    /// <summary>
    /// A quién se le manda: a los configurados; si no hay, al buzón de escalamiento de SLA; y si
    /// tampoco, a la propia cuenta de la aplicación. La cascada es la del escritorio: el resumen
    /// llega a alguien aunque nadie haya rellenado el campo.
    /// </summary>
    public static List<string> Destinatarios(string? configurados, string? escalamiento, string? propio)
    {
        var lista = AjustesDeCorreo.Separar(configurados);
        if (lista.Count > 0) return lista;

        lista = AjustesDeCorreo.Separar(escalamiento);
        if (lista.Count > 0) return lista;

        return string.IsNullOrWhiteSpace(propio) || !propio.Contains('@') ? [] : [propio.Trim()];
    }

    // ── El disparo automático ────────────────────────────────────────────────────

    /// <summary>
    /// Envía el resumen si toca. Devuelve true solo si salió un correo.
    ///
    /// <para>Es lo que llama el trabajo de fondo, así que <b>no lleva guarda de rol</b>: no hay
    /// sesión detrás. Lo que autoriza esta ejecución es la configuración del servidor. La versión
    /// que se puede pedir desde fuera es <see cref="EnviarAhoraAsync"/>, y esa sí la exige.</para>
    /// </summary>
    public async Task<bool> RevisarYEnviarAsync(CancellationToken ct = default)
    {
        var configuracion = await ajustes.LeerCorreoAsync(ct);
        var delResumen = await ConfiguracionAsync(ct);

        if (!delResumen.Activo || !configuracion.EstaConfigurada) return false;
        if (delResumen.Destinatarios.Count == 0) return false;

        var ahora = DateTime.UtcNow;
        if (delResumen.UltimaCorridaUtc is DateTime ultima
            && (ahora - ultima).TotalDays < delResumen.CadaDias) return false;

        var datos = await RecopilarAsync(ahora, ct);

        // Nada que reportar: no se manda un correo vacío, pero el período se da por cumplido para no
        // volver a revisarlo hasta el siguiente. Es la decisión del escritorio.
        if (!HayAlgoQueReportar(datos))
        {
            await MarcarCorridaAsync(ahora, ct);
            return false;
        }

        var (ok, mensaje) = await MandarAsync(configuracion, delResumen, datos, ct);
        if (!ok)
        {
            // Queda constancia del fallo y NO se marca el período: cuando el correo vuelva, el
            // resumen sale. Un servidor caído no puede saltarse el reporte del día en silencio.
            await bitacora.RecordDetailedAsync(AuditAction.Update, "Resumen", null,
                $"El envío del resumen por correo falló: {mensaje}", AuditOutcome.Fallo, ct: ct);
            return false;
        }

        await MarcarCorridaAsync(DateTime.UtcNow, ct);
        await bitacora.RecordAsync(AuditAction.Update, "Resumen", null,
            $"Resumen enviado a {delResumen.Destinatarios.Count} destinatario(s).", ct);
        return true;
    }

    // ── A petición ───────────────────────────────────────────────────────────────

    /// <summary>
    /// El resumen tal como saldría ahora, sin enviarlo. Sirve para revisar qué se está mandando y
    /// para entender por qué un día no llegó nada.
    /// </summary>
    public async Task<VistaPreviaDelResumenDto> VistaPreviaAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var delResumen = await ConfiguracionAsync(ct);
        var datos = await RecopilarAsync(DateTime.UtcNow, ct);
        var ahoraLocal = DateTime.Now;

        return new VistaPreviaDelResumenDto(
            HayAlgoQueReportar(datos),
            Asunto(ahoraLocal),
            Componer(datos, ahoraLocal, delResumen.CadaDias),
            delResumen.Destinatarios);
    }

    /// <summary>
    /// Manda el resumen ahora mismo, sin esperar a que toque.
    ///
    /// <para>Marca el período como enviado igual que el automático: si no lo hiciera, el trabajo de
    /// fondo mandaría un segundo correo idéntico minutos después.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EnviarAhoraAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta);

        var delResumen = await ConfiguracionAsync(ct);
        if (delResumen.Destinatarios.Count == 0)
            return (false, "No hay destinatarios: configúralos en Configuración (o pon el correo de escalamiento de SLA).");

        var datos = await RecopilarAsync(DateTime.UtcNow, ct);

        // Se respeta la regla del escritorio también aquí: un correo con todo en cero no se manda.
        // Decirlo es mejor que mandarlo, porque explica el silencio de los días tranquilos.
        if (!HayAlgoQueReportar(datos))
            return (false, "No hay nada que reportar ahora mismo, así que no se envía un correo vacío.");

        var (ok, mensaje) = await MandarAsync(configuracion, delResumen, datos, ct);
        if (!ok)
        {
            await bitacora.RecordDetailedAsync(AuditAction.Update, "Resumen", null,
                $"El envío del resumen por correo falló: {mensaje}", AuditOutcome.Fallo, ct: ct);
            return (false, mensaje);
        }

        await MarcarCorridaAsync(DateTime.UtcNow, ct);
        await bitacora.RecordAsync(AuditAction.Update, "Resumen", null,
            $"Resumen enviado a mano a {delResumen.Destinatarios.Count} destinatario(s).", ct);

        return (true, $"Resumen enviado a {delResumen.Destinatarios.Count} destinatario(s).");
    }

    // ── Composición (pura, y por eso comprobable) ────────────────────────────────

    /// <summary>true si alguna cifra es distinta de cero, o sea: si hay algo que valga un correo.</summary>
    public static bool HayAlgoQueReportar(DatosDelResumen d) =>
        d.SlaVencidos + d.SlaPorVencer24 + d.SlaActivos + d.SugerenciasNuevas
        + d.DesplieguesUlt24 + d.TicketsSyncUlt24 + d.ReqPorEntregar7d > 0;

    /// <summary>El asunto del correo, con la fecha del día.</summary>
    public static string Asunto(DateTime fechaLocal) =>
        $"Resumen del equipo — {fechaLocal.ToString("dd/MM/yyyy", Espanol)}";

    /// <summary>
    /// El texto del correo. Es una función pura —recibe las cifras y la fecha— para poder
    /// comprobarla sin base de datos y sin red, que es justo lo que no se podía hacer en el
    /// escritorio con nada que tocara el correo.
    /// </summary>
    public static string Componer(DatosDelResumen d, DateTime fechaLocal, int frecuenciaDias)
    {
        var sb = new StringBuilder();
        sb.AppendLine(frecuenciaDias >= 7 ? "Resumen semanal del equipo" : "Resumen del equipo");
        sb.AppendLine(fechaLocal.ToString("dddd dd/MM/yyyy HH:mm", Espanol));
        sb.AppendLine();
        sb.AppendLine("SLA (compromisos de atención):");
        sb.AppendLine($"  • {d.SlaVencidos} vencido(s)");
        sb.AppendLine($"  • {d.SlaPorVencer24} por vencer en las próximas 24 h");
        sb.AppendLine($"  • {d.SlaActivos} activo(s) en total");
        sb.AppendLine();
        sb.AppendLine("Trabajo:");
        sb.AppendLine($"  • {d.ReqPorEntregar7d} requerimiento(s) por entregar en los próximos 7 días");
        sb.AppendLine($"  • {d.TicketsSyncUlt24} ticket(s) de DevOps sincronizados en las últimas 24 h");
        sb.AppendLine($"  • {d.DesplieguesUlt24} despliegue(s) iniciados en las últimas 24 h");
        sb.AppendLine();
        sb.AppendLine("Sugerencias:");
        sb.AppendLine($"  • {d.SugerenciasNuevas} sin atender");
        sb.AppendLine();
        sb.AppendLine("— Generado automáticamente por Administrador de Desarrollo Web.");
        return sb.ToString();
    }

    // ── Las cifras ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reúne las cifras del resumen. Mismas consultas que el escritorio, en asíncrono.
    ///
    /// <para>Los vencimientos activos se traen a memoria porque el «vencido» se decide comparando
    /// con la hora de AHORA, que en el escritorio ya se resolvía así; son pocos, uno por compromiso
    /// abierto.</para>
    /// </summary>
    public async Task<DatosDelResumen> RecopilarAsync(DateTime ahoraUtc, CancellationToken ct = default)
    {
        var hace24 = ahoraUtc.AddHours(-24);
        var hoy = DateTime.Today;

        var vencimientos = await db.SlaCommitments.AsNoTracking()
            .Where(s => s.Status == SlaStatus.Activo)
            .Select(s => s.DueAtUtc)
            .ToListAsync(ct);

        // Vencidos = los que ya pasaron su fecha aunque sigan activos, más los que el sistema ya dio
        // por vencidos. Es la suma del escritorio: sin la primera parte, un SLA recién pasado no
        // aparecería hasta que alguien lo marcara.
        int vencidos = vencimientos.Count(due => due < ahoraUtc)
                     + await db.SlaCommitments.CountAsync(s => s.Status == SlaStatus.Vencido, ct);

        int porVencer = vencimientos.Count(due => due >= ahoraUtc && due <= ahoraUtc.AddHours(24));

        return new DatosDelResumen(
            SlaVencidos: vencidos,
            SlaPorVencer24: porVencer,
            SlaActivos: vencimientos.Count,
            SugerenciasNuevas: await db.Suggestions.CountAsync(s => s.Status == SuggestionStatus.Nueva, ct),
            DesplieguesUlt24: await db.DeploymentJobs.CountAsync(j => j.StartedAt != null && j.StartedAt >= hace24, ct),
            TicketsSyncUlt24: await db.DevOpsTickets.CountAsync(t => t.SyncedAt >= hace24, ct),
            ReqPorEntregar7d: await db.Requirements.CountAsync(r =>
                r.CommittedDeliveryDate != null
                && r.CommittedDeliveryDate >= hoy
                && r.CommittedDeliveryDate <= hoy.AddDays(7)
                && r.Status != RequirementStatus.Entregado
                && r.Status != RequirementStatus.Cancelado, ct));
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>Manda el correo y traduce el fallo; no toca la marca del período.</summary>
    private async Task<(bool ok, string mensaje)> MandarAsync(
        ConfiguracionDeCorreo configuracion, ConfiguracionDelResumen delResumen,
        DatosDelResumen datos, CancellationToken ct)
    {
        var ahoraLocal = DateTime.Now;
        try
        {
            await correo.EnviarAsync(configuracion, delResumen.Destinatarios,
                Asunto(ahoraLocal), Componer(datos, ahoraLocal, delResumen.CadaDias), ct: ct);

            return (true, "");
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Deja escrito que el período ya se cubrió.
    ///
    /// <para>Escribe la fila directamente, sin pasar por <c>SettingsService</c>: ese exige ser
    /// administrador —con razón, es el que decide a qué servidor se despliega— y aquí no hay nadie
    /// con sesión. La clave y el formato ISO son los del escritorio, porque hasta el corte los dos
    /// leen esta misma fila.</para>
    /// </summary>
    private async Task MarcarCorridaAsync(DateTime cuandoUtc, CancellationToken ct)
    {
        var fila = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == ClaveUltimaCorrida, ct);
        if (fila == null)
        {
            fila = new AppSetting
            {
                Key = ClaveUltimaCorrida,
                Description = "Última vez que se envió el resumen del equipo (UTC)"
            };
            db.AppSettings.Add(fila);
        }

        fila.Value = cuandoUtc.ToString("o");
        await db.SaveChangesAsync(ct);
    }

    private static DateTime? LeerFecha(string? texto) =>
        DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha
            : null;
}
