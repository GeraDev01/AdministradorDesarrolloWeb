using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AdminWeb.Infrastructure.Integraciones;

/// <summary>
/// Los datos con los que se habla con el servidor de correo, ya descifrados y listos para usar.
///
/// <para><b>Lleva la contraseña de aplicación en claro y por eso no sale nunca de esta capa.</b> No
/// tiene equivalente en Shared ni se serializa: quien necesite enseñar el estado del correo en la
/// pantalla usa <c>EstadoDeCorreoDto</c>, que no la incluye. El <c>ToString()</c> está reescrito a
/// propósito — el que genera un <c>record</c> imprime TODAS sus propiedades, así que una sola línea
/// de registro con la configuración entera habría dejado la contraseña escrita en el log.</para>
/// </summary>
public record ConfiguracionDeCorreo(
    bool Habilitado,
    string Direccion,
    string NombreParaMostrar,
    string Contrasena,
    string ServidorSmtp,
    int PuertoSmtp,
    string ServidorImap,
    int PuertoImap,
    string CarpetaDeIngesta)
{
    /// <summary>
    /// Por qué el correo no se puede usar, o null si sí se puede.
    ///
    /// Los dos textos son los del escritorio, palabra por palabra: distinguen «está apagado» de
    /// «está encendido pero incompleto», que llevan a arreglos distintos en la misma pantalla.
    /// </summary>
    public string? Diagnostico()
    {
        if (!Habilitado)
            return "El correo no está habilitado. Actívalo en Configuración.";

        if (string.IsNullOrWhiteSpace(Direccion) || string.IsNullOrWhiteSpace(Contrasena)
            || string.IsNullOrWhiteSpace(ServidorSmtp))
            return "Faltan datos de correo (dirección, contraseña o servidor SMTP) en Configuración.";

        return null;
    }

    /// <summary>Se puede enviar y leer con esto.</summary>
    public bool EstaConfigurada => Diagnostico() is null;

    /// <summary>Leer el buzón necesita además un servidor IMAP; enviar no.</summary>
    public bool TieneLectura => EstaConfigurada && !string.IsNullOrWhiteSpace(ServidorImap);

    /// <summary>Remitente que verá quien reciba el correo. Sin nombre para mostrar, la dirección.</summary>
    public string RemitenteVisible =>
        string.IsNullOrWhiteSpace(NombreParaMostrar) ? Direccion : NombreParaMostrar;

    public override string ToString() =>
        $"ConfiguracionDeCorreo {{ Direccion = {Direccion}, Smtp = {ServidorSmtp}:{PuertoSmtp}, " +
        $"Imap = {ServidorImap}:{PuertoImap}, Contrasena = (oculta) }}";
}

/// <summary>
/// Un mensaje leído del buzón, ya en texto plano.
/// </summary>
/// <param name="Identificador">UID del mensaje dentro de su carpeta IMAP.</param>
/// <param name="Identidad">
/// Con qué se reconoce este mensaje entre pasadas: su <c>Message-Id</c>, que lo pone quien lo envía
/// y viaja con él. Es lo que permite no crear dos veces el mismo requerimiento si algo se rompe
/// después de guardarlo y antes de marcarlo en el buzón. Cuando el correo no trae cabecera —los hay,
/// sobre todo los que genera una máquina—, se cae a «carpeta#uid», que dentro de la misma carpeta
/// distingue igual de bien.
/// </param>
public record MensajeDeCorreo(
    uint Identificador,
    string Identidad,
    string Asunto,
    string De,
    DateTime FechaUtc,
    string Vista,
    string Cuerpo);

