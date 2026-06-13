param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$SamplePath,
    [Parameter(Mandatory = $true)]
    [string]$TriangleCoveragePath,
    [Parameter(Mandatory = $true)]
    [string]$TriangleCoverageSummaryPath,
    [string]$OutputDirectory = "artifacts/sonic-background-attribution",
    [int]$DrawStart = 12181,
    [int]$DrawEnd = 16058,
    [int]$NonBlackThreshold = 8,
    [int]$ChangeThreshold = 8,
    [switch]$SkipImageAnalysis
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-ToNullableInt64 {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace("$Value")) {
        return $null
    }

    $parsed = 0L
    if ([long]::TryParse("$Value", [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
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

function New-Bounds {
    [pscustomobject]@{ any = $false; minX = 0; minY = 0; maxX = 0; maxY = 0 }
}

function Add-BoundsPoint {
    param($Bounds, [int]$X, [int]$Y)

    if (-not $Bounds.any) {
        $Bounds.any = $true
        $Bounds.minX = $X
        $Bounds.maxX = $X
        $Bounds.minY = $Y
        $Bounds.maxY = $Y
        return
    }

    $Bounds.minX = [Math]::Min($Bounds.minX, $X)
    $Bounds.maxX = [Math]::Max($Bounds.maxX, $X)
    $Bounds.minY = [Math]::Min($Bounds.minY, $Y)
    $Bounds.maxY = [Math]::Max($Bounds.maxY, $Y)
}

function Format-Bounds {
    param($Bounds)

    if (-not $Bounds.any) {
        return ""
    }

    return "{0}/{1}-{2}/{3}" -f $Bounds.minX, $Bounds.minY, $Bounds.maxX, $Bounds.maxY
}

function Test-NonBlack {
    param([System.Drawing.Color]$Color, [int]$Threshold)

    return ([int]$Color.R + [int]$Color.G + [int]$Color.B) -gt $Threshold
}

function Test-StarLike {
    param([System.Drawing.Color]$Color)

    $max = [Math]::Max([int]$Color.R, [Math]::Max([int]$Color.G, [int]$Color.B))
    $min = [Math]::Min([int]$Color.R, [Math]::Min([int]$Color.G, [int]$Color.B))
    $luma = (([int]$Color.R + [int]$Color.G + [int]$Color.B) / 3.0)
    return $luma -ge 55 -and ($max - $min) -le 95
}

function Test-WarmBuildingLight {
    param([System.Drawing.Color]$Color)

    return $Color.R -ge 70 -and $Color.G -ge 45 -and $Color.R -ge $Color.B + 10
}

function Save-Crop {
    param(
        [System.Drawing.Bitmap]$Image,
        [string]$Path,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )

    $rect = [System.Drawing.Rectangle]::new($X, $Y, $Width, $Height)
    $crop = $Image.Clone($rect, $Image.PixelFormat)
    try {
        $crop.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $crop.Dispose()
    }
}

function Analyze-ImageRegion {
    param(
        [System.Drawing.Bitmap]$Sample,
        [System.Drawing.Bitmap]$Candidate,
        $Region,
        [string]$OutputRoot,
        [int]$NonBlackThreshold,
        [int]$ChangeThreshold
    )

    $x0 = [Math]::Max(0, [int]$Region.x)
    $y0 = [Math]::Max(0, [int]$Region.y)
    $width = [Math]::Min([int]$Region.width, $Sample.Width - $x0)
    $height = [Math]::Min([int]$Region.height, $Sample.Height - $y0)
    if ($width -le 0 -or $height -le 0) {
        throw "Empty region: $($Region.name)"
    }

    $regionRoot = Join-Path $OutputRoot $Region.name
    New-Item -ItemType Directory -Force -Path $regionRoot | Out-Null
    $sampleCropPath = Join-Path $regionRoot "sample.png"
    $candidateCropPath = Join-Path $regionRoot "candidate.png"
    $maskPath = Join-Path $regionRoot "coverage-mask.png"
    Save-Crop -Image $Sample -Path $sampleCropPath -X $x0 -Y $y0 -Width $width -Height $height
    Save-Crop -Image $Candidate -Path $candidateCropPath -X $x0 -Y $y0 -Width $width -Height $height

    $mask = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = [int64]$width * [int64]$height
    $sampleNonBlack = 0L
    $candidateNonBlack = 0L
    $shared = 0L
    $sampleOnly = 0L
    $candidateOnly = 0L
    $changed = 0L
    $sampleStars = 0L
    $candidateStars = 0L
    $sharedStars = 0L
    $sampleWarm = 0L
    $candidateWarm = 0L
    $sampleOnlyBounds = New-Bounds
    $candidateOnlyBounds = New-Bounds
    $sharedBounds = New-Bounds

    try {
        for ($localY = 0; $localY -lt $height; $localY++) {
            $y = $y0 + $localY
            for ($localX = 0; $localX -lt $width; $localX++) {
                $x = $x0 + $localX
                $s = $Sample.GetPixel($x, $y)
                $c = $Candidate.GetPixel($x, $y)
                $snb = Test-NonBlack $s $NonBlackThreshold
                $cnb = Test-NonBlack $c $NonBlackThreshold
                $delta = [Math]::Abs([int]$s.R - [int]$c.R) + [Math]::Abs([int]$s.G - [int]$c.G) + [Math]::Abs([int]$s.B - [int]$c.B)
                if ($delta -gt $ChangeThreshold) {
                    $changed++
                }

                if (Test-StarLike $s) {
                    $sampleStars++
                }

                if (Test-StarLike $c) {
                    $candidateStars++
                }

                if ((Test-StarLike $s) -and (Test-StarLike $c)) {
                    $sharedStars++
                }

                if (Test-WarmBuildingLight $s) {
                    $sampleWarm++
                }

                if (Test-WarmBuildingLight $c) {
                    $candidateWarm++
                }

                if ($snb -and $cnb) {
                    $sampleNonBlack++
                    $candidateNonBlack++
                    $shared++
                    Add-BoundsPoint $sharedBounds $x $y
                    $mask.SetPixel($localX, $localY, [System.Drawing.Color]::FromArgb(255, 255, 216, 0))
                } elseif ($snb) {
                    $sampleNonBlack++
                    $sampleOnly++
                    Add-BoundsPoint $sampleOnlyBounds $x $y
                    $mask.SetPixel($localX, $localY, [System.Drawing.Color]::FromArgb(255, 0, 116, 255))
                } elseif ($cnb) {
                    $candidateNonBlack++
                    $candidateOnly++
                    Add-BoundsPoint $candidateOnlyBounds $x $y
                    $mask.SetPixel($localX, $localY, [System.Drawing.Color]::FromArgb(255, 255, 64, 64))
                } else {
                    $mask.SetPixel($localX, $localY, [System.Drawing.Color]::FromArgb(255, 0, 0, 0))
                }
            }
        }

        $mask.Save($maskPath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $mask.Dispose()
    }

    $union = $shared + $sampleOnly + $candidateOnly
    [pscustomobject]@{
        region = $Region.name
        x = $x0
        y = $y0
        width = $width
        height = $height
        pixels = $pixels
        changed_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $changed / $pixels, 6) } else { 0 }
        sample_nonblack_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $sampleNonBlack / $pixels, 6) } else { 0 }
        candidate_nonblack_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $candidateNonBlack / $pixels, 6) } else { 0 }
        shared_nonblack_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $shared / $pixels, 6) } else { 0 }
        sample_only_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $sampleOnly / $pixels, 6) } else { 0 }
        candidate_only_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $candidateOnly / $pixels, 6) } else { 0 }
        nonblack_jaccard = if ($union -gt 0) { [Math]::Round([double]$shared / [double]$union, 6) } else { 1 }
        sample_star_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $sampleStars / $pixels, 6) } else { 0 }
        candidate_star_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $candidateStars / $pixels, 6) } else { 0 }
        shared_star_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $sharedStars / $pixels, 6) } else { 0 }
        sample_warm_light_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $sampleWarm / $pixels, 6) } else { 0 }
        candidate_warm_light_percent = if ($pixels -gt 0) { [Math]::Round(100.0 * $candidateWarm / $pixels, 6) } else { 0 }
        sample_only_bounds = Format-Bounds $sampleOnlyBounds
        candidate_only_bounds = Format-Bounds $candidateOnlyBounds
        shared_bounds = Format-Bounds $sharedBounds
        sample_crop_path = $sampleCropPath
        candidate_crop_path = $candidateCropPath
        mask_path = $maskPath
    }
}

