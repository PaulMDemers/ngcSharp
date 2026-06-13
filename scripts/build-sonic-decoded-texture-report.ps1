param(
    [Parameter(Mandatory = $true)]
    [string]$TextureBinPath,
    [string]$BitstreamCsvPath = "",
    [string]$TextureBindCsvPath = "",
    [string]$TevSummaryCsvPath = "",
    [string]$TextureWriteProvenanceJsonPath = "",
    [string]$OutputDirectory = "",
    [string]$TextureAddress = "0x0072C600",
    [int]$Width = 64,
    [int]$Height = 64,
    [int]$MaxMipLevel = 6
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

function Normalize-HexAddress {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X8}" -f ([uint32]::Parse($text.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return "0x{0:X8}" -f ([uint32]::Parse($text, [System.Globalization.CultureInfo]::InvariantCulture))
}

function Get-CmprByteLength {
    param(
        [int]$LevelWidth,
        [int]$LevelHeight
    )

    $blockColumns = [int][Math]::Floor(($LevelWidth + 7) / 8)
    $blockRows = [int][Math]::Floor(($LevelHeight + 7) / 8)
    return [int]($blockColumns * $blockRows * 32)
}

function Add-CmprTextureReportType {
    if ("NgcSharpCmprTextureReportTools" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public sealed class NgcSharpDecodedTextureStats
{
    public int MipLevel { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SourceOffset { get; set; }
    public int SourceLength { get; set; }
    public bool Complete { get; set; }
    public string Path { get; set; }
    public long Pixels { get; set; }
    public long NonBlackPixels { get; set; }
    public long TransparentPixels { get; set; }
    public double NonBlackPercent { get; set; }
    public double TransparentPercent { get; set; }
    public double AverageR { get; set; }
    public double AverageG { get; set; }
    public double AverageB { get; set; }
    public double AverageLuma { get; set; }
    public string Samples { get; set; }
}

public static class NgcSharpCmprTextureReportTools
{
    public static NgcSharpDecodedTextureStats DecodeCmprMip(byte[] source, int sourceOffset, int sourceLength, int width, int height, int mipLevel, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            long pixels = 0;
            long nonBlack = 0;
            long transparent = 0;
            long totalR = 0;
            long totalG = 0;
            long totalB = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r;
                    byte g;
                    byte b;
                    byte a;
                    DecodeCmprTexel(source, sourceOffset, sourceLength, width, x, y, out r, out g, out b, out a);
                    bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                    pixels++;
                    totalR += r;
                    totalG += g;
                    totalB += b;
                    if (r != 0 || g != 0 || b != 0)
                    {
                        nonBlack++;
                    }

                    if (a == 0)
                    {
                        transparent++;
                    }
                }
            }

            bitmap.Save(outputPath, ImageFormat.Png);

            return new NgcSharpDecodedTextureStats
            {
                MipLevel = mipLevel,
                Width = width,
                Height = height,
                SourceOffset = sourceOffset,
                SourceLength = sourceLength,
                Complete = sourceOffset >= 0 && sourceLength >= 0 && sourceOffset + sourceLength <= source.Length,
                Path = outputPath,
                Pixels = pixels,
                NonBlackPixels = nonBlack,
                TransparentPixels = transparent,
                NonBlackPercent = pixels == 0 ? 0 : 100.0 * nonBlack / pixels,
                TransparentPercent = pixels == 0 ? 0 : 100.0 * transparent / pixels,
                AverageR = pixels == 0 ? 0 : (double)totalR / pixels,
                AverageG = pixels == 0 ? 0 : (double)totalG / pixels,
                AverageB = pixels == 0 ? 0 : (double)totalB / pixels,
                AverageLuma = pixels == 0 ? 0 : (double)(totalR + totalG + totalB) / (pixels * 3),
                Samples = DescribeSamples(bitmap)
            };
        }
    }

    public static void WriteContactSheet(string[] imagePaths, string[] labels, string[] details, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        int columns = 4;
        int cellWidth = 190;
        int imageHeight = 150;
        int labelHeight = 56;
        int padding = 12;
        int rows = Math.Max(1, (imagePaths.Length + columns - 1) / columns);
        using (var sheet = new Bitmap(padding + columns * (cellWidth + padding), padding + rows * (imageHeight + labelHeight + padding)))
        using (var graphics = Graphics.FromImage(sheet))
        using (var labelFont = new Font("Segoe UI", 9, FontStyle.Bold))
        using (var detailFont = new Font("Segoe UI", 8, FontStyle.Regular))
        using (var labelBrush = new SolidBrush(Color.White))
        using (var detailBrush = new SolidBrush(Color.FromArgb(205, 213, 224)))
        using (var cellBrush = new SolidBrush(Color.FromArgb(30, 34, 40)))
        {
            graphics.Clear(Color.FromArgb(16, 20, 26));
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            for (int i = 0; i < imagePaths.Length; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int x = padding + column * (cellWidth + padding);
                int y = padding + row * (imageHeight + labelHeight + padding);
                var bounds = new Rectangle(x, y, cellWidth, imageHeight);
                graphics.FillRectangle(cellBrush, bounds);
                if (File.Exists(imagePaths[i]))
                {
                    using (var image = new Bitmap(imagePaths[i]))
                    {
                        double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
                        int drawWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                        int drawHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
                        int drawX = bounds.X + (bounds.Width - drawWidth) / 2;
                        int drawY = bounds.Y + (bounds.Height - drawHeight) / 2;
                        graphics.DrawImage(image, drawX, drawY, drawWidth, drawHeight);
                    }
                }

                graphics.DrawString(labels[i] ?? String.Empty, labelFont, labelBrush, x, y + imageHeight + 6);
                graphics.DrawString(details[i] ?? String.Empty, detailFont, detailBrush, x, y + imageHeight + 26);
            }

            sheet.Save(outputPath, ImageFormat.Png);
        }
    }

    private static void DecodeCmprTexel(byte[] source, int sourceOffset, int sourceLength, int width, int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        int blockColumns = (width + 7) / 8;
        int block = (y / 8) * blockColumns + x / 8;
        int subBlock = ((y >> 2) & 1) * 2 + ((x >> 2) & 1);
        int offset = sourceOffset + block * 32 + subBlock * 8;
        if (offset < 0 || offset + 7 >= source.Length || offset + 7 >= sourceOffset + sourceLength)
        {
            r = g = b = 0;
            a = 0;
            return;
        }

        ushort color0 = ReadUInt16(source, offset);
        ushort color1 = ReadUInt16(source, offset + 2);
        uint selectors = ReadUInt32(source, offset + 4);
        int selectorShift = 30 - 2 * (((y & 3) * 4) + (x & 3));
        int selector = (int)((selectors >> selectorShift) & 3);
        byte r0;
        byte g0;
        byte b0;
        byte r1;
        byte g1;
        byte b1;
        DecodeRgb565(color0, out r0, out g0, out b0);
        DecodeRgb565(color1, out r1, out g1, out b1);
        a = 255;

        switch (selector)
        {
            case 0:
                r = r0; g = g0; b = b0; return;
            case 1:
                r = r1; g = g1; b = b1; return;
            case 2:
                if (color0 > color1)
                {
                    r = (byte)((2 * r0 + r1) / 3);
                    g = (byte)((2 * g0 + g1) / 3);
                    b = (byte)((2 * b0 + b1) / 3);
                }
                else
                {
                    r = (byte)((r0 + r1) / 2);
                    g = (byte)((g0 + g1) / 2);
                    b = (byte)((b0 + b1) / 2);
                }
                return;
            case 3:
                if (color0 > color1)
                {
                    r = (byte)((r0 + 2 * r1) / 3);
                    g = (byte)((g0 + 2 * g1) / 3);
                    b = (byte)((b0 + 2 * b1) / 3);
                }
                else
                {
                    r = g = b = 0;
                    a = 0;
                }
                return;
            default:
                r = g = b = 0;
                a = 0;
                return;
        }
    }

    private static string DescribeSamples(Bitmap bitmap)
    {
        return String.Join(" | ", new[] {
            DescribeSample(bitmap, "top_left", 0, 0),
            DescribeSample(bitmap, "center", bitmap.Width / 2, bitmap.Height / 2),
            DescribeSample(bitmap, "bottom_right", bitmap.Width - 1, bitmap.Height - 1)
        });
    }

    private static string DescribeSample(Bitmap bitmap, string name, int x, int y)
    {
        x = Math.Max(0, Math.Min(bitmap.Width - 1, x));
        y = Math.Max(0, Math.Min(bitmap.Height - 1, y));
        Color color = bitmap.GetPixel(x, y);
        return String.Format("{0}@{1}/{2}:{3}/{4}/{5}/{6}", name, x, y, color.R, color.G, color.B, color.A);
    }

    private static ushort ReadUInt16(byte[] source, int offset)
    {
        return (ushort)((source[offset] << 8) | source[offset + 1]);
    }

    private static uint ReadUInt32(byte[] source, int offset)
    {
        return ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16) | ((uint)source[offset + 2] << 8) | source[offset + 3];
    }

    private static void DecodeRgb565(ushort value, out byte r, out byte g, out byte b)
    {
        r = Expand5((value >> 11) & 0x1F);
        g = Expand6((value >> 5) & 0x3F);
        b = Expand5(value & 0x1F);
    }

    private static byte Expand5(int value)
    {
        return (byte)((value << 3) | (value >> 2));
    }

    private static byte Expand6(int value)
    {
        return (byte)((value << 2) | (value >> 4));
    }
}
"@
}

