param(
    [string]$OutputDirectory = "artifacts/compat-runs",
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [string[]]$Targets = @("sonic-5m", "sonic-20m"),
    [int]$TimeoutSeconds = 300,
    [int]$ProgressIntervalSeconds = 15,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "run-retail-benchmarks.ps1") `
    -OutputDirectory $OutputDirectory `
    -SonicPath $SonicPath `
    -Targets $Targets `
    -TimeoutSeconds $TimeoutSeconds `
    -ProgressIntervalSeconds $ProgressIntervalSeconds `
    -NoBuild:$NoBuild
