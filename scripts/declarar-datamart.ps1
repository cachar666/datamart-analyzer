<#
.SYNOPSIS
    Declara en dbo.ADPConfig de un ERP cual es su datamart.

.DESCRIPTION
    Escribe las cuatro claves que la API lee para resolver el datamart:

        DATAMART      base de datos      (obligatoria)
        DATAMARTSRV   servidor           (omitir = el mismo del ERP)
        DATAMARTUSR   usuario SQL        (omitir = autenticacion de Windows)
        DATAMARTPWD   clave SQL

    Los parametros que no pases se ELIMINAN de ADPConfig, para que no quede un
    servidor o un usuario viejo apuntando a ningun lado.

    Solo toca esas cuatro claves; el resto de la configuracion del ERP no se toca.

.PARAMETER ErpServidor
    Servidor donde vive la base del ERP. Ej: Cumbal-V01

.PARAMETER ErpBase
    Base de datos del ERP. Ej: Sinco

.PARAMETER Datamart
    Base de datos del datamart. Ej: SincoEthussTCoindustrialDW

.PARAMETER DatamartServidor
    Servidor del datamart. Omitir si esta en el mismo servidor del ERP.
    Ej: datamart.sincoerp.com,4263

.PARAMETER Usuario
    Usuario SQL del datamart. Omitir para autenticacion de Windows.

.PARAMETER Clave
    Clave SQL del datamart.

.PARAMETER Mostrar
    Solo muestra que hay declarado hoy, sin escribir nada.

.PARAMETER Quitar
    Elimina las cuatro claves.

.EXAMPLE
    # Ver que hay
    .\declarar-datamart.ps1 -ErpServidor Cumbal-V01 -ErpBase Sinco -Mostrar

.EXAMPLE
    # Datamart en el mismo servidor, Windows auth
    .\declarar-datamart.ps1 -ErpServidor Cumbal-V01 -ErpBase Sinco -Datamart SincoEthussTCoindustrialDW

