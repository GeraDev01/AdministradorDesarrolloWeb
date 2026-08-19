# Un solo camino para el trabajo del desarrollador

> **Estado: PROPUESTA, sin aprobar y sin empezar.** Es el análisis de viabilidad de unificar el
> trabajo del desarrollador en una sola pantalla y un solo camino de puntos. Nada de lo que hay aquí
> está implementado. Vive en el repositorio para poder retomarlo desde otra máquina.
>
> Lo que sí está implementado y va en el commit anterior a éste son los ocho arreglos que dieron
> origen al análisis (paginación, capacidad de trabajo, «la hice yo», eliminar del pool, retrabajo,
> detalle de permisos, columnas redimensionables y el detalle del desempeño).


## Contexto

Hoy un desarrollador tiene que saber **por dónde entra cada cosa y por dónde se cobra cada cosa**, y
ninguna de las dos respuestas es única. El propio manual lo admite sin querer: el artículo «De dónde
llega el trabajo» enumera **cinco puertas**, y el de «Desempeño» abre diciendo «hay tres caminos» y
a continuación lista **cuatro**.

**Ocho pantallas de trabajo para el mismo rol** (`Menu.cs:264-280`): `/mi-panel` · `/mi-pool` ·
`/mis-asignaciones` · `/mis-tickets` · `/mis-actividades` · `/mis-evaluaciones` · `/mis-sla` ·
`/mi-jornada` · `/sprint`.

**Cuatro caminos vivos de puntos:**

| Camino | Quién | Nace | Dónde |
|---|---|---|---|
| Autocalificación | el desarrollador | `Pendiente` → cola | `PerformanceScoringService.cs:213` |
| Pool aceptado | el líder | `Aprobado` (matriz congelada) | `PoolActivityService.cs:1102` |
| Actividad libre calificada | el líder | `Aprobado` (valor a mano) | `DevActivityService.cs:175` |
| Artículo de conocimiento | el líder | `Aprobado` (una vez) | `ConocimientoService.cs:357` |

Y **tres listas de criterios distintas** sobre el mismo catálogo de 98.

### Lo mismo aparece en varias pantallas a la vez

Un work item de DevOps puede estar simultáneamente en `/mis-tickets`, en `/mis-asignaciones` (como
`Requirement`, `DevOpsService.cs:1128`), en `/mis-sla` (`:1152`) y en `/mi-pool` — **con dos
estimaciones distintas y ningún puente**. El mismo requerimiento sale en cuatro rejillas
(`Sprint.razor:411`: *«Ésta es la TERCERA vista del mismo enum»*). Y `ActividadLibreDto` significa
dos cosas distintas en dos espacios de nombres.

---

## Tres fallos reales, verificados leyendo el código

Ninguno de los tres es hipotético. Los tres los cierra este trabajo.

**1 · La percha se puede cerrar, renombrar o borrar desde otra pantalla.** La guarda del pool solo
está en `DevActivityService.CalificarAsync:147-153`; el embudo común `ObtenerPropiaAsync:426` no la
tiene, así que `CerrarAsync`, `ReabrirAsync`, `RenombrarAsync` y `EliminarAsync` (`:372-424`) la
dejan pasar. Hoy un desarrollador puede **borrar el cronómetro de su propia actividad del pool**
desde `/mis-actividades`, y `LinkedDevActivityId` queda apuntando al vacío.

**2 · La percha devuelta se puede cobrar dos veces.** `DevolverAsync:934-944` guarda el id, llama a
`SoltarReclamo` —que pone `LinkedDevActivityId = null` (`:1605`)— y **luego** cierra la percha.
Queda una `DevActivity` **cerrada, con tiempo medido y sin `PointEntryId`, a la que ya no apunta
ninguna `PoolActivity`**. La guarda de `CalificarAsync` pregunta
`AnyAsync(p => p.LinkedDevActivityId == activityId)` y contesta **false**. El líder puede calificarla
por puntos; cuando otro termine la actividad, **el pool paga otra vez por el mismo trabajo**.
`LiberarAsync:706-731` tiene la misma forma.

