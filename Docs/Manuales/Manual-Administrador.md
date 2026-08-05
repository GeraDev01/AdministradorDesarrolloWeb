# Administrador de Desarrollo Web
## Manual del Administrador

**Rol: Administrador**  
**Versión del manual:** 1.4  ·  **Fecha:** 30 de julio de 2026

> Este manual describe únicamente lo que la aplicación hace de verdad. La misma aplicación
> se reparte a todo el equipo: el menú y los permisos dependen del rol de la cuenta con la
> que inicias sesión. Si tu menú no coincide con lo que aquí se describe, revisa con qué
> cuenta entraste.

---
## Qué puedes hacer con esta aplicación

Como **Administrador** ves el menú completo. No hay pantalla ni botón de la aplicación que esté fuera de tu alcance. En concreto puedes:

- Registrar al equipo (desarrolladores, equipos y contactos) y darles acceso a la aplicación.
- Llevar el trabajo: requerimientos, minutas, permisos, vacaciones, métricas y reportes.
- Calificar el desempeño del equipo y aprobar o rechazar lo que los desarrolladores se autoasignan.
- Asignar compromisos de atención (SLA) con recordatorios y vigilar los vencidos.
- Publicar versiones de los sistemas y desplegarlas a servidores por FTP/FTPS, en el momento o programadas.
- Administrar el contenedor de Azure Blob Storage: carpetas, metadatos y enlaces temporales de descarga.
- Sincronizar tickets de Azure DevOps y Freshdesk y vincularlos entre sí.
- Guardar en un solo lugar las plantillas del área: cuerpos de ticket de Freshdesk, respuestas al cliente, observaciones de requerimientos y de DevOps, documentos de entrega de estimaciones y scripts de utilería (SQL, PowerShell, Bash).
- Ver **quién está conectado** y en qué anda, con el **registro de jornadas** por día.
- Participar en el **foro del equipo**, y fijar o cerrar hilos cuando haga falta.
- Crear y desactivar usuarios, consultar la bitácora de auditoría y configurar toda la aplicación (base de datos, Azure, correo e integraciones).

**Cómo se navega.** El menú de la izquierda está en grupos plegables: *Equipo*, *Trabajo*, *Despliegue e Infraestructura*, *Integraciones y Correo* y *Administración*. Pulsa el encabezado de un grupo para abrirlo; al abrir uno se cierran los demás. *Avisos*, *Foro* y *Dashboard* están fuera de cualquier grupo, arriba de todo. Hasta abajo está **🚪 Cerrar sesión**.

**Tu estado.** Arriba a la derecha, junto a tu nombre, hay un botón con tu estado (🟢 Disponible, 🔴 Ocupado, 📅 En reunión, 🍽 Comiendo, ☕ En un descanso), con nota opcional. Lo tienen todos los roles; tú además ves el de los demás en *Equipo → Quién está*.

**La aplicación no se cierra con la X.** Al pulsar la X la ventana se esconde en la bandeja del sistema y sigue vigilando los SLA y ejecutando los despliegues programados. Verás el aviso *"La aplicación quedó en la bandeja vigilando tus SLA. Para cerrarla del todo, clic derecho → Salir."*. Para volver: doble clic en el icono de la bandeja o clic derecho → **Abrir**. Para terminar de verdad: clic derecho → **Salir**.

**Solo se abre una vez.** Si vuelves a abrir el ejecutable estando ya corriendo, no arranca una segunda: la que ya estaba se pone al frente (aunque estuviera escondida en la bandeja o minimizada). No verás ningún aviso de *"ya está abierta"* — la respuesta a abrir la aplicación es que aparezca la ventana. Es a propósito: dos instancias pondrían dos íconos en la bandeja, avisarían dos veces del mismo SLA y dos cronómetros sobre la misma persona contarían el tiempo doble. Si la aplicación se colgara y la mataras desde el Administrador de tareas, puedes volver a abrirla de inmediato; no hay que reiniciar nada.

**🔎 Búsqueda global (Ctrl+K).** Como Administrador, en cualquier pantalla puedes pulsar **Ctrl+K** para abrir un buscador rápido. Escribe al menos 2 caracteres y encuentra a la vez **requerimientos, tickets de Azure DevOps, desarrolladores, sugerencias y plantillas**; con Enter (o doble clic) saltas directo a la pantalla correspondiente. Debajo de la caja se lee *"Escribe al menos 2 caracteres. Enter para abrir, Esc para cerrar."* y, al buscar, *"N resultado(s) · hasta M por tipo; afina si falta alguno."*. Cierra con **Esc**. (Este atajo es solo para el rol Administrador.)

---

## Dashboard

Es la pantalla con la que abres sesión. No tiene botones: solo muestra información y se refresca cada vez que entras.

Arriba hay seis tarjetas con el conteo de requerimientos por estado y de pendientes:

| Tarjeta | Qué cuenta |
|---|---|
| Por estimar | Requerimientos en estado *Por estimar* |
| En desarrollo | Requerimientos en estado *En desarrollo* |
| Por entregar | Requerimientos en estado *Por entregar* |
| Entregados | Requerimientos en estado *Entregado* |
| Cancelados | Requerimientos en estado *Cancelado* |
| Pendientes | Notas de la pantalla Vacaciones/Notas que aún no se completan |

Debajo, cuatro paneles:

- **⏰ Próximas entregas (7 días)** — hasta 15 requerimientos sin entregar cuya fecha de compromiso cae dentro de la semana. Los ya atrasados salen en rojo y negritas.
- **👤 Carga por desarrollador** — activos y por entregar de cada persona.
- **📌 Recordatorios pendientes** — hasta 10 notas sin completar; en rojo las que ya pasaron su fecha de recordatorio.
- **🏆 Top ranking (mes en curso)** — los cinco primeros por puntos. Solo cuentan los puntos **aprobados**.

---

## Equipo → Desarrolladores

Catálogo de las personas del área. Es la base de casi todo lo demás: sin desarrolladores no puedes asignar requerimientos, ni puntos, ni SLA.

**Filtros.** El cuadro *Buscar...* filtra por nombre, correo o teléfono. La casilla **Solo activos** viene marcada.

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Eliminar · 🔐 Acceso · ✉ Correo · 📊 Excel

### Dar de alta un desarrollador
1. Pulsa **➕ Nuevo**.
2. Llena la ficha (nombre completo, correo, teléfono, seniority, fecha de ingreso, dirección, días de vacaciones, notas).
3. Pulsa **Guardar**.

### Darle acceso a la aplicación
1. Selecciona el desarrollador en la lista.
2. Pulsa **🔐 Acceso**.
3. Confirma en *"¿Crear la cuenta de acceso (rol Desarrollador) para 'X'?"*.
4. La aplicación genera el nombre de usuario y una **contraseña temporal** de 12 caracteres y te la muestra en pantalla. Cópiala y entrégasela: no se vuelve a mostrar. La persona deberá cambiarla al iniciar sesión.

Si el desarrollador ya tenía cuenta, el mismo botón te ofrece restablecer su contraseña y genera otra temporal. La columna **Acceso** muestra 🔓 Sí para quienes ya tienen cuenta.

### Escribirle
Selecciona y pulsa **✉ Correo**. Si el correo está configurado y habilitado se abre la ventana de redacción de la aplicación; si no, se abre tu cliente de correo de Windows.

> **Ojo:** **🗑 Eliminar** no borra: pregunta *"¿Desactivar a 'X'?"* y lo marca como inactivo. Se conserva su historial.

---

## Foro

Título en pantalla: *💬 Foro del equipo*. **Lo ve todo el equipo** — administradores, operaciones y desarrolladores —, porque el punto es compartir: si solo lo viera una parte, no sería un foro. Está arriba del menú, fuera de los grupos plegables.

Son **dos vistas de los mismos datos**:

| Vista | Para qué |
|---|---|
| **🗂 Muro** | El día a día: tarjetas con scroll, ❤ y contador de comentarios. Clic en cualquier parte de una tarjeta abre su hilo. |
| **📋 Auditoría** | Reconstruir una conversación: rejilla con *Cuándo · Autor · Tipo · Hilo · Texto · Estado*, filtros y **🧱 Columnas**. Doble clic abre el hilo. |

**Temas:** 💡 Idea · ❓ Pregunta · 📗 Aprendizaje · 📣 Anuncio · 💬 Otro. Cada publicación puede llevar **etiquetas** separadas por coma.

**Filtros del muro:** buscador (mira en título, texto, etiquetas y autor), tema, ventana de tiempo y *Solo mías*. La auditoría añade filtro por **autor**, armado con quien de verdad ha escrito.

### El hilo

Al abrir una publicación se ve arriba, y debajo sus comentarios **anidados**: cada respuesta sangrada bajo aquello a lo que contesta, y los hermanos por fecha. Se comenta en el hilo o se pulsa **↩ Responder** en un comentario concreto para colgarse de él.

La sangría **se corta a los cinco niveles**: más allá la conversación se va al margen derecho y deja de leerse, así que los comentarios siguientes cuelgan del último nivel.

### Enlaces e imágenes

Las direcciones escritas en el texto se vuelven **pulsables** al leer el hilo; con `[nombre](dirección)` se les puede poner nombre. Solo se abren `http` y `https` —todo lo demás se queda como texto—, y al pasar el ratón se ve el destino real antes de pulsar: un enlace del foro acaba abriendo algo en la máquina de quien lee, así que ahí no hay margen.

Cada entrada admite hasta **6 imágenes** (PNG, JPG, GIF, BMP), desde el disco o pegadas del portapapeles. Se guardan **dentro de la base**, reducidas y con una miniatura aparte: el muro y el hilo pintan la miniatura y el original solo viaja cuando alguien la abre. En la rejilla de auditoría, una entrada con imágenes se marca **🖼 n** en la columna *Texto*, para que una captura sin texto no salga en blanco.

> Vigila el **tamaño de la base** si el equipo se apoya mucho en capturas: el tope es de 4 MB por imagen ya reducida. *🧹 Limpieza de datos → Foro del equipo* se lleva publicaciones, comentarios, imágenes y ❤ juntos.

### Nada se borra

**🗑 Retirar** no borra: conserva la entrada y sustituye su texto por *«(contenido eliminado por su autor)»*. Es deliberado — un hilo con respuestas que contestan a algo que ya no está es **peor** de auditar que ver un hueco marcado. Por lo mismo, **editar deja constancia**: la entrada queda marcada *(editado)*.

En la búsqueda, el **texto** de lo retirado ya no se encuentra (si se pudiera, retirarlo no serviría de nada), pero **el título de una publicación retirada sí**, para poder auditarla.

Sus **imágenes dejan de servirse** en cuanto se retira la entrada — tampoco a ti. Es la misma regla que el texto: si una captura se siguiera viendo, retirar no querría decir nada. Las filas siguen en la base, como el cuerpo del mensaje; lo que cambia es que no salen.

### Lo que solo puede el administrador

| Acción | Quién |
|---|---|
| Publicar, comentar, dar ❤ | Cualquiera con sesión |
| Editar | **Solo su autor** |
| Retirar | Su autor **o** el administrador |
| **📌 Fijar** una publicación arriba del muro | Solo administrador |
| **🔒 Cerrar** un hilo (deja de admitir comentarios) | Solo administrador |

> **Cerrar un hilo lo cierra entero**, no solo su primer mensaje: tampoco se puede responder a un comentario de dentro. Si no fuera así, se seguiría conversando por la puerta de atrás en un hilo dado por cerrado. Se puede reabrir.

El orden del muro es por **última actividad**, no por fecha de publicación: un hilo viejo que revive con un comentario nuevo vuelve a subir. Las fijadas van siempre primero.

Todo (publicar, comentar, editar, retirar, fijar, cerrar) queda en la **Bitácora** con el tipo de entidad `ForumPost`.

---

## Equipo → Quién está

Título en pantalla: *🟢 Quién está y registro de jornadas*. Dos pestañas: **🟢 Quién está** (ahora mismo) y **📋 Registro de jornadas** (el histórico por día).

### 🟢 Quién está

Lista **a todas las cuentas activas**, conectadas o no — que alguien falte es justamente el dato. Columnas: el punto de color, *Persona*, *Estado*, *Nota* y *Desde*. Los conectados salen primero y en negritas; los demás en gris, con *«visto dd/mm hh:mm»* o *«nunca ha entrado»*. Se refresca solo cada 30 segundos, y con **🔄 Actualizar** cuando quieras. Tiene **🧱 Columnas**.

**Cómo se decide que alguien está conectado.** La aplicación manda una señal (un «latido») cada **2 minutos** mientras esté abierta. Quien lleve **10 minutos sin dar señales** pasa a desconectado.

No se usa el par inicio/cierre de sesión, y hay una razón concreta: **cerrar con la X no cierra la aplicación**, la deja en la bandeja vigilando SLA — y eso *es* estar conectado. Pero un cuelgue, un apagón o un corte de red no avisan de nada: con solo inicio/cierre, esa persona se quedaría marcada como conectada **para siempre**. La tolerancia es cinco veces el intervalo a propósito, para que bloquear la pantalla o que el equipo suspenda un momento no marque a nadie como ausente.

**Los estados** los elige cada quien desde el botón de su barra superior: 🟢 Disponible · 🔴 Ocupado · 📅 En reunión · 🍽 Comiendo · ☕ En un descanso. Pueden llevar una **nota corta** («vuelvo 15:30»). *Ausente* no se elige a mano: es lo que el sistema dice de quien no está.

> **Los estados no se guardan minutados.** Se ven aquí en vivo y se sobrescriben; **no queda histórico** de cuánto tiempo estuvo alguien en cada uno. Es una decisión deliberada: un registro cronometrado de las pausas de una persona es vigilancia, no asistencia. Lo que sí queda es la jornada — a qué hora entró y a qué hora salió.
>
> La nota de alguien desconectado tampoco se muestra: un *«Comiendo — vuelvo 15:30»* de hace tres días no informa de nada.

### 📋 Registro de jornadas

Elige el **Día** (o pulsa **Hoy**) y ves las jornadas de esa fecha: *Persona*, *Entrada*, *Salida*, *Duración*, *Cierre* y *Equipo* (desde qué máquina). Abajo, el total de jornadas, cuántas personas y la suma de horas.

La columna **Cierre** dice cómo terminó cada una:

| Cierre | Qué pasó |
|---|---|
| **Cerró sesión** | Salió por las buenas (Cerrar sesión, o Salir desde la bandeja). La hora de salida es real. |
| **⚠ Sin señales** | La aplicación dejó de responder: se colgó, se apagó el equipo o se cayó la red. |
| **En curso** | Sigue conectada ahora mismo. |

> **Una jornada «⚠ Sin señales» se cierra con la hora del ÚLTIMO latido, no con la de ahora.** Si a alguien se le apagó el equipo a las 14:00 y nadie lo abrió hasta el día siguiente, su jornada termina a las 14:00 — no a las 9:00 del día siguiente. Cerrarla con la hora actual le regalaría todas las horas que el equipo pasó apagado. Por eso esa salida **no es una hora real de salida**: es la última señal que dio.

