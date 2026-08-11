# Réplica web — Administrador de Desarrollo

Blazor WebAssembly + API ASP.NET Core sobre .NET 10, arquitectura limpia, contra la **misma base de
datos** y los **mismos usuarios** que la aplicación de escritorio.

> **La aplicación de escritorio sigue siendo la de producción y no se toca.** La web se construye en
> paralelo contra una base de *staging*; cuando alcance paridad se hace un **corte único** y el
> escritorio se retira. Nunca hay dos aplicaciones escribiendo producción a la vez, que es lo que
> evita de raíz los problemas de esquema, de trabajos duplicados y de concurrencia entre ambas.

## Estructura

| Proyecto | Qué contiene | De qué depende |
|---|---|---|
| `AdminWeb.Shared` | DTOs por endpoint, enums, contratos de SignalR | **nada** |
| `AdminWeb.Domain` | Entidades, reglas de cálculo puras, `PasswordHasher`, `AuthorizationGuard`, `ICurrentUser` | Shared |
| `AdminWeb.Infrastructure` | `AppDbContext`, migrador, integraciones, PDF, imágenes | Domain |
| `AdminWeb.Application` | Servicios de negocio (copiados del escritorio y adaptados) | Infrastructure, Shared |
| `AdminWeb.Api` | Endpoints, autenticación, SignalR, trabajos de fondo; **hospeda el cliente** | Application |
| `AdminWeb.Client` | Blazor WebAssembly | **solo Shared** |
| `tests/AdminWeb.Application.Tests` | Red de seguridad del port | Application |

**La frontera que no se cruza**: el cliente solo referencia `Shared`. Si algún día apareciera ahí una
referencia a `Domain` o `Infrastructure`, el navegador acabaría descargando el modelo de datos y la
lógica de negocio completos.

## Cómo levantarlo en local

```powershell
# 1. La cadena de conexión NUNCA se versiona: va en user-secrets.
cd src\Web\AdminWeb.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=...;Database=SOLTUM_DEV_WD_STAGING;..."

# 2. Correr (la API sirve también el cliente Blazor)
dotnet run
```

Apunta a **staging**, no a producción: la web todavía no es la aplicación oficial.

Sin SQL Server a mano se puede levantar sobre SQLite añadiendo `AdminWeb:ProveedorDeBase = Sqlite` y
una cadena `Data Source=...`. En staging y en producción es SQL Server y punto.

### Las cuatro redes de seguridad

| Qué | Cómo se lanza | Qué atrapa |
|---|---|---|
| `tests/AdminWeb.Application.Tests` | `dotnet test` | Las reglas dentro de los servicios. Es la red que viaja con el port. |
| `tests/AdminWeb.Api.Tests` | `dotnet test` | La **tubería**: políticas de rol, cookie, 401/403/400, filtro de excepciones. Levanta la API en memoria contra SQLite. |
| `humo.ps1` | a mano | La aplicación arrancada de verdad sobre SQLite: migrador, siembra, flujo completo. |
| `humo-docker.ps1` | Docker (ver abajo) | Lo mismo **contra SQL Server y sobre Linux**, que es lo que va a correr en producción. |

Un servicio puede estar impecable y la API responder mal por cosas que compilan sin quejarse: una
política mal escrita, un servicio sin registrar, el middleware en el orden equivocado, una cookie que
no viaja, un rechazo de negocio que sale como 500 en vez de como 400. Nada de eso lo ven las pruebas
de servicios.

```powershell
cd src\Web
dotnet build AdminWeb.Api\AdminWeb.Api.csproj
.\humo.ps1
```

### Contra SQL Server, en Linux (Docker)

```powershell
# desde la raíz del repositorio
docker compose -f src\Web\docker-compose.yml up -d --build
.\src\Web\humo-docker.ps1
docker compose -f src\Web\docker-compose.yml down -v     # borra también la base
```

**Esto no es comodidad: es la única forma de probar dos caminos que ninguna otra red alcanza.**

1. **La rama de SQL Server del migrador.** Todo lo demás corre sobre SQLite, que tiene otro dialecto
   y otros tipos. Los parches idempotentes de la rama de SQL Server no los había ejecutado nadie, y
   la primera vez que se ejecutaron **la aplicación no arrancaba** (ver abajo).