**3 · El cronómetro firma con dos cuentas distintas** *(lo que reportaste)*. **Verificado y
documentado como concesión en dos sitios**, no es un descuido:

- **Al arrancar** → `AvisoDeInicioEnDevOpsService`. Su propia cabecera lo dice: *«Sin `ICurrentUser`,
  y es deliberado: la mitad de sus llamadas vienen de un ámbito de fondo donde no hay sesión… Por eso
  las credenciales son las de la instalación. El precio: en DevOps el comentario aparece firmado por
  la cuenta compartida, así que el nombre de quien empezó va DENTRO del texto.»*
- **Al detener** → `JornadaEndpoints.ReportarADevOpsAsync:197` → `DevOpsService.ReportarTiempoAsync`
  → `CredencialesAsync(exigirPropio: false)`, que **busca primero el token personal**. Su comentario:
  *«lo que se publica queda firmado con su token en DevOps, y sumar el rato de otro dejaría horas
  ajenas a su nombre».*

Es decir: la cuenta compartida es la que tiene el PAT de la instalación —la tuya— y por eso el
«empezó a trabajar» sale a tu nombre y el «tiempo registrado» al del desarrollador.

**El arreglo correcto es invertir la dependencia, no quitar el barrido.** Que las credenciales las
**reciba** `AvisoDeInicioEnDevOpsService` en lugar de resolverlas: el endpoint —que sí tiene sesión—
le pasa las del desarrollador, y el barrido de fondo le pasa `null` y cae a las de la instalación,
que es el único caso en que no hay alternativa (un cronómetro arrancado desde el escritorio). Así las
dos puntas firman igual siempre que el arranque venga de la web, sin romper lo que el barrido
necesita. Y encaja aquí: la pantalla única deja **un solo sitio desde el que se arranca**.

---

## Veredicto de viabilidad

| Pieza | Viabilidad | Por qué |
|---|---|---|
| **Una sola pantalla** | **Alta** | Composición de consultas que ya existen. No toca el modelo. |
| **Un solo camino de puntos** | **Alta** | `PointEntry` ya es el único sitio donde viven los puntos: apagar productores no cambia el ranking, ni los reportes, ni el PDF. **No se migra ni una fila.** |
| **El desarrollador propone** | **Media** | `PorClasificar` ya significa «falta clasificarla». Solo tiene que aprender a nacer **con dueño**. |
| **El descuento del líder** | **Media** | Estado nuevo + una vía de pago que reaprovecha la transacción de `AceptarAsync`. |
| **Criterio «apliqué un artículo»** | **Media** | Dos columnas en una tabla que **crea SQL crudo**: tres sitios, no dos. |
| **Firma única del cronómetro** | **Media-alta** | Invertir la dependencia de credenciales. |
| **Fundir `PoolActivity` con `Requirement`** | **No** | Ver abajo. |
| **Quitar la percha** | **Después del corte** | Ver abajo. |

### No fundir el pool con el backlog

`MODELO-DE-DATOS.md:61-73` ya lo dejó escrito, y las cifras lo respaldan: `Requirement` reparte entre
**N personas** por `Assignment`, el pool tiene **reclamo único**; `CommittedDeliveryDate` es una
fecha prometida al cliente y `HorasLimite` un presupuesto que empieza a correr al tomar; y
`db.Requirements` lo consultan **20 servicios** contra 11 del pool. Absorber el backlog multiplica
por dos la superficie del pool y deja 20 servicios pidiendo un filtro que alguien olvidará. **La
petición no lo exige**: exige que el desarrollador no elija entre listas.

### No quitar la percha ahora — y la razón buena, no la que yo creía

