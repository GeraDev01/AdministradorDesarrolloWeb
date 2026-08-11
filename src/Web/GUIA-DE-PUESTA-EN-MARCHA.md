# Guía de puesta en marcha: de donde estamos hoy a la web en producción

> Esto es el camino completo. [EL-CORTE.md](EL-CORTE.md) describe **solo el día del corte**, que es
> la fase 3 de las cuatro que hay aquí. Si buscas el procedimiento del día, ve directo allá.

**Nada de las fases 0, 1 y 2 toca la base de producción.** Se puede hacer con calma, en varios días,
sin avisar a nadie y sin ventana de mantenimiento. Solo la fase 3 la toca.

---

## Fase 0 — Revisar lo que hay (una tarde)

### 0.1 Mirar la rama antes de subirla

```bash
git checkout replica-web
git log --oneline main..replica-web
```

Son seis commits: uno del escritorio (pool y asistencia, que ya está en producción) y el resto de la
web. El del escritorio va aparte a propósito, por si quieres llevarlo a `main` sin lo demás.

### 0.2 Levantarla en tu equipo y jugar con ella

```bash
cd src/Web
docker compose up -d --build
```

Arranca con SQL Server en un contenedor y una base **vacía y desechable**. No toca nada real.

Y no arranca vacía: se siembra **un equipo de demostración** —cuatro desarrolladores, dos equipos,
requerimientos repartidos, pool con actividades, vacaciones, despliegues hechos, foro con
conversaciones y bitácora—. Una aplicación en blanco no se puede juzgar: no se ve qué hace un
ranking sin puntos ni cómo se lee un sprint sin requerimientos.

Entra en <http://localhost:8080> con cualquiera de estas cuentas. **Todas usan `Demo.2026`**:

| Cuenta | Rol | Para ver |
|---|---|---|
| `lider` | Administrador | Todo: pool, desempeño, bitácora, despliegues, configuración |
| `ops` | Operaciones | La vista recortada de quien solo despliega |
| `ana`, `beto`, `caro`, `dani` | Desarrollador | El autoservicio: mis asignaciones, mi pool, mi jornada |

> **Los datos de demostración no pueden aparecer en producción.** Se siembran solo si se pide con
> `AdminWeb__DatosDeDemostracion` (puesta a `true` únicamente en el `docker-compose.yml`), **y**
> además el entorno no es `Production`, **y** además la base no tiene ni un desarrollador. Contra
> cualquier base con contenido no hace nada y lo dice en el registro. Está en `DatosDeDemostracion`.

También sigue existiendo el administrador de arranque, por si quieres probar el cambio de contraseña
obligatorio del primer acceso. Su contraseña temporal se **escribe en el registro**:

```bash
docker logs adminweb-api-1 | grep "contraseña temporal"
```

### 0.3 Pasar la prueba de humo

```powershell
.\humo-docker.ps1
```

Recorre todas las pantallas contra SQL Server, prueba la escritura, el conflicto de concurrencia y
las exportaciones. Si algo se rompió, sale aquí y no el día del corte.

**Al terminar la fase 0 no has tocado producción.** `docker compose down` lo deja todo como estaba.

---

## Fase 1 — Preparar Azure (medio día, sin prisa)

Todo esto se hace una vez y no se vuelve a tocar.

### 1.1 Generar las llaves VAPID

Son las que permiten que llegue un aviso con la pestaña cerrada — lo que sustituye al globo de la
bandeja del escritorio.

**Se generan UNA vez y no se regeneran nunca.** Regenerarlas después de que la gente se haya
suscrito invalida todas las suscripciones de golpe, y cada persona tendría que volver a aceptar los
avisos sin entender por qué dejaron de llegar.

La forma más simple, con `npx` (no instala nada permanente):

```bash
npx web-push generate-vapid-keys
```

Te da una pública y una privada. Guárdalas donde guardes lo importante **antes** de seguir: la
privada no se puede recuperar.

