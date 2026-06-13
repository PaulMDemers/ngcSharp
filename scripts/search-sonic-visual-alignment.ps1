param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$SampleDirectory,
    [string]$OutputDirectory = "artifacts/sonic-visual-alignment",
    [int]$MaxShiftX = 160,
    [int]$MaxShiftY = 120,
    [int]$Step = 8,
    [int]$PixelStride = 2,
    [double]$MinSampleNonBlackPercent = 1.0,
    [double]$MinCandidateNonBlackPercent = 1.0,
    [string]$RegionName = "",
    [int]$RegionX = 0,
    [int]$RegionY = 0,
    [int]$RegionWidth = 0,
    [int]$RegionHeight = 0,
    [int]$Top = 24
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Save-Crop {
    param(
        [string]$InputPath,
        [string]$OutputPath,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )

    $image = [System.Drawing.Bitmap]::FromFile($InputPath)
    try {
        $rect = [System.Drawing.Rectangle]::FromLTRB(
            [Math]::Max(0, $X),
            [Math]::Max(0, $Y),
            [Math]::Min($image.Width, $X + $Width),
            [Math]::Min($image.Height, $Y + $Height))

        if ($rect.Width -le 0 -or $rect.Height -le 0) {
            return $false
        }

        $crop = $image.Clone($rect, $image.PixelFormat)
        try {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
            $crop.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $crop.Dispose()
        }
    } finally {
        $image.Dispose()
    }

    return $true
}

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SonicVisualShiftComparer
{
    public static ShiftCompareResult[] Search(
        string samplePath,
        string candidatePath,
        int sampleX,
        int sampleY,
        int candidateBaseX,
        int candidateBaseY,
        int width,
        int height,
        int maxShiftX,
        int maxShiftY,
        int step,
        int pixelStride)
    {
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException("step");
        }

        if (pixelStride <= 0)
        {
            throw new ArgumentOutOfRangeException("pixelStride");
        }

        using (var sample = Load32(samplePath))
        using (var candidate = Load32(candidatePath))
        {
            if (sampleX < 0 || sampleY < 0 ||
                sampleX + width > sample.Width || sampleY + height > sample.Height)
            {
                return new ShiftCompareResult[0];
            }

            ImageBytes sampleBytes = ReadBytes(sample);
            ImageBytes candidateBytes = ReadBytes(candidate);
            var results = new List<ShiftCompareResult>();

            for (int dy = -maxShiftY; dy <= maxShiftY; dy += step)
            {
                int candidateY = candidateBaseY + dy;
                if (candidateY < 0 || candidateY + height > candidate.Height)
                {
                    continue;
                }

                for (int dx = -maxShiftX; dx <= maxShiftX; dx += step)
                {
                    int candidateX = candidateBaseX + dx;
                    if (candidateX < 0 || candidateX + width > candidate.Width)
                    {
                        continue;
                    }

                    results.Add(CompareBytes(sampleBytes, candidateBytes, sampleX, sampleY, candidateX, candidateY, width, height, dx, dy, pixelStride));
                }
            }

            return results.ToArray();
        }
    }

    private static Bitmap Load32(string path)
    {
        using (var source = new Bitmap(path))
        {
            var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(target))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            return target;
        }
    }

    private static ImageBytes ReadBytes(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new ImageBytes(bytes, bitmap.Width, bitmap.Height, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static ShiftCompareResult CompareBytes(
        ImageBytes sample,
        ImageBytes candidate,
        int sampleX,
        int sampleY,
        int candidateX,
        int candidateY,
        int width,
        int height,
        int dx,
        int dy,
        int pixelStride)
    {
        long changed = 0;
        long delta = 0;
        long pixels = 0;
        long sampleNonBlack = 0;
        long candidateNonBlack = 0;
        long sampleLuma = 0;
        long candidateLuma = 0;

        for (int y = 0; y < height; y += pixelStride)
        {
            int sampleRow = (sampleY + y) * sample.Stride;
            int candidateRow = (candidateY + y) * candidate.Stride;

            for (int x = 0; x < width; x += pixelStride)
            {
                int si = sampleRow + ((sampleX + x) * 4);
                int ci = candidateRow + ((candidateX + x) * 4);
                int sb = sample.Bytes[si];
                int sg = sample.Bytes[si + 1];
                int sr = sample.Bytes[si + 2];
                int cb = candidate.Bytes[ci];
                int cg = candidate.Bytes[ci + 1];
                int cr = candidate.Bytes[ci + 2];
                int db = Math.Abs(sb - cb);
                int dg = Math.Abs(sg - cg);
                int dr = Math.Abs(sr - cr);
                int d = dr + dg + db;
                delta += d;
                sampleLuma += sr + sg + sb;
                candidateLuma += cr + cg + cb;
                pixels++;

                if (sr != 0 || sg != 0 || sb != 0)
                {
                    sampleNonBlack++;
                }

                if (cr != 0 || cg != 0 || cb != 0)
                {
                    candidateNonBlack++;
                }

                if (d != 0)
                {
                    changed++;
                }
            }
        }

        double changedPercent = pixels == 0 ? 0 : (100.0 * changed / pixels);
        double averageDelta = pixels == 0 ? 0 : ((double)delta / (pixels * 3));
        double sampleNonBlackPercent = pixels == 0 ? 0 : (100.0 * sampleNonBlack / pixels);
        double candidateNonBlackPercent = pixels == 0 ? 0 : (100.0 * candidateNonBlack / pixels);
        double sampleAverageLuma = pixels == 0 ? 0 : ((double)sampleLuma / (pixels * 3));
        double candidateAverageLuma = pixels == 0 ? 0 : ((double)candidateLuma / (pixels * 3));
        return new ShiftCompareResult(
            dx,
            dy,
            true,
            pixels,
            changedPercent,
            averageDelta,
            sampleNonBlackPercent,
            candidateNonBlackPercent,
            sampleAverageLuma,
            candidateAverageLuma);
    }
}

public sealed class ImageBytes
{
    public ImageBytes(byte[] bytes, int width, int height, int stride)
    {
        Bytes = bytes;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public byte[] Bytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Stride { get; private set; }
}

public sealed class ShiftCompareResult
{
    public ShiftCompareResult(
        int dx,
        int dy,
        bool valid,
        long pixels,
        double changedPercent,
        double averageDelta,
        double sampleNonBlackPercent,
        double candidateNonBlackPercent,
        double sampleAverageLuma,
        double candidateAverageLuma)
    {
        Dx = dx;
        Dy = dy;
        Valid = valid;
        Pixels = pixels;
        ChangedPercent = changedPercent;
        AverageDelta = averageDelta;
        SampleNonBlackPercent = sampleNonBlackPercent;
        CandidateNonBlackPercent = candidateNonBlackPercent;
        SampleAverageLuma = sampleAverageLuma;
        CandidateAverageLuma = candidateAverageLuma;
    }

    public int Dx { get; private set; }
    public int Dy { get; private set; }
    public bool Valid { get; private set; }
    public long Pixels { get; private set; }
    public double ChangedPercent { get; private set; }
    public double AverageDelta { get; private set; }
    public double SampleNonBlackPercent { get; private set; }
    public double CandidateNonBlackPercent { get; private set; }
    public double SampleAverageLuma { get; private set; }
    public double CandidateAverageLuma { get; private set; }
}
"@

$candidate = Resolve-FullPath $CandidatePath
$sampleRoot = Resolve-FullPath $SampleDirectory
if (-not (Test-Path -LiteralPath $candidate)) {
    throw "Candidate image not found: $candidate"
}

if (-not (Test-Path -LiteralPath $sampleRoot)) {
    throw "Sample directory not found: $sampleRoot"
}

if ($Step -le 0) {
    throw "-Step must be positive."
}

if ($PixelStride -le 0) {
    throw "-PixelStride must be positive."
}

if ($MinSampleNonBlackPercent -lt 0 -or $MinSampleNonBlackPercent -gt 100) {
    throw "-MinSampleNonBlackPercent must be between 0 and 100."
}

if ($MinCandidateNonBlackPercent -lt 0 -or $MinCandidateNonBlackPercent -gt 100) {
    throw "-MinCandidateNonBlackPercent must be between 0 and 100."
}

$samples = @(Get-ChildItem -LiteralPath $sampleRoot -Filter "*.png" -File | Sort-Object Name)
if ($samples.Count -eq 0) {
    throw "No PNG samples found under $sampleRoot"
}

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$regions = if ($RegionWidth -gt 0 -and $RegionHeight -gt 0) {
    @([pscustomobject]@{
        name = if ([string]::IsNullOrWhiteSpace($RegionName)) { "custom" } else { $RegionName }
        x = $RegionX
        y = $RegionY
        width = $RegionWidth
        height = $RegionHeight
    })
} else {
    @(
        [pscustomobject]@{ name = "bridge"; x = 116; y = 156; width = 420; height = 210 },
        [pscustomobject]@{ name = "skyline"; x = 48; y = 72; width = 544; height = 170 },
        [pscustomobject]@{ name = "lower-track"; x = 92; y = 264; width = 456; height = 178 }
    )
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($region in $regions) {
    foreach ($sample in $samples) {
        $results = [SonicVisualShiftComparer]::Search(
            $sample.FullName,
            $candidate,
            $region.x,
            $region.y,
            $region.x,
            $region.y,
            $region.width,
            $region.height,
            $MaxShiftX,
            $MaxShiftY,
            $Step,
            $PixelStride)

        foreach ($result in $results) {
            if (-not $result.Valid) {
                continue
            }

            if ($result.SampleNonBlackPercent -lt $MinSampleNonBlackPercent -or
                $result.CandidateNonBlackPercent -lt $MinCandidateNonBlackPercent) {
                continue
            }

            $rows.Add([pscustomobject]@{
                region = $region.name
                sample = $sample.BaseName
                dx = $result.Dx
                dy = $result.Dy
                changedPercent = [Math]::Round($result.ChangedPercent, 6)
                averageDelta = [Math]::Round($result.AverageDelta, 6)
                sampleNonBlackPercent = [Math]::Round($result.SampleNonBlackPercent, 6)
                candidateNonBlackPercent = [Math]::Round($result.CandidateNonBlackPercent, 6)
                sampleAverageLuma = [Math]::Round($result.SampleAverageLuma, 6)
                candidateAverageLuma = [Math]::Round($result.CandidateAverageLuma, 6)
                pixels = $result.Pixels
                samplePath = $sample.FullName
                candidatePath = $candidate
            }) | Out-Null
        }
    }
}

$ordered = @($rows | Sort-Object region, averageDelta, changedPercent, sample, dx, dy)
$summaryPath = Join-Path $runRoot "shift-summary.csv"
$ordered | Export-Csv -LiteralPath $summaryPath -NoTypeInformation

$best = @($ordered |
    Group-Object region |
    ForEach-Object { $_.Group | Sort-Object averageDelta, changedPercent, sample, dx, dy | Select-Object -First $Top })
$bestPath = Join-Path $runRoot "best-shifts.csv"
$best | Export-Csv -LiteralPath $bestPath -NoTypeInformation

foreach ($row in @($best | Group-Object region | ForEach-Object { $_.Group | Select-Object -First 1 })) {
    $region = $regions | Where-Object { $_.name -eq $row.region } | Select-Object -First 1
    $regionDir = Join-Path $runRoot $row.region
    Save-Crop -InputPath $row.samplePath -OutputPath (Join-Path $regionDir "best-sample.png") -X $region.x -Y $region.y -Width $region.width -Height $region.height | Out-Null
    Save-Crop -InputPath $candidate -OutputPath (Join-Path $regionDir "best-candidate-shifted.png") -X ($region.x + [int]$row.dx) -Y ($region.y + [int]$row.dy) -Width $region.width -Height $region.height | Out-Null
}

[pscustomobject]@{
    candidatePath = $candidate
    sampleDirectory = $sampleRoot
    maxShiftX = $MaxShiftX
    maxShiftY = $MaxShiftY
    step = $Step
    pixelStride = $PixelStride
    minSampleNonBlackPercent = $MinSampleNonBlackPercent
    minCandidateNonBlackPercent = $MinCandidateNonBlackPercent
    rowCount = $rows.Count
    summaryCsv = $summaryPath
    bestCsv = $bestPath
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runRoot "run.json")

Write-Host "Sonic visual shift summary: $bestPath"
$best | Group-Object region | ForEach-Object { $_.Group | Select-Object -First 5 } |
    Select-Object region,sample,dx,dy,averageDelta,changedPercent,sampleNonBlackPercent,candidateNonBlackPercent |
    Format-Table -AutoSize
