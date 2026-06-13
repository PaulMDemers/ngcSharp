param(
    [string]$OutputDirectory = "artifacts/compat-runs",
    [string]$SonicPath = "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz",
    [string]$PikminPath = "Pikmin (USA).rvz",
    [string]$MarioKartDebugPath = "Mario Kart - Double Dash!! (USA) (Debug).rvz",
    [string[]]$Targets = @("sonic-5m", "sonic-20m", "pikmin-5m", "pikmin-20m"),
    [int]$TimeoutSeconds = 300,
    [int]$ProgressIntervalSeconds = 15,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string[]]$ExtraRunArgs = @(),
    [switch]$NoBuild,
    [switch]$SkipMissing,
    [switch]$DeepGx,
    [switch]$PerfOnly,
    [switch]$SkipProfileClusterReport
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

function Test-CsvHasDataRows {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Import-Csv -LiteralPath $Path | Select-Object -First 1)
}

function Convert-OptionalInt64 {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0L
    }

    return [int64]$Value
}

function Convert-OptionalDouble {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]$Value
}

function Format-OptionalRate {
    param(
        $Instructions,
        $Seconds
    )

    $instructionValue = Convert-OptionalDouble $Instructions
    $secondValue = Convert-OptionalDouble $Seconds
    if ($null -eq $instructionValue -or $null -eq $secondValue -or $secondValue -le 0) {
        return ""
    }

    return [Math]::Round($instructionValue / $secondValue / 1000000.0, 3)
}

function Get-PreferredPcProfile {
    param($EmulatorSummary)

    if ($null -eq $EmulatorSummary) {
        return $null
    }

    if ($null -ne $EmulatorSummary.pcProfileWithoutFastForwardLeaves) {
        return $EmulatorSummary.pcProfileWithoutFastForwardLeaves
    }

    if ($null -ne $EmulatorSummary.pcProfileWithoutExternalInterruptLeaves) {
        return $EmulatorSummary.pcProfileWithoutExternalInterruptLeaves
    }

    return $EmulatorSummary.pcProfile
}

function Format-PcProfileHead {
    param(
        $Profile,
        [int]$Count = 5
    )

    if ($null -eq $Profile -or $null -eq $Profile.entries) {
        return ""
    }

    $entries = @($Profile.entries) | Select-Object -First $Count
    if ($entries.Count -eq 0) {
        return ""
    }

    return (($entries | ForEach-Object { "{0}:{1}:{2}%" -f $_.pc, $_.count, $_.percent }) -join ";")
}

