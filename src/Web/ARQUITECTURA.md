# La arquitectura, y por qué es así

> Para quien hereda este código. Aquí está lo que **no** se deduce leyendo los archivos: qué hace
> cada capa, qué decisiones de estructura están tomadas a propósito, y dónde buscar cada cosa.
>
> Lo que sí está en el código no se repite: cuando algo ya está explicado en un comentario, esto
> enlaza al archivo. Un documento que copia comentarios miente en cuanto uno de los dos cambia.
>
> Si buscas el panorama general, empieza por [LEEME.md](LEEME.md). Si buscas el modelo de datos,
> [MODELO-DE-DATOS.md](MODELO-DE-DATOS.md). Si buscas cómo se prueba, [PRUEBAS.md](PRUEBAS.md). Y las
> decisiones que sorprenden al leer el código, con su porqué, están en [DECISIONES.md](DECISIONES.md).

## Lo primero que hay que entender

Esto es la **réplica web de una aplicación de escritorio que ya existía y que seguía en producción**
mientras se construía. Casi todo lo raro que encuentres se explica con esa frase:

- Los servicios de negocio están **copiados y adaptados**, no reescritos. Conservan los nombres, los
  mensajes de rechazo y hasta los `ExecuteUpdate` condicionales del original.
- Las entidades, las tablas y los nombres de columna son **los del escritorio**, en inglés, mientras
  que todo lo nuevo está en español. Esa mezcla no es descuido: renombrar una columna habría roto la
  aplicación que estaba en producción.
- Hay piezas que existen solo para que las dos aplicaciones pudieran convivir hasta el corte
  ([ProtectorPortable](AdminWeb.Infrastructure/Seguridad/ProtectorPortable.cs), los trabajos de fondo
  apagados por omisión), y que están marcadas como retirables después.

El corte ya está descrito paso a paso en [EL-CORTE.md](EL-CORTE.md); esto no lo repite.

---

## El mapa

