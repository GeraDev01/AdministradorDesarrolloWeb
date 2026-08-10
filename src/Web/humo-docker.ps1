# Prueba de humo contra la aplicacion LEVANTADA EN DOCKER, sobre SQL Server y Linux.
#
# Es la hermana de humo.ps1 y prueba lo mismo, pero contra lo que de verdad va a correr en
# produccion. La diferencia importa: humo.ps1 usa SQLite sobre Windows, asi que hay dos familias
# enteras de fallos que no puede ver.
#
#   1. La rama de SQL SERVER del migrador: otro dialecto, otros tipos, sp_getapplock. Ya aparecieron
#      tres defectos ahi que ninguna prueba de servicios podia atrapar.
#   2. LINUX: rutas, mayusculas en nombres de archivo, dependencias nativas.
#
# La base es un contenedor VACIO y desechable. No toca ninguna base real.
#
# Uso:
#   docker compose -f src\Web\docker-compose.yml up -d --build     (desde la raiz)
#   .\src\Web\humo-docker.ps1
$ErrorActionPreference = 'Stop'
$url = "http://127.0.0.1:8080"

function Pedir($ruta, $metodo = 'GET', $cuerpo = $null, $sesion = $null) {
    $peticion = @{ Uri = "$url$ruta"; Method = $metodo; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($sesion) { $peticion.WebSession = $sesion }
    if ($cuerpo) { $peticion.Body = $cuerpo; $peticion.ContentType = 'application/json' }
    try {
        $r = Invoke-WebRequest @peticion
        return @{ Codigo = [int]$r.StatusCode; Cuerpo = $r.Content }
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ Codigo = 0; Cuerpo = $_.Exception.Message } }
        $lector = New-Object System.IO.StreamReader($resp.GetResponseStream())
        return @{ Codigo = [int]$resp.StatusCode; Cuerpo = $lector.ReadToEnd() }
    }
}

Write-Output "Esperando a que el contenedor responda..."
$listo = $false
for ($i = 0; $i -lt 60; $i++) {
    $r = Pedir "/api/health"
    if ($r.Codigo -eq 200) { $listo = $true; break }
    Start-Sleep -Seconds 3
}
if (-not $listo) {
    Write-Output "NO RESPONDE. Ultimas lineas del contenedor:"
    docker compose -f "$PSScriptRoot\docker-compose.yml" logs api --tail 40
    exit 1
}
Write-Output "OK  /api/health -> 200 (contra SQL Server)"

# La contrasena temporal la anuncia el arranque y solo aparece ahi: no se guarda en ningun sitio, a
# proposito. Puede NO estar, y es normal en dos casos: la base ya venia sembrada de una corrida
# anterior, o se recreo el contenedor (un `up --build`) contra una base que ya existia. En los dos se
# entra con la definitiva.
$registro = docker compose -f "$PSScriptRoot\docker-compose.yml" logs api 2>&1 | Out-String
$tempPwd = if ($registro -match 'temporal (\S+?)\.') { $Matches[1] } else { $null }
if ($tempPwd) { Write-Output "OK  admin sembrado al arrancar" }
else { Write-Output "OK  la base ya venia sembrada (no hay contrasena temporal que anunciar)" }

$sesion = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$r = Pedir "/api/dashboard" 'GET' $null $sesion
if ($r.Codigo -ne 401) { Write-Output "FALLO: sin sesion devolvio $($r.Codigo)"; exit 1 }
Write-Output "OK  ruta protegida sin sesion -> 401"

# El guion tiene que poder correrse VARIAS VECES sobre el mismo contenedor: la base sobrevive entre
# corridas y la contrasena temporal es de un solo uso. Si ya se cambio, se entra con la definitiva.
$definitiva = "ClaveNueva123"

