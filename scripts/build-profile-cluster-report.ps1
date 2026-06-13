param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [ValidateSet("auto", "pcProfile", "pcProfileWithoutExternalInterruptLeaves", "pcProfileWithoutFastForwardLeaves")]
    [string]$ProfileName = "auto",
    [int]$MaxGapBytes = 4,
    [int]$TopClusters = 30
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
        [object]$Default = $null
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

function Convert-HexToUInt64 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [uint64]0
    }

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring(2)
    }

    return [uint64]::Parse($trimmed, [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hex32 {
    param([uint64]$Value)

    return "0x{0:X8}" -f $Value
}

function Get-PreferredProfile {
    param(
        [object]$Summary,
        [string]$RequestedName
    )

    if ($RequestedName -ne "auto") {
        return [pscustomobject]@{
            name = $RequestedName
            profile = Get-ObjectValue $Summary $RequestedName
        }
    }

    foreach ($name in @("pcProfileWithoutFastForwardLeaves", "pcProfileWithoutExternalInterruptLeaves", "pcProfile")) {
        $profile = Get-ObjectValue $Summary $name
        if ($null -ne $profile -and $null -ne (Get-ObjectValue $profile "entries")) {
            return [pscustomobject]@{
                name = $name
                profile = $profile
            }
        }
    }

    return [pscustomobject]@{
        name = ""
        profile = $null
    }
}

function Get-SummaryFiles {
    param([string]$Path)

    $fullPath = Resolve-FullPath $Path
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        return @(Get-Item -LiteralPath $fullPath)
    }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "Run path not found: $fullPath"
    }

    $direct = Join-Path $fullPath "emulator-summary.json"
    if (Test-Path -LiteralPath $direct) {
        return @(Get-Item -LiteralPath $direct)
    }

    return @(Get-ChildItem -LiteralPath $fullPath -Recurse -Filter "emulator-summary.json" -File)
}

$runRoot = Resolve-FullPath $RunDirectory
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $baseDirectory = if (Test-Path -LiteralPath $runRoot -PathType Leaf) {
        Split-Path -Parent $runRoot
    } else {
        $runRoot
    }

    $OutputDirectory = Join-Path $baseDirectory "profile-clusters"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$clusterRows = New-Object System.Collections.Generic.List[object]
$profileRows = New-Object System.Collections.Generic.List[object]
$summaryFiles = Get-SummaryFiles $RunDirectory

foreach ($summaryFile in $summaryFiles) {
    $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
    $selected = Get-PreferredProfile $summary $ProfileName
    $profile = $selected.profile
    if ($null -eq $profile -or $null -eq (Get-ObjectValue $profile "entries")) {
        Write-Warning "No PC profile entries found in $($summaryFile.FullName)"
        continue
    }

    $target = Split-Path -Leaf (Split-Path -Parent $summaryFile.FullName)
    $entries = @($profile.entries) |
        ForEach-Object {
            [pscustomobject]@{
                pc = [string]$_.pc
                pcValue = Convert-HexToUInt64 ([string]$_.pc)
                count = [uint64]$_.count
                percent = [double]$_.percent
            }
        } |
        Sort-Object pcValue

    foreach ($entry in $entries) {
        $profileRows.Add([pscustomobject]@{
            target = $target
            profile = $selected.name
            pc = $entry.pc
            count = $entry.count
            percent = $entry.percent
            summaryPath = $summaryFile.FullName
        })
    }

    $clusters = New-Object System.Collections.Generic.List[object]
    $currentEntries = New-Object System.Collections.Generic.List[object]
    $clusterStart = [uint64]0
    $lastPc = [uint64]0

    foreach ($entry in $entries) {
        if ($currentEntries.Count -eq 0) {
            $clusterStart = $entry.pcValue
            $lastPc = $entry.pcValue
            $currentEntries.Add($entry)
            continue
        }

        if ($entry.pcValue -le ($lastPc + [uint64]$MaxGapBytes)) {
            $currentEntries.Add($entry)
            $lastPc = $entry.pcValue
            continue
        }

        $clusters.Add([pscustomobject]@{
            start = $clusterStart
            end = $lastPc
            entries = @($currentEntries.ToArray())
        })
        $currentEntries.Clear()
        $clusterStart = $entry.pcValue
        $lastPc = $entry.pcValue
        $currentEntries.Add($entry)
    }

    if ($currentEntries.Count -gt 0) {
        $clusters.Add([pscustomobject]@{
            start = $clusterStart
            end = $lastPc
            entries = @($currentEntries.ToArray())
        })
    }

    $rank = 1
    foreach ($cluster in ($clusters | Sort-Object @{ Expression = { ($_.entries | Measure-Object -Property count -Sum).Sum }; Descending = $true }, start | Select-Object -First $TopClusters)) {
        $clusterEntries = @($cluster.entries)
        $totalSamples = [uint64](($clusterEntries | Measure-Object -Property count -Sum).Sum)
        $percent = [Math]::Round([double](($clusterEntries | Measure-Object -Property percent -Sum).Sum), 3)
        $topEntry = $clusterEntries | Sort-Object @{ Expression = "count"; Descending = $true }, pcValue | Select-Object -First 1
        $clusterRows.Add([pscustomobject]@{
            target = $target
            profile = $selected.name
            rank = $rank
            startPc = Format-Hex32 $cluster.start
            endPc = Format-Hex32 $cluster.end
            byteSpan = [uint64]($cluster.end - $cluster.start + 4)
            entryCount = $clusterEntries.Count
            totalSamples = $totalSamples
            percent = $percent
            topPc = $topEntry.pc
            topPcSamples = $topEntry.count
            topPcPercent = $topEntry.percent
            uniqueAddresses = Get-ObjectValue $profile "uniqueAddresses" ""
            profileTotalSamples = Get-ObjectValue $profile "totalSamples" ""
            excludedSamples = Get-ObjectValue $profile "excludedSamples" ""
            profiledInstructions = Get-ObjectValue $profile "profiledInstructions" ""
            stopReason = Get-ObjectValue $summary "stopReason" ""
            finalPc = Get-ObjectValue $summary "pc" ""
            executedInstructions = Get-ObjectValue $summary "executedInstructions" ""
            summaryPath = $summaryFile.FullName
        })
        $rank++
    }
}

$clustersCsvPath = Join-Path $outputRoot "profile-clusters.csv"
$entriesCsvPath = Join-Path $outputRoot "profile-entries.csv"
$reportJsonPath = Join-Path $outputRoot "profile-clusters.json"

$clusterRows | Export-Csv -NoTypeInformation -LiteralPath $clustersCsvPath
$profileRows | Export-Csv -NoTypeInformation -LiteralPath $entriesCsvPath

[ordered]@{
    schema = "ngcsharp.profile-clusters.v1"
    runDirectory = $runRoot
    profileName = $ProfileName
    maxGapBytes = $MaxGapBytes
    topClusters = $TopClusters
    summaryCount = $summaryFiles.Count
    clustersCsvPath = $clustersCsvPath
    entriesCsvPath = $entriesCsvPath
    clusters = @($clusterRows.ToArray())
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote profile cluster report: $clustersCsvPath"
Write-Host "Wrote profile entry report: $entriesCsvPath"
