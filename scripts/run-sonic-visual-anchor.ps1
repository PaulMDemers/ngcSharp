param(
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [string]$DolphinReferenceRoot = "",
    [string]$SampleDirectory = "",
    [string]$CandidatePath = "",
    [string]$OutputDirectory = "artifacts/sonic-visual-anchor",
    [int]$MaxInstructions = 50000000,
    [int]$TimeoutSeconds = 1200,
    [string[]]$ExtraRunArgs = @(),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [double]$VisibleBaselineMinPercent = 1.0,
    [int]$VisibleSampleMinFrame = 0,
    [int]$VisibleSampleMaxFrame = 2147483647,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Find-LatestDolphinReferenceRoot {
    $root = Resolve-FullPath "artifacts/dolphin-reference"
    $latest = Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "summary.csv") } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No Dolphin reference summary found under $root. Run scripts/generate-dolphin-reference.ps1 first."
    }

    return $latest.FullName
}

function Quote-ProcessArgument {
    param([string]$Argument)

    if ($Argument.Length -eq 0 -or $Argument.IndexOfAny([char[]]" `t`"") -ge 0) {
        return '"' + ($Argument -replace '\\', '\\' -replace '"', '\"') + '"'
    }

    return $Argument
}

function Invoke-DotnetApp {
    param(
        [string]$AppDll,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath,
        [int]$Timeout
    )

    $processArguments = @($AppDll) + $Arguments
    $argumentLine = ($processArguments | ForEach-Object { Quote-ProcessArgument $_ }) -join " "
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $argumentLine `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $StdoutPath `
        -RedirectStandardError $StderrPath `
        -WindowStyle Hidden `
        -PassThru

    if (-not $process.WaitForExit($Timeout * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
        return [pscustomobject]@{
            status = "timeout"
            exitCode = ""
        }
    }

    Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    $process.Refresh()
    $exitCode = $process.ExitCode
    return [pscustomobject]@{
        status = if ($null -eq $exitCode) { "exit-unknown" } elseif ($exitCode -eq 0) { "ok" } else { "exit-$exitCode" }
        exitCode = $exitCode
    }
}

function Get-ReportLine {
    param(
        [string]$Path,
        [string]$Pattern
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $line = Select-String -LiteralPath $Path -Pattern $Pattern | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Line.Trim()
}

function Get-ChangedPercent {
    param([string]$ChangedLine)

    if ($ChangedLine -match '\(([0-9.]+)%\)') {
        return [double]::Parse($Matches[1], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [double]::PositiveInfinity
}

function Get-AverageDelta {
    param([string]$DeltaLine)

    if ($DeltaLine -match 'avg ([0-9.]+)') {
        return [double]::Parse($Matches[1], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [double]::PositiveInfinity
}

function Get-NonblackPercent {
    param([string]$NonblackLine)

    if ($NonblackLine -match '\(([0-9.]+)%\)') {
        return [double]::Parse($Matches[1], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return 0.0
}

function Get-SampleFrameNumber {
    param([string]$SampleName)

    if ($SampleName -match '^frame-([0-9]+)-') {
        return [int]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return -1
}

function Save-ImageCrop {
    param(
        [string]$InputPath,
        [string]$OutputPath,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )

    $image = [System.Drawing.Bitmap]::FromFile($InputPath)
    try {
        $rect = [System.Drawing.Rectangle]::FromLTRB(
            [Math]::Max(0, $X),
            [Math]::Max(0, $Y),
            [Math]::Min($image.Width, $X + $Width),
            [Math]::Min($image.Height, $Y + $Height))

        if ($rect.Width -le 0 -or $rect.Height -le 0) {
            throw "Crop rectangle $X,$Y,$Width,$Height does not overlap ${InputPath}."
        }

        $crop = $image.Clone($rect, $image.PixelFormat)
        try {
            $parent = Split-Path -Parent $OutputPath
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            $crop.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $crop.Dispose()
        }
    } finally {
        $image.Dispose()
    }
}

function Draw-FitImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$ImagePath,
        [System.Drawing.Rectangle]$Bounds
    )

    $image = [System.Drawing.Bitmap]::FromFile($ImagePath)
    try {
        $scale = [Math]::Min($Bounds.Width / $image.Width, $Bounds.Height / $image.Height)
        $drawWidth = [int][Math]::Round($image.Width * $scale)
        $drawHeight = [int][Math]::Round($image.Height * $scale)
        $x = $Bounds.X + [int][Math]::Floor(($Bounds.Width - $drawWidth) / 2)
        $y = $Bounds.Y + [int][Math]::Floor(($Bounds.Height - $drawHeight) / 2)
        $Graphics.DrawImage($image, $x, $y, $drawWidth, $drawHeight)
    } finally {
        $image.Dispose()
    }
}

function Write-ContactSheet {
    param(
        [object[]]$Rows,
        [string]$OutputPath
    )

    $cellWidth = 320
    $cellHeight = 240
    $labelHeight = 46
    $leftLabelWidth = 150
    $padding = 12
    $columns = @("candidate", "best dolphin sample", "diff")
    $width = $leftLabelWidth + ($cellWidth * $columns.Count) + ($padding * ($columns.Count + 2))
    $height = $labelHeight + (($cellHeight + $labelHeight + $padding) * $Rows.Count) + $padding

    $sheet = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($sheet)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(24, 28, 34))
            $font = [System.Drawing.Font]::new("Segoe UI", 11)
            $boldFont = [System.Drawing.Font]::new("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)
            $smallFont = [System.Drawing.Font]::new("Segoe UI", 9)
            $brush = [System.Drawing.Brushes]::White
            $mutedBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(190, 198, 208))
            $panelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 41, 48))

            try {
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    $x = $leftLabelWidth + $padding + ($i * ($cellWidth + $padding))
                    $graphics.DrawString($columns[$i], $boldFont, $brush, $x, 14)
                }

                $y = $labelHeight
                foreach ($row in $Rows) {
                    $graphics.DrawString($row.region, $boldFont, $brush, $padding, $y + 8)
                    $graphics.DrawString($row.sample, $smallFont, $mutedBrush, $padding, $y + 30)
                    $graphics.DrawString(("changed {0:N3}%" -f $row.changedPercent), $smallFont, $mutedBrush, $padding, $y + 48)

                    $paths = @($row.candidatePath, $row.samplePath, $row.diffPath)
                    for ($i = 0; $i -lt $paths.Count; $i++) {
                        $x = $leftLabelWidth + $padding + ($i * ($cellWidth + $padding))
                        $bounds = [System.Drawing.Rectangle]::new($x, $y + $labelHeight, $cellWidth, $cellHeight)
                        $graphics.FillRectangle($panelBrush, $bounds)
                        if (-not [string]::IsNullOrWhiteSpace($paths[$i]) -and (Test-Path -LiteralPath $paths[$i])) {
                            Draw-FitImage -Graphics $graphics -ImagePath $paths[$i] -Bounds $bounds
                        }
                    }

                    $y += $cellHeight + $labelHeight + $padding
                }
            } finally {
                $font.Dispose()
                $boldFont.Dispose()
                $smallFont.Dispose()
                $mutedBrush.Dispose()
                $panelBrush.Dispose()
            }
        } finally {
            $graphics.Dispose()
        }

        $parent = Split-Path -Parent $OutputPath
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        $sheet.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $sheet.Dispose()
    }
}

$repoRoot = Resolve-FullPath "."
$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($DolphinReferenceRoot)) {
    $DolphinReferenceRoot = Find-LatestDolphinReferenceRoot
} else {
    $DolphinReferenceRoot = Resolve-FullPath $DolphinReferenceRoot
}

if ([string]::IsNullOrWhiteSpace($SampleDirectory)) {
    $SampleDirectory = Join-Path $DolphinReferenceRoot "sonic-adventure-2-battle/samples"
} else {
    $SampleDirectory = Resolve-FullPath $SampleDirectory
}

if (-not (Test-Path -LiteralPath $SampleDirectory)) {
    throw "Sonic Dolphin sample directory not found: $SampleDirectory"
}

$samples = @(Get-ChildItem -LiteralPath $SampleDirectory -Filter "*.png" -File | Sort-Object Name)
if ($samples.Count -eq 0) {
    throw "No Dolphin sample PNGs found under $SampleDirectory"
}

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/$Configuration/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    dotnet build $appProject --configuration $Configuration --no-restore | Out-Host
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "NgcSharp app DLL not found: $appDll"
}

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$candidate = Join-Path $runRoot "candidate.png"
$runStatus = "reused"
$runExitCode = ""
if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
    $sonicFullPath = Resolve-FullPath $SonicPath
    if (-not (Test-Path -LiteralPath $sonicFullPath)) {
        throw "Missing Sonic benchmark image: $sonicFullPath"
    }

    $stdoutPath = Join-Path $runRoot "run-stdout.txt"
    $stderrPath = Join-Path $runRoot "run-stderr.txt"
    $runArgs = @(
        "run-disc",
        $sonicFullPath,
        "--max-instructions", "$MaxInstructions",
        "--fast-forward-idle",
        "--fast-forward-write-watch",
        "--memory-card-a",
        "--controller-button", "a",
        "--dump-gx-frame", $candidate,
        "--gx-frame-source", "largest-display-copy",
        "--gx-frame-skip-draws", "8302",
        "--gx-frame-max-draws", "3900",
        "--gx-frame-max-raster-pixels", "50000000",
        "--run-summary", (Join-Path $runRoot "emulator-summary.json"),
        "--quiet",
        "--no-registers"
    ) + $ExtraRunArgs

    Write-Host "Running Sonic visual anchor capture..."
    $runResult = Invoke-DotnetApp `
        -AppDll $appDll `
        -Arguments $runArgs `
        -WorkingDirectory $repoRoot `
        -StdoutPath $stdoutPath `
        -StderrPath $stderrPath `
        -Timeout $TimeoutSeconds
    $runStatus = $runResult.status
    $runExitCode = $runResult.exitCode
} else {
    $sourceCandidate = Resolve-FullPath $CandidatePath
    if (-not (Test-Path -LiteralPath $sourceCandidate)) {
        throw "Candidate image not found: $sourceCandidate"
    }

    Copy-Item -LiteralPath $sourceCandidate -Destination $candidate -Force
}

