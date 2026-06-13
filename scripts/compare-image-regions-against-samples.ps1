param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$SampleDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$Stride = 4,
    [string]$Pattern = "*.png"
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

function Get-RegionDelta {
    param(
        [System.Drawing.Bitmap]$Sample,
        [System.Drawing.Bitmap]$Candidate,
        $Region,
        [int]$Stride
    )

    $x0 = [int]$Region.x
    $y0 = [int]$Region.y
    $width = [int]$Region.width
    $height = [int]$Region.height
    $sum = 0L
    $count = 0L
    for ($y = $y0; $y -lt $y0 + $height; $y += $Stride) {
        for ($x = $x0; $x -lt $x0 + $width; $x += $Stride) {
            $samplePixel = $Sample.GetPixel($x, $y)
            $candidatePixel = $Candidate.GetPixel($x, $y)
            $sum += [Math]::Abs([int]$samplePixel.R - [int]$candidatePixel.R)
            $sum += [Math]::Abs([int]$samplePixel.G - [int]$candidatePixel.G)
            $sum += [Math]::Abs([int]$samplePixel.B - [int]$candidatePixel.B)
            $count++
        }
    }

    if ($count -eq 0) {
        return 0.0
    }

    return [double]$sum / (3.0 * 255.0 * $count)
}

$candidateFullPath = Resolve-FullPath $CandidatePath
$sampleFullPath = Resolve-FullPath $SampleDirectory
$outputFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$regions = @(
    [pscustomobject]@{ name = "top_sky"; x = 0; y = 0; width = 640; height = 132 },
    [pscustomobject]@{ name = "left_sky"; x = 0; y = 0; width = 240; height = 220 },
    [pscustomobject]@{ name = "skyline"; x = 48; y = 72; width = 544; height = 170 },
    [pscustomobject]@{ name = "empire_building"; x = 330; y = 170; width = 160; height = 240 },
    [pscustomobject]@{ name = "right_city"; x = 430; y = 145; width = 190; height = 190 },
    [pscustomobject]@{ name = "left_void"; x = 0; y = 96; width = 220; height = 300 }
)

$candidate = [System.Drawing.Bitmap]::FromFile($candidateFullPath)
try {
    $rows = foreach ($sampleFile in (Get-ChildItem -LiteralPath $sampleFullPath -Filter $Pattern | Sort-Object Name)) {
        $sample = [System.Drawing.Bitmap]::FromFile($sampleFile.FullName)
        try {
            $regionScores = [ordered]@{}
            $sum = 0.0
            foreach ($region in $regions) {
                $score = Get-RegionDelta -Sample $sample -Candidate $candidate -Region $region -Stride $Stride
                $regionScores[$region.name] = [Math]::Round($score, 6)
                $sum += $score
            }

            [pscustomobject]@{
                sample = $sampleFile.Name
                score = [Math]::Round($sum / $regions.Count, 6)
                top_sky = $regionScores.top_sky
                left_sky = $regionScores.left_sky
                skyline = $regionScores.skyline
                empire_building = $regionScores.empire_building
                right_city = $regionScores.right_city
                left_void = $regionScores.left_void
            }
        }
        finally {
            $sample.Dispose()
        }
    }

    $csvPath = Join-Path $outputFullPath "regional-sample-match.csv"
    $jsonPath = Join-Path $outputFullPath "regional-sample-match.json"
    $rows | Export-Csv -Path $csvPath -NoTypeInformation
    [pscustomobject]@{
        candidate = $candidateFullPath
        sample_directory = $sampleFullPath
        stride = $Stride
        csv = $csvPath
        best = @($rows | Sort-Object score | Select-Object -First 10)
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding UTF8
    $rows | Sort-Object score | Select-Object -First 12 | Format-Table -AutoSize
}
finally {
    $candidate.Dispose()
}
