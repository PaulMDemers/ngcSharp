param(
    [string]$OutputDirectory = "artifacts/compat-runs",
    [string]$RunName = "",
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [int64]$MaxInstructions = 26660000,
    [int64]$TraceAfter = 26000000,
    [int]$WatchLimit = 768,
    [string]$RangeBaseAddress = "0xE0000000",
    [string]$RangeLength = "0xC0",
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

    if (-not (Test-Path -LiteralPath $Path)) {
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
    $RunName = "sonic-matrix-producer-" + (Get-Date).ToString("yyyyMMdd-HHmmss")
}

$runRoot = Join-Path $outputRoot $RunName
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    Invoke-Step "Building NgcSharp.App" {
        dotnet build $appProject --nologo
    }
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "App DLL not found after build: $appDll"
}

$lockedCacheCsvPath = Join-Path $runRoot "locked-cache-writes.csv"
$matrixWriterCsvPath = Join-Path $runRoot "sonic-matrix-writer.csv"
$rootMatrixCsvPath = Join-Path $runRoot "sonic-root-matrix.csv"
$packetSelectionCsvPath = Join-Path $runRoot "sonic-packet-selection.csv"
$summaryJsonPath = Join-Path $runRoot "emulator-summary.json"
$stdoutPath = Join-Path $runRoot "stdout.txt"
$stderrPath = Join-Path $runRoot "stderr.txt"

$runArgs = @(
    $appDll,
    "run-disc",
    $sonicFullPath,
    "--max-instructions", "$MaxInstructions",
    "--fast-forward-idle",
    "--fast-forward-write-watch",
    "--memory-card-a",
    "--controller-button", "a",
    "--trace-locked-cache-writes", $lockedCacheCsvPath, $RangeBaseAddress, $RangeLength,
    "--trace-sonic-matrix-writer", $matrixWriterCsvPath,
    "--trace-sonic-root-matrix", $rootMatrixCsvPath,
    "--trace-sonic-packet-selection", $packetSelectionCsvPath,
    "--trace-pc-after", "$TraceAfter",
    "--watch-limit", "$WatchLimit",
    "--run-summary", $summaryJsonPath,
    "--no-registers",
    "--quiet"
)

Invoke-Step "Running Sonic matrix producer probe" {
    & dotnet @runArgs > $stdoutPath 2> $stderrPath
}

Invoke-Step "Summarizing locked-cache writes" {
    & (Join-Path $PSScriptRoot "summarize-locked-cache-writes.ps1") -TraceCsvPath $lockedCacheCsvPath | Out-Null
}

Invoke-Step "Building matrix producer timeline" {
    & (Join-Path $PSScriptRoot "build-sonic-matrix-producer-timeline.ps1") `
        -LockedCacheCsvPath $lockedCacheCsvPath `
        -RangeBaseAddress $RangeBaseAddress | Out-Null
}

Invoke-Step "Summarizing matrix-slot orthogonality" {
    & (Join-Path $PSScriptRoot "summarize-sonic-matrix-orthogonality.ps1") `
        -TimelineCsvPath (Join-Path $runRoot "sonic-matrix-producer-timeline.csv") | Out-Null
}

if (Test-CsvHasDataRows $matrixWriterCsvPath) {
    Invoke-Step "Summarizing matrix-writer lanes" {
        & (Join-Path $PSScriptRoot "summarize-sonic-matrix-writer.ps1") -TraceCsvPath $matrixWriterCsvPath | Out-Null
        & (Join-Path $PSScriptRoot "summarize-sonic-transform-lanes.ps1") -TraceCsvPath $matrixWriterCsvPath | Out-Null
    }
}

if (Test-CsvHasDataRows $rootMatrixCsvPath) {
    Invoke-Step "Summarizing root matrix producer inputs" {
        & (Join-Path $PSScriptRoot "summarize-sonic-root-matrix.ps1") -TraceCsvPath $rootMatrixCsvPath | Out-Null
    }
}

if (Test-CsvHasDataRows $packetSelectionCsvPath) {
    Invoke-Step "Summarizing packet selection" {
        & (Join-Path $PSScriptRoot "summarize-sonic-packet-selection.ps1") -TraceCsvPath $packetSelectionCsvPath | Out-Null
    }
}

[pscustomobject]@{
    runRoot = $runRoot
    sonicPath = $sonicFullPath
    maxInstructions = $MaxInstructions
    traceAfter = $TraceAfter
    watchLimit = $WatchLimit
    rangeBaseAddress = $RangeBaseAddress
    rangeLength = $RangeLength
    lockedCacheWrites = $lockedCacheCsvPath
    matrixWriter = $matrixWriterCsvPath
    rootMatrix = $rootMatrixCsvPath
    packetSelection = $packetSelectionCsvPath
    emulatorSummary = $summaryJsonPath
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot "probe.json")

Write-Host "Sonic matrix producer probe complete: $runRoot"
