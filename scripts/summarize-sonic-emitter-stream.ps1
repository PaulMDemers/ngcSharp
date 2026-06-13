param(
    [Parameter(Mandatory = $true)]
    [string]$EmitterCsvPath,
    [string]$DrawPacketCsvPath = "",
    [string]$RecordCsvPath = "",
    [string]$JsonPath = "",
    [int]$Top = 16,
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

function To-Signed16 {
    param([uint16]$Value)

    if (($Value -band 0x8000) -ne 0) {
        return [int]$Value - 0x10000
    }

    return [int]$Value
}

function Format-Hex16 {
    param([int]$Value)

    return "0x{0:X4}" -f ($Value -band 0xFFFF)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

$emitterFullPath = Resolve-FullPath $EmitterCsvPath
if (-not (Test-Path -LiteralPath $emitterFullPath)) {
    throw "Sonic GX emitter CSV not found: $emitterFullPath"
}

$emitterRows = @(Import-Csv -LiteralPath $emitterFullPath)
if ($emitterRows.Count -eq 0) {
    throw "Sonic GX emitter CSV has no rows: $emitterFullPath"
}

$streamWindows = @()
if (-not [string]::IsNullOrWhiteSpace($DrawPacketCsvPath)) {
    $drawPacketFullPath = Resolve-FullPath $DrawPacketCsvPath
    if (-not (Test-Path -LiteralPath $drawPacketFullPath)) {
        throw "Sonic draw packet CSV not found: $drawPacketFullPath"
    }

    foreach ($packet in @(Import-Csv -LiteralPath $drawPacketFullPath)) {
        $stream1 = Convert-HexUInt32 $packet.stream1
        $bytes = Convert-HexStringToBytes $packet.stream1_bytes
        if ($stream1 -ne 0 -and $bytes.Length -gt 0) {
            $streamWindows += [pscustomobject][ordered]@{
                packet = $packet.packet
                stream1 = $stream1
                end = $stream1 + [uint32]$bytes.Length
                bytes = $bytes
            }
        }
    }
}

function Find-StreamWindow {
    param([uint32]$Address)

    foreach ($window in $streamWindows) {
        if ($Address -ge $window.stream1 -and $Address -lt $window.end) {
            return $window
        }
    }

    return $null
}

$sourceRows = @(
    $emitterRows |
        Where-Object { $_.pc -eq "0x801200C8" } |
        ForEach-Object {
            $streamCursor = Convert-HexUInt32 $_.stream_cursor
            $recordAddress = [uint32]($streamCursor - 6)
            $vertexBase = Convert-HexUInt32 $_.vertex_base
            $sourceRecord = Convert-HexUInt32 $_.source_record
            $actualIndex = if ($vertexBase -ne 0 -and $sourceRecord -ge $vertexBase) { [int](($sourceRecord - $vertexBase) / 0x20) } else { -1 }
            $window = Find-StreamWindow $recordAddress
            $decodedIndex = $null
            $decodedAttr0 = $null
            $decodedAttr1 = $null
            $decodedBytes = ""
            $packet = ""
            $streamOffset = ""

            if ($null -ne $window) {
                $offset = [int]($recordAddress - $window.stream1)
                $decodedIndex = [int](To-Signed16 (Read-BeUInt16 $window.bytes $offset))
                $decodedAttr0 = [int](To-Signed16 (Read-BeUInt16 $window.bytes ($offset + 2)))
                $decodedAttr1 = [int](To-Signed16 (Read-BeUInt16 $window.bytes ($offset + 4)))
                $decodedBytes = "{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}" -f $window.bytes[$offset], $window.bytes[$offset + 1], $window.bytes[$offset + 2], $window.bytes[$offset + 3], $window.bytes[$offset + 4], $window.bytes[$offset + 5]
                $packet = $window.packet
                $streamOffset = "0x{0:X}" -f $offset
            }

            $r29 = [int](To-Signed16 ([uint16]((Convert-HexUInt32 $_.r29) -band 0xFFFF)))
            $r28 = [int](To-Signed16 ([uint16]((Convert-HexUInt32 $_.r28) -band 0xFFFF)))

            [pscustomobject][ordered]@{
                instruction = [int64]$_.instruction
                gxFifoOffset = $_.gx_fifo_offset
                packet = $packet
                streamRecord = Format-Hex32 $recordAddress
                streamCursor = $_.stream_cursor
                streamOffset = $streamOffset
                recordBytes = $decodedBytes
                decodedIndex = $decodedIndex
                actualIndex = $actualIndex
                indexMatches = ($decodedIndex -eq $actualIndex)
                decodedAttr0 = if ($null -ne $decodedAttr0) { Format-Hex16 $decodedAttr0 } else { "" }
                r29 = $_.r29
                attr0Matches = ($decodedAttr0 -eq $r29)
                decodedAttr1 = if ($null -ne $decodedAttr1) { Format-Hex16 $decodedAttr1 } else { "" }
                r28 = $_.r28
                attr1Matches = ($decodedAttr1 -eq $r28)
                sourceRecord = $_.source_record
                sourceX = $_.source_x
                sourceY = $_.source_y
                sourceZ = $_.source_z
            }
        }
)

$summaryObject = [ordered]@{
    emitterCsvPath = $emitterFullPath
    rows = $emitterRows.Count
    decodedSourceRows = $sourceRows.Count
    streamWindows = $streamWindows.Count
    matchedStreamRecords = @($sourceRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.packet) }).Count
    indexMismatches = @($sourceRows | Where-Object { $_.indexMatches -eq $false }).Count
    attr0Mismatches = @($sourceRows | Where-Object { $_.attr0Matches -eq $false }).Count
    attr1Mismatches = @($sourceRows | Where-Object { $_.attr1Matches -eq $false }).Count
    sampleRows = @($sourceRows | Select-Object -First $Top)
}

if (-not [string]::IsNullOrWhiteSpace($RecordCsvPath)) {
    $recordCsvFullPath = Resolve-FullPath $RecordCsvPath
    $directory = Split-Path -Parent $recordCsvFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $sourceRows | Export-Csv -LiteralPath $recordCsvFullPath -NoTypeInformation
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    $directory = Split-Path -Parent $jsonFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $summaryObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if ($PassThru) {
    [pscustomobject]$summaryObject
} else {
    [pscustomobject]@{
        path = $emitterFullPath
        rows = $emitterRows.Count
        decodedSourceRows = $sourceRows.Count
        streamWindows = $streamWindows.Count
        matchedStreamRecords = $summaryObject.matchedStreamRecords
        indexMismatches = $summaryObject.indexMismatches
        attr0Mismatches = $summaryObject.attr0Mismatches
        attr1Mismatches = $summaryObject.attr1Mismatches
    } | Format-List

    Write-Host ""
    Write-Host "First decoded source stream records:"
    $sourceRows | Select-Object -First $Top | Format-Table -AutoSize
}
