using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Programados;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Cómo terminó el despliegue que lanzó una cita.</summary>
/// <param name="DespliegueId">El despliegue creado, para poder llegar a su registro completo.</param>
public record ResultadoDeDespliegue(int DespliegueId, JobStatus Estado, int ServidoresOk, int ServidoresFallidos);

/// <summary>
/// Lo mínimo que la agenda necesita del despliegue para poder dispararlo.
///
/// <para><b>Es una interfaz y no una llamada directa</b> porque el servicio de despliegue lo porta
/// otra persona en paralelo. Declarar aquí el contrato —dos operaciones, nada más— permite que la
/// agenda esté terminada y probada antes de que exista la implementación, y deja escrito qué se le
/// pide exactamente. La implementación se registra en <c>Program.cs</c>; mientras no exista, la
/// agenda lo dice con todas sus letras en vez de fallar de forma incomprensible.</para>
/// </summary>
public interface IEjecutorDeDespliegues
{
    /// <summary>
    /// Lanza el despliegue de una versión a un perfil y espera a que termine.
    /// </summary>
    /// <param name="evidenciaDeChecklist">
    /// Lo que se guarda como evidencia en lugar del checklist que nadie llenó. Un despliegue
    /// programado corre sin ninguna persona delante y el expediente tiene que decirlo.
    /// </param>
    Task<ResultadoDeDespliegue> DesplegarAsync(
        int versionId, int perfilId, string evidenciaDeChecklist, CancellationToken ct = default);

    /// <summary>
    /// Congela una selección directa de servidores en un perfil interno para poder agendarla.
    ///
    /// <para>Sin esto, una cita a «estos tres servidores» tendría que guardar la lista aparte y
    /// resolverla al dispararse — y para entonces la selección pudo haber cambiado. Congelarla es lo
    /// que garantiza que se despliegue exactamente a lo que se eligió.</para>
    /// </summary>
    Task<int> CrearPerfilCongeladoAsync(
        IReadOnlyList<int> servidorIds, string etiqueta, CancellationToken ct = default);
}

