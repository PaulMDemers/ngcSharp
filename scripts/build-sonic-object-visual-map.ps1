param(
    [Parameter(Mandatory = $true)]
    [string]$MatrixSummaryCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$VertexSummaryCsvPath,
    [string[]]$Anchor = @(),
    [string]$AnchorCsvPath = "",
    [int]$DrawsAfterAnchor = 12,
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = ""
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

function Update-MinMax {
    param(
        [hashtable]$Bounds,
        [string]$MinKey,
        [string]$MaxKey,
        $Value
    )

    if ($null -eq $Value) {
        return
    }

    $doubleValue = [double]$Value
    if ($null -eq $Bounds[$MinKey] -or $doubleValue -lt [double]$Bounds[$MinKey]) {
        $Bounds[$MinKey] = $doubleValue
    }

    if ($null -eq $Bounds[$MaxKey] -or $doubleValue -gt [double]$Bounds[$MaxKey]) {
        $Bounds[$MaxKey] = $doubleValue
    }
}

function Parse-AnchorMap {
    param([string[]]$AnchorTexts)

    $map = @{}
    foreach ($anchorArgument in $AnchorTexts) {
        foreach ($anchorText in $anchorArgument.Split(",", [System.StringSplitOptions]::RemoveEmptyEntries -bor [System.StringSplitOptions]::TrimEntries)) {
            if ([string]::IsNullOrWhiteSpace($anchorText)) {
                continue
            }

            $parts = $anchorText.Split("=", 2, [System.StringSplitOptions]::TrimEntries)
            if ($parts.Length -ne 2) {
                throw "Anchor must use <packet>=<fifo-offset>, got: $anchorText"
            }

            $packet = $parts[0].ToUpperInvariant()
            if (-not $packet.StartsWith("0X", [System.StringComparison]::Ordinal)) {
                $packet = "0x$packet".ToUpperInvariant()
            }

            $map[$packet] = Parse-HexOrDecimal $parts[1]
        }
    }

    return $map
}

function Read-AnchorTextsFromCsv {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return @()
    }

    $fullPath = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Anchor CSV not found: $fullPath"
    }

    return @(
        Import-Csv -LiteralPath $fullPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.anchor) } |
            ForEach-Object { $_.anchor }
    )
}

function Get-VertexRowsFromAnchor {
    param(
        [object[]]$Rows,
        [int64]$AnchorOffset,
        [int]$Count
    )

    $startIndex = -1
    for ($index = 0; $index -lt $Rows.Count; $index++) {
        $rowOffset = Parse-HexOrDecimal $Rows[$index].fifo_offset
        if ($rowOffset -le $AnchorOffset) {
            $startIndex = $index
            continue
        }

        break
    }

    if ($startIndex -lt 0) {
        $startIndex = 0
    }

    return @($Rows | Select-Object -Skip $startIndex -First $Count)
}

$matrixPath = Resolve-FullPath $MatrixSummaryCsvPath
$vertexPath = Resolve-FullPath $VertexSummaryCsvPath
if (-not (Test-Path -LiteralPath $matrixPath)) {
    throw "Matrix summary CSV not found: $matrixPath"
}

if (-not (Test-Path -LiteralPath $vertexPath)) {
    throw "GX vertex summary CSV not found: $vertexPath"
}

$outputDirectory = Split-Path -Parent $matrixPath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $outputDirectory "sonic-object-visual-map.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $outputDirectory "sonic-object-visual-map.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$anchorTexts = @($Anchor) + @(Read-AnchorTextsFromCsv $AnchorCsvPath)
$anchorMap = Parse-AnchorMap $anchorTexts
$matrixRows = @(Import-Csv -LiteralPath $matrixPath)
$vertexRows = @(Import-Csv -LiteralPath $vertexPath | Sort-Object { Parse-HexOrDecimal $_.fifo_offset })
$renderRows = @($matrixRows | Where-Object { $_.pc -eq "0x8011C19C" -and -not [string]::IsNullOrWhiteSpace($_.object_packet) })