function Get-GxCoverageSummary {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $rows = 0
    $decodedRows = 0
    $nonblackRows = 0
    $blackRows = 0
    $firstDraw = $null
    $lastDraw = $null
    $firstNonblackDraw = $null
    $lastNonblackDraw = $null
    $maxAfterNonblack = 0L
    $maxAfterNonblackDraw = $null
    $maxBounds = ""
    $lastRasterAfter = $null
    $minX = $null
    $maxX = $null
    $minY = $null
    $maxY = $null
    $totalColorWrites = 0L
    $totalBlackWrites = 0L
    $totalAlphaRejected = 0L
    $totalDegenerateTriangles = 0L
    $totalClippedVertices = 0L
    $totalClipInputTriangles = 0L
    $totalNearClipOutputTriangles = 0L
    $totalNearClipCulledTriangles = 0L
    $maxClippedVertices = 0L
    $maxClippedVerticesDraw = $null

    Import-Csv -LiteralPath $Path | ForEach-Object {
        $rows++
        $draw = Convert-OptionalInt64 $_.draw_index
        if ($null -eq $firstDraw) {
            $firstDraw = $draw
        }
        $lastDraw = $draw
        $lastRasterAfter = Convert-OptionalInt64 $_.raster_after

        if ($_.decoded -eq "True") {
            $decodedRows++
        }

        $clippedVertices = Convert-OptionalInt64 $_.clipped_vertices
        $totalClippedVertices += $clippedVertices
        if ($clippedVertices -gt $maxClippedVertices) {
            $maxClippedVertices = $clippedVertices
            $maxClippedVerticesDraw = $draw
        }

        $afterNonblack = Convert-OptionalInt64 $_.after_nonblack
        if ($afterNonblack -gt 0) {
            $nonblackRows++
            if ($null -eq $firstNonblackDraw) {
                $firstNonblackDraw = $draw
            }
            $lastNonblackDraw = $draw
        } else {
            $blackRows++
        }

        if ($afterNonblack -gt $maxAfterNonblack) {
            $maxAfterNonblack = $afterNonblack
            $maxAfterNonblackDraw = $draw
            $maxBounds = if ([string]::IsNullOrWhiteSpace($_.min_x) -or [string]::IsNullOrWhiteSpace($_.max_x) -or [string]::IsNullOrWhiteSpace($_.min_y) -or [string]::IsNullOrWhiteSpace($_.max_y)) {
                ""
            } else {
                "$($_.min_x)/$($_.min_y)-$($_.max_x)/$($_.max_y)"
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($_.min_x)) {
            $rowMinX = Convert-OptionalInt64 $_.min_x
            $rowMaxX = Convert-OptionalInt64 $_.max_x
            $rowMinY = Convert-OptionalInt64 $_.min_y
            $rowMaxY = Convert-OptionalInt64 $_.max_y
            $minX = if ($null -eq $minX) { $rowMinX } else { [Math]::Min([int64]$minX, $rowMinX) }
            $maxX = if ($null -eq $maxX) { $rowMaxX } else { [Math]::Max([int64]$maxX, $rowMaxX) }
            $minY = if ($null -eq $minY) { $rowMinY } else { [Math]::Min([int64]$minY, $rowMinY) }
            $maxY = if ($null -eq $maxY) { $rowMaxY } else { [Math]::Max([int64]$maxY, $rowMaxY) }
        }

        $totalColorWrites += Convert-OptionalInt64 $_.color_writes
        $totalBlackWrites += Convert-OptionalInt64 $_.black_color_writes
        $totalAlphaRejected += Convert-OptionalInt64 $_.alpha_rejected
        $totalDegenerateTriangles += Convert-OptionalInt64 $_.degenerate_triangles_delta
        $totalClipInputTriangles += Convert-OptionalInt64 $_.clip_input_triangles
        $totalNearClipOutputTriangles += Convert-OptionalInt64 $_.near_clip_output_triangles
        $totalNearClipCulledTriangles += Convert-OptionalInt64 $_.near_clip_culled_triangles
    } | Out-Null

    $bounds = if ($null -eq $minX) { "" } else { "$minX/$minY-$maxX/$maxY" }
    return [pscustomobject][ordered]@{
        path = (Resolve-FullPath $Path)
        rows = $rows
        decodedRows = $decodedRows
        nonblackRows = $nonblackRows
        blackRows = $blackRows
        firstDraw = $firstDraw
        lastDraw = $lastDraw
        firstNonblackDraw = $firstNonblackDraw
        lastNonblackDraw = $lastNonblackDraw
        maxAfterNonblack = $maxAfterNonblack
        maxAfterNonblackDraw = $maxAfterNonblackDraw
        maxAfterNonblackBounds = $maxBounds
        overallBounds = $bounds
        rasterAfterLast = $lastRasterAfter
        rasterExhausted = ($null -ne $lastRasterAfter -and $lastRasterAfter -le 0)
        totalColorWrites = $totalColorWrites
        totalBlackWrites = $totalBlackWrites
        totalAlphaRejected = $totalAlphaRejected
        totalDegenerateTriangles = $totalDegenerateTriangles
        totalClippedVertices = $totalClippedVertices
        maxClippedVertices = $maxClippedVertices
        maxClippedVerticesDraw = $maxClippedVerticesDraw
        totalClipInputTriangles = $totalClipInputTriangles
        totalNearClipOutputTriangles = $totalNearClipOutputTriangles
        totalNearClipCulledTriangles = $totalNearClipCulledTriangles
    }
}

$repoRoot = Resolve-FullPath "."
$dotnetRoot = Join-Path $repoRoot ".dotnet"
if (Test-Path -LiteralPath $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

$appProject = Join-Path $repoRoot "src/NgcSharp.App/NgcSharp.App.csproj"
$appDll = Join-Path $repoRoot "src/NgcSharp.App/bin/$Configuration/net10.0/NgcSharp.App.dll"
if (-not $NoBuild) {
    dotnet build $appProject --configuration $Configuration --no-restore | Out-Host
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "NgcSharp app DLL not found: $appDll"
}

$sonicFullPath = Resolve-FullPath $SonicPath
$pikminFullPath = Resolve-FullPath $PikminPath
$marioKartDebugFullPath = Resolve-FullPath $MarioKartDebugPath

function New-SonicGxWindowTarget {
    param(
        [int]$SkipDraws,
        [int]$Draws = 80,
        [switch]$Heavy,
        [switch]$Provenance,
        [switch]$Textures,
        [switch]$NoHeldInput
    )

    $slugSuffix = if ($Heavy) { "-heavy" } elseif ($Provenance) { "-provenance" } elseif ($Textures) { "-textures" } else { "" }
    if ($NoHeldInput) {
        $slugSuffix += "-no-held-input"
    }

    $extraArgs = @("--memory-card-a")
    if (-not $NoHeldInput) {
        $extraArgs += @("--controller-button", "a")
    }

    return [pscustomobject]@{
        slug = "sonic-gx-window-$SkipDraws$slugSuffix"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 50000000
        timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
        gxFrameSource = "largest-display-copy"
        gxFrameMaxDraws = $Draws
        gxFrameSkipDraws = $SkipDraws
        gxFrameMaxRasterPixels = 50000000
        gxDrawSkipDraws = $SkipDraws
        gxDrawMaxDraws = $Draws
        dumpGxFrame = $false
        dumpGxDraws = [bool]$Heavy
        dumpGxCopies = $true
        dumpGxCoverage = $true
        dumpGxTriangleCoverage = [bool]$Provenance
        dumpGxTevSamples = [bool]$Heavy
        dumpGxTextures = [bool]$Textures
        dumpGxTransforms = [bool]$Heavy
        dumpGxStateTimeline = [bool]$Provenance
        dumpGxVertices = [bool]$Provenance
        gxVertexFocusFifoOffset = if ($Provenance) { "+0x2D1E42" } else { "" }
        traceSonicVertexProvenance = [bool]$Provenance
        sonicVertexProvenanceStart = if ($Provenance) { "0x2D1E00" } else { "" }
        sonicVertexProvenanceLength = if ($Provenance) { "0x4000" } else { "" }
        traceSonicSceneState = [bool]$Provenance
        traceSonicMatrixWriter = [bool]$Provenance
        traceSonicPacketSelection = [bool]$Provenance
        traceSonicTraversalSource = [bool]$Provenance
        traceSonicTransformInputs = [bool]$Provenance
        sonicTransformOutputRangeAddress = if ($Provenance) { "0x80B28500" } else { "" }
        sonicTransformOutputRangeLength = if ($Provenance) { "0xE60" } else { "" }
        tracePcAfter = if ($Provenance) { "26650000" } else { "" }
        watchLimit = if ($Provenance) { "2000" } else { "" }
        windowGxCopies = $true
        extraArgs = $extraArgs
    }
}

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
        dumpGxFrame = $false
        dumpGxCopies = $false
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "sonic-profile-5m" = [pscustomobject]@{
        slug = "sonic-profile-5m"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxFrame = $false
        dumpGxCopies = $false
        pcProfileTop = 30
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
    "sonic-title-probe" = [pscustomobject]@{
        slug = "sonic-title-probe"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 50000000
        timeoutSeconds = [Math]::Max($TimeoutSeconds, 600)
        gxFrameSource = "largest-display-copy"
        gxFrameMaxDraws = 900
        gxFrameMaxRasterPixels = 12000000
        dumpGxCopies = $true
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "sonic-copy-events-50m" = [pscustomobject]@{
        slug = "sonic-copy-events-50m"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 50000000
        timeoutSeconds = [Math]::Max($TimeoutSeconds, 600)
        gxFrameSource = "largest-display-copy"
        gxFrameMaxDraws = 900
        gxFrameMaxRasterPixels = 12000000
        dumpGxFrame = $false
        dumpGxCopies = $false
        dumpGxCopyEvents = $true
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "sonic-late-black-window" = [pscustomobject]@{
        slug = "sonic-late-black-window"
        game = "Sonic Adventure 2 Battle"
        gamePath = $sonicFullPath
        maxInstructions = 50000000
        timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
        gxFrameSource = "largest-display-copy"
        gxFrameMaxDraws = 80
        gxFrameSkipDraws = 202
        gxFrameMaxRasterPixels = 50000000
        gxDrawSkipDraws = 202
        gxDrawMaxDraws = 80
        dumpGxFrame = $false
        dumpGxDraws = $true
        dumpGxCopies = $true
        dumpGxCoverage = $true
        dumpGxTevSamples = $true
        windowGxCopies = $true
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
        dumpGxCopies = $false
        extraArgs = @()
    }
    "pikmin-profile-5m" = [pscustomobject]@{
        slug = "pikmin-profile-5m"
        game = "Pikmin"
        gamePath = $pikminFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxFrame = $false
        dumpGxCopies = $false
        pcProfileTop = 30
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
        dumpGxCopies = $false
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
        dumpGxFrame = $false
        dumpGxCopies = $false
        extraArgs = @("--memory-card-a", "--controller-button", "a")
    }
    "mariokart-debug-profile-5m" = [pscustomobject]@{
        slug = "mariokart-debug-profile-5m"
        game = "Mario Kart Double Dash Debug"
        gamePath = $marioKartDebugFullPath
        maxInstructions = 5000000
        timeoutSeconds = [Math]::Min($TimeoutSeconds, 180)
        gxFrameSource = "auto"
        gxFrameMaxDraws = 700
        gxFrameMaxRasterPixels = 12000000
        dumpGxFrame = $false
        dumpGxCopies = $false
        pcProfileTop = 30
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

foreach ($skipDraws in @(280, 400, 480, 520, 560, 600, 640, 680, 1000, 1500, 4380, 8260)) {
    $targetDefinitions["sonic-gx-window-$skipDraws"] = New-SonicGxWindowTarget -SkipDraws $skipDraws
}

$targetDefinitions["sonic-gx-window-202-heavy"] = New-SonicGxWindowTarget -SkipDraws 202 -Heavy
$targetDefinitions["sonic-gx-window-560-heavy"] = New-SonicGxWindowTarget -SkipDraws 560 -Heavy
$targetDefinitions["sonic-gx-window-12140-heavy"] = New-SonicGxWindowTarget -SkipDraws 12140 -Heavy
$targetDefinitions["sonic-gx-window-12140-heavy-no-held-input"] = New-SonicGxWindowTarget -SkipDraws 12140 -Heavy -NoHeldInput
$targetDefinitions["sonic-gx-window-12140-provenance"] = New-SonicGxWindowTarget -SkipDraws 12140 -Draws 120 -Provenance
$targetDefinitions["sonic-gx-window-12140-materials"] = (New-SonicGxWindowTarget -SkipDraws 12140 -Draws 120 -Provenance -Textures) | ForEach-Object {
    $_.slug = "sonic-gx-window-12140-materials"
    $_
}
$targetDefinitions["sonic-gx-window-12180-efb-material"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 24 -Heavy) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-efb-material"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "efb"
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxTextures = $true
    $_.dumpGxVertices = $true
    $_
}
$targetDefinitions["sonic-gx-window-12180-provenance-materials"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 24 -Provenance -Textures) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-provenance-materials"
    $_.dumpGxFrame = $false
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $true
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxTextures = $true
    $_.dumpGxTransforms = $true
    $_.gxVertexFocusFifoOffset = "+0x2D20AC"
    $_.sonicVertexProvenanceStart = "0x2D1E00"
    $_.sonicVertexProvenanceLength = "0x1800"
    $_.sonicTransformOutputRangeAddress = "0x80B28500"
    $_.sonicTransformOutputRangeLength = "0xE60"
    $_ | Add-Member -NotePropertyName packetTimelineDrawsAfterAnchor -NotePropertyValue 24
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
    $_
}
$targetDefinitions["sonic-gx-window-12180-next-copy"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-next-copy"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "largest-display-copy"
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $true
    $_.dumpGxTriangleCoverage = $true
    $_.gxFrameMaxRasterPixels = 50000000
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawStart -NotePropertyValue 12181
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawEnd -NotePropertyValue 12204
    $_
}
$targetDefinitions["sonic-gx-window-12180-next-copy-vi-timeline"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-next-copy-vi-timeline"
    $_.dumpGxFrame = $false
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $false
    $_.dumpGxTriangleCoverage = $false
    $_.gxFrameMaxRasterPixels = 50000000
    $_ | Add-Member -NotePropertyName traceMmio -NotePropertyValue $true
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawStart -NotePropertyValue 12181
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawEnd -NotePropertyValue 12204
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 1200)
    $_
}
$targetDefinitions["sonic-gx-window-12180-next-copy-provenance"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 3900 -Provenance) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-next-copy-provenance"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "largest-display-copy"
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $true
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxTextures = $false
    $_.dumpGxTransforms = $true
    $_.gxFrameMaxRasterPixels = 50000000
    $_.gxVertexFocusFifoOffset = "+0x2D20AC"
    $_.sonicVertexProvenanceStart = "0x2D1E00"
    $_.sonicVertexProvenanceLength = "0x1800"
    $_.sonicTransformOutputRangeAddress = "0x80B28500"
    $_.sonicTransformOutputRangeLength = "0xE60"
    $_ | Add-Member -NotePropertyName packetTimelineDrawsAfterAnchor -NotePropertyValue 24
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawStart -NotePropertyValue 12181
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawEnd -NotePropertyValue 12204
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 1800)
    $_
}
$targetDefinitions["sonic-gx-window-16066-provenance-materials"] = (New-SonicGxWindowTarget -SkipDraws 16058 -Draws 32 -Provenance) | ForEach-Object {
    $_.slug = "sonic-gx-window-16066-provenance-materials"
    $_.dumpGxFrame = $false
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $true
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxTextures = $false
    $_.dumpGxTransforms = $true
    $_.traceSonicTraversalSource = $true
    $_.gxVertexFocusFifoOffset = "+0x37FF4A"
    $_.sonicVertexProvenanceStart = "0x37FD00"
    $_.sonicVertexProvenanceLength = "0x2000"
    $_.sonicTransformOutputRangeAddress = "0x80B28500"
    $_.sonicTransformOutputRangeLength = "0xE60"
    $_.watchLimit = "5000"
    $_ | Add-Member -NotePropertyName packetTimelineDrawsAfterAnchor -NotePropertyValue 32
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawStart -NotePropertyValue 16066
    $_ | Add-Member -NotePropertyName copyBracketFocusDrawEnd -NotePropertyValue 16089
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 1200)
    $_
}
$targetDefinitions["sonic-gx-window-12180-efb-lifecycle"] = (New-SonicGxWindowTarget -SkipDraws 12180 -Draws 24) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-efb-lifecycle"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "efb"
    $_.gxFrameMaxRasterPixels = 1000000
    $_.timeoutSeconds = [Math]::Min($TimeoutSeconds, 300)
    $_ | Add-Member -NotePropertyName gxFrameSkipCopyMemoryWrites -NotePropertyValue $true
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.windowGxCopies = $false
    $_
}
$targetDefinitions["sonic-gx-window-12180-display-copy"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-display-copy"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "copy-index"
    $_ | Add-Member -NotePropertyName gxFrameCopyIndex -NotePropertyValue 1657
    $_.gxFrameMaxRasterPixels = 50000000
    $_.timeoutSeconds = [Math]::Min($TimeoutSeconds, 420)
    $_ | Add-Member -NotePropertyName gxFrameSkipCopyMemoryWrites -NotePropertyValue $true
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.windowGxCopies = $false
    $_
}
$targetDefinitions["sonic-gx-window-16058-warmed-display-copy"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 7800) | ForEach-Object {
    $_.slug = "sonic-gx-window-16058-warmed-display-copy"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "copy-index"
    $_ | Add-Member -NotePropertyName gxFrameCopyIndex -NotePropertyValue 1660
    $_.gxFrameMaxRasterPixels = 50000000
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
    $_ | Add-Member -NotePropertyName gxFrameSkipCopyMemoryWrites -NotePropertyValue $true
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $false
    $_.dumpGxTriangleCoverage = $false
    $_.windowGxCopies = $true
    $_
}
$targetDefinitions["sonic-gx-window-12180-copy-source"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-window-12180-copy-source"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "copy-source-index"
    $_ | Add-Member -NotePropertyName gxFrameCopyIndex -NotePropertyValue 1657
    $_.gxFrameMaxRasterPixels = 50000000
    $_.timeoutSeconds = [Math]::Min($TimeoutSeconds, 420)
    $_ | Add-Member -NotePropertyName gxFrameSkipCopyMemoryWrites -NotePropertyValue $true
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.windowGxCopies = $false
    $_
}
$targetDefinitions["sonic-gx-window-16058-warmed-copy-source"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 7800) | ForEach-Object {
    $_.slug = "sonic-gx-window-16058-warmed-copy-source"
    $_.dumpGxFrame = $true
    $_.gxFrameSource = "copy-source-index"
    $_ | Add-Member -NotePropertyName gxFrameCopyIndex -NotePropertyValue 1660
    $_.gxFrameMaxRasterPixels = 50000000
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
    $_ | Add-Member -NotePropertyName gxFrameSkipCopyMemoryWrites -NotePropertyValue $true
    $_.dumpGxCopies = $true
    $_.dumpGxCoverage = $false
    $_.dumpGxTriangleCoverage = $false
    $_.windowGxCopies = $true
    $_
}
$targetDefinitions["sonic-gx-interval-8302"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-interval-8302"
    $_.dumpGxCoverage = $false
    $_
}
$targetDefinitions["sonic-gx-interval-8302-materials"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900 -Textures) | ForEach-Object {
    $_.slug = "sonic-gx-interval-8302-materials"
    $_.dumpGxFrame = $false
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxVertices = $true
    $_.windowGxCopies = $false
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 900)
    $_
}
$targetDefinitions["sonic-gx-window-8302-materials"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 30 -Textures) | ForEach-Object {
    $_.slug = "sonic-gx-window-8302-materials"
    $_.dumpGxFrame = $false
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.dumpGxTriangleCoverage = $true
    $_.dumpGxStateTimeline = $true
    $_.dumpGxVertices = $true
    $_.windowGxCopies = $false
    $_.timeoutSeconds = [Math]::Max($TimeoutSeconds, 420)
    $_
}
$targetDefinitions["sonic-gx-interval-8302-frame"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900) | ForEach-Object {
    $_.slug = "sonic-gx-interval-8302-frame"
    $_.dumpGxFrame = $true
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.windowGxCopies = $false
    $_
}
$targetDefinitions["sonic-gx-interval-8302-frame-no-held-input"] = (New-SonicGxWindowTarget -SkipDraws 8302 -Draws 3900 -NoHeldInput) | ForEach-Object {
    $_.slug = "sonic-gx-interval-8302-frame-no-held-input"
    $_.dumpGxFrame = $true
    $_.dumpGxCopies = $false
    $_.dumpGxCoverage = $false
    $_.windowGxCopies = $false
    $_
}