### 1.2 Crear los recursos

| Recurso | Para qué |
|---|---|
| **App Service** (Linux, .NET 10) | Donde corre la aplicación |
| **Ranura de ensayo** («staging») | Desplegar ahí y comprobar antes de intercambiar |
| **Contenedor de Blob** «llavero» | Las llaves con las que se cifran los PAT personales |
| **Certificado** subido al App Service | Con qué se cifra ese llavero. Ver 1.3 |
| **Identidad administrada** en el App Service | Cómo lee y escribe el Blob sin ninguna credencial |

Dale a la identidad administrada permiso de lectura/escritura sobre el contenedor del llavero.

**No hay Key Vault**, y eso cambia dos cosas que conviene tener claras antes de seguir.

### 1.3 Dónde viven los secretos sin Key Vault

Son dos problemas distintos y se resuelven distinto.

#### Los secretos de configuración: ajustes del App Service

La cadena de conexión y la llave privada VAPID pasan a ser ajustes de la aplicación, sin más.

**Lo que se gana:** nada que montar, nada que permisar, un recurso menos que pagar y una dependencia
menos en el arranque. Se lee de un sitio y ya está.

**Lo que se pierde, dicho sin adornos:**

- **La rotación deja de ser una operación y pasa a ser un despliegue.** Cambiar la contraseña de la
  base ya no es «una versión nueva del secreto en el Vault»; es editar el ajuste y reiniciar la
  aplicación. Con la llave VAPID da igual —esa no se rota nunca—, con la cadena de conexión no.
- **Se pierde la auditoría de quién leyó qué.** El Vault deja registro de cada lectura; un ajuste del
  App Service, no. Cualquiera con permiso de escritura sobre la aplicación puede verlos en el portal,
  y no queda rastro de que lo hizo. Los secretos siguen cifrados en reposo del lado de Azure, pero el
  control de quién los mira se queda en los permisos del App Service y en nada más.
- **No hay caducidad ni recordatorio.** Nadie te va a avisar de que ese secreto lleva tres años ahí.

Es un intercambio consciente, no un descuido. Con un equipo pequeño y pocos administradores es
razonable; si algún día crece el número de gente con acceso al App Service, es lo primero que hay que
revisar.

#### El llavero: Blob **cifrado con un certificado**

Este no se puede resolver igual, porque no es un secreto: es el juego de llaves con el que se cifran
los PAT personales de Azure DevOps de todo el equipo. Va en el Blob —tiene que sobrevivir a los
reinicios y ser el mismo para las dos instancias— y ahí, en claro, lo lee cualquiera con permiso de
lectura sobre el contenedor. Así que se cifra con un certificado:

1. Genera un certificado (uno autofirmado sirve: aquí solo se usa para cifrar, nadie lo valida) y
   **guarda el .pfx donde guardes lo importante**. Ponle caducidad larga, de años.
2. Súbelo al App Service, en *Certificados → Certificados de clave privada (.pfx)*.
3. Añade el ajuste `WEBSITE_LOAD_CERTIFICATES` con su huella —o con `*`— para que el proceso pueda
   verlo. **Sin este ajuste, el certificado está subido y la aplicación no lo encuentra**; es la
   causa habitual del fallo, y el mensaje de arranque te lo dice.
4. Pon la huella en `AdminWeb__Llavero__Certificado`.

> **Si hay huella configurada y el certificado no aparece, la aplicación no arranca.** Es
> deliberado. Arrancar significaría escribir llaves nuevas *sin cifrar* al lado de las viejas —que
> sin el certificado ya no se pueden descifrar—: se perderían todos los PAT y el llavero quedaría
> legible, mientras la configuración sigue diciendo que está cifrado. Como el despliegue pasa por la
> ranura de ensayo y comprueba `/api/health` antes de intercambiar, un arranque fallido detiene el
> intercambio y producción sigue corriendo la versión anterior. El razonamiento completo está en la
> cabecera de `AdminWeb.Api/Arranque/Llavero.cs`.