/// <summary>
/// La agenda de despliegues: qué se va a desplegar, a qué hora, y quién se lleva la ejecución.
///
/// <para><b>Lo que cambia de fondo respecto al escritorio.</b> Allí una cita solo se disparaba si
/// alguien tenía la aplicación abierta a esa hora; si no, se marcaba como perdida y no ocurría nunca.
/// Aquí la dispara un trabajo de fondo del servidor, así que ocurre siempre. La tolerancia sigue
/// existiendo y sigue significando lo mismo —pasado ese margen no se dispara— porque el motivo no era
/// técnico: desplegar a deshora, cuando ya nadie lo espera, es peor que no desplegar.</para>
///
/// <para><b>La toma atómica se CONSERVA.</b> Hoy hay una sola instancia de la API y podría parecer
/// innecesaria, pero es la única garantía de que un programado se ejecute una vez y no dos: el día
/// que haya dos instancias —o que alguien reinicie mientras otra sigue viva— sin ella las dos leerían
/// «Programado», las dos escribirían y el despliegue saldría por duplicado. La decide la BASE con un
/// UPDATE condicional, no el código.</para>
/// </summary>
public class ProgramadosService(
    AppDbContext db,
    ICurrentUser currentUser,
    AuditService audit,
    IEjecutorDeDespliegues? ejecutor = null)
{
    /// <summary>
    /// Quién se llevó la ejecución. En el escritorio era «MÁQUINA\usuario» porque cada quien corría
    /// su propio ejecutable; aquí identifica al PROCESO del servidor, que es lo que compite por una
    /// cita cuando hay más de una instancia.
    /// </summary>
    private static string EsteEjecutor => $"{Environment.MachineName}#{Environment.ProcessId}";

    /// <summary>¿Hay quién dispare los despliegues? Lo consulta el trabajo de fondo antes de tomar nada.</summary>
    public bool HayEjecutor => ejecutor != null;

    // ── Agenda ──────────────────────────────────────────────────────────────────

    /// <summary>Todo lo que necesita la pantalla de programados, en una sola respuesta.</summary>
    /// <param name="trabajoDeFondoActivo">
    /// Lo sabe la API, no este servicio: es un interruptor del alojamiento. Viaja hasta la pantalla
    /// porque agendar algo que nadie va a ejecutar es peor que no poder agendarlo.
    /// </param>
    public async Task<PantallaDeProgramadosDto> PantallaAsync(
        bool soloPendientes, bool trabajoDeFondoActivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(currentUser);

        var citas = await ListarAsync(soloPendientes, ct);

        var versiones = await db.AppReleases.AsNoTracking()
            .Include(r => r.AppSystem)
            .Where(r => r.AppSystem.IsActive)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(200)
            .Select(r => new VersionProgramableDto(r.Id, r.AppSystem.Name, r.Version, r.CreatedAt))
            .ToListAsync(ct);

        // Los perfiles internos (IsAdHoc) quedan fuera: son la selección congelada de una cita
        // anterior, no un perfil que nadie eligiera a mano. Enseñarlos llenaría la lista de entradas
        // que solo existen por dentro.
        var perfiles = await db.DeploymentProfiles.AsNoTracking()
            .Where(p => !p.IsAdHoc)
            .OrderBy(p => p.Name)
            .Select(p => new PerfilProgramableDto(
                p.Id, p.Name, p.Description, p.AllowedForOperaciones, p.ProfileTargets.Count))
            .ToListAsync(ct);

        var servidores = await db.DeploymentTargets.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Nombre)
            .Select(t => new ServidorProgramableDto(t.Id, t.Nombre, t.Host, t.IsActive))
            .ToListAsync(ct);

        return new PantallaDeProgramadosDto(citas, versiones, perfiles, servidores, trabajoDeFondoActivo);
    }

    /// <summary>Las citas de la agenda, de la más próxima a la más lejana.</summary>
    public async Task<IReadOnlyList<DespliegueProgramadoDto>> ListarAsync(
        bool soloPendientes = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(currentUser);

        var consulta = db.ScheduledDeployments.AsNoTracking()
            .Include(s => s.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(s => s.Profile)
            .AsQueryable();

        if (soloPendientes)
            consulta = consulta.Where(s => s.Status == ScheduledDeploymentStatus.Programado);

        var citas = await consulta.OrderBy(s => s.ScheduledAtUtc).ToListAsync(ct);
        if (citas.Count == 0) return [];

        var quienes = await NombresDeUsuarioAsync(
            [.. citas.Where(c => c.CreatedByUserId != null).Select(c => c.CreatedByUserId!.Value).Distinct()], ct);

        return [.. citas.Select(c => new DespliegueProgramadoDto(
            c.Id,
            c.ScheduledAtUtc,
            c.AppRelease?.AppSystem?.Name ?? "—",
            c.AppRelease?.Version ?? "—",
            c.Profile?.Name ?? "—",
            c.Status,
            EtiquetaDeEstado(c.Status),
            c.ToleranciaMinutos,
            c.ClaimedBy,
            c.ClaimedAtUtc,
            c.ResultMessage,
            c.Notes,
            c.DeploymentJobId,
            c.CreatedByUserId is int id && quienes.TryGetValue(id, out var n) ? n : "(desconocido)",
            c.CreatedAt))];
    }

    /// <summary>
    /// Agenda un despliegue, por perfil o por selección directa de servidores.
    ///
    /// <para>Las dos formas comparten validación porque son la misma decisión con distinto destino.
    /// La regla de Operaciones se conserva intacta del escritorio: solo perfiles habilitados para
    /// ella, y el intento contrario queda en la bitácora — quien agenda a las cuatro de la tarde un
    /// despliegue a producción para las tres de la mañana tiene que ser rastreable.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProgramarAsync(
        ProgramarDespliegueRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(currentUser);

        var ahora = DateTime.UtcNow;
        if (peticion.CuandoUtc <= ahora.AddMinutes(1))
            return (false, "La hora programada debe ser al menos un minuto en el futuro.");
        if (peticion.ToleranciaMinutos is < 5 or > 1440)
            return (false, "La tolerancia debe estar entre 5 minutos y 24 horas.");

        if (!await db.AppReleases.AsNoTracking().AnyAsync(r => r.Id == peticion.VersionId, ct))
            return (false, "La versión no existe.");

        var servidorIds = peticion.ServidorIds ?? [];
        int perfilId;
        string detalleParaBitacora;

        if (peticion.PerfilId is int elegido)
        {
            var perfil = await db.DeploymentProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == elegido, ct);
            if (perfil == null) return (false, "El perfil no existe.");

            // Misma regla que al desplegar en vivo: Operaciones solo perfiles habilitados.
            if (!currentUser.IsAdmin && !perfil.AllowedForOperaciones)
            {
                await audit.RecordDeniedAsync(AuditAction.Deploy, "DeploymentProfile", elegido.ToString(),
                    $"Intento de programar el perfil «{perfil.Name}», no habilitado para Operaciones.", ct: ct);
                throw new AuthorizationException(
                    $"El perfil «{perfil.Name}» no está habilitado para Operaciones.");
            }

            perfilId = perfil.Id;
            detalleParaBitacora = $"perfil «{perfil.Name}»";
        }
        else
        {
            if (servidorIds.Count == 0)
                return (false, "Elige un perfil o al menos un servidor.");
            if (ejecutor == null)
                return (false, "Todavía no se puede agendar a servidores sueltos: el servicio de despliegue " +
                               "no está disponible en este servidor.");

            var nombres = await db.DeploymentTargets.AsNoTracking()
                .Where(t => servidorIds.Contains(t.Id))
                .OrderBy(t => t.Nombre)
                .Select(t => t.Nombre)
                .ToListAsync(ct);
            if (nombres.Count == 0) return (false, "Los servidores seleccionados ya no existen.");

            // Etiqueta con los nombres, para reconocer la cita en la lista sin abrir nada.
            var etiqueta = "⏱ " + string.Join(", ", nombres.Take(3)) +
                           (nombres.Count > 3 ? $" (+{nombres.Count - 3})" : "");
            if (etiqueta.Length > 190) etiqueta = etiqueta[..190];

            try
            {
                perfilId = await ejecutor.CrearPerfilCongeladoAsync(servidorIds, etiqueta, ct);
            }
            catch (InvalidOperationException ex)
            {
                return (false, ex.Message);
            }

            detalleParaBitacora = $"{nombres.Count} servidor(es) directos";
        }

        var cita = new ScheduledDeployment
        {
            AppReleaseId = peticion.VersionId,
            DeploymentProfileId = perfilId,
            ScheduledAtUtc = peticion.CuandoUtc,
            ToleranciaMinutos = peticion.ToleranciaMinutos,
            Status = ScheduledDeploymentStatus.Programado,
            Notes = string.IsNullOrWhiteSpace(peticion.Notas) ? null : peticion.Notas.Trim(),
            CreatedByUserId = currentUser.UserId,
            CreatedAt = ahora
        };
        db.ScheduledDeployments.Add(cita);
        await db.SaveChangesAsync(ct);

        await audit.RecordDetailedAsync(AuditAction.Deploy, "ScheduledDeployment", cita.Id.ToString(),
            $"Despliegue programado para {peticion.CuandoUtc:dd/MM/yyyy HH:mm} UTC ({detalleParaBitacora})",
            AuditOutcome.Exito,
            newValues: new { cita.AppReleaseId, cita.DeploymentProfileId, cita.ScheduledAtUtc },
            ct: ct);

        return (true, $"Despliegue programado para {peticion.CuandoUtc.ToLocalTime():dd/MM/yyyy HH:mm}.");
    }

    /// <summary>Cancela una cita que todavía no se ha disparado.</summary>
    public async Task<(bool ok, string mensaje)> CancelarAsync(int citaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(currentUser);

        var cita = await db.ScheduledDeployments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == citaId, ct);
        if (cita == null) return (false, "La programación ya no existe.");
        if (cita.Status != ScheduledDeploymentStatus.Programado)
            return (false, $"No se puede cancelar: está en estado {EtiquetaDeEstado(cita.Status)}.");

        // Condicional por el mismo motivo que la toma: entre leer y guardar cabe que el trabajo de
        // fondo se la lleve, y cancelar entonces dejaría un despliegue corriendo marcado como
        // cancelado — lo peor de los dos mundos.
        int canceladas = await db.ScheduledDeployments
            .Where(s => s.Id == citaId && s.Status == ScheduledDeploymentStatus.Programado)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ScheduledDeploymentStatus.Cancelado)
                .SetProperty(c => c.ResultMessage, (string?)"Cancelada antes de su hora."), ct);

        if (canceladas == 0)
            return (false, "No se pudo cancelar: la programación acaba de empezar a ejecutarse.");

        await audit.RecordDetailedAsync(AuditAction.Deploy, "ScheduledDeployment", citaId.ToString(),
            "Programación cancelada", AuditOutcome.Exito, ct: ct);

        return (true, "Programación cancelada.");
    }

    // ── Ejecución ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Marca como perdidas las citas cuya tolerancia ya expiró. Se hace ANTES de tomar ninguna: una
    /// cita de anoche no debe dispararse porque el servidor se reinició esta mañana.
    /// </summary>
    /// <returns>Cuántas se marcaron.</returns>
    public async Task<int> MarcarPerdidasAsync(DateTime? ahoraUtc = null, CancellationToken ct = default)
    {
        var ahora = ahoraUtc ?? DateTime.UtcNow;

        // Las pendientes son pocas por naturaleza, así que el margen se evalúa en memoria: escrito
        // como consulta obligaría a que el proveedor supiera sumar minutos de una COLUMNA, y eso
        // cambia de un motor a otro.
        var pendientes = await db.ScheduledDeployments.AsNoTracking()
            .Where(s => s.Status == ScheduledDeploymentStatus.Programado)
            .ToListAsync(ct);

        var perdidas = pendientes.Where(s => s.SePerdio(ahora)).ToList();
        if (perdidas.Count == 0) return 0;

        int marcadas = 0;
        foreach (var cita in perdidas)
        {
            string? motivo = $"No se ejecutó: pasó su margen de {cita.ToleranciaMinutos} minutos de tolerancia.";

            // Condicional: si el trabajo de fondo de otra instancia la tomó entre la lectura y esto,
            // no se le pisa el estado.
            int filas = await db.ScheduledDeployments
                .Where(s => s.Id == cita.Id && s.Status == ScheduledDeploymentStatus.Programado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ScheduledDeploymentStatus.Perdido)
                    .SetProperty(c => c.ResultMessage, motivo), ct);

            if (filas == 0) continue;

            marcadas++;
            await audit.RecordDetailedAsync(AuditAction.Deploy, "ScheduledDeployment", cita.Id.ToString(),
                motivo, AuditOutcome.Fallo, ct: ct);
        }

        return marcadas;
    }

    /// <summary>
    /// Intenta TOMAR la siguiente cita cuya hora ya llegó.
    ///
    /// <para>La toma la decide la base con un UPDATE condicional sobre el estado: quien logra pasar
    /// la fila de <see cref="ScheduledDeploymentStatus.Programado"/> a
    /// <see cref="ScheduledDeploymentStatus.EnEjecucion"/> se la lleva, y a los demás les afecta cero
    /// filas. Releer y después escribir NO sirve: entre la lectura y el guardado caben varios viajes
    /// a la base, y dos ejecutores que miraron a la vez verían los dos «Programado».</para>
    /// </summary>
    /// <returns>El identificador de la cita tomada, o null si no había ninguna o se la llevó otro.</returns>
    public async Task<int?> TomarSiguienteAsync(DateTime? ahoraUtc = null, CancellationToken ct = default)
    {
        var ahora = ahoraUtc ?? DateTime.UtcNow;

        var candidatas = await db.ScheduledDeployments.AsNoTracking()
            .Where(s => s.Status == ScheduledDeploymentStatus.Programado && s.ScheduledAtUtc <= ahora)
            .OrderBy(s => s.ScheduledAtUtc)
            .ToListAsync(ct);

        string quienToma = EsteEjecutor;
        DateTime? tomadaA = ahora;

        foreach (var candidata in candidatas)
        {
            // Una que ya expiró no se toma: la marca como perdida el paso anterior.
            if (candidata.SePerdio(ahora)) continue;

            int ganadas = await db.ScheduledDeployments
                .Where(s => s.Id == candidata.Id && s.Status == ScheduledDeploymentStatus.Programado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ScheduledDeploymentStatus.EnEjecucion)
                    .SetProperty(c => c.ClaimedBy, quienToma)
                    .SetProperty(c => c.ClaimedAtUtc, tomadaA), ct);

            // Cero filas = otro ejecutor se adelantó. Se prueba con la siguiente en vez de darse por
            // vencido: en una vuelta puede haber varias citas vencidas y perderlas todas por una
            // carrera dejaría trabajo sin hacer hasta la vuelta siguiente.
            if (ganadas > 0) return candidata.Id;
        }

        return null;
    }

    /// <summary>
    /// Ejecuta una cita YA TOMADA. Solo debe llamarse después de que
    /// <see cref="TomarSiguienteAsync"/> la haya ganado.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EjecutarAsync(int citaId, CancellationToken ct = default)
    {
        var cita = await db.ScheduledDeployments.FirstOrDefaultAsync(s => s.Id == citaId, ct);
        if (cita == null) return (false, "La programación ya no existe.");

        if (ejecutor == null)
        {
            // La cita ya está tomada, así que no se puede dejar «en ejecución» para siempre: se marca
            // fallida con el motivo real, que es de configuración del servidor y no del despliegue.
            cita.Status = ScheduledDeploymentStatus.Fallido;
            cita.ResultMessage = "No hay servicio de despliegue disponible en este servidor.";
            await db.SaveChangesAsync(CancellationToken.None);
            return (false, cita.ResultMessage);
        }

        try
        {
            var resultado = await ejecutor.DesplegarAsync(
                cita.AppReleaseId, cita.DeploymentProfileId, await EvidenciaAutomaticaAsync(cita, ct), ct);

            cita.DeploymentJobId = resultado.DespliegueId;
            cita.Status = resultado.Estado == JobStatus.Completado
                ? ScheduledDeploymentStatus.Completado
                : ScheduledDeploymentStatus.Fallido;
            cita.ResultMessage = $"Despliegue #{resultado.DespliegueId}: {resultado.Estado} — " +
                                 $"{resultado.ServidoresOk} OK, {resultado.ServidoresFallidos} fallidos.";

            // Sin token: si el despliegue terminó y el servidor se está apagando, el resultado tiene
            // que quedar guardado igualmente. Perderlo dejaría la cita «en ejecución» para siempre.
            await db.SaveChangesAsync(CancellationToken.None);

            await audit.RecordDetailedAsync(AuditAction.Deploy, "ScheduledDeployment", citaId.ToString(),
                cita.ResultMessage,
                cita.Status == ScheduledDeploymentStatus.Completado ? AuditOutcome.Exito : AuditOutcome.Fallo,
                ct: CancellationToken.None);

            return (cita.Status == ScheduledDeploymentStatus.Completado, cita.ResultMessage);
        }
        catch (Exception ex)
        {
            cita.Status = ScheduledDeploymentStatus.Fallido;
            cita.ResultMessage = $"Error: {ex.Message}";
            await db.SaveChangesAsync(CancellationToken.None);

            await audit.RecordDetailedAsync(AuditAction.Deploy, "ScheduledDeployment", citaId.ToString(),
                cita.ResultMessage, AuditOutcome.Fallo, ct: CancellationToken.None);

            return (false, cita.ResultMessage);
        }
    }

    /// <summary>
    /// La evidencia que sustituye al checklist en un despliegue programado.
    ///
    /// <para>Se conserva tal cual del escritorio y por su razón original: nadie estaba frente a la
    /// pantalla, así que no hay checklist humano. Sin esta constancia —quién lo agendó, para cuándo y
    /// con qué notas— el expediente lo confundiría con un despliegue anterior a que existiera el
    /// checklist y le atribuiría la decisión a quien simplemente tenía la aplicación abierta a esa
    /// hora. En la web sería todavía peor: se la atribuiría al servidor.</para>
    /// </summary>
    private async Task<string> EvidenciaAutomaticaAsync(ScheduledDeployment cita, CancellationToken ct)
    {
        var quien = cita.CreatedByUserId is int uid
            ? (await NombresDeUsuarioAsync([uid], ct)).GetValueOrDefault(uid, $"usuario #{uid}")
            : "(desconocido)";

        var lineas = new List<string>
        {
            "DESPLIEGUE PROGRAMADO (sin checklist: lo ejecutó el servidor, sin nadie delante)",
            $"Agendado por : {quien}",
            $"Agendado el  : {cita.CreatedAt:dd/MM/yyyy HH:mm} UTC",
            $"Programado a : {cita.ScheduledAtUtc:dd/MM/yyyy HH:mm} UTC",
            $"Ejecutado el : {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC"
        };

        if (!string.IsNullOrWhiteSpace(cita.Notes))
            lineas.Add($"{Environment.NewLine}Nota de la programación: {cita.Notes.Trim()}");

        return string.Join(Environment.NewLine, lineas);
    }

    private async Task<Dictionary<int, string>> NombresDeUsuarioAsync(List<int> usuarioIds, CancellationToken ct)
    {
        if (usuarioIds.Count == 0) return [];

        var filas = await db.Users.AsNoTracking()
            .Where(u => usuarioIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToListAsync(ct);

        return filas.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);
    }

    /// <summary>
    /// La etiqueta de cada estado. La escribe el servidor para que diga lo mismo en la pantalla, en un
    /// mensaje de rechazo y en la bitácora.
    /// </summary>
    public static string EtiquetaDeEstado(ScheduledDeploymentStatus estado) => estado switch
    {
        ScheduledDeploymentStatus.Programado => "🕓 Programado",
        ScheduledDeploymentStatus.EnEjecucion => "▶ En ejecución",
        ScheduledDeploymentStatus.Completado => "✅ Completado",
        ScheduledDeploymentStatus.Fallido => "❌ Fallido",
        ScheduledDeploymentStatus.Cancelado => "⚪ Cancelado",
        _ => "⚠ Perdido"
    };
}
