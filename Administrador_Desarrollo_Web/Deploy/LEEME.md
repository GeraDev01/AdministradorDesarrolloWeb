# Empaquetado para repartir al equipo

Un solo ejecutable, el mismo para todos. Trae la conexión a la base incrustada para que nadie
tenga que configurarla, y **el menú lo decide el rol de la cuenta con la que cada quien entra**.

| Rol de la cuenta | Qué ve |
|---|---|
| Admin | todo: equipo, trabajo, despliegues, integraciones, administración y configuración |
| Desarrollador | Mi Panel · Mis Asignaciones · Mis Actividades · Mis Vacaciones |
| Operaciones | Despliegues |

Eso se decide en `MainForm` a partir de `CurrentUserContext.Role`, que es el rol real de la cuenta.
Las guardas de `AuthorizationGuard` salen del mismo lugar, así que la visibilidad del menú y los
permisos no pueden discrepar.

## Cómo generarlo

```powershell
# 1. Una sola vez: crear el login restringido en la base
#    (abre crear-login-desarrollador.sql, cambia la contraseña y ejecútalo contra la base de la app)

# 2. Publicar
cd Administrador_Desarrollo_Web\Deploy
.\build-app.ps1 -ConnectionString "server = TU-SERVIDOR.database.windows.net; uid = app_dev; pwd = ...; database = TU_BASE"
```

Sin `-ConnectionString` toma la conexión que tengas configurada en tu propio equipo, que es la
cuenta con permisos completos — sirve para probar, no para repartir.

Resultado: `dist\desarrollador\Administrador_Desarrollo_Web.exe`, ~64 MB, un solo archivo. Se copia
y se ejecuta; no requiere instalar .NET ni nada más.

## Qué NO lleva dentro (y por qué no hace falta)

En el `.exe` va la conexión a la base y nada más. El Blob Storage y los servidores FTP **no** se
incrustan, y aun así nadie tiene que capturarlos: viven en la base y se cifran con una llave común
de la aplicación —no con la cuenta de Windows de quien los capturó—, así que **cualquier ejecutable
los lee**.

Eso es lo que permite que Operaciones trabaje desde cualquier PC: un servidor dado de alta hoy desde
un equipo lo usan todos los demás enseguida, **sin republicar nada**.

Incrustarlos, en cambio, solo habría servido para arrancar contra una base vacía —una vez en la vida
del sistema— y a cambio cada `.exe` repartido llevaría dentro las contraseñas de todos los servidores
de producción. Ver `Docs\Conexion-portable.md`.

## De dónde sale la conexión

Precedencia, de mayor a menor (`DbConnectionResolver`):

1. **Configuración de este equipo** — lo capturado en Configuración → Base de datos (`dbprovider.json`), solo si es de SQL Server.
2. **Conexión incrustada** — la que se metió al publicar.

Que la local gane es lo que permite repartir un único ejecutable: el administrador configura en su
máquina las credenciales completas y sobrescribe la incrustada, mientras el resto del equipo usa
tal cual la que viene dentro.

**SQLite ya no es un destino.** Si no hay ninguna de las dos conexiones, o el servidor no responde,
la pantalla de inicio de sesión muestra un ● **rojo** con el motivo y no deja entrar. Antes la app
abría una base SQLite local y seguía trabajando: como ahí no existe ningún usuario del equipo, el
login solo decía «Usuario o contraseña incorrectos» y el problema real —repartir un ejecutable
publicado sin `EmbedDbConnection`, o tener SQLite elegido en `dbprovider.json`— pasaba inadvertido.
Tener SQLite elegido en la configuración local tampoco aparta ya a la incrustada.

La app registra en el log cuál quedó en efecto (`BD inicializada [origen]: destino`). El indicador del
login solo dice si hay conexión o no —esa pantalla se ve antes de autenticarse, así que no enseña
servidor ni base—; para saber contra QUÉ base está una PC, se consulta el log:

```powershell
Get-Content "$env:APPDATA\AdministradorDesarrolloWeb\logs\app-*.log" | Select-String "BD inicializada"
```

Un ejecutable publicado **sin** `-p:EmbedDbConnection=true` (por ejemplo con el botón *Publicar* de
Visual Studio en vez de este script) no trae conexión: en un equipo sin configurar arranca en rojo.
Es intencional — antes ese mismo ejecutable acababa en una base local vacía.