function Test-RectIntersect {
    param($A, $B)

    return $A.minX -lt $B.maxX -and $A.maxX -gt $B.minX -and $A.minY -lt $B.maxY -and $A.maxY -gt $B.minY
}

function Draw-FitImage {
    param([System.Drawing.Graphics]$Graphics, [string]$ImagePath, [System.Drawing.Rectangle]$Bounds)

    $image = [System.Drawing.Bitmap]::FromFile($ImagePath)
    try {
        $scale = [Math]::Min($Bounds.Width / $image.Width, $Bounds.Height / $image.Height)
        $drawWidth = [int][Math]::Round($image.Width * $scale)
        $drawHeight = [int][Math]::Round($image.Height * $scale)
        $x = $Bounds.X + [int][Math]::Floor(($Bounds.Width - $drawWidth) / 2)
        $y = $Bounds.Y + [int][Math]::Floor(($Bounds.Height - $drawHeight) / 2)
        $Graphics.DrawImage($image, $x, $y, $drawWidth, $drawHeight)
    } finally {
        $image.Dispose()
    }
}

function Write-ContactSheet {
    param([object[]]$Rows, [string]$OutputPath)

    $cellWidth = 240
    $cellHeight = 170
    $labelWidth = 185
    $labelHeight = 54
    $padding = 10
    $columns = @("sample", "candidate", "mask")
    $width = $labelWidth + ($columns.Count * $cellWidth) + (($columns.Count + 2) * $padding)
    $height = $labelHeight + (($cellHeight + $labelHeight + $padding) * $Rows.Count) + $padding
    $sheet = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($sheet)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(24, 28, 34))
            $font = [System.Drawing.Font]::new("Segoe UI", 9)
            $bold = [System.Drawing.Font]::new("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
            $muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(190, 198, 208))
            $panel = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 41, 48))
            try {
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    $x = $labelWidth + $padding + ($i * ($cellWidth + $padding))
                    $graphics.DrawString($columns[$i], $bold, [System.Drawing.Brushes]::White, $x, 14)
                }

                $y = $labelHeight
                foreach ($row in $Rows) {
                    $graphics.DrawString($row.region, $bold, [System.Drawing.Brushes]::White, $padding, $y + 4)
                    $graphics.DrawString(("sample-only {0:N1}%" -f [double]$row.sample_only_percent), $font, $muted, $padding, $y + 24)
                    $graphics.DrawString(("stars {0:N1}%/{1:N1}%" -f [double]$row.sample_star_percent, [double]$row.candidate_star_percent), $font, $muted, $padding, $y + 40)
                    $paths = @($row.sample_crop_path, $row.candidate_crop_path, $row.mask_path)
                    for ($i = 0; $i -lt $paths.Count; $i++) {
                        $x = $labelWidth + $padding + ($i * ($cellWidth + $padding))
                        $bounds = [System.Drawing.Rectangle]::new($x, $y + $labelHeight, $cellWidth, $cellHeight)
                        $graphics.FillRectangle($panel, $bounds)
                        if (Test-Path -LiteralPath $paths[$i]) {
                            Draw-FitImage -Graphics $graphics -ImagePath $paths[$i] -Bounds $bounds
                        }
                    }

                    $y += $cellHeight + $labelHeight + $padding
                }
            } finally {
                $font.Dispose()
                $bold.Dispose()
                $muted.Dispose()
                $panel.Dispose()
            }
        } finally {
            $graphics.Dispose()
        }

        $sheet.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $sheet.Dispose()
    }
}