2. **La concurrencia con `RowVersion`.** Esa columna solo existe en SQL Server; en SQLite el modelo
   la ignora. Sin esta prueba, «dos personas editan lo mismo y la segunda no pisa a la primera» era
   una afirmación sin comprobar. El guion lo ejercita: edita con un sello obsoleto y exige un 409.

La base es un contenedor **vacío y desechable**. No toca ninguna base real. El pipeline lo corre en
cada cambio, así que ya no depende de que alguien se acuerde.

> **Tres defectos que solo aparecieron aquí**, los tres en el arranque contra SQL Server y por tanto
> los tres condenados a saltar el día del corte:
> - `sp_getapplock` se llamaba con `SqlQuery` + `First()`, que EF rechaza por *non-composable SQL*.
>   La aplicación **no arrancaba**.
> - El candado se pedía con `LockOwner = 'Session'` sin fijar la conexión. EF toma una prestada del
>   pool por operación, así que el candado se pedía en una conexión, la migración corría en otra
>   —sin protección— y el `release` en una tercera. **Compilaba, no daba error y no protegía nada.**
> - Se intentaba candar una base que aún no existía: error 4060 en un entorno nuevo.

## Decisiones que conviene conocer antes de tocar nada

**El contexto de datos es Scoped, uno por petición.** En el escritorio era Singleton y eso obligaba a
defenderse con `Reload()` antes de decidir, `Detach` tras un fallo y `AsNoTracking` por todas partes.
Al copiar un servicio, **esas muletas se quitan**: con un contexto por petición lo que se lee ya es
fresco, y desanclar a mano rompe la unidad de trabajo. Lo que sí se conserva son los `ExecuteUpdate`
condicionales (por ejemplo al tomar o aceptar una actividad del pool): son atómicos en la base y
siguen siendo correctos.

**La autenticación se verifica en el servidor.** El escritorio traía la conexión incrustada y hacía
`BCrypt.Verify` en la máquina del usuario. Aquí el login pasa por `POST /api/auth/login` y la sesión
viaja en una **cookie HttpOnly** que el JavaScript del cliente no puede leer — un XSS no se lleva la
sesión. Los hashes existentes validan sin cambios: **nadie tiene que cambiar su contraseña** por la
mudanza.

**El menú recortado por rol es comodidad, no seguridad.** El cliente corre en la máquina del usuario
y es manipulable. La barrera real son las políticas de los endpoints; y detrás, los
`AuthorizationGuard` dentro de los servicios. Dos barreras, igual que en el escritorio.

**El sello de sesión (`SecurityStamp`) es nuevo de la web.** Permite echar a alguien de verdad al
cambiar su contraseña o desactivar su cuenta; antes la sesión moría con el proceso y no hacía falta.

**El cuerpo del foro es TEXTO, y se pinta como texto.** El servidor lo entrega troceado en segmentos
y con los enlaces ya validados (solo `http`/`https`); el cliente emite cada trozo con `@`, que Blazor
escapa. Nada de `MarkupString` ni de `innerHTML` sobre contenido escrito por alguien: en el escritorio
un enlace acababa en `Process.Start` y validar el esquema bastaba, aquí acabaría en el navegador de
quien lee, con su sesión. Diecinueve pruebas fijan esa garantía (`ForoSeguridadTests`).

**El mensaje de un rechazo se enseña tal cual.** Los servicios portados explican el motivo en
concreto —«ya marcaste tu entrada hoy a las 09:12», «esa actividad la tomó alguien más»— y ese texto
es la mitad de su valor. `ClienteApi` entiende las dos formas en que llega (el `ResultadoDto` de un
rechazo de negocio y el `ProblemDetails` del filtro de excepciones); cambiarlo por un «no se pudo»
genérico sería hacer la web peor que el escritorio.

**Lo que se sube se valida en el SERVIDOR** (`ArchivosSubidos`): el tipo lo dictan los BYTES y no la
extensión, el nombre se limpia de rutas y caracteres de control, y nada ejecutable ni marcado (`.svg`,
`.html`) entra aunque se llame `captura.png`. Al servirlo va con `nosniff`. Las subidas —y solo
ellas— exigen el testigo antiforgery, que `ClienteApi.SubirAsync` adjunta sin que la pantalla tenga
que acordarse.

