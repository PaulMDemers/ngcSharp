param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$JsonPath = "",
    [string]$RecordCsvPath = "",
    [int]$Top = 12,
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

function Convert-Int64Value {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [int64]0
    }

    return [int64]$Value
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

    return [uint32](
        ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3])
}

function Read-BeUInt16 {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or ($Offset + 2) -gt $Bytes.Length) {
        return [uint16]0
    }

    return [uint16]((([uint16]$Bytes[$Offset]) -shl 8) -bor [uint16]$Bytes[$Offset + 1])
}

function Convert-BeUInt32ToFloat {
    param([uint32]$Value)

    $bytes = [byte[]]@(
        [byte](($Value -shr 24) -band 0xFF),
        [byte](($Value -shr 16) -band 0xFF),
        [byte](($Value -shr 8) -band 0xFF),
        [byte]($Value -band 0xFF)
    )

    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($bytes)
    }

    return [BitConverter]::ToSingle($bytes, 0)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Format-Hex16 {
    param([uint16]$Value)

    return "0x{0:X4}" -f $Value
}

function Format-Float {
    param([single]$Value)

    if ([single]::IsNaN($Value) -or [single]::IsInfinity($Value)) {
        return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $Value.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-HexPrefix {
    param(
        [string]$Hex,
        [int]$ByteCount
    )

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return ""
    }

    $chars = [Math]::Min($Hex.Trim().Length, $ByteCount * 2)
    return $Hex.Trim().Substring(0, $chars).ToUpperInvariant()
}

function Decode-VertexSample {
    param([string]$Hex)

    $bytes = Convert-HexStringToBytes $Hex
    if ($bytes.Length -lt 16) {
        return ""
    }

    $x = Convert-BeUInt32ToFloat (Read-BeUInt32 $bytes 0)
    $y = Convert-BeUInt32ToFloat (Read-BeUInt32 $bytes 4)
    $z = Convert-BeUInt32ToFloat (Read-BeUInt32 $bytes 8)
    $word3 = Read-BeUInt32 $bytes 12
    return "xyz=($(Format-Float $x),$(Format-Float $y),$(Format-Float $z)) w3=$(Format-Hex32 $word3)"
}

function Decode-StreamHeader {
    param([string]$Hex)

    $bytes = Convert-HexStringToBytes $Hex
    if ($bytes.Length -eq 0) {
        return ""
    }

    $bytePrefix = @(
        for ($i = 0; $i -lt [Math]::Min(8, $bytes.Length); $i++) {
            "0x{0:X2}" -f $bytes[$i]
        }
    ) -join ","

    $halfwords = @(
        for ($i = 0; $i -lt [Math]::Min(4, [int]($bytes.Length / 2)); $i++) {
            Format-Hex16 (Read-BeUInt16 $bytes ($i * 2))
        }
    ) -join ","

    $words = @(
        for ($i = 0; $i -lt [Math]::Min(2, [int]($bytes.Length / 4)); $i++) {
            Format-Hex32 (Read-BeUInt32 $bytes ($i * 4))
        }
    ) -join ","

    return "b8=$bytePrefix h4=$halfwords w2=$words"
}

function Get-StreamMetrics {
    param([string]$Hex)

    $bytes = Convert-HexStringToBytes $Hex
    $h0 = Read-BeUInt16 $bytes 0
    $h1 = Read-BeUInt16 $bytes 2
    $h2 = Read-BeUInt16 $bytes 4
    $h3 = Read-BeUInt16 $bytes 6
    $packedCountMatches = ($h0 -eq (($h2 * 4) + 1))
    $opcode = if ($bytes.Length -gt 0) { "0x{0:X2}" -f $bytes[0] } else { "" }
    $subcode = if ($bytes.Length -gt 1) { "0x{0:X2}" -f $bytes[1] } else { "" }

    return [pscustomobject][ordered]@{
        h0 = Format-Hex16 $h0
        h1 = Format-Hex16 $h1
        h2 = Format-Hex16 $h2
        h3 = Format-Hex16 $h3
        candidateCount = [int]$h2
        packedCountMatches = $packedCountMatches
        opcode = $opcode
        subcode = $subcode
    }
}

function Decode-Packet {
    param([object]$Row)

    $packetBytes = Convert-HexStringToBytes $Row.packet_bytes
    $stream0Metrics = Get-StreamMetrics $Row.stream0_bytes
    $stream1Metrics = Get-StreamMetrics $Row.stream1_bytes
    $words = @()
    for ($i = 0; $i -lt 8; $i++) {
        $words += Read-BeUInt32 $packetBytes ($i * 4)
    }

    [pscustomobject][ordered]@{
        instruction = Convert-Int64Value $Row.instruction
        pc = $Row.pc
        lr = $Row.lr
        packet = $Row.packet
        stream0 = $Row.stream0
        stream1 = $Row.stream1
        vertexBase = $Row.vertex_base
        kind = Format-Hex32 $words[6]
        kindFromR30 = $Row.r30
        stream0Prefix = Get-HexPrefix $Row.stream0_bytes 16
        stream1Prefix = Get-HexPrefix $Row.stream1_bytes 16
        stream0Header = Decode-StreamHeader $Row.stream0_bytes
        stream1Header = Decode-StreamHeader $Row.stream1_bytes
        stream0CandidateCount = $stream0Metrics.candidateCount
        stream0PackedCountMatches = $stream0Metrics.packedCountMatches
        stream0H0 = $stream0Metrics.h0
        stream0H1 = $stream0Metrics.h1
        stream0H2 = $stream0Metrics.h2
        stream0H3 = $stream0Metrics.h3
        stream1Opcode = $stream1Metrics.opcode
        stream1Subcode = $stream1Metrics.subcode
        stream1H0 = $stream1Metrics.h0
        stream1H1 = $stream1Metrics.h1
        stream1H2 = $stream1Metrics.h2
        stream1H3 = $stream1Metrics.h3
        packetWord0 = Format-Hex32 $words[0]
        packetWord1 = Format-Hex32 $words[1]
        packetFloat2 = Format-Float (Convert-BeUInt32ToFloat $words[2])
        packetFloat3 = Format-Float (Convert-BeUInt32ToFloat $words[3])
        packetFloat4 = Format-Float (Convert-BeUInt32ToFloat $words[4])
        packetFloat5 = Format-Float (Convert-BeUInt32ToFloat $words[5])
        packetWord7 = Format-Hex32 $words[7]
        r4 = $Row.r4
        r5 = $Row.r5
        r6 = $Row.r6
        r7 = $Row.r7
        vertexSample = Decode-VertexSample $Row.vertex_base_bytes
    }
}

function Get-Stream0RecordRows {
    param(
        [object]$Row,
        [object]$Decoded
    )

    $bytes = Convert-HexStringToBytes $Row.stream0_bytes
    $recordCount = [int]$Decoded.stream0CandidateCount
    $maxRecords = [Math]::Min($recordCount, [int](($bytes.Length - 8) / 4))
    if ($maxRecords -le 0) {
        return @()
    }

    return @(
        for ($index = 0; $index -lt $maxRecords; $index++) {
            $offset = 8 + ($index * 4)
            $word = Read-BeUInt32 $bytes $offset
            $rawBytes = "{0:X2}{1:X2}{2:X2}{3:X2}" -f $bytes[$offset], $bytes[$offset + 1], $bytes[$offset + 2], $bytes[$offset + 3]
            [pscustomobject][ordered]@{
                instruction = $Decoded.instruction
                packet = $Decoded.packet
                kind = $Decoded.kind
                recordIndex = $index
                stream0Offset = "0x{0:X4}" -f $offset
                word = Format-Hex32 $word
                float = Format-Float (Convert-BeUInt32ToFloat $word)
                bytes = $rawBytes
            }
        }
    )
}

$traceFullPath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $traceFullPath)) {
    throw "Sonic draw packet trace not found: $traceFullPath"
}

