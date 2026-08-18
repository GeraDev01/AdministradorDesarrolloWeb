using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La administración del área: configuración compartida, limpieza de datos y minutas.
///
/// <b>Todo el grupo es del líder</b>, sin una sola ruta que se salga. Aquí se decide a qué servidor
/// se despliega, con qué credenciales y —en la limpieza— qué deja de existir; no hay nada en este
/// archivo que tenga sentido para otro rol.
///
/// <para>La limpieza pide además la contraseña de quien la ejecuta. La política de la ruta dice que
/// es el líder, pero no dice que sea ÉL quien está delante: una sesión abierta y olvidada en un
/// equipo compartido cumple la política igual de bien. Es la única operación del sistema que no se
/// puede deshacer, y por eso es la única que vuelve a preguntar quién eres.</para>
/// </summary>
public static class AdministracionEndpoints
{
    public static void MapAdministracionEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/administracion")
            .WithTags("Administración")
            .RequireAuthorization("SoloAdmin");

        // ── Configuración ────────────────────────────────────────────────────────

        grupo.MapGet("/configuracion", async (SettingsService ajustes, CancellationToken ct) =>
            Results.Ok(await ArmarConfiguracionAsync(ajustes, ct)))
        .WithSummary("Las claves de configuración, agrupadas y sin el valor de los secretos");

        grupo.MapPost("/configuracion", async (
            GuardarConfiguracionRequest cuerpo, SettingsService ajustes, CancellationToken ct) =>
        {
            // Clave por clave, y una que falle no invalida a las demás: el escritorio guardaba todo
            // de un tirón y el primer error dejaba el resto sin guardar sin decir cuáles.
            var resultados = new List<ResultadoDeClaveDto>();
            foreach (var cambio in cuerpo.Cambios ?? [])
            {
                var (ok, mensaje) = await ajustes.GuardarAsync(cambio.Clave, cambio.Valor, ct);
                resultados.Add(new ResultadoDeClaveDto(cambio.Clave, ok, mensaje));
            }

            return Results.Ok(new ResultadoDeConfiguracionDto(resultados));
        })
        .WithSummary("Guarda las claves que se hayan tocado; vacío borra el valor");

        grupo.MapPost("/configuracion/probar", async (
            ProbarConexionRequest cuerpo, PruebasDeConexionService pruebas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pruebas.ProbarAsync(cuerpo.Que, ct);

            // 200 también cuando NO conecta, que es la excepción a la regla de esta API. Un «la
            // contraseña no se aceptó» no es una petición mal hecha: es la respuesta que se vino a
            // buscar. Devolverlo como 400 lo convertiría en el aviso rojo pasajero que el cliente
            // muestra para los errores, y estos mensajes —«el contenedor no existe todavía», «esa
            // carpeta IMAP no existe»— hay que poder leerlos con calma junto al apartado que los
            // produjo.
            return Results.Ok(new ResultadoDto(ok, mensaje));
        })
        .WithSummary("Comprueba una integración contra lo que YA está guardado");

        // ── Limpieza de datos ────────────────────────────────────────────────────

        grupo.MapGet("/limpieza", async (DataCleanupService limpieza, CancellationToken ct) =>
        {
            var cuentas = await limpieza.ContarAsync(ct);
            var areas = DataCleanupService.Areas
                .Select(a => new AreaDeLimpiezaDto(
                    a.Clave, a.Grupo, a.Nombre, a.Arrastra, a.Advertencia,
                    cuentas.TryGetValue(a.Clave, out var n) ? n : -1))
                .ToList();

            return Results.Ok(new LimpiezaDto(DataCleanupService.FraseConfirmacion, areas));
        })
        .WithSummary("Los apartados que se pueden limpiar, con cuántos registros tiene cada uno hoy");

        grupo.MapPost("/limpieza", async (
            LimpiarRequest cuerpo, DataCleanupService limpieza, CancellationToken ct) =>
        {
            var (ok, mensaje, areas) = await limpieza.LimpiarAsync(
                cuerpo.Claves ?? [], cuerpo.Confirmacion ?? "", cuerpo.Contrasena ?? "", ct);

            // Un rechazo (contraseña equivocada, sin confirmar, nada seleccionado) sale como 400 con
            // el texto del servicio, que dice exactamente cuál de los tres fue.
            if (!ok) return Results.BadRequest(new ResultadoDto(false, mensaje));

            return Results.Ok(new ResultadoDeLimpiezaDto(mensaje,
                areas.Select(a => new AreaLimpiadaDto(a.Clave, a.Nombre, a.Habia, a.Filas, a.Error)).ToList()));
        })
        .WithSummary("Borra los apartados indicados; exige la palabra de confirmación y la contraseña");