**El cronómetro cuenta desde la hora del SERVIDOR.** El reloj de un portátil recién despertado suele
estar desajustado, y contar desde él enseñaría tiempo que nadie trabajó. Y si la conexión en vivo se
cae más de la tolerancia sin latido, el servidor ya consolidó la sesión: al reconectar, la pantalla
recarga en vez de seguir contando contra algo que ya se cerró.

## Estado

**Las cinco fases están terminadas: se alcanzó la paridad.** No queda ninguna entrada apagada en el
menú. Pasan **1187 pruebas de servicios**, **19 de la API levantada** y la prueba de humo completa,
que recorre unas setenta rutas contra la aplicación arrancada de verdad. El escritorio sigue intacto
y sus **1085 pruebas** también pasan.

**Lo que falta no es código: es el CORTE.**

- **[GUIA-DE-PUESTA-EN-MARCHA.md](GUIA-DE-PUESTA-EN-MARCHA.md)** — el camino completo desde aquí:
  probarla en local, preparar Azure, el ensayo y el corte. Empieza por ahí.
- **[EL-CORTE.md](EL-CORTE.md)** — solo el día del corte, paso por paso. Es una operación manual
  contra la base real y no la ejecuta ninguna herramienta.

Las fases 0, 1 y 2 de la guía —probar en local, preparar Azure y ensayar contra una copia— **no
tocan producción**: se pueden hacer con calma, en varios días y sin avisar a nadie. La única que la
toca es la fase 3, que es el corte.

### Fases 4 y 5 — integraciones, trabajos de fondo y despliegues

Azure DevOps, Freshdesk, correo (SMTP e IMAP), SLA, despliegues por FTP, despliegues programados,
almacenamiento en Blob y estado de servidores.

Lo que cambia de fondo en estas dos fases es **de quién depende que las cosas ocurran**. En el
escritorio, escalar un SLA, disparar un programado, ingerir el correo o mandar el resumen solo pasaba
si alguien tenía la aplicación abierta a esa hora; si nadie la tenía, no pasaba y nadie se enteraba.
Ahora lo hace el servidor. Los despliegues corrían en el `.exe` de quien pulsaba el botón, así que
cerrarlo a media subida los dejaba a medias; ahora corren en el servidor y cerrar la pestaña no los
cancela — al volver, la consola se reengancha.

Dos piezas que conviene conocer:

- **El PAT de Azure DevOps nunca baja al navegador.** Se guarda cifrado del lado del servidor y las
  llamadas las firma la API con el token de quien las provocó, para que en DevOps los comentarios
  queden a nombre de esa persona. De paso arregla que en el escritorio se perdiera al cambiar de
  equipo, porque estaba cifrado con DPAPI contra la cuenta de Windows.
- **Los avisos push** sustituyen a los globos de la bandeja del sistema, que era lo único que la web
  no podía hacer. El aviso guardado sigue siendo el que cuenta: si no hay llaves VAPID configuradas,
  si el navegador no dio permiso o si la entrega falla, el aviso está ahí al volver.

**Lo que NO se portó, a propósito**: el respaldo de la base de datos (`BackupService`,
`AutoBackupService`). Azure SQL trae respaldo continuo y restauración a un punto en el tiempo;
encima sería una copia peor de algo que ya existe — y en la que alguien confiaría. El respaldo de la
carpeta remota antes de desplegar sí se conserva.

### Fase 3 — administración

Las pantallas grandes del líder: requerimientos, sprint con su línea de tiempo, minutas, personas
(quién está, perfil y desarrollo, comunicados, equipos y usuarios), desempeño con la revisión de
puntos, evaluaciones, métricas, estimación y capacidad, vacaciones y permisos con su documento
firmado, configuración, limpieza de datos y los quince reportes.

Tres piezas nuevas que comparten todas:

- **Configuración compartida con el escritorio.** La web cifra los secretos con el MISMO esquema que
  la aplicación de escritorio, no con uno más fuerte, y es deliberado: hasta el corte las dos leen
  esas mismas filas, y un secreto guardado desde el navegador con otro cifrado dejaría al escritorio
  sin poder leerlo —fallando semanas después, al desplegar, con la contraseña equivocada—. Dos
  pruebas leen el código fuente del escritorio y comprueban que la semilla y el prefijo coinciden.
  Después del corte se sustituye por Data Protection, con el llavero en Blob y cifrado con un
  certificado — no con Key Vault, que esta suscripción no tiene; ver `Arranque/Llavero.cs`.
