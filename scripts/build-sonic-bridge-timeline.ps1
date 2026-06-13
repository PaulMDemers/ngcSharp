param(
    [Parameter(Mandatory = $true)]
    [string]$EmitterStreamCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$TransformCsvPath,
    [string]$DrawPacketCsvPath = "",
    [string]$InputWriteCsvPath = "",
    [uint32]$SourceRecord = 0x80B286E0,
    [string]$CsvPath = "",
    [string]$JsonPath = "",
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexUInt32 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [uint32]0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToUInt32($text.Substring(2), 16)
    }

    return [Convert]::ToUInt32($text, 16)
}

function Convert-HexStringToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return [byte[]]::new(0)
    }

    $text = $Hex.Trim()
    if (($text.Length % 2) -ne 0) {
        throw "Odd-length hex byte string: $text"
    }

    $bytes = [byte[]]::new($text.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($text.Substring($i * 2, 2), 16)
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

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Get-RecordBytes {
    param(
        [string]$Hex,
        [int]$Offset,
        [int]$Length
    )

    if ([string]::IsNullOrWhiteSpace($Hex) -or $Offset -lt 0) {
        return ""
    }

    $start = $Offset * 2
    $chars = $Length * 2
    if (($start + $chars) -gt $Hex.Length) {
        return ""
    }

    return $Hex.Substring($start, $chars)
}

$emitterFullPath = Resolve-FullPath $EmitterStreamCsvPath
$transformFullPath = Resolve-FullPath $TransformCsvPath
if (-not (Test-Path -LiteralPath $emitterFullPath)) {
    throw "Emitter stream CSV not found: $emitterFullPath"
}

if (-not (Test-Path -LiteralPath $transformFullPath)) {
    throw "Transform CSV not found: $transformFullPath"
}

$emitterRows = @(Import-Csv -LiteralPath $emitterFullPath)
$transformRows = @(Import-Csv -LiteralPath $transformFullPath)
$sourceRecordText = if ($SourceRecord -eq 0) { "all" } else { Format-Hex32 $SourceRecord }
$targetEmitterRows = if ($SourceRecord -eq 0) {
    @($emitterRows)
} else {
    @($emitterRows | Where-Object { $_.sourceRecord -eq $sourceRecordText })
}

$packetRows = @()
if (-not [string]::IsNullOrWhiteSpace($DrawPacketCsvPath)) {
    $drawPacketFullPath = Resolve-FullPath $DrawPacketCsvPath
    if (-not (Test-Path -LiteralPath $drawPacketFullPath)) {
        throw "Draw packet CSV not found: $drawPacketFullPath"
    }

    $packetRows = @(Import-Csv -LiteralPath $drawPacketFullPath)
}

$inputWriteRows = @()
$finalInputWriteBytes = ""
if (-not [string]::IsNullOrWhiteSpace($InputWriteCsvPath)) {
    $inputWriteFullPath = Resolve-FullPath $InputWriteCsvPath
    if (-not (Test-Path -LiteralPath $inputWriteFullPath)) {
        throw "Input write CSV not found: $inputWriteFullPath"
    }

    $inputWriteRows = @(Import-Csv -LiteralPath $inputWriteFullPath)
    if ($inputWriteRows.Count -gt 0) {
        $finalInputWriteBytes = $inputWriteRows[-1].range_bytes
    }
}

$timelineRows = foreach ($emitter in $targetEmitterRows) {
    $source = Convert-HexUInt32 $emitter.sourceRecord
    $transformMatches = @(
        $transformRows |
            Where-Object {
                $outputCursor = Convert-HexUInt32 $_.output_cursor
                $source -ge $outputCursor -and $source -lt ($outputCursor + [uint32]0x20)
            } |
            Sort-Object {[int64]$_.instruction}
    )

    $transform = if ($transformMatches.Count -gt 0) { $transformMatches[0] } else { $null }
    $packet = $null
    if ($packetRows.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($emitter.packet)) {
        $packet = @($packetRows | Where-Object { $_.packet -eq $emitter.packet } | Select-Object -First 1)[0]
    }

    $inputBytes = ""
    $inputColor = ""
    $inputX = ""
    $inputY = ""
    $inputZ = ""
    $transformOffset = ""
    if ($null -ne $transform) {
        $transformOutputCursor = Convert-HexUInt32 $transform.output_cursor
        $offset = [int]($source - $transformOutputCursor)
        $transformOffset = "0x{0:X}" -f $offset
        $inputBytes = Get-RecordBytes $transform.input_bytes 0 0x10
        $bytes = Convert-HexStringToBytes $inputBytes
        $inputColor = Format-Hex32 (Read-BeUInt32 $bytes 0)
        $inputX = Format-Hex32 (Read-BeUInt32 $bytes 4)
        $inputY = Format-Hex32 (Read-BeUInt32 $bytes 8)
        $inputZ = Format-Hex32 (Read-BeUInt32 $bytes 12)
    }

    [pscustomobject][ordered]@{
        packet = $emitter.packet
        packet_stream0 = if ($null -ne $packet) { $packet.stream0 } else { "" }
        packet_stream1 = if ($null -ne $packet) { $packet.stream1 } else { "" }
        vertex_base = if ($null -ne $packet) { $packet.vertex_base } else { "" }
        stream_record = $emitter.streamRecord
        stream_offset = $emitter.streamOffset
        stream_record_bytes = $emitter.recordBytes
        decoded_index = $emitter.decodedIndex
        decoded_attr0 = $emitter.decodedAttr0
        decoded_attr1 = $emitter.decodedAttr1
        transform_instruction = if ($null -ne $transform) { $transform.instruction } else { "" }
        transform_pc = if ($null -ne $transform) { $transform.pc } else { "" }
        transform_output_cursor = if ($null -ne $transform) { $transform.output_cursor } else { "" }
        transform_source_offset = $transformOffset
        transform_input_cursor = if ($null -ne $transform) { $transform.input_cursor } else { "" }
        transform_gqr1 = if ($null -ne $transform) { $transform.gqr1 } else { "" }
        transform_f0 = if ($null -ne $transform) { $transform.f0 } else { "" }
        transform_f1 = if ($null -ne $transform) { $transform.f1 } else { "" }
        transform_f2 = if ($null -ne $transform) { $transform.f2 } else { "" }
        transform_f3 = if ($null -ne $transform) { $transform.f3 } else { "" }
        transform_f4 = if ($null -ne $transform) { $transform.f4 } else { "" }
        transform_f5 = if ($null -ne $transform) { $transform.f5 } else { "" }
        transform_f6 = if ($null -ne $transform) { $transform.f6 } else { "" }
        transform_f7 = if ($null -ne $transform) { $transform.f7 } else { "" }
        input_record_bytes = $inputBytes
        input_color = $inputColor
        input_x_bits = $inputX
        input_y_bits = $inputY
        input_z_bits = $inputZ
        final_input_write_bytes = $finalInputWriteBytes
        source_record = $emitter.sourceRecord
        source_x = $emitter.sourceX
        source_y = $emitter.sourceY
        source_z = $emitter.sourceZ
        gx_fifo_offset = $emitter.gxFifoOffset
        emitter_instruction = $emitter.instruction
    }
}

if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
    $csvFullPath = Resolve-FullPath $CsvPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $csvFullPath) | Out-Null
    $timelineRows | Export-Csv -LiteralPath $csvFullPath -NoTypeInformation
}

$summary = [pscustomobject][ordered]@{
    sourceRecord = $sourceRecordText
    emitterStreamCsvPath = $emitterFullPath
    transformCsvPath = $transformFullPath
    targetEmitterRows = $targetEmitterRows.Count
    timelineRows = @($timelineRows).Count
    finalInputWriteBytes = $finalInputWriteBytes
    sampleRows = @($timelineRows | Select-Object -First 8)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 12
}
