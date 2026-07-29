# Desglose granular — Fases 4 y 5

Cada tarea de abajo es **autocontenida**: puede retomarse de forma aislada (incluso por otro
modelo sin el historial de la conversación). Incluye objetivo, contexto del código actual,
pasos y criterio de aceptación. Hazlas en orden dentro de cada fase salvo que se indique lo
contrario. **Compila después de cada tarea** (`dotnet build`) y no avances si hay errores.

---

## Orientación rápida del proyecto (leer una vez)

- **Tipo**: .NET 10 Windows Forms, C# con `Nullable` e `ImplicitUsings` activados.
- **Raíz del proyecto**: `c:\Users\gerar\source\repos\Administrador_Desarrollo_Web\Administrador_Desarrollo_Web\`
- **Solución/NuGet**: hay un `NuGet.Config` local (en la carpeta padre) que fuerza el origen a
  `nuget.org` únicamente (el feed corporativo "SOLTUM" da 401 para paquetes públicos). Si
  agregas paquetes y `dotnet restore` falla con 401, es por eso: usa `dotnet restore --force`.
- **Base de datos**: SQLite en `%AppData%\AdministradorDesarrolloWeb\app.db`.
  - `Data/AppDbContext.cs`: `DbContext` con todos los `DbSet<>`. Enums mapeados con
    `.HasConversion<int>()`. Relaciones configuradas en `OnModelCreating`.
  - `Data/DatabaseMigrator.cs`: `EnsureUpToDate(db)` llama `db.Database.EnsureCreated()` y luego
    ejecuta `CREATE TABLE IF NOT EXISTS` (SQL **específico de SQLite**: `AUTOINCREMENT`, `TEXT`,
    `INTEGER`) para añadir tablas nuevas a BDs existentes sin perder datos. Se llama en `Program.cs`.
- **DI**: `Program.cs` arma un `ServiceCollection`. Servicios = singletons; `AppDbContext` =
  singleton (`ServiceLifetime.Singleton` en `AddDbContext`); Forms/UserControls = transient.
  Arranca con `LoginForm`. También: `SeedAdmin()` (usuario `admin`/`Admin@123`, fuerza cambio)
  y `SeedDefaultCriteria()`.
- **Seguridad**:
  - `Security/PasswordHasher.cs`: BCrypt (`Hash`/`Verify`).
  - `Security/SecretProtector.cs`: DPAPI (`Protect`/`Unprotect`/`TryUnprotect`), scope CurrentUser.
    **Úsalo para cifrar cualquier secreto** (PAT, connection strings) antes de guardarlo en BD.
- **Configuración**: `Services/SettingsService.cs` — `Get(key)`, `Set(key, value, isSecret, desc)`.
  Tiene `SettingsService.Keys` con constantes (ya existe `AzureSqlConnectionString`,
  `AzureBlobConnectionString`, `AzureBlobContainer`, `DefaultDeployFolder`). Los valores con
  `isSecret:true` se guardan cifrados con DPAPI automáticamente.
- **Sesión y roles**: `Services/CurrentUserContext.cs` (`User`, `IsLoggedIn`, `IsAdmin`).
  `Models/User.cs`: enum `UserRole { Admin=0, Operaciones=1 }`, campos `DeveloperId` (FK opcional
  a `Developer`), `MustChangePassword`. `Services/AuthService.cs`: Login, CreateUser, UpdateUser,
  ResetPassword, ChangePassword, GetAllUsers, SeedAdmin.
- **Auditoría**: `Services/AuditService.cs` — `Record(AuditAction, entityType, entityId, details)`.
  Enum `AuditAction` en `Models/AuditLog.cs`.
- **UI / convenciones** (IMPORTANTE para que la interfaz no se rompa):
  - `Forms/AppTheme.cs`: paleta, fuentes y *factories*: `MakePrimaryButton/MakeSecondaryButton/
    MakeDangerButton`, `MakeGrid()` (DataGridView ya estilizado), `StatusColor(...)`.
  - **Layout SIEMPRE con `TableLayoutPanel`** como contenedor raíz (NO `Dock=Top/Fill` mezclados
    sueltos: provoca controles encimados). Patrón típico de un UserControl: tabla raíz de filas
    `[header 55px][toolbar ~52px][grid 100%]`. Toolbar de 2 columnas: filtros (izq, `Percent 100`)
    + botones (der, ancho `Absolute`). Ver `Forms/Controls/RequirementsControl.cs` como referencia.
  - Cada UserControl recibe dependencias por **constructor** (DI) y se registra en `Program.cs`.
  - `MainForm.cs`: sidebar con `AddNav(icono, etiqueta, key)`, `switch` en `Navigate(key)` que
    resuelve el UserControl por `key` y otro `switch` para el título. La navegación se filtra por
    rol (`_currentUser.IsAdmin`). Para agregar un módulo: registra en DI, agrega `AddNav`, agrega
    los dos `case` del switch.
  - Patrón de **grilla CRUD**: `LoadData()` llena la grid; selección por `CurrentRow.Cells["Id"].Value
    is int id`; botones Nuevo/Editar/Eliminar abren un form de `Forms/Details/*`; tras guardar,
    `_db.SaveChanges()` + `_audit.Record(...)` + recargar. `OnVisibleChanged` recarga al mostrarse.
  - Tareas largas con **`IProgress<string>`** y `async`/`await`; para diálogos de progreso existe
    `Forms/Details/ProgressDialog.cs` (con `Token` de cancelación, `Append`, `MarkDone`).
  - Exportación a Excel: `Services/ReportService.cs` — `PromptSaveDialog(nombre)` +
    `ExportToExcel(datos, headers[], row => object?[], hoja, ruta)`.
- **Despliegues (Fase 3, ya hecha)**: `Forms/Controls/DeploymentControl*.cs` es una **clase
  parcial** dividida por pestaña (`.Deploy`, `.Systems`, `.Servers`, `.Profiles`, `.History`,
  `.Backup`). Servicios: `DeploymentService`, `BlobStorageService`, `BackupService`.

---

# FASE 4 — Migración a Azure SQL Server + acceso multiusuario

**Meta de la fase**: poder usar **Azure SQL** como almacén compartido (en vez de SQLite local) y
habilitar el rol **Desarrollador** para que cada dev entre a ver sus asignaciones y pedir
vacaciones. Debe seguir funcionando 100% en modo SQLite local (el proveedor es elegible).

**Principio rector**: el código de acceso a datos ya es agnóstico (EF Core + LINQ, sin SQL crudo
salvo el migrador de SQLite). El cambio de proveedor debe ser **configuración**, no reescritura.

---

## F4.1 — Selección de proveedor de BD por configuración
**Objetivo**: que el motor (SQLite | SqlServer) y su connection string se lean de configuración,
sin cambiar todavía nada del esquema ni mover datos. SQLite sigue siendo el predeterminado.

**Contexto**:
- Hoy `Program.cs` registra: `services.AddDbContext<AppDbContext>(opts => opts.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Singleton);`
- `SettingsService.Keys.AzureSqlConnectionString` ya existe como constante.
- Problema del huevo y la gallina: el proveedor se decide **antes** de poder leer settings desde
  la propia BD. Solución: leer la preferencia de proveedor de un archivo simple junto al exe/AppData
  (`%AppData%\AdministradorDesarrolloWeb\dbprovider.json`) con `{ "Provider": "Sqlite|SqlServer",
  "SqlServerConnection": "<cifrada DPAPI>" }`. No uses `SettingsService` para esto (vive dentro de
  la BD). Crea un helper `Data/DbProviderConfig.cs` (Read/Write, descifra con `SecretProtector`).

**Pasos**:
1. Agregar paquete `Microsoft.EntityFrameworkCore.SqlServer` (versión 9.0.*) al `.csproj`.
   `dotnet restore --force`.
2. Crear `Data/DbProviderConfig.cs`: clase con `Provider` (enum `DbProvider { Sqlite, SqlServer }`)
   y `SqlServerConnection` (string en claro en memoria). Métodos estáticos `Load()` (lee el json de
   AppData; si no existe, devuelve `Sqlite`) y `Save(cfg)` (cifra la cadena con `SecretProtector`).
3. En `Program.cs`, antes de `AddDbContext`, hacer `var dbCfg = DbProviderConfig.Load();` y
   registrar el proveedor según `dbCfg.Provider`:
   - Sqlite: como hoy.
   - SqlServer: `opts.UseSqlServer(dbCfg.SqlServerConnection)`.

**Aceptación**: con el json ausente o `Provider=Sqlite`, la app arranca igual que hoy. Si se pone
`Provider=SqlServer` con una cadena válida a una BD vacía, la app inicia sin excepción de proveedor
(la creación de esquema es F4.2; puede fallar al crear tablas todavía — eso se resuelve en F4.2).

---

## F4.2 — Creación de esquema en SQL Server
**Objetivo**: que al apuntar a una Azure SQL vacía se creen todas las tablas correctamente.

**Contexto**: `DatabaseMigrator.EnsureUpToDate` usa SQL **específico de SQLite** (`AUTOINCREMENT`,
tipos `TEXT`/`INTEGER`), que NO es válido en SQL Server. Pero `db.Database.EnsureCreated()` de EF
genera el esquema correcto para CUALQUIER proveedor a partir del modelo (`OnModelCreating`).

**Pasos**:
1. En `DatabaseMigrator.EnsureUpToDate(AppDbContext db)`, detectar el proveedor:
   `bool isSqlite = db.Database.IsSqlite();` (extensión de `Microsoft.EntityFrameworkCore`).
2. Si NO es SQLite: ejecutar solo `db.Database.EnsureCreated()` y **return** (EF crea todo el
   esquema con tipos T-SQL correctos; no ejecutar los `CREATE TABLE IF NOT EXISTS` de SQLite).
3. Si es SQLite: dejar el comportamiento actual (EnsureCreated + los CREATE TABLE IF NOT EXISTS
   idempotentes para upgrades de instalaciones existentes).

**Aceptación**: apuntando a una Azure SQL vacía (F4.1 con Provider=SqlServer), al arrancar se
crean todas las tablas (verificar con SSMS/Azure Data Studio: Users, Developers, Requirements,
Assignments, Minutes, …, DeploymentJobs, etc.) y el `SeedAdmin()` inserta el admin.

---

## F4.3 — Utilidad de migración de datos SQLite → SQL Server (una sola vez)
**Objetivo**: copiar los datos existentes de la BD SQLite local a la Azure SQL ya creada.

**Contexto**: es una operación administrativa puntual. Lo más robusto es leer cada entidad del
contexto SQLite y escribirla en el contexto SqlServer respetando los IDs (para no romper FKs).
En SQL Server hay que habilitar `IDENTITY_INSERT` por tabla al insertar IDs explícitos.

**Pasos**:
1. Crear `Services/DataMigrationService.cs` con un método
   `Task MigrateSqliteToSqlServerAsync(string sqlitePath, string sqlServerConn, IProgress<string> p, CancellationToken ct)`.
2. Construir dos `AppDbContext` manualmente con `DbContextOptionsBuilder<AppDbContext>` (uno
   `UseSqlite`, otro `UseSqlServer`). En el destino, `EnsureCreated()`.
3. Copiar en **orden de dependencias** (padres antes que hijos): Users, Developers, AppSettings,
   AuditLogs, Requirements, Assignments, Minutes, MinuteActionItems, VacationRequests, Notes,
   ScoringCriteria, PointEntries, AppSystems, AppReleases, DeploymentTargets, DeploymentProfiles,
   DeploymentProfileTargets, DeploymentJobs, DeploymentLogEntries.
4. Para cada tabla con IDs explícitos: `SET IDENTITY_INSERT [Tabla] ON` (vía
   `dest.Database.ExecuteSqlRaw`), `AddRange` + `SaveChanges`, luego `OFF`. Reportar conteos por
   tabla vía `IProgress`.
5. Exponerla en `ConfigurationControl` (o un mini-form admin) con un botón
   "Migrar datos locales → Azure SQL" usando `ProgressDialog`.

**Aceptación**: tras migrar, los conteos por tabla en origen y destino coinciden; al cambiar el
proveedor a SqlServer y reabrir, se ven los mismos datos que en local. Documentar que es de un
solo uso (idealmente bloquear si el destino ya tiene datos en Users > 0, pidiendo confirmación).

---

## F4.4 — Rol Desarrollador (modelo + gestión de usuarios)
**Objetivo**: agregar el rol `Desarrollador` y poder crear usuarios de ese rol vinculados a un
`Developer`.

**Contexto**: `Models/User.cs` tiene `enum UserRole { Admin=0, Operaciones=1 }` y `DeveloperId`.
`Forms/Details/UserDetailForm.cs` arma el alta/edición (combo de rol con "Admin"/"Operaciones").
`Forms/Controls/UserManagementControl.cs` lista y opera usuarios.

**Pasos**:
1. Agregar `Desarrollador = 2` al enum `UserRole`.
2. En `UserDetailForm`: agregar "Desarrollador" al combo de rol; cuando el rol sea Desarrollador,
   mostrar un combo de `Developer` (activos) para setear `User.DeveloperId` (obligatorio en ese
   caso). Cargar los devs vía un `AppDbContext` recibido por constructor (ajustar la firma y el
   `new UserDetailForm(...)` en `UserManagementControl`).
3. En `AuthService.CreateUser/UpdateUser`: ya persiste `Role` y `DeveloperId`; validar que si
   `Role==Desarrollador` entonces `DeveloperId != null`.
4. Mostrar el rol "Desarrollador" en la columna Rol de `UserManagementControl`.

**Aceptación**: se puede crear un usuario rol Desarrollador ligado a un `Developer`; al guardarlo
queda con `DeveloperId` correcto (verificar en BD). No rompe la creación de Admin/Operaciones.

---

## F4.5 — Vistas self-service para el rol Desarrollador
**Objetivo**: cuando inicia sesión un usuario rol Desarrollador, ve SOLO lo suyo: sus
asignaciones/requerimientos y puede crear solicitudes de vacaciones.

**Contexto**: `MainForm.cs` filtra la navegación por rol (hoy: Admin ve todo; Operaciones ve
Dashboard + Despliegues). `CurrentUserContext.User.DeveloperId` identifica al dev logueado.
Reutiliza `AppTheme.MakeGrid()` y el patrón de UserControl.

**Pasos**:
1. Crear `Forms/Controls/MyAssignmentsControl.cs`: grid (solo lectura) con los `Requirement`
   donde exista un `Assignment` para `CurrentUserContext.User.DeveloperId` (estado, prioridad,
   fechas, % avance). Filtro por estado opcional.
2. Crear `Forms/Controls/MyVacationsControl.cs`: lista de las `VacationRequest` propias
   (filtradas por `DeveloperId`) + botón "Nueva solicitud" que reutiliza
   `Forms/Details/VacationRequestDetailForm.cs` pero **fijando el dev al propio** (no permitir
   elegir otro). Mostrar estado (Pendiente/Aprobada/…). No permitir editar una ya resuelta.
3. Registrar ambos en `Program.cs` (transient).
4. En `MainForm.cs`: agregar una rama `else if (_currentUser.User.Role == UserRole.Desarrollador)`
   en la construcción del menú que muestre Dashboard (opcional), "Mis asignaciones" y
   "Mis vacaciones"; añadir los `case` en `Navigate` y en el switch de títulos.
5. Asegurar que el Dashboard, si se muestra a un dev, no exponga datos sensibles (o no mostrarlo).

**Aceptación**: con un login rol Desarrollador, el menú solo muestra sus módulos; "Mis
asignaciones" lista únicamente requerimientos asignados a ese dev; puede crear una solicitud de
vacaciones que aparece como Pendiente para que el Admin la apruebe en su módulo de Vacaciones.

---

## F4.6 — Concurrencia optimista (multiusuario)
**Objetivo**: evitar que dos usuarios pisen cambios del otro en la BD compartida.

**Contexto**: con varios usuarios concurrentes en Azure SQL hace falta control de concurrencia.
EF Core lo soporta con una columna de versión de fila.

**Pasos**:
1. Agregar a las entidades mutables de uso compartido (al menos `Requirement`, `Assignment`,
   `VacationRequest`, `DeploymentTarget`, `DeploymentProfile`) una propiedad
   `public byte[]? RowVersion { get; set; }` y en `OnModelCreating` marcarla
   `.Property(e => e.RowVersion).IsRowVersion();`.
2. En SQLite esto se mapea distinto (no hay `rowversion`); para mantener compatibilidad, considerar
   aplicar `IsRowVersion` solo cuando el proveedor sea SqlServer (condicional en `OnModelCreating`
   usando `Database.IsSqlServer()`), o usar un `concurrency token` portable. Documentar la decisión.
3. Capturar `DbUpdateConcurrencyException` en los `SaveChanges` de los controles de edición y
   mostrar un mensaje "Otro usuario modificó este registro; recarga e intenta de nuevo".
4. Actualizar `DatabaseMigrator` (rama SQLite) si se agregan columnas a tablas existentes
   (`ALTER TABLE ... ADD COLUMN` idempotente con verificación).

**Aceptación**: al editar el mismo registro desde dos instancias contra Azure SQL, la segunda en
guardar recibe el aviso de conflicto en lugar de sobreescribir silenciosamente.

---

## F4.7 — UI de configuración del proveedor + probar conexión
**Objetivo**: poder elegir el proveedor y la cadena de Azure SQL desde la app, con prueba de
conexión, sin editar archivos a mano.

**Contexto**: `Forms/Controls/ConfigurationControl.cs` ya tiene la pantalla de Configuración (usa
el patrón de `TableLayoutPanel` + `AddSection`/`AddField`). `DbProviderConfig` (F4.1) persiste la
preferencia. El cambio de proveedor requiere **reiniciar** la app (el `AppDbContext` se arma en el
arranque).

**Pasos**:
1. En `ConfigurationControl`, agregar sección "Base de datos": combo Proveedor (SQLite local /
   Azure SQL) y campo de connection string de Azure SQL (cifrado, `UseSystemPasswordChar`).
2. Botón "Probar conexión": abre un `AppDbContext` temporal con `UseSqlServer(cadena)` y llama
   `Database.CanConnect()`; mostrar ✓/✗.
3. Botón "Guardar": persistir vía `DbProviderConfig.Save(...)` y avisar "Reinicia la aplicación
   para aplicar el cambio de proveedor".
4. (Opcional) Enlazar aquí el botón de migración de datos (F4.3).

**Aceptación**: desde la UI se puede probar una cadena de Azure SQL (✓/✗), guardarla cifrada, y
tras reiniciar la app trabaja contra Azure SQL.

---

# FASE 5 — Integración opcional con Azure DevOps

**Meta de la fase**: si el Admin lo configura, importar work items de Azure DevOps (bugs, tareas,
historias) como `Requirement`. **Totalmente opcional**: con la integración apagada, la app
funciona idéntico. Es puramente aditiva.

**Contexto del modelo ya existente**: `Models/Requirement.cs` YA tiene los campos
`Source` (enum `RequirementSource { Manual=0, AzureDevOps=1 }`), `ExternalId` y `ExternalUrl`.
No hace falta tocar el modelo de requerimientos.

---

## F5.1 — Configuración de la integración (settings + UI)
**Objetivo**: guardar Organización, Proyecto, PAT (cifrado) y un switch de habilitado.

**Contexto**: `SettingsService` con `Keys`. Secretos cifrados con `isSecret:true` (DPAPI).
`ConfigurationControl` para la UI (patrón `AddSection`/`AddField`).

**Pasos**:
1. Agregar claves a `SettingsService.Keys`: `AzureDevOpsOrgUrl`, `AzureDevOpsProject`,
   `AzureDevOpsPat`, `AzureDevOpsEnabled`.
2. En `ConfigurationControl`: sección "Azure DevOps" con: URL de organización
   (`https://dev.azure.com/<org>`), Proyecto, PAT (cifrado, `UseSystemPasswordChar`), y un
   checkbox "Habilitar integración". Guardar con `SettingsService.Set(...)` (PAT con
   `isSecret:true`).
3. Helper para leer el estado: `bool habilitado = settings.Get(Keys.AzureDevOpsEnabled) == "true"
   && !string.IsNullOrEmpty(settings.Get(Keys.AzureDevOpsPat))`.

**Aceptación**: se guarda la config; el PAT queda cifrado en la tabla `AppSettings` (no legible en
claro); el switch persiste.

---

## F5.2 — AzureDevOpsService (cliente REST/WIQL)
**Objetivo**: servicio que consulta work items usando el PAT, sin tocar la UI todavía.

**Contexto**: la API REST de Azure DevOps usa auth Basic con el PAT (usuario vacío, password=PAT,
en Base64 `:{pat}`). Endpoints: WIQL para obtener IDs
(`POST {org}/{project}/_apis/wit/wiql?api-version=7.0`) y luego
`GET {org}/_apis/wit/workitems?ids=...&fields=...&api-version=7.0` para los campos. Usar
`HttpClient` directo (sin paquete extra) o el SDK `Microsoft.TeamFoundationServer.Client`
(más pesado). **Preferir `HttpClient`** para no añadir dependencias grandes.

**Pasos**:
1. Crear `Services/AzureDevOpsService.cs` (recibe `SettingsService` por constructor).
2. Propiedad `IsEnabled` (según F5.1).
3. Método `Task<List<DevOpsWorkItem>> QueryWorkItemsAsync(string? wiql = null, CancellationToken ct)`:
   arma `HttpClient` con header `Authorization: Basic base64(":"+pat)`, ejecuta WIQL (por defecto
   "work items del proyecto no cerrados"), obtiene IDs, pide los campos
   (`System.Id, System.Title, System.WorkItemType, System.State, System.Description`), y mapea a un
   `record DevOpsWorkItem(int Id, string Title, string Type, string State, string? Description, string Url)`.
4. Manejo de errores claro (401 → "PAT inválido o sin permisos"; 404 → "Organización/Proyecto no
   encontrado").

**Aceptación**: con config válida, `QueryWorkItemsAsync` devuelve una lista de work items del
proyecto. (Probar con una org/proyecto reales o documentar cómo probar.)

---

## F5.3 — Mapeo e importación a Requirement
**Objetivo**: convertir work items en `Requirement` (upsert por `ExternalId`).

**Contexto**: `Requirement` tiene `Source`, `ExternalId`, `ExternalUrl`, `Status`
(`RequirementStatus { PorEstimar, Estimado, EnDesarrollo, EnPruebas, PorEntregar, Entregado,
Cancelado }`). `AuditService` para registrar la importación.

**Pasos**:
1. En `AzureDevOpsService` (o un `DevOpsImportService`), método
   `(int added, int updated) ImportAsRequirements(List<DevOpsWorkItem> items)`.
2. Mapeo de estado Azure DevOps → `RequirementStatus` (tabla simple y tolerante; default
   `PorEstimar`). Ej.: New/To Do→PorEstimar; Approved/Committed→Estimado;
   Active/Doing/In Progress→EnDesarrollo; Testing→EnPruebas; Resolved→PorEntregar;
   Closed/Done→Entregado; Removed→Cancelado.
3. Upsert por `ExternalId` (== `Id` del work item, como string): si existe, actualizar
   Title/Description/Status; si no, crear con `Source=AzureDevOps`, `ExternalUrl` = URL del item.
   No sobreescribir campos editados localmente que DevOps no controla (fechas, % avance, asignación).
4. `_db.SaveChanges()` + `_audit.Record(AuditAction.Create, "Requirement", null, "Import DevOps: X nuevos, Y actualizados")`.

**Aceptación**: importar crea/actualiza requerimientos con `Source=AzureDevOps` y el enlace al
ticket; reimportar no duplica (actualiza por `ExternalId`).

---

## F5.4 — UI de sincronización en Requerimientos
**Objetivo**: botón para importar desde la pantalla de Requerimientos y abrir el ticket en el
navegador, visible solo si la integración está habilitada.

**Contexto**: `Forms/Controls/RequirementsControl.cs` (toolbar con botones; patrón de grid CRUD).
Abrir URL: `Process.Start(new ProcessStartInfo(url){ UseShellExecute = true })` (ver
`DeploymentControl.Servers.cs`, método `SrvOpenUrl_Click`).

**Pasos**:
1. Inyectar `AzureDevOpsService` en `RequirementsControl` (ajustar constructor + registro DI en
   `Program.cs`).
2. Agregar botón "🔄 Importar de Azure DevOps" en la toolbar, **habilitado/visible solo si**
   `AzureDevOpsService.IsEnabled`. Al pulsarlo: `QueryWorkItemsAsync` (con `ProgressDialog` o
   cursor de espera) → `ImportAsRequirements` → recargar grid + `MessageBox` con el resumen.
3. Agregar botón/acción "Abrir en Azure DevOps" que, si el requerimiento seleccionado tiene
   `ExternalUrl`, lo abre en el navegador. Mostrar en el grid un indicador de origen (columna
   "Origen": Manual / DevOps).

**Aceptación**: con integración apagada el botón no aparece (o está deshabilitado); con
integración encendida, importa y la grid muestra los requerimientos con origen DevOps; "Abrir en
Azure DevOps" abre el ticket correcto en el navegador.

---

## Notas finales para quien retome
- **Compilar siempre** tras cada tarea: `dotnet build` desde la raíz del proyecto. Cero errores
  antes de avanzar.
- **No romper el modo SQLite local**: todo lo de Fase 4 debe ser aditivo y elegible; el flujo
  por defecto (SQLite, sin Azure DevOps) debe seguir funcionando igual.
- **Reusar, no reinventar**: `AppTheme` (UI), `SettingsService`+`SecretProtector` (config/secretos),
  `AuditService` (bitácora), `ReportService` (Excel), `ProgressDialog` (tareas largas), patrón de
  clase parcial por pestaña (ver `DeploymentControl*`).
- **Verificación de extremo a extremo** sugerida al cerrar cada fase: arrancar la app, ejercitar
  el flujo nuevo, y confirmar persistencia reabriendo; revisar la **bitácora** y los **logs** en
  `%AppData%\AdministradorDesarrolloWeb\`.
