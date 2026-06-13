param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [int]$NeighborWindow = 12,
    [int]$TopRadiusCount = 16
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

function Parse-DoubleList {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return @()
    }

    return @(
        ([string]$Value).Split("/", [System.StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { Convert-ToNullableDouble $_ } |
            Where-Object { $null -ne $_ }
    )
}

function Format-Double {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Vector3 {
    param(
        [double[]]$Values,
        [string]$Fallback = ""
    )

    if ($null -eq $Values -or $Values.Count -lt 3) {
        return $Fallback
    }

    return "{0}/{1}/{2}" -f (Format-Double $Values[0]), (Format-Double $Values[1]), (Format-Double $Values[2])
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

function Get-VectorDistance {
    param(
        [double[]]$A,
        [double[]]$B
    )

    if ($null -eq $A -or $null -eq $B -or $A.Count -lt 3 -or $B.Count -lt 3) {
        return $null
    }

    $dx = $A[0] - $B[0]
    $dy = $A[1] - $B[1]
    $dz = $A[2] - $B[2]
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy) + ($dz * $dz))
}

function Join-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Get-RadiusClass {
    param([object]$Radius)

    if ($null -eq $Radius) {
        return ""
    }

    if ($Radius -ge 1000.0) {
        return "large-radius"
    }

    if ($Radius -ge 300.0) {
        return "mid-radius"
    }

    return "small-radius"
}

function Get-FirstByPacket {
    param(
        [object[]]$Rows,
        [string]$PacketProperty = "packet"
    )

    $lookup = @{}
    foreach ($row in $Rows) {
        $packet = Normalize-Hex ([string](Get-ObjectValue $row $PacketProperty))
        if ([string]::IsNullOrWhiteSpace($packet) -or $packet.StartsWith("+")) {
            continue
        }

        if (-not $lookup.ContainsKey($packet)) {
            $lookup[$packet] = $row
        }
    }

    return $lookup
}

function Get-GroupedValuesByPacket {
    param(
        [object[]]$Rows,
        [string]$ValueProperty,
        [string]$PacketProperty = "packet"
    )

    $lookup = @{}
    foreach ($row in $Rows) {
        $packet = Normalize-Hex ([string](Get-ObjectValue $row $PacketProperty))
        if ([string]::IsNullOrWhiteSpace($packet) -or $packet.StartsWith("+")) {
            continue
        }

        if (-not $lookup.ContainsKey($packet)) {
            $lookup[$packet] = New-Object System.Collections.Generic.List[string]
        }

        $value = [string](Get-ObjectValue $row $ValueProperty)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $lookup[$packet].Add($value)
        }
    }

    return $lookup
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-packet-placement-comparison"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packetTimelineCsvPath = Join-Path $runRoot "sonic-packet-timeline.csv"
$sceneSummaryCsvPath = Join-Path $runRoot "sonic-scene-state.summary.csv"
$packetSelectionSummaryCsvPath = Join-Path $runRoot "sonic-packet-selection.summary.csv"
$nonrenderedSequenceCsvPath = Join-Path $runRoot "sonic-nonrendered-strips\nonrendered-strip-sequences.csv"

if (-not (Test-CsvHasRows $packetTimelineCsvPath)) {
    throw "Required CSV missing or empty: $packetTimelineCsvPath"
}

$focusPacket = Normalize-Hex $FocusPacket
$timelineRows = @(
    Import-Csv -LiteralPath $packetTimelineCsvPath |
        Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") } }, @{ Expression = { Normalize-Hex ([string](Get-ObjectValue $_ "packet")) } }
)
$sceneRows = if (Test-CsvHasRows $sceneSummaryCsvPath) { @(Import-Csv -LiteralPath $sceneSummaryCsvPath) } else { @() }
$selectionRows = if (Test-CsvHasRows $packetSelectionSummaryCsvPath) { @(Import-Csv -LiteralPath $packetSelectionSummaryCsvPath) } else { @() }
$nonrenderedSequenceRows = if (Test-CsvHasRows $nonrenderedSequenceCsvPath) { @(Import-Csv -LiteralPath $nonrenderedSequenceCsvPath) } else { @() }

$sceneByPacket = Get-FirstByPacket $sceneRows
$selectionByPacket = Get-FirstByPacket $selectionRows
$selectionPhasesByPacket = Get-GroupedValuesByPacket $selectionRows "phase"

$focusIndex = -1
for ($i = 0; $i -lt $timelineRows.Count; $i++) {
    if ((Normalize-Hex ([string](Get-ObjectValue $timelineRows[$i] "packet"))) -eq $focusPacket) {
        $focusIndex = $i
        break
    }
}

if ($focusIndex -lt 0) {
    throw "Focus packet $focusPacket not found in $packetTimelineCsvPath"
}

$focusTimeline = $timelineRows[$focusIndex]
$focusScene = if ($sceneByPacket.ContainsKey($focusPacket)) { $sceneByPacket[$focusPacket] } else { $null }
$focusSelection = if ($selectionByPacket.ContainsKey($focusPacket)) { $selectionByPacket[$focusPacket] } else { $null }
$focusInstruction = Convert-ToNullableInt64 (Get-ObjectValue $focusTimeline "instruction")
$focusObjectVector = Parse-DoubleList (Get-ObjectValue $focusTimeline "object_xyz")
if ($focusObjectVector.Count -lt 3 -and $null -ne $focusScene) {
    $focusObjectVector = Parse-DoubleList (Get-ObjectValue $focusScene "object_xyz")
}

$focusMatrixVector = Parse-DoubleList (Get-ObjectValue $focusTimeline "matrix_translation")
$focusStateHash = [string](Get-ObjectValue $focusTimeline "state_hash")
$focusSmallDataHash = [string](Get-ObjectValue $focusTimeline "small_data_hash")

function Get-PacketRadius {
    param(
        [object]$Timeline,
        [object]$Scene,
        [object]$Selection
    )

    $selectionRadius = Convert-ToNullableDouble (Get-ObjectValue $Selection "packet_bound_radius")
    if ($null -ne $selectionRadius) {
        return $selectionRadius
    }

    $wordValues = Parse-DoubleList (Get-ObjectValue $Scene "packet_word2_5")
    if ($wordValues.Count -ge 4) {
        return $wordValues[3]
    }

    return $null
}

$focusRadius = Get-PacketRadius $focusTimeline $focusScene $focusSelection

$allRows = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $timelineRows.Count; $i++) {
    $timeline = $timelineRows[$i]
    $packet = Normalize-Hex ([string](Get-ObjectValue $timeline "packet"))
    if ([string]::IsNullOrWhiteSpace($packet) -or $packet.StartsWith("+")) {
        continue
    }

    $scene = if ($sceneByPacket.ContainsKey($packet)) { $sceneByPacket[$packet] } else { $null }
    $selection = if ($selectionByPacket.ContainsKey($packet)) { $selectionByPacket[$packet] } else { $null }
    $phases = if ($selectionPhasesByPacket.ContainsKey($packet)) { Join-Unique $selectionPhasesByPacket[$packet] } else { "" }
    $instruction = Convert-ToNullableInt64 (Get-ObjectValue $timeline "instruction")
    $matrixInstruction = Convert-ToNullableInt64 (Get-ObjectValue $timeline "matrix_instruction")
    $objectVector = Parse-DoubleList (Get-ObjectValue $timeline "object_xyz")
    if ($objectVector.Count -lt 3 -and $null -ne $scene) {
        $objectVector = Parse-DoubleList (Get-ObjectValue $scene "object_xyz")
    }

    $matrixVector = Parse-DoubleList (Get-ObjectValue $timeline "matrix_translation")
    $packetWordValues = Parse-DoubleList (Get-ObjectValue $scene "packet_word2_5")
    $radius = Get-PacketRadius $timeline $scene $selection
    $radiusRatio = if ($null -ne $focusRadius -and $focusRadius -ne 0.0 -and $null -ne $radius) { $radius / $focusRadius } else { $null }
    $objectDistance = Get-VectorDistance $objectVector $focusObjectVector
    $matrixDistance = Get-VectorDistance $matrixVector $focusMatrixVector
    $stateHash = [string](Get-ObjectValue $timeline "state_hash")
    if ([string]::IsNullOrWhiteSpace($stateHash) -and $null -ne $scene) {
        $stateHash = [string](Get-ObjectValue $scene "state_hash")
    }

    $smallDataHash = [string](Get-ObjectValue $timeline "small_data_hash")
    if ([string]::IsNullOrWhiteSpace($smallDataHash) -and $null -ne $scene) {
        $smallDataHash = [string](Get-ObjectValue $scene "small_data_hash")
    }

    $mappedDrawStart = Convert-ToNullableInt64 (Get-ObjectValue $timeline "mapped_draw_start")
    $mappedDrawEnd = Convert-ToNullableInt64 (Get-ObjectValue $timeline "mapped_draw_end")
    $mappedDrawCount = Convert-ToNullableInt64 (Get-ObjectValue $timeline "mapped_draw_count")
    if ($null -eq $mappedDrawCount -and $null -ne $mappedDrawStart -and $null -ne $mappedDrawEnd) {
        $mappedDrawCount = $mappedDrawEnd - $mappedDrawStart + 1
    }

    $orderDelta = $i - $focusIndex
    $role = if ($orderDelta -eq 0) {
        "focus"
    } elseif ($orderDelta -lt 0) {
        "before"
    } else {
        "after"
    }

    $allRows.Add([pscustomobject][ordered]@{
        packet = $packet
        packet_kind = (Get-ObjectValue $timeline "packet_kind" (Get-ObjectValue $scene "packet_kind"))
        object = (Get-ObjectValue $timeline "object" (Get-ObjectValue $scene "object"))
        object_kind = (Get-ObjectValue $timeline "object_kind" (Get-ObjectValue $scene "object_kind"))
        role = $role
        packet_order = $i
        order_delta_from_focus = $orderDelta
        instruction = $instruction
        instruction_delta_from_focus = if ($null -ne $focusInstruction -and $null -ne $instruction) { $instruction - $focusInstruction } else { $null }
        matrix_instruction = $matrixInstruction
        matrix_delta = (Get-ObjectValue $timeline "matrix_delta")
        object_xyz = Format-Vector3 $objectVector ([string](Get-ObjectValue $timeline "object_xyz" (Get-ObjectValue $scene "object_xyz")))
        matrix_translation = Format-Vector3 $matrixVector ([string](Get-ObjectValue $timeline "matrix_translation"))
        object_distance_from_focus = Format-Double $objectDistance
        matrix_distance_from_focus = Format-Double $matrixDistance
        packet_word2_5 = [string](Get-ObjectValue $scene "packet_word2_5")
        packet_bound_xyz = Format-Vector3 $packetWordValues ([string](Get-ObjectValue $selection "packet_bound_xyz"))
        packet_bound_radius = Format-Double $radius
        focus_radius_ratio = Format-Double $radiusRatio
        radius_class = Get-RadiusClass $radius
        resource_flag = (Get-ObjectValue $timeline "resource_flag" (Get-ObjectValue $scene "resource_flag"))
        state_word80 = (Get-ObjectValue $timeline "state_word80" (Get-ObjectValue $scene "state_word80"))
        state_hash = $stateHash
        state_hash_match = Format-OptionalBool (-not [string]::IsNullOrWhiteSpace($focusStateHash) -and $stateHash -eq $focusStateHash)
        small_data_hash = $smallDataHash
        small_data_hash_match = Format-OptionalBool (-not [string]::IsNullOrWhiteSpace($focusSmallDataHash) -and $smallDataHash -eq $focusSmallDataHash)
        current_matrix_pointer = (Get-ObjectValue $scene "current_matrix_pointer")
        previous_matrix_pointer = (Get-ObjectValue $scene "previous_matrix_pointer")
        selection_phases = $phases
        cull_result = (Get-ObjectValue $selection "cull_result")
        mapped = Format-OptionalBool ($null -ne $mappedDrawStart)
        mapped_draw_count = $mappedDrawCount
        mapped_draw_start = $mappedDrawStart
        mapped_draw_end = $mappedDrawEnd
        decoded_vertices = (Get-ObjectValue $timeline "decoded_vertices")
        clipped_vertices = (Get-ObjectValue $timeline "clipped_vertices")
        view_bounds = (Get-ObjectValue $timeline "view_bounds")
        screen_bounds = (Get-ObjectValue $timeline "screen_bounds")
        anchor_fifo_offset = (Get-ObjectValue $timeline "anchor_fifo_offset")
        mapped_fifo_start = (Get-ObjectValue $timeline "mapped_fifo_start")
        mapped_fifo_end = (Get-ObjectValue $timeline "mapped_fifo_end")
    })
}