---

## Equipo → Equipos

Tablero tipo organigrama. Cada equipo es una columna con el líder arriba y el resto agrupado por rol (Frontend Dev, Backend Dev, Fullstack, QA, DevOps, UX/Diseño, Otro, Sin rol). La última columna, **Sin equipo**, junta a quienes no están asignados a ninguno.

**Botones:** ➕ Nuevo equipo · 🔄 Rotación · 📜 Historial · 📄 Generar PDF · 🔄 Recargar

### Mover a alguien de equipo
- **Arrastrando:** toma la tarjeta con el mouse y suéltala sobre otra columna. Queda registrada la rotación y la persona entra al nuevo equipo **sin rol**.
- **Con el diálogo:** pulsa **🔄 Rotación**, elige personas, equipo destino y una nota.

### Asignar un rol
Clic derecho sobre la tarjeta de la persona y elige el rol. El menú también incluye **🔄 Rotar a otro equipo...** y **❌ Quitar del equipo**.

Al marcar a alguien como **👑 Líder**, el líder anterior de ese equipo pasa a *Sin rol* automáticamente.

### Botones del encabezado de cada columna
- **🗂** — sistemas y proyectos del equipo (marca los sistemas que le pertenecen; alta, edición y borrado de proyectos).
- **✏** — editar nombre, descripción y color del equipo.
- **🗑** — eliminar el equipo. Sus integrantes quedan sin equipo, no se borran.

### Generar el organigrama en PDF
1. Pulsa **📄 Generar PDF**.
2. Elige dónde guardarlo.
3. Al terminar te pregunta si quieres abrirlo.

Requiere LibreOffice instalado y configurado (ver *Configuración*). Si no lo está, el botón te lo dice antes de intentarlo.

---

## Equipo → Contactos

Directorio de personas relevantes fuera del equipo de desarrollo (correo y Teams).

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Eliminar · ✉ Escribir · 💬 Teams · 📊 Excel

El cuadro *Buscar...* busca en nombre, puesto, empresa y correo. **💬 Teams** abre el enlace de Teams del contacto; si no tiene, te avisa.

---

## Trabajo → Requerimientos

El catálogo central de trabajo. Título en pantalla: *📋 Requerimientos*.

**Filtros:** cuadro *Buscar...* (título y descripción), **Estado** y **Dev**.

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Eliminar · 👥 Asignar · 📎 Adjuntos · 📊 Excel · 🔄 Importar DevOps · 🔗 Abrir en DevOps · ⚙ Reglas

Los tres últimos **solo aparecen si la integración con Azure DevOps está habilitada** en Configuración.

### Estados posibles

| Estado | Significado |
|---|---|
| Por estimar | Recién capturado, aún sin estimación |
| Estimado | Ya tiene horas estimadas |
| En desarrollo | Se está trabajando |
| En pruebas | En validación |
| Por entregar | Listo, pendiente de entrega |
| Entregado | Cerrado con entrega |
| Cancelado | Cerrado sin entrega |

Las prioridades son 🔵 Baja, 🟡 Media, 🟠 Alta y 🔴 Crítica.

### Crear un requerimiento
1. Pulsa **➕ Nuevo**.
2. Captura **Título** (obligatorio), Descripción, Estado, Prioridad y **Estimación (hrs)**.
3. Marca o desmarca las tres fechas: *Fecha de solicitud*, *Fecha compromiso* y *Fecha entrega real* (cada una tiene una casilla para dejarla vacía).
4. Ajusta el **Avance (%)** con la barra deslizante.
5. Pulsa **Guardar**.

### Asignar desarrolladores
1. Selecciona el requerimiento.
2. Pulsa **👥 Asignar**.
3. Marca a las personas y acepta. La asignación anterior se reemplaza por completo.

### Adjuntar archivos
Selecciona el requerimiento y pulsa **📎 Adjuntos**. La columna **📎** de la lista muestra cuántos tiene.

### Importar de Azure DevOps
1. Pulsa **🔄 Importar DevOps**.
2. Al terminar verás *Nuevos: N · Actualizados: N* y el listado de los work items nuevos, con a quién se autoasignaron.

Las reglas de autoasignación se editan en **⚙ Reglas**: cada regla tiene una condición, un valor a buscar, el desarrollador destino, un orden de evaluación y una casilla *Regla activa*.

> **Ojo:** **🗑 Eliminar** no borra el requerimiento. Pregunta *"¿Cancelar 'X'?"* y lo pasa a estado **Cancelado**, para no perder su historia.

---

## Trabajo → Métricas

Título en pantalla: *📈 Métricas de Tiempo de Vida*. Todo se calcula a partir de las fechas ya capturadas; aquí no se edita nada.

**KPIs:** Activos · Atrasados · Entregados a tiempo (%) · Edad prom. activos (días).

**Botones:** 🔄 Recalcular · 📊 Excel requerimientos · 📊 Excel por desarrollador

**Pestaña 📋 Requerimientos:** días vivo, días en el estado actual, fecha de compromiso y días restantes o de atraso (los atrasados se marcan en rojo con *"⚠ atraso N"*).

**Pestaña 👤 Por Desarrollador:** activos, atrasados, edad promedio y edad del más antiguo.

---

## Trabajo → Reportes

Centro de reportes con vista previa en tabla y en tablero.

1. Elige el reporte en la lista de la izquierda.
2. Ajusta el período con **Del:** y **Al:**.
3. Si quieres acotar por personas, pulsa **Devs: Todos ▾**, marca a quién y pulsa **Aplicar**.
4. Pulsa **▶ Generar** (también se genera solo al cambiar cualquiera de los filtros).
5. Revisa la pestaña **📋 Datos** o la pestaña **📊 Dashboard** (KPIs y gráfica de barras).
6. Exporta con **📊 Excel** o mándalo por correo con **✉ Enviar** (adjunta el Excel; requiere el correo configurado).

Reportes disponibles:

| Reporte | Qué muestra |
|---|---|
| Carga de trabajo por desarrollador | Activos, atrasados, horas y edad promedio |
| Requerimientos pendientes | Todo lo no entregado ni cancelado, con atraso y avance |
| Avance de requerimientos | Porcentaje de avance por requerimiento |
| Altas por período (tipo de item) | Items dados de alta en el rango, por origen |
| Requerimientos por desarrollador | Requerimientos asignados a cada persona |
| Ciclo de vida de requerimientos | Días vivo, días en estado y lead time |
| Entregas: a tiempo vs tardías | Compromiso contra entrega real y atraso |
| Tickets de DevOps por responsable | Work items agrupados por persona asignada |
| Requerimientos por origen | Conteo por Manual / DevOps / Correo |
| Desempeño (puntos) por período | Puntos individuales sumados en el rango |
| Estimado vs. real (por requerimiento) | Horas estimadas contra horas de cronómetro, con desviación |
| Estimado vs. real (por desarrollador) | Igual, agregado por persona (estimado prorrateado entre asignados) |
| Vacaciones por período | Solicitudes cuyo inicio cae en el rango |
| Rotaciones de equipo por período | Movimientos entre equipos |
| Resumen por equipo | Integrantes, requerimientos activos y puntos propios |

---

## Trabajo → Estimación y capacidad

Título en pantalla: *🎯 Estimación y capacidad*. Reporte de planeación (solo Administrador) que responde a dos preguntas: ¿qué tan bien estimamos? y ¿a quién le puedo asignar más trabajo? Tiene **dos pestañas**, cada una con sus KPIs, su rejilla y su botón **⬇ CSV** (y **🔄 Recargar**). Es de solo consulta.

### Pestaña 🎯 Estimación vs real

Compara las **horas estimadas** de cada requerimiento contra el **tiempo real cronometrado** (la suma de las sesiones de trabajo). Solo entran los requerimientos que tienen horas estimadas.

**KPIs:** Ratio promedio · ✓ Precisos · ▲ Subestimados · ▼ Sobreestimados · Horas estimadas · Horas reales.

**Columnas:** ID · Título · Estado · Estimadas (h) · Reales (h) · Δ (h) · Ratio · Clasificación.

La **Clasificación** compara el tiempo real con lo estimado:

| Clasificación | Cuándo |
|---|---|
| ✓ Preciso | El tiempo real quedó dentro de ±20 % de lo estimado |
| ▲ Subestimado (tomó más) | Tomó bastante más de lo estimado (ratio > 1.2) |
| ▼ Sobreestimado (tomó menos) | Tomó bastante menos de lo estimado (ratio < 0.8) |
| Sin tiempo aún | Tiene estimación pero todavía no hay tiempo cronometrado |

### Pestaña 👥 Capacidad del equipo

Muestra la carga abierta de cada desarrollador activo para saber a quién conviene asignarle. Arriba eliges **Vacaciones en los próximos N días** (30 por omisión) para contar las ausencias que caen en ese rango.

**KPIs:** 🟢 Libres · 🟡 Ocupados · 🔴 Sobrecargados · 🏖 De vacaciones.

**Columnas:** Desarrollador · Req. abiertos · Horas pendientes · Horas registradas · Vacaciones (días) · Disponibilidad.

La **Disponibilidad** se calcula así:

| Disponibilidad | Significado |
|---|---|
| 🟢 Libre | Sin requerimientos abiertos |
| 🟡 Ocupado | Con trabajo, dentro de su capacidad |
| 🔴 Sobrecargado | Más de 40 h estimadas pendientes |
| 🏖 De vacaciones | Está de vacaciones hoy |

---

## Trabajo → Minutas

Título en pantalla: *📝 Minutas*.

**Filtros:** **Tipo** (Todos / Daily / Sesión / Otro), **Desde** y **Hasta**.

**Botones:** ➕ Nueva · ✏ Abrir · 🗑 Eliminar · 📊 Excel

### Levantar una minuta
1. Pulsa **➕ Nueva**.
2. Elige **Tipo** y **Fecha**, escribe el **Título** (obligatorio) y el **Contenido**.
3. En *Compromisos / Items de acción* pulsa **➕ Agregar** por cada compromiso y captura Descripción, Responsable, Fecha límite y la casilla **✓ Hecho**. Para quitar uno, selecciónalo y pulsa **🗑 Eliminar**.
4. Pulsa **Guardar**.

La columna **Pendientes** de la lista muestra en amarillo cuántos compromisos siguen abiertos.

---

## Trabajo → Vacaciones/Notas

Título en pantalla: *🏖 Vacaciones y Notas*. Tiene dos pestañas.

### Pestaña 🏖 Solicitudes de Vacaciones

**Botones:** ➕ Nueva · ✏ Editar · ✅ Aprobar · ❌ Rechazar · 🚫 Cancelar · 📄 Documento / Firmar · 📊 Excel

| Estado | Se muestra como |
|---|---|
| Pendiente | ⏳ Pendiente (amarillo) |
| Aprobada | ✅ Aprobada (verde) |
| Rechazada | ❌ Rechazada (rojo) |
| Cancelada | 🚫 Cancelada (gris) |

**Resolver una solicitud:** selecciónala, pulsa **✅ Aprobar**, **❌ Rechazar** o **🚫 Cancelar** y confirma. Queda registrado quién la revisó y cuándo.

**Generar el documento firmado:**
1. Selecciona la solicitud y pulsa **📄 Documento / Firmar**.
2. Pulsa **🔄 Generar / Previsualizar** para ver el documento.
3. Elige la firma en el combo *Firma:* (o créala en **🖊 Firmas...**).
4. Pulsa **✍ Firmar y exportar PDF**, o **💾 Exportar PDF...** para guardar sin firmar.

La conversión a PDF requiere LibreOffice; si no está disponible te lo dirá.

### Pestaña 📌 Notas y Pendientes

**Botones:** ➕ Nueva · ✏ Editar · ✅ Completar · 🗑 Eliminar · 📊 Excel

Las notas tienen prioridad 🔴 Alta, 🟡 Media o 🔵 Baja y una fecha de recordatorio. Las vencidas sin completar salen en rojo; las completadas, en gris. Estas notas son las que alimentan la tarjeta *Pendientes* y el panel *📌 Recordatorios pendientes* del Dashboard.

---

## Trabajo → Permisos

Título en pantalla: *📋 Permisos*. Registro de permisos y ausencias del equipo (distinto de las vacaciones).

**Filtros:** *Buscar...* (persona, motivo o quién autorizó), **Desarrollador** y **Tipo**.

**Botones:** ➕ Registrar · ✏ Editar · 🗑 Eliminar

Para registrar uno: **➕ Registrar**, elige desarrollador, tipo, fecha, número de días, motivo y quién autorizó, y **Guardar**.

---

## Trabajo → Desempeño

Título en pantalla: *🏆 Desempeño y Ranking*. El botón del menú lleva un contador rojo (🔴 N) cuando hay autocalificaciones esperando tu revisión; se actualiza cada minuto.

Tiene cinco pestañas.

### 🏅 Ranking Mensual
Elige **Mes** y **Año** y pulsa **🔄 Cargar**.

**Botones:** 👁 Ver detalle · 🏅 Asignar puntos · 🗑 Borrar todas del dev · 📊 Excel

Columnas: Pos., Desarrollador, Total Pts, + Premio, − Penaliz., Entradas.

**Asignar puntos:** selecciona al desarrollador, pulsa **🏅 Asignar puntos** y elige el criterio. Si no hay criterios activos verás *"No hay criterios activos. Ve a la pestaña Criterios y crea algunos."*.

**🗑 Borrar todas del dev** elimina **todas** las entradas de esa persona en el mes y año seleccionados, previa confirmación con el número exacto.

### ⏳ Pendientes de aprobación
Aquí llegan los puntos que los desarrolladores se autoasignaron. El puntaje lo fija el criterio: **tú eres el único que puede cambiarlo**.

**Botones:** ✅ Aprobar · ❌ Rechazar · ✏ Ajustar puntos · 👁 Ver captura · 🔄 Recargar

1. Selecciona una o varias filas (se admite selección múltiple).
2. Para revisar la evidencia, pulsa **👁 Ver captura** (o doble clic en la fila). La columna 📷 indica cuáles traen captura.
3. Si el puntaje no corresponde, selecciona **exactamente una** fila y pulsa **✏ Ajustar puntos**; escribe el nuevo número entero (admite negativos). La entrada **sigue pendiente** después del ajuste.
4. Pulsa **✅ Aprobar** o **❌ Rechazar**. Al rechazar se te pide un motivo (opcional).

Si el correo está configurado, a cada desarrollador afectado le llega un correo-resumen y verás *"Se notificó por correo a N desarrollador(es)."*.

### 👥 Ranking por Equipo
Mismo esquema de Mes/Año. **Botones:** 👁 Ver detalle · 🏅 Asignar al equipo · 📊 Excel

