param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,

    [string]$OutputDirectory = "",

    [string]$FocusPacket = "0x813184D0",

    [string]$FocusObject = "0x813184E8",

    [string]$NeighborPacket = "0x81318FC8",

    [string]$NeighborObject = "0x81318FE0"
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

function Convert-HexNeedle {
    param([string]$Value)

    return (Normalize-Hex $Value).Substring(2).ToUpperInvariant()
}

function Get-HexWord {
    param(
        [string]$Hex,
        [int]$ByteOffset
    )

    $charOffset = $ByteOffset * 2
    if ([string]::IsNullOrWhiteSpace($Hex) -or $charOffset -lt 0 -or $charOffset + 8 -gt $Hex.Length) {
        return ""
    }

    return "0x$($Hex.Substring($charOffset, 8).ToUpperInvariant())"
}

function Get-NeighborWords {
    param(
        [string]$Hex,
        [int]$ByteOffset
    )

    $words = @()
    $base = $ByteOffset - ($ByteOffset % 4)
    for ($offset = $base - 8; $offset -le $base + 24; $offset += 4) {
        $word = Get-HexWord $Hex $offset
        if (-not [string]::IsNullOrWhiteSpace($word)) {
            $words += "{0:+0;-0;+0}:$word" -f ($offset - $base)
        }
    }

    return ($words -join " ")
}

function Join-Offsets {
    param([int[]]$Offsets)

    return (@($Offsets | Sort-Object -Unique | ForEach-Object { "0x{0:X}" -f $_ }) -join " ")
}

function Find-NeedleOffsets {
    param(
        [string]$Hex,
        [string]$Needle
    )

    $offsets = @()
    if ([string]::IsNullOrWhiteSpace($Hex) -or [string]::IsNullOrWhiteSpace($Needle)) {
        return $offsets
    }

    $index = $Hex.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase)
    while ($index -ge 0) {
        if (($index % 2) -eq 0) {
            $offsets += [int]($index / 2)
        }

        $index = $Hex.IndexOf($Needle, $index + 2, [System.StringComparison]::OrdinalIgnoreCase)
    }

    return $offsets
}

function New-ReferenceHit {
    param(
        [object]$Row,
        [string]$Column,
        [string]$NeedleName,
        [string]$NeedleValue,
        [int]$ByteOffset
    )

    $hex = [string](Get-ObjectValue $Row $Column)
    $wordBase = $ByteOffset - ($ByteOffset % 4)
    return [pscustomobject][ordered]@{
        instruction = Get-ObjectValue $Row "instruction"
        pc = Get-ObjectValue $Row "pc"
        phase = Get-ObjectValue $Row "phase"
        packet = Normalize-Hex ([string](Get-ObjectValue $Row "packet"))
        packet_kind = Get-ObjectValue $Row "packet_kind"
        object = Normalize-Hex ([string](Get-ObjectValue $Row "object"))
        object_kind = Get-ObjectValue $Row "object_kind"
        packet_source = Get-ObjectValue $Row "packet_source"
        column = $Column
        reference_name = $NeedleName
        reference_value = Normalize-Hex $NeedleValue
        byte_offset = $ByteOffset
        word_offset = $wordBase
        surrounding_words = Get-NeighborWords $hex $ByteOffset
        lr = Get-ObjectValue $Row "lr"
        ctr = Get-ObjectValue $Row "ctr"
        r3 = Get-ObjectValue $Row "r3"
        r4 = Get-ObjectValue $Row "r4"
        r5 = Get-ObjectValue $Row "r5"
        r27 = Get-ObjectValue $Row "r27"
        r29 = Get-ObjectValue $Row "r29"
        r30 = Get-ObjectValue $Row "r30"
        r31 = Get-ObjectValue $Row "r31"
    }
}

function Join-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Join-Values {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " ")
}

