using System.Data;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La puerta del despliegue: quién puede desplegar qué, a dónde, con qué respaldo y con qué
/// checklist confirmado. Lo que pasa DESPUÉS —conectarse, respaldar, subir— es del
/// <see cref="EjecutorDeDespliegues"/>.
///
/// <para><b>Por qué está partido en dos.</b> En el escritorio todo esto era un solo método porque
/// todo ocurría dentro del mismo clic: se validaba y se desplegaba en la misma llamada, y la persona
/// se quedaba mirando. Aquí la validación tiene que contestar de inmediato —hay una petición HTTP
/// esperando— y el despliegue tiene que sobrevivirla. Este servicio decide y devuelve; el ejecutor
/// trabaja.</para>
///
/// <para><b>Las guardas se conservan y se refuerzan.</b> El escritorio ya había movido la
/// autorización del combo de la pantalla al servicio, después de que se descubriera que cualquier
/// otra ruta desplegaba lo que quisiera. Aquí eso es todavía más necesario: la API se puede llamar
/// sin pasar por el navegador. Y el checklist, que allí lo exigía el diálogo, se exige también
/// aquí.</para>
///
/// <para><b>Y una guarda que el escritorio no necesitaba: dos despliegues a la vez.</b> Allí
/// desplegaba una persona desde su máquina y solaparse era imposible; aquí despliega el servidor, y
/// puede haber varios servidores atendiendo. Por eso comprobar que el destino está libre y apuntar
/// el trabajo son UNA sola operación bajo un candado de la base —lo único que ven todas las
/// instancias— y por eso lo que demuestra que un despliegue sigue vivo vive también en la base. Ver
/// <see cref="CandadoDelRegistroDeDespliegues"/> y <see cref="SenalDeVidaDelDespliegue"/>.</para>
/// </summary>
public class DeploymentService(
    AppDbContext db,
    ICurrentUser quien,
    AuditService bitacora,
    EjecutorDeDespliegues ejecutor)
{
    /// <summary>
    /// ¿Puede este usuario desplegar ESTE perfil? Admin siempre; Operaciones solo los perfiles
    /// habilitados. Los perfiles internos <c>IsAdHoc</c> están exentos porque no son perfiles
    /// guardados: son la foto congelada de una selección directa que ya pasó por
    /// <see cref="AuthorizationGuard.RequireAdminOrOperaciones"/>.
    /// </summary>
    public static bool PuedeDesplegarPerfil(ICurrentUser usuario, DeploymentProfile perfil) =>
        usuario.IsAdmin || perfil.IsAdHoc || perfil.AllowedForOperaciones;

    /// <summary>
    /// Valida la orden, la deja registrada y la pone en marcha. Devuelve el identificador del
    /// trabajo, que es con el que la pantalla se suscribe a su avance.
    /// </summary>
    public async Task<(bool ok, string mensaje, int? jobId)> LanzarAsync(
        LanzarDespliegueRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var version = await db.AppReleases.AsNoTracking()
            .Include(r => r.AppSystem)
            .FirstOrDefaultAsync(r => r.Id == peticion.VersionId, ct);
        if (version == null) return (false, "Esa versión ya no existe. Actualiza la lista.", null);

        var (servidores, error) = await ResolverDestinoAsync(peticion, ct);
        if (error != null) return (false, error, null);

        // El checklist se exige AQUÍ además de en la pantalla. Allí lo pide un formulario que corre
        // en la máquina del usuario; esta es la barrera que no se puede saltar.
        //
        // Se comprueba ANTES de tomar el candado del registro, y no al revés: son validaciones puras
        // que no tocan la base, y hacerlas dentro alargaría sin motivo lo único que se serializa
        // entre todas las instancias.
        var faltantes = DeploymentChecklist.Faltantes(peticion.Marcados ?? []);
        if (faltantes.Count > 0)
            return (false, "Falta confirmar el checklist previo: " +
                           string.Join("; ", faltantes.Select(p => p.Texto)) + ".", null);

        if (!DeploymentChecklist.NotaSuficiente(peticion.Nota))
            return (false, $"Escribe por qué se despliega ahora (al menos {DeploymentChecklist.MinimoNota} " +
                           "caracteres). Es lo único que contesta «¿por qué este despliegue?» un mes después.", null);

        // null = respaldar todos. Es el valor seguro por omisión: quien no se pronuncia obtiene la
        // red de seguridad, no su ausencia.
        var respaldar = peticion.RespaldarServidorIds == null
            ? servidores.Select(s => s.Id).ToHashSet()
            : peticion.RespaldarServidorIds.ToHashSet();

        var nombres = servidores.Select(s => s.Nombre).ToList();
        var destino = $"{servidores.Count} servidor(es): {string.Join(", ", nombres)}";
        var respaldoTexto = respaldar.Count == servidores.Count ? "todos"
                          : respaldar.Count == 0 ? "ninguno"
                          : $"{respaldar.Count} de {servidores.Count}";

        var queSeDespliega = $"{version.AppSystem.Name} v{version.Version}";
        var evidencia = DeploymentChecklist.Evidencia(
            quien.FullName ?? quien.Username ?? "(sin nombre)",
            DateTime.Now, queSeDespliega, destino, respaldoTexto,
            peticion.Marcados ?? [], peticion.Nota);

        DeploymentJob job;

        // ── COMPROBAR Y REGISTRAR, DE UNA PIEZA ──────────────────────────────────
        //
        // Todo lo que va aquí dentro tiene que ocurrir sin que nadie se cuele en medio: comprobar que
        // los servidores están libres, congelar la selección y dejar el trabajo apuntado con su señal
        // de vida. Antes esto era una comprobación suelta seguida de varios await, y dos personas que
        // pulsaran el botón en el mismo segundo pasaban las dos — cada una veía los servidores libres
        // porque la otra todavía no había llegado a registrarse.
        //
        // Dura milisegundos y es a propósito: el candado protege el REGISTRO, no la faena. Lo que
        // tarda media hora —leer el paquete, respaldar, subir— pasa fuera, después de soltarlo.
        await using (var candado = await CandadoDelRegistroDeDespliegues.TomarAsync(db, ct))
        {
            // Sin candado NO se sigue. Registrar «a pelo» porque el candado no llegó sería
            // exactamente el agujero que se está tapando, y con la agravante de que ocurriría solo
            // bajo carga, que es cuando dos despliegues a la vez son más probables.
            if (candado is null)
                return (false, "El servidor está registrando otro despliegue en este preciso momento. " +
                               "Vuelve a intentarlo en unos segundos.", null);

            // Dos despliegues simultáneos a la misma carpeta remota dejan una mezcla de dos
            // versiones, y lo peor es que no se nota. En el escritorio no podía pasar —desplegaba una
            // persona en su máquina—; aquí despliega el servidor, dos personas pueden pulsar el botón
            // a la vez y, además, pueden estar atendidas por instancias distintas de la API.
            var ocupado = await PrimeroOcupadoAsync(servidores, ct);
            if (ocupado != null)
                return (false, $"«{ocupado.Nombre}» está recibiendo otro despliegue ahora mismo. " +
                               "Espera a que termine o quítalo de la selección.", null);

            var perfilId = peticion.ServidorIds is { Count: > 0 }
                ? await CongelarSeleccionAsync(servidores, ct)
                : peticion.PerfilId!.Value;

            job = new DeploymentJob
            {
                AppReleaseId = version.Id,
                DeploymentProfileId = perfilId,
                Status = JobStatus.EnCurso,
                StartedAt = DateTime.UtcNow,
                StartedById = quien.UserId,
                TargetsTotal = servidores.Count,
                Notes = evidencia,
                CreatedAt = DateTime.UtcNow
            };
            db.DeploymentJobs.Add(job);
            await db.SaveChangesAsync(ct);

            // La señal se planta AQUÍ y no cuando el trabajo empieza a correr, que es un instante
            // después: en ese instante cabe otro lanzamiento, y el trabajo recién apuntado todavía no
            // ocuparía su servidor a ojos de nadie. Después la renueva el ejecutor mientras dura.
            await SenalDeVidaDelDespliegue.RefrescarAsync(db, job.Id, DateTime.UtcNow, ct);
        }

        // Fuera del candado: poner el trabajo en marcha no es registrarlo, y lo que arranca aquí dura
        // media hora. La reserva ya está hecha y es la que ven las demás instancias.
        ejecutor.Lanzar(new OrdenDeDespliegue(
            job.Id, version.Id, version.AppSystem.Name, version.Version, destino,
            [.. servidores.Select(s => s.Id)], respaldar, IdentidadDelDespliegue.De(quien)));

        return (true,
            $"Despliegue de {queSeDespliega} en marcha hacia {servidores.Count} servidor(es). " +
            "Corre en el servidor: puedes cerrar esta pestaña sin interrumpirlo.", job.Id);
    }

    /// <summary>Cancela un despliegue en curso. Es deliberado: cerrar la pestaña no cancela.</summary>
    public (bool ok, string mensaje) Cancelar(int jobId)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);
        return ejecutor.Cancelar(jobId, quien);
    }

    /// <summary>
    /// El primero de estos servidores que ya esté recibiendo un despliegue, o null si están todos
    /// libres. Se pregunta dos veces y por dos vías distintas, y las dos hacen falta:
    ///
    /// <para><b>La memoria de este proceso</b> es gratis y responde al instante. Además cubre un
    /// hueco que la base no cubre: los despliegues lanzados por una versión ANTERIOR de la
    /// aplicación —los segundos de un despliegue escalonado— no dejan señal de vida, y esta instancia
    /// solo los conoce si son suyos.</para>
    ///
    /// <para><b>La base</b> es la que de verdad cierra el agujero: es lo único que ven todas las
    /// instancias. Sin esto, cada una autorizaba despliegues mirando únicamente su propia memoria y
    /// dos servidores de la API podían estar subiendo versiones distintas a la misma carpeta.</para>
    /// </summary>
    private async Task<DeploymentTarget?> PrimeroOcupadoAsync(
        List<DeploymentTarget> servidores, CancellationToken ct)
    {
        var aqui = servidores.FirstOrDefault(s => ejecutor.ServidorOcupado(s.Id));
        if (aqui != null) return aqui;

        var ocupados = await SenalDeVidaDelDespliegue.ServidoresOcupadosAsync(
            db, [.. servidores.Select(s => s.Id)], DateTime.UtcNow, ct);

        return ocupados.Count == 0 ? null : servidores.FirstOrDefault(s => ocupados.Contains(s.Id));
    }

    // ── Destino ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A qué servidores va, ya sea por selección directa o por perfil guardado. Devuelve el motivo
    /// exacto cuando no se puede, para que la pantalla lo enseñe tal cual.
    /// </summary>
    private async Task<(List<DeploymentTarget> servidores, string? error)> ResolverDestinoAsync(
        LanzarDespliegueRequest peticion, CancellationToken ct)
    {
        // Selección DIRECTA de servidores (estilo Blobup): es la vía normal para Admin y Operaciones.
        if (peticion.ServidorIds is { Count: > 0 })
        {
            var elegidos = await db.DeploymentTargets
                .Where(t => peticion.ServidorIds.Contains(t.Id) && t.IsActive)
                .ToListAsync(ct);

            // Un servidor desactivado ya no debe recibir despliegues; antes se le desplegaba igual.
            if (elegidos.Count == 0)
                return ([], "Ninguno de los servidores elegidos sigue activo. Actualiza la lista.");

            return ([.. elegidos.OrderBy(t => t.Nombre)], null);
        }

        if (peticion.PerfilId is not int perfilId)
            return ([], "Elige al menos un servidor destino.");

        var perfil = await db.DeploymentProfiles
            .Include(p => p.ProfileTargets).ThenInclude(pt => pt.Target)
            .FirstOrDefaultAsync(p => p.Id == perfilId, ct);
        if (perfil == null) return ([], "Ese perfil ya no existe. Actualiza la lista.");

        // La regla se revalida contra el perfil REAL, no contra lo que la pantalla haya mostrado.
        if (!PuedeDesplegarPerfil(quien, perfil))
        {
            await bitacora.RecordDeniedAsync(AuditAction.Deploy, "DeploymentProfile", perfil.Id.ToString(),
                $"Intento de desplegar el perfil «{perfil.Name}», no habilitado para Operaciones.", ct: ct);
            throw new AuthorizationException(
                $"El perfil «{perfil.Name}» no está habilitado para el rol Operaciones.");
        }

        var activos = perfil.ProfileTargets.OrderBy(pt => pt.Order)
            .Select(pt => pt.Target).Where(t => t.IsActive).ToList();

        return activos.Count == 0
            ? ([], "El perfil no tiene servidores activos asignados.")
            : (activos, null);
    }

    /// <summary>
    /// Crea el perfil interno CONGELADO que respalda una selección directa.
    ///
    /// <para><b>Cambia respecto al escritorio y por un motivo concreto.</b> Allí la selección directa
    /// reutilizaba un único perfil oculto («⚡ Selección directa») que se reescribía en cada
    /// despliegue; el propio código anotaba que por eso el historial no podía decir a qué servidores
    /// había ido, y lo compensaba congelando los nombres dentro de la evidencia. Con un solo usuario
    /// eso bastaba. Aquí no: dos personas desplegando a la vez se pisarían el perfil mientras el otro
    /// despliegue sigue corriendo, y el historial de uno acabaría señalando los servidores del otro.
    /// El escritorio ya tenía la solución para su caso multiusuario —los perfiles congelados de los
    /// despliegues programados— y aquí se aplica a todos.</para>
    /// </summary>
    private async Task<int> CongelarSeleccionAsync(List<DeploymentTarget> servidores, CancellationToken ct)
    {
        var nombres = string.Join(", ", servidores.Select(s => s.Nombre));
        if (nombres.Length > 160) nombres = nombres[..160] + "…";

        var perfil = new DeploymentProfile
        {
            Name = $"⚡ {nombres}",
            Description = "Servidores elegidos directamente para un despliegue (no es un perfil guardado).",
            AllowedForOperaciones = false,   // da igual: IsAdHoc lo oculta de todos los selectores
            IsAdHoc = true,
            CreatedAt = DateTime.UtcNow
        };
        db.DeploymentProfiles.Add(perfil);
        await db.SaveChangesAsync(ct);

        int orden = 0;
        foreach (var servidor in servidores)
            db.DeploymentProfileTargets.Add(new DeploymentProfileTarget
            {
                ProfileId = perfil.Id,
                TargetId = servidor.Id,
                Order = orden++
            });
        await db.SaveChangesAsync(ct);

        return perfil.Id;
    }
}

