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

Copias ese `.exe` a la otra PC y listo. El menú y los permisos siguen dependiendo del rol de la cuenta con la que cada quien inicia sesión: es la misma aplicación para todos.

> **Córrelo con `powershell.exe`, no con `pwsh`.** El script necesita DPAPI (para leer tu conexión local) y `System.Data.SqlClient` (para probarla), que son del .NET Framework y PowerShell 7 no trae.
>
> Ese `System.Data.SqlClient` es el proveedor **viejo** y no entiende todas las palabras clave que escribe el nuevo (`Microsoft.Data.SqlClient`), que es el que usa la aplicación: por ejemplo `Trust Server Certificate` separado, que allá solo existe como `TrustServerCertificate`. El script **traduce** la cadena antes de abrirla para la prueba; lo que se incrusta en el `.exe` es siempre la cadena original. Si alguna vez ves *«Palabra clave no admitida»* en el paso *Probando la conexión*, es esto y no un problema de tu conexión.

## Seguridad — leer antes de repartir

El cifrado incrustado es **ofuscación, no seguridad**: la llave vive en el propio ejecutable, así que cualquiera con el `.exe` puede recuperar la cadena. Por eso **incrusta siempre un login de SQL restringido** (ver `Deploy\crear-login-desarrollador.sql`), nunca `sa` ni la cuenta con permisos completos. Esa, el administrador la captura a mano en su propio equipo (*Configuración → Base de datos*), que por precedencia gana sobre la incrustada.

## Actualizar la conexión incrustada

La cadena queda fija en el `.exe`. Si cambia (host, contraseña, base), vuelve a correr `build-app.ps1` y reparte el nuevo ejecutable. Un `.exe` con conexión vieja simplemente no conectará; el administrador siempre puede desbloquearse capturando la conexión en *Configuración → Base de datos* de ese equipo.
