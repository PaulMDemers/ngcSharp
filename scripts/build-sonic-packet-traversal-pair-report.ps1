param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,

    [string]$OutputDirectory = "",

    [string]$FocusPacket = "0x813184D0",

    [string]$NeighborPacket = "0x81318FC8"
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

function Join-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Join-Values {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " ")
}

function New-DispatchSummary {
    param(
        [object[]]$Rows,
        [int]$OccurrenceIndex
    )

    $orderedRows = @($Rows | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") })
    $first = $orderedRows | Select-Object -First 1
    $renderer = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "renderer_entry" } | Select-Object -Last 1
    if ($null -eq $renderer) {
        $renderer = $orderedRows | Select-Object -Last 1
    }

    $callback = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "callback_dispatch_call" } | Select-Object -Last 1
    $primary = $orderedRows | Where-Object { [string](Get-ObjectValue $_ "phase") -eq "primary_wrapper_call" } | Select-Object -Last 1

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
        callback_lr = Get-ObjectValue $callback "lr"
        callback_ctr = Get-ObjectValue $callback "ctr"
        primary_lr = Get-ObjectValue $primary "lr"
        primary_ctr = Get-ObjectValue $primary "ctr"
        phase_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "phase" })
        pc_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "pc" })
        packet_source_sequence = Join-Values ($orderedRows | ForEach-Object { Get-ObjectValue $_ "packet_source" })
        lr_set = Join-Unique ($orderedRows | ForEach-Object { Get-ObjectValue $_ "lr" })
        ctr_set = Join-Unique ($orderedRows | ForEach-Object { Get-ObjectValue $_ "ctr" })
        object_xyz = "{0}/{1}/{2}" -f (Get-ObjectValue $renderer "object_x"), (Get-ObjectValue $renderer "object_y"), (Get-ObjectValue $renderer "object_z")
        packet_bound_xyz = "{0}/{1}/{2}" -f (Get-ObjectValue $renderer "packet_bound_x"), (Get-ObjectValue $renderer "packet_bound_y"), (Get-ObjectValue $renderer "packet_bound_z")
        packet_bound_radius = Get-ObjectValue $renderer "packet_bound_radius"
        stream0 = Get-ObjectValue $renderer "stream0"
        stream1 = Get-ObjectValue $renderer "stream1"
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
    $OutputDirectory = Join-Path $runRoot "sonic-packet-traversal-pair"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$selectionPath = Join-Path $runRoot "sonic-packet-selection.csv"
if (-not (Test-CsvHasRows $selectionPath)) {
    throw "Sonic packet-selection CSV not found or empty: $selectionPath"
}

$focusPacketNormalized = Normalize-Hex $FocusPacket
$neighborPacketNormalized = Normalize-Hex $NeighborPacket
$rows = @(Import-Csv -LiteralPath $selectionPath | Sort-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") })

$dispatchRows = @()
$currentRows = @()
$occurrence = 0
foreach ($row in $rows) {
    $packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
    $phase = [string](Get-ObjectValue $row "phase")
    if ($packet -ne $focusPacketNormalized -and $packet -ne $neighborPacketNormalized) {
        continue
    }

    if ($phase -eq "callback_wrapper_entry" -and $currentRows.Count -gt 0) {
        $dispatchRows += New-DispatchSummary $currentRows $occurrence
        $occurrence++
        $currentRows = @()
    }

    $currentRows += $row
    if ($phase -eq "renderer_entry") {
        $dispatchRows += New-DispatchSummary $currentRows $occurrence
        $occurrence++
        $currentRows = @()
    }
}

if ($currentRows.Count -gt 0) {
    $dispatchRows += New-DispatchSummary $currentRows $occurrence
}

$pairRows = @()
$focusDispatches = @($dispatchRows | Where-Object { $_.packet -eq $focusPacketNormalized })
$neighborDispatches = @($dispatchRows | Where-Object { $_.packet -eq $neighborPacketNormalized })
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
    $pairRows += [pscustomobject][ordered]@{
        pair_index = $index
        focus_packet = $focus.packet
        focus_object = $focus.object
        focus_renderer_instruction = $focus.renderer_instruction
        focus_callback_ctr = $focus.callback_ctr
        focus_phase_sequence = $focus.phase_sequence
        focus_object_xyz = $focus.object_xyz
        focus_packet_bound_radius = $focus.packet_bound_radius
        neighbor_packet = Get-ObjectValue $neighbor "packet"
        neighbor_object = Get-ObjectValue $neighbor "object"
        neighbor_renderer_instruction = Get-ObjectValue $neighbor "renderer_instruction"
        neighbor_callback_ctr = Get-ObjectValue $neighbor "callback_ctr"
        neighbor_phase_sequence = Get-ObjectValue $neighbor "phase_sequence"
        neighbor_object_xyz = Get-ObjectValue $neighbor "object_xyz"
        neighbor_packet_bound_radius = Get-ObjectValue $neighbor "packet_bound_radius"
        renderer_instruction_delta = if ($null -ne $focusRendererInstruction -and $null -ne $neighborRendererInstruction) { $neighborRendererInstruction - $focusRendererInstruction } else { "" }
        same_callback_target = ($focus.callback_ctr -eq (Get-ObjectValue $neighbor "callback_ctr"))
        same_phase_sequence = ($focus.phase_sequence -eq (Get-ObjectValue $neighbor "phase_sequence"))
    }
}

$dispatchCsvPath = Join-Path $outputRoot "packet-dispatches.csv"
$pairCsvPath = Join-Path $outputRoot "packet-pairs.csv"
$summaryJsonPath = Join-Path $outputRoot "packet-traversal-pair-report.json"

$dispatchRows | Export-Csv -NoTypeInformation -LiteralPath $dispatchCsvPath
$pairRows | Export-Csv -NoTypeInformation -LiteralPath $pairCsvPath

$summary = [pscustomobject][ordered]@{
    runDirectory = $runRoot
    outputDirectory = $outputRoot
    focusPacket = $focusPacketNormalized
    neighborPacket = $neighborPacketNormalized
    dispatchCount = $dispatchRows.Count
    focusDispatchCount = $focusDispatches.Count
    neighborDispatchCount = $neighborDispatches.Count
    pairCount = $pairRows.Count
    uniqueFocusCallbackTargets = @(($focusDispatches | ForEach-Object { $_.callback_ctr }) | Sort-Object -Unique)
    uniqueNeighborCallbackTargets = @(($neighborDispatches | ForEach-Object { $_.callback_ctr }) | Sort-Object -Unique)
    outputFiles = [ordered]@{
        dispatchesCsv = $dispatchCsvPath
        pairsCsv = $pairCsvPath
        reportJson = $summaryJsonPath
    }
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

Write-Host "Wrote Sonic packet traversal pair report to $outputRoot"
