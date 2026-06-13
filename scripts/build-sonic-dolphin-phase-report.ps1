param(
    [Parameter(Mandatory = $true)]
    [string]$PhaseReportCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$AlignmentDirectory,
    [string]$OutputDirectory = "",
    [string]$Target = "",
    [string]$FocusTextureAddress = "0x0072C600"
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

function Convert-ToDouble {
    param(
        [object]$Value,
        [double]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToInt {
    param(
        [object]$Value,
        [int]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    return [int]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-FrameNumber {
    param([string]$Sample)

    if ($Sample -match 'frame-([0-9]+)') {
        return [int]$Matches[1]
    }

    return 0
}

function Select-FocusPhaseRow {
    param(
        [object[]]$Rows,
        [string]$Target
    )

    $candidates = @($Rows | Where-Object { $_.material_present -eq "True" })
    if (-not [string]::IsNullOrWhiteSpace($Target)) {
        $targetCandidates = @($candidates | Where-Object { $_.target -eq $Target })
        if ($targetCandidates.Count -gt 0) {
            $candidates = $targetCandidates
        }
    }

    if ($candidates.Count -eq 0) {
        $candidates = @($Rows | Where-Object { $_.packet_present -eq "True" })
    }

    return $candidates |
        Sort-Object `
            @{ Expression = { Convert-ToInt -Value (Get-ObjectValue $_ "gvrt_texture_match_count") }; Descending = $true },
            @{ Expression = { [string](Get-ObjectValue $_ "run_group") }; Descending = $true },
            @{ Expression = { Convert-ToDouble -Value (Get-ObjectValue $_ "elapsed_seconds") }; Descending = $true } |
        Select-Object -First 1
}

$phaseReportFullPath = Resolve-FullPath $PhaseReportCsvPath
$alignmentFullPath = Resolve-FullPath $AlignmentDirectory
if (-not (Test-Path -LiteralPath $phaseReportFullPath)) {
    throw "Phase report CSV not found: $phaseReportFullPath"
}

$bestShiftsPath = Join-Path $alignmentFullPath "best-shifts.csv"
if (-not (Test-Path -LiteralPath $bestShiftsPath)) {
    throw "Alignment best-shifts CSV not found: $bestShiftsPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $alignmentFullPath "dolphin-phase-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$phaseRows = @(Import-Csv -LiteralPath $phaseReportFullPath)
$focusPhase = Select-FocusPhaseRow $phaseRows $Target
$alignmentRows = @(Import-Csv -LiteralPath $bestShiftsPath)

$regionRows = New-Object System.Collections.ArrayList
foreach ($group in ($alignmentRows | Group-Object region | Sort-Object Name)) {
    $best = $group.Group |
        Sort-Object `
            @{ Expression = { Convert-ToDouble (Get-ObjectValue $_ "averageDelta") }; Descending = $false },
            @{ Expression = { Convert-ToDouble (Get-ObjectValue $_ "changedPercent") }; Descending = $false },
            @{ Expression = { Get-FrameNumber ([string](Get-ObjectValue $_ "sample")) }; Descending = $false } |
        Select-Object -First 1

    $topSameSampleCount = @($group.Group | Where-Object { $_.sample -eq $best.sample }).Count
    $sampleLuma = Convert-ToDouble (Get-ObjectValue $best "sampleAverageLuma")
    $candidateLuma = Convert-ToDouble (Get-ObjectValue $best "candidateAverageLuma")
    [void]$regionRows.Add([pscustomobject]@{
        region = $group.Name
        best_sample = Get-ObjectValue $best "sample"
        best_frame = Get-FrameNumber ([string](Get-ObjectValue $best "sample"))
        dx = Convert-ToInt (Get-ObjectValue $best "dx")
        dy = Convert-ToInt (Get-ObjectValue $best "dy")
        average_delta = Convert-ToDouble (Get-ObjectValue $best "averageDelta")
        changed_percent = Convert-ToDouble (Get-ObjectValue $best "changedPercent")
        sample_non_black_percent = Convert-ToDouble (Get-ObjectValue $best "sampleNonBlackPercent")
        candidate_non_black_percent = Convert-ToDouble (Get-ObjectValue $best "candidateNonBlackPercent")
        sample_average_luma = $sampleLuma
        candidate_average_luma = $candidateLuma
        luma_delta = [Math]::Round($candidateLuma - $sampleLuma, 6)
        top_rows_for_same_sample = $topSameSampleCount
        sample_path = Get-ObjectValue $best "samplePath"
        candidate_path = Get-ObjectValue $best "candidatePath"
    })
}

$frameGroups = @($regionRows | Group-Object best_sample | Sort-Object Count -Descending)
$dominantFrame = if ($frameGroups.Count -gt 0) { $frameGroups[0].Name } else { "" }
$dominantFrameCount = if ($frameGroups.Count -gt 0) { $frameGroups[0].Count } else { 0 }
$regionCount = $regionRows.Count
$dxValues = @($regionRows | ForEach-Object { [int]$_.dx })
$dyValues = @($regionRows | ForEach-Object { [int]$_.dy })
$avgDeltaValues = @($regionRows | ForEach-Object { [double]$_.average_delta })
$lumaDeltaValues = @($regionRows | ForEach-Object { [double]$_.luma_delta })

$dxSpread = if ($dxValues.Count -eq 0) { 0 } else { ($dxValues | Measure-Object -Minimum -Maximum | ForEach-Object { [int]$_.Maximum - [int]$_.Minimum }) }
$dySpread = if ($dyValues.Count -eq 0) { 0 } else { ($dyValues | Measure-Object -Minimum -Maximum | ForEach-Object { [int]$_.Maximum - [int]$_.Minimum }) }
$meanDelta = if ($avgDeltaValues.Count -eq 0) { 0 } else { ($avgDeltaValues | Measure-Object -Average).Average }
$meanLumaDelta = if ($lumaDeltaValues.Count -eq 0) { 0 } else { ($lumaDeltaValues | Measure-Object -Average).Average }

$verdict = if ($dominantFrameCount -eq $regionCount -and $dxSpread -le 16 -and $dySpread -le 16) {
    "same-frame-small-shift"
} elseif ($dominantFrameCount -eq $regionCount) {
    "same-frame-region-shift"
} elseif ($dominantFrameCount -ge [Math]::Max(1, [Math]::Ceiling($regionCount * 0.67))) {
    "mostly-same-frame"
} else {
    "mixed-frame"
}

$summary = [pscustomobject]@{
    generated_at = (Get-Date).ToString("o")
    phase_report_csv_path = $phaseReportFullPath
    alignment_directory = $alignmentFullPath
    best_shifts_path = $bestShiftsPath
    focus_texture_address = $FocusTextureAddress
    selected_phase_run = Get-ObjectValue $focusPhase "run_root"
    selected_phase_target = Get-ObjectValue $focusPhase "target"
    focus_packet = Get-ObjectValue $focusPhase "focus_packet"
    object = Get-ObjectValue $focusPhase "object"
    object_xyz = Get-ObjectValue $focusPhase "object_xyz"
    matrix_translation = Get-ObjectValue $focusPhase "matrix_translation"
    mapped_draws = ("{0}..{1}" -f (Get-ObjectValue $focusPhase "mapped_draw_start"), (Get-ObjectValue $focusPhase "mapped_draw_end"))
    material_draws = Get-ObjectValue $focusPhase "material_draws"
    material_black_ratio = Get-ObjectValue $focusPhase "material_black_ratio"
    material_color_writes = Get-ObjectValue $focusPhase "material_color_writes"
    gvrt_texture_match_count = Get-ObjectValue $focusPhase "gvrt_texture_match_count"
    gvrt_texture_draws = Get-ObjectValue $focusPhase "gvrt_texture_draws"
    region_count = $regionCount
    dominant_sample = $dominantFrame
    dominant_sample_region_count = $dominantFrameCount
    dx_spread = $dxSpread
    dy_spread = $dySpread
    mean_region_average_delta = [Math]::Round($meanDelta, 6)
    mean_luma_delta = [Math]::Round($meanLumaDelta, 6)
    phase_verdict = $verdict
}

$regionCsvPath = Join-Path $OutputDirectory "sonic-dolphin-phase-regions.csv"
$summaryCsvPath = Join-Path $OutputDirectory "sonic-dolphin-phase-summary.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-dolphin-phase-report.json"

$regionRows | Export-Csv -LiteralPath $regionCsvPath -NoTypeInformation -Encoding UTF8
$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    summary = $summary
    regions = $regionRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    summary_csv = $summaryCsvPath
    regions_csv = $regionCsvPath
    json = $jsonPath
    phase_verdict = $summary.phase_verdict
    dominant_sample = $summary.dominant_sample
    selected_phase_target = $summary.selected_phase_target
}
