using System.Security.Cryptography;
using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que hace falta para dar de alta el segundo factor: el secreto en texto, el URI y el dibujo del
/// código QR. <b>Se entrega una vez y no se guarda en ninguna pantalla</b>: si la persona recarga,
/// se empieza de nuevo con un secreto distinto.
/// </summary>
public record InicioDeAltaDeSegundoFactor(string SecretoEnBase32, string Uri, byte[] CodigoQrPng);

/// <summary>Cómo terminó un intento de segundo factor.</summary>
public record ResultadoDeSegundoFactor(
    bool Exito,
    string Mensaje,
    bool FueCodigoDeRescate = false,
    int CodigosDeRescateRestantes = 0);

/// <summary>Lo que se le puede contar a una pantalla sobre el segundo factor de alguien.</summary>
public record EstadoDeSegundoFactor(bool Activo, DateTime? DesdeUtc, int CodigosDeRescateRestantes);

/// <summary>
/// El segundo factor: el código de seis dígitos de la aplicación del teléfono.
///
/// <para><b>El orden del alta es lo que este servicio protege por encima de todo.</b> Enseñar el
/// código QR y dar el segundo factor por activado en ese momento es el error que arruina estas
/// implementaciones: si el escaneo salió mal —el QR se cortó, la cámara leyó otra cosa, la persona
/// lo dio de alta en la aplicación equivocada— la aplicación del teléfono NO avisa de nada; sigue
/// mostrando códigos de seis dígitos, solo que no son los buenos. La persona cierra la pantalla
/// convencida, y se entera al día siguiente, cuando ya no puede entrar y nadie sabe por qué.</para>
///
/// <para><b>Aquí ese orden es imposible de invertir, y no por disciplina sino por construcción:</b></para>
/// <list type="number">
///   <item><see cref="ComenzarAltaAsync"/> NO toca la tabla de usuarios. Escribe el secreto bajo el
///   propósito <c>2fa.totp.pendiente</c> y nada más.</item>
///   <item>Al entrar solo se lee el propósito <c>2fa.totp</c>, y en ese propósito no escribe nadie
///   más que <see cref="ConfirmarAltaAsync"/>, después de que un código tecleado haya coincidido.</item>
///   <item><c>SegundoFactorActivo = true</c> se asigna en un solo sitio del sistema entero: dentro
///   de ese mismo <see cref="ConfirmarAltaAsync"/> y detrás de la comprobación.</item>
/// </list>
/// <para>Consecuencia: aunque alguien pusiera la columna en cierto a mano en la base, no habría
/// secreto que comparar y el sistema lo diría con todas sus letras en vez de dejar a esa persona
/// fuera en silencio. No hay ningún camino que active el segundo factor sin haberlo probado.</para>
///
/// <para><b>Qué NO está aquí.</b> Las pantallas y el corte de las peticiones de quien todavía no lo
/// ha activado. Este servicio calcula, guarda y responde; quien pregunta decide.</para>
/// </summary>
public class SegundoFactorService(
    AppDbContext db,
    ICurrentUser currentUser,
    IProtectorDeSecretos protector,
    IDibujanteDeCodigoQr dibujante,
    AuditService audit)
{
    /// <summary>
    /// Cuántos días se recuerda un navegador antes de volver a pedir el código.
    ///
    /// <para>Público porque las pantallas se lo explican a la persona («no te lo volveremos a pedir
    /// en este equipo durante 30 días») y tener el número escrito a mano en la interfaz garantiza
    /// que un día dejen de coincidir. Es la misma razón por la que <c>AuthService</c> publica sus
    /// constantes de bloqueo.</para>
    /// </summary>
    public const int DiasQueSeRecuerdaElEquipo = 30;

    /// <summary>
    /// A partir de cuántos códigos de rescate restantes conviene avisar. No corta nada: es el umbral
    /// que usa la interfaz para decir «te quedan pocos» antes de que la persona se quede sin salida.
    /// </summary>
    public const int CodigosDeRescateParaAvisar = 2;

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ALTA — el orden es el que está descrito arriba y no se puede invertir
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PASO 1 de 2: genera un secreto nuevo y devuelve con qué darlo de alta en el teléfono.
    ///
    /// <para><b>Esto no activa nada.</b> Lo único que escribe es el secreto pendiente, cifrado. La
    /// cuenta sigue sin segundo factor hasta que <see cref="ConfirmarAltaAsync"/> reciba un código
    /// correcto, y mientras tanto ni estorba —el pendiente no se mira al entrar— ni protege.</para>
    ///
    /// <para>Volver a llamar SOBRESCRIBE el pendiente con otro secreto. Es lo correcto: quien vuelve
    /// a pedir el QR es porque el anterior no le sirvió, y dejar vivo el viejo permitiría confirmar
    /// con un secreto que ya no es el que está en pantalla.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, InicioDeAltaDeSegundoFactor? alta)> ComenzarAltaAsync(
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.", null);

        // Con el segundo factor ya activo no se empieza otra alta. Cambiar de teléfono pasa por que
        // el líder lo reinicie, y eso es deliberado: si bastara con dar de alta otro aparato desde
        // una sesión abierta, quien se hiciera con esa sesión se colocaría a sí mismo como segundo
        // factor de la cuenta y dejaría fuera a su dueña sin que ella pudiera hacer nada.
        if (user.SegundoFactorActivo)
            return (false, "Ya tienes el segundo factor activo. Para cambiar de teléfono, pide al líder que te lo reinicie.", null);

        var secreto = Totp.GenerarSecretoEnBase32();
        await GuardarSecretoAsync(userId, PropositosDeSecreto.SegundoFactorPendiente, secreto, ct);

        var uri = UriDeSegundoFactor.Construir(user.Username, secreto);
        var png = dibujante.DibujarPng(uri);

        // A la bitácora va el hecho, jamás el secreto ni el URI —que lo contiene entero—.
        await audit.RecordAsync(AuditAction.Update, "User", userId.ToString(),
            "Alta de segundo factor iniciada (todavía sin confirmar)", ct);

        return (true, "Escanea el código con tu aplicación de códigos y teclea el que te muestre.",
                new InicioDeAltaDeSegundoFactor(secreto, uri, png));
    }

    /// <summary>
    /// PASO 2 de 2: comprueba que el teléfono quedó bien dado de alta y ENTONCES activa.
    ///
    /// <para>Todo lo que cambia lo hace en un solo guardado: se promueve el secreto pendiente a
    /// definitivo, se marca la cuenta como activa, se anota la ventana usada —para que ese mismo
    /// código no valga otra vez— y se emiten los ocho códigos de rescate. Que sea un solo guardado
    /// importa: una cuenta marcada como activa cuyo secreto no llegó a promoverse sería una cuenta
    /// que nadie puede abrir.</para>
    ///
    /// <para>Devuelve los ocho códigos EN CLARO. Es la única vez que existen: en la base solo queda
    /// su hash.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, IReadOnlyList<string>? codigosDeRescate)> ConfirmarAltaAsync(
        string codigo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.", null);
        if (user.SegundoFactorActivo) return (false, "Ya tienes el segundo factor activo.", null);

        var pendiente = await db.UserSecrets
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Proposito == PropositosDeSecreto.SegundoFactorPendiente, ct);

        if (pendiente == null)
            return (false, "No hay ningún alta en curso. Vuelve a pedir el código QR.", null);

        var secretoEnClaro = protector.Desproteger(pendiente.CipherText);
        var secreto = Base32.Decodificar(secretoEnClaro);
        if (secreto == null)
            return (false, "El alta en curso no se puede leer. Vuelve a pedir el código QR.", null);

        var ahora = DateTimeOffset.UtcNow;
        var comprobacion = Totp.Verificar(secreto, codigo, user.SegundoFactorUltimaVentana, ahora);

        if (!comprobacion.Valido)
        {
            // Aquí NO se cuentan intentos fallidos ni se bloquea la cuenta, a diferencia de al
            // entrar. Quien está en esta pantalla ya demostró saber su contraseña y está dentro de
            // su propia sesión: bloquearlo no le quita nada a un atacante y sí convierte tres
            // teclazos torpes en una llamada al líder. Lo que sí se hace es no decir nunca «casi»:
            // el mensaje distingue el formato del código equivocado, que es lo que ayuda de verdad.
            return (false, MensajeDeCodigoRechazado(comprobacion.Motivo), null);
        }

        // ── A partir de aquí ya está probado que el teléfono calcula lo mismo que el servidor ──
        db.UserSecrets.Remove(pendiente);
        await GuardarSecretoAsync(userId, PropositosDeSecreto.SegundoFactor, secretoEnClaro!, ct, guardar: false);

        user.SegundoFactorActivo = true;
        user.SegundoFactorDesdeUtc = DateTime.UtcNow;
        // La ventana del código que acaba de usarse queda anotada: ese mismo código no vuelve a
        // valer, ni siquiera durante los segundos que le queden de vida.
        user.SegundoFactorUltimaVentana = comprobacion.Ventana;

        var codigos = ReemplazarCodigosDeRescate(userId);

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", userId.ToString(),
            $"Segundo factor activado, con {CodigosDeRescate.Cuantos} códigos de rescate emitidos", ct);

        return (true, "Segundo factor activado. Guarda los códigos de rescate: no se vuelven a mostrar.", codigos);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ENTRAR
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprueba el segundo factor de alguien que acaba de acertar su contraseña.
    ///
    /// <para><b>Recibe el usuario por parámetro y no lo saca de la sesión a propósito</b>, porque en
    /// este punto todavía NO hay sesión: la persona pasó la contraseña y no se le ha emitido nada.
    /// Es la misma forma que tiene <c>AuthService.LoginAsync</c> y arrastra la misma
    /// responsabilidad: <b>quien llame a esto debe haber verificado antes la contraseña</b>. Llamarlo
    /// sin eso convertiría el segundo factor en el único factor.</para>
    ///
    /// <para>Acepta las dos cosas —el código de seis dígitos del teléfono y un código de rescate— y
    /// distingue por la forma de lo tecleado. Tenerlo en una sola puerta evita el fallo de que un
    /// camino aplique el bloqueo por intentos y el otro no.</para>
    /// </summary>
    public async Task<ResultadoDeSegundoFactor> VerificarAsync(
        int userId, string? codigo, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user == null) return new(false, "Usuario o contraseña incorrectos.");

        // El bloqueo por intentos fallidos es el MISMO que el de la contraseña, y se reutiliza a
        // propósito: sin él, el segundo factor sería seis dígitos —un millón de posibilidades—
        // contra un atacante que ya tiene la contraseña y puede probar sin límite. Con cinco
        // intentos cada quince minutos, agotar ese millón lleva siglos.
        if (AuthService.EstaBloqueado(user))
            return new(false, "Cuenta bloqueada temporalmente por intentos fallidos. " +
                              $"Reintenta a las {user.LockoutUntil!.Value.ToLocalTime():HH:mm} o pide al líder que la desbloquee.");

        if (!user.SegundoFactorActivo)
            return new(false, "Esta cuenta todavía no tiene segundo factor. Actívalo para poder entrar.");

        // Seis dígitos es un código del teléfono; cualquier otra cosa se prueba como código de
        // rescate. Los dos formatos no se solapan: los de rescate son dieciséis caracteres y llevan
        // letras, así que nada de lo que teclee una persona puede pasar por los dos.
        bool pareceCodigoDelTelefono = EsSeisDigitos(codigo);

        return pareceCodigoDelTelefono
            ? await VerificarCodigoDelTelefonoAsync(user, codigo!, ct)
            : await CanjearCodigoDeRescateAsync(user, codigo, ct);
    }

    private async Task<ResultadoDeSegundoFactor> VerificarCodigoDelTelefonoAsync(
        User user, string codigo, CancellationToken ct)
    {
        var cifrado = await db.UserSecrets.AsNoTracking()
            .Where(s => s.UserId == user.Id && s.Proposito == PropositosDeSecreto.SegundoFactor)
            .Select(s => s.CipherText)
            .FirstOrDefaultAsync(ct);

        var secreto = Base32.Decodificar(cifrado == null ? null : protector.Desproteger(cifrado));

        if (secreto == null)
        {
            // Cuenta marcada como activa pero sin secreto legible. Solo puede pasar si se perdió el
            // llavero de la protección de datos o si alguien tocó la columna a mano. Se dice tal
            // cual y se señala la salida, porque el silencio aquí es una persona intentando entrar
            // una y otra vez con códigos que están bien.
            await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
                "Segundo factor activo pero sin secreto legible: hace falta reiniciarlo", ct);

            return new(false, "No se puede leer tu segundo factor. Usa un código de rescate o pide al líder que te lo reinicie.");
        }

        var comprobacion = Totp.Verificar(secreto, codigo, user.SegundoFactorUltimaVentana, DateTimeOffset.UtcNow);

        if (!comprobacion.Valido)
            return await RegistrarIntentoFallidoAsync(user, MensajeDeCodigoRechazado(comprobacion.Motivo), ct);

        // Anotar la ventana ES la antirrepetición. Si esta línea desapareciera, todo seguiría
        // funcionando y el mismo código valdría hasta minuto y medio para quien lo hubiera visto.
        user.SegundoFactorUltimaVentana = comprobacion.Ventana;
        LimpiarContadoresDeBloqueo(user);
        await db.SaveChangesAsync(ct);

        return new(true, "OK", false, await ContarCodigosDeRescateAsync(user.Id, ct));
    }

    private async Task<ResultadoDeSegundoFactor> CanjearCodigoDeRescateAsync(
        User user, string? tecleado, CancellationToken ct)
    {
        var hash = CodigosDeRescate.HashearLoTecleado(tecleado);
        if (hash == null)
            return await RegistrarIntentoFallidoAsync(user,
                "Ese código no tiene la forma de un código del teléfono ni de uno de rescate.", ct);

        // Se busca por hash, no se comparan los ocho uno por uno: una consulta por índice en vez de
        // ocho comprobaciones. Ver CodigosDeRescate.Hashear, que explica por qué el hash es rápido.
        var fila = await db.UserRecoveryCodes
            .FirstOrDefaultAsync(c => c.UserId == user.Id && c.CodigoHash == hash && c.UsadoEnUtc == null, ct);

        if (fila == null)
            return await RegistrarIntentoFallidoAsync(user,
                "Ese código de rescate no es válido o ya se usó.", ct);

        // Gastado, no borrado: la fila es la constancia de que alguien entró por aquí y de cuándo.
        fila.UsadoEnUtc = DateTime.UtcNow;
        LimpiarContadoresDeBloqueo(user);
        await db.SaveChangesAsync(ct);

        int quedan = await ContarCodigosDeRescateAsync(user.Id, ct);

        // Este SÍ se registra siempre, aunque haya salido bien. Entrar con un código de rescate
        // significa que alguien se quedó sin su teléfono, y esa es justo la señal que hay que poder
        // buscar después en la bitácora si algo resulta no haber sido lo que parecía.
        await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
            $"Entrada con código de rescate. Quedan {quedan} de {CodigosDeRescate.Cuantos}.", ct);

        var aviso = quedan == 0
            ? " Era el último: pide al líder que te reinicie el segundo factor."
            : quedan <= CodigosDeRescateParaAvisar ? $" Te quedan {quedan}." : "";

        return new(true, "Código de rescate aceptado." + aviso, true, quedan);
    }

    /// <summary>
    /// Suma un intento fallido y bloquea la cuenta al llegar al tope. Es una copia exacta de lo que
    /// hace <c>AuthService</c> con la contraseña, con sus mismas constantes: dos reglas distintas
    /// para la misma cuenta serían dos números que un día dejan de coincidir.
    /// </summary>
    private async Task<ResultadoDeSegundoFactor> RegistrarIntentoFallidoAsync(
        User user, string mensaje, CancellationToken ct)
    {
        user.FailedLoginCount++;

        if (user.FailedLoginCount >= AuthService.MaxFailedAttempts)
        {
            user.LockoutUntil = DateTime.UtcNow.AddMinutes(AuthService.LockoutMinutes);
            user.FailedLoginCount = 0;
            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(AuditAction.Login, "User", user.Id.ToString(),
                $"Cuenta bloqueada {AuthService.LockoutMinutes} min por {AuthService.MaxFailedAttempts} " +
                "códigos de segundo factor incorrectos", ct);

            return new(false, $"Cuenta bloqueada {AuthService.LockoutMinutes} minutos por demasiados intentos.");
        }

        await db.SaveChangesAsync(ct);
        return new(false, mensaje);
    }

    private static void LimpiarContadoresDeBloqueo(User user)
    {
        // Igual que al acertar la contraseña: quien entra bien deja el contador a cero, o el
        // siguiente error de un día cualquiera arrastraría los fallos de la semana pasada.
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  CÓDIGOS DE RESCATE
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Cuántos códigos de rescate le quedan sin gastar a una cuenta.</summary>
    public Task<int> ContarCodigosDeRescateAsync(int userId, CancellationToken ct = default) =>
        db.UserRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsadoEnUtc == null, ct);

    /// <summary>
    /// Emite ocho códigos nuevos y tira los anteriores, para quien se está quedando sin ellos.
    ///
    /// <para><b>Exige un código del teléfono para hacerlo</b>, y no basta con estar dentro. Sin esa
    /// exigencia, quien se hiciera con una sesión abierta —un equipo sin bloquear, una cookie
    /// robada— se llevaría ocho llaves permanentes de esa cuenta sin haber tenido nunca el teléfono
    /// delante. Con ella, para llevárselas hay que seguir teniendo el segundo factor.</para>
    ///
    /// <para><b>Y los intentos se cuentan, igual que al entrar.</b> Es la diferencia con
    /// <see cref="ConfirmarAltaAsync"/> —donde a propósito NO se cuentan— y tiene un motivo concreto:
    /// allí fallar no le da nada a nadie, aquí acertar entrega ocho llaves permanentes. Sin contar,
    /// la exigencia del párrafo anterior sería decorativa: quien tuviera la sesión robada probaría el
    /// millón de códigos posibles sin que nada se lo impidiera y acabaría llevándoselas igual. El
    /// precio asumido es que cinco errores de dedo seguidos bloquean la cuenta quince minutos, lo
    /// mismo que ya pasa en la pantalla de acceso.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, IReadOnlyList<string>? codigosDeRescate)> RegenerarCodigosDeRescateAsync(
        string codigoDelTelefono, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.", null);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.", null);
        if (!user.SegundoFactorActivo) return (false, "No tienes el segundo factor activo.", null);

        if (AuthService.EstaBloqueado(user))
            return (false, "Cuenta bloqueada temporalmente por intentos fallidos. " +
                           $"Reintenta a las {user.LockoutUntil!.Value.ToLocalTime():HH:mm}.", null);

        var cifrado = await db.UserSecrets.AsNoTracking()
            .Where(s => s.UserId == userId && s.Proposito == PropositosDeSecreto.SegundoFactor)
            .Select(s => s.CipherText)
            .FirstOrDefaultAsync(ct);

        var secreto = Base32.Decodificar(cifrado == null ? null : protector.Desproteger(cifrado));
        if (secreto == null)
            return (false, "No se puede leer tu segundo factor. Pide al líder que te lo reinicie.", null);

        var comprobacion = Totp.Verificar(secreto, codigoDelTelefono, user.SegundoFactorUltimaVentana, DateTimeOffset.UtcNow);
        if (!comprobacion.Valido)
        {
            var fallo = await RegistrarIntentoFallidoAsync(user, MensajeDeCodigoRechazado(comprobacion.Motivo), ct);
            return (false, fallo.Mensaje, null);
        }

        user.SegundoFactorUltimaVentana = comprobacion.Ventana;

        // Acertar aquí es haber demostrado que se tiene el teléfono, así que los fallos previos dejan
        // de contar — igual que al entrar bien.
        LimpiarContadoresDeBloqueo(user);

        var codigos = ReemplazarCodigosDeRescate(userId);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", userId.ToString(),
            $"Códigos de rescate regenerados ({CodigosDeRescate.Cuantos} nuevos, los anteriores dejan de valer)", ct);

        return (true, "Códigos nuevos. Los anteriores ya no sirven; guarda estos.", codigos);
    }

    /// <summary>
    /// Deja ocho códigos nuevos en el contexto y devuelve los de siempre en claro. NO guarda: quien
    /// llama decide en qué guardado entran, que es lo que permite que el alta sea una sola escritura.
    /// </summary>
    private IReadOnlyList<string> ReemplazarCodigosDeRescate(int userId)
    {
        // Los anteriores se BORRAN y no se marcan como usados: no se gastaron, dejaron de existir.
        // Dejarlos marcados haría que «cuántos has usado» mintiera para siempre.
        db.UserRecoveryCodes.RemoveRange(db.UserRecoveryCodes.Where(c => c.UserId == userId));

        var codigos = CodigosDeRescate.Generar();
        var ahora = DateTime.UtcNow;

        foreach (var codigo in codigos)
        {
            // Se hashea la forma canónica —la misma que producirá quien lo teclee más tarde con
            // guiones o en minúsculas—, o el código bueno no encontraría su propia fila.
            var canonico = CodigosDeRescate.Normalizar(codigo)!;
            db.UserRecoveryCodes.Add(new UserRecoveryCode
            {
                UserId = userId,
                CodigoHash = CodigosDeRescate.Hashear(canonico),
                CreatedAtUtc = ahora
            });
        }

        return codigos;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EQUIPOS RECORDADOS (treinta días)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Marca este navegador como conocido y devuelve el testigo que hay que guardar en su cookie.
    ///
    /// <para>El testigo se devuelve UNA vez y no se puede volver a consultar: en la base solo queda
    /// su hash, igual que con los códigos de rescate. Si se pierde, el navegador simplemente vuelve
    /// a pedir el código.</para>
    ///
    /// <para><b>La caducidad la fija el servidor.</b> La de la cookie la puede cambiar cualquiera
    /// desde su propio navegador; la de esta fila, no.</para>
    /// </summary>
    public async Task<string> RecordarEsteEquipoAsync(int userId, string? descripcion, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        // Barrido de oportunidad: las filas vencidas de esta persona no valen nada y se van ahora,
        // que es cuando ya estamos escribiendo. No hace falta un trabajo programado para dos o tres
        // filas por cuenta al mes.
        db.UserTrustedDevices.RemoveRange(
            db.UserTrustedDevices.Where(d => d.UserId == userId && d.ExpiraEnUtc <= ahora));

        // 256 bits: adivinarlo no es una vía de ataque, así que no hace falta ni contarlos ni
        // bloquear por intentos. En hexadecimal para que quepa en una cookie sin escapar nada.
        var testigo = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        db.UserTrustedDevices.Add(new UserTrustedDevice
        {
            UserId = userId,
            TokenHash = HashearTestigo(testigo),
            CreatedAtUtc = ahora,
            ExpiraEnUtc = ahora.AddDays(DiasQueSeRecuerdaElEquipo),
            Descripcion = Recortar(descripcion, 200)
        });

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", userId.ToString(),
            $"Equipo recordado {DiasQueSeRecuerdaElEquipo} días: {descripcion ?? "sin descripción"}", ct);

        return testigo;
    }

    /// <summary>
    /// ¿A este navegador ya se le puede ahorrar el código?
    ///
    /// <para>Comprueba tres cosas y las tres importan: que el testigo exista, que sea DE ESA CUENTA
    /// —una cookie de otra persona no puede servir para entrar en esta— y que no haya vencido según
    /// el reloj del servidor.</para>
    /// </summary>
    public async Task<bool> EsEquipoRecordadoAsync(int userId, string? testigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testigo)) return false;

        var fila = await db.UserTrustedDevices
            .FirstOrDefaultAsync(d => d.TokenHash == HashearTestigo(testigo), ct);

        if (fila == null || fila.UserId != userId) return false;

        var ahora = DateTime.UtcNow;
        if (fila.ExpiraEnUtc <= ahora)
        {
            // Vencida: se retira en el momento en que se descubre. Así la tabla se limpia sola por
            // el uso y no hace falta un trabajo programado que la recorra.
            db.UserTrustedDevices.Remove(fila);
            await db.SaveChangesAsync(ct);
            return false;
        }

        fila.UltimoUsoUtc = ahora;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // «Olvidar todos los equipos de una cuenta» NO está aquí, y es a propósito: vive en
    // AuthService.OlvidarEquiposRecordadosAsync, que es quien además DEJA EL ASIENTO EN LA BITÁCORA.
    // Hubo una segunda copia en este servicio, sin asiento, y se retiró: quitarle a alguien todos sus
    // equipos de confianza es una operación que hay que poder mirar después, y una copia muda es una
    // trampa esperando a que alguien la llame creyendo que hace lo mismo que la otra. El reinicio de
    // más abajo borra esas filas dentro de su propio guardado, con su propio asiento.

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ESTADO Y REINICIO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>El estado del segundo factor de una cuenta. Nunca incluye nada del secreto.</summary>
    public async Task<EstadoDeSegundoFactor> EstadoAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SegundoFactorActivo, u.SegundoFactorDesdeUtc })
            .FirstOrDefaultAsync(ct);

        if (user == null) return new EstadoDeSegundoFactor(false, null, 0);

        return new EstadoDeSegundoFactor(
            user.SegundoFactorActivo, user.SegundoFactorDesdeUtc,
            await ContarCodigosDeRescateAsync(userId, ct));
    }

    /// <summary>El estado del propio usuario de la petición.</summary>
    public Task<EstadoDeSegundoFactor> MiEstadoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return currentUser.UserId is int id
            ? EstadoAsync(id, ct)
            : Task.FromResult(new EstadoDeSegundoFactor(false, null, 0));
    }

    /// <summary>
    /// El líder reinicia el segundo factor de alguien: la salida para el teléfono perdido cuando
    /// tampoco quedan códigos de rescate.
    ///
    /// <para><b>Deja la cuenta como si nunca lo hubiera tenido</b>, no la deja sin protección: el
    /// segundo factor es obligatorio, así que a la siguiente entrada se le vuelve a exigir el alta.
    /// Lo que se borra es todo lo que dependía del teléfono viejo — el secreto, el alta a medias si
    /// la hubiera, los códigos de rescate y los navegadores recordados.</para>
    ///
    /// <para><b>Los navegadores recordados se van también</b>, y esa es la parte que se olvida. Si
    /// alguien pide un reinicio porque le robaron el teléfono, dejar vivo el navegador de ese
    /// teléfono —o el del equipo que se llevaron con él— sería reiniciar el segundo factor
    /// dejándole al ladrón la puerta por la que ya entraba sin código.</para>
    ///
    /// <para><b>Y SE ROTA EL SELLO DE SEGURIDAD, que es lo que echa fuera a las sesiones abiertas.</b>
    /// Sin eso el reinicio no reinicia nada de lo que importa, por dos motivos a la vez. Uno: la
    /// cookie de quien ya estaba dentro se emitió cuando su segundo factor estaba activo, así que no
    /// lleva el aviso de «te falta activarlo» y el middleware no le exigiría nada — la cuenta seguiría
    /// funcionando horas SIN ningún segundo factor, que es exactamente lo que la regla «obligatorio
    /// para todos» prohíbe. Dos: si el motivo del reinicio es que alguien se quedó con el teléfono o
    /// con la sesión, dejarle la sesión viva convierte esta operación en un trámite decorativo.
    /// Rotando el sello, la siguiente petición de cualquier sesión abierta de esa cuenta la echa la
    /// revalidación de la cookie, y al volver a entrar se le exige el alta.</para>
    ///
    /// <para>Queda asiento en la bitácora con quién lo hizo y a quién. Es una operación que quita
    /// una protección: tiene que poder mirarse después.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ReiniciarAsync(int userId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, "Usuario no encontrado.");

        bool loTenia = user.SegundoFactorActivo;

        db.UserSecrets.RemoveRange(db.UserSecrets.Where(s =>
            s.UserId == userId &&
            (s.Proposito == PropositosDeSecreto.SegundoFactor ||
             s.Proposito == PropositosDeSecreto.SegundoFactorPendiente)));

        db.UserRecoveryCodes.RemoveRange(db.UserRecoveryCodes.Where(c => c.UserId == userId));
        db.UserTrustedDevices.RemoveRange(db.UserTrustedDevices.Where(d => d.UserId == userId));

        user.SegundoFactorActivo = false;
        user.SegundoFactorDesdeUtc = null;
        // La ventana también se limpia: pertenecía a un secreto que ya no existe, y dejarla podría
        // hacer que el primer código del teléfono NUEVO se rechazara por «ya usado» si el reloj
        // cayera en la misma ventana. Sería el peor momento para un rechazo inexplicable.
        user.SegundoFactorUltimaVentana = null;

        // Se levanta también el bloqueo por intentos: quien llega hasta aquí suele venir de haber
        // probado su código viejo cinco veces, y reiniciarle el segundo factor para dejarlo
        // bloqueado quince minutos más no arregla nada.
        LimpiarContadoresDeBloqueo(user);

        // El sello nuevo, en el MISMO guardado que todo lo anterior: no debe existir ni un instante
        // con el segundo factor ya borrado y la sesión vieja todavía válida. Ver el resumen de arriba.
        user.SecurityStamp = AuthService.NuevoSello();

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", user.Id.ToString(),
            loTenia
                ? $"Segundo factor REINICIADO por el líder para «{user.Username}»: tendrá que darlo de alta otra vez"
                : $"Segundo factor de «{user.Username}» limpiado por el líder (no lo tenía activo)", ct);

        return (true, loTenia
            ? $"Segundo factor de «{user.Username}» reiniciado. Al entrar tendrá que darlo de alta de nuevo."
            : $"«{user.Username}» no tenía el segundo factor activo. Se limpió lo que hubiera pendiente.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  Piezas internas
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Guarda o reemplaza un secreto del segundo factor, cifrado.
    ///
    /// <para>No se reutiliza <c>UserSecretsService</c> a propósito: aquel guarda SIEMPRE el secreto
    /// del usuario de la petición —y con razón, porque un id por parámetro basta que un llamador lo
    /// pase mal para escribirle el secreto a otra persona—. Aquí hace falta poder escribir el de un
    /// id concreto durante el alta, así que la escritura vive donde están las reglas que la
    /// justifican y no se le abre un agujero al servicio general.</para>
    /// </summary>
    private async Task GuardarSecretoAsync(
        int userId, string proposito, string valorEnClaro, CancellationToken ct, bool guardar = true)
    {
        var fila = await db.UserSecrets
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Proposito == proposito, ct);

        if (fila == null)
        {
            fila = new UserSecret { UserId = userId, Proposito = proposito };
            db.UserSecrets.Add(fila);
        }

        fila.CipherText = protector.Proteger(valorEnClaro);
        fila.UpdatedAt = DateTime.UtcNow;

        if (guardar) await db.SaveChangesAsync(ct);
    }

    /// <summary>SHA-256 del testigo del navegador, en hexadecimal minúsculo.</summary>
    private static string HashearTestigo(string testigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testigo))).ToLowerInvariant();

    /// <summary>¿Lo tecleado son seis dígitos, ignorando espacios y guiones?</summary>
    private static bool EsSeisDigitos(string? tecleado)
    {
        if (string.IsNullOrWhiteSpace(tecleado)) return false;

        int digitos = 0;
        foreach (var c in tecleado)
        {
            if (c is ' ' or '-' or '\t') continue;
            if (!char.IsAsciiDigit(c)) return false;
            digitos++;
        }
        return digitos == Totp.Digitos;
    }

    /// <summary>
    /// El mensaje de un código rechazado, según por qué.
    ///
    /// <para>Distinguir «ya lo usaste» de «no coincide» NO le regala nada a un atacante: para ver
    /// «ya lo usaste» hace falta haber tecleado un código correcto, o sea tener el teléfono. Y a
    /// quien sí lo tiene le ahorra el peor rato de todos, el de repetir un código que la aplicación
    /// le sigue mostrando y que la pantalla rechaza sin explicar nada.</para>
    /// </summary>
    private static string MensajeDeCodigoRechazado(MotivoDelCodigo motivo) => motivo switch
    {
        MotivoDelCodigo.FormatoInvalido => $"El código son {Totp.Digitos} dígitos, los que muestra tu aplicación.",
        MotivoDelCodigo.YaSeUso => "Ese código ya se usó. Espera al siguiente, que aparece en menos de 30 segundos.",
        _ => "Ese código no es correcto. Comprueba que la hora de tu teléfono esté en automático."
    };

    private static string? Recortar(string? texto, int maximo) =>
        string.IsNullOrWhiteSpace(texto) ? null
        : texto.Length <= maximo ? texto
        : texto[..maximo];
}
