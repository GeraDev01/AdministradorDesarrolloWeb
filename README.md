# Administrador de Desarrollo Web

Aplicación de escritorio (**WinForms · .NET 10**) para gestionar un equipo de desarrollo de principio a fin: personas, trabajo, tickets de **Azure DevOps** y **Freshdesk**, despliegues a servidores, vacaciones, SLA, desempeño y comunicación interna. Es **un solo ejecutable para todo el equipo**; lo que cada quien ve depende del rol con el que inicia sesión.

---

## ✨ Características

**Equipo**
- Desarrolladores (datos, fecha de ingreso, **número de serie del equipo**) y equipos/roles.
- **Ficha de perfil** confidencial (solo administrador): fortalezas, debilidades, stack técnico, **salario** y expectativas de crecimiento — el salario nunca se registra en la bitácora.
- Contactos y **comunicados**: el administrador envía avisos que le llegan a cada desarrollador a su bandeja (con globo en la bandeja del sistema).

**Trabajo**
- Requerimientos, métricas, reportes (exportables a **Excel**) y estimación/capacidad.
- Minutas de reunión.
- **Vacaciones**: cálculo automático de días conforme a la **Ley Federal del Trabajo** de México (reforma "Vacaciones Dignas") a partir de la fecha de ingreso; solicitudes con documento generado (**.docx → PDF**).
- Permisos, desempeño (puntajes/aprobaciones), evaluaciones e hitos, actividades libres.
- **SLA**: compromisos por prioridad, recordatorios automáticos, escalamiento y tablero de cumplimiento.
- Sugerencias del equipo (con votos).

**Despliegue e infraestructura**
- Despliegues por **perfil** o por **selección directa de servidores** (estilo Blobup), con respaldo previo por servidor, streaming y reintentos.
- **Despliegues programados** a una hora concreta, por perfil o por servidores directos (los ejecuta cualquier instancia abierta).
- Inventario de recursos de Azure y de programas/licencias.

**Integraciones y avisos**
- **Azure DevOps**: sincronización de work items, materialización a requerimientos y aviso "te asignaron un ticket".
- **Freshdesk**: sincronización de tickets con filtro por **agente** (mis asignados) y/o **grupo/departamento**, vínculo con tickets de DevOps y aviso de asignación.
- **Correo** (SMTP/IMAP), **Azure Blob Storage** (versiones y respaldos) y despliegue por **FTPS**.
- Avisos in-app persistentes por usuario + notificación en la bandeja del sistema.

## 👥 Roles

| Rol | Alcance |
|-----|---------|
| **Administrador** | Todo: equipo, trabajo, despliegues, integraciones, configuración. |
| **Operaciones** | Despliegues (en vivo y programados, por perfil o servidores directos) y avisos. |
| **Desarrollador** | Autoservicio: sus tickets/actividades, evaluaciones, SLA, vacaciones y sugerencias. |

La autorización se valida **en los servicios** (`AuthorizationGuard`), no solo en el menú.

## 🛠️ Tecnologías

- **.NET 10** / WinForms (`net10.0-windows`, x64)
- **Entity Framework Core 9** — doble proveedor **SQLite** / **Azure SQL Server**
- Inyección de dependencias (`Microsoft.Extensions.DependencyInjection`)
- **BCrypt** (hash de contraseñas), **ClosedXML** (Excel), **DocumentFormat.OpenXml** + **LibreOffice** (documentos), **MailKit** (correo), **Azure.Storage.Blobs**, **FluentFTP**, **Serilog**, **WebView2**
- Pruebas con **xUnit**

## ✅ Requisitos

**Para compilar**
- SDK de **.NET 10**
- Windows de 64 bits

**Para ejecutar (equipo destino)**
- Windows de 64 bits (el ejecutable publicado es autocontenido: incluye el runtime).
- **WebView2 Runtime** (viene en Windows 11; en Windows 10 puede requerir instalarlo).
- Acceso de red a la base de datos si se usa **Azure SQL** (y su firewall debe permitir la IP).
- Para generar/convertir documentos de vacaciones: **LibreOffice** instalado.

