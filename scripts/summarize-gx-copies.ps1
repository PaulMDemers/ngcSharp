param(
    [Parameter(Mandatory = $true)]
    [string]$CopyCsvPath,
    [string]$JsonPath = "",
    [string]$TimelineCsvPath = "",
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

function Convert-OptionalInt {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0
    }

    return [int64]$Value
}

function Convert-OptionalDouble {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0.0
    }

    return [double]$Value
}

function Copy-ToSummary {
    param($Row)

    if ($null -eq $Row) {
        return $null
    }

    return [pscustomobject][ordered]@{
        copyIndex = Convert-OptionalInt $Row.copy_index
        fifoOffset = $Row.fifo_offset
        drawsSeen = Convert-OptionalInt $Row.draws_seen
        kind = $Row.kind
        destinationAddress = $Row.destination_address
        format = $Row.format
        displayAddress = $Row.display_address
        displayWidth = Convert-OptionalInt $Row.display_width
        displayHeight = Convert-OptionalInt $Row.display_height
        displayFormat = $Row.display_format
        displayNonblack = Convert-OptionalInt $Row.display_nonblack
        displayNonblackPercent = Convert-OptionalDouble $Row.display_nonblack_percent
        displayNonblackBounds = $Row.display_nonblack_bounds
        beforeNonblack = Convert-OptionalInt $Row.before_nonblack
        beforeNonblackPercent = Convert-OptionalDouble $Row.before_nonblack_percent
    }
}

function New-DisplayDestinationSummary {
    param([string]$Address)

    return [pscustomobject][ordered]@{
        address = $Address
        copyCount = 0
        nonblackCopies = 0
        blackCopies = 0
        blackCopiesAfterNonblack = 0
        maxDisplayNonblack = 0
        maxDisplayNonblackPercent = 0.0
        firstCopy = $null
        lastCopy = $null
        firstNonblackCopy = $null
        lastNonblackCopy = $null
        largestCopy = $null
    }
}

function Update-DisplayDestinationSummary {
    param(
        $Summary,
        $Row
    )

    $displayNonblack = Convert-OptionalInt $Row.display_nonblack
    $displayNonblackPercent = Convert-OptionalDouble $Row.display_nonblack_percent

    $Summary.copyCount = [int64]$Summary.copyCount + 1
    if ($null -eq $Summary.firstCopy) {
        $Summary.firstCopy = Copy-ToSummary $Row
    }
    $Summary.lastCopy = Copy-ToSummary $Row

    if ($displayNonblack -gt 0) {
        $Summary.nonblackCopies = [int64]$Summary.nonblackCopies + 1
        if ($null -eq $Summary.firstNonblackCopy) {
            $Summary.firstNonblackCopy = Copy-ToSummary $Row
        }
        $Summary.lastNonblackCopy = Copy-ToSummary $Row
    } else {
        $Summary.blackCopies = [int64]$Summary.blackCopies + 1
        if ($Summary.nonblackCopies -gt 0) {
            $Summary.blackCopiesAfterNonblack = [int64]$Summary.blackCopiesAfterNonblack + 1
        }
    }

    if ($displayNonblack -gt $Summary.maxDisplayNonblack) {
        $Summary.maxDisplayNonblack = $displayNonblack
        $Summary.maxDisplayNonblackPercent = $displayNonblackPercent
        $Summary.largestCopy = Copy-ToSummary $Row
    }
}

function Copy-DisplayTimelineEvent {
    param(
        $Row,
        [string]$State
    )

    return [pscustomobject][ordered]@{
        copyIndex = Convert-OptionalInt $Row.copy_index
        drawsSeen = Convert-OptionalInt $Row.draws_seen
        address = $Row.display_address
        state = $State
        displayNonblack = Convert-OptionalInt $Row.display_nonblack
        displayNonblackPercent = Convert-OptionalDouble $Row.display_nonblack_percent
        displayNonblackBounds = $Row.display_nonblack_bounds
    }
}

function New-DisplayActivityRun {
    param($Row, [string]$State)

    $displayNonblack = Convert-OptionalInt $Row.display_nonblack
    $displayNonblackPercent = Convert-OptionalDouble $Row.display_nonblack_percent
    return [pscustomobject][ordered]@{
        state = $State
        firstCopyIndex = Convert-OptionalInt $Row.copy_index
        lastCopyIndex = Convert-OptionalInt $Row.copy_index
        firstDrawsSeen = Convert-OptionalInt $Row.draws_seen
        lastDrawsSeen = Convert-OptionalInt $Row.draws_seen
        copyCount = 1
        nonblackCopies = if ($displayNonblack -gt 0) { 1 } else { 0 }
        maxDisplayNonblack = $displayNonblack
        maxDisplayNonblackPercent = $displayNonblackPercent
        largestCopyIndex = Convert-OptionalInt $Row.copy_index
        largestCopyAddress = $Row.display_address
        largestCopyBounds = $Row.display_nonblack_bounds
    }
}

