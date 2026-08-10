using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Cómo salió una ronda de escalamiento, para que el trabajo de fondo lo registre.</summary>
/// <param name="SinDestinatario">
/// Se vencieron compromisos pero no llegaron a nadie: ni por aviso dentro de la aplicación —no había
/// destinatario configurado ni un líder activo— ni por correo. Los vencimientos quedaron marcados
/// —se ven en la pantalla del líder— pero el aviso no salió, así que el trabajo de fondo lo deja en
/// el registro para que se pueda arreglar.
/// </param>
/// <param name="SalioPorCorreo">El escalamiento también se mandó por SMTP a los buzones configurados.</param>
/// <param name="FalloDelCorreo">
/// Por qué no salió por correo, o null si salió (o si no había nada que escalar). Distingue «el
/// correo no está configurado» de «el servidor SMTP lo rechazó», que llevan a arreglos distintos.
/// <b>Nunca lleva la contraseña</b>: son los textos que ya traduce el cliente de correo.
/// </param>
public readonly record struct EscalamientoDeSla(
    int Vencidos,
    int Escalados,
    bool SinDestinatario,
    bool SalioPorCorreo = false,
    string? FalloDelCorreo = null);

/// <summary>Cómo salió una ronda de recordatorios.</summary>
/// <param name="SinCuenta">
/// Pendientes de alguien que no tiene cuenta activa. No hay canal por el que avisarle; se cuenta para
/// que quede en el registro y el líder pueda arreglarlo.
/// </param>
public readonly record struct RecordatoriosDeSla(int Pendientes, int Avisados, int SinCuenta);

