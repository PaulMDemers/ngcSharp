param(
    [string]$RunDirectory = "",
    [string]$SceneStateCsvPath = "",
    [Parameter(Mandatory = $true)]
    [string[]]$TraceCsvPath,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [string[]]$Targets = @(
        "state-counter:0x801CC1E0:0x4",
        "state-word-e4:0x801CC1E4:0x4",
        "state-float:0x801CC244:0x4",
        "small-data-ptr:0x803ADC84:0x4",
        "small-data-word-88:0x803ADC88:0x4",
        "small-data-timer:0x803ADC94:0x4",
        "small-data-flag:0x803ADCE4:0x4"
    )
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

function Convert-ToUInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [uint64]0
    }

    $trimmed = ([string]$Value).Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [int64]0
    }

    return [int64]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hex32 {
    param([uint64]$Value)

    return "0x{0:X8}" -f ([uint32]($Value -band 0xFFFFFFFF))
}

function Normalize-Hex {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return Format-Hex32 (Convert-ToUInt64 $Value)
}

function Test-RangeOverlap {
    param(
        [uint64]$LeftStart,
        [uint64]$LeftLength,
        [uint64]$RightStart,
        [uint64]$RightLength
    )

    if ($LeftLength -eq 0 -or $RightLength -eq 0) {
        return $false
    }

    $leftEnd = $LeftStart + $LeftLength - 1
    $rightEnd = $RightStart + $RightLength - 1
    return $LeftStart -le $rightEnd -and $RightStart -le $leftEnd
}

function Convert-ToTarget {
    param([string]$Spec)

    $parts = $Spec.Split(":")
    if ($parts.Count -ne 3) {
        throw "Invalid target spec '$Spec'. Expected name:address:length."
    }

    $address = Convert-ToUInt64 $parts[1]
    $length = Convert-ToUInt64 $parts[2]
    if ($length -eq 0) {
        throw "Invalid target spec '$Spec'. Length must be positive."
    }

    [pscustomobject][ordered]@{
        name = $parts[0]
        address = $address
        length = $length
        end = $address + $length - 1
        address_hex = Format-Hex32 $address
        range = "{0}-0x{1:X8}" -f (Format-Hex32 $address), ([uint32](($address + $length - 1) -band 0xFFFFFFFF))
    }
}

function Test-CsvHasDataRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return @((Get-Content -LiteralPath $Path -TotalCount 2)).Count -gt 1
}

function Convert-HexToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return ,([byte[]]@())
    }

    $clean = $Hex -replace '[^0-9A-Fa-f]', ''
    if ($clean.Length % 2 -ne 0) {
        $clean = $clean.Substring(0, $clean.Length - 1)
    }

    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [byte]::Parse($clean.Substring($i * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ,$bytes
}

function Get-TraceTargetValue {
    param(
        [object]$Row,
        [object]$Target
    )

    $traceAddressText = [string](Get-ObjectValue $Row "trace_address")
    $traceLengthText = [string](Get-ObjectValue $Row "trace_length")
    if ([string]::IsNullOrWhiteSpace($traceAddressText) -or [string]::IsNullOrWhiteSpace($traceLengthText)) {
        return ""
    }

    $traceAddress = Convert-ToUInt64 $traceAddressText
    $traceLength = Convert-ToUInt64 $traceLengthText
    if ($traceLength -eq 0 -or $Target.address -lt $traceAddress) {
        return ""
    }

    $rangeBytes = Convert-HexToBytes ([string](Get-ObjectValue $Row "range_bytes"))
    $capturedLength = [uint64]$rangeBytes.Length
    $targetOffset = $Target.address - $traceAddress
    if ($targetOffset + $Target.length -gt $capturedLength -or $targetOffset + $Target.length -gt $traceLength) {
        return ""
    }

    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt [int]$Target.length; $i++) {
        $parts.Add("{0:X2}" -f [int]$rangeBytes[[int]($targetOffset + [uint64]$i)])
    }

    return "0x" + ($parts -join "")
}

if (-not [string]::IsNullOrWhiteSpace($RunDirectory)) {
    $runRoot = Resolve-FullPath $RunDirectory
    if (-not (Test-Path -LiteralPath $runRoot)) {
        throw "Run directory not found: $runRoot"
    }

    if ([string]::IsNullOrWhiteSpace($SceneStateCsvPath)) {
        $SceneStateCsvPath = Join-Path $runRoot "sonic-scene-state.csv"
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $runRoot "sonic-scene-writers"
    }
} else {
    $runRoot = ""
}