$rows = @(Import-Csv -LiteralPath $traceFullPath)
if ($rows.Count -eq 0) {
    throw "Sonic draw packet trace has no rows: $traceFullPath"
}

$decoded = @($rows | ForEach-Object { Decode-Packet $_ })
$firstInstruction = ($decoded | Select-Object -First 1).instruction
$lastInstruction = ($decoded | Select-Object -Last 1).instruction

$kindGroups = @(
    $decoded |
        Group-Object kind,kindFromR30,lr,r4,r6,r7 |
        Sort-Object Count -Descending |
        Select-Object -First $Top |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject][ordered]@{
                count = $_.Count
                kind = $first.kind
                r30 = $first.kindFromR30
                lr = $first.lr
                r4 = $first.r4
                r6 = $first.r6
                r7 = $first.r7
                firstInstruction = $first.instruction
                stream0Prefix = $first.stream0Prefix
                stream1Prefix = $first.stream1Prefix
                stream0Header = $first.stream0Header
                stream1Header = $first.stream1Header
                stream0CandidateCount = $first.stream0CandidateCount
                stream0PackedCountMatches = $first.stream0PackedCountMatches
                stream1Opcode = $first.stream1Opcode
                stream1Subcode = $first.stream1Subcode
                packetFloats = "$($first.packetFloat2),$($first.packetFloat3),$($first.packetFloat4),$($first.packetFloat5)"
                vertexSample = $first.vertexSample
            }
        }
)