/// <summary>
/// Un archivo que acompaña a un correo, ya en memoria.
/// </summary>
/// <param name="Nombre">Con qué nombre lo verá quien lo reciba, extensión incluida.</param>
/// <param name="Contenido">
/// Los BYTES, no una ruta. Es la diferencia que importa: en el escritorio el adjunto salía de un
/// diálogo de archivo y viajaba como ruta del disco de quien escribía. Aceptar una ruta aquí —donde
/// quien pide el envío está del otro lado de la red— convertiría cualquier envío en un lector de
/// ficheros arbitrarios del servidor. Lo que se adjunta es siempre algo que el servidor acaba de
/// generar o que ya tiene guardado.
/// </param>
/// <param name="TipoDeMedio">Tipo MIME. Si no se entiende, se manda como binario genérico.</param>
public record AdjuntoDeCorreo(string Nombre, byte[] Contenido, string TipoDeMedio)
{
    /// <summary>El tipo de una hoja de cálculo de Excel (.xlsx).</summary>
    public const string HojaDeCalculo =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>
/// Un fallo de la integración de correo, ya traducido a algo que una persona pueda leer y accionar.
///
/// <para>Existe para que «no hay red», «la contraseña de aplicación ya no sirve» y «esa carpeta no
/// existe» no acaben los tres como un 500 con una traza dentro. El detalle técnico se queda en el
/// registro del servidor; lo que sale es esta frase.</para>
/// </summary>
public class ErrorDeCorreo(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>
/// Lo que se puede hacer contra el servidor de correo.
///
/// <para>Existe como interfaz por una razón muy concreta: <b>las pruebas no pueden tocar la red</b>.
/// Todo lo que decide de verdad —qué correo ya se procesó, qué dice el resumen— vive en los
/// servicios de aplicación, y estos hablan con el buzón únicamente por aquí, así que una
/// implementación de mentira basta para probarlos enteros.</para>
///
/// <para>Cada método recibe la <see cref="ConfiguracionDeCorreo"/> en vez de leerla por su cuenta:
/// la configuración está cifrada y quien sabe descifrarla es la capa de aplicación
/// (<c>SettingsService</c>), que vive por encima de esta. Es la misma frontera que ya respeta el
/// resto de Infrastructure.</para>
/// </summary>
public interface IClienteDeCorreo
{
    /// <summary>
    /// Envía un correo de texto plano, con un adjunto opcional.
    /// </summary>
    /// <param name="adjunto">
    /// Null cuando el correo va solo con su texto. El adjunto viaja como contenido y nunca como
    /// ruta; ver <see cref="AdjuntoDeCorreo"/>.
    /// </param>
    Task EnviarAsync(ConfiguracionDeCorreo configuracion, IEnumerable<string> destinatarios,
        string asunto, string cuerpo, AdjuntoDeCorreo? adjunto = null, CancellationToken ct = default);

    /// <summary>Se conecta y autentica sin mandar nada, para comprobar que los datos sirven.</summary>
    Task<string> ProbarConexionAsync(ConfiguracionDeCorreo configuracion, CancellationToken ct = default);

    /// <summary>Las carpetas del buzón, para elegir cuál se mira o cuál se ingiere.</summary>
    Task<IReadOnlyList<string>> ListarCarpetasAsync(ConfiguracionDeCorreo configuracion,
        CancellationToken ct = default);

    /// <summary>Los mensajes más recientes de una carpeta, del más nuevo al más viejo.</summary>
    Task<IReadOnlyList<MensajeDeCorreo>> LeerRecientesAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Los mensajes de la carpeta que todavía NO están marcados como leídos, que es lo que el
    /// escritorio considera «sin procesar».
    /// </summary>
    Task<IReadOnlyList<MensajeDeCorreo>> LeerSinProcesarAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, CancellationToken ct = default);

    /// <summary>Un mensaje concreto por su UID, o null si ya no está en la carpeta.</summary>
    Task<MensajeDeCorreo?> LeerUnoAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        uint identificador, CancellationToken ct = default);

    /// <summary>
    /// Marca mensajes como procesados, que en este buzón significa marcarlos como leídos. Es la
    /// única señal que hace que la siguiente pasada no vuelva a mirarlos.
    /// </summary>
    Task MarcarComoProcesadosAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        IReadOnlyCollection<uint> identificadores, CancellationToken ct = default);
}

