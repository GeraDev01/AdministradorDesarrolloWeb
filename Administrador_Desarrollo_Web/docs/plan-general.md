# Administrador de Desarrollo Web — Plan de implementación

## Context
El usuario lidera un departamento de desarrollo y necesita una herramienta de escritorio
para: dar seguimiento a lo asignado a sus desarrolladores, generar minutas de sesiones/dailys,
registrar solicitudes de vacaciones y pendientes que le comentan, controlar requerimientos
(por estimar / desarrollar / entregar, fechas y avances) y administrar despliegues
(comprimir carpeta → ZIP → Azure Blob → desplegar a varios FTP con feedback en tiempo real),
con control de usuarios (Admin y Operaciones). Base de datos **SQLite** con copia en la nube.

Hoy existe solo el andamiaje vacío de un proyecto **.NET 10 Windows Forms**
([Form1.cs](Form1.cs), [Program.cs](Program.cs)). Partimos de cero sobre WinForms.

**Decisiones confirmadas con el usuario:**
- Nube: **Azure Blob Storage** (artefactos de despliegue + respaldo de la BD).
- Artefacto de despliegue: la app **comprime una carpeta** que el usuario señala.
- Usuarios/roles: **Admin** (acceso total) + **Operaciones** (ejecutar despliegues delegados).
- Entrega **por fases**. MVP = base común + módulo **Desarrolladores + Requerimientos**.

## Stack y dependencias (NuGet)
- `Microsoft.EntityFrameworkCore.Sqlite` (+ `Design`) — acceso a datos y migraciones.
  Se usa **EF Core de forma agnóstica del proveedor** (LINQ, sin SQL específico de SQLite)
  para que migrar a `Microsoft.EntityFrameworkCore.SqlServer` (Azure SQL) sea un cambio
  de proveedor + cadena de conexión, no una reescritura. (Ver Fase 4.)
