param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineRunDirectory,

    [Parameter(Mandatory = $true)]
    [string]$CandidateRunDirectory,

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

function Import-RequiredCsv {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-CsvHasRows $Path)) {
        throw "$Description not found or empty: $Path"
    }

    return @(Import-Csv -LiteralPath $Path)
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

function Convert-ToInt64OrZero {
    param([object]$Value)

    $result = Convert-ToNullableInt64 $Value
    if ($null -eq $result) {
        return 0L
    }

    return $result
}

function Convert-FifoOffset {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $normalized = Normalize-Hex $Value
    if ($normalized.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int64]::Parse($normalized.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($normalized.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int64]::Parse($normalized.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $null
}

function Format-HexDelta {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $number = [int64]$Value
    if ($number -lt 0) {
        return "-0x{0:X}" -f ([Math]::Abs($number))
    }

    return "+0x{0:X}" -f $number
}

function Normalize-MaterialSummaryForIdentity {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $normalized = $Value -replace ":\d+-\d+:cw", ":#-#:cw"
    $normalized = $normalized -replace "cw\d+", "cw#"
    $normalized = $normalized -replace "black\d+", "black#"
    return $normalized
}

function Get-RunInputs {
    param([string]$RunDirectory)

    $root = Resolve-FullPath $RunDirectory
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Run directory not found: $root"
    }

    $recurrencePath = Join-Path $root "sonic-packet-recurrence\packet-recurrence-events.csv"
    $timelinePath = Join-Path $root "sonic-packet-timeline.csv"
    return [pscustomobject][ordered]@{
        root = $root
        recurrencePath = $recurrencePath
        timelinePath = $timelinePath
        recurrenceRows = Import-RequiredCsv $recurrencePath "Packet recurrence events CSV"
        timelineRows = Import-RequiredCsv $timelinePath "Packet timeline CSV"
    }
}

function Build-TimelineIndex {
    param([object[]]$Rows)

    $index = @{}
    foreach ($row in $Rows) {
        $packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
        $instruction = Convert-ToNullableInt64 (Get-ObjectValue $row "instruction")
        if ([string]::IsNullOrWhiteSpace($packet) -or $null -eq $instruction) {
            continue
        }

        $key = "{0}|{1}" -f $packet, $instruction
        if (-not $index.ContainsKey($key)) {
            $index[$key] = $row
        }
    }

    return $index
}

function Get-TimelineRow {
    param(
        [hashtable]$Index,
        [string]$Packet,
        [object]$Instruction
    )

    $instructionValue = Convert-ToNullableInt64 $Instruction
    if ($null -eq $instructionValue) {
        return $null
    }

    $key = "{0}|{1}" -f (Normalize-Hex $Packet), $instructionValue
    if ($Index.ContainsKey($key)) {
        return $Index[$key]
    }

    return $null
}

function Get-MappedTimelineRows {
    param([object[]]$Rows)

    return @($Rows | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "mapped_draw_start"))
    } | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "mapped_draw_start") }, { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") })
}

