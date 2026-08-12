# EL ENSAYO DEL CORTE: aplicar el esquema sobre una COPIA de produccion y medir cuanto tarda.
#
# ── POR QUE EXISTE ──────────────────────────────────────────────────────────────────────────────
#
# Mide la ventana de mantenimiento, que sin esto se adivina. Pero lo que de verdad justifica el
# ensayo es lo OTRO: que la aplicacion funcione con datos que ya existian.
#
# La primera vez que se corrio encontro un defecto que ninguna de las 2.042 pruebas veia y que
# habria tumbado el corte entero: `Users.SecurityStamp` es una columna nueva, el migrador la
# agregaba anulable y no la rellenaba, y `User.SecurityStamp` es `string` NO anulable. Una fila con
# NULL no es «una cuenta sin sello», es una fila que EF no puede materializar: la consulta del login
# revienta y NO ENTRA NADIE. En las bases de prueba no aparece nunca, porque alli los usuarios se
# crean por el modelo, que ya trae el sello puesto. Solo sale contra una base que ya tenia cuentas.
#
# Esa clase de defecto —dato viejo que el codigo nuevo no contempla— solo la encuentra esto.
#
# ── DOS CAMINOS ─────────────────────────────────────────────────────────────────────────────────
#
# A) COPIA EN AZURE. Mas fiel: mismo motor, misma region, misma latencia. Cuesta dinero mientras la
#    copia exista. Desde el portal, sobre la base de produccion: Restaurar -> punto en el tiempo mas
#    reciente -> nombre SOLTUM_ENSAYO_<fecha>.
#
# B) COPIA LOCAL EN UN CONTENEDOR. Gratis y sin tocar Azure. Se exporta produccion a un .bacpac
#    (lectura, no la modifica) y se importa a un SQL Server en Docker:
#
#      dotnet tool install -g microsoft.sqlpackage
#      docker run -d --name ensayo-sql -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<clave>" `
#                 -e "MSSQL_PID=Developer" -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
#      sqlpackage /Action:Export /ssn:<servidor> /sdn:<base> /su:<usuario> /sp:<clave> /tf:copia.bacpac
#      sqlpackage /Action:Import /sf:copia.bacpac /tsn:"localhost,14333" /tdn:SOLTUM_ENSAYO_local `
#                 /tu:sa /tp:<clave> /ttsc:True
#
#    Medido: exportar 10.913 filas tardo 2 minutos y dio 27,6 MB; importarlo, 40 segundos.
#
#    OJO CON LO QUE ESA COPIA LLEVA DENTRO. Los secretos de la base estan cifrados con una llave
#    derivada de una semilla que vive en el ensamblado (ver ProtectorPortable): es OFUSCACION, no
#    seguridad. La copia MAS el codigo de este repositorio son las contrasenas de los servidores de
#    produccion en claro. Por eso el ultimo apartado de este guion insiste en borrarla.
#
# ── USO ─────────────────────────────────────────────────────────────────────────────────────────
#
#   .\ensayo.ps1 -Copia "Server=localhost,14333;Database=SOLTUM_ENSAYO_local;User Id=sa;Password=...;TrustServerCertificate=True"
#
# Antes hay que compilar:  dotnet build AdminWeb.Api\AdminWeb.Api.csproj

[CmdletBinding()]
param(
    # La cadena de conexion de LA COPIA. Nunca la de produccion; el guion lo comprueba.
    [Parameter(Mandatory = $true)][string] $Copia,

    [string] $Informe = "$PSScriptRoot\ensayo-$(Get-Date -Format 'yyyyMMdd-HHmm').md",
    [string] $Puerto = "5199"
)

$ErrorActionPreference = 'Stop'

# ── LA BARRERA CONTRA PRODUCCION ────────────────────────────────────────────────────────────────
#
# Es una LISTA BLANCA, no una lista negra, y la diferencia es el punto entero de este bloque.
#
# Una lista negra («negarse si la base se llama SOLTUM_DEV_WD») protege exactamente hasta el dia en
# que alguien cree una segunda base de produccion, la renombre, o la escriba con otras mayusculas.
# Ese dia el guion arranca contra produccion sin decir nada y aplica un esquema en una ventana no
# planificada, que es el peor resultado posible de todo este documento.
#
# Con lista blanca, para equivocarse hay que RENOMBRAR la base de produccion metiendole «ENSAYO» en
# el nombre. Eso ya no es un descuido.
$nombreDeLaBase = ([regex]::Match($Copia, '(?i)(?:Initial\s+Catalog|Database)\s*=\s*([^;]+)')).Groups[1].Value.Trim()

if (-not $nombreDeLaBase) {
    throw "No se pudo leer el nombre de la base en la cadena de conexion. El ensayo no arranca a ciegas."
}