if ([string]::IsNullOrWhiteSpace($SceneStateCsvPath)) {
    throw "SceneStateCsvPath or RunDirectory is required."
}

$sceneStatePath = Resolve-FullPath $SceneStateCsvPath
if (-not (Test-CsvHasDataRows $sceneStatePath)) {
    throw "Required scene-state CSV missing or empty: $sceneStatePath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $sceneStatePath) "sonic-scene-writers"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$focusPacket = Normalize-Hex $FocusPacket
$targetRows = @($Targets | ForEach-Object { Convert-ToTarget $_ })

$sceneRows = @(
    Import-Csv -LiteralPath $sceneStatePath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
        Sort-Object @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "instruction") } }
)
if ($sceneRows.Count -eq 0) {
    throw "No scene-state rows found for focus packet $focusPacket in $sceneStatePath"
}

$focusEvents = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $sceneRows.Count; $i++) {
    $row = $sceneRows[$i]
    $focusEvents.Add([pscustomobject][ordered]@{
        event_index = $i
        instruction = Convert-ToInt64 (Get-ObjectValue $row "instruction")
        instruction_text = [string](Get-ObjectValue $row "instruction")
        packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
        object = Normalize-Hex ([string](Get-ObjectValue $row "object"))
        pc = Normalize-Hex ([string](Get-ObjectValue $row "pc"))
        lr = Normalize-Hex ([string](Get-ObjectValue $row "lr"))
        state_base = Normalize-Hex ([string](Get-ObjectValue $row "state_base"))
        small_data_state_address = Normalize-Hex ([string](Get-ObjectValue $row "small_data_state_address"))
        state_word80 = [string](Get-ObjectValue $row "state_word80")
        state_word_ec = [string](Get-ObjectValue $row "state_word_ec")
        small_data_state = [string](Get-ObjectValue $row "small_data_state")
        current_matrix_pointer = Normalize-Hex ([string](Get-ObjectValue $row "current_matrix_pointer"))
        previous_matrix_pointer = Normalize-Hex ([string](Get-ObjectValue $row "previous_matrix_pointer"))
    })
}

$normalizedWriteRows = New-Object System.Collections.Generic.List[object]
$snapshotRows = New-Object System.Collections.Generic.List[object]
$resolvedTracePaths = New-Object System.Collections.Generic.List[string]
$expandedTraceCsvPaths = New-Object System.Collections.Generic.List[string]
foreach ($tracePathInput in $TraceCsvPath) {
    foreach ($tracePathPart in ([string]$tracePathInput -split '[,;]')) {
        if (-not [string]::IsNullOrWhiteSpace($tracePathPart)) {
            $expandedTraceCsvPaths.Add($tracePathPart.Trim())
        }
    }
}