Para asignar puntos a un equipo necesitas criterios con ámbito **Equipo**; si no los hay verás *"No hay criterios de equipo activos. Crea uno con 'Aplica a: Equipo' en la pestaña Criterios."*.

### 📋 Entradas de Puntos
Listado plano de todas las entradas del período. **Botones:** 🏅 Asignar puntos · 👁 Ver captura · 🗑 Eliminar seleccionadas

Estados de una entrada: ⏳ Pendiente, ✅ Aprobado, ❌ Rechazado.

### ⚙ Criterios de Evaluación
**Botones:** ➕ Nuevo criterio · ✏ Editar · ⚡ Activar/Desact.

Columnas: Criterio, Pts default, Tipo, Ámbito (*Individual* o *Equipo*), Descripción y Activo. La aplicación siembra por sí sola un catálogo amplio de criterios en el primer arranque; puedes desactivar los que no uses en lugar de borrarlos.

> Solo los puntos **aprobados** cuentan en el ranking y en los reportes. Los pendientes y los rechazados no suman.

---

## Trabajo → Actividades libres

Título en pantalla: *🧩 Actividades libres del equipo*. Responde a "¿en qué se fue el tiempo que no aparece en ningún requerimiento?".

**Filtros:** **Desarrollador** y **Estado** (Todas / Abiertas / Cerradas).

**KPIs:** 🧩 Actividades · 🟢 Abiertas · ⏱ Tiempo fuera de asignaciones

**Botones:** 🕑 Ver sesiones · 📊 Exportar · 🔄 Recargar

Para ver el desglose de tiempo: selecciona la actividad y pulsa **🕑 Ver sesiones** (o doble clic). Verás cada sesión con hora de inicio, hora de fin (o *en curso*) y duración, más el total. Si no tiene tiempo registrado te lo dice.

Esta pantalla es de consulta: aquí no se crean ni se cierran actividades, eso lo hace cada desarrollador.

---

## Trabajo → SLA y recordatorios

Título en pantalla: *⏱ SLA y recordatorios*. Un SLA es un compromiso de atención sobre un requerimiento o una actividad libre, con fecha límite y recordatorios para que el responsable comente el ticket de Azure DevOps.

**Filtro Estado:** Activos · Vencidos · Cumplidos · Cancelados · Todos.

**KPIs:** ⏱ Activos · ⚠ Vencen hoy · ❌ Vencidos

**Botones:** ➕ Asignar SLA · ✔ Cumplido · ✖ Cancelar · 🔔 Revisar vencidos

### Cómo se ven los estados

| En la columna Estado | Qué significa |
|---|---|
| 🟢 En plazo | Activo y dentro de la fecha límite |
| ⚠ Fuera de plazo | Activo pero ya pasó su fecha; aún no se marca como vencido |
| ✅ Cumplido | Cerrado correctamente |
| ❌ Vencido | Marcado como incumplido |
| ⚪ Cancelado | Dejado sin efecto |

Las filas activas, sin comentarios y fuera de plazo se pintan con fondo rojo claro.

### Cierre automático por el estado del ticket

Un SLA cuyo work item ya terminó en Azure DevOps **se cierra solo**, sin que nadie lo marque:

| Estado del ticket | Cómo queda el SLA |
|---|---|
| *Done* · *Closed* · *Completed* **dentro del plazo** | ✅ Cumplido |
| *Done* · *Closed* · *Completed* **fuera del plazo** | ❌ Vencido |
| *Removed* · *Cancelled* | ⚪ Cancelado (no cuenta como incumplimiento) |

Se compara contra la **fecha de cierre real** del ticket (su última modificación en DevOps), no contra el momento en que la aplicación lo detecta: si nadie entra en tres días, un ticket cerrado a tiempo no puede acabar contando como vencido por eso.

Se revisa al abrir *SLA*, *Mis SLA* y *Cumplimiento*, al pulsar **🔔 Revisar vencidos** y en el ciclo de avisos. Solo mira el estado **ya sincronizado** en local: si tu filtro de sincronización excluye los estados cerrados, esos tickets nunca se refrescan y sus SLA se quedan abiertos. Los cambios quedan en la bitácora y en las notas del compromiso.

**Un SLA ya marcado *Vencido* no se resucita**, aunque después se cierre su ticket: ese incumplimiento ya se escaló y hay constancia. Si consideras que se entregó, usa **✔ Cumplido**, que sí deja rastro de quién lo decidió.

### Asignar un SLA
1. Pulsa **➕ Asignar SLA**.
2. En **Aplicar a:** elige *Requerimiento* o *Actividad libre*. En la lista de **Objetivo** solo aparecen requerimientos abiertos (ni entregados ni cancelados) y actividades abiertas.
3. Elige el **Objetivo**. Si el requerimiento vino de Azure DevOps, el número de ticket y la URL se rellenan solos; el **Responsable** se preselecciona con quien ya lo tiene asignado.
4. Ajusta el **Responsable** si hace falta.
5. Fija **Fecha y hora límite** (debe ser futura).
6. Elige **Recordar cada:** 4 horas, 8 horas, 12 horas, 24 horas (diario), 48 horas o *Solo al vencer*.
7. Opcionalmente captura el **Ticket de Azure DevOps** (el número del work item) y su URL. **Sin ticket no hay recordatorio de comentar.**
8. Pulsa **Asignar SLA**.

### Cerrar o anular un SLA
- **✔ Cumplido** — selecciona la fila y pulsa el botón. Deja de generar recordatorios.
- **✖ Cancelar** — pregunta *"¿Dejar sin efecto este SLA?"* y lo deja en *Cancelado*.

### Revisar vencidos
Pulsa **🔔 Revisar vencidos**. La aplicación marca como *Vencido* todo lo que ya pasó su fecha límite y manda el correo de escalamiento al buzón configurado. Verás *"N compromiso(s) marcado(s) como vencido(s)..."* o *"No hay compromisos vencidos sin reportar."*.

Esta misma revisión se ejecuta **sola cada 5 minutos** mientras la aplicación esté abierta (incluso desde la bandeja), así que normalmente no necesitas pulsar el botón.

---

## Trabajo → Cumplimiento SLA

Título en pantalla: *📊 Cumplimiento SLA*. Reporte (solo Administrador) que mide, de los compromisos de SLA con fecha límite dentro de un período, **cuántos se cumplieron a tiempo frente a cuántos se vencieron**. Es de solo consulta.

**Barra:** **Desde:** · **Hasta:** (por omisión, del primer día del mes en curso a hoy) · combo **Agrupar por:** · **⬇ CSV** · **🔄 Recargar**.

**Agrupar por:** *Desarrollador*, *Prioridad*, *Cliente (tag)* o *Mes*. La prioridad y el cliente (tag) salen del ticket de Azure DevOps ligado al SLA, así que solo se reflejan en los compromisos que tengan uno.

**KPIs:** % Cumplimiento · ✓ Cumplidos · ⚠ Vencidos · ● En curso · Cancelados · Total.

**Columnas de la tabla:** Grupo · ✓ Cumplidos · ⚠ Vencidos · ● En curso · Cancelados · Total · % Cumplimiento.

### Cómo se calcula

- El **% de cumplimiento** se mide **solo sobre los ya resueltos** (cumplidos + vencidos): los que siguen en curso y los cancelados no entran en el porcentaje.
- Un compromiso **Activo cuya fecha límite ya pasó cuenta como vencido**, aunque nadie lo haya marcado todavía.
- El **% de cumplimiento** se colorea como semáforo: verde a partir de 90 %, amarillo desde 70 % y rojo por debajo; la columna **⚠ Vencidos** se pinta en rojo cuando hay alguno.

Abajo se resume *"N compromiso(s) con vencimiento en el período · agrupado por X."*; si no hay ninguno verás *"No hay compromisos de SLA con fecha límite en el período elegido."*.

**⬇ CSV** guarda a `cumplimiento_sla_AAAAMMDD.csv` lo que esté en la tabla. Si no hay datos verás *"No hay datos para exportar."*.

---

## Trabajo → Sugerencias

Título en pantalla: *💡 Sugerencias y propuestas*. En el menú aparece como **💡 Sugerencias**. Es donde revisas las sugerencias y propuestas que envía el equipo para mejorar el producto o el departamento: leerlas, filtrarlas, cambiarles el estado y responderlas.

**Botones:** 📝 Atender / responder · 👁 Ver · 🗑 Eliminar

**Filtros:** dos combos desplegables.

- **Estado:** Todos los estados · Nueva · En revisión · Aceptada · Rechazada · Implementada.
- **Categoría:** Todas las categorías · Producto · Departamento · Otro.
- **Visibilidad:** Todas las visibilidades · Pública (todo el equipo) · Solo administrador.

Junto a los combos hay una casilla **Más votadas primero**: al marcarla, la lista se ordena por el número de votos (de mayor a menor) en vez de por fecha.

**Columnas de la lista:** Fecha · De · Categoría · Título · **Quién la ve** · **👍** (votos) · Estado.

- La columna **Quién la ve** dice el alcance que le puso su autor: *🔒 Solo administrador*, *👥 Pública · se vota* o *👥 Pública · sin votación*. Las **🔒 solo administrador** salen **en ámbar**: nadie del equipo las está leyendo, así que si tú no las atiendes no las atiende nadie. Con el combo **Visibilidad** puedes quedarte solo con esas.
- La columna **👍** muestra cuántos votos («me gusta») lleva cada sugerencia del equipo; las que tienen votos salen en negritas. Un **—** significa que su autor no la abrió a votación (no que nadie la haya apoyado).
- Las sugerencias enviadas **como anónimas** muestran **«Anónima»** en la columna **De**; nunca el nombre del autor.
- Las que siguen en estado **Nueva** salen con el **título en negritas**.
- Abajo se resume *"N sugerencia(s)  ·  M sin atender  ·  K solo para ti."* (las *sin atender* son las que están en **Nueva**; las *solo para ti* son las 🔒). Si el filtro no devuelve nada verás *"No hay sugerencias con ese filtro."*.

### Atender o responder una sugerencia
1. Selecciona la sugerencia y pulsa **📝 Atender / responder** (o doble clic en la fila).
2. Se abre el diálogo *Atender sugerencia #N*, que muestra la categoría, de quién viene, la fecha, el título y el cuerpo completo (solo lectura).
3. Elige el nuevo **Estado** en el combo.
4. Si quieres, escribe una **Respuesta para el autor (opcional)**.
5. Pulsa **Guardar**. Al guardar, **al autor le llega un aviso** (*💡 Respondieron tu sugerencia*), incluso si la envió de forma anónima: ese aviso es privado, solo para él.

### Ver
**👁 Ver** abre la sugerencia en modo consulta, sin cambiarle nada.

> **Ojo:** aquí **🗑 Eliminar** sí borra la sugerencia. Pregunta *"¿Eliminar la sugerencia «X»?"* y advierte que la acción no se puede deshacer; la opción por omisión es *No*.

**Recibes un aviso cuando llega una sugerencia nueva** (*💡 Nueva sugerencia*, con su categoría y su título). Si el autor la mandó anónima, el aviso tampoco revela quién fue.

---

## Despliegue e Infraestructura → Despliegues

Título en pantalla: *🚀 Despliegues*. Es la pantalla más grande de la aplicación: como Administrador ves **siete pestañas**.

| Pestaña | Para qué |
|---|---|
| 🚀 Desplegar | Ejecutar el despliegue |
| 🖥 Sistemas y Versiones | Catálogo de aplicativos y sus versiones |
| 🌐 Servidores | Servidores FTP/FTPS destino |
| 🔧 Perfiles | Grupos de servidores (solo Admin) |
| 📜 Historial | Despliegues ejecutados y su log (solo Admin) |
| ☁ Blob Storage | Explorador del contenedor de Azure (solo Admin) |
| 💾 Respaldo BD | Copias de la base de datos (solo Admin) |

El orden natural la primera vez es: **Sistemas → Versión → Servidores → Perfil → Desplegar**.

### Pestaña 🖥 Sistemas y Versiones

Panel izquierdo *Sistemas / Aplicativos*: **➕ Nuevo** · **✏ Editar** · **🗑 Eliminar**
Panel derecho *Versiones del sistema seleccionado*: **➕ Nueva versión** · **✏ Editar** · **📝 Changelog** · **🗑 Eliminar**

**Crear un sistema**
1. Pulsa **➕ Nuevo** en el panel izquierdo.
2. Captura **Nombre** (obligatorio), Descripción y, si quieres, la **Carpeta de origen por defecto** (el botón 📁 abre el explorador).
3. Deja marcado **Activo** y pulsa **Guardar**.

Solo los sistemas **Activos** aparecen luego en la pestaña Desplegar.

**Crear una versión eligiendo carpeta de destino**
1. Selecciona el sistema en el panel izquierdo.
2. Pulsa **➕ Nueva versión**. Si Azure Blob Storage está configurado, la aplicación lee del contenedor las subcarpetas que existen realmente y las ofrece.
3. Captura la **Versión / Etiqueta** (obligatoria; por ejemplo `v1.2.3` o `2024-06-01-hotfix`).
4. Elige la **Carpeta de origen (publish/build)** con el botón 📁. Debe existir.
5. En **Carpeta de destino en Blob Storage** elige la subcarpeta (por ejemplo *QA* o *Productivo*) o deja *(raíz de versiones)*. Las subcarpetas se crean en la pestaña **☁ Blob Storage → 📁 Nueva carpeta**.
6. Escribe el **Changelog / Notas de la versión**.
7. Pulsa **Crear versión**. Se abre una ventana de progreso que comprime la carpeta en un ZIP, calcula el checksum SHA-256 y lo sube a Azure con metadatos (sistema, versión, checksum, fecha y quién la creó).

Si Azure no está configurado verás *"ℹ Azure Blob no configurado — versión solo local."* y la versión se queda en tu equipo. **El ZIP local es lo que se usa al desplegar**, así que la versión sigue siendo utilizable.

La columna **Azure** de la lista de versiones muestra *☁ Sí* cuando el ZIP también está en la nube.

**Ver o editar el changelog:** selecciona la versión y pulsa **📝 Changelog** (o doble clic en la fila).

**Editar una versión ya creada**

Selecciona la versión en el panel derecho y pulsa **✏ Editar**. Se abre el diálogo *Editar versión* (encabezado *✏ Editar versión de X*), donde puedes cambiar:

- **Versión / Etiqueta** — el nombre con el que se identifica (por ejemplo `v1.2.3` o `2024-06-01-hotfix`).
- **Carpeta de destino (etiqueta del entorno)** — puedes teclearla o elegir una subcarpeta existente en el combo. **Es solo una etiqueta:** *no mueve ni re-sube el paquete ya guardado*, únicamente cambia la ficha de la versión.
- **Changelog / Notas de la versión**.

