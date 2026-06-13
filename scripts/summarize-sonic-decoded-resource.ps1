param(
    [Parameter(Mandatory = $true)]
    [string]$DecodedPath,

    [string]$BaseAddress = "0x8125FE60",

    [string[]]$Addresses = @(
        "0x813184D0",
        "0x813184E8",
        "0x81318FC8",
        "0x81318FE0",
        "0x81319AC0",
        "0x81319AD8"
    ),

    [int]$WindowBytes = 0x100,

    [int]$MaxStreamBytes = 0x4000,

    [switch]$ScanAllPackets,

    [switch]$PacketOnly,

    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-Number {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Read-BeUInt16 {
    param([byte[]]$Bytes, [int]$Offset)

    if ($Offset -lt 0 -or ($Offset + 2) -gt $Bytes.Length) {
        return [uint16]0
    }

    return [uint16]((([uint16]$Bytes[$Offset]) -shl 8) -bor [uint16]$Bytes[$Offset + 1])
}

function Read-BeUInt32 {
    param([byte[]]$Bytes, [int]$Offset)

    if ($Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) {
        return [uint32]0
    }

    return [uint32](
        ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3])
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

function Format-HexOffset {
    param([int64]$Value)
    if ($Value -lt 0) {
        return "-0x{0:X}" -f (-$Value)
    }

    return "0x{0:X}" -f $Value
}

function Format-Float {
    param([single]$Value)

    if ([single]::IsNaN($Value) -or [single]::IsInfinity($Value) -or [Math]::Abs([double]$Value) -gt 1000000000.0) {
        return ""
    }

    return $Value.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToSigned32 {
    param([uint32]$Value)

    if ($Value -gt 0x7FFFFFFF) {
        return [int64]$Value - 0x100000000
    }

    return [int64]$Value
}

function Convert-ToSigned16 {
    param([uint16]$Value)

    if ($Value -gt 0x7FFF) {
        return [int]$Value - 0x10000
    }

    return [int]$Value
}

function Get-Ascii4 {
    param([byte[]]$Bytes, [int]$Offset)

    if ($Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) {
        return ""
    }

    $chars = for ($index = 0; $index -lt 4; $index++) {
        $value = $Bytes[$Offset + $index]
        if ($value -ge 0x20 -and $value -le 0x7E) {
            [char]$value
        } else {
            "."
        }
    }

    return -join $chars
}

$decodedFullPath = Resolve-FullPath $DecodedPath
if (-not (Test-Path -LiteralPath $decodedFullPath)) {
    throw "Decoded resource not found: $decodedFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Split-Path -Parent $decodedFullPath
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$bytes = [System.IO.File]::ReadAllBytes($decodedFullPath)
$base = Parse-Number $BaseAddress
$endExclusive = [uint64]$base + [uint64]$bytes.Length
$parsedAddresses = @($Addresses | ForEach-Object { Parse-Number $_ })

$addressSummary = foreach ($address in $parsedAddresses) {
    $relative = [int64]$address - [int64]$base
    $inRange = $relative -ge 0 -and $relative -lt $bytes.Length
    [pscustomobject]@{
        address = Format-Hex32 $address
        decoded_offset = Format-HexOffset $relative
        in_range = $inRange
        window_bytes = if ($inRange) { [Math]::Min($WindowBytes, $bytes.Length - [int]$relative) } else { 0 }
    }
}

$fields = foreach ($address in $parsedAddresses) {
    $relative = [int64]$address - [int64]$base
    if ($relative -lt 0 -or $relative -ge $bytes.Length) {
        continue
    }

    $start = [int]$relative
    $length = [Math]::Min($WindowBytes, $bytes.Length - $start)
    for ($offset = 0; $offset -le $length - 4; $offset += 4) {
        $absolute = [uint32]([uint64]$address + [uint64]$offset)
        $fileOffset = $start + $offset
        $word = Read-BeUInt32 $bytes $fileOffset
        $high = Read-BeUInt16 $bytes $fileOffset
        $low = Read-BeUInt16 $bytes ($fileOffset + 2)
        $float = Convert-BeUInt32ToFloat $word
        $isPointer = [uint64]$word -ge [uint64]$base -and [uint64]$word -lt $endExclusive
        $pointerOffset = if ($isPointer) { Format-HexOffset ([int64]$word - [int64]$base) } else { "" }
        $nearestNamed = ""
        if ($isPointer) {
            foreach ($named in $parsedAddresses) {
                if ([Math]::Abs([int64]$word - [int64]$named) -le 0x100) {
                    $nearestNamed = Format-Hex32 $named
                    break
                }
            }
        }

        [pscustomobject]@{
            window_base = Format-Hex32 $address
            address = Format-Hex32 $absolute
            window_offset = Format-HexOffset $offset
            decoded_offset = Format-HexOffset $fileOffset
            word = Format-Hex32 $word
            signed_word = Convert-ToSigned32 $word
            float = Format-Float $float
            half0 = "0x{0:X4}" -f $high
            half1 = "0x{0:X4}" -f $low
            signed_half0 = Convert-ToSigned16 $high
            signed_half1 = Convert-ToSigned16 $low
            ascii4 = Get-Ascii4 $bytes $fileOffset
            is_pointer = $isPointer
            pointer_offset = $pointerOffset
            nearest_named_address = $nearestNamed
        }
    }
}

$packetCandidateMap = [ordered]@{}
foreach ($address in $parsedAddresses) {
    $packetCandidateMap[(Format-Hex32 $address)] = $address
}

if ($ScanAllPackets) {
    for ($offset = 0; $offset -le $bytes.Length - 0x44; $offset += 4) {
        $address = [uint32]([uint64]$base + [uint64]$offset)
        $kind = Read-BeUInt32 $bytes ($offset + 0x18)
        $selfPointer = Read-BeUInt32 $bytes ($offset + 0x1C)
        if ($kind -eq 0 -or $kind -gt 0x40 -or $selfPointer -ne $address) {
            continue
        }

        $stream0 = Read-BeUInt32 $bytes ($offset + 0x00)
        $stream1 = Read-BeUInt32 $bytes ($offset + 0x04)
        if ([uint64]$stream0 -lt [uint64]$base -or [uint64]$stream0 -ge $endExclusive) {
            continue
        }

        if ([uint64]$stream1 -lt [uint64]$base -or [uint64]$stream1 -ge $endExclusive) {
            continue
        }

        if ($stream1 -ge $stream0 -or $stream0 -ge $address) {
            continue
        }

        if (([int64]$address - [int64]$stream1) -gt $MaxStreamBytes) {
            continue
        }

        $packetCandidateMap[(Format-Hex32 $address)] = $address
    }
}

$packetCandidateAddresses = @($packetCandidateMap.Values | Sort-Object)

$packetSummaries = foreach ($address in $packetCandidateAddresses) {
    $relative = [int64]$address - [int64]$base
    if ($relative -lt 0 -or ($relative + 0x44) -gt $bytes.Length) {
        continue
    }

    $offset = [int]$relative
    $stream0 = Read-BeUInt32 $bytes ($offset + 0x00)
    $stream1 = Read-BeUInt32 $bytes ($offset + 0x04)
    $field2 = Read-BeUInt32 $bytes ($offset + 0x08)
    $field3 = Read-BeUInt32 $bytes ($offset + 0x0C)
    $field4 = Read-BeUInt32 $bytes ($offset + 0x10)
    $field5 = Read-BeUInt32 $bytes ($offset + 0x14)
    $kind = Read-BeUInt32 $bytes ($offset + 0x18)
    $selfPointer = Read-BeUInt32 $bytes ($offset + 0x1C)
    $objectX = Read-BeUInt32 $bytes ($offset + 0x20)
    $objectY = Read-BeUInt32 $bytes ($offset + 0x24)
    $objectZ = Read-BeUInt32 $bytes ($offset + 0x28)
    $scaleX = Read-BeUInt32 $bytes ($offset + 0x38)
    $scaleY = Read-BeUInt32 $bytes ($offset + 0x3C)
    $scaleZ = Read-BeUInt32 $bytes ($offset + 0x40)
    $stream1Length = if ($stream1 -lt $stream0 -and $stream0 -lt $address) { [int64]$stream0 - [int64]$stream1 } else { -1 }
    $stream0Length = if ($stream0 -lt $address) { [int64]$address - [int64]$stream0 } else { -1 }

    if (($kind -eq 0) -or ($kind -gt 0x100) -or ($selfPointer -ne $address)) {
        continue
    }

    [pscustomobject]@{
        packet_address = Format-Hex32 $address
        packet_offset = Format-HexOffset $relative
        object_address = Format-Hex32 ([uint32]([uint64]$address + 0x18))
        kind = Format-Hex32 $kind
        stream0 = Format-Hex32 $stream0
        stream0_offset = if ([uint64]$stream0 -ge [uint64]$base -and [uint64]$stream0 -lt $endExclusive) { Format-HexOffset ([int64]$stream0 - [int64]$base) } else { "" }
        stream1 = Format-Hex32 $stream1
        stream1_offset = if ([uint64]$stream1 -ge [uint64]$base -and [uint64]$stream1 -lt $endExclusive) { Format-HexOffset ([int64]$stream1 - [int64]$base) } else { "" }
        stream1_length = if ($stream1Length -ge 0) { Format-HexOffset $stream1Length } else { "" }
        stream1_triplets = if ($stream1Length -ge 0) { [Math]::Floor($stream1Length / 6) } else { "" }
        stream0_length = if ($stream0Length -ge 0) { Format-HexOffset $stream0Length } else { "" }
        stream0_words = if ($stream0Length -ge 0) { [Math]::Floor($stream0Length / 4) } else { "" }
        field2_word = Format-Hex32 $field2
        field2_float = Format-Float (Convert-BeUInt32ToFloat $field2)
        field3_word = Format-Hex32 $field3
        field3_float = Format-Float (Convert-BeUInt32ToFloat $field3)
        field4_word = Format-Hex32 $field4
        field4_float = Format-Float (Convert-BeUInt32ToFloat $field4)
        field5_word = Format-Hex32 $field5
        field5_float = Format-Float (Convert-BeUInt32ToFloat $field5)
        field5_signed = Convert-ToSigned32 $field5
        object_x = Format-Float (Convert-BeUInt32ToFloat $objectX)
        object_y = Format-Float (Convert-BeUInt32ToFloat $objectY)
        object_z = Format-Float (Convert-BeUInt32ToFloat $objectZ)
        scale_x = Format-Float (Convert-BeUInt32ToFloat $scaleX)
        scale_y = Format-Float (Convert-BeUInt32ToFloat $scaleY)
        scale_z = Format-Float (Convert-BeUInt32ToFloat $scaleZ)
    }
}

$packetFamilies = foreach ($group in ($packetSummaries | Group-Object kind)) {
    $packets = @($group.Group)
    $objectXs = @($packets | ForEach-Object { if ($_.object_x -ne "") { [double]::Parse($_.object_x, [System.Globalization.CultureInfo]::InvariantCulture) } })
    $objectYs = @($packets | ForEach-Object { if ($_.object_y -ne "") { [double]::Parse($_.object_y, [System.Globalization.CultureInfo]::InvariantCulture) } })
    $objectZs = @($packets | ForEach-Object { if ($_.object_z -ne "") { [double]::Parse($_.object_z, [System.Globalization.CultureInfo]::InvariantCulture) } })
    [pscustomobject]@{
        kind = $group.Name
        count = $packets.Count
        packets = ($packets.packet_address -join ";")
        object_x_min = if ($objectXs.Count -gt 0) { ($objectXs | Measure-Object -Minimum).Minimum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        object_x_max = if ($objectXs.Count -gt 0) { ($objectXs | Measure-Object -Maximum).Maximum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        object_y_min = if ($objectYs.Count -gt 0) { ($objectYs | Measure-Object -Minimum).Minimum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        object_y_max = if ($objectYs.Count -gt 0) { ($objectYs | Measure-Object -Maximum).Maximum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        object_z_min = if ($objectZs.Count -gt 0) { ($objectZs | Measure-Object -Minimum).Minimum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
        object_z_max = if ($objectZs.Count -gt 0) { ($objectZs | Measure-Object -Maximum).Maximum.ToString("G9", [System.Globalization.CultureInfo]::InvariantCulture) } else { "" }
    }
}

$field5Distribution = @($packetSummaries |
    Where-Object { $_.field5_float -ne "" } |
    Group-Object kind, field5_word |
    ForEach-Object {
        $packets = @($_.Group)
        [pscustomobject]@{
            kind = ($packets | Select-Object -First 1).kind
            field5_word = ($packets | Select-Object -First 1).field5_word
            field5_float = ($packets | Select-Object -First 1).field5_float
            count = $packets.Count
            packets = (($packets | Select-Object -ExpandProperty packet_address) -join ";")
            object_xyz = (($packets | Select-Object -First 8 | ForEach-Object { "$($_.object_x)/$($_.object_y)/$($_.object_z)" }) -join ";")
        }
    } |
    Sort-Object @{ Expression = "kind"; Ascending = $true }, @{ Expression = { [double]::Parse($_.field5_float, [System.Globalization.CultureInfo]::InvariantCulture) }; Ascending = $true })

$stream1Triplets = if ($PacketOnly) { @() } else { foreach ($packet in $packetSummaries) {
    $packetAddress = Parse-Number $packet.packet_address
    $stream0 = Parse-Number $packet.stream0
    $stream1 = Parse-Number $packet.stream1
    if ($stream1 -lt $base -or $stream1 -ge $endExclusive -or $stream1 -ge $packetAddress) {
        continue
    }

    $streamEnd = if ($stream0 -gt $stream1 -and $stream0 -lt $packetAddress) { $stream0 } else { $packetAddress }
    $streamOffset = [int]([int64]$stream1 - [int64]$base)
    $streamLength = [Math]::Min($MaxStreamBytes, [int]([int64]$streamEnd - [int64]$stream1))
    for ($offset = 0; $offset -le $streamLength - 6; $offset += 6) {
        $h0 = Read-BeUInt16 $bytes ($streamOffset + $offset)
        $h1 = Read-BeUInt16 $bytes ($streamOffset + $offset + 2)
        $h2 = Read-BeUInt16 $bytes ($streamOffset + $offset + 4)
        [pscustomobject]@{
            packet_address = $packet.packet_address
            stream_address = $packet.stream1
            record_index = [int]($offset / 6)
            record_address = Format-Hex32 ([uint32]([uint64]$stream1 + [uint64]$offset))
            record_offset = Format-HexOffset $offset
            index = Convert-ToSigned16 $h0
            attr0 = Convert-ToSigned16 $h1
            attr1 = Convert-ToSigned16 $h2
            raw0 = "0x{0:X4}" -f $h0
            raw1 = "0x{0:X4}" -f $h1
            raw2 = "0x{0:X4}" -f $h2
        }
    }
} }

$stream0Words = if ($PacketOnly) { @() } else { foreach ($packet in $packetSummaries) {
    $packetAddress = Parse-Number $packet.packet_address
    $stream0 = Parse-Number $packet.stream0
    if ($stream0 -lt $base -or $stream0 -ge $endExclusive -or $stream0 -ge $packetAddress) {
        continue
    }

    $streamOffset = [int]([int64]$stream0 - [int64]$base)
    $streamLength = [Math]::Min($MaxStreamBytes, [int]([int64]$packetAddress - [int64]$stream0))
    for ($offset = 0; $offset -le $streamLength - 4; $offset += 4) {
        $word = Read-BeUInt32 $bytes ($streamOffset + $offset)
        $float = Convert-BeUInt32ToFloat $word
        $isPointer = [uint64]$word -ge [uint64]$base -and [uint64]$word -lt $endExclusive
        [pscustomobject]@{
            packet_address = $packet.packet_address
            stream_address = $packet.stream0
            word_index = [int]($offset / 4)
            word_address = Format-Hex32 ([uint32]([uint64]$stream0 + [uint64]$offset))
            word_offset = Format-HexOffset $offset
            word = Format-Hex32 $word
            signed_word = Convert-ToSigned32 $word
            float = Format-Float $float
            half0 = "0x{0:X4}" -f (Read-BeUInt16 $bytes ($streamOffset + $offset))
            half1 = "0x{0:X4}" -f (Read-BeUInt16 $bytes ($streamOffset + $offset + 2))
            is_pointer = $isPointer
            pointer_offset = if ($isPointer) { Format-HexOffset ([int64]$word - [int64]$base) } else { "" }
        }
    }
} }

$pointers = if ($PacketOnly) { @() } else { for ($offset = 0; $offset -le $bytes.Length - 4; $offset += 4) {
    $word = Read-BeUInt32 $bytes $offset
    if ([uint64]$word -lt [uint64]$base -or [uint64]$word -ge $endExclusive) {
        continue
    }

    [pscustomobject]@{
        source_address = Format-Hex32 ([uint32]([uint64]$base + [uint64]$offset))
        source_offset = Format-HexOffset $offset
        target_address = Format-Hex32 $word
        target_offset = Format-HexOffset ([int64]$word - [int64]$base)
        delta = Format-HexOffset ([int64]$word - ([int64]$base + [int64]$offset))
    }
} }

$prefix = Join-Path $OutputDirectory "sonic-decoded-resource"
$addressSummaryPath = "$prefix.addresses.csv"
$packetSummaryPath = "$prefix.packets.csv"
$packetFamiliesPath = "$prefix.packet-families.csv"
$field5DistributionPath = "$prefix.field5-distribution.csv"
$stream1TripletsPath = "$prefix.stream1-triplets.csv"
$stream0WordsPath = "$prefix.stream0-words.csv"
$fieldsPath = "$prefix.fields.csv"
$pointersPath = "$prefix.pointers.csv"
$jsonPath = "$prefix.summary.json"

$addressSummary | Export-Csv -NoTypeInformation -Path $addressSummaryPath
$packetSummaries | Export-Csv -NoTypeInformation -Path $packetSummaryPath
$packetFamilies | Export-Csv -NoTypeInformation -Path $packetFamiliesPath
$field5Distribution | Export-Csv -NoTypeInformation -Path $field5DistributionPath
$stream1Triplets | Export-Csv -NoTypeInformation -Path $stream1TripletsPath
$stream0Words | Export-Csv -NoTypeInformation -Path $stream0WordsPath
$fields | Export-Csv -NoTypeInformation -Path $fieldsPath
$pointers | Export-Csv -NoTypeInformation -Path $pointersPath

$json = [pscustomobject]@{
    decoded_path = $decodedFullPath
    base_address = Format-Hex32 $base
    decoded_length = "0x{0:X}" -f $bytes.Length
    address_count = @($addressSummary).Count
    packet_count = @($packetSummaries).Count
    packet_family_count = @($packetFamilies).Count
    field5_distribution_count = @($field5Distribution).Count
    stream1_triplet_count = @($stream1Triplets).Count
    stream0_word_count = @($stream0Words).Count
    field_count = @($fields).Count
    pointer_count = @($pointers).Count
    addresses_csv = $addressSummaryPath
    packets_csv = $packetSummaryPath
    packet_families_csv = $packetFamiliesPath
    field5_distribution_csv = $field5DistributionPath
    stream1_triplets_csv = $stream1TripletsPath
    stream0_words_csv = $stream0WordsPath
    fields_csv = $fieldsPath
    pointers_csv = $pointersPath
}
$json | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath

$json
