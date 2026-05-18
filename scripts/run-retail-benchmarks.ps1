param(
    [string]$OutputDirectory = "artifacts/compat-runs",
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [string]$PikminPath = "Pikmin (USA).rvz",
    [string]$MarioKartDebugPath = "Mario Kart - Double Dash!! (USA) (Debug).rvz",
    [string[]]$Targets = @("sonic-5m", "sonic-20m", "pikmin-5m", "pikmin-20m"),
    [int]$TimeoutSeconds = 300,
    [int]$ProgressIntervalSeconds = 15,
    [switch]$NoBuild,
    [switch]$SkipMissing,
    [switch]$DeepGx
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

function Get-GitValue {
    param([string[]]$Arguments)

    try {
        return (& git @Arguments 2>$null | Select-Object -First 1)
    } catch {
        return ""
    }
}

function Invoke-ProcessWithWatchdog {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath,
        [int]$Timeout,
        [int]$ProgressInterval,
        [string]$Label
    )

    $argumentLine = ($Arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join " "
    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $argumentLine `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $StdoutPath `
        -RedirectStandardError $StderrPath `
        -WindowStyle Hidden `
        -PassThru

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $nextProgress = [Math]::Max(1, $ProgressInterval)
    $timedOut = $false
    while (-not $process.HasExited) {
        Start-Sleep -Milliseconds 250
        if ($stopwatch.Elapsed.TotalSeconds -ge $Timeout) {
            $timedOut = $true
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
            break
        }

        if ($ProgressInterval -gt 0 -and $stopwatch.Elapsed.TotalSeconds -ge $nextProgress) {
            Write-Host ("[{0}] elapsed {1:n0}s / {2}s" -f $Label, $stopwatch.Elapsed.TotalSeconds, $Timeout)
            $nextProgress += $ProgressInterval
        }
    }

    if (-not $timedOut) {
        $process.WaitForExit()
    }

    $process.Refresh()
    $exitCode = if ($timedOut) { $null } else { $process.ExitCode }
    $status = if ($timedOut) {
        "timeout"
    } elseif ($null -eq $exitCode) {
        "exit-unknown"
    } elseif ($exitCode -eq 0) {
        "ok"
    } else {
        "exit-$exitCode"
    }

    return [pscustomobject]@{
        status = $status
        exitCode = $exitCode
        elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        timedOut = $timedOut
    }
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-FileSummary {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        path = $item.FullName
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$repoRoot = Resolve-FullPath "."
$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    dotnet build $appProject --no-restore | Out-Host
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "NgcSharp app DLL not found: $appDll"
}

$sonicFullPath = Resolve-FullPath $SonicPath
$pikminFullPath = Resolve-FullPath $PikminPath
$marioKartDebugFullPath = Resolve-FullPath $MarioKartDebugPath
$targetDefinitions = @{
    "sonic-5m" = [pscustomobject]@{
        slug = "sonic-5m"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "sonic-20m" = [pscustomobject]@{
        slug = "sonic-20m"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 20000000
        timeoutSeconds = $TimeoutSeconds
        gxFrameSource = "auto"
        gxFrameMaxDraws = 160
        gxFrameMaxRasterPixels = 3000000
        dumpGxCopies = $false
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "pikmin-5m" = [pscustomobject]@{
        slug = "pikmin-5m"
        game = "Pikmin"
        gamePath = $pikminFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @()
    }
    "pikmin-20m" = [pscustomobject]@{
        slug = "pikmin-20m"
        game = "Pikmin"
        gamePath = $pikminFullPath
        maxInstructions = 20000000
        timeoutSeconds = $TimeoutSeconds
        gxFrameSource = "auto"
        gxFrameMaxDraws = 1100
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @()
    }
    "mariokart-debug-5m" = [pscustomobject]@{
        slug = "mariokart-debug-5m"
        game = "Mario Kart Double Dash Debug"
        gamePath = $marioKartDebugFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "mariokart-debug-20m" = [pscustomobject]@{
        slug = "mariokart-debug-20m"
        game = "Mario Kart Double Dash Debug"
        gamePath = $marioKartDebugFullPath
        maxInstructions = 20000000
        timeoutSeconds = $TimeoutSeconds
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
}

$unknownTargets = @($Targets | Where-Object { -not $targetDefinitions.ContainsKey($_) })
if ($unknownTargets.Count -gt 0) {
    throw "Unknown target(s): $($unknownTargets -join ', '). Known targets: $($targetDefinitions.Keys -join ', ')"
}

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$commit = Get-GitValue @("rev-parse", "--short", "HEAD")
$branch = Get-GitValue @("branch", "--show-current")
$dirty = -not [string]::IsNullOrWhiteSpace((Get-GitValue @("status", "--porcelain")))
$summaryRows = New-Object System.Collections.Generic.List[object]

foreach ($targetName in $Targets) {
    $target = $targetDefinitions[$targetName]
    $targetRoot = Join-Path $runRoot $target.slug
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null

    if (-not (Test-Path -LiteralPath $target.gamePath)) {
        if ($SkipMissing) {
            Write-Warning "Skipping missing benchmark image: $($target.gamePath)"
            continue
        }

        throw "Missing benchmark image: $($target.gamePath)"
    }

    $framePath = Join-Path $targetRoot "auto.png"
    $copyCsvPath = Join-Path $targetRoot "gx-copies.csv"
    $exiTracePath = Join-Path $targetRoot "exi.csv"
    $stdoutPath = Join-Path $targetRoot "stdout.txt"
    $stderrPath = Join-Path $targetRoot "stderr.txt"
    $gxJsonPath = Join-Path $targetRoot "gx-copies.summary.json"
    $exiJsonPath = Join-Path $targetRoot "exi.summary.json"
    $emulatorSummaryPath = Join-Path $targetRoot "emulator-summary.json"
    $runJsonPath = Join-Path $targetRoot "run.json"

    $gxFrameMaxDraws = if ($DeepGx -and $target.slug -eq "sonic-20m") { 900 } else { $target.gxFrameMaxDraws }
    $gxFrameMaxRasterPixels = if ($DeepGx -and $target.slug -eq "sonic-20m") { 12000000 } else { $target.gxFrameMaxRasterPixels }
    $dumpGxCopies = $DeepGx -or [bool]$target.dumpGxCopies

    $runArgs = @(
        $appDll,
        "run-disc",
        $target.gamePath,
        "--max-instructions", "$($target.maxInstructions)",
        "--fast-forward-idle",
        "--fast-forward-write-watch",
        "--dump-gx-frame", $framePath,
        "--gx-frame-source", $target.gxFrameSource,
        "--gx-frame-max-draws", "$gxFrameMaxDraws",
        "--gx-frame-max-raster-pixels", "$gxFrameMaxRasterPixels",
        "--trace-exi", $exiTracePath,
        "--run-summary", $emulatorSummaryPath,
        "--no-registers",
        "--quiet"
    ) + $target.extraArgs
    if ($dumpGxCopies) {
        $runArgs += @("--dump-gx-copies", $copyCsvPath)
    }

    Write-Host "Running $($target.slug): $($target.game) at $($target.maxInstructions) instructions..."
    $result = Invoke-ProcessWithWatchdog `
        -FilePath "dotnet" `
        -Arguments $runArgs `
        -WorkingDirectory $repoRoot `
        -StdoutPath $stdoutPath `
        -StderrPath $stderrPath `
        -Timeout $target.timeoutSeconds `
        -ProgressInterval $ProgressIntervalSeconds `
        -Label $target.slug

    $exiSummary = $null
    if (Test-Path -LiteralPath $exiTracePath) {
        & (Join-Path $PSScriptRoot "summarize-exi-trace.ps1") -TracePath $exiTracePath -JsonPath $exiJsonPath | Out-Null
        $exiSummary = Read-JsonFile $exiJsonPath
    }

    $gxSummary = $null
    if ((Test-Path -LiteralPath $copyCsvPath) -and (Get-Item -LiteralPath $copyCsvPath).Length -gt 0) {
        & (Join-Path $PSScriptRoot "summarize-gx-copies.ps1") -CopyCsvPath $copyCsvPath -JsonPath $gxJsonPath | Out-Null
        $gxSummary = Read-JsonFile $gxJsonPath
    }

    $emulatorSummary = Read-JsonFile $emulatorSummaryPath
    $frameSummary = Get-FileSummary $framePath
    $effectiveExitCode = if ($null -ne $result.exitCode) {
        $result.exitCode
    } elseif ($null -ne $emulatorSummary) {
        $emulatorSummary.exitCode
    } else {
        $null
    }
    $effectiveProcessStatus = if ($result.timedOut) {
        "timeout"
    } elseif ($null -ne $effectiveExitCode -and $effectiveExitCode -eq 0) {
        "ok"
    } elseif ($null -ne $effectiveExitCode) {
        "exit-$effectiveExitCode"
    } else {
        $result.status
    }
    $status = if ($effectiveProcessStatus -eq "exit-unknown" -and ($null -ne $frameSummary -or $null -ne $exiSummary -or $null -ne $gxSummary)) {
        "ok"
    } else {
        $effectiveProcessStatus
    }

    $runSummary = [ordered]@{
        schema = "ngcsharp.compat-run.v1"
        target = $target.slug
        game = $target.game
        gamePath = $target.gamePath
        commit = $commit
        branch = $branch
        dirtyWorktree = $dirty
        startedAt = (Get-Date).ToString("o")
        maxInstructions = $target.maxInstructions
        status = $status
        exitCode = $effectiveExitCode
        elapsedSeconds = $result.elapsedSeconds
        timeoutSeconds = $target.timeoutSeconds
        deepGx = [bool]$DeepGx
        gxFrameMaxDraws = $gxFrameMaxDraws
        gxFrameMaxRasterPixels = $gxFrameMaxRasterPixels
        gxCopiesRequested = $dumpGxCopies
        emulatorSummary = $emulatorSummary
        frame = $frameSummary
        exiSummary = $exiSummary
        gxCopySummary = $gxSummary
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
    }
    $runSummary | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $runJsonPath

    $readArrayCount = if ($null -ne $exiSummary) { $exiSummary.commands.readArray } else { "" }
    $nonblackCopies = if ($null -ne $gxSummary) { $gxSummary.nonblackDisplayCopies } else { "" }
    $frameBytes = if ($null -ne $frameSummary) { $frameSummary.bytes } else { "" }
    $stopReason = if ($null -ne $emulatorSummary) { $emulatorSummary.stopReason } else { "" }
    $finalPc = if ($null -ne $emulatorSummary) { $emulatorSummary.pc } else { "" }
    $executedInstructions = if ($null -ne $emulatorSummary) { $emulatorSummary.executedInstructions } else { "" }
    $summaryRows.Add([pscustomobject]@{
        target = $target.slug
        status = $status
        elapsedSeconds = $result.elapsedSeconds
        maxInstructions = $target.maxInstructions
        executedInstructions = $executedInstructions
        stopReason = $stopReason
        finalPc = $finalPc
        frameBytes = $frameBytes
        exiReadArrayCommands = $readArrayCount
        nonblackDisplayCopies = $nonblackCopies
        runJson = $runJsonPath
    })
}

$summaryCsvPath = Join-Path $runRoot "summary.csv"
$summaryJsonPath = Join-Path $runRoot "summary.json"
$summaryRows | Export-Csv -NoTypeInformation -LiteralPath $summaryCsvPath

[ordered]@{
    schema = "ngcsharp.compat-suite.v1"
    runRoot = $runRoot
    commit = $commit
    branch = $branch
    dirtyWorktree = $dirty
    targets = @($summaryRows.ToArray())
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryJsonPath

Write-Host "Wrote compatibility run summary: $summaryCsvPath"
