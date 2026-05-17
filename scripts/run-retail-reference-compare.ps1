param(
    [string]$DolphinReferenceRoot = "",
    [string]$OutputDirectory = "artifacts/retail-reference-compare",
    [int]$SonicMaxInstructions = 50500000,
    [int]$PikminMaxInstructions = 12000000,
    [int]$TimeoutSeconds = 900,
    [string]$RecompareRunRoot = "",
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

function Get-ComparisonMetric {
    param(
        [string]$ReportPath,
        [string]$Pattern
    )

    if (-not (Test-Path -LiteralPath $ReportPath)) {
        return ""
    }

    $line = Select-String -LiteralPath $ReportPath -Pattern $Pattern | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Line.Trim()
}

$repoRoot = Resolve-FullPath "."
$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

if ([string]::IsNullOrWhiteSpace($DolphinReferenceRoot)) {
    $DolphinReferenceRoot = Find-LatestDolphinReferenceRoot
} else {
    $DolphinReferenceRoot = Resolve-FullPath $DolphinReferenceRoot
}

if (-not (Test-Path -LiteralPath (Join-Path $DolphinReferenceRoot "summary.csv"))) {
    throw "Dolphin reference root does not contain summary.csv: $DolphinReferenceRoot"
}

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll"

if (-not $NoBuild) {
    dotnet build $appProject --no-restore | Out-Host
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "NgcSharp app DLL not found: $appDll"
}

$recompareOnly = -not [string]::IsNullOrWhiteSpace($RecompareRunRoot)
$runRoot = if ($recompareOnly) {
    Resolve-FullPath $RecompareRunRoot
} else {
    Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
}
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$targets = @(
    [pscustomobject]@{
        slug = "sonic-adventure-2-battle"
        gamePath = Join-Path $repoRoot "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"
        maxInstructions = $SonicMaxInstructions
        gxFrameSource = "last-nonblack-display-copy"
        gxFrameMaxDraws = 900
        gxFrameMaxRasterPixels = 12000000
        extraArgs = @("--controller-button-window", "a", "22000000", "23000000")
    },
    [pscustomobject]@{
        slug = "pikmin"
        gamePath = Join-Path $repoRoot "Pikmin (USA).rvz"
        maxInstructions = $PikminMaxInstructions
        gxFrameSource = "largest-display-copy"
        gxFrameMaxDraws = 1100
        gxFrameMaxRasterPixels = 12000000
        extraArgs = @()
    }
)

$summaryRows = New-Object System.Collections.Generic.List[object]

foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target.gamePath)) {
        throw "Missing benchmark image: $($target.gamePath)"
    }

    $targetRoot = Join-Path $runRoot $target.slug
    $ourFrame = Join-Path $targetRoot "our.png"
    $drawsPath = Join-Path $targetRoot "gx-draws.txt"
    $stdoutPath = Join-Path $targetRoot "run-stdout.txt"
    $stderrPath = Join-Path $targetRoot "run-stderr.txt"
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null

    if ($recompareOnly) {
        Write-Host "Reusing $($target.slug) frame from $targetRoot..."
        $runResult = [pscustomobject]@{
            status = "reused"
            exitCode = ""
        }
    } else {
        Write-Host "Running $($target.slug) to $($target.maxInstructions) instructions..."
        $runArgs = @(
            "run-disc",
            $target.gamePath,
            "--max-instructions", "$($target.maxInstructions)",
            "--fast-forward-idle",
            "--dump-gx-frame", $ourFrame,
            "--gx-frame-source", $target.gxFrameSource,
            "--gx-frame-max-draws", "$($target.gxFrameMaxDraws)",
            "--gx-frame-max-raster-pixels", "$($target.gxFrameMaxRasterPixels)",
            "--dump-gx-draws", $drawsPath,
            "--quiet",
            "--no-registers"
        ) + $target.extraArgs

        $runResult = Invoke-DotnetApp `
            -AppDll $appDll `
            -Arguments $runArgs `
            -WorkingDirectory $repoRoot `
            -StdoutPath $stdoutPath `
            -StderrPath $stderrPath `
            -Timeout $TimeoutSeconds
    }

    $runStatus = if ((Test-Path -LiteralPath $ourFrame) -and $runResult.status -eq "exit-unknown") {
        "ok"
    } else {
        $runResult.status
    }

    $sampleDir = Join-Path $DolphinReferenceRoot "$($target.slug)/samples"
    $samples = @()
    if (Test-Path -LiteralPath $sampleDir) {
        $samples = @(Get-ChildItem -LiteralPath $sampleDir -Filter "*.png" -File | Sort-Object Name)
    }

    foreach ($sample in $samples) {
        $comparisonSlug = $sample.BaseName
        $comparisonDir = Join-Path $targetRoot $comparisonSlug
        New-Item -ItemType Directory -Force -Path $comparisonDir | Out-Null
        $diffPath = Join-Path $comparisonDir "diff.png"
        $reportPath = Join-Path $comparisonDir "compare.txt"
        $compareStdErr = Join-Path $comparisonDir "compare-stderr.txt"

        $compareStatus = "missing-our-frame"
        if (Test-Path -LiteralPath $ourFrame) {
            $compareResult = Invoke-DotnetApp `
                -AppDll $appDll `
                -Arguments @("compare-images", $sample.FullName, $ourFrame, "--diff", $diffPath) `
                -WorkingDirectory $repoRoot `
                -StdoutPath $reportPath `
                -StderrPath $compareStdErr `
                -Timeout 60
            $compareStatus = $compareResult.status
            if ($compareStatus -eq "exit-unknown" -and -not [string]::IsNullOrWhiteSpace((Get-ComparisonMetric -ReportPath $reportPath -Pattern "^Changed:"))) {
                $compareStatus = "ok"
            }
        }

        $summaryRows.Add([pscustomobject]@{
            slug = $target.slug
            maxInstructions = $target.maxInstructions
            gxFrameSource = $target.gxFrameSource
            runStatus = $runStatus
            dolphinSample = $sample.FullName
            ourFrame = $ourFrame
            diff = $diffPath
            compareStatus = $compareStatus
            changed = Get-ComparisonMetric -ReportPath $reportPath -Pattern "^Changed:"
            baselineNonblack = Get-ComparisonMetric -ReportPath $reportPath -Pattern "^Baseline nonblack:"
            candidateNonblack = Get-ComparisonMetric -ReportPath $reportPath -Pattern "^Candidate nonblack:"
            report = $reportPath
        })
    }

    if ($samples.Count -eq 0) {
        $summaryRows.Add([pscustomobject]@{
            slug = $target.slug
            maxInstructions = $target.maxInstructions
            gxFrameSource = $target.gxFrameSource
            runStatus = $runStatus
            dolphinSample = ""
            ourFrame = $ourFrame
            diff = ""
            compareStatus = "no-dolphin-samples"
            changed = ""
            baselineNonblack = ""
            candidateNonblack = ""
            report = ""
        })
    }
}

$summaryPath = Join-Path $runRoot "summary.csv"
$summaryRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
Write-Host "Retail reference comparison summary: $summaryPath"
