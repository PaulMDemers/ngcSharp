param(
    [string]$VisualAnchorDirectory = "",
    [string]$CompatRunDirectory = "",
    [string]$OutputDirectory = "",
    [int]$TopMaterialCount = 6
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

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Test-CsvHasRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Import-Csv -LiteralPath $Path | Select-Object -First 1)
}

function Convert-ToNullableInt {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Find-LatestVisualAnchorDirectory {
    $root = Resolve-FullPath "artifacts/sonic-visual-anchor"
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Visual anchor root not found: $root"
    }

    $latest = Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName "best-summary.csv")) -and (Test-Path -LiteralPath (Join-Path $_.FullName "run.json")) } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No visual-anchor directory with best-summary.csv and run.json found under $root"
    }

    return $latest.FullName
}

function Find-LatestCompatRunDirectory {
    $root = Resolve-FullPath "artifacts/compat-runs"
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Compatibility run root not found: $root"
    }

    $latest = Get-ChildItem -LiteralPath $root -Recurse -Filter run.json |
        Where-Object {
            $directory = Split-Path -Parent $_.FullName
            Test-Path -LiteralPath (Join-Path $directory "gx-triangle-coverage.summary.json")
        } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No compatibility run with gx-triangle-coverage.summary.json found under $root"
    }

    return Split-Path -Parent $latest.FullName
}

function Get-PhaseNote {
    param(
        [int]$MaterialWindowStart,
        [int]$MaterialWindowEnd,
        [int]$SelectedCopyDraw,
        [int]$VisualDrawsSinceSelectedCopy
    )

    if ($null -eq $SelectedCopyDraw -or $null -eq $MaterialWindowStart -or $null -eq $MaterialWindowEnd) {
        return "insufficient draw lifecycle metadata"
    }

    if ($MaterialWindowEnd -lt $SelectedCopyDraw) {
        return "material window ends before selected display copy by $($SelectedCopyDraw - $MaterialWindowEnd) draws"
    }

    if ($MaterialWindowStart -gt $SelectedCopyDraw) {
        return "material window starts after selected display copy by $($MaterialWindowStart - $SelectedCopyDraw) draws; visual replay has $VisualDrawsSinceSelectedCopy draws since that copy"
    }

    return "material window overlaps selected display-copy draw"
}

if ([string]::IsNullOrWhiteSpace($VisualAnchorDirectory)) {
    $VisualAnchorDirectory = Find-LatestVisualAnchorDirectory
} else {
    $VisualAnchorDirectory = Resolve-FullPath $VisualAnchorDirectory
}

if ([string]::IsNullOrWhiteSpace($CompatRunDirectory)) {
    $CompatRunDirectory = Find-LatestCompatRunDirectory
} else {
    $CompatRunDirectory = Resolve-FullPath $CompatRunDirectory
}

if (-not (Test-Path -LiteralPath $VisualAnchorDirectory)) {
    throw "Visual anchor directory not found: $VisualAnchorDirectory"
}

