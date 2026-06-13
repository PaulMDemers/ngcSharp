param(
    [Parameter(Mandatory = $true)]
    [string]$LineageCsvPath,
    [string]$SourceMapCsvPath = "",
    [string]$SummaryJsonPath = "",
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

function Get-InputRecord {
    param(
        [byte[]]$Bytes,
        [uint32]$InputCursor,
        [int]$OutputIndex
    )

    # In the Sonic 2D transform loop, output index 0 is already staged in FPRs
    # on entry, and output index 1 uses the preloaded f8/f9 pair. Input bytes
    # captured at input_cursor begin feeding output index 2.
    $inputIndex = $OutputIndex - 2
    if ($inputIndex -lt 0) {
        return $null
    }

    $offset = $inputIndex * 0x10
    if (($offset + 0x10) -gt $Bytes.Length) {
        return $null
    }

    [pscustomobject][ordered]@{
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

$lineagePath = Resolve-FullPath $LineageCsvPath
if (-not (Test-Path -LiteralPath $lineagePath)) {
    throw "Lineage CSV not found: $lineagePath"
}

$directory = Split-Path -Parent $lineagePath
if ([string]::IsNullOrWhiteSpace($SourceMapCsvPath)) {
    $SourceMapCsvPath = Join-Path $directory "sonic-transform-source-map.csv"
} else {
    $SourceMapCsvPath = Resolve-FullPath $SourceMapCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-transform-source-map.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $lineagePath)
if ($rows.Count -eq 0) {
    throw "Lineage CSV has no rows: $lineagePath"
}

$records = foreach ($row in $rows) {
    $sourceRecord = Parse-HexOrDecimal $row.source_record
    $outputCursor = Parse-HexOrDecimal $row.transform_output_cursor
    $inputCursor = Parse-HexOrDecimal $row.transform_input_cursor
    $outputIndex = if ($sourceRecord -ge ($outputCursor + 8)) {
        [int](($sourceRecord - $outputCursor - 8) / 0x20)
    } else {
        -1
    }

    $inputBytes = Convert-HexStringToBytes $row.transform_input_bytes
    $inputRecord = Get-InputRecord $inputBytes $inputCursor $outputIndex

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
        transform_instruction = $row.transform_instruction
        transform_pc = $row.transform_pc
        transform_output_cursor = $row.transform_output_cursor
        transform_input_cursor = $row.transform_input_cursor
        transform_iterations = $row.transform_iterations
        transform_gqr1 = $row.transform_gqr1
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

$summary = [pscustomobject][ordered]@{
    lineageCsvPath = $lineagePath
    sourceMapCsvPath = $SourceMapCsvPath
    rowCount = $records.Count
    rowsWithInput = @($records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.input_address) }).Count
    transformPcs = @($records | Group-Object transform_pc | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    inputColors = @($records | Where-Object input_color | Group-Object input_color | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    firstRows = @($records | Select-Object -First $Top gx_fifo_offset, source_record, output_index, source_x, source_y, source_z, input_address, input_color, input_x, input_y, input_z)
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic transform source map: $SourceMapCsvPath"
$records | Select-Object -First $Top gx_fifo_offset,source_record,output_index,source_x,source_y,source_z,input_address,input_color,input_x,input_y,input_z | Format-Table -AutoSize
