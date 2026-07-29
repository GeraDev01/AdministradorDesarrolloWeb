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
#>
#requires -PSEdition Desktop
[CmdletBinding()]
param(
    [string] $ConnectionString,
    [string] $OutputDir = "$PSScriptRoot\..\..\dist\desarrollador",
    [switch] $SkipConnectionTest,
    [switch] $KeepResource
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
if (-not $SkipConnectionTest) {
    Write-Paso 'Probando la conexión'
    $conn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
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
finally {
    # El recurso lleva credenciales: no debe quedarse en el árbol de fuentes.
    if (-not $KeepResource -and (Test-Path $recurso)) {
        Remove-Item $recurso -Force
        Write-Host "`n  Recurso temporal eliminado del repositorio." -ForegroundColor DarkGray
    }
}

# ── 5. Resultado ───────────────────────────────────────────────────────────────
$exe = Get-ChildItem $OutputDir -Filter '*.exe' | Select-Object -First 1
Write-Paso 'Listo'
Write-Host "  $($exe.FullName)"
Write-Host "  $([Math]::Round($exe.Length / 1MB, 1)) MB — un solo archivo, sin instalación ni configuración."
Write-Host "`n  Repártelo a todo el equipo. Cada quien inicia sesión con su usuario y ve" -ForegroundColor DarkGray
Write-Host "  el menú que le corresponde según su rol." -ForegroundColor DarkGray