$textureBinFullPath = Resolve-FullPath $TextureBinPath
if (-not (Test-Path -LiteralPath $textureBinFullPath)) {
    throw "Texture binary not found: $textureBinFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $textureBinFullPath) "decoded-texture-report"
}

$outputDirectoryFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null
Add-CmprTextureReportType

$normalizedTextureAddress = Normalize-HexAddress $TextureAddress
$bytes = [System.IO.File]::ReadAllBytes($textureBinFullPath)
$rows = New-Object System.Collections.Generic.List[object]
$imagePaths = New-Object System.Collections.Generic.List[string]
$labels = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

$offset = 0
$levelWidth = $Width
$levelHeight = $Height
$level = 0
while ($level -le $MaxMipLevel -and $levelWidth -gt 0 -and $levelHeight -gt 0) {
    $byteLength = Get-CmprByteLength $levelWidth $levelHeight
    $fileName = ("texture_{0}_mip{1}_{2}x{3}_CMPR.png" -f $normalizedTextureAddress.Replace("0x", ""), $level, $levelWidth, $levelHeight)
    $pngPath = Join-Path $outputDirectoryFullPath $fileName
    $stats = [NgcSharpCmprTextureReportTools]::DecodeCmprMip($bytes, $offset, $byteLength, $levelWidth, $levelHeight, $level, $pngPath)

    $row = [pscustomobject]@{
        texture_address = $normalizedTextureAddress
        mip_level = $level
        width = $levelWidth
        height = $levelHeight
        source_offset = ("0x{0:X}" -f $offset)
        source_length = ("0x{0:X}" -f $byteLength)
        complete = $stats.Complete
        path = $pngPath
        pixels = $stats.Pixels
        nonblack_pixels = $stats.NonBlackPixels
        nonblack_percent = [Math]::Round($stats.NonBlackPercent, 3)
        transparent_pixels = $stats.TransparentPixels
        transparent_percent = [Math]::Round($stats.TransparentPercent, 3)
        average_luma = [Math]::Round($stats.AverageLuma, 3)
        average_rgb = ("{0:N3}/{1:N3}/{2:N3}" -f $stats.AverageR, $stats.AverageG, $stats.AverageB)
        samples = $stats.Samples
    }
    $rows.Add($row) | Out-Null
    $imagePaths.Add($pngPath) | Out-Null
    $labels.Add(("mip{0} {1}x{2}" -f $level, $levelWidth, $levelHeight)) | Out-Null
    $details.Add(("luma {0}; nonblack {1}%" -f $row.average_luma, $row.nonblack_percent)) | Out-Null

    $offset += $byteLength
    if ($levelWidth -eq 1 -and $levelHeight -eq 1) {
        break
    }

    $levelWidth = [Math]::Max(1, [int]($levelWidth / 2))
    $levelHeight = [Math]::Max(1, [int]($levelHeight / 2))
    $level++
}

