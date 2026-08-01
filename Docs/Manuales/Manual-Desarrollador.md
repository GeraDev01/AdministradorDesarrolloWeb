# Administrador de Desarrollo Web
## Manual del Desarrollador

**Rol: Desarrollador**  
**Versión del manual:** 1.3  ·  **Fecha:** 27 de julio de 2026

> Este manual describe únicamente lo que la aplicación hace de verdad. La misma aplicación
> se reparte a todo el equipo: el menú y los permisos dependen del rol de la cuenta con la
> que inicias sesión. Si tu menú no coincide con lo que aquí se describe, revisa con qué
> cuenta entraste.

---
## Qué puedes hacer con esta aplicación

Con una cuenta de rol **Desarrollador** la aplicación te sirve para **ver tu trabajo asignado** (requerimientos y tickets de Azure DevOps), **medir el tiempo que le dedicas**, **dejar constancia de tus compromisos (SLA)**, **consultar tus evaluaciones** y **gestionar tus vacaciones**. Todo lo que ves y modificas es tuyo: la aplicación no te deja tocar datos de otros desarrolladores ni la configuración del equipo.

Tu menú lateral tiene exactamente estas opciones, en este orden:

| Opción del menú | Para qué |
|---|---|
| 🔔 Avisos | Los avisos que te llegan cuando te asignan un ticket de DevOps o un requerimiento; doble clic abre el enlace |
| 📊 Dashboard | Foto general del estado de los requerimientos y las entregas de los próximos 7 días |
| 📊 Mi Panel | Tu equipo, tus puntos del mes, tu posición en el ranking, tus tickets de DevOps y el registro de actividades (autocalificación) |
| 📋 Mis Asignaciones | Los requerimientos asignados a ti y el cronómetro para medir el tiempo |
| 🔷 Mis tickets DevOps | Los work items de Azure DevOps a tu nombre: comentar con evidencia, cambiar prioridad y abrirlos |
| 🧩 Mis Actividades | Trabajo que no cae en ningún requerimiento asignado, también con cronómetro |
| 📄 Mis Evaluaciones | Tus evaluaciones e hitos registrados por tu líder y la descarga de tu reporte en PDF |
| ⏱ Mis SLA | Compromisos con fecha límite y el botón para comentar el ticket en Azure DevOps |
| 🏖 Mis Vacaciones | Tu saldo de días, solicitar días, adjuntar respaldo, cancelar y eliminar solicitudes |
| 💡 Sugerencias | Enviar sugerencias o propuestas para mejorar el producto o el departamento, darles seguimiento y votar las propuestas del equipo |

Al iniciar sesión la aplicación te deja directamente en **Mi Panel**. Arriba a la derecha siempre ves tu nombre y tu rol, así: `👤 Tu Nombre (Desarrollador)`.

---

## Entrar y salir de la aplicación

1. Escribe tu **Usuario** y tu **Contraseña** y pulsa **Iniciar sesión**.
2. Si es tu primer ingreso con una contraseña temporal, se abre la ventana **Cambio de contraseña requerido** con el aviso «Debes cambiar tu contraseña temporal antes de continuar». La nueva contraseña debe tener **al menos 8 caracteres** y escribirse dos veces igual.
3. Para salir, pulsa **🚪 Cerrar sesión** al final del menú lateral y confirma en «¿Cerrar sesión?».

Ojo con la **X** de la ventana: **no cierra la aplicación**, la deja escondida en la bandeja del sistema vigilando tus SLA (lo explicamos en la sección de avisos). Para cerrarla de verdad hay que usar **Salir** en el menú del ícono de la bandeja.

Por eso mismo, **la aplicación solo se abre una vez**: si le vuelves a dar doble clic estando ya corriendo, no arranca otra — reaparece la que ya tenías, aunque estuviera escondida en la bandeja. Si alguna vez parece que «no pasa nada» al abrirla, busca su ícono junto al reloj.

### Tu estado

Arriba a la derecha, junto a tu nombre, hay un botón con tu **estado**: 🟢 Disponible · 🔴 Ocupado · 📅 En reunión · 🍽 Comiendo · ☕ En un descanso. Pulsa y elige; con **✏ Estado con nota…** puedes añadir una línea corta («vuelvo 15:30»). El administrador lo ve en su pantalla *Quién está*.

Dos cosas que conviene que sepas, para que no te sorprendan:

- **Se guarda a qué hora entras y a qué hora sales** (tu jornada), como registro de asistencia.
- **Tu estado NO se guarda minutado.** Se ve en el momento y se sobrescribe: no queda un histórico de cuánto tiempo estuviste en cada uno. Marcar «en un descanso» no deja rastro de cuánto duró.

Mientras la aplicación esté abierta manda una señal cada dos minutos. Si se cuelga o se apaga el equipo, tu jornada se cierra sola **con la hora de esa última señal** — no con la de cuando alguien vuelva a abrirla.

---

## Avisos

Es tu bandeja de avisos personales. Aquí llegan mensajes como «te asignaron un ticket en DevOps» o «el administrador te asignó un requerimiento». Los avisos son **por usuario** y quedan guardados hasta que los marcas leídos.

- Columnas: un punto **●** al inicio (los no leídos van en negritas), *Fecha*, *Tipo* (🔷 Ticket DevOps, 📋 Requerimiento o 🔔 Aviso), *Aviso* y *Detalle*.
- **✓ Marcar todo leído** pone en leído toda la lista; **🔄 Recargar** vuelve a leer.
- **Doble clic** sobre un aviso lo marca como leído y, si trae un enlace, lo abre en tu navegador.

Cuando tienes avisos sin leer, el botón **🔔 Avisos** del menú muestra el contador y, si la ventana está escondida, puede aparecer un globo en la bandeja: «N aviso(s) sin leer. Ábrelos en el menú «Avisos».». Si no tienes ninguno, la pantalla dice «No tienes avisos.».

---

## Foro

**💬 Foro**, arriba del menú. Es el espacio del equipo para compartir ideas, preguntas y lo que vas aprendiendo. **Lo ve y lo escribe todo el mundo**, sea cual sea su rol.

Dos pestañas de lo mismo: **🗂 Muro** (tarjetas con scroll, para el día a día) y **📋 Auditoría** (rejilla con filtros, para encontrar algo que se dijo hace meses).

### Publicar y conversar

1. **➕ Publicar**: elige el **Tema** (💡 Idea · ❓ Pregunta · 📗 Aprendizaje · 📣 Anuncio · 💬 Otro), pon **Título**, **Etiquetas** (opcionales, separadas por coma) y el texto.
2. Se abre el hilo. Ahí escribes comentarios, o pulsas **↩ Responder** en un comentario concreto para colgarte de él — así se arman los subhilos.
3. **❤** para apoyar una publicación o un comentario. Se quita pulsando otra vez.

Clic en cualquier parte de una tarjeta del muro abre su hilo. El muro se ordena por **última actividad**: un hilo viejo que revive vuelve a subir.

### Lo que puedes cambiar de lo tuyo

- **✏ Editar** — solo lo que tú escribiste. Queda marcado *(editado)*.
- **🗑 Retirar** — solo lo tuyo (el administrador puede retirar cualquier cosa).

> **Retirar no borra.** La entrada se queda con un aviso *«(contenido eliminado por su autor)»* en su lugar del hilo. Es a propósito: si desapareciera, las respuestas que le contestan dejarían de tener sentido. Su **texto** ya no aparece en las búsquedas.
>
> Dicho de otro modo: **lo que escribas queda**, aunque lo retires. Piénsalo como una conversación de trabajo, no como un chat que se borra.

Un hilo puede aparecer **🔒 cerrado**: el administrador decidió terminar esa conversación. Se sigue leyendo, pero ya no admite comentarios — tampoco respondiendo a un comentario de dentro.

---

## Dashboard

Es una vista de solo lectura. No hay botones ni filtros.