/// <summary>
/// Los avisos de SLA: el recordatorio al responsable y el escalamiento del incumplimiento al líder.
///
/// <para>Está separado de <see cref="SlaService"/> por lo mismo que en el escritorio: las reglas del
/// compromiso deben poder probarse sin depender del canal de avisos, y un fallo al avisar no debe
/// impedir que el SLA se marque como vencido.</para>
///
/// <para><b>El canal se amplía: aviso dentro de la aplicación Y correo.</b> En el escritorio los dos
/// avisos salían solo por SMTP porque no había otra forma de alcanzar a alguien que no tuviera la
/// aplicación abierta. Aquí el aviso in-app es el canal principal —es persistente, se lee al entrar
/// y no depende de que ningún servidor de terceros esté en pie—, y el escalamiento sale <b>además</b>
/// por correo, como en el escritorio, porque un incumplimiento tiene que alcanzar al líder aunque no
/// entre a la aplicación en todo el día. <b>El recordatorio al responsable no lleva correo</b>, y es
/// deliberado: va a quien tiene la aplicación abierta trabajando, así que el aviso in-app le llega
/// igual y un correo cada pocas horas por cada compromiso sería el ruido que hace que se dejen de
/// leer los dos canales.</para>
///
/// <para><b>El correo es el extra y nunca la condición.</b> El aviso in-app se entrega primero y el
/// correo después: si el correo no está configurado, o si el servidor SMTP lo rechaza, el
/// escalamiento in-app ya ocurrió y el motivo viaja en <see cref="EscalamientoDeSla.FalloDelCorreo"/>
/// para que el trabajo de fondo lo deje escrito. Al revés no puede pasar.</para>
///
/// <para>A quién se le avisa no cambia: el escalamiento sale de
/// <see cref="SettingsService.Claves.SlaEscalationEmail"/>, con varios destinatarios separados por
/// «;» o «,». Esas direcciones se usan tal cual para el correo y se resuelven contra las fichas del
/// equipo para saber a qué cuentas entregar el aviso dentro de la aplicación.</para>
///
/// <para>Los dos pasos —avisar y dejar constancia de que se avisó— van siempre separados y en ese
/// orden. Es la disciplina que el escritorio ya tenía con el escalamiento y la que hace que un fallo
/// no se convierta en un aviso perdido para siempre.</para>
/// </summary>
public class SlaNotificationService(
    AppDbContext db,
    SlaService sla,
    SlaAlertTracker rastro,
    NotificationService avisos,
    SettingsService configuracion,
    IClienteDeCorreo correo)
{
    /// <summary>Separadores admitidos en la lista de buzones de escalamiento, igual que el escritorio.</summary>
    private static readonly char[] SeparadoresDeBuzones = [';', ','];

    /// <summary>
    /// Marca vencimientos y escala el incumplimiento a quien corresponda: aviso dentro de la
    /// aplicación y, además, correo a los buzones configurados.
    ///
    /// <para>Solo da por avisado lo que de verdad se entregó: si ningún canal alcanzó a nadie, no se
    /// marca nada como notificado, para que quede constancia de que ese aviso no llegó a darse.</para>
    /// </summary>
    public async Task<EscalamientoDeSla> EscalarIncumplimientosAsync(CancellationToken ct = default)
    {
        var vencidos = await sla.RevisarVencimientosAsync(ct: ct);
        if (vencidos.Count == 0) return new EscalamientoDeSla(0, 0, false);

        var destinatarios = await DestinatariosDelEscalamientoAsync(ct);

        foreach (var s in vencidos)
        {
            var detalle =
                $"{s.Developer?.FullName ?? "?"} — {SlaService.DescribirObjetivo(s)}. " +
                $"Venció el {s.DueAtUtc:dd/MM/yyyy HH:mm} UTC" +
                (s.LastCommentAtUtc is DateTime c
                    ? $", último comentario el {c:dd/MM/yyyy HH:mm} UTC."
                    : ", sin comentarios en el ticket.");

            foreach (var userId in destinatarios)
                await avisos.NotifyAsync(userId, NotificationKind.General,
                    "SLA vencido", detalle,
                    // Sin Url: la clave de navegación la resuelve la pantalla de Avisos, y una ruta
                    // que no exista fallaría en silencio.
                    url: null,
                    // Una clave por compromiso: si el mismo incumplimiento se detecta dos veces
                    // —dos instancias de la API, o un reintento— el aviso no se duplica.
                    dedupeKey: $"sla:{s.Id}:incumplimiento", ct: ct);
        }

        // El correo va DESPUÉS de los avisos in-app y con su propio manejo de fallos. Ese orden es la
        // regla: lo que siempre tiene que quedar entregado ya está entregado cuando se intenta el
        // SMTP, así que un servidor de correo caído retrasa el correo, no el escalamiento.
        var (salioPorCorreo, falloDelCorreo) = await EscalarPorCorreoAsync(vencidos, ct);

        // Ni una cuenta a la que avisar ni un correo que saliera: nadie se enteró. No se marca como
        // notificado para que el trabajo de fondo pueda decirlo y la próxima vuelta lo reintente.
        if (destinatarios.Count == 0 && !salioPorCorreo)
            return new EscalamientoDeSla(vencidos.Count, 0, SinDestinatario: true,
                SalioPorCorreo: false, FalloDelCorreo: falloDelCorreo);

        await sla.MarcarIncumplimientoNotificadoAsync(vencidos.Select(v => v.Id), ct);
        return new EscalamientoDeSla(vencidos.Count, vencidos.Count, false, salioPorCorreo, falloDelCorreo);
    }

    /// <summary>
    /// Manda el escalamiento por correo. Devuelve por qué no salió en vez de lanzar: quien llama ya
    /// entregó los avisos dentro de la aplicación y no puede permitirse que el SMTP lo tumbe.
    /// </summary>
    private async Task<(bool enviado, string? fallo)> EscalarPorCorreoAsync(
        IReadOnlyCollection<SlaCommitment> vencidos, CancellationToken ct)
    {
        var datosDeCorreo = await configuracion.LeerCorreoAsync(ct);
        if (datosDeCorreo.Diagnostico() is string falta) return (false, falta);

        var buzones = await BuzonesDelEscalamientoAsync(ct);
        if (buzones.Count == 0)
            return (false, "No hay ningún buzón al que escalar: captura «"
                         + SettingsService.Claves.SlaEscalationEmail + "» en Configuración.");

        try
        {
            await correo.EnviarAsync(datosDeCorreo, buzones,
                AsuntoDelEscalamiento(vencidos.Count), ComponerEscalamiento(vencidos), ct: ct);

            return (true, null);
        }
        catch (ErrorDeCorreo ex)
        {
            // Es el único tipo que el cliente de correo deja salir: traduce todo lo demás. Su mensaje
            // ya está escrito para leerse y no incluye la contraseña de aplicación.
            return (false, ex.Message);
        }
    }

    /// <summary>El asunto del escalamiento. Es el del escritorio, palabra por palabra.</summary>
    public static string AsuntoDelEscalamiento(int cuantos) =>
        $"SLA vencido: {cuantos} compromiso(s)";

    /// <summary>
    /// El cuerpo del correo de escalamiento, con una línea por compromiso. Es el texto del
    /// escritorio; se mantiene igual para que quien ya recibía estos correos no tenga que releerlos.
    ///
    /// <para>Es una función pura para poder comprobarla sin base de datos y sin red.</para>
    /// </summary>
    public static string ComponerEscalamiento(IReadOnlyCollection<SlaCommitment> vencidos)
    {
        var lineas = vencidos.Select(s =>
            $"  · {s.Developer?.FullName ?? "?"} — {SlaService.DescribirObjetivo(s)}\n" +
            $"      venció: {s.DueAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}" +
            (s.LastCommentAtUtc is DateTime c
                ? $"  ·  último comentario: {c.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "  ·  sin comentarios en el ticket"));

        return $"Se vencieron {vencidos.Count} compromiso(s) de atención:\n\n"
             + string.Join("\n\n", lineas)
             + "\n\n— Administrador de Desarrollo";
    }

    /// <summary>
    /// Avisa a los responsables de los compromisos que piden un comentario en su ticket o que ya se
    /// pasaron de la fecha. Sin <paramref name="developerId"/> revisa a todo el equipo.
    /// </summary>
    public async Task<RecordatoriosDeSla> AvisarPendientesAsync(
        int? developerId = null, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;

        var pendientes = await sla.PendientesDeAvisoDelSistemaAsync(developerId, ahora, ct);
        var nuevos = SlaAlertTracker.Nuevos(pendientes, ahora);
        if (nuevos.Count == 0) return new RecordatoriosDeSla(pendientes.Count, 0, 0);

        int avisados = 0, sinCuenta = 0;
        foreach (var s in nuevos)
        {
            bool fueraDePlazo = s.EstaVencido(ahora);
            var titulo = fueraDePlazo ? "SLA fuera de plazo" : "Recordatorio de SLA";
            var ticket = s.DevOpsTicketExternalId is int t ? $" (ticket #{t})" : "";
            var mensaje = fueraDePlazo
                ? $"{SlaService.DescribirObjetivo(s)}{ticket} venció el {s.DueAtUtc:dd/MM/yyyy HH:mm} UTC."
                : $"{SlaService.DescribirObjetivo(s)}{ticket} vence el {s.DueAtUtc:dd/MM/yyyy HH:mm} UTC. " +
                  "Deja un comentario de avance en su ticket.";

            // La clave del recordatorio incluye el recordatorio al que corresponde: mientras no se
            // reprograme, el aviso no se repite; en cuanto se reprograma —al posponer o al comentar—
            // el siguiente vuelve a salir, que es la regla del escritorio.
            var clave = fueraDePlazo
                ? $"sla:{s.Id}:vencido"
                : $"sla:{s.Id}:recordatorio:{s.NextReminderAtUtc:yyyyMMddHHmm}";

            if (await avisos.NotifyDeveloperAsync(
                    s.DeveloperId, NotificationKind.General, titulo, mensaje, url: null, dedupeKey: clave, ct: ct))
                avisados++;
            else if (!await TieneCuentaActivaAsync(s.DeveloperId, ct))
                sinCuenta++;
        }

        // Se marcan TODOS los decididos, incluidos los de quien no tiene cuenta activa: sin cuenta no
        // hay canal, y reintentarlo cada cuarto de hora durante meses no crearía ningún aviso —solo
        // trabajo. El contador de arriba es lo que hace visible ese caso para poder arreglarlo.
        await rastro.MarcarAvisadosAsync(nuevos, ahora, ct);

        return new RecordatoriosDeSla(pendientes.Count, avisados, sinCuenta);
    }

    /// <summary>
    /// Las cuentas a las que va el escalamiento.
    ///
    /// Salen de la configuración, tal como en el escritorio: una o varias direcciones separadas por
    /// «;» o «,». Como aquí el aviso se entrega dentro de la aplicación, cada dirección se resuelve
    /// contra la ficha del equipo para dar con su cuenta. Lo que no se resuelve se ignora, y si no
    /// queda ninguna se cae en los LÍDERES activos — el equivalente del «cae a la propia cuenta de la
    /// aplicación» del escritorio: un incumplimiento no puede quedarse sin llegar a nadie.
    /// </summary>
    public async Task<List<int>> DestinatariosDelEscalamientoAsync(CancellationToken ct = default)
    {
        var configurado = await configuracion.ObtenerAsync(SettingsService.Claves.SlaEscalationEmail, ct);
        var buzones = SepararBuzones(configurado);

        if (buzones.Count > 0)
        {
            var cuentas = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.DeveloperId != null && u.Developer!.Email != null)
                .Select(u => new { u.Id, Correo = u.Developer!.Email! })
                .ToListAsync(ct);

            // La comparación se hace en memoria y no en la consulta a propósito: si el correo se
            // capturó con otra caja, SQL Server lo daría por igual y SQLite no, y el escalamiento
            // llegaría o no según el motor. Son unas pocas decenas de cuentas.
            var elegidos = cuentas
                .Where(c => buzones.Contains(c.Correo, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            if (elegidos.Count > 0) return elegidos;
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Los buzones a los que sale el correo del escalamiento.
    ///
    /// Los configurados en <see cref="SettingsService.Claves.SlaEscalationEmail"/> y, si nadie
    /// capturó ninguno, la propia cuenta de la aplicación — la caída del escritorio, que existe para
    /// que un incumplimiento no se quede sin salir de la máquina. Son direcciones, no cuentas: el
    /// escalamiento por correo alcanza también a quien no tiene ficha en el equipo, que es
    /// justamente para lo que sirve.
    /// </summary>
    public async Task<List<string>> BuzonesDelEscalamientoAsync(CancellationToken ct = default)
    {
        var buzones = SepararBuzones(
            await configuracion.ObtenerAsync(SettingsService.Claves.SlaEscalationEmail, ct));
        if (buzones.Count > 0) return buzones;

        var propio = (await configuracion.ObtenerAsync(SettingsService.Claves.EmailAddress, ct) ?? "").Trim();
        return propio.Contains('@') ? [propio] : [];
    }

    /// <summary>
    /// Las direcciones configuradas, saneadas. Misma regla del escritorio: separadas por «;» o «,»,
    /// solo las que parezcan un correo y sin repetidas.
    /// </summary>
    public static List<string> SepararBuzones(string? configurado) =>
        (configurado ?? "")
        .Split(SeparadoresDeBuzones, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(d => d.Contains('@'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private Task<bool> TieneCuentaActivaAsync(int developerId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.DeveloperId == developerId && u.IsActive, ct);
}
