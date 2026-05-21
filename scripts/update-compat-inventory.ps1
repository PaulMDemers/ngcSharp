param(
    [string[]]$Roots = @("artifacts/demo-dols", "artifacts/devkitpro"),
    [string]$OutputDirectory = "artifacts/compat-matrix",
    [switch]$IncludeDownloadExtracts
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function ConvertTo-RelativePath {
    param(
        [string]$Path,
        [string]$Root
    )

    $fullPath = Resolve-FullPath $Path
    $fullRoot = (Resolve-FullPath $Root).TrimEnd('\', '/')
    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    }

    return $fullPath
}

function Get-ManifestLookup {
    param([string]$ManifestPath)

    $lookup = @{}
    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return $lookup
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    foreach ($entry in @($manifest)) {
        if ($entry.PSObject.Properties.Name -contains "path") {
            $key = (Resolve-FullPath $entry.path).ToLowerInvariant()
            $lookup[$key] = $entry
        }
    }

    return $lookup
}

$repoRoot = Resolve-FullPath "."
$outRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$demoManifestLookup = Get-ManifestLookup "artifacts/demo-dols/manifest.json"
$files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($root in $Roots) {
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Warning "Skipping missing inventory root: $root"
        continue
    }

    Get-ChildItem -LiteralPath $root -Recurse -File -Include *.dol,*.elf,*.iso,*.gcm,*.rvz |
        Where-Object { $IncludeDownloadExtracts -or $_.FullName -notmatch '[\\/]downloads[\\/]' } |
        ForEach-Object { $files.Add($_) }
}

$rows = New-Object System.Collections.Generic.List[object]
$seen = @{}
foreach ($file in ($files | Sort-Object FullName)) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $duplicateOf = if ($seen.ContainsKey($hash)) { $seen[$hash] } else { "" }
    if (-not $seen.ContainsKey($hash)) {
        $seen[$hash] = ConvertTo-RelativePath $file.FullName $repoRoot
    }

    $manifestEntry = $null
    $manifestKey = $file.FullName.ToLowerInvariant()
    if ($demoManifestLookup.ContainsKey($manifestKey)) {
        $manifestEntry = $demoManifestLookup[$manifestKey]
    }

    $rows.Add([pscustomobject]@{
        path = ConvertTo-RelativePath $file.FullName $repoRoot
        name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        extension = $file.Extension.ToLowerInvariant()
        bytes = $file.Length
        sha256 = $hash
        duplicateOf = $duplicateOf
        category = if ($null -ne $manifestEntry -and $manifestEntry.PSObject.Properties.Name -contains "category") { $manifestEntry.category } else { "" }
        source = if ($null -ne $manifestEntry -and $manifestEntry.PSObject.Properties.Name -contains "source") { $manifestEntry.source } else { "" }
        sourceUrl = if ($null -ne $manifestEntry -and $manifestEntry.PSObject.Properties.Name -contains "sourceUrl") { $manifestEntry.sourceUrl } else { "" }
    })
}

$jsonPath = Join-Path $outRoot "inventory.json"
$csvPath = Join-Path $outRoot "inventory.csv"
[ordered]@{
    schema = "ngcsharp.compat-inventory.v1"
    generatedAt = (Get-Date).ToString("o")
    roots = @($Roots)
    includeDownloadExtracts = [bool]$IncludeDownloadExtracts
    totalFiles = $rows.Count
    uniqueFiles = @($rows | Where-Object { [string]::IsNullOrWhiteSpace($_.duplicateOf) }).Count
    targets = @($rows.ToArray())
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$rows | Export-Csv -NoTypeInformation -LiteralPath $csvPath

Write-Host "Inventory files: $($rows.Count)"
Write-Host "Unique hashes: $(@($rows | Where-Object { [string]::IsNullOrWhiteSpace($_.duplicateOf) }).Count)"
Write-Host "Wrote $jsonPath"
Write-Host "Wrote $csvPath"
