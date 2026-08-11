# =============================================================================
#  Empaqueta el conector MCP como .mcpb — el instalador de un clic.
#
#  Un paquete POR PLATAFORMA: llevar los dos binarios en el mismo archivo lo
#  dejaba en ~110 MB para que cada usuario use la mitad.
#
#  Se publica SELF-CONTAINED a proposito: quien lo instala no tiene por que
#  tener .NET. Pesa mas, pero el requisito previo es cero.
#
#  Uso:
#    powershell -ExecutionPolicy Bypass -File scripts\empaquetar-conector.ps1
#    ... -Plataforma mac          solo el de macOS (Apple Silicon)
#    ... -Plataforma ambas        los dos
#    ... -Version 1.1.0           sella otra version
#
#  OJO con el de macOS: sale SIN FIRMAR. Ver docs\CONECTOR-MAC.md antes de
#  entregarselo a un cliente.
# =============================================================================
[CmdletBinding()]
param(
    [ValidateSet('win', 'mac', 'ambas')]
    [string]$Plataforma = 'win',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$raiz     = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz 'backend\DatamartAnalyzer.Mcp\DatamartAnalyzer.Mcp.csproj'
$conector = Join-Path $raiz 'connector'
$dist     = Join-Path $raiz 'dist'
$logo     = Join-Path $raiz 'docs\LOGO_ADPRO.png'

function Paso($t) { Write-Host "==> $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "    $t" -ForegroundColor Green }
function Aviso($t){ Write-Host "    $t" -ForegroundColor Yellow }

# --- Icono (comun a las dos plataformas) -------------------------------------

# -----------------------------------------------------------------------------
#  El icono se genera a SANGRE, no escalando el PNG tal cual.
#
#  Hay DOS margenes que achican el logo, y hay que sacar los dos:
#
#   1. El margen transparente del archivo (~30 px por lado) mas las esquinas
#      redondeadas propias. Claude Desktop ya dibuja el icono dentro de un
#      contenedor redondeado, asi que eso se veia como un anillo vacio.
#
#   2. El margen DENTRO de la insignia. El logo es un circulo teal con el logotipo
#      "SINCO ADPRO" en el centro, y ese logotipo ocupa apenas el 63% del ancho y
#      el 38% del alto. Recortar a la insignia (lo que se hacia antes) llenaba el
#      recuadro de teal y dejaba las letras igual de chicas: el icono "llegaba al
#      borde" pero lo unico legible seguia siendo diminuto.
#
#  Solucion: recortar al LOGOTIPO — los pixeles que se apartan del color de fondo —
#  y redibujarlo, respetando su proporcion, sobre un cuadrado del teal del propio
#  logo con un margen chico. El redondeo lo pone la aplicacion.
# -----------------------------------------------------------------------------

# Fraccion del lado que queda como aire a cada costado. Suficiente para que el
# logotipo no toque el borde ni se coma el redondeo que aplica Claude Desktop.
$MargenIcono = 0.07

function Nuevo-Icono($destino) {
    if (-not (Test-Path $logo)) { throw "No encuentro el logo en $logo" }

    Add-Type -AssemblyName System.Drawing
    $src = New-Object System.Drawing.Bitmap $logo
    try {
        $W = $src.Width; $H = $src.Height

        # Caja de lo no transparente, y a la vez el color de fondo.
        #
        # El fondo se calcula como PROMEDIO de los pixeles opacos que no son
        # casi-blancos, no como "el color mas frecuente". El logo es un degradado:
        # ningun tono de teal individual gana, y el mas frecuente terminaba siendo el
        # blanco del contorno y de las letras — con lo que el icono habria quedado
        # con las esquinas blancas.
        $minX = $W; $minY = $H; $maxX = -1; $maxY = -1
        $sumR = 0.0; $sumG = 0.0; $sumB = 0.0; $n = 0

        for ($y = 0; $y -lt $H; $y++) {
            for ($x = 0; $x -lt $W; $x++) {
                $c = $src.GetPixel($x, $y)
                if ($c.A -le 8) { continue }
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }

                if ($c.A -gt 200 -and -not ($c.R -gt 225 -and $c.G -gt 225 -and $c.B -gt 225)) {
                    $sumR += $c.R; $sumG += $c.G; $sumB += $c.B; $n++
                }
            }
        }
        if ($maxX -lt 0) { throw 'El logo esta completamente transparente.' }
        if ($n -eq 0) { throw 'El logo no tiene pixeles de color: no se puede deducir el fondo.' }

        $fondo = [System.Drawing.Color]::FromArgb(255, [int]($sumR / $n), [int]($sumG / $n), [int]($sumB / $n))

        # Segunda pasada: la caja del LOGOTIPO. Es lo que se aparta del fondo, no lo
        # que es opaco. Se mide por distancia al color de fondo en vez de buscar
        # "pixeles claros": asi sigue funcionando si algun dia el logo se entrega en
        # otros colores, sin tener que volver a tocar el umbral a mano.
        #
        # Ojo con PowerShell: los nombres de variable NO distinguen mayusculas, asi
        # que $minX y $MinX serian la misma. De ahi los nombres en castellano.
        $izq = $W; $arr = $H; $der = -1; $aba = -1
        $umbral = 110    # distancia euclidea en RGB; el antialias del borde no llega
        for ($y = 0; $y -lt $H; $y++) {
            for ($x = 0; $x -lt $W; $x++) {
                $c = $src.GetPixel($x, $y)
                if ($c.A -le 128) { continue }
                $dr = $c.R - $fondo.R; $dg = $c.G - $fondo.G; $db = $c.B - $fondo.B
                if ([Math]::Sqrt($dr*$dr + $dg*$dg + $db*$db) -le $umbral) { continue }
                if ($x -lt $izq) { $izq = $x }
                if ($x -gt $der) { $der = $x }
                if ($y -lt $arr) { $arr = $y }
                if ($y -gt $aba) { $aba = $y }
            }
        }

        # Si el logotipo no se distingue del fondo, o sale tan grande que claramente
        # se agarro la insignia entera, se vuelve al recorte de lo opaco. Un icono
        # con margen de mas es feo; uno recortado por la mitad es peor.
        $anchoOpaco = $maxX - $minX + 1
        $altoOpaco  = $maxY - $minY + 1
        $hayLogotipo = ($der -ge 0) -and
                       (($der - $izq + 1) -lt $anchoOpaco * 0.95) -and
                       (($aba - $arr + 1) -lt $altoOpaco  * 0.95) -and
                       (($der - $izq + 1) -gt $W * 0.15)

        # Nada de operador ternario: este script tiene que correr en Windows
        # PowerShell 5.1, donde `? :` no existe.
        if ($hayLogotipo) {
            $recorte = New-Object System.Drawing.Rectangle $izq, $arr, ($der - $izq + 1), ($aba - $arr + 1)
            $queSeRecorto = 'logotipo'
        }
        else {
            $recorte = New-Object System.Drawing.Rectangle $minX, $minY, $anchoOpaco, $altoOpaco
            $queSeRecorto = 'insignia'
        }

        # 512x512 es lo recomendado por la especificacion (minimo 256).
        $lado = 512
        $bmp = New-Object System.Drawing.Bitmap $lado, $lado
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.Clear($fondo)

        # Se escala RESPETANDO la proporcion. Estirar un logotipo apaisado hasta
        # llenar un cuadrado lo deforma, y eso se nota mucho mas que el aire que
        # queda arriba y abajo.
        $util   = $lado * (1 - 2 * $MargenIcono)
        $escala = [Math]::Min($util / $recorte.Width, $util / $recorte.Height)
        $ancho  = [int]($recorte.Width  * $escala)
        $alto   = [int]($recorte.Height * $escala)
        $destinoRect = New-Object System.Drawing.Rectangle `
            ([int](($lado - $ancho) / 2)), ([int](($lado - $alto) / 2)), $ancho, $alto

        $g.DrawImage($src, $destinoRect, $recorte, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $bmp.Save($destino, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()

        Ok ("icono a sangre: {0} {1}x{2} -> {3}x{4} ({5:N0}% del lado), fondo #{6:X2}{7:X2}{8:X2}" -f `
            $queSeRecorto, $recorte.Width, $recorte.Height,
            $ancho, $alto, (100 * $ancho / $lado), $fondo.R, $fondo.G, $fondo.B)
    }
    finally { $src.Dispose() }
}