foreach ($tracePathInput in $expandedTraceCsvPaths) {
    $tracePath = Resolve-FullPath $tracePathInput
    if (-not (Test-CsvHasDataRows $tracePath)) {
        Write-Warning "Skipping missing or empty trace CSV: $tracePath"
        continue
    }

    $resolvedTracePaths.Add($tracePath)
    $traceRows = @(Import-Csv -LiteralPath $tracePath)
    $lastSnapshotByTarget = @{}
    foreach ($row in $traceRows) {
        $writeAddress = Convert-ToUInt64 (Get-ObjectValue $row "address")
        $width = Convert-ToUInt64 (Get-ObjectValue $row "width")
        if ($width -eq 0) {
            $width = 1
        }

        $instruction = Convert-ToInt64 (Get-ObjectValue $row "instruction")
        foreach ($target in $targetRows) {
            $targetValue = Get-TraceTargetValue $row $target
            if (-not [string]::IsNullOrWhiteSpace($targetValue)) {
                $previousTargetValue = if ($lastSnapshotByTarget.ContainsKey($target.name)) { [string]$lastSnapshotByTarget[$target.name] } else { "" }
                $targetValueChanged = if ([string]::IsNullOrWhiteSpace($previousTargetValue)) { "False" } elseif ($previousTargetValue -ne $targetValue) { "True" } else { "False" }
                $lastSnapshotByTarget[$target.name] = $targetValue
                $snapshotRows.Add([pscustomobject][ordered]@{
                    target = $target.name
                    target_range = $target.range
                    instruction = $instruction
                    pc = Normalize-Hex ([string](Get-ObjectValue $row "pc"))
                    opcode = Normalize-Hex ([string](Get-ObjectValue $row "opcode"))
                    disassembly = [string](Get-ObjectValue $row "disassembly")
                    kind = [string](Get-ObjectValue $row "kind")
                    width = [string](Get-ObjectValue $row "width")
                    write_address = Format-Hex32 $writeAddress
                    write_address_end = Format-Hex32 ($writeAddress + $width - 1)
                    write_overlaps_target = if (Test-RangeOverlap $writeAddress $width $target.address $target.length) { "True" } else { "False" }
                    write_value = [string](Get-ObjectValue $row "value")
                    target_value = $targetValue
                    previous_target_value = $previousTargetValue
                    target_value_changed = $targetValueChanged
                    r1 = [string](Get-ObjectValue $row "r1")
                    r3 = [string](Get-ObjectValue $row "r3")
                    r4 = [string](Get-ObjectValue $row "r4")
                    r5 = [string](Get-ObjectValue $row "r5")
                    r6 = [string](Get-ObjectValue $row "r6")
                    r13 = [string](Get-ObjectValue $row "r13")
                    r29 = [string](Get-ObjectValue $row "r29")
                    r30 = [string](Get-ObjectValue $row "r30")
                    r31 = [string](Get-ObjectValue $row "r31")
                    trace_address = Normalize-Hex ([string](Get-ObjectValue $row "trace_address"))
                    trace_length = [string](Get-ObjectValue $row "trace_length")
                    source_trace = $tracePath
                })
            }

            if (-not (Test-RangeOverlap $writeAddress $width $target.address $target.length)) {
                continue
            }

            $normalizedWriteRows.Add([pscustomobject][ordered]@{
                target = $target.name
                target_range = $target.range
                instruction = $instruction
                pc = Normalize-Hex ([string](Get-ObjectValue $row "pc"))
                opcode = Normalize-Hex ([string](Get-ObjectValue $row "opcode"))
                disassembly = [string](Get-ObjectValue $row "disassembly")
                kind = [string](Get-ObjectValue $row "kind")
                width = [string](Get-ObjectValue $row "width")
                address = Format-Hex32 $writeAddress
                address_end = Format-Hex32 ($writeAddress + $width - 1)
                value = [string](Get-ObjectValue $row "value")
                r1 = [string](Get-ObjectValue $row "r1")
                r3 = [string](Get-ObjectValue $row "r3")
                r4 = [string](Get-ObjectValue $row "r4")
                r5 = [string](Get-ObjectValue $row "r5")
                r6 = [string](Get-ObjectValue $row "r6")
                r7 = [string](Get-ObjectValue $row "r7")
                r8 = [string](Get-ObjectValue $row "r8")
                r9 = [string](Get-ObjectValue $row "r9")
                r10 = [string](Get-ObjectValue $row "r10")
                r13 = [string](Get-ObjectValue $row "r13")
                r24 = [string](Get-ObjectValue $row "r24")
                r25 = [string](Get-ObjectValue $row "r25")
                r27 = [string](Get-ObjectValue $row "r27")
                r28 = [string](Get-ObjectValue $row "r28")
                r29 = [string](Get-ObjectValue $row "r29")
                r30 = [string](Get-ObjectValue $row "r30")
                r31 = [string](Get-ObjectValue $row "r31")
                trace_address = Normalize-Hex ([string](Get-ObjectValue $row "trace_address"))
                trace_length = [string](Get-ObjectValue $row "trace_length")
                range_bytes_prefix = ([string](Get-ObjectValue $row "range_bytes")).Substring(0, [Math]::Min(64, ([string](Get-ObjectValue $row "range_bytes")).Length))
                post_write_bytes_prefix = ([string](Get-ObjectValue $row "post_write_bytes")).Substring(0, [Math]::Min(64, ([string](Get-ObjectValue $row "post_write_bytes")).Length))
                source_trace = $tracePath
            })
        }
    }
}

