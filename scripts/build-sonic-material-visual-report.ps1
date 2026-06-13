param(
    [Parameter(Mandatory = $true)]
    [string]$MaterialSummaryCsvPath,
    [string]$AlignmentDirectory = "",
    [string]$Region = "bridge",
    [string]$TextureAddress = "",
    [string]$OutJsonPath = "",
    [string]$OutCsvPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Find-LatestAlignmentDirectory {
    $root = Resolve-FullPath "artifacts/sonic-visual-alignment"
    if (-not (Test-Path -LiteralPath $root)) {
        return ""
    }

    $latest = Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "best-shifts.csv") } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return ""
    }

    return $latest.FullName
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

function Convert-ToInt64 {
    param(
        [object]$Value,
        [long]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    return [long]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
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

function Add-ImageDiagnosticsType {
    if ("SonicMaterialVisualImageDiagnostics" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public sealed class SonicImageStats
{
    public string Path { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Pixels { get; set; }
    public long NonBlackPixels { get; set; }
    public double NonBlackPercent { get; set; }
    public double AverageR { get; set; }
    public double AverageG { get; set; }
    public double AverageB { get; set; }
    public double AverageLuma { get; set; }
}

public sealed class SonicImageDiffStats
{
    public string BaselinePath { get; set; }
    public string CandidatePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Pixels { get; set; }
    public long ChangedPixels { get; set; }
    public double ChangedPercent { get; set; }
    public double AverageDelta { get; set; }
}

public static class SonicMaterialVisualImageDiagnostics
{
    public static SonicImageStats Analyze(string path)
    {
        using (var bitmap = Load32(path))
        {
            ImageBytes bytes = ReadBytes(bitmap);
            long pixels = 0;
            long nonBlack = 0;
            long totalR = 0;
            long totalG = 0;
            long totalB = 0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int row = y * bytes.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int i = row + (x * 4);
                    int b = bytes.Bytes[i];
                    int g = bytes.Bytes[i + 1];
                    int r = bytes.Bytes[i + 2];
                    pixels++;
                    totalR += r;
                    totalG += g;
                    totalB += b;

                    if (r != 0 || g != 0 || b != 0)
                    {
                        nonBlack++;
                    }
                }
            }

            return new SonicImageStats
            {
                Path = path,
                Width = bitmap.Width,
                Height = bitmap.Height,
                Pixels = pixels,
                NonBlackPixels = nonBlack,
                NonBlackPercent = pixels == 0 ? 0 : (100.0 * nonBlack / pixels),
                AverageR = pixels == 0 ? 0 : ((double)totalR / pixels),
                AverageG = pixels == 0 ? 0 : ((double)totalG / pixels),
                AverageB = pixels == 0 ? 0 : ((double)totalB / pixels),
                AverageLuma = pixels == 0 ? 0 : ((double)(totalR + totalG + totalB) / (pixels * 3))
            };
        }
    }

    public static SonicImageDiffStats Compare(string baselinePath, string candidatePath)
    {
        using (var baseline = Load32(baselinePath))
        using (var candidate = Load32(candidatePath))
        {
            int width = Math.Min(baseline.Width, candidate.Width);
            int height = Math.Min(baseline.Height, candidate.Height);
            ImageBytes baselineBytes = ReadBytes(baseline);
            ImageBytes candidateBytes = ReadBytes(candidate);
            long pixels = 0;
            long changed = 0;
            long delta = 0;

            for (int y = 0; y < height; y++)
            {
                int baselineRow = y * baselineBytes.Stride;
                int candidateRow = y * candidateBytes.Stride;
                for (int x = 0; x < width; x++)
                {
                    int bi = baselineRow + (x * 4);
                    int ci = candidateRow + (x * 4);
                    int db = Math.Abs(baselineBytes.Bytes[bi] - candidateBytes.Bytes[ci]);
                    int dg = Math.Abs(baselineBytes.Bytes[bi + 1] - candidateBytes.Bytes[ci + 1]);
                    int dr = Math.Abs(baselineBytes.Bytes[bi + 2] - candidateBytes.Bytes[ci + 2]);
                    int d = dr + dg + db;
                    pixels++;
                    delta += d;

                    if (d != 0)
                    {
                        changed++;
                    }
                }
            }

            return new SonicImageDiffStats
            {
                BaselinePath = baselinePath,
                CandidatePath = candidatePath,
                Width = width,
                Height = height,
                Pixels = pixels,
                ChangedPixels = changed,
                ChangedPercent = pixels == 0 ? 0 : (100.0 * changed / pixels),
                AverageDelta = pixels == 0 ? 0 : ((double)delta / (pixels * 3))
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

    private sealed class ImageBytes
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
}
"@
}

$materialSummaryCsvPath = Resolve-FullPath $MaterialSummaryCsvPath
if (-not (Test-Path -LiteralPath $materialSummaryCsvPath)) {
    throw "Material summary CSV not found: $materialSummaryCsvPath"
}

if ([string]::IsNullOrWhiteSpace($AlignmentDirectory)) {
    $AlignmentDirectory = Find-LatestAlignmentDirectory
}

if (-not [string]::IsNullOrWhiteSpace($AlignmentDirectory)) {
    $AlignmentDirectory = Resolve-FullPath $AlignmentDirectory
}

$materialDirectory = Split-Path -Parent $materialSummaryCsvPath
if ([string]::IsNullOrWhiteSpace($OutJsonPath)) {
    $OutJsonPath = Join-Path $materialDirectory "sonic-material-visual-report.json"
}

if ([string]::IsNullOrWhiteSpace($OutCsvPath)) {
    $OutCsvPath = Join-Path $materialDirectory "sonic-material-visual-report.csv"
}

$materials = @(Import-Csv -LiteralPath $materialSummaryCsvPath)
if ($materials.Count -eq 0) {
    throw "Material summary CSV has no rows: $materialSummaryCsvPath"
}

if (-not [string]::IsNullOrWhiteSpace($TextureAddress)) {
    $candidateMaterials = @($materials | Where-Object { $_.texture_address -eq $TextureAddress })
    if ($candidateMaterials.Count -eq 0) {
        throw "No material row found for texture address $TextureAddress in $materialSummaryCsvPath"
    }
} else {
    $candidateMaterials = $materials
}

$material = $candidateMaterials |
    Sort-Object `
        @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "black_color_writes") }; Descending = $true },
        @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "covered_pixels") }; Descending = $true } |
    Select-Object -First 1

$alignmentRow = $null
$sampleStats = $null
$candidateStats = $null
$diffStats = $null
$bestSamplePath = ""
$bestCandidatePath = ""
$bestShiftsPath = ""

if (-not [string]::IsNullOrWhiteSpace($AlignmentDirectory)) {
    $bestShiftsPath = Join-Path $AlignmentDirectory "best-shifts.csv"
    if (Test-Path -LiteralPath $bestShiftsPath) {
        $alignmentRows = @(Import-Csv -LiteralPath $bestShiftsPath | Where-Object { $_.region -eq $Region })
        if ($alignmentRows.Count -gt 0) {
            $alignmentRow = $alignmentRows |
                Sort-Object `
                    @{ Expression = { Convert-ToDouble (Get-ObjectValue $_ "averageDelta") }; Descending = $false },
                    @{ Expression = { Convert-ToDouble (Get-ObjectValue $_ "changedPercent") }; Descending = $false } |
                Select-Object -First 1
        }
    }

    $regionDirectory = Join-Path $AlignmentDirectory $Region
    $bestSamplePath = Join-Path $regionDirectory "best-sample.png"
    $bestCandidatePath = Join-Path $regionDirectory "best-candidate-shifted.png"

    if ((-not (Test-Path -LiteralPath $bestSamplePath)) -and $null -ne $alignmentRow) {
        $bestSamplePath = [string](Get-ObjectValue $alignmentRow "samplePath")
    }

    if ((-not (Test-Path -LiteralPath $bestCandidatePath)) -and $null -ne $alignmentRow) {
        $bestCandidatePath = [string](Get-ObjectValue $alignmentRow "candidatePath")
    }

    if ((Test-Path -LiteralPath $bestSamplePath) -and (Test-Path -LiteralPath $bestCandidatePath)) {
        Add-ImageDiagnosticsType
        $sampleStats = [SonicMaterialVisualImageDiagnostics]::Analyze($bestSamplePath)
        $candidateStats = [SonicMaterialVisualImageDiagnostics]::Analyze($bestCandidatePath)
        $diffStats = [SonicMaterialVisualImageDiagnostics]::Compare($bestSamplePath, $bestCandidatePath)
    }
}

$report = [pscustomobject]@{
    generated_at = (Get-Date).ToString("o")
    material_summary_csv_path = $materialSummaryCsvPath
    alignment_directory = $AlignmentDirectory
    alignment_best_shifts_path = $bestShiftsPath
    region = $Region
    selected_material = [pscustomobject]@{
        texture_address = Get-ObjectValue $material "texture_address"
        texture_format = Get-ObjectValue $material "texture_format"
        texture_size = Get-ObjectValue $material "texture_size"
        texture_filter = Get-ObjectValue $material "texture_filter"
        texture_lod = Get-ObjectValue $material "texture_lod"
        stage0_mode = Get-ObjectValue $material "stage0_mode"
        draw_count = Convert-ToInt64 (Get-ObjectValue $material "draw_count")
        triangle_count = Convert-ToInt64 (Get-ObjectValue $material "triangle_count")
        covered_pixels = Convert-ToInt64 (Get-ObjectValue $material "covered_pixels")
        color_writes = Convert-ToInt64 (Get-ObjectValue $material "color_writes")
        black_color_writes = Convert-ToInt64 (Get-ObjectValue $material "black_color_writes")
        black_write_ratio = Convert-ToDouble (Get-ObjectValue $material "black_write_ratio")
        uv_s_min = Convert-ToDouble (Get-ObjectValue $material "uv_s_min")
        uv_s_max = Convert-ToDouble (Get-ObjectValue $material "uv_s_max")
        uv_t_min = Convert-ToDouble (Get-ObjectValue $material "uv_t_min")
        uv_t_max = Convert-ToDouble (Get-ObjectValue $material "uv_t_max")
        view_w_min = Convert-ToDouble (Get-ObjectValue $material "view_w_min")
        view_w_max = Convert-ToDouble (Get-ObjectValue $material "view_w_max")
        sample_raster_rgba_top = Get-ObjectValue $material "sample_raster_rgba_top"
        sample_tev_rgba_top = Get-ObjectValue $material "sample_tev_rgba_top"
        texture_xy_top = Get-ObjectValue $material "texture_xy_top"
        texture_mip_samples_top = Get-ObjectValue $material "texture_mip_samples_top"
        draws = Get-ObjectValue $material "draws"
        triangles = Get-ObjectValue $material "triangles"
    }
    visual_alignment = if ($null -eq $alignmentRow) {
        $null
    } else {
        [pscustomobject]@{
            sample = Get-ObjectValue $alignmentRow "sample"
            dx = Convert-ToInt64 (Get-ObjectValue $alignmentRow "dx")
            dy = Convert-ToInt64 (Get-ObjectValue $alignmentRow "dy")
            changed_percent = Convert-ToDouble (Get-ObjectValue $alignmentRow "changedPercent")
            average_delta = Convert-ToDouble (Get-ObjectValue $alignmentRow "averageDelta")
            sample_non_black_percent = Convert-ToDouble (Get-ObjectValue $alignmentRow "sampleNonBlackPercent")
            candidate_non_black_percent = Convert-ToDouble (Get-ObjectValue $alignmentRow "candidateNonBlackPercent")
            sample_average_luma = Convert-ToDouble (Get-ObjectValue $alignmentRow "sampleAverageLuma")
            candidate_average_luma = Convert-ToDouble (Get-ObjectValue $alignmentRow "candidateAverageLuma")
            pixels = Convert-ToInt64 (Get-ObjectValue $alignmentRow "pixels")
            source_sample_path = Get-ObjectValue $alignmentRow "samplePath"
            source_candidate_path = Get-ObjectValue $alignmentRow "candidatePath"
            best_sample_path = $bestSamplePath
            best_candidate_shifted_path = $bestCandidatePath
        }
    }
    image_stats = [pscustomobject]@{
        sample = $sampleStats
        candidate = $candidateStats
        diff = $diffStats
    }
}

$outJsonFullPath = Resolve-FullPath $OutJsonPath
$outCsvFullPath = Resolve-FullPath $OutCsvPath
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outJsonFullPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outCsvFullPath) | Out-Null

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outJsonFullPath -Encoding UTF8

$csvRow = [pscustomobject]@{
    generated_at = $report.generated_at
    material_summary_csv_path = $report.material_summary_csv_path
    alignment_directory = $report.alignment_directory
    region = $report.region
    texture_address = $report.selected_material.texture_address
    texture_format = $report.selected_material.texture_format
    texture_size = $report.selected_material.texture_size
    texture_filter = $report.selected_material.texture_filter
    texture_lod = $report.selected_material.texture_lod
    stage0_mode = $report.selected_material.stage0_mode
    draw_count = $report.selected_material.draw_count
    triangle_count = $report.selected_material.triangle_count
    covered_pixels = $report.selected_material.covered_pixels
    color_writes = $report.selected_material.color_writes
    black_color_writes = $report.selected_material.black_color_writes
    black_write_ratio = $report.selected_material.black_write_ratio
    uv_s_min = $report.selected_material.uv_s_min
    uv_s_max = $report.selected_material.uv_s_max
    uv_t_min = $report.selected_material.uv_t_min
    uv_t_max = $report.selected_material.uv_t_max
    view_w_min = $report.selected_material.view_w_min
    view_w_max = $report.selected_material.view_w_max
    draws = $report.selected_material.draws
    triangles = $report.selected_material.triangles
    alignment_sample = Get-ObjectValue $report.visual_alignment "sample"
    alignment_dx = Get-ObjectValue $report.visual_alignment "dx"
    alignment_dy = Get-ObjectValue $report.visual_alignment "dy"
    alignment_changed_percent = Get-ObjectValue $report.visual_alignment "changed_percent"
    alignment_average_delta = Get-ObjectValue $report.visual_alignment "average_delta"
    alignment_sample_non_black_percent = Get-ObjectValue $report.visual_alignment "sample_non_black_percent"
    alignment_candidate_non_black_percent = Get-ObjectValue $report.visual_alignment "candidate_non_black_percent"
    alignment_sample_average_luma = Get-ObjectValue $report.visual_alignment "sample_average_luma"
    alignment_candidate_average_luma = Get-ObjectValue $report.visual_alignment "candidate_average_luma"
    sample_image_average_luma = Get-ObjectValue $sampleStats "AverageLuma"
    candidate_image_average_luma = Get-ObjectValue $candidateStats "AverageLuma"
    sample_image_non_black_percent = Get-ObjectValue $sampleStats "NonBlackPercent"
    candidate_image_non_black_percent = Get-ObjectValue $candidateStats "NonBlackPercent"
    shifted_crop_changed_percent = Get-ObjectValue $diffStats "ChangedPercent"
    shifted_crop_average_delta = Get-ObjectValue $diffStats "AverageDelta"
    best_sample_path = $bestSamplePath
    best_candidate_shifted_path = $bestCandidatePath
}

$csvRow | Export-Csv -LiteralPath $outCsvFullPath -NoTypeInformation -Encoding UTF8

[pscustomobject]@{
    json = $outJsonFullPath
    csv = $outCsvFullPath
    texture_address = $report.selected_material.texture_address
    black_color_writes = $report.selected_material.black_color_writes
    alignment_sample = Get-ObjectValue $report.visual_alignment "sample"
    alignment_average_delta = Get-ObjectValue $report.visual_alignment "average_delta"
}