## Sobre la contraseña incrustada

`Deploy\devbuild.bin` se cifra con AES-256, pero **la llave está en el propio ejecutable**. Es
ofuscación: evita que la contraseña salga con un `strings` sobre el .exe, nada más. Cualquiera con
el archivo puede recuperarla.

Por eso lo que se incrusta debe ser `app_dev` (lector/escritor, sin permisos de esquema, sin acceso
a `DeploymentTargets` ni `AppSettings`), nunca la cuenta administradora. El administrador captura
las credenciales completas en su propio equipo, donde ganan sobre la incrustada.

Como `app_dev` no tiene permisos de DDL, al arrancar la app intenta verificar el esquema y, si no
puede, lo registra como advertencia y continúa — el esquema ya lo preparó el administrador. Solo
aborta si además no hay conexión.

El script borra `devbuild.bin` del árbol de fuentes al terminar. Si necesitas conservarlo para
depurar, usa `-KeepResource` — y no lo subas a ningún repositorio.

Riesgo que permanece: la app verifica la contraseña del lado del cliente, así que `app_dev` necesita
leer `Users.PasswordHash`. Son hashes bcrypt, pero alguien puede extraerlos e intentar romperlos sin
conexión. Detalle y mitigaciones al final de `crear-login-desarrollador.sql`.

## El PAT de Azure DevOps es de cada quien

No se reparte incrustado ni se guarda en la base. Cada persona captura el suyo desde
**Mis SLA → 🔑 Mi PAT**, y queda cifrado con DPAPI en su propia computadora
(`%APPDATA%\AdministradorDesarrolloWeb\devops-personal.json`).

El motivo es de fondo, no de comodidad: cuando la aplicación publica un comentario en un work item,
DevOps lo firma con el dueño del token. Con un token compartido, todos los comentarios aparecerían a
nombre de la misma cuenta y el SLA dejaría de probar quién atendió.

La URL de organización y el proyecto sí salen de la configuración compartida (no son secretos), por
eso `app_dev` puede LEER `AppSettings` aunque no modificarla. Los valores marcados como secretos ahí
sí están cifrados, pero con una llave común de la aplicación —tienen que serlo: son credenciales de
equipo (Blob, FTP) que cualquier PC debe poder usar—. Es decir: **quien tenga el ejecutable y acceso
de lectura a la base puede recuperarlos**. Lo que de verdad los acota es este login restringido y el
acceso a la red, no el cifrado. Un secreto que deba ser *personal* no va en `AppSettings`: va en el
equipo de cada quien, como el PAT.

El PAT necesita permiso **Work Items → Read & write**. El botón «Probar conexión» del diálogo
confirma con qué cuenta quedó autenticado.

## La aplicación queda en la bandeja del sistema

Cerrar la ventana con la **X** no termina el programa: lo esconde junto al reloj y sigue revisando
los SLA cada 5 minutos. Para cerrarlo de verdad: clic derecho en el ícono → **Salir**.

Es lo que hace que los recordatorios y el escalamiento al jefe funcionen sin depender de que alguien
tenga la ventana abierta. Con la ventana escondida los avisos salen como globo de notificación en
lugar de ventana emergente (un diálogo detrás de todo no lo ve nadie); al tocar el globo se abre la
aplicación directo en «Mis SLA».

Al **cerrar sesión** el ícono desaparece y la vigilancia se apaga: sin usuario no hay a quién avisar.
El apagado de Windows tampoco se intercepta — ahí sí termina normalmente, consolidando los
cronómetros abiertos.

Sigue habiendo un límite: si la persona apaga su computadora, nada se revisa hasta que la vuelva a
encender y abrir la aplicación.

## Windows dice «Windows protegió su PC»

Eso es **SmartScreen**, y su causa es una sola: el ejecutable **no está firmado digitalmente**.
No es un falso positivo que se pueda desactivar con un ajuste ni con una exclusión; mientras el
binario no lleve firma, cada persona que lo descargue verá el aviso la primera vez.

Un ejecutable **autocontenido de un solo archivo** lo tiene todavía más difícil: para arrancar se
descomprime a sí mismo en una carpeta temporal, que es exactamente el comportamiento que los
antivirus asocian con empaquetadores maliciosos. Por eso a veces también salta el antivirus, no solo
SmartScreen.