Pulsa **Guardar cambios** (la versión / etiqueta es obligatoria). Editar aquí **no vuelve a comprimir ni a subir nada**: para cambiar el contenido del paquete se crea una versión nueva.

> **La etiqueta se bloquea si la versión ya se usó.** Si la versión **ya se desplegó o está programada**, su etiqueta es su identificador en el historial y en las citas programadas, así que **queda de solo lectura** para no alterar esos registros. El diálogo lo indica con *🔒 Esta versión ya se desplegó o está programada: su etiqueta no se puede cambiar para no alterar el historial. Sí puedes editar la carpeta y el changelog.*. Si la etiqueta sí es editable, no puede repetir la de otra versión del mismo sistema: verás *"Ya existe otra versión «X» en este sistema. Usa una etiqueta distinta."*.

### Pestaña 🌐 Servidores

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Dar de baja · 📥 Importar JSON · 🌍 Abrir URL · 📊 Excel

**Dar de alta un servidor**
1. Pulsa **➕ Nuevo**.
2. Captura:
   - **Nombre** — obligatorio y único.
   - **Host** — obligatorio, **incluyendo el esquema**: `ftps://` o `ftp://`.
   - **Puerto** — 21 por omisión.
   - **Usuario** — obligatorio.
   - **Contraseña** — obligatoria al crear. Se guarda cifrada. Al editar, si la dejas en blanco no se cambia.
   - **Ruta remota** — obligatoria.
   - **URL** — opcional, para poder abrir el sitio desde el botón **🌍 Abrir URL**.
   - **Activo**.
3. Pulsa **Guardar**.

**🗑 Dar de baja** no borra: el servidor deja de recibir despliegues pero se conserva para que el historial siga siendo legible. Si seleccionas uno ya dado de baja, el mismo botón te ofrece **reactivarlo**.

**📥 Importar JSON** carga varios servidores de un archivo `.json` de una sola vez y te informa cuántos se crearon y cuántos se actualizaron. Las contraseñas se guardan cifradas.

### Pestaña 🔧 Perfiles

Un perfil es un grupo ordenado de servidores. **El rol Operaciones siempre despliega a un perfil**; como Administrador, además, puedes elegir los servidores directamente en la pestaña *Desplegar* sin pasar por un perfil (y un perfil te sirve para **precargar** esa selección de un tirón).

**Botones:** ➕ Nuevo perfil · ✏ Editar · 🗑 Eliminar

**Crear un perfil**
1. Pulsa **➕ Nuevo perfil**.
2. Captura **Nombre** (obligatorio) y Descripción.
3. Marca **Permitir ejecución al rol Operaciones** si quieres que el equipo de Operaciones pueda usar este perfil. Si no lo marcas, solo tú podrás desplegarlo. La columna **Operaciones** de la lista muestra *✓ Sí* en los perfiles habilitados.
4. En *Servidores incluidos en este perfil* marca los servidores. Solo se ofrecen los **activos**.
5. Pulsa **Guardar**.

Un perfil con despliegues en el historial **no se puede eliminar**: verás *"No se puede eliminar: el perfil tiene despliegues en el historial."*.

### Pestaña 🚀 Desplegar

1. Elige **Sistema / Aplicativo** (solo aparecen los activos).
2. Elige la **Versión** (la más reciente sale seleccionada).
3. Elige el **destino**. Como **Administrador** eliges los **servidores directamente**: pulsa **🖧 Elegir servidores…** y marca en la lista los servidores destino; **no necesitas crear un perfil**. (El rol Operaciones, en cambio, sigue eligiendo un **Perfil (servidores destino)** de entre los que tenga habilitados.)
4. Pulsa **🚀 Desplegar**.
5. Confirma. Como Administrador verás *"¿Desplegar la versión 'X' a N servidor(es) seleccionados?"* seguido del detalle del respaldo; como Operaciones, *"¿Desplegar la versión 'X' a perfil 'Y' (N servidor/es)?"*.
6. Sigue el avance en la **barra de progreso (0–100 %)** y en la consola negra, que va diciendo de qué servidor y qué archivo se está subiendo. Si necesitas detenerlo, pulsa **⏹ Cancelar**.

**Elegir servidores y respaldo (solo Administrador).** El botón **🖧 Elegir servidores…** abre el diálogo *Elegir servidores destino y respaldo*, con dos casillas por servidor:
- **Incluir** — si el servidor entra en este despliegue.
- **Respaldar** — si, **antes** de publicar en ESE servidor, se respalda su carpeta remota. Al incluir un servidor se marca su respaldo por omisión; puedes quitarlo servidor por servidor.

Arriba hay un buscador (*Buscar servidor por nombre o host…*), un combo **Precargar de un perfil:** para partir de un perfil ya armado, y botones para aplicar en bloque a lo filtrado: **✓ Incluir filtrados**, **▢ Excluir filtrados**, **💾 Respaldar filtrados** y **🚫 Sin respaldo filtrados**. Abajo se lee *"N servidor(es) · M con respaldo"*. Pulsa **Aceptar** y, junto al botón, verás el resumen *"N srv · M c/resp."*.

> **El respaldo previo ahora se decide por servidor.** Si dejas algún servidor sin respaldo, la confirmación te lo advierte con *"⚠ SIN respaldo previo: si algo sale mal no habrá copia para revertir."*. Con respaldo a todos dice *"Respaldo previo: todos."*, y parcial *"Respaldo previo: N de M."*.

Qué hace la aplicación por cada servidor, en orden:

1. Si ese servidor está marcado para respaldo, **respalda su carpeta remota completa** y sube el ZIP del respaldo a Azure Blob Storage antes de tocar nada. Si no lo marcaste, publica directo, sin copia previa.
2. Lee el ZIP de la versión **en streaming** —del disco si la versión se creó en este equipo, o **directamente desde Azure Blob Storage** si solo vive en la nube, sin descargarlo entero— y sube archivo por archivo por FTP/FTPS, con reintentos automáticos ante conexiones lentas.
3. Registra en el servidor la fecha y la versión desplegada.

Al terminar verás uno de estos mensajes en la barra de estado: *"✅ Despliegue completado — N servidor(es)."*, *"⚠ Completado con errores — OK: N, Fallidos: N."*, *"⏹ Despliegue cancelado."* o *"❌ Error: ..."*.

**🧹 Limpiar** vacía la consola y la barra de progreso para dejarla lista (queda deshabilitado mientras hay un despliegue corriendo, para no borrar a media corrida).

**El despliegue no se interrumpe si cambias de pantalla.** Puedes irte a otra sección mientras corre: al volver a la pestaña *Desplegar* encontrarás su consola, su barra y su estado intactos. Lo que sí lo cancela es **cerrar la aplicación** o **cerrar sesión**; en ambos casos, si hay un despliegue en curso, la aplicación avisa antes —*"Hay un DESPLIEGUE EN CURSO. Si cierras la aplicación se cancela. ¿Cerrar de todos modos?"* o *"...Si cierras sesión se cancela (lo ya subido se queda como esté). ¿Cerrar sesión de todos modos?"*— y solo continúa si aceptas.

### Pestaña 📊 Estado

Contesta la pregunta que se hace en caliente: **qué versión tiene cada servidor ahora mismo, quién se la puso y cuándo**. El Historial cuenta lo que pasó ordenado por despliegue; ésta cuenta cómo quedaron las cosas, servidor por servidor.

| Columna | Qué significa |
|---|---|
| Servidor | Nombre del destino. |
| Sistema / Versión desplegada | Lo que tiene publicado hoy. |
| Última publicada | La versión más reciente que existe de ese sistema. |
| Estado | ✅ Al día · ⚠ Atrasado · ○ Sin desplegar. |
| Última actualización / Hace | Cuándo se desplegó, en fecha exacta y en lenguaje llano. |
| Quién lo desplegó | Quien lanzó ese despliegue. |
| Despliegue # | Número del trabajo en el Historial. |

Arriba hay un resumen de una línea: *"22 servidor(es) · ✅ 18 al día · ⚠ 3 atrasado(s) · ○ 1 sin desplegar nunca"*.

**Botones:** 🔄 Actualizar · **📜 Ver ese despliegue** (salta al Historial con ese despliegue ya seleccionado, para leer su log completo; también con doble clic en la fila) · 🌍 Abrir URL · 📊 Excel.

**Casillas:** *Solo atrasados* deja a la vista únicamente los que no tienen la última versión — es la lista de pendientes. *Incluir dados de baja* agrega los servidores desactivados, en gris.

> «Atrasado» se calcula contra la versión **más reciente registrada** del mismo sistema, tomando la fecha de alta y no el texto: «1.10» es posterior a «1.9» aunque alfabéticamente sea menor.
>
> En **Quién lo desplegó** puede salir «—» en despliegues antiguos: ese dato se empezó a guardar a partir de esta versión, y no se inventa hacia atrás.

### Pestaña 📜 Historial

Muestra los últimos 200 despliegues. Selecciona uno y abajo aparece su **log completo**, con hora, servidor y mensaje, coloreado por nivel. **🔄 Actualizar** recarga la lista.

| Estado del despliegue | Significado |
|---|---|
| ✅ Completado | Todos los servidores terminaron bien |
| Parcial | Unos sí y otros no |
| ❌ Fallido | Ninguno terminó bien |
| ⏹ Cancelado | Se detuvo a media ejecución |
| En curso / Pendiente | Todavía corriendo |

### Pestaña ☁ Blob Storage

Explorador del contenedor de Azure. Solo aparece si eres Administrador.

**Barra superior:** combo **Carpeta:** · 🔄 Listar · 📁 Nueva carpeta · 🏷 Metadatos · 🔗 Enlace de descarga · 📋 Copiar nombre · 🗑 Eliminar archivo · 🗑 Eliminar carpeta
**Segunda barra:** cuadro **Filtrar:** y combo **Ordenar por:**

**Listar el contenido de una carpeta**
1. Elige la carpeta en el combo (siempre aparecen las tres carpetas base aunque todavía no existan, más las que haya en el contenedor).
2. Pulsa **🔄 Listar**. Abajo verás *"N archivo(s) — X MB."*.

**Filtrar y ordenar.** El cuadro **Filtrar:** busca en el nombre **y también en los metadatos**, así que puedes localizar un archivo escribiendo el nombre de un cliente o un checksum. El combo **Ordenar por:** ofrece:

| Opción | Orden |
|---|---|
| Fecha (más reciente primero) | Por fecha de modificación, descendente (por omisión) |
| Fecha (más antigua primero) | Por fecha de modificación, ascendente |
| Versión (descendente) | Por número de versión, de mayor a menor |
| Versión (ascendente) | Por número de versión, de menor a mayor |
| Tamaño (mayor primero) | Por tamaño |

Filtrar y reordenar **no vuelve a consultar Azure**: trabaja sobre lo ya descargado. Para traer cambios recientes pulsa **🔄 Listar** de nuevo.

**Crear una carpeta**
1. Pulsa **📁 Nueva carpeta**.
2. Escribe el nombre (por ejemplo *QA*, *Productivo*, *Infrasur*). Puedes anidar con `/`.
3. Se propone crearla **dentro de la carpeta seleccionada**. Si la quieres colgada de la raíz del contenedor, marca la casilla **Crear en la raíz del contenedor**.
4. El renglón *Quedará como:* te muestra la ruta exacta antes de crearla.
5. Pulsa **Crear carpeta**.

Si la carpeta ya existía no pasa nada: no se toca su contenido y verás *"La carpeta «X» ya existía; no se modificó su contenido."*.

**Editar metadatos**
1. Selecciona el archivo y pulsa **🏷 Metadatos** (o doble clic).
2. Edita la rejilla Nombre / Valor. Usa la última fila para agregar y selecciona una fila completa para borrarla.
3. Pulsa **Guardar metadatos**.

Reglas de los nombres de metadato: solo letras **sin acento**, números y guion bajo, y no pueden empezar con número. Azure **no fusiona**: lo que dejes en la rejilla es lo que queda, así que no borres lo que quieras conservar.

**Generar un enlace de descarga (SAS)**
1. Selecciona el archivo y pulsa **🔗 Enlace de descarga**.
2. Elige la **Vigencia (horas)**: escríbela o usa los botones rápidos *1 h*, *24 h*, *7 días*, *30 días*. Por omisión son 24 horas; el mínimo es 1 hora y el máximo un año.
3. Pulsa **Generar enlace**.
4. El enlace **se copia solo al portapapeles** y se te muestra en una ventana. Abajo verás hasta cuándo caduca.

Cualquiera con ese enlace puede descargar el archivo completo sin credenciales hasta que caduque. Usa la vigencia más corta que te sirva. **Cada enlace generado queda en la bitácora** con quién lo hizo, para qué archivo y hasta cuándo sirve.

**📋 Copiar nombre** copia al portapapeles la ruta completa del archivo dentro del contenedor.

**Eliminar un archivo**

Selecciona el archivo y pulsa **🗑 Eliminar archivo**. Antes de borrar te lo confirma mostrando su **nombre y su tamaño** (*"¿Eliminar definitivamente este archivo?"*), con *No* por omisión. La acción es **irreversible** y queda en la bitácora. Al terminar verás *"✓ Archivo «X» eliminado."*.

**Eliminar una carpeta**

**🗑 Eliminar carpeta** borra la carpeta elegida en el combo **Carpeta:** y **TODO su contenido**: archivos, subcarpetas y el marcador de la propia carpeta. Antes de borrar nada cuenta los elementos y te avisa del alcance: *"¿Eliminar la carpeta «X» y TODO su contenido? Se borrarán N elemento(s), incluidas sus subcarpetas."*. La opción por omisión es *No* y la acción es **irreversible**; queda en la bitácora. Al terminar verás *"✓ Carpeta «X» eliminada (N elemento(s))."*, y si la carpeta ya estaba vacía te lo dice sin borrar nada.

> **Cuidado con las carpetas base.** Si la carpeta que vas a eliminar contiene una **carpeta base del sistema** (la de versiones o la de respaldos), aparece una advertencia extra: *"⚠ Contiene una CARPETA BASE del sistema (versiones o respaldos): al borrarla se elimina TODO ese histórico."*. Borrarla se lleva todo ese historial.

### Pestaña 💾 Respaldo BD

**☁ Respaldar en Azure** sube una copia de la base de datos SQLite a la carpeta de respaldos del contenedor, con el nombre `app_AAAAMMDD_HHMMSS.db`. Requiere Azure configurado; si no lo está verás *"Azure Blob Storage no está configurado. Ve a Configuración y agrega la connection string."*.

**📁 Copia local** te pide una carpeta y deja ahí la copia. No sobrescribe archivos existentes.

Ambas operaciones quedan en la bitácora. El recuadro negro de abajo muestra el detalle de lo que ocurrió.

---

## Despliegue e Infraestructura → Programados

Título en pantalla: *🗓 Programados*. Agenda de despliegues para ejecutarse solos a una hora concreta.