Hay que corregir un argumento: **la restricción del motor NO aplica** en esta dirección.
`MODELO-DE-DATOS.md:194-206` explica que los enlaces del pool van sin FK porque serían segundas
rutas de cascada **desde `Developers`**; pero `WorkSessions → PoolActivities` es una arista
**entrante**, y con `OnDelete(NoAction)` —lo que ya hace `WorkSession.Activity`— SQL Server la
acepta. El tercer objetivo *podría* llevar FK de verdad.

Se aplaza por tres razones mejores:

1. **Es lo único que escribiría en `WorkSessions`**, la tabla del tiempo, el mismo día en que el
   escritorio todavía es la marcha atrás (`EL-CORTE.md` paso 8).
2. **El relleno de las perchas ya devueltas es imposible hoy**: `SoltarReclamo` ya borró el vínculo,
   así que no queda de dónde rellenar. Necesita que `DevActivity.PoolActivityId` lleve **meses**
   escribiéndose para tener un relleno exacto en vez de una heurística sobre el título.
3. **Todos los lectores de `ActivityId` tendrían que aprender el tercer objetivo a la vez** —
   `WorkSessionService` (siete métodos), `JornadaQueryService`, `ReportesService`,
   `WorkItemDeLaSesion`, los dos relojes copiados a mano—. Olvidar uno **no lanza excepción: el
   tiempo deja de contarse en silencio** hasta que cierra el mes. Es el peor modo de fallo que hay.

---

## Decisiones tomadas

1. Una sola pantalla **y** un solo camino de puntos; el pool es la unidad de trabajo.
2. El desarrollador **propone**; el **líder pone el valor**.
3. Requerimientos, tickets y SLA son **fuentes**, no listas.
4. Conocimiento: sigue pagando por escribir, como **la única excepción declarada**.
5. Negativos: el líder publica un **descuento** al pool.
6. Criterio extra «aplicaste una práctica documentada», **con el artículo elegido de una lista**.
   Aplicar un artículo propio **sí cobra**.
7. **Despliegue de un tirón.** Los tramos son orden de construcción, no de despliegue.

---

## El diseño

### A. La pantalla única — sin ruta nueva

`/mi-pool` se queda; cambian el **rótulo del menú** («Mi trabajo»), el `PageTitle` y el `H5`. Una
ruta nueva obligaría a un recorrido nuevo y a reescribir rutas en el manual a cambio de nada, y
dejaría con 404 los avisos que guardaron `"pool"` como destino.

«Lo mío» pasa de **9 entradas a 4**: *Mi Panel* · **Mi trabajo** · *Mi jornada* · *Mis Evaluaciones*.
`/sprint` baja a «Herramientas». `/mis-asignaciones`, `/mis-tickets`, `/mis-actividades` y `/mis-sla`
**salen del menú y siguen vivas**: `[Authorize]` intacto, recorrido intacto, enlaces profundos
intactos — así `PuertaDeLasPantallasTests` y `RecorridosCoberturaTests` siguen verdes sin tocar sus
listas de exentas.

Cuatro secciones:

1. **Ahora** — `Tomada` / `Devuelta`. Checklist, plazo, **esfuerzo y medido en columnas separadas**,
   motivo de la última devolución, panel de DevOps, y **el cronómetro: único, aquí, y en ningún otro
   sitio**. Hoy el reloj está copiado a mano en dos pantallas; con una sola hay una sola copia.
2. **Tomar** — el pool disponible, con puntos y criterios extra **antes** de tomar.
3. **Propuestas** — botón *Proponer trabajo* y lo mío en `PorClasificar`, con el estado que se lee:
   «Esperando que el líder le ponga valor».
4. **Mis puntos** — banda compacta de solo lectura, con enlace a `/mi-panel`, que se queda.
   **Cero escrituras**: no hay nada que argumentar cuando el valor se fijó antes del trabajo.

**Consecuencia que hay que aceptar a sabiendas:** `/mis-asignaciones` es el único sitio donde el
cronómetro corre sobre un `Requirement`. Fuera del menú, el cronometraje de requerimientos muere
para el desarrollador. Bajo «el pool es la unidad de trabajo» es coherente —lo que hay que trabajar
se convierte en actividad del pool— y hay evidencia de apoyo: en los datos de demostración **el
100 % de las `WorkSession` cuelgan de `ActivityId` y ninguna de `RequirementId`**. Evidencia, no
prueba.

