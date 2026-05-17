param(
    [string]$DolDirectory = "artifacts/devkitpro",
    [string]$OutputDirectory = "artifacts/gx-sweep",
    [int]$MaxInstructions = 12000000,
    [int]$GxFrameMaxDraws = 500,
    [int]$TimeoutSeconds = 90,
    [string[]]$Dols = @(
        "gx-ladder",
        "triangle",
        "texturetest",
        "gxSprites",
        "acube",
        "lesson06",
        "lesson07",
        "lesson08",
        "lesson09",
        "lesson10",
        "lesson11",
        "lesson12",
        "lesson19"
    )
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$dolRoot = Resolve-Path (Join-Path $repoRoot $DolDirectory)
$outRoot = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll"

$summaryPath = Join-Path $outRoot "summary.tsv"
"name`tstatus`tdraws`tfirstNonzero`tfirstNonblack`tgxPngBytes`txfbPngBytes" | Set-Content -Path $summaryPath

function Quote-ProcessArgument {
    param([string]$Argument)

    if ($Argument.Length -eq 0 -or $Argument.IndexOfAny([char[]]" `t`"") -ge 0) {
        return '"' + ($Argument -replace '\\', '\\' -replace '"', '\"') + '"'
    }

    return $Argument
}

foreach ($name in $Dols) {
    $dolPath = Join-Path $dolRoot "$name.dol"
    $demoOut = Join-Path $outRoot $name
    New-Item -ItemType Directory -Force -Path $demoOut | Out-Null

    $gxDraws = Join-Path $demoOut "gx-draws.txt"
    $gxFrame = Join-Path $demoOut "gx-frame.png"
    $xfbFrame = Join-Path $demoOut "xfb-frame.png"
    $stdoutPath = Join-Path $demoOut "stdout.txt"
    $stderrPath = Join-Path $demoOut "stderr.txt"

    if (-not (Test-Path -LiteralPath $dolPath)) {
        "$name`tmissing`t`t`t`t`t" | Add-Content -Path $summaryPath
        Write-Warning "Missing DOL: $dolPath"
        continue
    }

    Write-Host "Running $name..."
    $arguments = @(
        $appDll,
        "run-dol", $dolPath,
        "--max-instructions", $MaxInstructions,
        "--quiet",
        "--no-registers",
        "--fast-forward-idle",
        "--dump-gx-draws", $gxDraws,
        "--dump-gx-frame", $gxFrame,
        "--gx-frame-max-draws", $GxFrameMaxDraws,
        "--dump-frame", $xfbFrame,
        "--frame-width", "640",
        "--frame-height", "480",
        "--frame-format", "yuyv"
    )

    $processArguments = ($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join " "
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $processArguments `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
        $status = "timeout"
    } else {
        $process.Refresh()
        $exitCodeText = "$($process.ExitCode)"
        if ([string]::IsNullOrWhiteSpace($exitCodeText)) {
            $status = if (Test-Path -LiteralPath $gxDraws) { "ok" } else { "exit-unknown" }
        } else {
            $exitCode = [int]$exitCodeText
            $status = if ($exitCode -eq 0) { "ok" } else { "exit-$exitCode" }
        }
    }
    $draws = ""
    $firstNonzero = ""
    $firstNonblack = ""
    if (Test-Path -LiteralPath $gxDraws) {
        $draws = Select-String -Path $gxDraws -Pattern "decoded draws:" | Select-Object -Last 1 | ForEach-Object { $_.Line.Trim() }
        $firstNonzero = Select-String -Path $gxDraws -Pattern "first nonzero XY draw:" | Select-Object -Last 1 | ForEach-Object { $_.Line.Trim() }
        $firstNonblack = Select-String -Path $gxDraws -Pattern "first nonblack RGB draw:" | Select-Object -Last 1 | ForEach-Object { $_.Line.Trim() }
    }

    $gxBytes = if (Test-Path -LiteralPath $gxFrame) { (Get-Item -LiteralPath $gxFrame).Length } else { "" }
    $xfbBytes = if (Test-Path -LiteralPath $xfbFrame) { (Get-Item -LiteralPath $xfbFrame).Length } else { "" }
    "$name`t$status`t$draws`t$firstNonzero`t$firstNonblack`t$gxBytes`t$xfbBytes" | Add-Content -Path $summaryPath
}

Write-Host "Wrote sweep summary: $summaryPath"
