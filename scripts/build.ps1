<#
.SYNOPSIS
    Compila Datamart Analyzer: solución .NET (API + migraciones) y, opcionalmente,
    el frontend.

.DESCRIPTION
    Resuelve todas las rutas desde la ubicación del script, así que corre desde
    cualquier directorio. Al terminar deja un resumen con los errores agrupados
    por archivo - más legible que el volcado crudo de MSBuild.

.PARAMETER Que
    Qué compilar: Todo (default), Api, Migraciones, Frontend, Backend.

.PARAMETER Limpiar
    Borra bin/ y obj/ antes de compilar. Usalo cuando cambiaste paquetes o el
    build quedó en un estado raro.

.PARAMETER Restaurar
    Fuerza dotnet restore. Necesario la primera vez tras agregar un paquete.

.PARAMETER Release
    Compila en configuración Release en vez de Debug.

.PARAMETER Detallado
    Muestra la salida completa de MSBuild en lugar del resumen.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Que Api -Detallado

.EXAMPLE
    .\build.ps1 -Limpiar -Restaurar
#>

[CmdletBinding()]
param(
    [ValidateSet('Todo', 'Backend', 'Api', 'Migraciones', 'Frontend')]
    [string] $Que = 'Todo',

    [switch] $Limpiar,
    [switch] $Restaurar,
    [switch] $Release,
    [switch] $Detallado,

    # Por defecto se detiene el backend antes de compilar: si esta corriendo,
    # tiene bloqueado DatamartAnalyzer.Api.exe y el build falla con MSB3027.
    [switch] $NoDetener,

    # El script relanza el backend si el mismo lo detuvo. Con esto no lo hace.
    [switch] $NoReiniciar
)

$ErrorActionPreference = 'Stop'

# --- Rutas absolutas desde la ubicación del script ---------------------
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$Solucion    = Join-Path $RepoRoot 'DatamartAnalyzer.sln'
$ApiProject  = Join-Path $RepoRoot 'backend\DatamartAnalyzer.Api\DatamartAnalyzer.Api.csproj'
$MigProject  = Join-Path $RepoRoot 'backend\DatamartAnalyzer.Migrations\DatamartAnalyzer.Migrations.csproj'
$FrontDir    = Join-Path $RepoRoot 'frontend'
$Config      = if ($Release) { 'Release' } else { 'Debug' }

$inicio = Get-Date
$fallos = @()
$huboBloqueo = $false
$detuvimosBackend = $false

function Escribir-Titulo([string]$texto) {
    Write-Host ""
    Write-Host "-- $texto " -NoNewline -ForegroundColor Cyan
    Write-Host ("-" * [Math]::Max(0, 60 - $texto.Length)) -ForegroundColor DarkGray
}

# --- Detener el backend si esta corriendo -------------------------------
function Detener-Backend {
    # Solo procesos de ESTE repo: no se toca nada ajeno.
    $procesos = @(Get-Process -Name 'DatamartAnalyzer.Api' -ErrorAction SilentlyContinue |
                  Where-Object {
                      try { $_.Path -and $_.Path.StartsWith($RepoRoot, 'OrdinalIgnoreCase') }
                      catch { $false }   # sin permisos para leer Path
                  })

    if ($procesos.Count -eq 0) { return $false }

    Escribir-Titulo "Deteniendo backend"
    foreach ($p in $procesos) {
        Write-Host "  PID $($p.Id) - $($p.ProcessName)" -ForegroundColor DarkGray
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }

    # Windows libera el archivo con algo de retraso; sin esta espera el build
    # puede volver a chocar con el mismo bloqueo.
    Start-Sleep -Milliseconds 800
    Write-Host "  Detenido. Reinicialo con .\iniciar.bat cuando termines." -ForegroundColor DarkGray
    return $true
}

