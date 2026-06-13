param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$SummaryJsonPath = "",
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

function Parse-HexOrDecimal {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [uint64]0
    }

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint64]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Parse-DoubleOrNull {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [double]::Parse($Text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-HexValue {
    param([uint64]$Value)

    return "0x{0:X}" -f $Value
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

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic vertex provenance CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-vertex-provenance.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-vertex-provenance.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic vertex provenance CSV has no rows: $tracePath"
}

foreach ($row in $rows) {
    $row | Add-Member -NotePropertyName _instruction -NotePropertyValue ([int64]$row.instruction)
    $row | Add-Member -NotePropertyName _fifo -NotePropertyValue (Parse-HexOrDecimal $row.gx_fifo_offset)
    $row | Add-Member -NotePropertyName _streamOffset -NotePropertyValue (Parse-HexOrDecimal $row.stream_offset)
    $row | Add-Member -NotePropertyName _sourceRecord -NotePropertyValue (Parse-HexOrDecimal $row.source_record)
    $row | Add-Member -NotePropertyName _sourceX -NotePropertyValue (Parse-DoubleOrNull $row.source_x_float)
    $row | Add-Member -NotePropertyName _sourceY -NotePropertyValue (Parse-DoubleOrNull $row.source_y_float)
    $row | Add-Member -NotePropertyName _sourceZ -NotePropertyValue (Parse-DoubleOrNull $row.source_z_float)
}

$summaryRows = foreach ($group in ($rows | Group-Object packet | Sort-Object { ($_.Group | Select-Object -First 1)._fifo })) {
    $packetRows = @($group.Group | Sort-Object _fifo)
    $first = $packetRows[0]
    $last = $packetRows[-1]
    $bounds = @{
        sourceCount = 0
        sourceMinX = $null
        sourceMaxX = $null
        sourceMinY = $null
        sourceMaxY = $null
        sourceMinZ = $null
        sourceMaxZ = $null
    }

    foreach ($row in $packetRows) {
        Update-Bounds $bounds "source" $row._sourceX $row._sourceY $row._sourceZ
    }

    $indexMismatches = @($packetRows | Where-Object { $_.index_matches -ne "True" }).Count
    $attr0Mismatches = @($packetRows | Where-Object { $_.attr0_matches -ne "True" }).Count
    $attr1Mismatches = @($packetRows | Where-Object { $_.attr1_matches -ne "True" }).Count
    $uniqueSourceRecords = @($packetRows | Select-Object -ExpandProperty source_record -Unique)
    $firstFifo = Format-HexValue $first._fifo
    $lastFifo = Format-HexValue $last._fifo

    [pscustomobject]@{
        packet = $group.Name
        packet_kind = $first.packet_kind
        anchor = "$($group.Name)=$firstFifo"
        first_instruction = $first.instruction
        last_instruction = $last.instruction
        first_fifo_offset = $firstFifo
        last_fifo_offset = $lastFifo
        fifo_span_bytes = [int64]($last._fifo - $first._fifo + 1)
        rows = $packetRows.Count
        unique_source_records = $uniqueSourceRecords.Count
        stream0 = $first.packet_stream0
        stream1 = $first.packet_stream1
        stream_offset_min = Format-HexValue (($packetRows | Measure-Object _streamOffset -Minimum).Minimum)
        stream_offset_max = Format-HexValue (($packetRows | Measure-Object _streamOffset -Maximum).Maximum)
        source_record_min = Format-HexValue (($packetRows | Measure-Object _sourceRecord -Minimum).Minimum)
        source_record_max = Format-HexValue (($packetRows | Measure-Object _sourceRecord -Maximum).Maximum)
        source_x = "$(Format-Double $bounds.sourceMinX)..$(Format-Double $bounds.sourceMaxX)"
        source_y = "$(Format-Double $bounds.sourceMinY)..$(Format-Double $bounds.sourceMaxY)"
        source_z = "$(Format-Double $bounds.sourceMinZ)..$(Format-Double $bounds.sourceMaxZ)"
        index_mismatches = $indexMismatches
        attr0_mismatches = $attr0Mismatches
        attr1_mismatches = $attr1Mismatches
        first_record_bytes = $first.record_bytes
        first_source_bytes = $first.source_bytes
    }
}

$summaryRows | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation

[pscustomobject]@{
    traceCsvPath = $tracePath
    rowCount = $rows.Count
    packetCount = @($summaryRows).Count
    anchors = @($summaryRows | Select-Object -ExpandProperty anchor)
    mismatchPackets = @($summaryRows | Where-Object { $_.index_mismatches -ne 0 -or $_.attr0_mismatches -ne 0 -or $_.attr1_mismatches -ne 0 } | Select-Object packet,index_mismatches,attr0_mismatches,attr1_mismatches)
    firstPackets = @($summaryRows | Select-Object -First $Top)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic vertex provenance summary: $SummaryCsvPath"
$summaryRows |
    Select-Object -First $Top packet,packet_kind,anchor,rows,unique_source_records,first_fifo_offset,last_fifo_offset,source_x,source_y,source_z,index_mismatches,attr0_mismatches,attr1_mismatches |
    Format-Table -AutoSize
