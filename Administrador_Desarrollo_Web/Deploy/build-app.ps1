<#
.SYNOPSIS
    Publica la aplicación en un solo .exe autocontenido con la conexión a la base ya incrustada.

.DESCRIPTION
    Es la MISMA aplicación para todo el equipo: el menú y los permisos dependen del rol de la
    cuenta con la que cada quien inicia sesión (Desarrollador ve lo suyo, Admin lo ve todo). Lo
    único que aporta este empaquetado es que nadie tenga que capturar la conexión a mano.

    La conexión incrustada es un VALOR POR DEFECTO: si el equipo ya tiene configuración propia
    (Configuración → Base de datos), esa gana. Así el administrador puede apuntar a otra base o
    usar credenciales con más permisos sin necesitar un ejecutable distinto.

    Por omisión toma la connection string que TÚ tengas configurada en este equipo
    (%APPDATA%\AdministradorDesarrolloWeb\dbprovider.json). Puedes pasar otra con -ConnectionString.

    IMPORTANTE: la cadena queda incrustada en el .exe con cifrado simétrico cuya llave está en el
    propio binario. Eso es OFUSCACIÓN, no seguridad: cualquiera con el ejecutable puede recuperar
    la contraseña. Usa un login de SQL restringido — ver crear-login-desarrollador.sql.

.EXAMPLE
    .\build-app.ps1
    Usa tu conexión actual.

.EXAMPLE
    .\build-app.ps1 -ConnectionString "server=...;uid=app_dev;pwd=...;database=SOLTUM_DEV_WD"
    Usa el login restringido (lo recomendado).

.NOTES
    Requiere WINDOWS POWERSHELL 5.1 (edición Desktop): usa System.Data.SqlClient (prueba de conexión)
    y DPAPI ProtectedData (leer la conexión local), tipos del .NET Framework que PowerShell 7 no trae.
    Ejecútalo con 'powershell.exe', no con 'pwsh'.

    Ese System.Data.SqlClient es el proveedor VIEJO y no entiende todas las palabras clave que
    escribe el nuevo (Microsoft.Data.SqlClient), que es el que usa la aplicación. La prueba de
    conexión traduce la cadena antes de abrirla — ver ConvertTo-CadenaDePrueba. Lo que se incrusta
    en el .exe es siempre la cadena original.
#>
#requires -PSEdition Desktop
[CmdletBinding()]
param(
    [string] $ConnectionString,
    [string] $OutputDir = "$PSScriptRoot\..\..\dist\desarrollador",
    [switch] $SkipConnectionTest,
    [switch] $KeepResource,

    # ── Firma digital (Authenticode) ────────────────────────────────────────────
    # Es lo ÚNICO que quita de verdad el aviso «Windows protegió su PC». Sin firma, cada
    # actualización vuelve a empezar de cero en reputación. Ver LEEME.md.
    #   -SignThumbprint  huella del certificado en el almacén del usuario (lo normal si el
    #                    certificado está en un token USB o ya importado en Windows)
    #   -SignPfx         archivo .pfx en disco (+ -SignPfxPassword)
    [string] $SignThumbprint,
    [string] $SignPfx,
    [string] $SignPfxPassword,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',

    # ── Actualización automática (Velopack) ─────────────────────────────────────
    # Con -Velopack se publica en carpeta (no en .exe único) y se genera con vpk un instalador
    # más el paquete de actualización. Ver LEEME.md.
    [switch] $Velopack,
    [string] $VelopackVersion,
    [string] $VelopackFeedDir,
    [string] $VelopackChannel = 'win'
)

$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '..\Administrador_Desarrollo_Web.csproj'
$recurso  = Join-Path $PSScriptRoot 'devbuild.bin'

# Debe coincidir con EmbeddedDbConfig.KeySeed en Data\EmbeddedDbConfig.cs (lo verifica un test).
$KeySeed = 'Administrador_Desarrollo_Web::DevBuild::v1'

function Write-Paso($texto) { Write-Host "`n=== $texto" -ForegroundColor Cyan }

# ── 1. Obtener la connection string ─────────────────────────────────────────────
Write-Paso 'Resolviendo la connection string'

if (-not $ConnectionString) {
    $cfgPath = Join-Path $env:APPDATA 'AdministradorDesarrolloWeb\dbprovider.json'
    if (-not (Test-Path $cfgPath)) {
        throw "No hay conexión configurada en este equipo ($cfgPath). Pasa -ConnectionString explícitamente."
    }

    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($cfg.Provider -ne 'SqlServer') {
        throw "Tu configuración actual usa '$($cfg.Provider)', no SQL Server. Configura SQL Server en la app o pasa -ConnectionString."
    }

    Add-Type -AssemblyName System.Security
    $cifrado = [Convert]::FromBase64String($cfg.SqlServerConnection)
    $ConnectionString = [Text.Encoding]::UTF8.GetString(
        [Security.Cryptography.ProtectedData]::Unprotect($cifrado, $null, 'CurrentUser'))
    Write-Host "  Tomada de tu configuración local." -ForegroundColor DarkGray
}