function New-DirectDispatchSummary {
    param(
        [object[]]$Rows,
        [int]$OccurrenceIndex,
        [string]$FocusObjectNeedle,
        [string]$NeighborObjectNeedle
    )

    $orderedRows = @($Rows | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") })
    $first = $orderedRows | Select-Object -First 1
    $renderer = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "renderer_entry" } | Select-Object -Last 1
    if ($null -eq $renderer) {
        $renderer = $orderedRows | Select-Object -Last 1
    }

    $dispatch = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "callback_dispatch_call" } | Select-Object -Last 1
    $listRow = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "traversal_callback_entry" } | Select-Object -First 1
    if ($null -eq $listRow) {
        $listRow = $first
    }

    $listBytes = [string](Get-ObjectValue $listRow "r4_bytes")
    return [pscustomobject][ordered]@{
        occurrence_index = $OccurrenceIndex
        packet = Normalize-Hex ([string](Get-ObjectValue $renderer "packet" (Get-ObjectValue $first "packet")))
        packet_kind = Get-ObjectValue $renderer "packet_kind" (Get-ObjectValue $first "packet_kind")
        object = Normalize-Hex ([string](Get-ObjectValue $renderer "object" (Get-ObjectValue $first "object")))
        object_kind = Get-ObjectValue $renderer "object_kind" (Get-ObjectValue $first "object_kind")
        first_instruction = Get-ObjectValue $first "instruction"
        renderer_instruction = Get-ObjectValue $renderer "instruction"
        first_pc = Get-ObjectValue $first "pc"
        renderer_pc = Get-ObjectValue $renderer "pc"
        traversal_target = Get-ObjectValue $first "ctr"
        dispatch_target = Get-ObjectValue $dispatch "ctr"
        list_base = Get-ObjectValue $listRow "r4"
        focus_object_list_offsets = Join-Offsets (Find-NeedleOffsets $listBytes $FocusObjectNeedle)
        neighbor_object_list_offsets = Join-Offsets (Find-NeedleOffsets $listBytes $NeighborObjectNeedle)
        list_reference_words = Get-NeighborWords $listBytes 0
        phase_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "phase" })
        pc_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "pc" })
        r3_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "r3" })
        r4_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "r4" })
        r30_values = Join-Unique ($orderedRows | ForEach-Object { Get-ObjectValue $_ "r30" })
        r31_values = Join-Unique ($orderedRows | ForEach-Object { Get-ObjectValue $_ "r31" })
        row_count = $orderedRows.Count
    }
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-traversal-source-report"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$sourcePath = Join-Path $runRoot "sonic-traversal-source.csv"
if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Sonic traversal-source CSV not found: $sourcePath"
}

$focusPacketNormalized = Normalize-Hex $FocusPacket
$focusObjectNormalized = Normalize-Hex $FocusObject
$neighborPacketNormalized = Normalize-Hex $NeighborPacket
$neighborObjectNormalized = Normalize-Hex $NeighborObject
$needles = @(
    [pscustomobject]@{ name = "focus_packet"; value = $focusPacketNormalized; hex = Convert-HexNeedle $focusPacketNormalized },
    [pscustomobject]@{ name = "focus_object"; value = $focusObjectNormalized; hex = Convert-HexNeedle $focusObjectNormalized },
    [pscustomobject]@{ name = "neighbor_packet"; value = $neighborPacketNormalized; hex = Convert-HexNeedle $neighborPacketNormalized },
    [pscustomobject]@{ name = "neighbor_object"; value = $neighborObjectNormalized; hex = Convert-HexNeedle $neighborObjectNormalized }
)
$memoryColumns = @("r3_bytes", "r4_bytes", "r5_bytes", "r27_bytes", "r29_bytes", "r30_bytes", "r31_bytes", "stack_bytes")