**Tarjetas superiores.** Seis contadores **de lo tuyo**: *Por estimar*, *En desarrollo*, *Por entregar*, *Entregados*, *Cancelados* (solo requerimientos **asignados a ti**) y *Pendientes* (recordatorios sin completar **que llevan tu nombre**). Antes contaban los de toda el área, así que podías ver «18 en desarrollo» sin que ninguno fuera tuyo.

**⏰ Próximas entregas (7 días).** Tabla con las columnas *Requerimiento*, *Estado* y *Fecha*. Lista hasta 15 requerimientos **tuyos** con fecha de compromiso dentro de los próximos 7 días que no estén entregados ni cancelados. Los que ya pasaron de fecha salen **en rojo y negritas**.

> Si tu cuenta no está vinculada a una ficha de desarrollador, las tarjetas salen en cero: no hay a quién atribuirle los requerimientos. Pide al administrador que vincule tu usuario.

Con tu rol, el Dashboard **no** muestra la carga por desarrollador, los recordatorios internos ni el top del ranking: esas tres secciones son de administración y ni siquiera se cargan.

---

## Mi Panel

Es tu tablero personal y la única pantalla donde puedes registrar actividades para puntos.

### La barra superior

- **Ranking del mes:** una lista de meses y un año (entre 2020 y 2099). Cambiar cualquiera de los dos recarga la pantalla. Este período afecta a los KPIs de puntos y a los dos rankings, **no** al tiempo dedicado ni a la lista de asignaciones.
- **📝 Registrar actividad** — abre la autocalificación (ver más abajo).
- **🔄 Recargar** — vuelve a leer los datos.

### Las tarjetas (KPIs)

| Tarjeta | Qué muestra exactamente |
|---|---|
| 👥 Mi equipo | El nombre de tu equipo actual. Si pasas el mouse encima, un globo te dice el equipo y tu papel: *Líder del equipo* o *Integrante*. Si no estás asignado a ninguno dice **Sin equipo** |
| ✅ Aprobado (mes) | Suma de los puntos **aprobados** en el mes y año seleccionados |
| ⏳ En revisión | Suma de los puntos que registraste y siguen **pendientes** de aprobación |
| ❌ Rechazado | **Cuántas** de tus entradas de ese mes fueron rechazadas (es un conteo, no una suma de puntos) |
| 🏅 Mi posición | Tu lugar en el ranking individual del período, con el formato `#3 de 12` |
| ⏱ Tiempo dedicado (total) | Todo el tiempo que llevas cronometrado en todos tus items. **No** se filtra por mes |
| 🔷 Tickets DevOps (abiertos) | Cuántos de tus tickets de Azure DevOps siguen abiertos (sin cerrar). El detalle está en la pestaña *Mis tickets DevOps* y en la pantalla del mismo nombre |

### Las cinco pestañas

- **📋 Mis asignaciones** — columnas *ID*, *Título*, *Estado*, *Prioridad*, *Avance* y *⏱ Tiempo dedicado*. Es una lista de consulta: aquí no hay cronómetro (para eso está la pantalla *Mis Asignaciones*). Si no tienes nada, aparece la línea «(no tienes requerimientos asignados)».
- **🔷 Mis tickets DevOps** — columnas *ID*, *Tipo*, *Título*, *Estado* y *Actualizado*. Es una vista de consulta de tus work items de Azure DevOps; los cerrados salen atenuados y el doble clic abre el ticket en el navegador. Para comentarlos o cambiarles la prioridad usa la pantalla *Mis tickets DevOps*. Si no hay, sale «(no encontramos tickets de DevOps a tu nombre — revisa tu correo con el administrador)».
- **📝 Mis actividades y puntos** — columnas *Fecha*, *Criterio*, *Puntos*, *Origen* (*Autocalificación* o *Asignado por jefe*), *Estado* (✅ Aprobado, ⏳ Pendiente o ❌ Rechazado) y *Motivo / comentario del jefe*. Aquí ves **por qué** te rechazaron una entrada: el motivo que escribió el jefe aparece en la última columna, en rojo (y si te rechazaron sin escribir nada, dice «(sin motivo registrado)»). Si no tienes nada, «(no tienes actividades ni puntos registrados)».
- **🏅 Ranking individual** — columnas *Pos.*, *Desarrollador* y *Puntos*. Salen todos los desarrolladores activos; **tu fila queda resaltada en amarillo**. Los tres primeros llevan medalla 🥇🥈🥉.
- **👥 Ranking por equipo** — columnas *Pos.*, *Equipo* y *Puntos*. El total de cada equipo es la suma de los puntos aprobados de sus integrantes más los puntos propios del equipo. **Tu equipo queda resaltado**.

En los dos rankings **solo cuentan los puntos aprobados**. Lo que tienes en revisión o rechazado no aparece ahí.

### Registrar actividad (autocalificación)

1. Pulsa **📝 Registrar actividad**. Se abre la ventana **Registrar actividad (autocalificación)**, encabezada con el aviso «Los puntos que registres quedan PENDIENTES hasta que el jefe los apruebe».
2. Elige la **Actividad / criterio**. La lista muestra el nombre y lo que vale, por ejemplo `Documentación entregada (+5 pts)`. Solo aparecen criterios activos, individuales y que otorguen puntos positivos.
3. Mira el recuadro **Puntos que otorga esta actividad**. Es una **etiqueta de solo lectura**, no un campo: lo fija el criterio que eligió el administrador. Al lado lo dice la propia ventana: «Lo define el administrador en el criterio. Solo él puede ajustarlo al revisar».
4. Confirma el **Período** (mes y año). Viene con el mes actual.
5. Opcional: elige un **Requerimiento** para vincular la actividad. La lista solo trae tus requerimientos asignados que no estén entregados ni cancelados; puedes dejar **(Ninguno)**.
6. Opcional pero muy recomendable: escribe el **Comentario / evidencia**.
7. Opcional: adjunta una **Captura de pantalla** con **📷 Adjuntar** (elegir un archivo de imagen), **📋 Pegar** (toma la imagen del portapapeles; usa `Win+Shift+S` para recortar la pantalla) o **✖ Quitar**. Máximo 15 MB.
8. Pulsa **Enviar a revisión**.

Si todo salió bien verás el mensaje «Actividad registrada (+N pts). Queda pendiente de aprobación» bajo el título *Enviado*, y la entrada aparecerá sumada en la tarjeta **⏳ En revisión**.

**Dos reglas que conviene tener claras:**

- **Los puntos no son editables.** Tú eliges *qué* actividad registras, no *cuánto* vale. Aunque llegaras a la ventana por otra vía, el sistema vuelve a leer el puntaje del criterio antes de guardar.
- **Nada cuenta hasta que el jefe apruebe.** Mientras esté pendiente no suma en el ranking ni en tu posición. Si te la rechazan, se ve en la tarjeta **❌ Rechazado** y el motivo queda en la pestaña **📝 Mis actividades y puntos**.

---

## Mis Asignaciones

Aquí están los requerimientos asignados a ti y el cronómetro con el que se mide el tiempo real de trabajo.

### La tabla

Filtro **Estado:** arriba a la izquierda (*Todos* más cada uno de los estados). Junto al filtro hay dos botones —**🔄 Traer mis tickets DevOps** y **📅 Tiempo por día**— y el recordatorio «Selecciona un item para cronometrar el tiempo que le dedicas».

Columnas: *ID*, *Título*, *Estado*, *Prioridad*, *Hrs Estimadas*, *F. Compromiso*, *Avance* y *⏱ Tiempo dedicado*. Las filas cuya fecha de compromiso ya pasó y que no están entregadas ni canceladas se pintan **en rojo**.

Los estados posibles de un requerimiento son: *Por estimar*, *Estimado*, *En desarrollo*, *En pruebas*, *Por entregar*, *Entregado* y *Cancelado*.

### Traer tus tickets de DevOps y ver el tiempo por día

Al abrir esta pantalla, la aplicación **trae automáticamente** los tickets de Azure DevOps asignados a ti y los agrega como requerimientos, para que puedas cronometrarlos aquí. Si quieres forzarlo, pulsa **🔄 Traer mis tickets DevOps**: responde «Se trajeron N ticket(s) de DevOps a tus asignaciones» o «No hay tickets nuevos de DevOps asignados a ti». Esto **no sincroniza** DevOps —eso es del administrador—, solo materializa como requerimientos los tickets que ya están a tu nombre.

