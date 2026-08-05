# Administrador de Desarrollo Web
## Manual del Usuario Operativo

**Rol: Operaciones**  
**Versión del manual:** 1.1  ·  **Fecha:** 25 de julio de 2026

> Este manual describe únicamente lo que la aplicación hace de verdad. La misma aplicación
> se reparte a todo el equipo: el menú y los permisos dependen del rol de la cuenta con la
> que inicias sesión. Si tu menú no coincide con lo que aquí se describe, revisa con qué
> cuenta entraste.

---
## Qué puedes hacer con esta aplicación

Tu cuenta tiene el rol **Operaciones**. La aplicación es la misma para todo el equipo, pero tu menú lateral tiene exactamente tres opciones:

- **🔔 Avisos**
- **🚀 Despliegues**
- **🗓 Programados**

Con eso puedes:

- Subir una versión ya empaquetada a un grupo de servidores (un "perfil"), viendo el avance en vivo.
- Consultar qué sistemas y versiones existen y leer el changelog de cada versión.
- Dar de alta servidores nuevos.
- Consultar el historial de despliegues y el registro detallado de cada uno.
- Agendar un despliegue para una hora futura y cancelar los que aún no han corrido.
- Revisar tus **avisos** (por ejemplo, cuando te asignan un ticket o un requerimiento) y abrirlos.

Todo lo demás del sistema (Dashboard, equipo, requerimientos, vacaciones, configuración, etc.) no forma parte de tu rol y no aparece en tu menú.

Arriba a la derecha de la ventana verás tu nombre y, entre paréntesis, **(Operaciones)**. Si ahí no dice Operaciones, estás en otra cuenta.

---

## La ventana, la bandeja del sistema y tu sesión

Esto no es un detalle cosmético: de aquí depende que los despliegues programados se ejecuten.

- **Cerrar con la ✕ no cierra la aplicación.** La manda a la bandeja del sistema (junto al reloj) y sale un globo que dice *"Sigo aquí"*. Sigue corriendo.
- **Solo se abre una vez.** Si vuelves a abrir el ejecutable estando ya corriendo, no arranca otra: la que ya estaba se pone al frente, aunque estuviera en la bandeja. Importa aquí más que en ningún lado: dos instancias podrían pelearse un mismo despliegue programado.
- **💬 Foro.** Arriba del menú tienes el foro del equipo, igual que todos los demás roles: publicaciones con comentarios anidados, ❤, búsqueda, enlaces pulsables e imágenes adjuntas (o pegadas con Ctrl+V). Se explica a detalle en el Manual del Desarrollador.
- **Tu estado.** Junto a tu nombre, arriba a la derecha, marcas si estás disponible, ocupado, en reunión, comiendo o en un descanso. El administrador lo ve en su pantalla *Quién está*. Se guarda a qué hora entras y sales; el estado solo se ve en vivo y no queda historial.
- Para **volver a la ventana**: doble clic en el ícono de la bandeja, o clic derecho → **Abrir**.
- Para **cerrar de verdad**: clic derecho en el ícono de la bandeja → **Salir**.
- **Cerrar sesión** (botón 🚪 Cerrar sesión, abajo del menú) te regresa a la pantalla de inicio de sesión. Un despliegue programado necesita una sesión iniciada para ejecutarse: si vas a dejar el equipo encendido para que corra una cita nocturna, **usa la ✕ (bandeja), no Cerrar sesión**.

> ⚠ **Si hay un despliegue corriendo** y pulsas **🚪 Cerrar sesión**, la aplicación te advierte:
> *"Hay un DESPLIEGUE EN CURSO. Si cierras sesión se cancela (lo ya subido se queda como esté).
> ¿Cerrar sesión de todos modos?"*. Lo mismo pasa al **Salir** desde la bandeja:
> *"Hay un DESPLIEGUE EN CURSO. Si cierras la aplicación se cancela. ¿Cerrar de todos modos?"*.
> Responde **No** para seguir desplegando. En cambio, mandar la ventana a la bandeja con la **✕**
> **no** interrumpe el despliegue: sigue corriendo ahí.

---

## Avisos (🔔)

Es tu bandeja de avisos personales: cosas que el sistema quiere que sepas, por ejemplo que te asignaron un ticket en Azure DevOps o un requerimiento. Los avisos son **por usuario** y quedan guardados hasta que los lees.

En el menú lateral, el botón **🔔 Avisos** muestra entre paréntesis cuántos llevas **sin leer** (por ejemplo *🔔 Avisos (3)*) y se resalta cuando hay pendientes. Si tienes la aplicación en la bandeja y llega uno nuevo, sale un globo *"Tienes avisos nuevos"* con el texto *"N aviso(s) sin leer. Ábrelos en el menú «Avisos»."*; al tocarlo se abre esta pantalla.

