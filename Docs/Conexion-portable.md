# Llevar la app a otra PC sin volver a capturar la conexión

La aplicación decide su base de datos en este orden:

1. **Configuración local de ese equipo** (`%AppData%\AdministradorDesarrolloWeb\dbprovider.json`), capturada en *Configuración → Base de datos*.
2. **Conexión incrustada** en el ejecutable (si se publicó con ella).

No hay tercera opción: **sin ninguna de las dos, la app no abre ninguna base**. La pantalla de inicio de sesión muestra un ● **rojo** con el motivo y no deja entrar. Cuando conecta, el ● se pone **verde**.

El indicador solo dice si hay conexión o no: esa pantalla se ve antes de autenticarse, así que no muestra servidor, base ni usuario.

## Cómo saber contra qué base está una PC

En el log de ese equipo:

```powershell
Get-Content "$env:APPDATA\AdministradorDesarrolloWeb\logs\app-*.log" | Select-String "BD inicializada"
```

## Por qué copiar `dbprovider.json` NO funciona

Ese archivo cifra la cadena con **DPAPI atado a la cuenta de Windows** que la guardó. En otra PC (u otra cuenta) no se puede descifrar: la app lo detecta y avisa. Es decir, **el archivo de configuración no es portable a propósito**. Antes, además, seguía adelante con una base SQLite vacía en la que nadie podía iniciar sesión; ahora se queda en rojo diciendo qué pasó.

## La forma correcta: publicar con la conexión incrustada

Se genera **un solo `.exe` autocontenido** que ya trae la conexión. Se copia a cualquier PC y conecta solo, sin capturar nada.

```powershell
# Desde la carpeta del proyecto:
cd Administrador_Desarrollo_Web\Deploy

# Opción A: usa la conexión que ya tienes configurada en este equipo
.\build-app.ps1

# Opción B (recomendada): pasa un login de SQL restringido
.\build-app.ps1 -ConnectionString "Server=tcp:...;Database=SOLTUM_DEV_WD;User Id=app_dev;Password=****;Encrypt=True"
```

El script:

