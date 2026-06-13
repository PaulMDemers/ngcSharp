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

function Import-OptionalCsv {
    param([string]$Path)

    if (Test-CsvHasRows $Path) {
        return @(Import-Csv -LiteralPath $Path)
    }

    return @()
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

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return ""
    }

    if ([bool]::Parse([string]$Value)) {
        return "True"
    }

    return "False"
}

function Join-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Get-FirstByInstruction {
    param(
        [object[]]$Rows,
        [object]$Instruction
    )

    if ($null -eq $Instruction) {
        return $null
    }

    foreach ($row in $Rows) {
        $rowInstruction = Convert-ToNullableInt64 (Get-ObjectValue $row "instruction")
        if ($rowInstruction -eq $Instruction) {
            return $row
        }
    }

    return $null
}

function Get-MaterialSequenceSignature {
    param([object[]]$Rows)

    return (@($Rows | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "sequence_index") } | ForEach-Object {
        $texture = [string](Get-ObjectValue $_ "texture_address")
        if ([string]::IsNullOrWhiteSpace($texture)) {
            $texture = "none"
        }

        "{0}:{1}:{2}-{3}:cw{4}:black{5}" -f `
            (Get-ObjectValue $_ "partition_kind"),
            $texture,
            (Get-ObjectValue $_ "draw_start"),
            (Get-ObjectValue $_ "draw_end"),
            (Get-ObjectValue $_ "total_color_writes"),
            (Get-ObjectValue $_ "total_black_color_writes")
    }) -join ";")
}

function Get-SequenceSummary {
    param(
        [object[]]$Rows,
        [string]$PartitionKind
    )

    $matches = @($Rows | Where-Object { [string](Get-ObjectValue $_ "partition_kind") -eq $PartitionKind })
    if ($matches.Count -eq 0) {
        return ""
    }

    return (@($matches | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_start") } | ForEach-Object {
        "{0}:{1}-{2}:cw{3}:black{4}:src{5}:out{6}:s{7}:t{8}" -f `
            (Get-ObjectValue $_ "texture_address"),
            (Get-ObjectValue $_ "draw_start"),
            (Get-ObjectValue $_ "draw_end"),
            (Get-ObjectValue $_ "total_color_writes"),
            (Get-ObjectValue $_ "total_black_color_writes"),
            (Get-ObjectValue $_ "source_records"),
            (Get-ObjectValue $_ "output_indices"),
            (Get-ObjectValue $_ "tex_s_range"),
            (Get-ObjectValue $_ "tex_t_range")
    }) -join ";")
}