# --- Validaciones ------------------------------------------------------
if (-not (Test-Path $Solucion)) {
    Write-Host "No encuentro la solución en: $Solucion" -ForegroundColor Red
    Write-Host "Este script debe vivir en <repo>\scripts\build.ps1" -ForegroundColor Yellow
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "'dotnet' no está en el PATH. Instalá el .NET 9 SDK." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Datamart Analyzer - build" -ForegroundColor Cyan
Write-Host "Repo          : $RepoRoot"
Write-Host "Configuración : $Config"
Write-Host "Objetivo      : $Que"

if (-not $NoDetener) { $detuvimosBackend = Detener-Backend }

# --- Limpiar -----------------------------------------------------------
if ($Limpiar) {
    Escribir-Titulo "Limpiando"
    Get-ChildItem -Path (Join-Path $RepoRoot 'backend') -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "  $($_.FullName.Replace($RepoRoot, '.'))" -ForegroundColor DarkGray
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    Write-Host "  Listo." -ForegroundColor DarkGray
}

# --- Restaurar ---------------------------------------------------------
# Si un .csproj cambio despues del ultimo restore, project.assets.json quedo viejo.
# Sin esto, --no-restore reutiliza el grafo anterior y siguen apareciendo avisos de
# paquetes que ya no estan en el csproj (NU1603 y compania).
function Restore-Desactualizado {
    foreach ($proj in @($ApiProject, $MigProject)) {
        if (-not (Test-Path $proj)) { continue }

        $assets = Join-Path (Split-Path -Parent $proj) 'obj\project.assets.json'
        if (-not (Test-Path $assets)) { return $true }

        if ((Get-Item $proj).LastWriteTime -gt (Get-Item $assets).LastWriteTime) {
            Write-Host ""
            Write-Host "  $(Split-Path -Leaf $proj) cambio desde el ultimo restore." -ForegroundColor DarkYellow
            return $true
        }
    }
    return $false
}

if (-not ($Restaurar -or $Limpiar)) {
    if (Restore-Desactualizado) { $Restaurar = $true }
}

if ($Restaurar -or $Limpiar) {
    Escribir-Titulo "Restaurando paquetes"
    dotnet restore $Solucion --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Falló el restore. Sin red o paquete inexistente." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# --- Compilar .NET -----------------------------------------------------
function Compilar-Proyecto([string]$nombre, [string]$ruta) {
    Escribir-Titulo "Compilando $nombre"

    $argumentos = @($ruta, '--configuration', $Config, '--nologo')
    if (-not ($Restaurar -or $Limpiar)) { $argumentos += '--no-restore' }
    if (-not $Detallado) { $argumentos += @('--verbosity', 'minimal') }

    $anterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $salida = & dotnet build @argumentos 2>&1
    $codigo = $LASTEXITCODE
    $ErrorActionPreference = $anterior

    if ($Detallado) {
        $salida | ForEach-Object { Write-Host $_ }
    }

    # Errores y advertencias, sin duplicados
    $errores = $salida | Select-String -Pattern ': error ' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique
    $avisos  = $salida | Select-String -Pattern ': warning ' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique

    if ($codigo -eq 0) {
        Write-Host "  OK" -NoNewline -ForegroundColor Green
        if ($avisos.Count -gt 0) {
            Write-Host " - $($avisos.Count) advertencia(s)" -ForegroundColor Yellow
            if (-not $Detallado) {
                $avisos | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
                if ($avisos.Count -gt 5) { Write-Host "    ... y $($avisos.Count - 5) más (usá -Detallado)" -ForegroundColor DarkGray }
            }
        } else {
            Write-Host ""
        }
        return $true
    }

    if ($errores -match 'MSB302[17]') { $script:huboBloqueo = $true }

    Write-Host "  FALLÓ - $($errores.Count) error(es)" -ForegroundColor Red
    if (-not $Detallado) {
        $errores | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    }
    $script:fallos += $nombre
    return $false
}

switch ($Que) {
    'Todo'        { Compilar-Proyecto 'API'         $ApiProject | Out-Null
                    Compilar-Proyecto 'Migraciones' $MigProject | Out-Null }
    'Backend'     { Compilar-Proyecto 'API'         $ApiProject | Out-Null
                    Compilar-Proyecto 'Migraciones' $MigProject | Out-Null }
    'Api'         { Compilar-Proyecto 'API'         $ApiProject | Out-Null }
    'Migraciones' { Compilar-Proyecto 'Migraciones' $MigProject | Out-Null }
}

# --- Compilar frontend -------------------------------------------------
if ($Que -in @('Todo', 'Frontend')) {
    if (-not (Test-Path (Join-Path $FrontDir 'package.json'))) {
        Write-Host ""
        Write-Host "Sin frontend/package.json - se omite." -ForegroundColor DarkGray
    }
    elseif (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Host ""
        Write-Host "'npm' no está en el PATH - se omite el frontend." -ForegroundColor Yellow
    }
    else {
        Escribir-Titulo "Compilando Frontend"
        Push-Location $FrontDir
        # npm manda avisos por stderr (Browserslist, deprecaciones). Con
        # ErrorActionPreference='Stop' eso se vuelve error terminante y tumba un
        # build que en realidad salio bien. Se afloja solo para este tramo.
        $eapFront = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            if (-not (Test-Path (Join-Path $FrontDir 'node_modules'))) {
                Write-Host "  node_modules ausente, instalando..." -ForegroundColor DarkGray
                npm install --silent
                if ($LASTEXITCODE -ne 0) { throw "npm install falló" }
            }

            npm run build
            if ($LASTEXITCODE -ne 0) { throw "npm run build falló" }

            Write-Host "  OK" -ForegroundColor Green
        }
        catch {
            Write-Host "  FALLÓ - $_" -ForegroundColor Red
            $fallos += 'Frontend'
        }
        finally { $ErrorActionPreference = $eapFront; Pop-Location }
    }
}

# --- Resumen -----------------------------------------------------------
$duracion = [Math]::Round(((Get-Date) - $inicio).TotalSeconds, 1)

Write-Host ""
Write-Host ("-" * 64) -ForegroundColor DarkGray

if ($fallos.Count -eq 0) {
    Write-Host "Build correcto en $duracion s." -ForegroundColor Green

    # Si lo detuvimos nosotros, lo devolvemos a como estaba: dejarlo caido hace
    # que los endpoints respondan 500 y parezca un error de la aplicacion.
    if ($detuvimosBackend -and -not $NoReiniciar) {
        $backendDir = Join-Path $RepoRoot 'backend\DatamartAnalyzer.Api'
        Write-Host ""
        Write-Host "Relanzando el backend..." -ForegroundColor DarkGray
        $cmdArgs = '/k title Datamart-Backend && cd /d "' + $backendDir +
                   '" && set ASPNETCORE_ENVIRONMENT=Development&& dotnet run'
        Start-Process 'cmd.exe' -ArgumentList $cmdArgs
        Write-Host "  Levantando en http://localhost:5000 (ventana 'Datamart-Backend')." -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "Siguiente:" -ForegroundColor DarkGray
    Write-Host "  .\scripts\almacen.ps1 -List        ver datamarts y estado del almacén" -ForegroundColor DarkGray
    Write-Host "  .\iniciar.bat                      levantar backend y frontend" -ForegroundColor DarkGray
    exit 0
}

Write-Host "Falló: $($fallos -join ', ')  ($duracion s)" -ForegroundColor Red
Write-Host ""
if ($script:huboBloqueo) {
    Write-Host "El .exe estaba bloqueado por un proceso en ejecucion (MSB3027)." -ForegroundColor Yellow
    Write-Host "No es un error de codigo: la compilacion paso, fallo al copiar." -ForegroundColor Yellow
    Write-Host "Cerra la ventana del backend y volve a correr el script." -ForegroundColor Yellow
} else {
    Write-Host "Proba:" -ForegroundColor DarkGray
    Write-Host "  .\scripts\build.ps1 -Detallado           salida completa de MSBuild" -ForegroundColor DarkGray
    Write-Host "  .\scripts\build.ps1 -Limpiar -Restaurar  si cambiaste paquetes" -ForegroundColor DarkGray
}
exit 1
