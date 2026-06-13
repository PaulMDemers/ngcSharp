param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$Top = 20
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Get-IntValue {
    param($Row, [string]$Name)

    $value = $Row.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return 0
    }

    return [int]$value
}

function Get-StringValue {
    param($Row, [string]$Name)

    $value = $Row.$Name
    if ($null -eq $value) {
        return ""
    }

    return [string]$value
}

function Get-PercentValue {
    param([int]$Value, [int]$Total)

    if ($Total -eq 0) {
        return ""
    }

    return (100.0 * $Value / $Total).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

$coverageFullPath = Resolve-FullPath $CoverageCsvPath
$outputFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$rows = @(Import-Csv -LiteralPath $coverageFullPath)
if ($rows.Count -eq 0) {
    throw "Coverage CSV has no draw rows: $coverageFullPath"
}

$regionNames = @(
    $rows[0].PSObject.Properties.Name |
        Where-Object { $_ -like "*_delta_nonblack" } |
        ForEach-Object { $_ -replace "_delta_nonblack$", "" } |
        Where-Object { $_ -notin @("delta") -and $_ -ne "" }
)

$regionNames = @($regionNames | Where-Object { $rows[0].PSObject.Properties.Name -contains "$($_)_after_nonblack" })
if ($regionNames.Count -eq 0) {
    throw "Coverage CSV does not contain region coverage columns: $coverageFullPath"
}

$last = $rows[-1]
$finalRows = foreach ($region in $regionNames) {
    [pscustomobject]@{
        region = $region
        final_draw = Get-StringValue $last "draw_index"
        final_nonblack = Get-IntValue $last "${region}_after_nonblack"
        final_percent = Get-StringValue $last "${region}_after_percent"
        final_bounds = Get-StringValue $last "${region}_after_bounds"
    }
}

$deltaRows = foreach ($region in $regionNames) {
    foreach ($row in $rows) {
        $delta = Get-IntValue $row "${region}_delta_nonblack"
        if ($delta -eq 0) {
            continue
        }

        [pscustomobject]@{
            region = $region
            draw_index = Get-StringValue $row "draw_index"
            fifo_offset = Get-StringValue $row "fifo_offset"
            primitive = Get-StringValue $row "primitive"
            vertices = Get-StringValue $row "vertices"
            copies_seen = Get-StringValue $row "copies_seen"
            color_writes = Get-StringValue $row "color_writes"
            black_color_writes = Get-StringValue $row "black_color_writes"
            before_nonblack = Get-IntValue $row "${region}_before_nonblack"
            after_nonblack = Get-IntValue $row "${region}_after_nonblack"
            delta_nonblack = $delta
            abs_delta_nonblack = [Math]::Abs($delta)
            after_percent = Get-StringValue $row "${region}_after_percent"
            after_bounds = Get-StringValue $row "${region}_after_bounds"
            covered_pixels = Get-IntValue $row "${region}_covered_pixels"
            depth_rejected = Get-IntValue $row "${region}_depth_rejected"
            alpha_rejected = Get-IntValue $row "${region}_alpha_rejected"
            texture_samples = Get-IntValue $row "${region}_texture_samples"
            texture_nonblack_samples = Get-IntValue $row "${region}_texture_nonblack_samples"
            texture_black_samples = Get-IntValue $row "${region}_texture_black_samples"
            texture_black_sample_percent = Get-PercentValue (Get-IntValue $row "${region}_texture_black_samples") (Get-IntValue $row "${region}_texture_samples")
            texture_alpha_zero_samples = Get-IntValue $row "${region}_texture_alpha_zero_samples"
            texture_s_min = Get-StringValue $row "${region}_texture_s_min"
            texture_s_max = Get-StringValue $row "${region}_texture_s_max"
            texture_t_min = Get-StringValue $row "${region}_texture_t_min"
            texture_t_max = Get-StringValue $row "${region}_texture_t_max"
            texture_nonblack_s_min = Get-StringValue $row "${region}_texture_nonblack_s_min"
            texture_nonblack_s_max = Get-StringValue $row "${region}_texture_nonblack_s_max"
            texture_nonblack_t_min = Get-StringValue $row "${region}_texture_nonblack_t_min"
            texture_nonblack_t_max = Get-StringValue $row "${region}_texture_nonblack_t_max"
            texture_frac8_buckets = Get-StringValue $row "${region}_texture_frac8_buckets"
            texture_frac8_nonblack_buckets = Get-StringValue $row "${region}_texture_frac8_nonblack_buckets"
            region_color_writes = Get-IntValue $row "${region}_color_writes"
            region_black_color_writes = Get-IntValue $row "${region}_black_color_writes"
            region_black_write_percent = Get-PercentValue (Get-IntValue $row "${region}_black_color_writes") (Get-IntValue $row "${region}_color_writes")
        }
    }
}

$writeRows = foreach ($region in $regionNames) {
    foreach ($row in $rows) {
        $covered = Get-IntValue $row "${region}_covered_pixels"
        $textureSamples = Get-IntValue $row "${region}_texture_samples"
        $textureNonBlackSamples = Get-IntValue $row "${region}_texture_nonblack_samples"
        $textureBlackSamples = Get-IntValue $row "${region}_texture_black_samples"
        $colorWrites = Get-IntValue $row "${region}_color_writes"
        $blackWrites = Get-IntValue $row "${region}_black_color_writes"
        if ($covered -eq 0 -and $textureSamples -eq 0 -and $colorWrites -eq 0 -and $blackWrites -eq 0) {
            continue
        }

        [pscustomobject]@{
            region = $region
            draw_index = Get-StringValue $row "draw_index"
            fifo_offset = Get-StringValue $row "fifo_offset"
            primitive = Get-StringValue $row "primitive"
            vertices = Get-StringValue $row "vertices"
            copies_seen = Get-StringValue $row "copies_seen"
            covered_pixels = $covered
            depth_rejected = Get-IntValue $row "${region}_depth_rejected"
            alpha_rejected = Get-IntValue $row "${region}_alpha_rejected"
            texture_samples = $textureSamples
            texture_nonblack_samples = $textureNonBlackSamples
            texture_black_samples = $textureBlackSamples
            texture_black_sample_percent = Get-PercentValue $textureBlackSamples $textureSamples
            texture_alpha_zero_samples = Get-IntValue $row "${region}_texture_alpha_zero_samples"
            texture_s_min = Get-StringValue $row "${region}_texture_s_min"
            texture_s_max = Get-StringValue $row "${region}_texture_s_max"
            texture_t_min = Get-StringValue $row "${region}_texture_t_min"
            texture_t_max = Get-StringValue $row "${region}_texture_t_max"
            texture_nonblack_s_min = Get-StringValue $row "${region}_texture_nonblack_s_min"
            texture_nonblack_s_max = Get-StringValue $row "${region}_texture_nonblack_s_max"
            texture_nonblack_t_min = Get-StringValue $row "${region}_texture_nonblack_t_min"
            texture_nonblack_t_max = Get-StringValue $row "${region}_texture_nonblack_t_max"
            texture_frac8_buckets = Get-StringValue $row "${region}_texture_frac8_buckets"
            texture_frac8_nonblack_buckets = Get-StringValue $row "${region}_texture_frac8_nonblack_buckets"
            color_writes = $colorWrites
            black_color_writes = $blackWrites
            black_write_percent = Get-PercentValue $blackWrites $colorWrites
            delta_nonblack = Get-IntValue $row "${region}_delta_nonblack"
            after_nonblack = Get-IntValue $row "${region}_after_nonblack"
            after_percent = Get-StringValue $row "${region}_after_percent"
            after_bounds = Get-StringValue $row "${region}_after_bounds"
        }
    }
}

$finalCsvPath = Join-Path $outputFullPath "region-final.csv"
$deltaCsvPath = Join-Path $outputFullPath "region-deltas.csv"
$topDeltaCsvPath = Join-Path $outputFullPath "region-deltas.top.csv"
$writeCsvPath = Join-Path $outputFullPath "region-writes.csv"
$topBlackWriteCsvPath = Join-Path $outputFullPath "region-writes.black.top.csv"
$topBlackTextureCsvPath = Join-Path $outputFullPath "region-texture.black.top.csv"
$summaryJsonPath = Join-Path $outputFullPath "region-coverage-summary.json"

$finalRows | Export-Csv -NoTypeInformation -LiteralPath $finalCsvPath
$deltaRows | Sort-Object region, {[int]$_.draw_index} | Export-Csv -NoTypeInformation -LiteralPath $deltaCsvPath
$topDeltaRows = @($deltaRows | Sort-Object abs_delta_nonblack -Descending | Select-Object -First $Top)
$topDeltaRows | Export-Csv -NoTypeInformation -LiteralPath $topDeltaCsvPath
$writeRows | Sort-Object region, {[int]$_.draw_index} | Export-Csv -NoTypeInformation -LiteralPath $writeCsvPath
$topBlackWriteRows = @($writeRows | Sort-Object black_color_writes -Descending | Select-Object -First $Top)
$topBlackWriteRows | Export-Csv -NoTypeInformation -LiteralPath $topBlackWriteCsvPath
$topBlackTextureRows = @($writeRows | Sort-Object texture_black_samples -Descending | Select-Object -First $Top)
$topBlackTextureRows | Export-Csv -NoTypeInformation -LiteralPath $topBlackTextureCsvPath

[pscustomobject]@{
    coverage_csv_path = $coverageFullPath
    rows = $rows.Count
    regions = $regionNames
    final_csv_path = $finalCsvPath
    delta_csv_path = $deltaCsvPath
    top_delta_csv_path = $topDeltaCsvPath
    write_csv_path = $writeCsvPath
    top_black_write_csv_path = $topBlackWriteCsvPath
    top_black_texture_csv_path = $topBlackTextureCsvPath
    final = $finalRows
    top_deltas = $topDeltaRows
    top_black_writes = $topBlackWriteRows
    top_black_texture_samples = $topBlackTextureRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryJsonPath

Write-Host "GX region coverage summary: $summaryJsonPath"