/// <summary>
/// El candado que hace de «comprobar que el servidor está libre» y «apuntar el despliegue» un solo
/// gesto que nadie puede partir por la mitad.
///
/// <para><b>Por qué vive en la BASE y no en el proceso.</b> Un cerrojo en memoria solo ordena a los
/// hilos de una instancia. Con dos —escalado, o los segundos en que conviven la vieja y la nueva
/// durante un intercambio de ranura— cada una tendría el suyo y las dos autorizarían el mismo
/// despliegue. La base es lo único que ven todas, y la casa ya usa este mismo mecanismo para migrar
/// al arrancar: ver <c>PreparacionDeLaBase</c>, que explica por qué <c>sp_getapplock</c> exige una
/// conexión abierta a mano (el candado se ata a la SESIÓN, y EF toma prestada del pool una conexión
/// distinta por operación si no se le fija una).</para>
///
/// <para><b>Se toma corto y se suelta siempre.</b> Un candado filtrado deja sin desplegar a todo el
/// mundo hasta que alguien reinicie, que es bastante peor que el problema que resuelve. De ahí el
/// plazo de espera —quien no lo consigue en unos segundos recibe un «inténtalo otra vez» y no se
/// queda colgado— y de ahí que soltarlo esté en el <c>DisposeAsync</c>, que corre también cuando lo
/// de dentro revienta o vuelve antes de tiempo. Y nunca envuelve el despliegue en sí: eso dura media
/// hora y dejaría el registro de todos los demás esperando.</para>
///
/// <para><b>Sobre SQLite no hay <c>sp_getapplock</c></b> y no pasa nada: ahí queda solo el cerrojo
/// de proceso, que es exactamente lo que hace falta, porque SQLite es un archivo local de una
/// aplicación que corre en un único proceso —el equipo de desarrollo y las pruebas—. No hay segunda
/// instancia contra la que protegerse. En SQL Server se toman los dos: el de proceso porque es
/// gratis y resuelve el caso frecuente sin ir a la base, y el de la base porque es el que ven las
/// demás instancias. Siempre en ese orden, que es lo que evita que dos se queden esperándose.</para>
/// </summary>
public sealed class CandadoDelRegistroDeDespliegues : IAsyncDisposable
{
    /// <summary>
    /// El nombre del recurso candado. Tiene que ser el MISMO en todas las instancias o el bloqueo no
    /// sirve de nada.
    /// </summary>
    private const string Recurso = "AdminWeb.Despliegues.Registro";

