param(
    [string]$OutputRoot = "artifacts/demo-dols",
    [switch]$BuildDevkitProExamples,
    [switch]$SkipDownloads,
    [switch]$SkipDevkitPro,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function ConvertTo-Slug {
    param([string]$Text)

    $slug = $Text -replace '[\\/:\*\?"<>\|]+', '-' -replace '\s+', '-' -replace '[^A-Za-z0-9._-]+', '-'
    $slug = $slug.Trim('.-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "dol"
    }

    return $slug.ToLowerInvariant()
}

function Copy-Dol {
    param(
        [string]$SourcePath,
        [string]$DestinationDirectory,
        [string]$Name,
        [string]$Category,
        [string]$Source,
        [string]$SourceUrl
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

    $sourceItem = Get-Item -LiteralPath $SourcePath
    $baseName = ConvertTo-Slug $Name
    $destination = Join-Path $DestinationDirectory "$baseName.dol"
    $index = 2
    while ((Test-Path -LiteralPath $destination) -and -not $Force) {
        $destination = Join-Path $DestinationDirectory "$baseName-$index.dol"
        $index++
    }

    Copy-Item -LiteralPath $sourceItem.FullName -Destination $destination -Force

    $script:manifest += [pscustomobject]@{
        name = $Name
        category = $Category
        path = $destination
        bytes = (Get-Item -LiteralPath $destination).Length
        source = $Source
        sourceUrl = $SourceUrl
        originalPath = $sourceItem.FullName
    }

    Write-Host "Added DOL: $Name"
}

function Copy-DolTree {
    param(
        [string]$Root,
        [string]$DestinationSubdirectory,
        [string]$Category,
        [string]$Source,
        [string]$SourceUrl,
        [string]$IncludeRegex = "",
        [string]$ExcludeRegex = ""
    )

    if (-not (Test-Path -LiteralPath $Root)) {
        Write-Warning "DOL source root not found: $Root"
        return
    }

    $resolvedRoot = Resolve-FullPath $Root
    $destinationDirectory = Join-Path $script:resolvedOutputRoot $DestinationSubdirectory
    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Filter *.dol |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/')
            if (-not [string]::IsNullOrWhiteSpace($IncludeRegex) -and $relative -notmatch $IncludeRegex) {
                return
            }

            if (-not [string]::IsNullOrWhiteSpace($ExcludeRegex) -and $relative -match $ExcludeRegex) {
                return
            }

            $relativeDirectory = [System.IO.Path]::GetDirectoryName($relative)
            $relativeWithoutExtension = [System.IO.Path]::GetFileNameWithoutExtension($relative)
            if (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
                $relativeWithoutExtension = Join-Path $relativeDirectory $relativeWithoutExtension
            }

            $name = $relativeWithoutExtension -replace '[\\/]+', '-'
            Copy-Dol -SourcePath $_.FullName -DestinationDirectory $destinationDirectory -Name $name -Category $Category -Source $Source -SourceUrl $SourceUrl
        }
}

function Invoke-DevkitProBuildSweep {
    param([string]$ExamplesRoot)

    $bash = "C:\devkitPro\msys2\usr\bin\bash.exe"
    if (-not (Test-Path -LiteralPath $bash)) {
        Write-Warning "devkitPro MSYS2 bash not found at $bash; skipping example builds."
        return
    }

    $logPath = Join-Path $script:resolvedOutputRoot "devkitpro-build.log"
    if ($ExamplesRoot -match '^([A-Za-z]):\\(.*)$') {
        $msysRoot = "/" + $matches[1].ToLowerInvariant() + "/" + ($matches[2] -replace '\\', '/')
    } else {
        $msysRoot = $ExamplesRoot -replace '\\', '/'
    }

    $buildCommand = @"
set +e
export DEVKITPRO=/opt/devkitpro
export DEVKITPPC=/opt/devkitpro/devkitPPC
export PATH=/opt/devkitpro/devkitPPC/bin:/opt/devkitpro/tools/bin:`$PATH
find '$msysRoot' -mindepth 2 -name Makefile -printf '%h\n' | sort -u | while read d; do
  echo "::BUILD::`$d"
  make -C "`$d" clean >/dev/null 2>&1
  make -C "`$d"
  rc=`$?
  if [ `$rc -eq 0 ]; then
    echo "::OK::`$d"
  else
    echo "::FAIL::`$d::`$rc"
  fi
done
"@

    Write-Host "Building devkitPro GameCube examples. Log: $logPath"
    & $bash -lc $buildCommand 2>&1 | Tee-Object -FilePath $logPath
}

function Get-GitHubLatestAsset {
    param(
        [string]$Repository,
        [string]$AssetRegex
    )

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers @{ "User-Agent" = "ngcsharp-demo-sweep" }
    $asset = $release.assets | Where-Object { $_.name -match $AssetRegex } | Select-Object -First 1
    if (-not $asset) {
        throw "No asset matching '$AssetRegex' found in latest release for $Repository."
    }

    return [pscustomobject]@{
        Name = $asset.name
        Url = $asset.browser_download_url
        SourceUrl = $release.html_url
        Version = $release.tag_name
    }
}