$orderedWrites = @($normalizedWriteRows | Sort-Object @{ Expression = { [int64]$_.instruction } }, target, pc)
$orderedSnapshots = @($snapshotRows | Sort-Object @{ Expression = { [int64]$_.instruction } }, target, pc)
$changedSnapshots = @($orderedSnapshots | Where-Object { $_.target_value_changed -eq "True" })

$targetNames = @($targetRows | ForEach-Object { $_.name })
$writesByTarget = @{}
foreach ($targetName in $targetNames) {
    $writesByTarget[$targetName] = @($orderedWrites | Where-Object { $_.target -eq $targetName } | Sort-Object @{ Expression = { [int64]$_.instruction } })
}

$snapshotsByTarget = @{}
$changedSnapshotsByTarget = @{}
foreach ($targetName in $targetNames) {
    $snapshotsByTarget[$targetName] = @($orderedSnapshots | Where-Object { $_.target -eq $targetName } | Sort-Object @{ Expression = { [int64]$_.instruction } })
    $changedSnapshotsByTarget[$targetName] = @($changedSnapshots | Where-Object { $_.target -eq $targetName } | Sort-Object @{ Expression = { [int64]$_.instruction } })
}

$eventWriterRows = New-Object System.Collections.Generic.List[object]
foreach ($event in $focusEvents) {
    foreach ($targetName in $targetNames) {
        $lastWrite = $null
        foreach ($candidate in $writesByTarget[$targetName]) {
            if ([int64]$candidate.instruction -lt [int64]$event.instruction) {
                $lastWrite = $candidate
                continue
            }

            break
        }

        $lastSnapshot = $null
        foreach ($candidate in $snapshotsByTarget[$targetName]) {
            if ([int64]$candidate.instruction -lt [int64]$event.instruction) {
                $lastSnapshot = $candidate
                continue
            }

            break
        }

        $lastChange = $null
        foreach ($candidate in $changedSnapshotsByTarget[$targetName]) {
            if ([int64]$candidate.instruction -lt [int64]$event.instruction) {
                $lastChange = $candidate
                continue
            }

            break
        }

        if ($null -eq $lastWrite) {
            $eventWriterRows.Add([pscustomobject][ordered]@{
                event_index = $event.event_index
                event_instruction = $event.instruction
                target = $targetName
                last_write_instruction = ""
                delta_instructions = ""
                pc = ""
                opcode = ""
                disassembly = ""
                kind = ""
                width = ""
                write_address = ""
                write_address_end = ""
                value = ""
                r1 = ""
                r3 = ""
                r4 = ""
                r5 = ""
                r6 = ""
                r7 = ""
                r8 = ""
                r9 = ""
                r10 = ""
                r13 = ""
                r24 = ""
                r25 = ""
                r27 = ""
                r28 = ""
                r29 = ""
                r30 = ""
                r31 = ""
                trace_address = ""
                trace_length = ""
                last_snapshot_instruction = if ($null -eq $lastSnapshot) { "" } else { $lastSnapshot.instruction }
                last_snapshot_value = if ($null -eq $lastSnapshot) { "" } else { $lastSnapshot.target_value }
                last_snapshot_delta_instructions = if ($null -eq $lastSnapshot) { "" } else { ([int64]$event.instruction - [int64]$lastSnapshot.instruction) }
                last_change_instruction = if ($null -eq $lastChange) { "" } else { $lastChange.instruction }
                last_change_value = if ($null -eq $lastChange) { "" } else { $lastChange.target_value }
                last_change_delta_instructions = if ($null -eq $lastChange) { "" } else { ([int64]$event.instruction - [int64]$lastChange.instruction) }
                last_change_pc = if ($null -eq $lastChange) { "" } else { $lastChange.pc }
                last_change_disassembly = if ($null -eq $lastChange) { "" } else { $lastChange.disassembly }
                last_change_write_address = if ($null -eq $lastChange) { "" } else { $lastChange.write_address }
                last_change_write_overlaps_target = if ($null -eq $lastChange) { "" } else { $lastChange.write_overlaps_target }
                source_trace = ""
            })
            continue
        }

        $eventWriterRows.Add([pscustomobject][ordered]@{
            event_index = $event.event_index
            event_instruction = $event.instruction
            target = $targetName
            last_write_instruction = $lastWrite.instruction
            delta_instructions = ([int64]$event.instruction - [int64]$lastWrite.instruction)
            pc = $lastWrite.pc
            opcode = $lastWrite.opcode
            disassembly = $lastWrite.disassembly
            kind = $lastWrite.kind
            width = $lastWrite.width
            write_address = $lastWrite.address
            write_address_end = $lastWrite.address_end
            value = $lastWrite.value
            r1 = $lastWrite.r1
            r3 = $lastWrite.r3
            r4 = $lastWrite.r4
            r5 = $lastWrite.r5
            r6 = $lastWrite.r6
            r7 = $lastWrite.r7
            r8 = $lastWrite.r8
            r9 = $lastWrite.r9
            r10 = $lastWrite.r10
            r13 = $lastWrite.r13
            r24 = $lastWrite.r24
            r25 = $lastWrite.r25
            r27 = $lastWrite.r27
            r28 = $lastWrite.r28
            r29 = $lastWrite.r29
            r30 = $lastWrite.r30
            r31 = $lastWrite.r31
            trace_address = $lastWrite.trace_address
            trace_length = $lastWrite.trace_length
            last_snapshot_instruction = if ($null -eq $lastSnapshot) { "" } else { $lastSnapshot.instruction }
            last_snapshot_value = if ($null -eq $lastSnapshot) { "" } else { $lastSnapshot.target_value }
            last_snapshot_delta_instructions = if ($null -eq $lastSnapshot) { "" } else { ([int64]$event.instruction - [int64]$lastSnapshot.instruction) }
            last_change_instruction = if ($null -eq $lastChange) { "" } else { $lastChange.instruction }
            last_change_value = if ($null -eq $lastChange) { "" } else { $lastChange.target_value }
            last_change_delta_instructions = if ($null -eq $lastChange) { "" } else { ([int64]$event.instruction - [int64]$lastChange.instruction) }
            last_change_pc = if ($null -eq $lastChange) { "" } else { $lastChange.pc }
            last_change_disassembly = if ($null -eq $lastChange) { "" } else { $lastChange.disassembly }
            last_change_write_address = if ($null -eq $lastChange) { "" } else { $lastChange.write_address }
            last_change_write_overlaps_target = if ($null -eq $lastChange) { "" } else { $lastChange.write_overlaps_target }
            source_trace = $lastWrite.source_trace
        })
    }
}