La lista tiene estas columnas: un punto **●** para los no leídos (que además van en negrita), **Fecha**, **Tipo** (*🔷 Ticket DevOps*, *📋 Requerimiento* o *🔔 Aviso*), **Aviso** y **Detalle**.

- **✓ Marcar todo leído** deja todos los avisos como leídos y pone el contador del menú en cero.
- **🔄 Recargar** vuelve a leer la lista.
- **Doble clic** sobre un aviso lo marca como leído y, si el aviso trae un enlace, lo abre en tu navegador (por ejemplo, el ticket de DevOps correspondiente).

Si no tienes ninguno, la pantalla dice *"No tienes avisos."*

---

## Antes de desplegar: lista de verificación

1. **La versión es la correcta.** Entra a **Despliegues → Sistemas y Versiones**, selecciona el sistema y lee el **Changelog** de la versión que vas a subir.
2. **El perfil que necesitas aparece en tu lista.** En la pestaña **Desplegar**, abre el combo *Perfil (servidores destino)*. Si el perfil que te pidieron no está, pídele a un administrador que lo habilite **antes** de la ventana de mantenimiento, no durante.
3. **Sabes qué hay hoy en esos servidores.** En la pestaña **Servidores**, las columnas *Últ. actualización* y *Versión desplegada* te dicen qué se subió por última vez y cuándo.
4. **Hay tiempo.** Si dejas marcado el respaldo (**💾 Respaldar antes**), antes de sobrescribir la aplicación descarga completa la carpeta remota de cada servidor y la respalda en Azure. En carpetas grandes o conexiones lentas eso puede tardar bastante. El paquete de la versión, en cambio, no se descarga entero: se lee en *streaming* y se publica por FTP sin extraerlo a disco.
5. **Nadie va a apagar el equipo.** El despliegue corre en esta aplicación: si cierras sesión, apagas o reinicias, se interrumpe a medias.
6. **Tienes a quién escalar.** Si falta configuración de Azure, un perfil, o hay que revertir, eso lo resuelve un administrador; tú no puedes.

---

## Despliegues → pestaña 🚀 Desplegar

Es la pantalla que se abre al entrar. Arriba hay tres listas desplegables, la casilla **💾 Respaldar antes** y los botones **🚀 Desplegar** y **⏹ Cancelar**; abajo, un renglón de estado con el botón **🧹 Limpiar**, una barra de avance con su porcentaje (que solo aparece mientras se despliega) y el registro (la consola negra).

### Qué contiene cada lista

| Lista | Qué muestra |
|---|---|
| **Sistema / Aplicativo** | Solo los sistemas marcados como activos. Un sistema inactivo no aparece aquí aunque sí se vea en *Sistemas y Versiones*. |
| **Versión** | Todas las versiones del sistema elegido, de la más reciente a la más vieja, con su fecha de creación entre paréntesis. |
| **Perfil (servidores destino)** | **Solo los perfiles marcados por un administrador como permitidos para el rol Operaciones.** Los demás perfiles existen, pero no los ves ni los puedes usar. |

Las listas se recargan cada vez que entras a la pestaña. Si un administrador acaba de crear una versión o de habilitarte un perfil y no lo ves, cambia a otra pestaña y regresa a **Desplegar**.

### La casilla 💾 Respaldar antes

Viene **marcada**. Con ella marcada, antes de sobrescribir cada servidor la aplicación respalda su carpeta remota en Azure: es la red de seguridad para poder revertir. Si la **desmarcas**, el despliegue corre **sin** respaldo previo: es más rápido, pero si algo sale mal no habrá copia para volver atrás. En tu rol es todo o nada para el despliegue completo; elegir el respaldo servidor por servidor es exclusivo de administrador.

### Ejecutar un despliegue

1. Elige el **Sistema / Aplicativo**.
2. Elige la **Versión**.
3. Elige el **Perfil (servidores destino)**.
4. Decide el respaldo: deja marcada **💾 Respaldar antes** (recomendado) o desmárcala para desplegar sin respaldo.
5. Pulsa **🚀 Desplegar**.
6. Aparece la confirmación *"¿Desplegar la versión 'X' a perfil 'Y' (N servidor/es)?"* (título *Confirmar despliegue*). Debajo se recuerda el respaldo: si lo dejaste marcado, *"Respaldo previo: todos."*; si lo quitaste, la advertencia *"⚠ SIN respaldo previo: si algo sale mal no habrá copia para revertir."*. Revisa que el número de servidores sea el que esperas y pulsa **Sí**.
7. El registro se limpia y empieza a llenarse. Las tres listas, la casilla **💾 Respaldar antes** y el botón **🧹 Limpiar** se bloquean, el botón **🚀 Desplegar** se deshabilita y **⏹ Cancelar** se habilita. Aparecen la barra de avance y su porcentaje, y el renglón de estado arranca en *"⏳ Despliegue en curso..."* y luego va mostrando en vivo qué se está haciendo (el servidor actual y, por ejemplo, el archivo que se está subiendo).
8. Al terminar, el renglón de estado muestra el resumen — *"✅ Despliegue completado — N servidor(es)."* o *"⚠ Completado con errores — OK: n, Fallidos: m."* — y todo se desbloquea.