**📅 Tiempo por día** abre un reporte con el desglose del tiempo por fecha. Eliges el rango con **Desde** y **Hasta** (o los atajos **Hoy** y **Últimos 30 días**, que es el rango inicial) y la tabla muestra, agrupado por día, cada *Item* con su *Tiempo*, más el **Total del rango** al pie. Si no hay nada, dice «(sin tiempo registrado en este rango)».

### El cronómetro

El panel de abajo muestra el item seleccionado, el tiempo acumulado, el estado del cronómetro y tres botones:

| Botón | Cuándo está disponible | Qué hace |
|---|---|---|
| **▶ Iniciar** / **▶ Reanudar** | Siempre que no esté corriendo. Dice *Reanudar* si el item quedó pausado | Arranca o continúa la medición |
| **⏸ Pausar** | Solo mientras está en curso | Consolida lo corrido y deja la sesión abierta |
| **⏹ Detener** | Si está en curso o pausado | Cierra la sesión y guarda el total |

El indicador de estado dice **● En curso**, **⏸ Pausado** u **○ Sin sesión activa**.

**Para cronometrar un requerimiento:**

1. Selecciona su fila en la tabla.
2. Pulsa **▶ Iniciar**. El tiempo empieza a correr y se actualiza cada segundo, tanto en el número grande como en la columna *⏱ Tiempo dedicado* de la fila.
3. Cuando te interrumpan, pulsa **⏸ Pausar**; para retomar, **▶ Reanudar**.
4. Al terminar la jornada o el trabajo en ese item, pulsa **⏹ Detener**.

**Tres cosas importantes del cronómetro:**

- **Solo puede haber un cronómetro corriendo.** Si inicias otro item —o una actividad de *Mis Actividades*— el que estaba corriendo se **pausa solo**, sin preguntarte. Es a propósito: si no, el mismo rato se contaría dos veces.
- **Iniciar mueve el requerimiento a «En desarrollo».** Si el item estaba en *Por estimar* o *Estimado*, al pulsar **▶ Iniciar** pasa automáticamente a **En desarrollo** y queda anotado en la bitácora. Si ya estaba más avanzado (en pruebas, por entregar…), su estado no se toca.
- **Detener no borra nada.** Puedes volver a iniciar el mismo item después; se abre una sesión nueva y el tiempo se sigue sumando al total.

Si cierras la aplicación correctamente, lo que estuviera corriendo se pausa y el tiempo se guarda. Si el equipo se apaga de golpe o la aplicación se cae, al siguiente arranque esa sesión se pasa a *Pausada* **descartando el último tramo**, porque no hay forma de saber a qué hora dejaste de trabajar. Ese tramo perdido no se recupera: es la razón para pausar o detener antes de irte.

### El tiempo se puede registrar en Azure DevOps

Cuando pulsas **⏹ Detener** sobre un item que es un **ticket de Azure DevOps**, y **solo si el administrador activó esta función**, la aplicación registra en el work item el tiempo que le dedicaste, usando **tu PAT personal** (el mismo que capturas en *Mis SLA* o *Mis tickets DevOps*). Según lo que haya configurado el administrador, eso puede ser un **comentario** en el ticket —del tipo «⏱ Tiempo registrado desde la app: +… (total dedicado: …).»— y/o la actualización de los campos **Completed Work** y **Remaining Work** del work item.

No tienes que hacer nada extra: ocurre solo al detener. En el panel del cronómetro aparece un aviso discreto: **«✓ Tiempo registrado en DevOps (…).»** si se pudo, o **«⚠ No se registró en DevOps: …»** si algo falló. Si el item no es un ticket de DevOps, o la función está desactivada, no aparece ningún aviso.

> **El cronómetro nunca se rompe por esto.** El tiempo local ya quedó guardado antes de intentar el registro; si DevOps falla (red, PAT, o un tipo de work item que no admite horas de trabajo…), solo verás el aviso ámbar y podrás reintentarlo la próxima vez que detengas.
>
> **No se duplican horas.** La aplicación solo reporta el tiempo **nuevo** desde el último registro; si vuelves a cronometrar y detienes el mismo ticket, únicamente se envía el tramo que aún no se había reportado.

---

## Mis tickets DevOps

Es la lista de los **work items de Azure DevOps asignados a ti**. **Sincronizas tú, sin esperar a nadie**: con tu PAT personal la aplicación le pregunta a DevOps qué hay asignado a tu cuenta. Comentar, abrir y cambiar la prioridad también van con **tu PAT**, para que todo quede firmado a tu nombre.

### Sincronizar tus tickets

**⟳ Sincronizar mis tickets** trae de DevOps lo asignado **a la cuenta de tu PAT**, dentro de la ventana de tiempo que tengas elegida en el combo. Antes había que esperar a que el administrador sincronizara: un ticket recién asignado no aparecía hasta que a otra persona le diera por hacerlo.

Dos cosas que conviene saber:

- **Pregunta por la cuenta de tu PAT, no por tu correo.** Usa la marca `@Me` de DevOps, que resuelve el servidor. Por eso funciona **aunque el correo de tu ficha no coincida** con el de tu cuenta de DevOps — que era justo lo que hacía fallar el empate.
- **Si el botón está gris**, te falta capturar tu PAT: pulsa **🔑 Mi PAT de DevOps**. Al pasar el ratón por encima, el botón te lo dice.
- Con *Todo el historial* elegido, la sincronización se acota a **un año**: traer de golpe años de work items cerrados por una sola pulsación no le sirve a nadie.

### La pantalla

En la barra: **⟳ Sincronizar mis tickets**, **🔄 Actualizar** (solo relee lo que ya está en la base, sin ir a DevOps), un buscador (*Buscar por título, ID, estado…*), **🔑 Mi PAT de DevOps** y **🧱 Columnas**.

**Filtros** (con **Limpiar** para dejarlos todos como estaban):

| Filtro | Por omisión |
|---|---|
| Ventana de tiempo | *Últimos 90 días* (también *30 días*, *Último año*, *Todo el historial*) |
| Estado · Tipo · Iteración | Todos — se arman con lo que de verdad hay en tus tickets |
| **Solo sin cerrar** | **Marcado** |

Esos dos valores por omisión son el arreglo de fondo: la pantalla traía **el historial completo**, así que años de tickets cerrados tapaban los tres que tienes abiertos hoy.

Columnas: *ID*, *Tipo*, *Título*, *Estado*, *Prioridad*, *Iteración*, *Pts*, *💬* (cuántos comentarios lleva) y *Actualizado* — escóndelas y recupéralas con **🧱 Columnas**. Abajo se lee «N de M ticket(s) · K sin cerrar · última sincronización: dd/mm/aaaa hh:mm». La cuenta de **sin cerrar** es tu pendiente real y **no cambia con el filtro**.

Si tu cuenta no está vinculada a un desarrollador, o todavía no has sincronizado, la pantalla lo explica en un mensaje central.

### Abrir, comentar y cambiar prioridad

- **Doble clic** sobre una fila **abre el ticket en Azure DevOps**. **Doble clic sobre la columna 💬** abre los comentarios.
- **Clic derecho** sobre una fila despliega el menú: **🔗 Abrir en Azure DevOps**, **💬 Ver / agregar comentarios** y **🔧 Cambiar prioridad…**.

### Comentar con evidencia

Al elegir **💬 Ver / agregar comentarios** se abre la ventana **Comentarios — #N: Título** con el hilo del ticket y, abajo, el recuadro «Nuevo comentario (se publica con TU PAT, a tu nombre):».

Puedes adjuntar **capturas como evidencia**:

- **📎 Imagen…** — elige uno o varios archivos de imagen (PNG, JPG, GIF, BMP, WEBP…).
- **📋 Pegar captura** — pega la imagen del portapapeles (usa `Win+Shift+S` para recortar la pantalla y luego pulsa el botón).

