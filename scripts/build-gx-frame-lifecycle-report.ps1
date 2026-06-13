param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$CopyCsvPath = "",
    [string]$CoverageCsvPath = "",
    [string]$RunJsonPath = "",
    [string]$EmulatorSummaryPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-InputPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Convert-OptionalInt {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int]::Parse([string]$Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-OptionalBool {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $false
    }

    return [bool]::Parse([string]$Value)
}

function Get-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

$runRoot = Resolve-InputPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($CopyCsvPath)) {
    $CopyCsvPath = Join-Path $runRoot "gx-copies.csv"
}
if ([string]::IsNullOrWhiteSpace($CoverageCsvPath)) {
    $CoverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
}
if ([string]::IsNullOrWhiteSpace($RunJsonPath)) {
    $RunJsonPath = Join-Path $runRoot "run.json"
}
if ([string]::IsNullOrWhiteSpace($EmulatorSummaryPath)) {
    $EmulatorSummaryPath = Join-Path $runRoot "emulator-summary.json"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "gx-frame-lifecycle-report"
}

$OutputDirectory = Resolve-InputPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runJson = Get-JsonFile $RunJsonPath
$emulatorSummary = Get-JsonFile $EmulatorSummaryPath
$summaryLifecycle = $emulatorSummary.gx.frameDump.lifecycle

$copies = @()
if (Test-Path -LiteralPath $CopyCsvPath) {
    $copies = @(Import-Csv -LiteralPath $CopyCsvPath)
}

$coverageRows = @()
if (Test-Path -LiteralPath $CoverageCsvPath) {
    $coverageRows = @(Import-Csv -LiteralPath $CoverageCsvPath)
}

$firstDraw = $coverageRows | Select-Object -First 1
$lastDraw = $coverageRows | Select-Object -Last 1
$firstDrawIndex = Convert-OptionalInt $firstDraw.draw_index
$lastDrawIndex = Convert-OptionalInt $lastDraw.draw_index
$copyEventsSeenAtFirstDraw = Convert-OptionalInt $firstDraw.copies_seen
$copyEventsSeenAtLastDraw = Convert-OptionalInt $lastDraw.copies_seen

$lastDisplayBeforeFirstDraw = $null
if ($null -ne $firstDrawIndex) {
    $lastDisplayBeforeFirstDraw = $copies |
        Where-Object { $_.kind -eq "display" -and (Convert-OptionalInt $_.draws_seen) -le $firstDrawIndex } |
        Select-Object -Last 1
}

$lastDisplayBeforeLastDraw = $null
if ($null -ne $lastDrawIndex) {
    $lastDisplayBeforeLastDraw = $copies |
        Where-Object { $_.kind -eq "display" -and (Convert-OptionalInt $_.draws_seen) -le $lastDrawIndex } |
        Select-Object -Last 1
}

$lastDisplay = $lastDisplayBeforeLastDraw
if ($null -eq $lastDisplay) {
    $lastDisplay = $lastDisplayBeforeFirstDraw
}

$lastDisplayCopyIndex = Convert-OptionalInt $lastDisplay.copy_index
$lastDisplayDrawsSeen = Convert-OptionalInt $lastDisplay.draws_seen
$copiesAfterLastDisplay = @()
if ($null -ne $lastDisplayCopyIndex) {
    $copiesAfterLastDisplay = @($copies | Where-Object { (Convert-OptionalInt $_.copy_index) -gt $lastDisplayCopyIndex })
}

$textureCopiesAfterLastDisplay = @($copiesAfterLastDisplay | Where-Object { $_.kind -eq "texture" }).Count
$clearsAfterLastDisplay = @($copiesAfterLastDisplay | Where-Object { Convert-OptionalBool $_.clear }).Count
$displayClear = Convert-OptionalBool $lastDisplay.clear
if ($displayClear) {
    $clearsAfterLastDisplay++
}

$phase = "unknown"
if ($summaryLifecycle -and $summaryLifecycle.phase) {
    $phase = [string]$summaryLifecycle.phase
} elseif ($null -ne $lastDisplayDrawsSeen -and $null -ne $firstDrawIndex -and $firstDrawIndex -gt $lastDisplayDrawsSeen) {
    $phase = "post-display-copy-efb"
} elseif ($null -ne $lastDisplayDrawsSeen -and $null -ne $lastDrawIndex -and $lastDrawIndex -eq $lastDisplayDrawsSeen) {
    $phase = "display-copy-boundary"
} elseif ($null -ne $lastDisplayDrawsSeen) {
    $phase = "display-copy"
}

