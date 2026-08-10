<#
.SYNOPSIS
    Instala o actualiza el almacén de consultas y dashboards (esquema ADP_DTM_APP)
    dentro de las bases de datamart de YOSEMITE.

.DESCRIPTION
    Envuelve el proyecto DatamartAnalyzer.Migrations resolviendo rutas absolutas
    desde la ubicación del script, así funciona desde cualquier directorio.

    El caso típico -se creó un datamart nuevo- se resuelve con -Pending: detecta
    qué bases todavía no tienen el almacén y solo instala en esas.

    Es idempotente: DbUp lleva su propia tabla SchemaVersions dentro de cada base,
    así que volver a ejecutarlo no repite migraciones ya aplicadas.

.PARAMETER List
    Muestra los datamarts detectados y el estado del almacén en cada uno.

.PARAMETER Pending
    Instala solo en los datamarts que aún no tienen el almacén. Para cuando se crea
    una base nueva.

.PARAMETER Database
    Instala en una base puntual.

.PARAMETER All
    Aplica migraciones pendientes en todos los datamarts.

.PARAMETER Simular
    Muestra qué se aplicaría sin tocar nada (--what-if del runner).

.PARAMETER SkipBuild
    No recompila el proyecto de migraciones antes de correr.

.PARAMETER Connection
    Cadena de conexión alternativa al servidor. Por defecto usa la de appsettings.json.

.EXAMPLE
    .\almacen.ps1 -List

.EXAMPLE
    # Se creó un datamart nuevo: instalar solo donde falta
    .\almacen.ps1 -Pending

.EXAMPLE
    .\almacen.ps1 -Database ADP_DTM_NORTE -Simular

.EXAMPLE
    .\almacen.ps1 -Database ADP_DTM_NORTE
#>

[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')]
    [switch] $List,

    [Parameter(ParameterSetName = 'Pending')]
    [switch] $Pending,

    [Parameter(ParameterSetName = 'One', Mandatory = $true)]
    [string] $Database,

    [Parameter(ParameterSetName = 'All')]
    [switch] $All,

    [switch] $Simular,
    [switch] $SkipBuild,
    [string] $Connection
)

$ErrorActionPreference = 'Stop'

# --- Rutas absolutas resueltas desde la ubicación del script -----------
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$MigProject = Join-Path $RepoRoot 'backend\DatamartAnalyzer.Migrations\DatamartAnalyzer.Migrations.csproj'

if (-not (Test-Path $MigProject)) {
    Write-Host "No encuentro el proyecto de migraciones en:" -ForegroundColor Red
    Write-Host "  $MigProject" -ForegroundColor Red
    Write-Host "Este script debe vivir en <repo>\scripts\almacen.ps1" -ForegroundColor Yellow
    exit 1
}

# --- Verificar dotnet --------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "'dotnet' no está en el PATH. Instalá el .NET 9 SDK." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Almacén Datamart Analyzer" -ForegroundColor Cyan
Write-Host "Repo: $RepoRoot"
Write-Host ""

# --- Compilar ----------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "Compilando el proyecto de migraciones..." -ForegroundColor DarkGray
    dotnet build $MigProject --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "El build falló. Revisá los errores arriba." -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host ""
}

# --- Armar los argumentos del runner -----------------------------------
$runnerArgs = @()

switch ($PSCmdlet.ParameterSetName) {
    'List'    { $runnerArgs += '--list' }
    'Pending' { $runnerArgs += '--pending' }
    'One'     { $runnerArgs += @('--database', $Database) }
    'All'     { $runnerArgs += '--all' }
}

if ($Simular)    { $runnerArgs += '--what-if' }
if ($Connection) { $runnerArgs += @('--connection', $Connection) }

# --- Ejecutar ----------------------------------------------------------
dotnet run --project $MigProject --no-build -- @runnerArgs
$exit = $LASTEXITCODE

Write-Host ""
if ($exit -eq 0) {
    switch ($PSCmdlet.ParameterSetName) {
        'One'     { if (-not $Simular) { Write-Host "Reiniciá el backend para que tome la base nueva." -ForegroundColor DarkGray } }
        'Pending' { if (-not $Simular) { Write-Host "Reiniciá el backend para que tome las bases nuevas." -ForegroundColor DarkGray } }
    }
} else {
    Write-Host "Terminó con errores (código $exit)." -ForegroundColor Red
    Write-Host "Si es por permisos, se necesita CREATE SCHEMA y CREATE TABLE sobre esa base." -ForegroundColor Yellow
}

exit $exit
