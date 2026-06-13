param(
    [Parameter(Mandatory = $true)]
    [string]$TriangleCoverageCsvPath,
    [string]$GxCopiesCsvPath = "",
    [string]$CandidateImagePath = "",
    [string]$SampleImagePath = "",
    [string]$OutputDirectory = "",
    [int]$DrawIndex = 12188,
    [string]$TextureAddress = "0x0072C600",
    [int]$Width = 640,
    [int]$Height = 480,
    [int]$BridgeCropX = 116,
    [int]$BridgeCropY = 156,
    [int]$BridgeCropWidth = 420,
    [int]$BridgeCropHeight = 210
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

function Convert-ToDouble {
    param(
        [object]$Value,
        [double]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToInt {
    param(
        [object]$Value,
        [int]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    $number = Convert-ToDouble -Value $Value
    return [int][Math]::Round($number)
}

function Add-MaskDrawingType {
    if ("SonicMaterialMaskDrawing" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;

public static class SonicMaterialMaskDrawing
{
    public static void WriteMask(string path, int width, int height, float[][] triangles)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var fill = new SolidBrush(Color.FromArgb(190, 255, 32, 32)))
        using (var outline = new Pen(Color.FromArgb(255, 255, 255, 0), 1.0f))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            foreach (var t in triangles)
            {
                var points = new[] {
                    new PointF(t[0], t[1]),
                    new PointF(t[2], t[3]),
                    new PointF(t[4], t[5])
                };
                graphics.FillPolygon(fill, points);
                graphics.DrawPolygon(outline, points);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    public static void WriteOverlay(string imagePath, string maskPath, string outputPath)
    {
        using (var image = Load32(imagePath))
        using (var mask = Load32(maskPath))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.DrawImage(mask, 0, 0, image.Width, image.Height);
            image.Save(outputPath, ImageFormat.Png);
        }
    }

    public static ImageStats AnalyzeRect(string imagePath, int x, int y, int width, int height)
    {
        using (var image = Load32(imagePath))
        {
            int left;
            int top;
            int right;
            int bottom;
            if (image.Width <= width && image.Height <= height)
            {
                left = 0;
                top = 0;
                right = image.Width;
                bottom = image.Height;
            }
            else
            {
                left = Math.Max(0, x);
                top = Math.Max(0, y);
                right = Math.Min(image.Width, x + width);
                bottom = Math.Min(image.Height, y + height);
            }
            long pixels = 0;
            long nonBlack = 0;
            long r = 0;
            long g = 0;
            long b = 0;
            for (int yy = top; yy < bottom; yy++)
            {
                for (int xx = left; xx < right; xx++)
                {
                    Color c = image.GetPixel(xx, yy);
                    pixels++;
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    if (c.R != 0 || c.G != 0 || c.B != 0)
                    {
                        nonBlack++;
                    }
                }
            }

            return new ImageStats
            {
                Pixels = pixels,
                NonBlackPixels = nonBlack,
                NonBlackPercent = pixels == 0 ? 0 : (100.0 * nonBlack / pixels),
                AverageR = pixels == 0 ? 0 : ((double)r / pixels),
                AverageG = pixels == 0 ? 0 : ((double)g / pixels),
                AverageB = pixels == 0 ? 0 : ((double)b / pixels),
                AverageLuma = pixels == 0 ? 0 : ((double)(r + g + b) / (pixels * 3))
            };
        }
    }

    public static ImageStats AnalyzeMask(string imagePath, string maskPath)
    {
        using (var image = Load32(imagePath))
        using (var mask = Load32(maskPath))
        {
            int width = Math.Min(image.Width, mask.Width);
            int height = Math.Min(image.Height, mask.Height);
            long pixels = 0;
            long nonBlack = 0;
            long r = 0;
            long g = 0;
            long b = 0;
            for (int yy = 0; yy < height; yy++)
            {
                for (int xx = 0; xx < width; xx++)
                {
                    Color m = mask.GetPixel(xx, yy);
                    if (m.A == 0)
                    {
                        continue;
                    }

                    Color c = image.GetPixel(xx, yy);
                    pixels++;
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    if (c.R != 0 || c.G != 0 || c.B != 0)
                    {
                        nonBlack++;
                    }
                }
            }

            return new ImageStats
            {
                Pixels = pixels,
                NonBlackPixels = nonBlack,
                NonBlackPercent = pixels == 0 ? 0 : (100.0 * nonBlack / pixels),
                AverageR = pixels == 0 ? 0 : ((double)r / pixels),
                AverageG = pixels == 0 ? 0 : ((double)g / pixels),
                AverageB = pixels == 0 ? 0 : ((double)b / pixels),
                AverageLuma = pixels == 0 ? 0 : ((double)(r + g + b) / (pixels * 3))
            };
        }
    }

    private static Bitmap Load32(string path)
    {
        using (var source = new Bitmap(path))
        {
            var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return target;
        }
    }
}

public sealed class ImageStats
{
    public long Pixels { get; set; }
    public long NonBlackPixels { get; set; }
    public double NonBlackPercent { get; set; }
    public double AverageR { get; set; }
    public double AverageG { get; set; }
    public double AverageB { get; set; }
    public double AverageLuma { get; set; }
}
"@
}

function Get-TriangleVertices {
    param(
        [object[]]$VertexRows,
        [object]$TriangleRow
    )

    $vertexA = Convert-ToInt -Value (Get-ObjectValue $TriangleRow "vertex_a")
    $vertexB = Convert-ToInt -Value (Get-ObjectValue $TriangleRow "vertex_b")
    $vertexC = Convert-ToInt -Value (Get-ObjectValue $TriangleRow "vertex_c")
    $indices = @($vertexA, $vertexB, $vertexC)

    $coords = New-Object System.Collections.Generic.List[float]
    foreach ($index in $indices) {
        $vertex = $VertexRows | Where-Object { (Convert-ToInt -Value (Get-ObjectValue $_ "vertex_index")) -eq $index } | Select-Object -First 1
        if ($null -eq $vertex) {
            return $null
        }

        $coords.Add([float](Convert-ToDouble -Value (Get-ObjectValue $vertex "screen_x"))) | Out-Null
        $coords.Add([float](Convert-ToDouble -Value (Get-ObjectValue $vertex "screen_y"))) | Out-Null
    }

    return $coords.ToArray()
}

function Get-RectOverlapArea {
    param(
        [double]$LeftA,
        [double]$TopA,
        [double]$RightA,
        [double]$BottomA,
        [double]$LeftB,
        [double]$TopB,
        [double]$RightB,
        [double]$BottomB
    )

    $left = [Math]::Max($LeftA, $LeftB)
    $right = [Math]::Min($RightA, $RightB)
    $top = [Math]::Max($TopA, $TopB)
    $bottom = [Math]::Min($BottomA, $BottomB)
    if ($right -le $left -or $bottom -le $top) {
        return 0
    }

    return ($right - $left) * ($bottom - $top)
}

$triangleCoverageFullPath = Resolve-FullPath $TriangleCoverageCsvPath
if (-not (Test-Path -LiteralPath $triangleCoverageFullPath)) {
    throw "Triangle coverage CSV not found: $triangleCoverageFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $triangleCoverageFullPath) "material-mask-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$targetRoot = Split-Path -Parent $triangleCoverageFullPath
$vertexCsvPath = Join-Path $targetRoot "gx-vertices.csv"
$hasVertexRows = Test-Path -LiteralPath $vertexCsvPath

$triangleRows = @(
    Import-Csv -LiteralPath $triangleCoverageFullPath |
        Where-Object {
            (Convert-ToInt -Value (Get-ObjectValue $_ "draw_index")) -eq $DrawIndex -and
            (
                [string](Get-ObjectValue $_ "texture_address") -eq $TextureAddress -or
                ([string](Get-ObjectValue $_ "stage_summary")).Contains("addr=$TextureAddress")
            ) -and
            [string](Get-ObjectValue $_ "rendered") -eq "True"
        } |
        Sort-Object { Convert-ToInt -Value (Get-ObjectValue $_ "triangle_index") }
)
$vertexRows = @(
    if ($hasVertexRows) {
        Import-Csv -LiteralPath $vertexCsvPath |
            Where-Object { (Convert-ToInt -Value (Get-ObjectValue $_ "draw_index")) -eq $DrawIndex }
    }
)

$triangles = New-Object System.Collections.Generic.List[float[]]
$triangleDetailRows = New-Object System.Collections.ArrayList
$materialLeft = [double]::PositiveInfinity
$materialRight = [double]::NegativeInfinity
$materialTop = [double]::PositiveInfinity
$materialBottom = [double]::NegativeInfinity
$totalCovered = 0
$totalColorWrites = 0
$totalBlackWrites = 0
foreach ($row in $triangleRows) {
    $coords = if ($hasVertexRows) {
        Get-TriangleVertices $vertexRows $row
    } else {
        $left = [float](Convert-ToDouble -Value (Get-ObjectValue $row "screen_min_x"))
        $right = [float](Convert-ToDouble -Value (Get-ObjectValue $row "screen_max_x"))
        $top = [float](Convert-ToDouble -Value (Get-ObjectValue $row "screen_min_y"))
        $bottom = [float](Convert-ToDouble -Value (Get-ObjectValue $row "screen_max_y"))
        @($left, $top, $right, $top, $right, $bottom)
    }
    if ($null -ne $coords) {
        $triangles.Add($coords) | Out-Null
        for ($i = 0; $i -lt 6; $i += 2) {
            $materialLeft = [Math]::Min($materialLeft, [double]$coords[$i])
            $materialRight = [Math]::Max($materialRight, [double]$coords[$i])
            $materialTop = [Math]::Min($materialTop, [double]$coords[$i + 1])
            $materialBottom = [Math]::Max($materialBottom, [double]$coords[$i + 1])
        }
    }

    $totalCovered += Convert-ToInt -Value (Get-ObjectValue $row "covered_pixels")
    $totalColorWrites += Convert-ToInt -Value (Get-ObjectValue $row "color_writes")
    $totalBlackWrites += Convert-ToInt -Value (Get-ObjectValue $row "black_color_writes")
    [void]$triangleDetailRows.Add([pscustomobject]@{
        draw_index = Get-ObjectValue $row "draw_index"
        triangle_index = Get-ObjectValue $row "triangle_index"
        vertices = ("{0}/{1}/{2}" -f (Get-ObjectValue $row "vertex_a"), (Get-ObjectValue $row "vertex_b"), (Get-ObjectValue $row "vertex_c"))
        screen_min_x = Get-ObjectValue $row "screen_min_x"
        screen_max_x = Get-ObjectValue $row "screen_max_x"
        screen_min_y = Get-ObjectValue $row "screen_min_y"
        screen_max_y = Get-ObjectValue $row "screen_max_y"
        covered_pixels = Get-ObjectValue $row "covered_pixels"
        color_writes = Get-ObjectValue $row "color_writes"
        black_color_writes = Get-ObjectValue $row "black_color_writes"
        black_write_ratio = if ((Convert-ToInt -Value (Get-ObjectValue $row "color_writes")) -eq 0) { 0 } else { [Math]::Round((Convert-ToInt -Value (Get-ObjectValue $row "black_color_writes")) / [double](Convert-ToInt -Value (Get-ObjectValue $row "color_writes")), 6) }
        sample_tev_rgba = Get-ObjectValue $row "sample_tev_rgba"
        texture_xy = Get-ObjectValue $row "texture_xy"
    })
}

$displayCopyBefore = $null
$displayCopyAfter = $null
if (-not [string]::IsNullOrWhiteSpace($GxCopiesCsvPath)) {
    $copiesFullPath = Resolve-FullPath $GxCopiesCsvPath
    if (Test-Path -LiteralPath $copiesFullPath) {
        $displayCopies = @(Import-Csv -LiteralPath $copiesFullPath | Where-Object { $_.kind -eq "display" })
        $displayCopyBefore = $displayCopies |
            Where-Object { (Convert-ToInt -Value (Get-ObjectValue $_ "draws_seen")) -le $DrawIndex } |
            Sort-Object { Convert-ToInt -Value (Get-ObjectValue $_ "draws_seen") } -Descending |
            Select-Object -First 1
        $displayCopyAfter = $displayCopies |
            Where-Object { (Convert-ToInt -Value (Get-ObjectValue $_ "draws_seen")) -ge $DrawIndex } |
            Sort-Object { Convert-ToInt -Value (Get-ObjectValue $_ "draws_seen") } |
            Select-Object -First 1
    }
}

Add-MaskDrawingType
$maskPath = Join-Path $OutputDirectory ("draw{0}_{1}_approx-mask.png" -f $DrawIndex, ($TextureAddress -replace '0x',''))
[SonicMaterialMaskDrawing]::WriteMask($maskPath, $Width, $Height, $triangles.ToArray())

$candidateOverlayPath = ""
$sampleOverlayPath = ""
$candidateBridgeStats = $null
$sampleBridgeStats = $null
$candidateMaskStats = $null
$sampleMaskStats = $null
if (-not [string]::IsNullOrWhiteSpace($CandidateImagePath)) {
    $candidateFullPath = Resolve-FullPath $CandidateImagePath
    if (Test-Path -LiteralPath $candidateFullPath) {
        $candidateOverlayPath = Join-Path $OutputDirectory "candidate-approx-material-overlay.png"
        [SonicMaterialMaskDrawing]::WriteOverlay($candidateFullPath, $maskPath, $candidateOverlayPath)
        $candidateBridgeStats = [SonicMaterialMaskDrawing]::AnalyzeRect($candidateFullPath, $BridgeCropX, $BridgeCropY, $BridgeCropWidth, $BridgeCropHeight)
        $candidateMaskStats = [SonicMaterialMaskDrawing]::AnalyzeMask($candidateFullPath, $maskPath)
    }
}

if (-not [string]::IsNullOrWhiteSpace($SampleImagePath)) {
    $sampleFullPath = Resolve-FullPath $SampleImagePath
    if (Test-Path -LiteralPath $sampleFullPath) {
        $sampleOverlayPath = Join-Path $OutputDirectory "sample-approx-material-overlay.png"
        [SonicMaterialMaskDrawing]::WriteOverlay($sampleFullPath, $maskPath, $sampleOverlayPath)
        $sampleBridgeStats = [SonicMaterialMaskDrawing]::AnalyzeRect($sampleFullPath, $BridgeCropX, $BridgeCropY, $BridgeCropWidth, $BridgeCropHeight)
        $sampleMaskStats = [SonicMaterialMaskDrawing]::AnalyzeMask($sampleFullPath, $maskPath)
    }
}

$cropOverlapArea = if ([double]::IsPositiveInfinity($materialLeft)) {
    0
} else {
    Get-RectOverlapArea $materialLeft $materialTop $materialRight $materialBottom $BridgeCropX $BridgeCropY ($BridgeCropX + $BridgeCropWidth) ($BridgeCropY + $BridgeCropHeight)
}

$displayTiming = if ($null -ne $displayCopyAfter -and (Convert-ToInt -Value (Get-ObjectValue $displayCopyAfter "draws_seen")) -ge $DrawIndex) {
    "covered-by-display-copy"
} elseif ($null -ne $displayCopyBefore) {
    "after-last-display-copy-in-window"
} else {
    "no-display-copy-context"
}

$summary = [pscustomobject]@{
    generated_at = (Get-Date).ToString("o")
    triangle_coverage_csv_path = $triangleCoverageFullPath
    gx_vertices_csv_path = if ($hasVertexRows) { $vertexCsvPath } else { "" }
    gx_copies_csv_path = if ([string]::IsNullOrWhiteSpace($GxCopiesCsvPath)) { "" } else { Resolve-FullPath $GxCopiesCsvPath }
    draw_index = $DrawIndex
    texture_address = $TextureAddress
    rendered_triangle_count = $triangleRows.Count
    covered_pixels = $totalCovered
    color_writes = $totalColorWrites
    black_color_writes = $totalBlackWrites
    black_write_ratio = if ($totalColorWrites -eq 0) { 0 } else { [Math]::Round($totalBlackWrites / [double]$totalColorWrites, 6) }
    projected_material_bounds = if ([double]::IsPositiveInfinity($materialLeft)) { "" } else { "{0:n3}/{1:n3}-{2:n3}/{3:n3}" -f $materialLeft, $materialTop, $materialRight, $materialBottom }
    bridge_crop_bounds = "{0}/{1}-{2}/{3}" -f $BridgeCropX, $BridgeCropY, ($BridgeCropX + $BridgeCropWidth), ($BridgeCropY + $BridgeCropHeight)
    projected_bounds_overlap_bridge_crop_area = [Math]::Round($cropOverlapArea, 3)
    display_timing = $displayTiming
    nearest_display_copy_before_draws_seen = Get-ObjectValue $displayCopyBefore "draws_seen"
    nearest_display_copy_before_fifo = Get-ObjectValue $displayCopyBefore "fifo_offset"
    nearest_display_copy_after_draws_seen = Get-ObjectValue $displayCopyAfter "draws_seen"
    nearest_display_copy_after_fifo = Get-ObjectValue $displayCopyAfter "fifo_offset"
    mask_path = $maskPath
    candidate_overlay_path = $candidateOverlayPath
    sample_overlay_path = $sampleOverlayPath
    candidate_bridge_crop_luma = Get-ObjectValue $candidateBridgeStats "AverageLuma"
    sample_bridge_crop_luma = Get-ObjectValue $sampleBridgeStats "AverageLuma"
    candidate_mask_pixels = Get-ObjectValue $candidateMaskStats "Pixels"
    candidate_mask_luma = Get-ObjectValue $candidateMaskStats "AverageLuma"
    candidate_mask_average_rgb = if ($null -eq $candidateMaskStats) { "" } else { "{0:n3}/{1:n3}/{2:n3}" -f $candidateMaskStats.AverageR, $candidateMaskStats.AverageG, $candidateMaskStats.AverageB }
    sample_mask_pixels = Get-ObjectValue $sampleMaskStats "Pixels"
    sample_mask_luma = Get-ObjectValue $sampleMaskStats "AverageLuma"
    sample_mask_average_rgb = if ($null -eq $sampleMaskStats) { "" } else { "{0:n3}/{1:n3}/{2:n3}" -f $sampleMaskStats.AverageR, $sampleMaskStats.AverageG, $sampleMaskStats.AverageB }
    note = if ($hasVertexRows) {
        "Mask is reconstructed from rendered triangle vertices and does not include exact depth/edge-rule pixel IDs. If display_timing is after-last-display-copy-in-window, compare this draw to a later EFB/display capture before treating visual crop deltas as material evidence."
    } else {
        "Mask is reconstructed from triangle coverage bounds because gx-vertices.csv was not available; use it only as a coarse coordinate-space overlay."
    }
}

$summaryCsvPath = Join-Path $OutputDirectory "sonic-material-mask-summary.csv"
$triangleCsvPath = Join-Path $OutputDirectory "sonic-material-mask-triangles.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-material-mask-report.json"

$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation -Encoding UTF8
$triangleDetailRows | Export-Csv -LiteralPath $triangleCsvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    summary = $summary
    triangles = $triangleDetailRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    summary_csv = $summaryCsvPath
    triangles_csv = $triangleCsvPath
    json = $jsonPath
    mask = $maskPath
    candidate_overlay = $candidateOverlayPath
    sample_overlay = $sampleOverlayPath
    display_timing = $summary.display_timing
    overlap_area = $summary.projected_bounds_overlap_bridge_crop_area
}