### Qué pasa durante el despliegue

En este orden, y así lo verás en el registro:

1. **Preparación.** Se anuncia el sistema y la versión (*"🚀 Iniciando despliegue de Sistema vX"*), el perfil y el número de servidores, el **Paquete** (el nombre del ZIP local o, si el paquete vive en Azure, *"Blob (streaming): …"*) junto con un código de **Bitácora** (correlación), el **Respaldo previo** (*todos*, *ninguno* o *N de M*) y la **🕐 Hora de inicio**. Ese código de Bitácora sirve para rastrear el despliegue completo en la auditoría; anótalo si vas a reportar un problema.
2. **Lectura del paquete.** La aplicación **no extrae el ZIP a una carpeta temporal**: lo lee en *streaming*. Si la versión tiene copia local verás *"📂 Leyendo el paquete (local)..."*; si el paquete vive en Azure Blob Storage, *"📡 Leyendo el paquete desde Blob Storage (streaming)..."* — se lee directo del blob, sin descargarlo entero. Termina con *"✓ N archivos listos para publicar."*. Cada archivo se sube después por FTP desde memoria; nada toca el disco.
3. **Por cada servidor del perfil, en orden** (encabezado *"🌐 [servidor] · 🕐 HH:mm:ss"*):
   - **Respaldo previo** (si dejaste marcada **💾 Respaldar antes**). *"💾 Respaldando <ruta remota> antes de sobrescribir…"*. Se descarga completa la carpeta remota, se comprime y se sube a Azure Blob Storage bajo la carpeta de respaldos de despliegue. Termina con *"✓ Respaldo subido: N archivos, X MB"*.
     - Si la carpeta remota todavía no existe (primer despliegue a ese servidor), verás *"· La carpeta remota no existe todavía: nada que respaldar."* y continúa.
     - **Si el respaldo falla, ese servidor NO se despliega** y cuenta como fallido. Es deliberado: desplegar creyendo que hay red de seguridad es peor que no desplegar.
     - Si **desmarcaste** el respaldo, ese paso se omite; en el detalle del despliegue (pestaña **Historial**) queda anotado *"Sin respaldo previo para este servidor (elección del usuario)."*
   - **Subida.** Se conecta por FTP/FTPS (*"✓ Conectado a host:puerto"*) y sube los archivos a la ruta remota; mientras tanto el renglón de estado va diciendo qué archivo se está subiendo y cuántos van. Al final: *"✓ N archivos publicados → /ruta"* y *"✅ <servidor> completado en X.Xs · 🕐 HH:mm:ss"*.
4. **Cierre.** Una línea de resumen: *"✅ Despliegue Completado — OK: n Fallidos: m"* (o ⚠ con el estado correspondiente y, si los hay, *"Sin intentar: k"*), seguida de la **🕐 Hora de fin** y la **Duración total**.

### El avance en vivo

Mientras el despliegue corre verás:

- Una **barra de avance con su porcentaje** (0–100 %), que sube conforme se completan servidores y archivos.
- El **renglón de estado** con el detalle del momento: el servidor actual y qué está haciendo — por ejemplo el archivo que se está subiendo (*"⏳ [servidor] subiendo <archivo> (x/y)"*), si está respaldando la carpeta remota, o si está reconectando.
- En el registro, la **hora de inicio y de fin** del despliegue y de cada servidor, y la **duración total**.

La barra y el porcentaje solo se ven mientras hay un despliegue; al terminar desaparecen.

### Si cambias de pantalla a media corrida

Un despliegue en curso **no se pierde ni se reinicia** si te vas a otra pantalla (Avisos, Programados) y regresas a Desplegar: la selección, la barra, el porcentaje y el registro se conservan, y el despliegue sigue corriendo. Lo que se acumuló en el registro mientras no estabas aparece en cuanto vuelves. Mientras haya un despliegue activo, las listas tampoco se recargan, justo para no reiniciar nada.

### Limpiar la consola

El botón **🧹 Limpiar** (junto al renglón de estado) borra el registro de la pantalla y reinicia la barra, dejando *"Consola limpia. Selecciona sistema, versión y destino para desplegar."*. Está **deshabilitado mientras hay un despliegue en curso**: no puedes limpiar a media corrida. Limpiar la pantalla **no borra nada guardado**: el registro completo de cada despliegue queda en la pestaña **Historial**.

