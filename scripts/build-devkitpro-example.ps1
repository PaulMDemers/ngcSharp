param(
    [string]$ExamplesRoot = "",
    [string]$Example = "templates/application",
    [string]$OutputDirectory = "artifacts/devkitpro"
)

$ErrorActionPreference = "Stop"

if ((-not $env:DEVKITPRO -or -not (Test-Path -LiteralPath $env:DEVKITPRO)) -and (Test-Path -LiteralPath "C:\devkitPro")) {
    $env:DEVKITPRO = "C:\devkitPro"
}

if ((-not $env:DEVKITPPC -or -not (Test-Path -LiteralPath $env:DEVKITPPC)) -and $env:DEVKITPRO) {
    $candidateDevkitPpc = Join-Path $env:DEVKITPRO "devkitPPC"
    if (Test-Path -LiteralPath $candidateDevkitPpc) {
        $env:DEVKITPPC = $candidateDevkitPpc
    }
}

if ($env:DEVKITPPC) {
    $devkitPpcBin = Join-Path $env:DEVKITPPC "bin"
    if (Test-Path -LiteralPath $devkitPpcBin) {
        $env:PATH = "$devkitPpcBin;$env:PATH"
    }
}

if ($env:DEVKITPRO) {
    $msysBin = Join-Path $env:DEVKITPRO "msys2\usr\bin"
    if (Test-Path -LiteralPath $msysBin) {
        $env:PATH = "$msysBin;$env:PATH"
    }
}

function Fail-WithSetupHelp {
    param([string]$Reason)

    Write-Error @"
$Reason

devkitPPC is not available in this shell.

Install devkitPro's Windows pacman/MSYS2 environment, then install the GameCube package group:
  pacman -Syu
  pacman -S gamecube-dev gamecube-examples

After installation, run this script from a devkitPro MSYS2 shell or from PowerShell.
The script auto-detects the default C:\devkitPro install when present.
"@
}

if (-not (Get-Command powerpc-eabi-gcc -ErrorAction SilentlyContinue)) {
    Fail-WithSetupHelp "Could not find powerpc-eabi-gcc."
}

if (-not $env:DEVKITPRO -or -not $env:DEVKITPPC) {
    Fail-WithSetupHelp "DEVKITPRO and DEVKITPPC environment variables must be set."
}

if ([string]::IsNullOrWhiteSpace($ExamplesRoot)) {
    $candidate = Join-Path $env:DEVKITPRO "examples\gamecube"
    if (Test-Path -LiteralPath $candidate) {
        $ExamplesRoot = $candidate
    } else {
        Fail-WithSetupHelp "Could not locate GameCube examples under `$env:DEVKITPRO\examples\gamecube."
    }
}

$examplePath = Join-Path $ExamplesRoot $Example
if (-not (Test-Path -LiteralPath $examplePath)) {
    Write-Error "Example path does not exist: $examplePath"
}

$resolvedExamplePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($examplePath)
$resolvedOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)

$make = Get-Command make -ErrorAction SilentlyContinue
if (-not $make) {
    Fail-WithSetupHelp "Could not find make."
}

Push-Location $resolvedExamplePath
try {
    & $make.Source clean
    & $make.Source
    if ($LASTEXITCODE -ne 0) {
        Write-Error "make failed with exit code $LASTEXITCODE."
    }

    $dol = Get-ChildItem -LiteralPath $resolvedExamplePath -Recurse -Filter *.dol | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $dol) {
        Write-Error "Build completed but no .dol was found under $examplePath."
    }

    New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null
    $destination = Join-Path $resolvedOutputDirectory $dol.Name
    Copy-Item -LiteralPath $dol.FullName -Destination $destination -Force
    Write-Host "Built and copied DOL: $destination"
}
finally {
    Pop-Location
}