$enmascarada = [Regex]::Replace($ConnectionString, '(?i)(pwd|password)\s*=\s*[^;]*', '$1=********')
Write-Host "  $enmascarada"

if ($ConnectionString -match '(?i)(^|;)\s*(uid|user\s*id)\s*=\s*(sa|soltum)\s*(;|$)') {
    Write-Warning "Vas a incrustar una cuenta con permisos amplios. Se recomienda un login restringido (crear-login-desarrollador.sql)."
}

# ── 2. Probar que la conexión sirve antes de repartirla ─────────────────────────

<#
.SYNOPSIS
    Adapta la connection string al proveedor VIEJO de SQL, solo para poder probarla aquí.

.DESCRIPTION
    La aplicación corre sobre Microsoft.Data.SqlClient (EF Core) y guarda la cadena en la forma
    canónica de ESE proveedor, que escribe algunas palabras clave separadas: «Trust Server
    Certificate». Windows PowerShell 5.1 solo trae el System.Data.SqlClient del .NET Framework,
    donde esa misma opción se llama «TrustServerCertificate» y la forma con espacios se rechaza con
    «Palabra clave no admitida», sin haber intentado conectar siquiera.

    Por eso cada palabra clave se prueba tal cual y, si el proveedor viejo no la conoce, otra vez
    sin espacios. Lo que sigue sin reconocer (p. ej. «Command Timeout», que solo existe en el
    proveedor nuevo) se omite de la PRUEBA: son ajustes que no cambian si el servidor responde.

    Lo que se incrusta en el .exe es siempre la cadena ORIGINAL, sin tocar: esto no la modifica.
#>
function ConvertTo-CadenaDePrueba {
    param([Parameter(Mandatory)] [string] $Cadena)

    # DbConnectionStringBuilder admite cualquier palabra clave (no valida ninguna) y respeta las
    # comillas, así que parte la cadena sin romper una contraseña que lleve «;» o «=» dentro.
    #
    # psbase en cada acceso: estos builders implementan IDictionary y PowerShell antepone el
    # diccionario a las propiedades reales. Sin psbase, «$b.ConnectionString = ...» crea una ENTRADA
    # llamada ConnectionString en lugar de asignar la propiedad, y la cadena resultante sale vacía.
    $origen = New-Object System.Data.Common.DbConnectionStringBuilder
    try { $origen.psbase.ConnectionString = $Cadena }
    catch { throw "La connection string no tiene un formato válido: $($_.Exception.Message)" }

    $destino = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $omitidas = @()
    foreach ($clave in @($origen.psbase.Keys)) {
        $valor = $origen.psbase.Item($clave)
        $puesta = $false
        foreach ($variante in @($clave, ($clave -replace '\s', ''))) {
            try { $destino.psbase.Item($variante) = $valor; $puesta = $true; break } catch { }
        }
        if (-not $puesta) { $omitidas += $clave }
    }

    [pscustomobject]@{ Cadena = $destino.psbase.ConnectionString; Omitidas = @($omitidas) }
}

if (-not $SkipConnectionTest) {
    Write-Paso 'Probando la conexión'

    $prueba = ConvertTo-CadenaDePrueba -Cadena $ConnectionString
    if ($prueba.Omitidas.Count -gt 0) {
        Write-Host "  (solo para esta prueba se omiten, por ser del proveedor nuevo: $($prueba.Omitidas -join ', '))" -ForegroundColor DarkGray
    }

    $conn = New-Object System.Data.SqlClient.SqlConnection $prueba.Cadena
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = 'SELECT DB_NAME(), SUSER_SNAME()'
        $r = $cmd.ExecuteReader()
        [void]$r.Read()
        Write-Host "  OK -> base '$($r.GetString(0))' como '$($r.GetString(1))'" -ForegroundColor Green
        $r.Close()
    }
    catch { throw "La conexión falló, no tiene caso publicar: $($_.Exception.Message)" }
    finally { $conn.Dispose() }
}

# ── 3. Generar el recurso incrustado (AES-256-CBC, IV al frente) ────────────────
Write-Paso 'Generando el recurso incrustado'