/// <summary>
/// El cliente de correo con MailKit: envío por SMTP y lectura por IMAP. Portado del
/// <c>EmailService</c> del escritorio.
///
/// <para><b>Qué cambia respecto del escritorio y por qué.</b></para>
///
/// <para>· <b>Ya no guarda estado ni conoce la base.</b> Allí el servicio leía la configuración,
/// escribía en la bitácora y creaba requerimientos él mismo. Aquí solo habla con los servidores: lo
/// que decide qué se guarda y quién puede pedirlo son los servicios de aplicación. Eso es lo que
/// permite probarlos sin red y lo que evita que una integración acabe siendo el sitio donde vive
/// media regla de negocio.</para>
///
/// <para>· <b>El adjunto viaja como contenido, no como ruta.</b> En el escritorio salía de un
/// <c>OpenFileDialog</c>: un archivo de la máquina de quien escribía. En la web ese archivo no está
/// en el servidor, y aceptar una RUTA desde el navegador convertiría el botón de adjuntar en un
/// lector de cualquier fichero del servidor. Por eso <see cref="AdjuntoDeCorreo"/> lleva bytes: lo
/// que se adjunta es lo que el servidor acaba de generar (un reporte, por ejemplo), nunca lo que
/// alguien de fuera nombre.</para>
///
/// <para>· <b>Los fallos salen traducidos</b> como <see cref="ErrorDeCorreo"/>. Una contraseña de
/// aplicación caducada es lo más frecuente que le pasa a esta integración y merece decirse con esas
/// palabras, no con una excepción de protocolo.</para>
/// </summary>
public class EmailService(ILogger<EmailService> log) : IClienteDeCorreo
{
    /// <summary>Cuántos caracteres del cuerpo se enseñan como vista previa. El mismo del escritorio.</summary>
    private const int LargoDeLaVista = 120;

