# Las decisiones que sorprenden

> Cosas de este repositorio que, leyendo el código, parecen un error, una omisión o una manía. No lo
> son: cada una está tomada a propósito y aquí está el porqué, con lo que costó y lo que costaría
> deshacerla.
>
> Se escribe esto porque **una decisión sin razón escrita se deshace sola**: llega alguien nuevo, ve
> algo raro, lo «arregla», y el daño aparece semanas después y en otro sitio.
>
> Cuando el razonamiento completo ya está en el código, aquí va el resumen y el enlace. El archivo
> manda; si alguna vez discrepan, el que está mal es este documento.

---

## El migrador son parches idempotentes, y no hay historial de EF

**Lo que sorprende:** no existe la carpeta `Migrations/`, no hay `__EFMigrationsHistory`, no se corre
`dotnet ef`. En su lugar hay un archivo de casi 2700 líneas
([DatabaseMigrator.cs](AdminWeb.Infrastructure/Data/DatabaseMigrator.cs)) que en **cada arranque**
ejecuta **todos** los parches acumulados, en los dos dialectos, envueltos en comprobaciones del tipo
«si la columna no existe, agrégala».

**Por qué:**

1. **Es lo que había.** El escritorio migraba así y llevaba años haciéndolo contra esta misma base.
   Introducir el historial de EF habría exigido generar una migración inicial que describiera un
   esquema que ya existía, y hacerlo mal deja la base marcada como migrada sin estarlo.
2. **Las dos aplicaciones compartieron base hasta el corte.** Con historial, la aplicación vieja
   habría visto una tabla que no entiende; sin él, cada una comprueba y agrega lo suyo.
