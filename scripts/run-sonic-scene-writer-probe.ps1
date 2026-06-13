param(
    [string]$OutputDirectory = "artifacts/compat-runs",
    [string]$RunName = "",
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("all", "state-range", "small-data-range")]
    [string]$ProbeName = "all",
    [int64]$MaxInstructions = 50000000,
    [int64]$TraceAfter = 26000000,
    [Nullable[int]]$WatchLimit = $null,
    [string[]]$ExtraRunArgs = @(),
    [switch]$SkipWriterReport,
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

function Invoke-Step {
    param(
        [string]$Label,
        [scriptblock]$Script
    )

    Write-Host "==> $Label"
    & $Script
}

function Test-CsvHasDataRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return @((Get-Content -LiteralPath $Path -TotalCount 2)).Count -gt 1
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$outputRoot = Resolve-FullPath $OutputDirectory
$sonicFullPath = Resolve-FullPath $SonicPath

if (-not (Test-Path -LiteralPath $sonicFullPath)) {
    throw "Missing Sonic image: $sonicFullPath"
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "sonic-scene-writer-" + (Get-Date).ToString("yyyyMMdd-HHmmss")
}

$runRoot = Join-Path $outputRoot $RunName
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/$Configuration/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    Invoke-Step "Building NgcSharp.App" {
        dotnet build $appProject -c $Configuration --nologo
    }
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "App DLL not found after build: $appDll"
}

$probes = @(
    [pscustomobject]@{
        name = "state-range"
        address = "0x801CC1E0"
        length = "0x68"
    },
    [pscustomobject]@{
        name = "small-data-range"
        address = "0x803ADC80"
        length = "0xA8"
    }
)

if ($ProbeName -ne "all") {
    $probes = @($probes | Where-Object { $_.name -eq $ProbeName })
}

$tracePaths = New-Object System.Collections.Generic.List[string]
$sceneStatePaths = New-Object System.Collections.Generic.List[string]
$probeRows = New-Object System.Collections.Generic.List[object]
foreach ($probe in $probes) {
    $probeRoot = Join-Path $runRoot $probe.name
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null

    $writeTracePath = Join-Path $probeRoot "sonic-input-writes.csv"
    $sceneStateCsvPath = Join-Path $probeRoot "sonic-scene-state.csv"
    $summaryJsonPath = Join-Path $probeRoot "emulator-summary.json"
    $stdoutPath = Join-Path $probeRoot "stdout.txt"
    $stderrPath = Join-Path $probeRoot "stderr.txt"

    $runArgs = @(
        $appDll,
        "run-disc",
        $sonicFullPath,
        "--max-instructions", "$MaxInstructions",
        "--fast-forward-idle",
        "--fast-forward-write-watch",
        "--memory-card-a",
        "--controller-button", "a",
        "--trace-sonic-input-writes", $writeTracePath, $probe.address, $probe.length,
        "--trace-sonic-scene-state", $sceneStateCsvPath,
        "--trace-pc-after", "$TraceAfter",
        "--run-summary", $summaryJsonPath
    )

    if ($null -ne $WatchLimit) {
        $runArgs += @("--watch-limit", "$WatchLimit")
    }

    if ($ExtraRunArgs.Count -gt 0) {
        $runArgs += $ExtraRunArgs
    }

    $runArgs += @(
        "--no-registers",
        "--quiet"
    )

    Invoke-Step "Running Sonic scene writer probe $($probe.name)" {
        & dotnet @runArgs > $stdoutPath 2> $stderrPath
    }

    if (Test-CsvHasDataRows $writeTracePath) {
        Invoke-Step "Summarizing writer trace $($probe.name)" {
            & (Join-Path $PSScriptRoot "summarize-sonic-input-writes.ps1") -TraceCsvPath $writeTracePath | Out-Null
        }

        $tracePaths.Add($writeTracePath)
    }

    if (Test-CsvHasDataRows $sceneStateCsvPath) {
        Invoke-Step "Summarizing scene-state trace $($probe.name)" {
            & (Join-Path $PSScriptRoot "summarize-sonic-scene-state.ps1") -TraceCsvPath $sceneStateCsvPath | Out-Null
        }

        $sceneStatePaths.Add($sceneStateCsvPath)
    }

    $probeRows.Add([pscustomobject][ordered]@{
        name = $probe.name
        address = $probe.address
        length = $probe.length
        writerTrace = $writeTracePath
        writerTraceHasRows = Test-CsvHasDataRows $writeTracePath
        sceneState = $sceneStateCsvPath
        sceneStateHasRows = Test-CsvHasDataRows $sceneStateCsvPath
        summary = $summaryJsonPath
        stdout = $stdoutPath
        stderr = $stderrPath
    })
}

$probeRows | Export-Csv -LiteralPath (Join-Path $runRoot "probe-runs.csv") -NoTypeInformation

if (-not $SkipWriterReport -and $tracePaths.Count -gt 0 -and $sceneStatePaths.Count -gt 0) {
    Invoke-Step "Building writer-to-scene event report" {
        & (Join-Path $PSScriptRoot "build-sonic-scene-writer-event-report.ps1") `
            -SceneStateCsvPath $sceneStatePaths[0] `
            -TraceCsvPath $tracePaths.ToArray() `
            -OutputDirectory (Join-Path $runRoot "sonic-scene-writers") | Out-Null
    }
}

[pscustomobject][ordered]@{
    runRoot = $runRoot
    sonicPath = $sonicFullPath
    maxInstructions = $MaxInstructions
    traceAfter = $TraceAfter
    watchLimit = if ($null -eq $WatchLimit) { "" } else { $WatchLimit }
    extraRunArgs = $ExtraRunArgs
    skipWriterReport = $SkipWriterReport.IsPresent
    configuration = $Configuration
    probeName = $ProbeName
    probes = $probeRows
    writerReport = Join-Path $runRoot "sonic-scene-writers/scene-writer-report.json"
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "probe.json")

Write-Host "Sonic scene writer probe complete: $runRoot"