# SHA256::HashData es .NET 5+; Windows PowerShell 5.1 corre sobre .NET Framework y no lo tiene, así
# que se usa ComputeHash (existe en ambas ediciones). El script exige de todos modos Windows
# PowerShell 5.1 (ver #requires arriba) por SqlClient y DPAPI.
$sha = [Security.Cryptography.SHA256]::Create()
try   { $llave = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($KeySeed)) }
finally { $sha.Dispose() }

$aes = [Security.Cryptography.Aes]::Create()
try {
    $aes.Key     = $llave
    $aes.Mode    = [Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    $aes.GenerateIV()

    $plano   = [Text.Encoding]::UTF8.GetBytes($ConnectionString.Trim())
    $cifrado = $aes.CreateEncryptor().TransformFinalBlock($plano, 0, $plano.Length)
    [IO.File]::WriteAllBytes($recurso, $aes.IV + $cifrado)
    Write-Host "  $recurso ($((Get-Item $recurso).Length) bytes)"
}
finally { $aes.Dispose() }

# ── 4. Publicar ────────────────────────────────────────────────────────────────
try {
    Write-Paso 'Publicando (esto tarda: incluye el runtime de .NET)'
    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

    if ($Velopack) {
        # SIN PublishSingleFile: vpk empaqueta una CARPETA, y las actualizaciones delta comparan
        # archivo por archivo. Con todo dentro de un .exe, cada actualización bajaría los 150 MB
        # completos y se perdería la única ventaja de las deltas.
        dotnet publish $proyecto `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:EmbedDbConnection=true `
            -p:DebugType=none `
            -o $OutputDir

        if ($LASTEXITCODE -ne 0) { throw "dotnet publish devolvió $LASTEXITCODE" }
    }
    else {
        # IncludeAllContentForSelfExtract mete también Plantillas\*.docx y las librerías nativas
        # (SQLite, WebView2) dentro del .exe; sin eso "un solo archivo" no sería cierto.
        dotnet publish $proyecto `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:EmbedDbConnection=true `
            -p:PublishSingleFile=true `
            -p:IncludeAllContentForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=none `
            -o $OutputDir

        if ($LASTEXITCODE -ne 0) { throw "dotnet publish devolvió $LASTEXITCODE" }
    }
}
finally {
    # El recurso lleva credenciales: no debe quedarse en el árbol de fuentes.
    if (-not $KeepResource -and (Test-Path $recurso)) {
        Remove-Item $recurso -Force
        Write-Host "`n  Recurso temporal eliminado del repositorio." -ForegroundColor DarkGray
    }
}

$exe = Get-ChildItem $OutputDir -Filter 'Administrador_Desarrollo_Web.exe' |
       Select-Object -First 1
if (-not $exe) { $exe = Get-ChildItem $OutputDir -Filter '*.exe' | Select-Object -First 1 }

# ── 5. Firmar ──────────────────────────────────────────────────────────────────
# Sin firma el .exe funciona igual, así que esto es opcional a propósito: quien no tenga
# certificado sigue pudiendo publicar. Pero mientras no se firme, cada equipo que lo descargue
# verá el aviso de SmartScreen y algunos antivirus lo pondrán en cuarentena.
if ($SignThumbprint -or $SignPfx) {
    Write-Paso 'Firmando el ejecutable'

    # signtool.exe viene con el Windows SDK y no está en el PATH. Se toma la versión más nueva.
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) {
        throw "No se encontró signtool.exe. Instala el Windows SDK (componente 'Windows SDK Signing Tools')."
    }

    # /fd y /td sha256: SHA-1 lleva años sin ser aceptado.
    # /tr (sello de tiempo): sin él, la firma deja de valer el día que expire el certificado, y
    # con ella los .exe ya repartidos empezarían a marcarse como no firmados.
    $firmaArgs = @('sign', '/fd', 'sha256', '/tr', $TimestampUrl, '/td', 'sha256')
    if ($SignThumbprint) {
        $firmaArgs += @('/sha1', $SignThumbprint)
    } else {
        if (-not (Test-Path $SignPfx)) { throw "No existe el .pfx: $SignPfx" }
        $firmaArgs += @('/f', $SignPfx)
        if ($SignPfxPassword) { $firmaArgs += @('/p', $SignPfxPassword) }
    }
    $firmaArgs += $exe.FullName

    & $signtool.FullName @firmaArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool devolvió $LASTEXITCODE" }

    # Que quede constancia de que la firma es verificable, no solo de que signtool no falló.
    $firma = Get-AuthenticodeSignature $exe.FullName
    if ($firma.Status -ne 'Valid') { throw "La firma quedó en estado '$($firma.Status)'." }
    Write-Host "  Firmado por: $($firma.SignerCertificate.Subject)" -ForegroundColor Green
}
else {
    Write-Host "`n  AVISO: el .exe NO va firmado. Windows mostrará 'Windows protegió su PC'" -ForegroundColor Yellow
    Write-Host "  la primera vez que cada quien lo abra. Ver Deploy\LEEME.md." -ForegroundColor Yellow
}