$uniqueByPacket = @{}
foreach ($row in $allRows) {
    if (-not $uniqueByPacket.ContainsKey($row.packet) -or $row.role -eq "focus") {
        $uniqueByPacket[$row.packet] = $row
    }
}

$uniqueRows = @($uniqueByPacket.Values)
$rankByPacket = @{}
$rankWithinKindByPacket = @{}
$radiusRank = 0
foreach ($row in @($uniqueRows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.packet_bound_radius) } | Sort-Object @{ Expression = { -[double]$_.packet_bound_radius } }, packet)) {
    $radiusRank++
    $rankByPacket[$row.packet] = $radiusRank
}

$kindGroups = $uniqueRows | Group-Object packet_kind
foreach ($group in $kindGroups) {
    $rank = 0
    foreach ($row in @($group.Group | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.packet_bound_radius) } | Sort-Object @{ Expression = { -[double]$_.packet_bound_radius } }, packet)) {
        $rank++
        $rankWithinKindByPacket[$row.packet] = $rank
    }
}

foreach ($row in $allRows) {
    $rankAll = if ($rankByPacket.ContainsKey($row.packet)) { $rankByPacket[$row.packet] } else { "" }
    $rankWithinKind = if ($rankWithinKindByPacket.ContainsKey($row.packet)) { $rankWithinKindByPacket[$row.packet] } else { "" }
    $row | Add-Member -NotePropertyName radius_rank_all -NotePropertyValue $rankAll -Force
    $row | Add-Member -NotePropertyName radius_rank_within_kind -NotePropertyValue $rankWithinKind -Force
}