- prueba la conexión antes de publicar (sáltalo con `-SkipConnectionTest`),
- cifra la cadena en `Deploy\devbuild.bin` (AES-256-CBC) y la incrusta al compilar,
- **borra `devbuild.bin` al terminar** para que las credenciales no queden en el repositorio,
- deja el ejecutable en `dist\desarrollador\` (`-OutputDir` para cambiarlo).

**Solo se incrusta la conexión a la base.** Lo demás —Blob Storage y servidores FTP— vive en esa base y lo lee cualquier ejecutable (ver la sección siguiente), así que meterlo en el `.exe` sería repartir las contraseñas de producción a cambio de nada.

Copias ese `.exe` a la otra PC y listo. El menú y los permisos siguen dependiendo del rol de la cuenta con la que cada quien inicia sesión: es la misma aplicación para todos.

> **Córrelo con `powershell.exe`, no con `pwsh`.** El script necesita DPAPI (para leer tu conexión local) y `System.Data.SqlClient` (para probarla), que son del .NET Framework y PowerShell 7 no trae.
>
> Ese `System.Data.SqlClient` es el proveedor **viejo** y no entiende todas las palabras clave que escribe el nuevo (`Microsoft.Data.SqlClient`), que es el que usa la aplicación: por ejemplo `Trust Server Certificate` separado, que allá solo existe como `TrustServerCertificate`. El script **traduce** la cadena antes de abrirla para la prueba; lo que se incrusta en el `.exe` es siempre la cadena original. Si alguna vez ves *«Palabra clave no admitida»* en el paso *Probando la conexión*, es esto y no un problema de tu conexión.

## Blob Storage y servidores FTP: por qué ya no hay que recapturarlos

Antes, desplegar desde otra PC fallaba así:

```
⚠ servidor FTPS …: intento 1/6 falló (Code: 530 Message: User cannot log in.)
```

Y el Blob aparecía como «no configurado» aunque estuviera capturado desde hacía meses.

**El motivo era el cifrado, no las credenciales.** Los secretos que viven en la base compartida —contraseñas FTP, connection string del Blob, PAT de la organización, contraseña del correo— se guardaban con **DPAPI, que ata el cifrado a la cuenta de Windows que los escribió**. El texto cifrado viajaba en la base, pero solo esa PC podía descifrarlo. Con las contraseñas FTP el síntoma era engañoso: la aplicación mandaba el texto cifrado ilegible *como si fuera la contraseña*, y el que contestaba «530» era el servidor.

Hoy esos valores se cifran con una **llave común de la aplicación**, así que:

- lo que captura una PC lo lee cualquier otra, sin recapturar nada;
- **un servidor nuevo dado de alta desde cualquier ejecutable lo usan todos los demás enseguida**, sin republicar. Esto es lo que hace que Operaciones pueda trabajar desde cualquier equipo;
- recapturar el Blob en una PC ya no se lo rompe a quien lo había capturado antes.

### Qué pasa con lo que ya estaba guardado

La aplicación **lo migra sola al abrirla**, pero solo puede convertir lo que ese equipo alcanza a descifrar. En la práctica: abre la aplicación **en la PC que capturó cada valor** y con eso queda. Lo que ninguna PC pueda leer hay que capturarlo una vez más, y ya.

Lo que no se pueda leer **no se borra ni se sobrescribe** — la única copia sigue ahí para la PC que sí pueda. Para saber qué falta:

```powershell
Get-Content "$env:APPDATA\AdministradorDesarrolloWeb\logs\app-*.log" | Select-String "no puede descifrar"
```

Y si intentas desplegar contra un servidor que quedó pendiente, ya no verás un «530»: el despliegue se detiene diciendo el nombre del servidor y qué hacer.

### Por qué no se incrustan en el `.exe`

Se pensó, y se descartó: no aporta nada y sí quita. Una vez que las credenciales viajan por la base, incrustarlas solo serviría para el arranque contra una base **vacía** —algo que pasa una vez en la vida del sistema— y a cambio cada ejecutable repartido llevaría dentro las contraseñas de todos los servidores de producción. El cifrado del `.exe` es ofuscación, así que eso equivale a repartir la lista en claro.

Lo único que se incrusta es la conexión a la base, y por un motivo distinto: esa no se puede leer *de* la base.

## Seguridad — leer antes de repartir

El cifrado incrustado es **ofuscación, no seguridad**: la llave vive en el propio ejecutable, así que cualquiera con el `.exe` puede recuperar la cadena. Por eso **incrusta siempre un login de SQL restringido** (ver `Deploy\crear-login-desarrollador.sql`), nunca `sa` ni la cuenta con permisos completos. Esa, el administrador la captura a mano en su propio equipo (*Configuración → Base de datos*), que por precedencia gana sobre la incrustada.

El cifrado de los secretos **dentro de la base** es igual de ofuscación: quien tenga el ejecutable *y* acceso de lectura a la base puede recuperarlos. Lo que de verdad los protege es el acceso a la base y a la red, no el cifrado. Ese es el precio de que las credenciales sean compartidas entre equipos — sin eso no hay forma de que Operaciones despliegue desde cualquier PC—, y es justo la razón de que el login de los desarrolladores sea restringido (`Deploy\crear-login-desarrollador.sql`).

Un secreto que deba ser **personal** no va en la base: va en el equipo de cada quien, cifrado con su cuenta de Windows. Es el caso del PAT de DevOps (*Mis SLA → 🔑 Mi PAT*), donde el aislamiento sí es el objetivo.

## Actualizar la conexión incrustada

La cadena de la base queda fija en el `.exe`. Si cambia (host, contraseña, base), vuelve a correr `build-app.ps1` y reparte el nuevo ejecutable. Un `.exe` con conexión vieja simplemente no conectará; el administrador siempre puede desbloquearse capturando la conexión en *Configuración → Base de datos* de ese equipo.

El Blob y los servidores FTP **nunca** necesitan republicación: viven en la base y se comparten solos.