$rows = @(Import-Csv -LiteralPath $sourcePath | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") })
$directRows = @(
    $rows |
        Where-Object {
            (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -in @($focusPacketNormalized, $neighborPacketNormalized) -or
            (Normalize-Hex ([string](Get-ObjectValue $_ "object"))) -in @($focusObjectNormalized, $neighborObjectNormalized)
        } |
        ForEach-Object {
            [pscustomobject][ordered]@{
                instruction = Get-ObjectValue $_ "instruction"
                pc = Get-ObjectValue $_ "pc"
                phase = Get-ObjectValue $_ "phase"
                packet = Normalize-Hex ([string](Get-ObjectValue $_ "packet"))
                packet_kind = Get-ObjectValue $_ "packet_kind"
                object = Normalize-Hex ([string](Get-ObjectValue $_ "object"))
                object_kind = Get-ObjectValue $_ "object_kind"
                packet_source = Get-ObjectValue $_ "packet_source"
                lr = Get-ObjectValue $_ "lr"
                ctr = Get-ObjectValue $_ "ctr"
                r3 = Get-ObjectValue $_ "r3"
                r4 = Get-ObjectValue $_ "r4"
                r5 = Get-ObjectValue $_ "r5"
                r27 = Get-ObjectValue $_ "r27"
                r29 = Get-ObjectValue $_ "r29"
                r30 = Get-ObjectValue $_ "r30"
                r31 = Get-ObjectValue $_ "r31"
                r4_focus_object_offsets = Join-Offsets (Find-NeedleOffsets ([string](Get-ObjectValue $_ "r4_bytes")) (Convert-HexNeedle $focusObjectNormalized))
                r4_neighbor_object_offsets = Join-Offsets (Find-NeedleOffsets ([string](Get-ObjectValue $_ "r4_bytes")) (Convert-HexNeedle $neighborObjectNormalized))
            }
        }
)

$sourceDispatchRows = @()
$currentRows = @()
$occurrence = 0
foreach ($row in $rows) {
    $packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
    $object = Normalize-Hex ([string](Get-ObjectValue $row "object"))
    $phase = [string](Get-ObjectValue $row "phase")
    $matchesFocus = $packet -eq $focusPacketNormalized -or $object -eq $focusObjectNormalized
    $matchesNeighbor = $packet -eq $neighborPacketNormalized -or $object -eq $neighborObjectNormalized
    if (-not $matchesFocus -and -not $matchesNeighbor) {
        continue
    }

    if ($phase -eq "traversal_callback_entry" -and $currentRows.Count -gt 0) {
        $sourceDispatchRows += New-DirectDispatchSummary $currentRows $occurrence (Convert-HexNeedle $focusObjectNormalized) (Convert-HexNeedle $neighborObjectNormalized)
        $occurrence++
        $currentRows = @()
    }

    $currentRows += $row
    if ($phase -eq "renderer_entry") {
        $sourceDispatchRows += New-DirectDispatchSummary $currentRows $occurrence (Convert-HexNeedle $focusObjectNormalized) (Convert-HexNeedle $neighborObjectNormalized)
        $occurrence++
        $currentRows = @()
    }
}

if ($currentRows.Count -gt 0) {
    $sourceDispatchRows += New-DirectDispatchSummary $currentRows $occurrence (Convert-HexNeedle $focusObjectNormalized) (Convert-HexNeedle $neighborObjectNormalized)
}

$sourcePairRows = @()
$focusDispatches = @($sourceDispatchRows | Where-Object { $_.packet -eq $focusPacketNormalized })
$neighborDispatches = @($sourceDispatchRows | Where-Object { $_.packet -eq $neighborPacketNormalized })
for ($index = 0; $index -lt $focusDispatches.Count; $index++) {
    $focus = $focusDispatches[$index]
    $focusRendererInstruction = Convert-ToNullableInt64 $focus.renderer_instruction
    $neighbor = $neighborDispatches |
        Where-Object {
            $candidateInstruction = Convert-ToNullableInt64 $_.renderer_instruction
            $null -ne $focusRendererInstruction -and $null -ne $candidateInstruction -and $candidateInstruction -gt $focusRendererInstruction
        } |
        Sort-Object { Convert-ToNullableInt64 $_.renderer_instruction } |
        Select-Object -First 1

    $neighborRendererInstruction = Convert-ToNullableInt64 (Get-ObjectValue $neighbor "renderer_instruction")
    $sourcePairRows += [pscustomobject][ordered]@{
        pair_index = $index
        focus_packet = $focus.packet
        focus_object = $focus.object
        focus_renderer_instruction = $focus.renderer_instruction
        focus_list_base = $focus.list_base
        focus_object_list_offsets = $focus.focus_object_list_offsets
        focus_neighbor_object_list_offsets = $focus.neighbor_object_list_offsets
        neighbor_packet = Get-ObjectValue $neighbor "packet"
        neighbor_object = Get-ObjectValue $neighbor "object"
        neighbor_renderer_instruction = Get-ObjectValue $neighbor "renderer_instruction"
        neighbor_list_base = Get-ObjectValue $neighbor "list_base"
        neighbor_focus_object_list_offsets = Get-ObjectValue $neighbor "focus_object_list_offsets"
        neighbor_object_list_offsets = Get-ObjectValue $neighbor "neighbor_object_list_offsets"
        renderer_instruction_delta = if ($null -ne $focusRendererInstruction -and $null -ne $neighborRendererInstruction) { $neighborRendererInstruction - $focusRendererInstruction } else { "" }
        same_list_base = ($focus.list_base -eq (Get-ObjectValue $neighbor "list_base"))
        same_traversal_target = ($focus.traversal_target -eq (Get-ObjectValue $neighbor "traversal_target"))
        same_dispatch_target = ($focus.dispatch_target -eq (Get-ObjectValue $neighbor "dispatch_target"))
    }
}

$referenceHits = @()
foreach ($row in $rows) {
    foreach ($column in $memoryColumns) {
        $hex = [string](Get-ObjectValue $row $column)
        if ([string]::IsNullOrWhiteSpace($hex)) {
            continue
        }

        foreach ($needle in $needles) {
            foreach ($offset in (Find-NeedleOffsets $hex $needle.hex)) {
                $referenceHits += New-ReferenceHit $row $column $needle.name $needle.value $offset
            }
        }
    }
}

$parentRows = @(
    $referenceHits |
        Group-Object instruction, pc, phase, packet, object, column |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject][ordered]@{
                instruction = $first.instruction
                pc = $first.pc
                phase = $first.phase
                packet = $first.packet
                packet_kind = $first.packet_kind
                object = $first.object
                object_kind = $first.object_kind
                packet_source = $first.packet_source
                column = $first.column
                reference_names = Join-Unique ($_.Group | ForEach-Object { $_.reference_name })
                reference_values = Join-Unique ($_.Group | ForEach-Object { $_.reference_value })
                byte_offsets = Join-Unique ($_.Group | ForEach-Object { $_.byte_offset })
                surrounding_words = Join-Unique ($_.Group | ForEach-Object { $_.surrounding_words })
                lr = $first.lr
                ctr = $first.ctr
                r3 = $first.r3
                r4 = $first.r4
                r5 = $first.r5
                r27 = $first.r27
                r29 = $first.r29
                r30 = $first.r30
                r31 = $first.r31
                hit_count = $_.Group.Count
            }
        } |
        Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") }, column
)

