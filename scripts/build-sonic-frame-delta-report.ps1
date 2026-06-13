param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$SamplePath,
    [string]$OutputDirectory = "artifacts/sonic-frame-delta",
    [string]$TriangleCoveragePath = "",
    [int]$FocusDrawStart = 16059,
    [int]$FocusDrawEnd = 16090,
    [int]$ChangeThreshold = 8
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

function Format-Percent {
    param([double]$Numerator, [double]$Denominator)

    if ($Denominator -le 0) {
        return "0"
    }

    return ([Math]::Round(100.0 * $Numerator / $Denominator, 6)).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-TextureDrawSummary {
    param([string]$Path, [int]$DrawStart, [int]$DrawEnd)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $rows = @(
        Import-Csv -LiteralPath $Path |
            Where-Object {
                $draw = Convert-ToNullableInt64 (Get-ObjectValue $_ "draw_index")
                $rendered = Get-ObjectValue $_ "rendered" "True"
                $draw -ne $null -and $draw -ge $DrawStart -and $draw -le $DrawEnd -and
                    $rendered -eq "True"
            }
    )

    $summary = New-Object System.Collections.ArrayList
    foreach ($group in ($rows | Group-Object texture_address,texture_format,stage0_mode | Sort-Object Count -Descending)) {
        $items = @($group.Group)
        $first = $items | Select-Object -First 1
        $covered = 0L
        $colorWrites = 0L
        $blackWrites = 0L
        $draws = New-Object System.Collections.Generic.SortedSet[int]
        foreach ($item in $items) {
            $coveredValue = Convert-ToNullableInt64 (Get-ObjectValue $item "covered_pixels")
            $colorWritesValue = Convert-ToNullableInt64 (Get-ObjectValue $item "color_writes")
            $blackWritesValue = Convert-ToNullableInt64 (Get-ObjectValue $item "black_color_writes")
            $covered += if ($null -ne $coveredValue) { [int64]$coveredValue } else { 0L }
            $colorWrites += if ($null -ne $colorWritesValue) { [int64]$colorWritesValue } else { 0L }
            $blackWrites += if ($null -ne $blackWritesValue) { [int64]$blackWritesValue } else { 0L }
            $draw = Convert-ToNullableInt64 (Get-ObjectValue $item "draw_index")
            if ($null -ne $draw) {
                [void]$draws.Add([int]$draw)
            }
        }

        [void]$summary.Add([pscustomobject]@{
            texture_address = Get-ObjectValue $first "texture_address"
            texture_format = Get-ObjectValue $first "texture_format"
            stage0_mode = Get-ObjectValue $first "stage0_mode"
            draw_count = $draws.Count
            draws = (@($draws | ForEach-Object { $_ }) -join "|")
            triangle_rows = $items.Count
            covered_pixels = $covered
            color_writes = $colorWrites
            black_color_writes = $blackWrites
            black_write_ratio = if ($colorWrites -gt 0) { [Math]::Round([double]$blackWrites / [double]$colorWrites, 6) } else { 0 }
            sample_tev_rgba = Get-ObjectValue $first "sample_tev_rgba"
            texture_mip_samples = Get-ObjectValue $first "texture_mip_samples"
        })
    }

    return @(
        $summary |
            Sort-Object `
                @{ Expression = { [int64](Get-ObjectValue $_ "color_writes") }; Descending = $true },
                @{ Expression = { [int64](Get-ObjectValue $_ "covered_pixels") }; Descending = $true }
    )
}

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public sealed class SonicFrameRegionSpec
{
    public SonicFrameRegionSpec(string name, int x, int y, int width, int height)
    {
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public string Name { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
}

public sealed class SonicFrameDeltaResult
{
    public string Region { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Pixels { get; set; }
    public long ChangedPixels { get; set; }
    public long SampleNonBlackPixels { get; set; }
    public long CandidateNonBlackPixels { get; set; }
    public long SharedNonBlackPixels { get; set; }
    public long SampleOnlyPixels { get; set; }
    public long CandidateOnlyPixels { get; set; }
    public long SharedChangedPixels { get; set; }
    public double ChangedPercent { get; set; }
    public double SampleNonBlackPercent { get; set; }
    public double CandidateNonBlackPercent { get; set; }
    public double SharedNonBlackPercent { get; set; }
    public double SampleOnlyPercent { get; set; }
    public double CandidateOnlyPercent { get; set; }
    public double NonBlackJaccard { get; set; }
    public double AverageDeltaAll { get; set; }
    public double AverageDeltaShared { get; set; }
    public double SampleAverageLuma { get; set; }
    public double CandidateAverageLuma { get; set; }
    public string SampleNonBlackBounds { get; set; }
    public string CandidateNonBlackBounds { get; set; }
    public string SharedNonBlackBounds { get; set; }
    public string SampleOnlyBounds { get; set; }
    public string CandidateOnlyBounds { get; set; }
    public string Classification { get; set; }
    public string MaskPath { get; set; }
    public string SampleCropPath { get; set; }
    public string CandidateCropPath { get; set; }
}

public static class SonicFrameDeltaAnalyzer
{
    public static SonicFrameDeltaResult[] Analyze(
        string samplePath,
        string candidatePath,
        string outputDirectory,
        SonicFrameRegionSpec[] regions,
        int changeThreshold)
    {
        using (Bitmap sample = Load32(samplePath))
        using (Bitmap candidate = Load32(candidatePath))
        {
            if (sample.Width != candidate.Width || sample.Height != candidate.Height)
            {
                throw new InvalidOperationException("Image sizes differ.");
            }

            ImageBytes sampleBytes = ReadBytes(sample);
            ImageBytes candidateBytes = ReadBytes(candidate);
            var results = new List<SonicFrameDeltaResult>();
            foreach (SonicFrameRegionSpec region in regions)
            {
                results.Add(AnalyzeRegion(sampleBytes, candidateBytes, sample, candidate, outputDirectory, region, changeThreshold));
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
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
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

    private static SonicFrameDeltaResult AnalyzeRegion(
        ImageBytes sample,
        ImageBytes candidate,
        Bitmap sampleBitmap,
        Bitmap candidateBitmap,
        string outputDirectory,
        SonicFrameRegionSpec region,
        int changeThreshold)
    {
        int x0 = Math.Max(0, region.X);
        int y0 = Math.Max(0, region.Y);
        int x1 = Math.Min(sample.Width, region.X + region.Width);
        int y1 = Math.Min(sample.Height, region.Y + region.Height);
        int width = Math.Max(0, x1 - x0);
        int height = Math.Max(0, y1 - y0);
        if (width == 0 || height == 0)
        {
            throw new InvalidOperationException("Empty region: " + region.Name);
        }

        long pixels = 0;
        long changed = 0;
        long sampleNonBlack = 0;
        long candidateNonBlack = 0;
        long sharedNonBlack = 0;
        long sampleOnly = 0;
        long candidateOnly = 0;
        long sharedChanged = 0;
        long deltaAll = 0;
        long deltaShared = 0;
        long sampleLuma = 0;
        long candidateLuma = 0;
        Bounds sampleBounds = new Bounds();
        Bounds candidateBounds = new Bounds();
        Bounds sharedBounds = new Bounds();
        Bounds sampleOnlyBounds = new Bounds();
        Bounds candidateOnlyBounds = new Bounds();

        using (Bitmap mask = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < height; y++)
            {
                int yy = y0 + y;
                int sampleRow = yy * sample.Stride;
                int candidateRow = yy * candidate.Stride;
                for (int x = 0; x < width; x++)
                {
                    int xx = x0 + x;
                    int si = sampleRow + xx * 4;
                    int ci = candidateRow + xx * 4;
                    int sb = sample.Bytes[si];
                    int sg = sample.Bytes[si + 1];
                    int sr = sample.Bytes[si + 2];
                    int cb = candidate.Bytes[ci];
                    int cg = candidate.Bytes[ci + 1];
                    int cr = candidate.Bytes[ci + 2];
                    bool snb = sr != 0 || sg != 0 || sb != 0;
                    bool cnb = cr != 0 || cg != 0 || cb != 0;
                    int d = Math.Abs(sr - cr) + Math.Abs(sg - cg) + Math.Abs(sb - cb);
                    bool isChanged = d > changeThreshold;
                    pixels++;
                    deltaAll += d;
                    sampleLuma += sr + sg + sb;
                    candidateLuma += cr + cg + cb;
                    if (isChanged)
                    {
                        changed++;
                    }

                    Color color;
                    if (snb && cnb)
                    {
                        sampleNonBlack++;
                        candidateNonBlack++;
                        sharedNonBlack++;
                        sharedBounds.Add(xx, yy);
                        sampleBounds.Add(xx, yy);
                        candidateBounds.Add(xx, yy);
                        deltaShared += d;
                        if (isChanged)
                        {
                            sharedChanged++;
                            color = Color.FromArgb(255, 255, 216, 0);
                        }
                        else
                        {
                            color = Color.FromArgb(255, 0, 180, 80);
                        }
                    }
                    else if (snb)
                    {
                        sampleNonBlack++;
                        sampleOnly++;
                        sampleBounds.Add(xx, yy);
                        sampleOnlyBounds.Add(xx, yy);
                        color = Color.FromArgb(255, 0, 116, 255);
                    }
                    else if (cnb)
                    {
                        candidateNonBlack++;
                        candidateOnly++;
                        candidateBounds.Add(xx, yy);
                        candidateOnlyBounds.Add(xx, yy);
                        color = Color.FromArgb(255, 255, 64, 64);
                    }
                    else
                    {
                        color = Color.FromArgb(255, 0, 0, 0);
                    }

                    mask.SetPixel(x, y, color);
                }
            }

            string regionDirectory = System.IO.Path.Combine(outputDirectory, region.Name);
            System.IO.Directory.CreateDirectory(regionDirectory);
            string maskPath = System.IO.Path.Combine(regionDirectory, "classification-mask.png");
            string sampleCropPath = System.IO.Path.Combine(regionDirectory, "sample.png");
            string candidateCropPath = System.IO.Path.Combine(regionDirectory, "candidate.png");
            mask.Save(maskPath, ImageFormat.Png);
            using (Bitmap sampleCrop = sampleBitmap.Clone(new Rectangle(x0, y0, width, height), PixelFormat.Format32bppArgb))
            {
                sampleCrop.Save(sampleCropPath, ImageFormat.Png);
            }

            using (Bitmap candidateCrop = candidateBitmap.Clone(new Rectangle(x0, y0, width, height), PixelFormat.Format32bppArgb))
            {
                candidateCrop.Save(candidateCropPath, ImageFormat.Png);
            }

            long nonBlackUnion = sampleNonBlack + candidateOnly;
            double changedPercent = Percent(changed, pixels);
            double sampleOnlyPercent = Percent(sampleOnly, pixels);
            double candidateOnlyPercent = Percent(candidateOnly, pixels);
            double sharedPercent = Percent(sharedNonBlack, pixels);
            double jaccard = nonBlackUnion == 0 ? 1.0 : (double)sharedNonBlack / nonBlackUnion;
            string classification = Classify(sampleOnlyPercent, candidateOnlyPercent, sharedPercent, jaccard, sharedNonBlack == 0 ? 0 : ((double)sharedChanged / sharedNonBlack));
            return new SonicFrameDeltaResult
            {
                Region = region.Name,
                X = x0,
                Y = y0,
                Width = width,
                Height = height,
                Pixels = pixels,
                ChangedPixels = changed,
                SampleNonBlackPixels = sampleNonBlack,
                CandidateNonBlackPixels = candidateNonBlack,
                SharedNonBlackPixels = sharedNonBlack,
                SampleOnlyPixels = sampleOnly,
                CandidateOnlyPixels = candidateOnly,
                SharedChangedPixels = sharedChanged,
                ChangedPercent = changedPercent,
                SampleNonBlackPercent = Percent(sampleNonBlack, pixels),
                CandidateNonBlackPercent = Percent(candidateNonBlack, pixels),
                SharedNonBlackPercent = sharedPercent,
                SampleOnlyPercent = sampleOnlyPercent,
                CandidateOnlyPercent = candidateOnlyPercent,
                NonBlackJaccard = jaccard,
                AverageDeltaAll = pixels == 0 ? 0 : (double)deltaAll / (pixels * 3),
                AverageDeltaShared = sharedNonBlack == 0 ? 0 : (double)deltaShared / (sharedNonBlack * 3),
                SampleAverageLuma = pixels == 0 ? 0 : (double)sampleLuma / (pixels * 3),
                CandidateAverageLuma = pixels == 0 ? 0 : (double)candidateLuma / (pixels * 3),
                SampleNonBlackBounds = sampleBounds.ToString(),
                CandidateNonBlackBounds = candidateBounds.ToString(),
                SharedNonBlackBounds = sharedBounds.ToString(),
                SampleOnlyBounds = sampleOnlyBounds.ToString(),
                CandidateOnlyBounds = candidateOnlyBounds.ToString(),
                Classification = classification,
                MaskPath = maskPath,
                SampleCropPath = sampleCropPath,
                CandidateCropPath = candidateCropPath,
            };
        }
    }

    private static double Percent(long value, long total)
    {
        return total == 0 ? 0 : 100.0 * value / total;
    }

    private static string Classify(double sampleOnlyPercent, double candidateOnlyPercent, double sharedPercent, double jaccard, double sharedChangedRatio)
    {
        if (jaccard < 0.25 && sampleOnlyPercent > 10 && candidateOnlyPercent > 10)
        {
            return "low-overlap-placement-or-timing";
        }

        if (sampleOnlyPercent > candidateOnlyPercent * 2 && sampleOnlyPercent > 10)
        {
            return "candidate-missing-sample-coverage";
        }

        if (candidateOnlyPercent > sampleOnlyPercent * 2 && candidateOnlyPercent > 10)
        {
            return "candidate-extra-coverage";
        }

        if (sharedPercent > 20 && sharedChangedRatio > 0.5)
        {
            return "shared-coverage-color-or-material";
        }

        return "mixed";
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

public struct Bounds
{
    private bool any;
    private int minX;
    private int minY;
    private int maxX;
    private int maxY;

    public void Add(int x, int y)
    {
        if (!any)
        {
            minX = maxX = x;
            minY = maxY = y;
            any = true;
            return;
        }

        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
    }

    public override string ToString()
    {
        return any ? minX + "/" + minY + "-" + maxX + "/" + maxY : "";
    }
}
"@

function Draw-FitImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$ImagePath,
        [System.Drawing.Rectangle]$Bounds
    )

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

    $cellWidth = 250
    $cellHeight = 190
    $labelWidth = 170
    $labelHeight = 58
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
            $boldFont = [System.Drawing.Font]::new("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
            $brush = [System.Drawing.Brushes]::White
            $muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(190, 198, 208))
            $panel = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 41, 48))
            try {
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    $x = $labelWidth + $padding + ($i * ($cellWidth + $padding))
                    $graphics.DrawString($columns[$i], $boldFont, $brush, $x, 14)
                }

                $y = $labelHeight
                foreach ($row in $Rows) {
                    $graphics.DrawString($row.region, $boldFont, $brush, $padding, $y + 4)
                    $graphics.DrawString($row.classification, $font, $muted, $padding, $y + 24)
                    $graphics.DrawString(("Jaccard {0:N3}" -f [double]$row.non_black_jaccard), $font, $muted, $padding, $y + 42)
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
                $boldFont.Dispose()
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
if (-not (Test-Path -LiteralPath $candidateFullPath)) {
    throw "Candidate image not found: $candidateFullPath"
}

if (-not (Test-Path -LiteralPath $sampleFullPath)) {
    throw "Sample image not found: $sampleFullPath"
}

if (-not [string]::IsNullOrWhiteSpace($TriangleCoveragePath)) {
    $TriangleCoveragePath = Resolve-FullPath $TriangleCoveragePath
}

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$regions = @(
    [SonicFrameRegionSpec]::new("full", 0, 0, 640, 480),
    [SonicFrameRegionSpec]::new("skyline", 48, 72, 544, 170),
    [SonicFrameRegionSpec]::new("bridge", 116, 156, 420, 210),
    [SonicFrameRegionSpec]::new("lower-track", 92, 264, 456, 178),
    [SonicFrameRegionSpec]::new("right-city", 360, 96, 260, 270),
    [SonicFrameRegionSpec]::new("left-void", 0, 96, 180, 300)
)

$deltaRows = [SonicFrameDeltaAnalyzer]::Analyze($sampleFullPath, $candidateFullPath, $runRoot, $regions, $ChangeThreshold) |
    ForEach-Object {
        [pscustomobject]@{
            region = $_.Region
            x = $_.X
            y = $_.Y
            width = $_.Width
            height = $_.Height
            pixels = $_.Pixels
            changed_pixels = $_.ChangedPixels
            changed_percent = [Math]::Round($_.ChangedPercent, 6)
            sample_nonblack_pixels = $_.SampleNonBlackPixels
            sample_nonblack_percent = [Math]::Round($_.SampleNonBlackPercent, 6)
            candidate_nonblack_pixels = $_.CandidateNonBlackPixels
            candidate_nonblack_percent = [Math]::Round($_.CandidateNonBlackPercent, 6)
            shared_nonblack_pixels = $_.SharedNonBlackPixels
            shared_nonblack_percent = [Math]::Round($_.SharedNonBlackPercent, 6)
            sample_only_pixels = $_.SampleOnlyPixels
            sample_only_percent = [Math]::Round($_.SampleOnlyPercent, 6)
            candidate_only_pixels = $_.CandidateOnlyPixels
            candidate_only_percent = [Math]::Round($_.CandidateOnlyPercent, 6)
            shared_changed_pixels = $_.SharedChangedPixels
            non_black_jaccard = [Math]::Round($_.NonBlackJaccard, 6)
            average_delta_all = [Math]::Round($_.AverageDeltaAll, 6)
            average_delta_shared = [Math]::Round($_.AverageDeltaShared, 6)
            sample_average_luma = [Math]::Round($_.SampleAverageLuma, 6)
            candidate_average_luma = [Math]::Round($_.CandidateAverageLuma, 6)
            luma_delta = [Math]::Round($_.CandidateAverageLuma - $_.SampleAverageLuma, 6)
            sample_nonblack_bounds = $_.SampleNonBlackBounds
            candidate_nonblack_bounds = $_.CandidateNonBlackBounds
            shared_nonblack_bounds = $_.SharedNonBlackBounds
            sample_only_bounds = $_.SampleOnlyBounds
            candidate_only_bounds = $_.CandidateOnlyBounds
            classification = $_.Classification
            sample_crop_path = $_.SampleCropPath
            candidate_crop_path = $_.CandidateCropPath
            mask_path = $_.MaskPath
        }
    }

$textureRows = @(Get-TextureDrawSummary -Path $TriangleCoveragePath -DrawStart $FocusDrawStart -DrawEnd $FocusDrawEnd)

$deltaCsvPath = Join-Path $runRoot "frame-delta-regions.csv"
$textureCsvPath = Join-Path $runRoot "focus-texture-draw-summary.csv"
$jsonPath = Join-Path $runRoot "sonic-frame-delta-report.json"
$contactSheetPath = Join-Path $runRoot "frame-delta-contact-sheet.png"

$deltaRows | Export-Csv -LiteralPath $deltaCsvPath -NoTypeInformation -Encoding UTF8
$textureRows | Select-Object -First 20 | Export-Csv -LiteralPath $textureCsvPath -NoTypeInformation -Encoding UTF8
Write-ContactSheet -Rows $deltaRows -OutputPath $contactSheetPath

$full = $deltaRows | Where-Object { $_.region -eq "full" } | Select-Object -First 1
$dominantClassification = @($deltaRows | Group-Object classification | Sort-Object Count -Descending | Select-Object -First 1)
$summary = [pscustomobject]@{
    schema = "ngcsharp.sonic-frame-delta.v1"
    candidatePath = $candidateFullPath
    samplePath = $sampleFullPath
    triangleCoveragePath = if (-not [string]::IsNullOrWhiteSpace($TriangleCoveragePath) -and (Test-Path -LiteralPath $TriangleCoveragePath)) { $TriangleCoveragePath } else { $null }
    focusDrawStart = $FocusDrawStart
    focusDrawEnd = $FocusDrawEnd
    changeThreshold = $ChangeThreshold
    fullChangedPercent = Get-ObjectValue $full "changed_percent"
    fullNonBlackJaccard = Get-ObjectValue $full "non_black_jaccard"
    fullSampleOnlyPercent = Get-ObjectValue $full "sample_only_percent"
    fullCandidateOnlyPercent = Get-ObjectValue $full "candidate_only_percent"
    fullSharedNonBlackPercent = Get-ObjectValue $full "shared_nonblack_percent"
    dominantClassification = if ($dominantClassification.Count -gt 0) { $dominantClassification[0].Name } else { "" }
    topTextureAddress = if ($textureRows.Count -gt 0) { $textureRows[0].texture_address } else { "" }
    topTextureDraws = if ($textureRows.Count -gt 0) { $textureRows[0].draws } else { "" }
    topTextureColorWrites = if ($textureRows.Count -gt 0) { $textureRows[0].color_writes } else { "" }
    deltaRegionsCsvPath = $deltaCsvPath
    focusTextureDrawSummaryCsvPath = $textureCsvPath
    contactSheetPath = $contactSheetPath
}

[pscustomobject]@{
    summary = $summary
    regions = $deltaRows
    focusTextures = $textureRows | Select-Object -First 20
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic frame delta report: $jsonPath"
$deltaRows | Select-Object region,classification,changed_percent,sample_nonblack_percent,candidate_nonblack_percent,shared_nonblack_percent,sample_only_percent,candidate_only_percent,non_black_jaccard,average_delta_shared | Format-Table -AutoSize
if ($textureRows.Count -gt 0) {
    $textureRows | Select-Object -First 8 texture_address,draws,color_writes,black_color_writes,black_write_ratio,sample_tev_rgba | Format-Table -AutoSize
}