    /// <summary>
    /// Cuánto se espera a que otro termine de registrar el suyo. Generoso para lo que dura un
    /// registro (milisegundos) y corto para una persona esperando delante de un botón: si de verdad
    /// hay que esperar tanto, algo va mal y es mejor decirlo que dejar la pantalla colgada.
    /// </summary>
    private const int EsperaMs = 15_000;

    /// <summary>El cerrojo de esta instancia. Estático: es del proceso, no de una petición.</summary>
    private static readonly SemaphoreSlim EnEsteProceso = new(1, 1);

    private readonly AppDbContext _db;
    private bool _enLaBase;
    private bool _conexionAbiertaAqui;
    private bool _soltado;

    private CandadoDelRegistroDeDespliegues(AppDbContext db) => _db = db;

    /// <summary>
    /// Toma el candado, o devuelve <c>null</c> si no se pudo dentro del plazo. Null NO es un detalle
    /// que se pueda ignorar: quien llama tiene que rechazar la operación, porque seguir sin candado
    /// es quedarse sin la protección entera.
    /// </summary>
    public static async Task<CandadoDelRegistroDeDespliegues?> TomarAsync(
        AppDbContext db, CancellationToken ct = default)
    {
        if (!await EnEsteProceso.WaitAsync(EsperaMs, ct)) return null;

        var candado = new CandadoDelRegistroDeDespliegues(db);
        try
        {
            if (!db.Database.IsSqlServer()) return candado;

            if (db.Database.GetDbConnection().State != ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(ct);
                candado._conexionAbiertaAqui = true;
            }

            candado._enLaBase = await PedirALaBaseAsync(db, ct);
            if (candado._enLaBase) return candado;

            await candado.DisposeAsync();
            return null;
        }
        catch
        {
            // Incluida la cancelación: si la petición se fue, el candado no se queda tomado.
            await candado.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_soltado) return;
        _soltado = true;

        // En orden inverso al de la toma, y cada paso a prueba de fallos del anterior: lo único
        // inaceptable aquí es salir sin haber liberado el cerrojo del proceso.
        if (_enLaBase) await SoltarDeLaBaseAsync();

        if (_conexionAbiertaAqui)
        {
            try { await _db.Database.CloseConnectionAsync(); }
            catch { /* si ya se cayó, no hay nada que cerrar */ }
        }

        EnEsteProceso.Release();
    }