> **Léelo antes de programar nada:** los despliegues programados los ejecuta **una aplicación abierta** (basta con dejarla en la bandeja del sistema). Si a la hora programada no hay ninguna corriendo, el despliegue se marca como **Perdido** y **no se ejecuta después**.

**Filtro:** casilla **Solo pendientes** (marcada por omisión).
**Botones:** 🗓 Programar despliegue · ✖ Cancelar · 🔄 Recargar

### Programar un despliegue
1. Pulsa **🗓 Programar despliegue**.
2. Elige **Sistema**, **Versión** y **Perfil de despliegue**.
3. Fija **Fecha y hora** (debe ser futura).
4. Elige la **Tolerancia para arrancar tarde**: 15 minutos, 30 minutos, 1 hora, 2 horas o 4 horas. Pasado ese margen la cita se marca como *Perdido*.
5. Escribe **Notas** si quieren dejar contexto.
6. Pulsa **Programar**.

### Estados de una programación

| Estado | Significado |
|---|---|
| 🕓 Programado | Esperando su hora |
| ▶ En ejecución | Ya la tomó una aplicación y está corriendo |
| ✅ Completado | Terminó bien |
| ❌ Fallido | Terminó con errores |
| ⚪ Cancelado | Se canceló antes de su hora |
| ⚠ Perdido | Pasó su hora y su tolerancia sin que nadie la ejecutara |

La columna **Ejecutó** muestra el equipo y usuario de Windows que la tomó, y **Resultado** el resumen del despliegue.

**Cancelar** solo funciona sobre citas en estado *Programado*; si no, verás *"No se puede cancelar: está en estado X."*.

Cuando una cita arranca, se completa o se pierde, aparece un globo de aviso desde el icono de la bandeja.

---

## Despliegue e Infraestructura → Recursos Azure

Título en pantalla: *☁ Recursos Azure*. Inventario manual de los recursos de Azure del área (no se sincroniza con Azure: lo capturas tú).

**Filtros:** *Buscar...* (nombre, resource group, notas y URL), **Tipo**, **Estado** y **Ambiente**.

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Eliminar

Columnas: Nombre, Tipo, Estado, Ambiente, Resource Group, Región, Costo/mes y Notas. Los estados se colorean: *En uso* en verde, *En prueba* en amarillo y *Archivado* en rojo.

---

## Despliegue e Infraestructura → Programas

Título en pantalla: *🛠 Programas y Utilerías*. Inventario de software y licencias.

**Filtros:** *Buscar...* (nombre, fabricante, notas y dónde está instalado), **Categoría** y **Estado**.

**Botones:** ➕ Nuevo · ✏ Editar · 🗑 Eliminar

Columnas: Nombre, Categoría, Estado, Licencia, Versión, Fabricante, Vence e Instalado en.

Las licencias **ya vencidas** se pintan en rojo; las que vencen **en menos de 30 días**, en amarillo. Es la forma rápida de revisar renovaciones.

---

## Integraciones y Correo → Correo

Título en pantalla: *✉ Correo*. Bandeja del buzón configurado en *Configuración → Correo*.

**Barra:** combo **Carpeta:** · 🔄 Cargar · ➡ Convertir en requerimiento · 📥 Importar no leídos · ✉ Redactar

Al entrar se cargan las carpetas del buzón por IMAP. Si el correo no está habilitado o le faltan datos, verás el aviso en la barra y no se cargará nada.

- **🔄 Cargar** trae los últimos 50 mensajes de la carpeta seleccionada. Selecciona uno para ver su contenido en el panel inferior.
- **➡ Convertir en requerimiento** crea un requerimiento con el asunto como título y el cuerpo del correo como descripción, en estado *Por estimar* y con origen *Correo*. Te confirma con el número: *"Requerimiento #N creado."*.
- **📥 Importar no leídos** crea un requerimiento por **cada correo no leído** de la carpeta y los marca como leídos. Úsalo con cuidado: procesa todos de golpe.
- **✉ Redactar** abre la ventana de redacción.

---

## Integraciones y Correo → Azure DevOps

Título en pantalla: *🔷 Azure DevOps — Tickets*. Copia local de los work items, para consultarlos y filtrarlos sin salir de la aplicación.

**Barra:** ⟳ Sincronizar · combo de auto-sincronización · *Título contiene...* · *Buscar en todo...* · 🧹 Limpiar · combo de filtros guardados · 💾 Guardar · 🗑 · ⬇ Exportar

### Traer los tickets
Pulsa **⟳ Sincronizar**. Al terminar verás *"Sincronización completada. N nuevos, N actualizados."*. Si la integración no está habilitada verás *"Azure DevOps no está habilitado. Configure la integración en Configuración."*.

Para que se actualice solo, elige en el combo: *Sin auto-sync*, *Cada 5 min*, *Cada 15 min*, *Cada 30 min* o *Cada hora*. La elección se guarda.

### Filtrar
- **Título contiene...** y **Buscar en todo...** filtran mientras escribes.
- Los encabezados de columna filtrables abren un panel con los valores existentes; pulsa **Aplicar** para filtrarlos o **Quitar filtro** para soltarlos.
- **🧹 Limpiar** quita todos los filtros de golpe.
- **💾 Guardar** guarda la combinación de filtros con un nombre para reutilizarla desde el combo. **🗑** borra el filtro guardado seleccionado.

Los filtros **sobreviven a la sincronización**; solo se descartan los valores que ya no existen.

### Trabajar con un ticket
Clic derecho sobre la fila:
- **🔗 Abrir en Azure DevOps** — abre el work item en el navegador.
- **💬 Ver / agregar comentarios** — lee y publica comentarios sin salir de la aplicación.
- **👤 Reasignar…** — cambia el responsable (*System.AssignedTo*) directamente en Azure DevOps.
- **↔ Mover a estado** — abre un submenú con los estados vistos en la organización (menos el actual) y cambia la columna del tablero (*System.State*) en DevOps; DevOps valida si la transición es posible.
- **🔧 Cambiar prioridad…** — abre el diálogo *Cambiar prioridad — #N*. Elige la **Nueva prioridad (se actualiza en Azure DevOps)** en el combo: *1 — Muy alta*, *2 — Alta*, *3 — Media* o *4 — Baja* (viene preseleccionada la actual). Pulsa **Aplicar** y la aplicación actualiza *Microsoft.VSTS.Common.Priority* en el work item. Si esa prioridad tiene una política de SLA automático activa (ver *Configuración*), el compromiso se **reajusta solo** al nuevo plazo; si no había SLA y hay a quién asignárselo, se crea. Si falla verás *"No se pudo cambiar la prioridad: ..."*.
- **🔔 Vigilar este ticket (notificaciones)** / **🔕 Dejar de vigilar este ticket**.
- **📋 Ver detalle** — resumen completo, incluidos los tickets de Freshdesk vinculados.

**⬇ Exportar** guarda a CSV lo que esté visible con los filtros aplicados.

---

## Integraciones y Correo → 📊 Dashboard por tag

Agrupa los tickets de Azure DevOps **ya sincronizados** por sus *tags* (las etiquetas del work item: el cliente, por ejemplo *Bepensa*, o el tipo, por ejemplo *Bug* o *Tarea*). Sirve para responder de un vistazo "¿qué cliente o categoría acumula más bugs, tareas o solicitudes?". Trabaja sobre la copia local: si no ves algo, sincroniza primero en la pantalla *Azure DevOps*.

**Barra superior:** combo **Entrar al tag:** · casilla **Solo abiertos** · combo **Ordenar por:** · cuadro *Buscar tag…* · **⬇ CSV** · **🔄 Recargar**

**Tarjetas (KPIs):** Tickets · Bugs · Tareas · User Stories · Tags distintos. Los conteos respetan el tag en el que hayas entrado y la casilla *Solo abiertos*.

**Columnas de la tabla:** Tag · Tickets (total) · Abiertos · Bugs · Tareas · User Stories · Otros. La columna **Bugs** se pinta en rojo cuando hay alguno. Un mismo ticket puede sumar en varios tags a la vez (los que tenga).

**Filtrar y ordenar:**
- **Entrar al tag:** empieza en *(todos los tags)*. Al elegir un tag (por ejemplo *Bepensa*) la tabla pasa a mostrar **con qué otros tags coexisten** esos tickets (por ejemplo, dentro de *Bepensa*, cuántos son *Bug* y cuántos *Tarea*). El propio tag no se lista a sí mismo. Abajo verás *"Dentro de «X»: N tag(s) que coexisten."*; sin entrar a ninguno, *"N tag(s) sobre M tickets sincronizados."*.
- **Solo abiertos** excluye los tickets cerrados (*Done*, *Closed*, *Resolved*, *Removed*, etc.).
- **Ordenar por:** *Total*, *Bugs*, *Tareas*, *User Stories* o *Abiertos*.
- *Buscar tag…* filtra la lista por el nombre del tag.

**⬇ CSV** guarda a un archivo `devops_tags_AAAAMMDD.csv` lo que esté en la tabla. Si no hay datos verás *"No hay datos para exportar."*.

Esta pantalla es de solo consulta: no cambia nada en DevOps ni en los requerimientos.

---

## Integraciones y Correo → Freshdesk

Título en pantalla: *🎫 Freshdesk — Tickets*.

**Barra:** ⟳ Sincronizar · combo de campo de búsqueda · *Buscar...* · combo de estado · combo de prioridad · ⬇ Exportar

**Tarjetas de resumen:** Total · Abiertos · Pendientes · Resueltos · Cerrados · Urgentes.

Pulsa **⟳ Sincronizar** para traer los tickets; verás *"Sincronización completada. N nuevos, N actualizados."*. Si la integración no está habilitada: *"Freshdesk no está habilitado. Configure la integración en Configuración."*.

El combo de campo permite acotar la búsqueda a *Todos los campos*, ID, Asunto, Estado, Prioridad, Tipo, Agente, Solicitante, Email, Fuente o Tags. Los filtros de estado (Abierto / Pendiente / Resuelto / Cerrado) y prioridad (Baja / Media / Alta / Urgente) se combinan con la búsqueda.

Doble clic o clic derecho → **📋 Ver detalle** muestra la ficha completa y los work items de DevOps vinculados. Clic derecho → **🔗 Abrir en Freshdesk** abre el ticket en el navegador.

**⬇ Exportar** genera un CSV con todos los tickets.

---

## Integraciones y Correo → Vínculos tickets

Título en pantalla: *🔗 Vínculos y Estadísticas*. Sirve para relacionar un work item de Azure DevOps con el ticket de Freshdesk que lo originó. Tres pestañas.

### 🔗 Vínculos
Lista de los vínculos existentes, con cuadro de búsqueda, **🧱 Columnas** y el botón **🗑 Desvincular** (pide confirmación con *"¿Eliminar este vínculo?"*).

**Columnas:** ID DevOps · Tipo · Título DevOps · Estado DO · ID FD · Asunto Freshdesk · Estado FD · Agente · Vinculado · Por · Notas.

### ➕ Vincular tickets
1. Busca y selecciona el **Work Item Azure DevOps** en la lista izquierda.
2. Busca y selecciona el **Ticket Freshdesk** en la lista derecha.
3. Pulsa **🔗 Vincular selección**.
4. Escribe las notas del vínculo (opcional) y pulsa **Vincular**.

Si ya existía ese par verás *"Este vínculo ya existe."*.

**Filtros.** Cada lado tiene su caja de búsqueda, sus filtros y su botón **Limpiar** (que los deja todos en blanco de una vez). Los filtros se **acumulan**: buscar *"error"* con tipo *Bug* deja solo los bugs cuyo título diga error.

| Lado | Busca por texto en | Filtros |
|---|---|---|
| Azure DevOps | Título · número · asignado | Estado · Tipo · Asignado · **Solo sin vincular** |
| Freshdesk | Asunto · número · solicitante | Estado · Prioridad · Agente · **Solo sin vincular** |

- **Solo sin vincular** es el que suele hacer falta: esconde lo que ya tiene al menos un vínculo y te deja justo lo que falta por atar. Viene apagado a propósito, porque un work item puede tener varios tickets de Freshdesk detrás y a veces quieres agregarle otro.
- Los combos de **Estado**, **Tipo**, **Asignado** y **Agente** se arman con lo que **de verdad hay** en los tickets sincronizados, no con una lista fija: no te ofrecen estados que nadie usa. Si vuelves a sincronizar, tu filtro se conserva mientras ese valor siga existiendo.
- Debajo de cada lista se lee *"N de M work item(s) · K sin vincular"*. La cuenta de **sin vincular** es el pendiente real y **no cambia con el filtro**: aunque estés viendo tres, te sigue diciendo cuántos faltan en total.

### 🧱 Columnas: quitar y poner

Las tres listas de esta pantalla (la de vínculos y las dos del vinculador) tienen su propio botón **🧱 Columnas**. También se llega con **clic derecho sobre el encabezado** de cualquier columna.

Se abre *🧱 Qué columnas quieres ver*: marca las que quieras, desmarca las que no, y **Aceptar**. Abajo se lee *"N de M columnas visibles."*. **Mostrar todas** las devuelve todas de golpe. **Tiene que quedar al menos una**: si las desmarcas todas, avisa *"Deja al menos una columna visible."* y no deja aceptar.

Lo que escondas **se recuerda para la próxima vez**, y es tuyo: se guarda en tu equipo (`%AppData%\AdministradorDesarrolloWeb\columnas.json`), no en la base del equipo, así que no le cambia la vista a nadie más. Cada lista se acuerda de lo suyo por separado. Si una versión nueva agrega una columna, esa aparece: se guarda lo que escondiste, no lo que dejaste ver.

### 📊 Estadísticas
Conteos globales: work items totales, tickets de Freshdesk totales, vínculos totales, cuántos de cada lado están vinculados, y desgloses de DevOps por estado y por tipo, y de Freshdesk por estado, prioridad y agente.

---

## Administración → Plantillas

Título en pantalla: *📚 Plantillas y scripts*. En el menú aparece como **📚 Plantillas**. Es la biblioteca de lo que se repite todos los días y que hasta ahora vivía en un bloc de notas de cada quien: el cuerpo de un ticket de Freshdesk, la respuesta con la que se acusa recibo, la observación que se pone en cada requerimiento, el comentario de avance de un work item de DevOps, el esqueleto del documento con el que se entrega una estimación y los scripts de utilería (SQL, PowerShell, Bash).

Vive en la base de datos del equipo, así que se captura una vez y está en cualquier equipo donde entres. **Es solo para el rol Administrador**: los scripts suelen traer nombres de servidores, bases y rutas internas.

> **La aplicación no ejecuta nada de lo que hay aquí.** Un script se copia o se guarda a un archivo; correrlo, contra qué servidor y con qué credenciales, es una decisión que se toma fuera, viendo lo que se va a ejecutar.

