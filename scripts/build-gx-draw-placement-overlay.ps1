param(
    [Parameter(Mandatory = $true)]
    [string]$VertexCsvPath,
    [Parameter(Mandatory = $true)]
    [int[]]$DrawIndex,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$CandidatePath = "",
    [string]$SamplePath = "",
    [int]$FrameWidth = 640,
    [int]$FrameHeight = 480
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Resolve-FullPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-Double {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [double]::NaN
    }

    return [double]::Parse($Text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Is-Finite {
    param([double]$Value)

    return -not [double]::IsNaN($Value) -and -not [double]::IsInfinity($Value)
}

function New-BaseBitmap {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) {
        $loaded = [System.Drawing.Bitmap]::FromFile($Path)
        try {
            return [System.Drawing.Bitmap]::new($loaded, $FrameWidth, $FrameHeight)
        }
        finally {
            $loaded.Dispose()
        }
    }

    $bitmap = [System.Drawing.Bitmap]::new($FrameWidth, $FrameHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 14, 18, 24))
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Get-DrawColor {
    param([int]$Draw)

    $palette = @(
        [System.Drawing.Color]::FromArgb(92, 190, 255),
        [System.Drawing.Color]::FromArgb(255, 186, 73),
        [System.Drawing.Color]::FromArgb(105, 220, 140),
        [System.Drawing.Color]::FromArgb(255, 110, 150),
        [System.Drawing.Color]::FromArgb(185, 135, 255),
        [System.Drawing.Color]::FromArgb(90, 230, 220)
    )
    return $palette[[Math]::Abs($Draw.GetHashCode()) % $palette.Count]
}

function Draw-Label {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [float]$X,
        [float]$Y,
        [System.Drawing.Brush]$Brush,
        [System.Drawing.Font]$Font
    )

    $back = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(190, 0, 0, 0))
    try {
        $size = $Graphics.MeasureString($Text, $Font)
        $Graphics.FillRectangle($back, $X - 2, $Y - 1, $size.Width + 4, $size.Height + 2)
        $Graphics.DrawString($Text, $Font, $Brush, $X, $Y)
    }
    finally {
        $back.Dispose()
    }
}

function Draw-Overlays {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [object[]]$Vertices
    )

    $graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
    $font = [System.Drawing.Font]::new("Consolas", 8)
    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        foreach ($draw in $DrawIndex) {
            $drawVertices = @($Vertices | Where-Object { $_.Draw -eq $draw } | Sort-Object Index)
            if ($drawVertices.Count -lt 3) {
                continue
            }

            $baseColor = Get-DrawColor $draw
            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(245, $baseColor.R, $baseColor.G, $baseColor.B), 2.0)
            $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(44, $baseColor.R, $baseColor.G, $baseColor.B))
            try {
                $labelPoint = $null
                for ($i = 2; $i -lt $drawVertices.Count; $i++) {
                    $a = $drawVertices[$i - 2]
                    $b = $drawVertices[$i - 1]
                    $c = $drawVertices[$i]
                    if (!(Is-Finite $a.X) -or !(Is-Finite $a.Y) -or !(Is-Finite $b.X) -or !(Is-Finite $b.Y) -or !(Is-Finite $c.X) -or !(Is-Finite $c.Y)) {
                        continue
                    }

                    $points = [System.Drawing.PointF[]]@(
                        [System.Drawing.PointF]::new([float]$a.X, [float]$a.Y),
                        [System.Drawing.PointF]::new([float]$b.X, [float]$b.Y),
                        [System.Drawing.PointF]::new([float]$c.X, [float]$c.Y)
                    )
                    $graphics.FillPolygon($fill, $points)
                    $graphics.DrawPolygon($pen, $points)

                    if ($null -eq $labelPoint) {
                        $cx = [Math]::Max(0, [Math]::Min($FrameWidth - 120, ($a.X + $b.X + $c.X) / 3.0))
                        $cy = [Math]::Max(0, [Math]::Min($FrameHeight - 18, ($a.Y + $b.Y + $c.Y) / 3.0))
                        $labelPoint = [pscustomobject]@{ X = $cx; Y = $cy }
                    }
                }

                if ($null -ne $labelPoint) {
                    Draw-Label -Graphics $graphics -Text "draw $draw" -X ([float]$labelPoint.X) -Y ([float]$labelPoint.Y) -Brush $whiteBrush -Font $font
                }
            }
            finally {
                $pen.Dispose()
                $fill.Dispose()
            }
        }
    }
    finally {
        $whiteBrush.Dispose()
        $font.Dispose()
        $graphics.Dispose()
    }
}

