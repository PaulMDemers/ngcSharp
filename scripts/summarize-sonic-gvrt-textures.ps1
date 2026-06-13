param(
    [Parameter(Mandatory = $true)]
    [string]$DecodedPath,
    [string]$BaseAddress = "0x8125FE60",
    [string]$OutputDirectory = "",
    [string]$FocusPayloadAddress = "0x8137DFA0",
    [int]$FocusRadiusBytes = 0x4000
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-UInt32 {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
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
        10 { "CI14X2" }
        14 { "CMPR" }
        default { "format$Format" }
    }
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
$base = Parse-UInt32 $BaseAddress
$focusPayload = Parse-UInt32 $FocusPayloadAddress
$magic = [byte[]][char[]]"GVRT"
$rows = New-Object System.Collections.Generic.List[object]

for ($offset = 0; $offset -le $bytes.Length - 0x10; $offset += 4) {
    if ($bytes[$offset] -ne $magic[0] -or $bytes[$offset + 1] -ne $magic[1] -or $bytes[$offset + 2] -ne $magic[2] -or $bytes[$offset + 3] -ne $magic[3]) {
        continue
    }

    $word4 = Read-BeUInt32 $bytes ($offset + 0x04)
    $word8 = Read-BeUInt32 $bytes ($offset + 0x08)
    $word12 = Read-BeUInt32 $bytes ($offset + 0x0C)
    $headerAddress = [uint32]([uint64]$base + [uint64]$offset)
    $payloadAddress = [uint32]([uint64]$headerAddress + 0x10)
    $copyCursorAddress = [uint32]([uint64]$headerAddress + 0x0C)
    $dataBytesHint = [int](($word4 -shr 8) -band 0xFFFF)
    $formatCode = [int]($word8 -band 0xFF)
    $formatFlags = [int](($word8 -shr 8) -band 0xFFFFFF)
    $width = [int](($word12 -shr 16) -band 0xFFFF)
    $height = [int]($word12 -band 0xFFFF)
    $delta = [int64]$payloadAddress - [int64]$focusPayload

    $rows.Add([pscustomobject]@{
        header_address = Format-Hex32 $headerAddress
        decoded_offset = Format-HexOffset $offset
        payload_address = Format-Hex32 $payloadAddress
        copy_cursor_address = Format-Hex32 $copyCursorAddress
        focus_payload_delta = Format-HexOffset $delta
        within_focus_radius = [Math]::Abs($delta) -le $FocusRadiusBytes
        word4 = Format-Hex32 $word4
        word8 = Format-Hex32 $word8
        word12 = Format-Hex32 $word12
        data_bytes_hint = $dataBytesHint
        data_bytes_hint_hex = "0x{0:X}" -f $dataBytesHint
        format_code = $formatCode
        format_name = Get-TextureFormatName $formatCode
        format_flags = "0x{0:X}" -f $formatFlags
        width = $width
        height = $height
    }) | Out-Null
}

$csvPath = Join-Path $OutputDirectory "sonic-gvrt-textures.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-gvrt-textures.json"
$focusCsvPath = Join-Path $OutputDirectory "sonic-gvrt-textures.focus.csv"

$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$focusRows = @($rows | Where-Object { $_.within_focus_radius -eq $true } | Sort-Object decoded_offset)
$focusRows | Export-Csv -LiteralPath $focusCsvPath -NoTypeInformation -Encoding UTF8

[pscustomobject]@{
    decoded_path = $decodedFullPath
    base_address = Format-Hex32 $base
    decoded_length = "0x{0:X}" -f $bytes.Length
    focus_payload_address = Format-Hex32 $focusPayload
    focus_radius_bytes = $FocusRadiusBytes
    texture_count = $rows.Count
    focus_texture_count = $focusRows.Count
    csv = $csvPath
    focus_csv = $focusCsvPath
    rows = @($rows.ToArray())
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    focus_csv = $focusCsvPath
    json = $jsonPath
    texture_count = $rows.Count
    focus_texture_count = $focusRows.Count
}
