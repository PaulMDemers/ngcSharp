param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [int]$Top = 12
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Read-Double {
    param(
        [object]$Row,
        [string]$Name
    )

    $value = $Row.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    try {
        return [double]::Parse($value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $null
    }
}

function Read-Int {
    param(
        [object]$Row,
        [string]$Name
    )

    $value = $Row.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    try {
        return [int]::Parse($value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $null
    }
}

$fullPath = Resolve-FullPath $Path
if (-not (Test-Path -LiteralPath $fullPath)) {
    throw "GX transform CSV not found: $fullPath"
}

$rows = @(Import-Csv -LiteralPath $fullPath)
if ($rows.Count -eq 0) {
    throw "GX transform CSV has no rows: $fullPath"
}

$summaryRows = foreach ($row in $rows) {
    $screenMinX = Read-Double $row "screen_min_x"
    $screenMaxX = Read-Double $row "screen_max_x"
    $screenMinY = Read-Double $row "screen_min_y"
    $screenMaxY = Read-Double $row "screen_max_y"
    $modelMinX = Read-Double $row "model_min_x"
    $modelMaxX = Read-Double $row "model_max_x"
    $viewMinX = Read-Double $row "view_min_x"
    $viewMaxX = Read-Double $row "view_max_x"
    $modelMinY = Read-Double $row "model_min_y"
    $modelMaxY = Read-Double $row "model_max_y"
    $viewMinY = Read-Double $row "view_min_y"
    $viewMaxY = Read-Double $row "view_max_y"
    $modelMinZ = Read-Double $row "model_min_z"
    $modelMaxZ = Read-Double $row "model_max_z"
    $viewMinZ = Read-Double $row "view_min_z"
    $viewMaxZ = Read-Double $row "view_max_z"

    $screenSpanX = if ($null -ne $screenMinX -and $null -ne $screenMaxX) { $screenMaxX - $screenMinX } else { $null }
    $screenSpanY = if ($null -ne $screenMinY -and $null -ne $screenMaxY) { $screenMaxY - $screenMinY } else { $null }
    $screenArea = if ($null -ne $screenSpanX -and $null -ne $screenSpanY) { [Math]::Abs($screenSpanX * $screenSpanY) } else { 0.0 }

    $modelViewDelta = 0.0
    foreach ($pair in @(
        @($modelMinX, $viewMinX), @($modelMaxX, $viewMaxX),
        @($modelMinY, $viewMinY), @($modelMaxY, $viewMaxY),
        @($modelMinZ, $viewMinZ), @($modelMaxZ, $viewMaxZ)
    )) {
        if ($null -ne $pair[0] -and $null -ne $pair[1]) {
            $modelViewDelta = [Math]::Max($modelViewDelta, [Math]::Abs($pair[0] - $pair[1]))
        }
    }

    [pscustomobject]@{
        draw = Read-Int $row "draw_index"
        fifoOffset = $row.fifo_offset
        primitive = $row.primitive
        vertices = Read-Int $row "vertices"
        decoded = $row.decoded
        projected = Read-Int $row "projected_vertices"
        clipped = Read-Int $row "clipped_vertices"
        projection = $row.projection_type
        matrixIndexRaw = $row.matrix_index_raw
        posBase = $row.pos_base
        modelViewMaxDelta = $modelViewDelta
        screenSpanX = $screenSpanX
        screenSpanY = $screenSpanY
        screenArea = $screenArea
        modelZ = "$($row.model_min_z)..$($row.model_max_z)"
        viewZ = "$($row.view_min_z)..$($row.view_max_z)"
        screen = "$($row.screen_min_x)..$($row.screen_max_x),$($row.screen_min_y)..$($row.screen_max_y)"
        firstModel = $row.first_model
        firstView = $row.first_view
        firstScreen = $row.first_screen
    }
}

$identityLike = @($summaryRows | Where-Object { $_.modelViewMaxDelta -lt 0.0001 }).Count
$projected = @($summaryRows | Where-Object { $_.projected -gt 0 }).Count
$clipped = @($summaryRows | Where-Object { $_.clipped -gt 0 }).Count
$projectionKinds = $rows |
    Group-Object projection_type |
    Sort-Object Count -Descending |
    ForEach-Object { "$($_.Name):$($_.Count)" }

[pscustomobject]@{
    path = $fullPath
    rows = $rows.Count
    projectedRows = $projected
    clippedRows = $clipped
    identityModelViewRows = $identityLike
    projectionKinds = $projectionKinds -join "; "
} | Format-List

Write-Host ""
Write-Host "Largest projected screen spans:"
$summaryRows |
    Sort-Object screenArea -Descending |
    Select-Object -First $Top draw,fifoOffset,primitive,vertices,decoded,projected,clipped,projection,matrixIndexRaw,posBase,modelViewMaxDelta,screenSpanX,screenSpanY,modelZ,viewZ,screen |
    Format-Table -AutoSize
