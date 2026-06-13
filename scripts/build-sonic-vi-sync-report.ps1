param(
    [Parameter(Mandatory = $true)]
    [string]$SyncSweepDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ViTimelineDirectory,
    [string]$OutputDirectory = "",
    [string]$BestByCandidateCsvPath = "",
    [string]$DisplayCopyViJoinCsvPath = "",
    [string]$BestOverallCsvPath = ""
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
    param($Object, [string]$Name, $Default = "")

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Convert-ToNullableInt64 {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    $text = "$Value".Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    if ($text.StartsWith("+0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(3), 16)
    }

    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(2), 16)
    }

    $parsed = 0L
    if ([long]::TryParse($text, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Convert-ToNullableDouble {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace("$Value")) {
        return $null
    }

    $parsed = 0.0
    if ([double]::TryParse("$Value", [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Format-NullableInt64 {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([int64]$Value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-NullableDouble {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([Math]::Round([double]$Value, 6)).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-CopyKey {
    param($DrawsSeen, $FifoOffset, $Address)

    return "{0}|{1}|{2}" -f "$DrawsSeen", "$FifoOffset", "$Address"
}

$syncSweepPath = Resolve-FullPath $SyncSweepDirectory
$viTimelinePath = Resolve-FullPath $ViTimelineDirectory

if ([string]::IsNullOrWhiteSpace($BestByCandidateCsvPath)) {
    $BestByCandidateCsvPath = Join-Path $syncSweepPath "best-by-candidate.csv"
} else {
    $BestByCandidateCsvPath = Resolve-FullPath $BestByCandidateCsvPath
}

if ([string]::IsNullOrWhiteSpace($BestOverallCsvPath)) {
    $BestOverallCsvPath = Join-Path $syncSweepPath "best-overall.csv"
} else {
    $BestOverallCsvPath = Resolve-FullPath $BestOverallCsvPath
}

if ([string]::IsNullOrWhiteSpace($DisplayCopyViJoinCsvPath)) {
    $DisplayCopyViJoinCsvPath = Join-Path $viTimelinePath "display-copy-vi-join.csv"
} else {
    $DisplayCopyViJoinCsvPath = Resolve-FullPath $DisplayCopyViJoinCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $syncSweepPath "vi-sync-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

if (-not (Test-Path -LiteralPath $BestByCandidateCsvPath)) {
    throw "Best-by-candidate CSV not found: $BestByCandidateCsvPath"
}

if (-not (Test-Path -LiteralPath $DisplayCopyViJoinCsvPath)) {
    throw "Display-copy VI join CSV not found: $DisplayCopyViJoinCsvPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$viRows = @(Import-Csv -LiteralPath $DisplayCopyViJoinCsvPath)
$viByCopy = @{}
foreach ($row in $viRows) {
    $key = Get-CopyKey (Get-ObjectValue $row "draws_seen") (Get-ObjectValue $row "fifo_offset") (Get-ObjectValue $row "display_address")
    if (-not $viByCopy.ContainsKey($key)) {
        $viByCopy[$key] = $row
    }
}

$candidateRows = New-Object System.Collections.ArrayList
foreach ($row in (Import-Csv -LiteralPath $BestByCandidateCsvPath)) {
    $copyKey = Get-CopyKey (Get-ObjectValue $row "candidateSelectedCopyDrawsSeen") (Get-ObjectValue $row "candidateSelectedCopyFifoOffset") (Get-ObjectValue $row "candidateSelectedCopyAddress")
    $vi = if ($viByCopy.ContainsKey($copyKey)) { $viByCopy[$copyKey] } else { $null }
    $copyInstruction = Convert-ToNullableInt64 (Get-ObjectValue $vi "instruction")
    $firstViInstruction = Convert-ToNullableInt64 (Get-ObjectValue $vi "first_vi_match_instruction")
    $delay = if ($null -ne $copyInstruction -and $null -ne $firstViInstruction) { $firstViInstruction - $copyInstruction } else { $null }
    [void]$candidateRows.Add([pscustomobject]@{
        candidate = Get-ObjectValue $row "candidate"
        candidate_skip_draws = Get-ObjectValue $row "candidateSkipDraws"
        selected_copy_index = Get-ObjectValue $row "candidateSelectedCopyIndex"
        selected_copy_draws_seen = Get-ObjectValue $row "candidateSelectedCopyDrawsSeen"
        selected_copy_fifo_offset = Get-ObjectValue $row "candidateSelectedCopyFifoOffset"
        selected_copy_address = Get-ObjectValue $row "candidateSelectedCopyAddress"
        lifecycle_phase = Get-ObjectValue $row "candidateLifecyclePhase"
        draws_since_last_display_copy = Get-ObjectValue $row "candidateDrawsSinceLastDisplayCopy"
        clears_since_last_display_copy = Get-ObjectValue $row "candidateClearsSinceLastDisplayCopy"
        best_sample = Get-ObjectValue $row "sample"
        best_sample_frame = Get-ObjectValue $row "sampleFrame"
        changed_percent = Get-ObjectValue $row "changedPercent"
        average_delta = Get-ObjectValue $row "averageDelta"
        sample_nonblack_percent = Get-ObjectValue $row "sampleNonblackPercent"
        candidate_nonblack_percent = Get-ObjectValue $row "candidateNonblackPercent"
        vi_joined = $null -ne $vi
        vi_field_at_copy = Get-ObjectValue $vi "vi_field"
        vi_field0_at_copy = Get-ObjectValue $vi "vi_field0"
        vi_field1_at_copy = Get-ObjectValue $vi "vi_field1"
        copy_instruction = Format-NullableInt64 $copyInstruction
        first_vi_match_instruction = Format-NullableInt64 $firstViInstruction
        first_vi_match_field = Get-ObjectValue $vi "first_vi_match_field"
        first_vi_match_delta_instructions = Format-NullableInt64 $delay
        candidate_path = Get-ObjectValue $row "candidatePath"
        sample_path = Get-ObjectValue $row "samplePath"
        diff_path = Get-ObjectValue $row "diffPath"
    })
}

$copyRows = New-Object System.Collections.ArrayList
foreach ($group in ($candidateRows | Group-Object selected_copy_draws_seen, selected_copy_fifo_offset, selected_copy_address)) {
    $items = @($group.Group)
    $best = $items |
        Sort-Object `
            @{ Expression = { Convert-ToNullableDouble (Get-ObjectValue $_ "changed_percent") }; Descending = $false },
            @{ Expression = { Convert-ToNullableDouble (Get-ObjectValue $_ "average_delta") }; Descending = $false } |
        Select-Object -First 1

    $frames = @($items | ForEach-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "best_sample_frame") } | Where-Object { $null -ne $_ })
    $skips = @($items | ForEach-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "candidate_skip_draws") } | Where-Object { $null -ne $_ })
    $first = $items | Select-Object -First 1
    $frameStats = if ($frames.Count -gt 0) { $frames | Measure-Object -Minimum -Maximum } else { $null }
    $skipStats = if ($skips.Count -gt 0) { $skips | Measure-Object -Minimum -Maximum } else { $null }
    [void]$copyRows.Add([pscustomobject]@{
        selected_copy_draws_seen = Get-ObjectValue $first "selected_copy_draws_seen"
        selected_copy_fifo_offset = Get-ObjectValue $first "selected_copy_fifo_offset"
        selected_copy_address = Get-ObjectValue $first "selected_copy_address"
        candidate_count = $items.Count
        candidate_skip_min = if ($null -ne $skipStats) { [int64]$skipStats.Minimum } else { "" }
        candidate_skip_max = if ($null -ne $skipStats) { [int64]$skipStats.Maximum } else { "" }
        best_sample_frame_min = if ($null -ne $frameStats) { [int64]$frameStats.Minimum } else { "" }
        best_sample_frame_max = if ($null -ne $frameStats) { [int64]$frameStats.Maximum } else { "" }
        best_sample_frame_span = if ($null -ne $frameStats) { [int64]$frameStats.Maximum - [int64]$frameStats.Minimum } else { "" }
        best_candidate = Get-ObjectValue $best "candidate"
        best_sample = Get-ObjectValue $best "best_sample"
        best_changed_percent = Get-ObjectValue $best "changed_percent"
        best_average_delta = Get-ObjectValue $best "average_delta"
        vi_joined = Get-ObjectValue $first "vi_joined"
        vi_field_at_copy = Get-ObjectValue $first "vi_field_at_copy"
        vi_field0_at_copy = Get-ObjectValue $first "vi_field0_at_copy"
        vi_field1_at_copy = Get-ObjectValue $first "vi_field1_at_copy"
        copy_instruction = Get-ObjectValue $first "copy_instruction"
        first_vi_match_instruction = Get-ObjectValue $first "first_vi_match_instruction"
        first_vi_match_field = Get-ObjectValue $first "first_vi_match_field"
        first_vi_match_delta_instructions = Get-ObjectValue $first "first_vi_match_delta_instructions"
    })
}

$overallRows = @()
if (Test-Path -LiteralPath $BestOverallCsvPath) {
    $overallRows = @(
        Import-Csv -LiteralPath $BestOverallCsvPath |
            Select-Object -First 20 candidate,candidateSkipDraws,candidateSelectedCopyDrawsSeen,candidateSelectedCopyFifoOffset,candidateSelectedCopyAddress,sample,sampleFrame,changedPercent,averageDelta,candidateNonblackPercent,sampleNonblackPercent
    )
}

$copyGroupsWithVi = @($copyRows | Where-Object { (Get-ObjectValue $_ "vi_joined") -eq "True" -or (Get-ObjectValue $_ "vi_joined") -eq $true }).Count
$copyGroupsDisplayedAfterCopy = @($copyRows | Where-Object { -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "first_vi_match_instruction")) }).Count
$distinctCopies = $copyRows.Count
$frameSpans = @($copyRows | ForEach-Object { Convert-ToNullableInt64 (Get-ObjectValue $_ "best_sample_frame_span") } | Where-Object { $null -ne $_ })
$maxFrameSpan = if ($frameSpans.Count -gt 0) { [int64](($frameSpans | Measure-Object -Maximum).Maximum) } else { 0L }
$verdict = if ($distinctCopies -eq 0) {
    "no-candidate-copies"
} elseif ($copyGroupsWithVi -lt $distinctCopies) {
    "missing-vi-join"
} elseif ($copyGroupsDisplayedAfterCopy -eq $distinctCopies -and $maxFrameSpan -le 20) {
    "vi-displayed-stable-sync-window"
} elseif ($copyGroupsDisplayedAfterCopy -eq $distinctCopies) {
    "vi-displayed-broad-sync-window"
} else {
    "vi-joined-no-display-flip"
}

$candidateCsvPath = Join-Path $OutputDirectory "vi-sync-candidates.csv"
$copyCsvPath = Join-Path $OutputDirectory "vi-sync-copy-summary.csv"
$overallCsvPath = Join-Path $OutputDirectory "vi-sync-best-overall.csv"
$jsonPath = Join-Path $OutputDirectory "vi-sync-report.json"

$candidateRows | Export-Csv -LiteralPath $candidateCsvPath -NoTypeInformation -Encoding UTF8
$copyRows | Export-Csv -LiteralPath $copyCsvPath -NoTypeInformation -Encoding UTF8
$overallRows | Export-Csv -LiteralPath $overallCsvPath -NoTypeInformation -Encoding UTF8

[pscustomobject]@{
    schema = "ngcsharp.sonic-vi-sync.v1"
    syncSweepDirectory = $syncSweepPath
    viTimelineDirectory = $viTimelinePath
    bestByCandidateCsvPath = $BestByCandidateCsvPath
    displayCopyViJoinCsvPath = $DisplayCopyViJoinCsvPath
    bestOverallCsvPath = if (Test-Path -LiteralPath $BestOverallCsvPath) { $BestOverallCsvPath } else { $null }
    verdict = $verdict
    candidateRows = $candidateRows.Count
    distinctSelectedCopies = $distinctCopies
    selectedCopiesWithViJoin = $copyGroupsWithVi
    selectedCopiesDisplayedAfterCopy = $copyGroupsDisplayedAfterCopy
    maxBestSampleFrameSpan = $maxFrameSpan
    viSyncCandidatesCsvPath = $candidateCsvPath
    viSyncCopySummaryCsvPath = $copyCsvPath
    viSyncBestOverallCsvPath = $overallCsvPath
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic VI sync report: $jsonPath"
$copyRows | Format-Table selected_copy_draws_seen,selected_copy_fifo_offset,selected_copy_address,candidate_count,best_sample_frame_min,best_sample_frame_max,best_candidate,best_changed_percent,first_vi_match_delta_instructions -AutoSize