Seis proyectos y dos suites de pruebas. La tabla resumida está en
[LEEME.md](LEEME.md#estructura); lo que sigue es lo que esa tabla no dice.

```
        ┌───────────────┐
        │ AdminWeb.Api  │  endpoints, auth, SignalR, trabajos de fondo, arranque
        └───┬───────┬───┘  (y hospeda los archivos del cliente)
            │       │
            │       └──────────────────────────┐
            ▼                                  ▼
  ┌──────────────────────┐            ┌──────────────────┐
  │ AdminWeb.Application │            │ AdminWeb.Client  │  Blazor WebAssembly
  └──────────┬───────────┘            └────────┬─────────┘
             │                                 │
             ▼                                 │
  ┌────────────────────────┐                   │
  │ AdminWeb.Infrastructure│                   │
  └──────────┬─────────────┘                   │
             ▼                                 │
      ┌───────────────┐                        │
      │AdminWeb.Domain│                        │
      └───────┬───────┘                        │
              ▼                                ▼
        ┌────────────────────────────────────────┐
        │             AdminWeb.Shared            │  sin dependencias
        └────────────────────────────────────────┘
```

La API referencia al cliente **a propósito**: lo sirve como archivos estáticos, así que todo viaja
en el mismo origen. Eso es lo que permite autenticar con una cookie `HttpOnly` en lugar de un token
que el JavaScript de la página pueda leer.

### Las dos fronteras, y solo una es dura

**La dura: el cliente solo ve `Shared`.** Está declarada en
[AdminWeb.Client.csproj](AdminWeb.Client/AdminWeb.Client.csproj) y no tiene excepciones. Si alguien
añade ahí una referencia a `Domain` o a `Infrastructure`, el navegador de cada persona acaba
descargando el modelo de datos y la lógica de negocio completos: las reglas de puntuación, los
cálculos de vacaciones, las guardas de autorización y la forma exacta de cada tabla. **Nada de eso
falla al compilar** — la aplicación seguiría funcionando perfectamente, solo que repartiendo por la
red lo que no debe salir del servidor.

Cómo comprobarlo en diez segundos:

```powershell
Select-String -Path AdminWeb.Client\AdminWeb.Client.csproj -Pattern "ProjectReference"
```

Tiene que salir **una sola línea**, la de `AdminWeb.Shared`.

**La blanda: `Application` depende de `Infrastructure`.** Va al revés de lo que dice el manual, y es
deliberado. Está razonado abajo.

---

## Por qué `Application` depende de `Infrastructure`

En una arquitectura limpia de libro, la capa de aplicación define interfaces de repositorio y la de
infraestructura las implementa; la flecha apunta hacia dentro. Aquí no: los servicios reciben el
`AppDbContext` por constructor y consultan con LINQ directamente contra los `DbSet`.

**Se decidió así y está escrito en el propio
[AdminWeb.Application.csproj](AdminWeb.Application/AdminWeb.Application.csproj).** El razonamiento
completo:

1. **Lo que se hizo fue un port, no un diseño nuevo.** Los servicios vienen del escritorio, donde ya
   consultaban el contexto directamente. Copiarlos y adaptarlos a `async`/scoped costó lo que costó;
   reescribirlos contra repositorios habría sido otro proyecto entero, con la aplicación de verdad
   corriendo en producción mientras tanto.
2. **Habría que abstraer casi setenta `DbSet`.** No son cinco agregados bien delimitados: son setenta
   tablas con consultas que cruzan media docena cada una (el tablero, las métricas, la capacidad, los
   reportes). Un repositorio por tabla sería una capa de reenvío sin valor, y un repositorio
   por caso de uso sería mover los servicios de sitio y llamarlo otra cosa.
3. **Lo que las interfaces darían de verdad ya está.** El motivo habitual para invertir la
   dependencia es poder probar sin base de datos. Aquí las pruebas corren contra **SQLite de verdad**
   ([PRUEBAS.md](PRUEBAS.md)), que es más fiel que cualquier doble y no cuesta nada. Y lo que sí
   convenía abstraer —lo que sale a la red y lo que no se puede ejecutar en una prueba— **sí está
   detrás de interfaces**: `IClienteAzureDevOps`, `IApiDeFreshdesk`, `IClienteDeCorreo`,
   `IClienteDeBlobs`, `IPublicacionDeDespliegue`, `IGeneradorDeDocumentos`,
   `IPlantillaDeVacacionesEnWord`, `IEnvioDeAvisosPush`, `IDibujanteDeCodigoQr`,
   `IProtectorDeSecretos`, `ICurrentUser`, `IRequestOrigin`.

**El precio, dicho sin adornos:** cambiar de motor de base de datos o de ORM tocaría casi todos los
archivos de `AdminWeb.Application/Services/`. Se aceptó porque ese cambio no está en el horizonte —la
base es la misma que lleva años en producción— y porque el coste de la alternativa era inmediato y
seguro.

**Lo que sí se respeta sin excepción es la otra frontera.** Que `Application` vea `Infrastructure` es
un detalle interno del servidor. Que el navegador viera cualquiera de las dos sería un problema de
verdad, y ese sí no se negocia.

---

## Dónde vive cada cosa

La pregunta real de quien hereda esto no es «cuántas capas hay» sino «dónde está lo que busco».

| Si buscas… | Está en… |
|---|---|
| El mapeo de una tabla, sus índices y su comportamiento de borrado | [AdminWeb.Infrastructure/Data/AppDbContext.cs](AdminWeb.Infrastructure/Data/AppDbContext.cs) |
| Los parches que ponen al día una base que ya existe | [AdminWeb.Infrastructure/Data/DatabaseMigrator.cs](AdminWeb.Infrastructure/Data/DatabaseMigrator.cs) |
| Una entidad y qué significa cada campo | `AdminWeb.Domain/Entities/` |
| Una regla de negocio | `AdminWeb.Application/Services/` |
| Una regla de cálculo pura, sin base de datos | `AdminWeb.Domain/Calculo/`, `AdminWeb.Domain/Security/` |
| Qué ruta HTTP existe, con qué política y qué recibe | `AdminWeb.Api/Endpoints/` |
| Lo que viaja entre servidor y navegador | `AdminWeb.Shared/Dtos/` (una carpeta por área) |
| Lo que corre solo, sin nadie delante | `AdminWeb.Api/Jobs/` |
| Lo que pasa al arrancar (migrar, sembrar, llavero, registro) | `AdminWeb.Api/Arranque/` |
| Lo que decide si una petición pasa (contraseña, segundo factor, identidad) | `AdminWeb.Api/Auth/` |
| Lo que habla con el mundo exterior | `AdminWeb.Infrastructure/Integraciones/` |
| Cómo se maqueta un documento | `AdminWeb.Infrastructure/Documentos/` y `AdminWeb.Domain/Documentos/` |
| Una pantalla | `AdminWeb.Client/Paginas/` (63 rutas, una carpeta por área) |
| El menú por rol | [AdminWeb.Client/Navegacion/Menu.cs](AdminWeb.Client/Navegacion/Menu.cs) |
| Lo que hace el cliente con las respuestas raras (401, 403, sesión caducada) | [AdminWeb.Client/Servicios/ManejadorDeRespuestas.cs](AdminWeb.Client/Servicios/ManejadorDeRespuestas.cs) |
| Los guiones de los recorridos guiados de cada pantalla | `AdminWeb.Client/Recorridos/Guiones/` |
| El texto del manual de uso que se siembra en la base de conocimiento | `AdminWeb.Application/Manual/` |
| Los datos de demostración | `AdminWeb.Application/Demo/` |

### Dos cosas que despistan al buscar

- **Hay dos familias de servicios en `Application`.** Los `*Service` son los portados del escritorio:
  escriben, validan y devuelven `(bool ok, string mensaje)`. Los `*QueryService` son **propios de la
  web** y no existen allá: agrupan en una sola respuesta todo lo que una pantalla necesita, para que
  el navegador no tenga que encadenar cinco peticiones y armar el resultado por su cuenta. Si buscas
  una regla, está en un `*Service`; si buscas de dónde salen los datos de una pantalla, en un
  `*QueryService`.
- **Los nombres están en dos idiomas y eso es información.** En inglés = viene del escritorio y se
  conserva por compatibilidad. En español = es nuevo de la web. `WorkSession` es del escritorio;
  `SaldoDeVacacionesService` no existe allá.

---

## El camino de una petición

Importa porque **el orden del middleware es una decisión**, no una casualidad, y moverlo compila sin
quejarse. Está declarado en [AdminWeb.Api/Program.cs](AdminWeb.Api/Program.cs):

```
UseExceptionHandler                 traduce las excepciones del dominio a códigos HTTP
  ↓
UseHsts / UseHttpsRedirection       solo fuera de desarrollo
  ↓
UseBlazorFrameworkFiles             _framework/… (nombres con huella, caché eterna)
  ↓
(reescritura de la fuente de Radzen)
  ↓
UseStaticFiles                      nuestros archivos, con «no-cache, must-revalidate»
  ↓
UseAuthentication                   lee la cookie y arma los claims
  ↓
UseAuthorization                    aplica la política del endpoint
  ↓
UseContrasenaObligatoria            ¿arrastra contraseña temporal?     ← necesita los claims
  ↓
UseSegundoFactorObligatorio         ¿le falta el segundo factor?       ← DESPUÉS de la contraseña
  ↓
UseAntiforgery                      valida el testigo de las subidas   ← DESPUÉS de autenticar
  ↓
los endpoints
```

Los tres «después» son la parte que hay que respetar:

- **El segundo factor va después de la contraseña**, y ese orden evita un secuestro de cuenta real.
  El razonamiento entero está en
  [SegundoFactorObligatorio.cs](AdminWeb.Api/Auth/SegundoFactorObligatorio.cs); en corto: la
  contraseña temporal se dicta por chat, y quien la haya visto no debe poder dar de alta *su*
  teléfono en la cuenta ajena.
- **El antiforgery va después de autenticar** porque el testigo se ata a la identidad de la sesión.
- **`UseAntiforgery` tiene que estar.** Sin esa línea, el `AddAntiforgery` de arriba queda como una
  configuración que *parece* protección y no valida nada — peor que no tenerla.

### Y al final, la autorización, que son tres capas y no una

| Dónde | Qué es | Qué protege |
|---|---|---|
| [Menu.cs](AdminWeb.Client/Navegacion/Menu.cs), en el navegador | Comodidad | Nada. Corre en la máquina de cada persona y se puede manipular |
| La política del endpoint (`RequireAuthorization("SoloAdmin")`) | **La barrera** | Toda llamada a la API, venga del navegador o de `curl` |
| [AuthorizationGuard](AdminWeb.Domain/Security/AuthorizationGuard.cs), dentro del servicio | **La segunda barrera** | La regla vive donde está el dato; sobrevive a que alguien añada un endpoint y olvide la política |

Hay tres políticas y se llaman igual en los dos lados (`SoloAdmin`, `AdminUOperaciones`,
`AdminUDesarrollador`). Que el cliente las declare no duplica la seguridad: solo permite que una
página diga `[Authorize(Policy="…")]` y no se pinte.

**Un rol no reconocido se queda sin menú y sin permisos, a propósito.** Escrito por descarte («si no
es admin…»), cualquier rol futuro heredaría accesos en silencio — que es un error que ya se cometió
una vez.

---

## Lo que corre fuera de una petición

Esta es la diferencia de fondo con el escritorio, donde **todo lo periódico dependía de que alguien
tuviera la aplicación abierta**. Si nadie la tenía, no pasaba y nadie se enteraba.

| Pieza | Vive como | Cada | Qué hace |
|---|---|---|---|
| `BarridoDePresenciaJob` | `BackgroundService` | 1 min | Cierra las jornadas que dejaron de latir |
| `ConsolidacionDeCronometrosJob` | `BackgroundService` | 1 min | Cierra los cronómetros huérfanos hasta su último latido |
| `EscalamientoDeSlaJob` | `BackgroundService` | 15 min | Escala los compromisos vencidos |
| `IngestaDeCorreoJob` | `BackgroundService` | 10 min | Convierte correos en requerimientos |
| `ResumenDiarioJob` | `BackgroundService` | 30 min | Manda el resumen del equipo cuando toca |
| `DesplieguesProgramadosJob` | `BackgroundService` | 1 min | Dispara las citas de despliegue |
| `EjecutorDeDespliegues` | **Singleton** | — | Ejecuta un despliegue lanzado desde una pantalla |
| `AppHub` (SignalR) | Hub | — | Presencia y latido del cronómetro |
| `RegistroDeConexiones` | **Singleton** | — | Cuántas pestañas tiene abiertas cada quien |

Tres cosas que hay que saber de esto:

1. **Los trabajos están APAGADOS por omisión** (`AdminWeb:TrabajosDeFondoActivos`). Ver
   [DECISIONES.md](DECISIONES.md#los-trabajos-de-fondo-nacen-apagados).
2. **Todo lo que corre fuera de una petición abre su propio ámbito** (`IServiceScopeFactory`). El
   `AppDbContext` es scoped y no hay petición de la que colgarse. La base común está en
   [TrabajoPeriodico.cs](AdminWeb.Api/Jobs/TrabajoPeriodico.cs), que además captura los errores de
   cada vuelta: un fallo de red no puede matar el trabajo, o dejaría de vigilarse hasta el siguiente
   despliegue.
3. **El ejecutor de despliegues es singleton a propósito**: el trabajo sobrevive a la petición que lo
   lanzó, así que cerrar la pestaña no lo cancela. En el escritorio corría dentro del `.exe` de quien
   pulsaba el botón y cerrarlo a media subida lo dejaba a medias.

**Con una sola instancia esto funciona tal cual.** Si algún día la API escala a más de una, dos cosas
dejan de ser ciertas y hay que resolverlas juntas: el hub de SignalR necesita Azure SignalR (una
línea, pero decidirlo tarde es caro) y los trabajos de fondo pasarían a correr por duplicado.

---

## El arranque, que es donde más cosas pueden salir mal

Todo está en [PreparacionDeLaBase.cs](AdminWeb.Api/Arranque/PreparacionDeLaBase.cs) y ocurre en este
orden:

1. **Crear la base si no existe.** Fuera del candado, porque un candado vive *dentro* de una base:
   pedirlo en una que no existe falla con el error 4060, que no se parece en nada al problema real.
2. **Abrir la conexión a mano y tomar `sp_getapplock`.** Abrirla explícitamente no es cosmética: el
   candado se ata a la conexión, y EF toma prestada una del pool por operación. Sin fijarla, el
   candado se pedía en una conexión, la migración corría en otra —sin protección— y el `release` en
   una tercera. **Compilaba, no daba error y no protegía nada.**
3. **Migrar.** Quien no consigue el candado no migra y arranca igual: la otra instancia ya dejó la
   base al día.
4. **Mirar lo que devolvió el migrador.** Devuelve la lista de sentencias que fallaron, y una lista
   vacía es la única señal honesta. Antes no devolvía nada y un migrador que se saltó 114 sentencias
   anunció «Esquema al día» durante días.
5. **Sembrar el administrador inicial** si no hay ninguna cuenta. Su contraseña temporal se escribe
   en el registro una sola vez, porque el arranque no tiene a nadie delante.
6. **Sembrar los catálogos con los que la aplicación tiene que arrancar** (criterios de puntuación,
   plantillas, configuración del pool), en el mismo orden que el escritorio. Un fallo aquí queda en
   el registro pero **no** tumba el arranque: sin catálogo inicial la aplicación funciona, y negarse
   a arrancar dejaría al equipo fuera por un dato de conveniencia.
7. **Sembrar el manual de uso** en la base de conocimiento, dentro del mismo candado. No lleva las
   guardas de los datos de demostración a propósito: es **contenido del producto**, como los
   criterios de puntuación, y va en cualquier base y en cualquier entorno. Solo inserta lo que nunca
   se ha sembrado, así que una corrección que alguien haya hecho sobre un artículo **sobrevive a
   todos los arranques siguientes**. Lo normal es que no agregue nada.

**La única cosa que sí tumba el arranque a propósito** es que haya una huella de certificado
configurada para el llavero y ese certificado no aparezca. El porqué está en la cabecera de
[Llavero.cs](AdminWeb.Api/Arranque/Llavero.cs) y resumido en
[DECISIONES.md](DECISIONES.md#el-llavero-va-cifrado-con-un-certificado-y-si-falta-la-aplicación-no-arranca).

---

## La trampa que ya costó una vez

**Un servicio que falta en el contenedor no se nota al compilar.** Un parámetro de endpoint cuyo tipo
no está registrado se interpreta como cuerpo de la petición, y la aplicación revienta al **arrancar**
con `Body was inferred but the method does not allow inferred body parameters`.

Ni el compilador ni las pruebas de servicios lo ven. Lo atrapan las pruebas de la API y la prueba de
humo, porque las dos levantan la aplicación de verdad.

**Si añades un endpoint: registra su servicio en [Program.cs](AdminWeb.Api/Program.cs) y pasa
`humo.ps1`.** Es la regla más barata de este repositorio.

---

## El cliente, en tres párrafos

**Un solo `HttpClient` con manejador, y otro sin él.** El que usan las pantallas lleva
[ManejadorDeRespuestas](AdminWeb.Client/Servicios/ManejadorDeRespuestas.cs), que atiende en un solo
sitio las respuestas que no son datos sino situaciones: sesión caducada, contraseña sin cambiar,
segundo factor pendiente. El de la consulta de sesión (`/api/auth/me`) va **sin** manejador, y no es
una excepción caprichosa: además de que un 401 ahí es la respuesta y no una expulsión, ponerlo
crearía una dependencia circular que **cuelga el hilo único de WebAssembly sin lanzar ninguna
excepción** — pantalla en «Cargando…» para siempre y nada en la consola. Está explicado en
[Program.cs del cliente](AdminWeb.Client/Program.cs).

**El recorte de ensamblados está apagado**, y de eso dependen cosas: los recorridos guiados se
descubren por reflexión, y la serialización de los DTO y los componentes de Radzen también se
alcanzan por ahí. El porqué —y qué habría que hacer para recuperarlo— está en
[AdminWeb.Client.csproj](AdminWeb.Client/AdminWeb.Client.csproj).

**Los recorridos guiados viven en C#, no en JavaScript.** Cada paso apunta a un control por una marca
`data-recorrido="…"`, y una prueba comprueba que todas esas marcas siguen existiendo en el marcado de
su pantalla. Escribir los guiones en el cliente y en C# es justo lo que permite esa prueba; en
JavaScript se pudrirían en silencio. Ver [PRUEBAS.md](PRUEBAS.md#los-recorridos-guiados).

---

## Reglas de la casa

Lo que hay que respetar aunque nada falle al compilar:

1. **El cliente no referencia más que `Shared`.**
2. **Toda columna nueva del modelo necesita su parche en el migrador, en los dos dialectos.** Ver
   [MODELO-DE-DATOS.md](MODELO-DE-DATOS.md#cómo-se-cambia-el-esquema).
3. **Todo endpoint nuevo lleva su servicio registrado**, y se comprueba levantando la aplicación.
4. **Toda operación sensible lleva su guarda dentro del servicio**, además de la política del
   endpoint.
5. **Nada de emoji ni de símbolos en los textos que escribe el servidor.** Ver
   [DECISIONES.md](DECISIONES.md#los-emoji-están-fuera-de-la-interfaz).
6. **Ningún icono que no esté en
   [wwwroot/fuentes/iconos.txt](AdminWeb.Client/wwwroot/fuentes/iconos.txt)**: la fuente está
   recortada y un nombre que no esté ahí se pinta como palabra.
7. **Ningún color escrito a mano**: variables del tema.
8. **Nada de `MarkupString` ni de `innerHTML` sobre contenido escrito por alguien.** El foro y la
   base de conocimiento llegan al navegador troceados por el servidor y ya validados; el cliente los
   emite con `@`, que Blazor escapa. Las imágenes incrustadas en un artículo llegan como un **número**
   y no como una dirección: la ruta la arma el cliente con ese entero, así que en un `src` no acaba
   nunca una cadena que haya tecleado una persona.