**Tipos de plantilla:** 🎫 Ticket Freshdesk · 💬 Respuesta Freshdesk · 📋 Comentario de requerimiento · 🔷 Comentario Azure DevOps · 📄 Documento de estimación · 🗄 Script SQL · 🟦 Script PowerShell · 🐧 Script Bash · 📌 Otro.

**Botones:** 📋 Copiar · 💾 Guardar como… · ➕ Nueva · ✏ Editar · ⧉ Duplicar · 🗄 Archivar (o ♻ Restaurar) · 🗑 Eliminar

**Filtros:** una caja de búsqueda (busca en título, etiquetas **y contenido**), un combo de tipo y una casilla **Ver archivadas**.

**Columnas de la lista:** Tipo · Título · Etiquetas · Usos · Actualizada.

- La lista sale ordenada por **Usos**, de mayor a menor: lo que de verdad usas queda arriba.
- Un **📎** junto al título significa que la plantilla trae un archivo adjunto.
- Las archivadas salen en gris y con **🗄** delante (solo si marcaste *Ver archivadas*).
- A la derecha hay una **vista previa** con el título, el tipo, las etiquetas, los usos, el archivo adjunto si lo hay, cuántos marcadores tiene, la nota de *cuándo usarla* y el contenido completo.

### Marcadores `{{así}}`

Dentro del contenido puedes escribir **`{{cliente}}`, `{{folio}}`, `{{sistema}}`**… lo que necesites. Al copiar o guardar, la aplicación te pide esos datos en un diálogo con **vista previa en vivo** y los sustituye.

Hay cinco que **se rellenan solos** y nunca se preguntan:

| Marcador | Se convierte en |
|---|---|
| `{{fecha}}` | La fecha de hoy (dd/mm/aaaa) |
| `{{hora}}` | La hora actual (hh:mm) |
| `{{fechahora}}` | Fecha y hora |
| `{{anio}}` (o `{{año}}`) | El año en curso |
| `{{usuario}}` (o `{{yo}}`) | Tu nombre de usuario |

Un marcador que dejes **vacío se queda escrito tal cual** en el resultado. Es a propósito: es preferible que se vea un `{{cliente}}` sin llenar a mandarle al cliente un hueco en blanco.

Al editar una plantilla, debajo del contenido se lee cuántos marcadores tiene y cuáles se van a preguntar.

### Copiar

**📋 Copiar** (o **doble clic** en la fila) deja el texto listo en el portapapeles. Si tiene marcadores, primero abre el diálogo *Rellenar*: llena lo que quieras, mira cómo va quedando a la derecha y pulsa **Usar este texto**. Abajo se confirma *"Copiado al portapapeles: «X» (N caracteres)."* y el contador de **Usos** sube.

### Guardar como…

**💾 Guardar como…** baja la plantilla a un archivo, con la extensión que le toca a su tipo: `.sql`, `.ps1`, `.sh`, `.md` o `.txt`. Un `.ps1` se escribe con **BOM** a propósito: sin él, Windows PowerShell 5.1 lee el archivo como ANSI y cualquier acento del script sale corrupto al ejecutarlo.

Si la plantilla trae **archivo adjunto** y además contenido de texto, pregunta cuál de los dos quieres guardar.

### Nueva / Editar

El diálogo pide **Tipo**, **Título**, **Etiquetas** (separadas por coma, para buscar), **Cuándo usarla** (la nota que sale en la vista previa) y el **Contenido**. Opcionalmente puedes **📎 Adjuntar archivo…** — es lo que se usa para el `.docx` o `.xlsx` de una estimación que ya viene formateado; el máximo son **10 MB**, porque esto vive en la base del equipo (para algo más grande, súbelo a Blob Storage y deja aquí el enlace).

Con un archivo adjunto, el contenido de texto pasa a ser **opcional**: sirve para explicar cómo se llena el documento.

Si abres **➕ Nueva** con un tipo filtrado, la plantilla nace ya de ese tipo.

### Archivar frente a eliminar

- **🗄 Archivar** la saca de la lista del día a día y del buscador global, pero no la pierde: para verla, marca *Ver archivadas*; para devolverla, **♻ Restaurar**.
- **🗑 Eliminar** sí borra. Pregunta *"¿Eliminar la plantilla «X»?"*, avisa que no se puede deshacer y sugiere archivar en su lugar; la opción por omisión es *No*.

### Catálogo inicial

La primera vez que arranca, la aplicación siembra un juego de plantillas de ejemplo de las ocho familias (alta y respuestas de Freshdesk, análisis y devolución de requerimientos, avance y cierre de DevOps, documento y correo de estimación, y scripts de bloqueos, tamaño de tablas, búsqueda de texto, respaldo previo, espacio en disco, respaldo de carpeta, estado de IIS y respaldo con rotación). Están para editarse, no para usarse tal cual: **ninguna trae datos reales de clientes ni de servidores**.

Se siembra **una sola vez**. Si borras las de ejemplo, no vuelven a aparecer en el siguiente arranque. Y si la base ya tenía plantillas cuando se actualizó la aplicación, no se siembra nada.

Todo lo que hagas aquí (crear, editar, duplicar, archivar, eliminar) queda en la **Bitácora** con el tipo de entidad `Template`.

---

## Administración → Usuarios

Título en pantalla: *🔐 Gestión de Usuarios*.

**Botones:** ➕ Nuevo usuario · ✏ Editar · 🔑 Reset pwd · ⚡ Activar/Desact. · 🗑 Eliminar

### Roles

| Rol | Qué ve |
|---|---|
| Admin | El menú completo (todo este manual) |
| Operaciones | Solo *Despliegues* y *Programados* |
| Desarrollador | Mi Panel, Mis Asignaciones, Mis Actividades, Mis SLA y Mis Vacaciones |

Cualquier cuenta con un rol distinto se queda **sin menú** a propósito.

### Crear un usuario
1. Pulsa **➕ Nuevo usuario**.
2. Captura **Nombre de usuario** y **Nombre completo** (ambos obligatorios).
3. Elige el **Rol**.
4. Si el rol es *Desarrollador*, se muestra **Desarrollador vinculado**: es obligatorio elegir uno.
5. Deja marcado **Activo**.
6. Escribe la **Contraseña** y **Confirmar contraseña**. Mínimo 8 caracteres y deben coincidir.
7. Pulsa **Guardar**.

Si el nombre de usuario ya existe verás *"El usuario 'X' ya existe."*.

### Editar un usuario
Selecciónalo y pulsa **✏ Editar** (o doble clic). El campo de contraseña dice *"Contraseña (dejar en blanco para no cambiar)"*: si lo dejas vacío, la contraseña no se toca.

### Restablecer una contraseña
1. Selecciona el usuario y pulsa **🔑 Reset pwd**.
2. Escribe la nueva contraseña dos veces (mínimo 8 caracteres).
3. Pulsa **Guardar**. Verás *"Contraseña restablecida. El usuario deberá cambiarla al iniciar sesión."*.

La próxima vez que esa persona entre, la aplicación le obligará a cambiarla antes de dejarla usar nada.

### Activar o desactivar
Selecciona el usuario y pulsa **⚡ Activar/Desact.**. Es inmediato, sin confirmación. Un usuario inactivo no puede iniciar sesión y aparece en gris en la lista. **Esta es la forma correcta de quitar el acceso a alguien.**

### Eliminar un usuario
1. Selecciona el usuario y pulsa **🗑 Eliminar**.
2. Si tiene registros históricos a su nombre, el aviso te dice cuántos quedarán **sin atribución** (puntos asignados, revisiones, despliegues iniciados…). La bitácora sí conserva su nombre.
3. Confirma. La opción por omisión del diálogo es *No*.

Límites que no puedes saltar:
- **No puedes eliminar a un usuario con rol Admin.** Verás *"«X» es administrador y no se puede eliminar. Si de verdad quieres darlo de baja, cámbiale antes el rol o desactívalo."*.
- **No puedes eliminar tu propia cuenta**: *"No puedes eliminar tu propia cuenta."*.

---

## Administración → Bitácora

Título en pantalla: *📋 Bitácora de Auditoría*. Registro de lo que se hace en la aplicación. Es **solo de consulta**: no se edita ni se borra desde aquí.

### Qué se registra

| Acción | Cuándo aparece |
|---|---|
| Login | Cada inicio de sesión exitoso, y el bloqueo de una cuenta por intentos fallidos |
| Logout | Al cerrar sesión |
| Create | Altas: desarrolladores, equipos, contactos, requerimientos, minutas, notas, usuarios, versiones, servidores, perfiles, SLA, importaciones desde correo y DevOps |
| Update | Modificaciones, aprobaciones y cambios de estado (vacaciones, puntos, SLA, servidores, requerimientos, roles de equipo, correos enviados) |
| Delete | Bajas y desactivaciones |
| Deploy | Inicio y fin de cada despliegue, y todo el ciclo de los programados |
| Backup | Respaldos de la base de datos (Azure y copia local) |
| PasswordChange | Cambios y restablecimientos de contraseña |
| ConfigChange | Cambios en Configuración, creación de carpetas en Blob Storage, edición de metadatos y generación de enlaces SAS |

Cada entrada guarda además el equipo y usuario de Windows desde donde se hizo, el resultado (éxito, fallo o **denegado**) y, en las operaciones importantes, el estado anterior y posterior. **Los intentos rechazados también quedan registrados**: si alguien trata de hacer algo que no le toca, se ve.

Los despliegues se agrupan con un identificador de correlación, de modo que respaldo, subida a cada servidor y cierre quedan enlazados como una sola operación.

### Consultar
1. Ajusta **Desde:** y **Hasta:** (por omisión, los últimos 30 días).
2. Acota por **Usuario:** y por **Acción:** si hace falta.
3. En **Texto:** escribe algo que aparezca en los detalles o en el tipo de entidad.
4. Pulsa **🔍 Buscar**.
5. **📊 Excel** exporta exactamente lo que está en pantalla.

Columnas visibles: Fecha/Hora, Usuario, Acción, Entidad, ID y Detalles.

---

## Administración → Configuración

Título en pantalla: *⚙ Configuración*. Es una sola página larga con desplazamiento. Tiene **dos botones de guardado distintos** y conviene no confundirlos:

- **💾 Guardar BD** (en la sección *Base de datos*) — guarda **únicamente** el proveedor y su connection string.
- **💾 Guardar configuración general** (hasta abajo) — guarda **todo lo demás**.

Los datos sensibles (connection string de Azure, PAT de DevOps, API Key de Freshdesk, contraseña de correo) se guardan **cifrados en la base compartida**. Los captura un administrador una sola vez y **los usan todos los equipos**: no hay que repetirlos PC por PC.

> **Si vienes de una versión anterior**, esos valores estaban cifrados con la cuenta de Windows que los capturó y solo servían en esa PC. La aplicación los convierte sola al abrirla, pero solo puede convertir los que ese equipo alcance a descifrar: abre la aplicación **en la PC donde se capturaron**. Lo que ninguna pueda leer, captúralo una vez más aquí y listo.

La contraseña del **PAT personal de DevOps** es la excepción y sigue siendo de cada quien: se guarda en su propia computadora, no en la base (ver *Mis SLA → 🔑 Mi PAT*).

### ☁ Azure Blob Storage

| Campo | Qué poner |
|---|---|
| Connection string (se guarda cifrada) | La cadena completa de la cuenta de almacenamiento, tal cual la copias del portal (*Cuenta de almacenamiento → Claves de acceso → Cadena de conexión*). Capturarla aquí basta para todo el equipo |
| Nombre del contenedor | Por ejemplo `despliegues` |
| Carpeta de versiones | Dónde van los ZIP de las versiones. Vacío = `releases` |
| Carpeta de respaldos de la base de datos | Vacío = `backups` |
| Carpeta de respaldos previos al despliegue | Vacío = `respaldos-despliegue` |

Las tres carpetas son opcionales: **déjalas vacías para volver al valor de por omisión**. Cambiarlas **no mueve lo ya guardado**: lo anterior se queda donde está y lo nuevo va a la carpeta nueva.

**Probar la conexión**
1. Escribe la connection string y el contenedor.
2. Pulsa **🔍 Probar conexión con Azure**. La prueba usa lo que está **escrito en pantalla**, no lo guardado, así que puedes detectar una cadena equivocada antes de guardarla.

Respuestas posibles:
- *"Conexión correcta con el contenedor «X» (N archivo(s) visibles). Se pueden generar enlaces SAS de descarga."* — todo bien.
- *"...ATENCIÓN: esta cadena no incluye la clave de la cuenta, así que NO se podrán generar enlaces SAS."* — conecta, pero los enlaces de descarga **no funcionarán**. Usa la cadena completa de la cuenta.
- *"Conexión correcta con la cuenta, pero el contenedor «X» todavía no existe. Se creará solo la primera vez que se suba algo."* — normal en una instalación nueva.

Cuando termines, no olvides **💾 Guardar configuración general**.

### 📁 Rutas por defecto
**Carpeta de despliegue** — la ruta que se propone al crear versiones (por ejemplo `C:\builds\publish`).

### 🗄 Base de datos

**Proveedor:** *SQLite local (predeterminado)* o *Azure SQL Server*. Al elegir SQL Server aparece el cuadro de la connection string y el botón 🙈 / 👁 para ocultarla o mostrarla.

Se aceptan formatos abreviados con o sin espacios (`server = ...; uid = ...; pwd = ...; database = ...`); al probar o guardar se normalizan solos y verás la versión normalizada en el cuadro.

**Probar la conexión**
1. Escribe la connection string.
2. Pulsa **🔍 Probar conexión**. Corre en segundo plano, así que la ventana no se congela.
3. Si funciona verás la base de datos, el usuario y la versión del servidor.

**Cambiar de proveedor**
1. Elige el proveedor en el combo.
2. Pulsa **💾 Guardar BD**.
3. Verás *"✓ Proveedor guardado. Reinicia la aplicación para aplicar el cambio."*. **El cambio no surte efecto hasta reiniciar.**

**Migrar de SQLite a SQL Server**

Este es el orden correcto y completo:

1. Elige *Azure SQL Server* y escribe la connection string.
2. Pulsa **🔍 Probar conexión** y confirma que funciona. No sigas si falla.
3. Pulsa **📤 Migrar SQLite → Azure SQL**. Se abre una ventana de progreso con el detalle tabla por tabla.
4. Si la base destino **ya tiene datos**, la aplicación se detiene y te pregunta: *"La base destino ya tiene datos en N tabla(s)... ¿Borrar TODO el contenido del destino y reemplazarlo con la base local? Esta acción no se puede deshacer."*. La opción por omisión es *No*. Si aceptas, se borra el destino y se copia todo desde cero.
5. Al terminar, revisa el resumen:
   - *"✓ MIGRACIÓN COMPLETADA — N fila(s) en N tabla(s)."* — bien.
   - *"⚠ No se copió ninguna fila: la base local está vacía."* — **no es un éxito**; revisa qué base local estás migrando.
   - *"✗ MIGRACIÓN FALLIDA en la tabla [X]. El destino quedó sin cambios."* — nada se tocó en el destino.