        // ── Minutas ──────────────────────────────────────────────────────────────

        grupo.MapGet("/minutas", async (
            MinuteType? tipo, DateOnly? desde, DateOnly? hasta,
            MinutasService minutas, CancellationToken ct) =>
            Results.Ok(await minutas.ListarAsync(tipo, ADia(desde), ADia(hasta), ct)))
        .WithSummary("Las minutas del período, con sus compromisos contados");

        grupo.MapGet("/minutas/{id:int}", async (
            int id, MinutasService minutas, CancellationToken ct) =>
        {
            var minuta = await minutas.ObtenerAsync(id, ct);
            return minuta is null
                ? Results.NotFound(new ResultadoDto(false, "Esa minuta ya no existe."))
                : Results.Ok(minuta);
        })
        .WithSummary("Una minuta completa, con su contenido y sus compromisos");

        grupo.MapPost("/minutas", async (
            GuardarMinutaRequest cuerpo, MinutasService minutas, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await minutas.GuardarAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de una minuta con sus compromisos");

        grupo.MapPost("/minutas/{id:int}/eliminar", async (
            int id, MinutasService minutas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await minutas.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una minuta y sus compromisos");

        grupo.MapGet("/minutas/excel", async (
            MinuteType? tipo, DateOnly? desde, DateOnly? hasta,
            MinutasService minutas, CancellationToken ct) =>
            ResultadosDeArchivo.Excel(await minutas.ExcelAsync(tipo, ADia(desde), ADia(hasta), ct), "Minutas"))
        .WithSummary("Las minutas del filtro, en una hoja de cálculo");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Los filtros de fecha viajan como día suelto (<c>DateOnly</c>) y no como instante: lo que se
    /// filtra es la fecha de una minuta, que es un día del calendario. Un <c>DateTimeOffset</c> aquí
    /// obligaría a decidir un huso para algo que no lo tiene.
    /// </summary>
    private static DateTime? ADia(DateOnly? dia) => dia?.ToDateTime(TimeOnly.MinValue);

    // ── El catálogo de claves conocidas ──────────────────────────────────────────────────────
    //
    // Vive aquí y no en Shared porque necesita los nombres de las claves, que son constantes de
    // SettingsService: repetirlos en Shared dejaría dos listas que se desincronizarían el día que
    // alguien renombre una. Y no vive en el servicio porque el servicio está hecho y no se toca.
    //
    // Es CATÁLOGO, no filtro: lo que esté guardado y no aparezca aquí se enseña igual en un grupo
    // aparte. Esconder una clave porque nadie la declaró sería la forma más rápida de que alguien
    // «arregle» a mano una fila que la aplicación ya no deja ver.

    private sealed record ClaveConocida(
        string Clave, string Etiqueta, string? Ayuda, TipoDeCampoDeConfiguracion Tipo);

    /// <param name="Prueba">
    /// El apartado se puede comprobar contra el servicio real. Se declara aquí porque es aquí donde se
    /// sabe qué claves componen cada conexión; la pantalla solo pinta el botón que llegue.
    /// </param>
    private sealed record GrupoConocido(
        string Titulo, string? Nota, ClaveConocida[] Claves, PruebaDeConexion? Prueba = null);

    private static readonly GrupoConocido[] Catalogo =
    [
        new("☁ Azure Blob Storage", "Cambiar las carpetas no mueve lo ya guardado: lo anterior se "
            + "queda donde está y lo nuevo va a la carpeta nueva.",
        [
            new(SettingsService.Claves.AzureBlobConnectionString, "Connection string",
                "DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…;EndpointSuffix=core.windows.net",
                TipoDeCampoDeConfiguracion.Secreto),
            new(SettingsService.Claves.AzureBlobContainer, "Nombre del contenedor",
                "despliegues", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureBlobReleasesPrefix, "Carpeta de versiones",
                "Vacía = la de por omisión.", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureBlobBackupsPrefix, "Carpeta de respaldos de la base de datos",
                "Vacía = la de por omisión.", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureBlobDeployBackupsPrefix, "Carpeta de respaldos previos al despliegue",
                "Vacía = la de por omisión.", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureBlobEnvironmentFolders, "Carpetas por ambiente",
                null, TipoDeCampoDeConfiguracion.Texto),
        ], PruebaDeConexion.Blob),

        new("📁 Rutas por defecto", null,
        [
            new(SettingsService.Claves.DefaultDeployFolder, "Carpeta de despliegue",
                @"C:\builds\publish", TipoDeCampoDeConfiguracion.Texto),
        ]),

        // Sección SIN campos, y a propósito. Antes tenía una caja para capturar
        // «AzureSqlConnectionString» diciendo que era la del escritorio. No lo era: en el escritorio
        // esa constante está declarada y no la lee ni la escribe nadie, y la web tampoco la usa. Era
        // una caja que no gobernaba nada, con el agravante de que alguien podía perder una tarde
        // «arreglando» ahí una conexión rota. Lo que queda es el botón, que ahora prueba la conexión
        // REAL de este servidor — la única pregunta que tenía sentido hacer aquí.
        new("🗄 Base de datos",
            "La cadena de conexión la pone la configuración del servidor (un ajuste del App Service en "
            + "Azure) y no se "
            + "captura aquí: cambiarla es una operación del despliegue, no de esta pantalla. El botón "
            + "comprueba si este servidor puede hablar ahora mismo con su base.",
        [], PruebaDeConexion.Sql),

        new("🔷 Azure DevOps", "Integración opcional.",
        [
            new(SettingsService.Claves.AzureDevOpsEnabled, "Habilitar la integración con Azure DevOps",
                null, TipoDeCampoDeConfiguracion.Interruptor),
            new(SettingsService.Claves.AzureDevOpsOrgUrl, "URL de organización",
                "https://dev.azure.com/mi-org", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureDevOpsProject, "Proyecto",
                "MiProyecto", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.AzureDevOpsPat, "PAT (Personal Access Token)",
                "Cada quien reporta su tiempo con su PAT personal; este es el de la sincronización.",
                TipoDeCampoDeConfiguracion.Secreto),
            new(SettingsService.Claves.DevOpsSyncIntervalMinutes,
                "Cada cuántos minutos se sincroniza sola con DevOps (0 o vacío = solo a mano)",
                null, TipoDeCampoDeConfiguracion.Numero),
        ]),

        new("🎫 Freshdesk", "Integración opcional.",
        [
            new(SettingsService.Claves.FreshDeskEnabled, "Habilitar la integración con Freshdesk",
                null, TipoDeCampoDeConfiguracion.Interruptor),
            new(SettingsService.Claves.FreshDeskDomain, "Dominio (sin .freshdesk.com)",
                "miempresa", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.FreshDeskApiKey, "API Key",
                null, TipoDeCampoDeConfiguracion.Secreto),
            new(SettingsService.Claves.FreshDeskFilterMine, "Traer solo los tickets propios",
                null, TipoDeCampoDeConfiguracion.Interruptor),
            new(SettingsService.Claves.FreshDeskGroup, "Grupo de Freshdesk",
                null, TipoDeCampoDeConfiguracion.Texto),
        ]),

        new("✉ Correo (SMTP + IMAP)", null,
        [
            new(SettingsService.Claves.EmailEnabled, "Habilitar el correo (envío y lectura de carpetas)",
                null, TipoDeCampoDeConfiguracion.Interruptor),
            new(SettingsService.Claves.EmailAddress, "Dirección de correo",
                "tucorreo@empresa.com", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.EmailDisplayName, "Nombre para mostrar",
                null, TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.EmailPassword, "Contraseña de aplicación",
                null, TipoDeCampoDeConfiguracion.Secreto),
            new(SettingsService.Claves.EmailSmtpHost, "Servidor SMTP",
                "smtp.office365.com  /  smtp.gmail.com", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.EmailSmtpPort, "Puerto SMTP",
                "587 (STARTTLS) o 465 (SSL)", TipoDeCampoDeConfiguracion.Numero),
            new(SettingsService.Claves.EmailImapHost, "Servidor IMAP",
                "outlook.office365.com  /  imap.gmail.com", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.EmailImapPort, "Puerto IMAP",
                "993", TipoDeCampoDeConfiguracion.Numero),
            new(SettingsService.Claves.EmailRequirementsFolder, "Carpeta para ingerir requerimientos",
                "INBOX", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.SlaEscalationEmail, "Correo del líder para escalamientos de SLA vencido",
                "Si se deja vacío, los avisos llegan a la propia cuenta de la aplicación. Varios, "
                + "separados por punto y coma.", TipoDeCampoDeConfiguracion.Texto),
        ], PruebaDeConexion.Correo),

        new("📄 Documentos de vacaciones", null,
        [
            new(SettingsService.Claves.VacationDepartamento, "Departamento (por defecto en la solicitud)",
                "DESARROLLO", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.VacationPuestoDefault, "Puesto (por defecto)",
                "Desarrollador Web", TipoDeCampoDeConfiguracion.Texto),
            new(SettingsService.Claves.VacationJefeDirecto, "Líder directo (nombre que firma la autorización)",
                null, TipoDeCampoDeConfiguracion.Texto),
        ]),

        new("🚀 Despliegues", null,
        [
            new(SettingsService.Claves.DeployBackupEnabled, "Respaldar antes de desplegar",
                null, TipoDeCampoDeConfiguracion.Interruptor),
        ]),

        new("🎯 Pool de actividades", null,
        [
            new(SettingsService.Claves.PoolMaxTomadas, "Cuántas actividades puede tener tomadas una persona a la vez",
                null, TipoDeCampoDeConfiguracion.Numero),
        ]),
    ];

    /// <summary>Título del grupo donde caen las claves guardadas que el catálogo no declara.</summary>
    private const string GrupoDeLasOtras = "🧩 Otras claves guardadas";

    /// <summary>
    /// Junta el catálogo con lo que hay guardado. Una clave conocida pero nunca capturada sale igual,
    /// vacía: si solo se enseñara lo que ya existe en la base, una instalación nueva mostraría una
    /// pantalla en blanco y no habría por dónde empezar.
    /// </summary>
    private static async Task<ConfiguracionDto> ArmarConfiguracionAsync(
        SettingsService ajustes, CancellationToken ct)
    {
        var guardadas = (await ajustes.TodoAsync(ct))
            .ToDictionary(v => v.Clave, StringComparer.OrdinalIgnoreCase);

        var grupos = Catalogo
            .Select(g => new GrupoDeConfiguracionDto(g.Titulo, g.Nota,
                g.Claves.Select(c => Describir(c, guardadas.GetValueOrDefault(c.Clave))).ToList(),
                g.Prueba))
            .ToList();

        var declaradas = Catalogo.SelectMany(g => g.Claves).Select(c => c.Clave)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var otras = guardadas.Values
            .Where(v => !declaradas.Contains(v.Clave))
            .OrderBy(v => v.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(v => new ClaveDeConfiguracionDto(
                v.Clave, v.Clave, v.Descripcion, v.Valor, v.EsSecreto, v.Configurado, v.RequiereRecaptura,
                v.EsSecreto ? TipoDeCampoDeConfiguracion.Secreto : TipoDeCampoDeConfiguracion.Texto))
            .ToList();

        if (otras.Count > 0)
            // Sin prueba: no se sabe qué son, así que tampoco contra qué se comprobarían.
            grupos.Add(new GrupoDeConfiguracionDto(GrupoDeLasOtras,
                "Están en la base pero esta pantalla no las declara: las escribió otra versión de la "
                + "aplicación o el escritorio. Se pueden ver y corregir, no adivinar qué hacen.",
                otras, null));

        return new ConfiguracionDto(grupos);
    }

    /// <summary>
    /// Una clave conocida, con lo que se sepa de ella. Sin fila guardada queda «sin configurar», y su
    /// carácter de secreta lo decide entonces el catálogo — con la MISMA regla por sufijo que aplica
    /// el servicio al guardar, para que lo que la pantalla anuncia como secreto acabe cifrado.
    /// </summary>
    private static ClaveDeConfiguracionDto Describir(ClaveConocida conocida, ValorDeConfiguracion? fila)
    {
        bool esSecreto = fila?.EsSecreto ?? conocida.Tipo == TipoDeCampoDeConfiguracion.Secreto;

        return new ClaveDeConfiguracionDto(
            conocida.Clave,
            conocida.Etiqueta,
            conocida.Ayuda ?? fila?.Descripcion,
            esSecreto ? null : fila?.Valor,
            esSecreto,
            fila?.Configurado ?? false,
            fila?.RequiereRecaptura ?? false,
            esSecreto ? TipoDeCampoDeConfiguracion.Secreto : conocida.Tipo);
    }
}