$neighborStart = [Math]::Max(0, $focusIndex - [Math]::Max(0, $NeighborWindow))
$neighborEnd = [Math]::Min($timelineRows.Count - 1, $focusIndex + [Math]::Max(0, $NeighborWindow))
$comparisonRows = @(
    $allRows |
        Where-Object { $_.packet_order -ge $neighborStart -and $_.packet_order -le $neighborEnd } |
        Sort-Object packet_order
)

$topRadiusRows = @(
    $uniqueRows |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.packet_bound_radius) } |
        Sort-Object @{ Expression = { [int]$_.radius_rank_all } } |
        Select-Object -First ([Math]::Max(1, $TopRadiusCount))
)

$focusRenderSummary = ""
if ($nonrenderedSequenceRows.Count -gt 0) {
    $focusRenderSummary = @(
        $nonrenderedSequenceRows |
            ForEach-Object {
                "{0}:{1}-{2}({3})" -f (Get-ObjectValue $_ "primary_reason"), (Get-ObjectValue $_ "draw_start"), (Get-ObjectValue $_ "draw_end"), (Get-ObjectValue $_ "draw_count")
            }
    ) -join ";"
}

$comparisonCsvPath = Join-Path $OutputDirectory "packet-placement-comparison.csv"
$eventsCsvPath = Join-Path $OutputDirectory "packet-placement-events.csv"
$focusEventsCsvPath = Join-Path $OutputDirectory "focus-packet-events.csv"
$topRadiusCsvPath = Join-Path $OutputDirectory "packet-radius-ranking.csv"
$summaryCsvPath = Join-Path $OutputDirectory "packet-placement-comparison.summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "packet-placement-comparison-report.json"

