param(
    [Parameter(Mandatory = $true)]
    [string]$WriteSummaryCsvPath,
    [string]$TextureBindCsvPath = "",
    [string]$TevSummaryCsvPath = "",
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = "",
    [string]$TextureAddress = "0x0072C600"
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexOrDecimalToUInt32 {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-AddressToOffset {
    param([uint32]$Address)

    $value = [uint64]$Address
    if ($value -lt 25165824) {
        return [uint32]$value
    }

    if ($value -ge 2147483648 -and $value -lt 2172649472) {
        return [uint32]($value - 2147483648)
    }

    if ($value -ge 3221225472 -and $value -lt 3246391296) {
        return [uint32]($value - 3221225472)
    }

    return $null
}

function Test-RangeOverlaps {
    param(
        [uint32]$Start,
        [uint32]$End,
        [uint32]$TargetStart,
        [uint32]$TargetEnd
    )

    $startOffset = Convert-AddressToOffset $Start
    $endOffset = Convert-AddressToOffset $End
    $targetStartOffset = Convert-AddressToOffset $TargetStart
    $targetEndOffset = Convert-AddressToOffset $TargetEnd
    if ($null -ne $startOffset -and $null -ne $endOffset -and $null -ne $targetStartOffset -and $null -ne $targetEndOffset) {
        return $startOffset -lt $targetEndOffset -and $targetStartOffset -lt $endOffset
    }

    return $Start -lt $TargetEnd -and $TargetStart -lt $End
}

function Parse-AddressRange {
    param([string]$Text)

    if ($Text -notmatch '^(0x[0-9A-Fa-f]+)-(0x[0-9A-Fa-f]+)$') {
        throw "Invalid address range: $Text"
    }

    $start = Convert-HexOrDecimalToUInt32 $Matches[1]
    $endInclusive = Convert-HexOrDecimalToUInt32 $Matches[2]
    [pscustomobject]@{
        Start = $start
        EndExclusive = [uint32]($endInclusive + 1)
    }
}

function Get-TextureAddress {
    param([string]$Text)

    $value = Convert-HexOrDecimalToUInt32 $Text
    if ([uint64]$value -lt 25165824) {
        return [uint32](2147483648 + [uint64]$value)
    }

    return $value
}

function Get-FirstMatching {
    param(
        [object[]]$Rows,
        [scriptblock]$Predicate
    )

    foreach ($row in $Rows) {
        if (& $Predicate $row) {
            return $row
        }
    }

    return $null
}

$writeSummaryPath = Resolve-FullPath $WriteSummaryCsvPath
if (-not (Test-Path -LiteralPath $writeSummaryPath)) {
    throw "Write summary CSV not found: $writeSummaryPath"
}

$directory = Split-Path -Parent $writeSummaryPath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $directory "sonic-texture-write-provenance.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $directory "sonic-texture-write-provenance.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$targetStart = Get-TextureAddress $TextureAddress
$targetLength = 0x1000
$targetEnd = [uint32]($targetStart + $targetLength)

$writeRows = @(Import-Csv -LiteralPath $writeSummaryPath)
$bindRows = @()
if (-not [string]::IsNullOrWhiteSpace($TextureBindCsvPath)) {
    $bindPath = Resolve-FullPath $TextureBindCsvPath
    if (Test-Path -LiteralPath $bindPath) {
        $bindRows = @(Import-Csv -LiteralPath $bindPath)
    }
}

$tevRows = @()
if (-not [string]::IsNullOrWhiteSpace($TevSummaryCsvPath)) {
    $tevPath = Resolve-FullPath $TevSummaryCsvPath
    if (Test-Path -LiteralPath $tevPath) {
        $tevRows = @(Import-Csv -LiteralPath $tevPath)
    }
}

$matchingWrites = foreach ($row in $writeRows) {
    $range = Parse-AddressRange $row.address_range
    if (Test-RangeOverlaps $range.Start $range.EndExclusive $targetStart $targetEnd) {
        $row
    }
}

$contentWrites = @($matchingWrites | Where-Object {
    $_.kind -eq "store" -and [int]$_.count -gt 1 -and $_.pc -eq "0x8010C128"
} | Sort-Object { [int64]$_.instruction })

$lastContentWrite = $contentWrites | Select-Object -Last 1
$firstBind = Get-FirstMatching $bindRows { param($row) $row.source_address -eq ($TextureAddress.ToUpperInvariant()) -or $row.source_address -eq $TextureAddress }
$firstTev = Get-FirstMatching $tevRows { param($row) $row.texture_address -eq ($TextureAddress.ToUpperInvariant()) -or $row.texture_address -eq $TextureAddress }

$rows = foreach ($row in ($matchingWrites | Sort-Object { [int64]$_.instruction })) {
    $isContentCopy = $row.kind -eq "store" -and [int]$row.count -gt 1 -and $row.pc -eq "0x8010C128"
    [pscustomobject]@{
        texture_address = ("0x{0:X8}" -f $targetStart)
        instruction = [int64]$row.instruction
        pc = $row.pc
        disassembly = $row.disassembly
        kind = $row.kind
        count = [int]$row.count
        address_range = $row.address_range
        classification = if ($isContentCopy) { "content-copy" } elseif ($row.kind -eq "bulk") { "bulk-load-or-clear" } else { "metadata-or-link" }
        source_pointer = if ($isContentCopy) { $row.r6 } else { "" }
        destination_pointer = if ($isContentCopy) { $row.r30 } else { "" }
        copy_words_or_loop_count = if ($isContentCopy) { $row.r4 } else { "" }
        copy_bytes_hint = if ($isContentCopy) { $row.r5 } else { "" }
        r3 = $row.r3_source
        r4 = $row.r4
        r5 = $row.r5
        r6 = $row.r6
        r29 = $row.r29
        r30 = $row.r30
        first_value = $row.first_value
        last_value = $row.last_value
        final_range_preview = $row.final_range_preview
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$rows | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation -Encoding UTF8

[pscustomobject]@{
    texture_address = ("0x{0:X8}" -f $targetStart)
    write_summary_csv_path = $writeSummaryPath
    texture_bind_csv_path = if ($bindRows.Count -gt 0) { (Resolve-FullPath $TextureBindCsvPath) } else { "" }
    tev_summary_csv_path = if ($tevRows.Count -gt 0) { (Resolve-FullPath $TevSummaryCsvPath) } else { "" }
    matching_write_count = @($matchingWrites).Count
    content_copy_count = $contentWrites.Count
    last_content_copy = $lastContentWrite
    first_bind = $firstBind
    first_tev_summary = $firstTev
    rows = @($rows)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $OutputCsvPath
    json = $OutputJsonPath
    matching_writes = @($matchingWrites).Count
    content_copies = $contentWrites.Count
    last_content_instruction = if ($null -ne $lastContentWrite) { $lastContentWrite.instruction } else { "" }
    last_content_source = if ($null -ne $lastContentWrite) { $lastContentWrite.r6 } else { "" }
}