$candidateFullPath = Resolve-FullPath $CandidatePath
$sampleFullPath = Resolve-FullPath $SamplePath
$triangleCoverageFullPath = Resolve-FullPath $TriangleCoveragePath
$triangleSummaryFullPath = Resolve-FullPath $TriangleCoverageSummaryPath
foreach ($path in @($candidateFullPath, $sampleFullPath, $triangleCoverageFullPath, $triangleSummaryFullPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required input not found: $path"
    }
}

Add-Type -AssemblyName System.Drawing

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$regions = @(
    [pscustomobject]@{ name = "top-sky"; x = 0; y = 0; width = 640; height = 132 },
    [pscustomobject]@{ name = "left-sky"; x = 0; y = 0; width = 240; height = 220 },
    [pscustomobject]@{ name = "skyline"; x = 48; y = 72; width = 544; height = 170 },
    [pscustomobject]@{ name = "empire-building"; x = 330; y = 170; width = 160; height = 240 },
    [pscustomobject]@{ name = "right-city"; x = 430; y = 145; width = 190; height = 190 },
    [pscustomobject]@{ name = "left-void"; x = 0; y = 96; width = 220; height = 300 }
)

if ($SkipImageAnalysis) {
    $imageRows = @(
        foreach ($region in $regions) {
            [pscustomobject]@{
                region = $region.name
                x = $region.x
                y = $region.y
                width = $region.width
                height = $region.height
                pixels = ""
                changed_percent = ""
                sample_nonblack_percent = ""
                candidate_nonblack_percent = ""
                shared_nonblack_percent = ""
                sample_only_percent = ""
                candidate_only_percent = ""
                nonblack_jaccard = ""
                sample_star_percent = ""
                candidate_star_percent = ""
                shared_star_percent = ""
                sample_warm_light_percent = ""
                candidate_warm_light_percent = ""
                sample_only_bounds = ""
                candidate_only_bounds = ""
                shared_bounds = ""
                sample_crop_path = ""
                candidate_crop_path = ""
                mask_path = ""
            }
        }
    )
} else {
    $sampleImage = [System.Drawing.Bitmap]::FromFile($sampleFullPath)
    $candidateImage = [System.Drawing.Bitmap]::FromFile($candidateFullPath)
    try {
        $imageRows = @(
            foreach ($region in $regions) {
                Analyze-ImageRegion -Sample $sampleImage -Candidate $candidateImage -Region $region -OutputRoot $runRoot -NonBlackThreshold $NonBlackThreshold -ChangeThreshold $ChangeThreshold
            }
        )
    } finally {
        $sampleImage.Dispose()
        $candidateImage.Dispose()
    }
}