**Lo que ya se hizo aquí** (ayuda, pero no elimina el aviso): el .exe lleva empresa, producto,
descripción, versión y copyright — un binario sin ningún metadato puntúa peor en las heurísticas de
antivirus. Se configura en `Administrador_Desarrollo_Web.csproj`.

**Lo que sí lo elimina:** firmar con un certificado de **firma de código (Authenticode)**.

| Opción | Costo aprox./año | Aviso de SmartScreen |
|---|---|---|
| **Azure Trusted Signing** | ~$120 USD | Desaparece de inmediato. Es lo más barato y no requiere token físico. Exige empresa con ≥3 años de antigüedad verificable. |
| Certificado **EV** (DigiCert, Sectigo…) | $250–500 USD | Desaparece de inmediato. Llega en un token USB físico. |
| Certificado **OV** estándar | $100–250 USD | **No** desaparece al principio: hay que acumular descargas hasta ganar reputación. Puede tardar semanas. |
| Sin firmar | $0 | Cada persona debe hacer «Más información → Ejecutar de todas formas». |

Ya teniendo el certificado, `build-app.ps1` lo firma solo:

```powershell
# con el certificado ya importado en Windows (token USB o almacén personal)
.\build-app.ps1 -SignThumbprint "A1B2C3...."

# o con un archivo .pfx
.\build-app.ps1 -SignPfx "C:\certs\soltum.pfx" -SignPfxPassword "..."
```

Requiere `signtool.exe`, que viene con el **Windows SDK** (componente *Windows SDK Signing Tools*).
El script lo busca solo y verifica que la firma quede en estado `Valid`, no solo que el comando no
falle. La firma lleva **sello de tiempo**: sin él, los .exe ya repartidos dejarían de estar firmados
el día que caduque el certificado.

Mientras no haya certificado, el aviso se puede saltar así (una sola vez por equipo):
**Más información → Ejecutar de todas formas**. Si el antivirus lo pone en cuarentena, hay que
agregar la carpeta como exclusión.

## ¿Se puede hacer un instalador que traiga también LibreOffice y la conexión?

**La conexión: ya viene.** Es justo lo que hace `build-app.ps1`: queda incrustada en el .exe. Ahí no
hace falta instalador.

Lo que **no** funcionaría es repartir un `dbprovider.json` ya hecho. Ese archivo se cifra con **DPAPI
a nombre del usuario de Windows que lo creó**: copiado a otra máquina —o al mismo equipo con otra
cuenta— no se puede descifrar. Por eso la conexión va incrustada en el binario y no en un archivo.