# ── 6. Empaquetar con Velopack ─────────────────────────────────────────────────
if ($Velopack) {
    Write-Paso 'Empaquetando con Velopack'

    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "No se encontró 'vpk'. Instálalo una vez con:  dotnet tool install -g vpk"
    }

    # La versión sale del .csproj si no se pasa: así no hay dos números que mantener sincronizados
    # a mano (y un desajuste haría que el aviso de versión no cuadre con lo publicado).
    $version = $VelopackVersion
    if (-not $version) {
        $version = ([xml](Get-Content $proyecto)).Project.PropertyGroup.Version |
                   Where-Object { $_ } | Select-Object -First 1
        if (-not $version) { throw "No se pudo leer <Version> del .csproj. Pasa -VelopackVersion." }
        Write-Host "  Versión tomada del .csproj: $version" -ForegroundColor DarkGray
    }

    $feedDir = $VelopackFeedDir
    if (-not $feedDir) { $feedDir = Join-Path (Split-Path $OutputDir -Parent) 'velopack' }
    New-Item -ItemType Directory -Force -Path $feedDir | Out-Null

    # -o es el feed: vpk lee de ahí las versiones ANTERIORES para generar el paquete delta. Si se
    # apunta a una carpeta vacía, la primera entrega es completa y las siguientes ya son deltas.
    $vpkArgs = @(
        'pack',
        '--packId',      'AdministradorDesarrolloWeb',
        '--packVersion', $version,
        '--packDir',     $OutputDir,
        '--mainExe',     'Administrador_Desarrollo_Web.exe',
        '--packTitle',   'Administrador de Desarrollo',
        '--packAuthors', 'Soltum',
        '--channel',     $VelopackChannel,
        '-o',            $feedDir
    )
    $icono = Join-Path $PSScriptRoot '..\Assets\app.ico'
    if (Test-Path $icono) { $vpkArgs += @('--icon', (Resolve-Path $icono).Path) }
    # La firma se le pasa a vpk para que firme TAMBIÉN el instalador y el updater, no solo el .exe.
    if ($SignThumbprint) {
        $vpkArgs += @('--signParams', "/fd sha256 /tr $TimestampUrl /td sha256 /sha1 $SignThumbprint")
    }
    elseif ($SignPfx) {
        # Las comillas internas van escapadas como \" : Windows PowerShell 5.1 NO las escapa al
        # invocar un ejecutable nativo, y sin la barra el argumento se parte en dos y vpk aborta
        # con «Unrecognized command or argument». Hacen falta porque la ruta del .pfx puede llevar
        # espacios.
        $sp = "/fd sha256 /tr $TimestampUrl /td sha256 /f \`"$SignPfx\`""
        if ($SignPfxPassword) { $sp += " /p $SignPfxPassword" }
        $vpkArgs += @('--signParams', $sp)
    }

    & vpk @vpkArgs
    if ($LASTEXITCODE -ne 0) { throw "vpk devolvió $LASTEXITCODE" }

    Write-Paso 'Listo (Velopack)'
    Write-Host "  Feed: $feedDir"
    Get-ChildItem $feedDir | Sort-Object Name | ForEach-Object {
        Write-Host ("    {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
    }
    Write-Host "`n  Sube TODO el contenido de esa carpeta al mismo sitio (Blob o carpeta de red)" -ForegroundColor DarkGray
    Write-Host "  y captura esa ubicación en Configuración → Aviso de versión nueva → Feed." -ForegroundColor DarkGray
    Write-Host "  La primera vez, cada quien instala con el Setup.exe; de ahí en adelante se" -ForegroundColor DarkGray
    Write-Host "  actualiza solo." -ForegroundColor DarkGray
    return
}

# ── 7. Resultado ───────────────────────────────────────────────────────────────
Write-Paso 'Listo'
Write-Host "  $($exe.FullName)"
Write-Host "  $([Math]::Round($exe.Length / 1MB, 1)) MB — un solo archivo, sin instalación ni configuración."
Write-Host "`n  Repártelo a todo el equipo. Cada quien inicia sesión con su usuario y ve" -ForegroundColor DarkGray
Write-Host "  el menú que le corresponde según su rol." -ForegroundColor DarkGray