- `Microsoft.Extensions.Hosting` / `DependencyInjection` — contenedor DI y configuración.
- `BCrypt.Net-Next` — hash de contraseñas.
- `FluentFTP` — despliegue FTP/FTPS con callbacks de progreso (fase de despliegues).
- `Azure.Storage.Blobs` — blob storage y respaldo en la nube (fase de despliegues/backup).
- `System.IO.Compression` (built-in) — generación de ZIP.
- `Serilog` + `Serilog.Extensions.Logging` + `Serilog.Sinks.File` — **logueo técnico** a
  archivo rotado en `%AppData%\AdministradorDesarrolloWeb\logs\` (errores, arranque,
  operaciones, fallos de despliegue), expuesto vía `ILogger<T>` por DI.
- `ClosedXML` — **exportación de reportes a Excel** (.xlsx) sin requerir Office instalado.
- `Microsoft.TeamFoundationServer.Client` (o REST API directa con PAT) — **integración
  opcional con Azure DevOps** para importar work items (bugs/tareas/historias). El PAT y la
  URL/proyecto se guardan cifrados en `AppSetting`; la integración está **desactivada por
  defecto** y solo se activa si el Admin la configura. (Ver Fase 5.)
- Secretos (contraseñas FTP, connection string de Azure): cifrados con **DPAPI**
  (`System.Security.Cryptography.ProtectedData`, scope CurrentUser).

## Arquitectura
Proyecto único con carpetas por capa:

```
/Data        AppDbContext, configuración EF, migraciones, seed
/Models      Entidades (POCO) + enums de estado
/Services    AuthService, CurrentUserContext, AuditService, SettingsService, ReportService(Excel), (futuro) DeploymentService, BlobStorageService, BackupService
/Security    Hash, DPAPI, chequeo de roles
/Forms       LoginForm, MainForm (shell) y UserControls por módulo
Program.cs   Bootstrap DI + migración + arranque del LoginForm
```

- **DI**: `Program.cs` construye un `IServiceProvider` (registra `AppDbContext`,
  servicios y forms) y resuelve `LoginForm` como punto de entrada.
- **BD**: archivo en `%AppData%\AdministradorDesarrolloWeb\app.db`. Se aplica
  `Database.Migrate()` al arrancar. La elección del proveedor (SQLite hoy / Azure SQL
  mañana) se centraliza en `AppDbContext.OnConfiguring` leyendo `appsettings.json`,
  de modo que un solo punto decide el motor.
- **Preparación multiusuario**: toda la persistencia pasa por servicios/`AppDbContext`
  (la UI nunca arma SQL), y `UserRole` se deja extensible para añadir **Desarrollador**.
  Esto permite que en la Fase 4 los desarrolladores y operaciones se conecten a una BD
  compartida en Azure SQL sin tocar las formas.
- **Shell**: tras login exitoso, `MainForm` muestra un menú lateral con módulos
  **filtrados por rol** (Operaciones solo ve Despliegues + Dashboard; Admin ve todo)
  y hospeda cada módulo como `UserControl` en un panel central.

## Modelo de datos
**Base (Fase 0):**
- `User`: Id, Username, PasswordHash, FullName, Role (`UserRole`: Admin|Operaciones,
  extensible a Desarrollador en Fase 4), `DeveloperId` (FK opcional para vincular un
  login con su ficha de desarrollador), IsActive, CreatedAt.
- `AuditLog` (bitácora): Id, Timestamp, UserId, UserName, Action (`AuditAction`:
  Login|Logout|Create|Update|Delete|Deploy|Backup…), EntityType, EntityId, Details (texto/JSON).
- `AppSetting` (configuración): clave/valor, con bandera `IsSecret` para cifrar el valor
  con DPAPI (p. ej. la **connection string de Azure** Blob/SQL, rutas por defecto).

**MVP (Fase 1):**
- `Developer`: Id, FullName, Email, Seniority, IsActive, Notes.
- `Requirement`: Id, Title, Description, Status (`RequirementStatus`:
  PorEstimar|Estimado|EnDesarrollo|EnPruebas|PorEntregar|Entregado|Cancelado),
  Priority, EstimateHours, RequestDate, CommittedDeliveryDate, ActualDeliveryDate,
  ProgressPercent, CreatedAt, **`Source`** (Manual|AzureDevOps) y **`ExternalId`/`ExternalUrl`**
  (opcionales, para enlazar el work item de Azure DevOps cuando provenga de la integración).
- `Assignment`: Id, RequirementId, DeveloperId, Role, AssignedAt (relación N:M dev↔requerimiento).

**Fases futuras (solo se diseñan los enums/tablas cuando se construyan):**
- `Minute` + `MinuteActionItem` (minutas de daily/sesión con responsables y pendientes).
- `VacationRequest` (DeveloperId, rango de fechas, Status, comentario).
- `Note` / `Reminder` (pendientes que comentan, con fecha y aviso).
- **Desempeño / gamificación**:
  - `ScoringCriterion`: catálogo de criterios de puntuación (Nombre, Descripción, Puntos
    por defecto —positivos o negativos—, IsActive). El Admin puede **crear/editar/desactivar
    criterios y sus puntuaciones**. Seed sugerido: corrección de bugs, entrega a tiempo,
    calidad/sin retrabajo, cero bugs en QA, ayuda a compañeros, documentación, cumplimiento
    del daily, mejora/innovación, y penalizaciones (entrega tardía, bug en producción).
  - `PointEntry`: asignación de puntos a un desarrollador (DeveloperId, CriterionId,
    Points —editable sobre el default—, Date, Period año/mes, Comment, AssignedByUserId,
    RequirementId opcional para ligar el punto a un requerimiento).
- **Versionamiento de aplicativos**:
  - `AppSystem`: catálogo de sistemas/aplicativos a desplegar (Nombre, Descripción,
    carpeta de origen por defecto, perfil de FTP por defecto).
  - `AppRelease`: una **versión** de un `AppSystem` (Version SemVer/etiqueta, **Changelog**
    editable, ZipBlobUrl, Checksum, Tamaño, CreatedBy, CreatedAt). Historial de versiones por sistema.
  - `DeploymentTarget` (servidor FTP), **alineado al JSON del usuario**: `Nombre`, `Host`
    (incluye esquema `ftps://`), `Puerto`, `Usuario`, `Contrasena` (**cifrada DPAPI** en BD),
    `RutaRemota`, `URL` (opcional), más `LastDeployedAt` y `LastReleaseId` (última versión
    desplegada a ese destino). Importable desde un archivo JSON con ese formato.
  - `DeploymentProfile`: selección nombrada de `DeploymentTarget` (a qué grupo desplegar).
  - `DeploymentJob` (referencia a **qué `AppRelease` se desplegó a qué perfil/destinos**, quién,
    estado, inicio/fin) + `DeploymentLogEntry` (log por destino en tiempo real).
  Así queda registrado qué versión de cada sistema se desplegó, cuándo y a dónde,
  con posibilidad de re-desplegar o identificar la última versión por destino.

