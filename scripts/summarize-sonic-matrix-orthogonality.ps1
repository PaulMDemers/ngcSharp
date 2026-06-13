param(
    [Parameter(Mandatory = $true)]
    [string]$TimelineCsvPath,
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Read-Double {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [double]::Parse($Text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Parse-Row3 {
    param([string]$Text)

    $parts = @($Text -split "/" | Select-Object -First 3)
    if ($parts.Count -lt 3) {
        return $null
    }

    $values = @($parts | ForEach-Object { Read-Double $_ })
    if ($values.Count -ne 3 -or $values -contains $null) {
        return $null
    }

    return [double[]]$values
}

function Get-Length {
    param([double[]]$Vector)

    return [Math]::Sqrt($Vector[0] * $Vector[0] + $Vector[1] * $Vector[1] + $Vector[2] * $Vector[2])
}

function Get-Dot {
    param(
        [double[]]$A,
        [double[]]$B
    )

    return $A[0] * $B[0] + $A[1] * $B[1] + $A[2] * $B[2]
}

function Format-Double {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function New-SlotDiagnostic {
    param(
        [object]$Row,
        [int]$Slot
    )

    $row0 = Parse-Row3 $Row."slot$($Slot)_row0"
    $row1 = Parse-Row3 $Row."slot$($Slot)_row1"
    $row2 = Parse-Row3 $Row."slot$($Slot)_row2"
    if ($null -eq $row0 -or $null -eq $row1 -or $null -eq $row2) {
        return $null
    }

    $len0 = Get-Length $row0
    $len1 = Get-Length $row1
    $len2 = Get-Length $row2
    $dot01 = Get-Dot $row0 $row1
    $dot02 = Get-Dot $row0 $row2
    $dot12 = Get-Dot $row1 $row2
    $maxLengthError = @(
        [Math]::Abs($len0 - 1.0),
        [Math]::Abs($len1 - 1.0),
        [Math]::Abs($len2 - 1.0)
    ) | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
    $maxDotError = @([Math]::Abs($dot01), [Math]::Abs($dot02), [Math]::Abs($dot12)) |
        Measure-Object -Maximum |
        Select-Object -ExpandProperty Maximum

    [pscustomobject]@{
        instruction = $Row.instruction
        pc = $Row.pc
        phase = $Row.phase
        source_slot = $Row.slot
        diagnostic_slot = $Slot
        address = $Row.address
        r3 = $Row.r3
        r4 = $Row.r4
        r5 = $Row.r5
        r6 = $Row.r6
        row0_length = Format-Double $len0
        row1_length = Format-Double $len1
        row2_length = Format-Double $len2
        dot01 = Format-Double $dot01
        dot02 = Format-Double $dot02
        dot12 = Format-Double $dot12
        max_length_error = Format-Double $maxLengthError
        max_dot_error = Format-Double $maxDotError
        slot_translation = $Row."slot$($Slot)_translation"
        row0 = $Row."slot$($Slot)_row0"
        row1 = $Row."slot$($Slot)_row1"
        row2 = $Row."slot$($Slot)_row2"
    }
}

$timelinePath = Resolve-FullPath $TimelineCsvPath
if (-not (Test-Path -LiteralPath $timelinePath)) {
    throw "Sonic matrix producer timeline not found: $timelinePath"
}

$directory = Split-Path -Parent $timelinePath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $directory "sonic-matrix-orthogonality.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $directory "sonic-matrix-orthogonality.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$timelineRows = @(Import-Csv -LiteralPath $timelinePath)
if ($timelineRows.Count -eq 0) {
    throw "Sonic matrix producer timeline has no rows: $timelinePath"
}

$diagnostics = New-Object System.Collections.Generic.List[object]
$slotIndexes = @(
    $timelineRows[0].PSObject.Properties.Name |
        Where-Object { $_ -match '^slot(\d+)_row0$' } |
        ForEach-Object { [int]([regex]::Match($_, '^slot(\d+)_row0$').Groups[1].Value) } |
        Sort-Object -Unique
)
foreach ($row in $timelineRows) {
    foreach ($slot in $slotIndexes) {
        $diagnostic = New-SlotDiagnostic -Row $row -Slot $slot
        if ($null -ne $diagnostic) {
            $diagnostics.Add($diagnostic) | Out-Null
        }
    }
}

$interesting = @(
    $diagnostics |
        Where-Object {
            $_.phase -like "*terminal" -or
            [double]$_.max_length_error -gt 0.05 -or
            [double]$_.max_dot_error -gt 0.05
        } |
        Sort-Object {[int64]$_.instruction}, {[int]$_.diagnostic_slot}
)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$interesting | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation

[pscustomobject]@{
    timelineCsvPath = $timelinePath
    outputCsvPath = $OutputCsvPath
    inputRows = $timelineRows.Count
    diagnosticRows = $diagnostics.Count
    interestingRows = $interesting.Count
    worstLengthRows = @($diagnostics | Sort-Object {[double]$_.max_length_error} -Descending | Select-Object -First 12)
    worstDotRows = @($diagnostics | Sort-Object {[double]$_.max_dot_error} -Descending | Select-Object -First 12)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Sonic matrix orthogonality summary: $OutputCsvPath"
$interesting |
    Select-Object -First 20 instruction,pc,phase,diagnostic_slot,row0_length,row1_length,row2_length,dot01,dot02,dot12,slot_translation |
    Format-Table -AutoSize