function Expand-DownloadedArchive {
    param(
        [string]$ArchivePath,
        [string]$DestinationDirectory
    )

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

    if ($ArchivePath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $DestinationDirectory -Force
        return
    }

    if ($ArchivePath.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase) -or
        $ArchivePath.EndsWith(".tar.gz", [System.StringComparison]::OrdinalIgnoreCase) -or
        $ArchivePath.EndsWith(".tgz", [System.StringComparison]::OrdinalIgnoreCase)) {
        $tar = Get-Command "C:\devkitPro\msys2\usr\bin\bsdtar.exe" -ErrorAction SilentlyContinue
        if (-not $tar) {
            $tar = Get-Command tar -ErrorAction SilentlyContinue
        }

        if (-not $tar) {
            Write-Warning "tar is not available; cannot extract $ArchivePath."
            return
        }

        & $tar.Source -xf $ArchivePath -C $DestinationDirectory
        return
    }

    if ($ArchivePath.EndsWith(".7z", [System.StringComparison]::OrdinalIgnoreCase)) {
        $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
        if (-not $sevenZip) {
            Write-Warning "7z is not available; cannot extract $ArchivePath."
            return
        }

        & $sevenZip.Source x "-o$DestinationDirectory" -y $ArchivePath | Out-Null
        return
    }

    Write-Warning "Unsupported archive type: $ArchivePath"
}

function Add-DownloadedPackage {
    param(
        [string]$Name,
        [string]$Category,
        [string]$Url,
        [string]$SourceUrl,
        [string]$AssetName,
        [string]$IncludeRegex = "",
        [string]$ExcludeRegex = ""
    )

    $packageRoot = Join-Path $script:resolvedOutputRoot "downloads\$Name"
    $archiveDirectory = Join-Path $packageRoot "archive"
    $extractDirectory = Join-Path $packageRoot "extract"
    New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null

    $archivePath = Join-Path $archiveDirectory $AssetName
    if ($Force -or -not (Test-Path -LiteralPath $archivePath)) {
        Write-Host "Downloading ${Name}: $Url"
        Invoke-WebRequest -Uri $Url -OutFile $archivePath -Headers @{ "User-Agent" = "ngcsharp-demo-sweep" }
    } else {
        Write-Host "Using cached download: $archivePath"
    }

    if ($Force -and (Test-Path -LiteralPath $extractDirectory)) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }

    Expand-DownloadedArchive -ArchivePath $archivePath -DestinationDirectory $extractDirectory
    Copy-DolTree -Root $extractDirectory -DestinationSubdirectory "downloaded\$Name" -Category $Category -Source $Name -SourceUrl $SourceUrl -IncludeRegex $IncludeRegex -ExcludeRegex $ExcludeRegex
}

$script:resolvedOutputRoot = Resolve-FullPath $OutputRoot
$script:manifest = @()
New-Item -ItemType Directory -Force -Path $script:resolvedOutputRoot | Out-Null

if (-not $SkipDevkitPro) {
    $devkitExamplesRoot = "C:\devkitPro\examples\gamecube"
    if ($BuildDevkitProExamples) {
        Invoke-DevkitProBuildSweep -ExamplesRoot $devkitExamplesRoot
    }

    Copy-DolTree -Root $devkitExamplesRoot -DestinationSubdirectory "devkitpro" -Category "devkitpro-example" -Source "local devkitPro gamecube-examples package" -SourceUrl "https://github.com/devkitPro/gamecube-examples"
}

if (-not $SkipDownloads) {
    $downloadSpecs = @(
        @{ Name = "swiss"; Category = "utility"; Repository = "emukidid/swiss-gc"; AssetRegex = "swiss_.*\.tar\.xz$"; SourceUrl = "https://github.com/emukidid/swiss-gc"; IncludeRegex = '(^|[\\/])DOL[\\/][^\\/]+\.dol$' },
        @{ Name = "gcmm"; Category = "utility"; Repository = "suloku/gcmm"; AssetRegex = "gcmm_.*\.zip$"; SourceUrl = "https://github.com/suloku/gcmm"; IncludeRegex = '(^|[\\/])gamecube[\\/].*\.dol$' },
        @{ Name = "vbagx"; Category = "stress-app"; Repository = "dborth/vbagx"; AssetRegex = "VisualBoyAdvanceGX-.*-GameCube\.zip$"; SourceUrl = "https://github.com/dborth/vbagx" },
        @{ Name = "meese-engine"; Category = "demo"; Url = "https://github.com/meese4/meese-engine/releases/download/indev-v0.1.1-gcn/meese_engine_indev-v0.1.1-gcn.zip"; AssetName = "meese_engine_indev-v0.1.1-gcn.zip"; SourceUrl = "https://meese4.github.io/" }
    )

    foreach ($spec in $downloadSpecs) {
        try {
            if ($spec.Repository) {
                $asset = Get-GitHubLatestAsset -Repository $spec.Repository -AssetRegex $spec.AssetRegex
                Add-DownloadedPackage -Name $spec.Name -Category $spec.Category -Url $asset.Url -SourceUrl $asset.SourceUrl -AssetName $asset.Name -IncludeRegex $spec.IncludeRegex -ExcludeRegex $spec.ExcludeRegex
            } else {
                Add-DownloadedPackage -Name $spec.Name -Category $spec.Category -Url $spec.Url -SourceUrl $spec.SourceUrl -AssetName $spec.AssetName -IncludeRegex $spec.IncludeRegex -ExcludeRegex $spec.ExcludeRegex
            }
        } catch {
            Write-Warning "Skipping $($spec.Name): $($_.Exception.Message)"
        }
    }
}

$manifestPath = Join-Path $script:resolvedOutputRoot "manifest.json"
$script:manifest |
    Sort-Object category, name, path |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Prepared $($script:manifest.Count) DOL(s). Manifest: $manifestPath"