function Update-DisplayActivityRun {
    param($Run, $Row)

    $displayNonblack = Convert-OptionalInt $Row.display_nonblack
    $displayNonblackPercent = Convert-OptionalDouble $Row.display_nonblack_percent
    $Run.lastCopyIndex = Convert-OptionalInt $Row.copy_index
    $Run.lastDrawsSeen = Convert-OptionalInt $Row.draws_seen
    $Run.copyCount = [int64]$Run.copyCount + 1
    if ($displayNonblack -gt 0) {
        $Run.nonblackCopies = [int64]$Run.nonblackCopies + 1
    }

    if ($displayNonblack -gt $Run.maxDisplayNonblack) {
        $Run.maxDisplayNonblack = $displayNonblack
        $Run.maxDisplayNonblackPercent = $displayNonblackPercent
        $Run.largestCopyIndex = Convert-OptionalInt $Row.copy_index
        $Run.largestCopyAddress = $Row.display_address
        $Run.largestCopyBounds = $Row.display_nonblack_bounds
    }
}

$copyFullPath = Resolve-FullPath $CopyCsvPath
if (-not (Test-Path -LiteralPath $copyFullPath)) {
    throw "GX copy CSV not found: $copyFullPath"
}

$rows = 0
$displayCopies = 0
$textureCopies = 0
$nonblackDisplayCopies = 0
$lastCopy = $null
$firstNonblackDisplay = $null
$lastNonblackDisplay = $null
$largestDisplay = $null
$maxDisplayNonblack = 0
$maxBeforeNonblack = 0
$displayDestinationsByAddress = [ordered]@{}
$displayTimeline = New-Object System.Collections.Generic.List[object]
$lastTimelineAddress = $null
$lastTimelineState = $null
$displayActivity = New-Object System.Collections.Generic.List[object]
$currentActivityRun = $null
$lastActivityState = $null

Import-Csv -LiteralPath $copyFullPath | ForEach-Object {
    $rows++
    $lastCopy = $_

    if ($_.kind -eq "display") {
        $displayCopies++
        $displayNonblack = Convert-OptionalInt $_.display_nonblack
        $beforeNonblack = Convert-OptionalInt $_.before_nonblack
        if ($displayNonblack -gt 0) {
            $nonblackDisplayCopies++
            if ($null -eq $firstNonblackDisplay) {
                $firstNonblackDisplay = $_
            }
            $lastNonblackDisplay = $_
        }
        if ($displayNonblack -gt $maxDisplayNonblack) {
            $maxDisplayNonblack = $displayNonblack
            $largestDisplay = $_
        }
        if ($beforeNonblack -gt $maxBeforeNonblack) {
            $maxBeforeNonblack = $beforeNonblack
        }

        $address = if ([string]::IsNullOrWhiteSpace($_.display_address)) { $_.destination_address } else { $_.display_address }
        if ([string]::IsNullOrWhiteSpace($address)) {
            $address = "(unknown)"
        }

        if (-not $displayDestinationsByAddress.Contains($address)) {
            $displayDestinationsByAddress[$address] = New-DisplayDestinationSummary $address
        }
        $null = Update-DisplayDestinationSummary $displayDestinationsByAddress[$address] $_

        $timelineState = if ($displayNonblack -gt 0) { "nonblack" } else { "black" }
        if ($address -ne $lastTimelineAddress -or $timelineState -ne $lastTimelineState) {
            $displayTimeline.Add((Copy-DisplayTimelineEvent $_ $timelineState)) | Out-Null
            $lastTimelineAddress = $address
            $lastTimelineState = $timelineState
        }

        if ($timelineState -ne $lastActivityState) {
            $currentActivityRun = New-DisplayActivityRun $_ $timelineState
            $displayActivity.Add($currentActivityRun) | Out-Null
            $lastActivityState = $timelineState
        } else {
            Update-DisplayActivityRun $currentActivityRun $_
        }
    } elseif ($_.kind -eq "texture") {
        $textureCopies++
    }
} | Out-Null

$displayDestinations = @(
    $displayDestinationsByAddress.Values |
        Sort-Object `
            @{ Expression = { [int64]$_["blackCopiesAfterNonblack"] }; Descending = $true },
            @{ Expression = { [int64]$_["maxDisplayNonblack"] }; Descending = $true },
            @{ Expression = { [int64]$_["copyCount"] }; Descending = $true },
            @{ Expression = { [string]$_["address"] }; Descending = $false }
)

$summary = [ordered]@{
    copyCsvPath = $copyFullPath
    bytes = (Get-Item -LiteralPath $copyFullPath).Length
    rows = $rows
    displayCopies = $displayCopies
    textureCopies = $textureCopies
    nonblackDisplayCopies = $nonblackDisplayCopies
    maxDisplayNonblack = $maxDisplayNonblack
    maxBeforeNonblack = $maxBeforeNonblack
    firstNonblackDisplayCopy = Copy-ToSummary $firstNonblackDisplay
    lastNonblackDisplayCopy = Copy-ToSummary $lastNonblackDisplay
    largestDisplayCopy = Copy-ToSummary $largestDisplay
    displayDestinations = $displayDestinations
    displayTimeline = @($displayTimeline.ToArray())
    displayActivity = @($displayActivity.ToArray())
    lastCopy = Copy-ToSummary $lastCopy
}
$summaryObject = [pscustomobject]$summary

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summaryObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath
}

if (-not [string]::IsNullOrWhiteSpace($TimelineCsvPath)) {
    $timelineCsvFullPath = Resolve-FullPath $TimelineCsvPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $timelineCsvFullPath) | Out-Null
    $displayActivity | Export-Csv -NoTypeInformation -LiteralPath $timelineCsvFullPath
}

if ($PassThru) {
    $summaryObject
} else {
    $summaryObject | ConvertTo-Json -Depth 8
}