## 🚀 Ejecutar en desarrollo

```bash
git clone https://github.com/GeraDev01/AdministradorDesarrolloWeb.git
cd AdministradorDesarrolloWeb
dotnet run --project Administrador_Desarrollo_Web
```

En el **primer arranque** se crea una base **SQLite** local en `%AppData%\AdministradorDesarrolloWeb\app.db`, se aplican las migraciones y se siembra un usuario **admin** con una contraseña temporal que se muestra una sola vez (te pedirá cambiarla al entrar).

## 🗄️ Base de datos y configuración

La conexión se resuelve en este orden (`DbConnectionResolver`):

1. **Configuración local** de ese equipo — `%AppData%\AdministradorDesarrolloWeb\dbprovider.json` (capturada en *Configuración → Base de datos*; la contraseña se cifra con DPAPI del usuario de Windows).
2. **Conexión incrustada** en el ejecutable (si se publicó con ella).
3. **SQLite local**, como último recurso.

Las migraciones son **hechas a mano, aditivas e idempotentes** (`DatabaseMigrator`): al arrancar, la app crea/actualiza el esquema tanto en SQLite como en SQL Server sin perder datos.

## 📦 Distribución

Se reparte como **un solo `.exe` autocontenido** con la conexión ya incrustada, para que cada persona lo abra sin configurar nada:

```powershell
# Windows PowerShell (no pwsh)
cd Administrador_Desarrollo_Web\Deploy
.\build-app.ps1 -ConnectionString "Server=tcp:...;Database=...;User Id=app_dev;Password=****;Encrypt=True"
```

Genera un `.exe` en `dist\desarrollador\`. Guía completa: **[Docs/Conexion-portable.md](Docs/Conexion-portable.md)**.

> ⚠️ **Seguridad:** la conexión incrustada es **ofuscación, no seguridad** (la llave viaja en el binario). Incrusta siempre un **login de SQL restringido** — ver [`Deploy/crear-login-desarrollador.sql`](Administrador_Desarrollo_Web/Deploy/crear-login-desarrollador.sql). Las credenciales reales nunca están en el repositorio: viven en `%AppData%` o en la base.

## 🧪 Pruebas

```bash
dotnet test Administrador_Desarrollo_Web.Tests
```

## 🗂️ Estructura

```
Administrador_Desarrollo_Web/          App principal (WinForms)
├─ Data/          DbContext, migrador, resolución de conexión, cifrado
├─ Models/        Entidades del dominio
├─ Services/      Lógica de negocio y autorización
├─ Forms/         Pantallas (Controls) y diálogos (Details)
├─ Deploy/        Script de publicación y SQL del login restringido
├─ Plantillas/    Plantillas de documentos (vacaciones)
└─ docs/          Notas técnicas de fases
Administrador_Desarrollo_Web.Tests/    Pruebas (xUnit)
Docs/Manuales/                          Manuales por rol (Admin, Operaciones, Desarrollador)
ResetPassword.cs / ResetAdminPassword/  Utilidad para restablecer el admin
```

## 🔧 Utilidad: restablecer el administrador

Si se pierde el acceso, restablece (o crea) el usuario `admin` sobre la base local:

```bash
dotnet run --project ResetAdminPassword
```

Deja `admin` con una contraseña temporal y forzando el cambio en el siguiente inicio de sesión.

## 📚 Documentación

- Manuales por rol en **[Docs/Manuales](Docs/Manuales)** (`.md`, `.docx`, `.pdf`).
- Distribución y conexión portable: **[Docs/Conexion-portable.md](Docs/Conexion-portable.md)**.

---

Proyecto de uso interno. Sin licencia pública explícita; todos los derechos reservados por sus autores.