# --- Manifest ----------------------------------------------------------------
# connector\manifest.json es la fuente de verdad y esta escrito para Windows, asi
# se puede validar tal cual esta en el repo. Para macOS se sustituyen tres cosas
# con reemplazo de texto exacto — no round-trip por ConvertTo-Json, que en
# PowerShell 5.1 reordena y reescapa medio archivo.

function Nuevo-Manifest($destino, $rid, $version) {
    $origen = Join-Path $conector 'manifest.json'
    if (-not (Test-Path $origen)) { throw "Falta $origen" }

    $t = [System.IO.File]::ReadAllText($origen, [System.Text.Encoding]::UTF8)
    $t = [regex]::Replace($t, '("version"\s*:\s*")[^"]+(")', ('${1}' + $version + '${2}'), 1)

    if ($rid -ne 'win-x64') {
        $t = $t.Replace('"platforms": ["win32"]', '"platforms": ["darwin"]')
        $t = $t.Replace('server/DatamartAnalyzer.Mcp.exe', 'server/DatamartAnalyzer.Mcp')

        if ($t -match 'win32' -or $t -match '\.exe') {
            throw 'Quedaron rastros de Windows en el manifest de macOS.'
        }
    }

    [System.IO.File]::WriteAllText($destino, $t, (New-Object System.Text.UTF8Encoding($false)))
}