$pcSummaryRows = New-Object System.Collections.Generic.List[object]
$groups = $orderedWrites | Group-Object target, pc, disassembly, kind
foreach ($group in $groups) {
    $rows = @($group.Group | Sort-Object @{ Expression = { [int64]$_.instruction } })
    $minAddress = [uint64]::MaxValue
    $maxAddress = [uint64]0
    foreach ($row in $rows) {
        $start = Convert-ToUInt64 $row.address
        $end = Convert-ToUInt64 $row.address_end
        if ($start -lt $minAddress) {
            $minAddress = $start
        }
        if ($end -gt $maxAddress) {
            $maxAddress = $end
        }
    }

    $first = $rows[0]
    $last = $rows[-1]
    $addressRange = ""
    if ($rows.Count -gt 0) {
        $addressRange = "{0}-{1}" -f (Format-Hex32 $minAddress), (Format-Hex32 $maxAddress)
    }

    $pcSummaryRows.Add([pscustomobject][ordered]@{
        target = $first.target
        pc = $first.pc
        disassembly = $first.disassembly
        kind = $first.kind
        count = $rows.Count
        first_instruction = $first.instruction
        last_instruction = $last.instruction
        address_range = $addressRange
        first_value = $first.value
        last_value = $last.value
        r3 = $last.r3
        r4 = $last.r4
        r5 = $last.r5
        r6 = $last.r6
        r13 = $last.r13
        r29 = $last.r29
        r30 = $last.r30
        source_traces = (($rows | Select-Object -ExpandProperty source_trace -Unique) -join ";")
    })
}

$eventsCsvPath = Join-Path $OutputDirectory "scene-writer-focus-events.csv"
$writesCsvPath = Join-Path $OutputDirectory "scene-writer-events.csv"
$writerToFocusCsvPath = Join-Path $OutputDirectory "scene-writer-to-focus-events.csv"
$targetSnapshotsCsvPath = Join-Path $OutputDirectory "scene-writer-target-snapshots.csv"
$targetChangesCsvPath = Join-Path $OutputDirectory "scene-writer-target-changes.csv"
$pcSummaryCsvPath = Join-Path $OutputDirectory "scene-writer-pc-summary.csv"
$summaryCsvPath = Join-Path $OutputDirectory "scene-writer-summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "scene-writer-report.json"

