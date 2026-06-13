param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FramePath = "",
    [int]$FrameWidth = 640,
    [int]$FrameHeight = 480
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

function Test-CsvHasRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Import-Csv -LiteralPath $Path | Select-Object -First 1)
}

function Convert-ToNullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToNullableInt {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Split-Range {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or -not $Value.Contains("..")) {
        return [pscustomobject]@{ HasValue = $false; Min = $null; Max = $null }
    }

    $parts = $Value -split "\.\.", 2
    $min = Convert-ToNullableDouble $parts[0]
    $max = Convert-ToNullableDouble $parts[1]
    if ($null -eq $min -or $null -eq $max) {
        return [pscustomobject]@{ HasValue = $false; Min = $null; Max = $null }
    }

    return [pscustomobject]@{ HasValue = $true; Min = [Math]::Min($min, $max); Max = [Math]::Max($min, $max) }
}

function Clamp-Double {
    param(
        [double]$Value,
        [double]$Min,
        [double]$Max
    )

    return [Math]::Max($Min, [Math]::Min($Max, $Value))
}

function Get-SequenceColor {
    param(
        [string]$PartitionKind,
        [string]$PrimaryReason
    )

    if ($PartitionKind -eq "dark-material") {
        return [System.Drawing.Color]::FromArgb(235, 255, 92, 92)
    }

    if ($PartitionKind -eq "lit-material") {
        return [System.Drawing.Color]::FromArgb(235, 70, 220, 120)
    }

    switch ($PrimaryReason) {
        "fully-clipped" { return [System.Drawing.Color]::FromArgb(235, 190, 120, 255) }
        "offscreen-above" { return [System.Drawing.Color]::FromArgb(235, 255, 190, 70) }
        "offscreen-left" { return [System.Drawing.Color]::FromArgb(235, 80, 180, 255) }
        "offscreen-right" { return [System.Drawing.Color]::FromArgb(235, 255, 130, 45) }
        default { return [System.Drawing.Color]::FromArgb(235, 230, 230, 230) }
    }
}

function Get-FrameCandidate {
    param([string]$RunRoot)

    foreach ($candidate in @(
        (Join-Path $RunRoot "auto.png"),
        (Join-Path $RunRoot "candidate.png"),
        (Join-Path $RunRoot "gx-frame.png"),
        (Join-Path $RunRoot "frame.png")
    )) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return ""
}

function New-BaseBitmap {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) {
        $loaded = [System.Drawing.Bitmap]::FromFile($Path)
        try {
            return New-Object System.Drawing.Bitmap $loaded
        } finally {
            $loaded.Dispose()
        }
    }

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 12, 16, 22))
    } finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Draw-TextWithBackplate {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [System.Drawing.Font]$Font,
        [System.Drawing.Brush]$Brush,
        [float]$X,
        [float]$Y
    )

    $size = $Graphics.MeasureString($Text, $Font)
    $back = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(190, 0, 0, 0))
    try {
        $Graphics.FillRectangle($back, $X - 2, $Y - 1, $size.Width + 4, $size.Height + 2)
        $Graphics.DrawString($Text, $Font, $Brush, $X, $Y)
    } finally {
        $back.Dispose()
    }
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-packet-placement-overlay"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$sequenceCsvPath = Join-Path $runRoot "sonic-nonrendered-strips\nonrendered-strip-sequences.csv"
if (-not (Test-CsvHasRows $sequenceCsvPath)) {
    throw "Required CSV missing or empty: $sequenceCsvPath"
}

if ([string]::IsNullOrWhiteSpace($FramePath)) {
    $FramePath = Get-FrameCandidate $runRoot
} else {
    $FramePath = Resolve-FullPath $FramePath
}

$base = New-BaseBitmap -Path $FramePath -Width $FrameWidth -Height $FrameHeight
$frameWidth = $base.Width
$frameHeight = $base.Height
$overlay = New-Object System.Drawing.Bitmap $base
$boundsRows = New-Object System.Collections.Generic.List[object]