if (-not (Test-Path -LiteralPath $candidate)) {
    throw "Candidate capture was not written. Run status: $runStatus"
}

$regions = @(
    [pscustomobject]@{ name = "full"; x = 0; y = 0; width = 640; height = 480 },
    [pscustomobject]@{ name = "skyline"; x = 48; y = 72; width = 544; height = 170 },
    [pscustomobject]@{ name = "bridge"; x = 116; y = 156; width = 420; height = 210 },
    [pscustomobject]@{ name = "lower-track"; x = 92; y = 264; width = 456; height = 178 }
)

$fullRows = New-Object System.Collections.Generic.List[object]
$cropRows = New-Object System.Collections.Generic.List[object]

foreach ($sample in $samples) {
    $sampleSlug = $sample.BaseName
    $comparisonDir = Join-Path $runRoot ("full/" + $sampleSlug)
    New-Item -ItemType Directory -Force -Path $comparisonDir | Out-Null
    $diffPath = Join-Path $comparisonDir "diff.png"
    $reportPath = Join-Path $comparisonDir "compare.txt"
    $stderrPath = Join-Path $comparisonDir "compare-stderr.txt"

    $status = (Invoke-DotnetApp `
        -AppDll $appDll `
        -Arguments @("compare-images", $sample.FullName, $candidate, "--diff", $diffPath) `
        -WorkingDirectory $repoRoot `
        -StdoutPath $reportPath `
        -StderrPath $stderrPath `
        -Timeout 60).status

    $changed = Get-ReportLine -Path $reportPath -Pattern "^Changed:"
    $delta = Get-ReportLine -Path $reportPath -Pattern "^Delta:"
    $baselineNonblack = Get-ReportLine -Path $reportPath -Pattern "^Baseline nonblack:"
    $candidateNonblack = Get-ReportLine -Path $reportPath -Pattern "^Candidate nonblack:"
    $sampleFrame = Get-SampleFrameNumber $sampleSlug
    $fullRows.Add([pscustomobject]@{
        region = "full"
        sample = $sampleSlug
        sampleFrame = $sampleFrame
        status = $status
        changedPercent = Get-ChangedPercent $changed
        averageDelta = Get-AverageDelta $delta
        changed = $changed
        delta = $delta
        baselineNonblack = $baselineNonblack
        candidateNonblack = $candidateNonblack
        baselineNonblackPercent = Get-NonblackPercent $baselineNonblack
        candidateNonblackPercent = Get-NonblackPercent $candidateNonblack
        samplePath = $sample.FullName
        candidatePath = $candidate
        diffPath = $diffPath
        reportPath = $reportPath
    }) | Out-Null
}

