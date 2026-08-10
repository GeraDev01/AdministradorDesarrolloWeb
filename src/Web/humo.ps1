# Prueba de humo de la API: la levanta de verdad contra una base SQLite temporal y recorre el flujo
# completo. No toca ninguna base real y borra la suya al terminar.
#
# Comprueba lo que las pruebas unitarias no pueden: que la aplicacion ARRANQUE (el migrador corre,
# el administrador inicial se siembra), que la autenticacion funcione de extremo a extremo con
# cookies de verdad, y que cada pantalla de consulta responda. Un cambio en la configuracion de
# arranque —el orden del middleware, una politica mal escrita, una cookie que no viaja— compila y
# pasa las pruebas igual; esto es lo que lo atrapa.
#
# Uso:  cd src\Web ;  dotnet build AdminWeb.Api\AdminWeb.Api.csproj ;  .\humo.ps1
#
# Escrito para Windows PowerShell 5.1: sin -SkipHttpErrorCheck, los errores HTTP llegan por excepcion.
$ErrorActionPreference = 'Stop'
$raiz = "c:\Users\gerar\source\repos\Administrador_Desarrollo_Web\src\Web\AdminWeb.Api"
$bd   = Join-Path $env:TEMP ("humo_" + [guid]::NewGuid().ToString("N") + ".db")
$url  = "http://127.0.0.1:5199"