$comparisonRows | Export-Csv -LiteralPath $comparisonCsvPath -NoTypeInformation
$allRows | Sort-Object packet_order | Export-Csv -LiteralPath $eventsCsvPath -NoTypeInformation
$focusEventRows = @($allRows | Where-Object { $_.packet -eq $focusPacket } | Sort-Object packet_order)
$focusEventRows | Export-Csv -LiteralPath $focusEventsCsvPath -NoTypeInformation
$topRadiusRows | Export-Csv -LiteralPath $topRadiusCsvPath -NoTypeInformation

$focusOutputRow = $allRows | Where-Object { $_.packet -eq $focusPacket } | Select-Object -First 1
$sameStateCount = @($uniqueRows | Where-Object { $_.state_hash_match -eq "True" }).Count
$sameSmallDataCount = @($uniqueRows | Where-Object { $_.small_data_hash_match -eq "True" }).Count
$mappedCount = @($uniqueRows | Where-Object { $_.mapped -eq "True" }).Count
$largeRadiusCount = @($uniqueRows | Where-Object { $_.radius_class -eq "large-radius" }).Count
$neighborMappedCount = @($comparisonRows | Where-Object { $_.mapped -eq "True" }).Count
$neighborSameStateCount = @($comparisonRows | Where-Object { $_.state_hash_match -eq "True" }).Count
$neighborLargeRadiusCount = @($comparisonRows | Where-Object { $_.radius_class -eq "large-radius" }).Count