.EXAMPLE
    # Datamart publicado, autenticacion SQL
    .\declarar-datamart.ps1 -ErpServidor Cumbal-V01 -ErpBase Sinco `
        -Datamart SincoEthussTCoindustrialDW `
        -DatamartServidor 'datamart.sincoerp.com,4263' `
        -Usuario UsuSincoEthussTCoindustrialDW -Clave '********'
#>

[CmdletBinding(DefaultParameterSetName = 'Declarar')]
param(
    [Parameter(Mandatory = $true)] [string] $ErpServidor,
    [Parameter(Mandatory = $true)] [string] $ErpBase,

    [Parameter(ParameterSetName = 'Declarar')] [string] $Datamart,
    [Parameter(ParameterSetName = 'Declarar')] [string] $DatamartServidor,
    [Parameter(ParameterSetName = 'Declarar')] [string] $Usuario,
    [Parameter(ParameterSetName = 'Declarar')] [string] $Clave,

    [Parameter(ParameterSetName = 'Mostrar')] [switch] $Mostrar,
    [Parameter(ParameterSetName = 'Quitar')]  [switch] $Quitar
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host "'sqlcmd' no esta en el PATH. Instala las herramientas de linea de comandos de SQL Server." -ForegroundColor Red
    exit 1
}

# Escapa comillas simples para T-SQL. NULL de PowerShell -> NULL de SQL.
function Sql-Texto([string]$v) {
    if ([string]::IsNullOrWhiteSpace($v)) { return 'NULL' }
    return "N'" + $v.Replace("'", "''") + "'"
}

function Invocar([string]$sql) {
    $salida = sqlcmd -S $ErpServidor -E -d $ErpBase -b -Q $sql 2>&1
    $codigo = $LASTEXITCODE
    $salida | ForEach-Object { Write-Host $_ }
    if ($codigo -ne 0) {
        Write-Host ""
        Write-Host "sqlcmd termino con codigo $codigo." -ForegroundColor Red
        Write-Host "Si dice 'Cannot connect', revisa la VPN hacia $ErpServidor." -ForegroundColor Yellow
        exit $codigo
    }
}

$consultaEstado = @"
SET NOCOUNT ON;
SELECT CnfCodigo,
       CASE WHEN CnfCodigo = 'DATAMARTPWD' THEN '********' ELSE CnfValor END AS Valor,
       CnfTipoDatos, CnfFechaMod
FROM dbo.ADPConfig
WHERE CnfCodigo IN ('DATAMART','DATAMARTSRV','DATAMARTUSR','DATAMARTPWD')
ORDER BY CnfCodigo;
"@

Write-Host ""
Write-Host "ERP: $ErpServidor / $ErpBase" -ForegroundColor Cyan
Write-Host ""

# --- Solo mostrar ------------------------------------------------------------
if ($Mostrar) {
    Write-Host "Declarado hoy:" -ForegroundColor DarkGray
    Invocar $consultaEstado
    exit 0
}

# --- Quitar ------------------------------------------------------------------
if ($Quitar) {
    Invocar @"
SET NOCOUNT ON;
DELETE FROM dbo.ADPConfig
WHERE CnfCodigo IN ('DATAMART','DATAMARTSRV','DATAMARTUSR','DATAMARTPWD');
PRINT 'Claves eliminadas: ' + CAST(@@ROWCOUNT AS varchar(10));
"@
    exit 0
}

# --- Declarar ----------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Datamart)) {
    Write-Host "Falta -Datamart (la base del datamart)." -ForegroundColor Red
    Write-Host "Usa -Mostrar para ver que hay declarado, o -Quitar para limpiar." -ForegroundColor Yellow
    exit 1
}

if ($Usuario -and -not $Clave) {
    Write-Host "Declaraste -Usuario sin -Clave: la API no podria conectarse." -ForegroundColor Red
    exit 1
}

$vDatamart = Sql-Texto $Datamart
$vServidor = Sql-Texto $DatamartServidor
$vUsuario  = Sql-Texto $Usuario
$vClave    = Sql-Texto $Clave

# CnfSesion y CnfTipoDatos son NOT NULL. Se toma el CnfTipoDatos dominante de la
# tabla en vez de inventar uno: en Cumbal-V01/Sinco eso da 'alfa' (1917 de 1997).
$sql = @"
SET NOCOUNT ON;

DECLARE @TipoDatos varchar(50) =
    ISNULL((SELECT TOP 1 CnfTipoDatos FROM dbo.ADPConfig
            GROUP BY CnfTipoDatos ORDER BY COUNT(*) DESC), 'alfa');

DECLARE @Valores TABLE (Clave varchar(20), Valor varchar(800), Descripcion varchar(200));
INSERT INTO @Valores VALUES
    ('DATAMART',    $vDatamart, 'Datamart de este ERP - base de datos'),
    ('DATAMARTSRV', $vServidor, 'Datamart de este ERP - servidor'),
    ('DATAMARTUSR', $vUsuario,  'Datamart de este ERP - usuario SQL'),
    ('DATAMARTPWD', $vClave,    'Datamart de este ERP - clave SQL');

-- Actualiza lo que ya existia, guardando el valor previo
UPDATE c
   SET c.CnfValueAnterior = c.CnfValor,
       c.CnfValor         = v.Valor,
       c.CnfDescripcion   = v.Descripcion,
       c.CnfFechaMod      = GETDATE()
FROM dbo.ADPConfig c
INNER JOIN @Valores v ON v.Clave = c.CnfCodigo
WHERE v.Valor IS NOT NULL;

-- Inserta lo que falta
INSERT INTO dbo.ADPConfig
    (CnfCodigo, CnfValor, CnfDescripcion, CnfSesion, CnfTipoDatos, CnfFechaMod)
SELECT v.Clave, v.Valor, v.Descripcion, 0, @TipoDatos, GETDATE()
FROM @Valores v
WHERE v.Valor IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ADPConfig c WHERE c.CnfCodigo = v.Clave);

-- Limpia las claves que no se declararon, para no dejar valores viejos colgados
DELETE c
FROM dbo.ADPConfig c
INNER JOIN @Valores v ON v.Clave = c.CnfCodigo
WHERE v.Valor IS NULL;

$consultaEstado
"@

Write-Host "Declarando:" -ForegroundColor DarkGray
Write-Host "  Datamart : $Datamart"
Write-Host "  Servidor : $(if ($DatamartServidor) { $DatamartServidor } else { '(el mismo del ERP)' })"
Write-Host "  Auth     : $(if ($Usuario) { "SQL ($Usuario)" } else { 'Windows' })"
Write-Host ""

Invocar $sql

Write-Host ""
Write-Host "Listo. Ahora:" -ForegroundColor Green
Write-Host "  1. En appsettings.Development.json pone ErpConnection apuntando a $ErpServidor / $ErpBase" -ForegroundColor DarkGray
Write-Host "  2. Reinicia el backend (.\iniciar.bat)" -ForegroundColor DarkGray
Write-Host "  3. GET https://localhost:7001/api/erp/context" -ForegroundColor DarkGray

if ($Usuario) {
    Write-Host ""
    Write-Host "Nota: la clave queda en texto plano en dbo.ADPConfig." -ForegroundColor Yellow
}