if ($nombreDeLaBase -notmatch '(?i)ENSAYO|COPIA|REHEARSAL') {
    throw @"
LA BASE «$nombreDeLaBase» NO PARECE UNA COPIA Y EL ENSAYO NO VA A CORRER.

Este guion aplica cambios de esquema. Contra produccion seria un corte no planificado.

Para que corra, la copia tiene que llevar ENSAYO, COPIA o REHEARSAL en el nombre. Es a proposito
que la unica forma de saltarse la barrera sea renombrar la base: asi no se salta por descuido.
Como sacar la copia, en la cabecera de este archivo.
"@
}

$lineas = @()
function Escribir($t) { Write-Host $t; $script:lineas += $t }

Escribir "# Ensayo del corte — $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Escribir ""
Escribir "Base de ensayo: ``$nombreDeLaBase``"
Escribir ""

# ── 1. QUE HAY DENTRO, ANTES ────────────────────────────────────────────────────────────────────
#
# Se cuenta antes y despues. Un migrador que se lleva por delante una tabla no lo dice; un recuento
# que baja, si. Y de paso confirma que la copia es una copia y no una base vacia, que es el error
# mas facil de cometer aqui y el que volveria el ensayo entero inutil: sobre una base vacia todo
# esto pasa en un segundo y no prueba nada.
function ContarFilas([string] $cadena) {
    $c = New-Object System.Data.SqlClient.SqlConnection $cadena
    $c.Open()
    try {
        $cmd = $c.CreateCommand()
        $cmd.CommandTimeout = 300
        # sys.partitions da el recuento sin recorrer las tablas. Es aproximado por diseno; para
        # «esto no se vacio» sobra, y sobre una base con anos de datos es la diferencia entre un
        # segundo y varios minutos.
        $cmd.CommandText = @"
SELECT t.name, SUM(p.rows)
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
GROUP BY t.name
"@
        $r = $cmd.ExecuteReader()
        $conteo = @{}
        while ($r.Read()) { $conteo[$r[0]] = [int64]$r[1] }
        $r.Close()
        return $conteo
    } finally { $c.Close() }
}

$antes = ContarFilas $Copia
$filasAntes = ($antes.Values | Measure-Object -Sum).Sum

Escribir "## Antes de migrar"
Escribir ""
Escribir "- Tablas: **$($antes.Count)**"
Escribir "- Filas (aproximado): **$filasAntes**"
Escribir ""

if ($filasAntes -lt 100) {
    throw "La copia tiene $filasAntes filas. Eso no es una copia de produccion, y ensayar contra una base vacia no mide nada. Revisa la restauracion."
}

# ── 2. ARRANCAR Y CRONOMETRAR ───────────────────────────────────────────────────────────────────
#
# La ventana es ESTO: lo que tarda desde que arranca hasta que responde. Se mide sobre el reloj y
# no sobre lo que diga el registro, porque lo que le cuesta al equipo esperar es el tiempo real.
#
# La primera pasada es la que vale: en las siguientes el esquema ya esta al dia y solo corren las
# comprobaciones idempotentes, que tardan la mitad.
$log = Join-Path $env:TEMP "ensayo-arranque.log"
$err = Join-Path $env:TEMP "ensayo-arranque.err"

$env:ConnectionStrings__Default = $Copia
$env:AdminWeb__ProveedorDeBase = ""            # SQL Server, no SQLite
$env:AdminWeb__TrabajosDeFondoActivos = "false"
$env:AdminWeb__DatosDeDemostracion = "false"   # jamas sobre datos reales
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http" + "://127.0.0.1:$Puerto"
$sonda = "http" + "://127.0.0.1:$Puerto/health"

Escribir "## La migracion"
Escribir ""

Push-Location $PSScriptRoot
$reloj = [System.Diagnostics.Stopwatch]::StartNew()
$api = Start-Process dotnet `
    -ArgumentList @("run", "--project", "AdminWeb.Api\AdminWeb.Api.csproj", "--no-build", "--no-launch-profile") `
    -RedirectStandardOutput $log -RedirectStandardError $err -NoNewWindow -PassThru
Pop-Location

