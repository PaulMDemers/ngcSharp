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

function Get-RangePreview {
    param(
        [string]$Hex,
        [int]$MaxBytes = 32
    )

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return ""
    }

    $chars = [Math]::Min($Hex.Length, $MaxBytes * 2)
    return $Hex.Substring(0, $chars)
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Input write trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-input-writes.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-input-writes.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Input write trace CSV has no rows: $tracePath"
}

$groups = [ordered]@{}
foreach ($row in $rows) {
    $key = "$($row.instruction)|$($row.pc)|$($row.disassembly)|$($row.kind)|$($row.r3)|$($row.r4)|$($row.r29)|$($row.r30)"
    if (-not $groups.Contains($key)) {
        $groups[$key] = @{
            instruction = [int64]$row.instruction
            pc = $row.pc
            disassembly = $row.disassembly
            kind = $row.kind
            count = 0
            min_address = Parse-HexOrDecimal $row.address
            max_address = Parse-HexOrDecimal $row.address
            first_value = $row.value
            last_value = $row.value
            r3 = $row.r3
            r4 = $row.r4
            r5 = $row.r5
            r6 = $row.r6
            r29 = $row.r29
            r30 = $row.r30
            first_range_bytes = $row.range_bytes
            last_range_bytes = $row.range_bytes
        }
    }

    $entry = $groups[$key]
    $entry.count = [int]$entry.count + 1
    $address = Parse-HexOrDecimal $row.address
    $width = [Math]::Max(1, [int]$row.width)
    if ($address -lt [uint32]$entry.min_address) {
        $entry.min_address = $address
        $entry.first_value = $row.value
        $entry.first_range_bytes = $row.range_bytes
    }

    $end = $address + [uint32]($width - 1)
    if ($end -ge [uint32]$entry.max_address) {
        $entry.max_address = $end
        $entry.last_value = $row.value
        $entry.last_range_bytes = $row.range_bytes
    }
}

$summary = foreach ($entry in $groups.Values) {
    [pscustomobject]@{
        instruction = $entry.instruction
        pc = $entry.pc
        disassembly = $entry.disassembly
        kind = $entry.kind
        count = $entry.count
        address_range = ("0x{0:X8}-0x{1:X8}" -f [uint32]$entry.min_address, [uint32]$entry.max_address)
        r3_source = $entry.r3
        r4 = $entry.r4
        r5 = $entry.r5
        r6 = $entry.r6
        r29 = $entry.r29
        r30 = $entry.r30
        first_value = $entry.first_value
        last_value = $entry.last_value
        final_range_preview = Get-RangePreview $entry.last_range_bytes 48
        final_range_bytes = $entry.last_range_bytes
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic input write summary: $SummaryCsvPath"
$summary | Select-Object instruction,pc,kind,count,address_range,r3_source,r4,r29,r30,final_range_preview | Format-Table -AutoSize
