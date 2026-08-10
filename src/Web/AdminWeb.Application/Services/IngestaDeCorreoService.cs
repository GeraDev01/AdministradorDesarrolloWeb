using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Correo;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Cómo se leen los ajustes del correo.
///
/// <para>Vive aparte del servicio porque lo necesitan los dos que hablan con el buzón —la ingesta y
/// el resumen— y repetir la lista de claves en cada uno dejaría dos sitios que se desincronizan el
/// día que alguien renombre una. Es la misma razón por la que <c>SettingsService</c> tiene sus claves
/// escritas en un solo lugar.</para>
///
/// <para>Es una extensión y no un método dentro de <c>SettingsService</c> a propósito: ese servicio
/// es el guardián de TODA la configuración y no tiene por qué saber de correo.</para>
/// </summary>
public static class AjustesDeCorreo
{
    /// <summary>587 (STARTTLS), que es el que sirve tanto en Office 365 como en Gmail.</summary>
    public const int PuertoSmtpPorOmision = 587;

    /// <summary>993, IMAP sobre SSL.</summary>
    public const int PuertoImapPorOmision = 993;

    /// <summary>Sin carpeta configurada se ingiere la bandeja de entrada, como en el escritorio.</summary>
    public const string CarpetaPorOmision = "INBOX";

    /// <summary>
    /// Los ajustes del correo, ya descifrados y listos para conectarse.
    ///
    /// <b>Lo que devuelve lleva la contraseña de aplicación</b> y por eso se queda del lado del
    /// servidor: no se responde nunca al navegador ni se anota en la bitácora.
    /// </summary>
    public static async Task<ConfiguracionDeCorreo> LeerCorreoAsync(
        this SettingsService ajustes, CancellationToken ct = default) =>
        new(
            Habilitado: await ajustes.ObtenerBooleanoAsync(SettingsService.Claves.EmailEnabled, ct),
            Direccion: (await ajustes.ObtenerAsync(SettingsService.Claves.EmailAddress, ct) ?? "").Trim(),
            NombreParaMostrar: (await ajustes.ObtenerAsync(SettingsService.Claves.EmailDisplayName, ct) ?? "").Trim(),
            Contrasena: await ajustes.ObtenerAsync(SettingsService.Claves.EmailPassword, ct) ?? "",
            ServidorSmtp: (await ajustes.ObtenerAsync(SettingsService.Claves.EmailSmtpHost, ct) ?? "").Trim(),
            PuertoSmtp: await ajustes.ObtenerEnteroAsync(SettingsService.Claves.EmailSmtpPort, PuertoSmtpPorOmision, ct),
            ServidorImap: (await ajustes.ObtenerAsync(SettingsService.Claves.EmailImapHost, ct) ?? "").Trim(),
            PuertoImap: await ajustes.ObtenerEnteroAsync(SettingsService.Claves.EmailImapPort, PuertoImapPorOmision, ct),
            CarpetaDeIngesta: Carpeta(await ajustes.ObtenerAsync(SettingsService.Claves.EmailRequirementsFolder, ct)));

    private static string Carpeta(string? configurada) =>
        string.IsNullOrWhiteSpace(configurada) ? CarpetaPorOmision : configurada.Trim();