### 1.4 Configurar la aplicación

En la configuración del App Service:

| Clave | Valor |
|---|---|
| `ConnectionStrings__Default` | La cadena de conexión a la base |
| `AdminWeb__Push__LlavePublica` | La pública del paso 1.1 |
| `AdminWeb__Push__LlavePrivada` | La privada del paso 1.1 |
| `AdminWeb__Push__Sujeto` | `mailto:` con un correo real del área |
| `AdminWeb__Llavero__Blob` | URI del contenedor del llavero |
| `AdminWeb__Llavero__Certificado` | Huella del certificado que lo cifra |
| `AdminWeb__Llavero__CertificadosAnteriores` | Vacío al principio. Se llena al rotar (fase 4) |
| `WEBSITE_LOAD_CERTIFICATES` | La misma huella, o `*` |
| `AdminWeb__TrabajosDeFondoActivos` | **`false`** — se enciende en el corte, no antes |

> **`AdminWeb__Llavero__Blob` no es opcional en Azure.** Sin él las llaves viven en el disco del
> contenedor, que es efímero: cada reinicio inventa unas nuevas y todos los PAT ya guardados dejan de
> descifrarse. El síntoma no es un error claro, es «tu token de DevOps no sirve» — y manda a mirar
> justo al sitio equivocado. El arranque lo avisa en el registro; búscalo la primera vez.

> Si algún día esta suscripción tiene Key Vault, `AdminWeb__Llavero__LlaveDeKeyVault` sigue
> soportado y tiene precedencia sobre el certificado: pasaría a cifrar el vault y los certificados se
> quedarían solo para descifrar lo antiguo. Es exactamente el mismo mecanismo que la rotación, así
> que la migración no tendría ningún paso especial.

### 1.5 Conectar el pipeline

En el repositorio de GitHub hacen falta tres secretos y una variable:

```
secrets.AZURE_CLIENT_ID          identidad federada
secrets.AZURE_TENANT_ID
secrets.AZURE_SUBSCRIPTION_ID
vars.AZURE_WEBAPP_NAME           el nombre del App Service
```

El pipeline (`.github/workflows/web.yml`) compila, corre las dos suites de pruebas, publica a la
**ranura de ensayo**, comprueba `/api/health` y solo entonces intercambia con producción.

---

## Fase 2 — El ensayo (medio día) — **esto no es opcional**

**El ensayo es lo que te dice cuánto dura la ventana de mantenimiento.** Sin él estás adivinando, y
lo que se adivina mal aquí se paga con el equipo parado.

### 2.1 Sacar una copia fresca de producción

En Azure SQL: restauración a un punto en el tiempo, **a una base nueva**. No toques la original.

### 2.2 Apuntar una instancia de la web a esa copia y arrancarla

Puede ser la ranura de ensayo con la cadena cambiada, o tu propio equipo. Al arrancar, el migrador
pone el esquema al día bajo `sp_getapplock`.

### 2.3 Medir cuánto tardó

**Cronométralo.** Ese número, más margen, **es tu ventana de mantenimiento**.

### 2.4 Recorrerla con datos reales

Entra con **al menos dos cuentas de rol distinto** —líder y desarrollador— y abre cada módulo.
Genera un documento de vacaciones, publica en el foro, marca una jornada, arranca un cronómetro.

Comprueba que las integraciones responden **con las credenciales de verdad**: Azure DevOps,
Freshdesk, correo, Blob, FTP.

### 2.5 Tirar la copia

Si algo falló, arréglalo y **vuelve a ensayar**. El ensayo es barato; el corte no.

---

## Fase 3 — El corte

A partir de aquí sí tocas producción. El procedimiento completo está en
[EL-CORTE.md](EL-CORTE.md); son nueve pasos y conviene leerlos allí, con su contexto.