    // ── Envío ────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task EnviarAsync(ConfiguracionDeCorreo configuracion, IEnumerable<string> destinatarios,
        string asunto, string cuerpo, AdjuntoDeCorreo? adjunto = null, CancellationToken ct = default) =>
        EjecutarAsync("enviar el correo", async token =>
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress(configuracion.RemitenteVisible, configuracion.Direccion));

            foreach (var destino in destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                // Una dirección mal escrita tumba el envío entero con una excepción de análisis; se
                // traduce aquí para que el mensaje diga CUÁL es la que no se entiende.
                try { mensaje.To.Add(MailboxAddress.Parse(destino.Trim())); }
                catch (ParseException ex) { throw new ErrorDeCorreo($"«{destino.Trim()}» no es una dirección de correo válida.", ex); }
            }

            if (mensaje.To.Count == 0) throw new ErrorDeCorreo("No hay destinatarios válidos.");

            mensaje.Subject = asunto ?? "";

            // Solo cuerpo de texto, como en el escritorio. Además de ser lo que la aplicación
            // necesita, un correo en HTML obligaría a decidir qué se escapa y qué no en algo que
            // acaba en el cliente de correo de otra persona.
            var constructor = new BodyBuilder { TextBody = cuerpo ?? "" };

            if (adjunto is { Contenido.Length: > 0 })
                constructor.Attachments.Add(
                    NombreDeAdjunto(adjunto.Nombre), adjunto.Contenido, TipoDeAdjunto(adjunto.TipoDeMedio));

            mensaje.Body = constructor.ToMessageBody();

            using var cliente = new SmtpClient();
            await ConectarSmtpAsync(cliente, configuracion, token);
            await cliente.SendAsync(mensaje, token);
            await cliente.DisconnectAsync(true, token);
            return true;
        }, ct);

    /// <inheritdoc/>
    public Task<string> ProbarConexionAsync(ConfiguracionDeCorreo configuracion, CancellationToken ct = default) =>
        EjecutarAsync("probar la conexión", async token =>
        {
            using (var smtp = new SmtpClient())
            {
                await ConectarSmtpAsync(smtp, configuracion, token);
                await smtp.DisconnectAsync(true, token);
            }

            // El IMAP se prueba solo si está configurado: hay instalaciones que únicamente envían, y
            // fallar por un servidor que nadie puso sería mentir sobre el estado del correo.
            if (string.IsNullOrWhiteSpace(configuracion.ServidorImap))
                return "Conexión exitosa (SMTP). Sin servidor IMAP configurado: se puede enviar, no leer el buzón.";

            using (var imap = new ImapClient())
            {
                await ConectarImapAsync(imap, configuracion, token);

                // Se ABRE la carpeta de ingesta, no basta con autenticar. Un nombre mal escrito ahí no
                // rompe nada visible hasta que la ingesta corre sola de madrugada y no importa ningún
                // correo; probar la conexión es justo el momento de enterarse, y AbrirAsync ya dice
                // cuál es la carpeta que no existe.
                await AbrirAsync(imap, configuracion.CarpetaDeIngesta, FolderAccess.ReadOnly, token);
                await imap.DisconnectAsync(true, token);
            }

            return $"Conexión exitosa (SMTP + IMAP). La carpeta de ingesta «{configuracion.CarpetaDeIngesta}» existe.";
        }, ct);

    // ── Lectura ──────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListarCarpetasAsync(ConfiguracionDeCorreo configuracion,
        CancellationToken ct = default) =>
        EjecutarAsync("listar las carpetas", async token =>
        {
            using var cliente = new ImapClient();
            await ConectarImapAsync(cliente, configuracion, token);

            var nombres = new List<string> { "INBOX" };
            foreach (var espacio in cliente.PersonalNamespaces)
                foreach (var carpeta in await cliente.GetFoldersAsync(espacio, cancellationToken: token))
                    if (!carpeta.Attributes.HasFlag(FolderAttributes.NonExistent))
                        nombres.Add(carpeta.FullName);

            await cliente.DisconnectAsync(true, token);

            return (IReadOnlyList<string>)nombres.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MensajeDeCorreo>> LeerRecientesAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, int maximo, CancellationToken ct = default) =>
        EjecutarAsync("leer los mensajes", async token =>
        {
            using var cliente = new ImapClient();
            await ConectarImapAsync(cliente, configuracion, token);

            var buzon = await AbrirAsync(cliente, carpeta, FolderAccess.ReadOnly, token);
            var mensajes = new List<MensajeDeCorreo>();

            if (buzon.Count > 0)
            {
                int desde = Math.Max(0, buzon.Count - Math.Max(1, maximo));

                // Los UID se piden de una sola vez para el tramo entero. El escritorio los pedía uno
                // por uno dentro del bucle: dos viajes al servidor por cada mensaje, que con la
                // bandeja llena se notaba.
                var resumenes = await buzon.FetchAsync(desde, buzon.Count - 1, MessageSummaryItems.UniqueId, token);

                foreach (var resumen in resumenes.Reverse())   // del más nuevo al más viejo, como el escritorio
                {
                    token.ThrowIfCancellationRequested();
                    var mensaje = await buzon.GetMessageAsync(resumen.UniqueId, token);
                    mensajes.Add(Describir(mensaje, resumen.UniqueId.Id, buzon.FullName));
                }
            }

            await cliente.DisconnectAsync(true, token);
            return (IReadOnlyList<MensajeDeCorreo>)mensajes;
        }, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MensajeDeCorreo>> LeerSinProcesarAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, CancellationToken ct = default) =>
        EjecutarAsync("leer los mensajes sin procesar", async token =>
        {
            using var cliente = new ImapClient();
            await ConectarImapAsync(cliente, configuracion, token);

            // ReadOnly a propósito: aquí solo se LEE. Marcar es un paso aparte que da el servicio de
            // aplicación cuando el requerimiento ya está guardado; si se marcara aquí, un fallo al
            // guardar dejaría el correo por leído y el requerimiento sin crear — que es justo lo que
            // el escritorio arriesgaba al marcar antes de su SaveChanges final.
            var buzon = await AbrirAsync(cliente, carpeta, FolderAccess.ReadOnly, token);

            var mensajes = new List<MensajeDeCorreo>();
            foreach (var uid in await buzon.SearchAsync(SearchQuery.NotSeen, token))
            {
                token.ThrowIfCancellationRequested();
                var mensaje = await buzon.GetMessageAsync(uid, token);
                mensajes.Add(Describir(mensaje, uid.Id, buzon.FullName));
            }

            await cliente.DisconnectAsync(true, token);
            return (IReadOnlyList<MensajeDeCorreo>)mensajes;
        }, ct);

    /// <inheritdoc/>
    public Task<MensajeDeCorreo?> LeerUnoAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        uint identificador, CancellationToken ct = default) =>
        EjecutarAsync("leer el mensaje", async token =>
        {
            using var cliente = new ImapClient();
            await ConectarImapAsync(cliente, configuracion, token);

            var buzon = await AbrirAsync(cliente, carpeta, FolderAccess.ReadOnly, token);

            MensajeDeCorreo? resultado = null;
            try
            {
                var mensaje = await buzon.GetMessageAsync(new UniqueId(identificador), token);
                resultado = Describir(mensaje, identificador, buzon.FullName);
            }
            catch (MessageNotFoundException)
            {
                // El mensaje se movió o se borró desde que la pantalla lo listó. No es un error del
                // servidor: quien pidió convertirlo tiene que recargar.
            }

            await cliente.DisconnectAsync(true, token);
            return resultado;
        }, ct);

    /// <inheritdoc/>
    public Task MarcarComoProcesadosAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        IReadOnlyCollection<uint> identificadores, CancellationToken ct = default) =>
        EjecutarAsync("marcar los mensajes procesados", async token =>
        {
            if (identificadores.Count == 0) return true;

            using var cliente = new ImapClient();
            await ConectarImapAsync(cliente, configuracion, token);

            var buzon = await AbrirAsync(cliente, carpeta, FolderAccess.ReadWrite, token);
            await buzon.AddFlagsAsync(identificadores.Select(id => new UniqueId(id)).ToList(),
                MessageFlags.Seen, true, token);

            await cliente.DisconnectAsync(true, token);
            return true;
        }, ct);

    // ── Conexión ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Conecta y autentica contra SMTP. El 465 es SSL desde el saludo y el resto STARTTLS: es la
    /// misma regla del escritorio y la que hace que la misma configuración sirva para Office 365 y
    /// para Gmail sin preguntar nada más.
    /// </summary>
    private static async Task ConectarSmtpAsync(SmtpClient cliente, ConfiguracionDeCorreo cfg, CancellationToken ct)
    {
        var seguridad = cfg.PuertoSmtp == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await cliente.ConnectAsync(cfg.ServidorSmtp, cfg.PuertoSmtp, seguridad, ct);
        await cliente.AuthenticateAsync(cfg.Direccion, cfg.Contrasena, ct);
    }

    private static async Task ConectarImapAsync(ImapClient cliente, ConfiguracionDeCorreo cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.ServidorImap))
            throw new ErrorDeCorreo("No hay servidor IMAP configurado: sin él no se puede leer el buzón.");

        await cliente.ConnectAsync(cfg.ServidorImap, cfg.PuertoImap, SecureSocketOptions.SslOnConnect, ct);
        await cliente.AuthenticateAsync(cfg.Direccion, cfg.Contrasena, ct);
    }

    /// <summary>
    /// Abre una carpeta por nombre.
    ///
    /// <para>Se conserva del escritorio que el nombre vacío o «INBOX» sea la bandeja de entrada. Lo
    /// que NO se conserva es que un nombre inexistente cayera en silencio a la bandeja de entrada:
    /// en una pantalla con alguien delante eso se ve, pero la ingesta corre sola, y una carpeta mal
    /// escrita en Configuración habría convertido todo el correo entrante en requerimientos sin que
    /// nadie se enterara. Ahora se dice.</para>
    /// </summary>
    private static async Task<IMailFolder> AbrirAsync(ImapClient cliente, string nombre, FolderAccess acceso,
        CancellationToken ct)
    {
        IMailFolder carpeta;
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            carpeta = cliente.Inbox;
        }
        else
        {
            try { carpeta = await cliente.GetFolderAsync(nombre, ct); }
            catch (FolderNotFoundException ex)
            {
                throw new ErrorDeCorreo(
                    $"La carpeta «{nombre}» no existe en el buzón. Revísala en Configuración.", ex);
            }
        }

        await carpeta.OpenAsync(acceso, ct);
        return carpeta;
    }

    // ── Adjuntos ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El nombre con el que se anuncia el adjunto, sin nada que parezca una ruta.
    ///
    /// <para>El contenido ya viene en memoria, así que aquí no hay ningún fichero que abrir; lo que
    /// se evita es que el nombre acabe escrito con separadores dentro en el buzón de quien lo
    /// recibe, porque ahí sí es una ruta que su cliente de correo interpreta al guardar.</para>
    /// </summary>
    private static string NombreDeAdjunto(string? nombre)
    {
        var limpio = new string([.. (nombre ?? "")
            .Replace('\\', '/').Split('/').Last()
            .Where(c => !char.IsControl(c) && c != '"')]).Trim();

        return limpio.Length == 0 ? "adjunto" : limpio;
    }

    /// <summary>
    /// El tipo MIME del adjunto. Uno que no se entienda cae a binario genérico en vez de tumbar el
    /// envío: el archivo llega igual y quien lo recibe lo abre con lo que le diga la extensión.
    /// </summary>
    private static ContentType TipoDeAdjunto(string? tipo)
    {
        try { return ContentType.Parse(tipo ?? ""); }
        catch (ParseException) { return new ContentType("application", "octet-stream"); }
    }

    // ── Traducción de los mensajes y de los fallos ───────────────────────────────

    private static MensajeDeCorreo Describir(MimeMessage mensaje, uint uid, string carpeta)
    {
        string cuerpo = mensaje.TextBody ?? SinEtiquetas(mensaje.HtmlBody) ?? "";

        return new MensajeDeCorreo(
            uid,
            Identidad(mensaje, carpeta, uid),
            string.IsNullOrWhiteSpace(mensaje.Subject) ? "(sin asunto)" : mensaje.Subject.Trim(),
            mensaje.From.ToString(),
            mensaje.Date.UtcDateTime,
            Recortar(cuerpo, LargoDeLaVista),
            cuerpo);
    }

    private static string Identidad(MimeMessage mensaje, string carpeta, uint uid) =>
        string.IsNullOrWhiteSpace(mensaje.MessageId)
            ? $"{carpeta}#{uid}"
            : Recortar(mensaje.MessageId.Trim(), 250);

    private static string Recortar(string texto, int maximo) =>
        string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>
    /// Deja en texto plano un cuerpo que venía en HTML. Copiado del escritorio, y aquí importa más:
    /// es lo que garantiza que a la pantalla nunca le llegue marcado que alguien de fuera escribió.
    /// </summary>
    private static string? SinEtiquetas(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Trim();
    }

    /// <summary>
    /// Corre la operación y convierte cualquier fallo en un <see cref="ErrorDeCorreo"/> que se pueda
    /// enseñar. El detalle técnico se queda en el registro del servidor.
    /// </summary>
    private async Task<T> EjecutarAsync<T>(string queSeIntentaba, Func<CancellationToken, Task<T>> operacion,
        CancellationToken ct)
    {
        try
        {
            return await operacion(ct);
        }
        catch (OperationCanceledException)
        {
            throw;   // el servidor se está apagando o la petición se abortó: no es un fallo del correo
        }
        catch (ErrorDeCorreo)
        {
            throw;   // ya viene traducido
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo del correo al {Que}.", queSeIntentaba);
            throw Traducir(ex, queSeIntentaba);
        }
    }

    /// <summary>
    /// Pone en palabras lo que pasó.
    ///
    /// <para>El mensaje de la excepción de autenticación NO se reenvía: lo escribe el servidor de
    /// correo y no hay ninguna garantía de qué trae dentro. Lo que sí se sabe es qué hay que hacer
    /// —recapturar la contraseña de aplicación—, y eso es lo que se dice.</para>
    /// </summary>
    private static ErrorDeCorreo Traducir(Exception ex, string queSeIntentaba) => ex switch
    {
        AuthenticationException or ServiceNotAuthenticatedException => new ErrorDeCorreo(
            "El servidor de correo rechazó la cuenta o la contraseña de aplicación. "
            + "Vuelve a capturarla en Configuración.", ex),

        SslHandshakeException => new ErrorDeCorreo(
            $"No se pudo establecer la conexión segura al {queSeIntentaba}. "
            + "Suele ser el puerto: 465 va con SSL y 587 con STARTTLS.", ex),

        SocketException or IOException or ServiceNotConnectedException => new ErrorDeCorreo(
            $"No se pudo contactar al servidor de correo al {queSeIntentaba}: {ex.Message}", ex),

        ImapCommandException or ImapProtocolException => new ErrorDeCorreo(
            $"El servidor IMAP rechazó la operación al {queSeIntentaba}.", ex),

        SmtpCommandException or SmtpProtocolException => new ErrorDeCorreo(
            "El servidor SMTP rechazó el envío. Revisa la cuenta y los destinatarios.", ex),

        _ => new ErrorDeCorreo($"No se pudo {queSeIntentaba}. Revisa el registro del servidor para el detalle.", ex)
    };
}
