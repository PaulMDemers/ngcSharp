param(
    [Parameter(Mandatory = $true)]
    [string]$ProvenanceCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$TransformCsvPath,
    [string]$SourceMapCsvPath = "",
    [string]$PacketSummaryCsvPath = "",
    [string]$SummaryJsonPath = "",
    [int]$RecordSize = 0x20,
    [int]$PackedInputRecordSize = 0x10,
    [int]$Top = 16
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
        return [uint32]0
    }

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-HexStringToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return [byte[]]::new(0)
    }

    $text = $Hex.Trim()
    if (($text.Length % 2) -ne 0) {
        throw "Odd-length hex byte string."
    }

    $bytes = [byte[]]::new($text.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }

    return $bytes
}

function Read-BeUInt32 {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) {
        return [uint32]0
    }

    return [uint32]((([uint32]$Bytes[$Offset]) -shl 24) -bor (([uint32]$Bytes[$Offset + 1]) -shl 16) -bor (([uint32]$Bytes[$Offset + 2]) -shl 8) -bor [uint32]$Bytes[$Offset + 3])
}

function Read-BeSingle {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) {
        return $null
    }

    $little = [byte[]]::new(4)
    $little[0] = $Bytes[$Offset + 3]
    $little[1] = $Bytes[$Offset + 2]
    $little[2] = $Bytes[$Offset + 1]
    $little[3] = $Bytes[$Offset]
    return [BitConverter]::ToSingle($little, 0)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Format-Number {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Test-RangeOverlap {
    param(
        [uint32]$Address,
        [uint64]$Length,
        [uint32]$TargetAddress,
        [uint64]$TargetLength
    )

    if ($Length -eq 0 -or $TargetLength -eq 0) {
        return $false
    }

    $end = [uint64]$Address + $Length
    $targetEnd = [uint64]$TargetAddress + $TargetLength
    return ([uint64]$Address -lt $targetEnd) -and ([uint64]$TargetAddress -lt $end)
}

function Get-TransformForRecord {
    param(
        [object[]]$Transforms,
        [uint32]$RecordAddress,
        [int64]$BeforeInstruction
    )

    $best = $null
    foreach ($transform in $Transforms) {
        if ([int64]$transform._instruction -gt $BeforeInstruction) {
            continue
        }

        if (-not (Test-RangeOverlap $transform._outputCursor $transform._outputSpan $RecordAddress ([uint64]$RecordSize))) {
            continue
        }

        if ($null -eq $best -or [int64]$transform._instruction -gt [int64]$best._instruction) {
            $best = $transform
        }
    }

    return $best
}

function Get-PackedInputRecord {
    param(
        [byte[]]$Bytes,
        [uint32]$InputCursor,
        [int]$OutputIndex
    )

    # Sonic's 2D transform loop has output index 0 already staged on entry,
    # and output index 1 in preloaded f8/f9. Captured input bytes feed index 2+.
    $inputIndex = $OutputIndex - 2
    if ($inputIndex -lt 0) {
        return $null
    }

    $offset = $inputIndex * $PackedInputRecordSize
    if (($offset + $PackedInputRecordSize) -gt $Bytes.Length) {
        return $null
    }

    [pscustomobject]@{
        input_index = $inputIndex
        input_address = Format-Hex32 ([uint32]($InputCursor + [uint32]$offset))
        input_color = Format-Hex32 (Read-BeUInt32 $Bytes $offset)
        input_x = Format-Number (Read-BeSingle $Bytes ($offset + 4))
        input_y = Format-Number (Read-BeSingle $Bytes ($offset + 8))
        input_z = Format-Number (Read-BeSingle $Bytes ($offset + 12))
        input_x_bits = Format-Hex32 (Read-BeUInt32 $Bytes ($offset + 4))
        input_y_bits = Format-Hex32 (Read-BeUInt32 $Bytes ($offset + 8))
        input_z_bits = Format-Hex32 (Read-BeUInt32 $Bytes ($offset + 12))
    }
}

$provenancePath = Resolve-FullPath $ProvenanceCsvPath
$transformPath = Resolve-FullPath $TransformCsvPath
if (-not (Test-Path -LiteralPath $provenancePath)) {
    throw "Sonic vertex provenance CSV not found: $provenancePath"
}

if (-not (Test-Path -LiteralPath $transformPath)) {
    throw "Sonic transform input CSV not found: $transformPath"
}

$directory = Split-Path -Parent $provenancePath
if ([string]::IsNullOrWhiteSpace($SourceMapCsvPath)) {
    $SourceMapCsvPath = Join-Path $directory "sonic-transform-source-map.csv"
} else {
    $SourceMapCsvPath = Resolve-FullPath $SourceMapCsvPath
}

if ([string]::IsNullOrWhiteSpace($PacketSummaryCsvPath)) {
    $PacketSummaryCsvPath = Join-Path $directory "sonic-transform-source-map.packet-summary.csv"
} else {
    $PacketSummaryCsvPath = Resolve-FullPath $PacketSummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-transform-source-map.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$provenanceRows = @(Import-Csv -LiteralPath $provenancePath)
$transformRows = @(Import-Csv -LiteralPath $transformPath)
if ($provenanceRows.Count -eq 0) {
    throw "Sonic vertex provenance CSV has no rows: $provenancePath"
}

if ($transformRows.Count -eq 0) {
    throw "Sonic transform input CSV has no rows: $transformPath"
}

foreach ($row in $transformRows) {
    $outputCursor = Parse-HexOrDecimal $row.output_cursor
    $iterations = Parse-HexOrDecimal $row.iterations
    $row | Add-Member -NotePropertyName _instruction -NotePropertyValue ([int64]$row.instruction)
    $row | Add-Member -NotePropertyName _outputCursor -NotePropertyValue $outputCursor
    $row | Add-Member -NotePropertyName _inputCursor -NotePropertyValue (Parse-HexOrDecimal $row.input_cursor)
    $row | Add-Member -NotePropertyName _outputSpan -NotePropertyValue ([uint64](([Math]::Max(1, [int]$iterations) * $RecordSize) + $RecordSize))
    $row | Add-Member -NotePropertyName _inputBytesDecoded -NotePropertyValue (Convert-HexStringToBytes $row.input_bytes)
}

$records = foreach ($row in $provenanceRows) {
    $sourceRecord = Parse-HexOrDecimal $row.source_record
    $instruction = [int64]$row.instruction
    $transform = Get-TransformForRecord $transformRows $sourceRecord $instruction
    if ($null -eq $transform) {
        continue
    }

    $outputIndex = if ($sourceRecord -ge ($transform._outputCursor + 8)) {
        [int](($sourceRecord - $transform._outputCursor - 8) / $RecordSize)
    } else {
        -1
    }

    $inputRecord = Get-PackedInputRecord $transform._inputBytesDecoded $transform._inputCursor $outputIndex

    [pscustomobject][ordered]@{
        instruction = $row.instruction
        gx_fifo_offset = $row.gx_fifo_offset
        packet = $row.packet
        source_record = $row.source_record
        output_index = $outputIndex
        source_x = $row.source_x_float
        source_y = $row.source_y_float
        source_z = $row.source_z_float
        source_x_bits = $row.source_x
        source_y_bits = $row.source_y
        source_z_bits = $row.source_z
        source_color = $row.source_color
        transform_instruction = $transform.instruction
        transform_pc = $transform.pc
        transform_output_cursor = $transform.output_cursor
        transform_input_cursor = $transform.input_cursor
        transform_iterations = $transform.iterations
        transform_gqr1 = $transform.gqr1
        input_index = if ($inputRecord) { $inputRecord.input_index } else { "" }
        input_address = if ($inputRecord) { $inputRecord.input_address } else { "" }
        input_color = if ($inputRecord) { $inputRecord.input_color } else { "" }
        input_x = if ($inputRecord) { $inputRecord.input_x } else { "" }
        input_y = if ($inputRecord) { $inputRecord.input_y } else { "" }
        input_z = if ($inputRecord) { $inputRecord.input_z } else { "" }
        input_x_bits = if ($inputRecord) { $inputRecord.input_x_bits } else { "" }
        input_y_bits = if ($inputRecord) { $inputRecord.input_y_bits } else { "" }
        input_z_bits = if ($inputRecord) { $inputRecord.input_z_bits } else { "" }
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SourceMapCsvPath) | Out-Null
$records | Export-Csv -LiteralPath $SourceMapCsvPath -NoTypeInformation

$packetSummary = @(
    $records |
        Group-Object packet |
        Sort-Object Name |
        ForEach-Object {
            $groupRows = @($_.Group)
            $rowsWithInput = @($groupRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.input_address) })
            [pscustomobject]@{
                packet = $_.Name
                rows = $groupRows.Count
                rows_with_input = $rowsWithInput.Count
                output_index_min = ($groupRows | Measure-Object output_index -Minimum).Minimum
                output_index_max = ($groupRows | Measure-Object output_index -Maximum).Maximum
                source_x = "$(Format-Number (($groupRows | Measure-Object source_x -Minimum).Minimum))..$(Format-Number (($groupRows | Measure-Object source_x -Maximum).Maximum))"
                source_y = "$(Format-Number (($groupRows | Measure-Object source_y -Minimum).Minimum))..$(Format-Number (($groupRows | Measure-Object source_y -Maximum).Maximum))"
                source_z = "$(Format-Number (($groupRows | Measure-Object source_z -Minimum).Minimum))..$(Format-Number (($groupRows | Measure-Object source_z -Maximum).Maximum))"
                input_x = "$(Format-Number (($rowsWithInput | Measure-Object input_x -Minimum).Minimum))..$(Format-Number (($rowsWithInput | Measure-Object input_x -Maximum).Maximum))"
                input_y = "$(Format-Number (($rowsWithInput | Measure-Object input_y -Minimum).Minimum))..$(Format-Number (($rowsWithInput | Measure-Object input_y -Maximum).Maximum))"
                input_z = "$(Format-Number (($rowsWithInput | Measure-Object input_z -Minimum).Minimum))..$(Format-Number (($rowsWithInput | Measure-Object input_z -Maximum).Maximum))"
                input_colors = (@($rowsWithInput | Group-Object input_color | Sort-Object Count -Descending | Select-Object -First 4 | ForEach-Object { "$($_.Name):$($_.Count)" }) -join "; ")
            }
        }
)

$packetSummary | Export-Csv -LiteralPath $PacketSummaryCsvPath -NoTypeInformation

[pscustomobject][ordered]@{
    provenanceCsvPath = $provenancePath
    transformCsvPath = $transformPath
    sourceMapCsvPath = $SourceMapCsvPath
    packetSummaryCsvPath = $PacketSummaryCsvPath
    rowCount = $records.Count
    rowsWithInput = @($records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.input_address) }).Count
    packetSummary = $packetSummary
    packets = @($records | Group-Object packet | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    transformPcs = @($records | Group-Object transform_pc | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    firstRows = @($records | Select-Object -First $Top gx_fifo_offset,packet,source_record,output_index,source_x,source_y,source_z,input_address,input_color,input_x,input_y,input_z)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic transform source map from provenance: $SourceMapCsvPath"
$records | Select-Object -First $Top gx_fifo_offset,packet,source_record,output_index,source_x,source_y,source_z,input_address,input_color,input_x,input_y,input_z | Format-Table -AutoSize