$bindRows = @()
if (-not [string]::IsNullOrWhiteSpace($TextureBindCsvPath)) {
    $bindPath = Resolve-FullPath $TextureBindCsvPath
    if (Test-Path -LiteralPath $bindPath) {
        $bindRows = @(Import-Csv -LiteralPath $bindPath | Where-Object { (Normalize-HexAddress ([string]$_.source_address)) -eq $normalizedTextureAddress })
    }
}

$tevRows = @()
if (-not [string]::IsNullOrWhiteSpace($TevSummaryCsvPath)) {
    $tevPath = Resolve-FullPath $TevSummaryCsvPath
    if (Test-Path -LiteralPath $tevPath) {
        $tevRows = @(Import-Csv -LiteralPath $tevPath | Where-Object { (Normalize-HexAddress ([string]$_.texture_address)) -eq $normalizedTextureAddress })
    }
}

$bitstreamRows = @()
if (-not [string]::IsNullOrWhiteSpace($BitstreamCsvPath)) {
    $bitstreamPath = Resolve-FullPath $BitstreamCsvPath
    if (Test-Path -LiteralPath $bitstreamPath) {
        $bitstreamRows = @(Import-Csv -LiteralPath $bitstreamPath)
    }
}

$writeProvenance = $null
if (-not [string]::IsNullOrWhiteSpace($TextureWriteProvenanceJsonPath)) {
    $writeProvenancePath = Resolve-FullPath $TextureWriteProvenanceJsonPath
    if (Test-Path -LiteralPath $writeProvenancePath) {
        $writeProvenance = Get-Content -LiteralPath $writeProvenancePath -Raw | ConvertFrom-Json
    }
}

