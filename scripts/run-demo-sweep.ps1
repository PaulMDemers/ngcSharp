param(
    [string]$DolsRoot = "artifacts/demo-dols",
    [string]$OutRoot = "artifacts/demo-sweep",
    [int]$MaxInstructions = 2000000,
    [int]$TimeoutSeconds = 60,
    [int]$TraceTail = 32,
    [int]$ProfilePc = 12,
    [string]$Filter = "",
    [switch]$DumpGxFrames,
    [int]$GxFrameWidth = 640,
    [int]$GxFrameHeight = 480,
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

function ConvertTo-Slug {
    param([string]$Text)

    $slug = $Text -replace '[\\/:\*\?"<>\|]+', '-' -replace '\s+', '-' -replace '[^A-Za-z0-9._-]+', '-'
    $slug = $slug.Trim('.-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "dol"
    }

    return $slug.ToLowerInvariant()
}

function ConvertTo-CommandLineArgument {
    param([string]$Argument)

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($char in $Argument.ToCharArray()) {
        if ($char -eq '\') {
            $backslashes++
            continue
        }

        if ($char -eq '"') {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * ($backslashes * 2)))
                $backslashes = 0
            }

            [void]$builder.Append('\"')
            continue
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }

        [void]$builder.Append($char)
    }

    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * ($backslashes * 2)))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath,
        [int]$TimeoutSeconds
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = (($ArgumentList | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " ")
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        try {
            $process.Kill()
        } catch {
        }

        try {
            [void]$process.WaitForExit()
        } catch {
        }
    } else {
        [void]$process.WaitForExit()
    }

    [System.IO.File]::WriteAllText($StdoutPath, $stdoutTask.Result)
    [System.IO.File]::WriteAllText($StderrPath, $stderrTask.Result)

    return [pscustomobject]@{
        timedOut = -not $completed
        exitCode = if ($completed) { $process.ExitCode } else { -999 }
    }
}

function Get-RunClassification {
    param(
        [int]$ExitCode,
        [string]$Output
    )

    if ($Output -match 'Unsupported instruction|NotImplementedException|unsupported') {
        return "unsupported"
    }

    if ($Output -match 'Unmapped|Invalid memory|out of range|Access violation') {
        return "memory-fault"
    }

    if ($Output -match 'Stopped after reaching max instruction count|Executed \d+ instruction') {
        return "max-instructions"
    }

    if ($ExitCode -eq 0) {
        return "completed"
    }

    return "failed"
}

function Get-MatchValue {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Default = ""
    )

    $match = [regex]::Match($Text, $Pattern)
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return $Default
}

function Get-PcProfileTop {
    param([string]$Text)

    $match = [regex]::Match($Text, '(?m)^0x([0-9A-Fa-f]{8})\s+([0-9]+)\s+([0-9.]+)%')
    if (-not $match.Success) {
        return [pscustomobject]@{ pc = ""; count = ""; percent = "" }
    }

    return [pscustomobject]@{
        pc = $match.Groups[1].Value.ToUpperInvariant()
        count = $match.Groups[2].Value
        percent = $match.Groups[3].Value
    }
}

function Get-MmioDevices {
    param([string]$Text)

    $devices = [ordered]@{}
    $inSection = $false
    foreach ($line in ($Text -split "`r?`n")) {
        if ($line -eq "MMIO by device:") {
            $inSection = $true
            continue
        }

        if ($inSection -and $line -eq "MMIO hot addresses:") {
            break
        }

        if (-not $inSection) {
            continue
        }

        $match = [regex]::Match($line, '^(?<name>.{1,9}?)\s+(?<total>[0-9]+)\s+read=\s*(?<read>[0-9]+)\s+write=\s*(?<write>[0-9]+)\s*$')
        if ($match.Success) {
            $devices[$match.Groups["name"].Value.Trim()] = [pscustomobject]@{
                total = [int]$match.Groups["total"].Value
                read = [int]$match.Groups["read"].Value
                write = [int]$match.Groups["write"].Value
            }
        }
    }

    return $devices
}