$summaryByTriangle = @{}
Import-Csv -LiteralPath $triangleSummaryFullPath | ForEach-Object {
    $key = "{0}|{1}" -f (Get-ObjectValue $_ "draw_index"), (Get-ObjectValue $_ "triangle_index")
    if (-not $summaryByTriangle.ContainsKey($key)) {
        $summaryByTriangle[$key] = $_
    }
}

$reasonTables = @{}
$textureTables = @{}
foreach ($region in $regions) {
    $reasonTables[$region.name] = @{}
    $textureTables[$region.name] = @{}
}

Import-Csv -LiteralPath $triangleCoverageFullPath | ForEach-Object {
    $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_index")
    if ($null -ne $draw -and $draw -ge $DrawStart -and $draw -le $DrawEnd) {
        $minX = Convert-ToNullableDouble (Get-ObjectValue $_ "screen_min_x")
        $maxX = Convert-ToNullableDouble (Get-ObjectValue $_ "screen_max_x")
        $minY = Convert-ToNullableDouble (Get-ObjectValue $_ "screen_min_y")
        $maxY = Convert-ToNullableDouble (Get-ObjectValue $_ "screen_max_y")
        if ($null -ne $minX -and $null -ne $maxX -and $null -ne $minY -and $null -ne $maxY) {
            $triRect = [pscustomobject]@{ minX = $minX; maxX = $maxX; minY = $minY; maxY = $maxY }
            foreach ($region in $regions) {
                $regionRect = [pscustomobject]@{
                    minX = [double]$region.x
                    maxX = [double]($region.x + $region.width)
                    minY = [double]$region.y
                    maxY = [double]($region.y + $region.height)
                }

                if (-not (Test-RectIntersect $triRect $regionRect)) {
                    continue
                }

                $reason = Get-ObjectValue $_ "reason" "unknown"
                if (-not $reasonTables[$region.name].ContainsKey($reason)) {
                    $reasonTables[$region.name][$reason] = [pscustomobject]@{ region = $region.name; reason = $reason; rows = 0; color_writes = 0L; black_color_writes = 0L; covered_pixels = 0L }
                }

                $colorWritesValue = Convert-ToNullableInt64 (Get-ObjectValue $_ "color_writes")
                $blackWritesValue = Convert-ToNullableInt64 (Get-ObjectValue $_ "black_color_writes")
                $coveredValue = Convert-ToNullableInt64 (Get-ObjectValue $_ "covered_pixels")
                $colorWrites = if ($null -ne $colorWritesValue) { [int64]$colorWritesValue } else { 0L }
                $blackWrites = if ($null -ne $blackWritesValue) { [int64]$blackWritesValue } else { 0L }
                $covered = if ($null -ne $coveredValue) { [int64]$coveredValue } else { 0L }

                $reasonRow = $reasonTables[$region.name][$reason]
                $reasonRow.rows++
                $reasonRow.color_writes += $colorWrites
                $reasonRow.black_color_writes += $blackWrites
                $reasonRow.covered_pixels += $covered

                if ((Get-ObjectValue $_ "rendered") -eq "True") {
                    $summaryKey = "{0}|{1}" -f (Get-ObjectValue $_ "draw_index"), (Get-ObjectValue $_ "triangle_index")
                    $summary = if ($summaryByTriangle.ContainsKey($summaryKey)) { $summaryByTriangle[$summaryKey] } else { $null }
                    $texture = Get-ObjectValue $summary "texture_address" "(unknown)"
                    if ([string]::IsNullOrWhiteSpace($texture)) {
                        $texture = "(none)"
                    }

                    $key = "{0}|{1}|{2}" -f $texture, (Get-ObjectValue $summary "texture_format"), (Get-ObjectValue $_ "draw_index")
                    if (-not $textureTables[$region.name].ContainsKey($key)) {
                        $textureTables[$region.name][$key] = [pscustomobject]@{
                            region = $region.name
                            texture_address = $texture
                            texture_format = Get-ObjectValue $summary "texture_format"
                            draws = New-Object System.Collections.Generic.SortedSet[int]
                            rows = 0
                            color_writes = 0L
                            black_color_writes = 0L
                            covered_pixels = 0L
                            sample_tev_rgba = Get-ObjectValue $summary "sample_tev_rgba"
                            texture_mip_samples = Get-ObjectValue $summary "texture_mip_samples"
                        }
                    }

                    $textureRow = $textureTables[$region.name][$key]
                    [void]$textureRow.draws.Add([int]$draw)
                    $textureRow.rows++
                    $textureRow.color_writes += $colorWrites
                    $textureRow.black_color_writes += $blackWrites
                    $textureRow.covered_pixels += $covered
                }
            }
        }
    }
}

