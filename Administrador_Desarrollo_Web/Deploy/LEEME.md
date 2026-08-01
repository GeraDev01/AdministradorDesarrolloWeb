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
están cifrados con DPAPI del usuario que los capturó, así que otra persona solo ve texto cifrado.

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

## ¿Cómo se manejan las actualizaciones?

Hoy: **sí, hay que volver a descargar el .exe y reemplazarlo.** No se pierde nada — la configuración
vive en `%APPDATA%\AdministradorDesarrolloWeb\` y los datos en la base, no dentro del ejecutable.

Un detalle técnico que condiciona todo lo demás: **un .exe en ejecución no se puede sobrescribir a sí
mismo** en Windows. Cualquier actualización automática necesita un segundo proceso que espere a que
la aplicación cierre, reemplace el archivo y la vuelva a abrir.

Tres caminos, de menor a mayor esfuerzo:

**1. Aviso dentro de la aplicación (lo más rentable aquí).** Al iniciar sesión, comparar la versión
del ejecutable contra la última publicada y, si hay una nueva, mostrar un aviso con el enlace de
descarga y las novedades. Sigue siendo descarga manual, pero nadie se queda meses atrás sin
enterarse.

La subida a Azure Blob ya está resuelta y se reutiliza tal cual. Lo que **no** sirve es la tabla
`AppRelease`: esa lleva las versiones de los **sistemas web que esta herramienta despliega**
(cuelga de `AppSystem` y de `DeploymentJob`), no las de la herramienta misma. Haría falta una tabla
aparte —o simplemente un `AppSetting` con la última versión y su URL, que para un solo producto
alcanza.

**2. Actualización automática con [Velopack](https://velopack.io/)** (gratis, .NET, sucesor de
Squirrel). Descarga la nueva versión en segundo plano, la aplica al cerrar y resuelve solo el
problema del ejecutable en uso. Requiere cambiar el empaquetado: Velopack usa su propio formato en
vez del .exe único.

**3. MSIX + App Installer.** Es lo que recomienda Microsoft y Windows actualiza solo. Pero exige
firma obligatoria (sin certificado ni se instala), un cambio de empaquetado completo y un servidor
donde publicar el `.appinstaller`. Solo tiene sentido si ya se compró el certificado.

**Recomendación:** empezar por el punto 1. Si el equipo crece o las entregas se vuelven frecuentes,
pasar a Velopack — pero solo después de tener el certificado, porque una actualización automática que
dispara SmartScreen en cada versión es peor que no tenerla.

En cualquier caso, **subir la versión en `Administrador_Desarrollo_Web.csproj`** (`Version`,
`FileVersion`, `AssemblyVersion`) en cada entrega: si no, no hay nada que comparar.

## Archivos

- `build-app.ps1` — genera el recurso cifrado, publica el .exe único y opcionalmente lo firma.
- `crear-login-desarrollador.sql` — crea `app_dev` con permisos mínimos.
- `devbuild.bin` — recurso temporal con credenciales. **Nunca versionarlo.**
