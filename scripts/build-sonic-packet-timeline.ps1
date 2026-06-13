param(
    [Parameter(Mandatory = $true)]
    [string]$SceneSummaryCsvPath,
    [string]$MatrixWriterSummaryCsvPath = "",
    [string]$VertexSummaryCsvPath = "",
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

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

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

            $packet = Format-PacketKey $parts[0]
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

function Format-PacketKey {
    param([string]$Packet)

    $trimmed = $Packet.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x$($trimmed.Substring(2).ToUpperInvariant())"
    }

    return "0x$($trimmed.ToUpperInvariant())"
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

function Import-OptionalCsv {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return @()
    }

    $fullPath = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "CSV not found: $fullPath"
    }

    return @(Import-Csv -LiteralPath $fullPath)
}

function Get-VertexAggregate {
    param(
        [object[]]$VertexRows,
        [hashtable]$AnchorMap,
        [string]$Packet,
        [int]$DrawLimit
    )

    $packetKey = Format-PacketKey $Packet
    if (-not $AnchorMap.ContainsKey($packetKey) -or $VertexRows.Count -eq 0) {
        return $null
    }

    $anchorOffset = [int64]$AnchorMap[$packetKey]
    $matchedRows = @(Get-VertexRowsFromAnchor $VertexRows $anchorOffset $DrawLimit)
    if ($matchedRows.Count -eq 0) {
        return [pscustomobject]@{
            anchor = "+0x{0:X}" -f $anchorOffset
            drawCount = 0
            drawStart = ""
            drawEnd = ""
            fifoStart = ""
            fifoEnd = ""
            decodedVertices = 0
            clippedVertices = 0
            viewBounds = ""
            screenBounds = ""
        }
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
        anchor = "+0x{0:X}" -f $anchorOffset
        drawCount = $matchedRows.Count
        drawStart = $matchedRows[0].draw_index
        drawEnd = $matchedRows[-1].draw_index
        fifoStart = $matchedRows[0].fifo_offset
        fifoEnd = $matchedRows[-1].fifo_offset
        decodedVertices = $decodedVertices
        clippedVertices = $clippedVertices
        viewBounds = "$(Format-Double $bounds.viewMinX)/$(Format-Double $bounds.viewMinY)/$(Format-Double $bounds.viewMinZ) .. $(Format-Double $bounds.viewMaxX)/$(Format-Double $bounds.viewMaxY)/$(Format-Double $bounds.viewMaxZ)"
        screenBounds = "$(Format-Double $bounds.screenMinX)/$(Format-Double $bounds.screenMinY) .. $(Format-Double $bounds.screenMaxX)/$(Format-Double $bounds.screenMaxY)"
    }
}

$scenePath = Resolve-FullPath $SceneSummaryCsvPath
if (-not (Test-Path -LiteralPath $scenePath)) {
    throw "Scene summary CSV not found: $scenePath"
}

$outputDirectory = Split-Path -Parent $scenePath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $outputDirectory "sonic-packet-timeline.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $outputDirectory "sonic-packet-timeline.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$sceneRows = @(Import-Csv -LiteralPath $scenePath | Sort-Object { [int64]$_.instruction })
$matrixRows = @(Import-OptionalCsv $MatrixWriterSummaryCsvPath | Where-Object { $_.pc -eq "0x8011C184" })
$vertexRows = @(Import-OptionalCsv $VertexSummaryCsvPath | Sort-Object { Parse-HexOrDecimal $_.fifo_offset })
$anchorTexts = @($Anchor) + @(Read-AnchorTextsFromCsv $AnchorCsvPath)
$anchorMap = Parse-AnchorMap $anchorTexts

$matrixByPacket = @{}
foreach ($matrixRow in $matrixRows) {
    $packetKey = Format-PacketKey $matrixRow.packet
    if (-not $matrixByPacket.ContainsKey($packetKey)) {
        $matrixByPacket[$packetKey] = $matrixRow
    }
}

$timeline = foreach ($sceneRow in $sceneRows) {
    $packetKey = Format-PacketKey $sceneRow.packet
    $matrixRow = if ($matrixByPacket.ContainsKey($packetKey)) { $matrixByPacket[$packetKey] } else { $null }
    $vertex = Get-VertexAggregate -VertexRows $vertexRows -AnchorMap $anchorMap -Packet $sceneRow.packet -DrawLimit $DrawsAfterAnchor
    $instruction = [int64]$sceneRow.instruction
    $matrixInstruction = if ($null -eq $matrixRow) { $null } else { [int64]$matrixRow.instruction }

    [pscustomobject]@{
        instruction = $instruction
        matrix_instruction = if ($null -eq $matrixInstruction) { "" } else { $matrixInstruction }
        matrix_delta = if ($null -eq $matrixInstruction) { "" } else { $matrixInstruction - $instruction }
        packet = $sceneRow.packet
        packet_kind = $sceneRow.packet_kind
        object = $sceneRow.object
        object_kind = $sceneRow.object_kind
        stream0 = $sceneRow.stream0
        stream1 = $sceneRow.stream1
        object_xyz = $sceneRow.object_xyz
        object_scaleish = $sceneRow.object_scaleish
        matrix_translation = if ($null -eq $matrixRow) { "" } else { $matrixRow.packed_translation }
        matrix_stream0 = if ($null -eq $matrixRow) { "" } else { $matrixRow.packet_stream0 }
        matrix_stream1 = if ($null -eq $matrixRow) { "" } else { $matrixRow.packet_stream1 }
        resource_flag = $sceneRow.resource_flag
        state_word80 = $sceneRow.state_word80
        state_hash = $sceneRow.state_hash
        small_data_hash = $sceneRow.small_data_hash
        packet_hash = $sceneRow.packet_hash
        object_hash = $sceneRow.object_hash
        anchor_fifo_offset = if ($null -eq $vertex) { "" } else { $vertex.anchor }
        mapped_draw_count = if ($null -eq $vertex) { "" } else { $vertex.drawCount }
        mapped_draw_start = if ($null -eq $vertex) { "" } else { $vertex.drawStart }
        mapped_draw_end = if ($null -eq $vertex) { "" } else { $vertex.drawEnd }
        mapped_fifo_start = if ($null -eq $vertex) { "" } else { $vertex.fifoStart }
        mapped_fifo_end = if ($null -eq $vertex) { "" } else { $vertex.fifoEnd }
        decoded_vertices = if ($null -eq $vertex) { "" } else { $vertex.decodedVertices }
        clipped_vertices = if ($null -eq $vertex) { "" } else { $vertex.clippedVertices }
        view_bounds = if ($null -eq $vertex) { "" } else { $vertex.viewBounds }
        screen_bounds = if ($null -eq $vertex) { "" } else { $vertex.screenBounds }
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$timeline | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation
$timeline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Sonic packet timeline: $OutputCsvPath"
$timeline | Select-Object instruction,packet,packet_kind,object_xyz,matrix_translation,anchor_fifo_offset,mapped_draw_start,mapped_draw_end,decoded_vertices,clipped_vertices | Format-Table -AutoSize
