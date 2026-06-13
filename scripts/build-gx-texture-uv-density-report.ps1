param(
    [Parameter(Mandatory = $true)]
    [string]$VertexCsvPath,
    [Parameter(Mandatory = $true)]
    [int]$DrawIndex,
    [Parameter(Mandatory = $true)]
    [string]$TexturePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$FrameWidth = 640,
    [int]$FrameHeight = 480,
    [ValidateSet("clamp", "repeat", "mirror")]
    [string]$WrapS = "repeat",
    [ValidateSet("clamp", "repeat", "mirror")]
    [string]$WrapT = "repeat",
    [string]$RegionName = "full",
    [int]$RegionX = 0,
    [int]$RegionY = 0,
    [int]$RegionWidth = 0,
    [int]$RegionHeight = 0,
    [int]$PhaseSweepSteps = 0,
    [switch]$AffineTexcoords
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-Double {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [double]::NaN
    }

    return [double]::Parse($Text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Positive-Modulo {
    param([int]$Value, [int]$Divisor)

    $result = $Value % $Divisor
    if ($result -lt 0) {
        $result += $Divisor
    }

    return $result
}

function Mirror-Index {
    param([int]$Index, [int]$Size)

    $period = $Size * 2
    $wrapped = Positive-Modulo $Index $period
    if ($wrapped -ge $Size) {
        return $period - $wrapped - 1
    }

    return $wrapped
}

function Texture-Index {
    param([double]$Coordinate, [int]$Size, [string]$Wrap)

    if ($Size -le 1 -or [double]::IsNaN($Coordinate) -or [double]::IsInfinity($Coordinate)) {
        return 0
    }

    switch ($Wrap) {
        "repeat" {
            $normalized = $Coordinate - [Math]::Floor($Coordinate)
            break
        }
        "mirror" {
            $period = [Math]::Floor($Coordinate)
            $fraction = $Coordinate - $period
            $normalized = if (([int64]$period % 2) -eq 0) { $fraction } else { 1.0 - $fraction }
            break
        }
        default {
            $normalized = [Math]::Min(1.0, [Math]::Max(0.0, $Coordinate))
            break
        }
    }

    return [Math]::Min($Size - 1, [Math]::Max(0, [int][Math]::Floor($normalized * $Size)))
}

function Edge {
    param($Ax, $Ay, $Bx, $By, $Px, $Py)

    return (($Px - $Ax) * ($By - $Ay)) - (($Py - $Ay) * ($Bx - $Ax))
}

function Is-Finite {
    param([double]$Value)

    return -not [double]::IsNaN($Value) -and -not [double]::IsInfinity($Value)
}

$vertexCsvFullPath = Resolve-FullPath $VertexCsvPath
$textureFullPath = Resolve-FullPath $TexturePath
$outputFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null
$regionLeft = if ($RegionWidth -gt 0 -and $RegionHeight -gt 0) { [Math]::Max(0, $RegionX) } else { 0 }
$regionTop = if ($RegionWidth -gt 0 -and $RegionHeight -gt 0) { [Math]::Max(0, $RegionY) } else { 0 }
$regionRight = if ($RegionWidth -gt 0 -and $RegionHeight -gt 0) { [Math]::Min($FrameWidth - 1, $RegionX + $RegionWidth - 1) } else { $FrameWidth - 1 }
$regionBottom = if ($RegionWidth -gt 0 -and $RegionHeight -gt 0) { [Math]::Min($FrameHeight - 1, $RegionY + $RegionHeight - 1) } else { $FrameHeight - 1 }
if ($regionLeft -gt $regionRight -or $regionTop -gt $regionBottom) {
    throw "Empty region '$RegionName': x=$RegionX y=$RegionY width=$RegionWidth height=$RegionHeight."
}

Add-Type -AssemblyName System.Drawing
$texture = [System.Drawing.Bitmap]::FromFile($textureFullPath)
try {
    $textureWidth = $texture.Width
    $textureHeight = $texture.Height
    $textureNonBlack = New-Object 'bool[,]' $textureWidth, $textureHeight
    $textureNonBlackCount = 0
    for ($y = 0; $y -lt $textureHeight; $y++) {
        for ($x = 0; $x -lt $textureWidth; $x++) {
            $pixel = $texture.GetPixel($x, $y)
            $nonBlack = $pixel.R -ne 0 -or $pixel.G -ne 0 -or $pixel.B -ne 0
            $textureNonBlack[$x, $y] = $nonBlack
            if ($nonBlack) {
                $textureNonBlackCount++
            }
        }
    }

    $vertices = Import-Csv $vertexCsvFullPath |
        Where-Object { [int]$_.draw_index -eq $DrawIndex -and $_.decoded -eq "True" } |
        Sort-Object { [int]$_.vertex_index } |
        ForEach-Object {
            [pscustomobject]@{
                Index = [int]$_.vertex_index
                X = Parse-Double $_.screen_x
                Y = Parse-Double $_.screen_y
                InvW = Parse-Double $_.inv_w
                S = Parse-Double $_.tex0_s
                T = Parse-Double $_.tex0_t
            }
        }

    if ($vertices.Count -lt 3) {
        throw "Draw $DrawIndex has fewer than three decoded vertices in $vertexCsvFullPath."
    }

    $hits = New-Object 'int[,]' $textureWidth, $textureHeight
    $screenHits = New-Object 'byte[,]' $FrameWidth, $FrameHeight
    $trianglesVisited = 0
    $trianglesWithCoverage = 0
    $samples = 0
    $sampledNonBlack = 0
    $sampledBlack = 0
    $sampleSValues = New-Object 'System.Collections.Generic.List[double]'
    $sampleTValues = New-Object 'System.Collections.Generic.List[double]'

    for ($i = 2; $i -lt $vertices.Count; $i++) {
        $a = $vertices[$i - 2]
        $b = $vertices[$i - 1]
        $c = $vertices[$i]
        $trianglesVisited++

        if (!(Is-Finite $a.X) -or !(Is-Finite $a.Y) -or !(Is-Finite $a.S) -or !(Is-Finite $a.T) -or
            !(Is-Finite $b.X) -or !(Is-Finite $b.Y) -or !(Is-Finite $b.S) -or !(Is-Finite $b.T) -or
            !(Is-Finite $c.X) -or !(Is-Finite $c.Y) -or !(Is-Finite $c.S) -or !(Is-Finite $c.T)) {
            continue
        }

        $area = Edge $a.X $a.Y $b.X $b.Y $c.X $c.Y
        if ([Math]::Abs($area) -lt 0.000001) {
            continue
        }

        $minX = [Math]::Max($regionLeft, [int][Math]::Floor([Math]::Min($a.X, [Math]::Min($b.X, $c.X))))
        $maxX = [Math]::Min($regionRight, [int][Math]::Ceiling([Math]::Max($a.X, [Math]::Max($b.X, $c.X))))
        $minY = [Math]::Max($regionTop, [int][Math]::Floor([Math]::Min($a.Y, [Math]::Min($b.Y, $c.Y))))
        $maxY = [Math]::Min($regionBottom, [int][Math]::Ceiling([Math]::Max($a.Y, [Math]::Max($b.Y, $c.Y))))
        if ($minX -gt $maxX -or $minY -gt $maxY) {
            continue
        }

        $triangleSamples = 0
        for ($py = $minY; $py -le $maxY; $py++) {
            for ($px = $minX; $px -le $maxX; $px++) {
                $sampleX = $px + 0.5
                $sampleY = $py + 0.5
                $w0 = (Edge $b.X $b.Y $c.X $c.Y $sampleX $sampleY) / $area
                $w1 = (Edge $c.X $c.Y $a.X $a.Y $sampleX $sampleY) / $area
                $w2 = 1.0 - $w0 - $w1
                if ($w0 -lt -0.00001 -or $w1 -lt -0.00001 -or $w2 -lt -0.00001) {
                    continue
                }

                $s = ($a.S * $w0) + ($b.S * $w1) + ($c.S * $w2)
                $t = ($a.T * $w0) + ($b.T * $w1) + ($c.T * $w2)
                $perspectiveWeight = ($a.InvW * $w0) + ($b.InvW * $w1) + ($c.InvW * $w2)
                if (!$AffineTexcoords -and [Math]::Abs($perspectiveWeight) -gt 0.000001) {
                    $s = (($a.S * $a.InvW * $w0) + ($b.S * $b.InvW * $w1) + ($c.S * $c.InvW * $w2)) / $perspectiveWeight
                    $t = (($a.T * $a.InvW * $w0) + ($b.T * $b.InvW * $w1) + ($c.T * $c.InvW * $w2)) / $perspectiveWeight
                }

                $tx = Texture-Index $s $textureWidth $WrapS
                $ty = Texture-Index $t $textureHeight $WrapT
                $hits[$tx, $ty]++
                $samples++
                [void]$sampleSValues.Add($s)
                [void]$sampleTValues.Add($t)
                $triangleSamples++
                if ($textureNonBlack[$tx, $ty]) {
                    $sampledNonBlack++
                    $screenHits[$px, $py] = 2
                } else {
                    $sampledBlack++
                    if ($screenHits[$px, $py] -eq 0) {
                        $screenHits[$px, $py] = 1
                    }
                }
            }
        }

        if ($triangleSamples -gt 0) {
            $trianglesWithCoverage++
        }
    }

    $uniqueTexels = 0
    $hitNonBlackTexels = 0
    $maxHit = 0
    $topTexels = New-Object System.Collections.Generic.List[object]
    for ($y = 0; $y -lt $textureHeight; $y++) {
        for ($x = 0; $x -lt $textureWidth; $x++) {
            $count = $hits[$x, $y]
            if ($count -le 0) {
                continue
            }

            $uniqueTexels++
            if ($textureNonBlack[$x, $y]) {
                $hitNonBlackTexels++
            }
            if ($count -gt $maxHit) {
                $maxHit = $count
            }
            $topTexels.Add([pscustomobject]@{ x = $x; y = $y; samples = $count; non_black = ($textureNonBlack[$x, $y]) })
        }
    }

    $coveredScreenPixels = 0
    for ($y = 0; $y -lt $FrameHeight; $y++) {
        for ($x = 0; $x -lt $FrameWidth; $x++) {
            if ($screenHits[$x, $y] -ne 0) {
                $coveredScreenPixels++
            }
        }
    }

    $screenMap = New-Object System.Drawing.Bitmap $FrameWidth, $FrameHeight, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        for ($y = 0; $y -lt $FrameHeight; $y++) {
            for ($x = 0; $x -lt $FrameWidth; $x++) {
                switch ($screenHits[$x, $y]) {
                    2 { $screenMap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(120, 255, 170)); break }
                    1 { $screenMap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(210, 82, 18)); break }
                    default { $screenMap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0)); break }
                }
            }
        }

        $safeRegionName = $RegionName -replace '[^A-Za-z0-9_.-]', '-'
        $screenMapPath = Join-Path $outputFullPath "draw-$DrawIndex-$safeRegionName-screen-sample-map.png"
        $screenMap.Save($screenMapPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $screenMap.Dispose()
    }

    $heatmap = New-Object System.Drawing.Bitmap $textureWidth, $textureHeight, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        for ($y = 0; $y -lt $textureHeight; $y++) {
            for ($x = 0; $x -lt $textureWidth; $x++) {
                $count = $hits[$x, $y]
                if ($count -le 0 -or $maxHit -le 0) {
                    $heatmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0))
                    continue
                }

                $intensity = [int][Math]::Round(255.0 * ([Math]::Log(1.0 + $count) / [Math]::Log(1.0 + $maxHit)))
                if ($textureNonBlack[$x, $y]) {
                    $heatmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($intensity, 255, $intensity))
                } else {
                    $heatmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($intensity, [int]($intensity * 0.22), 0))
                }
            }
        }

        $safeRegionName = $RegionName -replace '[^A-Za-z0-9_.-]', '-'
        $heatmapPath = Join-Path $outputFullPath "draw-$DrawIndex-$safeRegionName-uv-heatmap.png"
        $heatmap.Save($heatmapPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $heatmap.Dispose()
    }

    $top = $topTexels |
        Sort-Object -Property samples -Descending |
        Select-Object -First 20

    $phaseSweepCsvPath = $null
    $topPhaseSweeps = @()
    if ($PhaseSweepSteps -gt 1 -and $sampleSValues.Count -gt 0) {
        $phaseRows = New-Object System.Collections.Generic.List[object]
        for ($sStep = 0; $sStep -lt $PhaseSweepSteps; $sStep++) {
            $sOffset = $sStep / [double]$PhaseSweepSteps
            for ($tStep = 0; $tStep -lt $PhaseSweepSteps; $tStep++) {
                $tOffset = $tStep / [double]$PhaseSweepSteps
                $nonBlackPixels = 0
                for ($sampleIndex = 0; $sampleIndex -lt $sampleSValues.Count; $sampleIndex++) {
                    $tx = Texture-Index ($sampleSValues[$sampleIndex] + $sOffset) $textureWidth $WrapS
                    $ty = Texture-Index ($sampleTValues[$sampleIndex] + $tOffset) $textureHeight $WrapT
                    if ($textureNonBlack[$tx, $ty]) {
                        $nonBlackPixels++
                    }
                }

                $phaseRows.Add([pscustomobject]@{
                    s_offset = $sOffset.ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
                    t_offset = $tOffset.ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
                    sampled_pixels = $sampleSValues.Count
                    sampled_non_black_pixels = $nonBlackPixels
                    sampled_black_pixels = $sampleSValues.Count - $nonBlackPixels
                    sampled_non_black_pixel_ratio = if ($sampleSValues.Count -eq 0) { 0 } else { $nonBlackPixels / [double]$sampleSValues.Count }
                })
            }
        }

        $safeRegionName = $RegionName -replace '[^A-Za-z0-9_.-]', '-'
        $phaseSweepCsvPath = Join-Path $outputFullPath "draw-$DrawIndex-$safeRegionName-phase-sweep.csv"
        $phaseRows | Sort-Object -Property sampled_non_black_pixel_ratio -Descending | Export-Csv -Path $phaseSweepCsvPath -NoTypeInformation
        $topPhaseSweeps = @($phaseRows | Sort-Object -Property sampled_non_black_pixel_ratio -Descending | Select-Object -First 20)
    }

    $summary = [pscustomobject]@{
        draw_index = $DrawIndex
        region = $RegionName
        region_x = $regionLeft
        region_y = $regionTop
        region_width = $regionRight - $regionLeft + 1
        region_height = $regionBottom - $regionTop + 1
        vertex_csv = $vertexCsvFullPath
        texture = $textureFullPath
        texture_width = $textureWidth
        texture_height = $textureHeight
        texture_non_black_texels = $textureNonBlackCount
        texture_non_black_ratio = if ($textureWidth * $textureHeight -eq 0) { 0 } else { $textureNonBlackCount / ($textureWidth * $textureHeight) }
        frame_width = $FrameWidth
        frame_height = $FrameHeight
        wrap_s = $WrapS
        wrap_t = $WrapT
        affine_texcoords = [bool]$AffineTexcoords
        vertices = $vertices.Count
        triangles_visited = $trianglesVisited
        triangles_with_coverage = $trianglesWithCoverage
        covered_screen_pixels = $coveredScreenPixels
        samples = $samples
        unique_sampled_texels = $uniqueTexels
        sampled_non_black_texels = $hitNonBlackTexels
        sampled_non_black_texel_ratio = if ($uniqueTexels -eq 0) { 0 } else { $hitNonBlackTexels / $uniqueTexels }
        sampled_non_black_pixels = $sampledNonBlack
        sampled_black_pixels = $sampledBlack
        sampled_non_black_pixel_ratio = if ($samples -eq 0) { 0 } else { $sampledNonBlack / $samples }
        heatmap = $heatmapPath
        screen_sample_map = $screenMapPath
        phase_sweep_steps = $PhaseSweepSteps
        phase_sweep_csv = $phaseSweepCsvPath
        top_phase_sweeps = $topPhaseSweeps
        top_sampled_texels = $top
    }

    $safeRegionName = $RegionName -replace '[^A-Za-z0-9_.-]', '-'
    $jsonPath = Join-Path $outputFullPath "draw-$DrawIndex-$safeRegionName-uv-density-summary.json"
    $summary | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding UTF8
    $summary | Format-List
}
finally {
    $texture.Dispose()
}