$Targets = @(
    $Targets |
        ForEach-Object { $_ -split "," } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

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
$performanceRows = New-Object System.Collections.Generic.List[object]

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
    $copyEventCsvPath = Join-Path $targetRoot "gx-copy-events.csv"
    $gxDrawsPath = Join-Path $targetRoot "gx-draws.txt"
    $exiTracePath = Join-Path $targetRoot "exi.csv"
    $mmioTracePath = Join-Path $targetRoot "mmio.csv"
    $stdoutPath = Join-Path $targetRoot "stdout.txt"
    $stderrPath = Join-Path $targetRoot "stderr.txt"
    $gxJsonPath = Join-Path $targetRoot "gx-copies.summary.json"
    $gxTimelineCsvPath = Join-Path $targetRoot "gx-display-activity.csv"
    $gxCoveragePath = Join-Path $targetRoot "gx-coverage.csv"
    $gxTriangleCoveragePath = Join-Path $targetRoot "gx-triangle-coverage.csv"
    $gxTevSamplesPath = Join-Path $targetRoot "gx-tev-samples.csv"
    $gxTransformsPath = Join-Path $targetRoot "gx-transforms.csv"
    $gxStateTimelinePath = Join-Path $targetRoot "gx-state-timeline.csv"
    $gxVerticesPath = Join-Path $targetRoot "gx-vertices.csv"
    $gxTexturesPath = Join-Path $targetRoot "textures"
    $sonicVertexProvenancePath = Join-Path $targetRoot "sonic-vertex-provenance.csv"
    $sonicSceneStatePath = Join-Path $targetRoot "sonic-scene-state.csv"
    $sonicMatrixWriterPath = Join-Path $targetRoot "sonic-matrix-writer.csv"
    $sonicPacketSelectionPath = Join-Path $targetRoot "sonic-packet-selection.csv"
    $sonicTraversalSourcePath = Join-Path $targetRoot "sonic-traversal-source.csv"
    $sonicTransformInputsPath = Join-Path $targetRoot "sonic-transform-inputs.csv"
    $exiJsonPath = Join-Path $targetRoot "exi.summary.json"
    $emulatorSummaryPath = Join-Path $targetRoot "emulator-summary.json"
    $runJsonPath = Join-Path $targetRoot "run.json"

    $gxFrameMaxDraws = if ($DeepGx -and $target.slug -eq "sonic-20m") { 900 } else { $target.gxFrameMaxDraws }
    $gxFrameSkipDraws = if ($target.PSObject.Properties.Name -contains "gxFrameSkipDraws") { [int]$target.gxFrameSkipDraws } else { 0 }
    $gxFrameMaxRasterPixels = if ($DeepGx -and $target.slug -eq "sonic-20m") { 12000000 } else { $target.gxFrameMaxRasterPixels }
    $gxDrawSkipDraws = if ($target.PSObject.Properties.Name -contains "gxDrawSkipDraws") { [int]$target.gxDrawSkipDraws } else { 0 }
    $gxDrawMaxDraws = if ($target.PSObject.Properties.Name -contains "gxDrawMaxDraws") { [int]$target.gxDrawMaxDraws } else { 10 }
    $dumpGxCopies = $DeepGx -or [bool]$target.dumpGxCopies
    $dumpGxCopyEvents = ($target.PSObject.Properties.Name -contains "dumpGxCopyEvents") -and [bool]$target.dumpGxCopyEvents
    $dumpGxFrame = -not ($target.PSObject.Properties.Name -contains "dumpGxFrame") -or [bool]$target.dumpGxFrame
    $dumpGxDraws = ($target.PSObject.Properties.Name -contains "dumpGxDraws") -and [bool]$target.dumpGxDraws
    $dumpGxCoverage = ($target.PSObject.Properties.Name -contains "dumpGxCoverage") -and [bool]$target.dumpGxCoverage
    $dumpGxTriangleCoverage = ($target.PSObject.Properties.Name -contains "dumpGxTriangleCoverage") -and [bool]$target.dumpGxTriangleCoverage
    $dumpGxTevSamples = ($target.PSObject.Properties.Name -contains "dumpGxTevSamples") -and [bool]$target.dumpGxTevSamples
    $dumpGxTextures = ($target.PSObject.Properties.Name -contains "dumpGxTextures") -and [bool]$target.dumpGxTextures
    $dumpGxTransforms = ($target.PSObject.Properties.Name -contains "dumpGxTransforms") -and [bool]$target.dumpGxTransforms
    $dumpGxStateTimeline = ($target.PSObject.Properties.Name -contains "dumpGxStateTimeline") -and [bool]$target.dumpGxStateTimeline
    $dumpGxVertices = ($target.PSObject.Properties.Name -contains "dumpGxVertices") -and [bool]$target.dumpGxVertices
    $traceSonicVertexProvenance = ($target.PSObject.Properties.Name -contains "traceSonicVertexProvenance") -and [bool]$target.traceSonicVertexProvenance
    $traceSonicSceneState = ($target.PSObject.Properties.Name -contains "traceSonicSceneState") -and [bool]$target.traceSonicSceneState
    $traceSonicMatrixWriter = ($target.PSObject.Properties.Name -contains "traceSonicMatrixWriter") -and [bool]$target.traceSonicMatrixWriter
    $traceSonicPacketSelection = ($target.PSObject.Properties.Name -contains "traceSonicPacketSelection") -and [bool]$target.traceSonicPacketSelection
    $traceSonicTraversalSource = ($target.PSObject.Properties.Name -contains "traceSonicTraversalSource") -and [bool]$target.traceSonicTraversalSource
    $traceSonicTransformInputs = ($target.PSObject.Properties.Name -contains "traceSonicTransformInputs") -and [bool]$target.traceSonicTransformInputs
    $traceMmio = ($target.PSObject.Properties.Name -contains "traceMmio") -and [bool]$target.traceMmio
    $windowGxCopies = ($target.PSObject.Properties.Name -contains "windowGxCopies") -and [bool]$target.windowGxCopies
    $traceExi = -not [bool]$PerfOnly

    if ($PerfOnly) {
        $dumpGxCopies = $false
        $dumpGxCopyEvents = $false
        $dumpGxFrame = $false
        $dumpGxDraws = $false
        $dumpGxCoverage = $false
        $dumpGxTriangleCoverage = $false
        $dumpGxTevSamples = $false
        $dumpGxTextures = $false
        $dumpGxTransforms = $false
        $dumpGxStateTimeline = $false
        $dumpGxVertices = $false
        $traceSonicVertexProvenance = $false
        $traceSonicSceneState = $false
        $traceSonicMatrixWriter = $false
        $traceSonicPacketSelection = $false
        $traceSonicTraversalSource = $false
        $traceSonicTransformInputs = $false
        $traceMmio = $false
        $windowGxCopies = $false
    }

    $usesGxFrameWindow = $dumpGxFrame -or $dumpGxCoverage -or $dumpGxTriangleCoverage -or $windowGxCopies

    $runArgs = @(
        $appDll,
        "run-disc",
        $target.gamePath,
        "--max-instructions", "$($target.maxInstructions)",
        "--fast-forward-idle",
        "--fast-forward-write-watch",
        "--run-summary", $emulatorSummaryPath,
        "--no-registers",
        "--quiet"
    ) + $target.extraArgs + $ExtraRunArgs
    if ($traceExi) {
        $runArgs += @("--trace-exi", $exiTracePath)
    }
    if (($target.PSObject.Properties.Name -contains "pcProfileTop") -and $null -ne $target.pcProfileTop) {
        $runArgs += @("--profile-pc", "$($target.pcProfileTop)")
    }
    if (($target.PSObject.Properties.Name -contains "profileAfter") -and $null -ne $target.profileAfter) {
        $runArgs += @("--profile-after", "$($target.profileAfter)")
    }
    if ($traceMmio) {
        $runArgs += @("--trace-mmio", $mmioTracePath)
    }
    if (($target.PSObject.Properties.Name -contains "tracePcAfter") -and -not [string]::IsNullOrWhiteSpace($target.tracePcAfter)) {
        $runArgs += @("--trace-pc-after", $target.tracePcAfter)
    }
    if (($target.PSObject.Properties.Name -contains "watchLimit") -and -not [string]::IsNullOrWhiteSpace($target.watchLimit)) {
        $runArgs += @("--watch-limit", $target.watchLimit)
    }
    if ($usesGxFrameWindow) {
        $runArgs += @(
            "--gx-frame-skip-draws", "$gxFrameSkipDraws",
            "--gx-frame-max-draws", "$gxFrameMaxDraws",
            "--gx-frame-max-raster-pixels", "$gxFrameMaxRasterPixels"
        )
    }
    if ($dumpGxFrame) {
        $runArgs += @(
            "--dump-gx-frame", $framePath,
            "--gx-frame-source", $target.gxFrameSource
        )
    }
    if (($target.PSObject.Properties.Name -contains "gxFrameCopyIndex") -and $null -ne $target.gxFrameCopyIndex) {
        $runArgs += @("--gx-frame-copy-index", "$($target.gxFrameCopyIndex)")
    }
    if (($target.PSObject.Properties.Name -contains "gxFrameSkipCopyMemoryWrites") -and [bool]$target.gxFrameSkipCopyMemoryWrites) {
        $runArgs += @("--gx-frame-skip-copy-memory-writes")
    }
    if ($dumpGxCopies) {
        $runArgs += @("--dump-gx-copies", $copyCsvPath)
    }
    if ($dumpGxCopyEvents) {
        $runArgs += @("--dump-gx-copy-events", $copyEventCsvPath)
    }
    if ($dumpGxDraws) {
        $runArgs += @(
            "--dump-gx-draws", $gxDrawsPath,
            "--gx-draw-skip-draws", "$gxDrawSkipDraws",
            "--gx-draw-max-draws", "$gxDrawMaxDraws"
        )
    }
    if ($dumpGxCoverage) {
        $runArgs += @("--dump-gx-coverage", $gxCoveragePath)
    }
    if ($dumpGxTriangleCoverage) {
        $runArgs += @("--dump-gx-triangle-coverage", $gxTriangleCoveragePath)
    }
    if ($dumpGxTevSamples) {
        $runArgs += @("--dump-gx-tev-samples", $gxTevSamplesPath)
        if (-not $dumpGxDraws) {
            $runArgs += @(
                "--gx-draw-skip-draws", "$gxDrawSkipDraws",
                "--gx-draw-max-draws", "$gxDrawMaxDraws"
            )
        }
    }
    if ($dumpGxTextures) {
        $runArgs += @("--dump-gx-textures", $gxTexturesPath)
        if (-not $dumpGxDraws -and -not $dumpGxTevSamples) {
            $runArgs += @(
                "--gx-draw-skip-draws", "$gxDrawSkipDraws",
                "--gx-draw-max-draws", "$gxDrawMaxDraws"
            )
        }
    }
    if ($dumpGxTransforms) {
        $runArgs += @("--dump-gx-transforms", $gxTransformsPath)
        if (-not $dumpGxDraws -and -not $dumpGxTevSamples -and -not $dumpGxTextures) {
            $runArgs += @(
                "--gx-draw-skip-draws", "$gxDrawSkipDraws",
                "--gx-draw-max-draws", "$gxDrawMaxDraws"
            )
        }
    }
    if ($dumpGxStateTimeline) {
        $runArgs += @("--dump-gx-state-timeline", $gxStateTimelinePath)
        if (-not $dumpGxDraws -and -not $dumpGxTevSamples -and -not $dumpGxTextures -and -not $dumpGxTransforms) {
            $runArgs += @(
                "--gx-draw-skip-draws", "$gxDrawSkipDraws",
                "--gx-draw-max-draws", "$gxDrawMaxDraws"
            )
        }
    }
    if ($dumpGxVertices) {
        $runArgs += @("--dump-gx-vertices", $gxVerticesPath)
        if (-not $dumpGxDraws -and -not $dumpGxTevSamples -and -not $dumpGxTextures -and -not $dumpGxTransforms -and -not $dumpGxStateTimeline) {
            $runArgs += @(
                "--gx-draw-skip-draws", "$gxDrawSkipDraws",
                "--gx-draw-max-draws", "$gxDrawMaxDraws"
            )
        }
    }
    if ($traceSonicVertexProvenance) {
        $runArgs += @(
            "--trace-sonic-vertex-provenance", $sonicVertexProvenancePath, $target.sonicVertexProvenanceStart, $target.sonicVertexProvenanceLength
        )
    }
    if ($traceSonicSceneState) {
        $runArgs += @("--trace-sonic-scene-state", $sonicSceneStatePath)
    }
    if ($traceSonicMatrixWriter) {
        $runArgs += @("--trace-sonic-matrix-writer", $sonicMatrixWriterPath)
    }
    if ($traceSonicPacketSelection) {
        $runArgs += @("--trace-sonic-packet-selection", $sonicPacketSelectionPath)
    }
    if ($traceSonicTraversalSource) {
        $runArgs += @("--trace-sonic-traversal-source", $sonicTraversalSourcePath)
    }
    if ($traceSonicTransformInputs) {
        $runArgs += @(
            "--trace-sonic-transform-inputs", $sonicTransformInputsPath,
            "--trace-sonic-transform-output-range", $target.sonicTransformOutputRangeAddress, $target.sonicTransformOutputRangeLength
        )
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
        & (Join-Path $PSScriptRoot "summarize-gx-copies.ps1") -CopyCsvPath $copyCsvPath -JsonPath $gxJsonPath -TimelineCsvPath $gxTimelineCsvPath | Out-Null
        $gxSummary = Read-JsonFile $gxJsonPath
    }
    $gxCoverageSummary = Get-GxCoverageSummary $gxCoveragePath
    $gxTriangleCoverageSummaryCsvPath = Join-Path $targetRoot "gx-triangle-coverage.summary.csv"
    $gxTriangleCoverageSummaryJsonPath = Join-Path $targetRoot "gx-triangle-coverage.summary.json"
    $gxMaterialSummaryCsvPath = Join-Path $targetRoot "gx-materials.summary.csv"
    $gxMaterialSummaryJsonPath = Join-Path $targetRoot "gx-materials.summary.json"
    $viDisplayTimelineDirectory = Join-Path $targetRoot "vi-display-timeline"
    $viDisplayTimelineReportJsonPath = Join-Path $viDisplayTimelineDirectory "vi-display-timeline-report.json"
    $viDisplayCopyJoinCsvPath = Join-Path $viDisplayTimelineDirectory "display-copy-vi-join.csv"
    $viRegisterWritesCsvPath = Join-Path $viDisplayTimelineDirectory "vi-register-writes.csv"
    $viSelectedFrameCopyJoinCsvPath = Join-Path $viDisplayTimelineDirectory "selected-frame-copy-join.csv"
    $gxTriangleCoverageSummary = $null
    if (Test-CsvHasDataRows $copyCsvPath) {
        & (Join-Path $PSScriptRoot "build-vi-display-timeline-report.ps1") `
            -RunRoot $targetRoot `
            -GxCopiesPath $copyCsvPath `
            -MmioTracePath $mmioTracePath `
            -OutputDirectory $viDisplayTimelineDirectory | Out-Null
    }
    if ($dumpGxTriangleCoverage -and (Test-CsvHasDataRows $gxTriangleCoveragePath)) {
        & (Join-Path $PSScriptRoot "summarize-gx-triangle-coverage.ps1") `
            -TriangleCoverageCsvPath $gxTriangleCoveragePath `
            -SummaryCsvPath $gxTriangleCoverageSummaryCsvPath `
            -JsonPath $gxTriangleCoverageSummaryJsonPath | Out-Null

        if (Test-CsvHasDataRows $gxTriangleCoverageSummaryCsvPath) {
            & (Join-Path $PSScriptRoot "summarize-gx-materials.ps1") `
                -TriangleCoverageSummaryCsvPath $gxTriangleCoverageSummaryCsvPath `
                -MaterialCsvPath $gxMaterialSummaryCsvPath `
                -JsonPath $gxMaterialSummaryJsonPath | Out-Null
        }
    }
    if (Test-Path -LiteralPath $gxTriangleCoverageSummaryJsonPath) {
        $gxTriangleCoverageSummary = Read-JsonFile $gxTriangleCoverageSummaryJsonPath
    }

    $gxVertexSummaryPath = Join-Path $targetRoot "gx-vertices.summary.csv"
    $sonicVertexProvenanceSummaryPath = Join-Path $targetRoot "sonic-vertex-provenance.summary.csv"
    $sonicSceneStateSummaryPath = Join-Path $targetRoot "sonic-scene-state.summary.csv"
    $sonicMatrixWriterSummaryPath = Join-Path $targetRoot "sonic-matrix-writer.summary.csv"
    $sonicTransformSourceMapPath = Join-Path $targetRoot "sonic-transform-source-map.csv"
    $sonicFocusMatrixProvenanceDirectory = Join-Path $targetRoot "sonic-focus-matrix-provenance"
    $sonicFocusMatrixProvenanceCsvPath = Join-Path $sonicFocusMatrixProvenanceDirectory "focus-matrix-provenance.csv"
    $sonicFocusMatrixSourceSummaryPath = Join-Path $sonicFocusMatrixProvenanceDirectory "focus-matrix-source-summary.csv"
    $sonicFocusMatrixProvenanceReportJsonPath = Join-Path $sonicFocusMatrixProvenanceDirectory "focus-matrix-provenance-summary.json"

    if ($dumpGxVertices -and (Test-CsvHasDataRows $gxVerticesPath)) {
        $vertexSummaryArgs = @{
            VertexCsvPath = $gxVerticesPath
        }
        if (($target.PSObject.Properties.Name -contains "gxVertexFocusFifoOffset") -and -not [string]::IsNullOrWhiteSpace($target.gxVertexFocusFifoOffset)) {
            $vertexSummaryArgs.FocusFifoOffset = $target.gxVertexFocusFifoOffset
        }

        & (Join-Path $PSScriptRoot "summarize-gx-vertices.ps1") @vertexSummaryArgs | Out-Null
    }
    if ($traceSonicVertexProvenance -and (Test-CsvHasDataRows $sonicVertexProvenancePath)) {
        & (Join-Path $PSScriptRoot "summarize-sonic-vertex-provenance.ps1") -TraceCsvPath $sonicVertexProvenancePath | Out-Null
    }
    if ($traceSonicSceneState -and (Test-CsvHasDataRows $sonicSceneStatePath)) {
        & (Join-Path $PSScriptRoot "summarize-sonic-scene-state.ps1") -TraceCsvPath $sonicSceneStatePath | Out-Null
    }
    if ($traceSonicMatrixWriter -and (Test-CsvHasDataRows $sonicMatrixWriterPath)) {
        & (Join-Path $PSScriptRoot "summarize-sonic-matrix-writer.ps1") -TraceCsvPath $sonicMatrixWriterPath | Out-Null
        & (Join-Path $PSScriptRoot "summarize-sonic-transform-lanes.ps1") -TraceCsvPath $sonicMatrixWriterPath | Out-Null
        if (Test-CsvHasDataRows $sonicMatrixWriterSummaryPath) {
            & (Join-Path $PSScriptRoot "build-sonic-focus-matrix-provenance.ps1") `
                -MatrixWriterSummaryCsvPath $sonicMatrixWriterSummaryPath `
                -OutputDirectory $sonicFocusMatrixProvenanceDirectory | Out-Null
        }
    }
    if ($traceSonicPacketSelection -and (Test-CsvHasDataRows $sonicPacketSelectionPath)) {
        & (Join-Path $PSScriptRoot "summarize-sonic-packet-selection.ps1") -TraceCsvPath $sonicPacketSelectionPath | Out-Null
    }
    if ($traceSonicTransformInputs -and (Test-CsvHasDataRows $sonicTransformInputsPath)) {
        & (Join-Path $PSScriptRoot "summarize-sonic-transform-inputs.ps1") `
            -TransformCsvPath $sonicTransformInputsPath `
            -RecordCsvPath (Join-Path $targetRoot "sonic-transform-input-records.csv") `
            -JsonPath (Join-Path $targetRoot "sonic-transform-inputs.summary.json") | Out-Null

        if (Test-CsvHasDataRows $sonicVertexProvenancePath) {
            & (Join-Path $PSScriptRoot "build-sonic-transform-source-map-from-provenance.ps1") `
                -ProvenanceCsvPath $sonicVertexProvenancePath `
                -TransformCsvPath $sonicTransformInputsPath `
                -SourceMapCsvPath $sonicTransformSourceMapPath `
                -SummaryJsonPath (Join-Path $targetRoot "sonic-transform-source-map.summary.json") | Out-Null

            if (Test-CsvHasDataRows $sonicTransformSourceMapPath) {
                & (Join-Path $PSScriptRoot "fit-sonic-transform-affine-groups.ps1") -SourceMapCsvPath $sonicTransformSourceMapPath | Out-Null
            }
        }
    }
    if ((Test-CsvHasDataRows $sonicSceneStateSummaryPath) -and (Test-CsvHasDataRows $gxVertexSummaryPath) -and (Test-CsvHasDataRows $sonicVertexProvenanceSummaryPath)) {
        $packetTimelineArgs = @{
            SceneSummaryCsvPath = $sonicSceneStateSummaryPath
            VertexSummaryCsvPath = $gxVertexSummaryPath
            AnchorCsvPath = $sonicVertexProvenanceSummaryPath
            OutputCsvPath = Join-Path $targetRoot "sonic-packet-timeline.csv"
            OutputJsonPath = Join-Path $targetRoot "sonic-packet-timeline.json"
        }
        if (($target.PSObject.Properties.Name -contains "packetTimelineDrawsAfterAnchor") -and $null -ne $target.packetTimelineDrawsAfterAnchor) {
            $packetTimelineArgs.DrawsAfterAnchor = [int]$target.packetTimelineDrawsAfterAnchor
        }
        if (Test-CsvHasDataRows $sonicMatrixWriterSummaryPath) {
            $packetTimelineArgs.MatrixWriterSummaryCsvPath = $sonicMatrixWriterSummaryPath
        }

        & (Join-Path $PSScriptRoot "build-sonic-packet-timeline.ps1") @packetTimelineArgs | Out-Null
    }

    $sonicMaterialSourceReportDirectory = Join-Path $targetRoot "sonic-material-source-report"
    $sonicMaterialSourceSummaryPath = Join-Path $sonicMaterialSourceReportDirectory "material-source-summary.csv"
    $sonicMaterialSourceOverlapPath = Join-Path $sonicMaterialSourceReportDirectory "material-source-overlaps.csv"
    $sonicMaterialSourceReportJsonPath = Join-Path $sonicMaterialSourceReportDirectory "material-source-report.json"
    if ((Test-CsvHasDataRows $gxMaterialSummaryCsvPath) -and (Test-CsvHasDataRows $gxVerticesPath) -and (Test-CsvHasDataRows $sonicTransformSourceMapPath)) {
        & (Join-Path $PSScriptRoot "build-sonic-material-source-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicPacketMaterialPartitionDirectory = Join-Path $targetRoot "sonic-packet-material-partitions"
    $sonicPacketMaterialPartitionCsvPath = Join-Path $sonicPacketMaterialPartitionDirectory "packet-material-partitions.csv"
    $sonicPacketMaterialSequenceCsvPath = Join-Path $sonicPacketMaterialPartitionDirectory "packet-material-sequences.csv"
    $sonicPacketMaterialPartitionJsonPath = Join-Path $sonicPacketMaterialPartitionDirectory "packet-material-partition-report.json"
    if ((Test-CsvHasDataRows (Join-Path $targetRoot "sonic-packet-timeline.csv")) -and (Test-CsvHasDataRows $gxTriangleCoverageSummaryCsvPath) -and (Test-CsvHasDataRows $gxVerticesPath) -and (Test-CsvHasDataRows $sonicTransformSourceMapPath)) {
        & (Join-Path $PSScriptRoot "build-sonic-packet-material-partition-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicNonrenderedStripDirectory = Join-Path $targetRoot "sonic-nonrendered-strips"
    $sonicNonrenderedStripDrawCsvPath = Join-Path $sonicNonrenderedStripDirectory "nonrendered-strip-draws.csv"
    $sonicNonrenderedStripSequenceCsvPath = Join-Path $sonicNonrenderedStripDirectory "nonrendered-strip-sequences.csv"
    $sonicNonrenderedStripJsonPath = Join-Path $sonicNonrenderedStripDirectory "nonrendered-strip-report.json"
    if ((Test-CsvHasDataRows $sonicPacketMaterialPartitionCsvPath) -and (Test-CsvHasDataRows $gxCoveragePath) -and (Test-CsvHasDataRows $gxVertexSummaryPath)) {
        & (Join-Path $PSScriptRoot "build-sonic-nonrendered-strip-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicPacketPlacementOverlayDirectory = Join-Path $targetRoot "sonic-packet-placement-overlay"
    $sonicPacketPlacementOverlayPath = Join-Path $sonicPacketPlacementOverlayDirectory "packet-placement-overlay.png"
    $sonicPacketPlacementContactSheetPath = Join-Path $sonicPacketPlacementOverlayDirectory "packet-placement-contact-sheet.png"
    $sonicPacketPlacementBoundsPath = Join-Path $sonicPacketPlacementOverlayDirectory "packet-placement-bounds.csv"
    $sonicPacketPlacementReportJsonPath = Join-Path $sonicPacketPlacementOverlayDirectory "packet-placement-report.json"
    if (Test-CsvHasDataRows $sonicNonrenderedStripSequenceCsvPath) {
        $placementOverlayArgs = @{
            RunDirectory = $targetRoot
        }
        if (Test-Path -LiteralPath $framePath) {
            $placementOverlayArgs.FramePath = $framePath
        }

        & (Join-Path $PSScriptRoot "build-sonic-packet-placement-overlay.ps1") @placementOverlayArgs | Out-Null
    }

    $sonicPacketPlacementComparisonDirectory = Join-Path $targetRoot "sonic-packet-placement-comparison"
    $sonicPacketPlacementComparisonCsvPath = Join-Path $sonicPacketPlacementComparisonDirectory "packet-placement-comparison.csv"
    $sonicPacketPlacementEventsCsvPath = Join-Path $sonicPacketPlacementComparisonDirectory "packet-placement-events.csv"
    $sonicFocusPacketEventsCsvPath = Join-Path $sonicPacketPlacementComparisonDirectory "focus-packet-events.csv"
    $sonicPacketRadiusRankingCsvPath = Join-Path $sonicPacketPlacementComparisonDirectory "packet-radius-ranking.csv"
    $sonicPacketPlacementComparisonSummaryPath = Join-Path $sonicPacketPlacementComparisonDirectory "packet-placement-comparison.summary.csv"
    $sonicPacketPlacementComparisonReportJsonPath = Join-Path $sonicPacketPlacementComparisonDirectory "packet-placement-comparison-report.json"
    if (Test-CsvHasDataRows (Join-Path $targetRoot "sonic-packet-timeline.csv")) {
        & (Join-Path $PSScriptRoot "build-sonic-packet-placement-comparison-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicFocusPacketPhaseDirectory = Join-Path $targetRoot "sonic-focus-packet-phase"
    $sonicFocusPacketPhaseEventsCsvPath = Join-Path $sonicFocusPacketPhaseDirectory "focus-packet-phase-events.csv"
    $sonicFocusPacketPhaseSummaryPath = Join-Path $sonicFocusPacketPhaseDirectory "focus-packet-phase-summary.csv"
    $sonicFocusPacketPhaseReportJsonPath = Join-Path $sonicFocusPacketPhaseDirectory "focus-packet-phase-report.json"
    if ((Test-CsvHasDataRows (Join-Path $targetRoot "sonic-packet-timeline.csv")) -and (Test-CsvHasDataRows $gxCoveragePath)) {
        & (Join-Path $PSScriptRoot "build-sonic-focus-packet-phase-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicSceneStateDeltaDirectory = Join-Path $targetRoot "sonic-scene-state-delta"
    $sonicSceneStateScalarDeltaCsvPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-scalar-deltas.csv"
    $sonicSceneStateEventCsvPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-events.csv"
    $sonicSceneStateByteDeltaCsvPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-byte-deltas.csv"
    $sonicSceneStateWordDeltaCsvPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-word-deltas.csv"
    $sonicSceneStateBlobSummaryCsvPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-blob-summary.csv"
    $sonicSceneStateDeltaSummaryPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-delta-summary.csv"
    $sonicSceneStateDeltaReportJsonPath = Join-Path $sonicSceneStateDeltaDirectory "scene-state-delta-report.json"
    if (Test-CsvHasDataRows $sonicSceneStatePath) {
        & (Join-Path $PSScriptRoot "build-sonic-scene-state-delta-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicCopyBracketDirectory = Join-Path $targetRoot "sonic-copy-brackets"
    $sonicCopyBracketCsvPath = Join-Path $sonicCopyBracketDirectory "copy-brackets.csv"
    $sonicFocusPacketCopyBracketCsvPath = Join-Path $sonicCopyBracketDirectory "focus-packet-copy-brackets.csv"
    $sonicCopyBracketSummaryPath = Join-Path $sonicCopyBracketDirectory "copy-bracket-summary.csv"
    $sonicCopyBracketReportJsonPath = Join-Path $sonicCopyBracketDirectory "copy-bracket-report.json"
    if (Test-CsvHasDataRows $copyCsvPath) {
        $copyBracketArgs = @{
            RunDirectory = $targetRoot
        }
        if (($target.PSObject.Properties.Name -contains "copyBracketFocusDrawStart") -and ($target.PSObject.Properties.Name -contains "copyBracketFocusDrawEnd")) {
            $copyBracketArgs.FocusDrawStart = [int]$target.copyBracketFocusDrawStart
            $copyBracketArgs.FocusDrawEnd = [int]$target.copyBracketFocusDrawEnd
        }

        if ((Test-CsvHasDataRows (Join-Path $targetRoot "sonic-packet-timeline.csv")) -or $copyBracketArgs.ContainsKey("FocusDrawStart")) {
            & (Join-Path $PSScriptRoot "build-sonic-copy-bracket-report.ps1") @copyBracketArgs | Out-Null
        }
    }

    $sonicPacketRecurrenceDirectory = Join-Path $targetRoot "sonic-packet-recurrence"
    $sonicPacketRecurrenceEventsCsvPath = Join-Path $sonicPacketRecurrenceDirectory "packet-recurrence-events.csv"
    $sonicPacketRecurrenceStateSummaryPath = Join-Path $sonicPacketRecurrenceDirectory "packet-recurrence-state-summary.csv"
    $sonicPacketRecurrenceReportJsonPath = Join-Path $sonicPacketRecurrenceDirectory "packet-recurrence-report.json"
    if (Test-CsvHasDataRows $sonicFocusPacketEventsCsvPath) {
        & (Join-Path $PSScriptRoot "build-sonic-packet-recurrence-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicDisplayedPacketSetDirectory = Join-Path $targetRoot "sonic-displayed-packet-set"
    $sonicDisplayedDrawMaterialsCsvPath = Join-Path $sonicDisplayedPacketSetDirectory "displayed-draw-materials.csv"
    $sonicDisplayedPacketSetCsvPath = Join-Path $sonicDisplayedPacketSetDirectory "displayed-packet-set.csv"
    $sonicDisplayedTextureRankingCsvPath = Join-Path $sonicDisplayedPacketSetDirectory "displayed-texture-ranking.csv"
    $sonicDisplayedRegionRankingCsvPath = Join-Path $sonicDisplayedPacketSetDirectory "displayed-region-ranking.csv"
    $sonicDisplayedPacketSetReportJsonPath = Join-Path $sonicDisplayedPacketSetDirectory "displayed-packet-set-report.json"
    if ((Test-CsvHasDataRows $gxTriangleCoverageSummaryCsvPath) -and (Test-CsvHasDataRows $gxVertexSummaryPath) -and (Test-CsvHasDataRows (Join-Path $targetRoot "sonic-packet-timeline.csv"))) {
        & (Join-Path $PSScriptRoot "build-sonic-displayed-packet-set-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicPacketTraversalPairDirectory = Join-Path $targetRoot "sonic-packet-traversal-pair"
    $sonicPacketTraversalDispatchCsvPath = Join-Path $sonicPacketTraversalPairDirectory "packet-dispatches.csv"
    $sonicPacketTraversalPairCsvPath = Join-Path $sonicPacketTraversalPairDirectory "packet-pairs.csv"
    $sonicPacketTraversalPairReportJsonPath = Join-Path $sonicPacketTraversalPairDirectory "packet-traversal-pair-report.json"
    if (Test-CsvHasDataRows $sonicPacketSelectionPath) {
        & (Join-Path $PSScriptRoot "build-sonic-packet-traversal-pair-report.ps1") -RunDirectory $targetRoot | Out-Null
    }

    $sonicTraversalSourceReportDirectory = Join-Path $targetRoot "sonic-traversal-source-report"
    $sonicTraversalDirectFocusDispatchesCsvPath = Join-Path $sonicTraversalSourceReportDirectory "direct-focus-dispatches.csv"
    $sonicTraversalSourceDispatchesCsvPath = Join-Path $sonicTraversalSourceReportDirectory "source-dispatches.csv"
    $sonicTraversalSourcePairsCsvPath = Join-Path $sonicTraversalSourceReportDirectory "source-pairs.csv"
    $sonicTraversalFocusReferenceHitsCsvPath = Join-Path $sonicTraversalSourceReportDirectory "focus-reference-hits.csv"
    $sonicTraversalFocusReferenceParentsCsvPath = Join-Path $sonicTraversalSourceReportDirectory "focus-reference-parents.csv"
    $sonicTraversalSourceReportJsonPath = Join-Path $sonicTraversalSourceReportDirectory "traversal-source-report.json"
    if (Test-CsvHasDataRows $sonicTraversalSourcePath) {
        & (Join-Path $PSScriptRoot "build-sonic-traversal-source-report.ps1") -RunDirectory $targetRoot | Out-Null
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
        configuration = $Configuration
        startedAt = (Get-Date).ToString("o")
        maxInstructions = $target.maxInstructions
        status = $status
        exitCode = $effectiveExitCode
        elapsedSeconds = $result.elapsedSeconds
        timeoutSeconds = $target.timeoutSeconds
        deepGx = [bool]$DeepGx
        perfOnly = [bool]$PerfOnly
        gxFrameRequested = $dumpGxFrame
        gxFrameMaxDraws = $gxFrameMaxDraws
        gxFrameSkipDraws = $gxFrameSkipDraws
        gxFrameMaxRasterPixels = $gxFrameMaxRasterPixels
        gxFrameSkipCopyMemoryWrites = ($target.PSObject.Properties.Name -contains "gxFrameSkipCopyMemoryWrites") -and [bool]$target.gxFrameSkipCopyMemoryWrites
        gxCopiesRequested = $dumpGxCopies
        gxCopyEventsRequested = $dumpGxCopyEvents
        gxCopyEventsPath = if (Test-Path -LiteralPath $copyEventCsvPath) { $copyEventCsvPath } else { $null }
        gxDrawsRequested = $dumpGxDraws
        gxDrawsPath = if (Test-Path -LiteralPath $gxDrawsPath) { $gxDrawsPath } else { $null }
        gxCoverageRequested = $dumpGxCoverage
        gxCoveragePath = if (Test-Path -LiteralPath $gxCoveragePath) { $gxCoveragePath } else { $null }
        gxTriangleCoverageRequested = $dumpGxTriangleCoverage
        gxTriangleCoveragePath = if (Test-Path -LiteralPath $gxTriangleCoveragePath) { $gxTriangleCoveragePath } else { $null }
        gxTriangleCoverageSummaryCsvPath = if (Test-Path -LiteralPath $gxTriangleCoverageSummaryCsvPath) { $gxTriangleCoverageSummaryCsvPath } else { $null }
        gxTriangleCoverageSummaryJsonPath = if (Test-Path -LiteralPath $gxTriangleCoverageSummaryJsonPath) { $gxTriangleCoverageSummaryJsonPath } else { $null }
        gxMaterialSummaryCsvPath = if (Test-Path -LiteralPath $gxMaterialSummaryCsvPath) { $gxMaterialSummaryCsvPath } else { $null }
        gxMaterialSummaryJsonPath = if (Test-Path -LiteralPath $gxMaterialSummaryJsonPath) { $gxMaterialSummaryJsonPath } else { $null }
        gxTevSamplesRequested = $dumpGxTevSamples
        gxTevSamplesPath = if (Test-Path -LiteralPath $gxTevSamplesPath) { $gxTevSamplesPath } else { $null }
        gxTexturesRequested = $dumpGxTextures
        gxTexturesPath = if (Test-Path -LiteralPath (Join-Path $gxTexturesPath "index.csv")) { $gxTexturesPath } else { $null }
        gxTextureIndexPath = if (Test-Path -LiteralPath (Join-Path $gxTexturesPath "index.csv")) { Join-Path $gxTexturesPath "index.csv" } else { $null }
        gxTransformsRequested = $dumpGxTransforms
        gxTransformsPath = if (Test-Path -LiteralPath $gxTransformsPath) { $gxTransformsPath } else { $null }
        gxStateTimelineRequested = $dumpGxStateTimeline
        gxStateTimelinePath = if (Test-Path -LiteralPath $gxStateTimelinePath) { $gxStateTimelinePath } else { $null }
        gxVerticesRequested = $dumpGxVertices
        gxVerticesPath = if (Test-Path -LiteralPath $gxVerticesPath) { $gxVerticesPath } else { $null }
        mmioTraceRequested = $traceMmio
        mmioTracePath = if (Test-Path -LiteralPath $mmioTracePath) { $mmioTracePath } else { $null }
        viDisplayTimelineReportJsonPath = if (Test-Path -LiteralPath $viDisplayTimelineReportJsonPath) { $viDisplayTimelineReportJsonPath } else { $null }
        viDisplayCopyJoinCsvPath = if (Test-Path -LiteralPath $viDisplayCopyJoinCsvPath) { $viDisplayCopyJoinCsvPath } else { $null }
        viRegisterWritesCsvPath = if (Test-Path -LiteralPath $viRegisterWritesCsvPath) { $viRegisterWritesCsvPath } else { $null }
        viSelectedFrameCopyJoinCsvPath = if (Test-Path -LiteralPath $viSelectedFrameCopyJoinCsvPath) { $viSelectedFrameCopyJoinCsvPath } else { $null }
        sonicVertexProvenanceRequested = $traceSonicVertexProvenance
        sonicVertexProvenancePath = if (Test-Path -LiteralPath $sonicVertexProvenancePath) { $sonicVertexProvenancePath } else { $null }
        sonicSceneStateRequested = $traceSonicSceneState
        sonicSceneStatePath = if (Test-Path -LiteralPath $sonicSceneStatePath) { $sonicSceneStatePath } else { $null }
        sonicMatrixWriterRequested = $traceSonicMatrixWriter
        sonicMatrixWriterPath = if (Test-Path -LiteralPath $sonicMatrixWriterPath) { $sonicMatrixWriterPath } else { $null }
        sonicPacketSelectionRequested = $traceSonicPacketSelection
        sonicPacketSelectionPath = if (Test-Path -LiteralPath $sonicPacketSelectionPath) { $sonicPacketSelectionPath } else { $null }
        sonicTraversalSourceRequested = $traceSonicTraversalSource
        sonicTraversalSourcePath = if (Test-Path -LiteralPath $sonicTraversalSourcePath) { $sonicTraversalSourcePath } else { $null }
        sonicTransformInputsRequested = $traceSonicTransformInputs
        sonicTransformInputsPath = if (Test-Path -LiteralPath $sonicTransformInputsPath) { $sonicTransformInputsPath } else { $null }
        sonicTransformSourceMapPath = if (Test-Path -LiteralPath $sonicTransformSourceMapPath) { $sonicTransformSourceMapPath } else { $null }
        sonicTransformAffineFitPath = if (Test-Path -LiteralPath (Join-Path $targetRoot "sonic-transform-affine-fit.json")) { Join-Path $targetRoot "sonic-transform-affine-fit.json" } else { $null }
        sonicTransformAffineGroupFitPath = if (Test-Path -LiteralPath (Join-Path $targetRoot "sonic-transform-affine-fit-groups.json")) { Join-Path $targetRoot "sonic-transform-affine-fit-groups.json" } else { $null }
        sonicFocusMatrixProvenanceCsvPath = if (Test-Path -LiteralPath $sonicFocusMatrixProvenanceCsvPath) { $sonicFocusMatrixProvenanceCsvPath } else { $null }
        sonicFocusMatrixSourceSummaryPath = if (Test-Path -LiteralPath $sonicFocusMatrixSourceSummaryPath) { $sonicFocusMatrixSourceSummaryPath } else { $null }
        sonicFocusMatrixProvenanceReportJsonPath = if (Test-Path -LiteralPath $sonicFocusMatrixProvenanceReportJsonPath) { $sonicFocusMatrixProvenanceReportJsonPath } else { $null }
        sonicPacketTimelinePath = if (Test-Path -LiteralPath (Join-Path $targetRoot "sonic-packet-timeline.csv")) { Join-Path $targetRoot "sonic-packet-timeline.csv" } else { $null }
        sonicPacketTimelineJsonPath = if (Test-Path -LiteralPath (Join-Path $targetRoot "sonic-packet-timeline.json")) { Join-Path $targetRoot "sonic-packet-timeline.json" } else { $null }
        sonicMaterialSourceSummaryPath = if (Test-Path -LiteralPath $sonicMaterialSourceSummaryPath) { $sonicMaterialSourceSummaryPath } else { $null }
        sonicMaterialSourceOverlapPath = if (Test-Path -LiteralPath $sonicMaterialSourceOverlapPath) { $sonicMaterialSourceOverlapPath } else { $null }
        sonicMaterialSourceReportJsonPath = if (Test-Path -LiteralPath $sonicMaterialSourceReportJsonPath) { $sonicMaterialSourceReportJsonPath } else { $null }
        sonicPacketMaterialPartitionCsvPath = if (Test-Path -LiteralPath $sonicPacketMaterialPartitionCsvPath) { $sonicPacketMaterialPartitionCsvPath } else { $null }
        sonicPacketMaterialSequenceCsvPath = if (Test-Path -LiteralPath $sonicPacketMaterialSequenceCsvPath) { $sonicPacketMaterialSequenceCsvPath } else { $null }
        sonicPacketMaterialPartitionJsonPath = if (Test-Path -LiteralPath $sonicPacketMaterialPartitionJsonPath) { $sonicPacketMaterialPartitionJsonPath } else { $null }
        sonicNonrenderedStripDrawCsvPath = if (Test-Path -LiteralPath $sonicNonrenderedStripDrawCsvPath) { $sonicNonrenderedStripDrawCsvPath } else { $null }
        sonicNonrenderedStripSequenceCsvPath = if (Test-Path -LiteralPath $sonicNonrenderedStripSequenceCsvPath) { $sonicNonrenderedStripSequenceCsvPath } else { $null }
        sonicNonrenderedStripJsonPath = if (Test-Path -LiteralPath $sonicNonrenderedStripJsonPath) { $sonicNonrenderedStripJsonPath } else { $null }
        sonicPacketPlacementOverlayPath = if (Test-Path -LiteralPath $sonicPacketPlacementOverlayPath) { $sonicPacketPlacementOverlayPath } else { $null }
        sonicPacketPlacementContactSheetPath = if (Test-Path -LiteralPath $sonicPacketPlacementContactSheetPath) { $sonicPacketPlacementContactSheetPath } else { $null }
        sonicPacketPlacementBoundsPath = if (Test-Path -LiteralPath $sonicPacketPlacementBoundsPath) { $sonicPacketPlacementBoundsPath } else { $null }
        sonicPacketPlacementReportJsonPath = if (Test-Path -LiteralPath $sonicPacketPlacementReportJsonPath) { $sonicPacketPlacementReportJsonPath } else { $null }
        sonicPacketPlacementComparisonCsvPath = if (Test-Path -LiteralPath $sonicPacketPlacementComparisonCsvPath) { $sonicPacketPlacementComparisonCsvPath } else { $null }
        sonicPacketPlacementEventsCsvPath = if (Test-Path -LiteralPath $sonicPacketPlacementEventsCsvPath) { $sonicPacketPlacementEventsCsvPath } else { $null }
        sonicFocusPacketEventsCsvPath = if (Test-Path -LiteralPath $sonicFocusPacketEventsCsvPath) { $sonicFocusPacketEventsCsvPath } else { $null }
        sonicPacketRadiusRankingCsvPath = if (Test-Path -LiteralPath $sonicPacketRadiusRankingCsvPath) { $sonicPacketRadiusRankingCsvPath } else { $null }
        sonicPacketPlacementComparisonSummaryPath = if (Test-Path -LiteralPath $sonicPacketPlacementComparisonSummaryPath) { $sonicPacketPlacementComparisonSummaryPath } else { $null }
        sonicPacketPlacementComparisonReportJsonPath = if (Test-Path -LiteralPath $sonicPacketPlacementComparisonReportJsonPath) { $sonicPacketPlacementComparisonReportJsonPath } else { $null }
        sonicFocusPacketPhaseEventsCsvPath = if (Test-Path -LiteralPath $sonicFocusPacketPhaseEventsCsvPath) { $sonicFocusPacketPhaseEventsCsvPath } else { $null }
        sonicFocusPacketPhaseSummaryPath = if (Test-Path -LiteralPath $sonicFocusPacketPhaseSummaryPath) { $sonicFocusPacketPhaseSummaryPath } else { $null }
        sonicFocusPacketPhaseReportJsonPath = if (Test-Path -LiteralPath $sonicFocusPacketPhaseReportJsonPath) { $sonicFocusPacketPhaseReportJsonPath } else { $null }
        sonicSceneStateScalarDeltaCsvPath = if (Test-Path -LiteralPath $sonicSceneStateScalarDeltaCsvPath) { $sonicSceneStateScalarDeltaCsvPath } else { $null }
        sonicSceneStateEventCsvPath = if (Test-Path -LiteralPath $sonicSceneStateEventCsvPath) { $sonicSceneStateEventCsvPath } else { $null }
        sonicSceneStateByteDeltaCsvPath = if (Test-Path -LiteralPath $sonicSceneStateByteDeltaCsvPath) { $sonicSceneStateByteDeltaCsvPath } else { $null }
        sonicSceneStateWordDeltaCsvPath = if (Test-Path -LiteralPath $sonicSceneStateWordDeltaCsvPath) { $sonicSceneStateWordDeltaCsvPath } else { $null }
        sonicSceneStateBlobSummaryCsvPath = if (Test-Path -LiteralPath $sonicSceneStateBlobSummaryCsvPath) { $sonicSceneStateBlobSummaryCsvPath } else { $null }
        sonicSceneStateDeltaSummaryPath = if (Test-Path -LiteralPath $sonicSceneStateDeltaSummaryPath) { $sonicSceneStateDeltaSummaryPath } else { $null }
        sonicSceneStateDeltaReportJsonPath = if (Test-Path -LiteralPath $sonicSceneStateDeltaReportJsonPath) { $sonicSceneStateDeltaReportJsonPath } else { $null }
        sonicCopyBracketCsvPath = if (Test-Path -LiteralPath $sonicCopyBracketCsvPath) { $sonicCopyBracketCsvPath } else { $null }
        sonicFocusPacketCopyBracketCsvPath = if (Test-Path -LiteralPath $sonicFocusPacketCopyBracketCsvPath) { $sonicFocusPacketCopyBracketCsvPath } else { $null }
        sonicCopyBracketSummaryPath = if (Test-Path -LiteralPath $sonicCopyBracketSummaryPath) { $sonicCopyBracketSummaryPath } else { $null }
        sonicCopyBracketReportJsonPath = if (Test-Path -LiteralPath $sonicCopyBracketReportJsonPath) { $sonicCopyBracketReportJsonPath } else { $null }
        sonicPacketRecurrenceEventsCsvPath = if (Test-Path -LiteralPath $sonicPacketRecurrenceEventsCsvPath) { $sonicPacketRecurrenceEventsCsvPath } else { $null }
        sonicPacketRecurrenceStateSummaryPath = if (Test-Path -LiteralPath $sonicPacketRecurrenceStateSummaryPath) { $sonicPacketRecurrenceStateSummaryPath } else { $null }
        sonicPacketRecurrenceReportJsonPath = if (Test-Path -LiteralPath $sonicPacketRecurrenceReportJsonPath) { $sonicPacketRecurrenceReportJsonPath } else { $null }
        sonicDisplayedDrawMaterialsCsvPath = if (Test-Path -LiteralPath $sonicDisplayedDrawMaterialsCsvPath) { $sonicDisplayedDrawMaterialsCsvPath } else { $null }
        sonicDisplayedPacketSetCsvPath = if (Test-Path -LiteralPath $sonicDisplayedPacketSetCsvPath) { $sonicDisplayedPacketSetCsvPath } else { $null }
        sonicDisplayedTextureRankingCsvPath = if (Test-Path -LiteralPath $sonicDisplayedTextureRankingCsvPath) { $sonicDisplayedTextureRankingCsvPath } else { $null }
        sonicDisplayedRegionRankingCsvPath = if (Test-Path -LiteralPath $sonicDisplayedRegionRankingCsvPath) { $sonicDisplayedRegionRankingCsvPath } else { $null }
        sonicDisplayedPacketSetReportJsonPath = if (Test-Path -LiteralPath $sonicDisplayedPacketSetReportJsonPath) { $sonicDisplayedPacketSetReportJsonPath } else { $null }
        sonicPacketTraversalDispatchCsvPath = if (Test-Path -LiteralPath $sonicPacketTraversalDispatchCsvPath) { $sonicPacketTraversalDispatchCsvPath } else { $null }
        sonicPacketTraversalPairCsvPath = if (Test-Path -LiteralPath $sonicPacketTraversalPairCsvPath) { $sonicPacketTraversalPairCsvPath } else { $null }
        sonicPacketTraversalPairReportJsonPath = if (Test-Path -LiteralPath $sonicPacketTraversalPairReportJsonPath) { $sonicPacketTraversalPairReportJsonPath } else { $null }
        sonicTraversalDirectFocusDispatchesCsvPath = if (Test-Path -LiteralPath $sonicTraversalDirectFocusDispatchesCsvPath) { $sonicTraversalDirectFocusDispatchesCsvPath } else { $null }
        sonicTraversalSourceDispatchesCsvPath = if (Test-Path -LiteralPath $sonicTraversalSourceDispatchesCsvPath) { $sonicTraversalSourceDispatchesCsvPath } else { $null }
        sonicTraversalSourcePairsCsvPath = if (Test-Path -LiteralPath $sonicTraversalSourcePairsCsvPath) { $sonicTraversalSourcePairsCsvPath } else { $null }
        sonicTraversalFocusReferenceHitsCsvPath = if (Test-Path -LiteralPath $sonicTraversalFocusReferenceHitsCsvPath) { $sonicTraversalFocusReferenceHitsCsvPath } else { $null }
        sonicTraversalFocusReferenceParentsCsvPath = if (Test-Path -LiteralPath $sonicTraversalFocusReferenceParentsCsvPath) { $sonicTraversalFocusReferenceParentsCsvPath } else { $null }
        sonicTraversalSourceReportJsonPath = if (Test-Path -LiteralPath $sonicTraversalSourceReportJsonPath) { $sonicTraversalSourceReportJsonPath } else { $null }
        emulatorSummary = $emulatorSummary
        frame = $frameSummary
        exiTraceRequested = $traceExi
        exiSummary = $exiSummary
        gxCopySummary = $gxSummary
        gxCoverageSummary = $gxCoverageSummary
        gxTriangleCoverageSummary = $gxTriangleCoverageSummary
        gxDisplayActivityCsvPath = if (Test-Path -LiteralPath $gxTimelineCsvPath) { $gxTimelineCsvPath } else { $null }
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
    $timings = if ($null -ne $emulatorSummary) { $emulatorSummary.timings } else { $null }
    $totalMs = if ($null -ne $timings) { $timings.totalMs } else { "" }
    $emulationMs = if ($null -ne $timings) { $timings.emulationMs } else { "" }
    $postEmulationMs = if ($null -ne $timings) { $timings.postEmulationMs } else { "" }
    $measuredDiagnosticsMs = if ($null -ne $timings) { $timings.measuredDiagnosticsMs } else { "" }
    $gxFrameDumpMs = if ($null -ne $timings) { $timings.gxFrameDumpMs } else { "" }
    $gxCopyDumpMs = if ($null -ne $timings) { $timings.gxCopyDumpMs } else { "" }
    $gxCoverageDumpMs = if ($null -ne $timings) { $timings.gxCoverageDumpMs } else { "" }
    $gxTevSampleDumpMs = if ($null -ne $timings) { $timings.gxTevSampleDumpMs } else { "" }
    $gxTextureDumpMs = if ($null -ne $timings) { $timings.gxTextureDumpMs } else { "" }
    $processMips = Format-OptionalRate $executedInstructions $result.elapsedSeconds
    $emulationSeconds = if ($null -ne (Convert-OptionalDouble $emulationMs)) { (Convert-OptionalDouble $emulationMs) / 1000.0 } else { $null }
    $emulationMips = Format-OptionalRate $executedInstructions $emulationSeconds
    $diagnosticsShare = ""
    $totalMsValue = Convert-OptionalDouble $totalMs
    $diagnosticsMsValue = Convert-OptionalDouble $measuredDiagnosticsMs
    if ($null -ne $totalMsValue -and $totalMsValue -gt 0 -and $null -ne $diagnosticsMsValue) {
        $diagnosticsShare = [Math]::Round($diagnosticsMsValue / $totalMsValue, 4)
    }

    $stdoutBytes = if (Test-Path -LiteralPath $stdoutPath) { (Get-Item -LiteralPath $stdoutPath).Length } else { 0 }
    $stderrBytes = if (Test-Path -LiteralPath $stderrPath) { (Get-Item -LiteralPath $stderrPath).Length } else { 0 }
    $gxFrameTimings = if ($null -ne $emulatorSummary -and $null -ne $emulatorSummary.gx.frameDump) { $emulatorSummary.gx.frameDump.timings } else { $null }
    $gxFrameDump = if ($null -ne $emulatorSummary -and $null -ne $emulatorSummary.gx.frameDump) { $emulatorSummary.gx.frameDump } else { $null }
    $gxFrameLifecycle = if ($null -ne $gxFrameDump) { $gxFrameDump.lifecycle } else { $null }
    $gxFrameSource = if ($null -ne $gxFrameDump) { $gxFrameDump.source } else { "" }
    $gxFrameSourceAddress = if ($null -ne $gxFrameDump) { $gxFrameDump.sourceAddress } else { "" }
    $gxFrameSourceCopyIndex = if ($null -ne $gxFrameDump) { $gxFrameDump.sourceCopyIndex } else { "" }
    $gxFrameLifecyclePhase = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.phase } else { "" }
    $gxFrameRequestedSource = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.requestedSource } else { "" }
    $gxFrameSelectedSource = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.selectedSource } else { "" }
    $gxFrameCapturedCurrentEfb = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.capturedCurrentEfb } else { "" }
    $gxFrameLastDisplayCopyIndex = if ($null -ne $gxFrameLifecycle -and $null -ne $gxFrameLifecycle.lastDisplayCopy) { $gxFrameLifecycle.lastDisplayCopy.copyIndex } else { "" }
    $gxFrameLastDisplayDrawsSeen = if ($null -ne $gxFrameLifecycle -and $null -ne $gxFrameLifecycle.lastDisplayCopy) { $gxFrameLifecycle.lastDisplayCopy.drawsSeen } else { "" }
    $gxFrameLastDisplayFifoOffset = if ($null -ne $gxFrameLifecycle -and $null -ne $gxFrameLifecycle.lastDisplayCopy) { $gxFrameLifecycle.lastDisplayCopy.fifoOffset } else { "" }
    $gxFrameDrawsSinceLastDisplayCopy = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.drawsSinceLastDisplayCopy } else { "" }
    $gxFrameCopyEventsSinceLastDisplayCopy = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.copyEventsSinceLastDisplayCopy } else { "" }
    $gxFrameClearsSinceLastDisplayCopy = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.clearsSinceLastDisplayCopy } else { "" }
    $gxFrameTextureCopiesSinceLastDisplayCopy = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.textureCopiesSinceLastDisplayCopy } else { "" }
    $gxFrameEfbWasClearedAfterLastDisplayCopy = if ($null -ne $gxFrameLifecycle) { $gxFrameLifecycle.efbWasClearedAfterLastDisplayCopy } else { "" }
    $gxFrameReplayMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.replayMs } else { "" }
    $gxFrameVertexDecodeMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.vertexDecodeMs } else { "" }
    $gxFrameRasterizeMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.rasterizeMs } else { "" }
    $gxFrameRasterTevTextureMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.rasterTevTextureMs } else { "" }
    $gxFrameRasterBlendWriteMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.rasterBlendWriteMs } else { "" }
    $gxFrameEfbCopyMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.efbCopyMs } else { "" }
    $gxFramePngWriteMs = if ($null -ne $gxFrameTimings) { $gxFrameTimings.pngWriteMs } else { "" }
    $fastForwardSummary = if ($null -ne $emulatorSummary) { $emulatorSummary.fastForward } else { $null }
    $sonicPathLookupInstructions = if ($null -ne $fastForwardSummary) { $fastForwardSummary.sonicPathLookupInstructions } else { "" }
    $sonicPathRecordScanInstructions = if ($null -ne $fastForwardSummary) { $fastForwardSummary.sonicPathRecordScanInstructions } else { "" }
    $sonicPoolSlotScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicPoolSlotScanInstructions") { $fastForwardSummary.sonicPoolSlotScanInstructions } else { "" }
    $sonicTableKeyScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicTableKeyScanInstructions") { $fastForwardSummary.sonicTableKeyScanInstructions } else { "" }
    $sonicModeRefreshDispatchInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicModeRefreshDispatchInstructions") { $fastForwardSummary.sonicModeRefreshDispatchInstructions } else { "" }
    $sonicStatusCallerLoopInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicStatusCallerLoopInstructions") { $fastForwardSummary.sonicStatusCallerLoopInstructions } else { "" }
    $sonicStatusCallerDispatchInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicStatusCallerDispatchInstructions") { $fastForwardSummary.sonicStatusCallerDispatchInstructions } else { "" }
    $sonicTableByteBuildDispatchInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicTableByteBuildDispatchInstructions") { $fastForwardSummary.sonicTableByteBuildDispatchInstructions } else { "" }
    $sonicLineCopyInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicLineCopyInstructions") { $fastForwardSummary.sonicLineCopyInstructions } else { "" }
    $sonicLineSkipInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicLineSkipInstructions") { $fastForwardSummary.sonicLineSkipInstructions } else { "" }
    $sonicStringAppendScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicStringAppendScanInstructions") { $fastForwardSummary.sonicStringAppendScanInstructions } else { "" }
    $sonicFreeBlockScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicFreeBlockScanInstructions") { $fastForwardSummary.sonicFreeBlockScanInstructions } else { "" }
    $sonicCacheStoreSweepInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicCacheStoreSweepInstructions") { $fastForwardSummary.sonicCacheStoreSweepInstructions } else { "" }
    $sonicStateZeroFillInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicStateZeroFillInstructions") { $fastForwardSummary.sonicStateZeroFillInstructions } else { "" }
    $sonicManagerSlotScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicManagerSlotScanInstructions") { $fastForwardSummary.sonicManagerSlotScanInstructions } else { "" }
    $sonicTaskEntryScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicTaskEntryScanInstructions") { $fastForwardSummary.sonicTaskEntryScanInstructions } else { "" }
    $sonicObjectSlotScanInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicObjectSlotScanInstructions") { $fastForwardSummary.sonicObjectSlotScanInstructions } else { "" }
    $sonicHalfwordChecksumInstructions = if ($null -ne $fastForwardSummary -and $fastForwardSummary.PSObject.Properties.Name -contains "sonicHalfwordChecksumInstructions") { $fastForwardSummary.sonicHalfwordChecksumInstructions } else { "" }
    $sonicResourceLookupInstructions = if ($null -ne $fastForwardSummary) { $fastForwardSummary.resourceLookupInstructions } else { "" }
    $sonicPrsDecompressInstructions = if ($null -ne $fastForwardSummary) { $fastForwardSummary.prsDecompressInstructions } else { "" }
    $pcProfile = Get-PreferredPcProfile $emulatorSummary
    $pcProfileUniqueAddresses = if ($null -ne $pcProfile) { $pcProfile.uniqueAddresses } else { "" }
    $pcProfileTotalSamples = if ($null -ne $pcProfile) { $pcProfile.totalSamples } else { "" }
    $pcProfileExcludedSamples = if ($null -ne $pcProfile -and $pcProfile.PSObject.Properties.Name -contains "excludedSamples") { $pcProfile.excludedSamples } else { "" }
    $pcProfileHead = Format-PcProfileHead $pcProfile 5
    $displayActivityRuns = if ($null -ne $gxSummary -and $null -ne $gxSummary.displayActivity) { @($gxSummary.displayActivity).Count } else { "" }
    $lastDisplayActivityState = if ($null -ne $gxSummary -and $null -ne $gxSummary.displayActivity -and @($gxSummary.displayActivity).Count -gt 0) { @($gxSummary.displayActivity)[-1].state } else { "" }
    $lastDisplayActivityCopyCount = if ($null -ne $gxSummary -and $null -ne $gxSummary.displayActivity -and @($gxSummary.displayActivity).Count -gt 0) { @($gxSummary.displayActivity)[-1].copyCount } else { "" }
    $triangleCoverageRows = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.rows } else { "" }
    $triangleCoverageRenderedRows = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.renderedRows } else { "" }
    $triangleCoverageTotalCoveredPixels = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.totalCoveredPixels } else { "" }
    $triangleCoverageTotalColorWrites = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.totalColorWrites } else { "" }
    $triangleCoverageTotalBlackWrites = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.totalBlackColorWrites } else { "" }
    $triangleCoverageBlackWriteRatio = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.blackWriteRatio } else { "" }
    $triangleCoverageDarkRenderedRows = if ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.darkRenderedRows } else { "" }
    $triangleCoverageTopTexture = if ($null -ne $gxTriangleCoverageSummary -and $null -ne $gxTriangleCoverageSummary.textureGroups -and @($gxTriangleCoverageSummary.textureGroups).Count -gt 0) { @($gxTriangleCoverageSummary.textureGroups)[0].texture } else { "" }
    $coverageRows = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.rows } elseif ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.rows } else { "" }
    $coverageNonblackRows = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.nonblackRows } elseif ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.renderedRows } else { "" }
    $coverageMaxAfterNonblack = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.maxAfterNonblack } else { "" }
    $coverageMaxAfterNonblackDraw = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.maxAfterNonblackDraw } else { "" }
    $coverageOverallBounds = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.overallBounds } else { "" }
    $coverageRasterExhausted = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.rasterExhausted } else { "" }
    $coverageRasterAfterLast = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.rasterAfterLast } else { "" }
    $coverageTotalColorWrites = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalColorWrites } elseif ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.totalColorWrites } else { "" }
    $coverageTotalBlackWrites = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalBlackWrites } elseif ($null -ne $gxTriangleCoverageSummary) { $gxTriangleCoverageSummary.totalBlackColorWrites } else { "" }
    $coverageTotalAlphaRejected = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalAlphaRejected } else { "" }
    $coverageTotalDegenerateTriangles = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalDegenerateTriangles } else { "" }
    $coverageTotalClippedVertices = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalClippedVertices } else { "" }
    $coverageMaxClippedVertices = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.maxClippedVertices } else { "" }
    $coverageMaxClippedVerticesDraw = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.maxClippedVerticesDraw } else { "" }
    $coverageTotalClipInputTriangles = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalClipInputTriangles } else { "" }
    $coverageTotalNearClipOutputTriangles = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalNearClipOutputTriangles } else { "" }
    $coverageTotalNearClipCulledTriangles = if ($null -ne $gxCoverageSummary) { $gxCoverageSummary.totalNearClipCulledTriangles } else { "" }
    $sonicPacketTimelineCsvPath = Join-Path $targetRoot "sonic-packet-timeline.csv"
    $sonicPacketTimelineJsonPath = Join-Path $targetRoot "sonic-packet-timeline.json"
    $performanceRows.Add([pscustomobject]@{
        target = $target.slug
        game = $target.game
        status = $status
        configuration = $Configuration
        perfOnly = [bool]$PerfOnly
        elapsedSeconds = $result.elapsedSeconds
        processMips = $processMips
        executedInstructions = $executedInstructions
        maxInstructions = $target.maxInstructions
        stopReason = $stopReason
        finalPc = $finalPc
        totalMs = $totalMs
        emulationMs = $emulationMs
        emulationMips = $emulationMips
        postEmulationMs = $postEmulationMs
        measuredDiagnosticsMs = $measuredDiagnosticsMs
        diagnosticsShare = $diagnosticsShare
        stdoutBytes = $stdoutBytes
        stderrBytes = $stderrBytes
        pcProfileUniqueAddresses = $pcProfileUniqueAddresses
        pcProfileTotalSamples = $pcProfileTotalSamples
        pcProfileExcludedSamples = $pcProfileExcludedSamples
        pcProfileHead = $pcProfileHead
        gxFrameRequested = $dumpGxFrame
        gxCopiesRequested = $dumpGxCopies
        gxCoverageRequested = $dumpGxCoverage
        gxTriangleCoverageRequested = $dumpGxTriangleCoverage
        gxTevSamplesRequested = $dumpGxTevSamples
        gxTexturesRequested = $dumpGxTextures
        exiTraceRequested = $traceExi
        runJson = $runJsonPath
    })

    $summaryRows.Add([pscustomobject]@{
        target = $target.slug
        status = $status
        configuration = $Configuration
        perfOnly = [bool]$PerfOnly
        elapsedSeconds = $result.elapsedSeconds
        processMips = $processMips
        maxInstructions = $target.maxInstructions
        executedInstructions = $executedInstructions
        stopReason = $stopReason
        finalPc = $finalPc
        totalMs = $totalMs
        emulationMs = $emulationMs
        emulationMips = $emulationMips
        postEmulationMs = $postEmulationMs
        measuredDiagnosticsMs = $measuredDiagnosticsMs
        diagnosticsShare = $diagnosticsShare
        stdoutBytes = $stdoutBytes
        stderrBytes = $stderrBytes
        pcProfileUniqueAddresses = $pcProfileUniqueAddresses
        pcProfileTotalSamples = $pcProfileTotalSamples
        pcProfileExcludedSamples = $pcProfileExcludedSamples
        pcProfileHead = $pcProfileHead
        gxFrameDumpMs = $gxFrameDumpMs
        gxFrameSource = $gxFrameSource
        gxFrameSourceAddress = $gxFrameSourceAddress
        gxFrameSourceCopyIndex = $gxFrameSourceCopyIndex
        gxFrameLifecyclePhase = $gxFrameLifecyclePhase
        gxFrameRequestedSource = $gxFrameRequestedSource
        gxFrameSelectedSource = $gxFrameSelectedSource
        gxFrameCapturedCurrentEfb = $gxFrameCapturedCurrentEfb
        gxFrameLastDisplayCopyIndex = $gxFrameLastDisplayCopyIndex
        gxFrameLastDisplayDrawsSeen = $gxFrameLastDisplayDrawsSeen
        gxFrameLastDisplayFifoOffset = $gxFrameLastDisplayFifoOffset
        gxFrameDrawsSinceLastDisplayCopy = $gxFrameDrawsSinceLastDisplayCopy
        gxFrameCopyEventsSinceLastDisplayCopy = $gxFrameCopyEventsSinceLastDisplayCopy
        gxFrameClearsSinceLastDisplayCopy = $gxFrameClearsSinceLastDisplayCopy
        gxFrameTextureCopiesSinceLastDisplayCopy = $gxFrameTextureCopiesSinceLastDisplayCopy
        gxFrameEfbWasClearedAfterLastDisplayCopy = $gxFrameEfbWasClearedAfterLastDisplayCopy
        gxCopyDumpMs = $gxCopyDumpMs
        gxCoverageDumpMs = $gxCoverageDumpMs
        gxTevSampleDumpMs = $gxTevSampleDumpMs
        gxTextureDumpMs = $gxTextureDumpMs
        gxFrameReplayMs = $gxFrameReplayMs
        gxFrameVertexDecodeMs = $gxFrameVertexDecodeMs
        gxFrameRasterizeMs = $gxFrameRasterizeMs
        gxFrameRasterTevTextureMs = $gxFrameRasterTevTextureMs
        gxFrameRasterBlendWriteMs = $gxFrameRasterBlendWriteMs
        gxFrameEfbCopyMs = $gxFrameEfbCopyMs
        gxFramePngWriteMs = $gxFramePngWriteMs
        frameBytes = $frameBytes
        exiReadArrayCommands = $readArrayCount
        nonblackDisplayCopies = $nonblackCopies
        sonicPathLookupInstructions = $sonicPathLookupInstructions
        sonicPathRecordScanInstructions = $sonicPathRecordScanInstructions
        sonicPoolSlotScanInstructions = $sonicPoolSlotScanInstructions
        sonicTableKeyScanInstructions = $sonicTableKeyScanInstructions
        sonicModeRefreshDispatchInstructions = $sonicModeRefreshDispatchInstructions
        sonicStatusCallerLoopInstructions = $sonicStatusCallerLoopInstructions
        sonicStatusCallerDispatchInstructions = $sonicStatusCallerDispatchInstructions
        sonicTableByteBuildDispatchInstructions = $sonicTableByteBuildDispatchInstructions
        sonicLineCopyInstructions = $sonicLineCopyInstructions
        sonicLineSkipInstructions = $sonicLineSkipInstructions
        sonicStringAppendScanInstructions = $sonicStringAppendScanInstructions
        sonicFreeBlockScanInstructions = $sonicFreeBlockScanInstructions
        sonicCacheStoreSweepInstructions = $sonicCacheStoreSweepInstructions
        sonicStateZeroFillInstructions = $sonicStateZeroFillInstructions
        sonicManagerSlotScanInstructions = $sonicManagerSlotScanInstructions
        sonicTaskEntryScanInstructions = $sonicTaskEntryScanInstructions
        sonicObjectSlotScanInstructions = $sonicObjectSlotScanInstructions
        sonicHalfwordChecksumInstructions = $sonicHalfwordChecksumInstructions
        sonicResourceLookupInstructions = $sonicResourceLookupInstructions
        sonicPrsDecompressInstructions = $sonicPrsDecompressInstructions
        displayActivityRuns = $displayActivityRuns
        lastDisplayActivityState = $lastDisplayActivityState
        lastDisplayActivityCopyCount = $lastDisplayActivityCopyCount
        coverageRows = $coverageRows
        coverageNonblackRows = $coverageNonblackRows
        coverageMaxAfterNonblack = $coverageMaxAfterNonblack
        coverageMaxAfterNonblackDraw = $coverageMaxAfterNonblackDraw
        coverageOverallBounds = $coverageOverallBounds
        coverageRasterExhausted = $coverageRasterExhausted
        coverageRasterAfterLast = $coverageRasterAfterLast
        coverageTotalColorWrites = $coverageTotalColorWrites
        coverageTotalBlackWrites = $coverageTotalBlackWrites
        coverageTotalAlphaRejected = $coverageTotalAlphaRejected
        coverageTotalDegenerateTriangles = $coverageTotalDegenerateTriangles
        coverageTotalClippedVertices = $coverageTotalClippedVertices
        coverageMaxClippedVertices = $coverageMaxClippedVertices
        coverageMaxClippedVerticesDraw = $coverageMaxClippedVerticesDraw
        coverageTotalClipInputTriangles = $coverageTotalClipInputTriangles
        coverageTotalNearClipOutputTriangles = $coverageTotalNearClipOutputTriangles
        coverageTotalNearClipCulledTriangles = $coverageTotalNearClipCulledTriangles
        triangleCoverageRows = $triangleCoverageRows
        triangleCoverageRenderedRows = $triangleCoverageRenderedRows
        triangleCoverageTotalCoveredPixels = $triangleCoverageTotalCoveredPixels
        triangleCoverageTotalColorWrites = $triangleCoverageTotalColorWrites
        triangleCoverageTotalBlackWrites = $triangleCoverageTotalBlackWrites
        triangleCoverageBlackWriteRatio = $triangleCoverageBlackWriteRatio
        triangleCoverageDarkRenderedRows = $triangleCoverageDarkRenderedRows
        triangleCoverageTopTexture = $triangleCoverageTopTexture
        sonicPacketTimelinePath = if (Test-Path -LiteralPath $sonicPacketTimelineCsvPath) { $sonicPacketTimelineCsvPath } else { "" }
        sonicPacketTimelineJsonPath = if (Test-Path -LiteralPath $sonicPacketTimelineJsonPath) { $sonicPacketTimelineJsonPath } else { "" }
        sonicMaterialSourceSummaryPath = if (Test-Path -LiteralPath $sonicMaterialSourceSummaryPath) { $sonicMaterialSourceSummaryPath } else { "" }
        sonicMaterialSourceOverlapPath = if (Test-Path -LiteralPath $sonicMaterialSourceOverlapPath) { $sonicMaterialSourceOverlapPath } else { "" }
        sonicMaterialSourceReportJsonPath = if (Test-Path -LiteralPath $sonicMaterialSourceReportJsonPath) { $sonicMaterialSourceReportJsonPath } else { "" }
        sonicPacketMaterialPartitionCsvPath = if (Test-Path -LiteralPath $sonicPacketMaterialPartitionCsvPath) { $sonicPacketMaterialPartitionCsvPath } else { "" }
        sonicPacketMaterialSequenceCsvPath = if (Test-Path -LiteralPath $sonicPacketMaterialSequenceCsvPath) { $sonicPacketMaterialSequenceCsvPath } else { "" }
        sonicPacketMaterialPartitionJsonPath = if (Test-Path -LiteralPath $sonicPacketMaterialPartitionJsonPath) { $sonicPacketMaterialPartitionJsonPath } else { "" }
        sonicNonrenderedStripDrawCsvPath = if (Test-Path -LiteralPath $sonicNonrenderedStripDrawCsvPath) { $sonicNonrenderedStripDrawCsvPath } else { "" }
        sonicNonrenderedStripSequenceCsvPath = if (Test-Path -LiteralPath $sonicNonrenderedStripSequenceCsvPath) { $sonicNonrenderedStripSequenceCsvPath } else { "" }
        sonicNonrenderedStripJsonPath = if (Test-Path -LiteralPath $sonicNonrenderedStripJsonPath) { $sonicNonrenderedStripJsonPath } else { "" }
        sonicPacketPlacementOverlayPath = if (Test-Path -LiteralPath $sonicPacketPlacementOverlayPath) { $sonicPacketPlacementOverlayPath } else { "" }
        sonicPacketPlacementContactSheetPath = if (Test-Path -LiteralPath $sonicPacketPlacementContactSheetPath) { $sonicPacketPlacementContactSheetPath } else { "" }
        sonicPacketPlacementBoundsPath = if (Test-Path -LiteralPath $sonicPacketPlacementBoundsPath) { $sonicPacketPlacementBoundsPath } else { "" }
        sonicPacketPlacementReportJsonPath = if (Test-Path -LiteralPath $sonicPacketPlacementReportJsonPath) { $sonicPacketPlacementReportJsonPath } else { "" }
        runJson = $runJsonPath
    })
}

$summaryCsvPath = Join-Path $runRoot "summary.csv"
$summaryJsonPath = Join-Path $runRoot "summary.json"
$performanceCsvPath = Join-Path $runRoot "performance.csv"
$profileClusterCsvPath = Join-Path $runRoot "profile-clusters/profile-clusters.csv"
$profileEntryCsvPath = Join-Path $runRoot "profile-clusters/profile-entries.csv"
$summaryRows | Export-Csv -NoTypeInformation -LiteralPath $summaryCsvPath
$performanceRows | Export-Csv -NoTypeInformation -LiteralPath $performanceCsvPath

$hasProfileRows = @($performanceRows.ToArray() | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.pcProfileHead) }).Count -gt 0
if ($hasProfileRows -and -not $SkipProfileClusterReport) {
    & (Join-Path $PSScriptRoot "build-profile-cluster-report.ps1") -RunDirectory $runRoot | Out-Host
}

[ordered]@{
    schema = "ngcsharp.compat-suite.v1"
    runRoot = $runRoot
    commit = $commit
    branch = $branch
    dirtyWorktree = $dirty
    configuration = $Configuration
    perfOnly = [bool]$PerfOnly
    targets = @($summaryRows.ToArray())
    performance = @($performanceRows.ToArray())
    profileClustersCsvPath = if (Test-Path -LiteralPath $profileClusterCsvPath) { $profileClusterCsvPath } else { $null }
    profileEntriesCsvPath = if (Test-Path -LiteralPath $profileEntryCsvPath) { $profileEntryCsvPath } else { $null }
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryJsonPath

Write-Host "Wrote compatibility run summary: $summaryCsvPath"
Write-Host "Wrote performance run summary: $performanceCsvPath"
if (Test-Path -LiteralPath $profileClusterCsvPath) {
    Write-Host "Wrote profile cluster summary: $profileClusterCsvPath"
}
