param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [int]$FocusDrawStart = -1,
    [int]$FocusDrawEnd = -1
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Get-ObjectValue {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Default = ""
    )

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Test-CsvHasRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Import-Csv -LiteralPath $Path | Select-Object -First 1)
}

function Normalize-Hex {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "+0x{0:X}" -f ([int64]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X}" -f ([int64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return "0x{0:X}" -f ([int64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture))
}

function Convert-ToNullableInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int64]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-OptionalBool {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    if ([bool]$Value) {
        return "True"
    }

    return "False"
}

function Get-DrawCoverage {
    param(
        [object[]]$Rows,
        [object]$Start,
        [object]$End
    )

    $totalColor = 0L
    $totalBlack = 0L
    $maxAfter = 0L
    $maxAfterDraw = $null
    $firstColorDraw = $null
    $lastColorDraw = $null
    foreach ($row in $Rows) {
        $draw = Convert-ToNullableInt64 (Get-ObjectValue $row "draw_index")
        if ($null -eq $draw -or $null -eq $Start -or $null -eq $End -or $draw -lt $Start -or $draw -gt $End) {
            continue
        }

        $color = Convert-ToNullableInt64 (Get-ObjectValue $row "color_writes")
        $black = Convert-ToNullableInt64 (Get-ObjectValue $row "black_color_writes")
        $after = Convert-ToNullableInt64 (Get-ObjectValue $row "after_nonblack")
        if ($null -ne $color) {
            $totalColor += $color
            if ($color -gt 0) {
                if ($null -eq $firstColorDraw) {
                    $firstColorDraw = $draw
                }

                $lastColorDraw = $draw
            }
        }

        if ($null -ne $black) {
            $totalBlack += $black
        }

        if ($null -ne $after -and $after -gt $maxAfter) {
            $maxAfter = $after
            $maxAfterDraw = $draw
        }
    }

    return [pscustomobject][ordered]@{
        totalColorWrites = $totalColor
        totalBlackWrites = $totalBlack
        firstColorDraw = $firstColorDraw
        lastColorDraw = $lastColorDraw
        maxAfterNonblack = $maxAfter
        maxAfterNonblackDraw = $maxAfterDraw
    }
}

function Get-MaterialSummary {
    param(
        [object[]]$Rows,
        [object]$Start,
        [object]$End
    )

    return (@(
        $Rows | Where-Object {
            $drawStart = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_start" (Get-ObjectValue $_ "first_draw"))
            $drawEnd = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_end" (Get-ObjectValue $_ "last_draw"))
            $null -ne $drawStart -and $null -ne $drawEnd -and $null -ne $Start -and $null -ne $End -and $drawEnd -ge $Start -and $drawStart -le $End
        } | ForEach-Object {
            $texture = [string](Get-ObjectValue $_ "texture_address" (Get-ObjectValue $_ "texture"))
            if ([string]::IsNullOrWhiteSpace($texture)) {
                $texture = "unknown"
            }

            "{0}:{1}-{2}:cw{3}" -f $texture, (Get-ObjectValue $_ "draw_start" (Get-ObjectValue $_ "first_draw")), (Get-ObjectValue $_ "draw_end" (Get-ObjectValue $_ "last_draw")), (Get-ObjectValue $_ "color_writes")
        }
    ) -join ";")
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-copy-brackets"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packetTimelineCsvPath = Join-Path $runRoot "sonic-packet-timeline.csv"
$copyCsvPath = Join-Path $runRoot "gx-copies.csv"
$coverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
$materialsCsvPath = Join-Path $runRoot "gx-materials.summary.csv"

if (-not (Test-CsvHasRows $copyCsvPath)) {
    throw "Required CSV missing or empty: $copyCsvPath"
}

if (-not (Test-CsvHasRows $packetTimelineCsvPath) -and ($FocusDrawStart -lt 0 -or $FocusDrawEnd -lt $FocusDrawStart)) {
    throw "Required CSV missing or empty: $packetTimelineCsvPath. Supply -FocusDrawStart and -FocusDrawEnd to use an explicit focus range."
}

$focusPacket = Normalize-Hex $FocusPacket
$packetRows = if (Test-CsvHasRows $packetTimelineCsvPath) {
    @(
        Import-Csv -LiteralPath $packetTimelineCsvPath |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket -and -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "mapped_draw_start")) } |
            Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") } }
    )
} else {
    @()
}
if ($packetRows.Count -eq 0 -and $FocusDrawStart -ge 0 -and $FocusDrawEnd -ge $FocusDrawStart) {
    $packetRows = @(
        [pscustomobject][ordered]@{
            instruction = ""
            packet = $focusPacket
            state_hash = ""
            small_data_hash = ""
            mapped_draw_start = $FocusDrawStart
            mapped_draw_end = $FocusDrawEnd
            mapped_draw_count = $FocusDrawEnd - $FocusDrawStart + 1
        }
    )
}
if ($packetRows.Count -eq 0) {
    throw "No mapped packet rows found for $focusPacket in $packetTimelineCsvPath"
}

