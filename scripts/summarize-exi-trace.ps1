param(
    [Parameter(Mandatory = $true)]
    [string]$TracePath,
    [string]$JsonPath = "",
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexToUInt32 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $text = $text.Substring(2)
    }

    return [Convert]::ToUInt32($text, 16)
}

function New-CommandCounter {
    return [ordered]@{
        nintendoId = 0
        readArray = 0
        getStatus = 0
        getId = 0
        clearStatus = 0
        wake = 0
        sleep = 0
        sectorErase = 0
        pageProgram = 0
        chipErase = 0
        unknown = 0
    }
}

$traceFullPath = Resolve-FullPath $TracePath
if (-not (Test-Path -LiteralPath $traceFullPath)) {
    throw "EXI trace not found: $traceFullPath"
}

$commands = New-CommandCounter
$statusReads = New-Object System.Collections.Generic.List[string]
$dataReads = New-Object System.Collections.Generic.List[string]
$firstReadArrayInstruction = $null
$lastReadArrayInstruction = $null
$firstPageProgramInstruction = $null
$firstSectorEraseInstruction = $null
$lastInstruction = 0
$rowCount = 0
$selectedCardWrites = 0
$selectedInternalWrites = 0

Import-Csv -LiteralPath $traceFullPath | ForEach-Object {
    $rowCount++
    $instructionNumber = [int64]($_.instruction)
    $lastInstruction = [Math]::Max($lastInstruction, $instructionNumber)

    if ($_.kind -eq "Write" -and $_.address -eq "0xCC006800") {
        $parameter = Convert-HexToUInt32 $_.value
        if (($parameter -band 0x80) -ne 0) {
            $selectedCardWrites++
        }
        if (($parameter -band 0x100) -ne 0) {
            $selectedInternalWrites++
        }
    }

    if ($_.address -ne "0xCC006810") {
        return
    }

    $value = Convert-HexToUInt32 $_.value
    if ($_.kind -eq "Read") {
        if ($statusReads.Count -lt 12 -and (($value -band 0xFF000000) -ne 0)) {
            $statusReads.Add(("{0}:0x{1:X8}" -f $instructionNumber, $value))
        }
        return
    }

    if ($_.kind -ne "Write") {
        return
    }

    $command = ($value -shr 24) -band 0xFF
    switch ($command) {
        0x00 { $commands.nintendoId++ }
        0x52 {
            $commands.readArray++
            if ($null -eq $firstReadArrayInstruction) {
                $firstReadArrayInstruction = $instructionNumber
            }
            $lastReadArrayInstruction = $instructionNumber
            if ($dataReads.Count -lt 16) {
                $dataReads.Add(("{0}:0x{1:X8}" -f $instructionNumber, $value))
            }
        }
        0x83 { $commands.getStatus++ }
        0x85 { $commands.getId++ }
        0x87 { $commands.wake++ }
        0x88 { $commands.sleep++ }
        0x89 { $commands.clearStatus++ }
        0xF1 {
            $commands.sectorErase++
            if ($null -eq $firstSectorEraseInstruction) {
                $firstSectorEraseInstruction = $instructionNumber
            }
        }
        0xF2 {
            $commands.pageProgram++
            if ($null -eq $firstPageProgramInstruction) {
                $firstPageProgramInstruction = $instructionNumber
            }
        }
        0xF4 { $commands.chipErase++ }
        default {
            if ($command -ne 0xFF) {
                $commands.unknown++
            }
        }
    }
}

$summary = [ordered]@{
    tracePath = $traceFullPath
    bytes = (Get-Item -LiteralPath $traceFullPath).Length
    rows = $rowCount
    lastInstruction = $lastInstruction
    selectedCardWrites = $selectedCardWrites
    selectedInternalWrites = $selectedInternalWrites
    commands = $commands
    firstReadArrayInstruction = $firstReadArrayInstruction
    lastReadArrayInstruction = $lastReadArrayInstruction
    firstPageProgramInstruction = $firstPageProgramInstruction
    firstSectorEraseInstruction = $firstSectorEraseInstruction
    sampledStatusReads = @($statusReads)
    sampledReadArrayWrites = @($dataReads)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 8
}