function Get-MmioDeviceCount {
    param(
        [System.Collections.IDictionary]$Devices,
        [string]$Name
    )

    if ($Devices.Contains($Name)) {
        return $Devices[$Name].total
    }

    return 0
}

function Format-MmioDevices {
    param([System.Collections.IDictionary]$Devices)

    if ($Devices.Count -eq 0) {
        return ""
    }

    return (($Devices.GetEnumerator() | ForEach-Object {
        "{0}:{1}/{2}/{3}" -f $_.Key, $_.Value.total, $_.Value.read, $_.Value.write
    }) -join ";")
}

function Get-GxFifoStats {
    param([string]$Text)

    $commands = Get-MatchValue -Text $Text -Pattern 'recognized commands before stop/end: ([0-9]+)'
    $capturedBytes = Get-MatchValue -Text $Text -Pattern 'captured bytes: ([0-9]+)'
    $draws = Get-MatchValue -Text $Text -Pattern 'draws=([0-9]+)'
    $displayLists = Get-MatchValue -Text $Text -Pattern 'displayList=([0-9]+)'
    $unknown = Get-MatchValue -Text $Text -Pattern 'stopped on unknown command: \+0x[0-9A-Fa-f]+ 0x([0-9A-Fa-f]{2})'

    return [pscustomobject]@{
        capturedBytes = $capturedBytes
        commands = $commands
        draws = $draws
        displayLists = $displayLists
        unknownCommand = $unknown
    }
}

function Get-HotTracePc {
    param([string]$TracePath)

    if (-not (Test-Path -LiteralPath $TracePath)) {
        return [pscustomobject]@{ pc = ""; count = 0; finalPc = ""; finalInstruction = ""; finalDisassembly = "" }
    }

    $counts = @{}
    $finalPc = ""
    $finalInstruction = ""
    $finalDisassembly = ""
    foreach ($line in (Get-Content -LiteralPath $TracePath)) {
        $match = [regex]::Match($line, '0x([0-9A-Fa-f]{8}):\s+0x([0-9A-Fa-f]{8})\s*(.*)$')
        if (-not $match.Success) {
            continue
        }

        $pc = $match.Groups[1].Value.ToUpperInvariant()
        $finalPc = $pc
        $finalInstruction = $match.Groups[2].Value.ToUpperInvariant()
        $finalDisassembly = $match.Groups[3].Value.Trim()
        if (-not $counts.ContainsKey($pc)) {
            $counts[$pc] = 0
        }

        $counts[$pc]++
    }

    if ($counts.Count -eq 0) {
        return [pscustomobject]@{ pc = ""; count = 0; finalPc = ""; finalInstruction = ""; finalDisassembly = "" }
    }

    $hot = $counts.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 1
    return [pscustomobject]@{
        pc = $hot.Key
        count = $hot.Value
        finalPc = $finalPc
        finalInstruction = $finalInstruction
        finalDisassembly = $finalDisassembly
    }
}

function Convert-ToInt {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0
    }

    return [int]$Value
}

function Get-Milestone {
    param(
        [string]$Classification,
        [System.Collections.IDictionary]$Devices,
        [pscustomobject]$GxStats,
        [pscustomobject]$HotTrace
    )

    if ($Classification -in @("unsupported", "memory-fault", "timeout", "failed")) {
        return $Classification
    }

    if ((Convert-ToInt $GxStats.draws) -gt 0) {
        return "gx-draw"
    }

    if ((Convert-ToInt $GxStats.displayLists) -gt 0) {
        return "gx-display-list"
    }

    if ((Convert-ToInt $GxStats.capturedBytes) -gt 0 -and $HotTrace.finalInstruction -eq "48000000") {
        return "gx-setup-only"
    }

    if ((Convert-ToInt $GxStats.capturedBytes) -gt 0) {
        return "gx-fifo"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "VI") -gt 0) {
        return "vi-active"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "SI") -gt 0) {
        return "input-active"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "EXI") -gt 0) {
        return "exi-active"
    }

    if ($HotTrace.count -gt 1) {
        return "tight-loop"
    }

    if ($Classification -eq "completed") {
        return "completed"
    }

    return "cpu-run"
}