$graphics = [System.Drawing.Graphics]::FromImage($overlay)
$font = New-Object System.Drawing.Font "Consolas", 10
$smallFont = New-Object System.Drawing.Font "Consolas", 8
$whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    foreach ($row in (Import-Csv -LiteralPath $sequenceCsvPath)) {
        $index = Convert-ToNullableInt (Get-ObjectValue $row "sequence_index")
        $partitionKind = [string](Get-ObjectValue $row "partition_kind")
        $primaryReason = [string](Get-ObjectValue $row "primary_reason")
        $color = Get-SequenceColor -PartitionKind $partitionKind -PrimaryReason $primaryReason
        $pen = New-Object System.Drawing.Pen $color, 3
        $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45, $color.R, $color.G, $color.B))
        try {
            $xRange = Split-Range ([string](Get-ObjectValue $row "screen_x_range"))
            $yRange = Split-Range ([string](Get-ObjectValue $row "screen_y_range"))
            $hasBounds = $xRange.HasValue -and $yRange.HasValue
            $intersectsFrame = $false
            $clampedLeft = $null
            $clampedTop = $null
            $clampedRight = $null
            $clampedBottom = $null
            $centerX = $null
            $centerY = $null

            if ($hasBounds) {
                $centerX = ($xRange.Min + $xRange.Max) / 2.0
                $centerY = ($yRange.Min + $yRange.Max) / 2.0
                $intersectsFrame = $xRange.Max -ge 0 -and $xRange.Min -lt $frameWidth -and $yRange.Max -ge 0 -and $yRange.Min -lt $frameHeight
                $clampedLeft = Clamp-Double $xRange.Min 0 ($frameWidth - 1)
                $clampedTop = Clamp-Double $yRange.Min 0 ($frameHeight - 1)
                $clampedRight = Clamp-Double $xRange.Max 0 ($frameWidth - 1)
                $clampedBottom = Clamp-Double $yRange.Max 0 ($frameHeight - 1)
            }

            $label = "S$index $primaryReason"
            if (-not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $row "texture_address"))) {
                $label += " " + [string](Get-ObjectValue $row "texture_address")
            }

            if ($hasBounds -and $intersectsFrame -and ($clampedRight -gt $clampedLeft) -and ($clampedBottom -gt $clampedTop)) {
                $rect = New-Object System.Drawing.RectangleF ([float]$clampedLeft), ([float]$clampedTop), ([float]($clampedRight - $clampedLeft)), ([float]($clampedBottom - $clampedTop))
                $graphics.FillRectangle($fill, $rect)
                $graphics.DrawRectangle($pen, $rect.X, $rect.Y, $rect.Width, $rect.Height)
                Draw-TextWithBackplate -Graphics $graphics -Text $label -Font $smallFont -Brush $whiteBrush -X ([float]([Math]::Min($rect.X + 4, $frameWidth - 180))) -Y ([float]([Math]::Min($rect.Y + 4, $frameHeight - 18)))
            } else {
                $markerX = if ($hasBounds) { Clamp-Double $centerX 14 ($frameWidth - 100) } else { 14 }
                $markerY = if ($hasBounds) { Clamp-Double $centerY 14 ($frameHeight - 20) } else { 26 + ($index * 18) }
                switch ($primaryReason) {
                    "offscreen-left" { $markerX = 8 }
                    "offscreen-right" { $markerX = $frameWidth - 118 }
                    "offscreen-above" { $markerY = 8 }
                    "fully-clipped" {
                        $markerX = [Math]::Max(8, [Math]::Min($frameWidth - 150, 20 + ($index * 18)))
                        $markerY = $frameHeight - 24
                    }
                }

                $graphics.DrawLine($pen, [float]$markerX, [float]$markerY, [float]([Math]::Min($markerX + 28, $frameWidth - 2)), [float]$markerY)
                Draw-TextWithBackplate -Graphics $graphics -Text $label -Font $smallFont -Brush $whiteBrush -X ([float]([Math]::Min($markerX + 32, $frameWidth - 180))) -Y ([float]([Math]::Max(0, [Math]::Min($markerY - 8, $frameHeight - 18))))
            }

            $boundsRows.Add([pscustomobject][ordered]@{
                sequence_index = $index
                partition_kind = $partitionKind
                texture_address = Get-ObjectValue $row "texture_address"
                primary_reason = $primaryReason
                draw_start = Get-ObjectValue $row "draw_start"
                draw_end = Get-ObjectValue $row "draw_end"
                draw_count = Get-ObjectValue $row "draw_count"
                screen_min_x = if ($hasBounds) { $xRange.Min } else { "" }
                screen_max_x = if ($hasBounds) { $xRange.Max } else { "" }
                screen_min_y = if ($hasBounds) { $yRange.Min } else { "" }
                screen_max_y = if ($hasBounds) { $yRange.Max } else { "" }
                screen_center_x = if ($hasBounds) { "{0:0.###}" -f $centerX } else { "" }
                screen_center_y = if ($hasBounds) { "{0:0.###}" -f $centerY } else { "" }
                intersects_frame = $intersectsFrame
                clamped_min_x = if ($hasBounds) { "{0:0.###}" -f $clampedLeft } else { "" }
                clamped_max_x = if ($hasBounds) { "{0:0.###}" -f $clampedRight } else { "" }
                clamped_min_y = if ($hasBounds) { "{0:0.###}" -f $clampedTop } else { "" }
                clamped_max_y = if ($hasBounds) { "{0:0.###}" -f $clampedBottom } else { "" }
                view_z_range = Get-ObjectValue $row "view_z_range"
                tex_s_range = Get-ObjectValue $row "tex_s_range"
                tex_t_range = Get-ObjectValue $row "tex_t_range"
                source_records = Get-ObjectValue $row "source_records"
                output_indices = Get-ObjectValue $row "output_indices"
            })
        } finally {
            $pen.Dispose()
            $fill.Dispose()
        }
    }
} finally {
    $graphics.Dispose()
    $font.Dispose()
    $smallFont.Dispose()
    $whiteBrush.Dispose()
}