El resumen para que sepas a qué te enfrentas:

| | | |
|---|---|---|
| **0** | Convertir los secretos DPAPI | **Con el escritorio aún abierto** |
| **1** | Avisar y cerrar el escritorio | |
| **2** | Respaldo explícito, hora anotada | |
| **3** | Apuntar la web a producción y arrancarla | Aquí migra el esquema |
| **4** | `GET /api/health` → 200 | Si no, **se para** |
| **5** | Recapturar los secretos ilegibles | |
| **6** | Encender los trabajos de fondo | |
| **7** | Que entre el equipo | |
| **8** | La marcha atrás, si hace falta | El escritorio vuelve a abrirse |
| **9** | Días después: revocar su acceso | |

**El paso 0 es el único sin marcha atrás si se olvida.** Las contraseñas FTP y la cadena del Blob
pueden estar cifradas con DPAPI, que ata el cifrado a la cuenta de Windows que las guardó. El
servidor no puede leerlas nunca, y el único programa capaz de convertirlas es el escritorio que estás
retirando: lo hace solo al arrancar. Ábrelo en el equipo de quien capturó cada secreto **antes** de
cerrarlo para siempre.

---

## Fase 4 — Después del corte

### Los primeros días

Deja el escritorio **instalado y funcionando**. Mientras no hagas el paso 9, la marcha atrás existe
y es sencilla: los cambios de esquema son aditivos y el escritorio los ignora.

Avisa al equipo de dos cosas, porque ninguna se puede hacer por ellos:

- **Cada quien tiene que volver a capturar su PAT de Azure DevOps**, en «Mis tickets DevOps». El
  anterior vivía cifrado en su propia máquina y no hay forma de migrarlo. A cambio, el nuevo les
  sigue al cambiar de equipo. Mientras no lo hagan, sus comentarios en DevOps saldrán firmados por la
  cuenta de la instalación.
- **Acepta el permiso de avisos** la primera vez que abran la web. Sin eso no llegan avisos con la
  pestaña cerrada.

### Cuando ya esté estable

Entonces sí, el paso 9: revocar el acceso a la base del cliente de escritorio. Con eso el `.exe`
viejo queda inerte aunque alguien lo conserve, que es lo que de verdad cierra la puerta.

### Rotar el certificado del llavero

**Léelo antes de tocar nada, aunque parezca que sobra.** Hacer esto mal no da error: la aplicación
arranca, se ve normal y los PAT del equipo entero quedan ilegibles. El síntoma llega días después y
disfrazado de «tu token de DevOps no sirve».

El motivo es simple. `AdminWeb__Llavero__Certificado` dice con qué se cifran las llaves **nuevas**.
Las que ya están escritas siguen cifradas con el certificado que hubiera entonces, y solo se pueden
leer si ese certificado sigue disponible. Por eso hay dos ajustes y no uno: **rotar es añadir, no
sustituir.**

Apunta la fecha de caducidad en el calendario del área el día que lo subas. El arranque avisa cuando
quedan menos de 30 días, pero un servicio que va bien puede pasarse semanas sin reiniciar y ese aviso
no llega a tiempo si nadie lo provoca.

1. **Genera el certificado nuevo** y guarda su .pfx donde guardes lo importante.
2. **Súbelo al App Service** sin quitar el viejo. Los dos conviven; no estorban.
3. **Deja `WEBSITE_LOAD_CERTIFICATES` en `*`.** Si prefieres enumerar huellas, tienen que estar las
   dos. Si aquí dejas solo la nueva, el certificado viejo está subido pero el proceso no lo ve, que a
   efectos prácticos es igual que haberlo borrado — por eso la recomendación es `*` y olvidarse.
4. **Mueve la huella vieja** de `AdminWeb__Llavero__Certificado` a
   `AdminWeb__Llavero__CertificadosAnteriores` (es una lista separada por comas; se le van sumando).