La etiqueta de al lado lista las evidencias adjuntas («📎 N evidencia(s): …») y, si haces **clic sobre ella, las quita todas**. Pulsa **Enviar 💬** para publicar el comentario con las imágenes incrustadas. Puedes enviar **solo evidencia**, sin texto; lo único que no se puede es enviar vacío (sin texto ni imágenes). Como el comentario se firma con **tu PAT**, si aún no lo has capturado en este equipo, primero configúralo en **🔑 Mi PAT de DevOps**.

### Cambiar la prioridad del ticket

**🔧 Cambiar prioridad…** abre la ventana **Cambiar prioridad — #N**. Elige la prioridad en la lista **Nueva prioridad (se actualiza en Azure DevOps)**, que va de **1 — Muy alta** a **4 — Baja** (con **2 — Alta** y **3 — Media** en medio). La ventana te recuerda: «Si hay una política de SLA para esa prioridad, el compromiso se ajusta automáticamente». Pulsa **Aplicar**.

El cambio se escribe en el **work item real de Azure DevOps** (con tu PAT) y, si el administrador configuró una política de SLA para esa prioridad, **tu SLA se recalcula solo**. Si sale bien verás: «Prioridad del ticket #N cambiada a X (Nombre) en DevOps. Si hay una política de SLA para esa prioridad, tu compromiso se ajustó». El cambio también se refleja en la prioridad del requerimiento vinculado.

---

## Mis Actividades

Sirve para el trabajo real que **no** corresponde a ninguno de tus requerimientos asignados: soporte, juntas, investigación, apoyo a otro equipo… La propia pantalla lo dice: «Registra aquí el trabajo que no cae en tus requerimientos asignados».

### La tabla y los botones

Casilla **Ver cerradas** (viene marcada) para incluir o esconder las actividades ya cerradas.

Columnas: *ID*, *Actividad*, *Estado* (🟢 Abierta o ⚪ Cerrada), *Creada*, *⏱ Tiempo dedicado* y *Descripción*. Doble clic sobre una fila abre la edición.

Botones: **➕ Nueva actividad**, **✏ Editar**, **✔ Cerrar** (que cambia a **↩ Reabrir** cuando la actividad ya está cerrada) y **🗑 Eliminar**.

### Crear una actividad

1. Pulsa **➕ Nueva actividad**.
2. Escribe el **Título** (obligatorio, máximo 200 caracteres) y, si quieres, la **Descripción**.
3. Pulsa **Crear**.

La actividad nace **Abierta** y queda seleccionada, lista para cronometrar.

### Cronometrar una actividad

El panel inferior funciona igual que el de *Mis Asignaciones*: **▶ Iniciar** / **▶ Reanudar**, **⏸ Pausar** y **⏹ Detener**. Es el **mismo cronómetro**: iniciar aquí pausa lo que estuviera corriendo en *Mis Asignaciones*, y al revés.

Una actividad **cerrada** no se puede cronometrar; el indicador dice **✔ Actividad cerrada** y **▶ Iniciar** aparece deshabilitado. Su tiempo total sigue siendo consultable.

### Editar, cerrar y reabrir

- **✏ Editar** solo funciona con actividades abiertas. Si la actividad está cerrada te sale «La actividad está cerrada. Reábrela para editarla».
- **✔ Cerrar** pide confirmación mostrando el tiempo registrado y avisando de que, si el cronómetro está corriendo, se detendrá. Al confirmar responde «Actividad cerrada».
- **↩ Reabrir** vuelve a dejarla abierta, sin confirmación, y a partir de ahí puedes editarla y cronometrarla de nuevo.

### Por qué no siempre se puede eliminar

**🗑 Eliminar** solo funciona si la actividad **no tiene nada de tiempo registrado**. Si lo tiene, la aplicación se niega con:

> «La actividad tiene 2h 15m 30s de tiempo registrado y no se puede eliminar. Ciérrala si ya terminaste.»

La razón es simple: ese tiempo es la evidencia del trabajo que hiciste, y borrarlo falsearía tu total acumulado. Para una actividad terminada la salida correcta es **✔ Cerrar**, no eliminar. Eliminar está pensado para actividades creadas por error, antes de arrancar el cronómetro.

---

## Mis Evaluaciones

Es la vista de solo lectura de **tus** evaluaciones de desempeño e hitos, tal como los registró tu líder. La barra superior lo resume: «Tus evaluaciones e hitos, tal como los registró tu líder. Doble clic en una evaluación para ver el detalle completo». Aquí **no editas nada** —eso es del líder—; lo que sí puedes es **descargar tu reporte en PDF**.

### Tus evaluaciones

Tabla superior con las columnas *Fecha*, *Periodo*, *Calificación* (en estrellas, por ejemplo `★★★★☆ (4/5)`, o *Sin calificar*), *Evaluó* y un resumen de *Fortalezas / debilidades (resumen — doble clic para ver todo)*.

**Doble clic** en una evaluación abre el detalle completo: *Fortalezas*, *Debilidades / áreas de mejora* y *Comentarios*. Si aún no tienes ninguna, la pantalla dice «Todavía no tienes evaluaciones registradas por tu líder».

### 🏆 Mis hitos

Tabla inferior con las columnas *Fecha*, *Tipo* (🏆 Logro, 📁 Proyecto, 🎓 Certificación, ⭐ Reconocimiento o • Otro), *Título* y *Descripción*. Si no hay ninguno, muestra «(sin hitos registrados)».

### Descargar tu reporte en PDF

Pulsa **📄 Descargar mi reporte (PDF)**. Se abre un diálogo para guardar el archivo (con un nombre por defecto tipo `Mi_Reporte_20260725.pdf`). Al terminar, la aplicación pregunta «PDF generado: … ¿Abrirlo ahora?». **🔄 Recargar** vuelve a leer los datos.

---

## Mis SLA

### Qué es un compromiso

Un **SLA** es un compromiso con **fecha y hora límite** sobre un requerimiento tuyo o sobre una actividad libre tuya, normalmente ligado a un **ticket (work item) de Azure DevOps**. Mientras está vigente, la aplicación te recuerda cada cierto número de horas que dejes **un comentario de avance en ese ticket**: ese comentario es la constancia de que lo estás atendiendo.

**Los SLA los crea, ajusta, da por cumplidos y cancela el administrador.** Lo que tú puedes hacer es **comentar el ticket**, **posponer el recordatorio** y **abrir el ticket**.

Algunos compromisos se crean **automáticamente** a partir de la **prioridad del ticket en DevOps**: el administrador define, por prioridad, en cuántas horas hay que atender y cada cuánto recordar que se comente. Por eso, cuando cambias la prioridad de un ticket (desde *Mis tickets DevOps*), tu SLA se **recalcula solo**.

### La pantalla

Casilla **Ver también los cerrados** para incluir el historial (cumplidos, vencidos, cancelados). Sin marcarla solo ves los activos.

Bajo la barra de botones hay una línea de resumen que dice una de estas tres cosas:

- «No tienes compromisos pendientes.»
- «N compromiso(s) en plazo. Nada urgente.»
- «⚠ N compromiso(s) requieren que comentes el ticket ahora.» (en rojo)

Columnas de la tabla: *ID*, *Objetivo* (el requerimiento o la actividad), *Ticket*, *Vence*, *Falta*, *Estado*, *Comentarios* (cuántos llevas y la fecha del último) y *Notas*. Las filas vencidas se pintan con fondo rojizo y las que ya toca comentar, con fondo amarillo.

### Los estados

| Estado en pantalla | Qué significa | Qué debes hacer |
|---|---|---|
| 🟢 En plazo | Vigente y todavía no toca recordatorio | Nada por ahora |
| 🔔 Toca comentar | Vigente y ya llegó la hora del recordatorio | Comenta el ticket |
| ⚠ Fuera de plazo | Vigente pero ya pasó la fecha límite | Comenta cuanto antes; el administrador ya recibe el escalamiento |
| ✅ Cumplido | El administrador lo dio por atendido | Nada; es historial |
| ❌ Vencido | Pasó de fecha y se cerró como incumplido | Nada; es historial |
| ⚪ Cancelado | El administrador lo dejó sin efecto | Nada; es historial |