### B. Proponer

Nace `PorClasificar`, `Points = 0`, **`ClaimedByDeveloperId` = quien propone**. Ese reclamo es lo que
lo hace barato: `MisDelPoolAsync:171` la enseña sin tocar la consulta, `DisponiblesAsync:152` no,
`YaPublicada:362` la mantiene callada frente a DevOps, `TomarAsync:766` la rechaza, y `PoolAdmin`
ya la enseña con «Clasificar / Descartar».

`ProponerActividadRequest(Titulo, Detalle?, Enlace?, WorkItem?)` — **sin tipo, sin complejidad, sin
horas, sin puntos y sin persona**: el mismo argumento que sostiene `PublicarActividadRequest` sin
puntos (`PoolDtos.cs:269`).

**`EditarAsync:469` pasa a ser bimodal**: sin reclamo → `Disponible` (como hoy); con reclamo →
**`Tomada`**, materializando plazo, checklist y percha con un privado extraído de `TomarAsync:868`.
*Es la línea del plan que más merece una segunda lectura, y la regresión —que lo venido de DevOps
siga yendo a `Disponible`— es prueba obligatoria.*

Guardas: no se propone a nombre de otro (sin `devId` en la ruta + `RequireOwnershipOrAdmin`); **las
propuestas cuentan dentro del tope de tomadas** (`TomarAsync:781` hoy suma `Tomada` + `Devuelta`; sin
sumarles `PorClasificar` con reclamo, alguien acaba con 6 vivas y el tope diciendo 3); título ≤ 200,
detalle ≤ 4000, enlace http/https, y un work item no puede tener ya una actividad viva.

Si el líder la rechaza: `RetirarAsync:531` gana `motivo` y aviso. Una propuesta rechazada en silencio
mata la función en una semana.

### C. El descuento — y la pieza que hace verdad «un solo camino»

**Estado nuevo `Descuento`, al final del enum.** Se consideró reutilizar `Aceptada` con un
discriminador; se descarta porque haría que **todas** las consultas que hoy filtran `Aceptada`
—`DesempenoQueryService:213`, los reportes del pool, el detalle del mes— tuvieran que aprender a
distinguir, y una lista de estados olvidada es el modo de fallo que este modelo teme más. Un valor
nuevo lo hace visible al compilador y a las pruebas; `PoolSeed.OrdenDeEstado` y `ColoresDeEstado`
tienen rama `_ =>`, así que degrada bien.

`PublicarDescuentoAsync(developerId, criterionId, puntos, titulo, motivo)`: solo admin; criterio
individual, activo, no `Pool: `, y **`DefaultPoints < 0`** (ese filtro es lo que conserva vivos los
41 negativos del catálogo); puntos resultantes `< 0` con suelo; **motivo obligatorio** —el pool nunca
lo exigió para pagar porque el checklist era la justificación, y aquí no hay entrega que mirar—;
bitácora y aviso, porque un descuento silencioso es la peor versión de esto.

**`EditarAsync` rechaza los descuentos.** Sin eso, cambiar tipo o complejidad los re-tasaría en
silencio desde una celda de matriz de la que nunca salieron.

> **El corazón del encargo:** extraer de `AceptarAsync:1125-1153` un privado `AbonarAsync` con la
> transacción, el `SaveChanges` y el `ExecuteUpdate` condicional sobre `PointEntryId == null`.
> `AceptarAsync` y `PublicarDescuentoAsync` lo llaman los dos. **Después de esto hay una sola pieza
> de código que convierte trabajo en puntos.**

`PublicarDescuentoAsync` **no comparte nada con `CrearAsync`**: no consulta matriz, no copia
checklist, no crea percha, no empuja a DevOps. Así las exclusiones dejan de ser condiciones que
alguien puede olvidar y pasan a ser código que no existe.

