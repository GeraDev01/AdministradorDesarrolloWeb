using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Una persona en el tablero de presencia: su jornada abierta o su última jornada.</summary>
/// <param name="RegistraJornada">
/// False en las cuentas que NO abren jornada —hoy, las de Operaciones—. Existe para que la pantalla
/// pueda distinguir dos cosas que se ven igual y no lo son: «nunca ha abierto la aplicación» y «la
/// aplicación no le apunta las horas a propósito». Sin este dato, la columna «Desde» de un operativo
/// diría «nunca ha entrado» todos los días de su vida laboral, que es falso y acaba en un ticket.
/// <para>Va con valor por omisión <c>true</c> para no obligar a tocar a quien ya construye o consume
/// este registro: quien no sepa de esto sigue viendo el mundo de antes.</para>
/// </param>
public record PresenciaDeUsuario(
    int UserId,
    string Nombre,
    bool Conectado,
    PresenceState Estado,
    string? Nota,
    DateTime? DesdeUtc,
    DateTime UltimoLatidoUtc,
    bool RegistraJornada = true);

/// <summary>
/// Quién tiene la aplicación abierta AHORA MISMO, sin que eso quede escrito en ninguna parte.
///
/// <para><b>Por qué hace falta.</b> El tablero deduce «conectado» de que exista una jornada abierta.
/// Las cuentas de Operaciones dejaron de abrir jornada (ver <see cref="PresenceService.EntrarAsync"/>),
/// así que sin este contrato saldrían «Desconectado» para siempre —incluso mientras están
/// desplegando—, que es justo lo contrario de lo que se pidió. Aquí se pregunta por los SOCKETS
/// vivos, que es el único sitio donde ese hecho existe cuando no se persiste nada.</para>
///
/// <para><b>Por qué es una interfaz y no una clase.</b> Quien sabe de sockets es la capa web, y esta
/// capa no la conoce ni debe conocerla. Es el mismo arreglo que ya usan <c>IRequestOrigin</c> y
/// <c>IAvisosDeDespliegue</c>: el servicio declara QUÉ necesita, la capa web dice CÓMO se sabe. La
/// implementación natural es <c>RegistroDeConexiones.Cuantas(userId) &gt; 0</c>.</para>
///
/// <para><b>Límite conocido, y hay que dejarlo escrito.</b> Ese registro vive en la memoria del
/// proceso: con más de una instancia detrás de un balanceador, cada una solo ve sus propios sockets y
/// un operativo conectado contra otra instancia saldría «Desconectado». Es la misma salvedad que ya
/// documenta <c>RegistroDeConexiones</c>, y hoy la API corre en una sola instancia. Si algún día deja
/// de correr en una sola, esto es lo primero que hay que revisar.</para>
/// </summary>
public interface IConexionesEnVivo
{
    bool EstaConectado(int userId);
}

/// <summary>
/// Una jornada propia, para la pantalla «Mi jornada». Es una proyección y no la entidad: el
/// ESTADO (comiendo, descanso…) no sale de la capa de servicio, porque no se historiza a
/// propósito — un registro minutado de las pausas de alguien es vigilancia, no asistencia.
/// </summary>
/// <param name="Cierre">Null mientras sigue abierta; SinLatido si se cayó.</param>
public record MiJornada(
    DateTime InicioUtc,
    DateTime? FinUtc,
    TimeSpan Duracion,
    PresenceEnd? Cierre,
    string? Equipo);