function Get-WaitHint {
    param(
        [string]$Output,
        [System.Collections.IDictionary]$Devices,
        [pscustomobject]$GxStats,
        [pscustomobject]$HotTrace
    )

    if ($GxStats.unknownCommand -ne "") {
        return "gx-fifo-decode-0x$($GxStats.unknownCommand)"
    }

    if ((Convert-ToInt $GxStats.capturedBytes) -gt 0 -and (Convert-ToInt $GxStats.draws) -eq 0 -and $HotTrace.finalInstruction -eq "48000000") {
        return "setup-complete-no-draw"
    }

    if ((Convert-ToInt $GxStats.capturedBytes) -gt 0 -and (Convert-ToInt $GxStats.draws) -eq 0) {
        return "gx-before-first-draw"
    }

    if ($HotTrace.finalInstruction -eq "48000000" -and $HotTrace.count -gt 4) {
        return "self-branch-halt"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "AI") -gt 1000) {
        return "ai-status-poll"
    }

    if ($Output -match 'VI\s+Read\s+16-bit\s+0xCC00206C') {
        return "vi-current-line-poll"
    }

    if ($Output -match 'SI\s+Read\s+32-bit\s+0xCC00643[48]') {
        return "controller-poll"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "DVD") -gt 0) {
        return "dvd-status"
    }

    if ((Get-MmioDeviceCount -Devices $Devices -Name "EXI") -gt 0) {
        return "exi-device"
    }

    if ($HotTrace.count -gt 1 -and $HotTrace.pc -ne "") {
        return "hot-trace-pc-0x$($HotTrace.pc)"
    }

    return ""
}

$resolvedDolsRoot = Resolve-FullPath $DolsRoot
$resolvedOutRoot = Resolve-FullPath $OutRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $resolvedOutRoot $timestamp
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$appDll = Join-Path (Get-Location) "src\NgcSharp.App\bin\Debug\net10.0\NgcSharp.App.dll"

if (-not $NoBuild) {
    $env:DOTNET_ROOT = Join-Path (Get-Location) ".dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    dotnet build NgcSharp.slnx | Tee-Object -FilePath (Join-Path $runRoot "build.log")
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }
}

$dols = Get-ChildItem -LiteralPath $resolvedDolsRoot -Recurse -Filter *.dol |
    Where-Object { $_.FullName -notmatch '[\\/]downloads[\\/]' } |
    Sort-Object FullName
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $dols = $dols | Where-Object { $_.FullName -match $Filter }
}