$overlayPath = Join-Path $OutputDirectory "packet-placement-overlay.png"
$contactSheetPath = Join-Path $OutputDirectory "packet-placement-contact-sheet.png"
$boundsCsvPath = Join-Path $OutputDirectory "packet-placement-bounds.csv"
$jsonPath = Join-Path $OutputDirectory "packet-placement-report.json"

$overlay.Save($overlayPath, [System.Drawing.Imaging.ImageFormat]::Png)
$boundsRows | Export-Csv -LiteralPath $boundsCsvPath -NoTypeInformation -Encoding UTF8

$legendHeight = 170
$contact = New-Object System.Drawing.Bitmap ($frameWidth * 2), ($frameHeight + $legendHeight), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$contactGraphics = [System.Drawing.Graphics]::FromImage($contact)
$titleFont = New-Object System.Drawing.Font "Consolas", 12, ([System.Drawing.FontStyle]::Bold)
$legendFont = New-Object System.Drawing.Font "Consolas", 9
$blackBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)
$whiteBrush2 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
try {
    $contactGraphics.Clear([System.Drawing.Color]::FromArgb(255, 24, 28, 34))
    $contactGraphics.DrawImage($base, 0, 0, $frameWidth, $frameHeight)
    $contactGraphics.DrawImage($overlay, $frameWidth, 0, $frameWidth, $frameHeight)
    $contactGraphics.DrawString("source frame", $titleFont, $whiteBrush2, 8, 8)
    $contactGraphics.DrawString("packet placement overlay", $titleFont, $whiteBrush2, $frameWidth + 8, 8)

    $legendY = $frameHeight + 10
    $contactGraphics.DrawString("Sequence legend", $titleFont, $whiteBrush2, 10, $legendY)
    $legendY += 26
    foreach ($row in $boundsRows) {
        $partitionKind = [string](Get-ObjectValue $row "partition_kind")
        $primaryReason = [string](Get-ObjectValue $row "primary_reason")
        $color = Get-SequenceColor -PartitionKind $partitionKind -PrimaryReason $primaryReason
        $swatch = New-Object System.Drawing.SolidBrush $color
        try {
            $x = 12 + ((([int](Get-ObjectValue $row "sequence_index") - 1) % 2) * $frameWidth)
            $y = $legendY + ([Math]::Floor(([int](Get-ObjectValue $row "sequence_index") - 1) / 2) * 22)
            $contactGraphics.FillRectangle($swatch, $x, $y + 3, 14, 14)
            $text = "S{0}: draws {1}..{2}, {3}, {4}" -f (Get-ObjectValue $row "sequence_index"), (Get-ObjectValue $row "draw_start"), (Get-ObjectValue $row "draw_end"), $primaryReason, (Get-ObjectValue $row "texture_address")
            $contactGraphics.DrawString($text, $legendFont, $whiteBrush2, $x + 20, $y)
        } finally {
            $swatch.Dispose()
        }
    }
} finally {
    $contactGraphics.Dispose()
    $titleFont.Dispose()
    $legendFont.Dispose()
    $blackBrush.Dispose()
    $whiteBrush2.Dispose()
}

$contact.Save($contactSheetPath, [System.Drawing.Imaging.ImageFormat]::Png)

$report = [pscustomobject]([ordered]@{
    run_directory = $runRoot
    frame_path = if ([string]::IsNullOrWhiteSpace($FramePath)) { $null } else { $FramePath }
    sequence_csv_path = $sequenceCsvPath
    overlay_path = $overlayPath
    contact_sheet_path = $contactSheetPath
    bounds_csv_path = $boundsCsvPath
    bounds = [object[]]$boundsRows.ToArray()
})

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$base.Dispose()
$overlay.Dispose()
$contact.Dispose()

Write-Host "Sonic packet placement overlay: $contactSheetPath"
Write-Output $boundsRows