$reasonRows = foreach ($region in $regions) {
    $reasonTables[$region.name].Values |
        Sort-Object @{ Expression = { $_.rows }; Descending = $true }, reason
}

$textureRows = foreach ($region in $regions) {
    $textureTables[$region.name].Values |
        Sort-Object @{ Expression = { $_.color_writes }; Descending = $true }, texture_address |
        ForEach-Object {
            [pscustomobject]@{
                region = $_.region
                texture_address = $_.texture_address
                texture_format = $_.texture_format
                draws = (@($_.draws | ForEach-Object { $_ }) -join "|")
                rows = $_.rows
                color_writes = $_.color_writes
                black_color_writes = $_.black_color_writes
                black_write_ratio = if ($_.color_writes -gt 0) { [Math]::Round([double]$_.black_color_writes / [double]$_.color_writes, 6) } else { 0 }
                covered_pixels = $_.covered_pixels
                sample_tev_rgba = $_.sample_tev_rgba
                texture_mip_samples = $_.texture_mip_samples
            }
        }
}

$imageCsvPath = Join-Path $runRoot "background-region-image-metrics.csv"
$reasonCsvPath = Join-Path $runRoot "background-region-triangle-reasons.csv"
$textureCsvPath = Join-Path $runRoot "background-region-textures.csv"
$contactSheetPath = Join-Path $runRoot "background-attribution-contact-sheet.png"
$jsonPath = Join-Path $runRoot "sonic-background-attribution-report.json"