### Reintentos ante conexión lenta

La aplicación no se rinde al primer tropiezo. Hay dos niveles de reintento y ambos se ven en el registro:

- **Por archivo**: hasta **4 intentos** sin cortar la conexión, para el archivo que falló suelto.
- **Por servidor**: hasta **6 intentos**, tirando la conexión y reconectando desde cero. En cada reintento verás *"↻ Reintento n/6 sobre <host> (x/y ya subidos)"* — los archivos ya subidos no se vuelven a subir.

Entre intento e intento espera cada vez más (2 s, 4 s, 8 s, 16 s… con tope de 30 s) y lo avisa: *"⚠ …: intento n/6 falló (…). Reintentando en Xs…"*.

**Ver esos mensajes no significa que algo se rompió.** Significa que la conexión está lenta y la aplicación está insistiendo. Solo es un fallo real cuando aparece *"agotados 6 intentos"*.

### Cancelar

Pulsa **⏹ Cancelar**. Ten claro qué hace y qué no:

- Detiene el proceso en el servidor que esté en curso y **no intenta los servidores que faltaban**.
- **No deshace nada.** Los archivos ya subidos se quedan subidos. Si necesitas volver atrás, el respaldo previo está en Azure y quien lo restaura es un administrador.
- El trabajo queda registrado como **Cancelado**, con todo su registro guardado.

### Leer el registro

El registro usa color por tipo de línea:

| Color | Significa |
|---|---|
| Verde (✅ ✓) | Paso completado bien |
| Rojo (❌ ERROR) | Error |
| Amarillo (⚠) | Advertencia o reintento |
| Azul (🚀 🌐) | Inicio de despliegue o cambio de servidor |

El registro de la pantalla se borra cada vez que inicias otro despliegue (o cuando pulsas 🧹 Limpiar), pero **nada se pierde**: queda guardado y lo puedes releer en la pestaña **Historial**.

### Estados finales posibles

| Estado | Cuándo se asigna | Qué hacer |
|---|---|---|
| **Completado** | Ningún servidor falló | Nada. Verifica la aplicación publicada si aplica. |
| **Parcial** | Unos servidores sí y otros no | Revisa en Historial qué servidor falló y por qué; repite el despliegue solo cuando esté resuelto. |
| **Fallido** | Ningún servidor terminó bien | Lee el error en Historial. Casi siempre es conexión, credenciales o respaldo. |
| **Cancelado** | Lo detuviste con ⏹ Cancelar | Los servidores no intentados quedaron sin tocar; el que estaba en curso pudo quedar a medias. |

Mientras corre, el trabajo aparece en Historial como **En curso**.

> El renglón de estado que queda bajo las listas al terminar solo resume cuántos servidores salieron bien y cuántos mal. **El estado definitivo del trabajo (Completado, Parcial, Fallido o Cancelado) míralo en la pestaña Historial**, que es la fuente confiable.

---

## Despliegues → pestaña 🖥 Sistemas y Versiones

**Esta pestaña es solo de consulta para ti.** Bajo el título *Sistemas / Aplicativos* verás la leyenda **"Solo consulta"** en lugar de los botones de alta, edición y borrado.

La pantalla está partida en dos:

- **Izquierda — Sistemas / Aplicativos:** columnas *ID*, *Sistema*, *Versiones* (cuántas tiene) y *Activo* (✓ o ✗). Los sistemas inactivos se ven en gris y no aparecen en el combo de la pestaña Desplegar.
- **Derecha — Versiones del sistema seleccionado:** columnas *ID*, *Versión*, *Tamaño*, *Azure* (☁ Sí si el ZIP también está en Blob Storage, — si solo quedó local), *Creada* y *Changelog* (recortado a 60 caracteres).

### Leer el changelog completo

1. Selecciona el sistema en la lista de la izquierda.
2. Selecciona la versión en la lista de la derecha.
3. Pulsa **📝 Changelog** (o da doble clic sobre la fila de la versión).
4. Se abre la ventana *"Changelog — versión X"* con la marca **(solo lectura)**. Solo tiene botón **Cerrar**: no hay Guardar porque no puedes modificarlo.
5. Si la versión no trae changelog, verás *"(esta versión no tiene changelog registrado)"*.

---

## Despliegues → pestaña 🌐 Servidores

Aquí ves todos los servidores registrados: *ID*, *Nombre*, *Host*, *URL*, *Últ. actualización* y *Versión desplegada*. Los servidores dados de baja aparecen en gris.

Bajo la barra de botones hay una nota permanente para tu rol:

> *"Puedes dar de alta servidores. Para corregir o dar de baja uno existente, pídeselo a un administrador."*

Tus botones disponibles son **➕ Nuevo**, **🌍 Abrir URL** y **📊 Excel**. Los de editar, dar de baja e importar no se muestran.