$streamGroups = @(
    $decoded |
        Group-Object stream0Prefix,stream1Prefix,kind |
        Sort-Object Count -Descending |
        Select-Object -First $Top |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject][ordered]@{
                count = $_.Count
                kind = $first.kind
                stream0Prefix = $first.stream0Prefix
                stream1Prefix = $first.stream1Prefix
                stream0Header = $first.stream0Header
                stream1Header = $first.stream1Header
                stream0CandidateCount = $first.stream0CandidateCount
                stream0PackedCountMatches = $first.stream0PackedCountMatches
                stream1Opcode = $first.stream1Opcode
                stream1Subcode = $first.stream1Subcode
                firstPacket = $first.packet
                firstInstruction = $first.instruction
                packetFloats = "$($first.packetFloat2),$($first.packetFloat3),$($first.packetFloat4),$($first.packetFloat5)"
            }
        }
)

$samplePackets = @(
    $decoded |
        Select-Object -First $Top instruction,packet,kind,stream0,stream1,packetFloat2,packetFloat3,packetFloat4,packetFloat5,r4,r6,r7,stream0CandidateCount,stream0PackedCountMatches,stream1Opcode,stream1Subcode,stream0Header,stream1Header,vertexSample
)

$summaryObject = [ordered]@{
    traceCsvPath = $traceFullPath
    bytes = (Get-Item -LiteralPath $traceFullPath).Length
    rows = $rows.Count
    firstInstruction = $firstInstruction
    lastInstruction = $lastInstruction
    instructionSpan = $lastInstruction - $firstInstruction
    uniquePackets = @($decoded | Select-Object -ExpandProperty packet -Unique).Count
    uniqueStream0 = @($decoded | Select-Object -ExpandProperty stream0 -Unique).Count
    uniqueStream1 = @($decoded | Select-Object -ExpandProperty stream1 -Unique).Count
    vertexBases = @($decoded | Select-Object -ExpandProperty vertexBase -Unique)
    kindGroups = @($kindGroups)
    streamGroups = @($streamGroups)
    samplePackets = @($samplePackets)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    $directory = Split-Path -Parent $jsonFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $summaryObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if (-not [string]::IsNullOrWhiteSpace($RecordCsvPath)) {
    $recordCsvFullPath = Resolve-FullPath $RecordCsvPath
    $directory = Split-Path -Parent $recordCsvFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $recordRows = @(
        for ($i = 0; $i -lt $rows.Count; $i++) {
            Get-Stream0RecordRows $rows[$i] $decoded[$i]
        }
    )

    $recordRows | Export-Csv -LiteralPath $recordCsvFullPath -NoTypeInformation
}

if ($PassThru) {
    [pscustomobject]$summaryObject
} else {
    [pscustomobject]@{
        path = $traceFullPath
        rows = $rows.Count
        uniquePackets = $summaryObject.uniquePackets
        uniqueStream0 = $summaryObject.uniqueStream0
        uniqueStream1 = $summaryObject.uniqueStream1
        vertexBases = (@($summaryObject.vertexBases) -join ",")
        instructionSpan = $summaryObject.instructionSpan
    } | Format-List

    Write-Host ""
    Write-Host "Top packet/register groups:"
    $kindGroups | Format-Table -AutoSize

    Write-Host ""
    Write-Host "Top stream signature groups:"
    $streamGroups | Format-Table -AutoSize

    Write-Host ""
    Write-Host "First decoded packets:"
    $samplePackets | Format-Table -AutoSize
}