foreach ($region in @($regions | Where-Object { $_.name -ne "full" })) {
    $candidateCrop = Join-Path $runRoot ("crops/{0}/candidate.png" -f $region.name)
    Save-ImageCrop -InputPath $candidate -OutputPath $candidateCrop -X $region.x -Y $region.y -Width $region.width -Height $region.height

    foreach ($sample in $samples) {
        $sampleSlug = $sample.BaseName
        $comparisonDir = Join-Path $runRoot ("crops/{0}/{1}" -f $region.name, $sampleSlug)
        New-Item -ItemType Directory -Force -Path $comparisonDir | Out-Null
        $sampleCrop = Join-Path $comparisonDir "sample.png"
        $diffPath = Join-Path $comparisonDir "diff.png"
        $reportPath = Join-Path $comparisonDir "compare.txt"
        $stderrPath = Join-Path $comparisonDir "compare-stderr.txt"
        Save-ImageCrop -InputPath $sample.FullName -OutputPath $sampleCrop -X $region.x -Y $region.y -Width $region.width -Height $region.height

        $status = (Invoke-DotnetApp `
            -AppDll $appDll `
            -Arguments @("compare-images", $sampleCrop, $candidateCrop, "--diff", $diffPath) `
            -WorkingDirectory $repoRoot `
            -StdoutPath $reportPath `
            -StderrPath $stderrPath `
            -Timeout 60).status

        $changed = Get-ReportLine -Path $reportPath -Pattern "^Changed:"
        $delta = Get-ReportLine -Path $reportPath -Pattern "^Delta:"
        $baselineNonblack = Get-ReportLine -Path $reportPath -Pattern "^Baseline nonblack:"
        $candidateNonblack = Get-ReportLine -Path $reportPath -Pattern "^Candidate nonblack:"
        $sampleFrame = Get-SampleFrameNumber $sampleSlug
        $cropRows.Add([pscustomobject]@{
            region = $region.name
            sample = $sampleSlug
            sampleFrame = $sampleFrame
            status = $status
            changedPercent = Get-ChangedPercent $changed
            averageDelta = Get-AverageDelta $delta
            changed = $changed
            delta = $delta
            baselineNonblack = $baselineNonblack
            candidateNonblack = $candidateNonblack
            baselineNonblackPercent = Get-NonblackPercent $baselineNonblack
            candidateNonblackPercent = Get-NonblackPercent $candidateNonblack
            samplePath = $sampleCrop
            candidatePath = $candidateCrop
            diffPath = $diffPath
            reportPath = $reportPath
        }) | Out-Null
    }
}