### Dar de alta un servidor

1. Pulsa **➕ Nuevo**. Se abre la ventana *Nuevo Servidor* (encabezado *🌐 Servidor FTP/FTPS*).
2. Llena los campos:

| Campo | Obligatorio | Notas |
|---|---|---|
| Nombre | Sí | Debe ser único. Es el nombre con el que aparecerá en el registro y en el historial. |
| Host (incluye esquema: ftps:// o ftp://) | Sí | El esquema importa: `ftps://` activa el cifrado TLS explícito; `ftp://` no. |
| Puerto | Sí | Viene en 21 por omisión. |
| Usuario | Sí | |
| Contraseña | Sí | Se guarda cifrada. |
| Ruta remota | Sí | La carpeta destino en el servidor. |
| URL | No | Solo sirve para abrir el sitio en el navegador desde el botón 🌍 Abrir URL. |
| Activo | — | Viene marcado; un servidor nuevo siempre queda activo. |

3. Pulsa **Guardar**.
4. Si todo está bien, la ventana se cierra y el servidor aparece en la lista. **No sale ningún mensaje de confirmación**: que aparezca en la lista es la confirmación. Si algo falló, sale un aviso con título *"No se pudo"* explicando qué.

### Si te equivocaste al capturar un servidor

No puedes corregirlo ni borrarlo. Haz esto:

1. **No lo vuelvas a capturar.** Con el mismo nombre el sistema lo rechaza (*"Ya existe un servidor llamado «X»"*), y con otro nombre te quedan dos servidores para la misma máquina y el perfil puede terminar apuntando al equivocado.
2. Avisa a un administrador con el **ID** y el **Nombre** del servidor (columnas de la lista) y el dato correcto.
3. Él lo corrige o lo da de baja. Un servidor dado de baja deja de recibir despliegues pero se conserva para que el historial siga siendo legible.
4. **Mientras tanto no lo uses**: si ese servidor ya quedó dentro de un perfil, avisa antes de desplegar.

### Otras acciones

- **🌍 Abrir URL**: selecciona un servidor y pulsa el botón (o da doble clic en la fila) para abrir su URL en el navegador. Si no tiene URL: *"Este servidor no tiene URL configurada."*
- **📊 Excel**: exporta la lista completa de servidores a un archivo de Excel; te pide dónde guardarlo y al terminar muestra la ruta.

---

## Despliegues → pestaña 📊 Estado

Es la foto de **cómo están los servidores ahora**: qué versión tiene cada uno, quién se la puso y cuándo. Sirve para lo de todos los días — *«¿ya quedó el sandbox?»*, *«¿a cuáles les falta?»*, *«¿quién subió esto?»*— sin tener que leer el historial hacia atrás.

| Columna | Qué significa |
|---|---|
| Servidor | Nombre del destino. |
| Sistema / Versión desplegada | Lo que tiene publicado hoy. |
| Última publicada | La versión más nueva que existe de ese sistema. |
| Estado | ✅ Al día · ⚠ Atrasado · ○ Sin desplegar. |
| Última actualización / Hace | Cuándo fue, en fecha y en lenguaje llano («hace 3 h»). |
| Quién lo desplegó | Quien lanzó ese despliegue. |
| Despliegue # | Número del trabajo, por si hay que reportarlo. |

Arriba sale el resumen: *"22 servidor(es) · ✅ 18 al día · ⚠ 3 atrasado(s) · ○ 1 sin desplegar nunca"*.

Marca la casilla **Solo atrasados** y te queda justo la lista de lo que falta por subir. Con **📜 Ver ese despliegue** (o doble clic en la fila) saltas al Historial con ese despliegue ya seleccionado, para leer su log completo.

Si acabas de desplegar y no ves el cambio, pulsa **🔄 Actualizar**.

> En despliegues antiguos la columna **Quién lo desplegó** puede decir «—»: ese dato se empezó a guardar a partir de esta versión.

---

## Despliegues → pestaña 📜 Historial

Es la memoria de todo lo que se ha desplegado. La pantalla se divide en dos:

**Arriba — Historial de despliegues** (los 200 más recientes, del más nuevo al más viejo):

| Columna | Qué significa |
|---|---|
| ID | Número del trabajo. Úsalo al reportar un problema. |
| Sistema / Versión / Perfil | Qué se desplegó y a dónde. |
| Estado | Completado, Parcial, Fallido, Cancelado, En curso o Pendiente, con color. |
| OK / Fall. | Servidores que terminaron bien / servidores que fallaron. |
| Inicio | Fecha y hora de arranque. |
| Duración | Segundos que tardó. |

**Abajo — Log del despliegue seleccionado**: al seleccionar un renglón de arriba, aquí sale el registro completo de ese despliegue, con la hora de cada línea y, entre corchetes, el servidor al que corresponde. Si no hay nada guardado dirá *"(Sin entradas de log para este despliegue)"*.

Para consultar un despliegue anterior:

1. Entra a la pestaña **📜 Historial**.
2. Pulsa **🔄 Actualizar** si acabas de terminar un despliegue y no lo ves.
3. Selecciona el renglón que te interesa.
4. Lee el registro de abajo: ahí está, servidor por servidor, el respaldo, los reintentos y el error exacto si lo hubo.

---

## Programados (🗓)

Aquí agendas despliegues para una hora futura. En la parte superior de la pantalla hay un aviso permanente, y es la regla más importante de esta pantalla:

> ⚠ *"Los ejecuta una aplicación abierta (basta con dejarla en la bandeja del sistema). Si a la hora programada no hay ninguna corriendo, el despliegue se marca como Perdido y NO se ejecuta después."*

**No hay un servicio en el servidor haciendo esto.** Lo hace esta aplicación, en la computadora de alguien, con la sesión iniciada. Si a la hora fijada nadie la dejó corriendo, el despliegue no ocurre — y tampoco se dispara a destiempo cuando alguien abra la aplicación al día siguiente. Es a propósito: desplegar a deshora sin que nadie lo esté esperando es peor que no desplegar.

### Programar un despliegue

1. Pulsa **🗓 Programar despliegue**.
2. En la ventana *Programar despliegue* llena:
   - **Sistema** (solo los activos).
   - **Versión**.
   - **Perfil de despliegue** — igual que en la pestaña Desplegar, solo aparecen los perfiles habilitados para Operaciones.
   - **Fecha y hora** — la fecha viene sugerida para mañana y la hora a las 02:00. Debe ser futura.
   - **Tolerancia para arrancar tarde**: 15 minutos, 30 minutos, **1 hora (valor por omisión)**, 2 horas o 4 horas.
   - **Notas (opcional)**.
3. Pulsa **Programar**.
4. Sale la confirmación *"Despliegue programado para dd/mm/aaaa hh:mm."* y aparece en la lista.

### Qué es la tolerancia

Es cuánto se acepta arrancar tarde. Si la hora programada pasa y ninguna aplicación abierta lo toma **dentro de ese margen**, la cita se marca **⚠ Perdido** con el mensaje *"No se ejecutó: no había ninguna aplicación abierta dentro de los N minutos de tolerancia."* y ya no se ejecuta nunca.

Elige el margen según lo que estés dispuesto a tolerar: 15 minutos para una ventana estricta, 4 horas si te da igual que arranque más tarde.

### Estados de la lista

| Estado | Qué significa |
|---|---|
| 🕓 Programado | Esperando su hora. Es el único estado que puedes cancelar. |
| ▶ En ejecución | Una aplicación lo tomó y lo está corriendo ahora mismo. |
| ✅ Completado | Corrió y **todos** los servidores salieron bien. |
| ❌ Fallido | Corrió pero no terminó bien. **Un despliegue Parcial también queda aquí como Fallido**: revisa el detalle. |
| ⚪ Cancelado | Alguien lo canceló antes de su hora. |
| ⚠ Perdido | Llegó su hora y venció la tolerancia sin que ninguna aplicación lo ejecutara. |

Las columnas **Ejecutó** (qué equipo y usuario lo tomó) y **Resultado** (por ejemplo *"Job #37: Completado — 3 OK, 0 fallidos."*) te dicen quién lo corrió y cómo terminó. Con ese número de Job lo buscas en **Despliegues → Historial** para leer el registro completo.

La casilla **Solo pendientes** viene marcada y muestra únicamente lo que sigue esperando su hora. Desmárcala para ver también lo ya ejecutado, cancelado o perdido. **🔄 Recargar** actualiza la lista.

### Cancelar una programación

1. Selecciona el renglón.
2. Pulsa **✖ Cancelar**.
3. Confirma en *"¿Cancelar este despliegue programado?"*.
4. Sale *"Programación cancelada."*

Solo funciona si el estado es **🕓 Programado**. En cualquier otro caso responde *"No se puede cancelar: está en estado X."*

### Cómo asegurar que la cita se ejecute

1. Deja la aplicación **abierta y con la sesión iniciada** en un equipo que no se vaya a apagar ni suspender.
2. Puedes cerrarla con la ✕: se queda en la bandeja del sistema y sigue vigilando. **No uses Cerrar sesión.**
3. Cuando llegue la hora, si la ventana está a la vista aparece un diálogo de progreso *"Despliegue programado — Sistema vX → Perfil"* con el registro en vivo. Si está en la bandeja, corre en silencio y avisa con globos: *"Despliegue programado iniciado"* y después *"Despliegue programado completado"* o *"Despliegue programado con problemas"*.
4. Si varias personas dejaron la aplicación abierta, no pasa nada: **solo una toma la cita**; no se ejecuta dos veces.
5. Si se perdió, sale el globo *"Despliegue programado perdido"*. Tendrás que volver a programarlo.

---

## Problemas frecuentes

### Al desplegar

| Síntoma | Causa | Qué hacer |
|---|---|---|
| El combo *Perfil (servidores destino)* está vacío, o al pulsar Desplegar sale **"Selecciona un perfil."** | Ningún perfil está marcado como permitido para Operaciones | Pide a un administrador que habilite el perfil que necesitas. No hay forma de desplegar sin uno. |
| **"Selecciona una versión."** | No hay versión elegida (o el sistema no tiene ninguna) | Elige la versión. Si el sistema no tiene versiones, hay que crearla primero (lo hace un administrador). |
| **"El perfil seleccionado no tiene servidores."** | El perfil existe pero no tiene servidores asignados | Un administrador debe asignárselos. |
| **"❌ Error: El perfil no tiene servidores activos asignados."** | Todos los servidores del perfil están dados de baja | Un administrador debe reactivarlos o corregir el perfil. |
| Aviso *Sin permiso*: **"El perfil «X» no está habilitado para el rol Operaciones."** | El permiso del perfil cambió después de que abriste la pantalla | Cambia de pestaña y regresa para recargar la lista. El intento queda registrado en la bitácora. |
| **"❌ Error: El paquete de la versión no está disponible ni en disco ni en Blob Storage."** | Ni hay copia local del ZIP ni el paquete está en Azure | Despliega desde el equipo donde se generó la versión, o pide que se vuelva a generar o a subir a Azure. |
| En el registro: **"No se puede respaldar antes de desplegar: falta configurar Azure Blob Storage…"** | El respaldo previo está activado pero sin Azure configurado no hay dónde guardarlo, y sin respaldo no se despliega | Desmarca **💾 Respaldar antes** para desplegar sin respaldo (sin red de seguridad), o pide a un administrador que configure Azure Blob Storage para poder respaldar. |
| **"No se pudieron descargar N archivo(s) del respaldo (p.ej. …)"** | La carpeta remota no se pudo bajar completa | Ese servidor no se despliega. Revisa permisos/estado del servidor con un administrador y reintenta. |
| Muchas líneas **"⚠ …: intento n/6 falló … Reintentando en Xs…"** | Conexión lenta o inestable | Normal: deja que trabaje. Los archivos ya subidos no se repiten. |
| **"servidor X: agotados 6 intentos. Último error: …"** | El servidor no respondió tras 6 reconexiones | Verifica host, puerto, usuario y contraseña con un administrador, y que el servidor esté arriba. Ese servidor cuenta como fallido. |
| El resumen dice *completado* pero el Historial dice **Cancelado** o **Parcial** | El renglón de estado solo cuenta servidores OK/fallidos | Confía en la columna *Estado* de **Historial**: es el estado real del trabajo. |
| No encuentras un despliegue viejo | El Historial muestra los **200 más recientes** | Ubícalo por fecha; si es más antiguo, pídelo a un administrador (queda en la bitácora de auditoría). |

### Al dar de alta un servidor

| Síntoma | Causa | Qué hacer |
|---|---|---|
| **"Nombre, Host, Usuario y Ruta remota son obligatorios."** | Falta un campo | Complétalos. |
| **"La contraseña es obligatoria para un servidor nuevo."** | Dejaste la contraseña vacía | Captúrala. |
| **"Ya existe un servidor llamado «X»."** | El nombre debe ser único | Verifica si el servidor ya estaba dado de alta. Si el existente está mal capturado, pide a un administrador que lo corrija; no crees un duplicado con otro nombre. |
| **"El puerto debe estar entre 1 y 65535."** | Puerto inválido | Corrígelo (FTP suele ser 21). |
| No ves los botones *✏ Editar*, *🗑 Dar de baja* ni *📥 Importar JSON* | Son exclusivos de administrador | Es lo esperado. Ver la nota bajo la barra de botones. |

### Al programar

| Síntoma | Causa | Qué hacer |
|---|---|---|
| **"La hora debe ser futura."** / **"La hora programada debe ser al menos un minuto en el futuro."** | Fecha y hora en el pasado o demasiado cerca | Elige una hora al menos un minuto adelante. |
| **"Selecciona versión y perfil."** | Falta alguno de los dos | Complétalos. |
| **"No se puede cancelar: está en estado X."** | Solo se cancela lo que está en 🕓 Programado | Si ya corrió, revisa el resultado en Historial. |
| Estado **⚠ Perdido** | Nadie dejó la aplicación abierta dentro de la tolerancia | Vuelve a programarlo y asegúrate de dejar un equipo con la aplicación en la bandeja. |
| Estado **❌ Fallido** con resultado *"Job #N: Parcial…"* | Unos servidores sí y otros no; para la agenda eso no es éxito | Abre el Job #N en Historial y revisa qué servidor falló. |

### Generales

| Síntoma | Causa | Qué hacer |
|---|---|---|
| Aviso *Despliegue en curso*: **"Hay un DESPLIEGUE EN CURSO…"** al cerrar sesión o al **Salir** | Estás cortando un despliegue a medias | Si no quieres cancelarlo, responde **No** y espera a que termine. Si respondes **Sí**, el despliegue se cancela y lo ya subido se queda como esté. Mandar la ventana a la bandeja con la ✕ **no** lo interrumpe. |
| Aviso *Sin permiso*: **"No tienes acceso a esa sección."** | Se intentó abrir una pantalla que no es de tu rol | Ciérralo. Tus pantallas son **Avisos**, **Despliegues** y **Programados**. |
| Cerraste con la ✕ y sale el globo **"Sigo aquí"** | La aplicación se fue a la bandeja, no se cerró | Es lo correcto para dejar corriendo un programado. Para cerrarla de verdad: clic derecho en el ícono de la bandeja → **Salir**. |
| **"Usuario o contraseña incorrectos."** al entrar | Credenciales mal escritas | Reintenta con cuidado: varios intentos fallidos bloquean la cuenta temporalmente. |
| **"Cuenta bloqueada temporalmente por intentos fallidos. Reintenta a las HH:mm."** | Se acumularon intentos fallidos | Espera a la hora indicada o pide a un administrador que la desbloquee. |

---

## Lo que NO puedes hacer y por qué

**Navegación**

- **No ves el Dashboard ni ningún otro módulo.** El Dashboard muestra carga del equipo, recordatorios internos y ranking de desempeño; nada de eso es de tu área. Tu menú son Avisos, Despliegues y Programados, y si por cualquier vía se intenta abrir otra pantalla sale *"No tienes acceso a esa sección."*
- **En Despliegues no ves las pestañas Perfiles, Blobs ni Respaldo BD.** Son exclusivas de administrador.

**Sistemas y versiones**

- **No puedes crear, editar ni eliminar sistemas ni versiones**, ni editar el changelog. Lo lees porque necesitas saber qué vas a subir; escribirlo es de quien libera. En tu pantalla la ventana de changelog abre en modo *(solo lectura)* y ni siquiera tiene botón Guardar.

**Perfiles**

- **Solo puedes desplegar los perfiles que un administrador marcó como permitidos para Operaciones.** No es solo que el combo los oculte: la regla se vuelve a verificar contra el perfil real al momento de desplegar y de programar, así que un intento por otra vía falla y queda registrado en la bitácora con tu nombre.
- **No puedes crear perfiles ni cambiar qué servidores incluyen.**
- **No puedes elegir servidores sueltos por fuera de un perfil.** La selección directa de servidores es exclusiva de administrador; tu destino siempre es un perfil habilitado.

**Servidores**

- **Puedes dar de alta, pero no editar, dar de baja ni reactivar.** Un servidor mal editado o borrado a media tarde rompe los despliegues de todo el equipo, y la contraseña de un servidor existente no debería poder reapuntarse a otro host sin que lo revise alguien. Si te equivocaste al capturar, lo corrige un administrador; no hay ventana de gracia ni excepciones.
- **No puedes importar servidores desde JSON.**

**Despliegues**

- **No puedes elegir el respaldo previo servidor por servidor.** Puedes activarlo o desactivarlo para el despliegue completo con la casilla **💾 Respaldar antes** (viene marcada), pero decidir cuáles servidores se respaldan y cuáles no es exclusivo de administrador. Si dejas el respaldo activo y Azure Blob Storage no está configurado, el despliegue falla en lugar de correr sin red de seguridad.
- **No puedes revertir un despliegue desde la aplicación.** El respaldo de la carpeta remota queda en Azure y la restauración la hace un administrador. Por eso importa que anotes el número de Job.
- **Cancelar no deshace lo ya subido.** Detiene lo que falta, no regresa lo que ya se escribió.
- **No puedes borrar ni modificar el historial ni los registros.** Cada despliegue, cada intento denegado y cada alta de servidor quedan en la bitácora de auditoría con tu usuario.

**Programados**

- **No puedes ejecutar a la fuerza una cita antes de su hora**, ni recuperar una marcada como **Perdido**: hay que volver a programarla.
- **No puedes cancelar una cita que ya está corriendo o que ya terminó.**
- **Programar no garantiza que se ejecute.** Depende de que una aplicación quede abierta a esa hora. Es una limitación conocida del diseño y por eso la pantalla lo advierte en pantalla, no en letra chica.
