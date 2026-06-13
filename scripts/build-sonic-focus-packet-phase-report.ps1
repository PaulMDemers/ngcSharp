param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0"
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

function Convert-ToNullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Double {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
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

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-DrawRangeRelation {
    param(
        [object]$Draw,
        [object]$Start,
        [object]$End
    )

    if ($null -eq $Draw -or $null -eq $Start -or $null -eq $End) {
        return ""
    }

    if ($Draw -lt $Start) {
        return "before-packet"
    }

    if ($Draw -gt $End) {
        return "after-packet"
    }

    return "inside-packet"
}

function Get-NearestDisplayCopy {
    param(
        [object[]]$DisplayCopies,
        [object]$Start,
        [object]$End
    )

    if ($DisplayCopies.Count -eq 0 -or $null -eq $Start -or $null -eq $End) {
        return $null
    }

    $center = ([double]$Start + [double]$End) / 2.0
    return $DisplayCopies |
        Sort-Object @{ Expression = {
            $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen")
            if ($null -eq $draw) {
                [double]::PositiveInfinity
            } elseif ($draw -ge $Start -and $draw -le $End) {
                0.0
            } else {
                [Math]::Min([Math]::Abs([double]$draw - [double]$Start), [Math]::Abs([double]$draw - [double]$End))
            }
        } }, @{ Expression = {
            $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen")
            if ($null -eq $draw) { [double]::PositiveInfinity } else { [Math]::Abs([double]$draw - $center) }
        } } |
        Select-Object -First 1
}

function Get-NearestActivityRun {
    param(
        [object[]]$Rows,
        [object]$Start,
        [object]$End
    )

    if ($Rows.Count -eq 0 -or $null -eq $Start -or $null -eq $End) {
        return $null
    }

    $center = ([double]$Start + [double]$End) / 2.0
    return $Rows |
        Sort-Object @{ Expression = {
            $first = Convert-ToNullableInt64 (Get-ObjectValue $_ "firstDrawsSeen")
            $last = Convert-ToNullableInt64 (Get-ObjectValue $_ "lastDrawsSeen")
            if ($null -eq $first -or $null -eq $last) {
                [double]::PositiveInfinity
            } elseif ($last -ge $Start -and $first -le $End) {
                0.0
            } else {
                [Math]::Min([Math]::Abs([double]$last - [double]$Start), [Math]::Abs([double]$first - [double]$End))
            }
        } }, @{ Expression = {
            $first = Convert-ToNullableInt64 (Get-ObjectValue $_ "firstDrawsSeen")
            $last = Convert-ToNullableInt64 (Get-ObjectValue $_ "lastDrawsSeen")
            if ($null -eq $first -or $null -eq $last) {
                [double]::PositiveInfinity
            } else {
                [Math]::Abs((([double]$first + [double]$last) / 2.0) - $center)
            }
        } } |
        Select-Object -First 1
}

function Get-CoverageSummary {
    param(
        [object[]]$Rows,
        [object]$Start,
        [object]$End
    )

    $inRange = @($Rows | Where-Object {
        $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_index")
        $null -ne $draw -and $null -ne $Start -and $null -ne $End -and $draw -ge $Start -and $draw -le $End
    })

    $totalColor = 0L
    $totalBlack = 0L
    $maxAfterNonblack = 0L
    $maxAfterNonblackDraw = $null
    $firstColorDraw = $null
    $lastColorDraw = $null
    foreach ($row in $inRange) {
        $draw = Convert-ToNullableInt64 (Get-ObjectValue $row "draw_index")
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

        if ($null -ne $after -and $after -gt $maxAfterNonblack) {
            $maxAfterNonblack = $after
            $maxAfterNonblackDraw = $draw
        }
    }

    return [pscustomobject][ordered]@{
        drawCount = $inRange.Count
        firstColorDraw = $firstColorDraw
        lastColorDraw = $lastColorDraw
        totalColorWrites = $totalColor
        totalBlackWrites = $totalBlack
        blackWriteRatio = if ($totalColor -gt 0) { [double]$totalBlack / [double]$totalColor } else { $null }
        maxAfterNonblack = $maxAfterNonblack
        maxAfterNonblackDraw = $maxAfterNonblackDraw
    }
}

function Get-MaterialSummary {
    param(
        [object[]]$Rows,
        [object]$Start,
        [object]$End
    )

    $materials = @(
        $Rows | Where-Object {
            $drawStart = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_start" (Get-ObjectValue $_ "first_draw"))
            $drawEnd = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_end" (Get-ObjectValue $_ "last_draw"))
            $null -ne $drawStart -and $null -ne $drawEnd -and $null -ne $Start -and $null -ne $End -and $drawEnd -ge $Start -and $drawStart -le $End
        } | ForEach-Object {
            $texture = [string](Get-ObjectValue $_ "texture_address" (Get-ObjectValue $_ "texture"))
            $drawStart = Get-ObjectValue $_ "draw_start" (Get-ObjectValue $_ "first_draw")
            $drawEnd = Get-ObjectValue $_ "draw_end" (Get-ObjectValue $_ "last_draw")
            $colorWrites = Get-ObjectValue $_ "color_writes"
            $blackRatio = Get-ObjectValue $_ "black_write_ratio"
            if ([string]::IsNullOrWhiteSpace($texture)) {
                $texture = "unknown"
            }

            if ([string]::IsNullOrWhiteSpace([string]$blackRatio)) {
                "{0}:{1}-{2}:cw{3}" -f $texture, $drawStart, $drawEnd, $colorWrites
            } else {
                "{0}:{1}-{2}:cw{3}:black{4}" -f $texture, $drawStart, $drawEnd, $colorWrites, $blackRatio
            }
        }
    )

    return ($materials -join ";")
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-focus-packet-phase"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$placementDirectory = Join-Path $runRoot "sonic-packet-placement-comparison"
$focusEventsCsvPath = Join-Path $placementDirectory "focus-packet-events.csv"
$packetTimelineCsvPath = Join-Path $runRoot "sonic-packet-timeline.csv"
$gxCopiesCsvPath = Join-Path $runRoot "gx-copies.csv"
$displayActivityCsvPath = Join-Path $runRoot "gx-display-activity.csv"
$coverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
$materialsCsvPath = Join-Path $runRoot "gx-materials.summary.csv"
$runJsonPath = Join-Path $runRoot "run.json"
$emulatorSummaryJsonPath = Join-Path $runRoot "emulator-summary.json"

$focusPacket = Normalize-Hex $FocusPacket
if (Test-CsvHasRows $focusEventsCsvPath) {
    $focusRows = @(Import-Csv -LiteralPath $focusEventsCsvPath)
} elseif (Test-CsvHasRows $packetTimelineCsvPath) {
    $focusRows = @(
        Import-Csv -LiteralPath $packetTimelineCsvPath |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
            Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") } }
    )
} else {
    throw "No focus packet event source found. Expected $focusEventsCsvPath or $packetTimelineCsvPath"
}

if ($focusRows.Count -eq 0) {
    throw "No events found for focus packet $focusPacket"
}

$copyRows = if (Test-CsvHasRows $gxCopiesCsvPath) { @(Import-Csv -LiteralPath $gxCopiesCsvPath) } else { @() }
$displayCopies = @($copyRows | Where-Object { [string](Get-ObjectValue $_ "kind") -eq "display" })
$activityRows = if (Test-CsvHasRows $displayActivityCsvPath) { @(Import-Csv -LiteralPath $displayActivityCsvPath) } else { @() }
$coverageRows = if (Test-CsvHasRows $coverageCsvPath) { @(Import-Csv -LiteralPath $coverageCsvPath) } else { @() }
$materialRows = if (Test-CsvHasRows $materialsCsvPath) { @(Import-Csv -LiteralPath $materialsCsvPath) } else { @() }
$runJson = Read-JsonFile $runJsonPath
$emulatorSummary = Read-JsonFile $emulatorSummaryJsonPath

$selectedCopyIndex = Convert-ToNullableInt64 (Get-ObjectValue $runJson "gxFrameSourceCopyIndex")
$selectedCopyDrawsSeen = Convert-ToNullableInt64 (Get-ObjectValue $runJson "gxFrameLastDisplayDrawsSeen")
$selectedSource = [string](Get-ObjectValue $runJson "gxFrameSelectedSource" (Get-ObjectValue $runJson "gxFrameSource"))
$framePath = [string](Get-ObjectValue (Get-ObjectValue $runJson "frame") "path")
$frameDump = Get-ObjectValue (Get-ObjectValue $emulatorSummary "gx") "frameDump"
if ($null -ne $frameDump) {
    if ($null -eq $selectedCopyIndex) {
        $selectedCopyIndex = Convert-ToNullableInt64 (Get-ObjectValue $frameDump "sourceCopyIndex")
    }

    if ([string]::IsNullOrWhiteSpace($selectedSource)) {
        $selectedSource = [string](Get-ObjectValue $frameDump "source")
    }
}

$eventRows = New-Object System.Collections.Generic.List[object]
foreach ($focus in $focusRows) {
    $drawStart = Convert-ToNullableInt64 (Get-ObjectValue $focus "mapped_draw_start")
    $drawEnd = Convert-ToNullableInt64 (Get-ObjectValue $focus "mapped_draw_end")
    $drawCount = Convert-ToNullableInt64 (Get-ObjectValue $focus "mapped_draw_count")
    if ($null -eq $drawCount -and $null -ne $drawStart -and $null -ne $drawEnd) {
        $drawCount = $drawEnd - $drawStart + 1
    }

    $nearestCopy = Get-NearestDisplayCopy $displayCopies $drawStart $drawEnd
    $nearestCopyDraw = Convert-ToNullableInt64 (Get-ObjectValue $nearestCopy "draws_seen")
    $nearestCopyIndex = Convert-ToNullableInt64 (Get-ObjectValue $nearestCopy "copy_index")
    $nearestCopyRelation = Get-DrawRangeRelation $nearestCopyDraw $drawStart $drawEnd
    $activity = Get-NearestActivityRun $activityRows $drawStart $drawEnd
    $activityFirstDraw = Convert-ToNullableInt64 (Get-ObjectValue $activity "firstDrawsSeen")
    $activityLastDraw = Convert-ToNullableInt64 (Get-ObjectValue $activity "lastDrawsSeen")
    $coverage = Get-CoverageSummary $coverageRows $drawStart $drawEnd
    $copyToStartDelta = if ($null -ne $nearestCopyDraw -and $null -ne $drawStart) { $nearestCopyDraw - $drawStart } else { $null }
    $copyToEndDelta = if ($null -ne $nearestCopyDraw -and $null -ne $drawEnd) { $nearestCopyDraw - $drawEnd } else { $null }
    $selectedRelation = Get-DrawRangeRelation $selectedCopyDrawsSeen $drawStart $drawEnd
    $selectedCopyMatchesNearest = if ($null -ne $selectedCopyIndex -and $null -ne $nearestCopyIndex) { $selectedCopyIndex -eq $nearestCopyIndex } else { $null }

    $phaseConclusion = if ($nearestCopyRelation -eq "before-packet" -and $coverage.totalColorWrites -gt 0) {
        "packet-renders-after-nearest-display-copy"
    } elseif ($nearestCopyRelation -eq "inside-packet") {
        "display-copy-inside-packet"
    } elseif ($nearestCopyRelation -eq "after-packet") {
        "packet-renders-before-nearest-display-copy"
    } elseif ($coverage.totalColorWrites -gt 0) {
        "packet-renders-no-display-copy"
    } else {
        "no-rendered-focus-content"
    }

    $eventRows.Add([pscustomobject][ordered]@{
        packet = Normalize-Hex ([string](Get-ObjectValue $focus "packet" $focusPacket))
        packet_order = Get-ObjectValue $focus "packet_order"
        instruction = Get-ObjectValue $focus "instruction"
        instruction_delta_from_focus = Get-ObjectValue $focus "instruction_delta_from_focus"
        state_hash = Get-ObjectValue $focus "state_hash"
        small_data_hash = Get-ObjectValue $focus "small_data_hash"
        object_xyz = Get-ObjectValue $focus "object_xyz"
        matrix_translation = Get-ObjectValue $focus "matrix_translation"
        packet_bound_radius = Get-ObjectValue $focus "packet_bound_radius"
        mapped_draw_start = $drawStart
        mapped_draw_end = $drawEnd
        mapped_draw_count = $drawCount
        nearest_display_copy_index = $nearestCopyIndex
        nearest_display_copy_draws_seen = $nearestCopyDraw
        nearest_display_copy_relation = $nearestCopyRelation
        nearest_copy_to_start_delta = $copyToStartDelta
        nearest_copy_to_end_delta = $copyToEndDelta
        nearest_display_address = Get-ObjectValue $nearestCopy "display_address"
        nearest_display_nonblack = Get-ObjectValue $nearestCopy "display_nonblack"
        nearest_display_nonblack_percent = Get-ObjectValue $nearestCopy "display_nonblack_percent"
        nearest_display_nonblack_bounds = Get-ObjectValue $nearestCopy "display_nonblack_bounds"
        nearest_before_nonblack = Get-ObjectValue $nearestCopy "before_nonblack"
        activity_state = Get-ObjectValue $activity "state"
        activity_first_copy_index = Get-ObjectValue $activity "firstCopyIndex"
        activity_last_copy_index = Get-ObjectValue $activity "lastCopyIndex"
        activity_first_draws_seen = $activityFirstDraw
        activity_last_draws_seen = $activityLastDraw
        activity_nonblack_copies = Get-ObjectValue $activity "nonblackCopies"
        activity_max_display_nonblack = Get-ObjectValue $activity "maxDisplayNonblack"
        selected_source = $selectedSource
        selected_copy_index = $selectedCopyIndex
        selected_copy_draws_seen = $selectedCopyDrawsSeen
        selected_copy_relation = $selectedRelation
        selected_copy_matches_nearest = Format-OptionalBool $selectedCopyMatchesNearest
        coverage_draw_count = $coverage.drawCount
        first_color_draw = $coverage.firstColorDraw
        last_color_draw = $coverage.lastColorDraw
        total_color_writes = $coverage.totalColorWrites
        total_black_writes = $coverage.totalBlackWrites
        black_write_ratio = Format-Double $coverage.blackWriteRatio
        max_after_nonblack = $coverage.maxAfterNonblack
        max_after_nonblack_draw = $coverage.maxAfterNonblackDraw
        material_summary = Get-MaterialSummary $materialRows $drawStart $drawEnd
        frame_path = $framePath
        phase_conclusion = $phaseConclusion
    })
}

$summaryGroups = $eventRows | Group-Object phase_conclusion
$conclusionSummary = ($summaryGroups | ForEach-Object { "{0}:{1}" -f $_.Name, $_.Count }) -join ";"
$selectedRelationSummary = (($eventRows | Group-Object selected_copy_relation | ForEach-Object { "{0}:{1}" -f $_.Name, $_.Count }) -join ";")
$nearestRelationSummary = (($eventRows | Group-Object nearest_display_copy_relation | ForEach-Object { "{0}:{1}" -f $_.Name, $_.Count }) -join ";")
$renderedEvents = @($eventRows | Where-Object { (Convert-ToNullableInt64 $_.total_color_writes) -gt 0 })
$copyBeforeRenderedEvents = @($eventRows | Where-Object { $_.phase_conclusion -eq "packet-renders-after-nearest-display-copy" })

$eventsCsvPath = Join-Path $OutputDirectory "focus-packet-phase-events.csv"
$summaryCsvPath = Join-Path $OutputDirectory "focus-packet-phase-summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "focus-packet-phase-report.json"
$eventRows | Export-Csv -LiteralPath $eventsCsvPath -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    focus_packet = $focusPacket
    focus_events = $eventRows.Count
    rendered_focus_events = $renderedEvents.Count
    display_copy_count = $displayCopies.Count
    selected_source = $selectedSource
    selected_copy_index = $selectedCopyIndex
    selected_copy_draws_seen = $selectedCopyDrawsSeen
    selected_relation_summary = $selectedRelationSummary
    nearest_copy_relation_summary = $nearestRelationSummary
    phase_conclusion_summary = $conclusionSummary
    copy_before_rendered_focus_events = $copyBeforeRenderedEvents.Count
    first_focus_instruction = ($eventRows | Select-Object -First 1).instruction
    last_focus_instruction = ($eventRows | Select-Object -Last 1).instruction
    first_focus_draw_range = "{0}-{1}" -f (($eventRows | Select-Object -First 1).mapped_draw_start), (($eventRows | Select-Object -First 1).mapped_draw_end)
    events_csv = $eventsCsvPath
}

$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation
[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-focus-packet-phase.v1"
    runDirectory = $runRoot
    focusPacket = $focusPacket
    generatedAt = (Get-Date).ToString("o")
    inputs = [ordered]@{
        focusEventsCsv = if (Test-Path -LiteralPath $focusEventsCsvPath) { $focusEventsCsvPath } else { $null }
        packetTimelineCsv = if (Test-Path -LiteralPath $packetTimelineCsvPath) { $packetTimelineCsvPath } else { $null }
        gxCopiesCsv = if (Test-Path -LiteralPath $gxCopiesCsvPath) { $gxCopiesCsvPath } else { $null }
        displayActivityCsv = if (Test-Path -LiteralPath $displayActivityCsvPath) { $displayActivityCsvPath } else { $null }
        coverageCsv = if (Test-Path -LiteralPath $coverageCsvPath) { $coverageCsvPath } else { $null }
        materialsCsv = if (Test-Path -LiteralPath $materialsCsvPath) { $materialsCsvPath } else { $null }
        runJson = if (Test-Path -LiteralPath $runJsonPath) { $runJsonPath } else { $null }
        emulatorSummaryJson = if (Test-Path -LiteralPath $emulatorSummaryJsonPath) { $emulatorSummaryJsonPath } else { $null }
    }
    outputs = [ordered]@{
        eventsCsv = $eventsCsvPath
        summaryCsv = $summaryCsvPath
    }
    summary = $summary
    events = $eventRows
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic focus packet phase events: $eventsCsvPath"
Write-Host "Wrote Sonic focus packet phase summary: $summaryCsvPath"