6. Con la migración completada: **💾 Guardar BD** y **reinicia la aplicación**.

### 🔷 Azure DevOps (integración opcional)
1. Marca **Habilitar integración con Azure DevOps**.
2. **URL de organización** — por ejemplo `https://dev.azure.com/mi-org`.
3. **Proyecto** — el nombre del proyecto.
4. **PAT (Personal Access Token, cifrado)** — el token. Si lo dejas en blanco al guardar, **se conserva el que ya había**.
5. Pulsa **💾 Guardar configuración general**.

Con esto aparecen los botones de DevOps en *Requerimientos* y se activa la pantalla *Azure DevOps*.

### ⏱ SLA automático por prioridad de DevOps

Una tabla con **una fila por cada prioridad de Azure DevOps** (donde 1 es la más alta): *Prioridad 1 — Muy alta*, *Prioridad 2 — Alta*, *Prioridad 3 — Media* y *Prioridad 4 — Baja*. Cada fila tiene:

| Columna | Qué controla |
|---|---|
| (casilla de la prioridad) | Activa o no el SLA automático para esa prioridad |
| Horas para vencer | El plazo, en horas, contado desde que se trae el ticket |
| Recordar cada (h) | Cada cuántas horas se le recuerda al desarrollador que comente el ticket (**0 = solo al vencer**) |

**Cómo funciona.** Al **traer los tickets asignados a un desarrollador** (por ejemplo cuando esa persona trae sus asignaciones de DevOps), la aplicación revisa cada asignación: si su prioridad tiene aquí una fila **activada** y ese requerimiento **no tiene ningún SLA** (ni siquiera uno cancelado), le crea uno solo, con el plazo y el recordatorio de esa prioridad. Así se rellenan también las asignaciones antiguas.

- **Respeta los SLA que cerraste o cancelaste a propósito**: si un requerimiento ya tuvo un SLA (cumplido o cancelado), no se le crea otro automático.
- **Por omisión están todas desactivadas**, así que mientras no actives ninguna no se crea ningún SLA automático.
- El cambio de prioridad desde el tablero (**🔧 Cambiar prioridad…**) reutiliza estas mismas políticas para reajustar o crear el SLA.

Estas políticas **siempre se guardan** con **💾 Guardar configuración general** (incluso cuando las desactivas todas).

### ⏱ Registrar tiempo cronometrado en Azure DevOps

Cuando se activa, al **detener el cronómetro** cada desarrollador registra el tiempo trabajado en el ticket de Azure DevOps del requerimiento. Cada quien reporta **con su propio PAT personal**, y solo aplica a los requerimientos que son tickets de DevOps.

1. Marca **Al detener el cronómetro, registrar el tiempo en el ticket de DevOps**.
2. En **Cómo registrarlo:** elige el modo:
   - *Comentario con el tiempo (funciona en cualquier ticket)* — agrega un comentario con las horas.
   - *Campos Completed Work (solo Task/Bug)* — suma las horas al campo *Completed Work* (solo en work items que lo tienen, como Task y Bug).
   - *Ambos*.
3. Opcionalmente marca **Bajar también el «Remaining Work» por las horas registradas** para que el trabajo restante se descuente en la misma medida.
4. Pulsa **💾 Guardar configuración general**.

### 🎫 Freshdesk (integración opcional)
1. Marca **Habilitar integración con Freshdesk**.
2. **Dominio (sin .freshdesk.com)** — por ejemplo `miempresa`.
3. **API Key (cifrada)** — si la dejas en blanco al guardar, se conserva la anterior.
4. Pulsa **💾 Guardar configuración general**.

### 📄 Documentos de vacaciones y firmas
- **Departamento (por defecto en la solicitud)**, **Puesto (por defecto)** y **Jefe directo (nombre que firma la autorización)** — se usan al generar el documento de vacaciones. Estos tres **sí se guardan aunque los dejes vacíos**, así que puedes limpiarlos.
- **Ruta de LibreOffice (soffice.exe)** — se autodetecta si está instalado; el botón **📂** te deja seleccionarlo a mano.
- **🔍 Probar LibreOffice** guarda la ruta escrita y comprueba la conversión: *"✓ LibreOffice disponible. La conversión a PDF funcionará."* o el motivo del fallo.
- **🖊 Gestionar firmas** abre el administrador de firmas guardadas.

Sin LibreOffice no se generan los PDF de vacaciones ni el organigrama de equipos.

### ✉ Correo (SMTP + IMAP)
1. Marca **Habilitar correo (envío y lectura de carpetas)**.
2. **Dirección de correo** y **Nombre para mostrar**.
3. **Contraseña de aplicación (cifrada)** — usa una contraseña de aplicación, no la del usuario. En blanco al guardar = se conserva la anterior.
4. **Servidor SMTP** (`smtp.office365.com` / `smtp.gmail.com`) y **Puerto SMTP** (587 con STARTTLS, o 465 con SSL).
5. **Servidor IMAP** (`outlook.office365.com` / `imap.gmail.com`) y **Puerto IMAP** (993).
6. **Carpeta para ingerir requerimientos** — la carpeta del buzón de la que sale el botón *Importar no leídos*. Si la dejas vacía se usa `INBOX`.
7. **Correo del jefe para escalamientos de SLA vencido** — a dónde llegan los avisos de SLA incumplido. Admite **varios separados por `;`**. Si lo dejas vacío, los avisos llegan a la propia cuenta de la aplicación.
8. Pulsa **🔍 Probar correo**. Este botón **guarda primero toda la configuración** y luego prueba SMTP y, si configuraste IMAP, también IMAP. Verás *"✓ Conexión exitosa (SMTP + IMAP)."* o el error concreto.

Con el correo habilitado se activan: la pantalla *Correo*, el envío de reportes, los avisos de aprobación/rechazo de puntos y todos los correos de SLA.

### 🗓 Tareas automáticas (respaldo y resumen)

Dos tareas que corren **solas en segundo plano**, una vez por período, mientras la aplicación esté abierta (basta con dejarla en la bandeja).

**Respaldo automático de la base**
- Marca **Respaldar la base a Blob Storage automáticamente**. Solo aplica a la base **SQLite local** (si usas Azure SQL Server, respáldala por los medios del propio servidor).
- Elige la **Frecuencia del respaldo:** *Diario* o *Semanal*.
- Sube una copia a la carpeta de respaldos del contenedor, igual que el botón manual de *💾 Respaldo BD*. Requiere Azure Blob Storage configurado.

**Resumen del equipo por correo**
- Marca **Enviar un resumen del equipo por correo** (requiere el correo configurado arriba).
- Elige la **Frecuencia del resumen:** *Diario* o *Semanal*.
- En **Destinatarios del resumen** captura los correos separados por `;`. Si lo dejas vacío, el resumen llega al *correo del jefe para escalamientos de SLA* o, en su defecto, a la propia cuenta de la aplicación.

Ambas se guardan con **💾 Guardar configuración general**.

---

## Problemas frecuentes

### Acceso y sesión

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"Usuario o contraseña incorrectos."* | Credenciales mal escritas, o la cuenta está desactivada | Verifica en *Usuarios* que la cuenta esté **Activo ✓**. Si lo está, usa **🔑 Reset pwd** |
| *"Cuenta bloqueada temporalmente por intentos fallidos. Reintenta a las HH:mm."* | 5 intentos fallidos seguidos; bloqueo de 15 minutos | Espera a la hora indicada, o restablécele la contraseña (un inicio exitoso limpia el bloqueo) |
| Al entrar aparece *"Cambio de contraseña requerido"* sin poder cerrarse | La contraseña es temporal (cuenta nueva o recién restablecida) | Es correcto: hay que cambiarla para continuar. Mínimo 8 caracteres |
| *"No tienes acceso a esa sección."* | Se intentó abrir una pantalla fuera del rol | Como Admin no debería ocurrir. Si le pasa a alguien más, revisa su rol en *Usuarios* |
| *"Esta operación requiere permisos de administrador."* | La sesión no es Admin | Revisa el rol de la cuenta |
| *"Esta operación es del área de despliegues (Administrador u Operaciones)."* | Un Desarrollador intentó desplegar | No se puede: los desarrolladores no despliegan |
| Cerré la ventana y la aplicación sigue en la barra de tareas | Es el comportamiento esperado: se esconde en la bandeja para seguir vigilando SLA y ejecutando programados | Para cerrarla del todo: clic derecho en el icono de la bandeja → **Salir** |

### Base de datos

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"Usuario o contraseña incorrectos, o ese usuario no tiene acceso a la base de datos indicada."* (error 18456) | Credenciales de SQL mal capturadas | Revisa `uid` y `pwd` en la connection string |
| *"El servidor respondió, pero la base de datos no existe o el usuario no tiene permiso sobre ella."* (4060 / 911) | Nombre de base equivocado o sin permisos | Revisa `database` y los permisos del login |
| *"El firewall de Azure SQL está bloqueando tu IP..."* (40615 / 40532) | Tu IP no está autorizada | Agrégala en *Networking* del servidor, en el portal de Azure |
| *"La base de datos está despertando o no está disponible ahora mismo."* (40613) | Nivel serverless dormido | Reintenta en unos segundos |
| *"No se alcanzó el servidor. Revisa el nombre, que el puerto 1433 esté abierto..."* (53 / 10060 / 10061) | Red, VPN o puerto cerrado | Revisa tu conexión, VPN y firewall |
| *"Se agotó el tiempo de espera al conectar con el servidor."* | Servidor lento o inalcanzable | Reintenta; si persiste, revisa la red |
| El mensaje menciona un problema de *certificate* | Certificado autofirmado en SQL local | Agrega `TrustServerCertificate=True;` a la connection string |
| *"No se pudo conectar a la base de datos del equipo. Revisa tu red o VPN."* al abrir la aplicación | La aplicación está apuntando a SQL Server y no lo alcanza | Conéctate a la VPN y vuelve a abrir |
| Guardé el proveedor y sigue usando el anterior | El cambio de proveedor requiere reinicio | Cierra la aplicación **del todo** (bandeja → Salir) y ábrela otra vez |
| *"No se encontró la base de datos SQLite local."* al migrar | No hay base local que migrar en este equipo | Ejecuta la migración desde el equipo donde sí se usó SQLite |
| *"⚠ No se copió ninguna fila: la base local está vacía."* | La base local no tiene datos | No des la migración por buena; revisa desde qué equipo migras |

### Azure Blob Storage y enlaces de descarga

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"Azure Blob Storage no está configurado. Ve a Configuración y agrega la connection string."* | Falta la cadena de conexión | Captúrala en *Configuración → Azure Blob Storage* y prueba antes de guardar |
| *"⚠ Azure Blob Storage no está configurado (Configuración → Azure Blob Storage)."* en la pestaña Blob | Igual que el anterior | Igual que el anterior |
| *"La connection string no tiene el formato esperado..."* | Cadena incompleta o mal copiada | Cópiala completa desde *Cuenta de almacenamiento → Claves de acceso → Cadena de conexión* |
| *"Acceso denegado (403)."* | Clave revocada o sin permisos sobre el contenedor | Regenera la clave en el portal y actualízala aquí |
| *"No se encontró la cuenta o el contenedor (404)."* | Nombre mal escrito | Revisa el nombre de la cuenta y del contenedor |
| *"La connection string configurada no incluye la clave de la cuenta, así que no se puede firmar un enlace SAS."* | Estás usando una cadena basada solo en SAS | Cambia a la cadena completa de la cuenta de almacenamiento |
| *"El carácter «x» no se puede usar en el nombre de la carpeta."* | Carácter prohibido (`"` `<` `>` `\|` `:` `*` `?`) | Usa solo letras, números, guiones y `/` para anidar |
| *"«X»: solo se permiten letras sin acento, números y guion bajo"* al guardar metadatos | El nombre del metadato lleva acentos, espacios o símbolos | Renómbralo, por ejemplo `version_cliente` en vez de `versión cliente` |
| *"«X»: no puede empezar con un número."* | El nombre del metadato empieza con dígito | Antepón una letra |
| *"«X» está repetido. Azure no admite dos metadatos con el mismo nombre."* | Nombre duplicado en la rejilla | Elimina o renombra la fila duplicada |
| Guardé metadatos y desaparecieron otros que había | Azure reemplaza el conjunto completo, no fusiona | Vuelve a capturar todos los que deban quedar |
| Cambié la carpeta de versiones y no veo los ZIP viejos | Cambiar la carpeta no mueve nada | Selecciona la carpeta anterior en el combo de la pestaña Blob Storage |

### Despliegues

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"El perfil seleccionado no tiene servidores."* | El perfil está vacío | Edita el perfil y marca sus servidores |
| *"El perfil no tiene servidores activos asignados."* | Todos sus servidores están dados de baja | Reactiva alguno en la pestaña *Servidores* o edita el perfil |
| *"El archivo ZIP de la versión no existe en disco."* | El ZIP local se borró o la versión se creó en otro equipo | Vuelve a crear la versión desde este equipo |
| *"No se puede respaldar antes de desplegar: falta configurar Azure Blob Storage. Configúralo, o desactiva el respaldo previo si asumes el riesgo."* | Marcaste respaldo para un servidor pero Azure no está configurado | Configura Azure Blob Storage, o quita el respaldo de ese servidor en **🖧 Elegir servidores…** si asumes el riesgo |
| El despliegue termina con *"⚠ Completado con errores"* | Uno o más servidores fallaron | Ve a **📜 Historial**, selecciona el despliegue y lee su log; ahí sale el servidor y el error exacto |
| *"Ya existe un servidor llamado «X»."* | Nombres duplicados | Usa un nombre distinto: dos servidores iguales hacen ilegible la bitácora |
| *"La contraseña es obligatoria para un servidor nuevo."* | Falta la contraseña al dar de alta | Captúrala. Al **editar** sí puedes dejarla en blanco para no cambiarla |
| *"No se puede eliminar: el perfil tiene despliegues en el historial."* | El perfil ya se usó | No se puede borrar. Si ya no se usa, quítale los servidores |
| *"Selecciona una carpeta de origen válida."* al crear una versión | La ruta no existe | Elígela con el botón 📁 |
| *"No se pudieron leer las carpetas de Blob Storage"* al crear una versión | Problema de red o de credenciales con Azure | Puedes crear la versión igual: quedará en la raíz de versiones |

### Despliegues programados