$r = if ($tempPwd) {
    Pedir "/api/auth/login" 'POST' (@{ usuario = "admin"; contrasena = $tempPwd } | ConvertTo-Json) $sesion
} else {
    @{ Codigo = 0; Cuerpo = "" }
}
if ($r.Codigo -eq 200) {
    Write-Output "OK  login con la contrasena temporal"
    $r = Pedir "/api/auth/change-password" 'POST' (@{ nuevaContrasena = $definitiva; confirmacion = $definitiva } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: cambio de contrasena -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  cambio de contrasena"
}
else {
    $r = Pedir "/api/auth/login" 'POST' (@{ usuario = "admin"; contrasena = $definitiva } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: login -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  login (la base ya venia iniciada de una corrida anterior)"
}

# Todas las pantallas contra SQL Server. Aqui es donde se ven las consultas que SQLite traducia y
# SQL Server no —o al reves—, y las que fallan por un tipo distinto.
$rutas = @(
    "/api/auth/me", "/api/dashboard", "/api/avisos", "/api/avisos/contador",
    "/api/desempeno/ranking?anio=2026&mes=8", "/api/catalogos/desarrolladores",
    "/api/bitacora?pagina=1&tamanoPagina=10", "/api/foro?pagina=1&tamanoPagina=10",
    "/api/plantillas", "/api/cumplimiento-sla",
    "/api/jornada/mia", "/api/jornada/estado",
    "/api/pool/mio", "/api/pool/lider", "/api/pool/configuracion",
    "/api/autocalificacion/mias",
    "/api/ausencias/vacaciones/mias", "/api/ausencias/permisos/mios",
    "/api/sugerencias/mias", "/api/foro/imagenes?entradas=1,2",
    "/api/trabajo/requerimientos", "/api/trabajo/sprints", "/api/trabajo/sprints/historico",
    "/api/trabajo/mis-asignaciones",
    "/api/personas/presencia", "/api/personas/perfiles", "/api/personas/usuarios",
    "/api/personas/equipos/rotaciones", "/api/personas/comunicados/destinatarios",
    "/api/administracion/configuracion", "/api/administracion/limpieza", "/api/administracion/minutas",
    "/api/reportes/",
    "/api/evaluaciones/desarrolladores", "/api/evaluaciones/mias",
    "/api/metricas/", "/api/metricas/estimacion?dias=30",
    "/api/ausencias-lider/vacaciones", "/api/ausencias-lider/permisos",
    "/api/ausencias-lider/actividades", "/api/ausencias-lider/sugerencias",
    "/api/ausencias-lider/firmas",
    "/api/desempeno/pendientes",
    "/api/freshdesk/", "/api/vinculos/", "/api/correo/estado",
    "/api/sla/", "/api/sla/opciones", "/api/sla/mios",
    "/api/devops/estado", "/api/devops/tablero", "/api/devops/etiquetas", "/api/devops/mis-tickets",
    "/api/despliegues/opciones", "/api/despliegues/sistemas", "/api/despliegues/servidores",
    "/api/despliegues/perfiles", "/api/despliegues/en-curso", "/api/despliegues/historial",
    "/api/programados/", "/api/programados/estado-de-servidores", "/api/almacenamiento/",
    "/api/avisos/push/configuracion",
    # Cierre de huecos y costuras.
    "/api/notas/"
)
$fallos = 0
foreach ($ruta in $rutas) {
    $r = Pedir $ruta 'GET' $null $sesion
    if ($r.Codigo -eq 200) { Write-Output "OK  GET $ruta" }
    else { Write-Output "FALLO GET $ruta -> $($r.Codigo) $($r.Cuerpo)"; $fallos++ }
}
if ($fallos -gt 0) { Write-Output "`n$fallos ruta(s) fallaron contra SQL Server"; exit 1 }

# Escritura. En SQL Server importa mas que en SQLite: aqui viven RowVersion y las columnas con tipo
# estricto, que es donde un DateTime o un decimal mal mapeado revienta al INSERT y no al leer.
# La jornada solo se puede marcar una vez al dia, asi que en una segunda corrida sobre el mismo
# contenedor ya estara cerrada. Se comprueba que el rechazo sea el correcto en cada caso, en vez de
# saltarse la prueba.
$r = Pedir "/api/jornada/estado" 'GET' $null $sesion
$estado = $r.Cuerpo | ConvertFrom-Json

if ($estado.puedeMarcarEntrada) {
    $r = Pedir "/api/jornada/entrada" 'POST' (@{ nota = "humo en docker" } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: marcar entrada -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  POST /api/jornada/entrada"

    $r = Pedir "/api/jornada/entrada" 'POST' (@{ nota = $null } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 400 -or $r.Cuerpo -notmatch 'Ya marcaste') {
        Write-Output "FALLO: segunda entrada -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  segunda entrada rechazada con su motivo"

    $r = Pedir "/api/jornada/salida" 'POST' (@{ nota = $null } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: marcar salida -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  POST /api/jornada/salida"
}
else {
    $r = Pedir "/api/jornada/entrada" 'POST' (@{ nota = $null } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 400) { Write-Output "FALLO: la jornada ya estaba cerrada y no lo dijo -> $($r.Codigo)"; exit 1 }
    Write-Output "OK  jornada del dia ya cerrada -> rechaza con su motivo"
}

$r = Pedir "/api/adjuntos/foro/999999" 'GET' $null $sesion
if ($r.Codigo -ne 404) { Write-Output "FALLO: adjunto inexistente -> $($r.Codigo)"; exit 1 }
Write-Output "OK  adjunto inexistente -> 404"

# ── Concurrencia: el conflicto de RowVersion ────────────────────────────────────
#
# ESTO SOLO SE PUEDE PROBAR AQUI. RowVersion existe unicamente en SQL Server; en SQLite el modelo la
# ignora, asi que ninguna prueba de servicios ni humo.ps1 puede ejercitar este camino. Y es un
# camino que importa: en el escritorio no habia concurrencia real —una persona, una maquina—, pero
# en la web dos pestanas o dos personas editando lo mismo es lo normal. Sin esto, la segunda
# pisaria el cambio de la primera en silencio.
$nuevo = @{
    titulo = "Requerimiento de humo"; detalle = "Creado por la prueba de concurrencia."
    estado = 0; prioridad = 1; horasEstimadas = $null
    fechaSolicitud = $null; fechaCompromiso = $null; fechaEntrega = $null
    avancePct = 0; sello = $null
} | ConvertTo-Json

$r = Pedir "/api/trabajo/requerimientos/nuevo" 'POST' $nuevo $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: crear requerimiento -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
Write-Output "OK  POST /api/trabajo/requerimientos/nuevo"

$r = Pedir "/api/trabajo/requerimientos" 'GET' $null $sesion
$ficha = ($r.Cuerpo | ConvertFrom-Json).Filas | Where-Object { $_.titulo -eq "Requerimiento de humo" } | Select-Object -First 1
if ($null -eq $ficha) { Write-Output "FALLO: el requerimiento recien creado no aparece en la lista"; exit 1 }
if ([string]::IsNullOrEmpty($ficha.sello)) {
    Write-Output "FALLO: el requerimiento vino SIN sello de concurrencia. Contra SQL Server deberia traerlo."
    exit 1
}
Write-Output "OK  el requerimiento trae su sello de concurrencia"

# Dos ediciones con el MISMO sello: la primera gana, la segunda tiene que salir 409.
$edicion = @{
    titulo = "Editado por la primera"; detalle = $null
    estado = 0; prioridad = 1; horasEstimadas = $null
    fechaSolicitud = $null; fechaCompromiso = $null; fechaEntrega = $null
    avancePct = 10; sello = $ficha.sello
} | ConvertTo-Json

$r = Pedir "/api/trabajo/requerimientos/$($ficha.id)/editar" 'POST' $edicion $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: primera edicion -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
Write-Output "OK  la primera edicion pasa"

$segunda = @{
    titulo = "Editado por la segunda"; detalle = $null
    estado = 0; prioridad = 1; horasEstimadas = $null
    fechaSolicitud = $null; fechaCompromiso = $null; fechaEntrega = $null
    avancePct = 99; sello = $ficha.sello    # el sello VIEJO, el que ya quedo obsoleto
} | ConvertTo-Json

$r = Pedir "/api/trabajo/requerimientos/$($ficha.id)/editar" 'POST' $segunda $sesion
if ($r.Codigo -ne 409) {
    Write-Output "FALLO: la segunda edicion con el sello viejo devolvio $($r.Codigo), se esperaba 409."
    Write-Output "       Eso significa que pisaria el cambio de la otra persona sin avisar."
    Write-Output "       $($r.Cuerpo)"
    exit 1
}
Write-Output "OK  la segunda edicion con el sello viejo -> 409 (no pisa el cambio ajeno)"

# ── Escritura de catalogos, contra SQL Server ────────────────────────────────────
#
# El alta y la edicion de los cuatro catalogos, mas las dos reglas de la clave de licencia. Aqui y
# no solo en las pruebas de servicios porque estas escrituras tocan columnas que en SQLite y en SQL
# Server no se comportan igual (decimal del costo, fechas, el NVARCHAR de la clave), y porque el
# sembrado del arranque ya dejo filas en estas mismas tablas: es la primera vez que las escrituras
# conviven con datos que no puso la propia prueba.

$dev = @{
    id = $null; nombre = "Humo Catalogo"; correo = "humo.catalogo@x.com"; telefono = $null
    seniority = "Senior"; fechaIngreso = "2020-01-01"; direccion = $null; serieEquipo = $null
    diasVacaciones = 15; activo = $true; notas = $null
} | ConvertTo-Json

$r = Pedir "/api/catalogos/desarrolladores" 'POST' $dev $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: alta de desarrollador -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
Write-Output "OK  POST /api/catalogos/desarrolladores (alta)"

$r = Pedir "/api/catalogos/desarrolladores?soloActivos=true&texto=Humo%20Catalogo" 'GET' $null $sesion
$fila = ($r.Cuerpo | ConvertFrom-Json) | Where-Object { $_.nombre -eq "Humo Catalogo" } | Select-Object -First 1
if ($null -eq $fila) { Write-Output "FALLO: el desarrollador recien creado no aparece"; exit 1 }

# La fecha de ingreso es del 1-ene-2020: a hoy son mas de 5 anios, o sea 22 dias por el Art. 76.
$r = Pedir "/api/catalogos/desarrolladores/lft?fechaIngreso=2020-01-01" 'GET' $null $sesion
$lft = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or $lft.dias -lt 20) {
    Write-Output "FALLO: la sugerencia LFT devolvio $($r.Codigo) / $($lft.dias) dias."; exit 1
}
Write-Output "OK  la sugerencia de vacaciones por ley responde ($($lft.dias) dias)"

# La cuenta de acceso: usuario derivado del correo y contrasena temporal de una sola vez.
$r = Pedir "/api/catalogos/desarrolladores/$($fila.id)/acceso" 'POST' '{}' $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: crear acceso -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
$cred = $r.Cuerpo | ConvertFrom-Json
if ([string]::IsNullOrEmpty($cred.contrasenaTemporal)) {
    Write-Output "FALLO: la cuenta se creo pero sin contrasena temporal."; exit 1
}
Write-Output "OK  cuenta de acceso creada como «$($cred.usuario)» con temporal de un solo uso"

# Repetir NO debe recrearla ni restablecerla por la puerta de atras.
$r = Pedir "/api/catalogos/desarrolladores/$($fila.id)/acceso" 'POST' '{}' $sesion
if ($r.Codigo -ne 400) {
    Write-Output "FALLO: crear acceso dos veces devolvio $($r.Codigo), se esperaba 400."; exit 1
}
Write-Output "OK  crear acceso dos veces -> 400 (no recrea ni restablece)"

# La baja DESACTIVA: la ficha tiene que seguir existiendo despues.
$r = Pedir "/api/catalogos/desarrolladores/$($fila.id)/desactivar" 'POST' '{}' $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: desactivar -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
$r = Pedir "/api/catalogos/desarrolladores?soloActivos=false&texto=Humo%20Catalogo" 'GET' $null $sesion
$baja = ($r.Cuerpo | ConvertFrom-Json) | Where-Object { $_.id -eq $fila.id } | Select-Object -First 1
if ($null -eq $baja -or $baja.activo) {
    Write-Output "FALLO: la baja borro la ficha o no la desactivo."; exit 1
}
Write-Output "OK  la baja desactiva y conserva la ficha"

# Programas: la clave de licencia se escribe, sobrevive a una edicion que no la manda, y su
# consulta queda anotada en la bitacora.
$prog = @{
    id = $null; nombre = "Humo Licencia"; categoria = 0; estado = 0; licencia = 0
    version = "1.0"; fabricante = "ACME"; claveDeLicencia = "HUMO-CLAVE-123"
    vence = $null; instaladoEn = $null; url = $null; notas = $null
} | ConvertTo-Json

$r = Pedir "/api/catalogos/programas" 'POST' $prog $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: alta de programa -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }

$r = Pedir "/api/catalogos/programas?texto=Humo%20Licencia" 'GET' $null $sesion
$p = ($r.Cuerpo | ConvertFrom-Json) | Where-Object { $_.nombre -eq "Humo Licencia" } | Select-Object -First 1
if ($null -eq $p) { Write-Output "FALLO: el programa recien creado no aparece"; exit 1 }
if ($r.Cuerpo -match 'HUMO-CLAVE-123') {
    Write-Output "FALLO: la clave de licencia VIAJO en la respuesta de la rejilla."
    Write-Output "       Eso la deja a un «ver codigo fuente» de cualquiera que abra la pantalla."
    exit 1
}
Write-Output "OK  la clave de licencia NO viaja con la rejilla"

# Editar sin mandar la clave (null) tiene que conservarla.
$edit = @{
    id = $p.id; nombre = "Humo Licencia v2"; categoria = 0; estado = 0; licencia = 0
    version = "2.0"; fabricante = "ACME"; claveDeLicencia = $null
    vence = $null; instaladoEn = $null; url = $null; notas = $null
} | ConvertTo-Json
$r = Pedir "/api/catalogos/programas" 'POST' $edit $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: editar programa -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }

$r = Pedir "/api/catalogos/programas/$($p.id)/clave" 'POST' '{}' $sesion
$clave = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or $clave.clave -ne "HUMO-CLAVE-123") {
    Write-Output "FALLO: editar otro campo se llevo por delante la clave de licencia."
    Write-Output "       Devolvio $($r.Codigo) / «$($clave.clave)»."
    exit 1
}
Write-Output "OK  editar sin mandar la clave la CONSERVA, y consultarla la devuelve"

# Contactos y recursos de Azure: alta y borrado/edicion.
$con = @{
    id = $null; nombre = "Humo Contacto"; puesto = "Compras"; empresa = "ACME"
    correo = "humo@acme.com"; telefono = $null; enlaceTeams = $null; notas = $null
} | ConvertTo-Json
$r = Pedir "/api/catalogos/contactos" 'POST' $con $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: alta de contacto -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }

$r = Pedir "/api/catalogos/contactos?texto=Humo%20Contacto" 'GET' $null $sesion
$c = ($r.Cuerpo | ConvertFrom-Json) | Select-Object -First 1
$r = Pedir "/api/catalogos/contactos/$($c.id)/eliminar" 'POST' '{}' $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: borrar contacto -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
Write-Output "OK  contacto: alta y borrado"

# El costo es decimal: es el campo que mas facil se rompe entre SQLite y SQL Server.
$rec = @{
    id = $null; nombre = "humo-app"; tipo = 0; estado = 0; ambiente = 0
    grupoDeRecursos = "rg-humo"; suscripcion = "Pago x uso"; region = "eastus"
    costoMensual = 1234.56; url = $null; notas = $null
} | ConvertTo-Json
$r = Pedir "/api/catalogos/recursos-azure" 'POST' $rec $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: alta de recurso Azure -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }

$r = Pedir "/api/catalogos/recursos-azure?texto=humo-app" 'GET' $null $sesion
$ra = ($r.Cuerpo | ConvertFrom-Json) | Select-Object -First 1
if ($null -eq $ra -or [decimal]$ra.costoMensual -ne 1234.56) {
    Write-Output "FALLO: el costo mensual se guardo como «$($ra.costoMensual)», se esperaba 1234.56."
    exit 1
}
Write-Output "OK  recurso Azure: alta con decimal intacto"