$copyRows = @(
    Import-Csv -LiteralPath $copyCsvPath |
        Where-Object { [string](Get-ObjectValue $_ "kind") -eq "display" } |
        Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen") } }, @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "copy_index") } }
)
if ($copyRows.Count -eq 0) {
    throw "No display copy rows found in $copyCsvPath"
}

$coverageRows = if (Test-CsvHasRows $coverageCsvPath) { @(Import-Csv -LiteralPath $coverageCsvPath) } else { @() }
$materialRows = if (Test-CsvHasRows $materialsCsvPath) { @(Import-Csv -LiteralPath $materialsCsvPath) } else { @() }

$bracketRows = New-Object System.Collections.Generic.List[object]
$previousCopyDraw = $null
$previousCopyIndex = $null
foreach ($copy in $copyRows) {
    $copyDraw = Convert-ToNullableInt64 (Get-ObjectValue $copy "draws_seen")
    $copyIndex = Convert-ToNullableInt64 (Get-ObjectValue $copy "copy_index")
    $intervalStart = if ($null -eq $previousCopyDraw) { $null } else { $previousCopyDraw + 1 }
    $intervalEnd = $copyDraw
    $packetsInInterval = @(
        $packetRows | Where-Object {
            $start = Convert-ToNullableInt64 (Get-ObjectValue $_ "mapped_draw_start")
            $end = Convert-ToNullableInt64 (Get-ObjectValue $_ "mapped_draw_end")
            $null -ne $start -and $null -ne $end -and $null -ne $intervalEnd -and $end -le $intervalEnd -and ($null -eq $previousCopyDraw -or $start -gt $previousCopyDraw)
        }
    )

    $overlapsInterval = @(
        $packetRows | Where-Object {
            $start = Convert-ToNullableInt64 (Get-ObjectValue $_ "mapped_draw_start")
            $end = Convert-ToNullableInt64 (Get-ObjectValue $_ "mapped_draw_end")
            $null -ne $start -and $null -ne $end -and $null -ne $intervalEnd -and $start -le $intervalEnd -and ($null -eq $previousCopyDraw -or $end -gt $previousCopyDraw)
        }
    )

    $coverage = if ($null -ne $intervalStart -and $null -ne $intervalEnd) { Get-DrawCoverage $coverageRows $intervalStart $intervalEnd } else { $null }
    $materials = if ($null -ne $intervalStart -and $null -ne $intervalEnd) { Get-MaterialSummary $materialRows $intervalStart $intervalEnd } else { "" }
    $bracketRows.Add([pscustomobject][ordered]@{
        copy_index = $copyIndex
        copy_draws_seen = $copyDraw
        previous_copy_index = $previousCopyIndex
        previous_copy_draws_seen = $previousCopyDraw
        interval_start_draw = $intervalStart
        interval_end_draw = $intervalEnd
        interval_draw_count = if ($null -ne $intervalStart -and $null -ne $intervalEnd) { $intervalEnd - $intervalStart + 1 } else { "" }
        focus_packet_included = Format-OptionalBool ($packetsInInterval.Count -gt 0)
        focus_packet_overlaps = Format-OptionalBool ($overlapsInterval.Count -gt 0)
        focus_packet_event_count = $packetsInInterval.Count
        overlapping_focus_packet_event_count = $overlapsInterval.Count
        focus_packet_ranges = (@($packetsInInterval | ForEach-Object { "{0}-{1}@{2}" -f (Get-ObjectValue $_ "mapped_draw_start"), (Get-ObjectValue $_ "mapped_draw_end"), (Get-ObjectValue $_ "instruction") }) -join ";")
        overlapping_focus_packet_ranges = (@($overlapsInterval | ForEach-Object { "{0}-{1}@{2}" -f (Get-ObjectValue $_ "mapped_draw_start"), (Get-ObjectValue $_ "mapped_draw_end"), (Get-ObjectValue $_ "instruction") }) -join ";")
        copy_display_address = Get-ObjectValue $copy "display_address"
        copy_display_nonblack = Get-ObjectValue $copy "display_nonblack"
        copy_display_nonblack_percent = Get-ObjectValue $copy "display_nonblack_percent"
        copy_display_nonblack_bounds = Get-ObjectValue $copy "display_nonblack_bounds"
        copy_before_nonblack = Get-ObjectValue $copy "before_nonblack"
        copy_before_nonblack_percent = Get-ObjectValue $copy "before_nonblack_percent"
        copy_before_nonblack_bounds = Get-ObjectValue $copy "before_nonblack_bounds"
        interval_total_color_writes = if ($null -ne $coverage) { $coverage.totalColorWrites } else { "" }
        interval_total_black_writes = if ($null -ne $coverage) { $coverage.totalBlackWrites } else { "" }
        interval_first_color_draw = if ($null -ne $coverage) { $coverage.firstColorDraw } else { "" }
        interval_last_color_draw = if ($null -ne $coverage) { $coverage.lastColorDraw } else { "" }
        interval_max_after_nonblack = if ($null -ne $coverage) { $coverage.maxAfterNonblack } else { "" }
        interval_max_after_nonblack_draw = if ($null -ne $coverage) { $coverage.maxAfterNonblackDraw } else { "" }
        interval_material_summary = $materials
    })

    $previousCopyDraw = $copyDraw
    $previousCopyIndex = $copyIndex
}