### El PAT personal de Azure DevOps

Para publicar comentarios necesitas **tu propio Personal Access Token (PAT)**. Los comentarios quedan firmados en DevOps con **tu** cuenta: por eso el token es personal y no se comparte — si todos usaran el mismo, el SLA dejaría de probar quién atendió.

**Cómo obtenerlo:** en Azure DevOps, tu foto (arriba a la derecha) → **Personal access tokens** → **New Token**, con el permiso **Work Items → Read & write**. Copia el token en cuanto DevOps te lo muestre: después ya no se puede volver a ver.

**Cómo guardarlo:**

1. En *Mis SLA*, pulsa **🔑 Mi PAT**. Se abre la ventana **Mi PAT de Azure DevOps**.
2. Pega el token en **Personal Access Token**. El botón **👁** te deja verificar lo que pegaste.
3. Pulsa **🔍 Probar conexión**. Si el token sirve, responde algo como «✓ Conectado como «Tu Nombre» en el proyecto X» — así confirmas que es tu cuenta y no otra.
4. Pulsa **Guardar**.

El token **se guarda solo en esa computadora, cifrado con tu cuenta de Windows**. No viaja a la base de datos compartida y ninguna otra persona —ni siquiera otra cuenta del mismo equipo— puede leerlo. Consecuencia práctica: si trabajas en dos computadoras, tienes que capturarlo en cada una. Es el **mismo PAT** que usa la pantalla *Mis tickets DevOps* (allí el botón se llama **🔑 Mi PAT de DevOps**).

Si vuelves a abrir la ventana y ya había uno guardado, verás «✓ Ya tienes un token guardado en este equipo» y el campo dirá «ya hay un token guardado — escribe uno nuevo para reemplazarlo». Para quitarlo (por ejemplo, en un equipo prestado) usa **🗑 Quitar de este equipo** y confirma.

### Comentar el ticket

1. Selecciona el compromiso en la tabla.
2. Pulsa **💬 Comentar en DevOps**. El botón solo está activo si el compromiso está **Activo** y **tiene un ticket ligado**.
3. Si aún no capturaste tu PAT, sale el aviso *Falta tu PAT*: «Para comentar en Azure DevOps necesitas capturar tu Personal Access Token… ¿Capturarlo ahora?». Responde **Sí** y sigue los pasos del apartado anterior.
4. Se abre la ventana **Comentar el ticket #N** con un borrador ya escrito del tipo «Avance: tiempo dedicado 3h 12m 05s. ». Complétalo con lo que hiciste y lo que falta.
5. Opcional: adjunta **capturas como evidencia** con **📎 Imagen…** o **📋 Pegar captura** (misma mecánica que en *Mis tickets DevOps*: `Win+Shift+S` para recortar la pantalla). La etiqueta lista lo adjuntado y, si haces clic en ella, lo quita. Puedes publicar **solo con evidencia**; si dejas todo vacío, la ventana avisa «Escribe el comentario o adjunta una evidencia».
6. Pulsa **Publicar en DevOps**.

Si sale bien verás «Comentario publicado en el ticket #N», el contador de la columna *Comentarios* sube y **el siguiente recordatorio se reprograma** solo. Si DevOps falla, la aplicación te lo dice y **no marca nada como comentado**: la constancia solo cuenta si de verdad quedó en el ticket.

El texto se publica **tal cual** en Azure DevOps, donde lo puede leer todo el que tenga acceso al ticket. Léelo antes de publicarlo.

### Posponer

**⏰ Posponer** aplaza el recordatorio **4 horas** y **no cambia la fecha límite**. Sirve para cuando ya estás en eso y el recordatorio te está saliendo cada rato, no para ganar plazo: si las 4 horas caen después del vencimiento, el recordatorio se queda pegado al vencimiento.

### Abrir el ticket

**🔗 Abrir ticket** abre el work item en tu navegador. Solo está disponible si el administrador guardó la dirección del ticket al crear el compromiso.

---

## Los avisos de SLA y la bandeja del sistema

La aplicación revisa tus compromisos **al entrar y cada 5 minutos**, aunque no estés en la pantalla *Mis SLA*.

**En el menú lateral.** Cuando hay compromisos que requieren atención, el botón del menú cambia a `⏱ Mis SLA (3)` y se pone en color de alerta. El número desaparece cuando ya no queda nada urgente.

**Con la ventana a la vista.** Sale un cuadro de diálogo titulado **Recordatorio de SLA** —o **SLA fuera de plazo** si alguno ya pasó de fecha— con la lista de hasta 5 compromisos, su fecha de vencimiento y la indicación «Entra a «Mis SLA» para comentarlos».

**Con la ventana escondida o minimizada.** En vez del diálogo aparece un **globo en la bandeja del sistema** (junto al reloj de Windows). **Si tocas el globo, la aplicación se abre directamente en Mis SLA.**

El mismo aviso no se repite cada cinco minutos, pero **sí vuelve a salir** cuando aparece un compromiso nuevo o cuando uno se pasa de la fecha límite (eso es información nueva y más grave).

### Cerrar con la X deja la aplicación vigilando

Al pulsar la **X** de la ventana, la aplicación **no se cierra**: se esconde en la bandeja y te lo dice con un globo:

> **Sigo aquí** — «La aplicación quedó en la bandeja vigilando tus SLA. Para cerrarla del todo, clic derecho → Salir.»

Sobre el ícono de la bandeja tienes:

- **Doble clic** o **Abrir** → vuelve a mostrar la ventana.
- **Salir** → cierra la aplicación de verdad. Lo que estuviera cronometrándose se pausa y el tiempo se guarda.

Cerrar sesión con **🚪 Cerrar sesión** **apaga la vigilancia** y quita el ícono de la bandeja: sin sesión no hay a quién avisar. Si quieres seguir recibiendo recordatorios, deja la sesión abierta y usa la X.

Si el administrador tiene configurado el correo, además de estos avisos puedes recibir un correo con la lista de tus compromisos pendientes. El correo es un complemento; el aviso dentro de la aplicación siempre se da.

---

## Mis Vacaciones

### La pantalla

A la izquierda de la barra se muestra tu **saldo de vacaciones**: **🌴 Días de vacaciones disponibles: N**, donde *N* = los días que te asignó RH en tu ficha **menos** los días ya **aprobados** del año en curso (el descuento es automático). Debajo, una línea de detalle: «De X asignado(s) · Y tomado(s) este año · Z pendiente(s) de aprobación · Ingreso: dd/mm/aaaa». El número se pone **ámbar cuando quedan 3 o menos** y **rojo al llegar a 0 o menos**.

Botones: **➕ Nueva solicitud**, **📎 Ver adjunto**, **🚫 Cancelar** y **🗑 Eliminar**. Los tres últimos se activan o desactivan según la solicitud que tengas seleccionada.

Columnas: *ID*, *Inicio*, *Fin*, *Días*, *Estado*, *Adjunto*, *Mi comentario* y *Respuesta / motivo del jefe* (lo que respondió quien la revisó). Doble clic sobre una fila abre el documento adjunto.

Estados posibles: **⏳ Pendiente**, **✅ Aprobada**, **❌ Rechazada** y **🚫 Cancelada**.

### Solicitar vacaciones

1. Pulsa **➕ Nueva solicitud**. Se abre **Nueva Solicitud de Vacaciones**.
2. Elige la fecha de **Inicio** y la de **Fin**. A la derecha se va calculando `(N día(s))`; si pones el fin antes del inicio, el contador se pone en rojo.
3. Opcional: escribe un **Comentario**.
4. Opcional: pulsa **📎 Adjuntar archivo** para subir el **Documento de respaldo** (PDF, DOCX, DOC, PNG, JPG…). Máximo **15 MB**. Para quitarlo antes de enviar, **✖ Quitar**.
5. Pulsa **Solicitar**.

La solicitud queda en estado **⏳ Pendiente** esperando la revisión del administrador. Si la fecha fin es anterior a la de inicio, la ventana no te deja guardar: «La fecha fin debe ser posterior o igual a la fecha inicio».