# Las exportaciones a Excel: que devuelvan un .xlsx de verdad y no una pagina de error.
foreach ($ruta in @("/api/catalogos/desarrolladores/excel", "/api/catalogos/contactos/excel")) {
    $r = Pedir $ruta 'GET' $null $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: $ruta -> $($r.Codigo)"; exit 1 }
    Write-Output "OK  GET $ruta"
}

# El catalogo inicial de plantillas: el sembrado del arranque tuvo que dejarlo puesto.
$r = Pedir "/api/plantillas" 'GET' $null $sesion
$plantillas = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or $plantillas.Count -lt 1) {
    Write-Output "FALLO: la biblioteca de plantillas quedo VACIA tras el arranque."
    Write-Output "       El sembrado del catalogo inicial no corrio."
    exit 1
}
Write-Output "OK  el catalogo inicial de plantillas se sembro ($($plantillas.Count) plantillas)"

# ── Plantilla de Word y presencia ────────────────────────────────────────────────

# El origen de la plantilla: recien creada la base, tiene que decir que es la de FABRICA.
$r = Pedir "/api/ausencias-lider/plantilla-vacaciones" 'GET' $null $sesion
$origen = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or -not $origen.esDeFabrica) {
    Write-Output "FALLO: el origen de la plantilla dice $($r.Codigo) / esDeFabrica=$($origen.esDeFabrica)."
    exit 1
}
Write-Output "OK  la plantilla arranca siendo la de fabrica"