$packetBracketRows = New-Object System.Collections.Generic.List[object]
foreach ($packet in $packetRows) {
    $packetStart = Convert-ToNullableInt64 (Get-ObjectValue $packet "mapped_draw_start")
    $packetEnd = Convert-ToNullableInt64 (Get-ObjectValue $packet "mapped_draw_end")
    $copyAfter = $copyRows |
        Where-Object {
            $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen")
            $null -ne $draw -and $null -ne $packetEnd -and $draw -ge $packetEnd
        } |
        Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen") } }, @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "copy_index") } } |
        Select-Object -First 1
    $copyBefore = $copyRows |
        Where-Object {
            $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen")
            $null -ne $draw -and $null -ne $packetStart -and $draw -lt $packetStart
        } |
        Sort-Object @{ Expression = { -(Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen")) } }, @{ Expression = { -(Convert-ToNullableInt64 (Get-ObjectValue $_ "copy_index")) } } |
        Select-Object -First 1
    $copyAfterDraw = Convert-ToNullableInt64 (Get-ObjectValue $copyAfter "draws_seen")
    $copyBeforeDraw = Convert-ToNullableInt64 (Get-ObjectValue $copyBefore "draws_seen")
    $includedByAfter = if ($null -ne $copyAfterDraw -and $null -ne $packetStart) {
        $null -eq $copyBeforeDraw -or $packetStart -gt $copyBeforeDraw
    } else {
        $false
    }

    $packetBracketRows.Add([pscustomobject][ordered]@{
        packet = $focusPacket
        instruction = Get-ObjectValue $packet "instruction"
        state_hash = Get-ObjectValue $packet "state_hash"
        small_data_hash = Get-ObjectValue $packet "small_data_hash"
        mapped_draw_start = $packetStart
        mapped_draw_end = $packetEnd
        previous_display_copy_index = Get-ObjectValue $copyBefore "copy_index"
        previous_display_copy_draws_seen = $copyBeforeDraw
        next_display_copy_index = Get-ObjectValue $copyAfter "copy_index"
        next_display_copy_draws_seen = $copyAfterDraw
        next_copy_delta_from_packet_end = if ($null -ne $copyAfterDraw -and $null -ne $packetEnd) { $copyAfterDraw - $packetEnd } else { "" }
        next_copy_includes_packet = Format-OptionalBool $includedByAfter
        previous_copy_display_nonblack = Get-ObjectValue $copyBefore "display_nonblack"
        next_copy_display_nonblack = Get-ObjectValue $copyAfter "display_nonblack"
        next_copy_display_nonblack_bounds = Get-ObjectValue $copyAfter "display_nonblack_bounds"
    })
}

$bracketsCsvPath = Join-Path $OutputDirectory "copy-brackets.csv"
$packetCsvPath = Join-Path $OutputDirectory "focus-packet-copy-brackets.csv"
$summaryCsvPath = Join-Path $OutputDirectory "copy-bracket-summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "copy-bracket-report.json"

$bracketRows | Export-Csv -LiteralPath $bracketsCsvPath -NoTypeInformation
$packetBracketRows | Export-Csv -LiteralPath $packetCsvPath -NoTypeInformation

$includedCopyRows = @($bracketRows | Where-Object { $_.focus_packet_included -eq "True" })
$packetRowsWithNextCopy = @($packetBracketRows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.next_display_copy_index) })
$summary = [pscustomobject][ordered]@{
    focus_packet = $focusPacket
    display_copies = $copyRows.Count
    focus_packet_events = $packetRows.Count
    copies_including_focus_packet = $includedCopyRows.Count
    focus_events_with_later_display_copy = $packetRowsWithNextCopy.Count
    first_copy_including_focus_packet = if ($includedCopyRows.Count -gt 0) { $includedCopyRows[0].copy_index } else { "" }
    first_copy_including_focus_draws_seen = if ($includedCopyRows.Count -gt 0) { $includedCopyRows[0].copy_draws_seen } else { "" }
    first_focus_next_copy = if ($packetRowsWithNextCopy.Count -gt 0) { $packetRowsWithNextCopy[0].next_display_copy_index } else { "" }
    first_focus_next_copy_draws_seen = if ($packetRowsWithNextCopy.Count -gt 0) { $packetRowsWithNextCopy[0].next_display_copy_draws_seen } else { "" }
    first_focus_next_copy_delta = if ($packetRowsWithNextCopy.Count -gt 0) { $packetRowsWithNextCopy[0].next_copy_delta_from_packet_end } else { "" }
    brackets_csv = $bracketsCsvPath
    focus_packet_csv = $packetCsvPath
}

$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation
[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-copy-bracket.v1"
    runDirectory = $runRoot
    focusPacket = $focusPacket
    generatedAt = (Get-Date).ToString("o")
    inputs = [ordered]@{
        packetTimelineCsv = $packetTimelineCsvPath
        explicitFocusDrawStart = if ($FocusDrawStart -ge 0) { $FocusDrawStart } else { $null }
        explicitFocusDrawEnd = if ($FocusDrawEnd -ge 0) { $FocusDrawEnd } else { $null }
        gxCopiesCsv = $copyCsvPath
        gxCoverageCsv = if (Test-Path -LiteralPath $coverageCsvPath) { $coverageCsvPath } else { $null }
        materialsCsv = if (Test-Path -LiteralPath $materialsCsvPath) { $materialsCsvPath } else { $null }
    }
    outputs = [ordered]@{
        bracketsCsv = $bracketsCsvPath
        focusPacketCsv = $packetCsvPath
        summaryCsv = $summaryCsvPath
    }
    summary = $summary
    copyBrackets = $bracketRows
    focusPacketBrackets = $packetBracketRows
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic copy brackets: $bracketsCsvPath"
Write-Host "Wrote Sonic focus packet copy brackets: $packetCsvPath"
Write-Host "Wrote Sonic copy bracket summary: $summaryCsvPath"