- **Documentos en PDF con QuestPDF**, detrás de `IGeneradorDeDocumentos`. Se retiró el camino
  anterior —armar un DOCX y convertirlo con LibreOffice instalado en la máquina—, que era la única
  pieza de la web incapaz de correr sola. **Consecuencia aceptada: la plantilla `.docx` deja de ser
  editable por Recursos Humanos**; el diseño del documento vive ahora en el código.
- **Firma manuscrita en un lienzo**, con eventos de puntero: se firma con el dedo o con lápiz desde
  una tableta, que es como se firma de verdad. En el escritorio era GDI+ y solo entendía el ratón.
  El PNG sale con fondo transparente y recortado al trazo, porque acaba pegado dentro de un
  documento que alguien archiva.

### Fase 2 — autoservicio del desarrollador

Nueve pantallas de escritura: mi jornada, mi pool y la administración del pool, mis actividades y la
autocalificación, mis vacaciones, mis permisos, sugerencias, y el foro completo (publicar, comentar,
editar, retirar, ❤ y capturas). Con ellas llegó lo que todas comparten:

- **Conexión en vivo** (SignalR). Sustituye a «la aplicación abierta en la bandeja»: conectado es
  tener la conexión abierta, y eso se sabe al instante en lugar de esperar diez minutos de silencio.
  Late por la presencia y por el cronómetro.
- **Trabajos de fondo** —barrido de presencia y consolidación de cronómetros— detrás de
  `AdminWeb:TrabajosDeFondoActivos`, **apagados por omisión**: mientras el escritorio siga en
  producción, sus temporizadores hacen ese mismo trabajo y encender los dos duplicaría todo. Se
  encienden en el corte.
- **Patrón de adjuntos** común: subida validada por bytes, descarga con `nosniff`, y un componente de
  pegar imágenes con Ctrl+V que reemplaza al `Clipboard.GetImage()` que el escritorio repetía en ocho
  pantallas. La miniatura la genera el navegador con un lienzo —el servidor la comprueba, no se la
  cree— para no meter ninguna librería de imágenes ni volver a bajarse los originales en cada
  refresco.
- **Barrera antifalsificación** en las subidas, que son las únicas peticiones que un sitio ajeno
  puede provocar con la cookie puesta.

### Fase 1 — portal de consulta

15 pantallas de solo lectura: dashboard, avisos, desempeño (ranking y Mi Panel), cumplimiento de SLA,
desarrolladores, equipos, contactos, programas, recursos de Azure, plantillas, bitácora paginada y el
foro con sus hilos. Más el andamiaje que todas reutilizan: cliente HTTP que atiende en un solo sitio
la sesión caducada y la contraseña sin cambiar, avisos y diálogos, descargas, y las preferencias de
columnas.

El menú lateral está completo por rol. Durante las fases intermedias las pantallas sin portar se
enseñaban **apagadas en lugar de ocultas**, para que se viera de un vistazo qué faltaba; hoy ya no
queda ninguna apagada.

Cuidado con lo que esa comprobación NO cubre, porque costó encontrarlo: **que una entrada de menú
esté encendida no dice que su pantalla haga lo mismo que la del escritorio.** Los cuatro catálogos
—desarrolladores, contactos, programas y recursos de Azure— estuvieron mucho tiempo con su entrada
encendida y abriendo bien, pero en solo consulta: sin alta, sin edición y sin endpoints de escritura.
Para comprobar paridad hay que mirar las OPERACIONES, no las rutas.

### Fase 0 — cimientos

Lo que ya existe:

- **Acceso completo de punta a punta**: login verificado en el servidor, cookie HttpOnly, cambio de
  contraseña obligatorio, bloqueo por intentos, desbloqueo, restablecimiento y bitácora. Los hashes
  BCrypt existentes validan sin cambios — **nadie tendrá que cambiar su contraseña** en el corte.