# --- Empaquetado de una plataforma -------------------------------------------

function Nuevo-Paquete($rid, $etiqueta) {
    Paso "[$etiqueta] publicando ($rid, self-contained)"

    $stage = Join-Path $conector "build\$etiqueta"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    $servidor = Join-Path $stage 'server'

    # PublishTrimmed queda en false: el SDK de MCP descubre las herramientas por
    # atributos y Dapper materializa por reflexion. Recortar las borraria.
    dotnet publish $proyecto `
        -c Release -r $rid --self-contained true `
        -p:PublishTrimmed=false -p:PublishSingleFile=false `
        -p:InvariantGlobalization=false `
        -o $servidor --nologo -v q

    if ($LASTEXITCODE -ne 0) { throw "[$etiqueta] fallo dotnet publish." }

    $ejecutable = if ($rid -eq 'win-x64') { 'DatamartAnalyzer.Mcp.exe' } else { 'DatamartAnalyzer.Mcp' }
    $bin = Join-Path $servidor $ejecutable
    if (-not (Test-Path $bin)) { throw "[$etiqueta] no se genero $ejecutable" }

    Get-ChildItem $servidor -Filter *.pdb -Recurse | Remove-Item -Force

    $mb = [math]::Round((Get-ChildItem $servidor -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Ok "publicado: $mb MB"

    Nuevo-Icono (Join-Path $stage 'icon.png')
    Nuevo-Manifest (Join-Path $stage 'manifest.json') $rid $Version
    Ok "manifest y icono listos (version $Version)"

    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $salida = Join-Path $dist "DatamartAnalyzer-ADPRO-$etiqueta-$Version.mcpb"

    Push-Location $stage
    try {
        & mcpb validate manifest.json | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "[$etiqueta] el manifest no paso la validacion." }

        & mcpb pack . $salida | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "[$etiqueta] fallo mcpb pack." }
    }
    finally { Pop-Location }

    if ($rid -ne 'win-x64') {
        # NTFS no tiene bit de ejecucion: sin esto el binario sale del zip sin +x
        # y Claude Desktop no lo puede arrancar en macOS.
        Paso "[$etiqueta] aplicando permisos de Unix al zip"
        & node (Join-Path $PSScriptRoot 'permisos-mcpb.js') $salida "server/$ejecutable"
        if ($LASTEXITCODE -ne 0) { throw "[$etiqueta] fallo el ajuste de permisos." }

        Aviso 'Este paquete NO esta firmado: macOS lo va a poner en cuarentena.'
        Aviso 'Antes de entregarlo a un cliente, ver docs\CONECTOR-MAC.md'
    }

    $mbPaq = [math]::Round((Get-Item $salida).Length / 1MB, 1)
    Write-Host "    LISTO  $salida  ($mbPaq MB)" -ForegroundColor Green
}

# --- Main --------------------------------------------------------------------

if ($Plataforma -in @('win', 'ambas'))  { Nuevo-Paquete 'win-x64'   'win' }
if ($Plataforma -in @('mac', 'ambas'))  { Nuevo-Paquete 'osx-arm64' 'mac' }

Write-Host ''
Write-Host 'Para instalar: Claude Desktop -> Configuracion -> Extensiones -> arrastrar el .mcpb'