**Se puede deshacer.** `AnularDescuentoAsync` **no borra la `PointEntry`** —aquí nada que haya pagado
se borra—: escribe una **compensatoria** con el signo contrario, protegida contra la doble anulación
por `PoolActivity.AnulacionPointEntryId` (`int?`, sin FK, misma forma que `PointEntryId:272`).

**Descuento y `Retrabajo` no son lo mismo, y la regla cabe en una línea:** *si hay algo que hacer, es
`Retrabajo`; si no hay nada que hacer, es un descuento.* El retrabajo se toma, se cronometra, se
entrega y se verifica, y su número sale de la **matriz**; el descuento no lo toma nadie y su número
sale de un **criterio que nombra el hecho**. Ninguno hace innecesario al otro.

### D. El criterio «aplicaste una práctica documentada»

Criterio nuevo en `ScoringCriteriaSeed.Individuales` y alta en `PoolSeed.CriteriosExtraOfrecidos:208`
(de 8 a 9). **No rompe conteos**: `ScoringCriteriaSeedTests.TotalEsperado` se deriva de los arreglos
y `PoolCriteriosExtraTests:513` usa un rango.

`PoolActivityExtraCriterion` gana **`Justificacion`** (la escribe el desarrollador),
**`KnowledgeArticleId`** y **`KnowledgeArticleTitle`** (copia congelada). Sin FK — no por el motor
(`KnowledgeArticle` no tiene FK a `Developers`, así que una FK sería legal) sino **por consistencia
con la columna de al lado**: `ScoringCriterionId` va sin FK y «solo como traza» a propósito, para que
la fila siga explicando de dónde salieron unos puntos ya cobrados aunque el catálogo se depure. Dos
campos contiguos con doctrinas opuestas sería indefendible. Y el `Comment` **sigue siendo del
líder**: dos voces en una columna es lo que este repositorio evita en todas partes.

Se captura junto al checklist, **antes de entregar** (`EntregarAsync:991` gana una guarda con la
misma forma que la de la evidencia faltante). La lista sale de `GET /api/conocimiento/`, que ya
existe: **no se añade endpoint**. Solo artículos `Publicado`.

> **La pregunta incómoda, contestada:** ¿cobrar dos veces con un artículo propio? **No es doble pago
> y además ya está impedido.** Escribir se paga una vez y para siempre (`ConocimientoService.cs:310`);
> aplicar se paga cada vez, que es lo que se quiere premiar. Y **el desarrollador no puede añadirse
> este criterio**: los extras los elige el líder al publicar (`CrearAsync:257`). Si el líder no lo
> pidió, no hay nada que cobrar. Lo que sí se hace es que el panel de verificación **diga** «este
> artículo lo escribió la misma persona» — una derivación de una línea, y la diferencia entre una
> política que funciona y una que nadie aplica.

**Regalo casi gratis:** un `GROUP BY` sobre `IsMet = true` contesta por primera vez *qué artículos se
aplican de verdad*, y sobre todo **cuáles llevan un año publicados y no ha aplicado nadie**. Es la
primera señal de utilidad de la base de conocimiento que no es la opinión de su autor.

### E. Lo que se apaga, sin borrarse

`PerformanceScoringService.RegistrarAutocalificacionAsync:213` y `DevActivityService.CalificarAsync:129`
conservan su código y ganan **una guarda como primera línea**, que devuelve un rechazo de negocio
(400 con `ResultadoDto`) cuyo texto dice a dónde ir. **La guarda va en el servicio, no en el
endpoint** (`DECISIONES.md:466-538`).

**`EditarAutocalificacionAsync`, `ReplicarAsync` y `RevisionDePuntosService` entero NO se tocan**:
hay entradas pendientes y rechazadas ahí fuera, y cerrarlas dejaría conversaciones a medias y gente
con puntos en el limbo. La cola se drena sola.

