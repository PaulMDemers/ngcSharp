param(
    [string]$DolphinDirectory = "dolphin-2603a-x64/Dolphin-x64",
    [string]$OutputDirectory = "artifacts/dolphin-reference",
    [int]$Seconds = 18,
    [int[]]$SampleFrames = @(1, 60, 180, 300, 600, 900),
    [switch]$KeepFullDump
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Quote-ProcessArgument {
    param([string]$Argument)

    if ($Argument.Length -eq 0 -or $Argument.IndexOfAny([char[]]" `t`"") -ge 0) {
        return '"' + ($Argument -replace '\\', '\\' -replace '"', '\"') + '"'
    }

    return $Argument
}

function Write-DolphinConfig {
    param([string]$UserDirectory)

    $configDirectory = Join-Path $UserDirectory "Config"
    New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null

    @'
[Movie]
DumpFrames = True
DumpFramesSilent = True

[Core]
CPUThread = False
GFXBackend = D3D
EmulationSpeed = 1.0
SkipIPL = True

[DSP]
Backend = No audio output

[Display]
RenderWindowWidth = 640
RenderWindowHeight = 480
RenderWindowAutoSize = False
'@ | Set-Content -Encoding ASCII (Join-Path $configDirectory "Dolphin.ini")

    @'
[Settings]
DumpFramesAsImages = True
PNGCompressionLevel = 1
FrameDumpsResolutionType = 2
InternalResolution = 1
ShowFPS = False
OverlayStats = False

[Enhancements]
ForceTrueColor = True
DisableCopyFilter = True
'@ | Set-Content -Encoding ASCII (Join-Path $configDirectory "GFX.ini")
}

function Get-FrameNumber {
    param([System.IO.FileInfo]$File)

    if ($File.BaseName -match '^framedump_(\d+)$') {
        return [int]$Matches[1]
    }

    return -1
}

function Copy-ReferenceSamples {
    param(
        [string]$FrameDirectory,
        [string]$SampleDirectory,
        [int[]]$RequestedFrames
    )

    New-Item -ItemType Directory -Force -Path $SampleDirectory | Out-Null
    $allFrames = @(
        Get-ChildItem -LiteralPath $FrameDirectory -Filter "framedump_*.png" -File |
            Sort-Object { Get-FrameNumber $_ }
    )

    $frames = if ($allFrames.Count -gt 6) {
        @($allFrames | Select-Object -First ($allFrames.Count - 5))
    } else {
        $allFrames
    }

    if ($frames.Count -eq 0) {
        return @()
    }

    $copied = New-Object System.Collections.Generic.List[object]
    foreach ($requested in $RequestedFrames) {
        $match = $frames | Where-Object { (Get-FrameNumber $_) -ge $requested } | Select-Object -First 1
        if ($null -eq $match) {
            $match = $frames[$frames.Count - 1]
        }

        $actual = Get-FrameNumber $match
        $targetName = "frame-{0:D5}-requested-{1:D5}.png" -f $actual, $requested
        $targetPath = Join-Path $SampleDirectory $targetName
        Copy-Item -LiteralPath $match.FullName -Destination $targetPath -Force
        $copied.Add([pscustomobject]@{
            requestedFrame = $requested
            actualFrame = $actual
            path = $targetPath
            bytes = (Get-Item -LiteralPath $targetPath).Length
        })
    }

    return $copied
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

function Write-ContactSheet {
    param(
        [string]$SampleDirectory,
        [string]$OutPath,
        [string]$Title
    )

    $samples = @(Get-ChildItem -LiteralPath $SampleDirectory -Filter "*.png" -File | Sort-Object Name)
    if ($samples.Count -eq 0) {
        return
    }

    Add-Type -AssemblyName System.Drawing

    $columns = 3
    $thumbWidth = 240
    $thumbHeight = 180
    $labelHeight = 28
    $padding = 12
    $headerHeight = 48
    $cellWidth = $thumbWidth + $padding * 2
    $cellHeight = $thumbHeight + $labelHeight + $padding * 2
    $rows = [int][Math]::Ceiling($samples.Count / $columns)
    $width = $columns * $cellWidth
    $height = $headerHeight + $rows * $cellHeight

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $fonts = @()
    $brushes = @()
    $pens = @()

    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(18, 20, 24))
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        $titleFont = New-Object System.Drawing.Font "Segoe UI", 14, ([System.Drawing.FontStyle]::Bold)
        $labelFont = New-Object System.Drawing.Font "Segoe UI", 9
        $fonts += $titleFont, $labelFont

        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(238, 241, 245))
        $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(170, 177, 188))
        $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)
        $brushes += $white, $muted, $black

        $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(68, 74, 84)), 1
        $pens += $border

        $graphics.DrawString($Title, $titleFont, $white, 12, 10)
        for ($i = 0; $i -lt $samples.Count; $i++) {
            $column = $i % $columns
            $row = [int][Math]::Floor($i / $columns)
            $x = $column * $cellWidth + $padding
            $y = $headerHeight + $row * $cellHeight + $padding
            $imageRect = New-Object System.Drawing.Rectangle $x, $y, $thumbWidth, $thumbHeight

            $graphics.FillRectangle($black, $imageRect)
            $graphics.DrawRectangle($border, $imageRect)

            try {
                $image = New-LoadedBitmap $samples[$i].FullName
            } catch {
                $graphics.DrawString("unreadable sample", $labelFont, $muted, $x + 8, $y + 8)
                $graphics.DrawString($samples[$i].BaseName, $labelFont, $muted, $x, $y + $thumbHeight + 7)
                continue
            }

            try {
                $scale = [Math]::Min($thumbWidth / $image.Width, $thumbHeight / $image.Height)
                $drawWidth = [int][Math]::Round($image.Width * $scale)
                $drawHeight = [int][Math]::Round($image.Height * $scale)
                $drawX = $x + [int][Math]::Floor(($thumbWidth - $drawWidth) / 2)
                $drawY = $y + [int][Math]::Floor(($thumbHeight - $drawHeight) / 2)
                $graphics.DrawImage($image, $drawX, $drawY, $drawWidth, $drawHeight)
            } finally {
                $image.Dispose()
            }

            $graphics.DrawString($samples[$i].BaseName, $labelFont, $muted, $x, $y + $thumbHeight + 7)
        }

        $bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        foreach ($pen in $pens) { $pen.Dispose() }
        foreach ($brush in $brushes) { $brush.Dispose() }
        foreach ($font in $fonts) { $font.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$repoRoot = Resolve-FullPath "."
$dolphinRoot = Resolve-FullPath $DolphinDirectory
$dolphinExe = Join-Path $dolphinRoot "Dolphin.exe"
$dolphinTool = Join-Path $dolphinRoot "DolphinTool.exe"
if (-not (Test-Path -LiteralPath $dolphinExe)) {
    throw "Dolphin.exe not found: $dolphinExe"
}

$games = @(
    [pscustomobject]@{
        slug = "sonic-adventure-2-battle"
        path = Join-Path $repoRoot "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"
    },
    [pscustomobject]@{
        slug = "pikmin"
        path = Join-Path $repoRoot "Pikmin (USA).rvz"
    }
)

$missing = @($games | Where-Object { -not (Test-Path -LiteralPath $_.path) })
if ($missing.Count -gt 0) {
    $names = ($missing | ForEach-Object { $_.path }) -join ", "
    throw "Missing benchmark game(s): $names"
}

$outRoot = Resolve-FullPath $OutputDirectory
$runName = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $outRoot $runName
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$summaryRows = New-Object System.Collections.Generic.List[object]

foreach ($game in $games) {
    Write-Host "Dumping Dolphin reference for $($game.slug)..."
    $gameRoot = Join-Path $runRoot $game.slug
    $userDir = Join-Path $gameRoot "user"
    $sampleDir = Join-Path $gameRoot "samples"
    New-Item -ItemType Directory -Force -Path $gameRoot | Out-Null
    Write-DolphinConfig -UserDirectory $userDir

    $headerText = ""
    if (Test-Path -LiteralPath $dolphinTool) {
        $headerText = (& $dolphinTool header -i $game.path) -join "`n"
        $headerText | Set-Content -Encoding ASCII (Join-Path $gameRoot "dolphin-header.txt")
    }

    $stdoutPath = Join-Path $gameRoot "stdout.txt"
    $stderrPath = Join-Path $gameRoot "stderr.txt"
    $arguments = @("-u", $userDir, "-b", "-e", $game.path)
    $processArguments = ($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join " "
    $process = Start-Process `
        -FilePath $dolphinExe `
        -ArgumentList $processArguments `
        -WorkingDirectory $dolphinRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds $Seconds
    $wasRunning = -not $process.HasExited
    if ($wasRunning) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    }

    $frameDir = Join-Path $userDir "Dump/Frames"
    $frameCount = if (Test-Path -LiteralPath $frameDir) {
        @(Get-ChildItem -LiteralPath $frameDir -Filter "framedump_*.png" -File).Count
    } else {
        0
    }

    $samples = @()
    if ($frameCount -gt 0) {
        $samples = @(Copy-ReferenceSamples -FrameDirectory $frameDir -SampleDirectory $sampleDir -RequestedFrames $SampleFrames)
        Write-ContactSheet -SampleDirectory $sampleDir -OutPath (Join-Path $gameRoot "contact-sheet.png") -Title "Dolphin reference: $($game.slug)"
    }

    $sampleManifest = Join-Path $gameRoot "samples.csv"
    $samples | Export-Csv -LiteralPath $sampleManifest -NoTypeInformation

    $summaryRows.Add([pscustomobject]@{
        slug = $game.slug
        gamePath = $game.path
        seconds = $Seconds
        frameCount = $frameCount
        samples = $samples.Count
        userDirectory = $userDir
        sampleDirectory = $sampleDir
        contactSheet = Join-Path $gameRoot "contact-sheet.png"
        processStopped = $wasRunning
    })

    if (-not $KeepFullDump -and (Test-Path -LiteralPath $frameDir)) {
        Remove-Item -LiteralPath $frameDir -Recurse -Force
    }
}

$summaryPath = Join-Path $runRoot "summary.csv"
$summaryRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
Write-Host "Dolphin reference summary: $summaryPath"