# Descargarla tiene que dar un .docx de verdad: un ZIP empieza por «PK».
$r = Pedir "/api/ausencias-lider/plantilla-vacaciones/descargar" 'GET' $null $sesion
# Un .docx es un ZIP: sus dos primeros bytes son «PK» (0x50 0x4B). Se comprueban como BYTES porque
# el contenido binario no llega como texto y .StartsWith() no existe sobre un arreglo de bytes.
$esZip = $r.Cuerpo.Length -gt 1000 -and $r.Cuerpo[0] -eq 0x50 -and $r.Cuerpo[1] -eq 0x4B
if ($r.Codigo -ne 200 -or -not $esZip) {
    Write-Output "FALLO: la plantilla no se descarga como .docx ($($r.Codigo), $($r.Cuerpo.Length) bytes)."
    exit 1
}
Write-Output "OK  la plantilla de fabrica se descarga y es un .docx"

# Mi estado de presencia: el endpoint que faltaba y por el que el boton decia siempre «Disponible».
$r = Pedir "/api/jornada/presencia" 'GET' $null $sesion
$presencia = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or [string]::IsNullOrWhiteSpace($presencia.estadoTexto)) {
    Write-Output "FALLO: /api/jornada/presencia -> $($r.Codigo) $($r.Cuerpo)"; exit 1
}
Write-Output "OK  el estado de presencia propio se puede consultar ($($presencia.estadoTexto))"