$csvPath = Join-Path $outputDirectoryFullPath "decoded-texture-mips.csv"
$jsonPath = Join-Path $outputDirectoryFullPath "decoded-texture-report.json"
$contactSheetPath = Join-Path $outputDirectoryFullPath "decoded-texture-contact-sheet.png"

$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[NgcSharpCmprTextureReportTools]::WriteContactSheet($imagePaths.ToArray(), $labels.ToArray(), $details.ToArray(), $contactSheetPath)

$firstBind = $bindRows | Sort-Object { [int64]$_.instruction } | Select-Object -First 1
$firstTev = $tevRows | Sort-Object { [int]$_.draw_index } | Select-Object -First 1
$decoderRow = $bitstreamRows | Select-Object -First 1
$mip0 = $rows | Select-Object -First 1

[pscustomobject]@{
    texture_address = $normalizedTextureAddress
    texture_bin_path = $textureBinFullPath
    output_directory = $outputDirectoryFullPath
    width = $Width
    height = $Height
    format = "CMPR"
    bytes_available = $bytes.Length
    decoded_bytes_consumed = $offset
    mip_count = $rows.Count
    mip0_average_luma = Get-ObjectValue $mip0 "average_luma"
    mip0_nonblack_percent = Get-ObjectValue $mip0 "nonblack_percent"
    first_bind = $firstBind
    first_tev_summary = $firstTev
    bitstream_decoder = $decoderRow
    texture_write_provenance = $writeProvenance
    mip_rows = @($rows.ToArray())
    csv = $csvPath
    contact_sheet = $contactSheetPath
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    contact_sheet = $contactSheetPath
    mip_count = $rows.Count
    mip0_luma = Get-ObjectValue $mip0 "average_luma"
    mip0_nonblack_percent = Get-ObjectValue $mip0 "nonblack_percent"
    bind_rows = $bindRows.Count
    tev_rows = $tevRows.Count
    bitstream_rows = $bitstreamRows.Count
}