function Get-RecurrenceClass {
    param(
        [object]$PhaseRow,
        [object]$CopyRow
    )

    $nextIncludes = [string](Get-ObjectValue $CopyRow "next_copy_includes_packet")
    if ($nextIncludes -eq "True") {
        return "included-by-next-display-copy"
    }

    $selectedRelation = [string](Get-ObjectValue $PhaseRow "selected_copy_relation")
    if (-not [string]::IsNullOrWhiteSpace($selectedRelation)) {
        return "selected-copy-$selectedRelation"
    }

    $nearestRelation = [string](Get-ObjectValue $PhaseRow "nearest_display_copy_relation")
    if (-not [string]::IsNullOrWhiteSpace($nearestRelation)) {
        return "nearest-copy-$nearestRelation"
    }

    return "unclassified"
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-packet-recurrence"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$focusPacket = Normalize-Hex $FocusPacket
$focusEventsPath = Join-Path $runRoot "sonic-packet-placement-comparison/focus-packet-events.csv"
$phaseEventsPath = Join-Path $runRoot "sonic-focus-packet-phase/focus-packet-phase-events.csv"
$copyBracketsPath = Join-Path $runRoot "sonic-copy-brackets/focus-packet-copy-brackets.csv"
$materialSequencesPath = Join-Path $runRoot "sonic-packet-material-partitions/packet-material-sequences.csv"
$matrixSourceSummaryPath = Join-Path $runRoot "sonic-focus-matrix-provenance/focus-matrix-source-summary.csv"

if (-not (Test-CsvHasRows $focusEventsPath)) {
    throw "Required focus event CSV missing or empty: $focusEventsPath"
}

$focusRows = @(
    Import-Csv -LiteralPath $focusEventsPath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
        Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") }
)
if ($focusRows.Count -eq 0) {
    throw "No focus packet events found for $focusPacket in $focusEventsPath"
}

$phaseRows = Import-OptionalCsv $phaseEventsPath
$copyRows = Import-OptionalCsv $copyBracketsPath
$materialRows = Import-OptionalCsv $materialSequencesPath
$matrixRows = Import-OptionalCsv $matrixSourceSummaryPath
$matrixSummary = $matrixRows | Select-Object -First 1
$firstInstruction = Convert-ToNullableInt64 (Get-ObjectValue ($focusRows | Select-Object -First 1) "instruction")
$materialSignature = Get-MaterialSequenceSignature $materialRows
$darkMaterialSummary = Get-SequenceSummary $materialRows "dark-material"
$litMaterialSummary = Get-SequenceSummary $materialRows "lit-material"
$nonRenderedSummary = Get-SequenceSummary $materialRows "no-rendered-material"
$noRenderedCount = @($materialRows | Where-Object { [string](Get-ObjectValue $_ "partition_kind") -eq "no-rendered-material" }).Count

$eventRows = New-Object System.Collections.Generic.List[object]
$index = 0
foreach ($focus in $focusRows) {
    $instruction = Convert-ToNullableInt64 (Get-ObjectValue $focus "instruction")
    $phase = Get-FirstByInstruction $phaseRows $instruction
    $copy = Get-FirstByInstruction $copyRows $instruction
    $colorWrites = Convert-ToNullableInt64 (Get-ObjectValue $phase "total_color_writes")
    $blackWrites = Convert-ToNullableInt64 (Get-ObjectValue $phase "total_black_writes")
    $blackRatio = if ($null -ne $colorWrites -and $colorWrites -gt 0 -and $null -ne $blackWrites) {
        [double]$blackWrites / [double]$colorWrites
    } else {
        $null
    }

    $eventRows.Add([pscustomobject][ordered]@{
        packet = $focusPacket
        recurrence_index = $index
        role = Get-ObjectValue $focus "role"
        packet_order = Get-ObjectValue $focus "packet_order"
        instruction = $instruction
        instruction_delta_from_first = if ($null -ne $instruction -and $null -ne $firstInstruction) { $instruction - $firstInstruction } else { "" }
        state_hash = Get-ObjectValue $focus "state_hash"
        state_hash_match_focus = Get-ObjectValue $focus "state_hash_match"
        small_data_hash = Get-ObjectValue $focus "small_data_hash"
        small_data_hash_match_focus = Get-ObjectValue $focus "small_data_hash_match"
        object = Get-ObjectValue $focus "object"
        object_kind = Get-ObjectValue $focus "object_kind"
        object_xyz = Get-ObjectValue $focus "object_xyz"
        matrix_translation = Get-ObjectValue $focus "matrix_translation"
        matrix_instruction = Get-ObjectValue $focus "matrix_instruction"
        packet_bound_radius = Get-ObjectValue $focus "packet_bound_radius"
        radius_rank_all = Get-ObjectValue $focus "radius_rank_all"
        radius_rank_within_kind = Get-ObjectValue $focus "radius_rank_within_kind"
        mapped_draw_start = Get-ObjectValue $focus "mapped_draw_start"
        mapped_draw_end = Get-ObjectValue $focus "mapped_draw_end"
        mapped_draw_count = Get-ObjectValue $focus "mapped_draw_count"
        decoded_vertices = Get-ObjectValue $focus "decoded_vertices"
        clipped_vertices = Get-ObjectValue $focus "clipped_vertices"
        screen_bounds = Get-ObjectValue $focus "screen_bounds"
        nearest_display_copy_index = Get-ObjectValue $phase "nearest_display_copy_index"
        nearest_display_copy_draws_seen = Get-ObjectValue $phase "nearest_display_copy_draws_seen"
        nearest_display_copy_relation = Get-ObjectValue $phase "nearest_display_copy_relation"
        selected_copy_index = Get-ObjectValue $phase "selected_copy_index"
        selected_copy_draws_seen = Get-ObjectValue $phase "selected_copy_draws_seen"
        selected_copy_relation = Get-ObjectValue $phase "selected_copy_relation"
        previous_display_copy_index = Get-ObjectValue $copy "previous_display_copy_index"
        previous_display_copy_draws_seen = Get-ObjectValue $copy "previous_display_copy_draws_seen"
        next_display_copy_index = Get-ObjectValue $copy "next_display_copy_index"
        next_display_copy_draws_seen = Get-ObjectValue $copy "next_display_copy_draws_seen"
        next_copy_delta_from_packet_end = Get-ObjectValue $copy "next_copy_delta_from_packet_end"
        next_copy_includes_packet = Format-OptionalBool (Get-ObjectValue $copy "next_copy_includes_packet")
        next_copy_display_nonblack = Get-ObjectValue $copy "next_copy_display_nonblack"
        coverage_draw_count = Get-ObjectValue $phase "coverage_draw_count"
        first_color_draw = Get-ObjectValue $phase "first_color_draw"
        last_color_draw = Get-ObjectValue $phase "last_color_draw"
        total_color_writes = Get-ObjectValue $phase "total_color_writes"
        total_black_writes = Get-ObjectValue $phase "total_black_writes"
        black_write_ratio = Format-Double $blackRatio
        max_after_nonblack = Get-ObjectValue $phase "max_after_nonblack"
        max_after_nonblack_draw = Get-ObjectValue $phase "max_after_nonblack_draw"
        material_sequence_count = $materialRows.Count
        no_rendered_sequence_count = $noRenderedCount
        material_signature = $materialSignature
        dark_material_summary = $darkMaterialSummary
        lit_material_summary = $litMaterialSummary
        no_rendered_summary = $nonRenderedSummary
        matrix_source_matrix = Get-ObjectValue $matrixSummary "source_matrix"
        matrix_output_matrix = Get-ObjectValue $matrixSummary "output_matrix"
        matrix_nearest_source_pc = Get-ObjectValue $matrixSummary "nearest_source_pc"
        matrix_nearest_source_lr = Get-ObjectValue $matrixSummary "nearest_source_lr"
        matrix_nearest_source_delta = Get-ObjectValue $matrixSummary "nearest_source_delta"
        matrix_nearest_source_translation = Get-ObjectValue $matrixSummary "nearest_source_translation"
        matrix_focus_source_translation = Get-ObjectValue $matrixSummary "focus_source_translation"
        recurrence_class = Get-RecurrenceClass $phase $copy
        phase_conclusion = Get-ObjectValue $phase "phase_conclusion"
    })
    $index++
}

$stateGroups = @($eventRows | Group-Object state_hash, small_data_hash | ForEach-Object {
    $rows = @($_.Group)
    [pscustomobject][ordered]@{
        state_small_data_key = $_.Name
        recurrence_count = $rows.Count
        first_recurrence_index = ($rows | Select-Object -First 1).recurrence_index
        last_recurrence_index = ($rows | Select-Object -Last 1).recurrence_index
        first_instruction = ($rows | Select-Object -First 1).instruction
        last_instruction = ($rows | Select-Object -Last 1).instruction
        recurrence_classes = Join-Unique ($rows | Select-Object -ExpandProperty recurrence_class)
        matrix_translations = Join-Unique ($rows | Select-Object -ExpandProperty matrix_translation)
        total_color_writes = Join-Unique ($rows | Select-Object -ExpandProperty total_color_writes)
        black_write_ratios = Join-Unique ($rows | Select-Object -ExpandProperty black_write_ratio)
    }
})

$eventsCsv = Join-Path $OutputDirectory "packet-recurrence-events.csv"
$stateSummaryCsv = Join-Path $OutputDirectory "packet-recurrence-state-summary.csv"
$summaryJson = Join-Path $OutputDirectory "packet-recurrence-report.json"

$eventRows | Export-Csv -LiteralPath $eventsCsv -NoTypeInformation
$stateGroups | Export-Csv -LiteralPath $stateSummaryCsv -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    run_directory = $runRoot
    focus_packet = $focusPacket
    recurrence_count = $eventRows.Count
    unique_state_small_data_count = $stateGroups.Count
    material_sequence_count = $materialRows.Count
    no_rendered_sequence_count = $noRenderedCount
    recurrence_classes = @($eventRows | Group-Object recurrence_class | ForEach-Object {
        [pscustomobject][ordered]@{
            recurrence_class = $_.Name
            count = $_.Count
        }
    })
    events_csv = $eventsCsv
    state_summary_csv = $stateSummaryCsv
    material_signature = $materialSignature
    dark_material_summary = $darkMaterialSummary
    lit_material_summary = $litMaterialSummary
    no_rendered_summary = $nonRenderedSummary
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryJson
$summary | ConvertTo-Json -Depth 6