Los criterios **dejan de ofrecerse pero no se retiran**: `CriteriosAsync:203` devuelve solo los
conservados y `CriteriosParaCalificarAsync:231` devuelve vacío. **`IsActive` no se toca en ninguna
fila** — apagar la oferta es de consulta; desactivar el catálogo rompería el histórico legible.

**Lo que se pierde, sin adornos:** el trabajo que no cabe en el pool y no viene de un ticket
—investigación, apagafuegos, ayudar a otro equipo— pierde su camino propio y pasa por proponer, que
es un viaje más largo para algo ya hecho. **Esto revierte de frente una decisión escrita hace muy
poco** (commit `15316a3`). Va en `DECISIONES.md` como reversión declarada.

### F. El conocimiento, excepción declarada

No se apaga, no se migra, no se toca una línea de `AprobarAsync`. Lo que se añade es **que quede
escrito por qué**, y el argumento es de modelo, no de política: la invariante fundacional del pool es
*el precio se fija antes de trabajar*, y un artículo no funciona así — no se encarga «escribe sobre X,
vale 8»; lo que vale es el artículo. El pool tasa **trabajo encargado**; el artículo es **producción
espontánea**, sin reclamo, sin plazo, sin checklist y sin cronómetro. Meterlo costaría cuatro
columnas anulables cuya única función sería decir «esta fila no es realmente del pool».

Va en tres sitios: el resumen XML de `AprobarAsync`, la tabla de reglas de `MODELO-DE-DATOS.md`, y el
manual. Sin lo primero, el que lea el código en seis meses lo tomará por un camino que se olvidaron
de apagar.

### G. La percha: marcarla, blindarla, esconderla

`DevActivity` gana **`PoolActivityId`** (`int?`, sin FK), escrita al crear la percha y **nunca
limpiada** — sobrevive a `SoltarReclamo`. Con eso:

- `CalificarAsync` pasa a preguntar por esa columna → **cierra el fallo 2** (la percha devuelta
  cobrable);
- **los cuatro métodos reciben la misma guarda en `ObtenerPropiaAsync`** → **cierra el fallo 1**.
  Ojo: `CerrarActividadEnlazadaAsync:1577` escribe la entidad directamente y no pasa por ese embudo,
  así que no se autobloquea;
- la percha deja de verse cuando `/mis-actividades` sale del menú.

Y esta columna es **el prerrequisito no desechable** del tercer objetivo que se aplaza: es la única
fuente posible de un relleno exacto el día que se haga. No es un parche: es la primera mitad del
modelo limpio, hecha cuando es segura.

### H. Lo que NO se toca

`PointEntry` como único lugar de los puntos · los puntos congelados · los enlaces sin FK ·
`PendienteDeEnviarADevOps` derivado · `Requirement`/`Assignment`/`Sprint` · las cuatro tablas del
tiempo separadas · la segmentación por `EquipoId` · el migrador aditivo · `TeamPointEntry` sin
productor · `DiasLimite` hasta después del corte.

---

## La pieza más barata y más importante

**Una prueba que escanee el código fuente y falle cuando aparezca un `new PointEntry(` fuera de
`PoolActivityService` y `ConocimientoService`.** Hay precedente exacto de prueba que lee archivos del
disco: `RecorridosCoberturaTests.cs`.

Sin ella, «un solo camino» es una frase en un documento y dentro de dos años vuelve a haber cuatro
—que es exactamente cómo llegaron los de hoy—. Con ella, es una invariante. **Si de todo este plan
solo se hiciera una cosa, sería ésta.**

---

## Recomendación pendiente de tu visto bueno: un interruptor de emergencia

`pool.puerta-unica` en `AppSettings`, sembrado por el migrador (patrón de
`vacaciones.caducidad-meses`, `DatabaseMigrator.cs:1537`) y visible en `/configuracion`.