### Ver el respaldo que adjuntaste

Selecciona la solicitud y pulsa **📎 Ver adjunto** (o haz doble clic en la fila). El documento se abre con el programa que Windows tenga asociado. El botón solo se activa si la fila muestra **📎 Sí** en la columna *Adjunto*.

### Cancelar y eliminar: qué se puede según el estado

| Estado | 🚫 Cancelar | 🗑 Eliminar | Por qué |
|---|---|---|---|
| ⏳ Pendiente | Sí | Sí | Aún no es una decisión de nadie: puedes retirarla por completo |
| ✅ Aprobada | Sí | **No** | Ya hay una decisión del administrador; es historial. Si no vas a tomar los días, cancélala |
| ❌ Rechazada | **No** | **No** | Es historial de una decisión; no se toca |
| 🚫 Cancelada | **No** | Sí | Ya no tiene efecto; puedes limpiarla de tu lista |

**Cancelar una solicitud:**

1. Selecciona la fila y pulsa **🚫 Cancelar**.
2. Confirma. Si la solicitud **ya estaba aprobada**, la advertencia lo dice con todas sus letras: «Esta solicitud YA FUE APROBADA. Al cancelarla dejarás de tomar esos días».
3. Al terminar responde «Solicitud cancelada». La solicitud pasa a **🚫 Cancelada** y en *Respuesta / motivo del jefe* queda anotado quién la canceló y cuándo.

**Eliminar una solicitud:**

1. Selecciona la fila y pulsa **🗑 Eliminar**.
2. La confirmación te avisa si se van a borrar también documentos generados a partir de esa solicitud, y te recuerda que **la acción no se puede deshacer**.
3. Al confirmar responde «Solicitud eliminada».

---

## Sugerencias

### Para qué sirve

Es tu buzón para **enviar sugerencias o propuestas de mejora** —del **producto** (la aplicación) o del **departamento**— y **darles seguimiento**: ver en qué estado están y qué te respondió el administrador. Tú envías y sigues las tuyas; **quien las revisa, cambia el estado y responde es el administrador**.

La pantalla tiene **dos pestañas**:

- **💡 Mis sugerencias** — enviar tus propuestas y darles seguimiento (lo de siempre: ver, eliminar).
- **👍 Propuestas del equipo** — ver **todas** las propuestas del equipo y apoyarlas con tu **voto**, para que las más pedidas suban.

### 💡 Mis sugerencias

En esta pestaña están **tus** propuestas. Botones de la barra: **➕ Nueva sugerencia**, **👁 Ver**, **🗑 Eliminar** y **🔄 Recargar**. **👁 Ver** se activa al seleccionar una fila; **🗑 Eliminar** solo se habilita mientras la sugerencia siga en estado **Nueva**.

Columnas: *Fecha*, *Categoría*, *Título*, **Quién la ve**, *Estado* y *Respuesta*. Doble clic sobre una fila abre la sugerencia. Al pie, una línea de resumen dice «N sugerencia(s) enviada(s)» o, si aún no has mandado ninguna, «Todavía no has enviado sugerencias. Pulsa «➕ Nueva sugerencia» para proponer una mejora.».

### Enviar una sugerencia

1. Pulsa **➕ Nueva sugerencia**. Se abre la ventana **Nueva sugerencia**.
2. En **¿Sobre qué es tu propuesta?** elige la categoría: **Producto (la aplicación)**, **Departamento** u **Otro**.
3. Escribe el **Título** (al menos 3 caracteres) y la **Descripción (qué propones y por qué mejora)** (al menos 5 caracteres).
4. Opcional: marca la casilla **Enviar sin mostrar mi nombre al administrador** para mandarla de forma anónima (ver más abajo).
5. En **¿Quién la ve?** elige el alcance, y decide si abrirla a votos (ver el apartado siguiente).
6. Pulsa **Enviar 💡**.

### ¿Quién la ve? y ¿se vota?

Son **dos decisiones distintas**, y las tomas tú al enviarla:

| **¿Quién la ve?** | Qué significa |
|---|---|
| **Pública (todo el equipo)** *(por omisión)* | Aparece en **👍 Propuestas del equipo**. Tus compañeros la leen. |
| **Solo administrador** | **Nadie del equipo la ve**, ni siquiera aparece en tu propio tablero de propuestas. Para lo que no quieres plantear en público: el ambiente del área, una queja, algo delicado. Le sigues dando seguimiento aquí, en *Mis sugerencias*. |

**Abrirla a los votos del equipo** *(marcado por omisión)* decide si tus compañeros pueden apoyarla con 👍. Si la desmarcas, la propuesta **se lee pero no se vota**: hay cosas que no son un concurso de popularidad y que aun así conviene plantear. En la lista salen con un **—** en la columna de votos, no con un 0 — que se leería como «nadie la apoyó» cuando en realidad nadie puede.

Si eliges **Solo administrador**, la casilla de votación se apaga y se bloquea sola: no tendría sentido una votación que el equipo ni siquiera ve.

La columna **Quién la ve** te lo resume de un vistazo: *🔒 Solo administrador*, *👥 Pública · se vota* o *👥 Pública · sin votación*.

> **Anónima y «solo administrador» son cosas distintas.** La primera esconde **quién** lo dijo; la segunda limita **quiénes lo leen**. Puedes combinarlas.

Si todo está bien, sale «Sugerencia enviada. ¡Gracias!» y la sugerencia aparece en la lista en estado **Nueva**. Si dejas el título o la descripción demasiado cortos, la ventana te lo pide con «Escribe un título (al menos 3 caracteres).» o «Describe tu sugerencia (al menos 5 caracteres).».

### Los estados y ver la respuesta

Una sugerencia pasa por estos estados: **Nueva**, **En revisión**, **Aceptada**, **Rechazada** e **Implementada**. Los cambia el administrador conforme la atiende.

Con **👁 Ver** (o doble clic) se abre la sugerencia en modo de solo lectura: ves la categoría, el título, la descripción, el **Estado** y la **Respuesta del administrador** (si todavía no te contesta, dice «(sin respuesta todavía)»). Pulsa **Cerrar** para salir.

> **Recibes un aviso cuando el administrador responde tu sugerencia.** Llega a tu bandeja de **🔔 Avisos** como «💡 Respondieron tu sugerencia», con el título y el nuevo estado.

### Eliminar una sugerencia

**🗑 Eliminar** solo funciona con **tus** sugerencias y **únicamente mientras sigan en estado Nueva**. Al pulsarlo confirma con «¿Eliminar tu sugerencia «…»?». Si el administrador ya empezó a atenderla (cualquier estado que no sea *Nueva*), el botón está deshabilitado y, si se intentara, responde «Ya no puedes eliminarla: el administrador empezó a atenderla.».

### Qué pasa si la envías de forma anónima

La casilla **Enviar sin mostrar mi nombre al administrador** hace que tu sugerencia sea **anónima de cara al administrador**:

- En el panel del administrador tu sugerencia aparece como **Anónima**, sin tu nombre.
- Ningún aviso al administrador ni la **Bitácora** te delatan: el registro de una sugerencia anónima se guarda como acción del sistema, sin usuario ni título.
- **Pero no es un borrado del vínculo:** la aplicación conserva en la base la relación entre la sugerencia y tú, para que **TÚ** puedas seguir viéndola en esta pantalla y enterarte cuando te respondan. Es anónima frente al administrador, no una sugerencia sin dueño.

### 👍 Propuestas del equipo

La segunda pestaña, **👍 Propuestas del equipo**, muestra las propuestas **públicas** del equipo (no solo las tuyas), ordenadas de la **más votada a la menos votada**, para que las mejoras más pedidas suban a la vista de todos.

Las marcadas **Solo administrador** no salen aquí — **ni siquiera para quien las escribió**. Es a propósito: si su propio autor las viera en el tablero del equipo, no habría forma de saber que nadie más las está leyendo. Para darles seguimiento está *Mis sugerencias*.

