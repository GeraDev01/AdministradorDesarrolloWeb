# El modelo de datos

> **Esto no es un volcado de columnas.** Un catálogo de campos envejece a la primera migración y
> engaña a quien lo lee después. Lo que hay aquí es lo otro: qué grupos de entidades existen, para
> qué es cada uno, las relaciones que no se adivinan mirando una entidad sola, y **las reglas que el
> esquema no puede expresar** — que son las que muerden.
>
> Las columnas están en `AdminWeb.Domain/Entities/`, con un comentario por campo que lo merece. El
> mapeo, los índices y el comportamiento de borrado están en
> [AppDbContext.cs](AdminWeb.Infrastructure/Data/AppDbContext.cs), con la razón de cada decisión al
> lado. Nada de eso se repite aquí.

Hoy son **70 entidades**, **70 `DbSet`** y **46 enumeraciones**. Es la **misma base** que usaba la
aplicación de escritorio: mismas tablas, mismas columnas, mismos nombres en inglés. Lo que la web
añadió es aditivo y está listado en [EL-CORTE.md](EL-CORTE.md#3-apuntar-la-web-a-producción-y-arrancarla).

---

## Los grupos

### 1. Identidad y acceso — 8 entidades

`User` · `AuditLog` · `AppSetting` · `UserSecret` · `UserPreference` · `PushSubscription` ·
`UserRecoveryCode` · `UserTrustedDevice`

La cuenta con la que se entra y todo lo que cuelga de ella. `User` es la tabla del escritorio y sus
hashes BCrypt siguen validando: **nadie tuvo que cambiar su contraseña por la mudanza**.

Las cinco últimas son **propias de la web** y todas sustituyen a algo que antes vivía en la máquina
de cada persona (el token cifrado con DPAPI, el `columnas.json`) o que no hacía falta porque la
aplicación seguía viva en la bandeja del sistema (los avisos push).

`AppSetting` es un par clave-valor con marca de secreto. Ojo: **su contenido viaja entero al
navegador** cuando se abre Configuración, y esa es la razón de que las plantillas de documento tengan
tabla propia en vez de guardarse ahí.

### 2. Personas y equipos — 4

`Developer` · `DeveloperProfile` · `Team` · `TeamRotation`

`Developer` es la **ficha** de un integrante, distinta de la cuenta (ver más abajo).
`DeveloperProfile` es 1:1 con ella y **contiene el salario**: es información confidencial y su acceso
está restringido al administrador. Un equipo tiene miembros (`Developer.TeamId`) y además un líder,
que es otra clave foránea aparte.

`Team` se apunta **a sí misma** por `EquipoPadreId`: nulo es un equipo raíz y cualquier otro valor lo
cuelga de otro equipo, que es lo que permite los subequipos. La clave foránea **no lleva acción de
borrado** —SQL Server no la admite sobre una tabla que se referencia a sí misma— así que los
subequipos de un equipo que se elimina los recoloca el servicio: suben a colgar del abuelo. El árbol
no se recorre con SQL recursivo sino en memoria, con
[JerarquiaDeEquipos](AdminWeb.Domain/Equipos/JerarquiaDeEquipos.cs), que es también donde está la
regla que impide que un equipo acabe siendo su propio ancestro.

### 3. Trabajo planificado — 4

`Requirement` · `Sprint` · `Assignment` · `RequirementAttachment`

El backlog de siempre. Un requerimiento puede estar en un sprint o no estarlo; se asigna a una o
varias personas a través de `Assignment`.

### 4. El pool de actividades — 5

`PoolActivity` · `PoolPointsMatrixEntry` · `PoolChecklistTemplateItem` ·
`PoolActivityChecklistItem` · `PoolActivityExtraCriterion`

El sistema de puntos **con el valor fijado antes de trabajar**. La matriz (tipo × complejidad) dice
cuánto vale cada cosa y el líder la configura una vez; al publicar una actividad, los puntos se
copian y quedan **congelados**. Quien la toma sabe exactamente cuánto vale, y verificarla no es
negociar el precio: es comprobar que el checklist está hecho.

**Y desde la puerta única, es el ÚNICO camino que reparte trabajo encargado.** Una `PoolActivity`
cubre hoy cuatro cosas que antes vivían en sitios distintos, y las cuatro son la misma fila con
estados distintos: lo que el líder publica, lo que el alta automática trae de un work item, lo que
un desarrollador **propone** —`PorClasificar` con `ClaimedByDeveloperId` puesto— y el **descuento**
que el líder aplica, que nace pagado y en negativo. No hay tablas nuevas para ninguna: un estado
más y un campo que ya existía.

**No es lo mismo que un `Requirement`** y mezclarlos costaría caro: un filtro olvidado metería
actividades del pool en el backlog, en las métricas o en la importación de DevOps. El razonamiento
está en la cabecera de [PoolActivity.cs](AdminWeb.Domain/Entities/PoolActivity.cs).

### 5. El tiempo — 6

`WorkPresence` · `AttendanceRecord` · `WorkSession` · `WorkInterval` · `DevActivity` ·
`DevActivityAttachment`

**Cuatro tablas hablan de tiempo y son cuatro cosas distintas.** Es la confusión número uno de este
modelo, y tiene su apartado propio abajo.

### 6. Desempeño — 5

`ScoringCriterion` · `PointEntry` · `TeamPointEntry` · `DeveloperEvaluation` · `DeveloperMilestone`

`PointEntry` es **el único lugar donde viven los puntos**, vengan de donde vengan: los que asigna el
líder, los que el desarrollador se autocalifica y los que salen de aceptar una actividad del pool.
Por eso el ranking, los KPI, los reportes y el PDF funcionan sin saber que el pool existe.

### 7. Ausencias — 4

`VacationRequest` · `VacationDocument` · `LeaveRequest` · `SignatureProfile`

Vacaciones y permisos son **dos flujos distintos** y no comparten tabla: unas vacaciones son un rango
de fechas que genera un documento firmado; un permiso son uno o varios días —o **un tramo de horas de
un solo día**, en `HoraInicio`/`HoraFin`— con su justificante. Esas dos columnas van en nulo en los
permisos de día completo, que es todo el histórico: el nulo significa «día completo» y por eso no hay
nada que rellenar al migrar. `SignatureProfile` guarda las firmas manuscritas que se estampan en el
documento.

### 8. Comunicación y conocimiento — 13

`ForumPost` · `ForumLike` · `ForumAttachment` · `KnowledgeArticle` · `KnowledgeImage` ·
`Suggestion` · `SuggestionVote` · `Minute` · `MinuteActionItem` · `Notification` · `Note` ·
`Template` · `DocumentTemplate`

Todo lo que el equipo escribe. Tres cosas que no se ven en el nombre:

- **El foro es una sola tabla autorreferente.** Una publicación y un comentario son la misma fila con
  distinto `ParentId`; `RootId` es lo que permite traer un hilo entero de una vez.
- **El glosario no es otra tabla.** Un término del glosario es un `KnowledgeArticle` corto con sus
  etiquetas; una guía larga es otro con las suyas. Partirlo en dos habría obligado a buscar dos
  veces y a decidir en cuál va cada cosa cuando un término crece.
- **Las imágenes de un artículo sí son otra tabla**, `KnowledgeImage`, por lo mismo que las del foro:
  son binarios de megas y el cuerpo del artículo se lee entero en cada búsqueda. El cuerpo las nombra
  por NÚMERO —`![descripción](imagen:12)`— y nunca por una dirección: es lo que permite incrustar
  imágenes sin que el texto que escribe una persona acabe dentro de un atributo del navegador de
  quien lee. A diferencia de `ForumAttachment` no guarda miniatura, porque lo que se pinta aquí es un
  diagrama al ancho de la columna y no un recuadro de 200 píxeles.

### 9. Integraciones y SLA — 9

`DevOpsTicket` · `FreshDeskTicket` · `TicketLink` · `WatchedTicket` · `DevOpsAssignmentRule` ·
`DevOpsSavedFilter` · `DevOpsAssignmentSeen` · `FreshDeskAssignmentSeen` · `SlaCommitment`

Copias locales de lo que vive en Azure DevOps y en Freshdesk, más lo que las une (`TicketLink`) y lo
que se promete sobre ellas (`SlaCommitment`). Las dos tablas `…AssignmentSeen` existen para no volver
a avisar de lo mismo.

### 10. Despliegues — 8

`AppSystem` · `AppRelease` · `DeploymentTarget` · `DeploymentProfile` · `DeploymentProfileTarget` ·
`DeploymentJob` · `DeploymentLogEntry` · `ScheduledDeployment`

Un **sistema** tiene **versiones**; un **perfil** agrupa **destinos** (servidores FTP); un **trabajo**
es una versión publicada en un perfil, con su bitácora línea a línea; un **programado** es lo mismo
con una cita en el calendario.

### 11. Catálogos e inventario — 4

`AzureResource` · `Software` · `Contact` · `Project`

Los inventarios: recursos de Azure, programas con su licencia, contactos externos y proyectos.

**Cuidado con estos, y con `Developer`.** Los cuatro catálogos del menú —desarrolladores, contactos,
programas y recursos de Azure— estuvieron mucho tiempo con su entrada encendida y abriendo bien, pero
**en solo consulta**: sin alta, sin edición y sin endpoints de escritura. Ya no es así, pero la
lección se queda: **para comprobar paridad con el escritorio hay que mirar las operaciones, no las
rutas.**

---

## Las relaciones que no se adivinan

### `User` no es `Developer`, y esa distinción decide cosas

Una **cuenta** (`User`) es con lo que se entra. Una **ficha** (`Developer`) es la persona dentro del
equipo: su antigüedad, su equipo, sus vacaciones, sus puntos.

- La liga es `User.DeveloperId`, y es **opcional**. Hay cuentas sin ficha: las de Operaciones, por
  ejemplo, no tienen ficha de desarrollador.
- Al borrar la ficha, la cuenta se queda (`SetNull`). No al revés.
- **Presencia y asistencia se guardan por `UserId`, no por `DeveloperId`**, y eso es deliberado: la
  ficha es una copia opcional que se toma al marcar, así que las marcas anteriores a ligarla la
  tienen nula y se perderían del total. Está explicado en
  [AttendanceRecord.cs](AdminWeb.Domain/Entities/AttendanceRecord.cs).
- Lo demás —puntos, vacaciones, actividades, asignaciones— va por `DeveloperId`.

**Consecuencia práctica al escribir una consulta:** si cruzas asistencia con puntos, estás cruzando
dos identificadores distintos de la misma persona. Que hoy coincidan uno a uno no lo garantiza el
esquema.

### Las cuatro tablas del tiempo

| Tabla | Qué mide | Quién la escribe | Sirve para |
|---|---|---|---|
| `AttendanceRecord` | La entrada y la salida **marcadas a mano** | La persona, con un botón | **La asistencia oficial.** Es lo que cuenta |
| `WorkPresence` | La jornada que la aplicación abre y cierra **sola**, por latido | El sistema | Saber quién está conectado ahora, y contrastar |
| `WorkSession` | El **cronómetro** sobre un objetivo concreto | La persona, arrancando y parando | Cuánto tiempo llevó una cosa |
| `WorkInterval` | Cada **tramo** consolidado, con su fecha local | El sistema, al pausar o detener | El total por día, exacto aunque una sesión cruce la medianoche |

Separarlas es lo que impide que una mienta por la otra: **la aplicación puede quedarse abierta sola en
un equipo encendido, y alguien puede trabajar sin abrirla.** Ninguna de las dos primeras vale como la
otra.

Dos reglas que no están en el esquema:

- **Un solo `AttendanceRecord` por día y por persona.** La reentrada después de comer no se marca: un
  minutado de las pausas de alguien es vigilancia, no asistencia.
- **El estado de `WorkPresence` (en el baño, comiendo…) se sobrescribe y no deja histórico**, por lo
  mismo.

### El pool se enlaza con lo demás, y dos de esos enlaces no llevan clave foránea

```
PoolActivity ──LinkedDevActivityId──▶ DevActivity ──▶ WorkSession (el cronómetro de siempre)
     │
     └────────PointEntryId──────────▶ PointEntry  (creado ya APROBADO al aceptar la actividad)
```

Los dos son **`int?` sueltos, sin clave foránea**, y no es un olvido: `PointEntries` y `DevActivities`
ya caen en cascada desde `Developers`, así que una segunda ruta hasta la misma tabla es de las que
**SQL Server rechaza al crear las restricciones**. La misma razón explica `KnowledgeArticle.PointEntryId`
y varios `NoAction` y `Restrict` que parecen arbitrarios en el `AppDbContext`: **están ahí para que el
esquema se pueda crear**, no por gusto. Cambiarlos rompe la creación de la base.

Hay más enlaces con la misma forma y **no todos por el mismo motivo**, y conviene no confundirlos:

```
DevActivity ──PoolActivityId──▶ PoolActivity   (la marca de percha, nunca se limpia)
PoolActivity ──AnulacionPointEntryId──▶ PointEntry   (la compensatoria de un descuento anulado)
PoolActivityExtraCriterion ──KnowledgeArticleId──▶ KnowledgeArticle
```

Los dos primeros son el caso de arriba: segunda ruta de cascada. **El tercero no.**
`KnowledgeArticle` no tiene ninguna ruta hasta `Developers`, así que SQL Server aceptaría la
restricción sin rechistar — va sin ella **por coherencia con la columna de al lado**:
`ScoringCriterionId` es traza a propósito, para que la fila siga explicando de dónde salieron unos
puntos ya cobrados aunque el catálogo se depure. Dos campos contiguos con doctrinas opuestas —uno
que sobrevive al borrado y otro que lo impide— sería indefendible al leerlo. Por eso el título del
artículo va **congelado** al lado, igual que el nombre del criterio.

La **marca de percha** merece una línea aparte porque es la única que se escribe para no volver a
tocarse: se pone al crear el cronómetro de una actividad tomada y **no se limpia nunca**, ni al
devolver ni al liberar. Es lo que la hace reconocible cuando `LinkedDevActivityId` ya se borró — sin
ella, la percha de un trabajo devuelto volvía a parecer una actividad libre cualquiera, cerrada, con
tiempo medido y sin pagar, y calificarla abonaba puntos por trabajo que después iba a cobrar otro.

### Lo que nunca lleva clave foránea al autor

`AuditLog` · `WorkPresence` · `AttendanceRecord` · `WorkInterval` · `ForumPost` ·
`KnowledgeArticle` · `Template`

Todas guardan el **nombre** de quien lo hizo, congelado en el momento, en vez de apuntar a la cuenta.
El criterio es el mismo en las siete: **son histórico y tienen que sobrevivir a que se borre la
cuenta de esa persona.** Una publicación del foro de alguien que ya no está sigue siendo suya; una
línea de bitácora sin autor no sirve para nada.

Lo contrario también es una decisión: `UserSecret`, `UserPreference`, `PushSubscription`,
`UserRecoveryCode` y `UserTrustedDevice` **sí** van en cascada desde `User`, porque un secreto o una
preferencia sin dueño no significan nada.

### Un requerimiento sale de un sprint, nunca se borra con él

`Requirement.SprintId` es `SetNull`: borrar un sprint devuelve sus requerimientos al backlog. **Y
además el servicio los desliga a mano**, porque en la base real esa columna se agregó por `ALTER` sin
clave foránea —el migrador es aditivo— y ahí no hay nada que aplique el `SetNull`. El mapeo cubre las
bases nuevas; el servicio, las viejas.

### Un objetivo, dos formas

`WorkSession` y `SlaCommitment` apuntan **o** a un requerimiento **o** a una actividad libre. Las dos
claves foráneas son opcionales y **es el servicio quien garantiza que venga exactamente una**. El
esquema admitiría las dos a la vez o ninguna.

---

## Las reglas que el esquema no expresa

Éstas son las que hay que conocer antes de tocar nada. Ninguna está declarada en la base: viven en
los servicios, y romperlas no da error de integridad.

| Regla | Dónde vive | Qué pasa si se rompe |
|---|---|---|
| **El saldo de vacaciones no se guarda.** Se deriva de la fecha de ingreso, las solicitudes y un ajuste manual | [SaldoDeVacacionesService](AdminWeb.Application/Services/SaldoDeVacacionesService.cs) | Guardarlo como número lo desincroniza el primer día que alguien cancele por otro camino, y a partir de ahí miente sin avisar |
| **Los puntos se congelan al publicar**, no se leen de la matriz al aceptar | `PoolActivity.Points` | Cambiar la matriz revaluaría hacia atrás lo ya trabajado: quien tomó algo de 13 puntos podría cobrar 5 |
| **La prioridad no cambia los puntos** | `PoolActivityService` | Publicar «Crítica» sería la forma de regalarlos |
| **Los puntos nunca viajan en una petición de escritura** | `PoolEndpoints` | Aceptar los que mandara el cliente regala el sistema entero |
| **Una sola actividad viva por work item de DevOps.** El índice existe pero **no es único** a propósito: el mismo work item puede volver a necesitar una actividad cuando la anterior ya cerró | `PoolActivityService` | La regla depende del estado de la otra fila y eso no cabe en un índice único de los dos motores |
| **Lo pendiente de mandar a DevOps se DERIVA**, comparando lo que la actividad dice hoy con las cuatro marcas de agua de lo último que DevOps confirmó (`DevOpsEsfuerzoEnviado`, `DevOpsPrioridadEnviada`, `DevOpsAsignadoADeveloperId`, `DevOpsEstadoEnviado`) | `PoolActivity`, [PoolDevOpsService](AdminWeb.Application/Services/PoolDevOpsService.cs) | Una columna «pendiente» es un tercer dato que mantener de acuerdo con los otros dos, y el día que un camino olvide bajarla el sistema miente en la dirección peor: diciendo que ya se envió |
| **Una actividad del pool puede nacer SIN CLASIFICAR** (`PoolActivityStatus.PorClasificar`), con 0 puntos y sin tipo útil, cuando la trae sola el alta desde DevOps. No se puede tomar, no vale nada y nada sale hacia DevOps hasta que el líder la publica | `PoolActivity.YaPublicada`, [PoolDesdeDevOpsService](AdminWeb.Application/Services/PoolDesdeDevOpsService.cs) | Sin `YaPublicada`, el empuje intentaría resolver credenciales sin sesión —y reventaría dentro de esa guarda— y le mandaría la prioridad «Media» (un 3) a un ticket recién creado que tiene el 2 por omisión: se la BAJARÍA |
| **El alta automática deduplica contra el POOL, no contra el ticket**: se descarta todo work item que ya tenga actividad, en cualquier estado | [PoolDesdeDevOpsService](AdminWeb.Application/Services/PoolDesdeDevOpsService.cs) | Una marca en `DevOpsTickets` la borraría `DataCleanupService` al purgar esa tabla, y la siguiente pasada recrearía una actividad por cada ticket ya tratado. Consecuencia asumida: un work item descartado no vuelve a entrar solo |
| **Solo DOS archivos convierten trabajo en puntos**, y la lista está cerrada por una prueba que lee el código fuente | [ProductoresDePuntosTests](tests/AdminWeb.Application.Tests/ProductoresDePuntosTests.cs) | Reducir cuatro caminos a uno se hace una vez; mantenerlos reducidos no. Sin la prueba, dentro de dos años vuelve a haber cuatro por el mismo camino por el que llegaron los de hoy: alguien necesita abonar puntos desde una pantalla nueva, escribe `PointEntries.Add`, y nada falla |
| **Todo lo que paga desde el pool pasa por `AbonarAsync`**: una sola transacción con el `ExecuteUpdate` condicional sobre `PointEntryId == null`. Lo usan aceptar una entrega y publicar un descuento | [PoolActivityService](AdminWeb.Application/Services/PoolActivityService.cs) | Dos sitios que insertan una `PointEntry` y escriben su traza acaban con uno de los dos olvidándose de la condición, y entonces el mismo trabajo se paga dos veces sin que salte nada |
| **El precio se fija ANTES de trabajar.** Es la invariante fundacional, y de ella cuelga que se retiraran la autocalificación y calificar una actividad libre: las dos ponían valor a algo ya hecho | `PoolActivityService`, y las guardas de `PerformanceScoringService.RegistrarAutocalificacionAsync` y `DevActivityService.CalificarAsync` | Sin ella los puntos de dos personas dejan de ser comparables, que es lo único que los hace servir para algo |
| **El artículo de conocimiento es la ÚNICA excepción declarada** a la regla de arriba, y se declara por modelo y no por política: un artículo no se encarga —«escribe sobre X, vale 8»—, lo que vale es el artículo. No tiene reclamo, ni plazo, ni checklist, ni cronómetro | [ConocimientoService.AprobarAsync](AdminWeb.Application/Services/ConocimientoService.cs) | Meterlo en el pool costaría cuatro columnas anulables cuya única función sería decir «esta fila no es realmente del pool». Sin dejarlo escrito, quien lea el código en seis meses lo tomará por un camino que se olvidaron de apagar |
| **Una propuesta es un `PorClasificar` CON reclamo**, y ese único campo decide a dónde va al clasificarla: sin dueño sale `Disponible` al pool, con dueño sale `Tomada` a las manos de quien la propuso | `PoolActivityService.EditarAsync` | Mandar una propuesta al pool común dejaría que se la llevara otro después de saber lo que paga; asignar lo venido de DevOps se lo pondría a nombre de quien no lo pidió y el pool se quedaría vacío |
| **Las propuestas cuentan dentro del tope de tomadas** | `PoolActivityService.CuantasVivasAsync`, que miran proponer y tomar | Sin ellas se llega al doble de trabajo vivo proponiendo en vez de tomando, y se descubre el día que el líder clasifique y aparezcan todas tomadas de golpe |
| **Un descuento se anula con una compensatoria, no borrando la entrada** (`PoolActivity.AnulacionPointEntryId` la protege de anularse dos veces) | `PoolActivityService.AnularDescuentoAsync` | Aquí nada que haya pagado se borra: el histórico tiene que poder explicar por qué el marcador de alguien bajó y volvió a subir |
| **Una actividad libre se paga UNA vez** (`DevActivity.PointEntryId`), y la que arrastra el pool no se paga por ahí. **Calificarlas se retiró**; la traza y la guarda se quedan porque las entradas de antes del corte siguen ahí | [DevActivityService.CalificarAsync](AdminWeb.Application/Services/DevActivityService.cs) | Sin la traza se pagaría dos veces al calificar dos veces; sin la exclusión del pool, el mismo trabajo cobraría por dos caminos |
| **`PoolActivityExtraCriteria` la crea el MIGRADOR a mano, con SQL crudo, en las dos ramas.** Una columna nueva suya va en **tres** sitios por rama: la entidad, el cuerpo del `CREATE TABLE` y un `ALTER` idempotente | [DatabaseMigrator](AdminWeb.Infrastructure/Data/DatabaseMigrator.cs) | Olvidar el cuerpo del `CREATE` no rompe la pantalla nueva: rompe **todo lo que lea la tabla**, y solo en las instalaciones nuevas, que es el peor sitio para enterarse |
| **Devolver una actividad al pool NO desasigna su work item**, pero sí limpia `DevOpsEstadoEnviado` para que el siguiente que la tome vuelva a ponerlo en curso | `PoolActivityService.SoltarReclamo` | Desasignar vaciaría en DevOps un campo que quizá puso otra persona; no limpiar el estado dejaría el ticket parado en la columna donde lo dejó el anterior |
| **Una `WorkSession` tiene exactamente un objetivo** | `WorkSession.EsValida` | El esquema aceptaría dos o ninguno |
| **Un solo registro de asistencia por día y persona** | `AttendanceService` | — |
| **El secreto del segundo factor no está en `Users`**: vive cifrado en `UserSecrets`, como el PAT | [User.cs](AdminWeb.Domain/Entities/User.cs) | Una columna en claro sería una llave de acceso legible para quien consulte la base |
| **Los códigos de rescate y los equipos recordados se guardan solo como hash** | `UserRecoveryCode`, `UserTrustedDevice` | Ni la base ni un respaldo los contienen en claro |
| **El cuerpo del foro y de la base de conocimiento es TEXTO**, y el servidor lo entrega troceado, con los enlaces ya validados y las imágenes nombradas por NÚMERO | `ForumRichText`, `ConocimientoTexto` | `MarkupString` sobre eso es XSS almacenado con la sesión de quien lee; una dirección de imagen que llegara del cuerpo lo sería igual, dentro de un atributo |
| **La clave de licencia de un programa no viaja con la rejilla.** Se pide de una en una y queda en la bitácora como `AuditAction.Read` | `CatalogosService` | Al editar, mandarla nula la **conserva**; para quitarla hay que mandar algo en blanco |

---

## Dos trampas del motor

### `RowVersion` solo existe en SQL Server

Trece entidades traen sello de concurrencia optimista. En SQLite **el modelo lo ignora**
(`.Ignore(...)`), porque allí no hay concurrencia que controlar. Consecuencias:

- **Ninguna prueba en SQLite ejercita el 409.** Eso solo se prueba con
  [humo-docker.ps1](humo-docker.ps1), contra SQL Server de verdad.
- Las entidades **sin** sello se pisan en silencio si dos personas las editan a la vez. Es una
  decisión asumida: se pusieron donde la edición simultánea es plausible (el líder revisando lo que
  el desarrollador está entregando), no en todas.

### Los decimales, en SQLite, son texto

EF guarda `decimal` como `TEXT` en SQLite (`PoolActivity.HorasLimite`, `HorasEstimadas`,
`DeveloperProfiles.Salary`, la matriz de puntos). **Ordenar o filtrar por esas columnas dentro de un
LINQ traducido compararía cadenas**: `"9.0"` saldría mayor que `"40.0"`.

Por eso **toda comparación sobre esos campos se hace en C#, sobre objetos ya materializados**. Hoy no
hay ninguna consulta que ordene por el plazo del pool; que siga así.

Y por eso mismo la precisión va **declarada** (`HasPrecision(6, 2)`) y no a la omisión de EF: sin
declararla, una base creada por `EnsureCreated` y otra parcheada por el migrador dejarían de ser la
misma base.

---

## Cómo se cambia el esquema

**No hay migraciones de EF y no hay tabla de historial.** El esquema lo pone al día
[DatabaseMigrator.cs](AdminWeb.Infrastructure/Data/DatabaseMigrator.cs) con parches idempotentes que
se ejecutan **todos, en cada arranque**. El porqué está en
[DECISIONES.md](DECISIONES.md#el-migrador-son-parches-idempotentes-y-no-hay-historial-de-ef).

Lo que eso significa al añadir una columna:

> ### La regla
>
> **`EnsureCreated` crea el esquema a partir del modelo, pero NO altera una base que ya existe.**
>
> En una base nueva —la de las pruebas, la de Docker— tu columna aparece sola y todo pasa en verde.
> En la base real, que lleva años de datos, **no aparece nunca**. Y EF la pide en cada consulta de esa
> tabla: no se rompe la pantalla que la usa, se rompe **todo lo que lea esa tabla**.
>
> Ya ocurrió con `Developers.TeamFunction`. La suite entera pasaba y la columna no existía.

### La lista, para una columna nueva

1. **La propiedad** en la entidad, en `AdminWeb.Domain/Entities/`.
2. **La declaración de longitud o precisión** en
   [AppDbContext.cs](AdminWeb.Infrastructure/Data/AppDbContext.cs) si es texto o decimal. Sin ella, EF
   la crea como `nvarchar(max)` en las bases nuevas mientras el parche la deja en `nvarchar(200)` en
   las viejas: **la misma columna con dos tipos según cuándo se creó la base**. Y una columna `(max)`
   no cabe como clave de índice, que ya mordió una vez.
3. **El parche de SQLite**, con comillas dobles:
   `ALTER TABLE "Tabla" ADD COLUMN "Col" TEXT` dentro de un `try { } catch { }`.
4. **El parche de SQL Server**, con T-SQL:
   `IF COL_LENGTH('Tabla','Col') IS NULL ALTER TABLE [Tabla] ADD [Col] nvarchar(200) NULL;` pasado por
   `Exec(...)`, que recoge el fallo en vez de tragárselo.
5. **Una prueba que quite la columna y compruebe que el migrador la repone.** El patrón está en
   [FuncionDeEquipoMigracionTests.cs](tests/AdminWeb.Application.Tests/FuncionDeEquipoMigracionTests.cs):
   se crea la base con el modelo de hoy, se le hace `DROP COLUMN`, se corre el migrador y se
   comprueba que volvió — y que no devolvió ninguna sentencia fallida. Si la columna lleva **clave
   foránea**, `DROP COLUMN` no sirve: SQLite se niega, y hay que rehacer la tabla sin ella
   ([SubequiposMigracionTests.cs](tests/AdminWeb.Application.Tests/SubequiposMigracionTests.cs)).

### Las tres cosas que no se hacen

- **Copiar una sentencia de una rama a la otra sin traducirla.** Los dos dialectos no son
  intercambiables: comillas dobles contra corchetes, `TEXT` contra `nvarchar`, `INTEGER` contra `int`.
  **Ya ha costado dos veces.**
- **Borrar o renombrar nada.** Todo es aditivo. El escritorio siguió leyendo esas columnas hasta el
  corte, y el paso 8 de [EL-CORTE.md](EL-CORTE.md#8-si-hay-que-volver-atrás) —la marcha atrás— depende
  de que lo siga pudiendo hacer.
- **Ejecutar SQL a mano contra ninguna base.** El esquema se pone al día arrancando la aplicación.
  `SET PARSEONLY ON` **no valida sin ejecutar**, aunque suela creerse.

### Cuando el cambio no es una columna sino una conversión de datos

Si hay que transformar filas que ya existen —como cuando los plazos del pool pasaron de días a
horas—, **la marca de «ya se hizo» va en `AppSettings`, no en una condición sobre los propios datos.**
Las dos condiciones que uno escribiría primero (`WHERE Col = 0`, `WHERE Col IS NULL`) están mal, y las
dos fallan igual: deshaciendo en silencio una decisión que alguien acababa de tomar. El caso completo
está razonado en `ConvertirPlazosDeDiasAHorasUnaVez`, dentro del migrador.