    /// <summary>
    /// Parte una lista de direcciones escrita a mano. Se aceptan la coma y el punto y coma porque el
    /// escritorio aceptaba las dos, y se descarta lo que no lleve arroba: una entrada mal escrita no
    /// puede tumbar el envío a los demás.
    /// </summary>
    public static List<string> Separar(string? lista) =>
        (lista ?? "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// El buzón visto desde la aplicación: <b>la ingesta de requerimientos</b> —que es lo que corre
/// solo— y el resto de lo que la pantalla de correo necesita a su alrededor (estado, bandeja, envío
/// y prueba de conexión).
///
/// <para>Están juntos porque comparten exactamente las tres mismas cosas: la configuración cifrada,
/// la guarda de administrador y la traducción de un fallo de red a una frase que se entienda. El
/// cliente de correo (SMTP/IMAP) no sabe nada de eso: solo habla con los servidores.</para>
///
/// <para>── <b>QUÉ CUENTA COMO CORREO YA PROCESADO</b> ──────────────────────────────────────</para>
///
/// <para>La regla es la del escritorio y no cambia: <b>un correo está pendiente mientras esté SIN
/// LEER en su carpeta</b>; al convertirse en requerimiento se marca como leído, y esa marca es lo
/// que hace que la siguiente pasada no vuelva a mirarlo. Quien mueva o marque correos a mano en el
/// buzón está decidiendo qué se ingiere, y eso es deliberado.</para>
///
/// <para>Lo que sí cambia es el ORDEN, y hacía falta. El escritorio marcaba el correo como leído
/// dentro del bucle y guardaba todo al final: si el guardado fallaba, los correos quedaban leídos y
/// los requerimientos sin crear — se perdían sin dejar rastro. Aquí se guarda primero y se marca
/// después. Eso abre una ventana pequeña en la otra dirección (guardado sí, marcado no), y esa
/// ventana se cierra con el <c>Message-Id</c> del correo, que se guarda en el requerimiento: si la
/// siguiente pasada vuelve a ver el mismo mensaje, ya sabe que existe y no lo duplica. La ingesta
/// corre sola cada pocos minutos; una regla que dependiera de que nunca falle nada sería una
/// promesa que no se puede cumplir.</para>
/// </summary>
public class IngestaDeCorreoService(
    AppDbContext db,
    SettingsService ajustes,
    IClienteDeCorreo correo,
    DigestService resumen,
    AuditService bitacora,
    ICurrentUser usuarioActual)
{
    /// <summary>Lo que cabe en el título de un requerimiento. El mismo recorte del escritorio.</summary>
    public const int MaxTitulo = 200;

    /// <summary>
    /// Cuánto cuerpo se copia al requerimiento. El escritorio no ponía límite porque el correo lo
    /// pegaba una persona mirando; aquí lo trae un trabajo de fondo desde un buzón al que puede
    /// escribir cualquiera, y un mensaje de varios megas acabaría entero en una fila de la base.
    /// </summary>
    public const int MaxCuerpo = 20_000;

    /// <summary>Cuántos mensajes trae la bandeja de una vez. El del escritorio.</summary>
    public const int MensajesPorOmision = 50;

    // ── La pantalla ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cómo está el correo y cómo está el resumen. Es lo primero que pide la pantalla, y lo que
    /// decide si tiene sentido enseñar la bandeja o solo el aviso de que falta configurar.
    /// </summary>
    public async Task<EstadoDeCorreoDto> EstadoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        var delResumen = await resumen.ConfiguracionAsync(ct);

        return new EstadoDeCorreoDto(
            Configurado: configuracion.EstaConfigurada,
            Diagnostico: configuracion.Diagnostico() ?? "",
            Direccion: configuracion.Direccion,
            CarpetaDeIngesta: configuracion.CarpetaDeIngesta,
            ResumenActivo: delResumen.Activo,
            ResumenCadaDias: delResumen.CadaDias,
            DestinatariosDelResumen: delResumen.Destinatarios,
            UltimoResumenUtc: delResumen.UltimaCorridaUtc);
    }

    /// <summary>
    /// Las carpetas del buzón y los mensajes más recientes de una de ellas. Sin carpeta se abre la
    /// configurada para la ingesta, que es la que el escritorio preseleccionaba.
    /// </summary>
    public async Task<(bool ok, string mensaje, BandejaDeCorreoDto? bandeja)> BandejaAsync(
        string? carpeta, int maximo = MensajesPorOmision, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta, null);
        if (!configuracion.TieneLectura)
            return (false, "Falta el servidor IMAP en Configuración: sin él se puede enviar, pero no leer el buzón.", null);

        string elegida = string.IsNullOrWhiteSpace(carpeta) ? configuracion.CarpetaDeIngesta : carpeta.Trim();

        try
        {
            var carpetas = await correo.ListarCarpetasAsync(configuracion, ct);
            var mensajes = await correo.LeerRecientesAsync(
                configuracion, elegida, Math.Clamp(maximo, 1, 100), ct);

            return (true, $"{mensajes.Count} mensaje(s) en «{elegida}».",
                new BandejaDeCorreoDto(elegida, carpetas, mensajes.Select(Describir).ToList()));
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>Redacta y envía un correo desde la cuenta de la aplicación.</summary>
    public async Task<(bool ok, string mensaje)> EnviarAsync(
        string? destinatarios, string? asunto, string? cuerpo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta);

        var destinos = AjustesDeCorreo.Separar(destinatarios);
        if (destinos.Count == 0) return (false, "Indica al menos un destinatario válido.");

        asunto = (asunto ?? "").Trim();
        if (asunto.Length == 0) return (false, "Escribe un asunto.");

        try
        {
            await correo.EnviarAsync(configuracion, destinos, asunto, cuerpo ?? "", ct: ct);
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message);
        }

        // Se anota QUÉ se mandó y a cuántos, no el cuerpo: por ahí pasan datos que no tienen por qué
        // quedar copiados en una tabla que lee más gente que la que envió el correo.
        await bitacora.RecordAsync(AuditAction.Update, "Correo", null,
            $"Correo enviado a {destinos.Count} destinatario(s): {asunto}", ct);

        return (true, $"Correo enviado a {destinos.Count} destinatario(s).");
    }