$imageRows | Export-Csv -LiteralPath $imageCsvPath -NoTypeInformation -Encoding UTF8
$reasonRows | Export-Csv -LiteralPath $reasonCsvPath -NoTypeInformation -Encoding UTF8
$textureRows | Export-Csv -LiteralPath $textureCsvPath -NoTypeInformation -Encoding UTF8

if (-not $SkipImageAnalysis) {
    Write-ContactSheet -Rows $imageRows -OutputPath $contactSheetPath
}

$topSampleOnly = $imageRows | Sort-Object sample_only_percent -Descending | Select-Object -First 1
$topTextureRows = @($textureRows | Sort-Object @{ Expression = { [int64]$_.color_writes }; Descending = $true } | Select-Object -First 12)

[pscustomobject]@{
    schema = "ngcsharp.sonic-background-attribution.v1"
    candidatePath = $candidateFullPath
    samplePath = $sampleFullPath
    triangleCoveragePath = $triangleCoverageFullPath
    triangleCoverageSummaryPath = $triangleSummaryFullPath
    drawStart = $DrawStart
    drawEnd = $DrawEnd
    topMissingRegion = if ($null -ne $topSampleOnly) { $topSampleOnly.region } else { "" }
    topMissingRegionSampleOnlyPercent = if ($null -ne $topSampleOnly) { $topSampleOnly.sample_only_percent } else { "" }
    topSkySampleStarPercent = ($imageRows | Where-Object { $_.region -eq "top-sky" } | Select-Object -First 1).sample_star_percent
    topSkyCandidateStarPercent = ($imageRows | Where-Object { $_.region -eq "top-sky" } | Select-Object -First 1).candidate_star_percent
    imageMetricsCsvPath = $imageCsvPath
    triangleReasonCsvPath = $reasonCsvPath
    textureCsvPath = $textureCsvPath
    contactSheetPath = if (-not $SkipImageAnalysis -and (Test-Path -LiteralPath $contactSheetPath)) { $contactSheetPath } else { $null }
    topTextureRows = $topTextureRows
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic background attribution report: $jsonPath"
$imageRows | Select-Object region,sample_only_percent,candidate_only_percent,sample_star_percent,candidate_star_percent,sample_warm_light_percent,candidate_warm_light_percent,nonblack_jaccard | Format-Table -AutoSize
$reasonRows | Select-Object -First 18 region,reason,rows,color_writes,black_color_writes,covered_pixels | Format-Table -AutoSize
$textureRows | Select-Object -First 18 region,texture_address,draws,color_writes,black_color_writes,black_write_ratio | Format-Table -AutoSize
