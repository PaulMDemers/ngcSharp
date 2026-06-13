param(
    [Parameter(Mandatory = $true)]
    [string]$SamplePath,
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$NonBlackThreshold = 8,
    [int]$ChangeThreshold = 8,
    [int]$Stride = 1
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
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

function Percent {
    param([long]$Value, [long]$Total)

    if ($Total -eq 0) {
        return ""
    }

    return (100.0 * $Value / $Total).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
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

    $crop = $Image.Clone([System.Drawing.Rectangle]::new($X, $Y, $Width, $Height), $Image.PixelFormat)
    try {
        $crop.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $crop.Dispose()
    }
}

$sampleFullPath = Resolve-FullPath $SamplePath
$candidateFullPath = Resolve-FullPath $CandidatePath
$outputFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$regions = @(
    [pscustomobject]@{ name = "top-sky"; x = 0; y = 0; width = 640; height = 132 },
    [pscustomobject]@{ name = "left-sky"; x = 0; y = 0; width = 240; height = 220 },
    [pscustomobject]@{ name = "skyline"; x = 48; y = 72; width = 544; height = 170 },
    [pscustomobject]@{ name = "empire-building"; x = 330; y = 170; width = 160; height = 240 },
    [pscustomobject]@{ name = "right-city"; x = 430; y = 145; width = 190; height = 190 },
    [pscustomobject]@{ name = "left-void"; x = 0; y = 96; width = 220; height = 300 }
)

$sample = [System.Drawing.Bitmap]::FromFile($sampleFullPath)
$candidate = [System.Drawing.Bitmap]::FromFile($candidateFullPath)
try {
    $rows = foreach ($region in $regions) {
        $x0 = [Math]::Max(0, [int]$region.x)
        $y0 = [Math]::Max(0, [int]$region.y)
        $width = [Math]::Min([int]$region.width, [Math]::Min($sample.Width, $candidate.Width) - $x0)
        $height = [Math]::Min([int]$region.height, [Math]::Min($sample.Height, $candidate.Height) - $y0)
        $regionRoot = Join-Path $outputFullPath $region.name
        New-Item -ItemType Directory -Force -Path $regionRoot | Out-Null
        Save-Crop -Image $sample -Path (Join-Path $regionRoot "sample.png") -X $x0 -Y $y0 -Width $width -Height $height
        Save-Crop -Image $candidate -Path (Join-Path $regionRoot "candidate.png") -X $x0 -Y $y0 -Width $width -Height $height

        $pixels = 0L
        $changed = 0L
        $sampleNonBlack = 0L
        $candidateNonBlack = 0L
        $sharedNonBlack = 0L
        $sampleOnly = 0L
        $candidateOnly = 0L
        $sampleStars = 0L
        $candidateStars = 0L
        $sharedStars = 0L
        $sampleWarm = 0L
        $candidateWarm = 0L

        for ($localY = 0; $localY -lt $height; $localY += $Stride) {
            $y = $y0 + $localY
            for ($localX = 0; $localX -lt $width; $localX += $Stride) {
                $x = $x0 + $localX
                $pixels++
                $s = $sample.GetPixel($x, $y)
                $c = $candidate.GetPixel($x, $y)
                $snb = Test-NonBlack $s $NonBlackThreshold
                $cnb = Test-NonBlack $c $NonBlackThreshold
                $delta = [Math]::Abs([int]$s.R - [int]$c.R) + [Math]::Abs([int]$s.G - [int]$c.G) + [Math]::Abs([int]$s.B - [int]$c.B)
                if ($delta -gt $ChangeThreshold) { $changed++ }
                if ($snb) { $sampleNonBlack++ }
                if ($cnb) { $candidateNonBlack++ }
                if ($snb -and $cnb) { $sharedNonBlack++ }
                if ($snb -and !$cnb) { $sampleOnly++ }
                if (!$snb -and $cnb) { $candidateOnly++ }
                $ss = Test-StarLike $s
                $cs = Test-StarLike $c
                if ($ss) { $sampleStars++ }
                if ($cs) { $candidateStars++ }
                if ($ss -and $cs) { $sharedStars++ }
                if (Test-WarmBuildingLight $s) { $sampleWarm++ }
                if (Test-WarmBuildingLight $c) { $candidateWarm++ }
            }
        }

        [pscustomobject]@{
            region = $region.name
            x = $x0
            y = $y0
            width = $width
            height = $height
            pixels = $pixels
            changed_percent = Percent $changed $pixels
            sample_nonblack_percent = Percent $sampleNonBlack $pixels
            candidate_nonblack_percent = Percent $candidateNonBlack $pixels
            sample_only_percent = Percent $sampleOnly $pixels
            candidate_only_percent = Percent $candidateOnly $pixels
            nonblack_jaccard = if (($sampleNonBlack + $candidateOnly) -eq 0) { "" } else { ($sharedNonBlack / ($sampleNonBlack + $candidateOnly)).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture) }
            sample_star_percent = Percent $sampleStars $pixels
            candidate_star_percent = Percent $candidateStars $pixels
            shared_star_percent = Percent $sharedStars $pixels
            sample_warm_light_percent = Percent $sampleWarm $pixels
            candidate_warm_light_percent = Percent $candidateWarm $pixels
        }
    }

    $csvPath = Join-Path $outputFullPath "image-region-metrics.csv"
    $jsonPath = Join-Path $outputFullPath "image-region-metrics.json"
    $rows | Export-Csv -Path $csvPath -NoTypeInformation
    [pscustomobject]@{
        sample = $sampleFullPath
        candidate = $candidateFullPath
        csv = $csvPath
        regions = $rows
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath -Encoding UTF8
    $rows | Format-Table -AutoSize
}
finally {
    $sample.Dispose()
    $candidate.Dispose()
}