**No es convivencia** —el corte sigue siendo único—: es la **marcha atrás sin recompilar y sin
desplegar**, la forma más fuerte de lo que exige `EL-CORTE.md`. Segundo efecto que pesa: apagado por
omisión en código, las **~16 pruebas** que hoy ejercitan la autocalificación y la calificación libre
siguen valiendo sin reescribirse. Sin él hay que invertirlas o borrarlas.

---

## Orden de construcción (un solo despliegue)

Un commit por tramo, cada uno con la suite verde, para conservar `git bisect` y el revert parcial.

| # | Tramo | Esfuerzo |
|---|---|---|
| T1 | Interruptor y siembra | S |
| T2 | Proponer (`ProponerAsync`, `EditarAsync` bimodal) | M |
| T3 | **La percha: `PoolActivityId` + las cinco guardas** (fallos 1 y 2) | S |
| T4 | Apagar autocalificación y calificación libre | M |
| T5 | Descuento + extracción de `AbonarAsync` | M |
| T6 | Criterio del conocimiento (columnas + migrador + pantallas) | M |
| T7 | **Firma única del cronómetro** (fallo 3) | S |
| T8 | Pantalla única (menú S · sección «De dónde viene» L) | L |
| T9 | La prueba de escaneo de productores de `PointEntry` | S |
| T10 | `DECISIONES.md`, `MODELO-DE-DATOS.md`, manual y glosario | M |

**Dependencias que no se pueden reordenar:**
- **T5 antes que T4**: apagar `CalificarAsync` sin el descuento deja al líder sin ninguna puerta de
  puntos negativos, con 41 criterios sin uso posible.
- **T3 antes que T8**: sacar `/mis-actividades` del menú sin blindar la percha deja los dos fallos
  igual de alcanzables y con menos ojos encima.
- **T2 antes que T8**, o la pantalla única se queda sin puerta de generación.

**El 80 % del valor está en T2 + T4 + T5 + T9 + el menú de T8.**

### La trampa de T6

`PoolActivityExtraCriteria` **no la crea EF sobre una base existente: la crea el migrador con SQL
crudo** (`DatabaseMigrator.cs:1184` SQLite, `:2656` SQL Server). Las columnas van en **tres** sitios:
la entidad (para `EnsureCreated`), **el cuerpo del `CREATE TABLE`** de las dos ramas, y un `ALTER`
idempotente en las dos ramas. Más `AppDbContext` con la longitud declarada. Es el fallo de
`Developers.TeamFunction` (`MODELO-DE-DATOS.md:302-308`), y si falla **rompe todo lo que lea la
tabla**, no solo la pantalla nueva.

---

## El histórico

**Regla absoluta: ningún tramo borra, actualiza ni reasigna una sola fila de `PointEntry`.** Solo
columnas nulables nuevas y una fila de `AppSettings`.

La única conversión de datos es el relleno de `DevActivity.PoolActivityId`, con el patrón de
`ConvertirPlazosDeDiasAHorasUnaVez:1579`: transacción, SQL crudo (nunca el `ChangeTracker`), marca en
`AppSettings`, tope defensivo y excepción tragada. Dos pasadas: exacta para las que aún tienen
vínculo vivo, y **`-1` = «fue percha, no sé de cuál»** para las huérfanas que se reconocen por el
título `Pool #%` que solo escribe `CrearActividadEnlazada:1559`. El `-1` basta para lo único que la
marca tiene que hacer —bloquear las cinco operaciones y esconderla— sin inventar un vínculo
irrecuperable.

**Prueba que lo fija** — `HistoricoIntactoTests.cs`: sembrar entradas de las cuatro procedencias y
comprobar que el ranking de un mes pasado da **el mismo número** con la puerta apagada y encendida.
Es lo que impide que alguien «limpie» el catálogo dentro de seis meses.

---

## Riesgos

