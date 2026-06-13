param(
    [Parameter(Mandatory = $true)]
    [string]$TriangleCoverageCsvPath,

    [string]$SummaryCsvPath = "",

    [string]$JsonPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-OptionalInt64 {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0L
    }

    return [int64]$Value
}

function Get-StageField {
    param(
        [string]$Text,
        [string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $match = [regex]::Match($Text, $Pattern)
    if (-not $match.Success -or $match.Groups.Count -lt 2) {
        return ""
    }

    return $match.Groups[1].Value
}

function Get-CsvField {
    param(
        $Row,
        [string]$Name
    )

    if ($null -eq $Row.PSObject.Properties[$Name]) {
        return ""
    }

    return $Row.PSObject.Properties[$Name].Value
}

if (-not (Test-Path -LiteralPath $TriangleCoverageCsvPath)) {
    throw "Triangle coverage CSV not found: $TriangleCoverageCsvPath"
}

if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = [IO.Path]::ChangeExtension($TriangleCoverageCsvPath, ".summary.csv")
}

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $JsonPath = [IO.Path]::ChangeExtension($TriangleCoverageCsvPath, ".summary.json")
}

$rows = @(Import-Csv -LiteralPath $TriangleCoverageCsvPath)
$reasonCounts = $rows |
    Group-Object reason |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject][ordered]@{
            reason = $_.Name
            count = $_.Count
        }
    }

$renderedRows = @($rows | Where-Object { $_.rendered -eq "True" })
$renderedSummaries = $renderedRows |
    ForEach-Object {
        $colorWrites = Convert-OptionalInt64 $_.color_writes
        $blackWrites = Convert-OptionalInt64 $_.black_color_writes
        $coveredPixels = Convert-OptionalInt64 $_.covered_pixels
        $blackRatio = if ($colorWrites -gt 0) { [double]$blackWrites / [double]$colorWrites } else { 0.0 }
        $stageSummary = [string]$_.stage_summary
        [pscustomobject][ordered]@{
            draw_index = $_.draw_index
            triangle_index = $_.triangle_index
            vertex_a = $_.vertex_a
            vertex_b = $_.vertex_b
            vertex_c = $_.vertex_c
            covered_pixels = $coveredPixels
            color_writes = $colorWrites
            black_color_writes = $blackWrites
            black_write_ratio = [Math]::Round($blackRatio, 6)
            sample_raster_rgba = $_.sample_raster_rgba
            sample_tev_rgba = $_.sample_tev_rgba
            sample_alpha_test = $_.sample_alpha_test
            stage0_mode = $_.stage0_mode
            texture_address = Get-StageField $stageSummary "/addr=(0x[0-9A-Fa-f]+)"
            texture_format = Get-StageField $stageSummary "/fmt=([^/;]+)"
            texture_size = Get-StageField $stageSummary "/size=([^/;]+)"
            texture_filter = Get-StageField $stageSummary "/filter=([^/;]+)"
            texture_lod = Get-StageField $stageSummary "/lod=([^/;]+)"
            texture_s = Get-StageField $stageSummary "/s=([^/;]+)"
            texture_t = Get-StageField $stageSummary "/t=([^/;]+)"
            texture_xy = Get-StageField $stageSummary "/xy=([^/;]+)"
            texture_mip_samples = Get-StageField $stageSummary "/mips=([^;]+)"
            view_w_min = Get-CsvField $_ "view_w_min"
            view_w_max = Get-CsvField $_ "view_w_max"
            near_clip_w = Get-CsvField $_ "near_clip_w"
            stage0_tex_s_min = Get-CsvField $_ "stage0_tex_s_min"
            stage0_tex_s_max = Get-CsvField $_ "stage0_tex_s_max"
            stage0_tex_t_min = Get-CsvField $_ "stage0_tex_t_min"
            stage0_tex_t_max = Get-CsvField $_ "stage0_tex_t_max"
            stage_summary = $stageSummary
        }
    } |
    Sort-Object @{ Expression = "black_color_writes"; Descending = $true }, @{ Expression = "covered_pixels"; Descending = $true }

$renderedSummaries | Export-Csv -NoTypeInformation -LiteralPath $SummaryCsvPath

$totalCoveredPixels = 0L
$totalColorWrites = 0L
$totalBlackWrites = 0L
foreach ($row in $renderedSummaries) {
    $totalCoveredPixels += [int64]$row.covered_pixels
    $totalColorWrites += [int64]$row.color_writes
    $totalBlackWrites += [int64]$row.black_color_writes
}

$darkRenderedRows = @($renderedSummaries | Where-Object { $_.black_write_ratio -ge 0.5 })
$textureGroups = $renderedSummaries |
    Group-Object texture_address, texture_format, texture_size |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject][ordered]@{
            texture = $_.Name
            count = $_.Count
            covered_pixels = ($_.Group | Measure-Object covered_pixels -Sum).Sum
            black_color_writes = ($_.Group | Measure-Object black_color_writes -Sum).Sum
        }
    }

$textureMipGroups = $renderedSummaries |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.texture_mip_samples) } |
    Group-Object texture_address, texture_format, texture_size, texture_mip_samples |
    Sort-Object Count -Descending |
    Select-Object -First 50 |
    ForEach-Object {
        [pscustomobject][ordered]@{
            texture = $_.Name
            count = $_.Count
            covered_pixels = ($_.Group | Measure-Object covered_pixels -Sum).Sum
            black_color_writes = ($_.Group | Measure-Object black_color_writes -Sum).Sum
        }
    }

[pscustomobject][ordered]@{
    path = (Resolve-Path -LiteralPath $TriangleCoverageCsvPath).Path
    rows = $rows.Count
    renderedRows = $renderedRows.Count
    totalCoveredPixels = $totalCoveredPixels
    totalColorWrites = $totalColorWrites
    totalBlackColorWrites = $totalBlackWrites
    blackWriteRatio = if ($totalColorWrites -gt 0) { [Math]::Round([double]$totalBlackWrites / [double]$totalColorWrites, 6) } else { 0.0 }
    darkRenderedRows = $darkRenderedRows.Count
    reasonCounts = @($reasonCounts)
    textureGroups = @($textureGroups)
    textureMipGroups = @($textureMipGroups)
    topBlackTriangles = @($renderedSummaries | Select-Object -First 20)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $JsonPath

Write-Host "GX triangle coverage summary: $SummaryCsvPath"
