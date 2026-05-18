param(
    [Parameter(Mandatory = $true)]
    [string]$CopyCsvPath,
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

    return [ordered]@{
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
    } elseif ($_.kind -eq "texture") {
        $textureCopies++
    }
}

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
    lastCopy = Copy-ToSummary $lastCopy
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 8
}