# Peticion que devuelve (codigo, cuerpo) en vez de reventar con los 4xx/5xx, que aqui son resultados.
function Pedir($ruta, $metodo = 'GET', $cuerpo = $null, $sesion = $null) {
    $peticion = @{ Uri = "$url$ruta"; Method = $metodo; UseBasicParsing = $true; TimeoutSec = 20 }
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

# Publica en el foro con un cuerpo que lleva un intento de ataque y una url legitima. El testigo va
# aparte porque la gracia es poder llamar tambien SIN el.
function PublicarEnForo($sesion, $testigo) {
    $limite = "----humo" + [guid]::NewGuid().ToString("N")
    $eol = "`r`n"

    $cuerpo = "Mira <script>alert(1)</script> y javascript:alert(2) y https://ejemplo.test/ticket"
    $partes = ""
    foreach ($campo in @(@("titulo", "Prueba de humo"), @("cuerpo", $cuerpo), @("tema", "0"), @("etiquetas", ""))) {
        $partes += "--$limite$eol"
        $partes += "Content-Disposition: form-data; name=`"$($campo[0])`"$eol$eol"
        $partes += "$($campo[1])$eol"
    }
    $partes += "--$limite--$eol"

    $peticion = @{
        Uri = "$url/api/foro/publicaciones"; Method = 'POST'; UseBasicParsing = $true; TimeoutSec = 20
        WebSession = $sesion; Body = $partes
        ContentType = "multipart/form-data; boundary=$limite"
    }
    if ($testigo) { $peticion.Headers = @{ "X-XSRF-TOKEN" = $testigo } }

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

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = $url
$env:ConnectionStrings__Default = "Data Source=$bd"
$env:AdminWeb__ProveedorDeBase = "Sqlite"

$salida = "$env:TEMP\humo_out.txt"
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$raiz`" --no-build" `
    -PassThru -RedirectStandardOutput $salida -RedirectStandardError "$env:TEMP\humo_err.txt" -NoNewWindow

try {
    $listo = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 1500
        $r = Pedir "/api/health"
        if ($r.Codigo -eq 200) { $listo = $true; break }
        if ($proc.HasExited) { break }
    }
    if (-not $listo) { Write-Output "NO ARRANCO"; Get-Content $salida -Tail 25; exit 1 }
    Write-Output "OK  /api/health -> 200"

    $log = Get-Content $salida -Raw
    if ($log -notmatch 'temporal (\S+?)\.') { Write-Output "FALLO: sin contrasena temporal en el log"; exit 1 }
    $tempPwd = $Matches[1]
    Write-Output "OK  admin sembrado al arrancar"

    $sesion = New-Object Microsoft.PowerShell.Commands.WebRequestSession

    $r = Pedir "/api/dashboard" 'GET' $null $sesion
    if ($r.Codigo -ne 401) { Write-Output "FALLO: sin sesion devolvio $($r.Codigo), se esperaba 401"; exit 1 }
    Write-Output "OK  ruta protegida sin sesion -> 401"

    $r = Pedir "/api/auth/login" 'POST' (@{ usuario = "admin"; contrasena = $tempPwd } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: login -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  login con la contrasena temporal"

    $r = Pedir "/api/dashboard" 'GET' $null $sesion
    if ($r.Codigo -ne 403 -or $r.Cuerpo -notmatch 'MUST_CHANGE_PASSWORD') {
        Write-Output "FALLO: con contrasena temporal -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  contrasena temporal bloquea el resto -> 403 MUST_CHANGE_PASSWORD"

    $r = Pedir "/api/auth/change-password" 'POST' (@{ nuevaContrasena = "ClaveNueva123"; confirmacion = "ClaveNueva123" } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: cambio de contrasena -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  cambio de contrasena"

    $rutas = @(
        "/api/auth/me", "/api/dashboard", "/api/avisos", "/api/avisos/contador",
        "/api/desempeno/ranking?anio=2026&mes=8", "/api/catalogos/desarrolladores",
        "/api/bitacora?pagina=1&tamanoPagina=10", "/api/foro?pagina=1&tamanoPagina=10",
        "/api/plantillas", "/api/cumplimiento-sla",
        # Autoservicio (fase 2). El administrador llega a todas: su rol entra en las dos politicas.
        "/api/jornada/mia", "/api/jornada/estado",
        "/api/pool/mio", "/api/pool/lider", "/api/pool/configuracion",
        "/api/autocalificacion/mias",
        "/api/ausencias/vacaciones/mias", "/api/ausencias/permisos/mios",
        "/api/sugerencias/mias", "/api/foro/imagenes?entradas=1,2",
        # Administracion (fase 3).
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
        # Integraciones (fase 4). Sin credenciales configuradas tienen que responder igual, con su
        # estado en "no configurado": una integracion apagada no puede tumbar la pantalla.
        "/api/freshdesk/", "/api/vinculos/", "/api/correo/estado",
        "/api/sla/", "/api/sla/opciones", "/api/sla/mios",
        "/api/devops/estado", "/api/devops/tablero", "/api/devops/etiquetas", "/api/devops/mis-tickets",
        # Despliegues (fase 5). Sin servidores configurados responden vacio, no con un error.
        "/api/despliegues/opciones", "/api/despliegues/sistemas", "/api/despliegues/servidores",
        "/api/despliegues/perfiles", "/api/despliegues/en-curso", "/api/despliegues/historial",
        "/api/programados/", "/api/programados/estado-de-servidores", "/api/almacenamiento/",
        "/api/avisos/push/configuracion"
    )
    $fallos = 0
    foreach ($ruta in $rutas) {
        $r = Pedir $ruta 'GET' $null $sesion
        if ($r.Codigo -eq 200) { Write-Output "OK  GET $ruta" }
        else { Write-Output "FALLO GET $ruta -> $($r.Codigo) $($r.Cuerpo)"; $fallos++ }
    }
    if ($fallos -gt 0) { Write-Output "`n$fallos ruta(s) fallaron"; exit 1 }

    # ── Escritura ────────────────────────────────────────────────────────────────
    #
    # Hasta aqui todo era consulta. Lo que sigue ESCRIBE, y por eso importa: un endpoint de escritura
    # puede compilar, pasar las pruebas del servicio y aun asi fallar al llegarle una peticion de
    # verdad —el cuerpo no se deserializa, falta el registro en el contenedor, la politica rechaza—.
    # Nada de esto toca ninguna base real: la de esta prueba es un archivo temporal que se borra.

    $r = Pedir "/api/jornada/estado" 'GET' $null $sesion
    if ($r.Codigo -ne 200 -or $r.Cuerpo -notmatch '"puedeMarcarEntrada":true') {
        Write-Output "FALLO: estado inicial de jornada -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  jornada sin marcar -> ofrece marcar entrada"

    $r = Pedir "/api/jornada/entrada" 'POST' (@{ nota = "prueba de humo" } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: marcar entrada -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  POST /api/jornada/entrada"

    # Marcar dos veces tiene que RECHAZARSE con una explicacion, no aceptarse en silencio ni reventar.
    $r = Pedir "/api/jornada/entrada" 'POST' (@{ nota = $null } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 400 -or $r.Cuerpo -notmatch 'Ya marcaste') {
        Write-Output "FALLO: segunda entrada -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  segunda entrada rechazada con su motivo"

    $r = Pedir "/api/jornada/mia" 'GET' $null $sesion
    if ($r.Codigo -ne 200 -or $r.Cuerpo -notmatch '"entradaUtc"') {
        Write-Output "FALLO: mi jornada -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  GET /api/jornada/mia refleja la entrada"

    $r = Pedir "/api/jornada/salida" 'POST' (@{ nota = $null } | ConvertTo-Json) $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: marcar salida -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  POST /api/jornada/salida"

    # Una cuenta SIN ficha de desarrollador —el administrador de esta prueba lo es— pidiendo algo que
    # necesita ficha tiene que recibir una explicacion, no un 500 ni una pagina en blanco. Media
    # plantilla puede estar en esa situacion (operaciones, cuentas nuevas), asi que importa.
    $r = Pedir "/api/evaluaciones/mias/ficha" 'GET' $null $sesion
    if ($r.Codigo -ne 400 -or $r.Cuerpo -notmatch 'ficha de desarrollador') {
        Write-Output "FALLO: ficha sin desarrollador -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    Write-Output "OK  cuenta sin ficha -> 400 explicado"

    # Un adjunto que no existe responde 404, no 500 ni un archivo vacio.
    $r = Pedir "/api/adjuntos/foro/999999" 'GET' $null $sesion
    if ($r.Codigo -ne 404) { Write-Output "FALLO: adjunto inexistente -> $($r.Codigo)"; exit 1 }
    Write-Output "OK  adjunto inexistente -> 404"

    # El testigo antiforgery tiene que emitirse: sin el, las subidas de archivos fallarian todas.
    $r = Pedir "/api/auth/antiforgery" 'GET' $null $sesion
    if ($r.Codigo -ne 200 -or $r.Cuerpo -notmatch '"valor":"(.+?)"') {
        Write-Output "FALLO: testigo antiforgery -> $($r.Codigo) $($r.Cuerpo)"; exit 1
    }
    $testigo = $Matches[1]
    Write-Output "OK  testigo antiforgery emitido"

    # Publicar en el foro: es la unica escritura que va por formulario, y por tanto la unica que
    # necesita el testigo. Se prueba en los DOS sentidos, porque las dos formas de romperlo son
    # silenciosas: si el testigo dejara de adjuntarse, publicar empezaria a fallar sin que nada
    # avise; si la proteccion se desactivara "para que funcione", tampoco fallaria nada.
    # 400 y no 500: un 500 no le dice nada al usuario y a quien vigila el servidor le parece un fallo
    # de la aplicacion, cuando es esta proteccion haciendo su trabajo.
    $r = PublicarEnForo $sesion $null
    if ($r.Codigo -ne 400) { Write-Output "FALLO: publicar sin testigo -> $($r.Codigo), se esperaba 400"; exit 1 }
    Write-Output "OK  publicar sin testigo -> 400 con explicacion"

    $r = PublicarEnForo $sesion $testigo
    if ($r.Codigo -ne 200) { Write-Output "FALLO: publicar con testigo -> $($r.Codigo) $($r.Cuerpo)"; exit 1 }
    Write-Output "OK  publicar con testigo -> 200"

    # Y el cuerpo tiene que volver como TEXTO, con el ataque intacto y solo la url como enlace.
    $r = Pedir "/api/foro/muro?pagina=1&tamano=5" 'GET' $null $sesion
    if ($r.Cuerpo -notmatch 'javascript') {
        Write-Output "FALLO: el muro no devolvio la publicacion de prueba"; exit 1
    }
    if ($r.Cuerpo -match '"url":"javascript') {
        Write-Output "FALLO: un javascript: se convirtio en enlace"; exit 1
    }
    Write-Output "OK  el cuerpo vuelve como texto y javascript: no es enlace"

    # Cerrar sesion tiene que invalidarla de verdad, no solo borrar la cookie del navegador.
    $r = Pedir "/api/auth/logout" 'POST' $null $sesion
    if ($r.Codigo -ne 200) { Write-Output "FALLO: logout -> $($r.Codigo)"; exit 1 }
    $r = Pedir "/api/jornada/mia" 'GET' $null $sesion
    if ($r.Codigo -ne 401) { Write-Output "FALLO: tras cerrar sesion -> $($r.Codigo), se esperaba 401"; exit 1 }
    Write-Output "OK  tras cerrar sesion, ruta protegida -> 401"

    Write-Output "`nTODO OK"
}
finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    Remove-Item $bd -ErrorAction SilentlyContinue
}