## Fase 0 — Base común (se construye ahora)
1. Agregar paquetes NuGet base al [.csproj](Administrador_Desarrollo_Web.csproj).
2. `Models/User.cs` + enum `UserRole`.
3. `Data/AppDbContext.cs` con `DbSet<User>` y ruta de BD en AppData; migración inicial.
4. `Security/PasswordHasher.cs` (BCrypt) y `Services/CurrentUserContext.cs` (usuario en sesión).
5. `Services/AuthService.cs`: `Login(user, pass)`, `SeedAdmin()` (crea admin por defecto
   `admin` / contraseña temporal si la tabla está vacía y obliga a cambiarla).
6. `Services/AuditService.cs`: `Record(action, entityType, entityId, details)` que escribe
   en `AuditLog` con el usuario en sesión; invocado por los servicios en cada operación
   relevante (login/logout, alta/edición/baja, y luego despliegues/respaldos).
7. `Services/LoggingSetup.cs`: configuración de **Serilog** (archivo rotado diario) y
   registro de `ILogger<T>` en el contenedor DI; captura global de excepciones no manejadas.
8. `Forms/LoginForm.cs` (usuario/contraseña, validación, mensajes).
9. `Forms/MainForm.cs`: shell con menú lateral por rol + panel host de UserControls.
10. `Forms/UserManagementControl` (solo Admin): alta/baja/edición de usuarios y roles.
11. `Forms/AuditLogControl` (solo Admin): visor de la bitácora con filtros por fecha,
    usuario y acción.
12. `Services/SettingsService.cs` + `Forms/ConfigurationControl.cs` (solo Admin): pantalla
    de **Configuración** para guardar la **connection string de Azure** (cifrada DPAPI vía
    `AppSetting.IsSecret`), rutas por defecto y demás parámetros. La gestión de
    **información de los devs** vive en su propio módulo (Fase 1) accesible desde el menú.
13. `Services/ReportService.cs` (ClosedXML): utilidad `ExportToExcel(datos, ruta)` reutilizable;
    se añade botón **"Exportar a Excel"** en los grids de cada módulo.
14. Reescribir [Program.cs](Program.cs) para DI + Serilog + `Database.Migrate()` + `SeedAdmin()` + abrir `LoginForm`.
    Eliminar el `Form1` por defecto ([Form1.cs](Form1.cs), [Form1.Designer.cs](Form1.Designer.cs), Form1.resx).

## Fase 1 — Desarrolladores + Requerimientos (MVP, se construye ahora)
1. Entidades `Developer`, `Requirement`, `Assignment` + enums en `/Models`; migración.
2. `Forms/DevelopersControl`: grid (DataGridView) con alta/edición/baja de desarrolladores.
3. `Forms/RequirementsControl`: grid filtrable por estado; alta/edición con campos de
   fechas, estimación, % de avance y estado; barra/columna visual de progreso.
4. Asignación: en el detalle de un requerimiento, asignar uno o varios desarrolladores
   (`Assignment`); vista "Carga por desarrollador" que lista lo asignado a cada dev.
5. Filtros clave para el dolor diario: "por estimar", "por entregar", "en desarrollo",
   y orden por fecha comprometida.
6. Botón **"Exportar a Excel"** (ReportService) en los grids de desarrolladores,
   requerimientos y carga por desarrollador.

## Estado de las fases
- ✅ **Fase 0 — Base común** (BD, login, roles, bitácora, logueo, usuarios, configuración) — CONSTRUIDA.
- ✅ **Fase 1 — Desarrolladores + Requerimientos** (MVP) — CONSTRUIDA.
- ✅ **Fase 2 — Minutas + Vacaciones/Notas + Desempeño/Gamificación** — CONSTRUIDA.
- ✅ **Fase 3 — Despliegues + Versionamiento + Azure Blob + Respaldo** — CONSTRUIDA.
- ✅ **Fase 4 — Migración a Azure SQL + multiusuario** — CONSTRUIDA.
- ✅ **Fase 5 — Integración opcional con Azure DevOps** — CONSTRUIDA.