    /// <summary>Comprueba que la cuenta y los servidores configurados sirven, sin mandar nada.</summary>
    public async Task<(bool ok, string mensaje)> ProbarConexionAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta);

        try
        {
            return (true, await correo.ProbarConexionAsync(configuracion, ct));
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message);
        }
    }

    // ── La ingesta ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Importa como requerimientos los correos sin procesar de una carpeta. Es el botón de la
    /// pantalla; sin carpeta usa la configurada, para que hacerlo a mano y dejarlo correr solo sean
    /// exactamente lo mismo.
    /// </summary>
    public async Task<(bool ok, string mensaje, int creados)> IngerirAsync(
        string? carpeta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta, 0);
        if (!configuracion.TieneLectura)
            return (false, "Falta el servidor IMAP en Configuración: sin él no se puede ingerir el correo.", 0);

        string elegida = string.IsNullOrWhiteSpace(carpeta) ? configuracion.CarpetaDeIngesta : carpeta.Trim();

        try
        {
            int creados = await ImportarAsync(configuracion, elegida, ct);
            return (true, creados == 0
                ? $"No había correos sin procesar en «{elegida}»."
                : $"Se importaron {creados} requerimiento(s) desde «{elegida}».", creados);
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message, 0);
        }
    }

    /// <summary>
    /// La ingesta tal como la llama el trabajo de fondo.
    ///
    /// <para><b>Sin guarda de rol, y es correcto</b>: aquí no hay nadie con sesión detrás. Lo que
    /// autoriza a esta ejecución es la configuración del servidor —el interruptor de los trabajos de
    /// fondo y el del propio correo—, no un rol. La versión con guarda es <see cref="IngerirAsync"/>,
    /// que es la que puede llamarse desde fuera.</para>
    ///
    /// <para>Un buzón sin configurar no es un error: es lo normal hasta que alguien lo configure, y
    /// el trabajo simplemente no hace nada.</para>
    /// </summary>
    public async Task<int> EjecutarIngestaProgramadaAsync(CancellationToken ct = default)
    {
        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (!configuracion.EstaConfigurada || !configuracion.TieneLectura) return 0;

        return await ImportarAsync(configuracion, configuracion.CarpetaDeIngesta, ct);
    }

    /// <summary>
    /// Convierte UN mensaje en requerimiento y lo da por procesado.
    ///
    /// <para>Recibe la carpeta y el UID, no el texto: el servidor relee el correo del buzón. Si el
    /// título y el cuerpo llegaran desde el navegador, lo que quedaría guardado sería lo que escribió
    /// quien hizo la petición, no lo que decía el correo — y el requerimiento dejaría de ser
    /// evidencia de nada.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ConvertirEnRequerimientoAsync(
        string? carpeta, uint identificador, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuarioActual);

        var configuracion = await ajustes.LeerCorreoAsync(ct);
        if (configuracion.Diagnostico() is string falta) return (false, falta);
        if (!configuracion.TieneLectura)
            return (false, "Falta el servidor IMAP en Configuración: sin él no se puede leer el buzón.");

        string elegida = string.IsNullOrWhiteSpace(carpeta) ? configuracion.CarpetaDeIngesta : carpeta.Trim();

        try
        {
            var mensaje = await correo.LeerUnoAsync(configuracion, elegida, identificador, ct);
            if (mensaje is null)
                return (false, "Ese mensaje ya no está en la carpeta. Recarga la bandeja.");

            if (await YaImportadoAsync(mensaje.Identidad, ct))
                return (false, "Ese correo ya se había convertido en requerimiento.");

            var requerimiento = ARequerimiento(mensaje);
            db.Requirements.Add(requerimiento);
            await db.SaveChangesAsync(ct);

            // Se marca DESPUÉS de guardar y su fallo no tumba la operación, por lo mismo que en la
            // ingesta: el requerimiento ya existe, y decir que no se pudo haría que se intentara otra
            // vez. Lo peor que queda es un correo sin leer, y el Message-Id impide duplicarlo.
            try { await correo.MarcarComoProcesadosAsync(configuracion, elegida, [identificador], ct); }
            catch (ErrorDeCorreo fallo)
            {
                await bitacora.RecordDetailedAsync(AuditAction.Update, "Correo", null,
                    $"No se pudo marcar como leído el correo convertido en el requerimiento "
                    + $"#{requerimiento.Id}: {fallo.Message}", AuditOutcome.Fallo, ct: ct);
            }

            await bitacora.RecordAsync(AuditAction.Create, "Requirement", requerimiento.Id.ToString(),
                $"Desde correo: {requerimiento.Title}", ct);

            return (true, $"Requerimiento #{requerimiento.Id} creado.");
        }
        catch (ErrorDeCorreo ex)
        {
            return (false, ex.Message);
        }
    }

    // ── El mecanismo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea un requerimiento por cada correo sin procesar y los da por procesados.
    ///
    /// <para>Guarda primero y marca después, y descarta lo que ya exista por su <c>Message-Id</c>.
    /// El porqué de las dos cosas está en la documentación de la clase.</para>
    /// </summary>
    private async Task<int> ImportarAsync(ConfiguracionDeCorreo configuracion, string carpeta, CancellationToken ct)
    {
        var mensajes = await correo.LeerSinProcesarAsync(configuracion, carpeta, ct);
        if (mensajes.Count == 0) return 0;

        var identidades = mensajes.Select(m => m.Identidad).ToList();
        var yaExisten = (await db.Requirements.AsNoTracking()
                .Where(r => r.Source == RequirementSource.Email
                            && r.ExternalId != null
                            && identidades.Contains(r.ExternalId))
                .Select(r => r.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var procesados = new List<uint>();
        int creados = 0;

        foreach (var mensaje in mensajes)
        {
            ct.ThrowIfCancellationRequested();

            // Se marca como procesado incluso el que se descarta por repetido: si no, cada pasada
            // volvería a leerlo, a descartarlo y a intentar marcarlo, para siempre.
            procesados.Add(mensaje.Identificador);

            // Add sobre el conjunto devuelve false si ya estaba: cubre a la vez lo que ya existe en
            // la base y lo que viene repetido dentro de esta misma tanda.
            if (!yaExisten.Add(mensaje.Identidad)) continue;

            db.Requirements.Add(ARequerimiento(mensaje));
            creados++;
        }

        if (creados > 0) await db.SaveChangesAsync(ct);

        try
        {
            await correo.MarcarComoProcesadosAsync(configuracion, carpeta, procesados, ct);
        }
        catch (ErrorDeCorreo ex)
        {
            // Los requerimientos YA están guardados. Si el fallo del marcado se propagara, quien
            // mire la pantalla leería «no se importó nada» sobre unos requerimientos que sí existen,
            // y volvería a intentarlo. Se deja constancia y se sigue: la siguiente pasada verá los
            // mismos correos y no los duplicará, porque su Message-Id ya está en la base.
            await bitacora.RecordDetailedAsync(AuditAction.Update, "Correo", null,
                $"No se pudieron marcar como leídos {procesados.Count} correo(s) de «{carpeta}»: {ex.Message}",
                AuditOutcome.Fallo, ct: ct);
        }

        if (creados > 0)
            await bitacora.RecordAsync(AuditAction.Create, "Requirement", null,
                $"{creados} requerimiento(s) importado(s) desde correo (carpeta {carpeta})", ct);

        return creados;
    }

    private Task<bool> YaImportadoAsync(string identidad, CancellationToken ct) =>
        db.Requirements.AsNoTracking()
            .AnyAsync(r => r.Source == RequirementSource.Email && r.ExternalId == identidad, ct);

    /// <summary>
    /// El requerimiento que sale de un correo. Mismos campos que el escritorio —origen Correo, estado
    /// «Por estimar», remitente y fecha encabezando la descripción— con dos diferencias:
    ///
    /// <para>· el <c>Message-Id</c> queda en <c>ExternalId</c>, que es lo que impide duplicarlo;</para>
    ///
    /// <para>· la fecha se escribe en UTC y se dice que lo es. El escritorio ponía la hora local de
    /// quien estaba mirando; aquí quien escribe la línea es un servidor que en Azure corre en UTC, y
    /// una hora sin huso al lado se lee mal justo cuando importa.</para>
    /// </summary>
    private static Requirement ARequerimiento(MensajeDeCorreo mensaje) => new()
    {
        Title = Recortar(mensaje.Asunto, MaxTitulo),
        Description = $"De: {mensaje.De}\nFecha: {mensaje.FechaUtc:dd/MM/yyyy HH:mm} UTC\n\n"
                      + Recortar(mensaje.Cuerpo, MaxCuerpo).Trim(),
        Source = RequirementSource.Email,
        Status = RequirementStatus.PorEstimar,
        ExternalId = mensaje.Identidad,
        CreatedAt = DateTime.UtcNow,
        StatusChangedAt = DateTime.UtcNow
    };

    /// <summary>Al navegador va lo que se puede enseñar: ni el UID interno de más, ni nada del buzón.</summary>
    private static MensajeDeCorreoDto Describir(MensajeDeCorreo mensaje) => new(
        mensaje.Identificador, mensaje.Asunto, mensaje.De, mensaje.FechaUtc, mensaje.Vista, mensaje.Cuerpo);

    private static string Recortar(string texto, int maximo) =>
        string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto[..maximo];
}