$summaryLastDisplay = if ($summaryLifecycle -and $summaryLifecycle.lastDisplayCopy) { $summaryLifecycle.lastDisplayCopy } else { $null }
$recordLastDisplayCopyIndex = if ($null -ne $lastDisplayCopyIndex) { $lastDisplayCopyIndex } elseif ($summaryLastDisplay) { Convert-OptionalInt $summaryLastDisplay.copyIndex } else { $null }
$recordLastDisplayDrawsSeen = if ($null -ne $lastDisplayDrawsSeen) { $lastDisplayDrawsSeen } elseif ($summaryLastDisplay) { Convert-OptionalInt $summaryLastDisplay.drawsSeen } else { $null }
$recordLastDisplayFifoOffset = if ($lastDisplay -and -not [string]::IsNullOrWhiteSpace($lastDisplay.fifo_offset)) { $lastDisplay.fifo_offset } elseif ($summaryLastDisplay) { $summaryLastDisplay.fifoOffset } else { $null }
$recordLastDisplayDestination = if ($lastDisplay -and -not [string]::IsNullOrWhiteSpace($lastDisplay.display_address)) { $lastDisplay.display_address } elseif ($summaryLastDisplay) { $summaryLastDisplay.destinationAddress } else { $null }
$recordLastDisplayWidth = if ($lastDisplay -and -not [string]::IsNullOrWhiteSpace($lastDisplay.display_width)) { Convert-OptionalInt $lastDisplay.display_width } elseif ($summaryLastDisplay) { Convert-OptionalInt $summaryLastDisplay.width } else { $null }
$recordLastDisplayHeight = if ($lastDisplay -and -not [string]::IsNullOrWhiteSpace($lastDisplay.display_height)) { Convert-OptionalInt $lastDisplay.display_height } elseif ($summaryLastDisplay) { Convert-OptionalInt $summaryLastDisplay.height } else { $null }
$recordLastDisplayClear = if ($lastDisplay) { $displayClear } elseif ($summaryLastDisplay) { Convert-OptionalBool $summaryLastDisplay.clearAfterCopy } else { $false }
$recordDrawsSinceLastDisplayAtFirstDraw = if ($null -ne $firstDrawIndex -and $null -ne $recordLastDisplayDrawsSeen) { $firstDrawIndex - $recordLastDisplayDrawsSeen } elseif ($summaryLifecycle) { $summaryLifecycle.drawsSinceLastDisplayCopy } else { $null }
$recordDrawsSinceLastDisplayAtLastDraw = if ($null -ne $lastDrawIndex -and $null -ne $recordLastDisplayDrawsSeen) { $lastDrawIndex - $recordLastDisplayDrawsSeen } elseif ($summaryLifecycle) { $summaryLifecycle.drawsSinceLastDisplayCopy } else { $null }
$recordCopyEventsAfterLastDisplay = if ($copies.Count -gt 0) { $copiesAfterLastDisplay.Count } elseif ($summaryLifecycle) { Convert-OptionalInt $summaryLifecycle.copyEventsSinceLastDisplayCopy } else { 0 }
$recordTextureCopiesAfterLastDisplay = if ($copies.Count -gt 0) { $textureCopiesAfterLastDisplay } elseif ($summaryLifecycle) { Convert-OptionalInt $summaryLifecycle.textureCopiesSinceLastDisplayCopy } else { 0 }
$recordClearsAfterLastDisplay = if ($copies.Count -gt 0) { $clearsAfterLastDisplay } elseif ($summaryLifecycle) { Convert-OptionalInt $summaryLifecycle.clearsSinceLastDisplayCopy } else { 0 }
$recordEfbWasCleared = if ($copies.Count -gt 0) { $clearsAfterLastDisplay -gt 0 } elseif ($summaryLifecycle) { Convert-OptionalBool $summaryLifecycle.efbWasClearedAfterLastDisplayCopy } else { $false }

$record = [pscustomobject]@{
    runDirectory = $runRoot
    target = $runJson.target
    status = $runJson.status
    gxFrameSource = if ($runJson.frame -and $runJson.frame.source) { $runJson.frame.source } else { $emulatorSummary.gx.frameDump.source }
    phase = $phase
    firstDraw = $firstDrawIndex
    lastDraw = $lastDrawIndex
    copyEventsSeenAtFirstDraw = $copyEventsSeenAtFirstDraw
    copyEventsSeenAtLastDraw = $copyEventsSeenAtLastDraw
    lastDisplayCopyIndex = $recordLastDisplayCopyIndex
    lastDisplayDrawsSeen = $recordLastDisplayDrawsSeen
    lastDisplayFifoOffset = $recordLastDisplayFifoOffset
    lastDisplayDestination = $recordLastDisplayDestination
    lastDisplayWidth = $recordLastDisplayWidth
    lastDisplayHeight = $recordLastDisplayHeight
    lastDisplayClear = $recordLastDisplayClear
    drawsSinceLastDisplayCopyAtFirstDraw = $recordDrawsSinceLastDisplayAtFirstDraw
    drawsSinceLastDisplayCopyAtLastDraw = $recordDrawsSinceLastDisplayAtLastDraw
    copyEventsAfterLastDisplayCopy = $recordCopyEventsAfterLastDisplay
    textureCopiesAfterLastDisplayCopy = $recordTextureCopiesAfterLastDisplay
    clearsAfterLastDisplayCopy = $recordClearsAfterLastDisplay
    efbWasClearedAfterLastDisplayCopy = $recordEfbWasCleared
}

$csvPath = Join-Path $OutputDirectory "gx-frame-lifecycle-summary.csv"
$jsonPath = Join-Path $OutputDirectory "gx-frame-lifecycle-summary.json"
$record | Export-Csv -LiteralPath $csvPath -NoTypeInformation
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath

Write-Output $record
