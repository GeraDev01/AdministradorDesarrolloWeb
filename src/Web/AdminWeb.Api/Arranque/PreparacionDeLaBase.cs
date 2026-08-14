using System.Data;
using AdminWeb.Application.Demo;
using AdminWeb.Application.Manual;
using AdminWeb.Application.Services;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AdminWeb.Api.Arranque;

/// <summary>
/// Pone la base al día al arrancar y siembra lo imprescindible.
/// </summary>
public static class PreparacionDeLaBase
{
    /// <summary>
    /// Nombre del candado. Tiene que ser el MISMO en todas las instancias de la API o el bloqueo no
    /// sirve de nada.
    /// </summary>
    private const string Candado = "AdminWeb.Migrador";

    /// <summary>Cuánto espera una instancia a que la otra termine de migrar, en milisegundos.</summary>
    private const int EsperaMs = 120_000;

    /// <summary>
    /// Aplica el esquema y siembra el administrador inicial si la base está vacía.
    ///
    /// Todo va dentro de un <c>sp_getapplock</c>: la API puede estar corriendo en varias instancias
    /// —o reiniciándose durante un despliegue escalonado— y los parches del migrador son
    /// «comprobar si falta la columna y agregarla», que no es atómico. Sin el candado, dos arranques
    /// simultáneos comprueban a la vez que falta lo mismo y el segundo <c>ALTER</c> revienta.
    ///
    /// Quien no consigue el candado NO migra y sigue arrancando: la otra instancia ya dejó la base
    /// al día, y quedarse esperando indefinidamente sería peor que arrancar.
    /// </summary>
    public static async Task PrepararAsync(IServiceProvider servicios, ILogger log, CancellationToken ct = default)
    {
        using var alcance = servicios.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<AppDbContext>();

        bool esSqlServer = !db.Database.IsSqlite();
        bool candadoTomado = false;
        bool conexionAbiertaAqui = false;

        try
        {
            if (esSqlServer)
            {
                // ── LA BASE TIENE QUE EXISTIR ANTES DE PODER CANDARLA ─────────────
                //
                // El candado vive DENTRO de una base de datos: pedirlo en una que no existe falla al
                // abrir la conexión, con el error 4060 y no con nada que se entienda. En producción
                // esto no se nota —la base es la de siempre— pero en un entorno nuevo el arranque
                // moría antes de llegar a crearla.
                //
                // Crearla queda fuera del candado y es aceptable: una base que todavía no existe no
                // tiene dos instancias compitiendo por ella, porque no hay nada desplegado contra
                // ella. Desde la siguiente línea en adelante sí está todo protegido.
                var creador = db.GetService<IRelationalDatabaseCreator>();
                if (!await creador.ExistsAsync(ct))
                {
                    log.LogInformation("La base no existía: se crea antes de migrar.");
                    await creador.CreateAsync(ct);
                }

                // ── POR QUÉ SE ABRE LA CONEXIÓN A MANO ────────────────────────────
                //
                // El candado se pide con LockOwner = 'Session', o sea atado a la CONEXIÓN. Y EF
                // toma prestada una conexión del pool para cada operación y la devuelve al acabar:
                // sin fijarla, el candado se pediría en una conexión, la migración correría en otra
                // —sin protección ninguna— y el sp_releaseapplock intentaría soltar en una tercera
                // un candado que ahí nunca existió.
                //
                // Abrirla explícitamente hace que EF la reutilice para todo lo que pase con este
                // contexto, que es lo único que vuelve real el bloqueo. Sin esto el candado
                // compilaba, no daba error y no protegía absolutamente nada.
                if (db.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await db.Database.OpenConnectionAsync(ct);
                    conexionAbiertaAqui = true;
                }

                candadoTomado = await TomarCandadoAsync(db, ct);
                if (!candadoTomado)
                {
                    log.LogInformation(
                        "Otra instancia está migrando la base; esta arranca sin migrar (la base ya quedará al día).");
                    return;
                }
            }

            // «Al día» solo si NO falló ni una sentencia. El mensaje de antes se escribía pasara lo
            // que pasara, y por eso un migrador que se saltó 114 parches pudo anunciar durante días
            // que todo estaba correcto. Un aviso por cada sentencia fallida, con su motivo, porque
            // un resumen «hubo 3 fallos» obliga a ir a buscarlos justo cuando no hay tiempo.
            var fallidas = DatabaseMigrator.EnsureUpToDate(db);

            if (fallidas.Count == 0)
            {
                log.LogInformation("Esquema al día.");
            }
            else
            {
                log.LogError(
                    "ESQUEMA INCOMPLETO: {Cuantas} sentencia(s) de migración fallaron. La aplicación " +
                    "arranca igual —negarse dejaría a todo el equipo fuera por un índice— pero hay " +
                    "columnas o tablas que NO existen, y lo que dependa de ellas fallará más tarde y " +
                    "en otro sitio. Revísalo antes de dar el despliegue por bueno.", fallidas.Count);

                foreach (var fallo in fallidas)
                    log.LogError("Migración fallida: {Sentencia}", fallo);
            }

            await ReconciliarDesplieguesAsync(servicios, db, log, ct);

            // El administrador inicial solo se crea si NO hay ninguna cuenta. Su contraseña temporal
            // se escribe en el registro una única vez porque no hay dónde mostrarla: en el escritorio
            // salía en un cuadro de diálogo al arrancar, aquí el arranque no tiene a nadie delante.
            var auth = alcance.ServiceProvider.GetRequiredService<AuthService>();
            var temporal = await auth.SeedAdminAsync(ct);
            if (temporal != null)
                log.LogWarning(
                    "Base sin usuarios: se creó la cuenta «admin» con contraseña temporal {Temporal}. " +
                    "Entra con ella y cámbiala de inmediato; no vuelve a mostrarse.", temporal);

            await SembrarCatalogosAsync(db, log, ct);
            await SembrarManualAsync(db, log, ct);
            await SembrarFestivosAsync(alcance.ServiceProvider, log, ct);
            await SembrarDemostracionAsync(alcance.ServiceProvider, db, log, ct);
        }
        finally
        {
            if (candadoTomado) await SoltarCandadoAsync(db, ct);
            if (conexionAbiertaAqui) await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Cierra los despliegues que se quedaron «En curso» porque el servidor que los estaba corriendo
    /// se detuvo a media faena. La lógica —a quién se puede cerrar y a quién no— está en
    /// <see cref="ReconciliacionDeDespliegues"/>; aquí se decide CUÁNDO se le pregunta.
    ///
    /// <para><b>Un fallo NO tumba el arranque</b>, por lo mismo que los catálogos: sin reconciliar,
    /// la aplicación funciona igual y lo único que pasa es que el historial sigue enseñando en marcha
    /// un despliegue que ya no lo está. Negarse a arrancar por eso dejaría al equipo fuera para
    /// arreglar un dato de lectura.</para>
    ///
    /// <para>Va dentro del candado del migrador, como la siembra, para que dos instancias que
    /// arranquen a la vez no escriban lo mismo dos veces; y necesariamente DESPUÉS de él, porque
    /// consulta tablas que el propio migrador podría estar estrenando.</para>
    /// </summary>
    private static async Task ReconciliarDesplieguesAsync(
        IServiceProvider servicios, AppDbContext db, ILogger log, CancellationToken ct)
    {
        try
        {
            int cerrados = await ReconciliacionDeDespliegues.CerrarInterrumpidosAsync(db, DateTime.UtcNow, ct);
            if (cerrados > 0)
                log.LogWarning(
                    "{n} despliegue(s) se habían quedado «En curso» sin nadie ejecutándolos y se cerraron " +
                    "como interrumpidos. Su expediente explica hasta dónde se llegó a saber.", cerrados);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "No se pudieron reconciliar los despliegues interrumpidos. La aplicación arranca igual.");
        }

        ProgramarSegundaPasada(servicios, log);
    }

    /// <summary>
    /// La SEGUNDA pasada, unos minutos después de arrancar. No es un cinturón de más: es la que
    /// arregla el caso NORMAL.
    ///
    /// <para>Un reinicio dura segundos, y la señal de vida del despliegue que murió con el proceso
    /// anterior tarda <see cref="SenalDeVidaDelDespliegue.Tolerancia"/> en caducar. Cuando esta
    /// instancia arranca, esa señal todavía parece fresca, así que la primera pasada —con toda la
    /// razón— no la toca: desde fuera es indistinguible de un despliegue que OTRA instancia está
    /// corriendo ahora mismo, y cerrarle el trabajo a alguien que está desplegando de verdad sería
    /// bastante peor que dejar una fila mintiendo un rato. La única forma de distinguirlos es esperar
    /// a que la señal caduque y volver a mirar: si hay alguien ejecutándolo, la habrá renovado; si no
    /// la renovó nadie, ya no hay duda posible.</para>
    ///
    /// <para><b>No se espera al resultado</b> —el arranque no puede quedarse seis minutos parado— y
    /// se cancela si la aplicación se detiene antes. No es un trabajo periódico de los que se
    /// registran en <c>Program</c>: es la segunda mitad de ESTE arranque, ocurre una sola vez y
    /// carece de sentido fuera de él.</para>
    /// </summary>
    private static void ProgramarSegundaPasada(IServiceProvider servicios, ILogger log)
    {
        var parada = servicios.GetService<IHostApplicationLifetime>()?.ApplicationStopping
                     ?? CancellationToken.None;

        // El margen sobre la tolerancia no es simetría: es para que la señal esté caducada con
        // holgura y no justo en el filo, donde un reloj adelantado decidiría por nosotros.
        var espera = SenalDeVidaDelDespliegue.Tolerancia + TimeSpan.FromMinutes(1);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(espera, parada);

                // Ámbito propio: el de PrepararAsync se cerró hace rato y su contexto con él.
                using var alcance = servicios.CreateScope();
                var db = alcance.ServiceProvider.GetRequiredService<AppDbContext>();

                int cerrados = await ReconciliacionDeDespliegues.CerrarInterrumpidosAsync(
                    db, DateTime.UtcNow, parada);

                if (cerrados > 0)
                    log.LogWarning(
                        "{n} despliegue(s) que este arranque dejó en observación resultaron interrumpidos " +
                        "y se cerraron: nadie renovó su señal de vida.", cerrados);
            }
            catch (OperationCanceledException)
            {
                // La aplicación se detuvo antes. No hay nada que arreglar: el próximo arranque
                // vuelve a mirar, y esos trabajos seguirán ahí esperándolo.
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falló la segunda pasada de reconciliación de despliegues.");
            }
        });
    }

    /// <summary>
    /// Siembra los catálogos con los que la aplicación tiene que arrancar: criterios de puntuación,
    /// plantillas y la configuración del pool.
    ///
    /// <para><b>El orden importa y es el mismo del escritorio.</b> El renombrado va ANTES del
    /// sembrado de criterios: al revés, el sembrado insertaría la versión nueva de cada criterio y
    /// la base acabaría con el mismo criterio dos veces. Y el pool va DESPUÉS, porque agrega los
    /// suyos a ese mismo catálogo.</para>
    ///
    /// <para>Va dentro del candado, junto con la migración, por la misma razón que ella: dos
    /// instancias arrancando a la vez sembrarían el catálogo por duplicado.</para>
    ///
    /// <para>Un fallo aquí NO tumba el arranque. Es lo que hacía el escritorio y sigue siendo lo
    /// correcto: sin catálogo inicial la aplicación funciona —solo estrena algunas pantallas
    /// vacías—, mientras que negarse a arrancar dejaría a todo el equipo fuera por un dato de
    /// conveniencia. Queda en el registro para que se note.</para>
    /// </summary>
    private static async Task SembrarCatalogosAsync(
        AppDbContext db, ILogger log, CancellationToken ct)
    {
        try
        {
            var renombrados = await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db, ct);
            if (renombrados > 0)
                log.LogInformation("{n} criterio(s) renombrados al catálogo nuevo.", renombrados);

            await ScoringCriteriaSeed.SembrarAsync(db, ct);

            var plantillas = await TemplateSeed.SembrarAsync(db, ct);
            if (plantillas > 0)
                log.LogInformation("{n} plantilla(s) del catálogo inicial sembradas.", plantillas);

            var pool = await PoolSeed.SembrarAsync(db, ct);
            if (pool > 0)
                log.LogInformation("{n} fila(s) de configuración del pool sembradas.", pool);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo sembrar el catálogo inicial. La aplicación arranca igual.");
        }
    }

    /// <summary>
    /// Siembra el MANUAL DE USO como artículos publicados de la base de conocimiento.
    ///
    /// <para><b>Va SIEMPRE, en cualquier base y en cualquier entorno</b>, y ahí está la diferencia con
    /// <see cref="SembrarDemostracionAsync"/>, que es lo único con lo que se podría confundir. Los
    /// datos de demostración son personas y trabajo inventados para poder enseñar la aplicación, y por
    /// eso llevan tres guardas. El manual es contenido del producto —como los criterios de puntuación
    /// o las plantillas—, y copiarle esas guardas lo dejaría fuera precisamente de la base de
    /// producción, que es donde entra la gente nueva que lo necesita.</para>
    ///
    /// <para>Va después de los catálogos y dentro del candado, por lo mismo que ellos: dos instancias
    /// arrancando a la vez insertarían el manual por duplicado.</para>
    ///
    /// <para><b>Nunca pisa lo que haya.</b> Solo inserta lo que jamás se ha sembrado, y eso lo decide
    /// <see cref="ManualDeUso.SembrarAsync"/> con la lista de claves ya sembradas; una corrección que
    /// alguien haya hecho sobre un artículo del manual sobrevive a todos los arranques siguientes.
    /// Por eso el registro se escribe en <c>info</c> y no en <c>warning</c>: lo normal, arranque tras
    /// arranque, es que este método no agregue absolutamente nada.</para>
    ///
    /// <para>Un fallo NO tumba el arranque, igual que en los catálogos: sin manual la aplicación
    /// funciona, y negarse a arrancar por unos artículos de documentación dejaría a todo el equipo
    /// fuera. Queda en el registro para que se note.</para>
    /// </summary>
    private static async Task SembrarManualAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        try
        {
            int agregados = await ManualDeUso.SembrarAsync(db, ct);
            if (agregados > 0)
                log.LogInformation(
                    "{n} artículo(s) del manual de uso sembrados en la base de conocimiento, " +
                    "etiquetados «{Etiqueta}».", agregados, ManualDeUso.Etiqueta);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo sembrar el manual de uso. La aplicación arranca igual.");
        }
    }

    /// <summary>
    /// Llena la base con datos de DEMOSTRACIÓN, si se pidió y si se puede.
    ///
    /// <para>Va dentro del candado, igual que la migración y los catálogos: dos instancias arrancando
    /// a la vez sembrarían el juego de datos por duplicado y la aplicación se abriría con ocho
    /// desarrolladores en vez de cuatro.</para>
    ///
    /// <para>Las tres condiciones —pedirlo, no estar en Production y que la base esté virgen— las
    /// comprueba el propio sembrador, dentro de <c>DatosDeDemostracion.SembrarSiSePuedeAsync</c>, que
    /// es la única puerta que tiene; ahí está explicado por qué son tres y no una, y por qué se
    /// comprueban allá y no aquí. De este lado solo se decide con qué entorno se le pregunta y qué
    /// contar en el registro.</para>
    ///
    /// <para><b>El registro anuncia la contraseña Y el secreto del segundo factor</b>, y las dos
    /// cosas son deliberadas. Estas cuentas nacen con el segundo factor ya activo porque es
    /// obligatorio y sin él la demostración no pasa de la primera pantalla; el secreto es fijo y el
    /// mismo para todas, así que publicarlo no le quita nada a nadie —no protege nada por diseño— y
    /// en cambio es la única forma de que quien levanta esto pueda entrar sin un teléfono dado de
    /// alta. El mensaje lo dice con todas sus letras para que nadie confunda esta base con una de
    /// verdad si se topa con la línea en un registro suelto.</para>
    ///
    /// <para>Un fallo NO tumba el arranque, por lo mismo que el de los catálogos: sin datos de
    /// demostración la aplicación funciona perfectamente —solo se abre vacía—, y negarse a arrancar
    /// por un dato de conveniencia sería desproporcionado. Queda en el registro.</para>
    /// </summary>
    /// <summary>
    /// Repone los días de descanso obligatorio del artículo 74 que falten, del año en curso en
    /// adelante.
    ///
    /// <para>Corre en CADA arranque y solo añade lo que no está: no borra, no reescribe y no toca los
    /// días que haya puesto la casa. Así el calendario se mantiene solo y nadie tiene que acordarse de
    /// resembrarlo en enero — que es exactamente el olvido que dejaría de contar los festivos y le
    /// descontaría esos días a quien pida vacaciones.</para>
    ///
    /// <para>Un fallo aquí NO tumba el arranque: sin festivos el saldo sale algo más bajo, que es
    /// malo, pero dejar a todo el equipo fuera de la aplicación es peor.</para>
    /// </summary>
    private static async Task SembrarFestivosAsync(
        IServiceProvider servicios, ILogger log, CancellationToken ct)
    {
        try
        {
            var calendario = servicios.GetRequiredService<CalendarioLaboralService>();
            int puestos = await calendario.SembrarLosDeLeyAsync(DateTime.Today.Year, ct);

            if (puestos > 0)
                log.LogInformation(
                    "Calendario laboral: se sembraron {Cuantos} día(s) de descanso obligatorio "
                    + "(art. 74 LFT) para los próximos {Anios} años.",
                    puestos, CalendarioLaboralService.AniosQueSeSiembran);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "No se pudieron sembrar los días festivos. La aplicación arranca igual, pero mientras "
                + "la tabla esté incompleta las vacaciones descontarán esos días como si se "
                + "trabajaran. Revísalo.");
        }
    }

    private static async Task SembrarDemostracionAsync(
        IServiceProvider servicios, AppDbContext db, ILogger log, CancellationToken ct)
    {
        var configuracion = servicios.GetRequiredService<IConfiguration>();
        var entorno = servicios.GetRequiredService<IHostEnvironment>();

        bool pedido = configuracion.GetValue(DatosDeDemostracion.Clave, false);
        if (!pedido) return;

        try
        {
            var protector = servicios.GetRequiredService<IProtectorDeSecretos>();

            var resumen = await DatosDeDemostracion.SembrarSiSePuedeAsync(
                db, protector, pedido, entorno.IsProduction(), ct);

            if (resumen == null)
            {
                // Se pidió y aun así no se sembró: hay que decir por qué. En silencio parecería que
                // la clave de configuración no funciona.
                log.LogWarning(
                    "Se pidieron datos de demostración pero NO se sembraron: " +
                    "{Motivo}. Es la guarda que impide llenar de datos falsos una base con contenido.",
                    entorno.IsProduction()
                        ? "el entorno es Production"
                        : "la base ya tiene desarrolladores, así que no está vacía");
                return;
            }

            log.LogWarning(
                "DATOS DE DEMOSTRACIÓN sembrados: {Resumen}. Todas las cuentas entran con la " +
                "contraseña «{Contrasena}» — esto NO es una base de verdad.",
                resumen, DatosDeDemostracion.Contrasena);

            log.LogWarning(
                "SEGUNDO FACTOR DE LAS CUENTAS DE DEMOSTRACIÓN: ya lo tienen activo, y las seis con " +
                "el MISMO secreto fijo «{Secreto}». Tecléalo en cualquier aplicación de códigos —o " +
                "pásaselo a un guion— y el código de seis dígitos que salga sirve para entrar con " +
                "cualquiera de ellas. Está aquí porque el segundo factor es obligatorio y sin esto " +
                "la demostración no pasaría de la pantalla del código QR sin un teléfono a mano. Un " +
                "secreto publicado NO protege nada: da igual porque estas cuentas son inventadas, y " +
                "por eso el sembrador se niega a correr si el entorno es Production o si la base " +
                "tiene algo dentro.",
                DatosDeDemostracion.SecretoDelSegundoFactor);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudieron sembrar los datos de demostración. La aplicación arranca igual.");
        }
    }

    /// <summary>
    /// Pide el candado. Devuelve false si no se consiguió dentro del plazo.
    ///
    /// <para>Se llama como PROCEDIMIENTO ALMACENADO con un parámetro de retorno, y no como una
    /// consulta de EF. Es la única forma que funciona: <c>sp_getapplock</c> lleva <c>DECLARE</c> y
    /// <c>EXEC</c>, y EF rechaza construir sobre SQL así en cuanto se le encadena cualquier operador
    /// —un <c>First()</c> le añade un <c>TOP(1)</c> por fuera y revienta con «non-composable SQL»—.
    /// Ese fallo no se veía en las pruebas porque corren sobre SQLite, donde este camino ni se pisa;
    /// aparecía al arrancar contra SQL Server, o sea el día del corte.</para>
    /// </summary>
    private static async Task<bool> TomarCandadoAsync(AppDbContext db, CancellationToken ct)
    {
        var conexion = db.Database.GetDbConnection();

        using var comando = conexion.CreateCommand();
        comando.CommandType = CommandType.StoredProcedure;
        comando.CommandText = "sp_getapplock";

        // El valor de retorno: >= 0 concedido (0 inmediato, 1 tras esperar); negativo es plazo
        // agotado, interbloqueo o parámetro inválido.
        var retorno = comando.CreateParameter();
        retorno.ParameterName = "@Resultado";
        retorno.DbType = DbType.Int32;
        retorno.Direction = ParameterDirection.ReturnValue;
        comando.Parameters.Add(retorno);

        Agregar(comando, "@Resource", Candado);
        Agregar(comando, "@LockMode", "Exclusive");
        Agregar(comando, "@LockOwner", "Session");
        Agregar(comando, "@LockTimeout", EsperaMs);

        await comando.ExecuteNonQueryAsync(ct);

        return Convert.ToInt32(retorno.Value ?? -1) >= 0;
    }

    private static async Task SoltarCandadoAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            var conexion = db.Database.GetDbConnection();

            using var comando = conexion.CreateCommand();
            comando.CommandType = CommandType.StoredProcedure;
            comando.CommandText = "sp_releaseapplock";

            Agregar(comando, "@Resource", Candado);
            Agregar(comando, "@LockOwner", "Session");

            await comando.ExecuteNonQueryAsync(ct);
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
