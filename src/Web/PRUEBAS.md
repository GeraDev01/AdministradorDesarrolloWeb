# Cómo se prueba esto

> Hay **cuatro redes** y cada una atrapa una familia distinta de fallos. Las dos primeras corren en el
> pipeline en cada cambio; las dos últimas se lanzan a mano. Ninguna sustituye a otra, y lo que sigue
> explica exactamente **qué se le escapa a cada una**, que es lo que de verdad hay que saber.

| Red | Cómo se lanza | Dónde corre | Qué atrapa |
|---|---|---|---|
| `tests/AdminWeb.Application.Tests` | `dotnet test` | SQLite, en archivo temporal | Las reglas dentro de los servicios |
| `tests/AdminWeb.Api.Tests` | `dotnet test` | SQLite + la API en memoria | La tubería: políticas, cookie, middleware, filtro de excepciones |
| [humo.ps1](humo.ps1) | a mano | SQLite, aplicación arrancada de verdad | El arranque: migrador, siembra, servicios registrados, flujo completo |
| [humo-docker.ps1](humo-docker.ps1) | Docker | **SQL Server sobre Linux** | La rama de SQL Server del migrador y la concurrencia con `RowVersion` |

```powershell
cd src\Web
dotnet build AdminWeb.Api\AdminWeb.Api.csproj -v q --nologo
dotnet test  tests\AdminWeb.Application.Tests\AdminWeb.Application.Tests.csproj -v q --nologo
dotnet test  tests\AdminWeb.Api.Tests\AdminWeb.Api.Tests.csproj -v q --nologo
.\humo.ps1
```

> **No se citan aquí los conteos de pruebas.** Cambian con cada cosa que se añade, y un número escrito
> en un documento solo sirve para que alguien lo compare y se preocupe sin motivo. El número que vale
> es el que imprime `dotnet test` ahora mismo, y lo único que importa de él es que **no haya ninguna
> en rojo**.

---

## Suite 1 — las reglas: `AdminWeb.Application.Tests`

Es la **red que viajó con el port**. Los servicios se copiaron del escritorio, y estas pruebas son lo
que comprueba que siguen decidiendo lo mismo después de pasarlos a `async` y a un contexto por
petición.

Un centenar largo de archivos, uno por servicio o por regla. Lo que cubren, en tres familias:

- **Reglas de negocio**: quién puede tomar una actividad del pool, cuándo caduca un día de
  vacaciones, cómo se reparte un SLA, qué pasa al marcar dos entradas el mismo día.
- **Reglas de ley**, duplicadas a propósito: `LftVacacionesTests` y `SaldoDeVacacionesTests`
  comprueban las dos tablas año por año. Están duplicadas porque el escritorio tiene la suya y las
  dos aplicaciones tenían que dar el mismo número; si alguien toca una copia, la otra suite lo dice.
- **Garantías que no son de negocio pero se pueden romper sin darse cuenta**:
  `ForoSeguridadTests` (que un cuerpo escrito por alguien nunca se convierte en marcado),
  `EtiquetasDelServidorSinEmojiTests` (que ningún texto del servidor lleve un carácter que dibuje el
  sistema operativo), `ProtectorPortableTests` (que la semilla de cifrado siga coincidiendo byte por
  byte con la del escritorio) y las **pruebas de migración**, que tienen su apartado abajo.

### Cómo se arma una prueba

Todo el andamiaje está en
[TestSupport.cs](tests/AdminWeb.Application.Tests/TestSupport.cs):

- `TestDb.New()` — una base SQLite nueva en un archivo temporal, creada con `EnsureCreated()`.
- `UsuarioDePrueba.Como(rol, developerId)` — una identidad, porque los servicios dependen de
  `ICurrentUser` y no de una implementación concreta. Es justo lo que hizo portable el código.