$listo = $false
while (-not $listo -and $reloj.Elapsed.TotalMinutes -lt 45 -and -not $api.HasExited) {
    Start-Sleep -Milliseconds 400
    try { if ((Invoke-WebRequest $sonda -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200) { $listo = $true } } catch { }
}
$reloj.Stop()

if ($api.HasExited -or -not $listo) {
    Escribir ""
    Escribir "**LA APLICACION NO ARRANCO.** Ultimas lineas:"
    Escribir ""
    Escribir '```'
    Escribir (Get-Content $log -Tail 60 -ErrorAction SilentlyContinue | Out-String)
    Escribir (Get-Content $err -Tail 30 -ErrorAction SilentlyContinue | Out-String)
    Escribir '```'
    ($lineas -join "`n") | Set-Content $Informe -Encoding utf8
    throw "El ensayo fallo: la aplicacion no arranco contra la copia. Informe en $Informe"
}

Escribir "**Ventana medida: $($reloj.Elapsed.ToString('mm\:ss'))**  (arranque -> esquema al dia -> /health responde)"
Escribir ""
Escribir "Esa es la ventana MINIMA. Al planificar el corte hay que sumarle el respaldo explicito"
Escribir "previo, el cambio de la cadena de conexion en el App Service y el recorrido de"
Escribir "comprobacion. Doblarla es prudente, no pesimista."
Escribir ""

# ── 3. LAS SENTENCIAS FALLIDAS ──────────────────────────────────────────────────────────────────
#
# Este bloque es el motivo por el que el migrador devuelve la lista en vez de tragarse los errores.
# Durante dias anuncio «Esquema al dia» mientras se saltaba 114 sentencias, porque un `catch {}`
# vacio se comia todos los fallos y el arranque no distinguia «no hacia falta» de «no se pudo».
$registro = Get-Content $log -Raw -ErrorAction SilentlyContinue
$alDia = $registro -match 'Esquema al d'
$fallidas = @($registro -split "`n" | Where-Object { $_ -match 'no se pudo aplicar|sentencias? fallida' })

Escribir "## Sentencias del migrador"
Escribir ""
if ($alDia -and $fallidas.Count -eq 0) {
    Escribir "«Esquema al dia» y ninguna sentencia fallida."
} else {
    Escribir "**REVISAR. El corte no se hace hasta entenderlo.**"
    Escribir ""
    Escribir '```'
    if (-not $alDia) { Escribir "(no aparecio «Esquema al dia» en el registro)" }
    $fallidas | ForEach-Object { Escribir $_ }
    Escribir '```'
}
Escribir ""

# ── 4. QUE NO SE PERDIO NADA ────────────────────────────────────────────────────────────────────
$despues = ContarFilas $Copia
$perdidas = @()
foreach ($t in $antes.Keys) {
    $ahora = if ($despues.ContainsKey($t)) { $despues[$t] } else { -1 }
    if ($ahora -lt $antes[$t]) { $perdidas += "  $t : $($antes[$t]) -> $ahora" }
}

Escribir "## Despues de migrar"
Escribir ""
Escribir "- Tablas: **$($despues.Count)** (antes $($antes.Count); mas es normal, el migrador crea las nuevas)"
Escribir "- Filas: **$(($despues.Values | Measure-Object -Sum).Sum)** (antes $filasAntes)"
Escribir ""
if ($perdidas.Count -gt 0) {
    Escribir "**SE PERDIERON FILAS. Es un defecto grave del migrador, no un detalle:**"
    Escribir ""
    Escribir '```'
    $perdidas | ForEach-Object { Escribir $_ }
    Escribir '```'
} else {
    Escribir "Ninguna tabla perdio filas."
}
Escribir ""

# ── 5. EL RECORRIDO, QUE ES LO QUE ENCUENTRA LO CARO ────────────────────────────────────────────
#
# Esto NO se automatiza aqui a proposito. La aplicacion sigue levantada en $sonda y hay que
# recorrerla A MANO, con una cuenta REAL de la copia (la base no esta vacia, asi que no se siembra
# ningun administrador inicial y hay que entrar con alguien que ya exista).
Escribir "## Recorrido con datos reales"
Escribir ""
Escribir "La aplicacion sigue levantada. Entra y recorrela; el proceso es el $($api.Id)."
Escribir ""
Escribir "Lo que hay que mirar, por orden de lo que mas duele si falla:"
Escribir ""
Escribir "1. **ENTRAR.** Es donde aparecio el defecto del sello de sesion. Si el login falla, para"
Escribir "   aqui: el corte no se hace."
Escribir "2. Dar de alta el segundo factor de esa cuenta, que es lo que hara todo el equipo."
Escribir "3. Abrir cada modulo y mirar que las listas traen datos y no una pantalla vacia."
Escribir "4. Generar un documento (el organigrama en PDF sirve) y abrir una pantalla con imagenes."
Escribir "5. Comprobar que las integraciones responden con las credenciales de verdad."
Escribir ""
Escribir "**No dispares ningun despliegue.** La copia lleva las credenciales de los servidores de"
Escribir "produccion y la aplicacion puede alcanzarlos de verdad."
Escribir ""

# ── 6. Y ACORDARSE DE TIRAR LA COPIA ────────────────────────────────────────────────────────────
Escribir "---"
Escribir ""
Escribir "## Al terminar"
Escribir ""
Escribir "1. Detener la aplicacion: ``Stop-Process -Id $($api.Id)``"
Escribir "2. **Borrar la copia ``$nombreDeLaBase``**, y el .bacpac si lo hubo. No es solo el coste:"
Escribir "   una copia de produccion olvidada es una copia de los datos de todo el equipo —y de las"
Escribir "   credenciales de los servidores, que se descifran con el codigo de este repositorio—"
Escribir "   sin nadie vigilandola."
Escribir "   - En Azure: borrar la base desde el portal."
Escribir "   - En local: ``docker rm -f ensayo-sql``"
Escribir ""

($lineas -join "`n") | Set-Content $Informe -Encoding utf8
Write-Host ""
Write-Host "Informe escrito en $Informe"