$vertexCsvFullPath = Resolve-FullPath $VertexCsvPath
$candidateFullPath = Resolve-FullPath $CandidatePath
$sampleFullPath = Resolve-FullPath $SamplePath
$outputFullPath = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$drawSet = [System.Collections.Generic.HashSet[int]]::new()
foreach ($draw in $DrawIndex) {
    [void]$drawSet.Add($draw)
}

$vertices = @(Import-Csv -LiteralPath $vertexCsvFullPath | Where-Object { $drawSet.Contains([int]$_.draw_index) -and $_.decoded -eq "True" } | ForEach-Object {
    [pscustomobject]@{
        Draw = [int]$_.draw_index
        Index = [int]$_.vertex_index
        X = Parse-Double $_.screen_x
        Y = Parse-Double $_.screen_y
    }
})

$candidate = New-BaseBitmap $candidateFullPath
$sample = New-BaseBitmap $sampleFullPath
$candidateOverlay = [System.Drawing.Bitmap]::new($candidate)
$sampleOverlay = [System.Drawing.Bitmap]::new($sample)

try {
    Draw-Overlays -Bitmap $candidateOverlay -Vertices $vertices
    Draw-Overlays -Bitmap $sampleOverlay -Vertices $vertices

    $candidateOverlayPath = Join-Path $outputFullPath "candidate-draw-placement-overlay.png"
    $sampleOverlayPath = Join-Path $outputFullPath "sample-draw-placement-overlay.png"
    $contactPath = Join-Path $outputFullPath "draw-placement-contact-sheet.png"
    $candidateOverlay.Save($candidateOverlayPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $sampleOverlay.Save($sampleOverlayPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $titleHeight = 26
    $contact = [System.Drawing.Bitmap]::new($FrameWidth * 2, ($FrameHeight * 2) + ($titleHeight * 2), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($contact)
    $font = [System.Drawing.Font]::new("Consolas", 11, [System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 20, 24, 30))
        $graphics.DrawString("Dolphin sample", $font, $brush, 8, 5)
        $graphics.DrawString("Candidate", $font, $brush, $FrameWidth + 8, 5)
        $graphics.DrawImage($sample, 0, $titleHeight, $FrameWidth, $FrameHeight)
        $graphics.DrawImage($candidate, $FrameWidth, $titleHeight, $FrameWidth, $FrameHeight)
        $y2 = $FrameHeight + $titleHeight
        $graphics.DrawString("Dolphin sample + selected draw placement", $font, $brush, 8, $y2 + 5)
        $graphics.DrawString("Candidate + selected draw placement", $font, $brush, $FrameWidth + 8, $y2 + 5)
        $graphics.DrawImage($sampleOverlay, 0, $y2 + $titleHeight, $FrameWidth, $FrameHeight)
        $graphics.DrawImage($candidateOverlay, $FrameWidth, $y2 + $titleHeight, $FrameWidth, $FrameHeight)
        $contact.Save($contactPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $brush.Dispose()
        $font.Dispose()
        $graphics.Dispose()
        $contact.Dispose()
    }

    $summary = [pscustomobject]@{
        vertex_csv = $vertexCsvFullPath
        draw_indices = $DrawIndex
        vertices = $vertices.Count
        candidate = $candidateFullPath
        sample = $sampleFullPath
        candidate_overlay = $candidateOverlayPath
        sample_overlay = $sampleOverlayPath
        contact_sheet = $contactPath
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $outputFullPath "draw-placement-summary.json") -Encoding UTF8
    $summary | Format-List
}
finally {
    $candidate.Dispose()
    $sample.Dispose()
    $candidateOverlay.Dispose()
    $sampleOverlay.Dispose()
}
