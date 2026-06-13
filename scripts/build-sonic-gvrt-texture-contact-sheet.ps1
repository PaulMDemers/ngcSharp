param(
    [Parameter(Mandatory = $true)]
    [string]$DecodedPath,
    [Parameter(Mandatory = $true)]
    [string]$GvrtCsvPath,
    [string]$TextureIndexCsvPath = "",
    [string]$MaterialSummaryCsvPath = "",
    [string]$OutputDirectory = "",
    [string]$FocusPayloadAddress = "0x8137DFA0",
    [int]$FocusRadiusBytes = 0x4000,
    [int]$MaxRows = 24
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-UInt32 {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Parse-Int64 {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return 0L
    }

    $trimmed = $Text.Trim()
    $negative = $false
    if ($trimmed.StartsWith("-", [System.StringComparison]::Ordinal)) {
        $negative = $true
        $trimmed = $trimmed.Substring(1)
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $value = [int64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
        if ($negative) {
            return -$value
        }

        return $value
    }

    $parsed = [int64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
    if ($negative) {
        return -$parsed
    }

    return $parsed
}

function Convert-HexOffsetToInt {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [int]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-CmprMip0ByteLength {
    param(
        [int]$Width,
        [int]$Height
    )

    return [int]([Math]::Floor(($Width + 7) / 8) * [Math]::Floor(($Height + 7) / 8) * 32)
}

function Get-Fnv1A32Hex {
    param(
        [byte[]]$Bytes,
        [int]$Offset,
        [int]$Length
    )

    $hash = [uint32]2166136261
    for ($index = 0; $index -lt $Length; $index++) {
        $hash = $hash -bxor [uint32]$Bytes[$Offset + $index]
        $hash = [uint32](([uint64]$hash * [uint64]16777619) -band [uint64]4294967295)
    }

    return "0x{0:X8}" -f $hash
}

function Get-TopValues {
    param(
        [object[]]$Values,
        [int]$Count = 6
    )

    $nonEmpty = @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($nonEmpty.Count -eq 0) {
        return ""
    }

    return (($nonEmpty |
        Group-Object |
        Sort-Object @{ Expression = "Count"; Descending = $true }, @{ Expression = "Name"; Descending = $false } |
        Select-Object -First $Count |
        ForEach-Object { "$($_.Name)x$($_.Count)" }) -join " | ")
}

function Format-CompactDrawList {
    param([object[]]$Draws)

    $numbers = @($Draws | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { [int]$_ } | Sort-Object -Unique)
    if ($numbers.Count -eq 0) {
        return "none"
    }

    if ($numbers.Count -gt 1 -and ($numbers[-1] - $numbers[0] + 1) -eq $numbers.Count) {
        return "{0}..{1}" -f $numbers[0], $numbers[-1]
    }

    if ($numbers.Count -le 4) {
        return ($numbers -join ",")
    }

    return "{0},{1},{2},+{3}" -f $numbers[0], $numbers[1], $numbers[2], ($numbers.Count - 3)
}

function Add-GvrtContactSheetType {
    if ("NgcSharpGvrtContactSheetTools" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public sealed class NgcSharpGvrtImageStats
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

public static class NgcSharpGvrtContactSheetTools
{
    public static NgcSharpGvrtImageStats DecodeCmprMip0(byte[] source, int sourceOffset, int sourceLength, int width, int height, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            long pixels = 0;
            long nonBlack = 0;
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
                }
            }

            bitmap.Save(outputPath, ImageFormat.Png);
            return new NgcSharpGvrtImageStats
            {
                Path = outputPath,
                Width = width,
                Height = height,
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

    public static void WriteContactSheet(string[] imagePaths, string[] labels, string[] details, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        int columns = 3;
        int cellWidth = 260;
        int imageHeight = 190;
        int labelHeight = 70;
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
                graphics.DrawString(details[i] ?? String.Empty, detailFont, detailBrush, x, y + imageHeight + 28);
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
            default:
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
        }
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
        r = (byte)((((value >> 11) & 0x1F) << 3) | (((value >> 11) & 0x1F) >> 2));
        g = (byte)((((value >> 5) & 0x3F) << 2) | (((value >> 5) & 0x3F) >> 4));
        b = (byte)(((value & 0x1F) << 3) | ((value & 0x1F) >> 2));
    }
}
"@
}

$decodedFullPath = Resolve-FullPath $DecodedPath
$gvrtFullPath = Resolve-FullPath $GvrtCsvPath
if (-not (Test-Path -LiteralPath $decodedFullPath)) {
    throw "Decoded resource not found: $decodedFullPath"
}

if (-not (Test-Path -LiteralPath $gvrtFullPath)) {
    throw "GVRT CSV not found: $gvrtFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $gvrtFullPath) "gvrt-contact-sheet"
}

$outputDirectoryFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$textureIndexRows = @()
if (-not [string]::IsNullOrWhiteSpace($TextureIndexCsvPath)) {
    $textureIndexFullPath = Resolve-FullPath $TextureIndexCsvPath
    if (Test-Path -LiteralPath $textureIndexFullPath) {
        $textureIndexRows = @(Import-Csv -LiteralPath $textureIndexFullPath)
    }
}

$materialRows = @()
if (-not [string]::IsNullOrWhiteSpace($MaterialSummaryCsvPath)) {
    $materialFullPath = Resolve-FullPath $MaterialSummaryCsvPath
    if (Test-Path -LiteralPath $materialFullPath) {
        $materialRows = @(Import-Csv -LiteralPath $materialFullPath)
    }
}

Add-GvrtContactSheetType
$bytes = [System.IO.File]::ReadAllBytes($decodedFullPath)
$focusPayload = Parse-UInt32 $FocusPayloadAddress
$gvrtRows = @(Import-Csv -LiteralPath $gvrtFullPath |
    Where-Object {
        $_.format_name -eq "CMPR" -and
        [int]$_.width -gt 0 -and
        [int]$_.height -gt 0 -and
        [Math]::Abs((Parse-UInt32 $_.payload_address) - [int64]$focusPayload) -le $FocusRadiusBytes
    } |
    Sort-Object { [Math]::Abs((Parse-UInt32 $_.payload_address) - [int64]$focusPayload) } |
    Select-Object -First $MaxRows)

$imagePaths = New-Object System.Collections.Generic.List[string]
$labels = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]
$rows = New-Object System.Collections.Generic.List[object]

foreach ($gvrt in $gvrtRows) {
    $payload = Parse-UInt32 $gvrt.payload_address
    $decodedOffset = Convert-HexOffsetToInt $gvrt.decoded_offset
    $payloadOffset = $decodedOffset + 0x10
    $width = [int]$gvrt.width
    $height = [int]$gvrt.height
    $mip0Bytes = Get-CmprMip0ByteLength $width $height
    $payloadBytes = Convert-HexOffsetToInt $gvrt.data_bytes_hint_hex
    $hashBytes = [Math]::Min($payloadBytes, $bytes.Length - $payloadOffset)
    $sourceHash = if ($hashBytes -gt 0) { Get-Fnv1A32Hex $bytes $payloadOffset $hashBytes } else { "" }
    $mip0Hash = if ($mip0Bytes -gt 0 -and $payloadOffset + $mip0Bytes -le $bytes.Length) { Get-Fnv1A32Hex $bytes $payloadOffset $mip0Bytes } else { "" }
    $pngName = "gvrt_{0}_{1}x{2}_{3}.png" -f ([string]$gvrt.payload_address).Replace("0x", ""), $width, $height, $gvrt.format_name
    $pngPath = Join-Path $outputDirectoryFullPath $pngName
    $stats = [NgcSharpGvrtContactSheetTools]::DecodeCmprMip0($bytes, $payloadOffset, $mip0Bytes, $width, $height, $pngPath)

    $runtimeMatches = @($textureIndexRows | Where-Object {
        $_.mip_level -eq "0" -and
        $_.format -eq $gvrt.format_name -and
        [int]$_.width -eq $width -and
        [int]$_.height -eq $height -and
        [int]$_.source_bytes -eq $mip0Bytes -and
        $_.source_hash -eq $mip0Hash
    })

    $runtimeAddresses = @($runtimeMatches | Select-Object -ExpandProperty source_address -Unique)
    $runtimeDraws = @($runtimeMatches | Select-Object -ExpandProperty draw_index -Unique)
    $matchingMaterials = @()
    if ($runtimeAddresses.Count -gt 0 -and $materialRows.Count -gt 0) {
        $addressSet = @{}
        foreach ($address in $runtimeAddresses) {
            $addressSet[[string]$address] = $true
        }

        $matchingMaterials = @($materialRows | Where-Object { $addressSet.ContainsKey([string]$_.texture_address) })
    }

    $label = "{0} {1}x{2}" -f $gvrt.payload_address, $width, $height
    $detail = "luma {0:N3}; hash {1}; draws {2}" -f $stats.AverageLuma, $sourceHash, (Format-CompactDrawList $runtimeDraws)
    $imagePaths.Add($pngPath) | Out-Null
    $labels.Add($label) | Out-Null
    $details.Add($detail) | Out-Null

    $rows.Add([pscustomobject]@{
        header_address = $gvrt.header_address
        payload_address = $gvrt.payload_address
        focus_payload_delta = $gvrt.focus_payload_delta
        format_name = $gvrt.format_name
        width = $width
        height = $height
        payload_bytes = $payloadBytes
        payload_hash = $sourceHash
        mip0_bytes = $mip0Bytes
        mip0_hash = $mip0Hash
        png_path = $pngPath
        average_luma = [Math]::Round($stats.AverageLuma, 3)
        average_rgb = ("{0:N3}/{1:N3}/{2:N3}" -f $stats.AverageR, $stats.AverageG, $stats.AverageB)
        nonblack_percent = [Math]::Round($stats.NonBlackPercent, 3)
        runtime_match_count = $runtimeMatches.Count
        runtime_texture_addresses = $runtimeAddresses -join " "
        runtime_draws = $runtimeDraws -join " "
        runtime_texture_paths = (($runtimeMatches | Select-Object -First 6 | ForEach-Object { $_.path }) -join " ")
        material_count = $matchingMaterials.Count
        material_covered_pixels = (($matchingMaterials | ForEach-Object { if ([string]::IsNullOrWhiteSpace([string]$_.covered_pixels)) { 0L } else { [int64]$_.covered_pixels } } | Measure-Object -Sum).Sum)
        material_color_writes = (($matchingMaterials | ForEach-Object { if ([string]::IsNullOrWhiteSpace([string]$_.color_writes)) { 0L } else { [int64]$_.color_writes } } | Measure-Object -Sum).Sum)
        material_black_write_ratio_top = Get-TopValues ($matchingMaterials | ForEach-Object { $_.black_write_ratio }) 3
        material_draws = (($matchingMaterials | ForEach-Object { $_.draws }) -join " ")
    }) | Out-Null
}

$csvPath = Join-Path $outputDirectoryFullPath "sonic-gvrt-contact-sheet.csv"
$jsonPath = Join-Path $outputDirectoryFullPath "sonic-gvrt-contact-sheet.json"
$contactSheetPath = Join-Path $outputDirectoryFullPath "sonic-gvrt-contact-sheet.png"

$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[NgcSharpGvrtContactSheetTools]::WriteContactSheet($imagePaths.ToArray(), $labels.ToArray(), $details.ToArray(), $contactSheetPath)
[pscustomobject]@{
    decoded_path = $decodedFullPath
    gvrt_csv_path = $gvrtFullPath
    texture_index_csv_path = if ($textureIndexRows.Count -gt 0) { (Resolve-FullPath $TextureIndexCsvPath) } else { "" }
    material_summary_csv_path = if ($materialRows.Count -gt 0) { (Resolve-FullPath $MaterialSummaryCsvPath) } else { "" }
    focus_payload_address = "0x{0:X8}" -f $focusPayload
    focus_radius_bytes = $FocusRadiusBytes
    row_count = $rows.Count
    csv = $csvPath
    contact_sheet = $contactSheetPath
    rows = @($rows.ToArray())
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    contact_sheet = $contactSheetPath
    rows = $rows.Count
}