| Síntoma | Causa | Qué hacer |
|---|---|---|
| La cita quedó en **⚠ Perdido** | No había ninguna aplicación abierta dentro del margen de tolerancia | Deja la aplicación abierta (basta en la bandeja) en el equipo que deba ejecutarla, y vuelve a programarla |
| *"La hora programada debe ser al menos un minuto en el futuro."* | La fecha/hora ya pasó | Elige una hora futura |
| *"La tolerancia debe estar entre 5 minutos y 24 horas."* | Valor fuera de rango | Usa una de las opciones del combo |
| *"No se puede cancelar: está en estado X."* | Solo se cancelan las que están en *Programado* | Una cita ya ejecutada o perdida no se cancela |
| La cita dice **▶ En ejecución** desde hace rato | Otro equipo la tomó (mira la columna *Ejecutó*) y sigue corriendo, o esa aplicación se cerró a media ejecución | Consulta el *Historial* de despliegues para ver hasta dónde llegó |

### SLA

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"Ese objetivo ya tiene un SLA activo. Ciérralo o cancélalo antes de asignar otro."* | Dos fechas límite simultáneas sobre lo mismo no significan nada | Márcalo **✔ Cumplido** o **✖ Cancelar** y luego asigna el nuevo |
| *"La fecha límite ya pasó. Elige una futura."* / *"La fecha límite debe ser futura."* | Fecha en el pasado | Corrige la fecha y la hora |
| *"El ticket debe ser el número del work item."* | Se escribió texto en vez del número | Captura solo el número (por ejemplo `12551`) |
| El responsable dice que no le llegan recordatorios | El SLA no tiene ticket de DevOps ligado, o el correo no está configurado | Sin ticket no hay recordatorio de comentar. Revisa también *Configuración → Correo* |
| Los avisos de SLA vencido no llegan al jefe | El campo *Correo del jefe para escalamientos* está vacío | Captúralo. Si se deja vacío, los avisos llegan a la propia cuenta de la aplicación |
| *"No hay requerimientos abiertos."* / *"No hay actividades libres abiertas."* al asignar | No hay objetivos elegibles | Solo se ofrecen requerimientos no entregados ni cancelados, y actividades abiertas |

### Correo e integraciones

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"El correo no está habilitado. Actívalo en Configuración."* | Falta marcar la casilla | *Configuración → Correo* → **Habilitar correo** |
| *"Faltan datos de correo (dirección, contraseña o servidor SMTP) en Configuración."* | Configuración incompleta | Completa los tres campos y pulsa **🔍 Probar correo** |
| *"Azure DevOps no está habilitado. Configure la integración en Configuración."* | Integración apagada | Márcala y captura organización, proyecto y PAT |
| *"Freshdesk no está habilitado. Configure la integración en Configuración."* | Integración apagada | Márcala y captura dominio y API Key |
| Los botones de DevOps no aparecen en Requerimientos | La integración está deshabilitada | Se ocultan a propósito. Habilítala en Configuración |
| *"No se pudo publicar el comentario en DevOps"* | PAT vencido o sin permisos, o el ticket ya no existe | Regenera el PAT y actualízalo en Configuración |

### Otros

| Síntoma | Causa | Qué hacer |
|---|---|---|
| *"Otro usuario modificó este registro. Recarga e intenta de nuevo."* | Dos personas editaron lo mismo a la vez | Vuelve a abrir el registro y repite el cambio |
| *"Selecciona exactamente una entrada para ajustar sus puntos."* | Hay varias filas seleccionadas | Deja una sola fila seleccionada |
| *"No hay criterios activos. Ve a la pestaña Criterios y crea algunos."* | Todos los criterios están desactivados | Actívalos en *Desempeño → ⚙ Criterios de Evaluación* |
| *"No hay criterios de equipo activos."* | Falta un criterio con ámbito *Equipo* | Crea uno con **Aplica a: Equipo** |
| *"No hay equipos. Créalos en la pantalla de Equipos."* | Aún no hay equipos | Ve a *Equipo → Equipos* → **➕ Nuevo equipo** |
| No se genera el PDF de vacaciones ni el organigrama | Falta LibreOffice o la ruta es incorrecta | *Configuración → Documentos de vacaciones y firmas* → **🔍 Probar LibreOffice** |
| *"No hay firma preguardada. ¿Crear una ahora?"* | No hay firma registrada para esa cuenta | Acepta y créala, o usa **🖊 Firmas...** |

---

## Lo que NO puedes hacer y por qué

Aunque veas todo el menú, hay límites deliberados. Conocerlos ahorra tickets de soporte.

**Sobre usuarios**
- **No puedes eliminar a otro Administrador.** Quitarle el acceso a un admin es un cambio de gobierno de la aplicación, no algo que deba hacerse con un clic desde una lista; además evita que alguien borre al último administrador y deje el sistema sin quien lo gestione. Si de verdad hay que darlo de baja: cámbiale primero el rol, o desactívalo.
- **No puedes eliminar tu propia cuenta.**
- **No puedes ver la contraseña de nadie.** Solo se guardan cifradas. Lo único posible es restablecerla y generar una temporal.
- **No puedes crear un usuario con rol Desarrollador sin vincularlo** a una ficha de desarrollador.

**Sobre borrados**
Casi nada se borra de verdad, y es a propósito: si se borrara, el historial y la bitácora quedarían apuntando a registros inexistentes.

| Lo que parece borrado | Lo que realmente pasa |
|---|---|
| 🗑 Eliminar un desarrollador | Se **desactiva**. Su historial se conserva |
| 🗑 Eliminar un requerimiento | Pasa a estado **Cancelado** |
| 🗑 Dar de baja un servidor | Baja **lógica**: deja de recibir despliegues, se conserva para el historial |
| 🗑 Eliminar un equipo | El equipo sí se borra, pero **sus integrantes quedan sin equipo**, no se borran |

- **No puedes eliminar un perfil de despliegue que ya se usó.**
- **No puedes editar ni borrar la bitácora.** Es solo consulta y exportación.

**Sobre despliegues**
- **Como Administrador sí puedes desplegar a servidores sueltos** (los eliges directamente con **🖧 Elegir servidores…**). El rol **Operaciones** no: siempre despliega a un perfil habilitado, y un perfil sin servidores activos no despliega.
- **El respaldo previo es opcional por servidor, pero necesita Azure Blob Storage.** Si marcas respaldo para un servidor y Azure no está configurado, ese respaldo no se puede hacer. Dejar servidores sin respaldo es posible, pero la aplicación te advierte que sin copia previa no hay vuelta atrás.
- **No puedes revertir un despliegue desde la aplicación.** Lo que sí hay es el ZIP del respaldo de cada carpeta remota en Blob Storage, con metadatos de servidor, ruta remota y número de trabajo: la restauración se hace por fuera.
- **No puedes ejecutar un despliegue programado a destiempo.** Si pasó su tolerancia se marca como *Perdido* y no arranca: desplegar a deshora sin que nadie lo espere es peor que no hacerlo.
- **Los despliegues programados no corren si no hay ninguna aplicación abierta.** No hay un servicio de Windows detrás: es la aplicación (aunque esté en la bandeja) la que los dispara.

**Sobre Blob Storage**
- Desde la pestaña **☁ Blob Storage** puedes crear carpetas, editar metadatos, generar enlaces, copiar nombres y **eliminar archivos o carpetas completas** (acciones irreversibles que quedan en la bitácora). **No puedes renombrar archivos**; los ZIP de versiones y respaldos llegan al crear versiones y al respaldar.
- Cambiar las carpetas configuradas **no mueve lo ya guardado**.
- Los enlaces SAS, una vez generados, **no se pueden revocar** desde la aplicación: caducan solos. Por eso conviene la vigencia más corta que sirva, y por eso cada enlace queda en la bitácora.

**Sobre desempeño y SLA**
- **No puedes cambiar los puntos de una entrada ya aprobada o rechazada.** El ajuste (**✏ Ajustar puntos**) solo existe en la pestaña *Pendientes de aprobación*. Si ya está aprobada, hay que eliminarla y volver a capturarla.
- **No puedes ajustar puntos de varias entradas a la vez.**
- **Los puntos pendientes no cuentan** en el ranking, ni en el Dashboard, ni en los reportes, hasta que los apruebes.
- **No puedes tener dos SLA activos sobre el mismo objetivo.**
- **No puedes poner una fecha límite en el pasado.**

**Sobre plantillas y scripts**
- **La aplicación no ejecuta ningún script de la biblioteca.** Solo lo copia o lo guarda a un archivo. Un botón de "ejecutar" contra un servidor productivo, a un clic de distancia y sin ver lo que se va a correr, es exactamente el accidente que no queremos.
- **Las plantillas son solo del rol Administrador**, también para consultarlas: suelen traer nombres de servidores, bases y rutas internas.
- **Un archivo adjunto no puede pasar de 10 MB**: la plantilla vive en la base de datos del equipo. Para algo más grande, súbelo a Blob Storage y deja aquí el enlace.
- **El catálogo de ejemplo se siembra una sola vez.** Si borras esas plantillas, no vuelven en el siguiente arranque.

**Sobre configuración**
- **El cambio de proveedor de base de datos no aplica hasta reiniciar** la aplicación.
- **Las claves cifradas están atadas a este equipo y usuario de Windows.** Si la aplicación se instala en otra máquina, hay que volver a capturar las connection strings, el PAT, la API Key y la contraseña de correo.
- **El respaldo previo al despliegue es opcional y se elige por servidor** (o con la casilla *Respaldar antes* en el rol Operaciones). Cuando lo activas para un servidor y ese respaldo falla, ese servidor **no se despliega**: un respaldo que "se intentó" no sirve el día que hay que revertir.

---

## Puesta en marcha

Orden recomendado para dejar la aplicación lista desde cero. Cada paso se apoya en el anterior; si te saltas uno, el siguiente te va a fallar.

### 1. Primer arranque
Abre la aplicación. Como no hay usuarios, se crea el administrador inicial y verás **una sola vez** un mensaje con:

```
Usuario:  admin
Contraseña temporal:  (12 caracteres generados al azar)
```

Anótala antes de cerrar el mensaje. Inicia sesión con ella; la aplicación te obligará a cambiarla de inmediato.

En este primer arranque la aplicación también siembra el catálogo completo de criterios de desempeño (individuales y de equipo) y el juego inicial de **plantillas** (*Administración → Plantillas*), que está para editarse a la medida del área.

### 2. Base de datos definitiva
Si el equipo va a compartir una sola base:

1. **Configuración → Base de datos**: elige *Azure SQL Server* y captura la connection string.
2. **🔍 Probar conexión**. No sigas si falla.
3. **📤 Migrar SQLite → Azure SQL** si ya tienes datos locales que conservar.
4. **💾 Guardar BD** y **reinicia la aplicación**.

Si te quedas con SQLite local, no toques nada: es el valor por omisión.

### 3. Azure Blob Storage
1. **Configuración → Azure Blob Storage**: captura la connection string **completa** de la cuenta y el nombre del contenedor.
2. **🔍 Probar conexión con Azure**. Confirma que dice *"Se pueden generar enlaces SAS de descarga."*.
3. Deja las tres carpetas vacías salvo que quieras nombres propios.
4. **💾 Guardar configuración general**.

Sin este paso no puedes respaldar la base de datos, ni respaldar la carpeta remota de un servidor antes de sobrescribirla (puedes desplegar sin respaldo, pero te quedas sin red de seguridad).

### 4. Correo
1. **Configuración → Correo**: marca **Habilitar correo** y captura dirección, nombre para mostrar, contraseña de aplicación, SMTP e IMAP.
2. Captura la **carpeta para ingerir requerimientos** y el **correo del jefe para escalamientos de SLA vencido**.
3. **🔍 Probar correo**.

### 5. Integraciones (opcional)
- **Azure DevOps**: habilítala y captura organización, proyecto y PAT. Después, en *Requerimientos*, define las reglas de autoasignación con **⚙ Reglas**.
- **Freshdesk**: habilítala y captura dominio y API Key.
- **LibreOffice**: **🔍 Probar LibreOffice** si vas a generar PDF de vacaciones u organigramas. Registra tu firma en **🖊 Gestionar firmas**.

### 6. El equipo
1. **Equipo → Desarrolladores**: da de alta a cada persona con **➕ Nuevo**.
2. **Equipo → Equipos**: crea los equipos con **➕ Nuevo equipo**, arrastra a la gente a su columna y asigna roles con clic derecho (marca al **👑 Líder** de cada uno).
3. Vuelve a **Desarrolladores** y pulsa **🔐 Acceso** por cada persona que deba entrar a la aplicación. Entrégales su usuario y contraseña temporal.
4. **Administración → Usuarios**: crea las cuentas con rol **Operaciones** que necesites.
5. **Equipo → Contactos**: captura los contactos externos relevantes.

### 7. Infraestructura de despliegue
Este es el bloque que más orden requiere:

1. **Despliegues → 🖥 Sistemas y Versiones → ➕ Nuevo**: da de alta cada sistema, con su carpeta de origen por defecto.
2. **Despliegues → ☁ Blob Storage → 📁 Nueva carpeta**: crea las subcarpetas de destino que vayas a usar (*QA*, *Productivo*, por cliente…) colgadas de la carpeta de versiones.
3. **Despliegues → 🌐 Servidores → ➕ Nuevo**: da de alta cada servidor FTP/FTPS. Si los tienes en un JSON, usa **📥 Importar JSON**.
4. **Despliegues → 🔧 Perfiles → ➕ Nuevo perfil**: agrupa los servidores. Marca **Permitir ejecución al rol Operaciones** solo en los perfiles que Operaciones deba poder usar por su cuenta.
5. **Despliegues → 🖥 Sistemas y Versiones → ➕ Nueva versión**: crea la primera versión eligiendo carpeta de origen y carpeta de destino en Blob Storage.
6. **Despliegues → 🚀 Desplegar**: haz un primer despliegue de prueba a un perfil con un solo servidor no productivo, y revisa el resultado en **📜 Historial**.

### 8. Trabajo en marcha
1. **Trabajo → Requerimientos**: captura o importa desde DevOps y asigna desarrolladores con **👥 Asignar**.
2. **Trabajo → SLA y recordatorios**: asigna los compromisos que requieran seguimiento, con su ticket de DevOps.
3. **Trabajo → Desempeño → ⚙ Criterios**: revisa el catálogo sembrado y desactiva lo que no uses.
4. Deja la aplicación abierta o en la bandeja: es lo que mantiene vivos los recordatorios de SLA, el escalamiento al jefe y los despliegues programados.

### 9. Rutina de mantenimiento
- **Despliegues → 💾 Respaldo BD → ☁ Respaldar en Azure** con la periodicidad que definan (o deja que lo haga la tarea automática de *Configuración → 🗓 Tareas automáticas*, que solo aplica a la base SQLite local).
- **Administración → Bitácora**: revisión periódica, sobre todo de las acciones *Deploy*, *ConfigChange* y *Delete*.
- **Trabajo → Desempeño → ⏳ Pendientes de aprobación**: revisa lo que se acumule (el menú te avisa con el contador rojo).
- **Despliegue e Infraestructura → Programas**: revisa las licencias marcadas en amarillo o rojo.