- `Fabrica.…` — los servicios que necesitan cuatro piezas ya montadas.
- `BlobsSinConfigurar`, `DescargaSinRed` — dobles que **lanzan** en vez de devolver algo inventado.
  Si una prueba acabara pisando ese camino, conviene que se entere en el momento y no que pase en
  verde creyendo que probó algo.

`TestDb` además **barre las bases de tandas anteriores** al empezar. No es higiene opcional: son unos
1500 archivos y cerca de 400 MB por ejecución completa, y cuando el disco se llena la suite no falla
donde está el problema — fallan cientos de pruebas sin relación entre sí con
`SQLite Error 13: database or disk is full`, que señala a cualquier sitio menos al verdadero.

---

## Suite 2 — la tubería: `AdminWeb.Api.Tests`

**Existe porque un servicio puede estar impecable y la API responder mal.** Estas cosas compilan sin
una queja y pasan la suite de servicios en verde:

- una política de autorización mal escrita,
- **un servicio sin registrar** en el contenedor,
- el middleware en el orden equivocado,
- una cookie que no viaja,
- un rechazo de negocio que sale como 500 en vez de como 400.

La API se levanta **entera y en memoria** con `WebApplicationFactory`, contra su propia base SQLite
temporal. El andamiaje está en [ApiDePrueba.cs](tests/AdminWeb.Api.Tests/ApiDePrueba.cs), y hay tres
detalles suyos que conviene conocer antes de escribir una prueba nueva:

1. **No hay atajos de configuración.** La cadena de conexión y el proveedor se fijan por
   configuración, que es la misma vía que en producción. Así se prueba el arranque real —migrador y
   siembra incluidos— sin ocultar precisamente los fallos que estas pruebas existen para atrapar.
2. **Los trabajos de fondo se quedan apagados**, igual que en producción hasta el corte. Encenderlos
   haría que las pruebas compitieran contra un barrido que consolida cronómetros por debajo, y los
   fallos saldrían un día sí y otro no.
3. **Cada cuenta pasa por su «primer día» de verdad**: entra con la contraseña temporal, la cambia y
   da de alta el segundo factor por las rutas reales. No es comodidad — hasta que eso ocurre, la API
   responde 403 a todo lo demás, y cada prueba empezaría tropezando con eso en vez de probar lo suyo.

> **La parte delicada es el reloj, y explica una rareza del andamiaje.** Un código de seis dígitos
> vale para una ventana de treinta segundos y **no se puede repetir**: el servidor apunta la última
> ventana aceptada justamente para que nadie reutilice un código visto. Consecuencia: justo después
> de confirmar el alta, el siguiente acceso caería dentro de la misma ventana y sería rechazado con
> toda la razón. Por eso se entra **una vez con un código de rescate** —que no dependen del reloj—
> marcando «recuerda este equipo», y de ahí en adelante ese frasco de cookies ya no necesita ningún
> código. De ahí también que haya un frasco **por cuenta** y no uno por cliente.

### Los recorridos guiados

`RecorridosGuiadosTests` vive en esta suite y no en la otra por un motivo concreto: **es el único de
los dos proyectos que ve `AdminWeb.Client`**, porque la API sirve el cliente y por tanto lo
referencia.

Lo que comprueba: cada paso de cada uno de los recorridos apunta a un control por una marca
`data-recorrido="…"`, y esas marcas tienen que **seguir existiendo en el marcado de su pantalla**.

El fallo que evita no se ve venir. Alguien reordena una pantalla y quita un botón; el recorrido de
esa pantalla, escrito hace meses por otra persona, se queda apuntando al vacío. En ejecución el paso
se salta **en silencio** —así está hecho a propósito, para que a nadie se le corte el recorrido
porque su rol no ve un control—, o sea que nadie se entera hasta que alguien lo lanza para aprender
la pantalla y le explican tres cosas de cinco.

**Si una de estas pruebas se pone roja, no se ajusta la prueba.** El mensaje dice qué paso, de qué
recorrido y qué marca falta. Se arregla poniendo la marca en el control que el paso describe, o
quitando el paso del guion.