5. **Pon la huella nueva** en `AdminWeb__Llavero__Certificado`.
6. **Reinicia y lee el registro de arranque.** Tiene que decir con qué certificado quedó cifrado y
   cuántos anteriores hay disponibles para descifrar. Si falta alguno, lo dice con su huella.
7. **Compruébalo de verdad**: entra con una cuenta que tenga su PAT capturado y abre «Mis tickets
   DevOps». Si lista tickets, el llavero viejo se está descifrando bien. Este paso es el único que
   distingue una rotación buena de una que solo *parece* buena.

**Cuándo se puede borrar el certificado viejo: nunca, y no es una exageración.** Data Protection no
tira las llaves viejas del llavero — las conserva justamente para poder descifrar lo que se cifró con
ellas, y **nunca las vuelve a cifrar con el certificado nuevo**. Un PAT que alguien capturó hace dos
años y no ha vuelto a guardar sigue dependiendo de la llave de entonces, y esa llave sigue cifrada
con el certificado de entonces. No hay ningún plazo tras el cual eso deje de ser cierto.

Así que **la lista de anteriores solo crece**. Es barato: un certificado más cada varios años, y una
huella más en un ajuste. Si algún día molesta de verdad, la única forma limpia de vaciarla es
avisar al equipo, tirar el llavero entero y que todo el mundo vuelva a capturar su PAT — una
operación anunciada, no una limpieza de mantenimiento.

**Si ya te pasó** —rotaste borrando el viejo y no lo tienes—: no hay nada que recuperar, los PAT
guardados se perdieron. Deja la configuración consistente (huella nueva arriba, lista vacía) y avisa
al equipo de que vuelva a capturar su PAT en «Mis tickets DevOps», igual que después del corte.

---

## Lo que NO se hace nunca

- **Ejecutar SQL a mano contra producción.** El esquema lo pone al día el migrador al arrancar.
  `SET PARSEONLY ON` **no valida sin ejecutar**, aunque suele creerse lo contrario.
- **Encender los trabajos de fondo con el escritorio todavía en uso.** Los temporizadores de las dos
  aplicaciones hacen el mismo trabajo: todo llegaría por duplicado.
- **Apuntar la web a producción «solo para probar» con el escritorio vivo.** No es inofensivo: los
  dos migradores correrían sin orden determinado, y solo seis entidades tienen `RowVersion` — el
  resto se pisaría en silencio, sin error y sin rastro.
- **Regenerar las llaves VAPID** después de que la gente se haya suscrito.
- **Rotar el certificado del llavero borrando el viejo.** Su huella pasa a
  `AdminWeb__Llavero__CertificadosAnteriores` y el certificado se queda subido unos meses. Sustituirlo
  de golpe deja ilegible todo lo cifrado hasta ese momento, sin error y sin aviso.

---

## Si algo va mal

| Síntoma | Dónde mirar |
|---|---|
| No arranca | El registro del App Service. Si falta la cadena de conexión, lo dice por su nombre |
| No arranca y el registro habla de un **certificado** | Es a propósito. El certificado del llavero no aparece: repasa el paso 1.3, sobre todo `WEBSITE_LOAD_CERTIFICATES` |
| `/api/health` no responde 200 | La base no contesta. **No sigas**: diagnostica o vuelve atrás |
| «Tu token de DevOps no sirve» tras un reinicio | El llavero. Busca la línea de `Llavero` en el registro de arranque |
| «Tu token de DevOps no sirve» tras rotar el certificado | El anterior. Vuelve a subirlo y comprueba que su huella esté en `CertificadosAnteriores` **y** en `WEBSITE_LOAD_CERTIFICATES` |
| Un secreto de configuración sale como «hay que recapturarlo» | Es DPAPI. Recaptúralo en Configuración |
| No llega ningún aviso con la pestaña cerrada | Las llaves VAPID, o el permiso del navegador |
| No sé qué versión está corriendo | `GET /api/version`, o el pie del menú lateral |