| # | Riesgo | Mitigación |
|---|---|---|
| R1 | **De un tirón: si algo falla no se sabe qué tramo fue.** | Un commit por tramo con la suite verde. Ensayo (`ensayo.ps1`) contra copia con datos y humo contra SQL Server real —donde vive el 409 que SQLite no prueba— antes de desplegar. |
| R2 | **`EditarAsync` bimodal**: un olvido manda propuestas al pool a la vista de todos. | Una línea + la regresión de DevOps probada. |
| R3 | **La columna nueva en la tabla de SQL crudo.** | Tres sitios, no dos, + prueba de migración obligatoria. |
| R4 | El descuento nace terminal y se salta el ciclo entero. | `PublicarDescuentoAsync` no comparte nada con `CrearAsync`: las exclusiones son código que no existe, no condiciones que olvidar. |
| R5 | Se pierde el camino de puntos del trabajo que no cabe en el pool, revirtiendo una decisión reciente. | Reconocerlo en `DECISIONES.md`. El interruptor lo devuelve sin desplegar. |
| R6 | **Marcha atrás con descuentos publicados**: el código viejo no revienta (todas las traducciones tienen rama `_ =>`) pero **los lee como «Retirada»** con puntos negativos al lado. Sus `PointEntry` siguen contando bien. | Escrito en el plan de reversión: revisar a mano las filas del estado nuevo. |
| R7 | El cuello de botella se traslada al líder: todo lo que se autocalificaba hay que clasificarlo. | Medirlo desde el primer día con la cuenta de `PorClasificar`. |
| R8 | El barrido de fondo seguirá firmando con la cuenta compartida los cronómetros arrancados desde el escritorio. | Es el único caso sin alternativa. El nombre va dentro del texto, como hoy. Desaparece solo cuando el escritorio se apague. |

**Marcha atrás en tres escalones:** (1) apagar el interruptor desde `/configuracion`, minutos, sin
desplegar; (2) revertir solo el commit del cliente, que devuelve el menú; (3) `git revert` del merge,
seguro porque **no se borró ninguna columna, no se renumeró ningún enum, no se renombró nada y no se
migró un solo dato**.

---

## Verificación

- `dotnet build Administrador_Desarrollo_Web.slnx` y las dos suites verdes
  (`AdminWeb.Application.Tests` ~2 384 · `AdminWeb.Api.Tests` 149).
- **Archivos nuevos**: `PuertaUnicaDelPoolTests` (lo apagado rechaza; lo que sigue vivo sigue vivo) ·
  `DescuentoDelPoolTests` (incluida **la prueba de que el abono pasa por el mismo `AbonarAsync` que
  aceptar**) · `HistoricoIntactoTests` · `JustificacionDeConocimientoMigracionTests` (patrón de
  `FuncionDeEquipoMigracionTests`) · **`ProductoresDePuntosTests`** (el escaneo de `new PointEntry`).
- **Crecen**: `PoolActivityServiceTests` (proponer, clasificar a `Tomada`, **y la regresión de DevOps
  a `Disponible`**) · `PoolCriteriosExtraTests` · `EstadosDelPoolTests` · **`DevActivityTests` (los
  cinco métodos rechazan la percha, incluida la devuelta)** · `AvisoDeInicioEnDevOpsTests` (firma con
  el token personal desde la web, con el de la instalación desde el barrido) ·
  `AutorizacionEndpointsTests` · `ManualDeUsoTests`.
- **Intactas y es la señal de que nada se rompió**: `WorkSessionTests`, `WorkItemDeLaSesionTests`,
  `CronometroAjenoTests`, `JornadaQueryServiceTests` (~47 pruebas). Ése es el premio de conservar la
  percha.
- `RecorridosCoberturaTests` y `RecorridosGuiadosTests` verdes: cada `data-recorrido` nuevo con su
  `Paso`.
- **A mano, en la app**: proponer → clasificar como líder → marcar checklist y justificar con un
  artículo → verificar → comprobar que los puntos caen **una sola vez** y salen en `/mi-panel`.
  Publicar un descuento, anularlo, comprobar que quedan las dos entradas y el neto es cero. Y
  **arrancar y detener el cronómetro sobre un ticket, comprobando que los dos comentarios salen con
  la misma cuenta.**