if (-not (Test-Path -LiteralPath $CompatRunDirectory)) {
    throw "Compatibility run directory not found: $CompatRunDirectory"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $VisualAnchorDirectory "visual-phase-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$visualRunPath = Join-Path $VisualAnchorDirectory "run.json"
$visualSummaryPath = Join-Path $VisualAnchorDirectory "emulator-summary.json"
$visualBestPath = Join-Path $VisualAnchorDirectory "best-summary.csv"
$compatRunPath = Join-Path $CompatRunDirectory "run.json"

$visualRun = Read-JsonFile $visualRunPath
$visualSummary = Read-JsonFile $visualSummaryPath
$compatRun = Read-JsonFile $compatRunPath
if ($null -eq $visualRun) {
    throw "Visual anchor run.json not found or unreadable: $visualRunPath"
}

if ($null -eq $compatRun) {
    throw "Compatibility run.json not found or unreadable: $compatRunPath"
}

$frameDump = Get-ObjectValue (Get-ObjectValue $visualSummary "gx" $null) "frameDump" $null
$lifecycle = Get-ObjectValue $frameDump "lifecycle" $null
$selectedCopy = Get-ObjectValue $lifecycle "selectedCopy" $null
if ($null -eq $selectedCopy) {
    $selectedCopy = Get-ObjectValue $lifecycle "lastDisplayCopy" $null
}

$materialCsvPath = [string](Get-ObjectValue $compatRun "gxMaterialSummaryCsvPath")
if ([string]::IsNullOrWhiteSpace($materialCsvPath)) {
    $materialCsvPath = Join-Path $CompatRunDirectory "gx-materials.summary.csv"
}

$triangleSummaryJsonPath = [string](Get-ObjectValue $compatRun "gxTriangleCoverageSummaryJsonPath")
if ([string]::IsNullOrWhiteSpace($triangleSummaryJsonPath)) {
    $triangleSummaryJsonPath = Join-Path $CompatRunDirectory "gx-triangle-coverage.summary.json"
}

$triangleSummary = Read-JsonFile $triangleSummaryJsonPath
$materials = if (Test-CsvHasRows $materialCsvPath) {
    @(Import-Csv -LiteralPath $materialCsvPath |
        Sort-Object { [int64](Get-ObjectValue $_ "color_writes" 0) } -Descending |
        Select-Object -First $TopMaterialCount)
} else {
    @()
}

$bestRows = if (Test-CsvHasRows $visualBestPath) {
    @(Import-Csv -LiteralPath $visualBestPath)
} else {
    @()
}

$materialWindowStart = Convert-ToNullableInt (Get-ObjectValue $compatRun "gxFrameSkipDraws")
$materialWindowDraws = Convert-ToNullableInt (Get-ObjectValue $compatRun "gxFrameMaxDraws")
$materialWindowEnd = if ($null -ne $materialWindowStart -and $null -ne $materialWindowDraws) { $materialWindowStart + $materialWindowDraws } else { $null }
$selectedCopyDraw = Convert-ToNullableInt (Get-ObjectValue $selectedCopy "drawsSeen")
$visualDrawsSinceSelectedCopy = Convert-ToNullableInt (Get-ObjectValue $lifecycle "drawsSinceLastDisplayCopy")
$phaseNote = Get-PhaseNote $materialWindowStart $materialWindowEnd $selectedCopyDraw $visualDrawsSinceSelectedCopy

$overview = [pscustomobject][ordered]@{
    visual_anchor_directory = $VisualAnchorDirectory
    compat_run_directory = $CompatRunDirectory
    candidate_path = Get-ObjectValue $visualRun "candidatePath"
    dolphin_reference_root = Get-ObjectValue $visualRun "dolphinReferenceRoot"
    visual_frame_source = Get-ObjectValue $visualRun "gxFrameSource"
    visual_skip_draws = Get-ObjectValue $visualRun "gxFrameSkipDraws"
    visual_max_draws = Get-ObjectValue $visualRun "gxFrameMaxDraws"
    visual_parsed_draws = Get-ObjectValue $frameDump "parsedDraws"
    visual_rendered_triangles = Get-ObjectValue $frameDump "renderedTriangles"
    selected_copy_index = Get-ObjectValue $selectedCopy "copyIndex"
    selected_copy_draw = $selectedCopyDraw
    selected_copy_fifo_offset = Get-ObjectValue $selectedCopy "fifoOffset"
    selected_copy_destination = Get-ObjectValue $selectedCopy "destinationAddress"
    selected_copy_clear_after_copy = Get-ObjectValue $selectedCopy "clearAfterCopy"
    visual_draws_since_selected_copy = $visualDrawsSinceSelectedCopy
    visual_copy_events_since_selected_copy = Get-ObjectValue $lifecycle "copyEventsSinceLastDisplayCopy"
    visual_clears_since_selected_copy = Get-ObjectValue $lifecycle "clearsSinceLastDisplayCopy"
    visual_texture_copies_since_selected_copy = Get-ObjectValue $lifecycle "textureCopiesSinceLastDisplayCopy"
    visual_phase = Get-ObjectValue $lifecycle "phase"
    compat_target = Get-ObjectValue $compatRun "target"
    compat_status = Get-ObjectValue $compatRun "status"
    compat_configuration = Get-ObjectValue $compatRun "configuration"
    material_window_start = $materialWindowStart
    material_window_end = $materialWindowEnd
    material_window_draws = $materialWindowDraws
    phase_note = $phaseNote
    triangle_rows = Get-ObjectValue $triangleSummary "rows"
    triangle_rendered_rows = Get-ObjectValue $triangleSummary "renderedRows"
    triangle_covered_pixels = Get-ObjectValue $triangleSummary "totalCoveredPixels"
    triangle_color_writes = Get-ObjectValue $triangleSummary "totalColorWrites"
    triangle_black_writes = Get-ObjectValue $triangleSummary "totalBlackColorWrites"
    triangle_black_ratio = Get-ObjectValue $triangleSummary "blackWriteRatio"
    triangle_dark_rendered_rows = Get-ObjectValue $triangleSummary "darkRenderedRows"
    material_summary_csv = $materialCsvPath
    triangle_summary_json = $triangleSummaryJsonPath
}

$visualRows = $bestRows | ForEach-Object {
    [pscustomobject][ordered]@{
        region = Get-ObjectValue $_ "region"
        sample = Get-ObjectValue $_ "sample"
        changed_percent = Get-ObjectValue $_ "changedPercent"
        average_delta = Get-ObjectValue $_ "averageDelta"
        baseline_nonblack = Get-ObjectValue $_ "baselineNonblack"
        candidate_nonblack = Get-ObjectValue $_ "candidateNonblack"
        sample_path = Get-ObjectValue $_ "samplePath"
        candidate_path = Get-ObjectValue $_ "candidatePath"
        diff_path = Get-ObjectValue $_ "diffPath"
    }
}

$materialRows = $materials | ForEach-Object {
    [pscustomobject][ordered]@{
        texture_address = Get-ObjectValue $_ "texture_address"
        texture_format = Get-ObjectValue $_ "texture_format"
        texture_size = Get-ObjectValue $_ "texture_size"
        texture_filter = Get-ObjectValue $_ "texture_filter"
        texture_lod = Get-ObjectValue $_ "texture_lod"
        stage0_mode = Get-ObjectValue $_ "stage0_mode"
        draw_count = Get-ObjectValue $_ "draw_count"
        triangle_count = Get-ObjectValue $_ "triangle_count"
        covered_pixels = Get-ObjectValue $_ "covered_pixels"
        color_writes = Get-ObjectValue $_ "color_writes"
        black_color_writes = Get-ObjectValue $_ "black_color_writes"
        black_write_ratio = Get-ObjectValue $_ "black_write_ratio"
        uv_s_min = Get-ObjectValue $_ "uv_s_min"
        uv_s_max = Get-ObjectValue $_ "uv_s_max"
        uv_t_min = Get-ObjectValue $_ "uv_t_min"
        uv_t_max = Get-ObjectValue $_ "uv_t_max"
        sample_raster_rgba_top = Get-ObjectValue $_ "sample_raster_rgba_top"
        sample_tev_rgba_top = Get-ObjectValue $_ "sample_tev_rgba_top"
        texture_xy_top = Get-ObjectValue $_ "texture_xy_top"
        texture_mip_samples_top = Get-ObjectValue $_ "texture_mip_samples_top"
        draws = Get-ObjectValue $_ "draws"
        triangles = Get-ObjectValue $_ "triangles"
    }
}

$textureGroupRows = @(Get-ObjectValue $triangleSummary "textureGroups" @()) | ForEach-Object {
    [pscustomobject][ordered]@{
        texture = Get-ObjectValue $_ "texture"
        count = Get-ObjectValue $_ "count"
        covered_pixels = Get-ObjectValue $_ "covered_pixels"
        black_color_writes = Get-ObjectValue $_ "black_color_writes"
    }
}

$overviewCsvPath = Join-Path $OutputDirectory "visual-phase-overview.csv"
$visualCsvPath = Join-Path $OutputDirectory "visual-phase-regions.csv"
$materialCsvOutPath = Join-Path $OutputDirectory "visual-phase-materials.csv"
$textureGroupCsvPath = Join-Path $OutputDirectory "visual-phase-texture-groups.csv"
$jsonPath = Join-Path $OutputDirectory "visual-phase-report.json"

$overview | Export-Csv -LiteralPath $overviewCsvPath -NoTypeInformation -Encoding UTF8
$visualRows | Export-Csv -LiteralPath $visualCsvPath -NoTypeInformation -Encoding UTF8
$materialRows | Export-Csv -LiteralPath $materialCsvOutPath -NoTypeInformation -Encoding UTF8
$textureGroupRows | Export-Csv -LiteralPath $textureGroupCsvPath -NoTypeInformation -Encoding UTF8

[pscustomobject][ordered]@{
    overview = $overview
    visual_regions = @($visualRows)
    materials = @($materialRows)
    texture_groups = @($textureGroupRows)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic visual phase report: $jsonPath"
Write-Output $overview
