param(
    [Parameter(Mandatory = $true)]
    [string]$VertexCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$SummaryJsonPath = "",
    [string]$FocusedCsvPath = "",
    [string]$FocusFifoOffset = "",
    [int]$FocusBeforeBytes = 0x400,
    [int]$FocusAfterBytes = 0x400
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-HexOrDecimal {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int64]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [int64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Parse-DoubleOrNull {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [double]::Parse($Text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Double {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Update-Bounds {
    param(
        [hashtable]$Bounds,
        [string]$Prefix,
        $X,
        $Y,
        $Z
    )

    if ($null -eq $X -or $null -eq $Y -or $null -eq $Z) {
        return
    }

    $Bounds["${Prefix}Count"] = [int]$Bounds["${Prefix}Count"] + 1
    foreach ($axis in @(@("X", [double]$X), @("Y", [double]$Y), @("Z", [double]$Z))) {
        $name = $axis[0]
        $value = [double]$axis[1]
        $minKey = "${Prefix}Min$name"
        $maxKey = "${Prefix}Max$name"
        if ($null -eq $Bounds[$minKey] -or $value -lt [double]$Bounds[$minKey]) {
            $Bounds[$minKey] = $value
        }

        if ($null -eq $Bounds[$maxKey] -or $value -gt [double]$Bounds[$maxKey]) {
            $Bounds[$maxKey] = $value
        }
    }
}

function New-DrawAccumulator {
    param([object]$Row)

    @{
        draw_index = [int]$Row.draw_index
        fifo_offset = $Row.fifo_offset
        fifo_offset_value = Parse-HexOrDecimal $Row.fifo_offset
        primitive = $Row.primitive
        format = [int]$Row.format
        vertex_count = [int]$Row.vertex_count
        decoded_vertices = 0
        clipped_vertices = 0
        viewCount = 0
        viewMinX = $null
        viewMaxX = $null
        viewMinY = $null
        viewMaxY = $null
        viewMinZ = $null
        viewMaxZ = $null
        screenCount = 0
        screenMinX = $null
        screenMaxX = $null
        screenMinY = $null
        screenMaxY = $null
        screenMinZ = $null
        screenMaxZ = $null
        first_raw_bytes = ""
        first_view = ""
        first_screen = ""
    }
}

$vertexPath = Resolve-FullPath $VertexCsvPath
if (-not (Test-Path -LiteralPath $vertexPath)) {
    throw "Vertex CSV not found: $vertexPath"
}

$directory = Split-Path -Parent $vertexPath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "gx-vertices.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "gx-vertices.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

if ([string]::IsNullOrWhiteSpace($FocusedCsvPath)) {
    $FocusedCsvPath = Join-Path $directory "gx-vertices.focus.csv"
} else {
    $FocusedCsvPath = Resolve-FullPath $FocusedCsvPath
}

$rows = @(Import-Csv -LiteralPath $vertexPath)
$byDraw = [ordered]@{}
foreach ($row in $rows) {
    $key = $row.draw_index
    if (-not $byDraw.Contains($key)) {
        $byDraw[$key] = New-DrawAccumulator $row
    }

    $draw = $byDraw[$key]
    if ($row.decoded -eq "True") {
        $draw.decoded_vertices = [int]$draw.decoded_vertices + 1
    }

    if ($row.clip_rejected -eq "True") {
        $draw.clipped_vertices = [int]$draw.clipped_vertices + 1
    }

    if ([string]::IsNullOrWhiteSpace($draw.first_raw_bytes)) {
        $draw.first_raw_bytes = $row.raw_bytes
        $draw.first_view = "$($row.view_x)/$($row.view_y)/$($row.view_z)"
        $draw.first_screen = "$($row.screen_x)/$($row.screen_y)/$($row.screen_z)"
    }

    Update-Bounds -Bounds $draw -Prefix "view" -X (Parse-DoubleOrNull $row.view_x) -Y (Parse-DoubleOrNull $row.view_y) -Z (Parse-DoubleOrNull $row.view_z)
    Update-Bounds -Bounds $draw -Prefix "screen" -X (Parse-DoubleOrNull $row.screen_x) -Y (Parse-DoubleOrNull $row.screen_y) -Z (Parse-DoubleOrNull $row.screen_z)
}

$summary = foreach ($draw in $byDraw.Values) {
    [pscustomobject]@{
        draw_index = $draw.draw_index
        fifo_offset = $draw.fifo_offset
        primitive = $draw.primitive
        format = $draw.format
        vertex_count = $draw.vertex_count
        decoded_vertices = $draw.decoded_vertices
        clipped_vertices = $draw.clipped_vertices
        view_min_x = Format-Double $draw["viewMinX"]
        view_max_x = Format-Double $draw["viewMaxX"]
        view_min_y = Format-Double $draw["viewMinY"]
        view_max_y = Format-Double $draw["viewMaxY"]
        view_min_z = Format-Double $draw["viewMinZ"]
        view_max_z = Format-Double $draw["viewMaxZ"]
        screen_min_x = Format-Double $draw["screenMinX"]
        screen_max_x = Format-Double $draw["screenMaxX"]
        screen_min_y = Format-Double $draw["screenMinY"]
        screen_max_y = Format-Double $draw["screenMaxY"]
        screen_min_z = Format-Double $draw["screenMinZ"]
        screen_max_z = Format-Double $draw["screenMaxZ"]
        first_view = $draw.first_view
        first_screen = $draw.first_screen
        first_raw_bytes = $draw.first_raw_bytes
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

if (-not [string]::IsNullOrWhiteSpace($FocusFifoOffset)) {
    $focus = Parse-HexOrDecimal $FocusFifoOffset
    $start = $focus - $FocusBeforeBytes
    $end = $focus + $FocusAfterBytes
    $focused = @($rows | Where-Object {
        $offset = Parse-HexOrDecimal $_.fifo_offset
        $offset -ge $start -and $offset -le $end
    })
    $focused | Export-Csv -LiteralPath $FocusedCsvPath -NoTypeInformation
}

Write-Host "GX vertex summary: $SummaryCsvPath"
$summary | Select-Object -First 12 draw_index,fifo_offset,primitive,vertex_count,screen_min_x,screen_max_x,screen_min_y,screen_max_y,first_view,first_screen | Format-Table -AutoSize
if (-not [string]::IsNullOrWhiteSpace($FocusFifoOffset)) {
    Write-Host "Focused vertices: $FocusedCsvPath"
}
