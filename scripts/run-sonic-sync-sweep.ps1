param(
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [string]$DolphinReferenceRoot = "",
    [string]$SampleDirectory = "",
    [string]$CandidateDirectory = "",
    [string]$OutputDirectory = "artifacts/sonic-sync-sweep",
    [int]$MaxInstructions = 50000000,
    [int]$TimeoutSeconds = 1500,
    [int]$SweepStartSkipDraws = 11600,
    [int]$SweepStepDraws = 120,
    [int]$SweepCount = 36,
    [int]$SweepMaxDraws = 3900,
    [int]$SweepMaxRasterPixels = 50000000,
    [int]$SampleMinFrame = 600,
    [int]$SampleMaxFrame = 1800,
    [double]$SampleMinNonblackPercent = 1.0,
    [double]$CandidateMinNonblackPercent = 1.0,
    [string]$ViTimelineDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
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

function Get-Percent {
    param([string]$Line)

    if ($Line -match '\(([0-9.]+)%\)') {
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

function Get-CandidateSkipDraws {
    param([string]$CandidateName)

    if ($CandidateName -match 'skip-([0-9]+)') {
        return [int]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return -1
}

function Read-CandidateSweepMetadata {
    param([string]$CandidateRoot)

    $metadata = @{}
    $summaryPath = Join-Path $CandidateRoot "gx-frame-sweep.csv"
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        return $metadata
    }

    Import-Csv -LiteralPath $summaryPath | ForEach-Object {
        $path = $_.path
        if ([string]::IsNullOrWhiteSpace($path)) {
            return
        }

        $key = [System.IO.Path]::GetFileNameWithoutExtension($path)
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $metadata[$key] = $_
        }
    }

    return $metadata
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

function Write-PairContactSheet {
    param(
        [object[]]$Rows,
        [string]$OutputPath,
        [string]$Title
    )

    if ($Rows.Count -eq 0) {
        return
    }

    Add-Type -AssemblyName System.Drawing

    $cellWidth = 320
    $cellHeight = 240
    $labelHeight = 78
    $leftLabelWidth = 190
    $padding = 12
    $headerHeight = 46
    $columns = @("candidate", "dolphin", "diff")
    $width = $leftLabelWidth + ($cellWidth * $columns.Count) + ($padding * ($columns.Count + 2))
    $height = $headerHeight + (($cellHeight + $labelHeight + $padding) * $Rows.Count) + $padding

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
                $graphics.DrawString($Title, $boldFont, $brush, $padding, 12)
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    $x = $leftLabelWidth + $padding + ($i * ($cellWidth + $padding))
                    $graphics.DrawString($columns[$i], $boldFont, $brush, $x, 14)
                }

                $y = $headerHeight
                foreach ($row in $Rows) {
                    $graphics.DrawString($row.candidate, $boldFont, $brush, $padding, $y + 8)
                    $graphics.DrawString(("Dolphin {0}" -f $row.sampleFrame), $smallFont, $mutedBrush, $padding, $y + 30)
                    $graphics.DrawString(("changed {0:N3}%" -f $row.changedPercent), $smallFont, $mutedBrush, $padding, $y + 48)
                    if (-not [string]::IsNullOrWhiteSpace($row.candidateSelectedCopyIndex)) {
                        $copyLine = "copy {0} draw {1}" -f $row.candidateSelectedCopyIndex, $row.candidateSelectedCopyDrawsSeen
                        $graphics.DrawString($copyLine, $smallFont, $mutedBrush, $padding, $y + 66)
                    }

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

$candidateRoot = ""
$runStatus = "reused"
$runExitCode = ""
if ([string]::IsNullOrWhiteSpace($CandidateDirectory)) {
    $sonicFullPath = Resolve-FullPath $SonicPath
    if (-not (Test-Path -LiteralPath $sonicFullPath)) {
        throw "Missing Sonic benchmark image: $sonicFullPath"
    }

    $candidateRoot = Join-Path $runRoot "candidate-sweep"
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
        "--dump-gx-frame-sweep", $candidateRoot, "$SweepStartSkipDraws", "$SweepStepDraws", "$SweepCount",
        "--gx-frame-source", "largest-display-copy",
        "--gx-frame-max-draws", "$SweepMaxDraws",
        "--gx-frame-max-raster-pixels", "$SweepMaxRasterPixels",
        "--run-summary", (Join-Path $runRoot "emulator-summary.json"),
        "--quiet",
        "--no-registers"
    )

    Write-Host "Running Sonic candidate GX frame sweep..."
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
    $candidateRoot = Resolve-FullPath $CandidateDirectory
}

if (-not (Test-Path -LiteralPath $candidateRoot)) {
    throw "Candidate sweep directory not found: $candidateRoot"
}

$samples = @(
    Get-ChildItem -LiteralPath $SampleDirectory -Filter "*.png" -File |
        Sort-Object Name |
        Where-Object {
            $frame = Get-SampleFrameNumber $_.BaseName
            $frame -ge $SampleMinFrame -and $frame -le $SampleMaxFrame
        }
)
if ($samples.Count -eq 0) {
    throw "No Dolphin PNG samples found under $SampleDirectory in frame range $SampleMinFrame..$SampleMaxFrame."
}

$candidates = @(
    Get-ChildItem -LiteralPath $candidateRoot -Filter "*.png" -File |
        Sort-Object { Get-CandidateSkipDraws $_.BaseName }, Name
)
if ($candidates.Count -eq 0) {
    throw "No candidate PNGs found under $candidateRoot."
}
$candidateMetadata = Read-CandidateSweepMetadata -CandidateRoot $candidateRoot

$comparisonRoot = Join-Path $runRoot "comparisons"
$rows = New-Object System.Collections.Generic.List[object]
foreach ($candidate in $candidates) {
    $candidateSkipDraws = Get-CandidateSkipDraws $candidate.BaseName
    $metadata = $candidateMetadata[$candidate.BaseName]
    foreach ($sample in $samples) {
        $sampleFrame = Get-SampleFrameNumber $sample.BaseName
        $comparisonDir = Join-Path $comparisonRoot ("{0}__{1}" -f $candidate.BaseName, $sample.BaseName)
        New-Item -ItemType Directory -Force -Path $comparisonDir | Out-Null
        $diffPath = Join-Path $comparisonDir "diff.png"
        $reportPath = Join-Path $comparisonDir "compare.txt"
        $stderrPath = Join-Path $comparisonDir "compare-stderr.txt"

        $status = (Invoke-DotnetApp `
            -AppDll $appDll `
            -Arguments @("compare-images", $sample.FullName, $candidate.FullName, "--diff", $diffPath) `
            -WorkingDirectory $repoRoot `
            -StdoutPath $reportPath `
            -StderrPath $stderrPath `
            -Timeout 60).status

        $changed = Get-ReportLine -Path $reportPath -Pattern "^Changed:"
        $delta = Get-ReportLine -Path $reportPath -Pattern "^Delta:"
        $baselineNonblack = Get-ReportLine -Path $reportPath -Pattern "^Baseline nonblack:"
        $candidateNonblack = Get-ReportLine -Path $reportPath -Pattern "^Candidate nonblack:"
        $sampleNonblackPercent = Get-NonblackPercent $baselineNonblack
        $candidateNonblackPercent = Get-NonblackPercent $candidateNonblack
        if ($sampleNonblackPercent -lt $SampleMinNonblackPercent -or $candidateNonblackPercent -lt $CandidateMinNonblackPercent) {
            continue
        }

        $rows.Add([pscustomobject]@{
            candidate = $candidate.BaseName
            candidateSkipDraws = $candidateSkipDraws
            candidateSourceCopyIndex = if ($null -ne $metadata) { $metadata.source_copy_index } else { "" }
            candidateSelectedCopyIndex = if ($null -ne $metadata) { $metadata.selected_copy_index } else { "" }
            candidateSelectedCopyKind = if ($null -ne $metadata) { $metadata.selected_copy_kind } else { "" }
            candidateSelectedCopyDrawsSeen = if ($null -ne $metadata) { $metadata.selected_copy_draws_seen } else { "" }
            candidateSelectedCopyFifoOffset = if ($null -ne $metadata) { $metadata.selected_copy_fifo_offset } else { "" }
            candidateSelectedCopyAddress = if ($null -ne $metadata) { $metadata.selected_copy_destination_address } else { "" }
            candidateLifecyclePhase = if ($null -ne $metadata) { $metadata.lifecycle_phase } else { "" }
            candidateDrawsSinceLastDisplayCopy = if ($null -ne $metadata) { $metadata.draws_since_last_display_copy } else { "" }
            candidateClearsSinceLastDisplayCopy = if ($null -ne $metadata) { $metadata.clears_since_last_display_copy } else { "" }
            sample = $sample.BaseName
            sampleFrame = $sampleFrame
            status = $status
            changedPercent = Get-Percent $changed
            averageDelta = Get-AverageDelta $delta
            sampleNonblackPercent = $sampleNonblackPercent
            candidateNonblackPercent = $candidateNonblackPercent
            changed = $changed
            delta = $delta
            sampleNonblack = $baselineNonblack
            candidateNonblack = $candidateNonblack
            candidatePath = $candidate.FullName
            samplePath = $sample.FullName
            diffPath = $diffPath
            reportPath = $reportPath
        }) | Out-Null
    }
}

if ($rows.Count -eq 0) {
    throw "No comparison rows survived sample nonblack filter $SampleMinNonblackPercent%."
}

$orderedRows = @($rows | Sort-Object changedPercent, averageDelta, candidateSkipDraws, sampleFrame)
$bestByCandidate = @(
    $orderedRows |
        Group-Object candidate |
        ForEach-Object { $_.Group | Sort-Object changedPercent, averageDelta, sampleFrame | Select-Object -First 1 } |
        Sort-Object candidateSkipDraws
)
$bestOverall = @($orderedRows | Select-Object -First ([Math]::Min(12, $orderedRows.Count)))

$summaryPath = Join-Path $runRoot "sync-sweep-summary.csv"
$bestCandidatePath = Join-Path $runRoot "best-by-candidate.csv"
$bestOverallPath = Join-Path $runRoot "best-overall.csv"
$viSyncReportDirectory = Join-Path $runRoot "vi-sync-report"
$viSyncReportJsonPath = Join-Path $viSyncReportDirectory "vi-sync-report.json"
$orderedRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
$bestByCandidate | Export-Csv -LiteralPath $bestCandidatePath -NoTypeInformation
$bestOverall | Export-Csv -LiteralPath $bestOverallPath -NoTypeInformation

if (-not [string]::IsNullOrWhiteSpace($ViTimelineDirectory)) {
    $viTimelineFullPath = Resolve-FullPath $ViTimelineDirectory
    & (Join-Path $PSScriptRoot "build-sonic-vi-sync-report.ps1") `
        -SyncSweepDirectory $runRoot `
        -ViTimelineDirectory $viTimelineFullPath `
        -OutputDirectory $viSyncReportDirectory | Out-Null
}

$runInfo = [pscustomobject]@{
    runStatus = $runStatus
    runExitCode = $runExitCode
    candidateDirectory = $candidateRoot
    dolphinReferenceRoot = $DolphinReferenceRoot
    sampleDirectory = $SampleDirectory
    outputDirectory = $runRoot
    maxInstructions = $MaxInstructions
    sweepStartSkipDraws = $SweepStartSkipDraws
    sweepStepDraws = $SweepStepDraws
    sweepCount = $SweepCount
    sweepMaxDraws = $SweepMaxDraws
    sweepMaxRasterPixels = $SweepMaxRasterPixels
    sampleMinFrame = $SampleMinFrame
    sampleMaxFrame = $SampleMaxFrame
    sampleMinNonblackPercent = $SampleMinNonblackPercent
    candidateMinNonblackPercent = $CandidateMinNonblackPercent
    candidateCount = $candidates.Count
    candidateMetadataRows = $candidateMetadata.Count
    sampleCount = $samples.Count
    comparisonCount = $rows.Count
    viTimelineDirectory = if ([string]::IsNullOrWhiteSpace($ViTimelineDirectory)) { $null } else { Resolve-FullPath $ViTimelineDirectory }
    viSyncReportJsonPath = if (Test-Path -LiteralPath $viSyncReportJsonPath) { $viSyncReportJsonPath } else { $null }
}
$runInfo | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "run.json")

Write-PairContactSheet -Rows $bestByCandidate -OutputPath (Join-Path $runRoot "best-by-candidate-contact-sheet.png") -Title "Sonic sync sweep: best Dolphin match per candidate"
Write-PairContactSheet -Rows $bestOverall -OutputPath (Join-Path $runRoot "best-overall-contact-sheet.png") -Title "Sonic sync sweep: best overall pairs"

Write-Host "Sonic sync sweep summary: $summaryPath"
Write-Host "Best by candidate: $bestCandidatePath"
Write-Host "Best overall: $bestOverallPath"
$bestOverall | Select-Object candidate,candidateSkipDraws,candidateSelectedCopyIndex,candidateSelectedCopyDrawsSeen,sample,sampleFrame,changedPercent,averageDelta,sampleNonblackPercent,candidateNonblackPercent | Format-Table -AutoSize
