# El corte: pasar de la aplicación de escritorio a la web

> **Este documento describe una operación contra la BASE DE DATOS REAL.** Nada de lo que hay aquí se
> ejecuta solo ni lo ejecuta una herramienta automática: lo hace una persona, con el equipo avisado y
> con la marcha atrás preparada. Léelo entero antes de empezar.

> **Y una cosa hay que resolverla ANTES de todo lo demás**: hoy solo existe una cuenta de
> administrador, y el segundo factor obligatorio convierte eso en un riesgo nuevo. Está explicado en
> [«Hoy solo hay una cuenta de administrador»](#hoy-solo-hay-una-cuenta-de-administrador), más abajo.
> No se activa el segundo factor sin haber leído ese apartado.

## Por qué un corte único y no una convivencia

Durante todo el desarrollo, la web trabajó contra una base de **ensayo** y el escritorio siguió siendo
la única aplicación de producción. Esa decisión se tomó al principio y es la que evita de raíz los
tres problemas que habrían aparecido si las dos aplicaciones hubieran escrito producción a la vez:

- **Dos migradores.** Las dos ponen el esquema al día al arrancar. Corriendo a la vez sobre la misma
  base, el orden de los parches deja de estar determinado.
- **Dos juegos de trabajos de fondo.** Los temporizadores del escritorio y los trabajos de la web
  hacen lo mismo: escalar SLA, cerrar jornadas caídas, sincronizar tickets. Encendidos a la vez,
  todo llega por duplicado.
- **Concurrencia entre aplicaciones.** Solo seis entidades traen `RowVersion`; el resto se pisaría en
  silencio.

Por eso los trabajos de fondo de la web están **apagados por omisión**
(`AdminWeb:TrabajosDeFondoActivos`) y solo se encienden en el paso 6 de abajo.

## Antes del corte: el ensayo

**No se hace el corte sin haberlo ensayado antes.** El ensayo es el que dice cuánto dura la ventana
de mantenimiento; sin él se está adivinando.

1. Sacar una **copia fresca** de la base de producción (Azure SQL: restauración a un punto en el
   tiempo, a una base nueva).
2. Apuntar una instancia de la web a esa copia y **arrancarla**. El migrador corre al arrancar, bajo
   `sp_getapplock`, y deja el esquema al día.
3. **Medir cuánto tardó** ese arranque. Esa es la ventana de mantenimiento, más margen.
4. Recorrer con la web los datos reales: entrar con varias cuentas de rol distinto, abrir cada
   módulo, generar un documento, publicar en el foro, marcar una jornada.
5. Comprobar que las **integraciones responden** con las credenciales de verdad: Azure DevOps,
   Freshdesk, correo, Blob, FTP.
6. Tirar la copia.

Si algo falla en el ensayo, se arregla y se vuelve a ensayar. El ensayo es barato; el corte no.

**Hay un guion que hace los pasos 2 y 3 y escribe el informe: [`ensayo.ps1`](ensayo.ps1).** Se niega
a correr si la base no lleva `ENSAYO`, `COPIA` o `REHEARSAL` en el nombre, exige que tenga datos
—contra una base vacía todo esto tarda un segundo y no prueba nada— y compara los recuentos antes y
después, porque un migrador que se lleva una tabla por delante no lo dice. En su cabecera está cómo
sacar la copia por los dos caminos: restaurándola en Azure, o exportándola a un `.bacpac` e
importándola en un contenedor local, que sale gratis.

**Lo que este paso ya encontró, y que justifica el resto del apartado.** La primera vez que se
ejecutó apareció un defecto que ninguna de las 2.042 pruebas veía y que habría tumbado el corte
entero: `Users.SecurityStamp` es una columna nueva, el migrador la agregaba anulable y no la
rellenaba, y el modelo la declara no anulable. Una fila con `NULL` ahí no es «una cuenta sin sello»:
es una fila que EF **no puede leer**, así que la consulta del acceso reventaba y **no entraba nadie**.
En una base de prueba no aparece nunca, porque allí las cuentas se crean por el modelo, que ya trae
el sello puesto; solo sale contra una base que ya tenía cuentas. Ya está arreglado y con pruebas que
lo fijan, pero es el ejemplo exacto de lo que solo encuentra arrancar contra datos de verdad — y de
por qué el paso 4 de esta lista empieza por **entrar**, que es lo primero que hay que probar.

En esa misma ejecución la ventana medida fue de **diez segundos** contra una copia con 10.913 filas.
La migración no mueve datos: crea ocho tablas vacías y agrega veintitrés columnas, y la tabla más
grande que recibe una tiene noventa y cinco filas. Lo que cuesta son los viajes a la base, no el
volumen.

## Hoy solo hay una cuenta de administrador

**Esto es lo primero que hay que arreglar, y se arregla antes del corte, no durante.**

De las cuentas activas que hay, **solo una tiene rol de administrador**. Un solo líder. Compruébalo
antes de seguir, desde «Usuarios»: la columna del rol lo dice y no hace falta abrir la base.

Con el segundo factor obligatorio, cada quien tiene dos salidas si pierde el teléfono:

1. **Sus ocho códigos de rescate**, que se entregan una sola vez al darlo de alta.
2. **Que el líder se lo reinicie** desde «Usuarios», con lo que esa persona vuelve a darlo de alta
   como el primer día.

**El líder no tiene la segunda.** Si quien pierde el teléfono *y* los códigos de rescate es él, no
queda **nadie dentro de la aplicación** capaz de devolverle el acceso: el reinicio es una acción de
administrador y él era el único. La única salida sería tocar la base a mano —justo lo que este
documento prohíbe en su último apartado— o restaurar un respaldo anterior, con todo lo que eso
arrastra. No es un escenario rebuscado: un teléfono se moja, se pierde o se cambia de aparato sin
pasar los códigos, y los ocho papeles de rescate acaban en el mismo cajón que se traspapela.

### La recomendación, concreta

**Crear una segunda cuenta de administrador ANTES de activar el segundo factor.**

- Se puede hacer **hoy mismo, desde la aplicación de escritorio**, contra producción: su pantalla de
  usuarios ya permite crear cuentas con rol de administrador. No hace falta esperar al corte, y de
  hecho conviene que ya exista cuando el equipo empiece a darse de alta.
- **Y conviene hacerlo pronto, porque ese camino se cierra.** Vale mientras el escritorio siga
  encendido en alguna máquina. En cuanto se apague del todo, la única forma de crear esa cuenta será
  desde la web —y la web no toca producción hasta el corte, porque apuntarla ahí dispara el migrador
  y eso *es* el corte—. Si se llega al día sin la segunda cuenta, hay que crearla **en el corte
  mismo, como primera acción después de entrar y antes de que nadie más se dé de alta**, que deja una
  ventana de minutos en la que un solo administrador tiene su teléfono como único acceso.
- **Que sea de una persona de verdad**, con su propio nombre y su propia contraseña. Una cuenta
  compartida «de emergencia» que todo el mundo conoce es un agujero, no un respaldo: la contraseña
  circula, nadie la cambia y en la bitácora todas las acciones salen a nombre de nadie.
- **Que las dos personas guarden sus códigos de rescate en sitios distintos.** Los dos juegos en el
  mismo cajón —o en el mismo gestor de contraseñas, con la misma llave maestra— es tener una sola
  copia con dos nombres.
- **Que no compartan el teléfono**, por lo mismo.

Con eso, el peor caso deja de ser irreversible: quien se queda fuera se lo pide al otro
administrador, queda el asiento en la bitácora y se sigue trabajando.

### Si aun así ocurre

Si el único administrador se queda sin teléfono y sin códigos, **no hay nada dentro de la aplicación
que lo resuelva**, y este documento no va a describir cómo tocar la base a mano para arreglarlo: es
una intervención que se planifica con quien administra la base, con respaldo previo y por escrito, no
un paso que se improvisa un martes por la tarde. Crear la segunda cuenta cuesta dos minutos; esto,
un día.

## El día del corte

### 0. Con el escritorio TODAVÍA abierto: convertir los secretos heredados

**Este paso hay que hacerlo antes de cerrarlo, y es el único que no tiene marcha atrás si se
olvida.**

Las contraseñas FTP de los servidores y la cadena del Blob pueden estar cifradas con DPAPI, que ata
el cifrado a la cuenta de Windows que las guardó. **El servidor web no puede leerlas nunca**, y el
único programa capaz de convertirlas es el escritorio que se está retirando: su
`SharedSecretMigrationService` corre en cada arranque y pasa al formato portable todo lo que ESA
máquina alcance a descifrar.

Así que, antes de cerrar nada:

1. Abrir el escritorio **en el equipo de quien capturó esos secretos** (normalmente el líder). Con
   abrirlo basta: la conversión ocurre sola al arrancar.
2. Repetirlo en cada equipo donde se hubiera capturado alguno. Lo que una máquina no pueda leer,
   ninguna otra podrá.
3. Lo que quede sin convertir habrá que recapturarlo a mano en el paso 5 — que se puede, pero
   significa volver a pedir contraseñas de servidores que quizá nadie recuerde.

**A día de hoy esto ya está hecho**, comprobado contra producción: las **24** contraseñas de
servidor y los **3** ajustes marcados como secretos están en el formato portable, y no queda ni uno
heredado de DPAPI. El escritorio los fue convirtiendo solo, que es justo para lo que servía su
`SharedSecretMigrationService`.

Conviene volver a comprobarlo el día del corte, porque cualquiera puede capturar un secreto nuevo
desde una máquina distinta entre hoy y entonces. Se distinguen por el prefijo: lo portable empieza
por `ADW1:` y lo heredado no. En la propia aplicación se ve sin consultar la base — «Configuración»
marca los que hay que recapturar, y la pantalla de servidores no enseña como disponible una
contraseña que no pueda leer.

### 1. Avisar y cerrar el escritorio

El equipo tiene que estar fuera de la aplicación de escritorio. Mientras alguien la tenga abierta,
sus temporizadores siguen escribiendo.

### 2. Respaldo inmediato antes de tocar nada

Azure SQL tiene respaldo continuo, pero conviene un punto explícito y anotado: es lo que se va a
restaurar si hay que volver atrás, y quererlo buscar con prisa es la peor forma de encontrarlo.

**Anota la hora exacta.**

### 3. Apuntar la web a producción y arrancarla

La cadena de conexión vive en el ajuste `ConnectionStrings__Default` del App Service; se cambia allí,
no en el código. **No hay Key Vault en esta suscripción** — lo que eso cuesta y por qué se aceptó está
en el punto 1.3 de [GUIA-DE-PUESTA-EN-MARCHA.md](GUIA-DE-PUESTA-EN-MARCHA.md). Al arrancar, la API aplica
sus migraciones pendientes **de una vez y bajo `sp_getapplock`**, de modo que dos instancias no puedan
migrar a la vez.

Lo que la web añade al esquema —todo aditivo, nada se renombra ni se borra:

| Qué | Para qué |
|---|---|
| `Users.SecurityStamp` | Poder cerrar la sesión de alguien de verdad al cambiarle la contraseña o desactivarlo |
| `UserSecrets` | El PAT de Azure DevOps por persona, cifrado en el servidor (antes era un archivo DPAPI por máquina) |
| `UserPreferences` | El ancho y el orden de las columnas (antes, un `columnas.json` por máquina) |
| `PushSubscriptions` | A qué navegador entregar un aviso con la pestaña cerrada |
| `WorkSessions.LastHeartbeatUtc` | Consolidar el cronómetro hasta el último latido cuando se cierra la pestaña |
| `RowVersion` en seis entidades | Que dos ediciones simultáneas den un 409 en vez de pisarse |
| `Users.SegundoFactorActivo` / `SegundoFactorDesdeUtc` / `SegundoFactorUltimaVentana` | El **estado** del segundo factor de cada cuenta. El secreto no está aquí: va cifrado en `UserSecrets`, como el PAT |
| `UserRecoveryCodes` | Los códigos de rescate de cada persona, **solo como hash**: ni la base ni un respaldo los contienen en claro |
| `UserTrustedDevices` | Los navegadores a los que no se les vuelve a pedir el código durante 30 días, también solo como hash |

### 4. Comprobar que arrancó

`GET /api/health` responde 200 solo si la base contesta. Si no responde, **no se sigue**: se
diagnostica o se vuelve atrás (paso 8).

### 5. Recapturar los secretos que no se puedan leer

Los secretos de configuración cifrados con el esquema viejo de Windows (DPAPI) **no se pueden leer
desde el servidor**: dependían de la máquina que los guardó. La pantalla de configuración los marca
como «hay que volver a capturarlo» en lugar de enseñarlos como vacíos. Recaptúralos ahí mismo.

### 6. Encender los trabajos de fondo

Poner `AdminWeb:TrabajosDeFondoActivos = true` y reiniciar. **Este es el paso que no tiene sentido
hacer antes**: hasta aquí, los temporizadores del escritorio hacían ese trabajo.

### 7. Que entre el equipo

Y quedarse mirando la primera hora: los avisos, las jornadas que se abren, los cronómetros.

**Avísales de tres cosas al entrar**, porque ninguna se puede resolver por ellos:

- **Lo primero que verán es el alta del segundo factor**, antes que ninguna otra pantalla: un código
  QR que hay que escanear con la aplicación del teléfono y un código de seis dígitos que hay que
  teclear para comprobar que quedó bien. Al terminar, la pantalla enseña **una sola vez** ocho
  códigos de rescate. Esto no se improvisa el día del corte: la preparación está en
  «[Antes del día del corte — el segundo factor del equipo](GUIA-DE-PUESTA-EN-MARCHA.md#antes-del-día-del-corte--el-segundo-factor-del-equipo)»
  de la guía, y hay que haberla hecho **días antes**.
- **Cada quien tiene que volver a capturar su PAT de Azure DevOps**, en «Mis tickets DevOps». El
  anterior vivía en un archivo cifrado con DPAPI en su propia máquina y no hay forma de migrarlo. A
  cambio, el nuevo va cifrado en el servidor y les sigue al cambiar de equipo — que era justo lo que
  antes se perdía. Mientras no lo hagan, sus comentarios en DevOps saldrán firmados por la cuenta de
  la instalación.
- **La primera vez que se abra la web pedirá permiso para los avisos.** Sin aceptarlo no llegan
  avisos con la pestaña cerrada, que es lo que sustituye al globo de la bandeja del escritorio.

**Entra tú primero, antes que nadie.** Da de alta tu propio segundo factor y comprueba que el código
que muestra tu teléfono es aceptado. Si la hora del servidor estuviera desajustada, los códigos
serían rechazados sin explicación posible desde el lado de quien los teclea — y es mucho mejor
descubrirlo con una persona delante que con el equipo entero preguntando a la vez. El porqué del
reloj está en el punto 1.6 de la guía.

### 8. Si hay que volver atrás

Mientras no se haya hecho el paso 9, la marcha atrás existe y es sencilla: **el escritorio vuelve a
abrirse y sigue funcionando**. Los cambios de esquema son aditivos —columnas y tablas nuevas que el
escritorio ignora—, así que no le estorban. Si además hiciera falta descartar datos escritos desde la
web, se restaura el respaldo del paso 2.

Por eso el paso 9 va después de estabilizar, y no el mismo día.

### 9. Cuando ya no haya vuelta atrás (días después, no horas)

- **Revocar el acceso a la base del cliente de escritorio.** Con eso el `.exe` viejo queda inerte
  aunque alguien lo conserve en su equipo, que es lo que de verdad cierra la puerta.
- Archivar el instalador y su certificado de firma.
- Migrar los secretos de configuración del cifrado compartido a Data Protection, con el llavero en
  Blob y **cifrado con un certificado** (no con Key Vault, que esta suscripción no tiene: ver
  `AdminWeb.Api/Arranque/Llavero.cs` y el punto 1.3 de la guía de puesta en marcha).
  Hasta ahora la web escribía con el esquema del escritorio **a propósito**, para que las
  dos se entendieran (ver `ProtectorPortable`); retirado el escritorio, esa restricción desaparece y
  conviene pasar a protección de verdad.
- Quitar de este repositorio las dos pruebas que comprueban que la semilla de cifrado coincide con la
  del escritorio: dejan de aplicar.

## Lo que NO se hace nunca

- **Ejecutar SQL a mano contra la base de producción.** El esquema lo pone al día el migrador al
  arrancar la aplicación. `SET PARSEONLY ON` **no valida sin ejecutar** como suele creerse.
- **Encender los trabajos de fondo con el escritorio todavía en uso.**
- **Regenerar las llaves VAPID de los avisos push** después de que la gente se haya suscrito: todas
  las suscripciones dejarían de valer de golpe.
- **Dejar el segundo factor obligatorio con una sola cuenta de administrador.** Es el apartado de
  arriba, y está aquí repetido porque es el único de esta lista cuyo daño no lo paga la aplicación
  sino una persona concreta: el propio líder, encerrado fuera de su propio sistema.
- **Reiniciarle el segundo factor a alguien que lo pidió por escrito y nada más.** Es la acción que
  devuelve el acceso a una cuenta; una petición por chat la puede escribir cualquiera que ya tenga la
  contraseña. Se confirma por una vía que se reconozca —una llamada, en persona— antes de pulsar. La
  pantalla lo dice en el diálogo y el asiento queda en la bitácora a nombre de quien reinició.