$summary = [pscustomobject][ordered]@{
    focus_packet = $focusPacket
    focus_instruction = $focusInstruction
    focus_packet_kind = (Get-ObjectValue $focusOutputRow "packet_kind")
    focus_object = (Get-ObjectValue $focusOutputRow "object")
    focus_object_xyz = (Get-ObjectValue $focusOutputRow "object_xyz")
    focus_matrix_translation = (Get-ObjectValue $focusOutputRow "matrix_translation")
    focus_packet_bound_radius = (Get-ObjectValue $focusOutputRow "packet_bound_radius")
    focus_radius_class = (Get-ObjectValue $focusOutputRow "radius_class")
    focus_radius_rank_all = (Get-ObjectValue $focusOutputRow "radius_rank_all")
    focus_radius_rank_within_kind = (Get-ObjectValue $focusOutputRow "radius_rank_within_kind")
    focus_mapped_draws = "{0}-{1}" -f (Get-ObjectValue $focusOutputRow "mapped_draw_start"), (Get-ObjectValue $focusOutputRow "mapped_draw_end")
    focus_decoded_vertices = (Get-ObjectValue $focusOutputRow "decoded_vertices")
    focus_clipped_vertices = (Get-ObjectValue $focusOutputRow "clipped_vertices")
    focus_screen_bounds = (Get-ObjectValue $focusOutputRow "screen_bounds")
    focus_packet_events = $focusEventRows.Count
    focus_render_sequence_summary = $focusRenderSummary
    total_packet_events = $allRows.Count
    total_packets = $uniqueRows.Count
    mapped_packets = $mappedCount
    same_state_hash_packets = $sameStateCount
    same_small_data_hash_packets = $sameSmallDataCount
    large_radius_packets = $largeRadiusCount
    neighbor_window = $NeighborWindow
    neighbor_packets = $comparisonRows.Count
    neighbor_mapped_packets = $neighborMappedCount
    neighbor_same_state_hash_packets = $neighborSameStateCount
    neighbor_large_radius_packets = $neighborLargeRadiusCount
    comparison_csv = $comparisonCsvPath
    events_csv = $eventsCsvPath
    focus_events_csv = $focusEventsCsvPath
    radius_ranking_csv = $topRadiusCsvPath
}

$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation
[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-packet-placement-comparison.v1"
    runDirectory = $runRoot
    focusPacket = $focusPacket
    generatedAt = (Get-Date).ToString("o")
    inputs = [ordered]@{
        packetTimelineCsv = $packetTimelineCsvPath
        sceneSummaryCsv = if (Test-Path -LiteralPath $sceneSummaryCsvPath) { $sceneSummaryCsvPath } else { $null }
        packetSelectionSummaryCsv = if (Test-Path -LiteralPath $packetSelectionSummaryCsvPath) { $packetSelectionSummaryCsvPath } else { $null }
        nonrenderedSequenceCsv = if (Test-Path -LiteralPath $nonrenderedSequenceCsvPath) { $nonrenderedSequenceCsvPath } else { $null }
    }
    outputs = [ordered]@{
        comparisonCsv = $comparisonCsvPath
        eventsCsv = $eventsCsvPath
        focusEventsCsv = $focusEventsCsvPath
        radiusRankingCsv = $topRadiusCsvPath
        summaryCsv = $summaryCsvPath
    }
    summary = $summary
    focus = $focusOutputRow
    focusEvents = $focusEventRows
    nearestPackets = $comparisonRows
    topRadiusPackets = $topRadiusRows
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic packet placement comparison: $comparisonCsvPath"
Write-Host "Wrote Sonic packet placement events: $eventsCsvPath"
Write-Host "Wrote Sonic focus packet events: $focusEventsCsvPath"
Write-Host "Wrote Sonic packet radius ranking: $topRadiusCsvPath"
Write-Host "Wrote Sonic packet placement summary: $summaryCsvPath"