$summary = @()
$index = 0
foreach ($dol in $dols) {
    $index++
    $relative = $dol.FullName.Substring($resolvedDolsRoot.Length).TrimStart('\', '/')
    $slug = ConvertTo-Slug ([System.IO.Path]::ChangeExtension($relative, $null))
    $caseRoot = Join-Path $runRoot ("{0:D3}-{1}" -f $index, $slug)
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null

    $stdoutPath = Join-Path $caseRoot "stdout.txt"
    $stderrPath = Join-Path $caseRoot "stderr.txt"
    $tracePath = Join-Path $caseRoot "tail.trace"

    Write-Host "[$index/$($dols.Count)] $relative"

    $args = @(
        $appDll,
        "run-dol",
        $dol.FullName,
        "--max-instructions",
        $MaxInstructions.ToString(),
        "--trace-tail",
        $TraceTail.ToString(),
        "--trace-file",
        $tracePath,
        "--dump-mmio",
        "--dump-threads",
        "--dump-message-queues",
        "--fast-forward-idle",
        "--profile-pc",
        $ProfilePc.ToString(),
        "--no-registers"
    )
    if ($DumpGxFrames) {
        $args += @(
            "--dump-gx-frame",
            (Join-Path $caseRoot "gx.png"),
            "--frame-width",
            $GxFrameWidth.ToString(),
            "--frame-height",
            $GxFrameHeight.ToString()
        )
    }

    $run = Invoke-ProcessWithTimeout `
        -FilePath "dotnet" `
        -ArgumentList $args `
        -WorkingDirectory (Get-Location).Path `
        -StdoutPath $stdoutPath `
        -StderrPath $stderrPath `
        -TimeoutSeconds $TimeoutSeconds
    $timedOut = $run.timedOut

    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { "" }
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
    $combined = "$stdout`n$stderr"
    $exitCode = $run.exitCode
    $classification = if ($timedOut) { "timeout" } else { Get-RunClassification -ExitCode $exitCode -Output $combined }
    $topPc = Get-PcProfileTop -Text $combined
    $devices = Get-MmioDevices -Text $combined
    $gxStats = Get-GxFifoStats -Text $combined
    $hotTrace = Get-HotTracePc -TracePath $tracePath
    $registerPc = Get-MatchValue -Text $combined -Pattern 'PC=0x([0-9A-Fa-f]+)'
    $finalPc = if ($registerPc -ne "") { $registerPc } else { $hotTrace.finalPc }
    $instructions = Get-MatchValue -Text $combined -Pattern '(?:Executed|Halted after|Stopped after) ([0-9]+) instruction'
    $milestone = Get-Milestone -Classification $classification -Devices $devices -GxStats $gxStats -HotTrace $hotTrace
    $waitHint = Get-WaitHint -Output $combined -Devices $devices -GxStats $gxStats -HotTrace $hotTrace

    $summary += [pscustomobject]@{
        name = [System.IO.Path]::ChangeExtension($relative, $null)
        path = $dol.FullName
        bytes = $dol.Length
        exitCode = $exitCode
        result = $classification
        milestone = $milestone
        waitHint = $waitHint
        pc = $finalPc
        lr = Get-MatchValue -Text $combined -Pattern 'LR=0x([0-9A-Fa-f]+)'
        instructions = $instructions
        topPc = $topPc.pc
        topPcCount = $topPc.count
        topPcPercent = $topPc.percent
        hotTracePc = $hotTrace.pc
        hotTraceCount = $hotTrace.count
        finalInstruction = $hotTrace.finalInstruction
        finalDisassembly = $hotTrace.finalDisassembly
        gxBytes = $gxStats.capturedBytes
        gxCommands = $gxStats.commands
        gxDraws = $gxStats.draws
        gxDisplayLists = $gxStats.displayLists
        gxUnknownCommand = $gxStats.unknownCommand
        gxFrame = if ($DumpGxFrames) { Join-Path $caseRoot "gx.png" } else { "" }
        renderedQuads = Get-MatchValue -Text $combined -Pattern '([0-9]+) rendered quad\(s\)'
        renderedTriangles = Get-MatchValue -Text $combined -Pattern '([0-9]+) rendered triangle\(s\)'
        degenerateQuads = Get-MatchValue -Text $combined -Pattern '([0-9]+) degenerate quad\(s\)'
        degenerateTriangles = Get-MatchValue -Text $combined -Pattern '([0-9]+) degenerate triangle\(s\)'
        mmioDevices = Format-MmioDevices -Devices $devices
        mmioVI = Get-MmioDeviceCount -Devices $devices -Name "VI"
        mmioGX = Get-MmioDeviceCount -Devices $devices -Name "GX FIFO"
        mmioSI = Get-MmioDeviceCount -Devices $devices -Name "SI"
        mmioAI = Get-MmioDeviceCount -Devices $devices -Name "AI"
        mmioEXI = Get-MmioDeviceCount -Devices $devices -Name "EXI"
        mmioDVD = Get-MmioDeviceCount -Devices $devices -Name "DVD"
        stdout = $stdoutPath
        stderr = $stderrPath
        trace = $tracePath
    }
}

$summaryCsv = Join-Path $runRoot "summary.csv"
$summaryJson = Join-Path $runRoot "summary.json"
$summary | Export-Csv -LiteralPath $summaryCsv -NoTypeInformation
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryJson -Encoding UTF8

$summary | Group-Object result | Sort-Object Name | ForEach-Object {
    Write-Host ("{0}: {1}" -f $_.Name, $_.Count)
}

$summary | Group-Object milestone | Sort-Object Name | ForEach-Object {
    Write-Host ("milestone {0}: {1}" -f $_.Name, $_.Count)
}

Write-Host "Sweep output: $runRoot"