$visualMap = foreach ($objectRow in $renderRows) {
    $packet = $objectRow.object_packet.ToUpperInvariant()
    $anchorOffset = $anchorMap[$packet]
    $matchedRows = @()

    if ($null -ne $anchorOffset) {
        $matchedRows = @(Get-VertexRowsFromAnchor $vertexRows $anchorOffset $DrawsAfterAnchor)
    }

    $bounds = @{
        viewMinX = $null
        viewMaxX = $null
        viewMinY = $null
        viewMaxY = $null
        viewMinZ = $null
        viewMaxZ = $null
        screenMinX = $null
        screenMaxX = $null
        screenMinY = $null
        screenMaxY = $null
    }

    $decodedVertices = 0
    $clippedVertices = 0
    foreach ($row in $matchedRows) {
        $decodedVertices += [int]$row.decoded_vertices
        $clippedVertices += [int]$row.clipped_vertices
        Update-MinMax $bounds "viewMinX" "viewMaxX" (Parse-DoubleOrNull $row.view_min_x)
        Update-MinMax $bounds "viewMinX" "viewMaxX" (Parse-DoubleOrNull $row.view_max_x)
        Update-MinMax $bounds "viewMinY" "viewMaxY" (Parse-DoubleOrNull $row.view_min_y)
        Update-MinMax $bounds "viewMinY" "viewMaxY" (Parse-DoubleOrNull $row.view_max_y)
        Update-MinMax $bounds "viewMinZ" "viewMaxZ" (Parse-DoubleOrNull $row.view_min_z)
        Update-MinMax $bounds "viewMinZ" "viewMaxZ" (Parse-DoubleOrNull $row.view_max_z)
        Update-MinMax $bounds "screenMinX" "screenMaxX" (Parse-DoubleOrNull $row.screen_min_x)
        Update-MinMax $bounds "screenMinX" "screenMaxX" (Parse-DoubleOrNull $row.screen_max_x)
        Update-MinMax $bounds "screenMinY" "screenMaxY" (Parse-DoubleOrNull $row.screen_min_y)
        Update-MinMax $bounds "screenMinY" "screenMaxY" (Parse-DoubleOrNull $row.screen_max_y)
    }

    [pscustomobject]@{
        instruction = $objectRow.instruction
        object_pointer = $objectRow.r27
        object_kind = $objectRow.object_kind
        object_packet = $objectRow.object_packet
        object_extra_xyz = $objectRow.object_extra_xyz
        object_scaleish = $objectRow.object_scaleish
        matrix_translation = $objectRow.matrix_translation
        matrix_row0 = $objectRow.matrix_row0
        matrix_row1 = $objectRow.matrix_row1
        matrix_row2 = $objectRow.matrix_row2
        anchor_fifo_offset = if ($null -eq $anchorOffset) { "" } else { "+0x{0:X}" -f $anchorOffset }
        mapped_draw_count = $matchedRows.Count
        mapped_draw_start = if ($matchedRows.Count -eq 0) { "" } else { $matchedRows[0].draw_index }
        mapped_draw_end = if ($matchedRows.Count -eq 0) { "" } else { $matchedRows[-1].draw_index }
        mapped_fifo_start = if ($matchedRows.Count -eq 0) { "" } else { $matchedRows[0].fifo_offset }
        mapped_fifo_end = if ($matchedRows.Count -eq 0) { "" } else { $matchedRows[-1].fifo_offset }
        decoded_vertices = $decodedVertices
        clipped_vertices = $clippedVertices
        view_bounds = "$(Format-Double $bounds.viewMinX)/$(Format-Double $bounds.viewMinY)/$(Format-Double $bounds.viewMinZ) .. $(Format-Double $bounds.viewMaxX)/$(Format-Double $bounds.viewMaxY)/$(Format-Double $bounds.viewMaxZ)"
        screen_bounds = "$(Format-Double $bounds.screenMinX)/$(Format-Double $bounds.screenMinY) .. $(Format-Double $bounds.screenMaxX)/$(Format-Double $bounds.screenMaxY)"
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$visualMap | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation
$visualMap | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Sonic object visual map: $OutputCsvPath"
$visualMap | Select-Object object_pointer,object_kind,object_packet,anchor_fifo_offset,mapped_draw_start,mapped_draw_end,object_extra_xyz,matrix_translation,screen_bounds | Format-Table -AutoSize
