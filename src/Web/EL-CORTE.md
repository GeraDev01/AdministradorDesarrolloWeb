# El corte: pasar de la aplicación de escritorio a la web

> **Este documento describe una operación contra la BASE DE DATOS REAL.** Nada de lo que hay aquí se
> ejecuta solo ni lo ejecuta una herramienta automática: lo hace una persona, con el equipo avisado y
> con la marcha atrás preparada. Léelo entero antes de empezar.

## Por qué un corte único y no una convivencia

Durante todo el desarrollo, la web trabajó contra una base de **ensayo** y el escritorio siguió siendo
la única aplicación de producción. Esa decisión se tomó al principio y es la que evita de raíz los
tres problemas que habrían aparecido si las dos aplicaciones hubieran escrito producción a la vez:

- **Dos migradores.** Las dos ponen el esquema al día al arrancar. Corriendo a la vez sobre la misma
  base, el orden de los parches deja de estar determinado.
- **Dos juegos de trabajos de fondo.** Los temporizadores del escritorio y los trabajos de la web
  hacen lo mismo: escalar SLA, cerrar jornadas caídas, sincronizar tickets. Encendidos a la vez,
  todo llega por duplicado.
- **Concurrencia entre aplicaciones.** Solo seis entidades traen `RowVersion`; el resto se pisaría en
  silencio.

Por eso los trabajos de fondo de la web están **apagados por omisión**
(`AdminWeb:TrabajosDeFondoActivos`) y solo se encienden en el paso 6 de abajo.

## Antes del corte: el ensayo

**No se hace el corte sin haberlo ensayado antes.** El ensayo es el que dice cuánto dura la ventana
de mantenimiento; sin él se está adivinando.

1. Sacar una **copia fresca** de la base de producción (Azure SQL: restauración a un punto en el
   tiempo, a una base nueva).
2. Apuntar una instancia de la web a esa copia y **arrancarla**. El migrador corre al arrancar, bajo
   `sp_getapplock`, y deja el esquema al día.
3. **Medir cuánto tardó** ese arranque. Esa es la ventana de mantenimiento, más margen.
4. Recorrer con la web los datos reales: entrar con varias cuentas de rol distinto, abrir cada
   módulo, generar un documento, publicar en el foro, marcar una jornada.
5. Comprobar que las **integraciones responden** con las credenciales de verdad: Azure DevOps,
   Freshdesk, correo, Blob, FTP.
6. Tirar la copia.

Si algo falla en el ensayo, se arregla y se vuelve a ensayar. El ensayo es barato; el corte no.

## El día del corte

### 1. Avisar y cerrar el escritorio

El equipo tiene que estar fuera de la aplicación de escritorio. Mientras alguien la tenga abierta,
sus temporizadores siguen escribiendo.

### 2. Respaldo inmediato antes de tocar nada

Azure SQL tiene respaldo continuo, pero conviene un punto explícito y anotado: es lo que se va a
restaurar si hay que volver atrás, y quererlo buscar con prisa es la peor forma de encontrarlo.

**Anota la hora exacta.**

### 3. Apuntar la web a producción y arrancarla

La cadena de conexión vive en Key Vault; se cambia allí, no en el código. Al arrancar, la API aplica
sus migraciones pendientes **de una vez y bajo `sp_getapplock`**, de modo que dos instancias no puedan
migrar a la vez.

Lo que la web añade al esquema —todo aditivo, nada se renombra ni se borra:

| Qué | Para qué |
|---|---|
| `Users.SecurityStamp` | Poder cerrar la sesión de alguien de verdad al cambiarle la contraseña o desactivarlo |
| `UserSecrets` | El PAT de Azure DevOps por persona, cifrado en el servidor (antes era un archivo DPAPI por máquina) |
| `UserPreferences` | El ancho y el orden de las columnas (antes, un `columnas.json` por máquina) |
| `PushSubscriptions` | A qué navegador entregar un aviso con la pestaña cerrada |
| `WorkSessions.LastHeartbeatUtc` | Consolidar el cronómetro hasta el último latido cuando se cierra la pestaña |
| `RowVersion` en seis entidades | Que dos ediciones simultáneas den un 409 en vez de pisarse |

### 4. Comprobar que arrancó

`GET /api/health` responde 200 solo si la base contesta. Si no responde, **no se sigue**: se
diagnostica o se vuelve atrás (paso 8).

### 5. Recapturar los secretos que no se puedan leer

Los secretos de configuración cifrados con el esquema viejo de Windows (DPAPI) **no se pueden leer
desde el servidor**: dependían de la máquina que los guardó. La pantalla de configuración los marca
como «hay que volver a capturarlo» en lugar de enseñarlos como vacíos. Recaptúralos ahí mismo.

### 6. Encender los trabajos de fondo

Poner `AdminWeb:TrabajosDeFondoActivos = true` y reiniciar. **Este es el paso que no tiene sentido
hacer antes**: hasta aquí, los temporizadores del escritorio hacían ese trabajo.

### 7. Que entre el equipo

Y quedarse mirando la primera hora: los avisos, las jornadas que se abren, los cronómetros.

### 8. Si hay que volver atrás

Mientras no se haya hecho el paso 9, la marcha atrás existe y es sencilla: **el escritorio vuelve a
abrirse y sigue funcionando**. Los cambios de esquema son aditivos —columnas y tablas nuevas que el
escritorio ignora—, así que no le estorban. Si además hiciera falta descartar datos escritos desde la
web, se restaura el respaldo del paso 2.

Por eso el paso 9 va después de estabilizar, y no el mismo día.

### 9. Cuando ya no haya vuelta atrás (días después, no horas)

- **Revocar el acceso a la base del cliente de escritorio.** Con eso el `.exe` viejo queda inerte
  aunque alguien lo conserve en su equipo, que es lo que de verdad cierra la puerta.
- Archivar el instalador y su certificado de firma.
- Migrar los secretos de configuración del cifrado compartido a Data Protection con las llaves en
  Key Vault. Hasta ahora la web escribía con el esquema del escritorio **a propósito**, para que las
  dos se entendieran (ver `ProtectorPortable`); retirado el escritorio, esa restricción desaparece y
  conviene pasar a protección de verdad.
- Quitar de este repositorio las dos pruebas que comprueban que la semilla de cifrado coincide con la
  del escritorio: dejan de aplicar.

## Lo que NO se hace nunca

- **Ejecutar SQL a mano contra la base de producción.** El esquema lo pone al día el migrador al
  arrancar la aplicación. `SET PARSEONLY ON` **no valida sin ejecutar** como suele creerse.
- **Encender los trabajos de fondo con el escritorio todavía en uso.**
- **Regenerar las llaves VAPID de los avisos push** después de que la gente se haya suscrito: todas
  las suscripciones dejarían de valer de golpe.