- **Dominio**: 57 entidades y 43 enums.
- **Datos**: `AppDbContext` con 61 DbSets y el `DatabaseMigrator` portado. Se comprobó con un diff de
  modelos que el mapeo es idéntico al del escritorio salvo los tres cambios buscados (longitudes ya
  existentes, `SecurityStamp` y las seis columnas `RowVersion` nuevas): cero diferencias en claves
  foráneas, índices, comportamientos de borrado y nulabilidad.
- **Arranque**: la API pone el esquema al día bajo `sp_getapplock`, siembra el administrador inicial
  y, dentro del mismo candado, los catálogos con los que la aplicación tiene que arrancar: criterios
  de puntuación, plantillas y la configuración del pool. El orden importa y es el del escritorio
  —renombrar criterios, sembrarlos, plantillas, pool—; un fallo ahí queda en el registro pero **no**
  tumba el arranque, porque sin catálogo inicial la aplicación funciona y negarse a arrancar dejaría
  al equipo fuera por un dato de conveniencia.
- **~25 servicios de negocio** portados a async/scoped, con sus pruebas.
- **Cliente Blazor**: shell con menú por rol, acceso y cambio de contraseña.

### Antes de tocar producción

1. **Ensayar el corte** contra una copia fresca de producción y **medir cuánto tarda el arranque**:
   esa es la ventana de mantenimiento. Ver [EL-CORTE.md](EL-CORTE.md).
2. **Generar las llaves VAPID** de los avisos push y guardarlas donde guardes lo importante; la
   privada va como ajuste del App Service (no hay Key Vault). Se generan una vez y no se regeneran.
3. **Configurar el pipeline** ([.github/workflows/web.yml](../../.github/workflows/web.yml)): la
   identidad federada de Azure y el nombre de la aplicación. Despliega a una ranura de ensayo,
   comprueba que arranque contra la base y solo entonces intercambia con producción.
4. **Probar el cronómetro con cierres sucios** (cerrar la pestaña, dormir el portátil, cortar el
   wifi) y comprobar que el tiempo se consolida hasta el último latido en vez de descartarse.

### Deuda conocida y anotada

**Ya no queda funcionalidad del escritorio sin portar.** Lo que había anotado aquí antes —Excel en
vacaciones y actividades, Notas/Pendientes, la importación desde DevOps con reajuste de SLA, los
avisos que se habían quedado solo in-app, el CRUD de los cuatro catálogos y el catálogo inicial de
plantillas— está cerrado y con pruebas. Lo que queda son decisiones tomadas, no huecos:

- **El cronómetro no se maneja desde «Mis Actividades»**: esa pantalla enseña el tiempo acumulado,
  pero arrancar, pausar y detener vive en «Mi jornada». Es a propósito: dos sitios para el mismo
  botón es la forma más rápida de acabar con dos cronómetros corriendo.
- **`/actividades` consulta el tiempo actividad por actividad** porque `WorkSessionService` no ofrece
  una versión por lotes. Es lo mismo que hacía el escritorio, pero allí la base estaba al lado.
- **La clave de licencia de un programa no viaja con la rejilla.** Se pide de una en una por su
  propio endpoint, que anota en la bitácora quién la miró (`AuditAction.Read`, el único valor del
  enum que el escritorio no tiene). Al editar, mandarla nula la CONSERVA; para quitarla hay que
  mandar algo en blanco, que sí es una decisión explícita.
- **Los secretos cifrados con DPAPI habrá que recapturarlos** en el corte. No es portable: solo los
  descifra la máquina que los escribió, así que ningún código de la web podría leerlos. Está como
  paso 5 de [EL-CORTE.md](EL-CORTE.md).
- **`LftVacaciones` está duplicado** en el escritorio y en la web, con sus dos suites de pruebas.
  Es deliberado: es una regla de ley y hasta el corte las dos aplicaciones tienen que dar el mismo
  número. Si alguien toca una copia, la otra suite lo dice.

### Una trampa que ya costó una vez

Un servicio que falta en el contenedor **no se nota al compilar**. Un parámetro de endpoint cuyo tipo
no está registrado se interpreta como cuerpo de la petición, y la aplicación revienta al ARRANCAR con
«Body was inferred but the method does not allow inferred body parameters». Ni el compilador ni las
pruebas de servicios lo ven; lo atrapa la prueba de humo, que levanta la aplicación de verdad. Si
añades un endpoint, registra su servicio y pasa `humo.ps1`.
