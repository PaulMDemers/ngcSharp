param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$SummaryJsonPath = ""
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

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-HexBytesToWords {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return @()
    }

    $clean = $Hex.Trim()
    $wordCount = [Math]::Floor($clean.Length / 8)
    $words = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $wordCount; $i++) {
        $words.Add("0x$($clean.Substring($i * 8, 8))")
    }

    return $words.ToArray()
}

function Convert-WordHexToSingleText {
    param([string]$WordHex)

    if ([string]::IsNullOrWhiteSpace($WordHex)) {
        return ""
    }

    $word = [uint32]::Parse($WordHex.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    $bytes = [BitConverter]::GetBytes($word)
    $value = [BitConverter]::ToSingle($bytes, 0)
    return $value.ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Locked-cache trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "locked-cache-writes.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "locked-cache-writes.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Locked-cache trace CSV has no rows: $tracePath"
}

$byPc = [ordered]@{}
foreach ($row in $rows) {
    $key = "$($row.pc)|$($row.disassembly)"
    if (-not $byPc.Contains($key)) {
        $byPc[$key] = @{
            pc = $row.pc
            opcode = $row.opcode
            disassembly = $row.disassembly
            count = 0
            first_instruction = [int64]$row.instruction
            last_instruction = [int64]$row.instruction
            min_address = Parse-HexOrDecimal $row.address
            max_address = Parse-HexOrDecimal $row.address
            first_value = $row.value
            last_value = $row.value
            first_range_bytes = $row.range_bytes
            last_range_bytes = $row.range_bytes
            first_post_write_bytes = $row.post_write_bytes
            last_post_write_bytes = $row.post_write_bytes
            r3_values = [ordered]@{}
            r4_values = [ordered]@{}
            r5_values = [ordered]@{}
            r6_values = [ordered]@{}
        }
    }

    $entry = $byPc[$key]
    $entry.count = [int]$entry.count + 1
    $instruction = [int64]$row.instruction
    $address = Parse-HexOrDecimal $row.address
    if ($instruction -lt [int64]$entry.first_instruction) {
        $entry.first_instruction = $instruction
        $entry.first_value = $row.value
        $entry.first_range_bytes = $row.range_bytes
        $entry.first_post_write_bytes = $row.post_write_bytes
    }

    if ($instruction -ge [int64]$entry.last_instruction) {
        $entry.last_instruction = $instruction
        $entry.last_value = $row.value
        $entry.last_range_bytes = $row.range_bytes
        $entry.last_post_write_bytes = $row.post_write_bytes
    }

    if ($address -lt [uint32]$entry.min_address) {
        $entry.min_address = $address
    }

    $writeEnd = $address + [uint32]([int]$row.width - 1)
    if ($writeEnd -gt [uint32]$entry.max_address) {
        $entry.max_address = $writeEnd
    }

    foreach ($registerName in @("r3", "r4", "r5", "r6")) {
        $values = $entry["${registerName}_values"]
        $value = $row.$registerName
        if (-not $values.Contains($value)) {
            $values[$value] = 0
        }

        $values[$value] = [int]$values[$value] + 1
    }
}

$summary = foreach ($entry in $byPc.Values) {
    $finalWords = Convert-HexBytesToWords $entry.last_range_bytes
    $finalSingles = @($finalWords | ForEach-Object { Convert-WordHexToSingleText $_ })
    [pscustomobject]@{
        pc = $entry.pc
        opcode = $entry.opcode
        disassembly = $entry.disassembly
        count = $entry.count
        first_instruction = $entry.first_instruction
        last_instruction = $entry.last_instruction
        address_range = ("0x{0:X8}-0x{1:X8}" -f [uint32]$entry.min_address, [uint32]$entry.max_address)
        first_value = $entry.first_value
        last_value = $entry.last_value
        r3_values = (($entry.r3_values.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 4 | ForEach-Object { "$($_.Key):$($_.Value)" }) -join ";")
        r4_values = (($entry.r4_values.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 4 | ForEach-Object { "$($_.Key):$($_.Value)" }) -join ";")
        r5_values = (($entry.r5_values.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 4 | ForEach-Object { "$($_.Key):$($_.Value)" }) -join ";")
        r6_values = (($entry.r6_values.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 4 | ForEach-Object { "$($_.Key):$($_.Value)" }) -join ";")
        final_words = ($finalWords -join " ")
        final_singles = ($finalSingles -join " ")
        final_range_bytes = $entry.last_range_bytes
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Locked-cache write summary: $SummaryCsvPath"
$summary | Select-Object pc,disassembly,count,address_range,first_instruction,last_instruction,r4_values,r5_values,r6_values | Format-Table -AutoSize