function New-EmissionRow {
    param(
        [object]$Baseline,
        [object]$Candidate,
        [object]$BaselineTimeline,
        [object]$CandidateTimeline
    )

    $baselineStart = Convert-ToNullableInt64 (Get-ObjectValue $Baseline "mapped_draw_start")
    $candidateStart = Convert-ToNullableInt64 (Get-ObjectValue $Candidate "mapped_draw_start")
    $baselineEnd = Convert-ToNullableInt64 (Get-ObjectValue $Baseline "mapped_draw_end")
    $candidateEnd = Convert-ToNullableInt64 (Get-ObjectValue $Candidate "mapped_draw_end")
    $baselineColor = Convert-ToInt64OrZero (Get-ObjectValue $Baseline "total_color_writes")
    $candidateColor = Convert-ToInt64OrZero (Get-ObjectValue $Candidate "total_color_writes")
    $baselineBlack = Convert-ToInt64OrZero (Get-ObjectValue $Baseline "total_black_writes")
    $candidateBlack = Convert-ToInt64OrZero (Get-ObjectValue $Candidate "total_black_writes")
    $baselineFifoStart = Convert-FifoOffset ([string](Get-ObjectValue $BaselineTimeline "mapped_fifo_start"))
    $candidateFifoStart = Convert-FifoOffset ([string](Get-ObjectValue $CandidateTimeline "mapped_fifo_start"))
    $baselineFifoEnd = Convert-FifoOffset ([string](Get-ObjectValue $BaselineTimeline "mapped_fifo_end"))
    $candidateFifoEnd = Convert-FifoOffset ([string](Get-ObjectValue $CandidateTimeline "mapped_fifo_end"))
    $baselineDarkIdentity = Normalize-MaterialSummaryForIdentity ([string](Get-ObjectValue $Baseline "dark_material_summary"))
    $candidateDarkIdentity = Normalize-MaterialSummaryForIdentity ([string](Get-ObjectValue $Candidate "dark_material_summary"))
    $baselineLitIdentity = Normalize-MaterialSummaryForIdentity ([string](Get-ObjectValue $Baseline "lit_material_summary"))
    $candidateLitIdentity = Normalize-MaterialSummaryForIdentity ([string](Get-ObjectValue $Candidate "lit_material_summary"))

    return [pscustomobject][ordered]@{
        instruction = Get-ObjectValue $Baseline "instruction" (Get-ObjectValue $Candidate "instruction")
        packet = Normalize-Hex ([string](Get-ObjectValue $Baseline "packet" (Get-ObjectValue $Candidate "packet")))
        object = Normalize-Hex ([string](Get-ObjectValue $Baseline "object" (Get-ObjectValue $Candidate "object")))
        state_hash = Get-ObjectValue $Baseline "state_hash" (Get-ObjectValue $Candidate "state_hash")
        small_data_hash = Get-ObjectValue $Baseline "small_data_hash" (Get-ObjectValue $Candidate "small_data_hash")
        packet_hash_equal = ([string](Get-ObjectValue $BaselineTimeline "packet_hash") -eq [string](Get-ObjectValue $CandidateTimeline "packet_hash"))
        object_hash_equal = ([string](Get-ObjectValue $BaselineTimeline "object_hash") -eq [string](Get-ObjectValue $CandidateTimeline "object_hash"))
        matrix_translation_equal = ([string](Get-ObjectValue $Baseline "matrix_translation") -eq [string](Get-ObjectValue $Candidate "matrix_translation"))
        material_signature_equal_ignoring_draw_numbers = (($baselineDarkIdentity -eq $candidateDarkIdentity) -and ($baselineLitIdentity -eq $candidateLitIdentity))
        baseline_draw_start = $baselineStart
        baseline_draw_end = $baselineEnd
        candidate_draw_start = $candidateStart
        candidate_draw_end = $candidateEnd
        draw_start_delta = if ($null -ne $baselineStart -and $null -ne $candidateStart) { $candidateStart - $baselineStart } else { "" }
        draw_end_delta = if ($null -ne $baselineEnd -and $null -ne $candidateEnd) { $candidateEnd - $baselineEnd } else { "" }
        baseline_fifo_start = Get-ObjectValue $BaselineTimeline "mapped_fifo_start"
        baseline_fifo_end = Get-ObjectValue $BaselineTimeline "mapped_fifo_end"
        candidate_fifo_start = Get-ObjectValue $CandidateTimeline "mapped_fifo_start"
        candidate_fifo_end = Get-ObjectValue $CandidateTimeline "mapped_fifo_end"
        fifo_start_delta = if ($null -ne $baselineFifoStart -and $null -ne $candidateFifoStart) { Format-HexDelta ($candidateFifoStart - $baselineFifoStart) } else { "" }
        fifo_end_delta = if ($null -ne $baselineFifoEnd -and $null -ne $candidateFifoEnd) { Format-HexDelta ($candidateFifoEnd - $baselineFifoEnd) } else { "" }
        baseline_decoded_vertices = Get-ObjectValue $Baseline "decoded_vertices"
        candidate_decoded_vertices = Get-ObjectValue $Candidate "decoded_vertices"
        baseline_clipped_vertices = Get-ObjectValue $Baseline "clipped_vertices"
        candidate_clipped_vertices = Get-ObjectValue $Candidate "clipped_vertices"
        baseline_first_color_draw = Get-ObjectValue $Baseline "first_color_draw"
        baseline_last_color_draw = Get-ObjectValue $Baseline "last_color_draw"
        candidate_first_color_draw = Get-ObjectValue $Candidate "first_color_draw"
        candidate_last_color_draw = Get-ObjectValue $Candidate "last_color_draw"
        baseline_total_color_writes = $baselineColor
        candidate_total_color_writes = $candidateColor
        color_write_delta = $candidateColor - $baselineColor
        baseline_total_black_writes = $baselineBlack
        candidate_total_black_writes = $candidateBlack
        black_write_delta = $candidateBlack - $baselineBlack
        baseline_screen_bounds = Get-ObjectValue $Baseline "screen_bounds"
        candidate_screen_bounds = Get-ObjectValue $Candidate "screen_bounds"
        baseline_nearest_display_copy_draws_seen = Get-ObjectValue $Baseline "nearest_display_copy_draws_seen"
        candidate_nearest_display_copy_draws_seen = Get-ObjectValue $Candidate "nearest_display_copy_draws_seen"
        baseline_recurrence_class = Get-ObjectValue $Baseline "recurrence_class"
        candidate_recurrence_class = Get-ObjectValue $Candidate "recurrence_class"
    }
}