**LibreOffice: sí, pero conviene no meterlo dentro.** Se puede hacer un instalador con
[Inno Setup](https://jrsoftware.org/isinfo.php) (gratuito) que instale la aplicación y, si no
detecta LibreOffice, lance su instalador oficial:

```
[Files]
Source: "dist\desarrollador\Administrador_Desarrollo_Web.exe"; DestDir: "{app}"
Source: "redist\LibreOffice_25.2_Win_x86-64.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall; \
  Check: not LibreOfficeInstalado

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\LibreOffice_25.2_Win_x86-64.msi"" /qn"; \
  StatusMsg: "Instalando LibreOffice..."; Check: not LibreOfficeInstalado
```

con una función `LibreOfficeInstalado` que revise el registro o la ruta
`C:\Program Files\LibreOffice\program\soffice.exe`.

Tres cosas antes de decidirlo:

1. **Peso.** El .exe ya pesa ~150 MB por llevar el runtime de .NET; LibreOffice suma ~350 MB. El
   instalador quedaría cerca de **500 MB**.
2. **Licencia.** LibreOffice es MPL-2.0 y redistribuirlo es legal, pero hay que conservar los avisos
   de licencia.
3. **Firma.** El instalador también hay que firmarlo, y además SmartScreen ve un **instalador**, que
   levanta más sospecha que un .exe suelto. Sin firma, el instalador empeora el problema del punto
   anterior en vez de mejorarlo.

**Recomendación:** dejar el .exe único tal cual y usar el instalador solo si de verdad se quiere el
acceso directo en el menú Inicio y la desinstalación desde Windows. Para LibreOffice basta con
detectarlo al arrancar y avisar con un enlace de descarga: son 350 MB que la mayoría ya tiene.

## Actualizaciones

Hay **dos formas de repartir**, y las dos funcionan a la vez. La aplicación detecta sola en cuál
está y se comporta en consecuencia.

El detalle técnico que condiciona todo: **un .exe en ejecución no puede sobrescribirse a sí mismo**
en Windows. Por eso la copia portátil nunca podrá actualizarse sola, y por eso Velopack necesita
instalar en una carpeta con estructura propia.

En cualquiera de las dos, **sube la versión en `Administrador_Desarrollo_Web.csproj`** (`Version`,
`FileVersion`, `AssemblyVersion`) en cada entrega: si no, no hay nada que comparar y el aviso nunca
sale.

### A) Copia portátil (lo de siempre) — aviso manual

```powershell
.\build-app.ps1 -ConnectionString "..."
```

Sale un `.exe` único que se copia donde sea. **No se actualiza solo**, pero sí avisa: al entrar
compara su versión contra la publicada y, si hay una nueva, muestra las novedades y el enlace.

Se publica en **Configuración → Aviso de versión nueva**: última versión, enlace de descarga y
novedades. Orden correcto: subir la versión en el `.csproj` → publicar → subir el .exe → *recién
entonces* capturar la versión ahí. Al revés, todo el equipo ve un aviso que apunta a un archivo que
todavía no existe.

### B) Instalada con Velopack — actualización automática

```powershell
# Una sola vez por equipo de trabajo:
dotnet tool install -g vpk

# En cada entrega:
.\build-app.ps1 -ConnectionString "..." -Velopack -VelopackFeedDir "C:\ruta\al\feed"
```

Qué cambia: se publica en **carpeta** en vez de en un .exe único (`vpk` empaqueta una carpeta, y las
actualizaciones **delta** comparan archivo por archivo — con todo dentro de un solo .exe, cada
actualización bajaría los 150 MB completos y se perdería la única ventaja).

Qué sale en el feed:

- `AdministradorDesarrolloWeb-win-Setup.exe` — el instalador; es lo que reparte **la primera vez**.
- `...-full.nupkg` y `...-delta.nupkg` — los paquetes de actualización.
- `releases.win.json` — el índice que la aplicación consulta.

**Sube TODO el contenido de esa carpeta al mismo sitio** (contenedor de Blob o carpeta de red) y
captura esa ubicación en **Configuración → Feed de actualización automática**. El `-VelopackFeedDir`
debe apuntar a una copia local del feed **con las versiones anteriores dentro**: de ahí saca `vpk`
la base para generar el delta. Si apuntas a una carpeta vacía, la entrega sale completa (funciona,
pero pesa).

De ahí en adelante: cada quien recibe el aviso al entrar, pulsa **Actualizar ahora**, se descarga en
segundo plano y **se instala al salir de la aplicación**.

Aquí importa una precisión: «salir» significa **bandeja → Salir**, no cerrar la ventana con la X
(que solo la esconde). El updater de Velopack espera a que el proceso muera de verdad. Quien quiera
aplicarla en el momento pulsa **Reiniciar ahora**: la aplicación se cierra por su camino normal
—avisando si hay un despliegue en curso, cerrando la jornada y guardando los cronómetros— y vuelve
a abrirse ya actualizada.

### Lo que NO cambia con Velopack

**El aviso de SmartScreen.** Velopack automatiza la descarga, no la reputación: sin certificado de
firma, el instalador y **cada actualización** siguen disparando el aviso. Si vas a firmar, pásale el
certificado al script (`-SignThumbprint` o `-SignPfx`): en modo Velopack se le entrega también a
`vpk`, que firma el instalador y el actualizador además del ejecutable.

### Sobre la tabla AppRelease

No sirve para esto: lleva las versiones de los **sistemas web que esta herramienta despliega**
(cuelga de `AppSystem` y de `DeploymentJob`), no las de la herramienta misma. Por eso la versión
publicada y el feed viven en `AppSettings`, que además ya está blindada contra escritura para el
login restringido: los desarrolladores la leen, solo el administrador la escribe.

## Archivos

- `build-app.ps1` — genera el recurso cifrado, publica (portátil o Velopack), firma y empaqueta.
- `crear-login-desarrollador.sql` — crea `app_dev` con permisos mínimos.
- `devbuild.bin` — recurso temporal con credenciales. **Nunca versionarlo.**