Columnas: **👍** (número de votos), *Categoría*, *Título*, *Estado* y *De* (quién la propuso). Las propuestas enviadas de forma anónima salen como **Anónima** en la columna *De*, sin nombre. Al pie, la línea de resumen dice «N propuesta(s) del equipo · ordenadas por más votadas. Selecciona una y pulsa «👍 Votar».» o, si no hay ninguna, «No hay propuestas todavía.».

Botones de la barra: **👍 Votar**, **👁 Ver** y **🔄 Recargar**. Con **👁 Ver** (o doble clic) abres la propuesta en modo de solo lectura, igual que en *Mis sugerencias*.

**👍 Votar** se queda gris cuando el autor no abrió esa propuesta a votación (columna 👍 con un **—**). Puedes leerla y comentarla en persona; lo que no puedes es apoyarla.

**Votar una propuesta:**

1. Selecciona la fila de la propuesta en la tabla.
2. Pulsa **👍 Votar**. Tu apoyo queda registrado, la propuesta suma un voto y la lista se reordena manteniendo seleccionada esa misma propuesta.
3. Si ya habías votado esa propuesta, el botón aparece como **✓ Quitar voto**: púlsalo para retirar tu apoyo.

> **Cada persona vota una vez por propuesta.** El botón alterna entre **👍 Votar** y **✓ Quitar voto** según si ya la apoyaste; las propuestas que ya votaste aparecen con su número de votos resaltado en la columna **👍**.

---

## Problemas frecuentes

### «Tu cuenta no está vinculada a un desarrollador»

**Dónde:** en Mi Panel, Mis Asignaciones, Mis tickets DevOps, Mis Actividades, Mis Evaluaciones y Mis SLA. En Mis Vacaciones aparece como «No se encontró el desarrollador vinculado a tu usuario».
**Causa:** tu usuario existe pero no está ligado a una ficha de desarrollador, así que la aplicación no sabe de quién son las asignaciones, los puntos ni el tiempo.
**Qué hacer:** pídele al administrador que vincule tu usuario con tu ficha de desarrollador. Hasta entonces esas pantallas salen vacías y los botones deshabilitados.

### «No tienes acceso a esa sección»

**Causa:** se intentó abrir una pantalla que no corresponde a tu rol.
**Qué hacer:** usa las opciones de tu menú. Si necesitas algo de administración, pídelo a tu jefe.

### «No hay criterios positivos disponibles. Pide al administrador que configure algunos»

**Dónde:** al pulsar **📝 Registrar actividad**.
**Causa:** no hay ningún criterio de puntuación activo, individual y con puntos positivos.
**Qué hacer:** pide al administrador que dé de alta los criterios en la configuración de desempeño.

### «El criterio «X» fue desactivado» / «El criterio seleccionado ya no existe»

**Causa:** el administrador cambió los criterios mientras tenías la ventana abierta.
**Qué hacer:** cierra la ventana, pulsa **🔄 Recargar** y vuelve a registrar la actividad con un criterio vigente.

### ««X» no otorga puntos positivos. Los descuentos los aplica el administrador»

**Causa:** se intentó autocalificar con un criterio de puntos negativos.
**Qué hacer:** nada. Los descuentos son exclusivos del administrador; elige un criterio positivo.

### «La actividad tiene … de tiempo registrado y no se puede eliminar. Ciérrala si ya terminaste»

**Causa:** intentaste borrar una actividad libre que ya acumuló tiempo cronometrado.
**Qué hacer:** usa **✔ Cerrar** en vez de **🗑 Eliminar**. Eliminar solo sirve para actividades creadas por error, sin tiempo.

### «La actividad está cerrada. Reábrela para editarla»

**Causa:** pulsaste **✏ Editar** sobre una actividad cerrada.
**Qué hacer:** pulsa **↩ Reabrir**, edita y vuelve a cerrarla.

### «La actividad ya no existe. Actualiza la lista» / «La solicitud ya no existe. Actualiza la lista»

**Causa:** el registro se borró o cambió desde otra sesión mientras tu pantalla mostraba datos viejos.
**Qué hacer:** cambia de pantalla y vuelve, o recarga; la lista se refresca sola al entrar.

### «No tienes permiso para operar sobre datos de otro desarrollador» (título: *Sin permiso*)

**Causa:** la operación apuntaba a datos que no son tuyos.
**Qué hacer:** verifica qué fila tenías seleccionada. Si insiste, avisa al administrador: puede ser que tu usuario esté vinculado a la ficha equivocada.

### El cronómetro que dejé corriendo aparece con menos tiempo del esperado

**Causa:** la aplicación se cerró de forma anormal (crash, apagón, reinicio forzado). Al volver a arrancar, las sesiones que quedaron activas se pasan a *Pausada* **descartando el último tramo**, porque no se puede saber a qué hora dejaste de trabajar; si no se hiciera, contarían noches y fines de semana enteros.
**Qué hacer:** pulsa **⏸ Pausar** o **⏹ Detener** antes de irte, y cierra con **Salir** desde la bandeja cuando termines la jornada.

### El tiempo de un item dejó de correr solo

**Causa:** iniciaste el cronómetro de otro requerimiento o de una actividad. Solo puede haber uno corriendo.
**Qué hacer:** es el comportamiento correcto. Vuelve al item anterior y pulsa **▶ Reanudar** cuando retomes ese trabajo.

### «No encontramos tickets de DevOps a tu nombre»

**Dónde:** en *Mis tickets DevOps* (y en la pestaña del mismo nombre de *Mi Panel*).
**Causa:** el empate se hace por **tu correo**, y el de tu ficha de desarrollador no coincide con el de tu cuenta de DevOps, o el administrador aún no ha sincronizado Azure DevOps.
**Qué hacer:** pide al administrador que verifique tu correo en la ficha y que sincronice. Tú no sincronizas: solo lees lo que él ya trajo.

### «No tienes un PAT de Azure DevOps configurado en este equipo»

**Causa:** nunca capturaste tu token en esa computadora, o lo quitaste, o el archivo local se corrompió.
**Qué hacer:** pulsa **🔑 Mi PAT** (o **🔑 Mi PAT de DevOps**) y captúralo. Recuerda que es por computadora.

### «DevOps respondió 401 Unauthorized. Revisa el PAT y sus permisos»

**Causa:** el token caducó, se revocó o se creó sin el permiso **Work Items → Read & write**.
**Qué hacer:** genera un token nuevo en Azure DevOps con ese permiso, pégalo en **🔑 Mi PAT**, pulsa **🔍 Probar conexión** y **Guardar**.

### «Falta la URL de organización o el proyecto de Azure DevOps. Pídelo al administrador»

**Causa:** la instalación no tiene configurada la organización o el proyecto de DevOps. Eso no es algo que tú puedas capturar.
**Qué hacer:** avisa al administrador.

### «No se pudo publicar el comentario en DevOps: …»

**Causa:** falló la conexión con Azure DevOps (red, VPN, token, ticket inexistente).
**Qué hacer:** el comentario **no** se registró y el compromiso sigue exigiéndolo. Revisa tu conexión y vuelve a intentar; el texto que escribiste se pierde, así que si era largo cópialo antes de reintentar.

### «No se pudo enviar el comentario: …»

**Dónde:** al pulsar **Enviar 💬** en los comentarios de un ticket de *Mis tickets DevOps*.
**Causa:** falló la conexión con Azure DevOps (red, VPN, PAT, ticket inexistente).
**Qué hacer:** revisa tu conexión y tu PAT y vuelve a intentar. El comentario no se publicó.

### «No se pudo cambiar la prioridad: …»

**Dónde:** al aplicar **🔧 Cambiar prioridad…** en un ticket de DevOps.
**Causa:** DevOps rechazó el cambio (PAT sin permiso de escritura, ticket inexistente, red/VPN).
**Qué hacer:** confirma que tu PAT tiene **Work Items → Read & write**, revisa la conexión y reintenta.

### «No se pudieron traer los tickets de DevOps: …»

