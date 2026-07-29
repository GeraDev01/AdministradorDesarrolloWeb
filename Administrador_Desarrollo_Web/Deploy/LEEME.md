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

1. **Configuración de este equipo** — lo capturado en Configuración → Base de datos (`dbprovider.json`).
2. **Conexión incrustada** — la que se metió al publicar.
3. **SQLite local** — si no hay ninguna de las anteriores.

Que la local gane es lo que permite repartir un único ejecutable: el administrador configura en su
máquina las credenciales completas y sobrescribe la incrustada, mientras el resto del equipo usa
tal cual la que viene dentro. Si alguien eligió SQLite a propósito, tampoco se le impone la
incrustada.

La app registra en el log cuál quedó en efecto (`BD inicializada [origen]: destino`).

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

## Archivos

- `build-app.ps1` — genera el recurso cifrado y publica el .exe único.
- `crear-login-desarrollador.sql` — crea `app_dev` con permisos mínimos.
- `devbuild.bin` — recurso temporal con credenciales. **Nunca versionarlo.**