/// <summary>
/// Quién tiene la aplicación abierta ahora y desde cuándo, más el registro de jornadas.
///
/// Se sostiene en un LATIDO, no en el par inicio/cierre de sesión: cerrar con la X deja la
/// aplicación viva en la bandeja (y eso ES estar conectado), pero un cuelgue o un apagón no avisan
/// de nada. Sin latido, esa persona se quedaría marcada como conectada para siempre y el tablero
/// mentiría. Con latido, quien deja de dar señales se cierra solo y su jornada se cierra con la
/// hora del ÚLTIMO latido, no con la de ahora — que sería regalarle horas que nadie trabajó.
///
/// El estado (comiendo, en el baño…) se guarda en la jornada abierta y se sobrescribe: no queda
/// histórico de cuánto tiempo estuvo alguien en cada uno. Es deliberado — un registro minutado de
/// las pausas de una persona es vigilancia, no asistencia.
///
/// La única adaptación del port es de dónde sale el «equipo» de la jornada: en el escritorio era
/// <c>MÁQUINA\usuario</c>, que aquí sería siempre el nombre del servidor y no distinguiría nada, así
/// que se inyecta <see cref="IRequestOrigin"/> igual que en la bitácora.
///
/// <para><b>OPERACIONES NO REGISTRA JORNADA.</b> Es la decisión nueva y atraviesa todo el archivo:
/// no se le abren filas (<see cref="EntrarAsync"/>), no puede cambiar su estado
/// (<see cref="CambiarEstadoAsync"/>) y en el tablero su presencia sale de los sockets vivos
/// (<see cref="IConexionesEnVivo"/>) y no de una fila. Lo que ya tiene registrado NO se toca: se
/// conserva, se consulta y se cierra por las vías de siempre.</para>
/// </summary>
/// <param name="enVivo">
/// OPCIONAL a propósito: hay media docena de sitios que construyen este servicio a mano —las pruebas,
/// sobre todo— y un parámetro obligatorio los obligaría a todos a inventarse un doble para algo que
/// no ejercitan. Es el mismo recurso que ya usa <c>AnnouncementService</c>. Cuando falta, un operativo
/// sale «Desconectado», que es el lado seguro: <b>nunca se le pinta «Disponible» por no saber</b> —eso
/// sería mentir, y un tablero que miente deja de servir para lo que existe.
/// </param>
public class PresenceService(AppDbContext db, ICurrentUser currentUser, IRequestOrigin origin,
    IConexionesEnVivo? enVivo = null)
{
    /// <summary>Cada cuánto late la aplicación.</summary>
    public static readonly TimeSpan IntervaloLatido = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Sin latido en este tiempo, se da por desconectado. Es varias veces el intervalo a propósito:
    /// una pantalla bloqueada, un equipo que suspende un momento o una red que parpadea no deben
    /// marcar a nadie como ausente.
    /// </summary>
    public static readonly TimeSpan ToleranciaSinLatido = TimeSpan.FromMinutes(10);

    // ── Ciclo de la jornada ──────────────────────────────────────────────────────

    /// <summary>
    /// Abre la jornada de quien acaba de iniciar sesión. Si ya había una abierta de este mismo
    /// usuario (por ejemplo porque su equipo anterior se colgó), se cierra antes con la hora de su
    /// último latido: dos jornadas abiertas a la vez harían que el tablero contara doble.
    /// </summary>
    /// <summary>
    /// Cuánto puede haber estado cerrada una jornada para que volver a conectarse la REANUDE en vez
    /// de abrir otra.
    ///
    /// <para>Dos minutos cubren lo que de verdad ocurre: recargar con F5, que el portátil se
    /// suspenda un momento, que el wifi parpadee. Más allá de eso ya es alguien que se fue y volvió,
    /// y ahí dos tramos separados describen mejor el día que un único bloque que se comió el hueco.</para>
    /// </summary>
    private static readonly TimeSpan VentanaDeReanudacion = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Empieza —o RETOMA— la jornada de quien acaba de conectarse.
    ///
    /// <para><b>Antes abría siempre una fila nueva, y eso partía el día en pedazos.</b> En el
    /// escritorio había una instancia por sesión de Windows y la jornada era una sola de la mañana a
    /// la noche; en un navegador, cada F5 y cada pestaña es una conexión más, así que el registro
    /// acababa con ocho o diez jornadas de minutos donde hubo una de ocho horas. De paso se perdía
    /// el estado —cada conexión lo devolvía a «Disponible»— y con él la nota de «vuelvo a las 15:30».</para>
    ///
    /// <para>Ahora hay cuatro casos, en este orden:</para>
    /// <list type="number">
    ///   <item><b>Caso cero: la cuenta no registra jornada.</b> Operaciones no marca asistencia —su
    ///   alcance son los despliegues— así que no se le abre ninguna fila y esto termina aquí. Se
    ///   devuelve <c>null</c> y NO se lanza: el contrato ya admite null, quien llama ignora el
    ///   resultado, y lanzar convertiría cada conexión normal de un operativo en una advertencia en el
    ///   registro (AppHub apunta un aviso cuando esto falla) por algo que no es un fallo.</item>
    ///   <item>Ya hay una jornada ABIERTA: es otra pestaña del mismo día. Se reutiliza tal cual,
    ///   conservando estado y nota.</item>
    ///   <item>La última se cerró hace muy poco: fue un F5 o un parpadeo de red. Se REANUDA
    ///   —se le quita la marca de cierre— para no dejar dos tramos por un hueco de segundos.</item>
    ///   <item>No hay nada reciente: empieza una de verdad, en «Disponible».</item>
    /// </list>
    ///
    /// <para>Que el estado sobreviva a recargar es consecuencia, no un arreglo aparte: si la fila es
    /// la misma, lo que había en ella sigue ahí.</para>
    ///
    /// <para><b>Este método es EL punto de estrangulamiento, y por eso la guarda va aquí y no en el
    /// hub.</b> Sus tres llamadores —la conexión del hub, <see cref="LatirAsync"/> y
    /// <see cref="CambiarEstadoAsync"/>— pasan todos por esta línea. Puesta en el hub, el latido
    /// abriría por su cuenta la fila que la conexión no abrió, y el defecto no se vería hasta mirar el
    /// registro de jornadas de un mes después.</para>
    ///
    /// <para><b>Lo ya registrado se conserva.</b> La guarda es sobre la CREACIÓN, no sobre las filas:
    /// las jornadas que un operativo tenga de antes siguen ahí, el líder las sigue viendo en el
    /// registro y en la asistencia de días viejos, y la que estuviera ABIERTA el día que esto se
    /// despliegue se cierra sola por las vías de siempre —<see cref="SalirAsync"/> al desconectar, o
    /// <see cref="CerrarCaidasAsync"/> dentro de la tolerancia—. Ninguna de esas dos lleva esta guarda,
    /// y no debe llevarla: con ella, esa fila se quedaría abierta para siempre.</para>
    /// </summary>
    public async Task<WorkPresence?> EntrarAsync(string? origen = null, CancellationToken ct = default)
    {
        // (0) Comprobación POSITIVA por rol, no «ni admin ni desarrollador»: escrita por descarte, un
        //     rol nuevo caería aquí en silencio y dejaría de registrar jornada sin que nadie lo pidiera.
        if (currentUser.IsOperaciones) return null;

        if (currentUser.UserId is not int userId) return null;

        var ahora = DateTime.UtcNow;

        // (1) Una abierta Y CON LATIDO RECIENTE: es otra pestaña. Solo se refresca el latido; el
        //     estado y la nota no se tocan.
        //
        //     Lo de «con latido reciente» no es un detalle: sin esa condición, una jornada que se
        //     quedó colgada ayer —el proceso murió, nadie la selló— se reutilizaría hoy y quedaría
        //     una sola de dieciocho horas. Se usa la MISMA tolerancia que el barrido por latido para
        //     que las dos rutas coincidan en qué consideran «viva»; si no, una podría reutilizar lo
        //     que la otra acaba de dar por muerto.
        var vivaDesde = ahora - ToleranciaSinLatido;
        var abierta = await db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc == null && p.LastSeenUtc >= vivaDesde)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (abierta != null)
        {
            abierta.LastSeenUtc = ahora;
            await db.SaveChangesAsync(ct);
            return abierta;
        }

        // (2) Una recién cerrada: fue una recarga, no una salida. Se reabre la MISMA fila.
        var desde = ahora - VentanaDeReanudacion;
        var reciente = await db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc != null && p.EndedAtUtc >= desde)
            .OrderByDescending(p => p.EndedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (reciente != null)
        {
            reciente.EndedAtUtc = null;
            reciente.EndReason  = null;
            reciente.LastSeenUtc = ahora;
            await db.SaveChangesAsync(ct);
            return reciente;
        }

        // (3) Una nueva. Antes de abrirla se sellan las que quedaran colgando de un cierre sucio
        // viejo —fuera de la ventana de reanudación—, con la hora de su último latido.
        await CerrarAbiertasDeAsync(userId, PresenceEnd.SinLatido, ct);

        var jornada = new WorkPresence
        {
            UserId       = userId,
            DeveloperId  = currentUser.DeveloperId,
            DisplayName  = currentUser.FullName ?? currentUser.Username ?? $"Usuario #{userId}",
            StartedAtUtc = ahora,
            LastSeenUtc  = ahora,
            State        = PresenceState.Disponible,
            Origin       = origen ?? Equipo()
        };
        db.WorkPresences.Add(jornada);
        await db.SaveChangesAsync(ct);
        return jornada;
    }

    /// <summary>
    /// El latido. Refresca la marca de vida de la jornada abierta y, de paso, cierra las de quienes
    /// dejaron de dar señales — así el barrido no necesita un servicio aparte: lo hace cualquier
    /// aplicación que siga viva.
    ///
    /// El barrido va PRIMERO porque la jornada caída puede ser la de ESTE equipo: tras una
    /// suspensión o hibernación larga, el primer latido al despertar encontraría la jornada de
    /// anoche todavía abierta y, refrescándola, le regalaría a la persona todas las horas que la
    /// máquina pasó dormida. Con este orden la vieja se sella con su último latido real y el mismo
    /// latido abre la jornada nueva. El precio asumido: una pausa mayor a la tolerancia parte el
    /// día en dos filas — que es la verdad.
    /// </summary>
    public async Task LatirAsync(CancellationToken ct = default)
    {
        await CerrarCaidasAsync(ct);

        if (currentUser.UserId is int userId)
        {
            var mia = await AbiertaAsync(userId, ct);
            if (mia != null) { mia.LastSeenUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            else await EntrarAsync(ct: ct);   // se cerró (corte de red, suspensión): se abre otra
        }
    }

    /// <summary>Cierra la jornada al cerrar sesión o al salir de la aplicación.</summary>
    public async Task SalirAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not int userId) return;
        await CerrarAbiertasDeAsync(userId, PresenceEnd.CierreNormal, ct);
    }

    /// <summary>
    /// Cambia el estado propio. Nadie puede cambiar el de otra persona.
    ///
    /// <para>Operaciones no lo cambia: su estado es una constante que pone el servidor. Se contesta
    /// <c>(false, motivo)</c> y NO se lanza, por dos razones concretas. La primera es que este método
    /// ya usa ese par para todos sus rechazos y quien llama por el hub hace «si ok, difunde»: un false
    /// no difunde y ahí se acaba. La segunda es que el método del hub que llama aquí es el ÚNICO sin
    /// <c>try/catch</c>: una excepción le llegaría al navegador como error de hub sin explicación,
    /// mientras que un false se traga limpio.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> CambiarEstadoAsync(PresenceState estado, string? nota = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // Antes de tocar nada: si se dejara caer hacia abajo, la llamada a EntrarAsync de más adelante
        // devolvería null y el mensaje sería «No hay una sesión válida», que es mentira y manda a
        // quien lo lea a buscar un problema de acceso que no existe.
        if (currentUser.IsOperaciones)
            return (false, "Tu estado es siempre «Disponible»: no registras jornada.");

        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        var mia = await AbiertaAsync(userId, ct);
        if (mia == null)
        {
            mia = await EntrarAsync(ct: ct);
            if (mia == null) return (false, "No hay una sesión válida.");
        }

        nota = (nota ?? "").Trim();
        mia.State = estado;
        mia.StateNote = nota.Length == 0 ? null : (nota.Length > 200 ? nota[..200] : nota);
        mia.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, $"Estado: {Etiqueta(estado)}.");
    }

    /// <summary>El estado propio ahora mismo, para pintar el selector.</summary>
    public async Task<(PresenceState estado, string? nota)> MiEstadoAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is int userId && await AbiertaAsync(userId, ct) is { } mia)
            return (mia.State, mia.StateNote);
        return (PresenceState.Disponible, null);
    }

    // ── Tablero (administrador) ──────────────────────────────────────────────────

    /// <summary>
    /// Quién está conectado y en qué anda, más quién no lo está y desde cuándo. Incluye a TODAS las
    /// cuentas activas, no solo a las que han abierto la aplicación alguna vez: si alguien falta,
    /// esa ausencia es el dato.
    ///
    /// <para><b>Dos formas de saber si alguien está.</b> Para quien registra jornada, «conectado» es
    /// tener una fila abierta que siga latiendo — como siempre. Para quien NO la registra (Operaciones)
    /// no hay fila que mirar, así que se preguntan los sockets vivos a <see cref="IConexionesEnVivo"/>
    /// y, si está, su estado es «Disponible» por definición: desde su propio navegador está conectado,
    /// y no tiene ningún control con el que declarar otra cosa.</para>
    ///
    /// <para><b>Lo que NO se hace, y es la mitad del asunto:</b> a un operativo desconectado no se le
    /// pinta «Disponible». Sale «Desconectado» igual que cualquiera, porque un tablero que da por
    /// disponible a quien no está deja de servir para lo único que sirve. Si el contrato de sockets no
    /// está enchufado, todos los operativos salen desconectados: se pierde información, no se inventa.</para>
    /// </summary>
    public async Task<List<PresenciaDeUsuario>> TableroAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        await CerrarCaidasAsync(ct);

        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        // El rol viaja porque decide de dónde sale la presencia de cada fila. Sin él habría que
        // volver a consultar Users por cada persona, que es lo que esta consulta única evita.
        var usuarios = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Username, u.Role })
            .ToListAsync(ct);

        // Las jornadas abiertas son pocas (una por persona conectada): se traen enteras.
        var abiertas = (await db.WorkPresences.AsNoTracking()
            .Where(p => p.EndedAtUtc == null)
            .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.StartedAtUtc).First());

        // Para los desconectados basta con cuándo se les vio por última vez; se resuelve con un
        // agregado en la base en vez de arrastrar el historial completo a memoria.
        var ultimoVisto = (await db.WorkPresences.AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Ultimo = g.Max(p => p.LastSeenUtc) })
            .ToListAsync(ct))
            .ToDictionary(x => x.UserId, x => x.Ultimo);

        return usuarios
            .Select(u =>
            {
                // Positivo por rol, igual que en EntrarAsync: las dos decisiones tienen que decir lo
                // mismo sobre quién registra jornada o el tablero contradiría al registro.
                bool registraJornada = u.Role != UserRole.Operaciones;

                abiertas.TryGetValue(u.Id, out var abierta);
                bool conectado = registraJornada
                    ? abierta != null && abierta.LastSeenUtc >= corte
                    : enVivo?.EstaConectado(u.Id) == true;

                return new PresenciaDeUsuario(
                    u.Id,
                    string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName,
                    conectado,
                    // Sin jornada no hay estado guardado que leer, y estando conectado el único valor
                    // posible es «Disponible»: no hay control con el que decir otra cosa.
                    conectado ? (registraJornada ? abierta!.State : PresenceState.Disponible)
                              : PresenceState.Ausente,
                    conectado && registraJornada ? abierta!.StateNote : null,
                    // Tampoco hay «desde cuándo»: la hora de conexión no se persiste en ningún sitio, y
                    // poner la de ahora fingiría que acaba de llegar cada vez que alguien mira.
                    conectado && registraJornada ? abierta!.StartedAtUtc : null,
                    // El último visto sigue saliendo de las filas históricas: si las tuvo de antes,
                    // esa fecha es verdad; lo que la pantalla no debe hacer es leerla como «nunca ha
                    // entrado», y para eso va el indicador de al lado.
                    ultimoVisto.TryGetValue(u.Id, out var visto) ? visto : DateTime.MinValue,
                    registraJornada);
            })
            .OrderByDescending(x => x.Conectado)
            .ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Las jornadas de un día, para el registro de asistencia. Todas si no se indica usuario.</summary>
    public async Task<List<WorkPresence>> JornadasDelDiaAsync(DateTime diaLocal, int? userId = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        // El registro también se consulta cuando nadie más tuvo la aplicación abierta para barrer
        // (lunes por la mañana, tras un feriado): sin esto, una jornada caída se listaría como
        // «en curso». Es idempotente y casi siempre afecta 0 filas.
        await CerrarCaidasAsync(ct);

        var desde = diaLocal.Date.ToUniversalTime();
        var hasta = diaLocal.Date.AddDays(1).ToUniversalTime();

        var q = db.WorkPresences.AsNoTracking()
            .Where(p => p.StartedAtUtc >= desde && p.StartedAtUtc < hasta);
        if (userId is int uid) q = q.Where(p => p.UserId == uid);

        return await q.OrderBy(p => p.DisplayName).ThenBy(p => p.StartedAtUtc).ToListAsync(ct);
    }

    // ── Mi jornada (cada quien la suya) ──────────────────────────────────────────

    /// <summary>
    /// Las jornadas PROPIAS de un rango de días locales, para que cada quien vea su asistencia.
    ///
    /// SIN parámetro de usuario a propósito: no es un descuido de ergonomía, es lo que hace que
    /// este método no pueda convertirse nunca en un IDOR. Un id por parámetro —aunque hoy lo
    /// protegiera una guarda— basta que un llamador futuro lo pase mal para filtrar la asistencia
    /// de otra persona. Aquí solo hay un usuario posible: el de la sesión.
    ///
    /// Tampoco barre las caídas (CerrarCaidas cierra las de TODOS y convertiría una consulta en
    /// escritura de filas ajenas): una jornada caída aún sin barrer se muestra «en curso» con su
    /// duración calculada hasta el último latido, que es la verdad disponible.
    ///
    /// <para>La guarda es del líder y del desarrollador, y no «con sesión basta»: esto es la
    /// telemetría que alimenta «Mi jornada», y si el resto del módulo contesta 403 a un operativo y
    /// esta no, queda un hueco por el que sigue leyendo su registro. Las filas VIEJAS no se pierden:
    /// el líder las sigue viendo por el tablero y por la asistencia del día, que tienen su propia
    /// guarda de administrador.</para>
    /// </summary>
    public async Task<List<MiJornada>> MisJornadasAsync(DateTime desdeLocal, DateTime hastaLocal,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, "de la jornada propia");
        if (currentUser.UserId is not int userId) return [];

        // Por UserId y NO por DeveloperId: DeveloperId es una copia opcional tomada al entrar, y
        // las jornadas anteriores a ligar la ficha lo tienen en null — se perderían del total.
        var desde = desdeLocal.Date.ToUniversalTime();
        var hasta = hastaLocal.Date.AddDays(1).ToUniversalTime();   // el último día, completo

        return (await db.WorkPresences.AsNoTracking()
            .Where(p => p.UserId == userId && p.StartedAtUtc >= desde && p.StartedAtUtc < hasta)
            .OrderBy(p => p.StartedAtUtc)
            .ToListAsync(ct))
            // Se proyecta y no se devuelve la entidad: State y StateNote NO salen de aquí. El
            // estado es del momento y no se historiza (ver WorkPresence); devolver la fila entera
            // dejaría esa política sostenida solo por disciplina de quien la consuma.
            .Select(p => new MiJornada(p.StartedAtUtc, p.EndedAtUtc, p.Duracion, p.EndReason, p.Origin))
            .ToList();
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cierra las jornadas que llevan demasiado sin latir, sellándolas con la hora del ÚLTIMO
    /// latido. Cerrarlas con la hora actual le regalaría a alguien todas las horas que su equipo
    /// pasó apagado.
    ///
    /// Dos peticiones simultáneas pueden barrer a la vez, y no pasa nada: ambas sellan la fila con
    /// el MISMO valor (su propio LastSeenUtc), así que la segunda escritura no cambia el resultado.
    /// </summary>
    public async Task<int> CerrarCaidasAsync(CancellationToken ct = default)
    {
        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        var caidas = await db.WorkPresences
            .Where(p => p.EndedAtUtc == null && p.LastSeenUtc < corte)
            .ToListAsync(ct);
        if (caidas.Count == 0) return 0;

        foreach (var p in caidas)
        {
            p.EndedAtUtc = p.LastSeenUtc;
            p.EndReason  = PresenceEnd.SinLatido;
        }
        await db.SaveChangesAsync(ct);
        return caidas.Count;
    }

    private Task<WorkPresence?> AbiertaAsync(int userId, CancellationToken ct) =>
        db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc == null)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    private async Task CerrarAbiertasDeAsync(int userId, PresenceEnd motivo, CancellationToken ct)
    {
        var abiertas = await db.WorkPresences.Where(p => p.UserId == userId && p.EndedAtUtc == null).ToListAsync(ct);
        if (abiertas.Count == 0) return;

        var ahora = DateTime.UtcNow;
        foreach (var p in abiertas)
        {
            // En un cierre normal vale la hora de ahora; en uno caído, la del último latido.
            p.EndedAtUtc = motivo == PresenceEnd.CierreNormal ? ahora : p.LastSeenUtc;
            p.EndReason  = motivo;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// De dónde se abrió la jornada. En el escritorio era el nombre del equipo y del usuario de
    /// Windows; aquí todo corre en el mismo servidor, así que lo que distingue una jornada de otra
    /// del mismo día es el cliente: su IP y su navegador.
    /// </summary>
    private string Equipo() => origin.Describir();

    // ── Etiquetas ────────────────────────────────────────────────────────────────

    public static string Etiqueta(PresenceState e) => e switch
    {
        PresenceState.Disponible => "Disponible",
        PresenceState.Ocupado    => "Ocupado",
        PresenceState.EnReunion  => "En reunión",
        PresenceState.Comiendo   => "Comiendo",
        PresenceState.Descanso   => "En un descanso",
        _                        => "Ausente"
    };

    /// <summary>
    /// <b>DEUDA CONOCIDA: esto devuelve EMOJI y no debería.</b> Es el mismo defecto que ya se corrigió
    /// en el menú lateral y en el distintivo de la barra: los emojis los dibuja el SISTEMA OPERATIVO
    /// —se ven distintos en un Windows, en un Mac y en un móvil— y no heredan <c>currentColor</c>, así
    /// que en el tema oscuro siguen brillando con sus colores de siempre sobre el fondo nuevo.
    ///
    /// <para><b>Por qué no se arregla aquí y ya está.</b> Se miró antes de decidir: el único que llama
    /// a este método es <c>PersonasQueryService</c>, que mete la cadena en <c>PresenteDto.Icono</c>, y
    /// la pantalla de presencia la imprime TAL CUAL como texto (<c>&lt;Template&gt;@p.Icono&lt;/Template&gt;</c>).
    /// Con eso, devolver un <c>var(--…)</c> pintaría la palabra «var(--adminweb-presencia-disponible)»
    /// dentro de la celda, y devolver un nombre de icono de la fuente pintaría la palabra «circle».
    /// Las dos compilan y las dos se ven mal, que es exactamente la trampa de la que se viene.</para>
    ///
    /// <para><b>Cómo se arregla de verdad</b>, en un solo lote y en estos tres sitios a la vez: que el
    /// DTO lleve el ESTADO (ya lo lleva) en vez de una cadena decorativa, que la pantalla pinte el
    /// punto de color de <c>Componentes/BotonDeEstado.razor</c> —que ya resolvió este problema exacto
    /// para este mismo enumerado— y que este método desaparezca. Mientras tanto se queda como está: un
    /// emoji feo se lee; media cadena de CSS dentro de una celda, no.</para>
    ///
    /// <para><b>De la lista que había aquí ya solo quedan dos.</b> Las etiquetas de texto que
    /// acompañaban a estos iconos —el «Olvido (estimada)» y el «Corregida por el líder» de
    /// <c>AttendanceService</c>, el «Sin marcar (solo telemetría)» de <c>JornadaQueryService</c> y el
    /// «Sin marcar / Pide corrección» de <c>PersonasQueryService</c>— ya perdieron su símbolo: eran
    /// PALABRAS con un dibujo delante, así que quitarlo no dejaba la celda sin nada. Los que siguen
    /// aquí son de otra especie y por eso resisten: son el dato ENTERO, y borrar el emoji dejaría la
    /// columna vacía.</para>
    ///
    /// <para>Quedan, pues, dos y van juntos: este método y el «⚪» que <c>PersonasQueryService</c>
    /// pone a mano a los desconectados. Y con ellos su «⚠ Sin señales», que es el único texto que
    /// conservó el triángulo a propósito: la leyenda de la pantalla de presencia lo CITA entre
    /// comillas, así que si cambia uno sin el otro la leyenda manda a buscar en la rejilla algo que ya
    /// no está escrito así.</para>
    /// </summary>
    public static string Icono(PresenceState e) => e switch
    {
        PresenceState.Disponible => "🟢",
        PresenceState.Ocupado    => "🔴",
        PresenceState.EnReunion  => "📅",
        PresenceState.Comiendo   => "🍽",
        PresenceState.Descanso   => "☕",
        _                        => "⚪"
    };

    /// <summary>«2 h 15 min», para la duración de una jornada.</summary>
    public static string Duracion(TimeSpan t) =>
        t.TotalMinutes < 1 ? "menos de 1 min"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes:00} min";
}