$focusEvents | Export-Csv -LiteralPath $eventsCsvPath -NoTypeInformation
$orderedWrites | Export-Csv -LiteralPath $writesCsvPath -NoTypeInformation
$orderedSnapshots | Export-Csv -LiteralPath $targetSnapshotsCsvPath -NoTypeInformation
$changedSnapshots | Export-Csv -LiteralPath $targetChangesCsvPath -NoTypeInformation
$eventWriterRows | Export-Csv -LiteralPath $writerToFocusCsvPath -NoTypeInformation
$pcSummaryRows | Sort-Object target, @{ Expression = { -[int]$_.count } }, pc | Export-Csv -LiteralPath $pcSummaryCsvPath -NoTypeInformation

$targetSummaryRows = foreach ($target in $targetRows) {
    $targetWrites = @($orderedWrites | Where-Object { $_.target -eq $target.name })
    $targetSnapshots = @($orderedSnapshots | Where-Object { $_.target -eq $target.name })
    $targetChanges = @($changedSnapshots | Where-Object { $_.target -eq $target.name })
    $targetLastRows = @($eventWriterRows | Where-Object { $_.target -eq $target.name -and -not [string]::IsNullOrWhiteSpace([string]$_.last_write_instruction) })
    [pscustomobject][ordered]@{
        target = $target.name
        target_range = $target.range
        focus_event_count = $focusEvents.Count
        write_count = $targetWrites.Count
        snapshot_count = $targetSnapshots.Count
        snapshot_change_count = $targetChanges.Count
        writer_pc_count = @($targetWrites | Select-Object -ExpandProperty pc -Unique).Count
        last_writer_pc_count = @($targetLastRows | Select-Object -ExpandProperty pc -Unique).Count
        first_write_instruction = if ($targetWrites.Count -gt 0) { $targetWrites[0].instruction } else { "" }
        last_write_instruction = if ($targetWrites.Count -gt 0) { $targetWrites[-1].instruction } else { "" }
        first_snapshot_instruction = if ($targetSnapshots.Count -gt 0) { $targetSnapshots[0].instruction } else { "" }
        last_snapshot_instruction = if ($targetSnapshots.Count -gt 0) { $targetSnapshots[-1].instruction } else { "" }
        last_snapshot_value = if ($targetSnapshots.Count -gt 0) { $targetSnapshots[-1].target_value } else { "" }
        last_change_instruction = if ($targetChanges.Count -gt 0) { $targetChanges[-1].instruction } else { "" }
        last_change_value = if ($targetChanges.Count -gt 0) { $targetChanges[-1].target_value } else { "" }
        last_change_pcs = (($targetChanges | Select-Object -ExpandProperty pc -Unique) -join " ")
        last_writer_pcs = (($targetLastRows | Select-Object -ExpandProperty pc -Unique) -join " ")
    }
}
$targetSummaryRows | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation

[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-scene-writer-events.v1"
    generatedAt = (Get-Date).ToString("o")
    runDirectory = $runRoot
    focusPacket = $focusPacket
    inputs = [ordered]@{
        sceneStateCsv = $sceneStatePath
        traceCsvs = @($resolvedTracePaths)
    }
    outputs = [ordered]@{
        focusEventsCsv = $eventsCsvPath
        writerEventsCsv = $writesCsvPath
        targetSnapshotsCsv = $targetSnapshotsCsvPath
        targetChangesCsv = $targetChangesCsvPath
        writerToFocusEventsCsv = $writerToFocusCsvPath
        pcSummaryCsv = $pcSummaryCsvPath
        summaryCsv = $summaryCsvPath
    }
    targetSummary = $targetSummaryRows
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic scene writer events: $writesCsvPath"
Write-Host "Wrote Sonic scene writer target snapshots: $targetSnapshotsCsvPath"
Write-Host "Wrote Sonic scene writer-to-focus join: $writerToFocusCsvPath"
Write-Host "Wrote Sonic scene writer PC summary: $pcSummaryCsvPath"