> El desglose granular y autocontenido de las Fases 4 y 5 (una tarea por sección, lista para
> retomarse de forma aislada) está en
> **[fases-4-5-desglose.md](fases-4-5-desglose.md)** (misma carpeta de planes).

## Fases ya construidas (descripción original)
- **Fase 2 — Minutas + Vacaciones/Notas + Desempeño**: editor de minutas de daily/sesión con
  items de acción y responsables; solicitudes de vacaciones por dev; bandeja de
  pendientes/recordatorios con fechas. **Gamificación**: pantalla para administrar
  `ScoringCriterion` (criterios y puntos propios del Admin) y asignar `PointEntry` a los devs
  (con comentario y, opcionalmente, ligado a un requerimiento); **tablero/ranking mensual**
  que suma puntos por desarrollador, ordena posiciones, permite elegir mes/año y exporta a
  Excel. Dashboard integrador (pendientes, próximas entregas, recordatorios, top del ranking).
- **Fase 3 — Despliegues + Versionamiento + Azure + Respaldo**:
  catálogo de **sistemas/aplicativos** (`AppSystem`) con su **historial de versiones**
  (`AppRelease`). Flujo: elegir sistema → comprimir la carpeta señalada generando una
  nueva **versión** (con changelog y checksum) → `BlobStorageService` (Azure.Storage.Blobs)
  sube el ZIP de esa versión a Azure Blob → `DeploymentService` recorre los FTP del perfil
  (JSON) con `FluentFTP` reportando progreso vía `IProgress<T>` a un panel de log en tiempo real,
  registrando un `DeploymentJob` que vincula versión↔destinos. Vistas para consultar el
  historial por sistema, ver la última versión desplegada por destino y re-desplegar una versión previa.
  - **Lista de servidores**: grid con cada `DeploymentTarget` mostrando Nombre, Host, URL,
    **última actualización** (`LastDeployedAt`) y versión desplegada, con botones para
    **importar el JSON** de servidores y **abrir la URL en el navegador del sistema**
    (`Process.Start` con `UseShellExecute=true`).
  - **Changelogs**: editar/consultar el changelog de cada `AppRelease` desde la vista de versiones.
  - **Respaldo**: `BackupService` que respalda `app.db` a Azure Blob (manual + programado).
  - Rol **Operaciones** habilitado para ejecutar perfiles de despliegue que el Admin define.
> Fases 4 y 5: ver desglose granular en
> **[fases-4-5-desglose.md](fases-4-5-desglose.md)**.

## Archivos clave
- Crear: `Data/AppDbContext.cs`, `Models/{User,AuditLog,AppSetting,Developer,Requirement,Assignment}.cs`,
  `Security/PasswordHasher.cs`,
  `Services/{AuthService,CurrentUserContext,AuditService,SettingsService,ReportService,LoggingSetup}.cs`,
  `Forms/{LoginForm,MainForm,DevelopersControl,RequirementsControl,UserManagementControl,AuditLogControl,ConfigurationControl}.cs`.
- Modificar: [.csproj](Administrador_Desarrollo_Web.csproj) (paquetes), [Program.cs](Program.cs) (DI + arranque).
- Eliminar: [Form1.cs](Form1.cs), [Form1.Designer.cs](Form1.Designer.cs), Form1.resx.

## Verificación
- `dotnet build` compila sin errores.
- `dotnet run`: arranca `LoginForm`; entrar con el admin sembrado abre `MainForm`.
- Crear un desarrollador y un requerimiento; asignar el dev; cambiar estado/avance/fechas
  y confirmar que persisten al reabrir (verifica SQLite + migraciones).
- Crear un usuario rol Operaciones; al iniciar sesión con él, el menú oculta los módulos
  de administración (verifica control de acceso por rol).
- Inspeccionar `%AppData%\AdministradorDesarrolloWeb\app.db` con un visor SQLite para
  confirmar tablas y datos.
- Realizar acciones (login, crear/editar) y confirmar que aparecen en el visor de
  **bitácora** y que se generan archivos en `%AppData%\AdministradorDesarrolloWeb\logs\`.
- En **Configuración**, guardar una connection string de Azure y confirmar que se persiste
  cifrada (no legible en texto plano en la BD).
- Pulsar **"Exportar a Excel"** en un grid y abrir el `.xlsx` generado con los datos correctos.