$orderedFullRows = @($fullRows | Sort-Object changedPercent, averageDelta, sample)
$orderedCropRows = @($cropRows | Sort-Object region, changedPercent, averageDelta, sample)
$allRows = @($orderedFullRows) + @($orderedCropRows)
$bestRows = New-Object System.Collections.Generic.List[object]
$visibleBestRows = New-Object System.Collections.Generic.List[object]
foreach ($region in $regions) {
    $best = $allRows |
        Where-Object { $_.region -eq $region.name } |
        Sort-Object changedPercent, averageDelta, sample |
        Select-Object -First 1
    if ($null -ne $best) {
        $bestRows.Add($best) | Out-Null
    }

    $visibleBest = $allRows |
        Where-Object {
            $_.region -eq $region.name `
                -and $_.baselineNonblackPercent -ge $VisibleBaselineMinPercent `
                -and $_.sampleFrame -ge $VisibleSampleMinFrame `
                -and $_.sampleFrame -le $VisibleSampleMaxFrame
        } |
        Sort-Object changedPercent, averageDelta, sample |
        Select-Object -First 1
    if ($null -ne $visibleBest) {
        $visibleBestRows.Add($visibleBest) | Out-Null
    }
}

$runInfo = [pscustomobject]@{
    runStatus = $runStatus
    runExitCode = $runExitCode
    candidatePath = $candidate
    dolphinReferenceRoot = $DolphinReferenceRoot
    sampleDirectory = $SampleDirectory
    maxInstructions = $MaxInstructions
    gxFrameSource = "largest-display-copy"
    gxFrameSkipDraws = 8302
    gxFrameMaxDraws = 3900
    gxFrameMaxRasterPixels = 50000000
    visibleBaselineMinPercent = $VisibleBaselineMinPercent
    visibleSampleMinFrame = $VisibleSampleMinFrame
    visibleSampleMaxFrame = $VisibleSampleMaxFrame
    regions = $regions
}

$orderedFullRows | Export-Csv -LiteralPath (Join-Path $runRoot "full-summary.csv") -NoTypeInformation
$orderedCropRows | Export-Csv -LiteralPath (Join-Path $runRoot "crop-summary.csv") -NoTypeInformation
$bestRows | Export-Csv -LiteralPath (Join-Path $runRoot "best-summary.csv") -NoTypeInformation
$visibleBestRows | Export-Csv -LiteralPath (Join-Path $runRoot "visible-best-summary.csv") -NoTypeInformation
$runInfo | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "run.json")
Write-ContactSheet -Rows $bestRows.ToArray() -OutputPath (Join-Path $runRoot "contact-sheet.png")
if ($visibleBestRows.Count -gt 0) {
    Write-ContactSheet -Rows $visibleBestRows.ToArray() -OutputPath (Join-Path $runRoot "visible-contact-sheet.png")
}

Write-Host "Sonic visual anchor summary: $(Join-Path $runRoot "best-summary.csv")"
$bestRows | Select-Object region,sample,changedPercent,averageDelta,changed,delta | Format-Table -AutoSize
if ($visibleBestRows.Count -gt 0) {
    Write-Host "Sonic visible-anchor summary: $(Join-Path $runRoot "visible-best-summary.csv")"
    $visibleBestRows | Select-Object region,sample,sampleFrame,baselineNonblackPercent,changedPercent,averageDelta,changed,delta | Format-Table -AutoSize
}
