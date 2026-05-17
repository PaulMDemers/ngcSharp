param(
    [string]$SweepRoot = "",
    [string]$SummaryPath = "",
    [string]$OutPath = "",
    [int]$Columns = 4,
    [int]$ThumbWidth = 240,
    [int]$ThumbHeight = 180,
    [switch]$Open
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Find-LatestSweepRoot {
    $root = Resolve-FullPath "artifacts/demo-sweep"
    $latest = Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "summary.csv") } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No sweep with summary.csv found under $root."
    }

    return $latest.FullName
}

function Convert-ToInt {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0
    }

    return [int]$Value
}

function Get-ShortName {
    param([string]$Name)

    $text = $Name.TrimEnd(".")
    $parts = $text -split '[\\/]'
    if ($parts.Count -eq 0) {
        return $text
    }

    return $parts[$parts.Count - 1]
}

function Split-TextLines {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [System.Drawing.Font]$Font,
        [int]$MaxWidth,
        [int]$MaxLines
    )

    $words = $Text -split '\s+|-'
    $lines = New-Object System.Collections.Generic.List[string]
    $current = ""
    foreach ($word in $words) {
        if ([string]::IsNullOrWhiteSpace($word)) {
            continue
        }

        $candidate = if ($current.Length -eq 0) { $word } else { "$current $word" }
        if ($Graphics.MeasureString($candidate, $Font).Width -le $MaxWidth) {
            $current = $candidate
            continue
        }

        if ($current.Length -ne 0) {
            $lines.Add($current)
            $current = $word
        } else {
            $lines.Add($word)
        }

        if ($lines.Count -ge $MaxLines) {
            break
        }
    }

    if ($lines.Count -lt $MaxLines -and $current.Length -ne 0) {
        $lines.Add($current)
    }

    if ($lines.Count -gt $MaxLines) {
        return $lines.GetRange(0, $MaxLines)
    }

    return $lines
}

function New-LoadedBitmap {
    param([string]$Path)

    $source = [System.Drawing.Image]::FromFile($Path)
    try {
        return New-Object System.Drawing.Bitmap $source
    } finally {
        $source.Dispose()
    }
}

function Draw-ContainedImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.Rectangle]$Bounds
    )

    $scale = [Math]::Min($Bounds.Width / $Image.Width, $Bounds.Height / $Image.Height)
    $drawWidth = [int][Math]::Round($Image.Width * $scale)
    $drawHeight = [int][Math]::Round($Image.Height * $scale)
    $x = $Bounds.X + [int][Math]::Floor(($Bounds.Width - $drawWidth) / 2)
    $y = $Bounds.Y + [int][Math]::Floor(($Bounds.Height - $drawHeight) / 2)
    $Graphics.DrawImage($Image, $x, $y, $drawWidth, $drawHeight)
}

if ($Columns -le 0) {
    throw "Columns must be positive."
}

if ($ThumbWidth -le 0 -or $ThumbHeight -le 0) {
    throw "Thumbnail dimensions must be positive."
}

if ([string]::IsNullOrWhiteSpace($SweepRoot)) {
    $SweepRoot = Find-LatestSweepRoot
} else {
    $SweepRoot = Resolve-FullPath $SweepRoot
}

if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
    $SummaryPath = Join-Path $SweepRoot "summary.csv"
} else {
    $SummaryPath = Resolve-FullPath $SummaryPath
}

if (-not (Test-Path -LiteralPath $SummaryPath)) {
    throw "Summary file not found: $SummaryPath"
}

if ([string]::IsNullOrWhiteSpace($OutPath)) {
    $OutPath = Join-Path $SweepRoot "gx-contact-sheet.png"
} else {
    $OutPath = Resolve-FullPath $OutPath
}

Add-Type -AssemblyName System.Drawing

$items = @(
    Import-Csv -LiteralPath $SummaryPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.gxFrame) } |
        Sort-Object name
)

if ($items.Count -eq 0) {
    throw "No gxFrame entries found in $SummaryPath. Re-run the sweep with -DumpGxFrames."
}

$cellPadding = 12
$labelHeight = 86
$cellWidth = $ThumbWidth + $cellPadding * 2
$cellHeight = $ThumbHeight + $labelHeight + $cellPadding * 2
$headerHeight = 64
$rowCount = [int][Math]::Ceiling($items.Count / $Columns)
$sheetWidth = $Columns * $cellWidth
$sheetHeight = $headerHeight + $rowCount * $cellHeight

$bitmap = New-Object System.Drawing.Bitmap $sheetWidth, $sheetHeight
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$fonts = @()
$brushes = @()
$pens = @()

