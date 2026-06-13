param(
    [Parameter(Mandatory = $true)]
    [string]$TriangleCoverageSummaryCsvPath,

    [string]$MaterialCsvPath = "",

    [string]$JsonPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-OptionalDouble {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]$Value
}

function Convert-OptionalInt64 {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0L
    }

    return [int64]$Value
}

function Get-MinValue {
    param($Values)

    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) {
        return ""
    }

    return ($numbers | Measure-Object -Minimum).Minimum
}

function Get-MaxValue {
    param($Values)

    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) {
        return ""
    }

    return ($numbers | Measure-Object -Maximum).Maximum
}

function Get-TopValues {
    param(
        $Values,
        [int]$Count = 6
    )

    $valuesArray = @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($valuesArray.Count -eq 0) {
        return ""
    }

    return (($valuesArray |
        Group-Object |
        Sort-Object @{ Expression = "Count"; Descending = $true }, @{ Expression = "Name"; Descending = $false } |
        Select-Object -First $Count |
        ForEach-Object { "$($_.Name)x$($_.Count)" }) -join " | ")
}

if (-not (Test-Path -LiteralPath $TriangleCoverageSummaryCsvPath)) {
    throw "Triangle coverage summary CSV not found: $TriangleCoverageSummaryCsvPath"
}

if ([string]::IsNullOrWhiteSpace($MaterialCsvPath)) {
    $directory = Split-Path -Parent $TriangleCoverageSummaryCsvPath
    $MaterialCsvPath = Join-Path $directory "gx-materials.summary.csv"
}

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $directory = Split-Path -Parent $TriangleCoverageSummaryCsvPath
    $JsonPath = Join-Path $directory "gx-materials.summary.json"
}

$rows = @(Import-Csv -LiteralPath $TriangleCoverageSummaryCsvPath)
$materialRows = $rows |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.texture_address) } |
    Group-Object texture_address, texture_format, texture_size, texture_filter, texture_lod, stage0_mode |
    ForEach-Object {
        $group = @($_.Group)
        $coveredPixels = ($group | ForEach-Object { Convert-OptionalInt64 $_.covered_pixels } | Measure-Object -Sum).Sum
        $colorWrites = ($group | ForEach-Object { Convert-OptionalInt64 $_.color_writes } | Measure-Object -Sum).Sum
        $blackWrites = ($group | ForEach-Object { Convert-OptionalInt64 $_.black_color_writes } | Measure-Object -Sum).Sum
        $blackRatio = if ($colorWrites -gt 0) { [double]$blackWrites / [double]$colorWrites } else { 0.0 }
        $draws = @($group | Group-Object draw_index | Sort-Object Name | ForEach-Object { $_.Name })
        $triangles = @($group | Sort-Object {[int]$_.draw_index}, {[int]$_.triangle_index} | ForEach-Object { "$($_.draw_index):$($_.triangle_index)" })
        $sMin = Get-MinValue ($group | ForEach-Object { Convert-OptionalDouble $_.stage0_tex_s_min })
        $sMax = Get-MaxValue ($group | ForEach-Object { Convert-OptionalDouble $_.stage0_tex_s_max })
        $tMin = Get-MinValue ($group | ForEach-Object { Convert-OptionalDouble $_.stage0_tex_t_min })
        $tMax = Get-MaxValue ($group | ForEach-Object { Convert-OptionalDouble $_.stage0_tex_t_max })
        $viewWMin = Get-MinValue ($group | ForEach-Object { Convert-OptionalDouble $_.view_w_min })
        $viewWMax = Get-MaxValue ($group | ForEach-Object { Convert-OptionalDouble $_.view_w_max })

        [pscustomobject][ordered]@{
            texture_address = $group[0].texture_address
            texture_format = $group[0].texture_format
            texture_size = $group[0].texture_size
            texture_filter = $group[0].texture_filter
            texture_lod = $group[0].texture_lod
            stage0_mode = $group[0].stage0_mode
            draw_count = $draws.Count
            triangle_count = $group.Count
            covered_pixels = [int64]$coveredPixels
            color_writes = [int64]$colorWrites
            black_color_writes = [int64]$blackWrites
            black_write_ratio = [Math]::Round($blackRatio, 6)
            uv_s_min = $sMin
            uv_s_max = $sMax
            uv_t_min = $tMin
            uv_t_max = $tMax
            view_w_min = $viewWMin
            view_w_max = $viewWMax
            sample_raster_rgba_top = Get-TopValues ($group | ForEach-Object { $_.sample_raster_rgba })
            sample_tev_rgba_top = Get-TopValues ($group | ForEach-Object { $_.sample_tev_rgba })
            texture_xy_top = Get-TopValues ($group | ForEach-Object { $_.texture_xy })
            texture_mip_samples_top = Get-TopValues ($group | ForEach-Object { $_.texture_mip_samples }) 4
            draws = $draws -join " "
            triangles = $triangles -join " "
        }
    } |
    Sort-Object @{ Expression = "black_color_writes"; Descending = $true }, @{ Expression = "covered_pixels"; Descending = $true }

$materialRows | Export-Csv -NoTypeInformation -LiteralPath $MaterialCsvPath

[pscustomobject][ordered]@{
    path = (Resolve-Path -LiteralPath $TriangleCoverageSummaryCsvPath).Path
    materialCsvPath = (Resolve-Path -LiteralPath $MaterialCsvPath).Path
    materialCount = @($materialRows).Count
    topBlackMaterials = @($materialRows | Select-Object -First 20)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $JsonPath

Write-Host "GX material summary: $MaterialCsvPath"