$directCsvPath = Join-Path $outputRoot "direct-focus-dispatches.csv"
$sourceDispatchCsvPath = Join-Path $outputRoot "source-dispatches.csv"
$sourcePairCsvPath = Join-Path $outputRoot "source-pairs.csv"
$referenceCsvPath = Join-Path $outputRoot "focus-reference-hits.csv"
$parentCsvPath = Join-Path $outputRoot "focus-reference-parents.csv"
$summaryJsonPath = Join-Path $outputRoot "traversal-source-report.json"

$directRows | Export-Csv -NoTypeInformation -LiteralPath $directCsvPath
$sourceDispatchRows | Export-Csv -NoTypeInformation -LiteralPath $sourceDispatchCsvPath
$sourcePairRows | Export-Csv -NoTypeInformation -LiteralPath $sourcePairCsvPath
$referenceHits | Export-Csv -NoTypeInformation -LiteralPath $referenceCsvPath
$parentRows | Export-Csv -NoTypeInformation -LiteralPath $parentCsvPath

$summary = [pscustomobject][ordered]@{
    runDirectory = $runRoot
    outputDirectory = $outputRoot
    focusPacket = $focusPacketNormalized
    focusObject = $focusObjectNormalized
    neighborPacket = $neighborPacketNormalized
    neighborObject = $neighborObjectNormalized
    sourceRowCount = $rows.Count
    directFocusDispatchCount = $directRows.Count
    sourceDispatchCount = $sourceDispatchRows.Count
    sourceFocusDispatchCount = $focusDispatches.Count
    sourceNeighborDispatchCount = $neighborDispatches.Count
    sourcePairCount = $sourcePairRows.Count
    referenceHitCount = $referenceHits.Count
    referenceParentCount = $parentRows.Count
    uniqueSourceListBases = @(($sourceDispatchRows | ForEach-Object { $_.list_base }) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    uniqueReferenceParentPackets = @(($parentRows | ForEach-Object { $_.packet }) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    uniqueReferenceParentObjects = @(($parentRows | ForEach-Object { $_.object }) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    outputFiles = [ordered]@{
        directFocusDispatchesCsv = $directCsvPath
        sourceDispatchesCsv = $sourceDispatchCsvPath
        sourcePairsCsv = $sourcePairCsvPath
        focusReferenceHitsCsv = $referenceCsvPath
        focusReferenceParentsCsv = $parentCsvPath
        reportJson = $summaryJsonPath
    }
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

Write-Host "Wrote Sonic traversal source report to $outputRoot"
