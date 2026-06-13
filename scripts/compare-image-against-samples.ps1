param(
    [Parameter(Mandatory = $true)]
    [string]$CandidatePath,
    [Parameter(Mandatory = $true)]
    [string]$SampleDirectory,
    [string]$OutputDirectory = "artifacts/image-sample-compare",
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
        [string]$StderrPath
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
    $process.WaitForExit()
    $process.Refresh()

    if ($process.ExitCode -eq 0) {
        return "ok"
    }

    return "exit-$($process.ExitCode)"
}

function Get-ReportLine {
    param(
        [string]$Path,
        [string]$Pattern
    )

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

$repoRoot = Resolve-FullPath "."
$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

$candidate = Resolve-FullPath $CandidatePath
$sampleRoot = Resolve-FullPath $SampleDirectory
if (-not (Test-Path -LiteralPath $candidate)) {
    throw "Candidate image not found: $candidate"
}

if (-not (Test-Path -LiteralPath $sampleRoot)) {
    throw "Sample directory not found: $sampleRoot"
}

$samples = @(Get-ChildItem -LiteralPath $sampleRoot -Filter "*.png" -File | Sort-Object Name)
if ($samples.Count -eq 0) {
    throw "No PNG samples found under $sampleRoot"
}

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    dotnet build $appProject --no-restore | Out-Host
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "NgcSharp app DLL not found: $appDll"
}

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$rows = New-Object System.Collections.Generic.List[object]
foreach ($sample in $samples) {
    $sampleOutput = Join-Path $runRoot $sample.BaseName
    New-Item -ItemType Directory -Force -Path $sampleOutput | Out-Null
    $diffPath = Join-Path $sampleOutput "diff.png"
    $reportPath = Join-Path $sampleOutput "compare.txt"
    $stderrPath = Join-Path $sampleOutput "compare-stderr.txt"
    $status = Invoke-DotnetApp `
        -AppDll $appDll `
        -Arguments @("compare-images", $sample.FullName, $candidate, "--diff", $diffPath) `
        -WorkingDirectory $repoRoot `
        -StdoutPath $reportPath `
        -StderrPath $stderrPath

    $changed = Get-ReportLine -Path $reportPath -Pattern "^Changed:"
    $delta = Get-ReportLine -Path $reportPath -Pattern "^Delta:"
    $rows.Add([pscustomobject]@{
        sample = $sample.BaseName
        status = $status
        changedPercent = Get-ChangedPercent $changed
        averageDelta = Get-AverageDelta $delta
        changed = $changed
        delta = $delta
        baselineNonblack = Get-ReportLine -Path $reportPath -Pattern "^Baseline nonblack:"
        candidateNonblack = Get-ReportLine -Path $reportPath -Pattern "^Candidate nonblack:"
        samplePath = $sample.FullName
        candidatePath = $candidate
        diffPath = $diffPath
        reportPath = $reportPath
    }) | Out-Null
}

$ordered = @($rows | Sort-Object changedPercent, averageDelta, sample)
$summaryPath = Join-Path $runRoot "summary.csv"
$ordered | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
$ordered | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "summary.json")

Write-Host "Image sample comparison summary: $summaryPath"
$ordered | Select-Object -First 10 sample,changedPercent,averageDelta,changed,delta | Format-Table -AutoSize
