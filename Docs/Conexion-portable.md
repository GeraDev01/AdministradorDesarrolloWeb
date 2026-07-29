# Llevar la app a otra PC sin volver a capturar la conexión

La aplicación decide su base de datos en este orden:

1. **Configuración local de ese equipo** (`%AppData%\AdministradorDesarrolloWeb\dbprovider.json`), capturada en *Configuración → Base de datos*.
2. **Conexión incrustada** en el ejecutable (si se publicó con ella).
3. **SQLite local** (`%AppData%\AdministradorDesarrolloWeb\app.db`), como último recurso.

## Por qué copiar `dbprovider.json` NO funciona

Ese archivo cifra la cadena con **DPAPI atado a la cuenta de Windows** que la guardó. En otra PC (u otra cuenta) no se puede descifrar: la app lo detecta, avisa y cae a SQLite con una base vacía. Es decir, **el archivo de configuración no es portable a propósito**.

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

## Seguridad — leer antes de repartir

El cifrado incrustado es **ofuscación, no seguridad**: la llave vive en el propio ejecutable, así que cualquiera con el `.exe` puede recuperar la cadena. Por eso **incrusta siempre un login de SQL restringido** (ver `Deploy\crear-login-desarrollador.sql`), nunca `sa` ni la cuenta con permisos completos. Esa, el administrador la captura a mano en su propio equipo (*Configuración → Base de datos*), que por precedencia gana sobre la incrustada.

## Actualizar la conexión incrustada

La cadena queda fija en el `.exe`. Si cambia (host, contraseña, base), vuelve a correr `build-app.ps1` y reparte el nuevo ejecutable. Un `.exe` con conexión vieja simplemente no conectará; el administrador siempre puede desbloquearse capturando la conexión en *Configuración → Base de datos* de ese equipo.