function New-MappedTimelineSummaryRow {
    param(
        [string]$RunLabel,
        [object]$Row
    )

    return [pscustomobject][ordered]@{
        run = $RunLabel
        instruction = Get-ObjectValue $Row "instruction"
        packet = Normalize-Hex ([string](Get-ObjectValue $Row "packet"))
        packet_kind = Get-ObjectValue $Row "packet_kind"
        object = Normalize-Hex ([string](Get-ObjectValue $Row "object"))
        object_kind = Get-ObjectValue $Row "object_kind"
        mapped_draw_start = Get-ObjectValue $Row "mapped_draw_start"
        mapped_draw_end = Get-ObjectValue $Row "mapped_draw_end"
        mapped_draw_count = Get-ObjectValue $Row "mapped_draw_count"
        mapped_fifo_start = Get-ObjectValue $Row "mapped_fifo_start"
        mapped_fifo_end = Get-ObjectValue $Row "mapped_fifo_end"
        anchor_fifo_offset = Get-ObjectValue $Row "anchor_fifo_offset"
        state_hash = Get-ObjectValue $Row "state_hash"
        small_data_hash = Get-ObjectValue $Row "small_data_hash"
        packet_hash = Get-ObjectValue $Row "packet_hash"
        object_hash = Get-ObjectValue $Row "object_hash"
        matrix_translation = Get-ObjectValue $Row "matrix_translation"
        screen_bounds = Get-ObjectValue $Row "screen_bounds"
    }
}

$baseline = Get-RunInputs $BaselineRunDirectory
$candidate = Get-RunInputs $CandidateRunDirectory
$focusPacketNormalized = Normalize-Hex $FocusPacket

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $candidate.root "sonic-packet-emission-comparison"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$baselineTimelineIndex = Build-TimelineIndex $baseline.timelineRows
$candidateTimelineIndex = Build-TimelineIndex $candidate.timelineRows
$candidateByInstruction = @{}
foreach ($row in $candidate.recurrenceRows) {
    $packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
    $instruction = Convert-ToNullableInt64 (Get-ObjectValue $row "instruction")
    if ($packet -ne $focusPacketNormalized -or $null -eq $instruction) {
        continue
    }

    $candidateByInstruction[[string]$instruction] = $row
}

$comparisonRows = @()
foreach ($baselineRow in $baseline.recurrenceRows) {
    $packet = Normalize-Hex ([string](Get-ObjectValue $baselineRow "packet"))
    $instruction = Convert-ToNullableInt64 (Get-ObjectValue $baselineRow "instruction")
    if ($packet -ne $focusPacketNormalized -or $null -eq $instruction) {
        continue
    }

    $candidateRow = $candidateByInstruction[[string]$instruction]
    if ($null -eq $candidateRow) {
        continue
    }

    $baselineTimelineRow = Get-TimelineRow $baselineTimelineIndex $packet $instruction
    $candidateTimelineRow = Get-TimelineRow $candidateTimelineIndex $packet $instruction
    $comparisonRows += New-EmissionRow $baselineRow $candidateRow $baselineTimelineRow $candidateTimelineRow
}

$mappedRows = @()
foreach ($row in (Get-MappedTimelineRows $baseline.timelineRows)) {
    $mappedRows += New-MappedTimelineSummaryRow "baseline" $row
}
foreach ($row in (Get-MappedTimelineRows $candidate.timelineRows)) {
    $mappedRows += New-MappedTimelineSummaryRow "candidate" $row
}

$comparisonCsvPath = Join-Path $outputRoot "packet-emission-comparison.csv"
$mappedTimelineCsvPath = Join-Path $outputRoot "mapped-packet-timeline.csv"
$summaryJsonPath = Join-Path $outputRoot "packet-emission-comparison-report.json"

$comparisonRows | Export-Csv -NoTypeInformation -LiteralPath $comparisonCsvPath
$mappedRows | Export-Csv -NoTypeInformation -LiteralPath $mappedTimelineCsvPath

$samePacketHashCount = @($comparisonRows | Where-Object { $_.packet_hash_equal -eq $true }).Count
$sameMatrixCount = @($comparisonRows | Where-Object { $_.matrix_translation_equal -eq $true }).Count
$sameMaterialCount = @($comparisonRows | Where-Object { $_.material_signature_equal_ignoring_draw_numbers -eq $true }).Count
$candidateAdjacentPackets = @($mappedRows | Where-Object { $_.run -eq "candidate" -and $_.packet -ne $focusPacketNormalized } | Select-Object -First 20)

$summary = [pscustomobject][ordered]@{
    baselineRunDirectory = $baseline.root
    candidateRunDirectory = $candidate.root
    outputDirectory = $outputRoot
    focusPacket = $focusPacketNormalized
    joinedEmissionCount = $comparisonRows.Count
    samePacketHashCount = $samePacketHashCount
    sameMatrixTranslationCount = $sameMatrixCount
    sameMaterialSignatureIgnoringDrawNumbersCount = $sameMaterialCount
    candidateMappedNonFocusPacketCount = @($mappedRows | Where-Object { $_.run -eq "candidate" -and $_.packet -ne $focusPacketNormalized }).Count
    candidateMappedNonFocusPackets = $candidateAdjacentPackets
    outputFiles = [ordered]@{
        packetEmissionComparisonCsv = $comparisonCsvPath
        mappedPacketTimelineCsv = $mappedTimelineCsvPath
        reportJson = $summaryJsonPath
    }
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

Write-Host "Wrote Sonic packet emission comparison to $outputRoot"
