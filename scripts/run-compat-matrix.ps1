param(
    [string]$ManifestPath = "compat/targets.json",
    [string]$OutputDirectory = "artifacts/compat-matrix",
    [string[]]$Targets = @(),
    [string[]]$Suites = @(),
    [string[]]$Tags = @(),
    [switch]$IncludeDisabled,
    [switch]$SkipMissing,
    [switch]$List,
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 0,
    [int]$ProgressIntervalSeconds = 15
)

$ErrorActionPreference = "Stop"

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
        return "target"
    }

    return $slug.ToLowerInvariant()
}

function ConvertTo-List {
    param([object[]]$Values)

    @(
        $Values |
            ForEach-Object { "$_" -split "," } |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function ConvertTo-RelativePath {
    param(
        [string]$Path,
        [string]$Root
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $fullPath = Resolve-FullPath $Path
    $fullRoot = (Resolve-FullPath $Root).TrimEnd('\', '/')
    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    }

    return $fullPath
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

function Get-Value {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Default = $null
    )

    if ($null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name) {
        return $Object.PSObject.Properties[$Name].Value
    }

    return $Default
}

function Test-Intersects {
    param(
        [object[]]$Left,
        [object[]]$Right
    )

    $rightSet = @{}
    foreach ($item in @($Right)) {
        $rightSet["$item".ToLowerInvariant()] = $true
    }

    foreach ($item in @($Left)) {
        if ($rightSet.ContainsKey("$item".ToLowerInvariant())) {
            return $true
        }
    }

    return $false
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
    return [pscustomobject]@{
        timedOut = $timedOut
        exitCode = if ($timedOut) { $null } else { $process.ExitCode }
        elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
    }
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-HashSummary {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        path = $item.FullName
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Add-IfPresent {
    param(
        [System.Collections.Generic.List[string]]$Arguments,
        [object]$Object,
        [string]$Property,
        [string]$Flag
    )

    $value = Get-Value $Object $Property $null
    if ($null -ne $value -and "$value" -ne "") {
        $Arguments.Add($Flag)
        $Arguments.Add("$value")
    }
}

function Get-ExpectedStatus {
    param([object]$Target)

    $expected = Get-Value $Target "expected" $null
    $status = Get-Value $expected "status" "ok"
    return "$status"
}

$repoRoot = Resolve-FullPath "."
$manifestFullPath = Resolve-FullPath $ManifestPath
if (-not (Test-Path -LiteralPath $manifestFullPath)) {
    throw "Compatibility manifest not found: $manifestFullPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$defaults = Get-Value $manifest "defaults" ([pscustomobject]@{})
$targetFilter = ConvertTo-List $Targets
$suiteFilter = ConvertTo-List $Suites
$tagFilter = ConvertTo-List $Tags

$selectedTargets = @(
    foreach ($target in @($manifest.targets)) {
        $id = "$(Get-Value $target 'id' '')"
        if ([string]::IsNullOrWhiteSpace($id)) {
            continue
        }

        $enabled = [bool](Get-Value $target "enabled" $true)
        if (-not $enabled -and -not $IncludeDisabled) {
            continue
        }

        if ($targetFilter.Count -gt 0 -and -not ($targetFilter -contains $id)) {
            continue
        }

        $targetSuites = @((Get-Value $target "suites" @()))
        if ($suiteFilter.Count -gt 0 -and -not (Test-Intersects $suiteFilter $targetSuites)) {
            continue
        }

        $targetTags = @((Get-Value $target "tags" @()))
        if ($tagFilter.Count -gt 0 -and -not (Test-Intersects $tagFilter $targetTags)) {
            continue
        }

        $target
    }
)

if ($List) {
    $selectedTargets |
        Select-Object `
            @{ Name = "id"; Expression = { Get-Value $_ "id" "" } },
            @{ Name = "type"; Expression = { Get-Value $_ "type" "dol" } },
            @{ Name = "suites"; Expression = { @((Get-Value $_ "suites" @())) -join "," } },
            @{ Name = "tags"; Expression = { @((Get-Value $_ "tags" @())) -join "," } },
            @{ Name = "path"; Expression = { Get-Value $_ "path" "" } } |
        Format-Table -AutoSize
    return
}

if ($selectedTargets.Count -eq 0) {
    throw "No compatibility targets selected."
}

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

$runRoot = Join-Path (Resolve-FullPath $OutputDirectory) (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
Copy-Item -LiteralPath $manifestFullPath -Destination (Join-Path $runRoot "targets.json")

$commit = Get-GitValue @("rev-parse", "--short", "HEAD")
$branch = Get-GitValue @("branch", "--show-current")
$dirty = -not [string]::IsNullOrWhiteSpace((Get-GitValue @("status", "--porcelain")))
$rows = New-Object System.Collections.Generic.List[object]
$runDetails = New-Object System.Collections.Generic.List[object]

$index = 0
foreach ($target in $selectedTargets) {
    $index++
    $id = "$(Get-Value $target 'id' '')"
    $type = "$(Get-Value $target 'type' 'dol')".ToLowerInvariant()
    $path = "$(Get-Value $target 'path' '')"
    $targetRoot = Join-Path $runRoot ("{0:D3}-{1}" -f $index, (ConvertTo-Slug $id))
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null

    $resolvedPath = ""
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        $resolvedPath = Resolve-FullPath $path
    }

    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $resolvedPath)) {
        $message = "Missing target file: $path"
        if (-not $SkipMissing) {
            throw $message
        }

        Write-Warning $message
        $rows.Add([pscustomobject]@{
            id = $id
            type = $type
            status = "missing"
            expectedStatus = Get-ExpectedStatus $target
            elapsedSeconds = 0
            exitCode = ""
            stopReason = ""
            pc = ""
            executedInstructions = ""
            gxFifoBytes = ""
            displayCopies = ""
            textureCopies = ""
            nonblackDisplayCopies = ""
            maxDisplayNonblack = ""
            prsDecompressInstructions = ""
            resourceLookupInstructions = ""
            externalInterruptLeafInstructions = ""
            topPc = ""
            topPcCount = ""
            frameSource = ""
            frameSourceAddress = ""
            frameSourceCopyIndex = ""
            frameSha256 = ""
            regressions = "missing target file"
            targetPath = $path
            runDirectory = $targetRoot
        })
        continue
    }

    $maxInstructions = [int](Get-Value $target "maxInstructions" (Get-Value $defaults "maxInstructions" 1000000))
    $targetTimeout = if ($TimeoutSeconds -gt 0) { $TimeoutSeconds } else { [int](Get-Value $target "timeoutSeconds" (Get-Value $defaults "timeoutSeconds" 60)) }
    $traceTail = [int](Get-Value $target "traceTail" (Get-Value $defaults "traceTail" 32))
    $profilePc = [int](Get-Value $target "profilePc" (Get-Value $defaults "profilePc" 12))
    $command = if ($type -eq "disc") { "run-disc" } else { "run-dol" }

    $stdoutPath = Join-Path $targetRoot "stdout.txt"
    $stderrPath = Join-Path $targetRoot "stderr.txt"
    $tracePath = Join-Path $targetRoot "tail.trace"
    $runSummaryPath = Join-Path $targetRoot "run-summary.json"
    $gxFramePath = Join-Path $targetRoot "gx.png"
    $xfbFramePath = Join-Path $targetRoot "xfb.png"
    $exiTracePath = Join-Path $targetRoot "exi.csv"
    $siTracePath = Join-Path $targetRoot "si.csv"
    $gxCopiesPath = Join-Path $targetRoot "gx-copies.csv"
    $gxCopiesSummaryPath = Join-Path $targetRoot "gx-copies.summary.json"

    $args = New-Object System.Collections.Generic.List[string]
    $args.Add($appDll)
    $args.Add($command)
    $args.Add($resolvedPath)
    $args.Add("--max-instructions")
    $args.Add("$maxInstructions")
    $args.Add("--run-summary")
    $args.Add($runSummaryPath)

    if ([bool](Get-Value $target "fastForwardIdle" (Get-Value $defaults "fastForwardIdle" $true))) {
        $args.Add("--fast-forward-idle")
    }
    if ([bool](Get-Value $target "noRegisters" (Get-Value $defaults "noRegisters" $true))) {
        $args.Add("--no-registers")
    }
    if ([bool](Get-Value $target "quiet" (Get-Value $defaults "quiet" $true))) {
        $args.Add("--quiet")
    }
    if ($profilePc -gt 0) {
        $args.Add("--profile-pc")
        $args.Add("$profilePc")
    }

    $trace = Get-Value $target "trace" $null
    if ([bool](Get-Value $trace "instruction" $false)) {
        $args.Add("--trace-tail")
        $args.Add("$traceTail")
        $args.Add("--trace-file")
        $args.Add($tracePath)
    }
    if ([bool](Get-Value $trace "exi" $false)) {
        $args.Add("--trace-exi")
        $args.Add($exiTracePath)
    }
    if ([bool](Get-Value $trace "si" $false)) {
        $args.Add("--trace-si")
        $args.Add($siTracePath)
    }

    $gxFrame = Get-Value $target "gxFrame" $null
    if ($null -ne $gxFrame) {
        $args.Add("--dump-gx-frame")
        $args.Add($gxFramePath)
        Add-IfPresent $args $gxFrame "source" "--gx-frame-source"
        Add-IfPresent $args $gxFrame "maxDraws" "--gx-frame-max-draws"
        Add-IfPresent $args $gxFrame "skipDraws" "--gx-frame-skip-draws"
        Add-IfPresent $args $gxFrame "maxRasterPixels" "--gx-frame-max-raster-pixels"
    }

    if ([bool](Get-Value $target "dumpGxCopies" $false)) {
        $args.Add("--dump-gx-copies")
        $args.Add($gxCopiesPath)
    }

    $xfbFrame = Get-Value $target "xfbFrame" $null
    if ($null -ne $xfbFrame) {
        $args.Add("--dump-frame")
        $args.Add($xfbFramePath)
        Add-IfPresent $args $xfbFrame "address" "--frame-address"
        Add-IfPresent $args $xfbFrame "width" "--frame-width"
        Add-IfPresent $args $xfbFrame "height" "--frame-height"
        Add-IfPresent $args $xfbFrame "format" "--frame-format"
    }

    foreach ($extraArg in @((Get-Value $target "extraArgs" @()))) {
        $args.Add("$extraArg")
    }

    Write-Host ("[{0}/{1}] {2} ({3})" -f $index, $selectedTargets.Count, $id, $type)
    $processResult = Invoke-ProcessWithWatchdog `
        -FilePath "dotnet" `
        -Arguments $args.ToArray() `
        -WorkingDirectory $repoRoot `
        -StdoutPath $stdoutPath `
        -StderrPath $stderrPath `
        -Timeout $targetTimeout `
        -ProgressInterval $ProgressIntervalSeconds `
        -Label $id

    $summary = Read-JsonFile $runSummaryPath
    $gxCopySummary = $null
    if ((Test-Path -LiteralPath $gxCopiesPath) -and (Get-Item -LiteralPath $gxCopiesPath).Length -gt 0) {
        & (Join-Path $PSScriptRoot "summarize-gx-copies.ps1") -CopyCsvPath $gxCopiesPath -JsonPath $gxCopiesSummaryPath | Out-Null
        $gxCopySummary = Read-JsonFile $gxCopiesSummaryPath
    }

    $gxFrameSummary = Get-HashSummary $gxFramePath
    $xfbFrameSummary = Get-HashSummary $xfbFramePath
    $frameSummary = if ($null -ne $gxFrameSummary) { $gxFrameSummary } else { $xfbFrameSummary }

    $expected = Get-Value $target "expected" $null
    $expectedStatus = "$(Get-Value $expected "status" "ok")"
    $regressions = New-Object System.Collections.Generic.List[string]
    $exitCode = if ($null -eq $processResult.exitCode) { "" } else { $processResult.exitCode }
    $status = "ok"
    if ($processResult.timedOut) {
        $status = "timeout"
    } elseif ($null -ne $processResult.exitCode -and $processResult.exitCode -ne 0) {
        $status = "exit-$($processResult.exitCode)"
    }

    $stopReason = "$(Get-Value $summary 'stopReason' '')"
    $expectedStopReason = Get-Value $expected "stopReason" $null
    if ($status -eq "ok" -and $null -ne $expectedStopReason -and "$expectedStopReason" -ne $stopReason) {
        $regressions.Add("stopReason expected $expectedStopReason got $stopReason")
    }

    $pc = "$(Get-Value $summary 'pc' '')"
    $expectedPc = Get-Value $expected "pc" $null
    if ($status -eq "ok" -and $null -ne $expectedPc -and "$expectedPc" -ne $pc) {
        $regressions.Add("pc expected $expectedPc got $pc")
    }

    $gx = Get-Value $summary "gx" $null
    $gxFifoBytes = Get-Value $gx "fifoBytesWritten" ""
    $expectedMinGxFifoBytes = Get-Value $expected "minGxFifoBytes" $null
    if ($status -eq "ok" -and $null -ne $expectedMinGxFifoBytes -and [long]$gxFifoBytes -lt [long]$expectedMinGxFifoBytes) {
        $regressions.Add("gxFifoBytes expected >= $expectedMinGxFifoBytes got $gxFifoBytes")
    }

    $frameDump = Get-Value $gx "frameDump" $null
    $renderedQuads = Get-Value $frameDump "renderedQuads" ""
    $renderedTriangles = Get-Value $frameDump "renderedTriangles" ""
    $expectedMinRenderedQuads = Get-Value $expected "minRenderedQuads" $null
    if ($status -eq "ok" -and $null -ne $expectedMinRenderedQuads -and [long]$renderedQuads -lt [long]$expectedMinRenderedQuads) {
        $regressions.Add("renderedQuads expected >= $expectedMinRenderedQuads got $renderedQuads")
    }

    $expectedMinRenderedTriangles = Get-Value $expected "minRenderedTriangles" $null
    if ($status -eq "ok" -and $null -ne $expectedMinRenderedTriangles -and [long]$renderedTriangles -lt [long]$expectedMinRenderedTriangles) {
        $regressions.Add("renderedTriangles expected >= $expectedMinRenderedTriangles got $renderedTriangles")
    }

    $frameSource = Get-Value $frameDump "source" ""
    $frameSourceAddress = Get-Value $frameDump "sourceAddress" ""
    $frameSourceCopyIndex = Get-Value $frameDump "sourceCopyIndex" ""

    $expectedFrameSource = Get-Value $expected "frameSource" $null
    if ($status -eq "ok" -and $null -ne $expectedFrameSource -and "$expectedFrameSource" -ne "$frameSource") {
        $regressions.Add("frameSource expected $expectedFrameSource got $frameSource")
    }

    $expectedFrameSourceAddress = Get-Value $expected "frameSourceAddress" $null
    if ($status -eq "ok" -and $null -ne $expectedFrameSourceAddress -and "$expectedFrameSourceAddress" -ne "$frameSourceAddress") {
        $regressions.Add("frameSourceAddress expected $expectedFrameSourceAddress got $frameSourceAddress")
    }

    $expectedFrameSourceCopyIndex = Get-Value $expected "frameSourceCopyIndex" $null
    if ($status -eq "ok" -and $null -ne $expectedFrameSourceCopyIndex -and "$expectedFrameSourceCopyIndex" -ne "$frameSourceCopyIndex") {
        $regressions.Add("frameSourceCopyIndex expected $expectedFrameSourceCopyIndex got $frameSourceCopyIndex")
    }

    $fastForward = Get-Value $summary "fastForward" $null
    $prsDecompressInstructions = Get-Value $fastForward "prsDecompressInstructions" ""
    $resourceLookupInstructions = Get-Value $fastForward "resourceLookupInstructions" ""
    $externalInterruptLeafInstructions = Get-Value $fastForward "externalInterruptLeafInstructions" ""

    $expectedMinPrsDecompressInstructions = Get-Value $expected "minPrsDecompressInstructions" $null
    if ($status -eq "ok" -and $null -ne $expectedMinPrsDecompressInstructions -and [long]$prsDecompressInstructions -lt [long]$expectedMinPrsDecompressInstructions) {
        $regressions.Add("prsDecompressInstructions expected >= $expectedMinPrsDecompressInstructions got $prsDecompressInstructions")
    }

    $expectedMinResourceLookupInstructions = Get-Value $expected "minResourceLookupInstructions" $null
    if ($status -eq "ok" -and $null -ne $expectedMinResourceLookupInstructions -and [long]$resourceLookupInstructions -lt [long]$expectedMinResourceLookupInstructions) {
        $regressions.Add("resourceLookupInstructions expected >= $expectedMinResourceLookupInstructions got $resourceLookupInstructions")
    }

    $expectedMinExternalInterruptLeafInstructions = Get-Value $expected "minExternalInterruptLeafInstructions" $null
    if ($status -eq "ok" -and $null -ne $expectedMinExternalInterruptLeafInstructions -and [long]$externalInterruptLeafInstructions -lt [long]$expectedMinExternalInterruptLeafInstructions) {
        $regressions.Add("externalInterruptLeafInstructions expected >= $expectedMinExternalInterruptLeafInstructions got $externalInterruptLeafInstructions")
    }

    $pcProfile = Get-Value $summary "pcProfile" $null
    $pcProfileEntries = @((Get-Value $pcProfile "entries" @()))
    $topPcEntry = if ($pcProfileEntries.Count -gt 0) { $pcProfileEntries[0] } else { $null }
    $topPc = Get-Value $topPcEntry "pc" ""
    $topPcCount = Get-Value $topPcEntry "count" ""

    $expectedTopPc = Get-Value $expected "topPc" $null
    if ($status -eq "ok" -and $null -ne $expectedTopPc -and "$expectedTopPc" -ne "$topPc") {
        $regressions.Add("topPc expected $expectedTopPc got $topPc")
    }

    $expectedMinTopPcCount = Get-Value $expected "minTopPcCount" $null
    if ($status -eq "ok" -and $null -ne $expectedMinTopPcCount -and [long]$topPcCount -lt [long]$expectedMinTopPcCount) {
        $regressions.Add("topPcCount expected >= $expectedMinTopPcCount got $topPcCount")
    }

    $displayCopies = Get-Value $gxCopySummary "displayCopies" ""
    $textureCopies = Get-Value $gxCopySummary "textureCopies" ""
    $nonblackDisplayCopies = Get-Value $gxCopySummary "nonblackDisplayCopies" ""
    $maxDisplayNonblack = Get-Value $gxCopySummary "maxDisplayNonblack" ""

    $expectedMinDisplayCopies = Get-Value $expected "minDisplayCopies" $null
    if ($status -eq "ok" -and $null -ne $expectedMinDisplayCopies -and [long]$displayCopies -lt [long]$expectedMinDisplayCopies) {
        $regressions.Add("displayCopies expected >= $expectedMinDisplayCopies got $displayCopies")
    }

    $expectedMinTextureCopies = Get-Value $expected "minTextureCopies" $null
    if ($status -eq "ok" -and $null -ne $expectedMinTextureCopies -and [long]$textureCopies -lt [long]$expectedMinTextureCopies) {
        $regressions.Add("textureCopies expected >= $expectedMinTextureCopies got $textureCopies")
    }

    $expectedMinNonblackDisplayCopies = Get-Value $expected "minNonblackDisplayCopies" $null
    if ($status -eq "ok" -and $null -ne $expectedMinNonblackDisplayCopies -and [long]$nonblackDisplayCopies -lt [long]$expectedMinNonblackDisplayCopies) {
        $regressions.Add("nonblackDisplayCopies expected >= $expectedMinNonblackDisplayCopies got $nonblackDisplayCopies")
    }

    $expectedMinMaxDisplayNonblack = Get-Value $expected "minMaxDisplayNonblack" $null
    if ($status -eq "ok" -and $null -ne $expectedMinMaxDisplayNonblack -and [long]$maxDisplayNonblack -lt [long]$expectedMinMaxDisplayNonblack) {
        $regressions.Add("maxDisplayNonblack expected >= $expectedMinMaxDisplayNonblack got $maxDisplayNonblack")
    }

    $gxCopySummaryCompact = $null
    if ($null -ne $gxCopySummary) {
        $gxCopySummaryCompact = [ordered]@{
            path = ConvertTo-RelativePath $gxCopiesSummaryPath $repoRoot
            rows = Get-Value $gxCopySummary "rows" ""
            displayCopies = $displayCopies
            textureCopies = $textureCopies
            nonblackDisplayCopies = $nonblackDisplayCopies
            maxDisplayNonblack = $maxDisplayNonblack
            firstNonblackDisplayCopy = Get-Value $gxCopySummary "firstNonblackDisplayCopy" $null
            lastNonblackDisplayCopy = Get-Value $gxCopySummary "lastNonblackDisplayCopy" $null
            largestDisplayCopy = Get-Value $gxCopySummary "largestDisplayCopy" $null
            displayDestinations = Get-Value $gxCopySummary "displayDestinations" @()
            lastCopy = Get-Value $gxCopySummary "lastCopy" $null
        }
    }

    $expectedFrameHash = Get-Value $expected "frameSha256" $null
    if ($status -eq "ok" -and $null -ne $expectedFrameHash) {
        $actualFrameHash = if ($null -ne $frameSummary) { $frameSummary.sha256 } else { "" }
        if ("$expectedFrameHash".ToLowerInvariant() -ne $actualFrameHash) {
            $regressions.Add("frameSha256 expected $expectedFrameHash got $actualFrameHash")
        }
    }

    if ($expectedStatus -eq "known-failing") {
        if ($status -eq "ok" -and $regressions.Count -eq 0) {
            $status = "known-failing-ok"
        } else {
            $status = "known-failing"
        }
    } elseif ($status -eq "ok" -and $regressions.Count -gt 0) {
        $status = "regressed"
    }

    $row = [pscustomobject]@{
        id = $id
        type = $type
        status = $status
        expectedStatus = $expectedStatus
        elapsedSeconds = $processResult.elapsedSeconds
        exitCode = $exitCode
        stopReason = $stopReason
        pc = $pc
        executedInstructions = Get-Value $summary "executedInstructions" ""
        gxFifoBytes = $gxFifoBytes
        displayCopies = $displayCopies
        textureCopies = $textureCopies
        nonblackDisplayCopies = $nonblackDisplayCopies
        maxDisplayNonblack = $maxDisplayNonblack
        prsDecompressInstructions = $prsDecompressInstructions
        resourceLookupInstructions = $resourceLookupInstructions
        externalInterruptLeafInstructions = $externalInterruptLeafInstructions
        topPc = $topPc
        topPcCount = $topPcCount
        renderedQuads = $renderedQuads
        renderedTriangles = $renderedTriangles
        frameSource = $frameSource
        frameSourceAddress = $frameSourceAddress
        frameSourceCopyIndex = $frameSourceCopyIndex
        frameSha256 = if ($null -ne $frameSummary) { $frameSummary.sha256 } else { "" }
        regressions = $regressions -join "; "
        targetPath = ConvertTo-RelativePath $resolvedPath $repoRoot
        runDirectory = ConvertTo-RelativePath $targetRoot $repoRoot
    }
    $rows.Add($row)

    $runDetails.Add([ordered]@{
        target = $target
        result = $row
        command = "dotnet " + (($args.ToArray() | ForEach-Object { Quote-ProcessArgument $_ }) -join " ")
        stdout = $stdoutPath
        stderr = $stderrPath
        runSummary = $summary
        gxCopySummary = $gxCopySummaryCompact
        frame = $frameSummary
    })
}

$summaryCsvPath = Join-Path $runRoot "summary.csv"
$summaryJsonPath = Join-Path $runRoot "summary.json"
$rows | Export-Csv -NoTypeInformation -LiteralPath $summaryCsvPath
[ordered]@{
    schema = "ngcsharp.compat-matrix-run.v1"
    manifest = $manifestFullPath
    runRoot = $runRoot
    commit = $commit
    branch = $branch
    dirtyWorktree = $dirty
    selectedTargets = $selectedTargets.Count
    results = @($rows.ToArray())
    details = @($runDetails.ToArray())
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

$rows | Group-Object status | Sort-Object Name | ForEach-Object {
    Write-Host ("{0}: {1}" -f $_.Name, $_.Count)
}

Write-Host "Wrote compatibility matrix summary: $summaryCsvPath"