    /// <summary>
    /// Pide el candado al motor. Va como PROCEDIMIENTO ALMACENADO con parámetro de retorno y no como
    /// consulta de EF por el mismo motivo que en el arranque: <c>sp_getapplock</c> no es componible y
    /// EF revienta con «non-composable SQL» en cuanto le encadena cualquier cosa. Ese fallo no se ve
    /// en las pruebas —corren sobre SQLite, donde este camino ni se pisa— sino contra SQL Server.
    /// </summary>
    private static async Task<bool> PedirALaBaseAsync(AppDbContext db, CancellationToken ct)
    {
        using var comando = db.Database.GetDbConnection().CreateCommand();
        comando.CommandType = CommandType.StoredProcedure;
        comando.CommandText = "sp_getapplock";

        // >= 0 concedido (0 inmediato, 1 tras esperar); negativo es plazo agotado, interbloqueo o
        // parámetro inválido. Todo lo negativo se trata igual: no hay candado, no se registra.
        var retorno = comando.CreateParameter();
        retorno.ParameterName = "@Resultado";
        retorno.DbType = DbType.Int32;
        retorno.Direction = ParameterDirection.ReturnValue;
        comando.Parameters.Add(retorno);

        Agregar(comando, "@Resource", Recurso);
        Agregar(comando, "@LockMode", "Exclusive");
        Agregar(comando, "@LockOwner", "Session");
        Agregar(comando, "@LockTimeout", EsperaMs);

        await comando.ExecuteNonQueryAsync(ct);

        return Convert.ToInt32(retorno.Value ?? -1) >= 0;
    }

    private async Task SoltarDeLaBaseAsync()
    {
        try
        {
            using var comando = _db.Database.GetDbConnection().CreateCommand();
            comando.CommandType = CommandType.StoredProcedure;
            comando.CommandText = "sp_releaseapplock";

            Agregar(comando, "@Resource", Recurso);
            Agregar(comando, "@LockOwner", "Session");

            // Sin token: soltar el candado no se cancela. Que una petición abortada dejara el
            // registro bloqueado para todos sería el peor final posible de esta historia.
            await comando.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Si la conexión ya se cayó, el motor libera el candado solo al cerrarse la sesión.
        }
    }

    private static void Agregar(IDbCommand comando, string nombre, object valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }
}
