using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Catálogo con el que arranca la biblioteca de plantillas, para que la pantalla no se estrene
/// vacía. Son puntos de partida pensados para editarse, no reglas: todos usan marcadores
/// <c>{{así}}</c> y ninguno lleva datos reales de clientes ni de servidores.
///
/// Se siembra UNA sola vez y queda marcado en <c>AppSettings</c>. A diferencia de los criterios de
/// desempeño, aquí no se re-agrega lo que falte: si el administrador borra una plantilla de ejemplo
/// es porque no la quiere, y verla reaparecer en cada arranque sería un estorbo.
///
/// <para>Portado del escritorio sin tocar el catálogo: los textos son los mismos, palabra por
/// palabra. Lo único que cambia es que es asíncrono, como el resto de la capa — en un servidor
/// el arranque no debe bloquear un hilo esperando a la base.</para>
/// </summary>
public static class TemplateSeed
{
    /// <summary>Marca en AppSettings de que el catálogo inicial ya se sembró.</summary>
    public const string ClaveSembrado = "TemplatesSeeded";

    /// <summary>Siembra el catálogo si nunca se ha hecho. Devuelve cuántas plantillas agregó.</summary>
    public static async Task<int> SembrarAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.AppSettings.AnyAsync(s => s.Key == ClaveSembrado, ct)) return 0;

        // Una base que ya tiene plantillas (p. ej. viene de otra instalación) se marca como sembrada
        // sin tocar nada: nadie quiere ver aparecer ejemplos junto a su material real.
        int agregadas = 0;
        if (!await db.Templates.AnyAsync(ct))
        {
            var ahora = DateTime.UtcNow;
            foreach (var (kind, title, description, tags, body) in Catalogo)
            {
                db.Templates.Add(new Template
                {
                    Kind = kind,
                    Title = title,
                    Description = description,
                    Tags = tags,
                    Body = body.Replace("\r\n", "\n").Trim(),
                    CreatedAt = ahora
                });
                agregadas++;
            }
        }

        db.AppSettings.Add(new AppSetting
        {
            Key = ClaveSembrado,
            Value = "1",
            Description = "Ya se sembró el catálogo inicial de plantillas; no volver a hacerlo."
        });
        await db.SaveChangesAsync(ct);
        return agregadas;
    }

    private static readonly (TemplateKind Kind, string Title, string Description, string Tags, string Body)[] Catalogo =
    [
        // ── Freshdesk ────────────────────────────────────────────────────────────
        (TemplateKind.TicketFreshdesk,
         "Freshdesk — Alta de incidencia",
         "Para levantar un ticket con lo mínimo que se necesita para reproducir el problema.",
         "alta, incidencia, bug",
         """
         Asunto: [{{sistema}}] {{resumen del problema}}

         Sistema / módulo: {{sistema}} — {{modulo}}
         Ambiente: {{ambiente}}   (Productivo / QA / Desarrollo)
         Detectado el: {{fecha}}
         Reportado por: {{quien reporta}}

         Qué pasa
         {{descripcion del problema}}

         Pasos para reproducirlo
         1. {{paso 1}}
         2. {{paso 2}}
         3. {{paso 3}}

         Resultado esperado: {{lo que deberia pasar}}
         Resultado obtenido: {{lo que pasa}}

         Impacto: {{a quien y cuanto afecta}}
         Evidencias: se adjuntan capturas y el mensaje de error completo.
         """),

        (TemplateKind.TicketFreshdesk,
         "Freshdesk — Solicitud de cambio o requerimiento",
         "Alta de un ticket que no es una falla, sino algo nuevo o una modificación.",
         "alta, cambio, requerimiento",
         """
         Asunto: [{{sistema}}] Solicitud: {{resumen}}

         Solicita: {{area o persona}}
         Fecha de solicitud: {{fecha}}
         Sistema / módulo: {{sistema}} — {{modulo}}

         Qué se pide
         {{descripcion de lo solicitado}}

         Para qué (necesidad de negocio)
         {{motivo}}

         Criterios de aceptación
         - {{criterio 1}}
         - {{criterio 2}}

         Fecha deseada: {{fecha deseada}}
         Nota: la fecha se confirma después de estimar; se avisa por este mismo ticket.
         """),

        (TemplateKind.RespuestaFreshdesk,
         "Freshdesk — Acuse de recibo",
         "Primera respuesta al cliente: confirma que el ticket ya está en manos de alguien.",
         "primera respuesta, acuse",
         """
         Hola {{cliente}}:

         Recibimos tu reporte y ya quedó registrado con el folio {{folio}}. Lo está revisando
         {{responsable}} y te damos seguimiento por este mismo ticket.

         Compromiso de primera revisión: {{fecha compromiso}}.

         Si tienes capturas, el mensaje de error completo o el usuario con el que ocurrió,
         adjúntalos aquí: nos ahorra un ida y vuelta.

         Saludos,
         {{usuario}}
         """),

        (TemplateKind.RespuestaFreshdesk,
         "Freshdesk — Falta información para continuar",
         "Cuando el ticket no se puede reproducir con lo que mandaron.",
         "informacion, pendiente cliente",
         """
         Hola {{cliente}}:

         Estuvimos revisando el folio {{folio}} y no logramos reproducir lo que describes.
         Para poder avanzar necesitamos:

         - {{dato 1}}
         - {{dato 2}}
         - Captura completa de la pantalla, incluyendo la barra del navegador.
         - Fecha y hora aproximadas en que ocurrió, y el usuario con el que entraste.

         El ticket queda en espera de tu respuesta. En cuanto nos compartas lo anterior
         retomamos la revisión.

         Saludos,
         {{usuario}}
         """),

        (TemplateKind.RespuestaFreshdesk,
         "Freshdesk — Solución entregada y cierre",
         "Cierre del ticket: qué se hizo, dónde quedó y cómo validarlo.",
         "cierre, solucion",
         """
         Hola {{cliente}}:

         Ya quedó atendido el folio {{folio}}.

         Qué se encontró: {{causa}}
         Qué se hizo: {{solucion}}
         Dónde quedó: {{ambiente}}, versión {{version}}, liberado el {{fecha}}.

         Cómo validarlo
         1. {{paso de validacion 1}}
         2. {{paso de validacion 2}}

         Te pedimos confirmarlo por este medio. Si en {{dias}} días no tenemos respuesta,
         cerramos el ticket; puedes reabrirlo cuando lo necesites.

         Saludos,
         {{usuario}}
         """),

        // ── Requerimientos ───────────────────────────────────────────────────────
        (TemplateKind.ComentarioRequerimiento,
         "Requerimiento — Observaciones del análisis",
         "Lo que se detectó al analizar, antes de comprometer fecha.",
         "analisis, observaciones",
         """
         Análisis del requerimiento {{numero}} — {{fecha}}

         Alcance entendido
         {{que se va a hacer}}

         Fuera de alcance (para que no haya sorpresas)
         - {{fuera 1}}
         - {{fuera 2}}

         Observaciones
         - {{observacion 1}}
         - {{observacion 2}}

         Dependencias / bloqueos
         - {{dependencia}}

         Riesgos
         - {{riesgo}}

         Con esto la estimación queda en {{horas}} horas. Cualquier cambio al alcance
         obliga a re-estimar.
         """),

        (TemplateKind.ComentarioRequerimiento,
         "Requerimiento — Avance y siguiente paso",
         "Comentario de seguimiento para que nadie tenga que preguntar cómo va.",
         "avance, seguimiento",
         """
         Avance al {{fecha}} — {{porcentaje}}%

         Terminado
         - {{terminado}}

         En curso
         - {{en curso}}

         Siguiente
         - {{siguiente}}

         Bloqueos: {{bloqueos o "ninguno"}}
         Fecha de entrega estimada: {{fecha entrega}} (sin cambio / se recorre por {{motivo}})
         """),

        (TemplateKind.ComentarioRequerimiento,
         "Requerimiento — Devolución al solicitante",
         "Cuando lo pedido no alcanza para trabajar y hay que regresarlo.",
         "devolucion, incompleto",
         """
         Regreso el requerimiento {{numero}} porque no es posible trabajarlo con lo que hay hoy.

         Falta
         - {{faltante 1}}
         - {{faltante 2}}

         Por qué hace falta
         {{explicacion}}

         En cuanto se complete, se retoma y se estima. Queda en pausa desde el {{fecha}};
         ese tiempo no cuenta contra el compromiso de entrega.
         """),

        // ── Azure DevOps ─────────────────────────────────────────────────────────
        (TemplateKind.ComentarioDevOps,
         "DevOps — Comentario de avance en el work item",
         "El comentario periódico que exige el seguimiento de SLA.",
         "avance, sla, work item",
         """
         [{{fechahora}}] Avance por {{usuario}}

         Hecho desde el último comentario: {{avance}}
         En qué estoy ahora: {{en curso}}
         Tiempo invertido en este tramo: {{horas}} h
         Bloqueos: {{bloqueos o "ninguno"}}
         Siguiente revisión: {{fecha}}
         """),

        (TemplateKind.ComentarioDevOps,
         "DevOps — Cierre con evidencias",
         "Lo que debe quedar escrito antes de mover el work item a terminado.",
         "cierre, evidencias",
         """
         Cierre — {{fecha}}

         Causa raíz: {{causa}}
         Solución: {{solucion}}
         Archivos / objetos tocados: {{archivos}}
         Pull request: {{pr}}
         Pruebas realizadas:
         - {{prueba 1}}
         - {{prueba 2}}

         Desplegado en: {{ambiente}}, versión {{version}}
         Evidencias adjuntas: {{capturas}}
         Riesgo de regresión: {{riesgo}}
         """),

        (TemplateKind.ComentarioDevOps,
         "DevOps — Reasignación o escalamiento",
         "Para dejar constancia de por qué el ticket cambia de manos.",
         "reasignacion, escalamiento",
         """
         Reasigno este work item a {{persona}} el {{fecha}}.

         Motivo: {{motivo}}
         Qué ya está hecho: {{avance}}
         Qué falta: {{pendiente}}
         Dónde quedó el código: {{rama o pr}}
         Contexto que hay que conocer: {{contexto}}
         """),

        // ── Documentos de estimación ─────────────────────────────────────────────
        (TemplateKind.DocumentoEstimacion,
         "Estimación — Documento de entrega",
         "Esqueleto del documento con el que se entrega una estimación. Adjunta aquí el .docx del área si ya existe.",
         "estimacion, entrega, documento",
         """
         # Estimación — {{sistema}} / {{requerimiento}}

         **Folio:** {{folio}}
         **Elaborada por:** {{usuario}}
         **Fecha:** {{fecha}}
         **Vigencia de la estimación:** 30 días naturales

         ## 1. Qué se va a hacer
         {{descripcion del alcance}}

         ## 2. Qué NO incluye
         - {{fuera de alcance 1}}
         - {{fuera de alcance 2}}

         ## 3. Desglose

         | # | Actividad | Horas | Perfil |
         |---|-----------|------:|--------|
         | 1 | {{actividad 1}} | {{h1}} | {{perfil}} |
         | 2 | {{actividad 2}} | {{h2}} | {{perfil}} |
         | 3 | Pruebas y correcciones | {{h3}} | QA |
         | 4 | Liberación y documentación | {{h4}} | {{perfil}} |
         | | **Total** | **{{horas totales}}** | |

         ## 4. Supuestos
         - {{supuesto 1}}
         - {{supuesto 2}}

         ## 5. Dependencias del solicitante
         - {{dependencia 1}}

         ## 6. Riesgos
         | Riesgo | Impacto | Mitigación |
         |--------|---------|------------|
         | {{riesgo}} | {{impacto}} | {{mitigacion}} |

         ## 7. Plan de entrega
         - Inicio: {{fecha inicio}}
         - Entrega a QA: {{fecha qa}}
         - Liberación: {{fecha liberacion}}

         > Cualquier cambio al alcance descrito en el punto 1 obliga a re-estimar.
         """),

        (TemplateKind.DocumentoEstimacion,
         "Estimación — Resumen ejecutivo (correo)",
         "Versión corta, para el correo que acompaña al documento.",
         "estimacion, correo, resumen",
         """
         Asunto: Estimación {{folio}} — {{sistema}}

         Buen día:

         Adjunto la estimación del requerimiento {{requerimiento}}.

         - Esfuerzo estimado: {{horas totales}} horas
         - Fecha de entrega propuesta: {{fecha liberacion}} (arrancando el {{fecha inicio}})
         - Vigencia de la estimación: 30 días naturales

         Los supuestos y lo que queda fuera de alcance están en el documento; conviene
         revisarlos antes de autorizar, porque cualquier cambio obliga a re-estimar.

         Quedo pendiente de su confirmación para agendarlo.

         Saludos,
         {{usuario}}
         """),

        // ── Scripts SQL ──────────────────────────────────────────────────────────
        (TemplateKind.ScriptSql,
         "SQL — Quién está bloqueando a quién",
         "Cadena de bloqueos en SQL Server. Solo lectura: no mata sesiones.",
         "sql server, bloqueos, diagnostico",
         """
         -- Bloqueos activos: quién espera y quién lo está deteniendo.
         -- Solo consulta. Antes de matar cualquier sesión, avisa al dueño del proceso.
         SELECT  s.session_id            AS Sesion,
                 s.login_name            AS Usuario,
                 s.host_name             AS Equipo,
                 s.program_name          AS Programa,
                 r.blocking_session_id   AS BloqueadaPor,
                 r.wait_type             AS TipoEspera,
                 r.wait_time / 1000.0    AS SegundosEsperando,
                 DB_NAME(r.database_id)  AS BaseDeDatos,
                 t.text                  AS Consulta
         FROM        sys.dm_exec_sessions  s
         INNER JOIN  sys.dm_exec_requests  r  ON r.session_id = s.session_id
         CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
         WHERE   r.blocking_session_id <> 0
         ORDER BY r.wait_time DESC;
         """),

        (TemplateKind.ScriptSql,
         "SQL — Tamaño de las tablas",
         "Qué tablas se están comiendo la base. Útil antes de pedir más espacio.",
         "sql server, espacio, tablas",
         """
         -- Espacio por tabla, de mayor a menor.
         SELECT  s.name                                   AS Esquema,
                 t.name                                   AS Tabla,
                 p.rows                                   AS Filas,
                 CAST(SUM(a.total_pages) * 8.0 / 1024 AS DECIMAL(18,2)) AS TotalMB,
                 CAST(SUM(a.used_pages)  * 8.0 / 1024 AS DECIMAL(18,2)) AS UsadoMB
         FROM       sys.tables       t
         INNER JOIN sys.schemas      s  ON s.schema_id = t.schema_id
         INNER JOIN sys.indexes      i  ON i.object_id = t.object_id
         INNER JOIN sys.partitions   p  ON p.object_id = i.object_id AND p.index_id = i.index_id
         INNER JOIN sys.allocation_units a ON a.container_id = p.partition_id
         WHERE   i.index_id <= 1
         GROUP BY s.name, t.name, p.rows
         ORDER BY TotalMB DESC;
         """),

        (TemplateKind.ScriptSql,
         "SQL — Buscar un texto en procedimientos y vistas",
         "Dónde se usa una columna o una tabla antes de cambiarla.",
         "sql server, buscar, impacto",
         """
         -- ¿En qué objetos aparece {{texto}}? Sirve para medir el impacto de un cambio.
         DECLARE @buscar NVARCHAR(200) = N'{{texto}}';

         SELECT  o.type_desc      AS Tipo,
                 SCHEMA_NAME(o.schema_id) + '.' + o.name AS Objeto,
                 o.modify_date    AS UltimaModificacion
         FROM       sys.sql_modules m
         INNER JOIN sys.objects     o ON o.object_id = m.object_id
         WHERE   m.definition LIKE '%' + @buscar + '%'
         ORDER BY Tipo, Objeto;
         """),

        (TemplateKind.ScriptSql,
         "SQL — Respaldo previo de una tabla antes de tocarla",
         "Copia de seguridad rápida antes de un UPDATE o DELETE masivo.",
         "sql server, respaldo, seguridad",
         """
         -- Antes de cualquier UPDATE o DELETE masivo: copia de la tabla con sello de fecha.
         -- Cambia {{esquema}}, {{tabla}} y {{sello}} (por ejemplo 20260730).
         SELECT * INTO [{{esquema}}].[{{tabla}}_bkp_{{sello}}]
         FROM   [{{esquema}}].[{{tabla}}];

         SELECT COUNT(*) AS Original FROM [{{esquema}}].[{{tabla}}];
         SELECT COUNT(*) AS Respaldo FROM [{{esquema}}].[{{tabla}}_bkp_{{sello}}];

         -- Y siempre dentro de una transacción explícita:
         -- BEGIN TRAN;
         --   UPDATE [{{esquema}}].[{{tabla}}] SET ... WHERE ...;
         --   -- revisa @@ROWCOUNT antes de confirmar
         -- ROLLBACK TRAN;   -- COMMIT TRAN cuando el número cuadre
         """),

        // ── Scripts PowerShell ───────────────────────────────────────────────────
        (TemplateKind.ScriptPowerShell,
         "PowerShell — Espacio libre en los discos de un servidor",
         "Revisión rápida antes de un despliegue. Windows PowerShell 5.1.",
         "powershell, servidor, disco",
         """
         # Espacio libre en los discos de un servidor remoto.
         # Requiere permisos de administrador sobre el equipo destino.
         $Servidor = '{{servidor}}'

         Get-CimInstance -ClassName Win32_LogicalDisk -ComputerName $Servidor -Filter 'DriveType = 3' |
             Select-Object DeviceID,
                 @{ Name = 'TotalGB';   Expression = { [math]::Round($_.Size / 1GB, 2) } },
                 @{ Name = 'LibreGB';   Expression = { [math]::Round($_.FreeSpace / 1GB, 2) } },
                 @{ Name = 'Libre%';    Expression = { [math]::Round(($_.FreeSpace / $_.Size) * 100, 1) } } |
             Sort-Object 'Libre%' |
             Format-Table -AutoSize
         """),

        (TemplateKind.ScriptPowerShell,
         "PowerShell — Respaldo de una carpeta con sello de fecha",
         "Copia con marca de tiempo antes de sobrescribir un sitio publicado.",
         "powershell, respaldo, despliegue",
         """
         # Copia una carpeta a un destino con la fecha en el nombre, antes de sobrescribirla.
         $Origen  = '{{ruta origen}}'
         $Destino = '{{ruta destino}}'

         if (-not (Test-Path $Origen)) { throw "No existe la carpeta origen: $Origen" }

         $Sello   = Get-Date -Format 'yyyyMMdd-HHmmss'
         $Carpeta = Join-Path $Destino ("{0}_{1}" -f (Split-Path $Origen -Leaf), $Sello)

         New-Item -ItemType Directory -Path $Carpeta -Force | Out-Null
         Copy-Item -Path (Join-Path $Origen '*') -Destination $Carpeta -Recurse -Force

         $Cuantos = (Get-ChildItem $Carpeta -Recurse -File | Measure-Object).Count
         Write-Output "Respaldo en $Carpeta ($Cuantos archivos)."
         """),

        (TemplateKind.ScriptPowerShell,
         "PowerShell — Estado de un sitio y su grupo de aplicaciones (IIS)",
         "Comprobación después de desplegar. Se ejecuta en el propio servidor.",
         "powershell, iis, despliegue",
         """
         # Estado de un sitio de IIS y de su grupo de aplicaciones. Ejecutar EN el servidor.
         Import-Module WebAdministration

         $Sitio = '{{sitio}}'

         $s = Get-Website -Name $Sitio
         if ($null -eq $s) { throw "No existe el sitio '$Sitio' en este servidor." }

         $pool = Get-Item ("IIS:\AppPools\" + $s.applicationPool)

         [pscustomobject]@{
             Sitio        = $s.Name
             EstadoSitio  = $s.State
             RutaFisica   = $s.physicalPath
             AppPool      = $s.applicationPool
             EstadoPool   = $pool.State
             DotNetPool   = $pool.managedRuntimeVersion
         } | Format-List

         # Para reciclar el pool después de publicar:
         # Restart-WebAppPool -Name $s.applicationPool
         """),

        // ── Scripts Bash ─────────────────────────────────────────────────────────
        (TemplateKind.ScriptBash,
         "Bash — Espacio en disco y carpetas más pesadas",
         "Diagnóstico de un servidor Linux que se quedó sin espacio.",
         "bash, linux, disco",
         """
         #!/usr/bin/env bash
         # Qué está ocupando el disco. Solo lectura.
         set -euo pipefail

         RUTA="${1:-/var}"

         echo "== Espacio por sistema de archivos =="
         df -h --output=source,size,used,avail,pcent,target | sort -k5 -hr

         echo
         echo "== 20 carpetas más pesadas dentro de $RUTA =="
         du -h --max-depth=2 "$RUTA" 2>/dev/null | sort -hr | head -20

         echo
         echo "== 20 archivos más grandes dentro de $RUTA =="
         find "$RUTA" -type f -printf '%s\t%p\n' 2>/dev/null | sort -nr | head -20 |
             awk -F'\t' '{ printf "%8.1f MB  %s\n", $1/1048576, $2 }'
         """),

        (TemplateKind.ScriptBash,
         "Bash — Respaldo con rotación de una carpeta",
         "Copia comprimida con fecha y borrado de las más viejas.",
         "bash, linux, respaldo",
         """
         #!/usr/bin/env bash
         # Respaldo comprimido de una carpeta, conservando solo los últimos N.
         set -euo pipefail

         ORIGEN="{{ruta origen}}"
         DESTINO="{{ruta destino}}"
         CONSERVAR={{cuantos}}

         [ -d "$ORIGEN" ]  || { echo "No existe el origen: $ORIGEN" >&2; exit 1; }
         mkdir -p "$DESTINO"

         SELLO="$(date +%Y%m%d-%H%M%S)"
         ARCHIVO="$DESTINO/$(basename "$ORIGEN")-$SELLO.tar.gz"

         tar -czf "$ARCHIVO" -C "$(dirname "$ORIGEN")" "$(basename "$ORIGEN")"
         echo "Respaldo: $ARCHIVO ($(du -h "$ARCHIVO" | cut -f1))"

         # Rotación: se borran los respaldos más viejos que los últimos $CONSERVAR.
         ls -1t "$DESTINO"/"$(basename "$ORIGEN")"-*.tar.gz 2>/dev/null |
             tail -n +$((CONSERVAR + 1)) |
             xargs -r rm -v --
         """),
    ];
}
