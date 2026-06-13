param(
    [Parameter(Mandatory = $true)]
    [string]$TevSampleCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$TextureIndexCsvPath,
    [Parameter(Mandatory = $true)]
    [int]$DrawIndex,
    [string]$OutputDirectory = "",
    [int]$Scale = 8
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Get-StageField {
    param(
        [string]$Text,
        [string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $match = [regex]::Match($Text, $Pattern)
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value
}

function Convert-ToDouble {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0.0
    }

    return [double]::Parse($Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Add-TextureMapImageType {
    if ("NgcSharpTextureSampleMapTools" -as [type]) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    Add-Type -ReferencedAssemblies "System.Drawing" -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public sealed class NgcSharpTextureSamplePoint
{
    public string Label { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
}

public static class NgcSharpTextureSampleMapTools
{
    public static void WriteOverlay(string texturePath, string outputPath, NgcSharpTextureSamplePoint[] points, int scale)
    {
        scale = Math.Max(1, scale);
        using (var source = new Bitmap(texturePath))
        using (var output = new Bitmap(source.Width * scale, source.Height * scale))
        using (var graphics = Graphics.FromImage(output))
        using (var redPen = new Pen(Color.FromArgb(255, 64, 64), Math.Max(1, scale / 2)))
        using (var yellowPen = new Pen(Color.FromArgb(255, 230, 64), Math.Max(1, scale / 2)))
        using (var font = new Font("Segoe UI", Math.Max(8, scale + 2), FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
        using (var shadow = new SolidBrush(Color.Black))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.Clear(Color.Black);
            graphics.DrawImage(source, 0, 0, output.Width, output.Height);

            foreach (var point in points)
            {
                int x = point.X * scale + scale / 2;
                int y = point.Y * scale + scale / 2;
                var pen = point.R == 0 && point.G == 0 && point.B == 0 ? redPen : yellowPen;
                int radius = Math.Max(4, scale);
                graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                graphics.DrawLine(pen, x - radius - 2, y, x + radius + 2, y);
                graphics.DrawLine(pen, x, y - radius - 2, x, y + radius + 2);

                if (!String.IsNullOrEmpty(point.Label))
                {
                    float tx = Math.Min(output.Width - 40, x + radius + 2);
                    float ty = Math.Max(0, y - radius - 2);
                    graphics.DrawString(point.Label, font, shadow, tx + 1, ty + 1);
                    graphics.DrawString(point.Label, font, brush, tx, ty);
                }
            }

            string parent = System.IO.Path.GetDirectoryName(outputPath);
            if (!String.IsNullOrEmpty(parent))
            {
                System.IO.Directory.CreateDirectory(parent);
            }

            output.Save(outputPath, ImageFormat.Png);
        }
    }
}
"@
}

$tevSampleCsvFullPath = Resolve-FullPath $TevSampleCsvPath
$textureIndexCsvFullPath = Resolve-FullPath $TextureIndexCsvPath
if (-not (Test-Path -LiteralPath $tevSampleCsvFullPath)) {
    throw "TEV sample CSV not found: $tevSampleCsvFullPath"
}

if (-not (Test-Path -LiteralPath $textureIndexCsvFullPath)) {
    throw "Texture index CSV not found: $textureIndexCsvFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $tevSampleCsvFullPath) ("draw{0}-texture-samples" -f $DrawIndex)
}

$outputDirectoryFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$tevRows = @(Import-Csv -LiteralPath $tevSampleCsvFullPath | Where-Object { [int]$_.draw_index -eq $DrawIndex })
if ($tevRows.Count -eq 0) {
    throw "No TEV sample rows found for draw $DrawIndex."
}

$textureRows = @(Import-Csv -LiteralPath $textureIndexCsvFullPath)
$textureDirectory = Split-Path -Parent $textureIndexCsvFullPath

$sampleRows = New-Object System.Collections.Generic.List[object]
foreach ($row in $tevRows) {
    $stageSummary = [string]$row.stage_summary
    $textureAddress = Get-StageField $stageSummary 'addr=(0x[0-9A-Fa-f]+)'
    $format = Get-StageField $stageSummary 'fmt=([^/;]+)'
    $size = Get-StageField $stageSummary 'size=([^/;]+)'
    $s = Convert-ToDouble (Get-StageField $stageSummary '/s=([^/;]+)')
    $t = Convert-ToDouble (Get-StageField $stageSummary '/t=([^/;]+)')
    $xy = Get-StageField $stageSummary '/xy=([^/;]+)'
    $texture = Get-StageField $stageSummary 'texture=(\d+/\d+/\d+/\d+)'
    $x = ""
    $y = ""
    if ($xy -match '^(-?\d+):(-?\d+)$') {
        $x = [int]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
        $y = [int]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $sampleRows.Add([pscustomobject]@{
        draw_index = $row.draw_index
        fifo_offset = $row.fifo_offset
        triangle_index = $row.triangle_index
        sample_name = $row.sample_name
        selected_texture_lod = $row.selected_texture_lod
        texture_address = $textureAddress.ToUpperInvariant()
        texture_format = $format
        texture_size = $size
        s = $s
        t = $t
        x = $x
        y = $y
        texture_rgba = $texture
        raster_rgba = $row.raster_rgba
        tev_rgba = $row.tev_rgba
    }) | Out-Null
}

$baseTextureAddress = ($sampleRows | Select-Object -First 1).texture_address
$textureRow = $textureRows |
    Where-Object { $_.source_address.ToUpperInvariant() -eq $baseTextureAddress -and $_.mip_level -eq "0" } |
    Select-Object -First 1
if ($null -eq $textureRow) {
    throw "Could not find mip0 texture dump row for $baseTextureAddress."
}

$texturePath = Join-Path $textureDirectory ([string]$textureRow.path)
if (-not (Test-Path -LiteralPath $texturePath)) {
    throw "Texture PNG not found: $texturePath"
}

Add-TextureMapImageType

$points = New-Object System.Collections.Generic.List[NgcSharpTextureSamplePoint]
$index = 0
foreach ($sample in $sampleRows) {
    $index++
    if ([string]::IsNullOrWhiteSpace([string]$sample.x) -or [string]::IsNullOrWhiteSpace([string]$sample.y)) {
        continue
    }

    $r = 0
    $g = 0
    $b = 0
    if ([string]$sample.texture_rgba -match '^(\d+)\/(\d+)\/(\d+)\/(\d+)$') {
        $r = [int]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
        $g = [int]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
        $b = [int]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $points.Add([NgcSharpTextureSamplePoint]@{
        Label = $index.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        X = [int]$sample.x
        Y = [int]$sample.y
        R = $r
        G = $g
        B = $b
    }) | Out-Null
}

$csvPath = Join-Path $outputDirectoryFullPath ("draw{0}-texture-samples.csv" -f $DrawIndex)
$jsonPath = Join-Path $outputDirectoryFullPath ("draw{0}-texture-samples.json" -f $DrawIndex)
$overlayPath = Join-Path $outputDirectoryFullPath ("draw{0}-texture-samples.png" -f $DrawIndex)

$sampleRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    draw_index = $DrawIndex
    source = $tevSampleCsvFullPath
    texture_index = $textureIndexCsvFullPath
    texture_path = $texturePath
    texture_address = $baseTextureAddress
    samples = @($sampleRows.ToArray())
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[NgcSharpTextureSampleMapTools]::WriteOverlay($texturePath, $overlayPath, $points.ToArray(), $Scale)

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    overlay = $overlayPath
    samples = $sampleRows.Count
}