# La version, sin sesion: es lo primero que hay que poder preguntar cuando algo va mal.
$r = Pedir "/api/version"
$v = $r.Cuerpo | ConvertFrom-Json
if ($r.Codigo -ne 200 -or [string]::IsNullOrWhiteSpace($v.version)) {
    Write-Output "FALLO: /api/version -> $($r.Codigo) $($r.Cuerpo)"; exit 1
}
Write-Output "OK  /api/version responde sin sesion (v$($v.version))"

# La busqueda global, que hasta ahora era codigo muerto.
$r = Pedir "/api/busqueda?q=Humo" 'GET' $null $sesion
if ($r.Codigo -ne 200) { Write-Output "FALLO: /api/busqueda -> $($r.Codigo)"; exit 1 }
Write-Output "OK  la busqueda global responde"

# El cliente Blazor lo sirve la misma aplicacion: si esto no llega, no hay pantalla que abrir.
$r = Pedir "/"
if ($r.Codigo -ne 200 -or $r.Cuerpo -notmatch 'blazor') {
    Write-Output "FALLO: el cliente no se sirve -> $($r.Codigo)"; exit 1
}
Write-Output "OK  el cliente Blazor se sirve desde el contenedor"

Write-Output "`nTODO OK (SQL Server + Linux)"