3. **Es aditivo por construcción, y eso es lo que hizo posible la marcha atrás.** Nada se borra ni se
   renombra, así que el escritorio podía seguir abriéndose después del corte — ver el paso 8 de
   [EL-CORTE.md](EL-CORTE.md#8-si-hay-que-volver-atrás).

**El precio, que es real:** el arranque hace más trabajo del necesario, el archivo solo crece, y
**una columna sin parche no la detecta ninguna prueba** porque en una base nueva la crea
`EnsureCreated`. Ese último punto ya costó caro y está desarrollado en
[PRUEBAS.md](PRUEBAS.md#1-una-columna-sin-parche-pasa-en-verde).

**Dos cosas que hacen que esto sea sostenible y no una bomba:**

- **El migrador devuelve la lista de sentencias que fallaron.** Antes no devolvía nada y cada fallo se
  tragaba en silencio: el resultado fue un migrador que se saltó **114 sentencias** y anunció «Esquema
  al día». Una lista vacía es la única señal honesta.
- **Todo corre bajo `sp_getapplock`**, con la conexión fijada a mano. Sin eso, dos instancias
  arrancando a la vez comprueban al mismo tiempo que falta la misma columna. Ver
  [ARQUITECTURA.md](ARQUITECTURA.md#el-arranque-que-es-donde-más-cosas-pueden-salir-mal).

---

## El llavero va cifrado con un certificado, y si falta, la aplicación no arranca

**Lo que sorprende:** hay un caso en el que el arranque se niega a continuar. Solo uno, y en un
archivo cuyo resto degrada avisando.

**El contexto:** las llaves con las que se cifran los secretos por persona —el PAT de Azure DevOps y
el secreto del segundo factor— viven en un llavero de Data Protection. En Azure, ese llavero va en un
Blob, porque tiene que sobrevivir a los reinicios y ser el mismo para las dos instancias. Pero un Blob
en claro lo lee cualquiera con permiso de lectura sobre el contenedor, así que hay que cifrarlo.

**Y aquí está la decisión de fondo: no hay Key Vault en esta suscripción.** Lo normal sería cifrarlo
con una llave del vault, cuya privada no sale nunca de ahí. En su lugar se usa **un certificado**,
identificado por su huella. Funciona igual de bien para cifrar, con una diferencia importante: **los
certificados caducan y se rotan**, y las llaves ya escritas siguen necesitando el certificado con el
que se cifraron.

Por eso la configuración distingue el certificado **actual** (el que cifra) de una **lista de
anteriores** (que solo descifran). **Rotar es añadir, no sustituir**, y el procedimiento está en la
[guía de puesta en marcha](GUIA-DE-PUESTA-EN-MARCHA.md#rotar-el-certificado-del-llavero).

**Por qué tumbar el arranque, entonces.** Si hay una huella configurada, eso demuestra que ahí fuera
existe un llavero ya cifrado con ese certificado. Arrancar sin él no significaría «todavía sin
cifrar»: significaría **escribir llaves nuevas en claro al lado de otras que ya no se pueden abrir**.
Se pierde lo viejo y se expone lo nuevo, de una vez, mientras la configuración sigue diciendo que está
cifrado. Y nadie se queda fuera por negarse: el despliegue pasa por la ranura de ensayo y comprueba
`/api/health` antes de intercambiar, así que producción sigue corriendo la versión anterior. **El
coste real es un despliegue que no pasa, con el mensaje delante de quien lo lanzó.**

Que falte un certificado **anterior** no tumba nada, y la asimetría es exactamente ésta: el caso
mortal es **evitable**, y éste **ya ocurrió**. Negarse por el que cifra impide un daño que todavía no
se ha hecho; negarse por uno anterior no devuelve nada y solo añade una caída encima.

El razonamiento entero está en la cabecera de [Llavero.cs](AdminWeb.Api/Arranque/Llavero.cs).

---

## Los secretos de configuración se cifran con un esquema débil, a propósito

**Lo que sorprende:** [ProtectorPortable](AdminWeb.Infrastructure/Seguridad/ProtectorPortable.cs)
deriva su llave de una semilla que está **en el propio ensamblado**. Eso es ofuscación, no seguridad:
quien tenga el binario y acceso a la base recupera los secretos. Y hay **dos pruebas que leen el
código fuente del escritorio** para comprobar que la semilla y el prefijo coinciden byte por byte.

**Por qué:** hasta el corte, las dos aplicaciones leían y escribían **esas mismas filas**. Si la web
hubiera usado un cifrado propio —aunque fuera mejor—, el escritorio habría dejado de poder leer
cualquier secreto que alguien tocara desde el navegador. Y no habría fallado al guardar: habría
fallado semanas después, al desplegar, con un «530 User cannot log in» del servidor FTP.

Lo que de verdad protege esos valores es el acceso a la base. Esto solo evita que queden en claro para
quien abra una consulta de pasada.

**Cuándo se retira:** después del corte, sustituyéndolo por Data Protection. `Descifrar` ya reconoce
el prefijo nuevo, así que el día de la migración solo hay que dejar de escribir con este. Es el
paso 9 de [EL-CORTE.md](EL-CORTE.md#9-cuando-ya-no-haya-vuelta-atrás-días-después-no-horas), y ahí
mismo se anota que esas dos pruebas dejan de aplicar.

---

## El segundo factor es obligatorio para todo el mundo

**Lo que sorprende:** no hay forma de saltárselo. No hay excepción para el administrador, ni para las
cuentas de servicio, ni una casilla de «recordarme para siempre».

**Por qué:** el escritorio corría dentro de la red de la empresa. Una dirección pública es otra cosa,
y una contraseña filtrada dejaría de ser suficiente. Es TOTP —el estándar—, calculado con lo que ya
trae .NET: sin servicio externo, sin SMS y sin nada que pagar.

Tres piezas que conviene conocer antes de tocarlo:

- **El alta se fuerza con el mismo mecanismo que la contraseña temporal**, copiado a propósito. Dos
  mecanismos distintos para el mismo problema serían dos sitios donde mirar el día que algo no corte.
- **El orden con la contraseña no es arbitrario: primero la contraseña.** La temporal se dicta por
  chat o en voz alta, y mientras siga viva cualquiera que la haya visto puede entrar. Si esa persona
  pudiera dar de alta el segundo factor, daría de alta **su** teléfono y sería la dueña de la cuenta.
  Al revés no hay daño equivalente. Ver
  [SegundoFactorObligatorio.cs](AdminWeb.Api/Auth/SegundoFactorObligatorio.cs).
- **La conexión en vivo (`/hubs`) también se corta.** Es la línea que distingue esto de una
  comprobación a medias: por ahí viajan la presencia y el latido del cronómetro, así que sin cerrarla
  una cuenta sin segundo factor seguiría apareciendo como presente y acumulando horas mientras la API
  le contesta 403 a todo lo demás.

**Lo que esto sube de precio:** el secreto de cada persona va cifrado con el mismo llavero que los
PAT, así que perder el llavero pasa de «que cada quien recapture su token» a «nadie puede entrar con
el código de su teléfono». Lo amortiguan los códigos de rescate, **que se guardan como hash y no
dependen del llavero**, y el reinicio por parte del líder.

**La consecuencia organizativa está en [EL-CORTE.md](EL-CORTE.md#hoy-solo-hay-una-cuenta-de-administrador)
y no se puede resolver desde el código:** con una sola cuenta de administrador, el líder que pierda su
teléfono y sus códigos no tiene quién se lo reinicie.

---

## El pool se mide en horas, y la conversión ocurrió una sola vez

**Lo que sorprende:** hay columnas `DiasLimite` **y** `HorasLimite` en las mismas tablas, y una fila en
`AppSettings` llamada `PoolHorasConvertidas` que dice «NO BORRAR».

**Por qué horas:** el plazo prometido y el esfuerzo estimado acaban comparándose contra lo que miden
los cronómetros, que cuentan en segundos. Con los plazos en días había que multiplicar por ocho en
cada comparación, y el tope que ya existía (365 días) es el mismo dicho en la unidad nueva:
`MaxHorasDePlazo = 2920`. Se mantiene el techo, no se amplía de tapadillo lo que se puede prometer.

**Por qué las columnas de días siguen ahí:** el escritorio las leía en producción hasta el corte.
Borrarlas habría roto la aplicación que estaba en uso.

**Y por qué la marca va en `AppSettings` y no en una condición sobre los datos.** Ése es el punto
entero, y las dos condiciones que uno escribiría primero están mal:

- `WHERE HorasLimite = 0` — **0 es un valor legítimo** y significa «sin fecha límite». El día que el
  líder ponga 0 a mano en una celda cuya `DiasLimite` vieja dice 5, el arranque siguiente le
  resucitaría 40 horas.
- `WHERE HorasLimite IS NULL` — **NULL también es legítimo** y significa «usa el de la matriz». El
  líder vacía el campo a propósito y el arranque siguiente le vuelve a meter las horas del valor
  congelado que dejó el escritorio.

**La conversión no es un estado deducible de las filas: es un hecho que ocurrió una vez**, y se
registra como tal. Con la marca puesta no vuelve a correr nunca. Todo va en una sola transacción, para
que un reinicio a media conversión no multiplique por ocho lo ya convertido. Ver
`ConvertirPlazosDeDiasAHorasUnaVez` en el migrador.

---

## El saldo de vacaciones se deriva y no se guarda en ninguna parte

**Lo que sorprende:** no existe la columna «días disponibles». Cada consulta recalcula el saldo desde
la fecha de ingreso, la tabla de la ley y las solicitudes. Y sí existe `Developer.VacationDaysLeft`,
que **no es el saldo** — es un número suelto que alguien teclea y que nadie mantiene.

**Por qué:** un saldo guardado como número hay que sincronizarlo a mano en cada alta, cada aprobación,
cada cancelación, cada cambio de fecha de ingreso y cada corrección del líder. **Se desincroniza el
primer día que alguien toque algo por un camino que nadie recordó actualizar, y a partir de ahí miente
sin avisar.** Derivarlo cuesta una consulta y no puede quedar desfasado nunca.

**Lo único que se guarda es el ajuste manual**, porque es el único dato que un humano escribe y que no
se puede deducir de nada. Existe por una razón concreta: al arrancar la web no había ni una solicitud
registrada, así que a alguien con cinco años de antigüedad el cálculo le contaba todos los días que la
ley le fue dando y ninguno gozado. El líder corrige ese número **diciendo por qué**, y sin nota no se
guarda: un número corregido sin motivo no se puede defender delante de quien reclama sus días.

**Y hay dos tablas de la ley a propósito.** `LftVacaciones` es la copia literal del escritorio que
rellena el campo de la ficha —y reparte proporcionalmente el primer año, art. 77—; la de
`SaldoDeVacacionesService` aplica la política de la empresa, donde **el primer año vale cero hasta
cumplirlo**. No podían ser la misma función. Lo que sí hay es una prueba que compara las dos año por
año: si alguien toca una copia, la suite lo dice.

La tabla está escrita **como tabla y no como fórmula**, aunque la fórmula existe, porque esto lo va a
leer alguien con el artículo 76 delante para comprobar renglón por renglón. La ley cambió en 2023, así
que no es una hipótesis.

---

## Los emoji están fuera de la interfaz

**Lo que sorprende:** hay una prueba
([EtiquetasDelServidorSinEmojiTests](tests/AdminWeb.Application.Tests/EtiquetasDelServidorSinEmojiTests.cs))
dedicada a que ningún texto que escribe el servidor lleve un emoji. Parece cosmética y no lo es.

**Por qué:** los emoji **no los dibujamos nosotros, los dibuja el sistema operativo de quien mira**.
Consecuencias, las tres comprobadas:

- Se ven distintos en cada equipo.
- **No heredan el color del texto.** En el tema oscuro se quedaban con el suyo mientras la palabra de
  al lado cambiaba.
- Donde no hay fuente de emoji instalada salen como **un cuadro vacío**. Está en una captura, no es
  una precaución teórica.

**Y por qué una prueba y no una nota:** volver a colarlos es facilísimo. Son un carácter más dentro de
una cadena, nadie los ve en una revisión de código y el compilador no tiene nada que decir.

La comprobación no es «¿dice *Abierta*?» sino «¿queda algún carácter que tenga que dibujar el sistema
operativo?», y persigue dos familias: los pares suplentes (todo emoji fuera del plano básico) y los
símbolos sueltos del plano básico —`⚠ ✅ ⏳ ○ ▶`—, que tienen el mismo defecto aunque quepan en un
`char`. Las letras acentuadas y las barras separadoras no son símbolos y pasan sin problema.

**En su lugar van iconos de trazo**, que heredan `currentColor` y se ven igual en todas partes.

---

## La fuente de iconos está recortada, y hay una ruta reescrita para no servir la de Radzen

Dos decisiones que van juntas y que solo se entienden mirando lo que pesa la primera carga de una
aplicación de WebAssembly.

**El recorte.** `MaterialSymbolsSharp.woff2` **no es la fuente completa**: lleva exactamente los 177
iconos listados en [iconos.txt](AdminWeb.Client/wwwroot/fuentes/iconos.txt).

| | Tamaño |
|---|---|
| Material Symbols Sharp completa | ~3,5 MB |
| La que trae Radzen (Outlined, completa) | 3,1 MB |
| **La nuestra, recortada** | **0,15 MB** |

**Qué pasa si usas un icono que no está:** se ve **su nombre escrito en letras** dentro del botón —
`delete` en vez del bote de basura. Falla a la vista y no en silencio, que es lo que se buscaba, pero
hay que regenerar el archivo. Cómo, y qué no quitar nunca de la lista (los iconos que Radzen usa por
dentro: el paginador, el aspa de cerrar, los cheurones, las flechas de ordenación), está en
[wwwroot/fuentes/LEEME.md](AdminWeb.Client/wwwroot/fuentes/LEEME.md).

**La ruta reescrita.** Radzen empaqueta su propia fuente de 3 MB, y el navegador **la descargaba igual
en cada primera carga** aunque el tema no la use. Se persiguió: la variable apunta a la nuestra,
ninguna regla de Radzen nombra la vieja salvo su propio `@font-face`, no queda ningún elemento cuya
familia calculada sea ésa, y redeclarar la familia tampoco lo evitó. El iniciador que reporta el
navegador es **el parser de su hoja de estilos**, así que la pide al leer el CSS y no al pintar nada.

Perseguirlo más costaba más de lo que vale, así que se zanja sin depender del porqué: **una línea de
middleware reescribe la ruta antes de que los archivos estáticos la atiendan**, y quien pida la de
Radzen recibe la nuestra. Son 3 MB menos en cada primera carga, y si algún día un componente de Radzen
pide un icono por esa vía saldrá con el mismo trazo que el resto — los nombres de las ligaduras
coinciden en las dos variantes.

---

## El contexto de datos es Scoped, y por eso hay que QUITAR código al portar un servicio

**Lo que sorprende:** los servicios portados perdieron los `Reload()` antes de decidir, los `Detach`
tras un fallo y los `AsNoTracking` puestos «porque el contexto es Singleton». Parece que se olvidaron.

**Por qué:** en el escritorio el `AppDbContext` era **Singleton**, compartido por toda la interfaz, y
esas eran defensas necesarias contra entidades rastreadas y datos rancios. Aquí es **uno por
petición**: lo que se lee ya es fresco, y desanclar a mano rompe la unidad de trabajo de la petición.
Mantener esas muletas no solo sobra: entorpece.

**Lo que sí se conserva son los `ExecuteUpdate` condicionales** —al tomar o aceptar una actividad del
pool, por ejemplo—. Son atómicos en la base, siguen siendo correctos, y en la web importan **más** que
en el escritorio porque ahora sí hay peticiones concurrentes de verdad.

Está explicado en la cabecera de [AppDbContext.cs](AdminWeb.Infrastructure/Data/AppDbContext.cs).

---

## Los trabajos de fondo nacen apagados

**Lo que sorprende:** `AdminWeb:TrabajosDeFondoActivos` está en `false` por omisión, así que una
instancia recién levantada **no escala SLA, no cierra jornadas caídas, no ingiere correo y no dispara
despliegues programados**.

**Por qué:** mientras el escritorio siguió en producción, sus temporizadores hacían ese mismo trabajo.
Con los dos encendidos a la vez, **todo llegaba por duplicado**: los avisos, los escalamientos, los
despliegues. Se encienden en el paso 6 del corte, cuando el escritorio ya se retiró.

**Consecuencia para quien desarrolla:** si estás probando algo que depende de un trabajo de fondo y no
pasa nada, mira ese ajuste antes que nada. Y en las pruebas de la API están apagados a propósito:
encenderlos haría que compitieran contra un barrido que consolida cronómetros por debajo, y los
fallos saldrían un día sí y otro no.

---

## La sesión es una cookie, no un token

**Lo que sorprende:** no hay JWT, no hay `Authorization: Bearer`, y el cliente no guarda ningún token.

**Por qué:** la API sirve el cliente Blazor, así que todo es del **mismo origen** y una cookie viaja
sola. Con `HttpOnly` el token **no es alcanzable desde JavaScript**: un XSS no se lleva la sesión. Y
con el **sello de seguridad** (`User.SecurityStamp`, que es nuevo de la web) se puede revocar de
verdad al cambiar una contraseña o desactivar una cuenta — cosa que un JWT no da sin infraestructura
extra.

Dos detalles que cuestan un rato entender si no se saben:

- **`Secure` se afloja en desarrollo.** Ahí la aplicación se levanta en HTTP y una cookie `Secure`
  sencillamente no se devuelve: el síntoma es que el login «funciona» y la siguiente petición responde
  401.
- **Los redirects de la cookie se convierten en códigos.** Sin eso, una petición de la API a una ruta
  protegida respondería con un 302 a una página de login que no existe. El cliente necesita el código.

---

## Los estáticos propios se revalidan siempre

**Lo que sorprende:** hay una cabecera `no-cache, must-revalidate` sobre **nuestros** archivos
estáticos. Parece una optimización al revés.

**Por qué:** nuestros archivos **no llevan huella en el nombre**. `css/tema.css` se llama igual hoy que
mañana, y la fuente de iconos también —cambia de contenido cada vez que se añade un icono,
conservando el nombre—. Sin ninguna cabecera, el navegador aplica su regla heurística y puede dar por
bueno un archivo guardado sin volver a preguntar.

**Ya ocurrió:** una hoja de estilos vieja dejó la pantalla de acceso con la marca convertida en un
cuadrado negro de 600 píxeles y el formulario desordenado debajo, **en una aplicación cuyo servidor
estaba sirviendo la versión correcta**. Diagnosticarlo desde fuera es carísimo, porque todo lo que se
mire en el servidor sale bien.

`no-cache` **no significa «no guardes»**: significa «guárdalo, pero pregunta antes de usarlo». La
respuesta normal es un 304 sin cuerpo, así que el coste es un viaje por archivo y no la descarga.

**Lo de `_framework/` no pasa por ahí y no hay que tocarlo**: esos nombres sí llevan huella y ahí
cachear para siempre es correcto. Y `index.html` **sí** necesita las mismas opciones, porque no lo
sirve `UseStaticFiles` sino el `MapFallbackToFile` — y es el archivo del que cuelgan los enlaces a
todos los demás.

---

## El recorte de ensamblados está apagado

**Lo que sorprende:** `PublishTrimmed` es `false` en una aplicación de WebAssembly, donde el tamaño de
descarga es el riesgo conocido.

**Por qué:** el recorte quita el código que el análisis estático no ve usado, y aquí hay mucho que solo
se alcanza **por reflexión**: la serialización de los DTO, los componentes de Radzen, el enrutador y
los guiones de los recorridos guiados. Con el recorte encendido, la aplicación descargaba sus 98
archivos y después **se quedaba colgada sin lanzar ninguna excepción** — la peor forma de fallar,
porque no hay nada que leer en la consola.

**El coste resultó pequeño:** unos 4,6 MB comprimidos, muy por debajo del presupuesto de 8 MB. Si
alguna vez hace falta recuperarlo, hay que hacerlo con `TrimmerRootDescriptor` y **volver a probar el
arranque en un navegador de verdad**: que compile no dice nada.

---

## El mensaje de un rechazo se enseña tal cual

**Lo que sorprende:** los rechazos de negocio devuelven un texto del servicio y el cliente lo pinta
literalmente, en vez de traducirlo a un mensaje de la interfaz.

**Por qué:** los servicios portados explican el motivo **en concreto** —«ya marcaste tu entrada hoy a
las 09:12», «esa actividad la tomó alguien más», «tu salida de hoy ya quedó marcada»—, y ese texto es
la mitad de su valor. Cambiarlo por un «no se pudo» genérico sería hacer la web peor que el
escritorio.

`ClienteApi` entiende **las dos formas** en que llega: el `ResultadoDto` de un rechazo de negocio y el
`ProblemDetails` del filtro de excepciones.

---

## Los documentos salen por dos caminos, y el de Word volvió

**Lo que sorprende:** hay un generador de PDF que maqueta por código (`IGeneradorDeDocumentos`,
QuestPDF) **y** un rellenador de plantillas de Word (`IPlantillaDeVacacionesEnWord`, OpenXml). Parecen
dos formas de hacer lo mismo.

**La historia, porque explica los comentarios antiguos que aún hay en el árbol:**

1. El escritorio armaba los documentos como `.docx` desde una plantilla y los convertía a PDF con
   **LibreOffice instalado en la máquina de cada quien**. Eso se descartó: obligaba a instalar un
   programa de escritorio en el servidor y era la única pieza de la web incapaz de correr sola.
2. Se sustituyó por maquetado en código con QuestPDF. **La consecuencia aceptada fue que Recursos
   Humanos perdía poder cambiar una palabra del formato sin recompilar**, y así está anotado en varios
   comentarios del código.
3. **Después se vio que LibreOffice nunca hizo falta para eso**: solo para *convertir* a PDF.
   Rellenar la plantilla es OpenXml, que es gratis y corre en cualquier sitio. Así que el Word volvió.

**Hoy conviven las dos salidas del mismo documento**, y cada una tiene su momento: el Word para
editarlo o imprimirlo con el formato de RH, el PDF para archivarlo. La plantilla se sube desde la
aplicación y se guarda **entera en la base** (`DocumentTemplate`), no en disco: el contenedor tiene
sistema de archivos efímero y con dos instancias cada una tendría la suya. La de fábrica va incrustada
en el ensamblado, así que restablecer siempre es posible.

**Si encuentras un comentario que dice que la plantilla ya no es editable, está desactualizado.** El
contrato vigente es
[ContratosDeDocumentos.cs](AdminWeb.Domain/Documentos/ContratosDeDocumentos.cs).

---

## Otras cosas pequeñas que parecen errores

| Lo que se ve | Lo que es |
|---|---|
| `AuditAction.Read = 9`, un valor que el escritorio no tiene | Registrar quién **consultó** la clave de licencia de un programa. Reusar `Update` haría imposible responder «¿quién ha visto esta clave?», que es justo la pregunta para la que se registra |
| Un `extern alias` en [Llavero.cs](AdminWeb.Api/Arranque/Llavero.cs) | Azure.Core 1.55 absorbió `DefaultAzureCredential`, que Azure.Identity también define: con los dos ensamblados presentes el compilador se niega a elegir (CS0433) |
| El cronómetro no se maneja desde «Mis Actividades» | A propósito. Arrancar, pausar y detener vive solo en «Mi jornada»: dos sitios para el mismo botón es la forma más rápida de acabar con dos cronómetros corriendo |
| `LftVacaciones` duplicado en el escritorio y en la web | Deliberado. Es una regla de ley y hasta el corte las dos aplicaciones tenían que dar el mismo número. Si alguien toca una copia, la otra suite lo dice |
| `AppSettings.vacaciones.caducidad-meses` sembrada por el migrador | Es política de la empresa (18 meses) y por eso se puede cambiar sin recompilar. Si la fila faltara, el servicio cae al valor por omisión: el saldo sigue saliendo bien |
| El respaldo de base de datos **no** se portó | Azure SQL trae respaldo continuo y restauración a un punto en el tiempo. Encima sería una copia peor de algo que ya existe — y en la que alguien confiaría |
| `/actividades` consulta el tiempo actividad por actividad | Es lo mismo que hacía el escritorio, pero allí la base estaba al lado. `WorkSessionService` no ofrece una versión por lotes |
