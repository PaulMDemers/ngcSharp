param(
    [Parameter(Mandatory = $true)]
    [string]$TransformCsvPath,
    [uint32]$TargetAddress = 0,
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

$transformFullPath = Resolve-FullPath $TransformCsvPath
if (-not (Test-Path -LiteralPath $transformFullPath)) {
    throw "Sonic transform CSV not found: $transformFullPath"
}

$rows = @(Import-Csv -LiteralPath $transformFullPath)
if ($rows.Count -eq 0) {
    throw "Sonic transform CSV has no rows: $transformFullPath"
}

$records = foreach ($row in $rows) {
    $outputCursor = Convert-HexUInt32 $row.output_cursor
    $inputCursor = Convert-HexUInt32 $row.input_cursor
    $targetOffset = if ($TargetAddress -ne 0 -and $TargetAddress -ge $outputCursor) {
        [int]($TargetAddress - $outputCursor)
    } else {
        -1
    }

    $inputBytes = Convert-HexStringToBytes $row.input_bytes
    $outputBytes = Convert-HexStringToBytes $row.output_bytes

    [pscustomobject][ordered]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        output_cursor = $row.output_cursor
        input_cursor = $row.input_cursor
        iterations = $row.iterations
        gqr1 = $row.gqr1
        target_offset = $targetOffset
        input_color = Format-Hex32 (Read-BeUInt32 $inputBytes 0)
        input_x_bits = Format-Hex32 (Read-BeUInt32 $inputBytes 4)
        input_y_bits = Format-Hex32 (Read-BeUInt32 $inputBytes 8)
        input_z_bits = Format-Hex32 (Read-BeUInt32 $inputBytes 12)
        output_word0 = Format-Hex32 (Read-BeUInt32 $outputBytes 0)
        output_word1 = Format-Hex32 (Read-BeUInt32 $outputBytes 4)
        output_word2 = Format-Hex32 (Read-BeUInt32 $outputBytes 8)
        output_word3 = Format-Hex32 (Read-BeUInt32 $outputBytes 12)
        output_word4 = Format-Hex32 (Read-BeUInt32 $outputBytes 16)
        output_word5 = Format-Hex32 (Read-BeUInt32 $outputBytes 20)
        output_word6 = Format-Hex32 (Read-BeUInt32 $outputBytes 24)
        output_word7 = Format-Hex32 (Read-BeUInt32 $outputBytes 28)
        f0 = $row.f0
        f1 = $row.f1
        f2 = $row.f2
        f3 = $row.f3
        f4 = $row.f4
        f5 = $row.f5
        f6 = $row.f6
        f7 = $row.f7
        f8 = $row.f8
        f9 = $row.f9
        f10 = $row.f10
        f11 = $row.f11
        f12 = $row.f12
        f13 = $row.f13
    }
}

if (-not [string]::IsNullOrWhiteSpace($RecordCsvPath)) {
    $recordFullPath = Resolve-FullPath $RecordCsvPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $recordFullPath) | Out-Null
    $records | Export-Csv -LiteralPath $recordFullPath -NoTypeInformation
}

$summary = [pscustomobject][ordered]@{
    transformCsvPath = $transformFullPath
    rowCount = $rows.Count
    targetAddress = if ($TargetAddress -eq 0) { $null } else { Format-Hex32 $TargetAddress }
    pcs = @($records | Group-Object pc | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    outputCursors = @($records | Group-Object output_cursor | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    inputCursors = @($records | Select-Object -First $Top input_cursor, input_color, input_x_bits, input_y_bits, input_z_bits)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 8
}
