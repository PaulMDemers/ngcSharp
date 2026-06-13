param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$OutCsvPath = "",
    [string]$OutJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexToUInt32 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $text = $text.Substring(2)
    }

    return [uint32]::Parse($text, [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToInt32Invariant {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0
    }

    return [int]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-TextureRegisterInfo {
    param([byte]$Register)

    $mode0 = @(0x80, 0x81, 0x82, 0x83, 0xA0, 0xA1, 0xA2, 0xA3)
    $mode1 = @(0x84, 0x85, 0x86, 0x87, 0xA4, 0xA5, 0xA6, 0xA7)
    $image0 = @(0x88, 0x89, 0x8A, 0x8B, 0xA8, 0xA9, 0xAA, 0xAB)
    $image1 = @(0x8C, 0x8D, 0x8E, 0x8F, 0xAC, 0xAD, 0xAE, 0xAF)
    $image2 = @(0x90, 0x91, 0x92, 0x93, 0xB0, 0xB1, 0xB2, 0xB3)
    $image3 = @(0x94, 0x95, 0x96, 0x97, 0xB4, 0xB5, 0xB6, 0xB7)
    $tlut = @(0x98, 0x99, 0x9A, 0x9B, 0xB8, 0xB9, 0xBA, 0xBB)

    $groups = @(
        @{ Name = "mode0"; Registers = $mode0 },
        @{ Name = "mode1"; Registers = $mode1 },
        @{ Name = "image0"; Registers = $image0 },
        @{ Name = "image1"; Registers = $image1 },
        @{ Name = "image2"; Registers = $image2 },
        @{ Name = "image3"; Registers = $image3 },
        @{ Name = "tlut"; Registers = $tlut }
    )

    foreach ($group in $groups) {
        for ($index = 0; $index -lt $group.Registers.Count; $index++) {
            if ($Register -eq [byte]$group.Registers[$index]) {
                return [pscustomobject]@{
                    kind = $group.Name
                    slot = $index
                }
            }
        }
    }

    return $null
}

function Get-TextureFormatName {
    param([int]$Format)

    switch ($Format) {
        0 { "I4" }
        1 { "I8" }
        2 { "IA4" }
        3 { "IA8" }
        4 { "RGB565" }
        5 { "RGB5A3" }
        6 { "RGBA8" }
        8 { "CI4" }
        9 { "CI8" }
        10 { "CI14" }
        14 { "CMPR" }
        default { "fmt$Format" }
    }
}

function Get-WrapName {
    param([int]$Wrap)

    switch ($Wrap) {
        0 { "clamp" }
        1 { "repeat" }
        2 { "mirror" }
        default { "wrap$Wrap" }
    }
}

function Get-MagFilterName {
    param([int]$Filter)

    if ($Filter -eq 1) { "linear" } else { "nearest" }
}

function Get-MinFilterName {
    param([int]$Filter)

    switch ($Filter) {
        0 { "nearest" }
        1 { "nearest-mipmap-nearest" }
        2 { "nearest-mipmap-linear" }
        3 { "reserved" }
        4 { "linear" }
        5 { "linear-mipmap-nearest" }
        6 { "linear-mipmap-linear" }
        default { "min$Filter" }
    }
}

function Get-TextureDecodedFields {
    param(
        [string]$Kind,
        [uint32]$Data
    )

    switch ($Kind) {
        "mode0" {
            $lodBiasRaw = [int](($Data -shr 9) -band 0xFF)
            if (($lodBiasRaw -band 0x80) -ne 0) {
                $lodBiasRaw -= 0x100
            }

            return [pscustomobject]@{
                decoded = "wrap={0}:{1};filter={2}:{3};lod_bias={4};lod_clamp={5}" -f (Get-WrapName ([int]($Data -band 3))), (Get-WrapName ([int](($Data -shr 2) -band 3))), (Get-MagFilterName ([int](($Data -shr 4) -band 1))), (Get-MinFilterName ([int](($Data -shr 5) -band 7))), ($lodBiasRaw / 32.0).ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture), ((($Data -shr 21) -band 1) -ne 0)
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
        "mode1" {
            return [pscustomobject]@{
                decoded = "min_lod={0};max_lod={1}" -f (($Data -band 0xFF) / 16.0).ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture), ((($Data -shr 8) -band 0xFF) / 16.0).ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
        "image0" {
            $width = [int]($Data -band 0x3FF) + 1
            $height = [int](($Data -shr 10) -band 0x3FF) + 1
            $format = [int](($Data -shr 20) -band 0xF)
            return [pscustomobject]@{
                decoded = "size={0}x{1};format={2}" -f $width, $height, (Get-TextureFormatName $format)
                source_address = ""
                width = $width
                height = $height
                format = Get-TextureFormatName $format
            }
        }
        "image1" {
            return [pscustomobject]@{
                decoded = "tmem_even=0x{0:X6}" -f ($Data -band 0x7FFF)
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
        "image2" {
            return [pscustomobject]@{
                decoded = "tmem_odd=0x{0:X6}" -f ($Data -band 0x7FFF)
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
        "image3" {
            $sourceAddress = ($Data -band 0x00FFFFFF) -shl 5
            return [pscustomobject]@{
                decoded = "source=0x{0:X8}" -f $sourceAddress
                source_address = "0x{0:X8}" -f $sourceAddress
                width = ""
                height = ""
                format = ""
            }
        }
        "tlut" {
            return [pscustomobject]@{
                decoded = "tlut_base=0x{0:X3};tlut_format={1}" -f ($Data -band 0x3FF), (($Data -shr 10) -band 3)
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
        default {
            return [pscustomobject]@{
                decoded = ""
                source_address = ""
                width = ""
                height = ""
                format = ""
            }
        }
    }
}

$traceCsvFullPath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $traceCsvFullPath)) {
    throw "GX FIFO trace CSV not found: $traceCsvFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutCsvPath)) {
    $OutCsvPath = [System.IO.Path]::ChangeExtension($traceCsvFullPath, ".texture-bindings.csv")
}

if ([string]::IsNullOrWhiteSpace($OutJsonPath)) {
    $OutJsonPath = [System.IO.Path]::ChangeExtension($traceCsvFullPath, ".texture-bindings.json")
}

$outCsvFullPath = Resolve-FullPath $OutCsvPath
$outJsonFullPath = Resolve-FullPath $OutJsonPath
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outCsvFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outJsonFullPath) | Out-Null

$traceRows = @(Import-Csv -LiteralPath $traceCsvFullPath)
$bytes = New-Object System.Collections.Generic.List[object]
foreach ($row in $traceRows) {
    $width = Convert-ToInt32Invariant $row.width
    $value = Convert-HexToUInt32 $row.value
    $offsetStart = Convert-HexToUInt32 $row.fifo_offset_start
    for ($shift = ($width - 1) * 8; $shift -ge 0; $shift -= 8) {
        $byteValue = [byte](($value -shr $shift) -band 0xFF)
        $byteOffset = [uint32]($offsetStart + (($width - 1) - ($shift / 8)))
        $bytes.Add([pscustomobject]@{
            offset = $byteOffset
            value = $byteValue
            row = $row
        }) | Out-Null
    }
}

$bytes = @($bytes | Sort-Object offset)
$decodedRows = New-Object System.Collections.Generic.List[object]
$drawsSeen = 0
$index = 0
while ($index -lt $bytes.Count) {
    $command = [byte]$bytes[$index].value
    $commandOffset = [uint32]$bytes[$index].offset

    if ($command -eq 0x61 -and $index + 4 -lt $bytes.Count) {
        $payloadContiguous = $true
        for ($payloadIndex = 1; $payloadIndex -le 4; $payloadIndex++) {
            if ([uint32]$bytes[$index + $payloadIndex].offset -ne [uint32]($commandOffset + $payloadIndex)) {
                $payloadContiguous = $false
                break
            }
        }

        if ($payloadContiguous) {
            $bpValue = [uint32]0
            for ($payloadIndex = 1; $payloadIndex -le 4; $payloadIndex++) {
                $bpValue = ($bpValue -shl 8) -bor [uint32]$bytes[$index + $payloadIndex].value
            }

            $register = [byte](($bpValue -shr 24) -band 0xFF)
            $data = [uint32]($bpValue -band 0x00FFFFFF)
            $info = Get-TextureRegisterInfo $register
            if ($null -ne $info) {
                $decoded = Get-TextureDecodedFields -Kind $info.kind -Data $data
                $meta = $bytes[$index + 1].row
                $decodedRows.Add([pscustomobject]@{
                    instruction = $meta.instruction
                    pc = $meta.pc
                    opcode = $meta.opcode
                    disassembly = $meta.disassembly
                    fifo_offset = ("+0x{0:X}" -f $commandOffset)
                    draw_count_before = $drawsSeen
                    register = ("0x{0:X2}" -f $register)
                    slot = $info.slot
                    kind = $info.kind
                    value = ("0x{0:X6}" -f $data)
                    source_address = $decoded.source_address
                    width = $decoded.width
                    height = $decoded.height
                    format = $decoded.format
                    decoded = $decoded.decoded
                }) | Out-Null
            }

            $index += 5
            continue
        }
    }

    if (($command -band 0x80) -ne 0 -and $index + 2 -lt $bytes.Count) {
        $vertexCount = ([uint32]$bytes[$index + 1].value -shl 8) -bor [uint32]$bytes[$index + 2].value
        $drawsSeen++
        $index += 3
        continue
    }

    $index++
}

$decodedRows | Export-Csv -LiteralPath $outCsvFullPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    generated_at = (Get-Date).ToString("o")
    trace_csv_path = $traceCsvFullPath
    rows = @($decodedRows.ToArray())
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outJsonFullPath -Encoding UTF8

[pscustomobject]@{
    csv = $outCsvFullPath
    json = $outJsonFullPath
    rows = $decodedRows.Count
}
