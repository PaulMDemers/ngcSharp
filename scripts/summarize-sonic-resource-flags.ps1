param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$JsonPath = "",
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexUInt32 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [uint32]0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToUInt32($text.Substring(2), 16)
    }

    return [Convert]::ToUInt32($text, 10)
}

function Convert-Int64Value {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [int64]0
    }

    return [int64]$Value
}

function New-SlotSummary {
    param([int]$Slot)

    return [pscustomobject][ordered]@{
        slot = $Slot
        events = 0
        setCount = 0
        clearCount = 0
        redundantSetCount = 0
        redundantClearCount = 0
        firstInstruction = 0
        lastInstruction = 0
        firstSetInstruction = 0
        lastSetInstruction = 0
        firstClearInstruction = 0
        lastClearInstruction = 0
        activeSpans = 0
        longestActiveInstructionSpan = 0
        finalActive = $false
        finalTask = ""
        operations = @()
        producerPcs = @()
        taskPointers = @()
    }
}

function Add-UniqueString {
    param(
        [System.Collections.Generic.HashSet[string]]$Set,
        [string]$Value
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $null = $Set.Add($Value)
    }
}

$traceFullPath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $traceFullPath)) {
    throw "Sonic resource flag trace not found: $traceFullPath"
}

$rows = @(Import-Csv -LiteralPath $traceFullPath)
$slotStates = @{}
$slotOperationSets = @{}
$slotProducerSets = @{}
$slotTaskSets = @{}
$activeSinceBySlot = @{}
$lastFlag = [uint32]0
$flagAddress = ""
$firstInstruction = 0
$lastInstruction = 0

foreach ($row in $rows) {
    $instruction = Convert-Int64Value $row.instruction
    $slot = [int]$row.task_slot
    if ($slot -lt 0) {
        continue
    }

    if (-not $slotStates.ContainsKey($slot)) {
        $slotStates[$slot] = New-SlotSummary $slot
        $slotOperationSets[$slot] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $slotProducerSets[$slot] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $slotTaskSets[$slot] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    }

    $summary = $slotStates[$slot]
    $oldFlag = Convert-HexUInt32 $row.old_flag
    $newFlag = Convert-HexUInt32 $row.new_flag
    $changed = Convert-HexUInt32 $row.xor
    $slotBit = if ($slot -lt 32) { [uint32]([uint32]1 -shl (31 - $slot)) } else { [uint32]0 }
    $oldActive = (($oldFlag -band $slotBit) -ne 0)
    $newActive = (($newFlag -band $slotBit) -ne 0)
    $slotChanged = (($changed -band $slotBit) -ne 0)

    if ($firstInstruction -eq 0) {
        $firstInstruction = $instruction
    }
    $lastInstruction = $instruction
    $lastFlag = $newFlag
    $flagAddress = $row.flag_address

    $summary.events = [int]$summary.events + 1
    if ($summary.firstInstruction -eq 0) {
        $summary.firstInstruction = $instruction
    }
    $summary.lastInstruction = $instruction
    $summary.finalTask = $row.task
    Add-UniqueString $slotOperationSets[$slot] $row.operation
    Add-UniqueString $slotProducerSets[$slot] $row.pc
    Add-UniqueString $slotTaskSets[$slot] $row.task

    if ($row.operation -like "set-*") {
        if ($slotChanged -and -not $oldActive -and $newActive) {
            $summary.setCount = [int]$summary.setCount + 1
            if ($summary.firstSetInstruction -eq 0) {
                $summary.firstSetInstruction = $instruction
            }
            $summary.lastSetInstruction = $instruction
            $activeSinceBySlot[$slot] = $instruction
        } else {
            $summary.redundantSetCount = [int]$summary.redundantSetCount + 1
        }
    } elseif ($row.operation -like "clear-*") {
        if ($slotChanged -and $oldActive -and -not $newActive) {
            $summary.clearCount = [int]$summary.clearCount + 1
            if ($summary.firstClearInstruction -eq 0) {
                $summary.firstClearInstruction = $instruction
            }
            $summary.lastClearInstruction = $instruction
            if ($activeSinceBySlot.ContainsKey($slot)) {
                $span = $instruction - [int64]$activeSinceBySlot[$slot]
                $summary.activeSpans = [int]$summary.activeSpans + 1
                if ($span -gt $summary.longestActiveInstructionSpan) {
                    $summary.longestActiveInstructionSpan = $span
                }
                $activeSinceBySlot.Remove($slot)
            }
        } else {
            $summary.redundantClearCount = [int]$summary.redundantClearCount + 1
        }
    }

    $summary.finalActive = $newActive
}

foreach ($slot in @($slotStates.Keys)) {
    $summary = $slotStates[$slot]
    $summary.operations = @($slotOperationSets[$slot] | Sort-Object)
    $summary.producerPcs = @($slotProducerSets[$slot] | Sort-Object)
    $summary.taskPointers = @($slotTaskSets[$slot] | Sort-Object)
    if ($activeSinceBySlot.ContainsKey($slot)) {
        $span = $lastInstruction - [int64]$activeSinceBySlot[$slot]
        if ($span -gt $summary.longestActiveInstructionSpan) {
            $summary.longestActiveInstructionSpan = $span
        }
    }
}

$slots = @(
    $slotStates.Values |
        Sort-Object `
            @{ Expression = { [bool]$_.finalActive }; Descending = $true },
            @{ Expression = { [int64]$_.longestActiveInstructionSpan }; Descending = $true },
            @{ Expression = { [int]$_.events }; Descending = $true },
            @{ Expression = { [int]$_.slot }; Descending = $false }
)

$activeSlots = @($slots | Where-Object { $_.finalActive } | ForEach-Object { $_.slot })
$topSlot = if ($slots.Count -gt 0) { $slots[0] } else { $null }

$summaryObject = [ordered]@{
    traceCsvPath = $traceFullPath
    bytes = (Get-Item -LiteralPath $traceFullPath).Length
    events = $rows.Count
    firstInstruction = $firstInstruction
    lastInstruction = $lastInstruction
    instructionSpan = if ($firstInstruction -eq 0) { 0 } else { $lastInstruction - $firstInstruction }
    flagAddress = $flagAddress
    finalFlag = ("0x{0:X8}" -f $lastFlag)
    activeSlots = @($activeSlots)
    activeSlotText = (@($activeSlots) -join ",")
    topSlot = $topSlot
    slots = @($slots)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    $directory = Split-Path -Parent $jsonFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $summaryObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if ($PassThru) {
    [pscustomobject]$summaryObject
}