**Dónde:** al pulsar **🔄 Traer mis tickets DevOps** en *Mis Asignaciones*.
**Causa:** todavía no hay tickets sincronizados, o falló el acceso a la base compartida.
**Qué hacer:** pide al administrador que sincronice Azure DevOps; la pantalla sigue funcionando con lo que ya tenías.

### «No se pudo generar el PDF: …»

**Dónde:** al pulsar **📄 Descargar mi reporte (PDF)** en *Mis Evaluaciones*.
**Causa:** falló la generación del PDF o el componente que lo produce no está disponible en el equipo (la aplicación lo avisa antes de intentarlo).
**Qué hacer:** vuelve a intentar; si el aviso dice que falta el convertidor, avisa al administrador.

### El botón **💬 Comentar en DevOps** está gris

**Causa:** el compromiso seleccionado ya no está activo (cumplido, vencido, cancelado) o no tiene ticket de DevOps ligado.
**Qué hacer:** si debía tener ticket, pídele al administrador que lo ligue al compromiso.

### «No se puede eliminar una solicitud «Aprobada»: es parte del historial. Si ya no la vas a tomar, cancélala»

**Causa:** intentaste borrar una solicitud que ya fue revisada.
**Qué hacer:** usa **🚫 Cancelar**.

### «El archivo supera 15 MB» / «La imagen supera 15 MB»

**Causa:** el adjunto pesa demasiado.
**Qué hacer:** comprime el PDF o reduce la resolución de la imagen antes de adjuntarla.

### «No hay una imagen en el portapapeles»

**Dónde:** al pulsar **📋 Pegar** (autocalificación) o **📋 Pegar captura** (evidencia de un comentario en *Mis tickets DevOps* o en *Mis SLA*).
**Qué hacer:** usa `Win+Shift+S` para recortar la pantalla y vuelve a pulsar el botón de pegar.

### «No se pudo leer la imagen del portapapeles. Vuelve a copiarla e inténtalo de nuevo»

**Dónde:** al pulsar **📋 Pegar captura** para adjuntar evidencia.
**Causa:** el portapapeles tenía algo que Windows no pudo entregar como imagen.
**Qué hacer:** vuelve a tomar la captura (`Win+Shift+S`) y pulsa de nuevo **📋 Pegar captura**.

### «Esta solicitud no tiene documento adjunto»

**Causa:** pulsaste **📎 Ver adjunto** (o doble clic) sobre una solicitud sin respaldo.
**Qué hacer:** nada; la columna *Adjunto* te dice de antemano cuáles tienen documento.

### «Usuario o contraseña incorrectos» / «Cuenta bloqueada temporalmente por intentos fallidos»

**Causa:** credenciales equivocadas; tras varios fallos la cuenta se bloquea un rato y el mensaje te dice a qué hora puedes reintentar.
**Qué hacer:** espera a la hora indicada. Si no recuerdas tu contraseña, pide al administrador que te genere una temporal; al entrar con ella la aplicación te obligará a cambiarla.

### Cerré la ventana y la aplicación sigue en la barra de tareas

**Causa:** es el comportamiento buscado. La X esconde la aplicación en la bandeja para seguir vigilando tus SLA.
**Qué hacer:** si de verdad quieres cerrarla, clic derecho en el ícono de la bandeja → **Salir**.

---

## Lo que NO puedes hacer y por qué

- **No ves el módulo de administración.** Desarrolladores, Equipos, Contactos, Requerimientos, Métricas, Reportes, Minutas, Vacaciones/Notas, Permisos, Desempeño, Actividades libres del equipo, SLA y recordatorios, Despliegues, Recursos Azure, Correo, Usuarios, Bitácora y Configuración no aparecen en tu menú, y si se alcanzaran por otra vía la aplicación responde «No tienes acceso a esa sección». Tu rol es de ejecución, no de administración del equipo.
- **No decides cuánto valen tus puntos.** En *Registrar actividad* eliges **qué** actividad registras; el puntaje lo fija el criterio que configuró el administrador y se muestra como texto, no como campo editable. Aunque se intentara por otra ruta, el sistema relee el puntaje del criterio antes de guardar.
- **No puedes registrar puntos negativos ni aprobar los tuyos.** Solo aparecen criterios positivos, tu entrada nace **Pendiente** y no cuenta en el ranking hasta que el jefe la apruebe. Los descuentos son exclusivos del administrador. Cuando te rechazan una entrada, el motivo que escribió el jefe lo ves en la pestaña **📝 Mis actividades y puntos**.
- **No ves el detalle de los puntos de los demás.** En los rankings solo se muestran nombre y total; ni las entradas, ni los comentarios, ni las evidencias de otros.
- **No editas los requerimientos.** No puedes cambiar su título, descripción, horas estimadas, fecha de compromiso ni porcentaje de avance. El único cambio de estado que provocas es el automático de *Por estimar*/*Estimado* a **En desarrollo** al iniciar el cronómetro. **La excepción es la prioridad:** desde *Mis tickets DevOps* sí puedes cambiar la prioridad de un ticket de Azure DevOps a tu nombre; eso se escribe en DevOps, actualiza la prioridad del requerimiento vinculado y recalcula tu SLA.
- **No te asignas ni te quitas requerimientos.** Las asignaciones las hace el administrador.
- **No sincronizas Azure DevOps.** La sincronización de los tickets la hace el administrador; tú lees los que ya están a tu nombre (empatados por tu correo) y, sobre ellos, comentas —con evidencia— y cambias su prioridad usando tu PAT personal. **🔄 Traer mis tickets DevOps** solo materializa como requerimientos los tickets ya sincronizados, para cronometrarlos.
- **No editas tus evaluaciones ni tus hitos.** Los registra tu líder; tú solo los consultas en *Mis Evaluaciones* y descargas tu reporte en PDF.
- **No creas, cierras ni cancelas SLA, ni mueves su fecha límite.** Eso es del administrador. **⏰ Posponer** solo mueve el **recordatorio** 4 horas y nunca más allá del vencimiento: posponer no sirve para saltarse un SLA. Lo único que puede ajustar tu SLA de forma automática es **cambiar la prioridad del ticket**, y solo si el administrador configuró una política para esa prioridad.
- **No puedes marcar un SLA como cumplido comentando.** Comentar deja constancia y reprograma el recordatorio; darlo por cumplido es una decisión del administrador. Al comentar (en *Mis SLA* o en *Mis tickets DevOps*) sí puedes **adjuntar capturas como evidencia**, e incluso enviar solo la evidencia.
- **No puedes usar el PAT de otra persona ni compartir el tuyo.** El token se guarda cifrado con tu cuenta de Windows en esa computadora y no se sube a la base compartida. Es lo que hace que un comentario en DevOps pruebe que **tú** atendiste el ticket. Si cambias de equipo, captúralo de nuevo ahí.
- **No borras actividades con tiempo registrado.** Ese tiempo es evidencia del trabajo hecho; para una actividad terminada usa **✔ Cerrar**.
- **No apruebas tus vacaciones ni borras el historial.** Solicitas y, si hace falta, cancelas. Las solicitudes aprobadas o rechazadas no se eliminan porque son la constancia de una decisión. Tu **saldo de días** solo se lee: lo asigna RH en tu ficha y se descuenta solo con los días aprobados.
- **No decides el estado ni respondes las sugerencias.** En *Sugerencias* tú envías y das seguimiento a las tuyas; cambiar el estado (a *En revisión*, *Aceptada*, *Rechazada* o *Implementada*) y escribir la respuesta es del administrador. Solo puedes eliminar **tus** sugerencias y únicamente mientras sigan en estado **Nueva**; una vez que el administrador empieza a atenderla, ya no se borra.
- **No tocas datos de otros desarrolladores.** Toda operación sobre tiempo, actividades, SLA o vacaciones se valida contra tu propia ficha; si apunta a otra persona, se rechaza con «No tienes permiso para operar sobre datos de otro desarrollador».
- **No despliegas.** Despliegues, despliegues programados y recursos de Azure son del área de Operaciones y del administrador.
- **En el Dashboard no ves la carga por desarrollador, los recordatorios internos ni el top del ranking.** Son datos del equipo: con tu rol esas secciones ni siquiera se consultan.