try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(20, 22, 25))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $titleFont = New-Object System.Drawing.Font "Segoe UI", 15, ([System.Drawing.FontStyle]::Bold)
    $metaFont = New-Object System.Drawing.Font "Segoe UI", 9
    $nameFont = New-Object System.Drawing.Font "Segoe UI", 9, ([System.Drawing.FontStyle]::Bold)
    $smallFont = New-Object System.Drawing.Font "Segoe UI", 8
    $fonts += $titleFont, $metaFont, $nameFont, $smallFont

    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(238, 241, 245))
    $mutedBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(168, 176, 188))
    $cardBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(34, 37, 42))
    $imageBackBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)
    $greenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(79, 194, 119))
    $orangeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(229, 168, 74))
    $redBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 92, 92))
    $brushes += $whiteBrush, $mutedBrush, $cardBrush, $imageBackBrush, $greenBrush, $orangeBrush, $redBrush

    $cardPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(68, 73, 82)), 1
    $greenPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(79, 194, 119)), 2
    $orangePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(229, 168, 74)), 2
    $redPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 92, 92)), 2
    $pens += $cardPen, $greenPen, $orangePen, $redPen

    $graphics.DrawString("GX diagnostic contact sheet", $titleFont, $whiteBrush, 12, 10)
    $graphics.DrawString("$($items.Count) frame(s) from $SweepRoot", $metaFont, $mutedBrush, 13, 38)

    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $column = $index % $Columns
        $row = [int][Math]::Floor($index / $Columns)
        $cellX = $column * $cellWidth
        $cellY = $headerHeight + $row * $cellHeight
        $cardRect = New-Object System.Drawing.Rectangle ($cellX + 6), ($cellY + 6), ($cellWidth - 12), ($cellHeight - 12)
        $imageRect = New-Object System.Drawing.Rectangle ($cellX + $cellPadding), ($cellY + $cellPadding), $ThumbWidth, $ThumbHeight

        $draws = Convert-ToInt $item.gxDraws
        $rendered = (Convert-ToInt $item.renderedQuads) + (Convert-ToInt $item.renderedTriangles)
        $setupOnly = $item.milestone -eq "gx-setup-only" -or ($draws -eq 0 -and (Convert-ToInt $item.gxBytes) -gt 0 -and $item.finalInstruction -eq "48000000")
        $borderPen = if ($rendered -gt 0 -or $setupOnly) { $greenPen } elseif ($draws -gt 0) { $orangePen } else { $redPen }
        $statusBrush = if ($rendered -gt 0 -or $setupOnly) { $greenBrush } elseif ($draws -gt 0) { $orangeBrush } else { $redBrush }

        $graphics.FillRectangle($cardBrush, $cardRect)
        $graphics.DrawRectangle($borderPen, $cardRect)
        $graphics.FillRectangle($imageBackBrush, $imageRect)
        $graphics.DrawRectangle($cardPen, $imageRect)

        if (Test-Path -LiteralPath $item.gxFrame) {
            $image = New-LoadedBitmap $item.gxFrame
            try {
                Draw-ContainedImage -Graphics $graphics -Image $image -Bounds $imageRect
            } finally {
                $image.Dispose()
            }
        } else {
            $graphics.DrawString("missing frame", $metaFont, $redBrush, $imageRect.X + 8, $imageRect.Y + 8)
        }

        $labelX = $cellX + $cellPadding
        $labelY = $cellY + $cellPadding + $ThumbHeight + 8
        $lines = Split-TextLines -Graphics $graphics -Text (Get-ShortName $item.name) -Font $nameFont -MaxWidth $ThumbWidth -MaxLines 2
        foreach ($line in $lines) {
            $graphics.DrawString($line, $nameFont, $whiteBrush, $labelX, $labelY)
            $labelY += 16
        }

        $metric = "draws $draws | quads $($item.renderedQuads) | tris $($item.renderedTriangles)"
        $graphics.DrawString($metric, $smallFont, $mutedBrush, $labelX, $cellY + $cellHeight - 42)
        $graphics.FillEllipse($statusBrush, $labelX, $cellY + $cellHeight - 22, 8, 8)
        $graphics.DrawString($item.milestone, $smallFont, $mutedBrush, $labelX + 13, $cellY + $cellHeight - 25)
    }

    $outDirectory = Split-Path -Parent $OutPath
    if (-not [string]::IsNullOrWhiteSpace($outDirectory)) {
        New-Item -ItemType Directory -Force -Path $outDirectory | Out-Null
    }

    $bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($pen in $pens) {
        $pen.Dispose()
    }

    foreach ($brush in $brushes) {
        $brush.Dispose()
    }

    foreach ($font in $fonts) {
        $font.Dispose()
    }

    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "GX contact sheet: $OutPath"

if ($Open) {
    Invoke-Item -LiteralPath $OutPath
}