`RecorridosCoberturaTests` cubre el hueco que la anterior no puede ver, y son dos fallos distintos:
aquélla comprueba que los recorridos escritos **son correctos**, ésta que están escritos **todos**.
Una pantalla sin recorrido no deja ningún rastro —se ve igual que una cuyo guion nadie ha escrito
todavía: el botón de la barra, apagado—, así que sin esta comprobación la pantalla número sesenta y
cuatro se queda sin ayuda y nadie se entera.

La exención **no es una lista escrita a mano**, que envejecería igual de mal que lo que protege. Se
deduce del marcado: está exenta la pantalla cuyo armazón no pinta el botón que lanza los recorridos,
porque en ella el recorrido sería inalcanzable. Hoy son las dos que van con `LayoutVacio` —acceso y
segundo factor, que no tienen barra superior—; el día que ese armazón cambie, la prueba pedirá sus
recorridos sola.

Ahí mismo va el cinturón contra el **falso verde**: las comprobaciones de `RecorridosGuiadosTests`
son todas de la forma «recorre los recorridos y no encuentres problemas», y sobre una lista vacía
pasan todas. Si el descubrimiento por reflexión dejara de encontrar los guiones, la suite se pondría
verde anunciando que están perfectos justo el día en que no queda ninguno.

---

## Por qué las pruebas corren en SQLite

Por dos motivos, y los dos siguen siendo buenos:

1. **Es el mismo andamiaje que ya usaban las ~1000 pruebas del escritorio**, así que se pudieron
   portar tal cual.
2. **Es un motor de verdad, no un doble.** Las consultas se traducen, las restricciones se aplican,
   las transacciones existen. Es infinitamente más fiel que un repositorio en memoria, y no cuesta
   nada: un archivo temporal por prueba.

**Y no hace falta ninguna base real para nada.** Eso es una regla, no una comodidad.

### Lo que SQLite NO puede ver

Cuatro huecos. El primero ya costó caro y es el que hay que tener siempre presente.

#### 1. Una columna sin parche pasa en verde

**`EnsureCreated()` crea el esquema a partir del modelo. La base de una prueba nace siempre con todas
las columnas.** Así que si añades una propiedad a una entidad y **olvidas su parche en el migrador**,
la suite entera pasa y la columna sencillamente no existirá en la base real — que lleva años de datos
y donde `EnsureCreated` no altera nada.

**El ejemplo real:** `Developers.TeamFunction`. Toda la suite en verde y la columna sin crear. Y EF la
pide en **cada** consulta de `Developers`, así que no se habría roto el organigrama: se habría roto
todo lo que lee una persona.

**Cómo se cubre:** con pruebas que **simulan la base vieja**. Se crea la base con el modelo de hoy, se
le hace `DROP COLUMN` de lo que se acaba de añadir, se corre `DatabaseMigrator.EnsureUpToDate` y se
comprueba que la columna volvió **y que no devolvió ninguna sentencia fallida**. El patrón está en:

- [FuncionDeEquipoMigracionTests.cs](tests/AdminWeb.Application.Tests/FuncionDeEquipoMigracionTests.cs) — una columna
- [MigracionDelSegundoFactorTests.cs](tests/AdminWeb.Application.Tests/MigracionDelSegundoFactorTests.cs) — tres columnas y dos tablas
- [ConocimientoMigracionTests.cs](tests/AdminWeb.Application.Tests/ConocimientoMigracionTests.cs), [MigracionDePlazosAHorasTests.cs](tests/AdminWeb.Application.Tests/MigracionDePlazosAHorasTests.cs)

