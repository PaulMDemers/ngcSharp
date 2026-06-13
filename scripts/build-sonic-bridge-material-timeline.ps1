param(
    [Parameter(Mandatory = $true)]
    [string]$MaterialSummaryCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$TextureIndexCsvPath,
    [string]$PacketTimelineCsvPath = "",
    [string]$TextureBindTraceCsvPath = "",
    [string]$OutputDirectory = "",
    [string]$FocusTextureAddress = "0x0072C600",
    [int]$TopMaterials = 10
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
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

function Convert-HexToInt64 {
    param(
        [object]$Value,
        [long]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    $text = ([string]$Value).Trim()
    if ($text.StartsWith("+", [System.StringComparison]::Ordinal)) {
        $text = $text.Substring(1)
    }

    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [long]::Parse($text.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [long]::Parse($text, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
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

function Parse-DrawList {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    return @([regex]::Matches($Text, '\d+') | ForEach-Object {
        [int]::Parse($_.Value, [System.Globalization.CultureInfo]::InvariantCulture)
    })
}

function Add-BridgeMaterialImageType {
    if ("SonicBridgeMaterialImageTools" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public sealed class SonicBridgeImageStats
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

public sealed class SonicBridgeContactSheetItem
{
    public string Label { get; set; }
    public string Detail { get; set; }
    public string ImagePath { get; set; }
}

public static class SonicBridgeMaterialImageTools
{
    public static SonicBridgeImageStats Analyze(string path)
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

            return new SonicBridgeImageStats
            {
                Path = path,
                Width = bitmap.Width,
                Height = bitmap.Height,
                Pixels = pixels,
                NonBlackPixels = nonBlack,
                NonBlackPercent = pixels == 0 ? 0 : 100.0 * nonBlack / pixels,
                AverageR = pixels == 0 ? 0 : (double)totalR / pixels,
                AverageG = pixels == 0 ? 0 : (double)totalG / pixels,
                AverageB = pixels == 0 ? 0 : (double)totalB / pixels,
                AverageLuma = pixels == 0 ? 0 : (double)(totalR + totalG + totalB) / (pixels * 3)
            };
        }
    }

    public static void WriteContactSheet(SonicBridgeContactSheetItem[] items, string outputPath)
    {
        int columns = 4;
        int cellWidth = 220;
        int imageHeight = 170;
        int labelHeight = 54;
        int padding = 12;
        int rows = Math.Max(1, (items.Length + columns - 1) / columns);
        int width = padding + columns * (cellWidth + padding);
        int height = padding + rows * (imageHeight + labelHeight + padding);

        using (var sheet = new Bitmap(width, height))
        using (var graphics = Graphics.FromImage(sheet))
        using (var labelFont = new Font("Segoe UI", 10, FontStyle.Bold))
        using (var detailFont = new Font("Segoe UI", 8))
        using (var labelBrush = new SolidBrush(Color.White))
        using (var detailBrush = new SolidBrush(Color.FromArgb(198, 206, 216)))
        using (var cellBrush = new SolidBrush(Color.FromArgb(32, 36, 42)))
        {
            graphics.Clear(Color.FromArgb(18, 22, 28));
            for (int i = 0; i < items.Length; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int x = padding + column * (cellWidth + padding);
                int y = padding + row * (imageHeight + labelHeight + padding);
                var imageBounds = new Rectangle(x, y, cellWidth, imageHeight);
                graphics.FillRectangle(cellBrush, imageBounds);

                if (!String.IsNullOrWhiteSpace(items[i].ImagePath) && System.IO.File.Exists(items[i].ImagePath))
                {
                    DrawFitImage(graphics, items[i].ImagePath, imageBounds);
                }

                graphics.DrawString(items[i].Label ?? String.Empty, labelFont, labelBrush, x, y + imageHeight + 6);
                graphics.DrawString(items[i].Detail ?? String.Empty, detailFont, detailBrush, x, y + imageHeight + 27);
            }

            string parent = System.IO.Path.GetDirectoryName(outputPath);
            if (!String.IsNullOrEmpty(parent))
            {
                System.IO.Directory.CreateDirectory(parent);
            }

            sheet.Save(outputPath, ImageFormat.Png);
        }
    }

    private static void DrawFitImage(Graphics graphics, string imagePath, Rectangle bounds)
    {
        using (var image = Load32(imagePath))
        {
            double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
            int drawWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
            int x = bounds.X + (bounds.Width - drawWidth) / 2;
            int y = bounds.Y + (bounds.Height - drawHeight) / 2;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.DrawImage(image, x, y, drawWidth, drawHeight);
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
            return new ImageBytes(bytes, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private sealed class ImageBytes
    {
        public ImageBytes(byte[] bytes, int stride)
        {
            Bytes = bytes;
            Stride = stride;
        }

        public byte[] Bytes { get; private set; }
        public int Stride { get; private set; }
    }
}
"@
}

function Find-TextureRows {
    param(
        [object[]]$TextureRows,
        [string]$TextureAddress,
        [int[]]$Draws
    )

    $drawText = @($Draws | ForEach-Object { $_.ToString([System.Globalization.CultureInfo]::InvariantCulture) })
    $matching = @($TextureRows | Where-Object {
        $_.source_address -eq $TextureAddress -and ($drawText.Count -eq 0 -or $drawText -contains $_.draw_index)
    })

    if ($matching.Count -eq 0) {
        $matching = @($TextureRows | Where-Object { $_.source_address -eq $TextureAddress })
    }

    return $matching
}

function Find-PacketForDraw {
    param(
        [object[]]$PacketRows,
        [int]$Draw
    )

    foreach ($packet in $PacketRows) {
        $start = Convert-ToInt64 (Get-ObjectValue $packet "mapped_draw_start")
        $end = Convert-ToInt64 (Get-ObjectValue $packet "mapped_draw_end")
        if ($start -gt 0 -and $end -gt 0 -and $Draw -ge $start -and $Draw -le $end) {
            return $packet
        }
    }

    return $null
}

function Find-TextureBind {
    param(
        [object[]]$TextureBindRows,
        [string]$TextureAddress,
        [long]$TargetFifoOffset
    )

    if ($TextureBindRows.Count -eq 0 -or [string]::IsNullOrWhiteSpace($TextureAddress)) {
        return $null
    }

    $matches = @($TextureBindRows | Where-Object { $_.source_address -eq $TextureAddress })
    if ($matches.Count -eq 0) {
        return $null
    }

    if ($TargetFifoOffset -le 0) {
        return $matches | Sort-Object @{ Expression = { Convert-HexToInt64 (Get-ObjectValue $_ "gx_fifo_offset") }; Descending = $false } | Select-Object -First 1
    }

    $beforeOrAt = @($matches | Where-Object { (Convert-HexToInt64 (Get-ObjectValue $_ "gx_fifo_offset")) -le $TargetFifoOffset })
    if ($beforeOrAt.Count -gt 0) {
        return $beforeOrAt | Sort-Object @{ Expression = { Convert-HexToInt64 (Get-ObjectValue $_ "gx_fifo_offset") }; Descending = $true } | Select-Object -First 1
    }

    return $matches | Sort-Object @{ Expression = { [Math]::Abs((Convert-HexToInt64 (Get-ObjectValue $_ "gx_fifo_offset")) - $TargetFifoOffset) }; Descending = $false } | Select-Object -First 1
}

$materialSummaryCsvPath = Resolve-FullPath $MaterialSummaryCsvPath
$textureIndexCsvPath = Resolve-FullPath $TextureIndexCsvPath
if (-not (Test-Path -LiteralPath $materialSummaryCsvPath)) {
    throw "Material summary CSV not found: $materialSummaryCsvPath"
}

if (-not (Test-Path -LiteralPath $textureIndexCsvPath)) {
    throw "Texture index CSV not found: $textureIndexCsvPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $materialSummaryCsvPath) "sonic-bridge-material-timeline"
}

$outputDirectoryFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$packetTimelineFullPath = ""
$packetRows = @()
if (-not [string]::IsNullOrWhiteSpace($PacketTimelineCsvPath)) {
    $packetTimelineFullPath = Resolve-FullPath $PacketTimelineCsvPath
    if (Test-Path -LiteralPath $packetTimelineFullPath) {
        $packetRows = @(Import-Csv -LiteralPath $packetTimelineFullPath)
    }
}

$textureBindTraceFullPath = ""
$textureBindRows = @()
if (-not [string]::IsNullOrWhiteSpace($TextureBindTraceCsvPath)) {
    $textureBindTraceFullPath = Resolve-FullPath $TextureBindTraceCsvPath
    if (Test-Path -LiteralPath $textureBindTraceFullPath) {
        $textureBindRows = @(Import-Csv -LiteralPath $textureBindTraceFullPath)
    }
}

$materials = @(Import-Csv -LiteralPath $materialSummaryCsvPath)
$textureRows = @(Import-Csv -LiteralPath $textureIndexCsvPath)
$textureDirectory = Split-Path -Parent $textureIndexCsvPath

if ($materials.Count -eq 0) {
    throw "Material summary has no rows: $materialSummaryCsvPath"
}

if ($textureRows.Count -eq 0) {
    throw "Texture index has no rows: $textureIndexCsvPath"
}

$rankedMaterials = @($materials |
    Sort-Object `
        @{ Expression = { if ($_.texture_address -eq $FocusTextureAddress) { 1 } else { 0 } }; Descending = $true },
        @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "black_color_writes") }; Descending = $true },
        @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "covered_pixels") }; Descending = $true } |
    Select-Object -First ([Math]::Max(1, $TopMaterials)))

Add-BridgeMaterialImageType

$timelineRows = New-Object System.Collections.Generic.List[object]
$contactItems = New-Object 'System.Collections.Generic.List[SonicBridgeContactSheetItem]'
$rank = 0
foreach ($material in $rankedMaterials) {
    $rank++
    $textureAddress = [string](Get-ObjectValue $material "texture_address")
    $draws = Parse-DrawList ([string](Get-ObjectValue $material "draws"))
    $textureMatches = @(Find-TextureRows -TextureRows $textureRows -TextureAddress $textureAddress -Draws $draws)
    $baseTextureRow = $textureMatches |
        Sort-Object `
            @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "mip_level") }; Descending = $false },
            @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "draw_index") }; Descending = $false } |
        Select-Object -First 1
    $mipRows = @($textureMatches | Sort-Object @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "mip_level") }; Descending = $false })
    $firstDraw = if ($draws.Count -gt 0) { $draws[0] } else { Convert-ToInt64 (Get-ObjectValue $baseTextureRow "draw_index") }
    $packet = if ($packetRows.Count -gt 0 -and $firstDraw -gt 0) { Find-PacketForDraw -PacketRows $packetRows -Draw $firstDraw } else { $null }
    $texturePath = ""
    $stats = $null
    if ($null -ne $baseTextureRow -and -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $baseTextureRow "path"))) {
        $texturePath = Join-Path $textureDirectory ([string](Get-ObjectValue $baseTextureRow "path"))
        if (Test-Path -LiteralPath $texturePath) {
            $stats = [SonicBridgeMaterialImageTools]::Analyze($texturePath)
        }
    }

    if ($null -ne $baseTextureRow) {
        $baseDraw = [string](Get-ObjectValue $baseTextureRow "draw_index")
        $baseSlot = [string](Get-ObjectValue $baseTextureRow "slot")
        $mipRows = @($textureRows | Where-Object { $_.draw_index -eq $baseDraw -and $_.slot -eq $baseSlot } | Sort-Object @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "mip_level") }; Descending = $false })
    }

    $textureDumpFifoOffset = Convert-HexToInt64 (Get-ObjectValue $baseTextureRow "fifo_offset")
    $textureBind = if ($textureBindRows.Count -gt 0) { Find-TextureBind -TextureBindRows $textureBindRows -TextureAddress $textureAddress -TargetFifoOffset $textureDumpFifoOffset } else { $null }
    $textureBindFifoOffset = Convert-HexToInt64 (Get-ObjectValue $textureBind "gx_fifo_offset")

    $textureAverageRgb = ""
    $textureAverageLuma = ""
    $textureNonBlackPercent = ""
    if ($null -ne $stats) {
        $textureAverageRgb = ("{0:N3}/{1:N3}/{2:N3}" -f $stats.AverageR, $stats.AverageG, $stats.AverageB)
        $textureAverageLuma = $stats.AverageLuma
        $textureNonBlackPercent = $stats.NonBlackPercent
    }

    $timelineRows.Add([pscustomobject]@{
        rank = $rank
        focus = if ($textureAddress -eq $FocusTextureAddress) { "True" } else { "False" }
        texture_address = $textureAddress
        texture_format = Get-ObjectValue $material "texture_format"
        texture_size = Get-ObjectValue $material "texture_size"
        texture_filter = Get-ObjectValue $material "texture_filter"
        texture_lod = Get-ObjectValue $material "texture_lod"
        stage0_mode = Get-ObjectValue $material "stage0_mode"
        draws = Get-ObjectValue $material "draws"
        triangles = Get-ObjectValue $material "triangles"
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
        texture_dump_draw = Get-ObjectValue $baseTextureRow "draw_index"
        texture_dump_mip_count = @($mipRows | Select-Object -ExpandProperty mip_level -Unique).Count
        texture_dump_mip_sources = (($mipRows | ForEach-Object { "{0}:mip{1}" -f (Get-ObjectValue $_ "source_address"), (Get-ObjectValue $_ "mip_level") }) -join " ")
        texture_dump_path = $texturePath
        texture_bind_instruction = Get-ObjectValue $textureBind "instruction"
        texture_bind_fifo_offset = Get-ObjectValue $textureBind "gx_fifo_offset"
        texture_bind_to_dump_fifo_delta = if ($textureDumpFifoOffset -gt 0 -and $textureBindFifoOffset -gt 0) { $textureDumpFifoOffset - $textureBindFifoOffset } else { "" }
        texture_bind_lr = Get-ObjectValue $textureBind "lr"
        texture_bind_texture_object = Get-ObjectValue $textureBind "texture_object"
        texture_bind_sampler_object = Get-ObjectValue $textureBind "sampler_object"
        texture_bind_word0 = Get-ObjectValue $textureBind "word0"
        texture_bind_word4 = Get-ObjectValue $textureBind "word4"
        texture_bind_word8 = Get-ObjectValue $textureBind "word8"
        texture_bind_sampler0 = Get-ObjectValue $textureBind "sampler0"
        texture_bind_sampler4 = Get-ObjectValue $textureBind "sampler4"
        texture_bind_word12 = Get-ObjectValue $textureBind "word12"
        texture_dump_nonblack_pixels = Convert-ToInt64 (Get-ObjectValue $baseTextureRow "nonblack_pixels")
        texture_dump_nonblack_percent = $textureNonBlackPercent
        texture_dump_average_luma = $textureAverageLuma
        texture_dump_average_rgb = $textureAverageRgb
        texture_dump_samples = Get-ObjectValue $baseTextureRow "samples"
        packet = Get-ObjectValue $packet "packet"
        packet_kind = Get-ObjectValue $packet "packet_kind"
        object = Get-ObjectValue $packet "object"
        object_kind = Get-ObjectValue $packet "object_kind"
        object_xyz = Get-ObjectValue $packet "object_xyz"
        matrix_translation = Get-ObjectValue $packet "matrix_translation"
        mapped_draw_start = Get-ObjectValue $packet "mapped_draw_start"
        mapped_draw_end = Get-ObjectValue $packet "mapped_draw_end"
        view_bounds = Get-ObjectValue $packet "view_bounds"
        screen_bounds = Get-ObjectValue $packet "screen_bounds"
    }) | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($texturePath)) {
        $contactLuma = if ($null -ne $stats) { $stats.AverageLuma } else { 0 }
        $detail = ("draw {0}; black {1:N1}%; luma {2:N2}" -f $firstDraw, ((Convert-ToDouble (Get-ObjectValue $material "black_write_ratio")) * 100), $contactLuma)
        $label = ("#{0} {1} {2}" -f $rank, $textureAddress, (Get-ObjectValue $material "texture_size"))
        $contactItems.Add([SonicBridgeContactSheetItem]@{
            Label = $label
            Detail = $detail
            ImagePath = $texturePath
        }) | Out-Null
    }
}

$csvPath = Join-Path $outputDirectoryFullPath "sonic-bridge-material-timeline.csv"
$jsonPath = Join-Path $outputDirectoryFullPath "sonic-bridge-material-timeline.json"
$contactSheetPath = Join-Path $outputDirectoryFullPath "sonic-bridge-material-contact-sheet.png"

$timelineRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    generated_at = (Get-Date).ToString("o")
    material_summary_csv_path = $materialSummaryCsvPath
    texture_index_csv_path = $textureIndexCsvPath
    packet_timeline_csv_path = $packetTimelineFullPath
    texture_bind_trace_csv_path = $textureBindTraceFullPath
    focus_texture_address = $FocusTextureAddress
    rows = @($timelineRows.ToArray())
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[SonicBridgeMaterialImageTools]::WriteContactSheet($contactItems.ToArray(), $contactSheetPath)

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    contact_sheet = $contactSheetPath
    rows = $timelineRows.Count
}