La receta completa para añadir una columna está en
[MODELO-DE-DATOS.md](MODELO-DE-DATOS.md#cómo-se-cambia-el-esquema).

#### 2. La rama de SQL Server del migrador no la ejecuta nadie

Esas pruebas ejercitan **el dialecto de SQLite**. La rama de SQL Server escribe las mismas columnas
con los tipos de aquel motor, y hasta que apareció `humo-docker.ps1` **no la había corrido nunca
nadie**. La primera vez que se ejecutó, **la aplicación no arrancaba**. Tres defectos, los tres
condenados a saltar el día del corte:

- `sp_getapplock` se llamaba de una forma que EF rechaza por *non-composable SQL*.
- El candado se pedía con `LockOwner = 'Session'` sin fijar la conexión: se pedía en una, la migración
  corría en otra —sin protección— y el `release` en una tercera. **Compilaba, no daba error y no
  protegía nada.**
- Se intentaba candar una base que aún no existía: error 4060 en un entorno nuevo.

**Por eso los parches se escriben en los dos dialectos, uno al lado del otro, traducidos y no
copiados.** Comillas dobles y `TEXT` en SQLite; corchetes, `IF COL_LENGTH` y `nvarchar` en T-SQL.
Copiar una sentencia de una rama a la otra ya ha costado dos veces.

#### 3. `RowVersion` no existe en SQLite

El modelo lo **ignora** ahí. Ninguna prueba en SQLite puede comprobar que dos ediciones simultáneas
dan un 409 en vez de pisarse. Eso solo lo prueba `humo-docker.ps1`, que edita con un sello obsoleto y
exige el 409.

#### 4. Los decimales son texto

EF guarda `decimal` como `TEXT` en SQLite. Una consulta que ordene o compare por esas columnas dentro
de un LINQ traducido **daría resultados distintos en los dos motores**, y en SQLite parecería
correcta. Ver [MODELO-DE-DATOS.md](MODELO-DE-DATOS.md#los-decimales-en-sqlite-son-texto).

---

## Red 3 — la prueba de humo

```powershell
cd src\Web
dotnet build AdminWeb.Api\AdminWeb.Api.csproj
.\humo.ps1
```

Levanta la API **de verdad** contra una base SQLite temporal, la recorre entera y la borra al
terminar. **No toca ninguna base real.** Está escrita para Windows PowerShell 5.1.

Lo que hace, en orden:

1. **Arranca la aplicación.** Aquí ya se prueba el migrador, la siembra del administrador inicial y
   la siembra de catálogos.
2. **El primer día completo**: entra con la contraseña temporal, la cambia, da de alta el segundo
   factor y comprueba que salgan **ocho** códigos de rescate.
3. **Más de sesenta rutas de consulta**, una por pantalla, esperando 200 en todas. Incluye las
   integraciones **sin credenciales configuradas**, que tienen que responder igual con su estado en
   «no configurado»: una integración apagada no puede tumbar una pantalla.
4. **Escritura de verdad**: marcar entrada, marcar entrada dos veces (y que el rechazo llegue **con
   su motivo**), marcar salida, una cuenta sin ficha pidiendo algo que necesita ficha (400 explicado,
   no un 500), un adjunto inexistente (404).
5. **El testigo antiforgery, en los dos sentidos**: publicar sin él tiene que dar 400, y con él 200.
   Las dos formas de romperlo son silenciosas — si el testigo dejara de adjuntarse, publicar fallaría
   sin que nada avise; si la protección se desactivara «para que funcione», tampoco fallaría nada.
6. **Que el cuerpo del foro vuelva como texto**, con el intento de ataque intacto y solo la URL
   legítima convertida en enlace.
7. **Que cerrar sesión la invalide de verdad**, no solo borre la cookie.
8. **Que entre los dos tramos del acceso no quede sesión**: acertar la contraseña tiene que dejar la
   API contestando 401 a todo. Es el único fallo de ese diseño que **no se nota usando la
   aplicación**, porque con la puerta abierta todo funcionaría exactamente igual de bien.

**Lo que solo atrapa esto:** un endpoint cuyo servicio no está registrado. La aplicación revienta al
arrancar con `Body was inferred but the method does not allow inferred body parameters`, y ni el
compilador ni las pruebas de servicios lo ven.

---

## Red 4 — contra SQL Server, en Linux

```powershell
# desde la raíz del repositorio
docker compose -f src\Web\docker-compose.yml up -d --build
.\src\Web\humo-docker.ps1
docker compose -f src\Web\docker-compose.yml down -v     # borra también la base
```

**Esto no es comodidad: es la única forma de probar dos caminos que ninguna otra red alcanza** —la
rama de SQL Server del migrador y la concurrencia con `RowVersion`—, y además es lo único que corre
sobre **Linux**, que es donde va a estar en producción. Ya apareció un defecto de esa familia
(`Path.GetFileName` no corta `C:\ruta\foto.png` en Linux) y no será el último.

La base es un contenedor **vacío y desechable**. El pipeline lo corre en cada cambio, así que ya no
depende de que alguien se acuerde.

Además de todo lo de `humo.ps1`, este guion comprueba:

- **El conflicto de concurrencia de verdad**: crea un requerimiento, lo edita, y vuelve a editarlo con
  el sello viejo esperando **409**.
- El alta y la baja de los catálogos, **que la clave de licencia no viaje con la rejilla** y que
  editar sin mandarla la conserve.
- Que el catálogo inicial de plantillas se sembró y que la plantilla de fábrica se descarga como
  `.docx` de verdad.
- Que `/api/version` responda **sin sesión** y que el cliente Blazor se sirva desde el contenedor.

Y arranca con **un equipo de demostración sembrado** —cuatro desarrolladores, dos equipos,
requerimientos, pool, vacaciones, despliegues, foro y bitácora—. Los datos de demostración **no pueden
aparecer en producción**: hacen falta tres condiciones a la vez y están explicadas en la
[guía de puesta en marcha](GUIA-DE-PUESTA-EN-MARCHA.md#02-levantarla-en-tu-equipo-y-jugar-con-ella).

---

## El pipeline

[.github/workflows/web.yml](../../.github/workflows/web.yml) corre en toda rama y en cada pull
request, **en Linux a propósito** aunque se desarrolle en Windows: es donde corre App Service y es lo
que atrapa los fallos que solo se ven ahí. Compila, corre las dos suites, publica a la **ranura de
ensayo**, comprueba `/api/health` y **solo entonces** intercambia con producción.

Un arranque fallido detiene el intercambio y producción sigue corriendo la versión anterior. Eso es
lo que permite que el llavero mal configurado se niegue a arrancar sin dejar a nadie fuera; ver
[DECISIONES.md](DECISIONES.md#el-llavero-va-cifrado-con-un-certificado-y-si-falta-la-aplicación-no-arranca).

---

## Qué probar según lo que toques

| Si tocas… | Además de la suite de servicios, corre… | Y añade… |
|---|---|---|
| Una regla dentro de un servicio | — | Su prueba en `Application.Tests` |
| Una **columna o tabla nueva** | `humo-docker.ps1` | **Una prueba de migración** que quite la pieza y compruebe que el migrador la repone |
| Un **endpoint nuevo** | `humo.ps1` | Registrar el servicio en `Program.cs` |
| Una **política o el orden del middleware** | `humo.ps1` | Su prueba en `Api.Tests` |
| Algo de **concurrencia** o `RowVersion` | `humo-docker.ps1` | — |
| Una **pantalla** (mover o quitar controles) | `dotnet test` de `Api.Tests` | Las marcas `data-recorrido` que el guion espera |
| Un **texto que escribe el servidor** | — | Que no lleve emoji ni símbolos: `EtiquetasDelServidorSinEmojiTests` |
| El **cifrado compartido** con el escritorio | — | Nada: `ProtectorPortableTests` ya lo vigila, y ponerse en rojo es la señal |
